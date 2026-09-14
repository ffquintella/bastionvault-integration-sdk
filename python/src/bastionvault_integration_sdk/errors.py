"""The BastionVault error type, the generated catalogue, and the ERR-020 status table.

`ErrorCategory` and the `BastionVaultError` shape are M1a's (D-M1a-1). M1c replaces the
hand-transcribed message/hint tables with the rows `tools/error-catalogue` generates from
`specifications/appendix-b-error-catalogue.md` §1 (DR-0005 D-M1c-1): messages, hints,
categories, retryability and the `ErrorCodes` constants all come from
`_generated/error_catalog_data.py` and are never written by hand again. Add a code by
editing the appendix and regenerating.
"""

from __future__ import annotations

import json
from collections.abc import Mapping
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from types import MappingProxyType
from typing import Any, Final

from ._categories import ErrorCategory
from ._error_paths import one_line, redact
from ._generated.error_catalog_data import ENTRIES as _GENERATED_ENTRIES
from ._generated.error_catalog_data import ErrorCodes

# ERR-006: Retryable is true only for these codes; everything else (including every
# BV-CONFIG-* code) is false. Recorded here so the invariant is checkable in one place;
# the generator asserts the same set over Appendix B's `R` column at generation time
# (D-M1c-8), so this constant is a cross-check, not a second source.
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


@dataclass(frozen=True)
class ErrorCatalogEntry:
    """One row of `specifications/appendix-b-error-catalogue.md` §1 (ERR-036).

    The shape is pinned by `decisions/0005-m1c-error-model.md` D-M1c-7; the rows
    themselves are generated from the appendix (D-M1c-1) and never hand-transcribed.
    """

    code: str
    name: str
    category: ErrorCategory
    message: str
    hint: str
    retryable: bool


class ErrorCatalog:
    """The programmatically inspectable code -> message -> hint table ERR-036 requires.

    Spelled `ErrorCatalog`, not `Catalogue`, because ERR-036 names `ErrorCatalog.Get(code)`
    (D-M1c-7). The message-recognition and hint-enrichment rules that consume this table
    stay private: they implement ERR-020 and are not a supported extension point.
    """

    @staticmethod
    def get(code: str) -> ErrorCatalogEntry | None:
        """The entry for `code`, or `None` when no such code exists. Never raises."""

        return _INDEX.get(code)

    @staticmethod
    def all() -> tuple[ErrorCatalogEntry, ...]:
        """Every catalogue row, in Appendix B order, so generated docs are stable."""

        return _ENTRIES


_ENTRIES: Final[tuple[ErrorCatalogEntry, ...]] = tuple(
    ErrorCatalogEntry(
        code=code, name=name, category=category, message=message, hint=hint, retryable=retryable
    )
    for code, name, category, message, hint, retryable in _GENERATED_ENTRIES
)
_INDEX: Final[Mapping[str, ErrorCatalogEntry]] = MappingProxyType(
    {entry.code: entry for entry in _ENTRIES}
)


