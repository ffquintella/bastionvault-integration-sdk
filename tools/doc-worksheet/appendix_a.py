"""Parses ``specifications/appendix-a-endpoint-catalogue.md`` for cross-referencing.

Appendix A is written for a human, not a generator: canonical-name cells routinely group
several operations behind one shared prefix (``` `Auth.Token.Renew` / `RenewSelf` ```,
``` `Sys.Cluster.RemoveNode/Leave/Failover` ```), and one section (LDAP/Files/Resources/
Cert lifecycle/Notifications/Rustion) drops canonical names entirely in favour of prose
paths. This module expands what can be expanded — a shared dotted prefix applied across a
``/``- or comma-separated alternation — and leaves the rest alone rather than guessing at
a grouping the document does not spell out. A placeholder cell such as
``Auth.AppId.Admin.<Field>`` is recorded under its literal prefix (not expanded, since the
brief enumerates the concrete field list, not this document) so a lookup miss against it
is still visible in the worksheet rather than silently absent.
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from pathlib import Path

SECTION_RE = re.compile(r"^##\s+(?P<heading>.+)$", re.MULTILINE)
HEADING_LEVEL_RE = re.compile(r"—\s*Level\s+([CSX])\b")
BACKTICK_RE = re.compile(r"`([^`]+)`")
EXCLUDED_HEADING_PREFIXES = ("Endpoints that do **not** exist",)


@dataclass(frozen=True)
class AppendixEntry:
    canonical_name: str
    verb_cell: str
    path_cell: str
    level: str | None  # "C", "S", "X", a combined cell like "C/S", or None if absent
    section: str
    raw_row: str


def _split_row(line: str) -> list[str]:
    line = line.strip()
    if line.startswith("|"):
        line = line[1:]
    if line.endswith("|"):
        line = line[:-1]
    return [cell.strip() for cell in line.split("|")]


def _is_separator_row(cells: list[str]) -> bool:
    return all(re.fullmatch(r":?-{2,}:?", cell) for cell in cells if cell)


def _expand_canonical_cell(cell: str) -> tuple[list[str], list[str]]:
    """Return (concrete names, placeholder prefixes).

    A placeholder alternative (``<Field>``) cannot be turned into a concrete name from
    this document alone, but its *prefix* (``Auth.AppId.Admin.``) is still useful: a
    worksheet lookup that misses exactly can fall back to "starts with this prefix,
    approximate match only" instead of reporting a plain miss.
    """

    groups = BACKTICK_RE.findall(cell)
    names: list[str] = []
    placeholder_prefixes: list[str] = []
    last_prefix: str | None = None
    for group in groups:
        if "." in group:
            prefix, _, tail = group.rpartition(".")
            prefix = prefix + "."
            last_prefix = prefix
        else:
            tail = group
            prefix = last_prefix
        if prefix is None:
            names.append(group)
            continue
        for alternative in tail.split("/"):
            alternative = alternative.strip()
            if not alternative:
                continue
            if "<" in alternative or "(" in alternative:
                # A placeholder (`<Field>`) or an annotated form; not a concrete name
                # this document actually enumerates, so it is not expanded — only its
                # prefix is kept, for an approximate fallback match.
                placeholder_prefixes.append(prefix)
                continue
            names.append(prefix + alternative)
    return names, placeholder_prefixes


@dataclass(frozen=True)
class ParsedAppendixA:
    entries: dict[str, list[AppendixEntry]]
    placeholder_entries: list[tuple[str, AppendixEntry]]  # (prefix, its row's entry data)


def parse_appendix_a(path: Path) -> ParsedAppendixA:
    text = path.read_text(encoding="utf-8")
    sections = list(SECTION_RE.finditer(text))
    entries: dict[str, list[AppendixEntry]] = {}
    placeholder_entries: list[tuple[str, AppendixEntry]] = []

    for index, section_match in enumerate(sections):
        heading = section_match.group("heading").strip()
        if any(heading.startswith(prefix) for prefix in EXCLUDED_HEADING_PREFIXES):
            continue
        section_start = section_match.end()
        section_end = sections[index + 1].start() if index + 1 < len(sections) else len(text)
        section_text = text[section_start:section_end]
        heading_level_match = HEADING_LEVEL_RE.search(heading)
        heading_level = heading_level_match.group(1) if heading_level_match else None

        lines = section_text.splitlines()
        header_index = next((i for i, line in enumerate(lines) if line.strip().startswith("|")), None)
        if header_index is None:
            continue
        header_cells = [cell.lower() for cell in _split_row(lines[header_index])]
        if not header_cells or "canonical operation" not in header_cells[0]:
            continue  # a prose table (e.g. LDAP/Files/...), not one this can expand

        def column(name: str) -> int | None:
            for position, cell in enumerate(header_cells):
                if cell == name:
                    return position
            return None

        verb_index = column("verb")
        path_index = column("path")
        level_index = column("level")

        row_index = header_index + 1
        while row_index < len(lines):
            line = lines[row_index]
            if not line.strip().startswith("|"):
                break
            cells = _split_row(line)
            row_index += 1
            if _is_separator_row(cells):
                continue
            if not cells or not cells[0].strip():
                continue
            canonical_cell = cells[0]
            verb_cell = cells[verb_index] if verb_index is not None and verb_index < len(cells) else ""
            path_cell = cells[path_index] if path_index is not None and path_index < len(cells) else ""
            row_level = cells[level_index] if level_index is not None and level_index < len(cells) else ""
            level = row_level.strip() or heading_level

            names, placeholder_prefixes = _expand_canonical_cell(canonical_cell)
            for name in names:
                entry = AppendixEntry(
                    canonical_name=name,
                    verb_cell=verb_cell,
                    path_cell=path_cell,
                    level=level,
                    section=heading,
                    raw_row=line.strip(),
                )
                entries.setdefault(name, []).append(entry)
            for prefix in placeholder_prefixes:
                placeholder_entries.append(
                    (
                        prefix,
                        AppendixEntry(
                            canonical_name=prefix + "<placeholder>",
                            verb_cell=verb_cell,
                            path_cell=path_cell,
                            level=level,
                            section=heading,
                            raw_row=line.strip(),
                        ),
                    )
                )

    return ParsedAppendixA(entries=entries, placeholder_entries=placeholder_entries)


def find_entry(parsed: ParsedAppendixA, canonical_name: str) -> tuple[AppendixEntry | None, bool]:
    """Look up ``canonical_name``. Returns (entry, is_approximate).

    An exact hit is preferred; failing that, the longest placeholder prefix the name
    starts with is used as an approximate match (e.g. ``Auth.AppId.Admin.ReadField``
    against the ``Auth.AppId.Admin.<Field>`` row) — approximate because the document
    enumerates the concrete field list nowhere this generator can read it.
    """

    exact = parsed.entries.get(canonical_name)
    if exact:
        return exact[0], False
    best: tuple[str, AppendixEntry] | None = None
    for prefix, entry in parsed.placeholder_entries:
        if canonical_name.startswith(prefix) and (best is None or len(prefix) > len(best[0])):
            best = (prefix, entry)
    if best is not None:
        return best[1], True
    return None, False
