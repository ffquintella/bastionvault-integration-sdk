"""The logical layer (section 03): `Read`/`Write`/`Delete`/`List`/`Raw`, one retry loop
(D-M1b-24), and the D-M1b-4 status->code mapping applied at exactly one call site.

Nothing here reopens a DR-0004 decision: URL/header construction is TRN-002/003/010-023,
envelope parsing is TRN-040-043, status handling is TRN-050-054/060, retry is
CFG-050-055/RES-001-004, the rate-gate pause is D-M1b-16/22.
"""

from __future__ import annotations

import json
from collections.abc import Mapping
from dataclasses import dataclass
from datetime import timedelta
from typing import TYPE_CHECKING, Any, cast
from urllib.parse import quote

from .errors import (
    BastionVaultError,
    ErrorCodes,
    make_config_error,
    make_error,
    map_status_to_code,
    parse_error_body,
)
from .secrets import SecretString
from .transport import RequestOptions

if TYPE_CHECKING:
    from .client import Client

_MAX_REQUEST_BODY_BYTES = 32 * 1024 * 1024  # TRN-032
_RESERVED_HEADERS_CASEFOLD = frozenset(
    {
        "x-bastionvault-token",
        "x-vault-token",
        "authorization",
        "cookie",
        "x-bastionvault-namespace",
        "content-length",
        "host",
    }
)
# D-M1b-7: hard exclusions. Never retried even if a caller puts them in `RetryOn`
# (CFG-052/053 are prohibitions on the SDK, not preferences).
_NEVER_RETRY = frozenset({ErrorCodes.SERVER_SEALED, ErrorCodes.RATE_LIMITED_BY_DOS_GUARD})
_SNIPPET_LENGTH = 256


@dataclass(frozen=True)
class AuthInfo:
    """The five fields the server ever emits on `auth` (section 03)."""

    client_token: SecretString
    policies: tuple[str, ...]
    metadata: Mapping[str, str]
    lease_duration: timedelta
    renewable: bool


@dataclass(frozen=True)
class Response:
    """The canonical logical-operation result (TRN-040-043)."""

    data: Mapping[str, Any] | None
    auth: AuthInfo | None
    lease_id: str | None
    renewable: bool | None
    lease_duration: timedelta | None
    warnings: tuple[str, ...]
    status_code: int
    headers: Mapping[str, str]
    raw: Any


@dataclass(frozen=True)
class RawResponse:
    """`Logical.Raw`'s unparsed result (D-M1b-12)."""

    status_code: int
    headers: Mapping[str, str]
    body: bytes


def _encode_segment(segment: str) -> str:
    return quote(segment, safe="")


def _split_path_and_query(raw_path: str) -> tuple[str, str]:
    path_part, _, query_part = raw_path.partition("?")
    return path_part, query_part


def _encode_path(path_part: str) -> str:
    return "/".join(_encode_segment(segment) for segment in path_part.split("/"))


def _encode_query(query_part: str) -> str:
    if not query_part:
        return ""
    encoded_pairs = []
    for pair in query_part.split("&"):
        if "=" in pair:
            key, _, value = pair.partition("=")
            encoded_pairs.append(f"{quote(key, safe='/')}={quote(value, safe='/')}")
        else:
            encoded_pairs.append(quote(pair, safe="/"))
    return "&".join(encoded_pairs)


def _build_url(*, address: str, prefix: str | None, path: str, absolute: bool) -> str:
    base = address.rstrip("/")
    if absolute:
        stripped = path[1:] if path.startswith("/") else path
        path_part, query_part = _split_path_and_query(stripped)
        url = f"{base}/{_encode_path(path_part)}"
    else:
        stripped = path[1:] if path.startswith("/") else path
        path_part, query_part = _split_path_and_query(stripped)
        url = f"{base}/{prefix or 'v1'}/{_encode_path(path_part)}"
    query_encoded = _encode_query(query_part)
    return f"{url}?{query_encoded}" if query_encoded else url


