"""Unit tests for the M2a auth surface: the seam, its concurrency, and its error contract.

The fixtures in `test_auth_fixtures.py` cover the request paths. These cover the rulings no
captured server response can reach: D-M2-11(a)'s single-flight, D-M2-17's re-arm-on-fault,
D-M2-18 item 1's except ordering, and the exact conditions AUT-084/AUT-085 are gated on.
"""

from __future__ import annotations

import asyncio
import os
import stat
from datetime import datetime, timedelta, timezone
from pathlib import Path

import pytest

from bastionvault_integration_sdk import Client, ClientOptions
from bastionvault_integration_sdk.auth import CreateTokenRequest, TokenInfo
from bastionvault_integration_sdk.errors import BastionVaultError, ErrorCodes, make_error
from bastionvault_integration_sdk.logical import _interpret_error_status
from bastionvault_integration_sdk.secrets import REDACTION_MARKER, SecretString
from bastionvault_integration_sdk.testing import FakeTransport
from bastionvault_integration_sdk.token_source import TokenSource, TokenSourceKind
from bastionvault_integration_sdk.transport import (
    RequestEvent,
    RequestOptions,
    RetryPolicy,
    TransportResponse,
)


class _FrozenClock:
    def __init__(self, start: datetime | None = None) -> None:
        self._now = start if start is not None else datetime(2026, 1, 1, tzinfo=timezone.utc)

    def now_utc(self) -> datetime:
        return self._now

    async def delay(self, duration: timedelta) -> None:
        self._now += duration


class _ZeroJitter:
    def next_double(self) -> float:
        return 0.5


class _CapturingObserver:
    def __init__(self) -> None:
        self.events: list[RequestEvent] = []

    def on_request_completed(self, event: RequestEvent) -> None:
        self.events.append(event)


def _body(transport: FakeTransport, index: int = 0) -> str:
    """The request body as text. `TransportRequest.body` is optional, so this asserts it."""
    raw = transport.requests[index].body
    assert raw is not None
    return raw.decode()


def _json(status: int, body: str = "{}") -> TransportResponse:
    return TransportResponse(
        status_code=status, headers={"Content-Type": "application/json"}, body=body.encode()
    )


def _client(
    *,
    token: str | None = "s.token",
    token_source: TokenSource | None = None,
    exchanges: list[object] | None = None,
    observer: _CapturingObserver | None = None,
    token_file: str | None = None,
    max_attempts: int = 1,
) -> tuple[Client, FakeTransport]:
    transport = FakeTransport(exchanges=exchanges or [])  # type: ignore[arg-type]
    client = Client(
        ClientOptions(
            address="https://vault.example.com:8200",
            token=token,
            token_source=token_source,
            token_file=token_file,
            retry_policy=RetryPolicy(max_attempts=max_attempts),
            observer=observer,
        ),
        transport=transport,
        clock=_FrozenClock(),
        jitter_source=_ZeroJitter(),
    )
    return client, transport


# --------------------------------------------------------------------------------
# D-M2-3: one redaction marker in all three languages
# --------------------------------------------------------------------------------


def test_secret_string_renders_the_shared_redaction_marker() -> None:
    """Python rendered `SecretString(***)` until M2a; .NET and Rust rendered `[REDACTED]`.

    One marker is what keeps TST-051's assertion one assertion rather than three.

    @req TST-051
    """
    secret = SecretString("s.FAKEtoken0000000000000000")

    assert REDACTION_MARKER == "[REDACTED]"
    assert str(secret) == "[REDACTED]"
    assert repr(secret) == 'SecretString("[REDACTED]")'
    assert "FAKEtoken" not in f"{secret!s} {secret!r}"
    assert secret.reveal() == "s.FAKEtoken0000000000000000"


# --------------------------------------------------------------------------------
# D-M2-2: the clock states which time it means
# --------------------------------------------------------------------------------


def test_the_clock_seam_is_now_utc_and_python_has_no_monotonic_member() -> None:
    """`Clock.now()` became `Clock.now_utc()`; the monotonic member is Rust-only.

    @req RES-003
    """
    from bastionvault_integration_sdk.transport import Clock, SystemClock

    assert hasattr(SystemClock(), "now_utc")
    assert not hasattr(SystemClock(), "now")
    assert not hasattr(SystemClock(), "now_monotonic")
    assert isinstance(SystemClock().now_utc(), datetime)
    assert SystemClock().now_utc().tzinfo is not None
    assert isinstance(SystemClock(), Clock)


# --------------------------------------------------------------------------------
# AUT-001 / AUT-004 / AUT-020: the source cell and the credential view
# --------------------------------------------------------------------------------


def test_aut001_a_client_holds_exactly_one_source_and_set_token_replaces_it() -> None:
    """@req AUT-001 @req CFG-070"""
    client, _ = _client(token="s.configured")

    assert client.auth.token_source.kind is TokenSourceKind.STATIC
    first = client.auth.token_source

    client.set_token(SecretString("s.rotated"))

    assert client.auth.token_source is not first
    assert client.auth.token_source.kind is TokenSourceKind.STATIC
    assert client.auth.current_token is not None
    assert client.auth.current_token.reveal() == "s.rotated"


def test_aut001_an_injected_callback_source_is_the_clients_one_source() -> None:
    """D-M2-12: without an injection point `TokenSource.callback` is decorative.

    @req AUT-001
    """

    async def from_kms() -> SecretString:
        return SecretString("s.from-kms")

    client, _ = _client(token=None, token_source=TokenSource.callback(from_kms))

    assert client.auth.token_source.kind is TokenSourceKind.CALLBACK
    # AUT-004: reading the credential never resolves, so a callback source reports nothing
    # until a request has actually resolved it.
    assert client.auth.current_token is None

    client.set_token(SecretString("s.explicit"))

    replaced: TokenSourceKind = client.auth.token_source.kind
    assert replaced is TokenSourceKind.STATIC


def test_aut004_current_token_redacts_and_never_resolves() -> None:
    """@req AUT-004"""
    calls = 0

    async def counting() -> SecretString:
        nonlocal calls
        calls += 1
        return SecretString("s.callback")

    client, _ = _client(token=None, token_source=TokenSource.callback(counting))

    for _ in range(3):
        assert client.auth.current_token is None

    assert calls == 0


def test_aut004_token_info_is_the_last_lookup_self_result() -> None:
    """@req AUT-004 @req AUT-014"""
    body = (
        '{"data": {"id": "s.me", "policies": ["default"], "display_name": "alice",'
        ' "creation_time": 1767225600, "creation_ttl": 3600, "num_uses": 0}}'
    )
    client, _ = _client(exchanges=[_json(200, body)])

    assert client.auth.token_info is None
    info = asyncio.run(client.auth.token.lookup_self())

    assert client.auth.token_info is info
    assert info.display_name == "alice"


