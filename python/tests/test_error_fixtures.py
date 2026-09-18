"""Runs every `errors.*` fixture (Appendix C) through the real `Client`/`Logical` code.

The generated `errors.recognition.*` set (one per Appendix B section 2 rule, D-M1c-10),
the five hand-authored `errors.enrichment.*` fixtures, and the four `errors.recognition.*`
fixtures that predate the generator. The operation registry is the same one
`test_transport_fixtures.py` builds, so these fixtures exercise the shipped code path and
not a second harness.
"""

from __future__ import annotations

import pytest

from .harness.fixture_driver import FixtureDriver
from .harness.fixture_loader import FixtureLoader
from .test_transport_fixtures import _build_registry

# The three fixtures that stay `pending` after M1c, each with a named owning milestone.
# They are listed, not deleted or edited to fit (D-M1c-10, D-M1c-14 item 10, CLA-004):
# the driver reports them pending because the typed operation they drive is not
# registered yet.
_PENDING = frozenset(
    {
        # Drives Auth.Token.Lookup -- M2 (D-M1c-10).
        "errors.format.one-line",
        # Needs the Sys.ListMounts cache to know the mount is KV v2 -- M4 (D-M1c-5).
        "errors.enrichment.404-kv2-hint",
        # ERR-022 is a typed-layer guard and the fixture drives Kv.V2.ReadSecret -- M2.
        "errors.recognition.missing-token-client-side",
    }
)


def _error_fixture_ids() -> list[str]:
    return sorted(
        fixture["id"]
        for fixture in FixtureLoader().enumerate_fixtures()
        if str(fixture["id"]).startswith("errors.")
    )


@pytest.mark.parametrize("fixture_id", _error_fixture_ids())
def test_error_fixture_passes_against_real_sdk_code(fixture_id: str) -> None:
    """@req ERR-020 @req ERR-021 @req ERR-035 @req ERR-040 @req CNF-043"""
    fixture = FixtureLoader().load_fixture(fixture_id)

    result = FixtureDriver(_build_registry()).run(fixture)

    expected = "pending" if fixture_id in _PENDING else "passed"
    assert result.status == expected


def test_every_recognition_rule_has_a_fixture_and_only_three_stay_pending() -> None:
    """@req FIX-001"""
    ids = _error_fixture_ids()

    # 130 generated + 4 hand-authored recognition + 5 enrichment + 1 format = 140.
    # 124 -> 130 is M8a's R-23 fix (D-M8-3): a rule with a qualifier group now gets one
    # fixture per alternative, because a message carrying every alternative at once passed
    # under the conjunctive reading the fix removed.
    assert len(ids) == 140
    assert len([id_ for id_ in ids if id_.startswith("errors.recognition.bv-")]) == 130
    assert len([id_ for id_ in ids if id_.startswith("errors.enrichment.")]) == 5
    assert _PENDING.issubset(ids)
