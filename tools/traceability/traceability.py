"""Requirement traceability scanner and ratchet gate.

Parser contract
===============

Only test files in the language-specific test locations are scanned.  A
canonical requirement ID is ``AREA-NNN`` where ``AREA`` is one or more upper-
case letters/digits and ``NNN`` is exactly three digits, or it is an
integration scenario ``ITG-Snn`` where ``nn`` is exactly two digits.

The accepted marker grammar is deliberately narrow:

* .NET files (``dotnet/**`` beneath a directory whose name ends in
  ``.Tests``) accept ``[Requirement("AREA-NNN")]`` and
  ``[Trait("Requirement", "AREA-NNN")]``.  Whitespace around the attribute
  name, parentheses, comma, and string is optional.  Markers may be stacked
  and are associated with the following method declaration.
* Rust files under ``rust/**/tests/**/*.rs`` and test functions inside an
  inline ``#[cfg(test)] mod ... { ... }`` in ``rust/**/src/**/*.rs`` accept a
  function name ending in one or more suffixes.  Each ordinary suffix is
  ``_<lower-case-area>_<nnn>`` and maps to ``AREA-NNN``; the scenario suffix is
  ``_itg_snn`` and maps to ``ITG-Snn``.  For example,
  ``test_kv2_004_trn_040`` maps to two IDs.
* Python files under ``python/tests/**/*.py`` accept one or more ``@req``
  tags in the first docstring statement of a function whose name starts with
  ``test``.  Each tag contains one or more canonical IDs separated by
  whitespace and/or commas, for example ``@req KV2-004, TRN-040``.

The parser does not infer IDs from production code, comments, arbitrary test
names, or non-test directories.  IDs are returned exactly as written for
the .NET/Python forms and normalised to upper case for Rust suffixes.
"""

from __future__ import annotations

import argparse
import ast
import json
import re
import sys
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, Mapping, Sequence


ID_TOKEN = r"(?:[A-Z][A-Z0-9]*-\d{3}|ITG-S\d{2})"
CANONICAL_ID_RE = re.compile(rf"^{ID_TOKEN}$")
DOTNET_REQUIREMENT_RE = re.compile(
    rf'\[\s*Requirement\s*\(\s*"({ID_TOKEN})"\s*\)\s*\]'
)
DOTNET_TRAIT_RE = re.compile(
    rf'\[\s*Trait\s*\(\s*"Requirement"\s*,\s*"({ID_TOKEN})"\s*\)\s*\]'
)
CS_FUNCTION_RE = re.compile(
    r"^\s*(?:\[[^\]]+\]\s*)*"
    r"(?:(?:public|private|protected|internal|static|async|virtual|override|"
    r"sealed|partial|unsafe|new|extern)\s+)*"
    r"(?:[A-Za-z_][A-Za-z0-9_<>.,?\[\]]*\s+)+"
    r"(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*\("
)
RUST_TEST_ATTRIBUTE_RE = re.compile(
    r"^\s*#\[\s*(?:(?:tokio::\s*)?test)(?:\s*\([^\]]*\))?\s*\]\s*$"
)
RUST_FUNCTION_RE = re.compile(r"\bfn\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)\b")
RUST_SUFFIX_RE = re.compile(
    r"(?:_(?:itg_s\d{2}|[a-z][a-z0-9]*_\d{3}))+\Z"
)
RUST_SUFFIX_PART_RE = re.compile(
    r"_(?:(?P<scenario>itg_s\d{2})|(?P<area>[a-z][a-z0-9]*)_(?P<number>\d{3}))"
)
PYTHON_REQ_TAG_RE = re.compile(
    rf"@req\b\s*((?:{ID_TOKEN})(?:[\s,]+(?:{ID_TOKEN}))*)"
)
APPENDIX_TOTAL_RE = re.compile(r"Total requirements:\s*\*\*(\d+)\*\*")
APPENDIX_HEADING_RE = re.compile(r"^##\s+All requirement IDs\s*$")
SCENARIO_HEADING_RE = re.compile(r"^###\s+Required scenarios\s*$")
NEXT_THIRD_LEVEL_HEADING_RE = re.compile(r"^###\s+")
SCENARIO_NUMBER_RE = re.compile(r"^\s*(\d+)\.\s+")