def test_aut020_use_installs_a_static_source_without_a_request() -> None:
    """@req AUT-020 @req AUT-001"""
    client, transport = _client()

    client.auth.token.use(SecretString("s.used"))

    assert transport.requests == []
    assert client.auth.token_source.kind is TokenSourceKind.STATIC
    assert client.auth.current_token is not None
    assert client.auth.current_token.reveal() == "s.used"


@pytest.mark.parametrize("value", ["", "   ", "\t\n"])
def test_aut020_use_refuses_an_empty_or_whitespace_token(value: str) -> None:
    """@req AUT-020"""
    client, transport = _client()

    with pytest.raises(BastionVaultError) as raised:
        client.auth.token.use(SecretString(value))

    assert raised.value.code == ErrorCodes.INPUT_INVALID_ARGUMENT
    assert raised.value.attempts == 0
    assert transport.requests == []


# --------------------------------------------------------------------------------
# AUT-014: remaining_ttl, and the wire `ttl` that is never exposed
# --------------------------------------------------------------------------------


def test_aut014_remaining_ttl_is_computed_from_the_clock_not_from_the_wire_ttl() -> None:
    """`creation_time + creation_ttl - now_utc`, and the wire `ttl` is always 0.

    Unlike `auth.token.lookup-self-remaining-ttl`, this pins the *arithmetic*: `now_utc`
    is 30 minutes after `creation_time`, so a `creation_ttl` of an hour must leave half an
    hour. Returning a bare `creation_ttl` -- which that fixture cannot distinguish, because
    its `clock.start` coincides with its `creation_time` -- fails here.

    @req AUT-014
    """
    creation = datetime(2026, 9, 13, 12, 0, tzinfo=timezone.utc)
    body = (
        '{"data": {"id": "s.me", "ttl": 0, "creation_time": %d, "creation_ttl": 3600}}'
        % int(creation.timestamp())
    )
    transport = FakeTransport(exchanges=[_json(200, body)])
    client = Client(
        ClientOptions(address="https://vault.example.com:8200", token="s.token"),
        transport=transport,
        clock=_FrozenClock(creation + timedelta(minutes=30)),
        jitter_source=_ZeroJitter(),
    )

    info = asyncio.run(client.auth.token.lookup_self())

    assert info.remaining_ttl == timedelta(minutes=30)
    assert info.creation_ttl == timedelta(hours=1)
    assert not hasattr(info, "ttl")


def test_aut014_remaining_ttl_is_none_when_creation_ttl_is_zero() -> None:
    """@req AUT-014"""
    body = '{"data": {"id": "s.me", "ttl": 0, "creation_time": 1767225600, "creation_ttl": 0}}'
    client, _ = _client(exchanges=[_json(200, body)])

    info = asyncio.run(client.auth.token.lookup_self())

    assert info.remaining_ttl is None


def test_d_m2_12_token_info_id_is_a_secret_type() -> None:
    """A lookup's `id` *is* token material; a plain `str` would be a second, non-redacting
    way to read the very token AUT-004 requires `current_token` to redact.

    @req AUT-004
    """
    body = '{"data": {"id": "s.FAKEtoken0000000000000000", "creation_ttl": 0}}'
    client, _ = _client(exchanges=[_json(200, body)])

    info = asyncio.run(client.auth.token.lookup_self())

    assert isinstance(info.id, SecretString)
    assert str(info.id) == REDACTION_MARKER
    assert "FAKEtoken" not in repr(info)
    assert info.id.reveal() == "s.FAKEtoken0000000000000000"


def test_a_lookup_without_a_data_object_is_a_protocol_error_not_a_fabricated_result() -> None:
    """D-M1c-25: a deferred or impossible branch never returns a plausible guess.

    A `204` carries no envelope at all, so there is no `data` to map. That is reported as
    `BV-PROTOCOL-001` -- what `04-error-model.md` names for a response that does not match
    the documented envelope -- rather than as a `None` the caller would dereference or a
    fabricated empty `TokenInfo`.

    @req ERR-020
    """
    client, _ = _client(exchanges=[TransportResponse(204, {}, b"")])

    with pytest.raises(BastionVaultError) as raised:
        asyncio.run(client.auth.token.lookup_self())

    assert raised.value.code == ErrorCodes.PROTOCOL_UNEXPECTED_RESPONSE
    assert raised.value.details["field"] == "data"


def test_a_renew_without_an_auth_object_is_a_protocol_error() -> None:
    """@req ERR-020"""
    client, _ = _client(exchanges=[_json(200, '{"data": {}}')])

    with pytest.raises(BastionVaultError) as raised:
        asyncio.run(client.auth.token.renew("s.other", 3600))

    assert raised.value.code == ErrorCodes.PROTOCOL_UNEXPECTED_RESPONSE
    assert raised.value.details["field"] == "auth"


# --------------------------------------------------------------------------------
# AUT-080..AUT-083: the token-store paths
# --------------------------------------------------------------------------------


def test_aut080_renew_self_uses_renew_path_and_resolves_the_token_exactly_once() -> None:
    """There is no `renew-self`, so the live token goes in the path -- resolved **once**.

    Resolving twice (once for the path, once for the header) was M2a's F2 defect in .NET:
    a `Callback` source is contracted to be called on every resolution, so two resolutions
    can return two different tokens and the request would renew token A while
    authenticating as token B.

    @req AUT-080 @req CFG-070
    """
    resolutions = 0

    async def rotating() -> SecretString:
        nonlocal resolutions
        resolutions += 1
        return SecretString(f"s.token-{resolutions}")

    body = '{"auth": {"client_token": "s.token-1", "lease_duration": 3600, "renewable": true}}'
    client, transport = _client(
        token=None, token_source=TokenSource.callback(rotating), exchanges=[_json(200, body)]
    )

    asyncio.run(client.auth.token.renew_self(3600))

    assert resolutions == 1
    request = transport.requests[0]
    assert request.url.endswith("/v1/auth/token/renew/s.token-1")
    assert request.headers["X-BastionVault-Token"] == "s.token-1"


def test_aut081_reserved_meta_keys_are_refused_before_any_request() -> None:
    """The offending keys are interpolated into the hint that points at `Details.keys`.

    @req AUT-081 @req ERR-034
    """
    client, transport = _client()
    request = CreateTokenRequest(
        policies=["default"],
        meta={"purpose": "x", "spiffe_id": "spiffe://a/b", "approle_env_secret": "prod"},
    )

    with pytest.raises(BastionVaultError) as raised:
        asyncio.run(client.auth.token.create(request))

    assert raised.value.code == ErrorCodes.INPUT_RESERVED_TOKEN_META_KEY
    assert raised.value.attempts == 0
    assert transport.requests == []
    assert set(raised.value.details["keys"]) == {"spiffe_id", "approle_env_secret"}
    assert "approle_env_secret" in raised.value.hint
    assert "purpose" not in raised.value.details["keys"]


