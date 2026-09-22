"""Builds the DOC-005 worksheet: one row per public operation, the mechanically
derivable half filled in, the rest marked with a reason instead of a guess (D-M11-20).

This module owns no interpretation of its own beyond assembling what ``cs_model``,
``resolve``, ``apisurface`` and ``appendix_a`` already produced, plus reading each
operation's existing doc comment for what it already carries (DOC-005/DOC-006 citations,
an existing ``<spec>`` tag) so a later slice can tell "already documented" from "still
needs research" at a glance.
"""

from __future__ import annotations

import re
import sys
from dataclasses import asdict, dataclass
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parent
REPO_ROOT = TOOL_DIR.parents[1]
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

import apisurface  # noqa: E402
import appendix_a  # noqa: E402
import cs_model  # noqa: E402
import resolve  # noqa: E402
from tools.traceability.traceability import parse_appendix_d  # noqa: E402

PUBLIC_API_SURFACE = "dotnet/BastionVault.IntegrationSdk/PublicApiSurface.txt"
APPENDIX_A = "specifications/appendix-a-endpoint-catalogue.md"
APPENDIX_D = "specifications/appendix-d-requirement-index.md"
SDK_SOURCE_SUBPATH = "dotnet/BastionVault.IntegrationSdk"

# Doc comments quote error codes constantly (`BV-KV-007`, `BV-AUTH-003`), which are
# hyphen-chains of *three* parts and must not be mistaken for a two-part requirement ID
# — a naive `AREA-NNN` regex matches `KV-007` inside `BV-KV-007` because the preceding
# `-` is a word boundary. Chain-and-filter instead of matching the short form directly.
_ID_CHAIN_RE = re.compile(r"\b[A-Z][A-Z0-9]*(?:-[A-Z0-9]+)+\b")
SPEC_TAG_RE = re.compile(r"<spec>\s*(?P<name>[^—-]+?)\s*[—-]\s*(?P<id>[A-Z][A-Z0-9]*-\d{3})\s*</spec>")


@dataclass
class OperationRow:
    canonical_name: str | None
    canonical_name_ambiguous: list[str]
    declaring_type: str
    method_name: str
    file: str
    line: int

    http_verb: str | None
    http_verb_reason: str | None
    http_path_template: str | None
    http_path_reason: str | None
    execute_method: str | None
    call_site_count: int

    appendix_a_match: str | None  # "exact", "approximate", or None
    appendix_a_canonical: str | None
    appendix_a_verb: str | None
    appendix_a_path: str | None
    appendix_a_level: str | None
    appendix_a_section: str | None

    existing_requirement_ids: list[str]
    unknown_requirement_ids: list[str]
    has_spec_tag: bool
    spec_tag_valid: bool | None  # None when there is no tag to check
    spec_tag_text: str | None

    suggested_spec_tag: str | None  # ready-to-paste, when the data supports one

    complete: bool  # every mechanically derivable field is present
    gaps: list[str]


def _requirement_ids_in(doc_comment: str) -> list[str]:
    ids: list[str] = []
    for match in _ID_CHAIN_RE.finditer(doc_comment):
        parts = match.group(0).split("-")
        if len(parts) != 2:
            continue  # a three-part chain is an error code (`BV-KV-007`), not an ID
        area, tail = parts
        if area == "BV":
            continue
        is_requirement = re.fullmatch(r"\d{3}", tail) is not None
        is_scenario = area == "ITG" and re.fullmatch(r"S\d{2}", tail) is not None
        if not (is_requirement or is_scenario):
            continue
        candidate = match.group(0)
        if candidate not in ids:
            ids.append(candidate)
    return ids


def _spec_tag_in(doc_comment: str) -> tuple[bool, str | None, str | None, str | None]:
    """Returns (has_tag, full_tag_text, name, requirement_id)."""

    match = SPEC_TAG_RE.search(doc_comment)
    if not match:
        return False, None, None, None
    return True, match.group(0), match.group("name").strip(), match.group("id")


