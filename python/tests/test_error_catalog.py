"""The generated Appendix B catalogue, message recognition, captures and enrichment.

M1c replaced three hand-transcribed catalogues with one generated from
`specifications/appendix-b-error-catalogue.md` (DR-0005 D-M1c-1). This module is the
Python side of that: it asserts the generated rows against the generator's checked-in
intermediate, the public `ErrorCatalog` surface D-M1c-7 pins, and the runtime behaviour
built on top of them -- ERR-020 recognition, ERR-035 captures, ERR-002/003 formatting,
ERR-034 path interpolation and the seven ERR-040 enrichment rows.
"""

from __future__ import annotations

import asyncio
import datetime
import json
import re
from collections.abc import Callable, Mapping
from pathlib import Path
from typing import Any

import pytest

from bastionvault_integration_sdk import (
    BastionVaultError,
    Client,
    ClientOptions,
    ErrorCatalog,
    ErrorCatalogEntry,
    ErrorCategory,
    ErrorCodes,
)
from bastionvault_integration_sdk._generated import error_catalog_data as generated
from bastionvault_integration_sdk._recognition import normalise
from bastionvault_integration_sdk.errors import (
    RETRYABLE_CODES,
    make_config_error,
    make_error,
)
from bastionvault_integration_sdk.testing import FakeTransport
from bastionvault_integration_sdk.transport import TransportResponse

from .harness.fixture_loader import FixtureLoader

_CATEGORY_BY_NAME = {
    "Configuration": ErrorCategory.CONFIGURATION,
    "Input": ErrorCategory.INPUT,
    "Transport": ErrorCategory.TRANSPORT,
    "Protocol": ErrorCategory.PROTOCOL,
    "Authentication": ErrorCategory.AUTHENTICATION,
    "Authorization": ErrorCategory.AUTHORIZATION,
    "NotFound": ErrorCategory.NOT_FOUND,
    "Conflict": ErrorCategory.CONFLICT,
    "RateLimit": ErrorCategory.RATE_LIMIT,
    "Quota": ErrorCategory.QUOTA,
    "ServerState": ErrorCategory.SERVER_STATE,
    "Discovery": ErrorCategory.DISCOVERY,
    "Engine": ErrorCategory.ENGINE,
}

_CATEGORY_BY_PREFIX = {
    "BV-CONFIG": ErrorCategory.CONFIGURATION,
    "BV-INPUT": ErrorCategory.INPUT,
    "BV-TRANSPORT": ErrorCategory.TRANSPORT,
    "BV-PROTOCOL": ErrorCategory.PROTOCOL,
    "BV-AUTH": ErrorCategory.AUTHENTICATION,
    "BV-AUTHZ": ErrorCategory.AUTHORIZATION,
    "BV-NOTFOUND": ErrorCategory.NOT_FOUND,
    "BV-CONFLICT": ErrorCategory.CONFLICT,
    "BV-RATE": ErrorCategory.RATE_LIMIT,
    "BV-QUOTA": ErrorCategory.QUOTA,
    "BV-SERVER": ErrorCategory.SERVER_STATE,
    "BV-DISCOVERY": ErrorCategory.DISCOVERY,
}

_ADDRESS = "https://vault.example.com:8200"


def _intermediate() -> dict[str, Any]:
    root: Path = FixtureLoader().repository_root
    text = (root / "tools" / "error-catalogue" / "catalogue.json").read_text(encoding="utf-8")
    payload: dict[str, Any] = json.loads(text)
    return payload


def _fail(
    status: int,
    message: str | None,
    *,
    path: str = "secret/data/app",
    headers: Mapping[str, str] | None = None,
    configure: Callable[[dict[str, Any]], None] | None = None,
) -> BastionVaultError:
    """Drive one failing `Logical.Read` through the real client and return the error."""

    body = b"" if message is None else json.dumps({"error": message}).encode("utf-8")
    transport = FakeTransport(
        exchanges=[TransportResponse(status_code=status, headers=dict(headers or {}), body=body)]
    )
    return _run(transport, path=path, configure=configure)