def test_the_keys_interpolation_is_a_no_op_without_keys_or_without_the_marker() -> None:
    """ERR-034 never rewrites a hint that does not point at the details key it names.

    @req ERR-034
    """
    from bastionvault_integration_sdk._enrichment import interpolate_keys

    assert interpolate_keys("Remove `Details.keys`.", []) == "Remove `Details.keys`."
    assert interpolate_keys("A hint with no marker.", ["a"]) == "A hint with no marker."
    assert "were `a`, `b`" in interpolate_keys("Remove `Details.keys`.", ["a", "b"])


def test_a_successful_lookup_maps_the_other_tokens_data(
) -> None:
    """`Auth.Token.Lookup` on a live token returns that token's `TokenInfo`.

    @req AUT-084 @req AUT-014
    """
    body = (
        '{"data": {"id": "s.other", "policies": ["ops"], "path": "auth/userpass/login/bob",'
        ' "meta": {"username": "bob"}, "display_name": "bob", "num_uses": 4,'
        ' "creation_time": 1767225600, "creation_ttl": 7200, "explicit_max_ttl": 86400,'
        ' "period": 600, "ttl": 0}}'
    )
    client, transport = _client(exchanges=[_json(200, body)])

    info = asyncio.run(client.auth.token.lookup("s.other"))

    assert transport.requests[0].url.endswith("/v1/auth/token/lookup/s.other")
    assert info.id is not None and info.id.reveal() == "s.other"
    assert info.policies == ("ops",)
    assert info.meta == {"username": "bob"}
    assert info.num_uses == 4
    assert info.creation_ttl == timedelta(hours=2)
    assert info.explicit_max_ttl == timedelta(days=1)
    assert info.period == timedelta(minutes=10)
    # A `Lookup` of another token is not the client's own, so it is not recorded as
    # `Auth.token_info` -- only `lookup_self` is (AUT-004).
    assert client.auth.token_info is None


def test_aut081_the_prefix_rule_and_the_named_set_are_both_enforced() -> None:
    """@req AUT-081"""
    client, _ = _client()

    for key in ("machine_id", "namespace_path", "ferrogate_kid", "approle_env_anything"):
        with pytest.raises(BastionVaultError) as raised:
            asyncio.run(client.auth.token.create(CreateTokenRequest(meta={key: "v"})))
        assert raised.value.code == ErrorCodes.INPUT_RESERVED_TOKEN_META_KEY


def test_aut082_create_does_not_switch_the_token_unless_use_result_is_set() -> None:
    """@req AUT-082"""
    body = '{"auth": {"client_token": "s.child", "lease_duration": 60, "renewable": true}}'

    client, _ = _client(exchanges=[_json(200, body)])
    asyncio.run(client.auth.token.create(CreateTokenRequest(policies=["default"])))
    assert client.auth.current_token is not None
    assert client.auth.current_token.reveal() == "s.token"

    client, _ = _client(exchanges=[_json(200, body)])
    asyncio.run(
        client.auth.token.create(CreateTokenRequest(policies=["default"], use_result=True))
    )
    assert client.auth.current_token is not None
    assert client.auth.current_token.reveal() == "s.child"


def test_aut082_create_omits_every_unset_field_and_never_sends_use_result() -> None:
    """@req AUT-082"""
    import json as _json_module

    body = '{"auth": {"client_token": "s.child", "lease_duration": 60, "renewable": true}}'
    client, transport = _client(exchanges=[_json(200, body)])

    asyncio.run(
        client.auth.token.create(
            CreateTokenRequest(policies=["a"], ttl=timedelta(minutes=5), use_result=False)
        )
    )

    sent = _json_module.loads(_body(transport, 0))
    assert sent == {"policies": ["a"], "ttl": 300, "renewable": True}
    assert "use_result" not in sent
    assert "UseResult" not in _body(transport, 0)


def test_aut083_revoke_self_clears_the_local_token() -> None:
    """A root-policy token is accepted but not revoked; the SDK clears its token anyway.

    @req AUT-083 @req CFG-070 @req AUT-001
    """
    client, transport = _client(exchanges=[TransportResponse(204, {}, b"")])

    asyncio.run(client.auth.token.revoke_self())

    assert transport.requests[0].url.endswith("/v1/auth/token/revoke-self")
    assert client.auth.current_token is None
    # AUT-001 still holds: exactly one source, now a `Static` one holding nothing.
    assert client.auth.token_source.kind is TokenSourceKind.STATIC


def test_the_nine_server_paths_are_exposed_and_the_absent_ones_are_not() -> None:
    """The specification forbids exposing operations for paths the server does not have.

    @req AUT-080 @req AUT-084
    """
    token = _client()[0].auth.token

    for name in (
        "create",
        "lookup",
        "lookup_self",
        "renew",
        "renew_self",
        "revoke",
        "revoke_orphan",
        "revoke_self",
        "audit_login",
        "use",
        "verify",
    ):
        assert hasattr(token, name), name

    for absent in (
        "lookup_accessor",
        "renew_accessor",
        "revoke_accessor",
        "create_orphan",
        "roles",
        "tidy",
    ):
        assert not hasattr(token, absent), absent


def test_the_deferred_auth_surfaces_are_absent_not_stubbed() -> None:
    """D-M1c-25 / D-M2-8: `Auth.Cert`, FerroGate, OIDC/SAML, AppID admin and FIDO2 are
    **absent** at M2a, not stubbed. A stub is a branch no test can reach.

   
    """
    auth = _client()[0].auth

    for absent in ("cert", "ferrogate", "oidc", "saml", "fido2", "userpass", "app_id"):
        assert not hasattr(auth, absent), absent
    assert not hasattr(TokenSource, "login")


def test_verify_is_a_lookup_self() -> None:
    """@req AUT-004"""
    body = '{"data": {"id": "s.me", "creation_ttl": 0}}'
    client, transport = _client(exchanges=[_json(200, body)])

    asyncio.run(client.auth.token.verify())

    assert transport.requests[0].url.endswith("/v1/auth/token/lookup-self")


@pytest.mark.parametrize(
    ("method_name", "suffix"),
    [
        ("revoke", "/v1/auth/token/revoke/s.other"),
        ("revoke_orphan", "/v1/auth/token/revoke-orphan/s.other"),
    ],
)
def test_the_revoke_paths_are_the_specified_ones(method_name: str, suffix: str) -> None:
    """@req AUT-083"""
    client, transport = _client(exchanges=[TransportResponse(204, {}, b"")])

    asyncio.run(getattr(client.auth.token, method_name)("s.other"))

    assert transport.requests[0].url.endswith(suffix)
    assert transport.requests[0].method == "POST"


def test_audit_login_posts_to_the_audit_login_path() -> None:
    client, transport = _client(exchanges=[TransportResponse(204, {}, b"")])

    asyncio.run(client.auth.token.audit_login())

    assert transport.requests[0].url.endswith("/v1/auth/token/audit-login")


