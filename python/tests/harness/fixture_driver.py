"""Test-only fixture driver, scripted transport, and comparison rules.

The scripted transport driven here is `bastionvault_integration_sdk.testing.FakeTransport`
itself (D-M1b-15) -- not a second, private implementation. Fixture-schema knowledge (the
`expectRequest`/`respond`/`fail` shapes) stays here, in the test harness, because it is a
fixture-format concern, not something the shipped `FakeTransport` needs to know.
"""

from __future__ import annotations

import asyncio
import inspect
import json
from collections.abc import Callable, Coroutine, Mapping, Sequence
from dataclasses import dataclass, field
from typing import Any, Final, cast

from bastionvault_integration_sdk.errors import BastionVaultError, make_error
from bastionvault_integration_sdk.testing import FakeTransport, ScriptedExchange
from bastionvault_integration_sdk.transport import TransportResponse

_MISSING: Final = object()
FAIL_MODES: Final = frozenset(
    {"connection_refused", "timeout", "tls_verify", "tls_handshake", "reset", "dns"}
)
# D-M1b-4a: the six fixture `fail` kinds map to fixed codes.
_FAIL_MODE_CODES: Final[dict[str, str]] = {
    "connection_refused": "BV-TRANSPORT-001",
    "dns": "BV-TRANSPORT-001",
    "reset": "BV-TRANSPORT-001",
    "timeout": "BV-TRANSPORT-002",
    "tls_verify": "BV-TRANSPORT-003",
    "tls_handshake": "BV-TRANSPORT-003",
}


class ComparisonFailure(AssertionError):
    """Base class for a fixture comparison failure."""


class RequestMismatch(ComparisonFailure):
    """Raised when a scripted request does not match."""


class ResultMismatch(ComparisonFailure):
    """Raised when a result or error does not match."""


@dataclass(frozen=True)
class RedactedValue:
    """A value whose representation never reveals the original value."""

    _original: Any = field(repr=False)

    def __str__(self) -> str:
        return "[REDACTED]"

    def __repr__(self) -> str:
        return "RedactedValue('[REDACTED]')"


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
    client_state: Mapping[str, Any] = field(default_factory=dict)


class OperationError(Exception):
    """An operation callback's expected, language-neutral error."""

    def __init__(self, **kwargs: Any) -> None:
        self.outcome = ErrorOutcome(**kwargs)
        super().__init__(self.outcome.code)


OperationHandler = Callable[[ClientConfiguration, FakeTransport, Mapping[str, Any]], Any]


class OperationRegistry:
    """Small M0-to-M10 seam mapping operation names to test callbacks."""

    def __init__(self) -> None:
        self._handlers: dict[str, OperationHandler] = {}

    def register(self, name: str, handler: OperationHandler) -> None:
        self._handlers[name] = handler

    def resolve(self, name: str) -> OperationHandler | None:
        return self._handlers.get(name)


def build_fake_transport(
    exchanges: Sequence[Mapping[str, Any]], *, supports_custom_verbs: bool = True
) -> FakeTransport:
    """Build the SDK's real `FakeTransport` (D-M1b-15) from raw fixture exchanges.

    Fixture-schema knowledge (`respond`/`fail`) stays here; the shipped `FakeTransport`
    only records and replays (`ScriptedExchange` items), it does not parse fixtures.
    """
    queued: list[ScriptedExchange] = []
    for exchange in exchanges:
        if not isinstance(exchange, Mapping):
            raise RequestMismatch("each exchange must be an object")
        if "fail" in exchange:
            fail_mode = str(exchange["fail"])
            if fail_mode not in _FAIL_MODE_CODES:
                raise RequestMismatch(f"unsupported scripted transport failure: {fail_mode}")
            queued.append(make_error(_FAIL_MODE_CODES[fail_mode], attempts=0))
            continue
        response = exchange.get("respond")
        if not isinstance(response, Mapping):
            raise RequestMismatch("scripted respond value must be an object")
        raw_body = response.get("rawBody")
        if raw_body is not None:
            body_bytes = str(raw_body).encode("utf-8")
        elif "body" in response:
            body_bytes = json.dumps(response["body"]).encode("utf-8")
        else:
            body_bytes = b""
        headers = {str(key): str(value) for key, value in response.get("headers", {}).items()}
        queued.append(
            TransportResponse(status_code=int(response["status"]), headers=headers, body=body_bytes)
        )
    return FakeTransport(exchanges=queued, supports_custom_verbs=supports_custom_verbs)


def compare_recorded_requests(
    exchanges: Sequence[Mapping[str, Any]], transport: FakeTransport, strict_headers: bool = False
) -> None:
    """Post-hoc FIX-002 comparison: every recorded `TransportRequest` against its exchange."""
    if len(transport.requests) != len(exchanges):
        remaining = len(exchanges) - len(transport.requests)
        raise RequestMismatch(f"{remaining} scripted exchange(s) were not consumed")
    for exchange, request in zip(exchanges, transport.requests):
        if not isinstance(exchange, Mapping):
            raise RequestMismatch("each exchange must be an object")
        actual_request = {
            "method": request.method,
            "url": request.url,
            "headers": dict(request.headers),
            "body": json.loads(request.body.decode("utf-8")) if request.body else None,
        }
        compare_request(exchange.get("expectRequest", {}), actual_request, strict_headers)


def compare_client_state(expected: Mapping[str, Any], actual: Mapping[str, Any]) -> None:
    """Compare the (unrequired) `clientState` block a fixture may also assert."""
    for key, value in expected.items():
        if actual.get(key) != value:
            raise ResultMismatch(
                f"clientState.{key} mismatch: expected {value!r}, got {actual.get(key)!r}"
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
        strict_headers = bool(fixture.get("strictHeaders", False))
        transport = build_fake_transport(
            exchanges, supports_custom_verbs=bool(fixture.get("supportsCustomVerbs", True))
        )
        try:
            actual_result = handler(configuration, transport, operation)
            if inspect.isawaitable(actual_result):
                actual_result = asyncio.run(cast(Coroutine[Any, Any, Any], actual_result))
            compare_recorded_requests(exchanges, transport, strict_headers)
            expected = fixture.get("expect", {})
            if not isinstance(expected, Mapping) or "result" not in expected:
                raise ResultMismatch("expected an error but the operation returned a result")
            compare_result(expected["result"], actual_result)
        except OperationError as operation_error:
            try:
                compare_recorded_requests(exchanges, transport, strict_headers)
                expected = fixture.get("expect", {})
                if not isinstance(expected, Mapping) or "error" not in expected:
                    raise ResultMismatch("expected a result but the operation returned an error")
                compare_error(expected["error"], operation_error.outcome)
                if "clientState" in expected:
                    compare_client_state(expected["clientState"], operation_error.outcome.client_state)
            except ComparisonFailure as error:
                raise FixtureRunFailure(f"Fixture '{fixture_id}' failed: {error}") from error
        except (ComparisonFailure, BastionVaultError) as error:
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


__all__ = [
    "ClientConfiguration",
    "ComparisonFailure",
    "ErrorOutcome",
    "FakeTransport",
    "FixtureDriver",
    "FixtureRunFailure",
    "FixtureRunResult",
    "OperationError",
    "OperationHandler",
    "OperationRegistry",
    "RedactedValue",
    "RequestMismatch",
    "ResultMismatch",
    "build_fake_transport",
    "compare_client_state",
    "compare_error",
    "compare_recorded_requests",
    "compare_request",
    "compare_result",
]
