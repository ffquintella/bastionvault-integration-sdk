"""Runs all 18 `specifications/fixtures/transport/*.json` fixtures end-to-end.

Registers `Logical.Read`/`Write`/`Delete`/`List`/`Raw` in the fixture driver's operation
registry (`Client.Construct` was registered at M1a). Every operation handler here drives
the *same* `FakeTransport` instance the driver builds from the fixture's raw exchanges
(D-M1b-15) -- it is not rebuilt or duplicated -- against the real `Client`/`Logical` code.
"""

from __future__ import annotations

import re
from collections.abc import Mapping
from dataclasses import replace
from datetime import timedelta
from typing import Any

import pytest

from bastionvault_integration_sdk import Client, ClientOptions, MapEnvironmentSource, RateGate, RetryPolicy
from bastionvault_integration_sdk.errors import BastionVaultError
from bastionvault_integration_sdk.logical import RawResponse, Response
from bastionvault_integration_sdk.testing import FakeTransport

from .harness.fixture_driver import (
    ClientConfiguration,
    FixtureDriver,
    OperationError,
    OperationRegistry,
    RedactedValue,
)
from .harness.fixture_loader import FixtureLoader

_ISO8601_DURATION_RE = re.compile(r"^PT(?:(\d+(?:\.\d+)?)H)?(?:(\d+(?:\.\d+)?)M)?(?:(\d+(?:\.\d+)?)S)?$")

_FIXTURE_IDS = (
    "transport.envelope.200-empty-body",
    "transport.envelope.204",
    "transport.envelope.non-json",
    "transport.envelope.shape-b",
    "transport.headers.authenticated-read",
    "transport.headers.login-omits-token",
    "transport.headers.namespace",
    "transport.headers.reserved-rejected",
    "transport.method.list-verb",
    "transport.retry.connection-refused-then-ok",
    "transport.retry.write-not-retried",
    "transport.status.307-not-followed",
    "transport.status.404-empty-read-returns-null",
    "transport.status.405-empty",
    "transport.status.429-dos-guard",
    "transport.status.429-namespace-quota",
    "transport.status.503-sealed",
    "transport.url.encoding-vectors",
)


def _parse_iso8601_duration(text: str) -> timedelta:
    match = _ISO8601_DURATION_RE.match(text)
    if not match:
        raise ValueError(f"unsupported ISO-8601 duration in fixture: {text!r}")
    hours, minutes, seconds = (float(part) if part else 0.0 for part in match.groups())
    return timedelta(hours=hours, minutes=minutes, seconds=seconds)


class _FixedJitterSource:
    def next_double(self) -> float:
        return 0.5


def _client_options_from_settings(configuration: ClientConfiguration) -> ClientOptions:
    settings = configuration.settings if isinstance(configuration.settings, Mapping) else {}
    retry_settings = settings.get("RetryPolicy")
    retry_settings = retry_settings if isinstance(retry_settings, Mapping) else {}
    rate_settings = settings.get("RateGate")
    rate_settings = rate_settings if isinstance(rate_settings, Mapping) else {}
    headers_settings = settings.get("Headers")
    headers_settings = headers_settings if isinstance(headers_settings, Mapping) else {}

    retry_kwargs: dict[str, Any] = {}
    if "MaxAttempts" in retry_settings:
        retry_kwargs["max_attempts"] = int(retry_settings["MaxAttempts"])
    if "InitialBackoff" in retry_settings:
        retry_kwargs["initial_backoff"] = _parse_iso8601_duration(str(retry_settings["InitialBackoff"]))
    retry_policy = RetryPolicy(**retry_kwargs) if retry_kwargs else None

    rate_kwargs: dict[str, Any] = {}
    if "RatePerSecond" in rate_settings:
        rate_kwargs["rate_per_second"] = int(rate_settings["RatePerSecond"])
    if "Burst" in rate_settings:
        rate_kwargs["burst"] = int(rate_settings["Burst"])
    rate_gate = RateGate(**rate_kwargs) if rate_kwargs else None

    return ClientOptions(
        address=configuration.address,
        token=configuration.token,
        namespace=configuration.namespace,
        api_prefix=configuration.api_prefix,
        retry_policy=retry_policy,
        rate_gate=rate_gate,
        headers=dict(headers_settings) or None,
    )


def _make_client(configuration: ClientConfiguration, transport: FakeTransport) -> Client:
    """Build the real `Client`, wired to D-M2-7's two instruments.

    The clock, the capturing logger and the capturing `RequestObserver` all come from
    `configuration.instruments` rather than from local fakes: the driver asserts on all
    three after **every** fixture run (TST-051 is a whole-run assertion, not a per-fixture
    opt-in), and a handler that quietly injected its own clock would make the driver's
    "was the declared clock honoured" check unfalsifiable.
    """
    options = _client_options_from_settings(configuration)
    instruments = configuration.instruments
    return Client(
        replace(options, logger=instruments.logger, observer=instruments.observer),
        environment=MapEnvironmentSource(configuration.environment),
        transport=transport,
        clock=instruments.clock,
        jitter_source=_FixedJitterSource(),
    )


