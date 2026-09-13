"""Test-only fixture driver, scripted transport, and comparison rules."""

from __future__ import annotations

import json
from collections.abc import Callable, Mapping, Sequence
from dataclasses import dataclass, field
from typing import Any, Final

_MISSING: Final = object()
FAIL_MODES: Final = frozenset(
    {"connection_refused", "timeout", "tls_verify", "tls_handshake", "reset", "dns"}
)


class ComparisonFailure(AssertionError):
    """Base class for a fixture comparison failure."""


class RequestMismatch(ComparisonFailure):
    """Raised when a scripted request does not match."""


class ResultMismatch(ComparisonFailure):
    """Raised when a result or error does not match."""


class TransportFailure(ConnectionError):
    """A deterministic scripted transport failure."""

    def __init__(self, mode: str) -> None:
        self.mode = mode
        super().__init__(f"scripted transport failure: {mode}")


@dataclass(frozen=True)
class RedactedValue:
    """A value whose representation never reveals the original value."""

    _original: Any = field(repr=False)

    def __str__(self) -> str:
        return "[REDACTED]"

    def __repr__(self) -> str:
        return "RedactedValue('[REDACTED]')"


@dataclass(frozen=True)
class TransportResponse:
    """Response returned by a scripted exchange."""

    status: int
    headers: dict[str, str]
    body: Any
    raw_body: str | None = None


@dataclass(frozen=True)
class ClientConfiguration:
    """Client/environment values made available to a registered operation."""

    address: str | None
    token: str | None
    namespace: str | None
    api_prefix: str | None
    settings: Mapping[str, Any]
    environment: Mapping[str, str]


@dataclass(frozen=True)
class ErrorOutcome:
    """Language-neutral error shape used only by the test driver."""

    code: str
    status_code: int | None = None
    retryable: bool | None = None
    attempts: int | None = None
    retry_after: int | None = None
    details: Mapping[str, Any] = field(default_factory=dict)
    hint: str | None = None
    server_message: str | None = None


class OperationError(Exception):
    """An operation callback's expected, language-neutral error."""

    def __init__(self, **kwargs: Any) -> None:
        self.outcome = ErrorOutcome(**kwargs)
        super().__init__(self.outcome.code)


OperationHandler = Callable[[ClientConfiguration, "FakeTransport", Mapping[str, Any]], Any]


class OperationRegistry:
    """Small M0-to-M10 seam mapping operation names to test callbacks."""

    def __init__(self) -> None:
        self._handlers: dict[str, OperationHandler] = {}

    def register(self, name: str, handler: OperationHandler) -> None:
        self._handlers[name] = handler

    def resolve(self, name: str) -> OperationHandler | None:
        return self._handlers.get(name)


class FakeTransport:
    """Consume exchanges in order and apply request/response or fail semantics."""

    def __init__(self, exchanges: Sequence[Mapping[str, Any]], strict_headers: bool = False) -> None:
        self._exchanges = exchanges
        self._strict_headers = strict_headers
        self._position = 0

    def request(
        self,
        method: str,
        url: str,
        headers: Mapping[str, str] | None = None,
        body: Any = None,
    ) -> TransportResponse:
        if self._position >= len(self._exchanges):
            raise RequestMismatch("request has no corresponding scripted exchange")
        exchange = self._exchanges[self._position]
        self._position += 1
        actual_request = {
            "method": method,
            "url": url,
            "headers": dict(headers or {}),
            "body": body,
        }
        compare_request(exchange.get("expectRequest", {}), actual_request, self._strict_headers)
        if "fail" in exchange:
            fail_mode = str(exchange["fail"])
            if fail_mode not in FAIL_MODES:
                raise RequestMismatch(f"unsupported scripted transport failure: {fail_mode}")
            raise TransportFailure(fail_mode)
        response = exchange["respond"]
        if not isinstance(response, Mapping):
            raise RequestMismatch("scripted respond value must be an object")
        raw_body = response.get("rawBody")
        body = raw_body if raw_body is not None else response.get("body")
        return TransportResponse(
            status=int(response["status"]),
            headers={str(key): str(value) for key, value in response.get("headers", {}).items()},
            body=body,
            raw_body=str(raw_body) if raw_body is not None else None,
        )

    def assert_exhausted(self) -> None:
        if self._position != len(self._exchanges):
            raise RequestMismatch(
                f"{len(self._exchanges) - self._position} scripted exchange(s) were not consumed"
            )


@dataclass(frozen=True)
class FixtureRunResult:
    """Outcome of one fixture run."""

    fixture_id: str
    operation_name: str
    status: str
    pending_reason: str | None = None


class FixtureRunFailure(AssertionError):
    """Failure that names the fixture which could not be run or compared."""


