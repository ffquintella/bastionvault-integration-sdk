"""The `Auth` area (OVR-008): the token source, the current credential, the token store.

This is the project's first sub-API grouping, so it fixes the shape every later engine
grouping copies. `AuthOperations` is a lightweight view over the shared client state,
exactly as `Client.logical` is: constructing one allocates nothing that outlives the call
and reads no state, so a `Client.with_namespace` view's `auth` sees the same token cell as
its parent's (CFG-071).

Every operation issues its request through the same retry loop the logical layer uses, via
`Client._execute` (D-M2-4). None of them builds an HTTP path, a status mapping or a
recognition table of its own; the two refinements the section-05 requirements add
(AUT-084, AUT-085) live in the shared mapper in `logical.py`, not here. D-M2-4 also rules
that Python's executor is **not** extracted into a `RequestExecutor`: calling
`Client._execute` is the accepted pattern -- it is what `Logical` already does -- and an
extraction has no requirement behind it (CLA-007).
"""

from __future__ import annotations

from collections.abc import Mapping, Sequence
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from typing import TYPE_CHECKING, Any, Final

from . import _token_files
from ._enrichment import interpolate_keys
from .errors import ErrorCatalog, ErrorCodes, make_error
from .logical import AuthInfo, Response
from .secrets import SecretString
from .token_source import TokenSource
from .transport import RequestOptions

if TYPE_CHECKING:
    from .client import Client

#: AUT-081's reserved `meta` keys, verbatim from `05-authentication.md`, plus the
#: `approle_env_` prefix rule below. The server refuses them too (`meta key(s) ... are
#: reserved` -> `BV-INPUT-009`); refusing client-side means the caller is told before a
#: request is spent, and it is the same code either way.
_RESERVED_META_KEYS: Final[frozenset[str]] = frozenset(
    {
        "spiffe_id",
        "machine_id",
        "username",
        "entity_id",
        "mount_path",
        "role_name",
        "role",
        "namespace_path",
        "namespace_id",
        "child_visible",
        "auth_method",
        "groups",
        "subject",
        "name_id",
        "name_id_format",
        "ferrogate_kid",
        "session_id",
        "approle_machine_bypass",
        "machine_identity_exempt",
    }
)

_RESERVED_META_KEY_PREFIX: Final = "approle_env_"


@dataclass(frozen=True)
class TokenInfo:
    """A lookup's `data` object (05 section "Token store operations").

    `id` is a `SecretString`, not a `str` (D-M2-12): a lookup's `id` *is* token material,
    and a plain string would be a second, non-redacting way to read the very token AUT-004
    requires `current_token` to redact (CNF-031/032).

    The wire `ttl` field is read by nothing: it is always `0` and the specification forbids
    exposing it. `remaining_ttl` is computed instead (AUT-014).
    """

    id: SecretString | None = None
    policies: tuple[str, ...] = ()
    path: str | None = None
    meta: Mapping[str, str] | None = None
    display_name: str | None = None
    num_uses: int = 0
    creation_time: datetime | None = None
    creation_ttl: timedelta = timedelta(0)
    explicit_max_ttl: timedelta = timedelta(0)
    period: timedelta | None = None
    remaining_ttl: timedelta | None = None


@dataclass
class CreateTokenRequest:
    """`Auth.Token.Create`'s request body (AUT-082).

    Every field the caller leaves unset is omitted from the wire body (OVR-007).
    `use_result` is a client-side switch and is never sent.
    """

    policies: Sequence[str] | None = None
    ttl: timedelta | None = None
    period: timedelta | None = None
    num_uses: int | None = None
    renewable: bool = True
    meta: Mapping[str, str] | None = None
    display_name: str | None = None
    explicit_max_ttl: timedelta | None = None
    no_default_policy: bool | None = None
    no_parent: bool | None = None
    id: str | None = None
    type: str | None = None
    child_visible: bool | None = None
    use_result: bool = False
    """AUT-082's opt-in that switches the client's token. Default `False`."""


