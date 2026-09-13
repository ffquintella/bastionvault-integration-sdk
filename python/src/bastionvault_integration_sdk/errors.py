"""The BastionVault error type (D-M1a-1).

M1a ships the whole `ERR-001` shape and the full `ErrorCategory` enum, but only the
`BV-CONFIG-001..008` codes are populated with real messages and hints (transcribed
verbatim from ``specifications/appendix-b-error-catalogue.md`` rows 14-21). The
remaining categories are wired for M1c.
"""

from __future__ import annotations

from collections.abc import Mapping
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
    """Stable error code constants (ERR-005). Only `BV-CONFIG-*` is populated at M1a."""

    CONFIG_INVALID_ADDRESS: Final[str] = "BV-CONFIG-001"
    CONFIG_INSECURE_HTTP_NOT_ALLOWED: Final[str] = "BV-CONFIG-002"
    CONFIG_INVALID_SETTING_VALUE: Final[str] = "BV-CONFIG-003"
    CONFIG_CLIENT_CERT_INCOMPLETE: Final[str] = "BV-CONFIG-004"
    CONFIG_FILE_NOT_READABLE: Final[str] = "BV-CONFIG-005"
    CONFIG_INVALID_PEM: Final[str] = "BV-CONFIG-006"
    CONFIG_INVALID_NAMESPACE: Final[str] = "BV-CONFIG-007"
    CONFIG_RESERVED_HEADER: Final[str] = "BV-CONFIG-008"


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