@dataclass(frozen=True, order=True)
class TestReference:
    """One requirement-to-test link emitted by a language parser."""

    requirement_id: str
    file: str
    line: int
    test_name: str

    @property
    def display(self) -> str:
        return f"{self.file}:{self.line} — {self.test_name}"


def _relative_file(path: Path, root: Path) -> str:
    try:
        return path.resolve().relative_to(root.resolve()).as_posix()
    except ValueError:
        return path.as_posix()


def _read_lines(path: Path) -> list[str]:
    return path.read_text(encoding="utf-8").splitlines()


def _is_dotnet_test_file(path: Path, root: Path) -> bool:
    try:
        relative = path.resolve().relative_to(root.resolve())
    except ValueError:
        return False
    return (
        relative.parts
        and relative.parts[0] == "dotnet"
        and path.suffix.lower() == ".cs"
        and any(part.endswith(".Tests") for part in relative.parts[:-1])
    )


def parse_dotnet_file(path: Path, root: Path) -> list[TestReference]:
    """Parse accepted .NET attributes in one eligible test file."""

    if not _is_dotnet_test_file(path, root):
        return []

    lines = _read_lines(path)
    functions: list[tuple[int, str]] = []
    for index, line in enumerate(lines):
        match = CS_FUNCTION_RE.search(line)
        if match:
            functions.append((index, match.group("name")))

    references: list[TestReference] = []
    relative_file = _relative_file(path, root)
    for index, test_name in functions:
        attribute_lines = [lines[index]]
        previous = index - 1
        while previous >= 0:
            stripped = lines[previous].strip()
            if not stripped or not stripped.startswith("["):
                break
            attribute_lines.append(lines[previous])
            previous -= 1
        block = "\n".join(reversed(attribute_lines))
        ids = DOTNET_REQUIREMENT_RE.findall(block) + DOTNET_TRAIT_RE.findall(block)
        seen: set[str] = set()
        for requirement_id in ids:
            if requirement_id in seen:
                continue
            seen.add(requirement_id)
            references.append(TestReference(requirement_id, relative_file, index + 1, test_name))
    return references


def _rust_brace_delta(line: str) -> int:
    """Count structural braces for the simple inline cfg-module scanner."""

    without_comment = line.split("//", 1)[0]
    without_strings = re.sub(r'"(?:\\.|[^"\\])*"', "", without_comment)
    return without_strings.count("{") - without_strings.count("}")


def _cfg_test_module_ranges(lines: Sequence[str]) -> list[tuple[int, int]]:
    ranges: list[tuple[int, int]] = []
    for index, line in enumerate(lines):
        if not re.match(r"^\s*#\[\s*cfg\s*\(\s*test\s*\)\s*\]\s*$", line):
            continue
        module_index = index + 1
        while module_index < len(lines) and module_index <= index + 10:
            module_line = lines[module_index]
            if re.search(r"\bmod\s+[A-Za-z_][A-Za-z0-9_]*\s*\{", module_line):
                break
            if module_line.strip() and not module_line.strip().startswith(("#", "//")):
                module_line = ""
                break
            module_index += 1
        if not module_line:
            continue
        balance = 0
        end_index = module_index
        for end_index in range(module_index, len(lines)):
            balance += _rust_brace_delta(lines[end_index])
            if balance <= 0:
                break
        else:
            end_index = len(lines) - 1
        ranges.append((module_index, end_index))
    return ranges


def _rust_suffix_ids(test_name: str) -> list[str]:
    suffix = RUST_SUFFIX_RE.search(test_name)
    if suffix is None:
        return []
    ids: list[str] = []
    for part in RUST_SUFFIX_PART_RE.finditer(suffix.group(0)):
        scenario = part.group("scenario")
        if scenario:
            requirement_id = "ITG-S" + scenario.rsplit("_s", 1)[1]
        else:
            requirement_id = f"{part.group('area').upper()}-{part.group('number')}"
        if requirement_id not in ids:
            ids.append(requirement_id)
    return ids