def _is_login_path(path: str) -> bool:
    """TRN-015: `auth/*/login` or `auth/*/login/{user}`."""
    stripped = path.split("?", 1)[0].rstrip("/")
    segments = [segment for segment in stripped.split("/") if segment]
    if not segments:
        return False
    if segments[-1] == "login":
        return True
    return len(segments) >= 2 and segments[-2] == "login"


def _header(headers: Mapping[str, str], name: str) -> str | None:
    target = name.casefold()
    for key, value in headers.items():
        if key.casefold() == target:
            return value
    return None


def _build_headers(
    *,
    client_headers: Mapping[str, str],
    option_headers: Mapping[str, str],
    has_body: bool,
    user_agent: str,
    path: str,
    token: SecretString | None,
    explicit_token: bool,
    namespace: str,
) -> dict[str, str]:
    headers: dict[str, str] = {"Accept": "application/json", "User-Agent": user_agent}
    if has_body:
        headers["Content-Type"] = "application/json"
    for name, value in client_headers.items():
        headers[name] = value
    for name, value in option_headers.items():
        if name.casefold() in _RESERVED_HEADERS_CASEFOLD:
            raise make_config_error(ErrorCodes.CONFIG_RESERVED_HEADER, details={"header": name})
        headers[name] = value
    if token is not None and (explicit_token or not _is_login_path(path)):
        headers["X-BastionVault-Token"] = token.reveal()
    if namespace:
        headers["X-BastionVault-Namespace"] = namespace.rstrip("/")
    return headers


def _encode_body(body: Any) -> bytes | None:
    if body is None:
        return None
    encoded = json.dumps(body, separators=(",", ":")).encode("utf-8")
    if len(encoded) > _MAX_REQUEST_BODY_BYTES:
        raise make_error(ErrorCodes.INPUT_BODY_TOO_LARGE, attempts=0)
    return encoded


def _display_path(namespace: str, path: str) -> str:
    return f"[ns={namespace}] {path}" if namespace else path


def _details_for(code: str, *, path: str, snippet: str | None) -> dict[str, Any]:
    if code == ErrorCodes.PROTOCOL_UNEXPECTED_RESPONSE:
        return {"snippet": snippet or ""}
    if code in (ErrorCodes.NOTFOUND_PATH_NOT_FOUND, ErrorCodes.AUTHZ_PERMISSION_DENIED):
        return {"path": path}
    return {}


def _sanitized_snippet(body_text: str) -> str:
    snippet = body_text[:_SNIPPET_LENGTH]

    def _safe(character: str) -> str:
        return character if character.isprintable() or character in "\n\t" else " "

    return "".join(_safe(character) for character in snippet)


def _parse_retry_after(value: str | None) -> float | None:
    if value is None:
        return None
    try:
        return float(int(value.strip()))
    except ValueError:
        return None  # HTTP-date form: MAY be parsed; not attempted at M1b.


def _build_response(parsed: Any, *, status_code: int, headers: Mapping[str, str]) -> Response:
    if isinstance(parsed, Mapping):
        auth_obj = parsed.get("auth")
        has_auth_token = isinstance(auth_obj, Mapping) and "client_token" in auth_obj
        if "data" in parsed or has_auth_token:
            lease_id_raw = parsed.get("lease_id")
            lease_id = lease_id_raw if lease_id_raw else None  # TRN-041
            lease_duration_raw = parsed.get("lease_duration")
            lease_duration = (
                timedelta(seconds=lease_duration_raw)
                if isinstance(lease_duration_raw, (int, float))
                else None
            )
            auth: AuthInfo | None = None
            if isinstance(auth_obj, Mapping) and "client_token" in auth_obj:
                auth_lease_raw = auth_obj.get("lease_duration", 0)
                auth = AuthInfo(
                    client_token=SecretString(str(auth_obj.get("client_token", ""))),
                    policies=tuple(auth_obj.get("policies") or ()),
                    metadata=dict(auth_obj.get("metadata") or {}),
                    lease_duration=timedelta(
                        seconds=auth_lease_raw if isinstance(auth_lease_raw, (int, float)) else 0
                    ),
                    renewable=bool(auth_obj.get("renewable", False)),
                )
            return Response(
                data=parsed.get("data"),
                auth=auth,
                lease_id=lease_id,
                renewable=parsed.get("renewable"),
                lease_duration=lease_duration,
                warnings=tuple(parsed.get("warnings") or ()),  # ERR-050
                status_code=status_code,
                headers=headers,
                raw=parsed,
            )
    return Response(
        data=parsed,
        auth=None,
        lease_id=None,
        renewable=None,
        lease_duration=None,
        warnings=(),
        status_code=status_code,
        headers=headers,
        raw=parsed,
    )