def _fail_transport(
    code: str, *, configure: Callable[[dict[str, Any]], None] | None = None
) -> BastionVaultError:
    transport = FakeTransport(exchanges=[make_error(code, attempts=0)])
    return _run(transport, path="secret/data/app", configure=configure)


def _run(
    transport: FakeTransport,
    *,
    path: str,
    configure: Callable[[dict[str, Any]], None] | None,
) -> BastionVaultError:
    from bastionvault_integration_sdk import RetryPolicy

    kwargs: dict[str, Any] = {"address": _ADDRESS, "retry_policy": RetryPolicy(max_attempts=1)}
    if configure is not None:
        configure(kwargs)
    client = Client(ClientOptions(**kwargs), transport=transport)
    with pytest.raises(BastionVaultError) as excinfo:
        asyncio.run(client.logical.read(path))
    return excinfo.value


# --------------------------------------------------------------------------------------
# The generated table (ERR-005, ERR-006, ERR-010, ERR-030, ERR-031, ERR-033, ERR-036/037)
# --------------------------------------------------------------------------------------


def test_catalog_matches_the_intermediate_row_for_row_and_in_appendix_order() -> None:
    """@req ERR-036 @req ERR-010"""
    rows = _intermediate()["codes"]

    assert len(ErrorCatalog.all()) == len(rows)
    for entry, row in zip(ErrorCatalog.all(), rows, strict=True):
        assert entry.code == row["code"]
        assert entry.name == row["name"]
        assert entry.category is _CATEGORY_BY_NAME[row["category"]]
        assert entry.message == row["message"]
        assert entry.hint == row["hint"]
        assert entry.retryable is row["retryable"]


def test_generated_rules_match_the_intermediate_in_appendix_order() -> None:
    """@req ERR-020"""
    rows = _intermediate()["recognition"]
    captures = _intermediate()["detailsCaptures"]

    assert len(generated.RULES) == len(rows)
    for rule, row in zip(generated.RULES, rows, strict=True):
        kind, text, contains_all, status, status_class, path_contains, code, capture = rule
        assert kind == row["kind"]
        assert text == row["text"]
        assert list(contains_all) == row["containsAll"]
        assert status == (row["guard"]["status"] if row["guard"] else None)
        assert status_class == (row["guard"]["statusClass"] if row["guard"] else None)
        assert path_contains == row["pathContains"]
        assert code == row["code"]
        assert (capture is not None) == any(
            capture_row["stem"] == text for capture_row in captures
        )


def test_generated_captures_match_the_hand_authored_table() -> None:
    """@req ERR-035"""
    rows = _intermediate()["detailsCaptures"]

    assert len(generated.CAPTURES) == len(rows)
    for capture, row in zip(generated.CAPTURES, rows, strict=True):
        kind, keys, prefix, suffix = capture
        assert kind == row["kind"]
        assert list(keys) == row["keys"]
        assert prefix == row["prefix"]
        assert suffix == row["suffix"]


def test_catalog_is_exhaustive_and_every_message_is_unique() -> None:
    """@req ERR-036 @req ERR-037"""
    messages = [entry.message for entry in ErrorCatalog.all()]

    assert len(messages) == len(set(messages))
    assert all(entry.message and entry.hint for entry in ErrorCatalog.all())


def test_retryable_is_true_for_exactly_the_ERR_006_set() -> None:
    """@req ERR-006"""
    assert {entry.code for entry in ErrorCatalog.all() if entry.retryable} == set(RETRYABLE_CODES)


def test_every_category_matches_its_code_prefix() -> None:
    """@req ERR-004"""
    for entry in ErrorCatalog.all():
        prefix = entry.code.rsplit("-", 1)[0]
        expected = _CATEGORY_BY_PREFIX.get(prefix, ErrorCategory.ENGINE)
        assert entry.category is expected, entry.code


