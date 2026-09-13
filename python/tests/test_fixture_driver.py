"""Unit tests for the five-step fixture driver and comparisons."""

from collections.abc import Mapping
from typing import Any

import pytest

from .harness.fixture_driver import (
    ErrorOutcome,
    FixtureDriver,
    OperationError,
    OperationRegistry,
    RedactedValue,
    RequestMismatch,
    ResultMismatch,
    TransportFailure,
    compare_error,
    compare_request,
    compare_result,
)


def _fixture(
    *,
    operation: str = "Synthetic.Read",
    request: Mapping[str, Any] | None = None,
    response: Mapping[str, Any] | None = None,
    expect: Mapping[str, Any] | None = None,
    fail: str | None = None,
    strict_headers: bool = False,
) -> dict[str, Any]:
    exchange: dict[str, Any] = {"expectRequest": dict(request or {})}
    if fail is None:
        exchange["respond"] = dict(response or {"status": 200, "body": {}})
    else:
        exchange["fail"] = fail
    return {
        "id": "synthetic.driver-case",
        "title": "Synthetic driver case",
        "requirements": ["TST-011"],
        "level": "core",
        "sections": ["test"],
        "client": {
            "address": "https://synthetic.invalid",
            "token": None,
            "namespace": "",
            "apiPrefix": "v1",
            "settings": {"RetryPolicy": {"MaxAttempts": 1}},
        },
        "environment": {"BV_TEST_MODE": "synthetic"},
        "operation": {"name": operation, "args": {"path": "value"}},
        "exchanges": [exchange],
        "strictHeaders": strict_headers,
        "expect": dict(expect or {"result": {}}),
    }


def test_empty_registry_reports_pending_without_failure() -> None:
    """@req TST-011 @req TST-013"""
    result = FixtureDriver(OperationRegistry()).run(_fixture())

    assert result.status == "pending"
    assert result.operation_name == "Synthetic.Read"
    assert result.pending_reason == "operation is not registered"


def test_driver_configures_and_runs_scripted_response() -> None:
    """@req TST-011 @req FIX-002 @req FIX-003"""
    observed: dict[str, Any] = {}
    registry = OperationRegistry()

    def operation(configuration: Any, transport: Any, operation_spec: Mapping[str, Any]) -> Any:
        observed["address"] = configuration.address
        observed["environment"] = configuration.environment["BV_TEST_MODE"]
        observed["args"] = operation_spec["args"]
        response = transport.request(
            "GET",
            "https://synthetic.invalid/v1/value",
            {"X-Request": "present", "x-unlisted": "ignored"},
            {"b": 2, "a": 1},
        )
        return response.body

    registry.register("Synthetic.Read", operation)
    fixture = _fixture(
        request={
            "method": "GET",
            "url": "https://synthetic.invalid/v1/value",
            "headers": {"x-request": "present"},
            "absentHeaders": ["X-Forbidden"],
            "body": {"a": 1, "b": 2},
        },
        response={"status": 200, "headers": {"X-Response": "yes"}, "body": {"ok": True}},
        expect={"result": {"ok": True}},
    )

    result = FixtureDriver(registry).run(fixture)

    assert result.status == "passed"
    assert observed == {
        "address": "https://synthetic.invalid",
        "environment": "synthetic",
        "args": {"path": "value"},
    }


def test_request_comparison_rules_positive_and_negative() -> None:
    """@req FIX-002 @req TST-011"""
    expected = {
        "method": "POST",
        "url": "https://synthetic.invalid/v1/item",
        "headers": {"X-Token": "header-value", "Accept": "application/json"},
        "absentHeaders": ["X-Missing"],
        "body": {"z": [1, 2], "a": {"b": True}},
    }
    actual: dict[str, Any] = {
        "method": "POST",
        "url": "https://synthetic.invalid/v1/item",
        "headers": {"x-token": "header-value", "accept": "application/json", "X-Extra": "free"},
        "body": {"a": {"b": True}, "z": [1, 2]},
    }

    compare_request(expected, actual, strict_headers=False)

    with pytest.raises(RequestMismatch, match="method"):
        compare_request(expected, {**actual, "method": "GET"}, strict_headers=False)
    with pytest.raises(RequestMismatch, match="url"):
        compare_request(expected, {**actual, "url": "https://synthetic.invalid/other"}, strict_headers=False)
    wrong_header_request = dict(actual)
    wrong_header_request["headers"] = {
        "X-Token": "other-value",
        "accept": "application/json",
        "X-Extra": "free",
    }
    with pytest.raises(RequestMismatch, match="header 'X-Token'"):
        compare_request(expected, wrong_header_request, strict_headers=False)
    with pytest.raises(RequestMismatch, match="absent header"):
        compare_request(
            expected,
            {**actual, "headers": {**actual["headers"], "x-missing": "present"}},
            strict_headers=False,
        )
    with pytest.raises(RequestMismatch, match="body"):
        compare_request(expected, {**actual, "body": {"z": [1, 3], "a": {"b": True}}}, strict_headers=False)
    with pytest.raises(RequestMismatch, match="unlisted header"):
        compare_request(expected, actual, strict_headers=True)