@pytest.mark.parametrize("method_name", ["lookup", "revoke", "revoke_orphan"])
def test_an_empty_token_argument_is_refused_client_side(method_name: str) -> None:
    """@req AUT-020"""
    client, transport = _client()

    with pytest.raises(BastionVaultError) as raised:
        asyncio.run(getattr(client.auth.token, method_name)("  "))

    assert raised.value.code == ErrorCodes.INPUT_INVALID_ARGUMENT
    assert transport.requests == []


def test_renew_refuses_an_empty_token_argument() -> None:
    """@req AUT-085"""
    client, transport = _client()

    with pytest.raises(BastionVaultError) as raised:
        asyncio.run(client.auth.token.renew("", 60))

    assert raised.value.code == ErrorCodes.INPUT_INVALID_ARGUMENT
    assert transport.requests == []


# --------------------------------------------------------------------------------
# AUT-084 / AUT-085: every condition, and the generic rows off those paths
# --------------------------------------------------------------------------------


def _map_error(status: int, body: str, path: str) -> BastionVaultError:
    return _interpret_error_status(
        status_code=status,
        body_text=body,
        headers={"Content-Type": "application/json"},
        method="GET",
        display_path=path,
    )


def test_aut084_fires_only_on_a_404_with_an_empty_body_under_the_lookup_endpoint() -> None:
    """Each condition reproduced, and each one alone flips the result back.

    `AUT-084`'s shape is a `404` **with an empty body** under `auth/token/lookup/{token}`.
    A substring test on `auth/token/lookup` was half of M2a's F1 defect in .NET: it also
    matches `auth/token/lookup-self` and any caller path containing that text.

    @req AUT-084
    """
    assert (
        _map_error(404, "", "auth/token/lookup/s.unknown").code
        == ErrorCodes.NOT_FOUND_TOKEN_NOT_FOUND
    )

    # A 404 carrying a body is the server saying something else.
    assert (
        _map_error(404, '{"error": "no handler for route"}', "auth/token/lookup/s.unknown").code
        != ErrorCodes.NOT_FOUND_TOKEN_NOT_FOUND
    )
    # `lookup-self` is a different endpoint the specification names no refinement for.
    assert (
        _map_error(404, "", "auth/token/lookup-self").code
        == ErrorCodes.NOT_FOUND_PATH_NOT_FOUND
    )
    # An endpoint test, not a substring test: a caller path containing the text does not match.
    assert (
        _map_error(404, "", "secret/data/auth/token/lookup/notes").code
        == ErrorCodes.NOT_FOUND_PATH_NOT_FOUND
    )
    # The prefix must be followed by a further segment.
    assert _map_error(404, "", "auth/token/lookup/").code == ErrorCodes.NOT_FOUND_PATH_NOT_FOUND
    # A different status is not AUT-084's shape.
    assert _map_error(403, "", "auth/token/lookup/s.unknown").code != (
        ErrorCodes.NOT_FOUND_TOKEN_NOT_FOUND
    )


def test_aut085_fires_only_on_a_400_whose_message_is_the_request_is_invalid_row() -> None:
    """Gated on the *recognition outcome*, not on the post-fallthrough code.

    Gating on the code was the other half of M2a's F1 defect: `map_status_to_code` sends
    every unmapped 4xx to `BV-INPUT-100` and Appendix B section 2 has three further rows
    that yield it, so a `400` caused by the caller's own malformed body -- including the
    missing-`increment` shape, and `increment` is required -- became
    `BV-AUTH-015 TokenNotRenewable`.

    @req AUT-085
    """
    assert (
        _map_error(400, '{"error": "Request is invalid."}', "auth/token/renew/s.old").code
        == ErrorCodes.AUTH_TOKEN_NOT_RENEWABLE
    )

    # The three sibling rows that share BV-INPUT-100 are not swept in with it.
    for sibling in (
        "request field is not found",
        "request field is invalid",
        "no data field is available for the request",
    ):
        mapped = _map_error(400, '{"error": "%s"}' % sibling, "auth/token/renew/s.old")
        assert mapped.code == ErrorCodes.INPUT_SERVER_REJECTED_REQUEST, sibling

    # A 400 whose message nothing recognises stays BV-INPUT-100 even on the renew path.
    assert (
        _map_error(400, '{"error": "something else entirely"}', "auth/token/renew/s.old").code
        == ErrorCodes.INPUT_SERVER_REJECTED_REQUEST
    )
    # Off the renew path, the row keeps its general meaning.
    assert (
        _map_error(400, '{"error": "Request is invalid."}', "secret/data/x").code
        == ErrorCodes.INPUT_SERVER_REJECTED_REQUEST
    )
    # And an endpoint test again, not a substring one.
    assert (
        _map_error(400, '{"error": "Request is invalid."}', "secret/auth/token/renew/x").code
        == ErrorCodes.INPUT_SERVER_REJECTED_REQUEST
    )
    # A different status is not AUT-085's shape.
    assert (
        _map_error(404, '{"error": "Request is invalid."}', "auth/token/renew/s.old").code
        != ErrorCodes.AUTH_TOKEN_NOT_RENEWABLE
    )


def test_the_generic_rows_still_fire_on_the_token_store_paths() -> None:
    """The refinements are additive: every other Appendix B row is unaffected.

    @req AUT-084 @req AUT-085 @req ERR-020
    """
    assert (
        _map_error(403, '{"error": "Permission denied."}', "auth/token/lookup/s.x").code
        == ErrorCodes.AUTHZ_PERMISSION_DENIED
    )
    assert (
        _map_error(503, '{"errors": ["bastionvault is sealed"]}', "auth/token/renew/s.x").code
        == ErrorCodes.SERVER_SEALED
    )
    assert (
        _map_error(500, '{"error": "unexpected"}', "auth/token/lookup/s.x").code
        == ErrorCodes.SERVER_INTERNAL_ERROR
    )


def test_err003_redacts_the_token_segment_in_the_error_and_the_observer_event() -> None:
    """The observer is the second consumer of the path after the error (D-M2-7).

    @req ERR-003 @req CFG-080 @req AUT-080
    """
    observer = _CapturingObserver()
    client, _ = _client(
        exchanges=[_json(404, "")], observer=observer, token="s.FAKEtoken0000000000000000"
    )

    with pytest.raises(BastionVaultError) as raised:
        asyncio.run(client.auth.token.lookup("s.FAKEother000000000000000000"))

    assert raised.value.path == "auth/token/lookup/<redacted>"
    assert "FAKEother" not in str(raised.value)
    assert observer.events[0].path == "auth/token/lookup/<redacted>"
    assert "FAKEother" not in repr(observer.events)
    assert "FAKEtoken" not in repr(observer.events)


