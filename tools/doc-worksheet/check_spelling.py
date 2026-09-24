#!/usr/bin/env python3
"""DOC-025 spell gate: spell-check the documentation set with a project dictionary that
includes error codes and wire fields (DR-0018 D-M11-25 g2).

    python3 tools/doc-worksheet/check_spelling.py

This is the "or equivalent" limb of DOC-025 (`16-documentation-requirements.md`): rather
than depend on `cspell` (a Node package with no existing footprint in this Python/.NET
repository), it uses the OS-provided English wordlist already present on both the
developer image (macOS ships one at ``/usr/share/dict/words``) and CI (installed by the
workflow step immediately before this one, a small package) plus a project dictionary
tracked at ``tools/doc-worksheet/project-dictionary.txt`` — the "error codes and wire
fields" DOC-025 names by name, plus SDK proper nouns and technical terms that are correct
spellings a general dictionary does not carry.

Scans every Markdown file under ``docs/`` plus ``dotnet/README.md``, skipping fenced code
blocks and inline code spans (code is not prose), and flags any remaining word that is in
neither dictionary. An all-uppercase token (an acronym or an error-code fragment such as
``BV`` from ``BV-KV-007``) is never flagged — those are exactly what the project dictionary
exists to hold in full where they need a specific check, and near-total false positives
otherwise given how densely this corpus cites error codes.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
SCAN_ROOTS = [REPO_ROOT / "docs"]
EXTRA_FILES = [REPO_ROOT / "dotnet" / "README.md"]
PROJECT_DICTIONARY = Path(__file__).resolve().parent / "project-dictionary.txt"

CANDIDATE_WORDLISTS = [
    Path("/usr/share/dict/words"),
    Path("/usr/share/dict/american-english"),
    Path("/usr/share/dict/british-english"),
]

FENCE_RE = re.compile(r"```.*?```", re.S)
INLINE_CODE_RE = re.compile(r"`[^`]*`")
LINK_URL_RE = re.compile(r"\]\([^)]*\)")
XML_TAG_RE = re.compile(r"<[^>]*>")
# Latin-1/Latin-Extended letters (e.g. the ç in "façade") stay inside the word instead of
# splitting it into two fragments — one of which (≤2 chars) would otherwise vanish
# silently and the other surface as a false "unrecognised word".
WORD_RE = re.compile(r"[A-Za-zÀ-ɏ']+")


def _load_base_dictionary() -> set[str]:
    for candidate in CANDIDATE_WORDLISTS:
        if candidate.is_file():
            return {line.strip().lower() for line in candidate.read_text(encoding="utf-8", errors="ignore").splitlines()}
    return set()


def _load_project_dictionary() -> set[str]:
    if not PROJECT_DICTIONARY.is_file():
        return set()
    words = set()
    for line in PROJECT_DICTIONARY.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        words.add(line.lower())
    return words


_SUFFIXES = ("'s", "ies", "ied", "es", "ing", "ed", "ly", "s")


def _known(word: str, dictionaries: tuple[set[str], ...]) -> bool:
    """Whether ``word`` (already lower-cased) is a known word, directly or as a common
    inflection of one. The base wordlist (``/usr/share/dict/words``) is a headword list —
    it carries ``comment`` but not ``comments`` or ``commented`` — so a raw membership
    test would flag nearly every plural and verb inflection in the corpus as unknown.
    Stripping one common suffix and re-checking is the smallest fix that stays honest:
    it still flags a genuine typo (``comitted`` strips to ``comit``, not a word either)."""

    if any(word in d for d in dictionaries):
        return True
    for suffix in _SUFFIXES:
        if word.endswith(suffix) and len(word) > len(suffix) + 2:
            stem = word[: -len(suffix)]
            if any(stem in d for d in dictionaries):
                return True
            if suffix == "ies" and any((stem + "y") in d for d in dictionaries):
                return True
            if suffix in ("ed", "ing") and any((stem + "e") in d for d in dictionaries):
                return True
    return False


def _strip_non_prose(text: str) -> str:
    text = FENCE_RE.sub(" ", text)
    text = INLINE_CODE_RE.sub(" ", text)
    text = LINK_URL_RE.sub("] ", text)
    # D11's generated pages reproduce XML doc tags verbatim (<see cref="..."/>,
    # <paramref name="..."/>, <see langword="..."/>): their content is a C# identifier
    # or keyword, never English prose, so the whole tag is dropped rather than scanned.
    text = XML_TAG_RE.sub(" ", text)
    return text


def _all_markdown_files() -> list[Path]:
    files: list[Path] = []
    for root in SCAN_ROOTS:
        if root.is_dir():
            files.extend(sorted(root.rglob("*.md")))
    files.extend(p for p in EXTRA_FILES if p.is_file())
    return files


def check(files: list[Path], base_dict: set[str], project_dict: set[str]) -> tuple[bool, list[str]]:
    ok = True
    lines: list[str] = []
    unknown_by_file: dict[str, set[str]] = {}

    for md_file in files:
        prose = _strip_non_prose(md_file.read_text(encoding="utf-8", errors="ignore"))
        unknown: set[str] = set()
        for match in WORD_RE.finditer(prose):
            word = match.group(0)
            if word.isupper() and len(word) > 1:
                continue  # acronym / error-code fragment (BV, KV2, ...)
            if "'" in word:
                word = word.split("'")[0]
            lowered = word.lower()
            if _known(lowered, (base_dict, project_dict)):
                continue
            if len(lowered) <= 2:
                continue
            unknown.add(word)
        if unknown:
            unknown_by_file[str(md_file.relative_to(REPO_ROOT))] = unknown

    lines.append(f"DOC-025 spell check: {len(files)} file(s) scanned against {len(base_dict)} base + {len(project_dict)} project word(s).")
    if unknown_by_file:
        ok = False
        for path, words in sorted(unknown_by_file.items()):
            lines.append(f"FAIL: {path}: unrecognised word(s): {', '.join(sorted(words))}")
    return ok, lines


def main(argv: list[str] | None = None) -> int:
    del argv
    base_dict = _load_base_dictionary()
    if not base_dict:
        print(
            "FAIL: no base English wordlist found at any of: "
            + ", ".join(str(p) for p in CANDIDATE_WORDLISTS)
            + " — install one (e.g. `sudo apt-get install -y wamerican` on the CI runner)."
        )
        return 2

    project_dict = _load_project_dictionary()
    files = _all_markdown_files()
    ok, lines = check(files, base_dict, project_dict)
    for line in lines:
        print(line)
    if ok:
        print("No unrecognised words.")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
