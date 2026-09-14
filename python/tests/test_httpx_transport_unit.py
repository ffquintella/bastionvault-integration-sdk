"""Unit coverage for the production `HttpxTransport` (D-M1b-3), with no socket opened.

`test_httpx_transport.py` drives the same class against a real in-process HTTPS listener
and stays exactly as it is, marked `integration`. This module is the complement: it
substitutes `httpx.MockTransport` for the pooled connection so the request construction,
the custom `LIST` verb, the failure mapping (D-M1b-4a) and the `MaxResponseBytes` bound
(D-M1b-20) are exercised by the CI job's own invocation, which deselects
`integration`. Neither suite replaces the other.
"""

from __future__ import annotations

import asyncio
import datetime
import ssl
from collections.abc import AsyncIterator, Callable, Iterable
from datetime import timedelta
from pathlib import Path
from typing import Any

import httpx
import pytest

from bastionvault_integration_sdk.config import ClientConfig, ClientOptions
from bastionvault_integration_sdk.errors import BastionVaultError, ErrorCodes
from bastionvault_integration_sdk.httpx_transport import (
    HttpxTransport,
    _contains_ssl_error,
    build_ssl_context,
)
from bastionvault_integration_sdk.transport import TransportRequest, TransportResponse


def _config(**overrides: Any) -> ClientConfig:
    overrides.setdefault("address", "https://vault.example.com:8200")
    return ClientConfig.resolve(ClientOptions(**overrides))


def _request(**overrides: Any) -> TransportRequest:
    overrides.setdefault("method", "GET")
    overrides.setdefault("url", "https://vault.example.com:8200/v1/secret/data/app")
    overrides.setdefault("timeout", timedelta(seconds=5))
    overrides.setdefault("connect_timeout", timedelta(seconds=2))
    return TransportRequest(**overrides)


def _send(
    handler: Callable[[httpx.Request], httpx.Response],
    request: TransportRequest,
    *,
    config: ClientConfig | None = None,
) -> TransportResponse:
    """Run one `send` against `httpx.MockTransport`, in a single event loop.

    The transport is built exactly as production builds it -- including the real SSL
    context -- and only its pooled `AsyncClient` is swapped, so everything above the
    connection is the shipped code path.
    """

    async def _run() -> TransportResponse:
        transport = HttpxTransport(config if config is not None else _config())
        await transport._client.aclose()
        transport._client = httpx.AsyncClient(transport=httpx.MockTransport(handler))
        try:
            return await transport.send(request)
        finally:
            await transport.aclose()

    return asyncio.run(_run())


# --------------------------------------------------------------------------------------
# Request construction and the LIST verb
# --------------------------------------------------------------------------------------


def test_method_url_headers_and_body_reach_the_wire_unchanged() -> None:
    """@req TRN-010 @req TRN-020"""
    seen: list[httpx.Request] = []

    def handler(request: httpx.Request) -> httpx.Response:
        seen.append(request)
        return httpx.Response(200, json={"data": {"a": 1}})

    response = _send(
        handler,
        _request(
            method="POST",
            headers={"X-BastionVault-Token": "s.fake", "Accept": "application/json"},
            body=b'{"a":1}',
        ),
    )

    assert response.status_code == 200
    assert seen[0].method == "POST"
    assert str(seen[0].url) == "https://vault.example.com:8200/v1/secret/data/app"
    assert seen[0].headers["x-bastionvault-token"] == "s.fake"
    assert seen[0].read() == b'{"a":1}'


def test_the_custom_list_verb_is_sent_verbatim() -> None:
    """@req TRN-010"""
    seen: list[str] = []

    def handler(request: httpx.Request) -> httpx.Response:
        seen.append(request.method)
        return httpx.Response(200, json={"data": {"keys": []}})

    _send(handler, _request(method="LIST"))

    assert seen == ["LIST"]
    assert HttpxTransport.supports_custom_verbs is True


def test_response_status_headers_and_body_are_returned_verbatim() -> None:
    """@req TRN-040"""

    def handler(request: httpx.Request) -> httpx.Response:
        del request
        return httpx.Response(418, headers={"X-Teapot": "yes"}, content=b"short and stout")

    response = _send(handler, _request())

    assert response.status_code == 418
    assert response.headers["x-teapot"] == "yes"
    assert response.body == b"short and stout"


