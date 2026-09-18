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

## Slice b handback: rulings taken inside the implementation

Authored by the Engineering implementation worker (`eng-implementation`, Claude Sonnet 5)
at handback, under this record's problem statement and the table in "Slices, rungs and the
recorded escalation triggers". Subject to `strategic-review` acceptance. Per **REC-007** the
record's `revision` is not bumped for a handback addendum.

### D-M8-15 — `TransitKeyTypes` is a plain-string constant set, not an `Other(string)` wrapper type

TRS-001 asks for "the types as constants" and for unknown strings to "pass through
(`Other(string)`)". A dedicated discriminated-union type mirroring a Rust `enum … { Other
(String) }` was considered and rejected: KV2-011's `Operation` field already settled this
question for an open string set in this SDK (D-M4-5) — a plain `string` field with `const`
members for the known values, because an enum would need an invented member for a value a
later server adds, which is the guess D-M1c-25 forbids. A plain string already "passes an
unknown value through" by construction, so `Other(string)` needs no wrapper to be true of
it. `TransitKeyOptions.KeyType` and `TransitKey.Type` are therefore `string?`/`string`,
against the `TransitKeyTypes` constants. Smaller surface, same guarantee (CLA-007).

### D-M8-16 — `TransitKeyConfig` follows `KvV2ConfigPatch`'s null-means-unchanged shape

08's operations table's parenthetical "(0 = unchanged)" on `ConfigureKey`'s
`min_decryption_version`/`min_available_version`/`deletion_allowed` is ambiguous taken
literally — `deletion_allowed` is boolean, and "0" is not a boolean the wire can carry
unless the field were tri-state, which nothing in section 08 confirms and which no capture
in `test-matrix.json`'s reach confirms either. Rather than guess a tri-state wire encoding
(D-M1c-25), `TransitKeyConfig` follows the shape this codebase already uses for exactly this
problem — `KvV2ConfigPatch` (D-M4-6): every field nullable, `null` means "omit, leave
unchanged," and a caller who wants to actually configure a field sends a real value. This
answers 08's parenthetical honestly for the two integer fields and leaves the boolean field
un-guessed at its literal, ordinary meaning.

### D-M8-17 — `Transit.Byok.*` binds its routes and passes a caller-supplied body/response map through unshaped

Section 08 names three BYOK routes (`wrapping_key`, `keys/{name}/import`,
`import_version`) and states they are feature-gated, but pins no body or response shape for
either import route. Typing them against an invented shape would be exactly the plausible
guess D-M1c-25 forbids, and BYOK is out of this slice's requirement set (`TRS-001`…`013`)
in the first place — only "bind it so an absent feature surfaces `BV-SERVER-004`
unchanged" is asked. `TransitByokOperations` therefore takes and returns
`IReadOnlyDictionary<string, JsonElement>`, going through the same `LogicalOperations`
executor and the same recognition path as every other route, so the absent-feature case
needs no bespoke handling to answer `BV-SERVER-004` — it is the ordinary `500 Logical
backend path not supported.` row already generated from Appendix B. A typed contract is a
later slice's, once section 08 or a decision record pins one.

### D-M8-18 — `SecretBytes` is a new type, not `SecretString` reused over a base64 string

TRS-013 asks that `Decrypt`'s output and a datakey's plaintext be "returned as bytes in a
type that redacts in logs." `SecretString` (D-M1a-9) redacts a `string`; wrapping a
base64-encoded string in it would leak the byte length relationship one `Convert.FromBase64String`
away from being the SDK's problem again, and would return the wrong CLR shape for
`Transit.Decrypt`'s declared `bytes` return (08 §Operations). `SecretBytes` is
`SecretString`'s exact pattern — an internal `byte[]?`, a redacted `ToString`, a named
`Reveal()` rather than a property so a call site is always searchable (CNF-031, CNF-032) —
transposed to bytes, the smallest change that satisfies the requirement without reusing a
type for a shape it does not carry.

### D-M8-19 — `transit.encrypt-decrypt` drives a harness-only composite operation, not two exchanges under one op name

FIX-011 requires one fixture to test one behaviour, with a named exception for "flows that
are inherently multi-request." A round trip cannot be certified by encrypting alone (the
ciphertext could be wrong) or by decrypting alone (the input would have to be fabricated,
proving nothing about `Encrypt`'s own request shape) — the two must be chained through one
real key. `Transit.EncryptThenDecrypt` is registered in `TransitFixtureOperations` as a
harness-only operation name, exactly the shape `Auth.AutoRenew.Run` already establishes for
a fixture whose whole point is a chained, real multi-request flow rather than one call.

**Citation corrected at the handback gate.** This entry first rested its case on FIX-011's
parenthetical, which reads like a closed enumeration and is therefore the weaker of the two
available warrants. The primary warrant is that **Appendix C names `transit.encrypt-decrypt`
itself** as a required `transit.*` fixture
(`specifications/appendix-c-conformance-fixtures.md:126`), so the corpus is obliged to carry a
fixture that cannot be satisfied by a single exchange; FIX-011's exception is what makes the
chaining *legal*, not what makes the fixture *required*. Resting a correct conclusion on the
weaker of two arguments is the kind of thing a later reader inherits and then has to defend
(**CLA-008**).

## Slice b required fixes (R3 handback gate)

The R3 handback gate on slice b (Transit REST bindings) raised three findings. Each is
closed here, in place, on the same uncommitted slice — this is a correction to slice b, not
a new slice.

### D-M8-20 — `Verify`/`VerifyHmac` raise on an unparseable `valid`, rather than reporting `false`

`KvWire.ReadBool` answers `false` for anything that is not a wire `true` — an absent
`valid`, a JSON `null`, or a `"false"` string all collapse to the same "signature invalid"
result a *rejected* signature produces. TRS-012 requires `false` **from `{valid: false}`**
specifically; anything else is a protocol violation the caller must not read as a
cryptographic verdict. `TransitWire.ReadValid` is added, mirroring `TotpWire.ReadValid`'s
exact shape (require `True` or `False`, raise `KvWire.EnvelopeMismatch` otherwise), and
`VerifyAsync`/`VerifyHmacAsync` (`TransitOperations.cs:360-364`, `:422-426`) now read
through it instead of `KvWire.ReadBool`. This is restoring the internal consistency every
other reader in this file already had (`:229`, `:268`/`:269`, `:460` all raise on a missing
field); it is not a new rule.

### D-M8-21 — a malformed base64 field from the server raises, never a raw `FormatException`

`Convert.FromBase64String` on a server-supplied `plaintext`, `random_bytes` or `sum` threw
an unguarded `FormatException` at five call sites (`Decrypt`, `GenerateDataKey`'s plaintext
mode, `UnwrapDataKey`, `Random`, `Hash`), which escapes the SDK's error model entirely — no
code, no hint, no `BastionVaultException`. `TransitWire.RequireBase64Decoded(encoded, path,
field)` wraps the decode in the same try/catch shape `ParseCiphertext`
(`TransitWire.cs:90-98`) and `TotpWire.ReadBarcode` already use, raising
`KvWire.EnvelopeMismatch` — the same protocol-violation code the adjacent missing-field
checks on the same lines already raise. All five call sites now route through it; no sixth
variant was written (**CLA-007**).

### D-M8-22 — the branch-coverage regression is closed with tests of the failure arms it named, not a coverage-only pass

The R3 gate traced 96.72 % → 95.11 % branch coverage entirely to slice b and named three
concrete gaps. Each is now covered directly, table-driven where the shape repeats
(**CLA-007**), rather than chased to 100 % (**TST-030** floor is 95 %, already cleared
before this fix):

- **The `mount`/`name` null-or-empty guard** on every `TransitOperations` and
  `TransitByokOperations` member that takes one (21 members) — one theory,
  `TransitUnitTests.MountGuardedMembers`, asserting `ArgumentException` and zero requests
  sent.
- **The `response?.Data ?? throw KvWire.EnvelopeMismatch` null-envelope arm** on `Encrypt`,
  `Decrypt`, `Rewrap`, `Sign`, `Hmac`, `GenerateDataKey`, `UnwrapDataKey`, `Random` and
  `Hash` — one theory, `TransitUnitTests.NullEnvelopeMembers`, run against three response
  shapes per member: `{"data":null}` (the envelope-null arm this fix targets); `{"data":{}}`
  (the *next* guard in the same member — the field-level `KvWire.ReadString(wire, field) ??
  throw`, which for five of the nine sits on a later line than the envelope guard, not the
  same one, so the pair is exercised end to end rather than one arm of a single compound
  branch); and — added after the gate re-opened this fix — `204`, which reaches the
  `response is null` half.

  > **Struck at the second handback gate (2026-09-18).** This bullet originally asserted that
  > the `response is null` half of `response?.Data` was *unreachable* for these nine call
  > sites, "because all nine call `ExecuteShapedAsync` with
  > `treatNotFoundEmptyAsAbsent: false`, so `Shape` never returns a null `Response` for
  > them," and declined to cover it as a contrived branch. **The claim is false.** That flag
  > gates only `IsNotFoundEmpty` (`Internal/RequestExecutor.cs:1113-1116`, the 404 branch);
  > `IsEmpty` is set independently for a `204`, or any 2xx with an empty body
  > (`:1107-1111`, `:1131-1137`), and `Shape` returns `null` when
  > **`outcome.IsEmpty || outcome.IsNotFoundEmpty`** (`LogicalOperations.cs:171-174`). So all
  > nine arms are reachable, and nine reachable failure arms stood recorded as unreachable.
  >
  > **The sibling slice falsified it in a passing test, in this same record.**
  > `TotpUnitTests.cs:348` enqueues a `204` against `Totp.CreateKeyAsync` — which also passes
  > `treatNotFoundEmptyAsAbsent: false` — and asserts `ProtocolUnexpectedResponse`, with a
  > comment stating the correct rule outright. Two slices of one milestone asserted opposite
  > things about one seam.
  >
  > The **behaviour was never wrong**: a `204` to an encrypt correctly raises
  > `BV-PROTOCOL`. What was wrong was a statement of fact in an R3 decision record, and the
  > coverage it was used to decline. A deliberate stop short of 100 % is legitimate
  > (**CLA-004** forbids weakening a test, not declining to manufacture one); a *wrong*
  > unreachability claim is a defect with a comment on it, and it is struck rather than
  > softened.
- **`Internal/TransitWire.cs`** (0.922) and **`SecretBytes.cs`** (0.917) — the remaining
  `ParseCiphertext` framing cases (`bvault:v:...`'s short version segment, a five-part
  ciphertext whose second segment is not literally `pqc`), the asymmetric `keys` entry that
  omits `public_key`, `ReadKey`'s `type` fallback, and `SecretBytes.Equals`'s
  one-side-null branch (`a.Equals(Empty)` and `Empty.Equals(a)`) each get one direct test.

### Verification at handback (fix pass)

- `dotnet test dotnet/BastionVault.IntegrationSdk.Tests/BastionVault.IntegrationSdk.Tests.csproj`:
  1258 passed, 0 failed; coverage 99.36 % line / **95.94 % branch** / 99.92 % method (floor
  95 % line and branch, CNF-010/TST-030/VER-004) — up from the gate's 95.11 %, against the
  same 96.72 % slice-a baseline.

  > **Superseded as a current figure (2026-09-18, b-2 gate re-run).** `95.94 %` is what this
  > pass measured *at the time*, and it stays as the record of that pass. It is **not** the
  > tree's branch coverage now: slice c's fixes landed after it, and the merged `v0.12.0` tree
  > measures **99.36 % line / 96.27 % branch / 99.92 % method**, confirmed independently by the
  > re-run gate and by the orchestrator's own full gate pass. A reader comparing a later
  > measurement against `95.94 %` would conclude branch coverage had risen by a third of a
  > point when it had in fact risen by more; the handback blocks in this record are
  > point-in-time and are not a running total.
- `cargo test --manifest-path ./rust/bastionvault-integration-sdk/Cargo.toml`: 220 passed, 0
  failed, summed across all eight test binaries (159 lib + 22 + 2 + 16 + 10 + 6 + 3 + 2);
  untouched, per D-6's freeze.
- `(cd python && /opt/homebrew/bin/python3.11 -m pytest tests -m "not integration")`: 491
  passed, coverage 98.93 %; untouched, per D-6's freeze.
- `/opt/homebrew/bin/python3.11 scripts/validate-agent-docs.py`: PASS, all seven checks.
- `/opt/homebrew/bin/python3.11 tools/traceability/traceability.py --check`: `covered: 267 /
  baselined: 158 / total: 425` — unchanged; this pass adds tests, not new requirement
  coverage.

### Verification at handback

- `dotnet test dotnet/BastionVault.IntegrationSdk.Tests/BastionVault.IntegrationSdk.Tests.csproj`:
  1210 passed, 0 failed; coverage 99.36 % line / 95.11 % branch / 99.92 % method (floor 95 %
  line and branch, CNF-010/TST-030/VER-004).
- `cargo test --manifest-path ./rust/bastionvault-integration-sdk/Cargo.toml`: all suites
  green, fixture count 241 (touched only for the count, per D-6's freeze).
- `(cd python && /opt/homebrew/bin/python3.11 -m pytest tests -m "not integration")`: 491
  passed, coverage 98.93 % (touched only for the count, per D-6's freeze; confirms D-M8-14's
  finding still holds on this host).
- `/opt/homebrew/bin/python3.11 scripts/validate-agent-docs.py`: PASS, all seven checks.
- `/opt/homebrew/bin/python3.11 tools/traceability/traceability.py --check`: `covered: 267 /
  baselined: 158 / total: 425` — `TRS-001`, `TRS-002`, `TRS-003`, `TRS-010`, `TRS-011`,
  `TRS-012`, `TRS-013` all move from baselined to covered.

## Slice c required fixes (R2 handback gate)

> **Numbering corrected.** These two entries were authored as `D-M8-20` and `D-M8-21`,
> colliding with slice b's fixes of the same numbers: the two fix passes ran concurrently and
> each took "the next free number" from a record the other was appending to, neither able to
> see the other's write. They are renumbered **D-M8-23** and **D-M8-24**. Recorded rather than
> silently fixed because it is the same defect this session spent several exchanges preventing
> *between* sessions, and it still occurred *within* one record between two delegates — which
> says the safeguard has to be allocation by the orchestrator, not "next free number" by the
> author (**CLA-008**).

Authored by the Engineering implementation worker (`eng-implementation`, Claude Sonnet 5)
in response to the R2 handback gate's two required fixes against slice c (TOTP REST
bindings). Per **REC-007** the record's `revision` is not bumped for a handback addendum.

### D-M8-23 — the TST-051 log-hygiene test is no longer vacuous, on both request and response legs

`CapturingClientLogger` only accumulates on `Warn`/`Info`, and the only request-path log
emission in the SDK fires on a server `warnings` array (`LogicalOperations.cs`). The
original `CreateKey_holds_key_and_url_in_SecretString_and_nothing_logs_the_seed` test's
fixture response carried no `warnings`, so `logger.Lines` was empty and its two
`DoesNotContain` assertions passed trivially. Fixed by following the Transit suite's
precedent (`TransitUnitTests.cs`'s `Decrypt_returns_plaintext...` test): the fixture
response now carries a `warnings` entry so the logger actually captures something, a
`CapturingRequestObserver` is added and scanned alongside the logger, and
`Assert.NotEmpty` guards both capture surfaces before the substring scan runs — a
regression that stops the SDK's logger seam from ever firing again would now fail this
test instead of silently keeping it green. A second test, `CreateKey_provider_mode_never
_logs_or_observes_the_supplied_seed_or_url`, covers the request leg the original test
never touched: `TotpWire.Serialise` writes a caller-supplied `key` and `url` straight into
the outbound body, which is the direction a seed is most likely to leak from and had no
log-hygiene test at all.


**What this fix does and does not prove — established at the second handback gate, and it
goes further than the gate's first finding.** Neither scan can fail today:

- The **logger** scan cannot fail because `logger.Lines` holds exactly
  `"BastionVault server warning: {warning}"` (`LogicalOperations.cs:163`), interpolating a
  `warnings` string this test itself authors. A TOTP seed was never a candidate for that line.
- The **observer** scan cannot fail either, which the fix did not anticipate. `RequestEvent`
  (`RequestObserver.cs:18-26`) is eight scalars — method, path, namespace, status, duration,
  request id, attempt, error code. **No request body, no response body, no headers.** The seed
  lives in the request body (`Internal/TotpWire.cs:119`) and the response body, so neither is
  reachable from `observer.Events`.

So what actually proves `TOT-002` is the direct redaction assertion —
`Assert.Equal("[REDACTED]", created.Key!.ToString())` and the same for `Url`
(`TotpUnitTests.cs:139-140`) — **plus the structural fact that neither observability surface
carries a body at all.**

What the fix genuinely bought, and it is worth having: the assertions are no longer vacuous
(both collections are non-empty), and they are a **regression tripwire** — if a body or header
dump is ever added to `RequestEvent` or to the warning line, these scans begin to bite. The
new request-leg test (`TotpUnitTests.cs:168-213`), paired with `:419`'s assertion that the
body *does* carry the seed, establishes that the seed reaches the wire and not the
observability surface.

This entry states the limitation explicitly because the alternative is a record implying a
seed-absence scan is meaningful when it cannot fail — which is the defect the fix was raised
to remove, reintroduced one level up as a claim about a test rather than a test.

### D-M8-24 — a present-but-unparsable `barcode` raises `BV-PROTOCOL-002`, not a silent `null`

`TotpWire.ReadBarcode` caught `FormatException` and returned `null`, which is also what a
legitimately absent `barcode` returns (the ordinary `generate && exported` false case,
11's Operations table) — making a corrupt wire value indistinguishable from an absent one.
TOT-002 requires `barcode` to be exposed as bytes when the server sends it; a value it
cannot parse is a protocol violation, not an absence. `ReadBarcode` now raises
`TotpWire.EnvelopeMismatch` on a `FormatException`, matching the sibling readers in the
same file (`ReadCreated`'s `name`, `ReadCode`'s `code`, `ReadValid`'s `valid`). A
genuinely absent `barcode` is unaffected — `ReadString` returning `null` still short-
circuits to `null` before the parse is attempted. Three tests now cover the three states:
absent (`CreateKey_leaves_barcode_absent_when_the_wire_omits_it`), valid
(`CreateKey_reads_a_valid_barcode_as_bytes`), and present-but-malformed
(`CreateKey_raises_a_protocol_error_for_a_present_but_unparsable_barcode`, replacing the
prior `CreateKey_tolerates_an_unparsable_barcode_by_leaving_it_absent`, whose name and
assertion this fix reverses).

### Verification at slice c fix handback

- `dotnet test dotnet/BastionVault.IntegrationSdk.Tests/BastionVault.IntegrationSdk.Tests.csproj`:
  1213 passed, 0 failed; coverage 99.33 % line / 95.05 % branch / 99.92 % method (floor
  95 % line and branch, CNF-010/TST-030/VER-004).
- `cargo test --manifest-path ./rust/bastionvault-integration-sdk/Cargo.toml`: nine test
  binaries, summed: 159 + 22 + 2 + 16 + 10 + 6 + 3 + 2 + 0 = 220 passed, 0 failed (D-6
  freeze untouched; the code and binaries are unchanged, only the sum was previously
  misreported as 67 by reading a single binary's block).
- `(cd python && /opt/homebrew/bin/python3.11 -m pytest tests -m "not integration")`: 491
  passed, coverage 98.93 % (D-6 freeze untouched).
- `/opt/homebrew/bin/python3.11 tools/traceability/traceability.py --check`: `covered: 267
  / baselined: 158 / total: 425`, unchanged — this fix is entirely inside `TOT-002`'s
  already-covered surface.

## Handed forward to slice d (not yet started)

Two constraints established after slices a–c landed. Both are verified here, not inherited.

### D-M8-25 — `EFF-005` is not a constraint discovery imposes on slice d; it is something slice d must build

`EFF-005` exempts cluster-discovery health probes from the client rate gate. It is natural to
read that as a seam slice d must fit into. **It is not: nothing implements it today, and
nothing can.** Verified at source — `Internal/DiscoveryEngine.cs` contains **zero** references
to `RateGate`, no `EFF-005` reference exists anywhere in `dotnet/`, and the token bucket the
exemption would bypass does not exist (`RateGateState.cs:5` still records it as M8's, with only
D-M1b-16's pause half shipped).

So discovery does not touch the gate, and no prior decision constrains how slice d expresses
the exemption — flag on the probe call, ambient bypass, or a separate un-gated transport path
is an open design choice, and slice d makes it.

The relation runs the **opposite** way to how it was first recorded, by the session closing
R-16 and by this record's own §"Sequencing" note: R-16 does not wait on slice d's seam, and
slice d inherits a requirement from R-16 instead — see D-M8-26. That session found and
corrected its own version of the error (D-R16-14); it is corrected here too rather than left
to contradict the handback.

### D-M8-26 — the shipped SRV resolver's DNS I/O must be exempted from the gate *explicitly*

Once DR-0014's resolver ships, the SDK performs **DNS** I/O as well as HTTP. It is not an HTTP
request and so will never pass through the token-bucket gate at all.

**Decision:** slice d must exempt it **by name in the seam**, not by omission. An un-gated path
that is un-gated because nobody routed it through the gate is indistinguishable, to a later
reader, from one that was forgotten — and `EFF-001` says *every outgoing request* goes through
the gate, so the next person to audit that claim against DNS traffic will find an apparent
violation with no record of a decision. State it, so "never gated" and "not yet gated" cannot
be confused.

This is the same failure mode as R-19, R-23, R-24 and this milestone's own D-M8-22: the
artefact is correct and the *record* of why is missing, so a later reader cannot tell a decision
from an oversight.

---

## Slice b fix b-2 — handback gate, re-run (Strategic-tree Claude Opus 5, `agents.md` §4.2 row 4, §4.4)

`v0.12.0` shipped fix **b-2** verified green but **ungated**: the reviewing agent terminated on
a session rate limit before returning its verdict, and what b-2 closes is a *false
unreachability claim in an R3 decision record* — the one class of defect a second pass most
obviously exists to catch. The gate was re-run before slice d opened, against the merged tree
rather than against the original diff (the fix is not separable as its own commit; it is inside
`e223ea7`).

**Verdict: approve with required fixes** — four inaccuracies, all in prose, no code or test
change required. R1, R2 and R3 are applied above; R4 is applied in `CHANGELOG.md`.

**What the gate confirmed, by dataflow rather than by line citation** — the standard D-M8-22
itself established, and the standard the original false claim failed:

- `RequestExecutor.TryHandleResponse` (`:1108`) tests `StatusCode == 204 || (StatusCode == 200
  && bodyEmptyRaw)` and returns an outcome with `IsEmpty = true`. That test sits **before** the
  `404` branch (`:1113`) and never reads `treatNotFoundEmptyAsAbsent`, which gates only `:1113`.
  `LogicalOperations.Shape` (`:171`) returns `null` on `IsEmpty || IsNotFoundEmpty`, so
  `response?.Data ?? throw` takes its null-response arm. All nine Transit call sites
  (`TransitOperations.cs:229, 268, 300, 326, 391, 460, 487, 506, 533`) are therefore reachable.
  **The struck claim was false and the strike is accurate.**
- The omission of a `200`-with-empty-body case is **legitimate**: both disjuncts at `:1108`
  return the same outcome literal (`:1110`), so a 200-empty and a 204 are indistinguishable
  downstream. The gate corroborated this with an executed coverage report — `:1108` is at
  4/4 condition coverage, so the `200 && empty` disjunct is already exercised elsewhere in the
  suite. No arm is left uncovered by the omission.
- `TotpUnitTests.cs:348` says what this record says it says, and no second copy of the false
  claim survives anywhere in `decisions/`.
- `BV-PROTOCOL-002` is the **specification's** answer here, not merely the code's: `TRN-050`
  confines null-means-absence to reads and lists, and these nine are POST crypto operations
  whose returns section 08's operations table declares non-optional.

### D-M8-27 — the one arm this seam leaves uncovered is a recorded deliberate stop, not an unreachability claim

The gate found that `RequestExecutor.cs:1134-1135` — a 2xx that is **neither 200 nor 204**
carrying an empty body — has **zero hits**, while D-M8-22's struck bullet asserts "any 2xx with
an empty body" as fact. The assertion is true *by reading the source*; it is simply not
executed by any test.

**Decision:** it is covered by no test and that is deliberate. Reaching it requires a server
answering, say, `202` with an empty body to a Transit encrypt — a shape no endpoint in
`specifications/08-transit-engine.md` produces, so a test for it would assert against a
contrived transport rather than against the contract. The coverage floor is cleared with
margin (96.27 % branch against a 95 % floor), and **CLA-004** forbids weakening a test, not
declining to manufacture one.

**What this entry exists to prevent** is the failure D-M8-22 already committed once in this
same milestone: an arm left uncovered, and the *reason* recorded as "unreachable" instead of as
"not worth manufacturing". Those are different statements, and only one of them is falsifiable
by a passing test in a sibling slice. This one is the second, stated as the second, and the
next reader who audits `:1134-1135` against the "every 2xx" phrasing will find a decision here
rather than an apparent oversight — the same reason D-M8-26 requires the DNS exemption to be
named rather than implied.

**Rejected:** *write the test anyway, to make the bullet's "any 2xx" literally executed.*
Rejected because it buys a green arm with a fixture that asserts nothing the server can do,
which is R-19's shape — a test that passes for the wrong reason.

---

## Slice d handback: rulings taken inside the implementation

Slice d landed `EFF-001`…`EFF-006`, `BAT-001`…`BAT-008` and `KV-010` (baselined 158 → 143,
covered 267 → 282; fixture corpus 241 → 245). Numbers below are assigned by the Strategic
Orchestrator, not by the delegate — `D-M8-20`/`D-M8-21` were written twice in one session by two
agents each taking "the next free number", and central allocation is the fix.

### D-M8-28 — the token bucket is a schedule, not a polled counter

One field, `nextFree`, holds the instant the next token is available. A waiter reserves that
instant, advances it by one interval, and sleeps the difference. Arithmetically this is the same
bucket — the clamp `nextFree >= now - (Burst-1) × interval` **is** the burst cap — but it never
re-reads the clock after waiting.

**Rejected:** *refill-and-poll.* Every test clock in this repository (`FixtureClock`, the unit
clocks) completes `Delay` **without moving wall time**, so a poll loop would never observe the
refill it just waited for and would spin for ever. D-M1b-7's "no test sleeps in real time" only
survives in the schedule form. **Cost:** the wait is computed once, so a clock that jumps
backwards mid-queue is not re-evaluated.

### D-M8-29 — `EFF-002` FIFO is an explicit chain, not `Task.Delay` ordering

Each acquirer atomically swaps its completion into `tail` and awaits the one it displaced.

**Rejected:** *assign grant times and let each waiter sleep independently.* Equal or near-equal
grant times leave resume order to the thread pool, and under an instant test clock the order is
arbitrary — `EFF-002` would be untestable, which is the R-19 shape. **Cost:** a slow waiter
blocks its successors (the gate's intent) and one cancelled waiter consumes a slot. The chain
also yields the invariant D-M8-43 depends on: **at most one reservation is outstanding at any
instant.**

### D-M8-30 — `EFF-005` and the DNS exemption are expressed by name — this discharges D-M8-25 and D-M8-26

`EgressKind { Request, DiscoveryProbe, SrvResolution }`: every egress calls `AcquireAsync` and
**states which exemption it claims**. The assembly has exactly two `Transport.SendAsync` call
sites — `Internal/RequestExecutor.cs:942` (`Request`) and `Internal/DiscoveryEngine.cs:435`
(`DiscoveryProbe`) — plus the `DSC-014` resolver call in `DiscoveryEngine.ResolveAsync`
(`SrvResolution`). The handback gate confirmed the enumeration exhaustive by dataflow —
`TokenRenewal` and `LoginRunner` both reach the network through `RunLoopAsync`,
`HttpClientTransport` sits below the seam, and the retry/failover replay re-enters the loop head,
so every attempt re-acquires — and confirmed both exemptions are **live call sites, not stubs**
(hit 32× and 85× in the coverage data).

**Rejected:** *(a) gate inside `RunLoopAsync` only* — the probe bypasses it structurally, so the
exemption stays silence, which D-M8-26 rules against; *(b) an `ITransport` decorator* — it makes
`EFF-001` literally true at one seam but forces the probe to hold a second, undecorated transport
reference, which is the same silence one layer down.

### D-M8-31 — the gate sits inside the existing `try` in `RunLoopAsync`, with `stopwatch.Restart()` after acquisition

A cancellation while queued is then mapped by the existing `OperationCanceledException` arm to
`BV-TRANSPORT-005` (`ERR-020`: no bare runtime exception escapes), and `RES-002`'s `Duration`
stays request latency rather than queue latency.

**Rejected:** *acquiring before the `try`* — it adds a second cancellation-mapping branch
duplicating one three lines below (**CLA-007**).

### D-M8-32 — two .NET-only parity gaps the bucket made reachable are closed, not opened

`RateGate.IsDisabled` now reads **both** limbs (`RatePerSecond == 0 || Burst == 0`): `EFF-001`
says "setting *either* to `0` disables", and `rust/…/rate.rs` has read both since M1a
(`either_field_at_zero_disables_the_gate`). Before the bucket existed the missing limb had no
reader; with it, `Burst = 0` would have meant "nothing may ever pass". Likewise
`RateGateState.Paused` now **expires by the clock** instead of latching `true` for the client's
lifetime, as Rust's `paused(now)` and Python's `rate_gate_state(now)` already did; `PausedUntil`
keeps its value after expiry.

### D-M8-33 — a pause is never shortened by a nearer one, and Rust is the outlier

`Pause` writes only when `until > current`, so `pausedUntil` is monotone non-decreasing.
`nextFree` is a high-water mark and cannot move back without releasing tokens `EFF-003` says were
dropped, so a `PausedUntil` that moved backwards would report a resumption that is not going to
happen.

**Verified at source by the Strategic Orchestrator, because the delegate reported this as a
divergence it had created:** it has not. `python/src/…/client.py:76-77` takes the maximum, the
same as .NET now does. `rust/…/rate.rs:42` assigns `self.paused_until = Some(now + bounded)`
**unconditionally**, so a second `429` carrying a shorter `Retry-After` moves Rust's resume
instant *backwards*. Two of three languages agree and **Rust is the outlier** — this slice
exposed a pre-existing Rust defect rather than introducing a .NET one. Frozen by D-6; owner
**M13**, recorded as a risk row.

### D-M8-34 — "drop accumulated tokens" and "pause the queue" are one assignment

`nextFree = max(nextFree, until)`. At `until` exactly one reservation is grantable — the queue
resuming — not a full burst. `AvailableTokens` therefore reads 1 at the pause end and 5 one
second later at 4/s, asserted directly rather than inferred.

### D-M8-35 — a **disabled** gate reports `int.MaxValue`, not the configured `Burst`

This entry records a ruling that was **taken, found wrong at the handback gate, and replaced**;
the first form is kept because the failure is instructive.

*Originally:* report the configured `Burst`, rejecting `int.MaxValue` as unportable across three
languages and `0` as indistinguishable from "throttled". *Sound for `Burst > 0`, and wrong at
precisely the value D-M8-32's second limb had just made meaningful:* `RateGate { RatePerSecond =
8, Burst = 0 }` — reachable from `BASTIONVAULT_RATE_BURST=0` — disables the gate and would then
report `Paused = false, AvailableTokens = 0`, the one pair a diagnostics consumer reads as *fully
throttled*, for a gate that withholds nothing. The original ruling reached the exact inversion it
was written to avoid, by way of the disabling value itself.

**Decision:** `int.MaxValue`, one rule on both settings rather than a special case for `Burst =
0`. The portability objection that originally rejected it is answered rather than dropped: the
invariant a consumer relies on is **`AvailableTokens > 0` means "may proceed without waiting"**,
and each language expresses it with its own maximum sentinel.

### D-M8-36 — `BatchMaxOperations` is constructor-settable only, with no environment variable

`CFG-001`'s settings table (`specifications/02-client-configuration.md:11-29`) is the normative
list of environment-bound settings and carries no row for it; §14 says "configurable" without
naming a variable. Precedent: `Discovery`, `Health`, `MaxResponseBytes`, `AutoRenew`. Inventing a
`BASTIONVAULT_*` name would be a specification change taken by an implementation agent
(**D-M1c-25**: a deferred branch returns the value the specification names, never a plausible
guess). Validated `< 1` → `BV-CONFIG-003`, appended **after** D-M1a-5's fixed order so
first-failure-wins is unchanged for every pre-existing setting.

Whether the specification *should* grow that row is an open question for the project owner, not a
gap in this slice.

### D-M8-37 — `BAT-003` refuses an API-version prefix rather than trimming it, and knowingly over-refuses

"Never prefixed with `/v1/`" is read as: the caller meant the API prefix, and silently rewriting
hides a mistake. The leading-`/` strip `BAT-003` *does* require is implemented and exercised
(`efficiency.batch.per-op-errors` sends `/secret/data/app/db` and the wire carries
`secret/data/app/db`).

**The cost is recorded rather than hidden:** the refusal is stricter than `BAT-003` requires and
hard-refuses a mount literally named `v1` or `v2`. The justification first written for it — that
"the same string still fails on every other operation" — was **false**, and is corrected here
rather than quietly deleted: `Logical.Read("v1/secret/x")` builds `/v1/v1/secret/x` and fails at
the **server**, not client-side. It is now pinned by a test that asserts the sent URI, not by
reading. Same failure mode as D-M8-22 and D-M8-27: the artefact was defensible, the stated reason
was not.

### D-M8-38 — `BAT-004` is one comparison, not two

`isWrite != Data.HasValue`, so "required for `Write`" and "rejected for the others" cannot be
fixed in one direction and left broken in the other.

### D-M8-39 — `Kv.ReadMany`'s success payload is `KvReadManyEntry`, not `KvV2Secret`

**By dataflow, confirmed at source by the handback gate:** `KvV2VersionMetadata.CreatedTime` is
`required` (`KvTypes.cs:45`) and `KvWire.ReadVersionMetadata` raises `BV-PROTOCOL-002` via
`RequireInstant` (`KvWire.cs:103`) when `created_time` is absent — and the M4-era, `FIX-010`
captured fixture `kv.read-many-batch` carries `"metadata": {"version": 1}` with no
`created_time`. Reusing `KvV2Secret` would make the SDK **reject a response the server really
sends**. §14 writes the success side as `KvSecret`, not `KvV2Secret`, so the new type is closer to
the specification than reuse would be. `Metadata` is nullable: absence is reported, never
invented (**D-M1c-25**).

**Rejected:** *fabricating `CreatedTime = UnixEpoch`* (inventing a value the wire did not carry);
*mapping the mismatch to a per-op error* (the pre-existing fixture expects `app/db` to **succeed**,
and changing it is `FIX-012`, a specification change this slice does not own); *relaxing
`KvV2VersionMetadata.CreatedTime`* (a wider break on a type every standalone read uses —
**CLA-007**).

### D-M8-40 — the `BAT-007` fallback reads through `Kv.V2.GetSecretAsync`, and only `BV-SERVER-004` triggers it

`GetSecretAsync` makes an absent path an error on **both** routes; `ReadSecretAsync` returns
`null`, which would make "missing" mean two different things depending on the server's age. A
`403` on `sys/batch` propagates rather than earning N more 403s.

### D-M8-41 — `Kv.ReadMany` refuses duplicate paths (`BV-INPUT-001`)

The returned map would otherwise silently answer fewer questions than it was asked.

### D-M8-42 — `EFF-002` is asserted by a unit test, and the conformance corpus cannot carry it

A fixture drives one operation and the harness transport is sequential, so concurrent FIFO is not
portably expressible in the fixture format. `efficiency.rategate.fifo-throughput` pins the
*schedule* (burst 2, then one per 125 ms, via `clock.expectWaits`) over `BAT-007`'s fallback —
which also exercises `BAT-007`'s "through the rate gate" clause — and
`Waiters_are_served_in_arrival_order_and_a_later_one_cannot_overtake_an_earlier_one` pins ordering
with three concurrent waiters on a hand-released clock.

**Consequence for Stage 2:** the shared corpus does **not** pin `EFF-002`, so Rust and Python each
need their own ordering test. Pinning it portably would require a concurrency primitive in the
fixture schema, which is a specification change. Owner **M13**.

`RateGate.AvailableTokens` was also added to `LogicalFixtureOperations`' `clientState` alongside
`Paused`: a fixture asserting only `Paused` would pass against a gate that paused **without**
dropping tokens, which is half of `EFF-003`.

### D-M8-43 — `EFF-003`'s pause holds a waiter that was already in the queue

Found by the R3 handback gate, and **fixed rather than recorded as a deviation** — the ruling is
the orchestrator's.

A waiter's grant instant was decided before it slept, and `Pause` had no edge to it: `nextFree`
was read in exactly one place, `Reserve`, which the waiter had already left. So a `429` arriving
mid-sleep let **exactly one** request out during the window the server is banning the client for
— the precise failure section 14 exists to prevent, and one that earns a second `429` and a longer
pause. The one-per-pause bound came free from D-M8-29's chain; the specification offers no such
bound.

**Why fixed and not deviated from:** `EFF-003` states "pause the whole queue" unqualified, and a
waiter in the queue is in the queue. Departing from a MUST is a **specification** change;
`agents.md` §1 requires an implementation agent to escalate rather than improvise, and a decision
record cannot grant what only `specifications/` can. M8 does not own that call.

**The fix, and why it is not the poll loop D-M8-28 rules out.** `Reserve` returns its grant
instant; the single delay becomes a loop that re-validates against the pause and nothing else. It
never re-reads the clock hoping time has passed — it compares two absolute instants that only a
received `429` can move. `pausedUntil` is monotone non-decreasing (D-M8-33) and a re-reservation
is clamped past it, so each further pass requires a *strictly later* pause. Iterations are bounded
by 429s actually received, never by the clock; with no new pause the loop runs **exactly once**,
including under a test clock whose `Delay` completes without moving wall time.

**Rejected:** *sleeping straight to `pausedUntil`* — held waiters would all release at the pause
end as a burst, contradicting `EFF-003`'s "drop the accumulated tokens, and then resume";
re-reserving takes a fresh slot from the pause-clamped `nextFree`, so resumption obeys the rate.
*A pause-generation counter* — a `429` whose pause does not extend the window would still bump it
and push the waiter back an interval; comparing instants is self-limiting. *Having `Pause` cancel
and re-issue waiters' delays* — it needs `Pause` to hold references to in-flight waiters and
produces a cancellation path indistinguishable from caller cancellation at `RunLoopAsync`'s
`catch`.

**Cost, and it reaches slice e:** a paused waiter's total sleep is now two or more `Delay` calls,
so a fixture asserting `clock.expectWaits` across a pause sees each segment separately.

