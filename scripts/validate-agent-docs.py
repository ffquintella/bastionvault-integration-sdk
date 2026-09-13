#!/usr/bin/env python3
"""Consistency validator for the multi-agent orchestration documents.

Enforces the five guarantees listed in agents.md section 10:

  1. All four agent documents exist at their specified paths.
  2. The token-optimization block is byte-identical in all four.
  3. Every model named in the documents exists in the agents.md model registry.
  4. Agent-count and parallelism limits agree across all four documents.
  5. No responsibility is owned by both orchestrators.
  6. Tree isolation: every model is a Claude model, each routing document names only the
     models its own tree is permitted to run, and both agree with the agents.md registry.

Usage:
    python scripts/validate-agent-docs.py

Exit code 0 when every check passes, 1 otherwise.
"""

from __future__ import annotations

import pathlib
import re
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = pathlib.Path(__file__).resolve().parent.parent

AGENTS = "agents.md"
CLAUDE = "claude.md"
CLAUDE_SKILLS = "skills/claude/SKILLS.md"
CODEX_SKILLS = "skills/codex/SKILLS.md"
DOCS = [AGENTS, CLAUDE, CLAUDE_SKILLS, CODEX_SKILLS]

BEGIN = "<!-- BEGIN:TOKEN-OPTIMIZATION-POLICY -->"
END = "<!-- END:TOKEN-OPTIMIZATION-POLICY -->"

# Canonical agent-count policy, defined once in agents.md section 7.1.
SIZING = {
    "Simple": "1",
    "Medium": "2–3",
    "Large": "3–5",
    "Enterprise": "5–8",
    "Hard maximum": "10",
}
CLAUDE_PARALLEL_LIMIT = "8"
CODEX_PARALLEL_LIMIT = "10"
SYSTEM_PARALLEL_LIMIT = "10"

# Any token matching this shape is treated as a model name and must be registered.
# GPT, Gemini and Fable stay in the pattern deliberately: they are retired, so any
# surviving mention becomes an unregistered-model failure rather than passing silently.
# An unversioned "Claude Sonnet" also fails, because the registry names versions.
MODEL_PATTERN = re.compile(
    r"GPT-\d+(?:\s+Mini)?|GPT\s+[A-Z][a-z]+"
    r"|Claude\s+(?:Sonnet|Opus|Haiku|Fable)(?:\s+\d+(?:\.\d+)*)?"
    r"|Gemini\s+[A-Z][a-z]+|(?<![\w-])Fable(?![\w-])"
)

# The only family either tree may run. Canonical source: agents.md section 4.1.
ONLY_FAMILY = "Claude"
TREES = ("Strategic", "Engineering")

errors: list[str] = []
notes: list[str] = []


def fail(check: str, message: str) -> None:
    errors.append(f"[{check}] {message}")


def read(rel: str) -> str:
    return (ROOT / rel).read_text(encoding="utf-8")


def extract_block(text: str, rel: str) -> str | None:
    start = text.find(BEGIN)
    stop = text.find(END)
    if start == -1 or stop == -1:
        fail("C2", f"{rel}: token-optimization markers missing")
        return None
    return text[start : stop + len(END)]


HEADING_PATTERN = re.compile(r"^#{2,4} ", re.MULTILINE)


def table_rows(text: str, heading: str) -> list[list[str]]:
    """Return the cells of every markdown table row under `heading`.

    The section ends at the next markdown heading of any level, so a table in
    section 4.1 never leaks rows from section 4.2.
    """
    start = text.find(heading)
    if start == -1:
        return []
    nxt = HEADING_PATTERN.search(text, start + len(heading))
    section = text[start : nxt.start() if nxt else len(text)]
    rows = []
    for line in section.splitlines():
        line = line.strip()
        if not line.startswith("|"):
            continue
        cells = [c.strip() for c in line.strip("|").split("|")]
        if all(set(c) <= set("-: ") for c in cells):
            continue
        rows.append(cells)
    return rows


def plain(cell: str) -> str:
    return cell.replace("*", "").replace("`", "").strip()


# ---------------------------------------------------------------- check 1
missing = [rel for rel in DOCS if not (ROOT / rel).is_file()]
for rel in missing:
    fail("C1", f"required document missing: {rel}")
if missing:
    print("\n".join(errors))
    sys.exit(1)