def test_tls_server_name_is_passed_as_the_sni_hostname_extension() -> None:
    """@req CFG-044"""
    seen: list[dict[str, Any]] = []

    def handler(request: httpx.Request) -> httpx.Response:
        seen.append(dict(request.extensions))
        return httpx.Response(200, json={})

    _send(handler, _request(), config=_config(tls_server_name="vault.internal"))

    assert seen[0]["sni_hostname"] == "vault.internal"


def test_no_sni_extension_is_set_when_tls_server_name_is_unset() -> None:
    """@req CFG-044"""
    seen: list[dict[str, Any]] = []

    def handler(request: httpx.Request) -> httpx.Response:
        seen.append(dict(request.extensions))
        return httpx.Response(200, json={})

    _send(handler, _request())

    assert "sni_hostname" not in seen[0]


# --------------------------------------------------------------------------------------
# Transport failure mapping (D-M1b-4a)
# --------------------------------------------------------------------------------------


def _failing(error: BaseException) -> Callable[[httpx.Request], httpx.Response]:
    def handler(request: httpx.Request) -> httpx.Response:
        del request
        raise error

    return handler


def _expect_code(error: BaseException, code: str) -> BastionVaultError:
    with pytest.raises(BastionVaultError) as excinfo:
        _send(_failing(error), _request())
    assert excinfo.value.code == code
    return excinfo.value


def test_a_connect_timeout_and_a_read_timeout_both_map_to_transport_timeout() -> None:
    """@req TRN-054 @req RES-004"""
    connect = _expect_code(httpx.ConnectTimeout("too slow"), ErrorCodes.TRANSPORT_TIMEOUT)
    read = _expect_code(httpx.ReadTimeout("too slow"), ErrorCodes.TRANSPORT_TIMEOUT)

    assert connect.retryable is True
    assert isinstance(connect.cause, httpx.TimeoutException)
    assert isinstance(read.cause, httpx.TimeoutException)


def test_a_plain_connect_error_maps_to_connection_failed() -> None:
    """@req TRN-054"""
    error = _expect_code(httpx.ConnectError("refused"), ErrorCodes.TRANSPORT_CONNECTION_FAILED)

    assert error.retryable is True


def test_a_connect_error_wrapping_an_ssl_error_maps_to_the_tls_code() -> None:
    """@req TRN-054"""
    wrapped = httpx.ConnectError("handshake failed")
    wrapped.__cause__ = ssl.SSLCertVerificationError("unable to get local issuer certificate")

    _expect_code(wrapped, ErrorCodes.TRANSPORT_TLS_ERROR)


def test_a_bare_ssl_error_maps_to_the_tls_code() -> None:
    """@req TRN-054"""
    _expect_code(ssl.SSLError("wrong version number"), ErrorCodes.TRANSPORT_TLS_ERROR)


def test_any_other_httpx_error_maps_to_connection_failed() -> None:
    """@req TRN-054"""
    # TRN-054/ERR-020: no runtime exception type ever escapes the transport seam.
    _expect_code(httpx.ReadError("reset by peer"), ErrorCodes.TRANSPORT_CONNECTION_FAILED)
    _expect_code(httpx.RemoteProtocolError("bad chunk"), ErrorCodes.TRANSPORT_CONNECTION_FAILED)


def test_cancellation_is_not_swallowed_by_the_transport() -> None:
    """@req OVR-006"""
    # `CancelledError` is not an `httpx.HTTPError`; it propagates so `Client` can map it
    # to BV-TRANSPORT-005 at the one place that knows the operation was cancelled.
    with pytest.raises(asyncio.CancelledError):
        _send(_failing(asyncio.CancelledError()), _request())


# --------------------------------------------------------------------------------------
# `_contains_ssl_error`: the cause-chain search
# --------------------------------------------------------------------------------------