def test_get_returns_none_for_an_unknown_code_and_never_raises() -> None:
    """@req ERR-036"""
    assert ErrorCatalog.get("BV-NOPE-999") is None
    assert ErrorCatalog.get("") is None
    entry = ErrorCatalog.get(ErrorCodes.CONFIG_INVALID_ADDRESS)
    assert entry is not None
    assert entry.code == "BV-CONFIG-001"


def test_entry_is_a_frozen_value_type() -> None:
    """@req ERR-036"""
    entry = ErrorCatalog.get(ErrorCodes.AUTHZ_PERMISSION_DENIED)
    assert isinstance(entry, ErrorCatalogEntry)
    assert entry == ErrorCatalog.get(ErrorCodes.AUTHZ_PERMISSION_DENIED)
    with pytest.raises(AttributeError):
        entry.code = "BV-OTHER-001"  # type: ignore[misc]


def test_every_code_is_reachable_as_a_constant_and_as_its_literal_string() -> None:
    """@req ERR-005"""
    constants = {
        value for name, value in vars(ErrorCodes).items()
        if not name.startswith("_") and isinstance(value, str)
    }

    assert constants == {entry.code for entry in ErrorCatalog.all()}
    assert ErrorCodes.AUTHZ_PERMISSION_DENIED == "BV-AUTHZ-001"


def test_configuration_errors_quote_the_generated_catalogue_verbatim() -> None:
    """@req ERR-036"""
    entry = ErrorCatalog.get(ErrorCodes.CONFIG_INVALID_ADDRESS)
    assert entry is not None

    error = make_config_error(ErrorCodes.CONFIG_INVALID_ADDRESS)

    assert error.message == entry.message
    assert error.hint == entry.hint
    assert error.category is entry.category
    assert error.retryable is False
    assert error.attempts == 0


def _sentence_count(text: str) -> int:
    """Sentences, ignoring anything inside backticks and decimal points.

    Counted exactly as the .NET pathfinder counts them, so the same catalogue row can
    never be two sentences in one language and three in another (CLA-003).
    """

    masked = re.sub(r"`[^`]*`", "X", text)
    masked = re.sub(r"(?<=\d)\.(?=\d)", "", masked)
    return len([part for part in re.split(r"[.!?](?:\s|$)", masked) if part.strip()])


def test_every_message_is_one_sentence_in_the_present_tense() -> None:
    """@req ERR-030"""
    for entry in ErrorCatalog.all():
        assert entry.message.endswith("."), entry.code
        assert _sentence_count(entry.message) == 1, entry.code
        assert " will " not in entry.message, entry.code
        assert not entry.message.startswith("Please"), entry.code


def test_every_hint_is_at_most_two_sentences() -> None:
    """@req ERR-031"""
    # D-M1c-13: Appendix B carried a three-sentence hint (BV-RATE-001) from M0 to M1c
    # with no gate on it. The generator now hard-fails on one; this is the same invariant
    # asserted against the compiled catalogue, counted the same way.
    for entry in ErrorCatalog.all():
        assert _sentence_count(entry.hint) <= 2, f"{entry.code}: {entry.hint}"

    dos_guard = ErrorCatalog.get(ErrorCodes.RATE_LIMITED_BY_DOS_GUARD)
    assert dos_guard is not None
    assert _sentence_count(dos_guard.hint) == 2
    assert _sentence_count("Use `Sys.Batch` or `*-info` pages.") == 1
    assert _sentence_count("Do this. Then do that.") == 2


def test_no_hint_offers_disabling_a_security_control_as_its_first_suggestion() -> None:
    """@req ERR-033"""
    unsafe_knobs = ("TlsSkipVerify", "AllowInsecureHttp", "InsecureSkipVerify")

    for entry in ErrorCatalog.all():
        for knob in unsafe_knobs:
            index = entry.hint.find(knob)
            if index < 0:
                continue
            # It may appear, but never first, and never without an explicit warning.
            assert _sentence_count(entry.hint[:index]) >= 1, f"{entry.code}: {knob} is first"
            lowered = entry.hint.casefold()
            assert (
                "never in production" in lowered
                or "only for isolated test networks" in lowered
                or "diagnostic" in lowered
            ), f"{entry.code}: {knob} is mentioned without a warning"


