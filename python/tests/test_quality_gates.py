"""Tests for committed Python quality-gate configuration."""

from pathlib import Path


def test_quality_gates_are_committed_and_library_has_no_exclusion_pragma() -> None:
    """@req CNF-020 @req CNF-022 @req CNF-023 @req TST-030 @req TST-031 @req TST-040"""
    python_root = Path(__file__).parents[1]
    project = (python_root / "pyproject.toml").read_text(encoding="utf-8")
    library_files = (python_root / "src").rglob("*.py")

    assert 'strict = true' in project
    assert 'select = ["E", "F", "I"]' in project
    assert '"--cov-fail-under=95"' in project
    assert '"--cov-branch"' in project
    assert '"--cov-report=html:coverage/html"' in project
    assert '"--cov-report=xml:coverage.xml"' in project
    assert all("pragma: no cover" not in path.read_text(encoding="utf-8") for path in library_files)

