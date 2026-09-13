"""Committed baseline check for the M0 package API surface."""

import ast
from pathlib import Path


def test_package_api_matches_committed_m0_baseline() -> None:
    """@req CNF-027 @req TST-040"""
    package_init = Path(__file__).parents[1] / "src" / "bastionvault_integration_sdk" / "__init__.py"
    baseline = Path(__file__).parents[1] / "api_surface.txt"
    tree = ast.parse(package_init.read_text(encoding="utf-8"))
    public_names = sorted(
        node.name
        for node in tree.body
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef))
        and not node.name.startswith("_")
    )
    expected_names = [
        line.strip()
        for line in baseline.read_text(encoding="utf-8").splitlines()
        if line.strip() and not line.lstrip().startswith("#")
    ]

    assert public_names == expected_names == ["sdk_version", "specification_version"]

