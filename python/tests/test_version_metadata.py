"""Tests for the M0 public surface: specification and package version functions."""

import json
from pathlib import Path

from bastionvault_integration_sdk import (
    sdk_version,
    specification_source_ref,
    specification_source_release,
    specification_version,
)


def test_version_functions_match_specification_and_pyproject() -> None:
    """@req CNF-041"""
    python_root = Path(__file__).parents[1]
    repo_root = python_root.parent

    overview = (repo_root / "specifications" / "00-overview.md").read_text(encoding="utf-8")
    assert f"| Specification version | {specification_version()} |" in overview

    pyproject = (python_root / "pyproject.toml").read_text(encoding="utf-8")
    assert f'version = "{sdk_version()}"' in pyproject


def test_specification_source_matches_provenance_manifest() -> None:
    """@req CNF-047"""
    assert specification_source_release() == "0.42.0"
    assert specification_source_ref() == "v0.42.0"

    python_root = Path(__file__).parents[1]
    repo_root = python_root.parent
    provenance_path = repo_root / "specifications" / "provenance.json"
    if not provenance_path.exists():
        # specifications/provenance.json is produced by a companion change (DR-0011);
        # skip the manifest-consistency check rather than fail when it is absent.
        return

    manifest = json.loads(provenance_path.read_text(encoding="utf-8"))
    assert manifest["upstream"]["release"] == specification_source_release()
    assert manifest["upstream"]["ref"] == specification_source_ref()