def _is_rust_test_file(path: Path, root: Path) -> tuple[bool, bool]:
    try:
        relative = path.resolve().relative_to(root.resolve())
    except ValueError:
        return False, False
    if not relative.parts or relative.parts[0] != "rust" or path.suffix.lower() != ".rs":
        return False, False
    if "tests" in relative.parts[:-1]:
        return True, True
    if "src" in relative.parts[:-1]:
        return True, False
    return False, False


def parse_rust_file(path: Path, root: Path) -> list[TestReference]:
    """Parse Rust test-name suffixes in eligible test functions."""

    eligible, is_tests_directory = _is_rust_test_file(path, root)
    if not eligible:
        return []

    lines = _read_lines(path)
    allowed_ranges = [(0, len(lines) - 1)] if is_tests_directory else _cfg_test_module_ranges(lines)

    def allowed(index: int) -> bool:
        return any(start <= index <= end for start, end in allowed_ranges)

    references: list[TestReference] = []
    relative_file = _relative_file(path, root)
    pending_attribute_line: int | None = None
    for index, line in enumerate(lines):
        if RUST_TEST_ATTRIBUTE_RE.match(line):
            pending_attribute_line = index
            continue
        if pending_attribute_line is None:
            continue
        stripped = line.strip()
        if not stripped or stripped.startswith(("#[", "//")):
            continue
        function = RUST_FUNCTION_RE.search(line)
        pending_attribute_line = None
        if function is None or not allowed(index):
            continue
        test_name = function.group("name")
        for requirement_id in _rust_suffix_ids(test_name):
            references.append(TestReference(requirement_id, relative_file, index + 1, test_name))
    return references


def _is_python_test_file(path: Path, root: Path) -> bool:
    try:
        relative = path.resolve().relative_to(root.resolve())
    except ValueError:
        return False
    return (
        len(relative.parts) >= 3
        and relative.parts[0] == "python"
        and relative.parts[1] == "tests"
        and path.suffix.lower() == ".py"
    )


def parse_python_file(path: Path, root: Path) -> list[TestReference]:
    """Parse ``@req`` tags from Python test-function docstrings."""

    if not _is_python_test_file(path, root):
        return []

    source = path.read_text(encoding="utf-8")
    tree = ast.parse(source, filename=str(path))
    references: list[TestReference] = []
    relative_file = _relative_file(path, root)
    for node in ast.walk(tree):
        if not isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            continue
        if not node.name.startswith("test"):
            continue
        docstring = ast.get_docstring(node, clean=False)
        if not docstring:
            continue
        for tag in PYTHON_REQ_TAG_RE.finditer(docstring):
            for requirement_id in re.findall(ID_TOKEN, tag.group(1)):
                references.append(TestReference(requirement_id, relative_file, node.lineno, node.name))
    return references


def parse_appendix_d(path: Path) -> set[str]:
    """Read the first column of Appendix D's per-area ID tables.

    The table count is checked against the document's ``Total requirements``
    declaration so a malformed or stale appendix cannot silently shrink the
    applicable set.
    """

    lines = _read_lines(path)
    total_matches = [APPENDIX_TOTAL_RE.search(line) for line in lines]
    total_matches = [match for match in total_matches if match is not None]
    if len(total_matches) != 1:
        raise ValueError(f"expected exactly one Appendix D total, found {len(total_matches)}")
    expected_total = int(total_matches[0].group(1))

    try:
        heading_index = next(
            index for index, line in enumerate(lines) if APPENDIX_HEADING_RE.match(line.strip())
        )
    except StopIteration as error:
        raise ValueError("Appendix D section '## All requirement IDs' was not found") from error

    ids: set[str] = set()
    for line in lines[heading_index + 1 :]:
        if re.match(r"^##\s+", line):
            break
        match = re.match(r"^\s*\|\s*([A-Z][A-Z0-9]*-\d{3})\s*\|", line)
        if match:
            ids.add(match.group(1))

    if len(ids) != expected_total:
        raise ValueError(f"Appendix D total mismatch: expected {expected_total} IDs, parsed {len(ids)}")
    return ids