class FixtureDriver:
    """Execute registered operations against fixture configuration and exchanges."""

    def __init__(self, registry: OperationRegistry | None = None) -> None:
        self.registry = registry or OperationRegistry()

    def run(self, fixture: Mapping[str, Any]) -> FixtureRunResult:
        fixture_id = str(fixture.get("id", "<unknown>"))
        operation = fixture.get("operation", {})
        if not isinstance(operation, Mapping):
            raise FixtureRunFailure(f"Fixture '{fixture_id}' operation must be an object")
        operation_name = str(operation.get("name", "<unknown>"))
        handler = self.registry.resolve(operation_name)
        if handler is None:
            return FixtureRunResult(
                fixture_id=fixture_id,
                operation_name=operation_name,
                status="pending",
                pending_reason="operation is not registered",
            )

        configuration = self._configure(fixture)
        exchanges = fixture.get("exchanges", [])
        if not isinstance(exchanges, Sequence) or isinstance(exchanges, (str, bytes)):
            raise FixtureRunFailure(f"Fixture '{fixture_id}' exchanges must be an array")
        transport = FakeTransport(exchanges, bool(fixture.get("strictHeaders", False)))
        try:
            actual_result = handler(configuration, transport, operation)
            transport.assert_exhausted()
            expected = fixture.get("expect", {})
            if not isinstance(expected, Mapping) or "result" not in expected:
                raise ResultMismatch("expected an error but the operation returned a result")
            compare_result(expected["result"], actual_result)
        except OperationError as operation_error:
            try:
                transport.assert_exhausted()
                expected = fixture.get("expect", {})
                if not isinstance(expected, Mapping) or "error" not in expected:
                    raise ResultMismatch("expected a result but the operation returned an error")
                compare_error(expected["error"], operation_error.outcome)
            except ComparisonFailure as error:
                raise FixtureRunFailure(f"Fixture '{fixture_id}' failed: {error}") from error
        except (ComparisonFailure, TransportFailure) as error:
            raise FixtureRunFailure(f"Fixture '{fixture_id}' failed: {error}") from error
        return FixtureRunResult(fixture_id, operation_name, "passed")

    def run_all(self, fixtures: Sequence[Mapping[str, Any]]) -> list[FixtureRunResult]:
        """Run all registered fixtures and emit the M0 pending count."""
        results = [self.run(fixture) for fixture in fixtures]
        pending_count = sum(result.status == "pending" for result in results)
        print(f"pending fixtures: {pending_count}")
        return results

    @staticmethod
    def _configure(fixture: Mapping[str, Any]) -> ClientConfiguration:
        client = fixture.get("client", {})
        environment = fixture.get("environment", {})
        client_mapping = client if isinstance(client, Mapping) else {}
        environment_mapping = environment if isinstance(environment, Mapping) else {}
        return ClientConfiguration(
            address=client_mapping.get("address"),
            token=client_mapping.get("token"),
            namespace=client_mapping.get("namespace"),
            api_prefix=client_mapping.get("apiPrefix"),
            settings=client_mapping.get("settings", {}),
            environment={str(key): str(value) for key, value in environment_mapping.items()},
        )


def compare_request(
    expected: Mapping[str, Any], actual: Mapping[str, Any], strict_headers: bool = False
) -> None:
    """Compare the FIX-002 request contract."""
    for field_name in ("method", "url"):
        if field_name in expected and expected[field_name] != actual.get(field_name):
            raise RequestMismatch(
                f"{field_name} mismatch: expected {expected[field_name]!r}, got {actual.get(field_name)!r}"
            )

    expected_headers = expected.get("headers", {})
    actual_headers = actual.get("headers", {})
    if not isinstance(expected_headers, Mapping) or not isinstance(actual_headers, Mapping):
        raise RequestMismatch("headers must be objects")
    normalized_actual: dict[str, str] = {}
    for name, value in actual_headers.items():
        normalized_name = str(name).lower()
        if normalized_name in normalized_actual and normalized_actual[normalized_name] != str(value):
            raise RequestMismatch(f"duplicate case-insensitive header '{name}'")
        normalized_actual[normalized_name] = str(value)
    for name, expected_value in expected_headers.items():
        normalized_name = str(name).lower()
        if normalized_name not in normalized_actual:
            raise RequestMismatch(f"missing header '{name}'")
        if normalized_actual[normalized_name] != str(expected_value):
            raise RequestMismatch(
                f"header '{name}' mismatch: expected {expected_value!r}, "
                f"got {normalized_actual[normalized_name]!r}"
            )
    for name in expected.get("absentHeaders", []):
        if str(name).lower() in normalized_actual:
            raise RequestMismatch(f"absent header '{name}' was present")
    if strict_headers:
        expected_names = {str(name).lower() for name in expected_headers}
        unlisted = sorted(set(normalized_actual) - expected_names)
        if unlisted:
            raise RequestMismatch(f"unlisted header(s) present: {', '.join(unlisted)}")
    if "body" in expected and _canonical_json(expected["body"]) != _canonical_json(actual.get("body")):
        raise RequestMismatch("body mismatch after canonical JSON comparison")