def test_every_canonical_ERR_001_field_is_present_on_the_error_type() -> None:
    """@req ERR-001"""
    error = _fail(500, "boom")

    for field_name in (
        "code", "category", "message", "hint", "server_message", "server_errors",
        "status_code", "retry_after", "retryable", "method", "path", "address",
        "attempts", "details", "cause", "timestamp",
    ):
        assert hasattr(error, field_name), field_name


# --------------------------------------------------------------------------------------
# Message recognition (ERR-020, D-M1c-3)
# --------------------------------------------------------------------------------------


@pytest.mark.parametrize(
    ("message", "expected"),
    [
        ("  Permission denied.  ", ErrorCodes.AUTHZ_PERMISSION_DENIED),
        ("PERMISSION DENIED", ErrorCodes.AUTHZ_PERMISSION_DENIED),
        ("Account temporarily locked (retry after 300s)", ErrorCodes.AUTH_ACCOUNT_LOCKED),
        ("Account temporarily locked (retry after 300s).", ErrorCodes.AUTH_ACCOUNT_LOCKED),
    ],
)
def test_normalisation_trims_strips_the_stop_the_retry_suffix_and_the_case(
    message: str, expected: str
) -> None:
    """@req ERR-020"""
    assert _fail(400, message).code == expected


@pytest.mark.parametrize("message", ["", "   ", ".", "something the catalogue has never heard of"])
def test_recognition_falls_through_to_the_status_table_when_no_rule_matches(message: str) -> None:
    """@req ERR-020"""
    assert _fail(400, message).code == ErrorCodes.INPUT_SERVER_REJECTED_REQUEST


def test_normalise_is_matching_only_and_leaves_the_server_message_alone() -> None:
    """@req ERR-020"""
    assert normalise("  Permission Denied. ") == "permission denied"
    assert normalise("Account temporarily locked (Retry After 30s).") == "account temporarily locked"

    error = _fail(403, "Permission denied.")
    assert error.server_message == "Permission denied."


def test_unrecognised_messages_reach_each_status_branch() -> None:
    """@req ERR-020"""
    assert _fail(401, "authentication required by this listener").code == ErrorCodes.AUTH_UNAUTHENTICATED
    assert _fail(403, "forbidden by the listener").code == ErrorCodes.AUTHZ_PERMISSION_DENIED
    assert _fail(404, "nothing here").code == ErrorCodes.NOT_FOUND_PATH_NOT_FOUND
    assert _fail(405, "method rejected").code == ErrorCodes.PROTOCOL_METHOD_NOT_ALLOWED
    # D-M1c-12: 400 and every other unmapped 4xx, not just 400.
    assert _fail(400, "unexplained").code == ErrorCodes.INPUT_SERVER_REJECTED_REQUEST
    assert _fail(418, "unexplained teapot").code == ErrorCodes.INPUT_SERVER_REJECTED_REQUEST
    assert _fail(500, "unexplained").code == ErrorCodes.SERVER_INTERNAL_ERROR
    assert _fail(507, "out of capacity").code == ErrorCodes.QUOTA_NAMESPACE_QUOTA_EXCEEDED


def test_status_guards_keep_a_5xx_only_rule_off_a_4xx() -> None:
    """@req ERR-020"""
    # `exact bastionvault is sealed` / `contains (5xx) is sealed`.
    assert _fail(503, "The vault is sealed right now.").code == ErrorCodes.SERVER_SEALED
    assert _fail(400, "The vault is sealed right now.").code == ErrorCodes.INPUT_SERVER_REJECTED_REQUEST


