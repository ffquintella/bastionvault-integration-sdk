"""The BastionVault error type (D-M1a-1).

M1a ships the whole `ERR-001` shape and the full `ErrorCategory` enum, but only the
`BV-CONFIG-001..008` codes are populated with real messages and hints (transcribed
verbatim from ``specifications/appendix-b-error-catalogue.md`` rows 14-21). The
remaining categories are wired for M1c.
"""

from __future__ import annotations

import json
from collections.abc import Mapping
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from enum import Enum
from types import MappingProxyType
from typing import Any, Final


class ErrorCategory(Enum):
    """One of the categories from specifications/04-error-model.md."""

    CONFIGURATION = "configuration"
    INPUT = "input"
    TRANSPORT = "transport"
    PROTOCOL = "protocol"
    AUTHENTICATION = "authentication"
    AUTHORIZATION = "authorization"
    NOT_FOUND = "not_found"
    CONFLICT = "conflict"
    RATE_LIMIT = "rate_limit"
    QUOTA = "quota"
    SERVER_STATE = "server_state"
    DISCOVERY = "discovery"
    ENGINE = "engine"


class ErrorCodes:
    """Stable error code constants (ERR-005).

    M1a populated only `BV-CONFIG-*`. M1b (D-M1b-4) adds the status-derived set: every
    transport/protocol code, plus the rows the status table maps by status alone or by
    a discriminator section 03 itself names.
    """

    CONFIG_INVALID_ADDRESS: Final[str] = "BV-CONFIG-001"
    CONFIG_INSECURE_HTTP_NOT_ALLOWED: Final[str] = "BV-CONFIG-002"
    CONFIG_INVALID_SETTING_VALUE: Final[str] = "BV-CONFIG-003"
    CONFIG_CLIENT_CERT_INCOMPLETE: Final[str] = "BV-CONFIG-004"
    CONFIG_FILE_NOT_READABLE: Final[str] = "BV-CONFIG-005"
    CONFIG_INVALID_PEM: Final[str] = "BV-CONFIG-006"
    CONFIG_INVALID_NAMESPACE: Final[str] = "BV-CONFIG-007"
    CONFIG_RESERVED_HEADER: Final[str] = "BV-CONFIG-008"
    CONFIG_LIST_VERB_UNSUPPORTED: Final[str] = "BV-CONFIG-009"

    INPUT_INVALID_ARGUMENT: Final[str] = "BV-INPUT-001"
    INPUT_UNSUPPORTED_OPTION: Final[str] = "BV-INPUT-006"
    INPUT_BODY_TOO_LARGE: Final[str] = "BV-INPUT-007"
    INPUT_CHUNK_INDEX_OUT_OF_RANGE: Final[str] = "BV-INPUT-008"

    TRANSPORT_CONNECTION_FAILED: Final[str] = "BV-TRANSPORT-001"
    TRANSPORT_TIMEOUT: Final[str] = "BV-TRANSPORT-002"
    TRANSPORT_TLS_ERROR: Final[str] = "BV-TRANSPORT-003"
    TRANSPORT_RESPONSE_TOO_LARGE: Final[str] = "BV-TRANSPORT-004"
    TRANSPORT_CANCELLED: Final[str] = "BV-TRANSPORT-005"

    PROTOCOL_METHOD_NOT_ALLOWED: Final[str] = "BV-PROTOCOL-001"
    PROTOCOL_UNEXPECTED_RESPONSE: Final[str] = "BV-PROTOCOL-002"
    PROTOCOL_UNEXPECTED_REDIRECT: Final[str] = "BV-PROTOCOL-003"

    AUTH_UNAUTHENTICATED: Final[str] = "BV-AUTH-002"
    AUTHZ_PERMISSION_DENIED: Final[str] = "BV-AUTHZ-001"

    NOTFOUND_PATH_NOT_FOUND: Final[str] = "BV-NOTFOUND-001"

    CONFLICT_RECORDING_DIGEST_MISMATCH: Final[str] = "BV-CONFLICT-002"
    CONFLICT_BROKERED_RESOURCE_STATIC_CREDENTIAL: Final[str] = "BV-CONFLICT-003"

    RATE_LIMITED_BY_DOS_GUARD: Final[str] = "BV-RATE-001"
    NAMESPACE_RATE_QUOTA_EXCEEDED: Final[str] = "BV-RATE-002"

    QUOTA_NAMESPACE_QUOTA_EXCEEDED: Final[str] = "BV-QUOTA-001"

    SERVER_SEALED: Final[str] = "BV-SERVER-001"
    SERVER_UNAVAILABLE: Final[str] = "BV-SERVER-002"
    SERVER_INTERNAL_ERROR: Final[str] = "BV-SERVER-005"


