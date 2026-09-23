#!/usr/bin/env python3
"""DOC-020 presence gate: D1-D13 exist with non-placeholder content (DR-0018 D-M11-5,
D-M11-12, D-M11-25 g2).

    python3 tools/doc-worksheet/check_presence.py

Checks presence and a minimum word count (DOC-001, section 16) for D1-D13. D1 is
``dotnet/README.md`` (not moved under ``docs/``, D-M11-2) and D12 is the repository-root
``CHANGELOG.md`` (Strategic-tree owned, D-M11-12) — both take a different path than the
``docs/dotnet/`` glob D2-D11 and D13 use. D6 is eleven per-mount pages under
``docs/dotnet/engines/`` (D-M11-10), each checked individually rather than as one
directory-presence fact, so a missing engine page cannot hide behind ten present ones.

This is presence and a word-count floor only (DOC-001's own text). It does not, and
cannot, judge whether a document's *content* is correct — that is Claude's review step at
handback, not a mechanical gate.
"""

from __future__ import annotations

import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]

MIN_WORDS = 200

ENGINE_PAGES = [
    "transit", "pki", "ssh", "totp", "ldap", "files", "resources",
    "identity", "notifications", "cert-lifecycle", "rustion",
]

# (document id, path relative to repo root, minimum word count)
DOCUMENTS: list[tuple[str, str, int]] = [
    ("D1", "dotnet/README.md", MIN_WORDS),
    ("D2", "docs/dotnet/getting-started.md", MIN_WORDS),
    ("D3", "docs/dotnet/configuration.md", MIN_WORDS),
    ("D4", "docs/dotnet/authentication.md", MIN_WORDS),
    ("D5", "docs/dotnet/secrets-kv.md", MIN_WORDS),
    *[(f"D6:{engine}", f"docs/dotnet/engines/{engine}.md", MIN_WORDS) for engine in ENGINE_PAGES],
    ("D7", "docs/dotnet/errors.md", MIN_WORDS),
    ("D8", "docs/dotnet/resilience-and-operations.md", MIN_WORDS),
    ("D9", "docs/dotnet/compatibility-gaps.md", MIN_WORDS),
    ("D10", "docs/dotnet/security.md", MIN_WORDS),
    ("D11", "docs/dotnet/api/README.md", 1),  # generated index; word-count floor does not apply
    ("D12", "CHANGELOG.md", 1),  # pre-existing, continuously maintained; presence only (D-M11-12)
    ("D13", "docs/dotnet/contributing.md", MIN_WORDS),
]

PLACEHOLDER_MARKERS = ("TODO", "TBD", "PLACEHOLDER", "FIXME")


def _word_count(text: str) -> int:
    return len(text.split())


def main(argv: list[str] | None = None) -> int:
    del argv
    ok = True
    lines: list[str] = []
    checked = 0

    for doc_id, rel_path, min_words in DOCUMENTS:
        checked += 1
        path = REPO_ROOT / rel_path
        if not path.is_file():
            ok = False
            lines.append(f"FAIL: {doc_id} missing — expected {rel_path}")
            continue

        text = path.read_text(encoding="utf-8")
        words = _word_count(text)
        if words < min_words:
            ok = False
            lines.append(
                f"FAIL: {doc_id} ({rel_path}) is {words} words, below the {min_words}-word "
                f"floor (DOC-001)"
            )
            continue

        # D11 is generated (no placeholder markers possible by construction) and D12 is a
        # large, pre-existing, continuously-maintained changelog (D-M11-12) where the
        # English word "placeholder" appears legitimately in historical entries — the
        # marker scan below only applies to the documents this milestone authors fresh.
        if min_words == MIN_WORDS:
            upper = text.upper()
            placeholder_hits = [marker for marker in PLACEHOLDER_MARKERS if marker in upper]
            if placeholder_hits:
                ok = False
                lines.append(
                    f"FAIL: {doc_id} ({rel_path}) contains placeholder marker(s): {', '.join(placeholder_hits)}"
                )
                continue

    print(f"DOC-020 presence: {checked} documents checked (D1-D13, D6 as {len(ENGINE_PAGES)} engine pages).")
    for line in lines:
        print(line)
    if ok:
        print("All documents present, over the word-count floor, and free of placeholder markers.")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