def test_an_unrecognised_503_lands_on_the_generic_unavailable_code() -> None:
    """@req ERR-020"""
    # D-M1c-23: `04-error-model.md` step 5 says 503 => BV-SERVER-002 flatly. Only the two
    # Appendix B section 2 rows produce BV-SERVER-001, and both need `is sealed`.
    assert _fail(503, None).code == ErrorCodes.SERVER_UNAVAILABLE
    assert _fail(503, "cluster node is unhealthy").code == ErrorCodes.SERVER_UNAVAILABLE
    # The word `sealed` alone is not a recognition row and no longer a status heuristic.
    assert _fail(503, "the seal was sealed by an operator yesterday").code == ErrorCodes.SERVER_UNAVAILABLE
    assert _fail(502, "bad gateway").code == ErrorCodes.SERVER_UNAVAILABLE
    assert _fail(504, "gateway timeout").code == ErrorCodes.SERVER_UNAVAILABLE


def test_the_409_recordings_rule_is_scoped_to_a_recordings_path() -> None:
    """@req ERR-020"""
    # `contains (409, recordings) sha256 / digest` is a path-guarded step-4 rule.
    assert (
        _fail(409, "sha256 mismatch", path="rustion/recordings/a/chunk/0").code
        == ErrorCodes.CONFLICT_RECORDING_DIGEST_MISMATCH
    )
    # Outside a recordings path the guard fails and the message is not recognised, so the
    # request falls through to step 5, which D-M1c-19 fixes at BV-CONFLICT-001. There is
    # no status-table discriminator left to guess at the digest case.
    assert _fail(409, "digest mismatch on upload", path="rustion/blobs/a").code == ErrorCodes.CONFLICT
    # `brokered_resource_no_static_credential` is recognised at step 4 regardless of path.
    assert (
        _fail(409, "brokered_resource_no_static_credential", path="ssh/resources/a").code
        == ErrorCodes.CONFLICT_BROKERED_RESOURCE_STATIC_CREDENTIAL
    )


def test_an_unrecognised_409_lands_on_the_generic_conflict_code() -> None:
    """@req ERR-020"""
    # D-M1c-19: `04-error-model.md` step 5 names BV-CONFLICT-001 for 409.
    assert _fail(409, None, path="ssh/resources/a").code == ErrorCodes.CONFLICT
    assert _fail(409, "some conflict the catalogue has never heard of").code == ErrorCodes.CONFLICT
    assert _fail(409, "brokered resource cannot take a static credential").code == ErrorCodes.CONFLICT


def test_prefix_rules_keep_the_appendix_trailing_space_and_compound_rules_need_both_parts() -> None:
    """@req ERR-020"""
    # `prefix key ` + contains `already exists with type`. The trailing space in
    # Appendix B's literal is load-bearing: it stops this rule swallowing `key_name`.
    assert (
        _fail(400, "Key app already exists with type aes256-gcm96").code
        == ErrorCodes.TRANSIT_KEY_TYPE_CONFLICT
    )
    assert (
        _fail(400, "key_name already exists with type aes256-gcm96").code
        == ErrorCodes.INPUT_SERVER_REJECTED_REQUEST
    )
    assert _fail(400, "Key app is fine").code == ErrorCodes.INPUT_SERVER_REJECTED_REQUEST


# --------------------------------------------------------------------------------------
# Details captures (ERR-035, D-M1c-4)
# --------------------------------------------------------------------------------------