class TokenOperations:
    """The token-store operations (`auth/token/*`, AUT-020, AUT-080...AUT-085).

    Only the nine paths the server actually has are exposed. `lookup-accessor`,
    `renew-self`, `renew-accessor`, `revoke-accessor`, `create-orphan` as a distinct path,
    `roles` and `tidy` do not exist on the server and the specification forbids exposing
    operations for them (05 section "Token store operations").
    """

    def __init__(self, client: "Client") -> None:
        self._client = client

    def use(self, token: SecretString) -> None:
        """AUT-020: replace the client's source with a `Static` one. No network call.

        An empty or whitespace-only token is refused with `BV-INPUT-001`.
        """
        value = token.reveal()
        if not value or not value.strip():
            raise make_error(
                ErrorCodes.INPUT_INVALID_ARGUMENT,
                attempts=0,
                details={
                    "argument": "token",
                    "reason": "A token must be a non-empty, non-whitespace string.",
                },
            )
        self._client._set_token(token)

    async def verify(self, options: RequestOptions | None = None) -> TokenInfo:
        """A `lookup_self` whose purpose is to fail with `BV-AUTHZ-001` when the token is
        invalid (05 section "Method: Token")."""
        return await self.lookup_self(options)

    async def create(
        self, request: CreateTokenRequest, options: RequestOptions | None = None
    ) -> AuthInfo:
        """AUT-082: `POST auth/token/create`.

        Returns the created token's `AuthInfo` and does **not** switch the client's token
        unless `request.use_result` is set. Reserved `meta` keys are refused before any
        request (AUT-081).
        """
        _guard_reserved_meta(request.meta)
        response = await self._execute(
            "POST", "auth/token/create", _serialise_create(request), options, idempotent=False
        )
        auth = _require_auth(response, "auth/token/create")
        if request.use_result:
            self._client._set_token(auth.client_token)
        return auth

    async def lookup(self, token: str, options: RequestOptions | None = None) -> TokenInfo:
        """`GET auth/token/lookup/{token}`.

        A `404` with an empty body is `BV-NOTFOUND-006 TokenNotFound`, not absence
        (AUT-084), and the token segment of the path is redacted in the error and in the
        observer event (ERR-003, CFG-080).
        """
        _require_token_argument(token)
        response = await self._execute(
            "GET", f"auth/token/lookup/{token}", None, options, idempotent=True
        )
        return self._read_token_info(response, "auth/token/lookup")

    async def lookup_self(self, options: RequestOptions | None = None) -> TokenInfo:
        """`GET auth/token/lookup-self`. Also recorded as `Auth.token_info` (AUT-004)."""
        response = await self._execute(
            "GET", "auth/token/lookup-self", None, options, idempotent=True
        )
        info = self._read_token_info(response, "auth/token/lookup-self")
        self._client._set_token_info(info)
        return info

    async def renew(
        self, token: str, increment: int, options: RequestOptions | None = None
    ) -> AuthInfo:
        """`POST auth/token/renew/{token}` with the **required** `increment` body.

        An unknown or expired token yields `BV-AUTH-015 TokenNotRenewable` (AUT-085).
        """
        _require_token_argument(token)
        return await self._renew_path(token, increment, options)

    async def renew_self(self, increment: int, options: RequestOptions | None = None) -> AuthInfo:
        """AUT-080: `renew_self` goes through `renew/{current_token}`.

        There is no `renew-self` path on the server, so the live token appears in the
        request path, and the path is therefore redacted wherever it is surfaced (ERR-003
        in the error, CFG-080 in the observer event).
        """
        # Resolved exactly **once**, and then pinned onto the request so the executor's own
        # resolution is skipped: `RequestOptions.token` is the first thing the executor
        # honours.
        #
        # Resolving twice -- once here for the path, once there for the header -- was M2a's
        # F2 defect in .NET. AUT-080 says "the current token in the path", and with a
        # `Callback` source, whose contract is to be called on every resolution, two
        # resolutions can return two different tokens: the request would then renew token A
        # while authenticating as token B. A per-call `RequestOptions.token` is itself "the
        # current token" for this call (CFG-060), so it is used as-is and the client's
        # source is not resolved at all.
        base = options if options is not None else RequestOptions()
        current = base.token if base.token is not None else await self._client._resolve_token()
        current = current if current is not None else SecretString("")
        from dataclasses import replace as _replace

        pinned = _replace(base, token=current)
        return await self._renew_path(current.reveal(), increment, pinned)

    async def revoke(self, token: str, options: RequestOptions | None = None) -> None:
        """`POST auth/token/revoke/{token}`."""
        _require_token_argument(token)
        await self._execute(
            "POST", f"auth/token/revoke/{token}", None, options, idempotent=False
        )

    async def revoke_orphan(self, token: str, options: RequestOptions | None = None) -> None:
        """`POST auth/token/revoke-orphan/{token}` (sudo)."""
        _require_token_argument(token)
        await self._execute(
            "POST", f"auth/token/revoke-orphan/{token}", None, options, idempotent=False
        )

    async def revoke_self(self, options: RequestOptions | None = None) -> None:
        """AUT-083: `POST auth/token/revoke-self`, then clear the local token.

        A root-policy token is accepted by the server but not actually revoked (the logout
        is only recorded); the SDK clears its token either way, because the server's
        response is identical and the client cannot tell the two apart.
        """
        await self._execute("POST", "auth/token/revoke-self", None, options, idempotent=False)
        self._client.clear_token()

    async def audit_login(self, options: RequestOptions | None = None) -> None:
        """`POST auth/token/audit-login`: records a login event for a token sign-in."""
        await self._execute("POST", "auth/token/audit-login", None, options, idempotent=False)

    async def _renew_path(
        self, token: str, increment: int, options: RequestOptions | None
    ) -> AuthInfo:
        response = await self._execute(
            "POST",
            f"auth/token/renew/{token}",
            {"increment": increment},
            options,
            idempotent=False,
        )
        return _require_auth(response, "auth/token/renew")

    async def _execute(
        self,
        method: str,
        path: str,
        body: Mapping[str, Any] | None,
        options: RequestOptions | None,
        *,
        idempotent: bool,
    ) -> Response | None:
        """D-M2-4: the shared retry loop, never a private request path.

        `read_like=False` because no token-store operation treats a `404` with an empty
        body as absence -- AUT-084 makes exactly that shape an error.
        """
        result = await self._client._execute(
            method=method,
            path=path,
            body=body,
            options=options,
            default_idempotent=idempotent,
            read_like=False,
        )
        # `_execute` is typed for the logical layer's union; a non-absolute call never
        # returns a `RawResponse`, and a 204 returns `None`.
        return result if isinstance(result, Response) else None

    def _read_token_info(self, response: Response | None, path: str) -> TokenInfo:
        """Map a lookup's `data` object, computing AUT-014's `remaining_ttl`."""
        data = response.data if response is not None else None
        if data is None:
            raise _envelope_mismatch(path, "data")

        creation_time = _read_unix_time(data, "creation_time")
        creation_ttl = _read_seconds(data, "creation_ttl") or timedelta(0)
        # AUT-014: creation_time + creation_ttl - now_utc, and None when creation_ttl is 0.
        # Both operands come from the response and the injected clock, never from the wire
        # `ttl`.
        remaining_ttl: timedelta | None = None
        if creation_ttl != timedelta(0) and creation_time is not None:
            remaining_ttl = creation_time + creation_ttl - self._client._clock.now_utc()

        raw_id = data.get("id")
        return TokenInfo(
            id=SecretString(raw_id) if isinstance(raw_id, str) else None,
            policies=_read_string_tuple(data, "policies"),
            path=_read_string(data, "path"),
            meta=_read_string_map(data, "meta"),
            display_name=_read_string(data, "display_name"),
            num_uses=_read_int(data, "num_uses") or 0,
            creation_time=creation_time,
            creation_ttl=creation_ttl,
            explicit_max_ttl=_read_seconds(data, "explicit_max_ttl") or timedelta(0),
            period=_read_seconds(data, "period"),
            remaining_ttl=remaining_ttl,
        )


