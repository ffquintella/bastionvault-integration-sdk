"""Tests for repository fixture discovery and schema validation."""

from pathlib import Path

import pytest

from .harness.fixture_driver import FixtureDriver, OperationRegistry
from .harness.fixture_loader import FixtureLoader, FixtureRepositoryNotFoundError, FixtureValidationError


def test_all_repository_fixtures_validate() -> None:
    """@req FIX-001 @req TST-010 @req TST-012 @req TST-013"""
    # Corpus grew 218 -> 224 (M5's six resilience.*), -> 226 (M6's two
    # auth.ferrogate.*), -> 228 (D-M7-9's sys.mount.204, sys.unseal.invalid-key),
    # -> 230 (D-M7-24's sys.policies.acl-read, sys.namespaces.write-full-replace),
    # -> 236 (M8a's six generated errors.recognition.*, one per qualifier alternative,
    # R-23 / D-M8-3), -> 238 (M8c's two newly authored totp.*: totp.generate-mode-create,
    # totp.validate-false), -> 241 (DR-0013 slice b's three newly authored transit.*:
    # transit.encrypt-decrypt, transit.below-min-decryption, transit.random-cap),
    # -> 245 (DR-0013 slice d's three efficiency.* plus kv.read-many-fallback-on-unsupported),
    # -> 247 (slice e's two newly authored efficiency.*: efficiency.pagination.cursor-passthrough,
    # efficiency.cache-version.topics-limit), -> 250 (M9 slice a's three newly authored pki.*:
    # pki.issue, pki.certs-info-page, pki.role-not-found), -> 253 (M9 slice d's three newly
    # authored ssh.*: ssh.sign, ssh.creds-ip-not-allowed, ssh.verify-invalid-otp;
    # sshbroker.effective-v2-pinned was already on disk and already counted).
    loader = FixtureLoader()

    fixtures = loader.enumerate_fixtures()

    assert len(fixtures) == 253
    assert all(fixture["id"] for fixture in fixtures)


def test_fixture_filters_and_load_by_id() -> None:
    """@req TST-010 @req TST-012"""
    loader = FixtureLoader()

    core = loader.filter_fixtures(level="core")
    section_03 = loader.filter_fixtures(sections={"03"})
    loaded = loader.load_fixture("transport.method.list-verb")

    assert core
    assert all(fixture["level"] == "core" for fixture in core)
    assert section_03
    assert all("03" in fixture["sections"] for fixture in section_03)
    assert loaded["id"] == "transport.method.list-verb"


def test_fixture_root_failure_is_clear(tmp_path: Path) -> None:
    """@req TST-010 @req TST-013"""
    with pytest.raises(FixtureRepositoryNotFoundError, match="specifications/fixtures/schema"):
        FixtureLoader(start_path=tmp_path)


def test_invalid_fixture_names_id_and_constraint() -> None:
    """@req FIX-001 @req TST-013 @req TST-040"""
    loader = FixtureLoader()

    with pytest.raises(FixtureValidationError, match="synthetic.invalid.*required property"):
        loader.validate_fixture({"id": "synthetic.invalid"})


def test_all_repository_fixtures_are_pending_until_operations_register() -> None:
    """@req TST-010 @req TST-011 @req TST-013 @req TST-040"""
    # See count rationale above: corpus is currently 253 fixtures.
    results = FixtureDriver(OperationRegistry()).run_all(FixtureLoader().enumerate_fixtures())

    assert len(results) == 253
    assert all(result.status == "pending" for result in results)