def test_every_details_capture_shape_extracts_what_D_M1c_4_pins() -> None:
    """@req ERR-035"""
    policy = _fail(403, "Cannot assign policy app-admin: not granted.")
    assert policy.code == ErrorCodes.AUTHZ_PERMISSION_DENIED
    assert policy.details["policy"] == "app-admin"

    locked = _fail(400, "Account temporarily locked (retry after 300s).")
    assert locked.code == ErrorCodes.AUTH_ACCOUNT_LOCKED
    assert locked.details["retry_after_secs"] == 300

    source = _fail(403, "Source address 203.0.113.17 is unauthorized.")
    assert source.code == ErrorCodes.AUTHZ_PERMISSION_DENIED
    assert source.details["source_ip"] == "203.0.113.17"

    batch = _fail(400, "Batch has 200 operations, exceeds max 128.")
    assert batch.code == ErrorCodes.INPUT_BATCH_TOO_LARGE
    assert (batch.details["count"], batch.details["max"]) == (200, 128)

    meta = _fail(400, "Meta key(s) username, spiffe_id are reserved.")
    assert meta.code == ErrorCodes.INPUT_RESERVED_TOKEN_META_KEY
    assert meta.details["keys"] == ("username", "spiffe_id")

    missing = _fail(404, "No such namespace dti/esi.")
    assert missing.code == ErrorCodes.NOT_FOUND_NAMESPACE_NOT_FOUND
    assert missing.details["namespace"] == "dti/esi"

    named = _fail(404, "No policy named app-read.")
    assert named.code == ErrorCodes.NOT_FOUND_POLICY_NOT_FOUND
    assert named.details["policy"] == "app-read"


@pytest.mark.parametrize(
    ("status", "message", "code", "key"),
    [
        (400, "Account temporarily locked", ErrorCodes.AUTH_ACCOUNT_LOCKED, "retry_after_secs"),
        (400, "Batch has many operations, exceeds max quota", ErrorCodes.INPUT_BATCH_TOO_LARGE, "count"),
        (400, "Meta key(s)  are reserved", ErrorCodes.INPUT_RESERVED_TOKEN_META_KEY, "keys"),
        (404, "No policy named ", ErrorCodes.NOT_FOUND_POLICY_NOT_FOUND, "policy"),
    ],
)
def test_a_capture_that_cannot_match_is_not_an_error_the_code_still_lands(
    status: int, message: str, code: str, key: str
) -> None:
    """@req ERR-035"""
    # D-M1c-4: recognition must never be more fragile than the code it produces.
    error = _fail(status, message)

    assert error.code == code
    assert key not in error.details


# --------------------------------------------------------------------------------------
# ERR-002 / ERR-003 formatting and redaction
# --------------------------------------------------------------------------------------


@pytest.mark.parametrize(
    ("raw", "expected"),
    [
        ("/auth/token/lookup/s.secret", "/auth/token/lookup/<redacted>"),
        ("/auth/token/renew/s.secret", "/auth/token/renew/<redacted>"),
        ("/auth/token/revoke/s.secret", "/auth/token/revoke/<redacted>"),
        ("/auth/token/revoke-orphan/s.secret", "/auth/token/revoke-orphan/<redacted>"),
        ("/secret/data/app", "/secret/data/app"),
        ("lookup", "lookup"),
        ("/auth/token/lookup/", "/auth/token/lookup/"),
        ("", ""),
    ],
)
def test_path_redaction_replaces_only_the_token_segments(raw: str, expected: str) -> None:
    """@req ERR-003"""
    assert make_error(ErrorCodes.SERVER_INTERNAL_ERROR, path=raw).path == expected


def test_a_path_free_error_stays_path_free() -> None:
    """@req ERR-003"""
    assert make_error(ErrorCodes.SERVER_INTERNAL_ERROR).path is None


def test_one_line_form_has_no_newline_even_when_the_server_message_carries_one() -> None:
    """@req ERR-002 @req ERR-003"""
    fake_token = "s." + "FAKEtoken" + "0" * 16
    error = make_error(
        ErrorCodes.SERVER_INTERNAL_ERROR,
        status_code=500,
        method="GET",
        path=f"/auth/token/lookup/{fake_token}",
        server_message="line one\nline two\r\nline three",
    )

    line = str(error)

    assert "\n" not in line
    assert "\r" not in line
    assert fake_token not in line
    assert line.startswith("BV-SERVER-005: ")
    assert " — " in line
    assert "[HTTP 500 GET /auth/token/lookup/<redacted>]" in line
    assert '(server: "line one line two line three")' in line