def test_result_comparison_is_recursive_and_honours_all_sentinels() -> None:
    """@req FIX-003 @req TST-011"""
    expected = {
        "present": {"nested": ["value", {"number": 4}]},
        "optional_missing": "$absent",
        "optional_null": "$absent",
        "wildcard": "$any",
        "redacted": "$redacted",
    }
    actual = {
        "present": {"nested": ["value", {"number": 4, "extra": "ignored"}]},
        "optional_null": None,
        "wildcard": object(),
        "redacted": RedactedValue("redaction-input"),
        "extra": "ignored",
    }

    compare_result(expected, actual)

    with pytest.raises(ResultMismatch, match="optional_null"):
        compare_result({"optional_null": "$absent"}, {"optional_null": "not-null"})
    with pytest.raises(ResultMismatch, match="wildcard"):
        compare_result({"wildcard": "$any"}, {})
    with pytest.raises(ResultMismatch, match="redacted"):
        compare_result({"redacted": "$redacted"}, {"redacted": "redaction-input"})
    with pytest.raises(ResultMismatch, match="array length"):
        compare_result({"items": [1, 2]}, {"items": [1]})


def test_error_comparison_checks_every_declared_field() -> None:
    """@req FIX-004 @req TST-011"""
    expected = {
        "code": "BV-AUTH-001",
        "statusCode": 429,
        "retryable": True,
        "attempts": 2,
        "retryAfter": 60,
        "detailsKeys": ["namespace", "request_id"],
        "hintContains": ["TRY LATER", "quota"],
        "serverMessage": "quota exceeded",
    }
    actual = ErrorOutcome(
        code="BV-AUTH-001",
        status_code=429,
        retryable=True,
        attempts=2,
        retry_after=60,
        details={"namespace": "team", "request_id": "id"},
        hint="Please try later because quota is exhausted",
        server_message="quota exceeded",
    )

    compare_error(expected, actual)

    for field, value in (
        ("code", "BV-AUTH-002"),
        ("statusCode", 500),
        ("retryable", False),
        ("attempts", 1),
        ("retryAfter", 1),
        ("detailsKeys", ["namespace", "missing"]),
        ("hintContains", ["absent phrase"]),
        ("serverMessage", "different"),
    ):
        changed = dict(expected)
        changed[field] = value
        with pytest.raises(ResultMismatch, match=field):
            compare_error(changed, actual)


@pytest.mark.parametrize(
    "mode",
    ["connection_refused", "timeout", "tls_verify", "tls_handshake", "reset", "dns"],
)
def test_transport_honours_each_fail_mode(mode: str) -> None:
    """@req TST-011 @req FIX-002 @req FIX-004"""
    registry = OperationRegistry()

    def operation(configuration: Any, transport: Any, operation_spec: Mapping[str, Any]) -> Any:
        del configuration, operation_spec
        transport.request("GET", "https://synthetic.invalid/fail", {}, None)
        return None

    registry.register("Synthetic.Read", operation)
    fixture = _fixture(
        request={"method": "GET", "url": "https://synthetic.invalid/fail", "body": None},
        fail=mode,
        expect={"error": {"code": "BV-TRANSPORT-001"}},
    )

    def operation_with_expected_failure(
        configuration: Any, transport: Any, operation_spec: Mapping[str, Any]
    ) -> Any:
        del configuration, operation_spec
        try:
            transport.request("GET", "https://synthetic.invalid/fail", {}, None)
        except TransportFailure as failure:
            raise OperationError(code="BV-TRANSPORT-001", hint=failure.mode) from failure
        return None

    registry.register("Synthetic.Read", operation_with_expected_failure)
    result = FixtureDriver(registry).run(fixture)

    assert result.status == "passed"


def test_driver_error_expectation_is_executed() -> None:
    """@req FIX-004 @req TST-011"""
    registry = OperationRegistry()

    def operation(configuration: Any, transport: Any, operation_spec: Mapping[str, Any]) -> Any:
        del configuration, transport, operation_spec
        raise OperationError(
            code="BV-AUTH-001",
            status_code=401,
            retryable=False,
            attempts=1,
            details={"subject": "user"},
            hint="Check credentials",
            server_message="login failed",
        )

    registry.register("Synthetic.Read", operation)
    fixture = _fixture(
        expect={
            "error": {
                "code": "BV-AUTH-001",
                "statusCode": 401,
                "retryable": False,
                "attempts": 1,
                "detailsKeys": ["subject"],
                "hintContains": ["credentials"],
                "serverMessage": "login failed",
            }
        }
    )
    fixture["exchanges"] = []

    assert FixtureDriver(registry).run(fixture).status == "passed"