def build_rows(root: Path) -> list[OperationRow]:
    class_index = cs_model.build_index(root, [SDK_SOURCE_SUBPATH])
    public_operations = apisurface.build_public_operations(root / PUBLIC_API_SURFACE)
    appendix = appendix_a.parse_appendix_a(root / APPENDIX_A)
    valid_requirement_ids = parse_appendix_d(root / APPENDIX_D)

    rows: list[OperationRow] = []
    for operation in public_operations:
        method_info = None
        for class_info in class_index.get(operation.declaring_type, []):
            candidates = [m for m in class_info.methods.get(operation.method_name, []) if m.is_public]
            if candidates:
                method_info = candidates[0]
                break

        canonical_name = operation.canonical_names[0] if len(operation.canonical_names) == 1 else None
        ambiguous_names = list(operation.canonical_names) if len(operation.canonical_names) != 1 else []

        if method_info is None:
            rows.append(
                OperationRow(
                    canonical_name=canonical_name,
                    canonical_name_ambiguous=ambiguous_names,
                    declaring_type=operation.declaring_type,
                    method_name=operation.method_name,
                    file="",
                    line=0,
                    http_verb=None,
                    http_verb_reason="method body not found in the parsed .cs source",
                    http_path_template=None,
                    http_path_reason="method body not found in the parsed .cs source",
                    execute_method=None,
                    call_site_count=0,
                    appendix_a_match=None,
                    appendix_a_canonical=None,
                    appendix_a_verb=None,
                    appendix_a_path=None,
                    appendix_a_level=None,
                    appendix_a_section=None,
                    existing_requirement_ids=[],
                    unknown_requirement_ids=[],
                    has_spec_tag=False,
                    spec_tag_valid=None,
                    spec_tag_text=None,
                    suggested_spec_tag=None,
                    complete=False,
                    gaps=["source not found"],
                )
            )
            continue

        route = resolve.resolve_route(method_info, class_index)
        doc_comment = method_info.doc_comment
        requirement_ids = _requirement_ids_in(doc_comment)
        unknown_ids = [rid for rid in requirement_ids if rid not in valid_requirement_ids]
        has_tag, tag_text, _tag_name, tag_id = _spec_tag_in(doc_comment)
        tag_valid = (tag_id in valid_requirement_ids) if has_tag else None

        appendix_match: str | None = None
        appendix_entry = None
        if canonical_name is not None:
            appendix_entry, is_approximate = appendix_a.find_entry(appendix, canonical_name)
            if appendix_entry is not None:
                appendix_match = "approximate" if is_approximate else "exact"

        gaps: list[str] = []
        if canonical_name is None:
            gaps.append("canonical name ambiguous (reachable by more than one facade path)")
        if route.verb is None:
            gaps.append(f"HTTP verb: {route.verb_reason}")
        if route.path_template is None:
            gaps.append(f"HTTP path: {route.path_reason}")
        if appendix_entry is None and route.call_site_count != 0:
            gaps.append("no Appendix A match (exact or approximate)")
        if not requirement_ids:
            gaps.append("no requirement ID cited in the existing doc comment")
        if unknown_ids:
            gaps.append(f"cited requirement ID(s) not in Appendix D: {', '.join(unknown_ids)}")

        suggested_tag = None
        if canonical_name is not None and requirement_ids and not unknown_ids:
            suggested_tag = f"<spec>{canonical_name} — {requirement_ids[0]}</spec>"

        rows.append(
            OperationRow(
                canonical_name=canonical_name,
                canonical_name_ambiguous=ambiguous_names,
                declaring_type=operation.declaring_type,
                method_name=operation.method_name,
                file=method_info.file,
                line=method_info.line,
                http_verb=route.verb,
                http_verb_reason=route.verb_reason,
                http_path_template=route.path_template,
                http_path_reason=route.path_reason,
                execute_method=route.execute_method,
                call_site_count=route.call_site_count,
                appendix_a_match=appendix_match,
                appendix_a_canonical=appendix_entry.canonical_name if appendix_entry else None,
                appendix_a_verb=appendix_entry.verb_cell if appendix_entry else None,
                appendix_a_path=appendix_entry.path_cell if appendix_entry else None,
                appendix_a_level=appendix_entry.level if appendix_entry else None,
                appendix_a_section=appendix_entry.section if appendix_entry else None,
                existing_requirement_ids=requirement_ids,
                unknown_requirement_ids=unknown_ids,
                has_spec_tag=has_tag,
                spec_tag_valid=tag_valid,
                spec_tag_text=tag_text,
                suggested_spec_tag=suggested_tag,
                complete=not gaps,
                gaps=gaps,
            )
        )

    rows.sort(key=lambda row: (row.canonical_name or f"~{row.declaring_type}.{row.method_name}"))
    return rows