class AuthOperations:
    """The `Auth` area (OVR-008), reached from `Client.auth`."""

    def __init__(self, client: "Client") -> None:
        self._client = client
        self._token = TokenOperations(client)

    @property
    def token_source(self) -> TokenSource:
        """AUT-001: the one `TokenSource` this client holds.

        `Client.set_token` and `Auth.token.use` replace it with a `Static` one.
        """
        return self._client._token_source

    @property
    def current_token(self) -> SecretString | None:
        """AUT-004: the current token as a redacting secret type, or `None`.

        Reading this never performs a resolution, so it can neither trigger a login nor
        call an application callback.
        """
        return self._client._current_token

    @property
    def token_info(self) -> TokenInfo | None:
        """AUT-004: the last `lookup_self` result, if any."""
        return self._client._token_info

    @property
    def token(self) -> TokenOperations:
        """The token-store operations (AUT-020, AUT-080...AUT-085)."""
        return self._token

    def persist_token(self) -> None:
        """CFG-031: write the current token to `TokenFile` with owner-only permissions.

        This is the **only** way the SDK ever writes that file -- a login never does, which
        is why the requirement makes it an explicit call rather than a side effect.

        Raises `BV-INPUT-001` when the client holds no token, because persisting "no token"
        would silently leave a stale one on disk, and `BV-CONFIG-011` when the file cannot
        be written (D-M2-16).
        """
        token = self._client._current_token
        if token is None:
            raise make_error(
                ErrorCodes.INPUT_INVALID_ARGUMENT,
                attempts=0,
                details={
                    "argument": "current_token",
                    "reason": "The client holds no token to persist.",
                },
            )
        _token_files.write(self._client.config.token_file, token.reveal())

    def forget_persisted_token(self) -> None:
        """CFG-032: delete `TokenFile` if present; do not fail if it is absent."""
        _token_files.delete(self._client.config.token_file)


# --------------------------------------------------------------------------------
# Helpers
# --------------------------------------------------------------------------------


