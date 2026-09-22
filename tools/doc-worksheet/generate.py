#!/usr/bin/env python3
"""Generate the DOC-005 worksheet for every public .NET SDK operation (DR-0018 D-M11-20).

    python tools/doc-worksheet/generate.py --out build/doc-worksheet

Writes ``report.json`` (machine-readable, one row per public operation) and
``report.md`` (human-readable summary + table) to ``--out``. Read-only with respect to
the SDK: this tool never edits a ``.cs`` file, `PublicApiSurface.txt`, or anything under
``specifications/`` (R1; see the slice brief). It exists so a later doc-comment slice
transcribes verified data instead of re-deriving the HTTP call, the canonical name, and
the Appendix A level by re-reading the implementation each time (DR-0018 D-M11-20).
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parent
if str(TOOL_DIR) not in sys.path:
    sys.path.insert(0, str(TOOL_DIR))

import worksheet  # noqa: E402

REPO_ROOT = TOOL_DIR.parents[1]


def run(root: Path, out_dir: Path) -> int:
    rows = worksheet.build_rows(root)
    out_dir.mkdir(parents=True, exist_ok=True)
    out_dir.joinpath("report.json").write_text(
        json.dumps(worksheet.to_json_document(rows), indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )
    out_dir.joinpath("report.md").write_text(worksheet.render_markdown(rows), encoding="utf-8")

    summary = worksheet.build_summary(rows)
    print(
        f"{summary['total_operations']} public operations; "
        f"{summary['http_verb_and_path_resolved']} verb+path resolved, "
        f"{summary['http_no_call_client_side_helper']} no HTTP call, "
        f"{summary['http_ambiguous_multiple_calls']} ambiguous (multiple calls), "
        f"{summary['http_unresolved_other']} unresolved; "
        f"{summary['appendix_a_exact_match']} Appendix A exact matches, "
        f"{summary['appendix_a_approximate_match']} approximate, "
        f"{summary['appendix_a_no_match']} no match; "
        f"{summary['already_tagged']} already carry a <spec> tag."
    )
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, required=True, help="directory for report.json and report.md")
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