contents = {rel: read(rel) for rel in DOCS}
notes.append(f"C1 four agent documents present: {', '.join(DOCS)}")

# ---------------------------------------------------------------- check 2
blocks = {rel: extract_block(contents[rel], rel) for rel in DOCS}
if all(b is not None for b in blocks.values()):
    canonical = blocks[AGENTS]
    for rel in DOCS[1:]:
        if blocks[rel] != canonical:
            fail(
                "C2",
                f"{rel}: token-optimization block differs from the canonical block "
                f"in {AGENTS}. Re-copy it verbatim.",
            )
    if not errors:
        rules = len(re.findall(r"\*\*TOK-\d+\*\*", canonical))
        notes.append(
            f"C2 token-optimization block byte-identical in all four documents "
            f"({len(canonical)} bytes, {rules} TOK rules)"
        )

# ---------------------------------------------------------------- check 3
registry_rows = table_rows(contents[AGENTS], "### 4.1 Registry")
registry = {plain(r[0]) for r in registry_rows[1:] if r and plain(r[0])}
family_of = {
    plain(r[0]): plain(r[1]) for r in registry_rows[1:] if len(r) >= 2 and plain(r[0])
}
tree_of = {
    plain(r[0]): plain(r[2]) for r in registry_rows[1:] if len(r) >= 3 and plain(r[0])
}
if not registry:
    fail("C3", f"{AGENTS}: model registry in section 4.1 could not be parsed")
else:
    for rel in DOCS:
        for match in MODEL_PATTERN.finditer(contents[rel]):
            name = re.sub(r"\s+", " ", match.group(0)).strip()
            if name not in registry:
                line = contents[rel][: match.start()].count("\n") + 1
                fail("C3", f"{rel}:{line}: unregistered model '{name}'")
    if not errors:
        notes.append(f"C3 every model reference resolves to the registry: {sorted(registry)}")

# ---------------------------------------------------------------- check 4
sizing_rows = table_rows(contents[AGENTS], "### 7.1 Sizing table")
parsed_sizing = {}
for row in sizing_rows[1:]:
    if len(row) >= 3:
        parsed_sizing[plain(row[0])] = plain(row[2])
if parsed_sizing != SIZING:
    fail(
        "C4",
        f"{AGENTS} section 7.1 sizing table is {parsed_sizing}, expected {SIZING}. "
        "Update SIZING in this script only if the policy itself changed.",
    )

sizing_sentence = (
    "simple 1, medium 2–3, large 3–5, enterprise 5–8, hard maximum 10"
)
for rel in (CLAUDE_SKILLS, CODEX_SKILLS):
    if sizing_sentence not in re.sub(r"\s+", " ", contents[rel]):
        fail("C4", f"{rel}: agent sizing policy does not restate '{sizing_sentence}'")

limit_checks = [
    (CLAUDE_SKILLS, "Maximum parallel agents (Claude side)", CLAUDE_PARALLEL_LIMIT),
    (CLAUDE_SKILLS, "System-wide ceiling shared with Codex", SYSTEM_PARALLEL_LIMIT),
    (CODEX_SKILLS, "Hard maximum agents (Codex side)", CODEX_PARALLEL_LIMIT),
    (CODEX_SKILLS, "System-wide ceiling shared with Claude", SYSTEM_PARALLEL_LIMIT),
]
for rel, label, expected in limit_checks:
    row = next(
        (ln for ln in contents[rel].splitlines() if label in ln and ln.startswith("|")),
        None,
    )
    if row is None:
        fail("C4", f"{rel}: parallelism limit row '{label}' not found")
    elif plain(row.strip("|").split("|")[1]) != expected:
        fail("C4", f"{rel}: '{label}' must be {expected}, found '{row.strip()}'")

agents_limits = table_rows(contents[AGENTS], "### 7.2 Parallelism limits")
agents_limit_values = {plain(r[0]): plain(r[1]) for r in agents_limits[1:] if len(r) >= 2}
for label, expected in (
    ("Claude-side concurrent agents (skills/claude/SKILLS.md)", CLAUDE_PARALLEL_LIMIT),
    ("Codex-side concurrent agents (skills/codex/SKILLS.md)", CODEX_PARALLEL_LIMIT),
    ("System-wide concurrent agents", SYSTEM_PARALLEL_LIMIT),
):
    if agents_limit_values.get(label) != expected:
        fail(
            "C4",
            f"{AGENTS} section 7.2: '{label}' must be {expected}, "
            f"found '{agents_limit_values.get(label)}'",
        )