def build_summary(rows: list[OperationRow]) -> dict[str, object]:
    total = len(rows)

    def count(predicate) -> int:
        return sum(1 for row in rows if predicate(row))

    return {
        "total_operations": total,
        "canonical_name_resolved": count(lambda r: r.canonical_name is not None),
        "canonical_name_ambiguous": count(lambda r: r.canonical_name is None),
        "http_verb_and_path_resolved": count(lambda r: r.http_verb is not None and r.http_path_template is not None),
        "http_no_call_client_side_helper": count(
            lambda r: r.call_site_count == 0 and r.http_verb_reason is not None
        ),
        "http_ambiguous_multiple_calls": count(lambda r: r.call_site_count > 1),
        "http_unresolved_other": count(
            lambda r: r.call_site_count == 1 and (r.http_verb is None or r.http_path_template is None)
        ),
        "appendix_a_exact_match": count(lambda r: r.appendix_a_match == "exact"),
        "appendix_a_approximate_match": count(lambda r: r.appendix_a_match == "approximate"),
        "appendix_a_no_match": count(lambda r: r.appendix_a_match is None),
        "already_tagged": count(lambda r: r.has_spec_tag),
        "tag_valid_requirement_id": count(lambda r: r.spec_tag_valid is True),
        "tag_invalid_requirement_id": count(lambda r: r.spec_tag_valid is False),
        "has_requirement_id_cited": count(lambda r: bool(r.existing_requirement_ids)),
        "cites_unknown_requirement_id": count(lambda r: bool(r.unknown_requirement_ids)),
        "ready_to_paste_spec_tag": count(lambda r: r.suggested_spec_tag is not None),
        "fully_complete": count(lambda r: r.complete),
    }


def to_json_document(rows: list[OperationRow]) -> dict[str, object]:
    return {"summary": build_summary(rows), "operations": [asdict(row) for row in rows]}


def _flatten(text: str) -> str:
    """Collapse whitespace so a multi-line reason (e.g. a quoted C# ternary) fits one
    Markdown table cell instead of breaking the table."""

    return " ".join(text.split()).replace("|", "\\|")


def render_markdown(rows: list[OperationRow]) -> str:
    summary = build_summary(rows)
    lines = [
        "# DOC-005 worksheet",
        "",
        "Generated by `tools/doc-worksheet` (DR-0018 D-M11-20). The mechanically derivable "
        "half of DOC-005 — HTTP verb + path, canonical name, Appendix A level, existing "
        "requirement-ID citations — is filled in from the .NET source and the "
        "specification; purpose, return/null semantics and the error list still need a "
        "human. A `null` field always carries a reason; nothing here is guessed (R-23).",
        "",
        "## Summary",
        "",
    ]
    for key, value in summary.items():
        lines.append(f"- **{key}**: {value}")
    lines.extend(
        [
            "",
            "## Operations",
            "",
            "| Canonical name | Verb | Path template | Appendix A | Tagged | Gaps |",
            "|---|---|---|---|---|---|",
        ]
    )
    for row in rows:
        name = row.canonical_name or f"ambiguous: {', '.join(row.canonical_name_ambiguous)}"
        verb = row.http_verb or f"— ({row.http_verb_reason})"
        path = row.http_path_template or f"— ({row.http_path_reason})"
        appendix = row.appendix_a_match or "no match"
        tagged = "yes" if row.has_spec_tag else "no"
        gaps = "; ".join(row.gaps) if row.gaps else "none"
        lines.append(
            f"| `{_flatten(name)}` | {_flatten(verb)} | `{_flatten(path)}` | "
            f"{appendix} | {tagged} | {_flatten(gaps)} |"
        )
    lines.append("")
    return "\n".join(lines)
