#!/usr/bin/env python3
"""DOC-023 link gate: no broken internal links in the documentation set (DR-0018 D-M11-25
g2).

    python3 tools/doc-worksheet/check_links.py

Scans every Markdown file under ``docs/`` plus ``dotnet/README.md`` for relative Markdown
links (``[text](path)`` and ``[text](path#anchor)``) and fails if the target file does not
exist, or the target has a ``#anchor`` that does not match a heading in the target file
(GitHub's own slug rule: lower-case, spaces to hyphens, punctuation stripped). This is the
"or equivalent" limb of DOC-023 (`16-documentation-requirements.md`) — a project-local
script rather than `lychee`, so the check runs without a network dependency and without an
extra CI-only tool to install; it checks only relative, in-repository links (an external
`https://` link is out of scope, since verifying those requires network access this
repository's CI does not grant any other gate either).
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
SCAN_ROOTS = [REPO_ROOT / "docs"]
EXTRA_FILES = [REPO_ROOT / "dotnet" / "README.md"]

LINK_RE = re.compile(r"\[[^\]]*\]\((?P<target>[^)\s]+)\)")
HEADING_RE = re.compile(r"^#{1,6}\s+(.*)$", re.MULTILINE)


def _slug(heading: str) -> str:
    heading = heading.strip().lower()
    heading = re.sub(r"[`*_]", "", heading)
    heading = re.sub(r"[^\w\s-]", "", heading)
    heading = re.sub(r"\s+", "-", heading)
    return heading


def _headings_in(path: Path) -> set[str]:
    if not path.is_file():
        return set()
    text = path.read_text(encoding="utf-8", errors="ignore")
    return {_slug(h) for h in HEADING_RE.findall(text)}


def _all_markdown_files() -> list[Path]:
    files: list[Path] = []
    for root in SCAN_ROOTS:
        if root.is_dir():
            files.extend(sorted(root.rglob("*.md")))
    files.extend(p for p in EXTRA_FILES if p.is_file())
    return files


def check(files: list[Path]) -> tuple[bool, list[str]]:
    ok = True
    lines: list[str] = []
    checked = 0

    for md_file in files:
        text = md_file.read_text(encoding="utf-8", errors="ignore")
        for match in LINK_RE.finditer(text):
            target = match.group("target")
            if target.startswith(("http://", "https://", "mailto:")):
                continue  # external — out of scope, see module docstring
            checked += 1
            path_part, _, anchor = target.partition("#")

            if path_part == "":
                resolved = md_file
            else:
                resolved = (md_file.parent / path_part).resolve()

            if not (resolved.is_file() or resolved.is_dir()):
                ok = False
                lines.append(f"FAIL: {md_file.relative_to(REPO_ROOT)}: broken link target `{target}`")
                continue

            if anchor and resolved.is_file() and not re.fullmatch(r"L\d+(-L\d+)?", anchor):
                # A `#L24`-style GitHub line anchor is not a heading and is not
                # mechanically verifiable without knowing the file's rendered line
                # count; only a heading-slug anchor is checked here.
                headings = _headings_in(resolved)
                if _slug(anchor) not in headings:
                    ok = False
                    lines.append(
                        f"FAIL: {md_file.relative_to(REPO_ROOT)}: anchor `#{anchor}` not found "
                        f"in {resolved.relative_to(REPO_ROOT)}"
                    )

    lines.insert(0, f"DOC-023 link check: {len(files)} file(s) scanned, {checked} internal link(s) checked.")
    return ok, lines


def main(argv: list[str] | None = None) -> int:
    del argv
    files = _all_markdown_files()
    ok, lines = check(files)
    for line in lines:
        print(line)
    if ok:
        print("No broken internal links.")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
