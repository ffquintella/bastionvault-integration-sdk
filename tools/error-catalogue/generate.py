#!/usr/bin/env python3
"""Regenerate the error catalogue for all three SDKs (DR-0005 D-M1c-1).

    python tools/error-catalogue/generate.py            # write
    python tools/error-catalogue/generate.py --check    # fail if anything would change

``--check`` exists for local use; CI proves the same property the way every other
gate in this repository does — regenerate, then ``git diff --exit-code`` — so a
hand edit to a generated file and a specification edit without regeneration both
fail, and neither depends on this script agreeing with itself.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import captures  # noqa: E402
from catalogue import CatalogueError, parse  # noqa: E402
from emitters import artefacts, owned_fixture_paths  # noqa: E402

APPENDIX_B = "specifications/appendix-b-error-catalogue.md"


def repository_root() -> Path:
    return Path(__file__).resolve().parents[2]


def build(root: Path) -> dict[str, str]:
    catalogue = parse(root / APPENDIX_B)
    captures.check(catalogue.rules)
    return artefacts(catalogue)


def run(root: Path, check: bool) -> int:
    files = build(root)
    stale = owned_fixture_paths(root) - {root / name for name in files}
    changed: list[str] = []

    for name, content in sorted(files.items()):
        path = root / name
        current = path.read_text(encoding="utf-8") if path.is_file() else None
        if current == content:
            continue
        changed.append(name)
        if not check:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8", newline="\n")

    for path in sorted(stale):
        changed.append(str(path.relative_to(root)) + " (stale)")
        if not check:
            path.unlink()

    if check and changed:
        print(f"FAIL: {len(changed)} generated artefact(s) are out of date:")
        for name in changed:
            print(" -", name)
        print("Run: python tools/error-catalogue/generate.py")
        return 1

    verb = "would change" if check else "wrote"
    print(f"{len(files)} artefact(s) generated from {APPENDIX_B}; {verb} {len(changed)}.")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="fail instead of writing")
    parser.add_argument("--root", type=Path, default=None, help="repository root (default: inferred)")
    arguments = parser.parse_args(argv)
    root = arguments.root or repository_root()
    try:
        return run(root, arguments.check)
    except CatalogueError as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