@dataclass
class _Outcome:
    result: Any = None
    error: BastionVaultError | None = None


def _interpret_error_status(
    *,
    status_code: int,
    body_text: str,
    headers: Mapping[str, str],
    method: str,
    display_path: str,
) -> BastionVaultError:
    """Shared error mapping for both the envelope path and `Logical.Raw` (D-M1b-12)."""

    content_type = _header(headers, "content-type")
    is_json = content_type is None or "json" in content_type.casefold()
    stripped = body_text.strip()
    server_message: str | None
    server_errors: tuple[str, ...] = ()
    if not stripped:
        server_message = f"HTTP {status_code} (no body)"
    elif not is_json:
        return make_error(
            ErrorCodes.PROTOCOL_UNEXPECTED_RESPONSE,
            status_code=status_code,
            method=method,
            path=display_path,
            details=_details_for(
                ErrorCodes.PROTOCOL_UNEXPECTED_RESPONSE,
                path=display_path,
                snippet=_sanitized_snippet(stripped),
            ),
        )
    else:
        try:
            json.loads(stripped)
        except json.JSONDecodeError:
            return make_error(
                ErrorCodes.PROTOCOL_UNEXPECTED_RESPONSE,
                status_code=status_code,
                method=method,
                path=display_path,
                details=_details_for(
                    ErrorCodes.PROTOCOL_UNEXPECTED_RESPONSE,
                    path=display_path,
                    snippet=_sanitized_snippet(stripped),
                ),
            )
        parsed_body = parse_error_body(stripped)
        server_message = parsed_body.server_message
        server_errors = parsed_body.server_errors

    retry_after_seconds = _parse_retry_after(_header(headers, "retry-after"))  # TRN-051
    code = map_status_to_code(
        status_code, server_message=server_message, retry_after_present=retry_after_seconds is not None
    )
    return make_error(
        code,
        status_code=status_code,
        server_message=server_message,
        server_errors=server_errors,
        retry_after=timedelta(seconds=retry_after_seconds) if retry_after_seconds is not None else None,
        method=method,
        path=display_path,
        details=_details_for(code, path=display_path, snippet=None),
    )


