"""Closes the gap between "code exists" and "MUST is tested" (D-M1a-10) for a handful
of M1b requirements not otherwise exercised by a fixture or the retry/logical suite:
TRN-002 (leading slash / per-call ApiVersion), TRN-043 (`Raw` retains the exact parsed
body), TRN-090/091 (pooling, proxy default), TRN-092 (unbracketed IPv6 rejected).
"""

from __future__ import annotations

import asyncio
from datetime import timedelta

import pytest

from bastionvault_integration_sdk import BastionVaultError, Client, ClientOptions
from bastionvault_integration_sdk.errors import ErrorCodes
from bastionvault_integration_sdk.httpx_transport import HttpxTransport
from bastionvault_integration_sdk.testing import FakeTransport
from bastionvault_integration_sdk.transport import RequestOptions, TransportRequest, TransportResponse

from .harness.mock_server import MockHttpsServer


def test_trn_002_leading_slash_is_stripped_and_api_version_overrides_prefix() -> None:
    """@req TRN-002"""
    transport = FakeTransport(
        exchanges=[TransportResponse(status_code=200, headers={}, body=b'{"data": {}}')]
    )
    client = Client(
        ClientOptions(address="https://vault.example.com:8200", api_prefix="v1"), transport=transport
    )

    asyncio.run(client.logical.read("/secret/data/x", RequestOptions(api_version="v2")))

    assert transport.requests[0].url == "https://vault.example.com:8200/v2/secret/data/x"


def test_trn_043_raw_response_carries_the_exact_parsed_body() -> None:
    """@req TRN-040 @req TRN-043"""
    transport = FakeTransport(
        exchanges=[TransportResponse(status_code=200, headers={}, body=b'{"data": {"a": 1}, "extra": true}')]
    )
    client = Client(ClientOptions(address="https://vault.example.com:8200"), transport=transport)

    response = asyncio.run(client.logical.read("secret/data/x"))

    assert response is not None
    assert response.raw == {"data": {"a": 1}, "extra": True}


@pytest.mark.parametrize("address", ["https://::1:8200", "https://2001:db8::1:8200"])
def test_trn_092_unbracketed_ipv6_with_port_is_rejected(address: str) -> None:
    """@req TRN-092"""
    with pytest.raises(BastionVaultError) as excinfo:
        Client(ClientOptions(address=address))
    assert excinfo.value.code == ErrorCodes.CONFIG_INVALID_ADDRESS


def test_trn_092_bracketed_ipv6_is_accepted() -> None:
    """@req TRN-092"""
    client = Client(ClientOptions(address="https://[::1]:8200"))
    assert client.config.address == "https://[::1]:8200"


@pytest.mark.integration
def test_trn_090_connections_are_pooled_across_requests() -> None:
    """@req TRN-090"""
    with MockHttpsServer() as mock:
        from bastionvault_integration_sdk.config import ClientConfig

        config = ClientConfig.resolve(
            ClientOptions(address=mock.base_url, ca_cert_path=str(mock.ca_certificate_path))
        )

        async def _run() -> None:
            transport = HttpxTransport(config)
            try:
                for _ in range(3):
                    await transport.send(
                        TransportRequest(
                            method="GET",
                            url=f"{mock.base_url}/x",
                            timeout=timedelta(seconds=5),
                            connect_timeout=timedelta(seconds=5),
                        )
                    )
            finally:
                await transport.aclose()

        asyncio.run(_run())
        assert mock.accepted_connections == 1


@pytest.mark.integration
def test_trn_091_proxy_is_disabled_unless_use_system_proxy_is_set() -> None:
    """@req TRN-091"""
    from bastionvault_integration_sdk.config import ClientConfig

    default_config = ClientConfig.resolve(ClientOptions(address="https://vault.example.com:8200"))
    opted_in_config = ClientConfig.resolve(
        ClientOptions(address="https://vault.example.com:8200", use_system_proxy=True)
    )

    default_transport = HttpxTransport(default_config)
    opted_in_transport = HttpxTransport(opted_in_config)
    try:
        assert default_transport._client.trust_env is False
        assert opted_in_transport._client.trust_env is True
    finally:
        asyncio.run(default_transport.aclose())
        asyncio.run(opted_in_transport.aclose())
