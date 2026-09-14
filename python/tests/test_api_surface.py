"""Committed baseline check for the package's public API surface (CNF-027).

Member-level (D-M1c-22): every public class, method, property, dataclass field,
enum member and constant *value* is captured by `tests/_api_surface_extractor.py`
over the transitive closure of `sdk.__all__`, not just the top-level names. This
in-pytest assertion is one of two gate mechanisms (the other is
`.github/workflows/python.yml`'s regenerate-then-`git diff` step); both stay, so a
local `pytest` run catches drift too, not only CI.
"""

from pathlib import Path

import bastionvault_integration_sdk as sdk
from tests._api_surface_extractor import capture, render_baseline


def test_package_member_surface_matches_committed_baseline() -> None:
    """@req CNF-027 @req TST-040"""
    baseline = Path(__file__).parents[1] / "api_surface.txt"
    expected_lines = [
        line
        for line in baseline.read_text(encoding="utf-8").splitlines()
        if line.strip() and not line.startswith("#")
    ]

    actual_lines = capture()

    added = sorted(set(actual_lines) - set(expected_lines))
    removed = sorted(set(expected_lines) - set(actual_lines))
    assert not added and not removed, (
        "Public API surface drifted from python/api_surface.txt.\n"
        "Added (present in the package, missing from the baseline):\n  " + "\n  ".join(added) + "\n"
        "Removed (present in the baseline, missing from the package):\n  " + "\n  ".join(removed) + "\n"
        "Regenerate the baseline mechanically with "
        "`python -m tests._api_surface_extractor --write` (do not hand-edit) if this "
        "change is intended."
    )

    # The committed file must also be byte-identical to a fresh regeneration -- this
    # is the same "regenerate, then diff" property the CI step proves, checked here
    # too so a hand-edit that happens to preserve every *line* but reorders or
    # reformats them still fails locally.
    assert baseline.read_text(encoding="utf-8") == render_baseline()


def test_all_exports_are_defined_and_are_the_root_of_the_surface() -> None:
    """@req CNF-027: `__all__` stays the documented root set of the member-level scan."""
    for name in sdk.__all__:
        assert hasattr(sdk, name), f"'{name}' is listed in __all__ but not defined"

    lines = capture()
    documented_class_names = {
        line.split(" : ", 1)[0].rsplit(".", 1)[-1] for line in lines if " : type " in line
    }
    documented_function_names = {
        line.split("function ", 1)[1].split("(", 1)[0]
        for line in lines
        if f"{sdk.__name__} : function " in line or f"{sdk.__name__} : async function " in line
    }
    # `Logical` enters the baseline only through the transitive closure (it is public
    # but deliberately not exported); every *exported* name must still resolve to a
    # documented class or a documented module-level function.
    documented = documented_class_names | documented_function_names
    missing = [name for name in sdk.__all__ if name not in documented]
    assert not missing, f"exported but not captured by the member-level scan: {missing}"


def test_client_has_no_set_address() -> None:
    """@req CFG-072"""
    assert not hasattr(sdk.Client, "set_address")
    assert not hasattr(sdk.Client, "SetAddress")