if not any(e.startswith("[C4]") for e in errors):
    notes.append(
        f"C4 agent limits agree: sizing {SIZING}, Claude {CLAUDE_PARALLEL_LIMIT}, "
        f"Codex {CODEX_PARALLEL_LIMIT}, system-wide {SYSTEM_PARALLEL_LIMIT}"
    )

# ---------------------------------------------------------------- check 5
def owned(rel: str) -> set[str]:
    rows = table_rows(contents[rel], "## 1. Responsibilities owned here")
    return {plain(r[1]).lower() for r in rows[1:] if len(r) >= 2 and plain(r[1])}


claude_owned = owned(CLAUDE_SKILLS)
codex_owned = owned(CODEX_SKILLS)
if not claude_owned or not codex_owned:
    fail("C5", "responsibility tables in one or both SKILLS.md files could not be parsed")
else:
    overlap = claude_owned & codex_owned
    if overlap:
        fail("C5", f"responsibility owned by both orchestrators: {sorted(overlap)}")
    else:
        notes.append(
            f"C5 responsibilities disjoint: Claude owns {len(claude_owned)}, "
            f"Codex owns {len(codex_owned)}, overlap 0"
        )

# ---------------------------------------------------------------- check 6
def models_in(rel: str, heading: str) -> set[str]:
    """Model names appearing in the tables under `heading`."""
    found = set()
    for row in table_rows(contents[rel], heading):
        for cell in row:
            for m in MODEL_PATTERN.finditer(cell):
                found.add(re.sub(r"\s+", " ", m.group(0)).strip())
    return found


def permitted(tree: str) -> set[str]:
    """Models the registry places in `tree`. A 'Both' row counts for either tree."""
    return {m for m, t in tree_of.items() if t in (tree, "Both")}


if family_of and set(family_of.values()) == {ONLY_FAMILY}:
    # Every registry row must name a tree the policy knows about.
    for model, tree in tree_of.items():
        if tree not in TREES and tree != "Both":
            fail("C6", f"{AGENTS} section 4.1: model '{model}' names unknown tree '{tree}'")

    # agents.md section 4.5 permitted-models table must match the registry.
    for row in table_rows(contents[AGENTS], "### 4.5 Tree isolation rule"):
        tree = plain(row[0])
        if tree not in TREES or len(row) < 3:
            continue
        listed = {m.strip() for m in plain(row[2]).split(",") if m.strip()}
        expected = permitted(tree)
        if listed != expected:
            fail(
                "C6",
                f"{AGENTS} section 4.5: {tree} tree lists {sorted(listed)}, "
                f"registry says {sorted(expected)}",
            )

    # A routing file's direct-use table may only name models its own tree may run.
    direct_use = [
        (CLAUDE_SKILLS, "### 2.1 Models Claude runs directly", "Strategic"),
        (CODEX_SKILLS, "## 2. Model preference", "Engineering"),
    ]
    for rel, heading, tree in direct_use:
        named = models_in(rel, heading)
        if not named:
            fail("C6", f"{rel}: no models found under '{heading}'")
            continue
        wrong = named - permitted(tree)
        if wrong:
            fail(
                "C6",
                f"{rel} '{heading}' runs models directly, so it may only name models "
                f"the {tree} tree is permitted to run. Found {sorted(wrong)}",
            )

    if not any(e.startswith("[C6]") for e in errors):
        notes.append(
            f"C6 tree isolation holds: Strategic tree {sorted(permitted('Strategic'))}, "
            f"Engineering tree {sorted(permitted('Engineering'))}, "
            f"one family only ({ONLY_FAMILY})"
        )
else:
    fail(
        "C6",
        f"{AGENTS} section 4.1: Family column missing, or names a family other than "
        f"{ONLY_FAMILY}",
    )

# ---------------------------------------------------------------- report
if errors:
    print("FAIL: agent documents are inconsistent\n")
    for e in errors:
        print(f"  {e}")
    sys.exit(1)

print("PASS: agent documents are consistent\n")
for n in notes:
    print(f"  {n}")
sys.exit(0)
