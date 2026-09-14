"""ERR-034's path interpolation and ERR-040's context-aware hint notes.

One deterministic function applied once, where the error leaves the request loop
(`Client._execute_uncancelled` -> `logical._present`), not in the mapper: two of the
seven rows fire on transport failures, which never reach the mapper (D-M1c-14 item 7).

Only the seven rows of `specifications/04-error-model.md#hint-enrichment-from-context`
that are decidable from client-side state alone are here. The two rows that need
`Sys.CapabilitiesSelf` (M3) and the `Sys.ListMounts` cache (M4) are **absent, not
stubbed**: a stub would be a branch no test can reach and the CNF-010 coverage floor
allows no exclusion pragma to excuse it. The owning milestone adds the branch and its
fixture (D-M1c-5).
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import timedelta
from typing import Final

from .errors import ErrorCodes

#: The address `ClientConfig.resolve` falls back to when none is configured.
DEFAULT_ADDRESS: Final = "https://127.0.0.1:8200"

_NO_NAMESPACE_NOTE: Final = (
    "No namespace is set; if the credential is scoped to a namespace, set `Namespace`."
)
_API_VERSION_NOTE: Final = "Pin this call to `/v2` (RequestOptions.ApiVersion = 2)."
_SEALED_NOTE: Final = (
    "Run `bvault operator unseal` on the node or wait for auto-unseal; the SDK will not retry."
)
_UNSUPPORTED_PATH_NOTE: Final = (
    "This server version does not have this endpoint; check `Client.ServerVersion()` and "
    "use the documented fallback."
)
_TLS_NO_CA_NOTE: Final = "Provide the server's CA bundle via `CaCertPath` or `BASTIONVAULT_CACERT`."
_CONNECTION_REFUSED_NOTE: Final = (
    "No `Address` was configured; the default is `https://127.0.0.1:8200`. Set `Address` "
    "or `BASTIONVAULT_ADDR`."
)
_NAMESPACE_SCOPED_MOUNTS: Final = ("auth/", "secret/")


@dataclass(frozen=True)
class Context:
    """The client-side facts the seven landed rows need; nothing about retry state."""

    status_code: int | None
    retry_after: timedelta | None
    path: str
    active_namespace: str
    has_ca_certificate: bool
    address: str


def interpolate_path(hint: str, redacted_path: str) -> str:
    """ERR-034: a hint that points at `Details.path` names the path the SDK actually sent.

    Runs before the ERR-040 table so the enrichment rows below stay exactly the seven
    D-M1c-5 lists (D-M1c-14 item 5).
    """

    if not redacted_path or "Details.path" not in hint:
        return hint
    return _append(hint, f"The path as sent was `{redacted_path}`.")


def enrich(code: str, hint: str, context: Context) -> str:
    """Append the ERR-040 notes whose condition holds, in `04-error-model.md` order.

    Separated by a single space; never rewrites the catalogue hint (D-M1c-5).
    """

    result = hint

    if (
        context.status_code == 403
        and not context.active_namespace
        and _is_under_namespace_scoped_mount(context.path)
    ):
        result = _append(result, _NO_NAMESPACE_NOTE)

    if context.status_code == 400 and code == ErrorCodes.SERVER_API_VERSION_MISMATCH:
        result = _append(result, _API_VERSION_NOTE)

    if context.status_code == 429 and context.retry_after is not None:
        # `Retry-After` is only ever parsed from its integer-seconds form (TRN-051), so
        # the total is always whole and no rounding mode has to be chosen here.
        seconds = int(context.retry_after.total_seconds())
        result = _append(
            result,
            f"The client rate gate is now paused for `{seconds}`s; reduce request "
            "fan-out (use `Sys.Batch` or `*-info` pages).",
        )

    if context.status_code == 503 and code == ErrorCodes.SERVER_SEALED:
        result = _append(result, _SEALED_NOTE)

    if context.status_code == 500 and code == ErrorCodes.SERVER_UNSUPPORTED_BY_SERVER:
        result = _append(result, _UNSUPPORTED_PATH_NOTE)

    # The SDK maps a handshake failure and a verification failure to the same
    # BV-TRANSPORT-003 (D-M1b-4a), so the trigger is the code plus "no CA configured"
    # rather than a distinction the error does not carry (D-M1c-14 item 8).
    if code == ErrorCodes.TRANSPORT_TLS_ERROR and not context.has_ca_certificate:
        result = _append(result, _TLS_NO_CA_NOTE)

    # "Connection refused to default address": the resolved address is the observable
    # client-side fact; `ClientConfig` does not record whether it was defaulted, and
    # adding a flag to it would be a public API change this row does not justify
    # (D-M1c-14 item 8).
    if code == ErrorCodes.TRANSPORT_CONNECTION_FAILED and context.address == DEFAULT_ADDRESS:
        result = _append(result, _CONNECTION_REFUSED_NOTE)

    return result


def _is_under_namespace_scoped_mount(path: str) -> bool:
    return path.lstrip("/").startswith(_NAMESPACE_SCOPED_MOUNTS)


def _append(hint: str, note: str) -> str:
    """Append one note after a single space.

    No guard against a repeat: each ERR-040 row is tested once per error and the rows are
    disjoint, so a duplicate is unreachable and would only be dead code (D-M1c-5).
    """

    return f"{hint.rstrip()} {note}"


__all__ = ["DEFAULT_ADDRESS", "Context", "enrich", "interpolate_path"]