def _response_to_dict(response: Response | None) -> Any:
    if response is None:
        return None
    auth_dict: Any = None
    if response.auth is not None:
        auth_dict = {
            "ClientToken": RedactedValue(response.auth.client_token.reveal()),
            "Policies": list(response.auth.policies),
            "LeaseDuration": int(response.auth.lease_duration.total_seconds()),
            "Renewable": response.auth.renewable,
        }
    lease_duration = (
        int(response.lease_duration.total_seconds()) if response.lease_duration is not None else None
    )
    return {
        "Data": response.data,
        "Auth": auth_dict,
        "LeaseId": response.lease_id,
        "Renewable": response.renewable,
        "LeaseDuration": lease_duration,
        "StatusCode": response.status_code,
    }


def _raw_response_to_dict(response: RawResponse) -> Any:
    return {"StatusCode": response.status_code, "Body": response.body}


def _error_to_operation_error(error: BastionVaultError, *, client: Client) -> OperationError:
    retry_after = int(error.retry_after.total_seconds()) if error.retry_after is not None else None
    return OperationError(
        surfaced=error,
        code=error.code,
        status_code=error.status_code,
        retryable=error.retryable,
        attempts=error.attempts,
        retry_after=retry_after,
        details=dict(error.details),
        hint=error.hint,
        server_message=error.server_message,
        client_state={"RateGate.Paused": client.rate_gate_state.paused},
    )


async def _run_logical(
    configuration: ClientConfiguration,
    transport: FakeTransport,
    operation: Mapping[str, Any],
    method_name: str,
) -> Any:
    client = _make_client(configuration, transport)
    args = operation.get("args", {})
    try:
        if method_name == "read":
            result = await client.logical.read(args["path"])
        elif method_name == "write":
            result = await client.logical.write(args["path"], args.get("body"))
        elif method_name == "delete":
            result = await client.logical.delete(args["path"], args.get("body"))
        else:
            result = await client.logical.list(args["path"])
    except BastionVaultError as error:
        raise _error_to_operation_error(error, client=client) from error
    return _response_to_dict(result)


async def _run_raw(
    configuration: ClientConfiguration, transport: FakeTransport, operation: Mapping[str, Any]
) -> Any:
    client = _make_client(configuration, transport)
    args = operation.get("args", {})
    try:
        result = await client.logical.raw(args["method"], args["path"], args.get("body"))
    except BastionVaultError as error:
        raise _error_to_operation_error(error, client=client) from error
    return _raw_response_to_dict(result)


def _run_client_construct(
    configuration: ClientConfiguration, transport: FakeTransport, operation: Mapping[str, Any]
) -> Any:
    del operation
    try:
        client = _make_client(configuration, transport)
    except BastionVaultError as error:
        raise OperationError(
            code=error.code,
            status_code=error.status_code,
            retryable=error.retryable,
            attempts=error.attempts,
            details=dict(error.details),
            hint=error.hint,
            server_message=error.server_message,
        ) from error
    return {"is_insecure": client.is_insecure}


def _build_registry() -> OperationRegistry:
    registry = OperationRegistry()
    registry.register("Client.Construct", _run_client_construct)
    registry.register("Logical.Read", lambda c, t, o: _run_logical(c, t, o, "read"))
    registry.register("Logical.Write", lambda c, t, o: _run_logical(c, t, o, "write"))
    registry.register("Logical.Delete", lambda c, t, o: _run_logical(c, t, o, "delete"))
    registry.register("Logical.List", lambda c, t, o: _run_logical(c, t, o, "list"))
    registry.register("Logical.Raw", _run_raw)
    return registry


@pytest.mark.parametrize("fixture_id", _FIXTURE_IDS)
def test_transport_fixture_passes(fixture_id: str) -> None:
    """@req TRN-001 @req TRN-010 @req TRN-040 @req TRN-050 @req CFG-050 @req CFG-052"""
    loader = FixtureLoader()
    fixture = loader.load_fixture(fixture_id)

    result = FixtureDriver(_build_registry()).run(fixture)

    assert result.status == "passed"


def test_all_transport_fixtures_are_covered() -> None:
    """Guards against a new fixture landing under transport/ without being wired here."""
    loader = FixtureLoader()
    transport_fixtures = {
        fixture["id"] for fixture in loader.enumerate_fixtures() if fixture["id"].startswith("transport.")
    }
    assert transport_fixtures == set(_FIXTURE_IDS)