def _require(code: str) -> ErrorCatalogEntry:
    """The entry for a code this package raises.

    Unlike `ErrorCatalog.get` this is an internal contract: a missing code is a generator
    or wiring defect, not a caller error.
    """

    return _INDEX[code]


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
    """Build a `BastionVaultError` from the generated catalogue entry for `code`.

    `Retryable` comes from that entry, which the generator emits from Appendix B's `R`
    column and cross-checks against ERR-006's list at generation time (D-M1c-8) --
    independently of any `RetryPolicy.retry_on` configuration (D-M1b-4b).
    """

    entry = _require(code)
    hint = f"{entry.hint} {extra_hint}" if extra_hint else entry.hint
    return BastionVaultError(
        code=code,
        category=entry.category,
        message=entry.message,
        hint=hint,
        retryable=entry.retryable,
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
    """Step 5 of ERR-020: the status fallback, reached only when no §2 rule matched.

    One function, no throwing default arm. Message recognition (step 4) runs ahead of it
    in `logical._interpret_error_status` and does not change this function's signature
    (D-M1b-4, D-M1c-3).

    `server_message` is deliberately unread: D-M1c-19 and D-M1c-23 deleted the `409` and
    `503` message heuristics, which were its only two readers, because step 5 names one
    code per status and Appendix B section 2 answers the message-dependent cases at step
    4. The parameter stays because D-M1b-4 pinned this signature; removing it is a
    separate change, not a side effect of deleting a heuristic.
    """

    if status_code == 401:
        return ErrorCodes.AUTH_UNAUTHENTICATED
    if status_code == 403:
        return ErrorCodes.AUTHZ_PERMISSION_DENIED
    if status_code == 404:
        return ErrorCodes.NOT_FOUND_PATH_NOT_FOUND
    if status_code == 405:
        return ErrorCodes.PROTOCOL_METHOD_NOT_ALLOWED
    if status_code == 409:
        # D-M1c-19: `04-error-model.md` step 5 names BV-CONFLICT-001 for 409. There is no
        # discriminator here: Appendix B section 2 already recognises the digest/sha256
        # rows and `brokered_resource_no_static_credential` at step 4, which is exactly
        # what D-M1b-23's best-effort heuristic was guessing at, so it has no remaining
        # job (CLA-007). This supersedes the heuristic in all three languages.
        return ErrorCodes.CONFLICT
    if status_code == 416:
        return ErrorCodes.INPUT_CHUNK_INDEX_OUT_OF_RANGE
    if status_code == 429:
        if retry_after_present:
            return ErrorCodes.RATE_LIMITED_BY_DOS_GUARD
        return ErrorCodes.RATE_NAMESPACE_RATE_QUOTA_EXCEEDED
    if status_code in (502, 503, 504):
        # D-M1c-23: `04-error-model.md` step 5 says 503 => BV-SERVER-002 flatly, so 503
        # joins 502/504 and D-M1b-23's `sealed` discriminator is deleted. Appendix B
        # section 2 carries `exact bastionvault is sealed` and `contains (5xx) is sealed`
        # -> BV-SERVER-001, both firing at step 4; the heuristic could only still answer a
        # 503 containing `sealed` but not `is sealed`, which no fixture covers and no spec
        # row describes (CLA-007). Supersedes the heuristic in all three languages.
        return ErrorCodes.SERVER_UNAVAILABLE
    if status_code == 507:
        return ErrorCodes.QUOTA_NAMESPACE_QUOTA_EXCEEDED
    if 300 <= status_code < 400:
        return ErrorCodes.PROTOCOL_UNEXPECTED_REDIRECT
    if 400 <= status_code < 500:
        # D-M1c-12 corrects D-M1b-21: `04-error-model.md` step 5 maps `400` **and** every
        # other unmapped 4xx to BV-INPUT-100, which D-M1b-21 could not use because the
        # hand-transcribed catalogue did not carry it. There is deliberately no separate
        # `400` branch -- the specification's `400` row and its "other 4xx" row name the
        # same code. BV-INPUT-001 stays what its message says it is: client-side argument
        # validation, raised before any request.
        return ErrorCodes.INPUT_SERVER_REJECTED_REQUEST
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
        # ERR-003 is applied once, here, so the one-line form, any verbose form and any
        # hint that interpolates the path are redacted by the same rule rather than by
        # three that can drift (D-M1c-14 item 6).
        self.path = redact(path)
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
        # ERR-002: newlines are not permitted in the one-line form. Nothing in the
        # catalogue carries one, but a server message is attacker-influenced input and
        # must not be able to forge a second log line.
        return one_line(text)

    def __repr__(self) -> str:
        return f"BastionVaultError(code={self.code!r})"


def make_config_error(
    code: str,
    *,
    details: Mapping[str, Any] | None = None,
    cause: BaseException | None = None,
) -> BastionVaultError:
    """Build a `BV-CONFIG-*` error from the catalogue (D-M1a-1: attempts=0, retryable=False)."""

    entry = _require(code)
    return BastionVaultError(
        code=code,
        category=entry.category,
        message=entry.message,
        hint=entry.hint,
        retryable=False,
        attempts=0,
        details=details,
        cause=cause,
    )


__all__ = [
    "RETRYABLE_CODES",
    "BastionVaultError",
    "ErrorCatalog",
    "ErrorCatalogEntry",
    "ErrorCategory",
    "ErrorCodes",
    "ParsedErrorBody",
    "make_config_error",
    "make_error",
    "map_status_to_code",
    "parse_error_body",
]
