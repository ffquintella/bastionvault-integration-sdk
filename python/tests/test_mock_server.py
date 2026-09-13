"""Raw-client tests for the runtime HTTPS mock server."""

from __future__ import annotations

import http.client
import ssl
from dataclasses import dataclass
from pathlib import Path

import pytest

from .harness.mock_server import MockHttpsServer


@dataclass(frozen=True)
class RawResponse:
    status: int
    headers: dict[str, str]
    body: bytes


class RawHttpsClient:
    """Small raw HTTPS client used only by the mock-server tests."""

    def __init__(self, server: MockHttpsServer, context: ssl.SSLContext) -> None:
        host, port_text = server.base_url.removeprefix("https://").split(":")
        self._connection = http.client.HTTPSConnection(host, int(port_text), context=context, timeout=3)

    def request(self, method: str, path: str) -> RawResponse:
        self._connection.request(method, path, headers={"Connection": "keep-alive"})
        response = self._connection.getresponse()
        return RawResponse(response.status, dict(response.getheaders()), response.read())

    def close(self) -> None:
        self._connection.close()


def _pinned_context(server: MockHttpsServer) -> ssl.SSLContext:
    return ssl.create_default_context(cadata=server.ca_pem.decode("ascii"))


def test_list_reaches_handler_and_keep_alive_reuses_connection() -> None:
    """@req TST-020 @req TST-040"""
    with MockHttpsServer() as server:
        client = RawHttpsClient(server, _pinned_context(server))
        try:
            first = client.request("LIST", "/v1/secret/metadata/")
            second = client.request("LIST", "/v1/secret/metadata/")
        finally:
            client.close()

        assert first.status == second.status == 200
        assert server.list_request_count == 2
        assert server.request_count == 2
        assert server.accepted_connections == 1


@pytest.mark.parametrize(
    ("scenario", "status", "body", "retry_after"),
    [
        ("sealed", 503, b'{"errors":["server is sealed"]}', None),
        ("standby_health", 429, b'{"standby":true}', None),
        ("uninitialised", 501, b'{"errors":["server is uninitialised"]}', None),
        ("dos_ban", 429, b'{"errors":["request blocked by DoS protection"]}', "60"),
        ("namespace_quota", 429, b'{"errors":["namespace quota exceeded"]}', None),
        ("not_found", 404, b"", None),
        ("method_not_allowed", 405, b"", None),
        ("no_content", 204, b"", None),
        ("login_failure", 200, b'{"data":{"error":"invalid credentials"}}', None),
    ],
)
def test_each_tst021_simulation_returns_exact_contract(
    scenario: str, status: int, body: bytes, retry_after: str | None
) -> None:
    """@req TST-020 @req TST-021 @req TST-040"""
    with MockHttpsServer() as server:
        server.set_scenario("/scenario", scenario)
        client = RawHttpsClient(server, _pinned_context(server))
        try:
            response = client.request("GET", "/scenario")
        finally:
            client.close()

        assert response.status == status
        assert response.body == body
        expected_headers = {"Content-Length": str(len(body))}
        if body:
            expected_headers["Content-Type"] = "application/json"
        if retry_after is not None:
            expected_headers["Retry-After"] = retry_after
        assert response.headers == expected_headers


def test_tls_pin_hostname_mismatch_mtls_and_skip_verify() -> None:
    """@req TST-020 @req TST-040"""
    with MockHttpsServer() as server:
        unpinned = RawHttpsClient(server, ssl.create_default_context())
        try:
            with pytest.raises(ssl.SSLError):
                unpinned.request("GET", "/tls")
        finally:
            unpinned.close()

        pinned = RawHttpsClient(server, _pinned_context(server))
        try:
            assert pinned.request("GET", "/tls").status == 200
        finally:
            pinned.close()

        skipped = RawHttpsClient(server, ssl._create_unverified_context())
        try:
            assert skipped.request("GET", "/tls").status == 200
        finally:
            skipped.close()

    with MockHttpsServer(hostname_mismatch=True) as mismatch_server:
        mismatch = RawHttpsClient(mismatch_server, _pinned_context(mismatch_server))
        try:
            with pytest.raises(ssl.SSLError):
                mismatch.request("GET", "/tls")
        finally:
            mismatch.close()

    with MockHttpsServer(require_client_certificate=True) as mtls_server:
        without_client = RawHttpsClient(mtls_server, _pinned_context(mtls_server))
        try:
            # A missing client certificate is always rejected, but depending on
            # scheduling the client observes either a clean TLS alert (ssl.SSLError,
            # including its ssl.SSLEOFError subclass) or the raw connection reset that
            # precedes it (ConnectionResetError, whose subclass
            # http.client.RemoteDisconnected is raised by getresponse()). Both are the
            # same rejection; TST-020 cares that the connection is refused, not which
            # of the two layers reports it first.
            with pytest.raises((ssl.SSLError, ConnectionResetError)):
                without_client.request("GET", "/mtls")
        finally:
            without_client.close()

        client_context = _pinned_context(mtls_server)
        client_context.load_cert_chain(mtls_server.client_certificate_path, mtls_server.client_key_path)
        with_client = RawHttpsClient(mtls_server, client_context)
        try:
            assert with_client.request("GET", "/mtls").status == 200
        finally:
            with_client.close()


def test_delay_and_oversized_response_are_configurable_and_material_is_ephemeral() -> None:
    """@req TST-020 @req TST-021 @req TST-050"""
    with MockHttpsServer(response_delay=0.0, oversized_response_size=4096) as server:
        temp_dir = server.temp_dir
        server.configure_response_delay(0.0)
        assert server.response_delay == 0.0
        client = RawHttpsClient(server, _pinned_context(server))
        try:
            response = client.request("GET", "/oversized")
        finally:
            client.close()

        assert temp_dir.is_dir()
        assert response.status == 200
        assert len(response.body) == 4096
        assert Path(server.server_key_path).is_file()

    assert not temp_dir.exists()