def test_one_line_form_omits_the_bracket_and_the_server_clause_when_there_is_nothing_to_show() -> None:
    """@req ERR-002"""
    entry = ErrorCatalog.get(ErrorCodes.CONFIG_INVALID_ADDRESS)
    assert entry is not None

    error = make_config_error(ErrorCodes.CONFIG_INVALID_ADDRESS)

    assert str(error) == f"{entry.code}: {entry.message} — {entry.hint}"


# --------------------------------------------------------------------------------------
# ERR-034 interpolation and the seven ERR-040 enrichment rows
# --------------------------------------------------------------------------------------


def test_a_hint_that_points_at_details_path_names_the_path_the_sdk_sent() -> None:
    """@req ERR-034"""
    not_found = _fail(404, "unmapped", path="secret/app")

    assert not_found.code == ErrorCodes.NOT_FOUND_PATH_NOT_FOUND
    assert "Details.path" in not_found.hint
    assert "The path as sent was `secret/app`." in not_found.hint
    assert not_found.details["path"] == "secret/app"

    # A hint that does not mention Details.path is left alone.
    sealed = _fail(503, "BastionVault is sealed.", path="secret/app")
    assert sealed.code == ErrorCodes.SERVER_SEALED
    assert "The path as sent was" not in sealed.hint


def test_enrichment_row_403_no_namespace_fires_only_under_auth_or_secret_with_no_namespace() -> None:
    """@req ERR-021 @req ERR-040"""
    note = "No namespace is set"

    assert note in _fail(403, "Permission denied.", path="secret/data/x").hint
    assert note in _fail(403, "Permission denied.", path="auth/userpass/users/bob").hint
    assert note not in _fail(403, "Permission denied.", path="sys/health").hint
    assert note not in _fail(
        403, "Permission denied.", path="secret/data/x",
        configure=lambda kwargs: kwargs.update(namespace="dti"),
    ).hint
    # ERR-021: a 403 is authorization, never authentication.
    assert _fail(403, "Permission denied.", path="secret/data/x").code == ErrorCodes.AUTHZ_PERMISSION_DENIED


def test_enrichment_rows_for_api_version_rate_gate_sealed_and_unsupported_path() -> None:
    """@req ERR-040 @req CNF-043"""
    assert "Pin this call to `/v2` (RequestOptions.ApiVersion = 2)." in _fail(
        400, "API version mismatch: not on this version."
    ).hint

    throttled = _fail(
        429, "request temporarily blocked by DoS protection", headers={"Retry-After": "17"}
    )
    assert throttled.code == ErrorCodes.RATE_LIMITED_BY_DOS_GUARD
    assert "paused for `17`s" in throttled.hint

    # A 429 without Retry-After gets no rate-gate note.
    assert "paused for" not in _fail(429, "too many requests").hint

    assert (
        "Run `bvault operator unseal` on the node or wait for auto-unseal; the SDK will not retry."
        in _fail(503, "BastionVault is sealed.").hint
    )
    assert "bvault operator unseal" not in _fail(503, "cluster node is unhealthy").hint

    # CNF-043 / D-M1c-6: one recognition row, no bespoke code path.
    unsupported = _fail(500, "Logical backend path not supported.")
    assert unsupported.code == ErrorCodes.SERVER_UNSUPPORTED_BY_SERVER
    assert "does not have this endpoint" in unsupported.hint


def test_enrichment_rows_for_tls_without_a_ca_and_connection_refused_to_the_default_address() -> None:
    """@req ERR-040"""
    tls_note = "Provide the server's CA bundle via `CaCertPath` or `BASTIONVAULT_CACERT`."
    address_note = "No `Address` was configured; the default is `https://127.0.0.1:8200`."

    assert tls_note in _fail_transport(ErrorCodes.TRANSPORT_TLS_ERROR).hint
    assert tls_note not in _fail_transport(
        ErrorCodes.TRANSPORT_TLS_ERROR,
        configure=lambda kwargs: kwargs.update(ca_cert_pem=_self_signed_ca_pem()),
    ).hint

    assert address_note in _fail_transport(
        ErrorCodes.TRANSPORT_CONNECTION_FAILED,
        configure=lambda kwargs: kwargs.update(address="https://127.0.0.1:8200"),
    ).hint
    assert address_note not in _fail_transport(ErrorCodes.TRANSPORT_CONNECTION_FAILED).hint


