# DR-0013 — M8: Transit, TOTP and request efficiency in .NET

**Status:** **proposed** — authored by the Strategic Orchestrator as the milestone's framing
record, before any slice is dispatched. Each slice appends its own `D-M8-n` entries below
rather than opening a second record. Awaiting Strategic-tree Claude Opus 5 architecture
review (`agents.md` §4.2 row 4, §4.4), **revision 1** — no architecture-review round yet.
**Risk tier:** R3 (`agents.md` §5.3). Transit is crypto and datakey material; the rate gate
changes the observable behaviour of every request a released consumer makes; slice a changes
generated recognition behaviour for a code shipped since `v0.5.0`. The tier is assigned here,
before dispatch, and each slice's brief carries it (**CRS-001**).
**Milestone:** M8, five slices · **Date:** 2026-09-18
**Supersedes nothing. Amends:** nothing in `specifications/`.
**Discharges:** **R-23** (slice a), the M8 half of [DR-0009](0009-m4-kv-engine.md) D-M4-2
(`KV-010`, waiting on `BAT-007`), and [DR-0012](0012-m7-system-api-remainder.md) D-M7-36
(the `RestoreAsync` remap that was designed to delete).
**Inherits:** [DR-0004](0004-m1b-transport.md) (the one transport seam, the single retry
loop, D-M1b-16's pause-only half of the rate gate),
[DR-0005](0005-m1c-error-model.md) (recognition, enrichment, the generated catalogue
D-M1c-1, and **D-M1c-25**: a deferred branch returns the value the specification names,
never a plausible guess), [DR-0009](0009-m4-kv-engine.md) (D-M4-2's
deferral-with-a-named-owner, D-M4-8's fixture-authoring precedent),
[DR-0012](0012-m7-system-api-remainder.md) (the `Page<T>` shape landed at `SYS-060`).

## Problem

`ROADMAP.md` §4 books M8 as 38 requirement IDs — `TRS` 7, `TOT` 4, `BAT` 8, `PAG` 7,
`CCH` 6, `EFF` 6 — at Enterprise size and R3 risk, with `KV-010` a 39th that M4 parked on
`BAT-007`. That is wider than one reviewable handback and wider than one Large brief
(**TOK-011**), so it is decomposed, not granted a larger budget.

Three things about M8 are decided here rather than inside a slice, because a delegate that
reopened any of them would be re-litigating a Strategic call (**FAM-002**).

## Decisions

### D-M8-1 — R-23 is M8's first slice, not a later repair

`ROADMAP.md` R-23 records that `tools/error-catalogue` compiles Appendix B §2's `+ a/b/c`
qualifier as a **conjunction** where the appendix means **alternation**, disabling five
recognition rules. One of the five is `BV-TRANSIT-004`, which this milestone lands. Writing
Transit on top of a recognition rule that cannot fire would produce a slice whose tests pass
for the wrong reason, which is R-19's shape and the failure R-23 point 4 already names.

**Decision:** the generator fix is slice **a**, dispatched and reviewed before Transit opens.

**Rejected:** *fix it inside the Transit slice.* Rejected because the fix changes generated
recognition behaviour for `BV-AUTH-011`, shipped since `v0.5.0`, in all three languages —
that is a published-artefact change (R3, CRS-004) and it must be reviewed on its own
evidence, not bundled with a new engine's diff where a reviewer reads it as incidental.

### D-M8-2 — inside a qualifier, `/` and `,` are alternation

Appendix B §2 uses `/` as alternation everywhere else in the table, and row 283
(`` `version ` + contains `is below min_decryption_version` / `not found on key` ``) settles
the intent past argument: a single message cannot be both. Line 270's parenthesised
`` (`is not bound`, `is not approved`) `` is the same construct with a comma.

**Decision:** a qualifier group is ANDed with the stem literal, and the items **within** a
qualifier group are alternatives. The compiled rule gains `containsAny` alongside the
existing `containsAll`; `containsAll` stays in the schema and the three emitters for a
future genuine conjunction and is emitted empty for every rule Appendix B carries today.

**Rejected:** *reinterpret `containsAll` in place.* Rejected because it leaves the schema
lying about its own field name in three languages, and a later genuine conjunction would
have no way to say so.

### D-M8-3 — the generator fix is not done until a single-alternative fixture exists

R-23 point 4: `specifications/fixtures/errors/errors.recognition.bv-input-102.1.json`
answers a message containing *both* alternatives, so it passes identically under either
semantics. A fix that leaves the corpus unchanged leaves the corpus certifying the bug, and
Rust and Python would inherit the false confidence at Stage 2.

**Decision:** slice a authors one fixture per affected code that exercises **one** alternative
in isolation, and the pre-fix generator must be shown to fail it. Affected: `BV-INPUT-102`,
`BV-INPUT-103`, `BV-AUTH-011`, `BV-TRANSIT-004`, `BV-SSH-005`.

### D-M8-4 — `Sys.Batch` and `Kv.ReadMany` land with the rate gate, not apart from it

`EFF-001` routes every outgoing request through a token bucket; `BAT-007` falls back to
sequential reads *through the rate gate*; `PAG-004`'s iterator honours it. The gate is the
shared premise of section 14, so it lands in the same slice as the first consumer that
depends on its behaviour rather than being retro-fitted under one.

### D-M8-5 — `Page<Namespace>` is not re-shaped in M8

`06-system-api.md:203` says `Page<Namespace>` and `14-batch-and-request-efficiency.md:123`
says `Page<NamespaceSummary>`. M7 followed the owning section and shipped `Page<Namespace>`
(`SYS-060`). Re-shaping it now would be a breaking change to a released public API in
service of a contradiction that `specifications/` still has to resolve.

**Decision:** M8 extends `Page<T>` with the `PAG-001`…`PAG-007` behaviour and leaves
`Page<Namespace>`'s element type alone. The contradiction stays recorded and R3, owned by
the project owner, and is one of the four items `ROADMAP.md` §2 requires closed before the
Rust pass opens.

### D-M8-6 — M8 exits declaring no conformance level

`ROADMAP.md` §4 states M8's exit gate as "conformance level `Standard` declared". **R-14**
records that this gate is unsatisfiable as sequenced: `CNF-002` forbids claiming a level
whose sections carry unimplemented MUSTs, and sections 16–17 (`DOC`, 21 IDs) are M11's.

**Decision:** M8 exits on its requirement-ID content, declares no level, and updates
`dotnet/README.md`'s `CNF-002` gap list — exactly M4's precedent (DR-0009 D-M4-3). The
resequencing remains §10 question 4, a project-owner decision Claude has not taken.

### D-M8-7 — `PAG-004` is booked against the areas that exist

Section 14 lists seven `*-info` endpoints. Five of them (`PKI` ×3, `SSH`, cert lifecycle)
belong to areas M9 and M10 have not built. M8 lands the shared machinery — `Page<T>`
validation, the zipped record/key pairing, the cursor iterator with its `MaxRecords` cap —
and wires the two areas that exist today: `Sys.ListNamespacesInfo` and
`Auth.Userpass.ListUsersInfo`. Any `PAG` ID that cannot be honestly covered by a test in M8
stays baselined with **M9** or **M10** named as its owner, per D-M4-2's
deferral-with-a-named-owner rule. The slice reports which; it does not guess.

## Slices, rungs and the recorded escalation triggers

Recorded **before dispatch** (`agents.md` §4.3 rule 4, **TOK-012**).

| Slice | Content | IDs | Rung | Trigger for a rung above row 2 |
|-------|---------|-----|------|-------------------------------|
| **a** | R-23: alternation in the recognition qualifier; regenerate all three languages; single-alternative fixtures; delete D-M7-36's remap | — (repairs 5 rules) | `eng-deep` | Row 3 **(b)**: a change to an existing cross-language contract and a published artefact — generated recognition behaviour for `BV-AUTH-011`, shipped since `v0.5.0` |
| **b** | Transit engine, section 08 | `TRS-001`…`013` (7) | `eng-implementation` | none — section 08's operation table pins the contract; this is row 2 |
| **c** | TOTP engine, section 11 | `TOT-001`…`004` (4) | `eng-implementation` | none — row 2 |
| **d** | Rate gate token bucket + FIFO; `Sys.Batch`; `Kv.ReadMany`; `KV-010` | `EFF` (6), `BAT` (8), `KV-010` | `eng-deep` | Row 3 **(d)**: spans 3+ components — the transport executor, the discovery probe path (`EFF-005`), client configuration, and every engine client's request path |
| **e** | Cursor pagination + cache coherence + section 14's documentation guidance | `PAG` (7), `CCH` (6) | `eng-implementation` | none — `Page<T>` already shipped at `SYS-060`; this is extension against a settled shape (D-M8-5) |

**Sequencing.** a → (b ∥ c) → d → e. Slice a gates b because it owns `BV-TRANSIT-004`.
Slices b and c touch disjoint files and share no decision (§7.4). Slice d touches the
transport executor every other slice routes through, so it runs after b and c rather than
beside them. Slice e depends on d's gate for `PAG-004`. Only one `eng-deep` agent runs at a
time (`skills/claude/SKILLS.md` §8: concurrent Claude Opus 5 agents, 1).

**Gate.** Every slice is R2 or above, so the handback gate is `strategic-review`, never a
re-read by the author (`agents.md` §4.4, **REV-002**).

---

## Slice a handback: rulings taken inside the fix

Authored by the Engineering deep worker (`eng-deep`, Claude Opus 5) at handback, under
D-M8-2 and D-M8-3, and subject to `strategic-review` acceptance. These are implementation
rulings the slice could not avoid taking; none reopens D-M8-1…D-M8-3. Per **REC-007** the
record's `revision` is not bumped for a handback addendum.

### D-M8-8 — `containsAny` is a new field in row position 4, and the three matchers move with it

`containsAll` is retained (D-M8-2), so `containsAny` is an additional element of the
generated row, not a renamed one. It is placed immediately after `containsAll` — the two
qualifier fields read together — which changes the arity and the tail indices of the
generated row type in all three languages. The consequence is that `MessageRecognition.cs`,
`recognition.rs` and `_recognition.py` are all edited: the matching predicate is
hand-written per language, and a row-shape change it cannot destructure does not compile.

**Consequence for the Stage 1 freeze (D-6).** The Rust and Python edits are the arity change
plus the predicate clause, and nothing else. This is the same exception M2c took for
regenerated artefacts, one step wider: the freeze exempts regenerated files, and a
regenerated file whose shape changed drags exactly the destructuring that reads it.

**Rejected:** *append `containsAny` last, after `code` and the capture index.* It would have
left Rust's `rule.6`/`rule.7` and Python's `rule[:6]`/`rule[6]`/`rule[7]` untouched, so the
frozen languages would show a two-line diff each instead of a four-line one. Rejected
because the saving is cosmetic and permanent damage to the schema is not: a reader of the
generated table would find the two halves of one concept at opposite ends of the row, and
the next genuine conjunction would have to be explained rather than read.

### D-M8-9 — the single-alternative fixtures are generated, not hand-authored

`specifications/fixtures/errors/errors.recognition.bv-*.json` is generator-owned:
`emitters.owned_fixture_paths` deletes any file matching that glob the emitters did not
produce. A hand-authored fixture there would therefore be deleted by the next regeneration,
and the CI gate (regenerate, `git diff --exit-code`) would go red. D-M8-3's fixtures are
produced by the generator instead: a rule with a qualifier group emits **one fixture per
alternative**, each message carrying that alternative alone. The generated set moves 124 →
130, and the corpus 230 → 236.

**Consequence:** fixture ids renumber *within* a code when a rule ahead of them gains
alternatives. `errors.recognition.bv-input-103.3` was the `contains (500) hmac verification
failed` row and is now `backup` + `unsupported version`; that row is `.5`. D-M1c-10's
id-stability convention is about ids not being *renamed on a whim*, and no test, workflow or
document references an individual generated id — the three suites assert counts.

**Rejected:** *keep the joint-message fixture and add single-alternative ones beside it.* The
joint message passes under either reading (D-M8-3's whole point), so retaining it would keep
a fixture in the corpus that certifies nothing while costing a reader the question of why two
fixtures exist for one alternative set.

### D-M8-10 — SYS-091's 400 case answers `BV-INPUT-103`, not `BV-INPUT-100`

`SysCompleteUnitTests.The_SYS_091_remap_is_scoped_to_a_500_…` asserted that a **400**
carrying `backup archive is corrupted` maps to `BV-INPUT-100`. That expectation was the
*remap's*: D-M7-36's remap was scoped to a 500, and the generated rule could not fire at any
status. With the remap deleted the shared table answers, and Appendix B §2's row carries no
status qualifier — unlike the `contains (500) hmac verification failed` row below it, which
shows the appendix qualifies by status when it means to. Recognition precedes the status
table (D-M1c-3), so `BV-INPUT-103` at a 400 is the appendix's answer. The test is kept and
its third row re-expected, with the reasoning in the test; the other two rows (a 500 with no
body, a 500 reading `disk is full`) are unchanged and still hold the rule narrow.

**This is the one behavioural widening in the slice beyond the five rules R-23 names**, and
it is a consequence of them, not a separate choice: any message the fixed rule recognises is
now recognised at every status, which is what the appendix says.

### D-M8-11 — a second qualifier group on one stem is refused, not flattened

No Appendix B row today carries two qualifier groups on one stem, and the compiled row has
one `containsAny` slot. Rather than silently merge a hypothetical second group into the
first — turning an `(a|b) AND (c|d)` into `(a|b|c|d)`, which is R-23's failure mode with the
operands swapped — `parse_server_text` raises. The appendix row that needs a genuine
conjunction will fail the build and get `containsAll` wired through the three emitters, which
is what `containsAll` is being kept for.

### D-M8-12 — two unit tests that passed under either reading are repaired

R-23 point 4 named the `BV-INPUT-102` fixture. Two unit tests had the same defect and are
repaired with it, each split into one case per alternative:

- `SysPolicyUnitTests` (SYS-042) drove `"namespace refuses this cross-namespace policy path"`,
  which carries **both** alternatives.
- `AuthLoginTests` (AUT-012) drove `"machine m-17 is not bound and is not approved for this
  role"`, a message contrived to satisfy the conjunction, with a comment recording that the
  real single-phrase message fell through to `BV-INPUT-100`.

Both now drive realistic single-alternative messages, and all four cases fail against the
pre-fix generated table.

## Slice a — handback gate (Strategic-tree Claude Opus 5, `agents.md` §4.2 row 4, §4.4)

**Verdict: approve with required fixes.** The reviewer re-ran all three suites, the
generator's `--check` gate and three replays of its own rather than trusting the author's
numbers: 130 generated fixtures, five rules with two or more alternatives (exactly the five
R-23 names), **zero fixtures carrying two alternatives of their own rule's group**, eleven
fixtures failing the pre-fix conjunctive semantics across all five codes, and — the check
the brief did not ask for — the **entire pre-change corpus replayed against the new table
with zero regressions**, which is what proves no broadened rule steals a first-match from a
later one. `containsAll` is empty for all 127 rules, confirming D-M8-2 as specified.

Required fixes, and their disposition:

| # | Finding | Disposition |
|---|---------|-------------|
| RF-1 | No `CHANGELOG.md` entry, so the change is undocumented (**REC-001**, **CLA-009**) | Written by the Strategic Orchestrator on acceptance, which is where **REC-004** puts it. The entry the reviewer could not read was a *concurrent session's*, reverted mid-review, not the author's — the author correctly proposed a line and did not write it |
| RF-2 | `ROADMAP.md` R-23 still reads `Open, unowned` and carries two false statements | Closed and corrected — see **D-M8-13** |
| RF-3 | 54 lines of CR-at-EOL churn inflating an R3 diff from 26 lines to 80 (**CLA-007**) | Reverted. `AuthLoginTests.cs` 59→9, `MessageRecognition.cs` 21/5→17/1. `HEAD` carries mixed endings in both files; only lines the slice did not otherwise touch were restored, so the pre-existing inconsistency is preserved rather than normalised inside an R3 diff |

Both non-blocking observations were taken as well: `catalogue.py:571`'s parenthesised
second-qualifier-group raise is now covered (all three malformed shapes assert, 55 tests
plus 3 subtests green), and `HarnessTests.Repository_loads_and_validates_all_230_fixtures`
— stale the moment the count moved, which is the drift its own comment block exists to
prevent — is renamed to `…_every_fixture_on_disk`.

### D-M8-13 — R-23's retryability claim is false, and is corrected rather than carried forward

**R-23 states that `BV-SERVER-005` is retryable, and that a restore rejected for a tampered
or corrupt file therefore "currently looks retryable" — the same shape as D-M1c-25's `503`
defect with the sign flipped.** That is the sentence that justified the risk framing, and
the review disproved it from `specifications/appendix-b-error-catalogue.md:138`: the
`BV-SERVER-*` table's `R` column reads `no` for `BV-SERVER-005`. Every code on every side of
this change — `BV-INPUT-100`, `BV-INPUT-102`, `BV-INPUT-103`, `BV-AUTH-011`,
`BV-TRANSIT-004`, `BV-SSH-005`, `BV-SERVER-005` — is non-retryable, confirmed through the
parser rather than by eye. `SYS-090` excludes restore from retry in any case.

**Decision:** the defect's real blast radius is an **error code and hint** change, not a
retryability change. R-23's row is closed with the claim corrected in place rather than
preserved in a closed entry, and the quoted body of `errors.recognition.bv-input-102.1`
(which the slice changed from `namespace refuse cross-namespace` to `namespace refuse`) is
corrected with it. The R3 tier is **not** lowered, but it stands on a different limb of **CRS-004** than
first recorded. No workflow publishes to NuGet, crates.io or PyPI — `build-artifacts.yml`
builds and never pushes — so `v0.5.0`…`v0.11.0` are **repository tags, not released
packages**, and CRS-004's "published artefact" limb does not bite. What does bite is the
other limb: slice a edited `specifications/appendix-c-conformance-fixtures.md`, and
**CRS-004** makes any `specifications/` change R3 outright. The tier is unchanged and the
human confirmation before release still stands; only the reason is corrected. Found by the
session closing R-16, which checked the workflows rather than inheriting the phrase.

This is the third finding in this project of a *record* asserting something its own
specification contradicts (after D-M5-26's fixture and R-23's own fixture), and the first
where the false statement had already propagated into a downstream brief. It is worth the
line: the register is read as evidence, so an uncorrected entry in it is an uncorrected
defect.

### D-M8-14 — `agents.md` §9's Python command does not run on the development host

Both the author and the reviewer had to reach `/opt/homebrew/bin/python3.11` directly;
`python` is not on `PATH` and `python3` resolves to a 3.14 without `pytest`. `agents.md` §9
publishes `(cd python && python -m pytest tests -m "not integration")` as *the* verification
command, so every agent rediscovers this. Not a finding against slice a, and not fixed here:
recorded as a DevOps item for the M8 exit sweep.
