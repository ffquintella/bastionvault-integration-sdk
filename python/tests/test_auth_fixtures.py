"""Runs the M2a `specifications/fixtures/auth/*.json` and `errors.format.one-line` fixtures.

Registers the `Auth.Token.*` operations in the fixture driver's registry. Every handler
drives the *same* `FakeTransport` the driver built from the fixture's raw exchanges
(D-M1b-15) against the real `Client`/`AuthOperations` code, through the same retry loop the
logical layer uses (D-M2-4).

`auth.token.lookup-self-no-token-client-side` is held **pending** through M2a with the
reason D-M2-10 gives it: its operation `Auth.Token.LookupSelf` lands here, so by D-M2-10's
own rule it stops being pending here -- one slice *before* the `CFG-020`/`ERR-022` preflight
it asserts, which is M2b's. Left to run it would go red at M2a's handback, read as a
regression, and the cheapest-looking fix would be to weaken it (CLA-004).
"""

from __future__ import annotations

from collections.abc import Mapping
from datetime import timedelta
from typing import Any

import pytest

from bastionvault_integration_sdk import Client
from bastionvault_integration_sdk.auth import CreateTokenRequest, TokenInfo
from bastionvault_integration_sdk.errors import BastionVaultError
from bastionvault_integration_sdk.logical import AuthInfo
from bastionvault_integration_sdk.testing import FakeTransport

from .harness.fixture_client import (
    error_to_operation_error,
    iso8601_duration,
    make_client,
)
from .harness.fixture_driver import (
    ClientConfiguration,
    FixtureDriver,
    OperationRegistry,
    RedactedValue,
)
from .harness.fixture_loader import FixtureLoader

#: The six M2a fixtures that go green, and the seventh that is deliberately held.
_GREEN_FIXTURE_IDS = (
    "auth.token.create-reserved-meta-client-side",
    "auth.token.lookup-self-remaining-ttl",
    "auth.token.lookup-unknown-404",
    "auth.token.renew-self-uses-renew-path",
    "auth.token.revoke-self-clears-token",
    "errors.format.one-line",
)

#: D-M2-10's pending entry, with the reason it records verbatim.
_PENDING_FIXTURE_ID = "auth.token.lookup-self-no-token-client-side"
_PENDING_REASON = "operation exists; asserted behaviour is M2b"


def _token_info_to_dict(info: TokenInfo) -> dict[str, Any]:
    return {
        # AUT-004/CNF-031: `TokenInfo.id` is a `SecretString`, so it is compared as a
        # redacted value and never as a plain string (D-M2-12).
        "Id": RedactedValue(info.id.reveal()) if info.id is not None else None,
        "Policies": list(info.policies),
        "Path": info.path,
        "Meta": dict(info.meta) if info.meta is not None else None,
        "DisplayName": info.display_name,
        "NumUses": info.num_uses,
        "CreationTtl": iso8601_duration(info.creation_ttl),
        "ExplicitMaxTtl": iso8601_duration(info.explicit_max_ttl),
        "Period": iso8601_duration(info.period) if info.period is not None else None,
        "RemainingTtl": (
            iso8601_duration(info.remaining_ttl) if info.remaining_ttl is not None else None
        ),
    }


def _auth_info_to_dict(auth: AuthInfo) -> dict[str, Any]:
    return {
        "ClientToken": RedactedValue(auth.client_token.reveal()),
        "Policies": list(auth.policies),
        "Metadata": dict(auth.metadata),
        "LeaseDuration": int(auth.lease_duration.total_seconds()),
        "Renewable": auth.renewable,
    }


def _current_token_state(client: Client) -> dict[str, Any]:
    token = client.auth.current_token
    return {"Auth.CurrentToken": None if token is None else RedactedValue(token.reveal())}


def _create_request_from_args(raw: Mapping[str, Any]) -> CreateTokenRequest:
    """Map the fixture's canonical `CreateTokenRequest` field names onto the Python idiom."""
    request = CreateTokenRequest()
    if "Policies" in raw:
        request.policies = list(raw["Policies"])
    if "Ttl" in raw:
        request.ttl = timedelta(seconds=int(raw["Ttl"]))
    if "Period" in raw:
        request.period = timedelta(seconds=int(raw["Period"]))
    if "NumUses" in raw:
        request.num_uses = int(raw["NumUses"])
    if "Renewable" in raw:
        request.renewable = bool(raw["Renewable"])
    if "Meta" in raw:
        request.meta = dict(raw["Meta"])
    if "DisplayName" in raw:
        request.display_name = str(raw["DisplayName"])
    if "ExplicitMaxTtl" in raw:
        request.explicit_max_ttl = timedelta(seconds=int(raw["ExplicitMaxTtl"]))
    if "NoDefaultPolicy" in raw:
        request.no_default_policy = bool(raw["NoDefaultPolicy"])
    if "NoParent" in raw:
        request.no_parent = bool(raw["NoParent"])
    if "Id" in raw:
        request.id = str(raw["Id"])
    if "Type" in raw:
        request.type = str(raw["Type"])
    if "ChildVisible" in raw:
        request.child_visible = bool(raw["ChildVisible"])
    if "UseResult" in raw:
        request.use_result = bool(raw["UseResult"])
    return request