def _interpret_envelope(
    *,
    status_code: int,
    body: bytes,
    headers: Mapping[str, str],
    method: str,
    display_path: str,
    read_like: bool,
) -> _Outcome:
    if status_code == 204:
        return _Outcome(result=None)
    if status_code == 304:
        return _Outcome(
            result=Response(
                data=None,
                auth=None,
                lease_id=None,
                renewable=None,
                lease_duration=None,
                warnings=(),
                status_code=304,
                headers=headers,
                raw=None,
            )
        )
    if 300 <= status_code < 400:
        return _Outcome(
            error=make_error(
                ErrorCodes.PROTOCOL_UNEXPECTED_REDIRECT,
                status_code=status_code,
                method=method,
                path=display_path,
            )
        )
    body_text = body.decode("utf-8", errors="replace")
    stripped = body_text.strip()
    if status_code == 404 and read_like and not stripped:
        return _Outcome(result=None)  # D-M1b-11
    if status_code < 300:
        if not stripped:
            return _Outcome(result=None)  # TRN-050: 200 with empty/whitespace body
        content_type = _header(headers, "content-type")
        is_json = content_type is None or "json" in content_type.casefold()
        if not is_json:
            return _Outcome(
                error=make_error(
                    ErrorCodes.PROTOCOL_UNEXPECTED_RESPONSE,
                    status_code=status_code,
                    method=method,
                    path=display_path,
                    details=_details_for(
                        ErrorCodes.PROTOCOL_UNEXPECTED_RESPONSE,
                        path=display_path,
                        snippet=_sanitized_snippet(stripped),
                    ),
                )
            )
        try:
            parsed = json.loads(stripped)
        except json.JSONDecodeError:
            return _Outcome(
                error=make_error(
                    ErrorCodes.PROTOCOL_UNEXPECTED_RESPONSE,
                    status_code=status_code,
                    method=method,
                    path=display_path,
                    details=_details_for(
                        ErrorCodes.PROTOCOL_UNEXPECTED_RESPONSE,
                        path=display_path,
                        snippet=_sanitized_snippet(stripped),
                    ),
                )
            )
        return _Outcome(result=_build_response(parsed, status_code=status_code, headers=headers))
    return _Outcome(
        error=_interpret_error_status(
            status_code=status_code,
            body_text=body_text,
            headers=headers,
            method=method,
            display_path=display_path,
        )
    )


def _interpret_raw(
    *, status_code: int, body: bytes, headers: Mapping[str, str], method: str, display_path: str
) -> _Outcome:
    if 300 <= status_code < 400 and status_code != 304:
        return _Outcome(
            error=make_error(
                ErrorCodes.PROTOCOL_UNEXPECTED_REDIRECT,
                status_code=status_code,
                method=method,
                path=display_path,
            )
        )
    if status_code < 400:
        return _Outcome(result=RawResponse(status_code=status_code, headers=headers, body=body))
    body_text = body.decode("utf-8", errors="replace")
    return _Outcome(
        error=_interpret_error_status(
            status_code=status_code,
            body_text=body_text,
            headers=headers,
            method=method,
            display_path=display_path,
        )
    )


class Logical:
    """`Logical.Read`/`Write`/`Delete`/`List`/`Raw` (TRN-001), bound to one `Client`."""

    def __init__(self, client: "Client") -> None:
        self._client = client

    async def read(self, path: str, options: RequestOptions | None = None) -> Response | None:
        result = await self._client._execute(
            method="GET", path=path, body=None, options=options, default_idempotent=True, read_like=True
        )
        return cast("Response | None", result)

    async def write(
        self, path: str, body: Mapping[str, Any] | None = None, options: RequestOptions | None = None
    ) -> Response | None:
        result = await self._client._execute(
            method="POST", path=path, body=body, options=options, default_idempotent=False, read_like=False
        )
        return cast("Response | None", result)

    async def delete(
        self, path: str, body: Mapping[str, Any] | None = None, options: RequestOptions | None = None
    ) -> Response | None:
        result = await self._client._execute(
            method="DELETE", path=path, body=body, options=options, default_idempotent=False, read_like=False
        )
        return cast("Response | None", result)

    async def list(self, path: str, options: RequestOptions | None = None) -> Response | None:
        result = await self._client._execute(
            method="LIST", path=path, body=None, options=options, default_idempotent=True, read_like=True
        )
        return cast("Response | None", result)

    async def raw(
        self,
        method: str,
        absolute_path: str,
        body: Mapping[str, Any] | None = None,
        options: RequestOptions | None = None,
    ) -> RawResponse:
        idempotent_methods = {"GET", "HEAD", "OPTIONS", "LIST"}
        result = await self._client._execute(
            method=method,
            path=absolute_path,
            body=body,
            options=options,
            default_idempotent=method.upper() in idempotent_methods,
            read_like=False,
            absolute=True,
        )
        assert isinstance(result, RawResponse)  # noqa: S101 - raw always returns RawResponse
        return result


__all__ = ["AuthInfo", "Logical", "RawResponse", "Response"]