def test_contains_ssl_error_finds_a_direct_a_caused_and_an_argument_wrapped_error() -> None:
    """@req TRN-054"""
    assert _contains_ssl_error(ssl.SSLError("direct")) is True

    caused = httpx.ConnectError("outer")
    caused.__cause__ = ssl.SSLError("inner")
    assert _contains_ssl_error(caused) is True

    # httpcore sometimes carries the real error positionally rather than as __cause__.
    # `ConnectError.__init__` is typed `str`, but the runtime shape this normalises is a
    # nested exception object, so the argument is built and attached deliberately.
    positional = httpx.ConnectError("outer")
    positional.args = (ssl.SSLError("positional"),)
    assert _contains_ssl_error(positional) is True


def test_contains_ssl_error_is_false_for_an_unrelated_chain_and_tolerates_a_cycle() -> None:
    """@req TRN-054"""
    assert _contains_ssl_error(httpx.ConnectError("refused")) is False

    first = httpx.ConnectError("first")
    second = httpx.ConnectError("second")
    first.__cause__ = second
    second.__cause__ = first
    assert _contains_ssl_error(first) is False


def test_contains_ssl_error_stops_at_the_depth_cap() -> None:
    """@req TRN-054"""
    # A chain longer than _MAX_CAUSE_CHAIN_DEPTH is abandoned rather than walked forever.
    deepest: BaseException = ssl.SSLError("buried far too deep")
    for index in range(20):
        wrapper = httpx.ConnectError(f"layer {index}")
        wrapper.__cause__ = deepest
        deepest = wrapper

    assert _contains_ssl_error(deepest) is False


# --------------------------------------------------------------------------------------
# `MaxResponseBytes` (TRN-033, D-M1b-20)
# --------------------------------------------------------------------------------------


class _ChunkedStream(httpx.AsyncByteStream):
    """A chunked body with no `Content-Length`, so the streaming bound is the only guard."""

    def __init__(self, chunks: Iterable[bytes]) -> None:
        self._chunks = list(chunks)

    async def __aiter__(self) -> AsyncIterator[bytes]:
        for chunk in self._chunks:
            yield chunk


def _streaming(
    chunks: Iterable[bytes], headers: dict[str, str] | None = None
) -> Callable[[httpx.Request], httpx.Response]:
    def handler(request: httpx.Request) -> httpx.Response:
        del request
        return httpx.Response(200, headers=headers, stream=_ChunkedStream(chunks))

    return handler


def test_an_over_bound_content_length_is_rejected_without_reading_the_body() -> None:
    """@req TRN-033"""

    def handler(request: httpx.Request) -> httpx.Response:
        del request
        return httpx.Response(200, content=b"x" * 100)

    with pytest.raises(BastionVaultError) as excinfo:
        _send(handler, _request(max_response_bytes=10))

    assert excinfo.value.code == ErrorCodes.TRANSPORT_RESPONSE_TOO_LARGE
    assert excinfo.value.status_code == 200


def test_an_unparsable_content_length_falls_through_to_the_streaming_bound() -> None:
    """@req TRN-033"""
    handler = _streaming([b"x" * 8, b"y" * 8], headers={"content-length": "not-a-number"})

    with pytest.raises(BastionVaultError) as excinfo:
        _send(handler, _request(max_response_bytes=10))

    assert excinfo.value.code == ErrorCodes.TRANSPORT_RESPONSE_TOO_LARGE


def test_a_stream_that_crosses_the_bound_is_aborted_mid_read() -> None:
    """@req TRN-033"""
    handler = _streaming([b"x" * 8, b"y" * 8])

    with pytest.raises(BastionVaultError) as excinfo:
        _send(handler, _request(max_response_bytes=10))

    assert excinfo.value.code == ErrorCodes.TRANSPORT_RESPONSE_TOO_LARGE


def test_a_stream_inside_the_bound_is_joined_whole() -> None:
    """@req TRN-033"""
    response = _send(_streaming([b"abc", b"def"]), _request(max_response_bytes=64))

    assert response.body == b"abcdef"


def test_no_bound_reads_the_whole_body() -> None:
    """@req TRN-033"""
    response = _send(_streaming([b"abc", b"def"]), _request(max_response_bytes=None))

    assert response.body == b"abcdef"


# --------------------------------------------------------------------------------------
# `build_ssl_context` (CFG-040/041/042/044)
# --------------------------------------------------------------------------------------