async def _run_lookup_self(
    configuration: ClientConfiguration, transport: FakeTransport, operation: Mapping[str, Any]
) -> Any:
    del operation
    client = make_client(configuration, transport)
    try:
        return _token_info_to_dict(await client.auth.token.lookup_self())
    except BastionVaultError as error:
        raise error_to_operation_error(
            error, client_state=_current_token_state(client)
        ) from error


async def _run_lookup(
    configuration: ClientConfiguration, transport: FakeTransport, operation: Mapping[str, Any]
) -> Any:
    client = make_client(configuration, transport)
    args = operation.get("args", {})
    try:
        return _token_info_to_dict(await client.auth.token.lookup(str(args["token"])))
    except BastionVaultError as error:
        raise error_to_operation_error(
            error, client_state=_current_token_state(client)
        ) from error


async def _run_renew_self(
    configuration: ClientConfiguration, transport: FakeTransport, operation: Mapping[str, Any]
) -> Any:
    client = make_client(configuration, transport)
    args = operation.get("args", {})
    try:
        return _auth_info_to_dict(await client.auth.token.renew_self(int(args["increment"])))
    except BastionVaultError as error:
        raise error_to_operation_error(
            error, client_state=_current_token_state(client)
        ) from error


async def _run_create(
    configuration: ClientConfiguration, transport: FakeTransport, operation: Mapping[str, Any]
) -> Any:
    client = make_client(configuration, transport)
    args = operation.get("args", {})
    raw = args.get("request", {})
    request = _create_request_from_args(raw if isinstance(raw, Mapping) else {})
    try:
        return _auth_info_to_dict(await client.auth.token.create(request))
    except BastionVaultError as error:
        raise error_to_operation_error(
            error, client_state=_current_token_state(client)
        ) from error


async def _run_revoke_self(
    configuration: ClientConfiguration, transport: FakeTransport, operation: Mapping[str, Any]
) -> Any:
    del operation
    client = make_client(configuration, transport)
    try:
        await client.auth.token.revoke_self()
    except BastionVaultError as error:
        raise error_to_operation_error(
            error, client_state=_current_token_state(client)
        ) from error
    # AUT-083's whole observable is the cleared token, so the operation returns no result
    # of its own and the assertion lives in `clientState`.
    return {"clientState": _current_token_state(client)}


def build_registry() -> OperationRegistry:
    """The `Auth.*` operations M2a registers. Shared with the instrument-proof tests."""
    registry = OperationRegistry()
    registry.register("Auth.Token.LookupSelf", _run_lookup_self)
    registry.register("Auth.Token.Lookup", _run_lookup)
    registry.register("Auth.Token.RenewSelf", _run_renew_self)
    registry.register("Auth.Token.Create", _run_create)
    registry.register("Auth.Token.RevokeSelf", _run_revoke_self)
    return registry


@pytest.mark.parametrize("fixture_id", _GREEN_FIXTURE_IDS)
def test_auth_fixture_passes(fixture_id: str) -> None:
    """@req AUT-014 @req AUT-080 @req AUT-081 @req AUT-083 @req AUT-084 @req CFG-070
    @req ERR-002 @req ERR-003"""
    fixture = FixtureLoader().load_fixture(fixture_id)

    result = FixtureDriver(build_registry()).run(fixture)

    assert result.status == "passed"


def test_lookup_self_no_token_fixture_is_pending_with_its_recorded_reason() -> None:
    """D-M2-10: the operation lands at M2a, the behaviour it asserts lands at M2b.

    The fixture is *not* edited and *not* weakened (FIX-012, CLA-004); it is declared
    pending with the reason the decision record states, and M2b's exit removes this test
    along with the entry.

   
    """
    fixture = FixtureLoader().load_fixture(_PENDING_FIXTURE_ID)

    assert _PENDING_REASON == "operation exists; asserted behaviour is M2b"
    assert fixture["operation"]["name"] == "Auth.Token.LookupSelf"
    # The operation *is* registered, which is exactly why D-M2-10 had to rule on it: the
    # driver would otherwise report it pending for the wrong reason ("not registered") and
    # the entry would disappear silently when M2b wired the operation.
    assert build_registry().resolve("Auth.Token.LookupSelf") is not None


def test_every_auth_fixture_is_either_green_or_recorded_as_pending() -> None:
    """Guards against a new fixture landing under auth/ without being wired or deferred."""
    loader = FixtureLoader()
    auth_fixtures = {
        fixture["id"]
        for fixture in loader.enumerate_fixtures()
        if fixture["id"].startswith("auth.")
    }
    handled = set(_GREEN_FIXTURE_IDS) | {_PENDING_FIXTURE_ID}
    # M2b (Userpass, AppID), M2c (auto-renew) and M6 (Certificate, FerroGate) own the rest;
    # each is pending because its *operation* does not exist yet, which is D-M2-10's rule
    # for booking a deferral. The auto-renew and FerroGate fixtures landed in .NET after
    # this parity pass was parked for Stage 2.
    deferred_to_later_milestones = {
        fixture_id
        for fixture_id in auth_fixtures - handled
        if fixture_id.startswith(
            (
                "auth.userpass.",
                "auth.appid.",
                "auth.cert.",
                "auth.ferrogate.",
                "auth.autorenew.",
            )
        )
    }
    assert auth_fixtures - handled == deferred_to_later_milestones
