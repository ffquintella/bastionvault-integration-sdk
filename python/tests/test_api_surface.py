"""Committed baseline check for the package's public API surface (CNF-027)."""

from pathlib import Path

import bastionvault_integration_sdk as sdk


def test_package_api_matches_committed_baseline() -> None:
    """@req CNF-027 @req TST-040"""
    baseline = Path(__file__).parents[1] / "api_surface.txt"
    expected_names = [
        line.strip()
        for line in baseline.read_text(encoding="utf-8").splitlines()
        if line.strip() and not line.lstrip().startswith("#")
    ]

    assert sorted(sdk.__all__) == sorted(expected_names)
    for name in sdk.__all__:
        assert hasattr(sdk, name), f"'{name}' is listed in __all__ but not defined"


def test_client_has_no_set_address() -> None:
    """@req CFG-072"""
    assert not hasattr(sdk.Client, "set_address")
    assert not hasattr(sdk.Client, "SetAddress")