def compare_result(expected: Any, actual: Any) -> None:
    """Compare FIX-003 recursively, ignoring extra object fields."""
    _compare_value(expected, actual, "result")


def _compare_value(expected: Any, actual: Any, path: str) -> None:
    if expected == "$absent":
        if actual is not _MISSING and actual is not None:
            raise ResultMismatch(f"{path}: expected absent or null")
        return
    if expected == "$any":
        if actual is _MISSING:
            raise ResultMismatch(f"{path}: expected a present value")
        return
    if expected == "$redacted":
        if not isinstance(actual, RedactedValue):
            raise ResultMismatch(f"{path}: expected a RedactedValue")
        if actual._original is not None and str(actual._original) in str(actual):
            raise ResultMismatch(f"{path}: redacted string contains the original value")
        return
    if isinstance(expected, Mapping):
        if not isinstance(actual, Mapping):
            raise ResultMismatch(f"{path}: expected an object")
        for key, expected_value in expected.items():
            actual_value = actual.get(key, _MISSING)
            if actual_value is _MISSING and expected_value != "$absent":
                raise ResultMismatch(f"{path}.{key}: expected field is absent")
            _compare_value(expected_value, actual_value, f"{path}.{key}")
        return
    if isinstance(expected, list):
        if not isinstance(actual, Sequence) or isinstance(actual, (str, bytes)):
            raise ResultMismatch(f"{path}: expected an array")
        if len(expected) != len(actual):
            raise ResultMismatch(f"{path}: array length expected {len(expected)}, got {len(actual)}")
        for index, expected_value in enumerate(expected):
            _compare_value(expected_value, actual[index], f"{path}[{index}]")
        return
    if type(expected) is not type(actual) or expected != actual:
        raise ResultMismatch(f"{path}: expected {expected!r}, got {actual!r}")


def compare_error(
    expected: Mapping[str, Any], actual: ErrorOutcome | OperationError | Mapping[str, Any]
) -> None:
    """Compare FIX-004's declared error fields and containment rules."""
    actual_outcome = actual.outcome if isinstance(actual, OperationError) else actual
    actual_mapping = _error_mapping(actual_outcome)
    if actual_mapping.get("code") != expected.get("code"):
        raise ResultMismatch(
            f"code mismatch: expected {expected.get('code')!r}, got {actual_mapping.get('code')!r}"
        )
    aliases = {
        "statusCode": "status_code",
        "retryable": "retryable",
        "attempts": "attempts",
        "retryAfter": "retry_after",
    }
    for expected_name, actual_name in aliases.items():
        if expected_name in expected and actual_mapping.get(actual_name) != expected[expected_name]:
            raise ResultMismatch(
                f"{expected_name} mismatch: expected {expected[expected_name]!r}, "
                f"got {actual_mapping.get(actual_name)!r}"
            )
    if "detailsKeys" in expected:
        details = actual_mapping["details"]
        if not isinstance(details, Mapping):
            raise ResultMismatch("detailsKeys cannot be checked because details is not an object")
        for key in expected["detailsKeys"]:
            if key not in details:
                raise ResultMismatch(f"detailsKeys missing key '{key}'")
    if "hintContains" in expected:
        hint = str(actual_mapping.get("hint") or "")
        for fragment in expected["hintContains"]:
            if str(fragment).casefold() not in hint.casefold():
                raise ResultMismatch(f"hintContains missing substring '{fragment}'")
    if "serverMessage" in expected and actual_mapping.get("server_message") != expected["serverMessage"]:
        raise ResultMismatch(
            f"serverMessage mismatch: expected {expected['serverMessage']!r}, "
            f"got {actual_mapping.get('server_message')!r}"
        )


def _error_mapping(actual: ErrorOutcome | Mapping[str, Any]) -> dict[str, Any]:
    if isinstance(actual, ErrorOutcome):
        return {
            "code": actual.code,
            "status_code": actual.status_code,
            "retryable": actual.retryable,
            "attempts": actual.attempts,
            "retry_after": actual.retry_after,
            "details": actual.details,
            "hint": actual.hint,
            "server_message": actual.server_message,
        }
    return {
        "code": actual.get("code"),
        "status_code": actual.get("status_code", actual.get("statusCode")),
        "retryable": actual.get("retryable"),
        "attempts": actual.get("attempts"),
        "retry_after": actual.get("retry_after", actual.get("retryAfter")),
        "details": actual.get("details", {}),
        "hint": actual.get("hint"),
        "server_message": actual.get("server_message", actual.get("serverMessage")),
    }


def _canonical_json(value: Any) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)