# --------------------------------------------------------------------------------
# D-M2-11(a) / D-M2-17: single-flight, the in-flight token, and the re-arm
# --------------------------------------------------------------------------------


def test_d_m2_11a_concurrent_first_use_resolutions_await_one_login() -> None:
    """N concurrent first-use resolutions share **one** login, not N.

    The thundering-herd failure D-M2-11 blocked on: eight concurrent operations at startup
    would each resolve, none would find a cached token, and all eight would log in -- eight
    tokens issued, seven orphaned, against the one path the server's DoS guard rate-limits.

    @req CFG-070 @req AUT-001
    """
    logins = 0

    async def login() -> SecretString:
        nonlocal logins
        logins += 1
        await asyncio.sleep(0)  # a real await point, so the race is real
        return SecretString("s.logged-in")

    source = TokenSource._login_with(login)

    async def scenario() -> list[SecretString | None]:
        return list(await asyncio.gather(*(source.resolve() for _ in range(8))))

    resolved = asyncio.run(scenario())

    assert logins == 1
    assert all(token is not None and token.reveal() == "s.logged-in" for token in resolved)
    # A later resolution is served from the cache, still without a second login.
    assert asyncio.run(source.resolve()) is not None
    assert logins == 1


def test_d_m2_11a_static_and_callback_are_not_single_flighted() -> None:
    """A callback is the application's own coroutine; the SDK does not deduplicate it.

    @req AUT-001
    """
    calls = 0

    async def callback() -> SecretString:
        nonlocal calls
        calls += 1
        await asyncio.sleep(0)
        return SecretString("s.kms")

    source = TokenSource.callback(callback)

    async def scenario() -> None:
        await asyncio.gather(*(source.resolve() for _ in range(5)))

    asyncio.run(scenario())

    assert calls == 5


def test_cfg070_an_in_flight_request_keeps_the_token_it_started_with() -> None:
    """The snapshot is taken once per pass, above the retry loop (D-M1b-9, unmoved).

    `CFG-070`'s M1a justification was an atomic reference read, which D-M2-9 invalidates by
    making resolution async and side-effecting -- which is why the ID is re-opened onto the
    baseline and owned by M2a (D-M2-11(c)). This asserts the invariant under **real**
    concurrency, which its M1a test never did.

    @req CFG-070
    """
    release = asyncio.Event()

    class _BlockingTransport:
        supports_custom_verbs = True

        def __init__(self) -> None:
            self.tokens: list[str | None] = []

        async def send(self, request: object) -> TransportResponse:
            headers = getattr(request, "headers")
            self.tokens.append(headers.get("X-BastionVault-Token"))
            await release.wait()
            return _json(200, '{"data": {}}')

    transport = _BlockingTransport()
    client = Client(
        ClientOptions(address="https://vault.example.com:8200", token="s.first"),
        transport=transport,
        clock=_FrozenClock(),
        jitter_source=_ZeroJitter(),
    )

    async def scenario() -> None:
        in_flight = asyncio.gather(*(client.logical.read("secret/data/x") for _ in range(4)))
        await asyncio.sleep(0)
        await asyncio.sleep(0)
        # The rotation lands while all four requests are in flight.
        client.set_token(SecretString("s.second"))
        release.set()
        await in_flight
        # A request started *after* the rotation sees the new token.
        await client.logical.read("secret/data/x")

    asyncio.run(scenario())

    assert transport.tokens[:4] == ["s.first"] * 4
    assert transport.tokens[4] == "s.second"


def test_d_m2_17_a_faulted_flight_is_observed_by_its_awaiters_and_then_re_arms() -> None:
    """All three states D-M2-17 names, asserted together.

    `Lazy<Task<T>>` in .NET caches the *faulted* task, so without the re-arm one transient
    login failure at startup makes the client permanently unusable. D-M2-11(a) made
    concurrent callers share one login *attempt*, not one permanent verdict.

    @req CFG-070
    """
    attempts = 0

    async def flaky() -> SecretString:
        nonlocal attempts
        attempts += 1
        await asyncio.sleep(0)
        if attempts == 1:
            raise RuntimeError("login unavailable")
        return SecretString("s.second-attempt")

    source = TokenSource._login_with(flaky)

    async def scenario() -> tuple[list[BaseException | SecretString | None], SecretString | None]:
        # State 1: the awaiters of one flight all observe that flight's failure...
        outcomes = list(
            await asyncio.gather(*(source.resolve() for _ in range(4)), return_exceptions=True)
        )
        # State 3: ...and a *subsequent* resolution re-attempts.
        return outcomes, await source.resolve()

    outcomes, later = asyncio.run(scenario())

    assert all(isinstance(outcome, RuntimeError) for outcome in outcomes)
    # State 2: none of them retried inside its own call, so there was no retry storm.
    assert attempts == 2
    assert later is not None and later.reveal() == "s.second-attempt"


def test_d_m2_17_a_performer_raising_synchronously_still_re_arms() -> None:
    """A performer that throws *before its first await* must not poison the cell.

    In .NET this is the case `Lazy`'s factory exception makes unreachable even by a re-arm,
    which is why the performer is invoked through a coroutine function: calling it cannot
    raise, so the failure always lands as a faulted task.

    @req CFG-070
    """
    attempts = 0

    async def raises_before_awaiting() -> SecretString:
        nonlocal attempts
        attempts += 1
        if attempts == 1:
            raise RuntimeError("synchronous failure")  # no await executed first
        return SecretString("s.recovered")

    source = TokenSource._login_with(raises_before_awaiting)

    async def scenario() -> tuple[list[SecretString | BaseException | None], SecretString | None]:
        first: list[SecretString | BaseException | None] = list(
            await asyncio.gather(*(source.resolve() for _ in range(3)), return_exceptions=True)
        )
        return first, await source.resolve()

    outcomes, later = asyncio.run(scenario())

    assert all(isinstance(outcome, RuntimeError) for outcome in outcomes)
    assert attempts == 2
    assert later is not None and later.reveal() == "s.recovered"


def test_d_m2_17_a_performer_raising_cancelled_error_itself_also_re_arms() -> None:
    """A cancelled task is just as poisonous to cache as a faulted one.

    `CancelledError` derives from `BaseException`, not `Exception`, so the guard is
    `except BaseException` -- a bare `except Exception` would not see this at all.

    @req CFG-070
    """
    attempts = 0

    async def cancels_itself() -> SecretString:
        nonlocal attempts
        attempts += 1
        if attempts == 1:
            raise asyncio.CancelledError
        return SecretString("s.after-cancel")

    source = TokenSource._login_with(cancels_itself)

    async def scenario() -> SecretString | None:
        with pytest.raises(asyncio.CancelledError):
            await source.resolve()
        return await source.resolve()

    later = asyncio.run(scenario())

    assert attempts == 2
    assert later is not None and later.reveal() == "s.after-cancel"