def test_the_two_deferred_enrichment_rows_are_absent_not_stubbed() -> None:
    """@req ERR-040"""
    # D-M1c-5: no branch exists for `Details.namespace_operable == false` (M3) or for a
    # 404 on a KV v2 mount (M4), so neither can fire early and neither is dead code the
    # CNF-010 floor would have to excuse.
    not_found = _fail(404, "unmapped", path="secret/app")

    assert "This mount is KV v2" not in not_found.hint
    assert "child-visible" not in not_found.hint


def _self_signed_ca_pem() -> str:
    """A throwaway CA so `HasCaCertificate` is true without reaching the network."""

    from cryptography import x509
    from cryptography.hazmat.primitives import hashes, serialization
    from cryptography.hazmat.primitives.asymmetric import rsa
    from cryptography.x509.oid import NameOID

    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    name = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, "bastionvault-test-ca")])
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
    return certificate.public_bytes(serialization.Encoding.PEM).decode("ascii")


# --------------------------------------------------------------------------------------
# ERR-050 warnings
# --------------------------------------------------------------------------------------


class _RecordingLogger:
    """The CNF-030 logger seam, capturing what ERR-050 emits."""

    def __init__(self) -> None:
        self.warnings: list[str] = []

    def warning(self, message: str) -> None:
        self.warnings.append(message)


def _read_with_warnings(body: bytes) -> tuple[Any, _RecordingLogger]:
    logger = _RecordingLogger()
    transport = FakeTransport(exchanges=[TransportResponse(status_code=200, headers={}, body=body)])
    client = Client(ClientOptions(address=_ADDRESS, logger=logger), transport=transport)
    return asyncio.run(client.logical.read("secret/data/app")), logger


def test_server_warnings_are_surfaced_and_logged_at_warning_level() -> None:
    """@req ERR-050"""
    response, logger = _read_with_warnings(
        b'{"data": {"a": 1}, "warnings": ["policy is deprecated", "mount is read-only"]}'
    )

    assert response is not None
    assert response.warnings == ("policy is deprecated", "mount is read-only")
    assert logger.warnings == [
        "BastionVault server warning: policy is deprecated",
        "BastionVault server warning: mount is read-only",
    ]


def test_a_warning_is_never_turned_into_an_error() -> None:
    """@req ERR-050"""
    response, _ = _read_with_warnings(b'{"data": {"a": 1}, "warnings": ["heads up"]}')

    assert response is not None
    assert response.data == {"a": 1}


def test_warnings_default_to_an_empty_list_and_a_non_array_is_ignored() -> None:
    """@req ERR-050"""
    plain, plain_logger = _read_with_warnings(b'{"data": {"a": 1}}')
    assert plain is not None
    assert plain.warnings == ()
    assert plain_logger.warnings == []

    odd, odd_logger = _read_with_warnings(b'{"data": {"a": 1}, "warnings": "not an array"}')
    assert odd is not None
    assert odd.warnings == ()
    assert odd_logger.warnings == []


def test_a_non_string_warning_is_surfaced_as_its_raw_json() -> None:
    """@req ERR-050"""
    response, logger = _read_with_warnings(b'{"data": {"a": 1}, "warnings": [{"note": 1}]}')

    assert response is not None
    assert response.warnings == ('{"note": 1}',)
    assert logger.warnings == ['BastionVault server warning: {"note": 1}']


def test_warnings_are_surfaced_on_a_body_without_a_data_member() -> None:
    """@req ERR-050"""
    response, logger = _read_with_warnings(b'{"warnings": ["standalone"]}')

    assert response is not None
    assert response.warnings == ("standalone",)
    assert logger.warnings == ["BastionVault server warning: standalone"]
