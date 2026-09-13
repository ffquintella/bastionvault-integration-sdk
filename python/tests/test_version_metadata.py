"""Tests for the M0 public surface: specification and package version functions."""

from pathlib import Path

from bastionvault_integration_sdk import sdk_version, specification_version


def test_version_functions_match_specification_and_pyproject() -> None:
    """@req CNF-041"""
    python_root = Path(__file__).parents[1]
    repo_root = python_root.parent

    overview = (repo_root / "specifications" / "00-overview.md").read_text(encoding="utf-8")
    assert f"| Specification version | {specification_version()} |" in overview

    pyproject = (python_root / "pyproject.toml").read_text(encoding="utf-8")
    assert f'version = "{sdk_version()}"' in pyproject