def test_one_awaiters_cancellation_does_not_fail_the_others_sharing_the_flight() -> None:
    """The shared login keeps running when a single awaiter walks away.

    @req CFG-070
    """
    logins = 0
    started = asyncio.Event()

    async def slow_login() -> SecretString:
        nonlocal logins
        logins += 1
        started.set()
        await asyncio.sleep(0.05)
        return SecretString("s.slow")

    source = TokenSource._login_with(slow_login)

    async def scenario() -> SecretString | None:
        abandoned = asyncio.ensure_future(source.resolve())
        patient = asyncio.ensure_future(source.resolve())
        await started.wait()
        abandoned.cancel()
        with pytest.raises(asyncio.CancelledError):
            await abandoned
        return await patient

    resolved = asyncio.run(scenario())

    assert logins == 1
    assert resolved is not None and resolved.reveal() == "s.slow"


def test_d_m2_11a_a_loser_of_the_race_reads_the_winners_cached_token() -> None:
    """The *inner* half of D-M2-11(a)'s double-checked cache read.

    The ruling names "a module-private `asyncio.Lock` with a double-checked cache read",
    and this is the check that makes the second read load-bearing: a caller that passed the
    outer read while the cache was empty, then queued on the lock, must find the winner's
    token rather than start a second login. The interleaving is forced by holding the lock
    from the test, because the implementation deliberately never awaits while holding it --
    which is also why an ordinary `gather` cannot contend it.

    @req CFG-070
    """
    logins = 0

    async def login() -> SecretString:
        nonlocal logins
        logins += 1
        return SecretString("s.should-not-run")

    source = TokenSource._login_with(login)

    async def scenario() -> SecretString | None:
        lock = source._flight_lock()
        await lock.acquire()
        waiter = asyncio.ensure_future(source.resolve())
        await asyncio.sleep(0)  # the waiter is now queued on the lock
        source._cached = SecretString("s.won-the-race")
        lock.release()
        return await waiter

    resolved = asyncio.run(scenario())

    assert logins == 0
    assert resolved is not None and resolved.reveal() == "s.won-the-race"


def test_invalidating_a_login_source_re_arms_it_and_is_a_no_op_elsewhere() -> None:
    """@req AUT-001"""
    logins = 0

    async def login() -> SecretString:
        nonlocal logins
        logins += 1
        return SecretString(f"s.login-{logins}")

    source = TokenSource._login_with(login)
    assert asyncio.run(source.resolve()) is not None
    assert asyncio.run(source.resolve()) is not None
    assert logins == 1

    source._invalidate()
    assert asyncio.run(source.resolve()) is not None
    assert logins == 2

    static = TokenSource.static(SecretString("s.static"))
    static._invalidate()
    assert asyncio.run(static.resolve()) is not None


# --------------------------------------------------------------------------------
# D-M2-16 / D-M2-18 item 1: the resolution guard and its ordering
# --------------------------------------------------------------------------------


def test_bv_auth_017_wraps_a_non_sdk_source_failure_with_attempts_zero() -> None:
    """A `Callback` source is application code: an `httpx` error from a KMS lookup must not
    reach the caller as a raw runtime exception (ERR-020, TRN-054).

    `BV-AUTH-001 NoToken` is the closest code and is wrong: its message is "No token is
    *configured*", and here a token *is* configured and its resolution failed.

    @req ERR-020 @req AUT-001
    """
    cause = OSError("KMS unreachable")

    async def failing() -> SecretString:
        raise cause

    client, transport = _client(token=None, token_source=TokenSource.callback(failing))

    with pytest.raises(BastionVaultError) as raised:
        asyncio.run(client.logical.read("secret/data/x"))

    assert raised.value.code == ErrorCodes.AUTH_TOKEN_SOURCE_FAILED
    assert raised.value.code == "BV-AUTH-017"
    assert raised.value.attempts == 0
    assert raised.value.retryable is False  # ERR-006's set is closed and excludes it
    assert raised.value.__cause__ is cause
    assert transport.requests == []


def test_a_cancelled_resolution_reports_bv_transport_005_not_bv_auth_017() -> None:
    """D-M2-18 item 1: the ordering is load-bearing and Python has no exception filters.

    Inverted, cancellation would map to `BV-AUTH-017`. Note that `CancelledError` derives
    from `BaseException`, so a bare `except Exception` would not catch it -- it would escape
    untyped, which ERR-020/TRN-054 forbid just as firmly as the wrong code.

    @req ERR-020
    """

    async def cancelled() -> SecretString:
        raise asyncio.CancelledError

    client, transport = _client(token=None, token_source=TokenSource.callback(cancelled))

    with pytest.raises(BastionVaultError) as raised:
        asyncio.run(client.logical.read("secret/data/x"))

    assert raised.value.code == ErrorCodes.TRANSPORT_CANCELLED
    assert raised.value.code == "BV-TRANSPORT-005"
    assert raised.value.attempts == 0
    assert transport.requests == []


def test_the_two_arms_are_distinguished_rather_than_collapsed() -> None:
    """Both arms asserted side by side, which is what makes the ordering falsifiable.

    @req ERR-020
    """

    async def cancelled() -> SecretString:
        raise asyncio.CancelledError

    async def failed() -> SecretString:
        raise RuntimeError("boom")

    codes = []
    for performer in (cancelled, failed):
        client, _ = _client(token=None, token_source=TokenSource.callback(performer))
        with pytest.raises(BastionVaultError) as raised:
            asyncio.run(client.logical.read("secret/data/x"))
        codes.append(raised.value.code)

    assert codes == [ErrorCodes.TRANSPORT_CANCELLED, ErrorCodes.AUTH_TOKEN_SOURCE_FAILED]


def test_a_bastion_vault_error_from_the_source_is_not_wrapped() -> None:
    """M2b's `Login` source must keep its recogniser codes: AUT-003's replay keys on
    `BV-AUTHZ-001` specifically, and wrapping would break it outright (D-M2-18 item 3).

    @req ERR-020
    """
    coded = make_error(ErrorCodes.AUTH_INVALID_CREDENTIALS, attempts=1)

    async def failing() -> SecretString:
        raise coded

    client, _ = _client(token=None, token_source=TokenSource.callback(failing))

    with pytest.raises(BastionVaultError) as raised:
        asyncio.run(client.logical.read("secret/data/x"))

    assert raised.value is coded
    assert raised.value.code != ErrorCodes.AUTH_TOKEN_SOURCE_FAILED


