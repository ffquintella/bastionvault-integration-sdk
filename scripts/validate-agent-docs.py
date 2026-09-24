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
  7. Harness bindings: the configuration under .claude/ exists and matches the binding
     table in agents.md section 4.1.2, so the documented routing is the routing the
     harness actually applies (BND-001, BND-002, BND-003).

Usage:
    python scripts/validate-agent-docs.py

Exit code 0 when every check passes, 1 otherwise.
"""

from __future__ import annotations

import json
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

# Model tiers are bound to the harness by alias, never by dated identifier (BND-003).
MODEL_ALIASES = {
    "haiku": "Claude Haiku 4.5",
    "sonnet": "Claude Sonnet 5",
    "opus": "Claude Opus 5",
}

# Which tree owns an agent definition, by filename prefix. Canonical: agents.md 4.5.
AGENT_FILE_TREES = {"eng-": "Engineering", "strategic-": "Strategic"}

SETTINGS = ".claude/settings.json"
AGENT_DIR = ".claude/agents"
SKILL_DIR = ".claude/skills"

# claude.md is the only document the harness loads unprompted, so these must be imported.
REQUIRED_IMPORTS = ("@agents.md", "@skills/claude/SKILLS.md")

FRONTMATTER = re.compile(r"\A---\n(.*?)\n---\n", re.DOTALL)

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

# ---------------------------------------------------------------- check 7
def frontmatter(rel: str) -> dict[str, str] | None:
    """Parse the YAML frontmatter of an agent or skill definition.

    Only flat `key: value` pairs are read, which is all these files use.
    """
    match = FRONTMATTER.match(read(rel))
    if match is None:
        return None
    fields = {}
    for line in match.group(1).splitlines():
        if ":" in line and not line.startswith((" ", "\t", "#")):
            key, _, value = line.partition(":")
            fields[key.strip()] = value.strip()
    return fields


def tree_for(stem: str) -> str | None:
    for prefix, tree in AGENT_FILE_TREES.items():
        if stem.startswith(prefix):
            return tree
    return None


bindings: dict[str, str] = {}
for row in table_rows(contents[AGENTS], "### 4.1.2 Harness bindings"):
    if len(row) < 3:
        continue
    path = re.search(r"`([^`]*\.claude/[^`]+)`", row[1])
    model = plain(row[2])
    if path and model in registry:
        bindings[path.group(1)] = model

if not bindings:
    fail("C7", f"{AGENTS} section 4.1.2: harness binding table could not be parsed")
else:
    for rel in sorted(bindings) + [
        f"{SKILL_DIR}/strategic-orchestration/SKILL.md",
        f"{SKILL_DIR}/engineering-delegation/SKILL.md",
    ]:
        if not (ROOT / rel).is_file():
            fail("C7", f"binding declared in {AGENTS} section 4.1.2 has no file: {rel}")

    # The session default, the one setting that decides which model runs unprompted.
    settings_path = ROOT / SETTINGS
    expected_default = bindings.get(SETTINGS, "Claude Sonnet 5")
    if not settings_path.is_file():
        fail("C7", f"{SETTINGS} missing: the session has no default model binding")
    else:
        try:
            settings = json.loads(settings_path.read_text(encoding="utf-8"))
        except json.JSONDecodeError as exc:
            settings = None
            fail("C7", f"{SETTINGS} is not valid JSON: {exc}")
        if settings is not None:
            alias = str(settings.get("model", ""))
            resolved = MODEL_ALIASES.get(alias)
            if resolved is None:
                fail(
                    "C7",
                    f'{SETTINGS}: "model" is {alias!r}, expected one of '
                    f"{sorted(MODEL_ALIASES)} (**BND-003**)",
                )
            elif resolved != expected_default:
                fail(
                    "C7",
                    f'{SETTINGS}: "model" resolves to {resolved}, but '
                    f"{AGENTS} section 4.1.2 binds the session default to "
                    f"{expected_default}",
                )

    # Every agent definition: parseable, self-consistent, bound to the documented rung,
    # and inside the models its own tree may run.
    agent_files = sorted((ROOT / AGENT_DIR).glob("*.md")) if (ROOT / AGENT_DIR).is_dir() else []
    if not agent_files:
        fail("C7", f"{AGENT_DIR}/ defines no agents: every delegation rung is unbound")
    for path in agent_files:
        rel = f"{AGENT_DIR}/{path.name}"
        fields = frontmatter(rel)
        if fields is None:
            fail("C7", f"{rel}: no YAML frontmatter, so the harness cannot load it")
            continue
        if fields.get("name") != path.stem:
            fail("C7", f"{rel}: name is {fields.get('name')!r}, expected {path.stem!r}")
        if not fields.get("description"):
            fail("C7", f"{rel}: description missing, so the agent is never selected")
        resolved = MODEL_ALIASES.get(fields.get("model", ""))
        if resolved is None:
            fail(
                "C7",
                f"{rel}: model is {fields.get('model')!r}, expected one of "
                f"{sorted(MODEL_ALIASES)} (**BND-003**)",
            )
            continue
        documented = bindings.get(rel)
        if documented is None:
            fail(
                "C7",
                f"{rel} exists but {AGENTS} section 4.1.2 does not bind it. "
                "Document the rung or delete the agent (**BND-002**)",
            )
        elif documented != resolved:
            fail(
                "C7",
                f"{rel}: runs {resolved}, but {AGENTS} section 4.1.2 binds it to "
                f"{documented} (**BND-002**)",
            )
        tree = tree_for(path.stem)
        if tree is None:
            fail(
                "C7",
                f"{rel}: filename names no tree. Prefix it with one of "
                f"{sorted(AGENT_FILE_TREES)} (**BND-001**)",
            )
        elif resolved not in permitted(tree):
            fail(
                "C7",
                f"{rel}: {resolved} is not permitted in the {tree} tree, which may run "
                f"{sorted(permitted(tree))} (**BND-001**)",
            )

    # Skills are only reachable if the harness can discover them.
    skill_files = sorted((ROOT / SKILL_DIR).glob("*/SKILL.md")) if (ROOT / SKILL_DIR).is_dir() else []
    if not skill_files:
        fail("C7", f"{SKILL_DIR}/*/SKILL.md missing: the routing rules are undiscoverable")
    for path in skill_files:
        rel = f"{SKILL_DIR}/{path.parent.name}/SKILL.md"
        fields = frontmatter(rel)
        if fields is None:
            fail("C7", f"{rel}: no YAML frontmatter, so the harness cannot discover it")
            continue
        if fields.get("name") != path.parent.name:
            fail(
                "C7",
                f"{rel}: name is {fields.get('name')!r}, expected {path.parent.name!r}",
            )
        if not fields.get("description"):
            fail("C7", f"{rel}: description missing, so the skill never triggers")

    # The import chain: without it, only claude.md is ever in context.
    for token in REQUIRED_IMPORTS:
        if not re.search(rf"^{re.escape(token)}\s*$", contents[CLAUDE], re.MULTILINE):
            fail(
                "C7",
                f"{CLAUDE}: missing import line '{token}'. Without it the harness never "
                "loads that document, and the policy it owns is never applied",
            )

if not any(e.startswith("[C7]") for e in errors):
    notes.append(
        f"C7 harness bindings match {AGENTS} section 4.1.2: "
        f"{len(bindings)} bindings, {len(list((ROOT / AGENT_DIR).glob('*.md')))} agents, "
        f"{len(list((ROOT / SKILL_DIR).glob('*/SKILL.md')))} skills, "
        f"imports {', '.join(REQUIRED_IMPORTS)}"
    )

# ---------------------------------------------------------------- C8
# A risk row may not name a milestone that has already exited.
#
# Why this check exists (ROADMAP.md D-7, 2026-09-24): a sweep at M12's close found
# eleven risk rows with no live owner, and six of them *read as owned* because they
# named a milestone that had since closed - "Open, owned by M10" is indistinguishable
# from genuinely handled until you check whether M10 is still open. Nobody checked for
# eleven rows. R-16's own recorded moral is that a gap booked with no owner survives a
# milestone; this is the mechanical version of remembering that.
#
# The check is deliberately narrow: it reads the milestone plan's completion markers and
# the risk register's owner phrases from ROADMAP.md itself, so it cannot drift from the
# document it polices.
#
# WHAT THIS CHECK IS LOOKING AT, AND WHAT IT IS NOT (D-M11-27, risk row R-10).
# Seeding a violation proves the predicate fires; it proves nothing about the boundary of
# the set the predicate runs over. Both were seeded here. C8 recognises exactly one
# phrasing - the literal "owned by M<n>" - and of the eleven ownerless rows found at M12's
# close it would have caught six. It would have MISSED these three, which named an owner
# without using the phrase:
#   R-30  "an M9/M10 candidate"                        - a candidate is not an owner
#   R-36  "whichever milestone next has the server capture"  - a description, not a name
#   R-25  "owned by the count-derivation session"       - a session is not a milestone
# That is a known, enumerated exclusion rather than an accident of the regex, and it is
# the reason the register's convention is now to name an owner as "owned by <milestone>"
# or to say "unowned" outright. A row that describes its owner in prose is outside this
# gate's corpus and stays a human-review item.
ROADMAP = "roadmap.md"

roadmap_path = ROOT / "ROADMAP.md"
if not roadmap_path.is_file():
    fail("C8", "ROADMAP.md missing: the risk register cannot be checked for orphaned owners")
else:
    roadmap = roadmap_path.read_text(encoding="utf-8")

    # Milestone plan rows look like:  | **M10** ✅ | ... |   - the tick is the completion mark.
    closed = {
        m.group(1)
        for m in re.finditer(r"^\|\s*\*\*(M\d+[a-z]?)\*\*\s*\u2705", roadmap, re.MULTILINE)
    }

    # Risk rows look like:  | R-32 | <statement> | <tier> | <mitigation> |
    orphans = []
    for row in re.finditer(r"^\|\s*(R-\d+)\s*\|(.*)$", roadmap, re.MULTILINE):
        rid, body = row.group(1), row.group(2)
        # A dated resolution stamp - ASSIGNED / CLOSED / MEASURED plus an ISO date - means
        # someone stated in this row what happened to it, and when. The stamp is an
        # assertion, not proof: C8 checks that a row does not silently name a departed
        # owner, never that a claim in it is true. The date is what makes a false stamp
        # auditable later.
        if re.search(r"(ASSIGNED|CLOSED|MEASURED)\s+\d{4}-\d{2}-\d{2}", body):
            continue
        for phrase in re.finditer(r"owned by (M\d+[a-z]?)", body):
            milestone = phrase.group(1)
            if milestone in closed:
                orphans.append((rid, milestone))

    for rid, milestone in orphans:
        fail(
            "C8",
            f"ROADMAP.md section 8: {rid} is 'owned by {milestone}', but {milestone} is "
            f"marked complete in section 4. A row whose owner has exited reads as handled "
            f"and is not (D-7). Re-assign it, or record that unowned is deliberate",
        )

    if not any(e.startswith("[C8]") for e in errors):
        notes.append(
            f"C8 no risk row names a closed milestone as owner: "
            f"{len(closed)} milestones complete, risk register clean"
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
