#!/usr/bin/env python3
"""DOC-006 tag gate and D-M11-20 HTTP verb/path drift gate (DR-0018 D-M11-13, D-M11-20).

    python3 tools/doc-worksheet/check_docs.py

Two checks over the 473 public facade operations, both driven by
``tools/doc-worksheet``'s static model rather than re-derived here:

1. **DOC-006 tag check (D-M11-13).** Every public operation MUST carry a ``<spec>`` tag
   that parses as either the requirement-ID form (``<spec>Name — AREA-NNN</spec>``) or the
   D-M11-21 section-file fallback (``<spec>Name — 08-transit-engine.md</spec>``). An
   ID-form tag's requirement ID MUST exist in ``appendix-d-requirement-index.md``. The two
   forms are counted separately and both count as satisfying the gate — the fallback is a
   deliberate, correct citation (D-M11-21), not a lesser one.

   **What this does not check.** It validates the tag that is written against Appendix D's
   existence list, never against ``suggested_spec_tag`` (a suggestion, wrong at least four
   times — D-M11-23). It cannot decide *governance*: whether the cited ID actually governs
   the operation it is attached to is a judgement call, not a mechanical fact — D-M11-26
   found 43 tags citing a real, existing ID that did not govern the operation, and an
   existence check passes every one of them. That distinction was made by human review
   (D-M11-21, D-M11-23, D-M11-26) and is not re-derived here; this gate only guards against
   the corpus *regressing* below that reviewed state (a tag going missing, or a new
   operation shipping with an unresolvable ID).

2. **HTTP verb/path drift check (D-M11-20).** For every operation whose HTTP verb and path
   are statically resolvable from the .NET source (383 of 473), the ``<c>VERB path</c>``
   the doc comment states (preferring text inside ``<remarks>``, since that is where the
   corpus's "HTTP call:" convention lives, and falling back to the whole comment) MUST
   still name the same verb and the same path shape as the code. Placeholder *names*
   (``{n}`` vs ``{version}``) are not compared — only path *shape* is, because DOC-005 does
   not require the prose placeholder to match the C# parameter name, and a mechanical
   requirement to do so would fail on cosmetic difference, not drift. A leading ``v2/``
   segment is stripped before comparison: it is the transport layer's own prefix, stated in
   some doc comments as an addressing reminder, not part of the operation's own path.

   **What is exempted, and why — not silently skipped.** 90 operations carry no
   mechanically resolvable verb+path to check against: 74 are genuinely unresolved by
   static analysis (conditional/switch path expressions — this includes the six PKI
   conditional/switch-path operations and the section-12 grouped-level operations named in
   the slice brief — or an identifier the scanner does not resolve), 11 are client-side
   helpers with no HTTP call of their own, and 5 have more than one call site and are
   therefore ambiguous. All three counts are reported, not folded into a silent "skipped".
   A further small number of resolved operations (currently 9) state their HTTP call in a
   form this scanner's pattern does not extract (e.g. the verb and the path appear in
   separate sentences); these are reported as **not comparable**, not as a pass and not as
   a failure — R-23's rule applies to this gate too: an unresolved comparison is reported,
   never guessed into either verdict.

Exit code is non-zero if any operation lacks a tag, cites an ID-form tag whose ID is not in
Appendix D, or states an HTTP verb/path that mechanically disagrees with the code.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parent
REPO_ROOT = TOOL_DIR.parents[1]
if str(TOOL_DIR) not in sys.path:
    sys.path.insert(0, str(TOOL_DIR))
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

import cs_model  # noqa: E402
import worksheet  # noqa: E402

SDK_SOURCE_SUBPATH = "dotnet/BastionVault.IntegrationSdk"

_CALL_RE = re.compile(r"<c>\s*(GET|PUT|POST|DELETE|LIST|PATCH|HEAD)\s+([^<]+?)\s*</c>")
_REMARKS_RE = re.compile(r"<remarks>(.*?)</remarks>", re.S)
_PLACEHOLDER_RE = re.compile(r"\{[^}/]+\}")
_BARE_IDENTIFIER_RE = re.compile(r"[A-Za-z_][A-Za-z0-9_]*")


def _normalise_path(path: str) -> str:
    """Path *shape* only: no query string, no leading/trailing slash, no v2 transport
    prefix, and every placeholder segment collapsed to one canonical token so a prose
    parameter name (``{n}``) is not compared against a C# one (``{version}``)."""

    path = path.split("?", 1)[0].strip().strip("/")
    if path.startswith("v2/"):
        path = path[3:]
    path = _PLACEHOLDER_RE.sub("{}", path)
    if _BARE_IDENTIFIER_RE.fullmatch(path):
        # A doc comment sometimes names the path with the bare parameter word itself
        # (e.g. "path" for Logical.Read's caller-supplied route) rather than wrapping it
        # in braces. Equivalent in shape to a single placeholder segment.
        path = "{}"
    return path