def _require_token_argument(token: str) -> None:
    if not token or not token.strip():
        raise make_error(
            ErrorCodes.INPUT_INVALID_ARGUMENT,
            attempts=0,
            details={
                "argument": "token",
                "reason": "A token must be a non-empty, non-whitespace string.",
            },
        )


def _guard_reserved_meta(meta: Mapping[str, str] | None) -> None:
    """AUT-081's client-side refusal.

    The catalogue hint for `BV-INPUT-009` points at `details.keys`, so the offending keys
    are interpolated into it on the same ERR-034 principle the path uses: a hint that names
    a details key must name the value the SDK actually saw.
    """
    if meta is None:
        return
    offending = sorted(key for key in meta if _is_reserved_meta_key(key))
    if not offending:
        return
    entry = ErrorCatalog.get(ErrorCodes.INPUT_RESERVED_TOKEN_META_KEY)
    interpolated = interpolate_keys(entry.hint, offending) if entry is not None else ""
    # `make_error` rebuilds the hint from the catalogue, so only the *appended* sentence is
    # passed to it; the guard above decides whether there is one at all.
    appended = interpolated[len(entry.hint) :].strip() if entry is not None else ""
    raise make_error(
        ErrorCodes.INPUT_RESERVED_TOKEN_META_KEY,
        attempts=0,
        details={"keys": tuple(offending)},
        extra_hint=appended or None,
    )


def _is_reserved_meta_key(key: str) -> bool:
    return key.startswith(_RESERVED_META_KEY_PREFIX) or key in _RESERVED_META_KEYS


def _serialise_create(request: CreateTokenRequest) -> dict[str, Any]:
    """Serialise to the wire field names of `05-authentication.md`'s table (OVR-007)."""
    body: dict[str, Any] = {}
    if request.policies is not None:
        body["policies"] = list(request.policies)
    if request.ttl is not None:
        body["ttl"] = int(request.ttl.total_seconds())
    if request.period is not None:
        body["period"] = int(request.period.total_seconds())
    if request.num_uses is not None:
        body["num_uses"] = request.num_uses
    body["renewable"] = request.renewable
    if request.meta is not None:
        body["meta"] = dict(request.meta)
    if request.display_name is not None:
        body["display_name"] = request.display_name
    if request.explicit_max_ttl is not None:
        body["explicit_max_ttl"] = int(request.explicit_max_ttl.total_seconds())
    if request.no_default_policy is not None:
        body["no_default_policy"] = request.no_default_policy
    if request.no_parent is not None:
        body["no_parent"] = request.no_parent
    if request.id is not None:
        body["id"] = request.id
    if request.type is not None:
        body["type"] = request.type
    if request.child_visible is not None:
        body["child_visible"] = request.child_visible
    return body


def _require_auth(response: Response | None, path: str) -> AuthInfo:
    """An operation whose response contract is an envelope `auth` object got something else.

    Reported as `BV-PROTOCOL-001`, which is what `04-error-model.md` names for a response
    that does not match the documented envelope -- not as a `None` the caller would
    dereference, and not as a fabricated empty `AuthInfo` (D-M1c-25).
    """
    auth = response.auth if response is not None else None
    if auth is None:
        raise _envelope_mismatch(path, "auth")
    return auth


def _envelope_mismatch(path: str, missing_field: str) -> Any:
    return make_error(
        ErrorCodes.PROTOCOL_UNEXPECTED_RESPONSE,
        attempts=1,
        path=path,
        details={"field": missing_field},
    )


def _read_string(data: Mapping[str, Any], name: str) -> str | None:
    value = data.get(name)
    return value if isinstance(value, str) else None


def _read_int(data: Mapping[str, Any], name: str) -> int | None:
    value = data.get(name)
    return int(value) if isinstance(value, int) and not isinstance(value, bool) else None


def _read_seconds(data: Mapping[str, Any], name: str) -> timedelta | None:
    value = data.get(name)
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return None
    return timedelta(seconds=int(value))


def _read_unix_time(data: Mapping[str, Any], name: str) -> datetime | None:
    value = data.get(name)
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return None
    return datetime.fromtimestamp(int(value), tz=timezone.utc)


def _read_string_tuple(data: Mapping[str, Any], name: str) -> tuple[str, ...]:
    value = data.get(name)
    if not isinstance(value, Sequence) or isinstance(value, (str, bytes)):
        return ()
    return tuple(item if isinstance(item, str) else "" for item in value)


def _read_string_map(data: Mapping[str, Any], name: str) -> Mapping[str, str] | None:
    value = data.get(name)
    if not isinstance(value, Mapping):
        return None
    return {str(key): item if isinstance(item, str) else "" for key, item in value.items()}


__all__ = ["AuthOperations", "CreateTokenRequest", "TokenInfo", "TokenOperations"]
