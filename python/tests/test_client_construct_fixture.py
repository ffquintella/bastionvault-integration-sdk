"""Registers `Client.Construct` against the shared fixture repository (D-M1a-6/11/12).

`transport.headers.reserved-rejected` is the one Appendix C fixture in scope for M1a
(CFG-017 / `BV-CONFIG-008`). It must pass end-to-end against real SDK code, not a
test-only shim.
"""

from collections.abc import Mapping
from typing import Any

from bastionvault_integration_sdk import Client, ClientOptions, MapEnvironmentSource
from bastionvault_integration_sdk.errors import BastionVaultError

from .harness.fixture_driver import (
    ClientConfiguration,
    FakeTransport,
    FixtureDriver,
    OperationError,
    OperationRegistry,
)
from .harness.fixture_loader import FixtureLoader


def _client_construct(
    configuration: ClientConfiguration, transport: FakeTransport, operation: Mapping[str, Any]
) -> Any:
    del transport, operation
    settings = configuration.settings
    options = ClientOptions(
        address=configuration.address,
        token=configuration.token,
        namespace=configuration.namespace,
        api_prefix=configuration.api_prefix,
    )
    headers = settings.get("Headers") if isinstance(settings, Mapping) else None
    if isinstance(headers, Mapping):
        options.headers = dict(headers)

    try:
        client = Client(options, environment=MapEnvironmentSource(configuration.environment))
    except BastionVaultError as error:
        raise OperationError(
            code=error.code,
            status_code=error.status_code,
            retryable=error.retryable,
            attempts=error.attempts,
            retry_after=None,
            details=dict(error.details),
            hint=error.hint,
            server_message=error.server_message,
        ) from error
    return {"is_insecure": client.is_insecure}


def test_client_construct_rejects_reserved_header_fixture() -> None:
    """@req CFG-017 @req OVR-001"""
    loader = FixtureLoader()
    fixture = loader.load_fixture("transport.headers.reserved-rejected")
    registry = OperationRegistry()
    registry.register("Client.Construct", _client_construct)

    result = FixtureDriver(registry).run(fixture)

    assert result.status == "passed"