def test_a_login_path_never_resolves_the_source() -> None:
    """CFG-020's first MUST: a login carries no token header, and resolution is skipped
    entirely rather than resolved-and-discarded -- so a `Login` source does not recurse
    into a login in order to send one.

    @req TRN-015
    """
    resolutions = 0

    async def counting() -> SecretString:
        nonlocal resolutions
        resolutions += 1
        return SecretString("s.callback")

    client, transport = _client(
        token=None,
        token_source=TokenSource.callback(counting),
        exchanges=[_json(200, '{"auth": {"client_token": "s.new", "lease_duration": 1}}')],
    )

    asyncio.run(client.logical.write("auth/userpass/login/alice", {"password": "p"}))

    assert resolutions == 0
    assert "X-BastionVault-Token" not in transport.requests[0].headers


# --------------------------------------------------------------------------------
# D-M2-9 ruling 2: one request id, two counters
# --------------------------------------------------------------------------------


def test_one_logical_operation_keeps_one_request_id_across_its_attempts() -> None:
    """@req RES-002 @req CFG-080"""
    observer = _CapturingObserver()
    client, _ = _client(
        exchanges=[
            make_error(ErrorCodes.TRANSPORT_TIMEOUT, attempts=0),
            _json(200, '{"data": {}}'),
        ],
        observer=observer,
        max_attempts=3,
    )

    asyncio.run(client.logical.read("secret/data/x"))

    assert len({event.request_id for event in observer.events}) == 1
    assert [event.attempt for event in observer.events] == [1, 2]


def test_the_reported_attempt_count_is_the_accumulated_one() -> None:
    """Per-pass `attempt` drives eligibility and backoff; `attempts_total` is reported.

    @req CFG-051 @req CFG-052 @req ERR-001
    """
    observer = _CapturingObserver()
    client, _ = _client(
        exchanges=[make_error(ErrorCodes.TRANSPORT_TIMEOUT, attempts=0)] * 3,
        observer=observer,
        max_attempts=3,
    )

    with pytest.raises(BastionVaultError) as raised:
        asyncio.run(client.logical.read("secret/data/x"))

    assert raised.value.attempts == 3
    assert [event.attempt for event in observer.events] == [1, 2, 3]


def test_an_inbound_request_id_and_attempt_offset_are_honoured() -> None:
    """The executor entry point gained both so AUT-003's replay (M2b) can run a second pass
    under the same id with its per-pass counter reset (D-M2-9 ruling 2).

    @req RES-002
    """
    observer = _CapturingObserver()
    client, _ = _client(exchanges=[_json(200, '{"data": {}}')], observer=observer)

    asyncio.run(
        client._execute(
            method="GET",
            path="secret/data/x",
            body=None,
            options=None,
            default_idempotent=True,
            read_like=True,
            request_id="carried-across-the-replay",
            attempts_before=2,
        )
    )

    assert observer.events[0].request_id == "carried-across-the-replay"
    assert observer.events[0].attempt == 3


# --------------------------------------------------------------------------------
# CFG-031 / CFG-032: the token-helper write path
# --------------------------------------------------------------------------------


def test_cfg031_persist_token_writes_owner_only(tmp_path: Path) -> None:
    """@req CFG-031"""
    target = tmp_path / "token"
    client, _ = _client(token="s.persisted", token_file=str(target))

    client.auth.persist_token()

    assert target.read_text() == "s.persisted"
    assert stat.S_IMODE(target.stat().st_mode) == 0o600


def test_cfg031_persist_token_overwrites_and_re_applies_the_mode(tmp_path: Path) -> None:
    """@req CFG-031"""
    target = tmp_path / "token"
    target.write_text("stale")
    os.chmod(target, 0o644)
    client, _ = _client(token="s.fresh", token_file=str(target))

    client.auth.persist_token()

    assert target.read_text() == "s.fresh"
    assert stat.S_IMODE(target.stat().st_mode) == 0o600


def test_cfg031_persisting_with_no_token_is_refused(tmp_path: Path) -> None:
    """Persisting "no token" would silently leave a stale one on disk.

    @req CFG-031
    """
    target = tmp_path / "token"
    client, _ = _client(token=None, token_file=str(target))

    with pytest.raises(BastionVaultError) as raised:
        client.auth.persist_token()

    assert raised.value.code == ErrorCodes.INPUT_INVALID_ARGUMENT
    assert not target.exists()


def test_cfg031_an_unwritable_token_file_is_bv_config_011(tmp_path: Path) -> None:
    """`BV-CONFIG-011 TokenFileNotWritable` (D-M2-16, amending D-M2-13).

    Not `BV-CONFIG-005 FileNotReadable`, whose message says the file cannot be *read* --
    the wrong sentence for a failed `persist_token`, and one a caller matching on the code
    could not tell apart from a genuine read failure.

    @req CFG-031
    """
    directory = tmp_path / "a-directory"
    directory.mkdir()
    client, _ = _client(token="s.persisted", token_file=str(directory))

    with pytest.raises(BastionVaultError) as raised:
        client.auth.persist_token()

    assert raised.value.code == ErrorCodes.CONFIG_TOKEN_FILE_NOT_WRITABLE
    assert raised.value.code == "BV-CONFIG-011"
    assert raised.value.details["path"] == str(directory)
    assert raised.value.__cause__ is not None
    assert "read" not in raised.value.message.casefold()


def test_cfg032_forget_deletes_the_file_and_tolerates_its_absence(tmp_path: Path) -> None:
    """@req CFG-032"""
    target = tmp_path / "token"
    target.write_text("s.persisted")
    client, _ = _client(token_file=str(target))

    client.auth.forget_persisted_token()
    assert not target.exists()

    # MUST NOT fail if it is absent -- neither a missing file nor a missing directory.
    client.auth.forget_persisted_token()
    missing_directory, _ = _client(token_file=str(tmp_path / "nope" / "token"))
    missing_directory.auth.forget_persisted_token()


def test_cfg032_an_undeletable_token_file_is_bv_config_011(tmp_path: Path) -> None:
    """@req CFG-032"""
    directory = tmp_path / "a-directory"
    directory.mkdir()
    (directory / "occupant").write_text("x")
    client, _ = _client(token_file=str(directory))

    with pytest.raises(BastionVaultError) as raised:
        client.auth.forget_persisted_token()

    assert raised.value.code == ErrorCodes.CONFIG_TOKEN_FILE_NOT_WRITABLE


def test_cfg032_a_token_file_under_a_non_directory_is_absent_not_an_error(
    tmp_path: Path,
) -> None:
    """`unlink` raises `NotADirectoryError` here, which is the same "absent" to a caller.

    @req CFG-032
    """
    occupant = tmp_path / "not-a-directory"
    occupant.write_text("x")
    client, _ = _client(token_file=str(occupant / "token"))

    client.auth.forget_persisted_token()  # must not raise


def test_cfg031_persist_token_respects_the_mode_even_under_a_permissive_umask(
    tmp_path: Path,
) -> None:
    """`O_CREAT`'s mode is masked by the umask, so the mode is re-applied on the descriptor.

    @req CFG-031
    """
    target = tmp_path / "token"
    client, _ = _client(token="s.persisted", token_file=str(target))
    previous = os.umask(0o000)
    try:
        client.auth.persist_token()
    finally:
        os.umask(previous)

    assert stat.S_IMODE(target.stat().st_mode) == 0o600