# Transcribed verbatim from specifications/appendix-b-error-catalogue.md (rows 14-21).
# code -> (message, hint, retryable)
_CONFIG_CATALOG: Final[dict[str, tuple[str, str, bool]]] = {
    ErrorCodes.CONFIG_INVALID_ADDRESS: (
        "The server address is missing or not a valid URL or cluster name.",
        "Set `Address` (or `BASTIONVAULT_ADDR`) to `https://host:8200`, or to a bare "
        "DNS name for cluster discovery. IPv6 literals must be bracketed.",
        False,
    ),
    ErrorCodes.CONFIG_INSECURE_HTTP_NOT_ALLOWED: (
        "Plain `http://` to a non-loopback host is not allowed.",
        "Use `https://`, or set `AllowInsecureHttp = true` only for isolated test networks.",
        False,
    ),
    ErrorCodes.CONFIG_INVALID_SETTING_VALUE: (
        "A configuration value has the wrong type or range.",
        "Check `Details.setting`; booleans accept 1/0/true/false/yes/no/on/off, "
        "durations accept `30s`, `1m30s` or integer seconds; timeouts must be > 0.",
        False,
    ),
    ErrorCodes.CONFIG_CLIENT_CERT_INCOMPLETE: (
        "Only one of `ClientCertPath` / `ClientKeyPath` is set.",
        "Provide both the client certificate and its private key (PEM), or neither.",
        False,
    ),
    ErrorCodes.CONFIG_FILE_NOT_READABLE: (
        "A configured file cannot be read.",
        "Check `Details.path` exists and the process user can read it.",
        False,
    ),
    ErrorCodes.CONFIG_INVALID_PEM: (
        "A certificate or key is not valid PEM.",
        "Ensure the file contains `-----BEGIN CERTIFICATE-----`/`PRIVATE KEY` blocks "
        "and is not DER or PKCS#12.",
        False,
    ),
    ErrorCodes.CONFIG_INVALID_NAMESPACE: (
        "The namespace path is malformed.",
        "Use `parent/child` without a leading slash, whitespace or control characters.",
        False,
    ),
    ErrorCodes.CONFIG_RESERVED_HEADER: (
        "A custom header would override a header the SDK manages.",
        "Remove `X-BastionVault-Token`, `X-Vault-Token`, `Authorization`, `Cookie`, "
        "`X-BastionVault-Namespace`, `Host`, `Content-Length` from `Headers`; use "
        "`Token`/`Namespace` instead.",
        False,
    ),
}

# ERR-006: Retryable is true only for these codes; everything else (including every
# BV-CONFIG-* code) is false. Recorded here so the invariant is checkable in one place.
RETRYABLE_CODES: Final[frozenset[str]] = frozenset(
    {
        "BV-TRANSPORT-001",
        "BV-TRANSPORT-002",
        "BV-TRANSPORT-003",
        "BV-SERVER-002",
        "BV-SERVER-003",
        "BV-RATE-002",
        "BV-DISCOVERY-003",
    }
)

