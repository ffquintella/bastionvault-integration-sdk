"""The production `Transport`: `httpx.AsyncClient` (D-M1b-3).

Custom verbs go through `request("LIST", ...)`; TLS material comes from an injected
`ssl.SSLContext` built from the resolved `ClientConfig` (CFG-040/041/042/044);
`trust_env=False` disables proxies by default (TRN-091); `follow_redirects=False` keeps
CNF-034/TRN-060 in the transport's own hands, not `httpx`'s. `MaxResponseBytes` is
enforced while streaming the body (D-M1b-20), never audited after buffering.
"""

from __future__ import annotations

import ssl

import httpx

from .config import ClientConfig
from .errors import ErrorCodes, make_error
from .transport import TransportRequest, TransportResponse

_MAX_CAUSE_CHAIN_DEPTH = 8


def _contains_ssl_error(error: BaseException) -> bool:
    """Search the cause chain (and each exception's `args`) for a wrapped `ssl.SSLError`.

    `httpx`/`httpcore` wrap a TLS handshake or verification failure inside
    `httpx.ConnectError`, with the real `ssl.SSLError` sometimes one level deeper, as a
    positional argument rather than `__cause__` -- this normalises both shapes.
    """
    seen: set[int] = set()
    frontier: list[BaseException] = [error]
    for _ in range(_MAX_CAUSE_CHAIN_DEPTH):
        if not frontier:
            break
        current = frontier.pop()
        if id(current) in seen:
            continue
        seen.add(id(current))
        if isinstance(current, ssl.SSLError):
            return True
        if current.__cause__ is not None:
            frontier.append(current.__cause__)
        for argument in current.args:
            if isinstance(argument, BaseException):
                frontier.append(argument)
    return False


def build_ssl_context(config: ClientConfig) -> ssl.SSLContext:
    """CFG-040/041/042/044: one SSL context, built once per `Client`."""

    context = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    context.check_hostname = not config.tls_skip_verify
    context.verify_mode = ssl.CERT_NONE if config.tls_skip_verify else ssl.CERT_REQUIRED
    if config.ca_cert_replaces_system_roots and (config.ca_cert_pem or config.ca_cert_path):
        context.load_verify_locations(
            cadata=config.ca_cert_pem, cafile=config.ca_cert_path if not config.ca_cert_pem else None
        )
    else:
        context.load_default_certs()
        if config.ca_cert_pem:
            context.load_verify_locations(cadata=config.ca_cert_pem)
        elif config.ca_cert_path:
            context.load_verify_locations(cafile=config.ca_cert_path)
    if config.client_cert_path and config.client_key_path:
        context.load_cert_chain(config.client_cert_path, config.client_key_path)
    return context


class HttpxTransport:
    """The default `Transport`, backed by one pooled `httpx.AsyncClient` (TRN-090)."""

    supports_custom_verbs: bool = True

    def __init__(self, config: ClientConfig) -> None:
        self._server_name = config.tls_server_name
        self._client = httpx.AsyncClient(
            verify=build_ssl_context(config),
            trust_env=config.use_system_proxy,
            follow_redirects=False,
        )

    async def aclose(self) -> None:
        await self._client.aclose()

    async def send(self, request: TransportRequest) -> TransportResponse:
        timeout = httpx.Timeout(
            timeout=request.timeout.total_seconds(), connect=request.connect_timeout.total_seconds()
        )
        try:
            async with self._client.stream(
                request.method,
                request.url,
                headers=dict(request.headers),
                content=request.body,
                timeout=timeout,
                extensions={"sni_hostname": self._server_name} if self._server_name else {},
            ) as response:
                body = await _read_bounded(response, request.max_response_bytes)
                return TransportResponse(
                    status_code=response.status_code, headers=dict(response.headers), body=body
                )
        except httpx.TimeoutException as error:
            raise make_error(ErrorCodes.TRANSPORT_TIMEOUT, cause=error) from error
        except (httpx.ConnectError, ssl.SSLError) as error:
            code = (
                ErrorCodes.TRANSPORT_TLS_ERROR
                if _contains_ssl_error(error)
                else ErrorCodes.TRANSPORT_CONNECTION_FAILED
            )
            raise make_error(code, cause=error) from error
        except httpx.HTTPError as error:
            raise make_error(ErrorCodes.TRANSPORT_CONNECTION_FAILED, cause=error) from error


async def _read_bounded(response: httpx.Response, max_response_bytes: int | None) -> bytes:
    """D-M1b-20: reject an over-bound `Content-Length` unread; abort mid-stream otherwise."""

    if max_response_bytes is not None:
        content_length = response.headers.get("content-length")
        if content_length is not None:
            try:
                declared = int(content_length)
            except ValueError:
                declared = None
            if declared is not None and declared > max_response_bytes:
                raise make_error(ErrorCodes.TRANSPORT_RESPONSE_TOO_LARGE, status_code=response.status_code)
    chunks: list[bytes] = []
    total = 0
    async for chunk in response.aiter_bytes():
        total += len(chunk)
        if max_response_bytes is not None and total > max_response_bytes:
            raise make_error(ErrorCodes.TRANSPORT_RESPONSE_TOO_LARGE, status_code=response.status_code)
        chunks.append(chunk)
    return b"".join(chunks)


__all__ = ["HttpxTransport"]
