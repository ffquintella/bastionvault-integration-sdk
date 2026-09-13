"""Integration tests for the production `HttpxTransport` (D-M1b-3) against a real
in-process HTTPS listener (`MockHttpsServer`). Marked `integration` (TST convention):
these open a real socket, unlike every other test in this suite.
"""

from __future__ import annotations

import asyncio
import socket
from datetime import timedelta

import pytest

from bastionvault_integration_sdk.config import ClientConfig, ClientOptions
from bastionvault_integration_sdk.errors import BastionVaultError, ErrorCodes
from bastionvault_integration_sdk.httpx_transport import HttpxTransport
from bastionvault_integration_sdk.transport import TransportRequest, TransportResponse

from .harness.mock_server import MockHttpsServer

pytestmark = pytest.mark.integration


def _config(**overrides: object) -> ClientConfig:
    return ClientConfig.resolve(ClientOptions(**overrides))  # type: ignore[arg-type]


def _send(config: ClientConfig, request: TransportRequest) -> TransportResponse:
    """Send one request and close the transport, all inside one event loop.

    Two separate `asyncio.run()` calls would create the connection in one loop and
    close it from another, which Windows' proactor loop rejects.
    """

    async def _run() -> TransportResponse:
        transport = HttpxTransport(config)
        try:
            return await transport.send(request)
        finally:
            await transport.aclose()

    return asyncio.run(_run())


def test_successful_get_round_trips_status_headers_and_body() -> None:
    """@req TRN-090 @req D-M1b-3"""
    with MockHttpsServer() as mock:
        config = _config(address=mock.base_url, ca_cert_path=str(mock.ca_certificate_path))
        response = _send(
            config,
            TransportRequest(
                method="GET",
                url=f"{mock.base_url}/anything",
                timeout=timedelta(seconds=5),
                connect_timeout=timedelta(seconds=5),
            ),
        )
        assert response.status_code == 200
        assert b"ok" in response.body


def test_custom_list_verb_is_sent_literally() -> None:
    """@req TRN-010"""
    with MockHttpsServer() as mock:
        config = _config(address=mock.base_url, ca_cert_path=str(mock.ca_certificate_path))
        response = _send(config, TransportRequest(method="LIST", url=f"{mock.base_url}/x"))
        assert response.status_code == 200
        assert mock.list_request_count == 1


def test_tls_verification_failure_is_bv_transport_003() -> None:
    """@req TRN-090 @req D-M1b-4a"""
    with MockHttpsServer() as mock:
        # No CA configured: the mock server's self-signed root is not trusted.
        config = _config(address=mock.base_url)
        with pytest.raises(BastionVaultError) as excinfo:
            _send(
                config,
                TransportRequest(
                    method="GET",
                    url=f"{mock.base_url}/x",
                    timeout=timedelta(seconds=5),
                    connect_timeout=timedelta(seconds=5),
                ),
            )
        assert excinfo.value.code == ErrorCodes.TRANSPORT_TLS_ERROR


def test_connection_refused_is_a_transport_failure() -> None:
    """@req D-M1b-4a"""
    # Bind an ephemeral port, then close it so the port is refused, not filtered.
    probe = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    probe.bind(("127.0.0.1", 0))
    _, closed_port = probe.getsockname()
    probe.close()

    config = _config(address=f"https://127.0.0.1:{closed_port}")
    with pytest.raises(BastionVaultError) as excinfo:
        _send(
            config,
            TransportRequest(
                method="GET",
                url=f"https://127.0.0.1:{closed_port}/x",
                timeout=timedelta(seconds=5),
                connect_timeout=timedelta(seconds=5),
            ),
        )
    # However this sandbox's network stack surfaces a closed local port (immediate
    # refusal on some hosts, a connect timeout on others), both are transport-level
    # failures a caller must see identically distinguished from a TLS/protocol error.
    assert excinfo.value.code in (ErrorCodes.TRANSPORT_CONNECTION_FAILED, ErrorCodes.TRANSPORT_TIMEOUT)


def test_response_timeout_is_bv_transport_002() -> None:
    """@req D-M1b-4a"""
    with MockHttpsServer(response_delay=1.0) as mock:
        config = _config(address=mock.base_url, ca_cert_path=str(mock.ca_certificate_path))
        with pytest.raises(BastionVaultError) as excinfo:
            _send(
                config,
                TransportRequest(
                    method="GET",
                    url=f"{mock.base_url}/slow",
                    timeout=timedelta(milliseconds=100),
                    connect_timeout=timedelta(seconds=5),
                ),
            )
        assert excinfo.value.code == ErrorCodes.TRANSPORT_TIMEOUT


def test_oversized_response_is_aborted_not_buffered_then_checked() -> None:
    """@req TRN-033 @req D-M1b-20

    Proves the *abort*, not merely the resulting code (D-M1b-20's central point): the
    server is configured to answer with 8 MiB, `MaxResponseBytes` is capped at 1 KiB,
    and the transport must raise as soon as the bound is passed while streaming, never
    after buffering the full body.
    """
    oversized = 8 * 1024 * 1024
    with MockHttpsServer(oversized_response_size=oversized) as mock:
        config = _config(
            address=mock.base_url,
            ca_cert_path=str(mock.ca_certificate_path),
            max_response_bytes=1024,
        )
        with pytest.raises(BastionVaultError) as excinfo:
            _send(
                config,
                TransportRequest(
                    method="GET",
                    url=f"{mock.base_url}/oversized",
                    timeout=timedelta(seconds=10),
                    connect_timeout=timedelta(seconds=5),
                    max_response_bytes=1024,
                ),
            )
        assert excinfo.value.code == ErrorCodes.TRANSPORT_RESPONSE_TOO_LARGE


def test_client_certificate_is_presented_on_every_connection() -> None:
    """@req CFG-044"""
    with MockHttpsServer(require_client_certificate=True) as mock:
        config = _config(
            address=mock.base_url,
            ca_cert_path=str(mock.ca_certificate_path),
            client_cert_path=str(mock.client_certificate_path),
            client_key_path=str(mock.client_key_path),
        )
        response = _send(
            config,
            TransportRequest(
                method="GET",
                url=f"{mock.base_url}/x",
                timeout=timedelta(seconds=5),
                connect_timeout=timedelta(seconds=5),
            ),
        )
        assert response.status_code == 200


def test_tls_skip_verify_accepts_hostname_mismatch() -> None:
    """@req CFG-018"""
    with MockHttpsServer(hostname_mismatch=True) as mock:
        config = _config(address=mock.base_url, tls_skip_verify=True)
        response = _send(
            config,
            TransportRequest(
                method="GET",
                url=f"{mock.base_url}/x",
                timeout=timedelta(seconds=5),
                connect_timeout=timedelta(seconds=5),
            ),
        )
        assert response.status_code == 200