# D-M1b-4: the status-derived set, transcribed verbatim from Appendix B. Retryable
# follows ERR-006 (`RETRYABLE_CODES`), never the mapper's own opinion (D-M1b-4b).
# code -> (category, message, hint)
_STATUS_CATALOG: Final[dict[str, tuple[ErrorCategory, str, str]]] = {
    ErrorCodes.CONFIG_LIST_VERB_UNSUPPORTED: (
        ErrorCategory.CONFIGURATION,
        "The HTTP stack cannot send the custom `LIST` method.",
        "Use the SDK's default transport or an HTTP client that allows non-standard "
        "methods; the server does not support `?list=true`.",
    ),
    ErrorCodes.INPUT_INVALID_ARGUMENT: (
        ErrorCategory.INPUT,
        "An argument is missing or invalid.",
        "See `Details.argument` and `Details.reason`; required strings must be "
        "non-empty, `env` cannot contain `/`, `env` and `envs` are mutually exclusive.",
    ),
    ErrorCodes.INPUT_UNSUPPORTED_OPTION: (
        ErrorCategory.INPUT,
        "The option is not supported by BastionVault.",
        "Response wrapping (`WrapTtl`) is not implemented by the server; remove the option.",
    ),
    ErrorCodes.INPUT_BODY_TOO_LARGE: (
        ErrorCategory.INPUT,
        "The request body exceeds the server limit.",
        "Keep bodies under 32 MiB; for files, upload smaller versions or use sync targets.",
    ),
    ErrorCodes.INPUT_CHUNK_INDEX_OUT_OF_RANGE: (
        ErrorCategory.INPUT,
        "The recording chunk index is past the end.",
        "Read chunk 0 first and stop at `eof`; `Details.chunk_count` is the real count.",
    ),
    ErrorCodes.TRANSPORT_CONNECTION_FAILED: (
        ErrorCategory.TRANSPORT,
        "Could not connect to the server.",
        "Check `Address`, DNS, firewall and that the server is listening "
        "(default `https://127.0.0.1:8200`).",
    ),
    ErrorCodes.TRANSPORT_TIMEOUT: (
        ErrorCategory.TRANSPORT,
        "The request timed out.",
        "Increase `Timeout`/`ConnectTimeout`, check server load; long-poll calls need >= 40 s.",
    ),
    ErrorCodes.TRANSPORT_TLS_ERROR: (
        ErrorCategory.TRANSPORT,
        "TLS handshake or certificate verification failed.",
        "Provide the server CA via `CaCertPath`; check `TlsServerName` matches a SAN; "
        "verify the clock. Only as a diagnostic step, and never in production, "
        "`TlsSkipVerify` confirms whether trust is the cause.",
    ),
    ErrorCodes.TRANSPORT_RESPONSE_TOO_LARGE: (
        ErrorCategory.TRANSPORT,
        "The response exceeded `MaxResponseBytes`.",
        "Use the chunked route (`Rustion.Recordings.Download`) or paging (`*-info`), "
        "or raise `MaxResponseBytes`.",
    ),
    ErrorCodes.TRANSPORT_CANCELLED: (
        ErrorCategory.TRANSPORT,
        "The operation was cancelled.",
        "The caller cancelled; no request state is known. Retry is the caller's decision.",
    ),
    ErrorCodes.PROTOCOL_METHOD_NOT_ALLOWED: (
        ErrorCategory.PROTOCOL,
        "The server does not accept this HTTP method on this path.",
        "Only GET, POST/PUT, DELETE and LIST are routed; use the matching logical operation.",
    ),
    ErrorCodes.PROTOCOL_UNEXPECTED_RESPONSE: (
        ErrorCategory.PROTOCOL,
        "The server response could not be interpreted.",
        "The body was not JSON or not a known shape (`Details.snippet`); confirm "
        "`Address` points at a BastionVault API listener, not a proxy or GUI.",
    ),
    ErrorCodes.PROTOCOL_UNEXPECTED_REDIRECT: (
        ErrorCategory.PROTOCOL,
        "The server answered with a redirect.",
        "BastionVault never redirects; a proxy or load balancer in front of it does. "
        "Point `Address` at the vault or fix the proxy.",
    ),
    ErrorCodes.AUTH_UNAUTHENTICATED: (
        ErrorCategory.AUTHENTICATION,
        "The server requires authentication for this call.",
        "Provide a valid token; for connect-MFA calls the caller must be a userpass principal.",
    ),
    ErrorCodes.AUTHZ_PERMISSION_DENIED: (
        ErrorCategory.AUTHORIZATION,
        "The token does not have permission for this path (or the token is invalid, "
        "expired or revoked).",
        "Check the token's policies grant the capability on `Details.path` "
        "(`Sys.CapabilitiesSelf`); verify the token with `Auth.Token.LookupSelf`; if the "
        "credential is namespace-scoped set `Namespace`; a `token_bound_cidrs` or "
        "`bound_source_ips` rule may exclude this client.",
    ),
    ErrorCodes.NOTFOUND_PATH_NOT_FOUND: (
        ErrorCategory.NOT_FOUND,
        "Nothing exists at this path.",
        "Check the mount and the engine's path layout (`Details.path`); KV v2 data "
        "lives under `<mount>/data/<name>`; unregistered `sys/*` routes also answer 404.",
    ),
    ErrorCodes.CONFLICT_RECORDING_DIGEST_MISMATCH: (
        ErrorCategory.CONFLICT,
        "The recording bytes do not match the recorded digest.",
        "Deterministic failure: do not retry; inspect the bastion and the sidecar digest.",
    ),
    ErrorCodes.CONFLICT_BROKERED_RESOURCE_STATIC_CREDENTIAL: (
        ErrorCategory.CONFLICT,
        "A static SSH credential cannot be attached to a brokered resource.",
        "Remove `private_key`/`password` or change the resource's `login_class`.",
    ),
    ErrorCodes.RATE_LIMITED_BY_DOS_GUARD: (
        ErrorCategory.RATE_LIMIT,
        "The server's abuse guard temporarily blocked this client IP.",
        "The rate gate is paused for `RetryAfter` seconds. Reduce request fan-out: use "
        "`Sys.Batch`, `Kv.ReadMany`, `*-info` pages and a read cache. Do not add retries.",
    ),
    ErrorCodes.NAMESPACE_RATE_QUOTA_EXCEEDED: (
        ErrorCategory.RATE_LIMIT,
        "The namespace request-rate quota was exceeded.",
        "Slow down or ask an admin to raise `request_rate` on the namespace; back off "
        "before retrying.",
    ),
    ErrorCodes.QUOTA_NAMESPACE_QUOTA_EXCEEDED: (
        ErrorCategory.QUOTA,
        "A namespace capacity quota was reached.",
        "`ServerMessage` names the quota (mounts, leases, entities, storage); free "
        "capacity or raise the quota via `Sys.UpdateNamespace`.",
    ),
    ErrorCodes.SERVER_SEALED: (
        ErrorCategory.SERVER_STATE,
        "The vault is sealed.",
        "An operator must unseal it (`bvault operator unseal` or HSM auto-unseal); the "
        "SDK does not retry. Use `Sys.Health` to watch for readiness.",
    ),
    ErrorCodes.SERVER_UNAVAILABLE: (
        ErrorCategory.SERVER_STATE,
        "The server is temporarily unavailable.",
        "Cluster has no leader/quorum, node unhealthy, or HSM unreachable; the SDK "
        "retries idempotent calls. Check `Sys.ClusterStatus` and node health.",
    ),
    ErrorCodes.SERVER_INTERNAL_ERROR: (
        ErrorCategory.SERVER_STATE,
        "The server reported an internal error.",
        "Read `ServerMessage`; many engine validation errors are reported as 500 -- "
        "the message names the field or object. Check server logs if it is generic.",
    ),
}