def derive_integration_scenarios(path: Path) -> set[str]:
    """Derive ``ITG-Snn`` IDs from the numbered Required-scenarios list."""

    lines = _read_lines(path)
    try:
        heading_index = next(
            index for index, line in enumerate(lines) if SCENARIO_HEADING_RE.match(line.strip())
        )
    except StopIteration as error:
        raise ValueError("Required scenarios heading was not found") from error

    numbers: list[int] = []
    for line in lines[heading_index + 1 :]:
        if NEXT_THIRD_LEVEL_HEADING_RE.match(line):
            break
        match = SCENARIO_NUMBER_RE.match(line)
        if match:
            number = int(match.group(1))
            if number <= 0 or number in numbers:
                raise ValueError(f"invalid or duplicate Required scenario number: {number}")
            numbers.append(number)
    if not numbers:
        raise ValueError("Required scenarios list is empty")
    return {f"ITG-S{number:02d}" for number in numbers}


def scan_tests(root: Path) -> dict[str, list[TestReference]]:
    """Scan all three SDK test suites and group references by requirement ID."""

    coverage: dict[str, list[TestReference]] = defaultdict(list)
    for path in sorted(root.rglob("*.cs")):
        for reference in parse_dotnet_file(path, root):
            coverage[reference.requirement_id].append(reference)
    for path in sorted(root.rglob("*.rs")):
        for reference in parse_rust_file(path, root):
            coverage[reference.requirement_id].append(reference)
    for path in sorted(root.rglob("*.py")):
        for reference in parse_python_file(path, root):
            coverage[reference.requirement_id].append(reference)
    for references in coverage.values():
        references.sort(key=lambda item: (item.file, item.line, item.test_name, item.requirement_id))
    return dict(sorted(coverage.items()))


def load_baseline(path: Path) -> set[str]:
    """Load a baseline JSON array, also accepting common object wrappers."""

    data = json.loads(path.read_text(encoding="utf-8"))
    if isinstance(data, list):
        values = data
    elif isinstance(data, dict):
        values = data.get("baseline", data.get("uncovered", data.get("ids")))
        if values is None:
            raise ValueError("baseline JSON object must contain baseline, uncovered, or ids")
    else:
        raise ValueError("baseline JSON must be an array or object containing an ID array")
    if not isinstance(values, list) or not all(isinstance(value, str) for value in values):
        raise ValueError("baseline IDs must be a JSON array of strings")
    return set(values)


def write_baseline(path: Path, ids: Iterable[str]) -> None:
    """Write a sorted, stable JSON baseline."""

    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(sorted(set(ids)), indent=2) + "\n", encoding="utf-8")


def evaluate_gate(
    applicable: set[str], coverage: Mapping[str, Sequence[TestReference]], baseline: set[str]
) -> list[str]:
    """Return sorted IDs that violate the M0 ratchet gate."""

    covered = {requirement_id for requirement_id, references in coverage.items() if references}
    missing = applicable - covered - baseline
    stale = applicable & covered & baseline
    outside = baseline - applicable
    return sorted(missing | stale | outside)


def build_report(
    applicable: set[str], coverage: Mapping[str, Sequence[TestReference]], baseline: set[str]
) -> dict[str, object]:
    """Build the stable machine-readable report model."""

    requirements: list[dict[str, object]] = []
    area_counts: dict[str, dict[str, int]] = defaultdict(
        lambda: {"total": 0, "covered": 0, "baselined": 0, "uncovered": 0}
    )
    for requirement_id in sorted(applicable):
        references = sorted(
            coverage.get(requirement_id, []),
            key=lambda item: (item.file, item.line, item.test_name),
        )
        is_covered = bool(references)
        is_baselined = requirement_id in baseline
        status = "covered" if is_covered else ("baselined" if is_baselined else "uncovered")
        area = requirement_id.split("-", 1)[0]
        area_counts[area]["total"] += 1
        area_counts[area]["covered"] += int(is_covered)
        area_counts[area]["baselined"] += int(is_baselined)
        area_counts[area]["uncovered"] += int(not is_covered)
        requirements.append(
            {
                "id": requirement_id,
                "area": area,
                "status": status,
                "baselined": is_baselined,
                "tests": [reference.display for reference in references],
            }
        )

    summary = {
        "covered": sum(1 for item in requirements if item["status"] == "covered"),
        "baselined": sum(1 for item in requirements if item["baselined"]),
        "uncovered": sum(1 for item in requirements if not item["tests"]),
        "total": len(requirements),
        "by_area": {area: area_counts[area] for area in sorted(area_counts)},
    }
    return {"summary": summary, "requirements": requirements}


