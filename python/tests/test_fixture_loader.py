"""Tests for repository fixture discovery and schema validation."""

from pathlib import Path

import pytest

from .harness.fixture_driver import FixtureDriver, OperationRegistry
from .harness.fixture_loader import FixtureLoader, FixtureRepositoryNotFoundError, FixtureValidationError


def test_all_repository_fixtures_validate() -> None:
    """@req FIX-001 @req TST-010 @req TST-012 @req TST-013"""
    loader = FixtureLoader()

    fixtures = loader.enumerate_fixtures()

    assert len(fixtures) == 210
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
    results = FixtureDriver(OperationRegistry()).run_all(FixtureLoader().enumerate_fixtures())

    assert len(results) == 210
    assert all(result.status == "pending" for result in results)