def _ca_pem(tmp_path: Path) -> tuple[str, str]:
    """A throwaway self-signed CA, returned as PEM text and as a file path."""

    from cryptography import x509
    from cryptography.hazmat.primitives import hashes, serialization
    from cryptography.hazmat.primitives.asymmetric import rsa
    from cryptography.x509.oid import NameOID

    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    name = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, "bastionvault-unit-ca")])
    now = datetime.datetime.now(datetime.timezone.utc)
    certificate = (
        x509.CertificateBuilder()
        .subject_name(name)
        .issuer_name(name)
        .public_key(key.public_key())
        .serial_number(x509.random_serial_number())
        .not_valid_before(now - datetime.timedelta(days=1))
        .not_valid_after(now + datetime.timedelta(days=1))
        .add_extension(x509.BasicConstraints(ca=True, path_length=None), critical=True)
        .sign(key, hashes.SHA256())
    )
    pem = certificate.public_bytes(serialization.Encoding.PEM).decode("ascii")
    path = tmp_path / "ca.pem"
    path.write_text(pem, encoding="utf-8")
    key_path = tmp_path / "ca.key.pem"
    key_path.write_bytes(
        key.private_bytes(
            serialization.Encoding.PEM,
            serialization.PrivateFormat.PKCS8,
            serialization.NoEncryption(),
        )
    )
    return pem, str(path)


def test_the_default_context_verifies_and_checks_the_hostname() -> None:
    """@req CFG-040"""
    context = build_ssl_context(_config())

    assert context.check_hostname is True
    assert context.verify_mode is ssl.CERT_REQUIRED
    assert context.minimum_version is ssl.TLSVersion.TLSv1_2


def test_tls_skip_verify_disables_verification_and_hostname_checking() -> None:
    """@req CFG-018"""
    context = build_ssl_context(_config(tls_skip_verify=True))

    assert context.check_hostname is False
    assert context.verify_mode is ssl.CERT_NONE


def test_a_ca_pem_and_a_ca_path_are_each_added_to_the_system_roots(tmp_path: Path) -> None:
    """@req CFG-041"""
    pem, path = _ca_pem(tmp_path)

    from_pem = build_ssl_context(_config(ca_cert_pem=pem))
    from_path = build_ssl_context(_config(ca_cert_path=path))

    assert any(cert["subject"] for cert in from_pem.get_ca_certs())
    assert any(cert["subject"] for cert in from_path.get_ca_certs())


def test_ca_cert_replaces_system_roots_loads_only_the_configured_ca(tmp_path: Path) -> None:
    """@req CFG-042"""
    pem, path = _ca_pem(tmp_path)

    from_pem = build_ssl_context(_config(ca_cert_pem=pem, ca_cert_replaces_system_roots=True))
    from_path = build_ssl_context(_config(ca_cert_path=path, ca_cert_replaces_system_roots=True))

    assert len(from_pem.get_ca_certs()) == 1
    assert len(from_path.get_ca_certs()) == 1


def test_a_client_certificate_and_key_are_loaded_into_the_context(tmp_path: Path) -> None:
    """@req CFG-012 @req CFG-013"""
    from cryptography import x509
    from cryptography.hazmat.primitives import hashes, serialization
    from cryptography.hazmat.primitives.asymmetric import rsa
    from cryptography.x509.oid import NameOID

    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    name = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, "bastionvault-unit-client")])
    now = datetime.datetime.now(datetime.timezone.utc)
    certificate = (
        x509.CertificateBuilder()
        .subject_name(name)
        .issuer_name(name)
        .public_key(key.public_key())
        .serial_number(x509.random_serial_number())
        .not_valid_before(now - datetime.timedelta(days=1))
        .not_valid_after(now + datetime.timedelta(days=1))
        .sign(key, hashes.SHA256())
    )
    cert_path = tmp_path / "client.crt.pem"
    key_path = tmp_path / "client.key.pem"
    cert_path.write_bytes(certificate.public_bytes(serialization.Encoding.PEM))
    key_path.write_bytes(
        key.private_bytes(
            serialization.Encoding.PEM,
            serialization.PrivateFormat.PKCS8,
            serialization.NoEncryption(),
        )
    )

    context = build_ssl_context(
        _config(client_cert_path=str(cert_path), client_key_path=str(key_path))
    )

    assert context.get_ca_certs() is not None