def make_error(
    code: str,
    *,
    attempts: int = 1,
    server_message: str | None = None,
    server_errors: tuple[str, ...] = (),
    status_code: int | None = None,
    retry_after: timedelta | None = None,
    method: str | None = None,
    path: str | None = None,
    address: str | None = None,
    details: Mapping[str, Any] | None = None,
    cause: BaseException | None = None,
    extra_hint: str | None = None,
) -> BastionVaultError:
    """Build a `BastionVaultError` from the D-M1b-4 status-derived catalog.

    `Retryable` always follows `RETRYABLE_CODES` (ERR-006), never the caller's opinion
    (D-M1b-4b).
    """

    category, message, hint = _STATUS_CATALOG[code]
    if extra_hint:
        hint = f"{hint} {extra_hint}"
    return BastionVaultError(
        code=code,
        category=category,
        message=message,
        hint=hint,
        retryable=code in RETRYABLE_CODES,
        attempts=attempts,
        server_message=server_message,
        server_errors=server_errors,
        status_code=status_code,
        retry_after=retry_after,
        method=method,
        path=path,
        address=address,
        details=details,
        cause=cause,
    )


@dataclass(frozen=True)
class ParsedErrorBody:
    """The result of TRN-052's total error-body parsing."""

    server_message: str | None
    server_errors: tuple[str, ...]


def parse_error_body(body_text: str) -> ParsedErrorBody:
    """TRN-052: parse the three server error-body shapes. Never raises.

    1. ``{"error": "<string>"}`` -- the normal shape.
    2. ``{"errors": ["<string>", ...]}`` -- DoS guard 429 and HashiCorp compatibility;
       joined with ``"; "`` for the message, the array kept in `ServerErrors`.
    3. Empty (or unparsable) body -- no message; the caller synthesizes
       ``HTTP <status> (no body)``.
    """

    stripped = body_text.strip()
    if not stripped:
        return ParsedErrorBody(server_message=None, server_errors=())
    try:
        parsed = json.loads(stripped)
    except json.JSONDecodeError:
        return ParsedErrorBody(server_message=None, server_errors=())
    if isinstance(parsed, Mapping):
        errors = parsed.get("errors")
        if isinstance(errors, list) and all(isinstance(item, str) for item in errors):
            server_errors = tuple(errors)
            return ParsedErrorBody(server_message="; ".join(server_errors), server_errors=server_errors)
        error = parsed.get("error")
        if isinstance(error, str):
            return ParsedErrorBody(server_message=error, server_errors=())
    return ParsedErrorBody(server_message=None, server_errors=())