def render_report(
    out_dir: Path, applicable: set[str], coverage: Mapping[str, Sequence[TestReference]], baseline: set[str]
) -> None:
    """Write human and machine report artefacts to ``out_dir``."""

    out_dir.mkdir(parents=True, exist_ok=True)
    report = build_report(applicable, coverage, baseline)
    out_dir.joinpath("report.json").write_text(
        json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )

    summary = report["summary"]
    assert isinstance(summary, dict)
    lines = [
        "# Requirement traceability report",
        "",
        (
            f"Covered: {summary['covered']} · Baselined: {summary['baselined']} · "
            f"Uncovered: {summary['uncovered']} · Total: {summary['total']}"
        ),
        "",
        "## Summary by area",
        "",
        "| Area | Total | Covered | Baselined | Uncovered |",
        "|------|------:|--------:|----------:|----------:|",
    ]
    by_area = summary["by_area"]
    assert isinstance(by_area, dict)
    for area, counts in by_area.items():
        lines.append(
            f"| {area} | {counts['total']} | {counts['covered']} | "
            f"{counts['baselined']} | {counts['uncovered']} |"
        )
    lines.extend(
        [
            "",
            "## Requirement coverage",
            "",
            "| ID | Status | Baselined | Covering tests |",
            "|----|--------|-----------|----------------|",
        ]
    )
    for item in report["requirements"]:
        assert isinstance(item, dict)
        tests = "<br>".join(item["tests"]) or "—"
        lines.append(
            f"| {item['id']} | {item['status']} | {item['baselined']} | {tests} |"
        )
    out_dir.joinpath("report.md").write_text("\n".join(lines) + "\n", encoding="utf-8")


def _applicable_ids(root: Path) -> set[str]:
    appendix = root / "specifications" / "appendix-d-requirement-index.md"
    testing = root / "specifications" / "15-testing-requirements.md"
    return parse_appendix_d(appendix) | derive_integration_scenarios(testing)


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__.split("\n", 1)[0])
    modes = parser.add_mutually_exclusive_group(required=True)
    modes.add_argument("--check", action="store_true", help="run the ratchet gate")
    modes.add_argument(
        "--write-baseline", action="store_true", help="regenerate the committed baseline"
    )
    parser.add_argument("--out", type=Path, help="directory for report.md and report.json")
    parser.add_argument(
        "--root",
        type=Path,
        help="repository root (defaults to the parent of the tools directory)",
    )
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    parser = _build_parser()
    args = parser.parse_args(argv)
    root = (args.root or Path(__file__).resolve().parents[2]).resolve()
    try:
        applicable = _applicable_ids(root)
        coverage = scan_tests(root)
        baseline_path = root / "tools" / "traceability" / "baseline.json"
        if args.write_baseline:
            covered = {requirement_id for requirement_id, references in coverage.items() if references}
            baseline = applicable - covered
            write_baseline(baseline_path, baseline)
            if args.out:
                render_report(args.out, applicable, coverage, baseline)
            print(f"wrote baseline: {len(baseline)} IDs")
            return 0

        baseline = load_baseline(baseline_path)
        if args.out:
            render_report(args.out, applicable, coverage, baseline)
        violations = evaluate_gate(applicable, coverage, baseline)
        for requirement_id in violations:
            print(f"OFFENDING {requirement_id}")
        covered_count = sum(1 for requirement_id in applicable if coverage.get(requirement_id))
        baselined_count = len(applicable & baseline)
        print(
            f"covered: {covered_count} / baselined: {baselined_count} / total: {len(applicable)}"
        )
        return 1 if violations else 0
    except (OSError, ValueError, json.JSONDecodeError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
