#!/usr/bin/env python3
"""Generate D11 (the .NET API reference) from doc comments (DR-0018 D-M11-2, D-M11-25 g1).

    python3 tools/api-reference/generate.py --out docs/dotnet/api

Section 16 (specifications/16-documentation-requirements.md:24) requires D11 to be
"generated from doc comments for every public symbol". This tool reuses
``tools/doc-worksheet/cs_model.py`` — the same text-based C# scanner the DOC-005/DOC-006
worksheet already depends on and that is exercised by that tool's own unit tests — rather
than introducing a second, unverified parser. It is read-only with respect to the SDK: it
never edits a ``.cs`` file, ``PublicApiSurface.txt``, or anything under
``specifications/``.

Output is one Markdown page per source file that declares at least one public type with
at least one public member (a file that is entirely internal-facing emits no page), plus
an index page (``README.md``) linking every emitted page. This mirrors the one-file
grouping ``docs/dotnet/engines/`` already uses for D6, and keeps the page count tied to a
fact the reader can check (``ls dotnet/BastionVault.IntegrationSdk/*.cs | wc -l`` bounds
it), rather than to a per-symbol page count that would balloon with every DTO.

A member's ``<spec>`` tag (DR-0018 D-M11-21) is reproduced verbatim when present, so this
page cross-references the same requirement IDs and section-file fallbacks the worksheet
sweep verified — it does not re-derive or re-judge them.
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parent
WORKSHEET_DIR = TOOL_DIR.parents[0] / "doc-worksheet"
if str(WORKSHEET_DIR) not in sys.path:
    sys.path.insert(0, str(WORKSHEET_DIR))

import cs_model  # noqa: E402

REPO_ROOT = TOOL_DIR.parents[1]
SDK_SOURCE_SUBPATH = "dotnet/BastionVault.IntegrationSdk"

SUMMARY_RE = re.compile(r"<summary>(.*?)</summary>", re.DOTALL)
SPEC_RE = re.compile(r"<spec>(.*?)</spec>", re.DOTALL)
REMARKS_RE = re.compile(r"<remarks>(.*?)</remarks>", re.DOTALL)

PUBLIC_API_TYPE_RE = re.compile(r"^BastionVault\.IntegrationSdk\.([A-Za-z0-9_]+)")


def _public_type_names(root: Path) -> set[str]:
    """The type names PublicApiSurfaceScanner (D-M1b-19) already proved are public.

    This is the ground truth for "public symbol" (D11): a ``public`` method inside an
    ``internal`` class is not part of the assembly's public surface, and this file — not
    a second accessibility heuristic in this tool — is what already tracks that
    distinction for the whole SDK.
    """

    surface_path = root / SDK_SOURCE_SUBPATH / "PublicApiSurface.txt"
    names: set[str] = set()
    for line in surface_path.read_text(encoding="utf-8").splitlines():
        match = PUBLIC_API_TYPE_RE.match(line)
        if match:
            names.add(match.group(1))
    return names


_CREF_RE = re.compile(r'<see cref="[^"]*?\.([A-Za-z0-9_]+)"\s*/>')
_PARAMREF_RE = re.compile(r'<paramref name="([^"]*)"\s*/>')
_LANGWORD_RE = re.compile(r'<see langword="([^"]*)"\s*/>')
_CODE_RE = re.compile(r"<c>(.*?)</c>", re.DOTALL)
_PARA_RE = re.compile(r"</?para>")
_B_I_RE = re.compile(r"</?[bi]>")


def _to_markdown(text: str) -> str:
    """Turn a doc-comment fragment's XML markup into plain Markdown-ish prose.

    Best-effort, not a full renderer (DocFX is not available in this environment) — a
    `<see cref>`/`<c>` survives as a backtick-quoted name rather than a resolved link, and
    an unrecognised tag is left as-is rather than dropped, so a reader always sees the
    underlying fact even when the formatting is imperfect.
    """

    text = _CREF_RE.sub(lambda m: f"`{m.group(1)}`", text)
    text = _PARAMREF_RE.sub(lambda m: f"`{m.group(1)}`", text)
    text = _LANGWORD_RE.sub(lambda m: f"`{m.group(1)}`", text)
    text = _CODE_RE.sub(lambda m: f"`{m.group(1).strip()}`", text)
    text = _PARA_RE.sub("\n\n", text)
    text = _B_I_RE.sub("", text)
    return text


def _clean(text: str) -> str:
    """Collapse a doc-comment fragment to readable Markdown prose."""

    text = _to_markdown(text)
    return re.sub(r"[ \t]+", " ", text).strip()


def _first_tag(pattern: re.Pattern[str], doc_comment: str) -> str | None:
    match = pattern.search(doc_comment)
    return _clean(match.group(1)) if match else None


def _signature(method: "cs_model.MethodInfo") -> str:
    params = ", ".join(method.params) if method.params else ""
    if method.kind == "property":
        return f"{method.name}"
    return f"{method.name}({params})"


def _render_class(class_info: "cs_model.ClassInfo") -> str | None:
    public_methods = [
        method
        for methods in class_info.methods.values()
        for method in methods
        if method.is_public
    ]
    if not public_methods:
        return None

    public_methods.sort(key=lambda method: method.line)

    lines: list[str] = [f"### `{class_info.name}`", ""]
    for method in public_methods:
        summary = _first_tag(SUMMARY_RE, method.doc_comment) or "*(no `<summary>` doc comment.)*"
        spec = _first_tag(SPEC_RE, method.doc_comment)
        remarks = _first_tag(REMARKS_RE, method.doc_comment)

        lines.append(f"#### `{_signature(method)}`")
        lines.append("")
        lines.append(summary)
        lines.append("")
        if remarks:
            lines.append(remarks)
            lines.append("")
        if spec:
            lines.append(f"**Spec:** `{spec}`")
            lines.append("")
        lines.append(f"*Source: `{class_info.file}:{method.line}`*")
        lines.append("")
    return "\n".join(lines)


def _page_title(relative_file: str) -> str:
    return Path(relative_file).stem


def run(root: Path, out_dir: Path) -> int:
    out_dir.mkdir(parents=True, exist_ok=True)

    public_type_names = _public_type_names(root)
    class_index = cs_model.build_index(root, [SDK_SOURCE_SUBPATH])
    by_file: dict[str, list["cs_model.ClassInfo"]] = {}
    for name, classes in class_index.items():
        if name not in public_type_names:
            continue
        for class_info in classes:
            by_file.setdefault(class_info.file, []).append(class_info)

    pages: list[tuple[str, str, int]] = []  # (title, relative_path, public_member_count)
    for relative_file in sorted(by_file):
        classes = sorted(by_file[relative_file], key=lambda c: c.name)
        rendered_sections = [_render_class(c) for c in classes]
        rendered_sections = [s for s in rendered_sections if s]
        if not rendered_sections:
            continue

        title = _page_title(relative_file)
        member_count = sum(
            1
            for c in classes
            for methods in c.methods.values()
            for m in methods
            if m.is_public
        )
        body = "\n".join(
            [
                f"# `{title}` (.NET API reference)",
                "",
                f"Generated from doc comments in [`{relative_file}`](../../../{relative_file}) by",
                "`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —",
                "regenerate with:",
                "",
                "```",
                "python3 tools/api-reference/generate.py --out docs/dotnet/api",
                "```",
                "",
                *rendered_sections,
            ]
        )
        page_path = out_dir / f"{title}.md"
        page_path.write_text(body + "\n", encoding="utf-8")
        pages.append((title, f"{title}.md", member_count))

    index_lines = [
        "# API reference (.NET)",
        "",
        "**Implements** D11",
        "([`specifications/16-documentation-requirements.md`](../../../specifications/16-documentation-requirements.md#L24)).",
        "Generated from doc comments for every public symbol by",
        "`tools/api-reference/generate.py`, which reuses",
        "`tools/doc-worksheet/cs_model.py` (the same C# scanner DOC-005/DOC-006's worksheet",
        "depends on) rather than a second, unverified parser. Regenerate with:",
        "",
        "```",
        "python3 tools/api-reference/generate.py --out docs/dotnet/api",
        "```",
        "",
        f"{len(pages)} pages, one per source file that declares at least one public member.",
        "A file that is entirely internal-facing (`Internal/`, most of `Testing/`) emits no",
        "page, so this count is smaller than",
        "`ls dotnet/BastionVault.IntegrationSdk/*.cs dotnet/BastionVault.IntegrationSdk/**/*.cs`.",
        "",
        "| Page | Public members |",
        "|---|---:|",
    ]
    for title, relative_path, member_count in pages:
        index_lines.append(f"| [`{title}`]({relative_path}) | {member_count} |")
    index_lines.append("")

    out_dir.joinpath("README.md").write_text("\n".join(index_lines), encoding="utf-8")

    print(f"{len(pages)} pages written to {out_dir} for {sum(c for _, _, c in pages)} public members.")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, required=True, help="output directory for the generated pages")
    parser.add_argument("--root", type=Path, default=None, help="repository root (default: inferred)")
    arguments = parser.parse_args(argv)
    root = (arguments.root or REPO_ROOT).resolve()
    try:
        return run(root, arguments.out)
    except (OSError, ValueError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