def map_status_to_code(
    status_code: int,
    *,
    server_message: str | None,
    retry_after_present: bool,
) -> str:
    """The D-M1b-4/4a/21/23 status -> code mapping. One function, no throwing default arm.

    Only status-derived branches are populated at M1b; message recognition (Appendix B
    section 2) is M1c and does not change this function's signature (D-M1b-4).
    """

    if status_code == 401:
        return ErrorCodes.AUTH_UNAUTHENTICATED
    if status_code == 403:
        return ErrorCodes.AUTHZ_PERMISSION_DENIED
    if status_code == 404:
        return ErrorCodes.NOTFOUND_PATH_NOT_FOUND
    if status_code == 405:
        return ErrorCodes.PROTOCOL_METHOD_NOT_ALLOWED
    if status_code == 416:
        return ErrorCodes.INPUT_CHUNK_INDEX_OUT_OF_RANGE
    if status_code == 429:
        if retry_after_present:
            return ErrorCodes.RATE_LIMITED_BY_DOS_GUARD
        return ErrorCodes.NAMESPACE_RATE_QUOTA_EXCEEDED
    if status_code == 503:
        sealed = server_message is not None and "sealed" in server_message.casefold()
        return ErrorCodes.SERVER_SEALED if sealed else ErrorCodes.SERVER_UNAVAILABLE
    if status_code in (502, 504):
        return ErrorCodes.SERVER_UNAVAILABLE
    if status_code == 507:
        return ErrorCodes.QUOTA_NAMESPACE_QUOTA_EXCEEDED
    if 300 <= status_code < 400:
        return ErrorCodes.PROTOCOL_UNEXPECTED_REDIRECT
    if 400 <= status_code < 500:
        # D-M1b-21: 400 (and every other unmapped 4xx, e.g. 409 best-effort per
        # D-M1b-23) falls back here; message recognition is M1c.
        return ErrorCodes.INPUT_INVALID_ARGUMENT
    if 500 <= status_code < 600:
        return ErrorCodes.SERVER_INTERNAL_ERROR
    return ErrorCodes.PROTOCOL_UNEXPECTED_RESPONSE


class BastionVaultError(Exception):
    """The one error type every SDK failure is an instance of (ERR-001)."""

    def __init__(
        self,
        *,
        code: str,
        category: ErrorCategory,
        message: str,
        hint: str,
        retryable: bool,
        attempts: int = 0,
        server_message: str | None = None,
        server_errors: tuple[str, ...] = (),
        status_code: int | None = None,
        retry_after: timedelta | None = None,
        method: str | None = None,
        path: str | None = None,
        address: str | None = None,
        details: Mapping[str, Any] | None = None,
        cause: BaseException | None = None,
        timestamp: datetime | None = None,
    ) -> None:
        super().__init__(code)
        self.code = code
        self.category = category
        self.message = message
        self.hint = hint
        self.retryable = retryable
        self.attempts = attempts
        self.server_message = server_message
        self.server_errors = server_errors
        self.status_code = status_code
        self.retry_after = retry_after
        self.method = method
        self.path = path
        self.address = address
        self.details: Mapping[str, Any] = MappingProxyType(dict(details or {}))
        self.cause = cause
        self.timestamp = timestamp or datetime.now(timezone.utc)

    def __str__(self) -> str:
        # ERR-002: "<Code>: <Message> — <Hint>" plus optional HTTP/server suffixes.
        text = f"{self.code}: {self.message} — {self.hint}"
        if self.status_code is not None:
            method = self.method or ""
            path = self.path or ""
            text += f" [HTTP {self.status_code} {method} {path}]".rstrip()
        if self.server_message:
            text += f' (server: "{self.server_message}")'
        return text

    def __repr__(self) -> str:
        return f"BastionVaultError(code={self.code!r})"


def make_config_error(
    code: str,
    *,
    details: Mapping[str, Any] | None = None,
    cause: BaseException | None = None,
) -> BastionVaultError:
    """Build a `BV-CONFIG-*` error from the catalog (D-M1a-1: attempts=0, retryable=False)."""

    message, hint, retryable = _CONFIG_CATALOG[code]
    return BastionVaultError(
        code=code,
        category=ErrorCategory.CONFIGURATION,
        message=message,
        hint=hint,
        retryable=retryable,
        attempts=0,
        details=details,
        cause=cause,
    )