def _doc_comment_for(class_index, declaring_type: str, method_name: str) -> str | None:
    for class_info in class_index.get(declaring_type, []):
        candidates = [m for m in class_info.methods.get(method_name, []) if m.is_public]
        if candidates:
            return candidates[0].doc_comment
    return None


def _stated_call(doc_comment: str) -> tuple[str, str] | None:
    remarks_text = " ".join(_REMARKS_RE.findall(doc_comment))
    match = _CALL_RE.search(remarks_text) or _CALL_RE.search(doc_comment)
    if match is None:
        return None
    return match.group(1), match.group(2)


def check_spec_tags(rows: list[worksheet.OperationRow]) -> tuple[bool, list[str]]:
    ok = True
    lines: list[str] = []
    untagged = [r for r in rows if not r.has_spec_tag]
    invalid = [r for r in rows if r.spec_tag_valid is False]
    id_form = sum(1 for r in rows if r.spec_tag_form == "id")
    fallback_form = sum(1 for r in rows if r.spec_tag_form == "section-file")

    lines.append(
        f"DOC-006 tags: {len(rows)} operations, {id_form} requirement-ID form, "
        f"{fallback_form} section-file fallback (D-M11-21), {len(untagged)} untagged, "
        f"{len(invalid)} citing an ID not in Appendix D."
    )
    if untagged:
        ok = False
        lines.append(f"FAIL: {len(untagged)} operation(s) carry no <spec> tag:")
        for row in untagged:
            lines.append(f"  - {row.canonical_name or row.method_name} ({row.file}:{row.line})")
    if invalid:
        ok = False
        lines.append(f"FAIL: {len(invalid)} operation(s) cite a requirement ID not in Appendix D:")
        for row in invalid:
            lines.append(f"  - {row.canonical_name or row.method_name} ({row.file}:{row.line}): {row.spec_tag_text}")
    return ok, lines


def check_http_drift(root: Path, rows: list[worksheet.OperationRow]) -> tuple[bool, list[str]]:
    ok = True
    lines: list[str] = []
    class_index = cs_model.build_index(root, [SDK_SOURCE_SUBPATH])

    resolved = [r for r in rows if r.http_verb is not None and r.http_path_template is not None]
    exempt_no_call = [r for r in rows if r.call_site_count == 0 and r.http_verb_reason is not None]
    exempt_ambiguous = [r for r in rows if r.call_site_count > 1]
    exempt_ids = {id(r) for r in exempt_no_call} | {id(r) for r in exempt_ambiguous}
    exempt_unresolved = [
        r for r in rows if id(r) not in exempt_ids and (r.http_verb is None or r.http_path_template is None)
    ]

    matched = 0
    not_comparable: list[str] = []
    mismatches: list[str] = []
    for row in resolved:
        doc_comment = _doc_comment_for(class_index, row.declaring_type, row.method_name)
        stated = _stated_call(doc_comment) if doc_comment else None
        if stated is None:
            not_comparable.append(f"{row.canonical_name} ({row.file}:{row.line})")
            continue
        verb, path = stated
        if verb == row.http_verb and _normalise_path(path) == _normalise_path(row.http_path_template):
            matched += 1
        else:
            mismatches.append(
                f"{row.canonical_name} ({row.file}:{row.line}): doc states "
                f"`{verb} {path}`, code resolves to `{row.http_verb} {row.http_path_template}`"
            )

    lines.append(
        f"HTTP verb/path drift: {len(resolved)} resolvable operations, {matched} match the "
        f"doc comment, {len(mismatches)} disagree, {len(not_comparable)} not mechanically "
        f"comparable (doc states the call in a form this scanner does not extract). "
        f"Exempt by construction: {len(exempt_unresolved)} unresolved (includes the PKI "
        f"conditional/switch-path operations and section-12 grouped-level operations), "
        f"{len(exempt_no_call)} client-side helpers with no HTTP call, "
        f"{len(exempt_ambiguous)} ambiguous (more than one call site)."
    )
    if mismatches:
        ok = False
        lines.append(f"FAIL: {len(mismatches)} operation(s) whose doc comment disagrees with the code:")
        for entry in mismatches:
            lines.append(f"  - {entry}")
    if not_comparable:
        lines.append(f"INFO: not mechanically comparable ({len(not_comparable)}):")
        for entry in not_comparable:
            lines.append(f"  - {entry}")
    return ok, lines


def main(argv: list[str] | None = None) -> int:
    del argv
    rows = worksheet.build_rows(REPO_ROOT)

    tags_ok, tag_lines = check_spec_tags(rows)
    drift_ok, drift_lines = check_http_drift(REPO_ROOT, rows)

    for line in tag_lines:
        print(line)
    print()
    for line in drift_lines:
        print(line)

    return 0 if (tags_ok and drift_ok) else 1


if __name__ == "__main__":
    raise SystemExit(main())