def test_aut082_every_create_field_reaches_the_wire_under_its_specified_name() -> None:
    """The wire field names of `05-authentication.md`'s table, and nothing else.

    @req AUT-082
    """
    import json as _json_module

    body = '{"auth": {"client_token": "s.child", "lease_duration": 60, "renewable": true}}'
    client, transport = _client(exchanges=[_json(200, body)])

    asyncio.run(
        client.auth.token.create(
            CreateTokenRequest(
                policies=["a", "b"],
                ttl=timedelta(minutes=10),
                period=timedelta(hours=1),
                num_uses=3,
                renewable=False,
                meta={"purpose": "x"},
                display_name="worker",
                explicit_max_ttl=timedelta(hours=2),
                no_default_policy=True,
                no_parent=True,
                id="preset-id",
                type="service",
                child_visible=False,
                use_result=False,
            )
        )
    )

    assert _json_module.loads(_body(transport, 0)) == {
        "policies": ["a", "b"],
        "ttl": 600,
        "period": 3600,
        "num_uses": 3,
        "renewable": False,
        "meta": {"purpose": "x"},
        "display_name": "worker",
        "explicit_max_ttl": 7200,
        "no_default_policy": True,
        "no_parent": True,
        "id": "preset-id",
        "type": "service",
        "child_visible": False,
    }


def test_a_lookup_ignores_wrongly_typed_fields_rather_than_raising() -> None:
    """A field the server sent with the wrong JSON type is absent, not fatal.

    Recognition of a token's own metadata must never be more fragile than the operation
    that returned it, which is D-M1c-4's rule applied to the lookup mapper.

    @req AUT-014
    """
    body = (
        '{"data": {"id": 7, "policies": "not-an-array", "path": 1, "meta": [],'
        ' "display_name": false, "num_uses": "many", "creation_time": true,'
        ' "creation_ttl": null, "explicit_max_ttl": "x", "period": {}}}'
    )
    client, _ = _client(exchanges=[_json(200, body)])

    info = asyncio.run(client.auth.token.lookup_self())

    assert info.id is None
    assert info.policies == ()
    assert info.path is None
    assert info.meta is None
    assert info.display_name is None
    assert info.num_uses == 0
    assert info.creation_time is None
    assert info.creation_ttl == timedelta(0)
    assert info.explicit_max_ttl == timedelta(0)
    assert info.period is None
    assert info.remaining_ttl is None


def test_a_lookup_maps_a_policies_array_containing_non_strings() -> None:
    """@req AUT-014"""
    body = '{"data": {"policies": ["default", 7], "creation_ttl": 0}}'
    client, _ = _client(exchanges=[_json(200, body)])

    assert asyncio.run(client.auth.token.lookup_self()).policies == ("default", "")


def test_a_lookup_maps_a_meta_object_containing_non_strings() -> None:
    """@req AUT-014"""
    body = '{"data": {"meta": {"a": "1", "b": 2}, "creation_ttl": 0}}'
    client, _ = _client(exchanges=[_json(200, body)])

    assert asyncio.run(client.auth.token.lookup_self()).meta == {"a": "1", "b": ""}


def test_renew_posts_the_required_increment_body() -> None:
    """`increment` is **required** on the renew paths.

    @req AUT-085
    """
    import json as _json_module

    body = '{"auth": {"client_token": "s.x", "lease_duration": 60, "renewable": true}}'
    client, transport = _client(exchanges=[_json(200, body)])

    asyncio.run(client.auth.token.renew("s.other", 900))

    assert transport.requests[0].url.endswith("/v1/auth/token/renew/s.other")
    assert _json_module.loads(_body(transport, 0)) == {"increment": 900}


def test_renew_self_with_no_token_posts_to_the_bare_renew_path() -> None:
    """Accepted for M2a without comment in the addendum: the `CFG-020` preflight is M2b's,
    so this is the absence of an unimplemented requirement, not a guess (D-M1c-25).

    @req AUT-080
    """
    body = '{"auth": {"client_token": "s.x", "lease_duration": 60, "renewable": true}}'
    client, transport = _client(token=None, exchanges=[_json(200, body)])

    asyncio.run(client.auth.token.renew_self(60))

    assert transport.requests[0].url.endswith("/v1/auth/token/renew/")


def test_cfg031_a_login_never_writes_the_token_file(tmp_path: Path) -> None:
    """`persist_token` is the **only** way the SDK ever writes that file.

    @req CFG-031
    """
    target = tmp_path / "token"
    body = '{"auth": {"client_token": "s.new", "lease_duration": 60, "renewable": true}}'
    client, _ = _client(token_file=str(target), exchanges=[_json(200, body)])

    asyncio.run(client.auth.token.create(CreateTokenRequest(use_result=True)))

    assert not target.exists()


# --------------------------------------------------------------------------------
# CFG-071: a namespace view shares the one source
# --------------------------------------------------------------------------------


def test_cfg071_a_namespace_view_shares_the_token_source_with_its_parent() -> None:
    """@req CFG-071 @req AUT-001"""
    client, _ = _client(token="s.shared")
    view = client.with_namespace("team-a")

    assert view.auth.token_source is client.auth.token_source

    view.set_token(SecretString("s.rotated-by-the-view"))

    assert client.auth.current_token is not None
    assert client.auth.current_token.reveal() == "s.rotated-by-the-view"
    assert client.auth.token_source is view.auth.token_source


def test_ovr008_the_auth_grouping_is_a_lightweight_view() -> None:
    """The first sub-API grouping, and the shape every later engine grouping copies.

   
    """
    client, _ = _client()

    assert client.auth is not client.auth  # a view, allocated per access, like `logical`
    auth = client.auth
    assert auth.token is auth.token  # within one view its children are stable
    assert isinstance(client.auth.token_info, (TokenInfo, type(None)))
    # Constructing a view reads no state and performs no resolution, which is what makes it
    # safe to allocate one per access (CFG-071).
    assert client.auth.current_token is not None


# --------------------------------------------------------------------------------
# RequestOptions still never mutates the client (CFG-061)
# --------------------------------------------------------------------------------


def test_cfg061_a_per_call_token_does_not_change_the_clients_source() -> None:
    """@req CFG-060 @req CFG-061"""
    client, transport = _client(
        token="s.client", exchanges=[_json(200, '{"data": {"id": "s.x", "creation_ttl": 0}}')]
    )

    asyncio.run(
        client.auth.token.lookup_self(RequestOptions(token=SecretString("s.per-call")))
    )

    assert transport.requests[0].headers["X-BastionVault-Token"] == "s.per-call"
    assert client.auth.current_token is not None
    assert client.auth.current_token.reveal() == "s.client"
