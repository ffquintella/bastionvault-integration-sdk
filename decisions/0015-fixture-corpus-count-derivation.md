# DR-0015 — retiring the hand-transcribed fixture-corpus count

**Number:** `0015`. `0013` is [DR-0013](0013-m8-transit-totp-and-efficiency.md) (M8, landed
in `81ecb87`) and `0014` is the R-16 session's. This record was drafted as `0013` and
renumbered before review concluded, on the M8 owner's notice — `0001` and `0011` are each
already used twice in this repository and a decision that is not citable by one stable
identifier defeats **CLA-008**.

**Status:** **proposed** — authored by the Strategic Orchestrator acting as Architect
(`agents.md` §3.3), awaiting Strategic-tree Claude Opus 5 architecture review
(§4.2 row 4, §4.4), **revision 1** — no architecture-review round yet.
**Risk tier:** **R2** (`agents.md` §5.3 — the change touches all three test harnesses, so
it is cross-language by construction; it carries no secret-material or token-lifecycle
dimension, so CRS-003 does not apply and the tier is not R3). The tier requires an
Architect decision record (this file), a parity check across all three languages, and
Engineering-Orchestrator sign-off at handback.
**Milestone:** none — cross-cutting maintenance, sequenced **after M8 closes** (§6 below).
**Date:** 2026-09-18
**Supersedes nothing. Amends:** nothing in `specifications/`. `TST-010` and `FIX-001` are
unchanged by this record; §3 argues the chosen design strengthens the guard they ask for.
**Inherits:** [DR-0005](0005-m1c-error-model.md) **D-M1c-1** — the generated-artefact gate
(generator plus `git diff --exit-code`) that this record reuses rather than invents;
[DR-0001](0001-m0-harness.md) **D-M0-10** — only `baseline.json` is committed, report
outputs are build artefacts, which is the precedent for a committed ratchet file under
`tools/`.

## 1. Problem

The conformance-fixture corpus count is a magic number hand-transcribed into six test
assertions across three languages, plus two prose sites. It has drifted on every corpus
change since M5:

| Corpus change | Sites updated | Result |
|---|---|---|
| M5: 218 → 224 | `dotnet/` only | Rust run 35006009906 and the Python gate red, 2026-09-15 (`assert 224 == 218`) |
| M6/M7: 224 → 230 | `dotnet/` only | Rust run 35344102617, Python run 35344102547 red, 2026-09-18 (`assert 230 == 218`) |
| M8 slice a: 230 → 236 | all three, in one pass | third hand transcription; correct, and correct by hand |

The drift is observable in this worktree at `main` (42dd778) as of this record:

```
$ find specifications/fixtures -name '*.json' -not -path '*/schema/*' | wc -l
     230
dotnet/BastionVault.IntegrationSdk.Tests/HarnessTests.cs:98    Assert.Equal(230, ...)
rust/bastionvault-integration-sdk/tests/fixture_harness.rs:19  assert_eq!(fixtures.len(), 218);
python/tests/test_fixture_loader.py:22                         assert len(fixtures) == 218
```

The prose sites have drifted further, and disagree with each other and with the corpus:

| Site | Reads |
|---|---|
| `ROADMAP.md:34` (§2 current-state line) | 230 |
| `ROADMAP.md:62` (§2 `specifications/` row) | 224 |
| `dotnet/README.md:133` | 218 |

**The defect is not the assertion.** The assertion is the `TST-010` / `FIX-001` guard
against a fixture being silently dropped from the corpus, and it has value: it is the only
check that fails when a fixture file disappears. The defect is that its expected value is
maintained by hand in six places, so the guard's correctness depends on an author
remembering five files they are not editing.

## 2. Forces

1. **The guard must survive.** A design that no longer fails when a fixture is dropped is
   a regression, not a fix (**CLA-004**).
2. **One expectation, not three.** Any design that leaves a per-language number leaves the
   defect.
3. **Parity is a hard project goal** (**CLA-003**). Whatever the harnesses read, all three
   must read the same thing and reach the same verdict.
4. **Corpus growth is already partly generated.** M8 slice a's six `errors.recognition.*`
   fixtures are emitted by `tools/error-catalogue/generate.py` from
   `specifications/appendix-b-error-catalogue.md` (D-M1c-1). Corpus size is therefore
   already a *derived* quantity for part of the corpus, and the repository already has a
   gate shape for derived quantities.
5. **The loaders' non-fixture exclusion rules are already divergent** — see §5, a finding
   this analysis produced and which bears directly on option B.
6. **Smallest change that satisfies the requirement** (**CLA-007**).

## 3. Options

### Option A — derive the count at test time, assert the delta in the commit

Each harness counts the fixture directory at test time and asserts against what it
counted; the commit that changes the corpus states the delta in its message and review.

**Tradeoff.** The mechanism is a tautology: `assert len(fixtures) == count_files()`
compares the corpus to itself and passes for every corpus, including one with a fixture
dropped. What is supposed to restore the guard — "assert the delta in the commit" — is a
*process* rule with no enforcement point: nothing in CI can fail when an author changes
the corpus and does not assert a delta. It replaces a guard that fires automatically with
a guard that fires when a reviewer remembers. Against force 1, this is the one option that
is a regression rather than a fix, and it is **rejected on that ground alone** — not
because it is unattractive, but because it discharges `TST-010` in name only.

### Option B — a committed manifest under `specifications/fixtures/`, hand-maintained

A manifest file each harness reads and asserts against. `schema/` is the precedent for
non-fixture files living in that tree.

**Tradeoff.** This satisfies force 2 — one expectation, three readers — and keeps a real
guard (a committed expectation compared against runtime reality). Two costs. First, the
manifest is still a hand-maintained magic number; it moves the transcription from six
places to one, which is a large improvement and not a solution. Second, and less obvious:
placing a second non-fixture JSON inside `specifications/fixtures/` lands on the
divergence in §5, forcing an exclusion-logic parity fix in all three loaders as part of
this change — more blast radius than the problem needs.

### Option C — a repo gate in `scripts/` that fails when the three languages disagree

**Tradeoff.** Cheapest by a wide margin, and it would have caught both historical reds at
PR time rather than on `main`. But it checks the three *assertions* against each other,
not any of them against the corpus: three harnesses that agree on a wrong number pass, and
a dropped fixture accompanied by three consistent decrements passes. It catches divergence
rather than preventing it, as stated in the brief. **Not sufficient alone**; its value is
subsumed by option D, which gets the same PR-time failure from a mechanism that also
compares against the corpus.

### Option D — **chosen** — generate a committed manifest, gate it with `git diff --exit-code`

1. `tools/fixtures/manifest.py` enumerates the corpus with the loaders' inclusion rule and
   writes `tools/fixtures/manifest.json`: the count **and the sorted fixture-id list**.
2. The file is committed. All three harnesses read it and assert their enumeration against
   it — count and id set.
3. `.github/workflows/repo-gates.yml` runs the generator and `git diff --exit-code`,
   exactly as the error-catalogue gate does (D-M1c-1).

**Why this satisfies the guard.** Nothing derives the expectation at test time; the
harnesses compare the corpus against a *committed* expectation, so a dropped fixture fails
all three suites. The `git diff --exit-code` gate then makes the stale-expectation failure
mode impossible in the other direction: an author who changes the corpus and does not
regenerate fails CI, and an author who regenerates lands a manifest diff that states the
delta in reviewable form. Option A's intent — "assert the delta in the commit" — is
obtained **mechanically** rather than by process: the manifest diff *is* the delta
assertion.

**Why the id list, not just the count.** A count alone passes a swap — one fixture dropped
and one added in the same change. Asserting the id set fails it. This is strictly stronger
than the guard being replaced, which is the CLA-004 direction of travel.

**Placement: `tools/fixtures/`, not `specifications/fixtures/`.** Three reasons.
`tools/traceability/baseline.json` is the established precedent for a committed gate file
(D-M0-10). It keeps the manifest out of the fixtures tree, so the count does not shift by
one and **no loader exclusion rule changes** — the §5 divergence stays a separately
tracked latent defect instead of being dragged into an R2 change. And it keeps the change
out of `specifications/`, which **CRS-004** would score R3.

**Tradeoffs accepted.** A generated committed file is one more artefact a contributor can
hand-edit — mitigated by the same gate that protects the error catalogue, and by the
comment convention that gate already uses (`CLA-004`: regenerate, never add an exclusion).
A second cost is that the harnesses gain a dependency on a file outside their own language
tree; all three already depend on `specifications/fixtures/` at test time, so this widens
an existing dependency rather than introducing a kind.

**Prose sites.** The same generator asserts that `ROADMAP.md` §2 and `dotnet/README.md`
carry the current number, failing the gate when they do not. This closes the second drift
class at near-zero marginal cost and removes the three-way prose disagreement recorded in
§1.

## 4. Decisions

- **D-FC-1.** The expected corpus composition is a **generated, committed** artefact at
  `tools/fixtures/manifest.json`, emitted by `tools/fixtures/manifest.py`. No harness and
  no prose site carries a hand-maintained count after this change.
- **D-FC-2.** The manifest carries the **count and the sorted fixture-id list**. Harnesses
  assert both. A swap must fail, not only a net loss.
- **D-FC-3.** The manifest lives under `tools/`, not `specifications/fixtures/`, so the
  corpus count does not shift and no loader exclusion rule is touched by this change
  (D-FC-5 owns that separately).
- **D-FC-4.** The staleness gate is generator plus `git diff --exit-code` in
  `repo-gates.yml`, reusing D-M1c-1's shape. Option C's cross-language agreement check is
  not added separately: with one committed expectation there is nothing left to disagree.
- **D-FC-5.** The exclusion-rule divergence in §5 is a **separate** defect, recorded here
  and fixed in its own change. It is not folded into this one. When it is taken, the three
  rules converge on **.NET's** shape — the only one of the three that cannot silently drop
  a real fixture (§5). Rust's rule is a live `TST-010` hole today and is the first part to
  fix.
- **D-FC-6.** Timing: this change starts only after **all five** M8 slices merge, not
  after slice a (§6) — b and c are in flight, d and e follow, and each authors fixtures and
  so rewrites the same assertions. It does not land as part of any M8 slice. No date: the
  M8 owner signals when slice e lands.

## 5. Finding: the three loaders exclude non-fixture JSON by three different rules

Not a design option — a latent parity defect this analysis surfaced, which any future file
added under `specifications/fixtures/` will trip:

| Loader | Rule |
|---|---|
| `python/tests/harness/fixture_loader.py:66-70` | excludes any path with a component named `schema` |
| `dotnet/.../Harness/FixtureRepository.cs:102-105` | excludes exactly `<root>/schema/fixture.schema.json`, by full path |
| `rust/.../tests/harness/fixture.rs:251-257` | excludes any file *named* `fixture.schema.json`, anywhere in the tree |

All three agree today, because exactly one such file exists at exactly that path. They
diverge on the second one, and **the interesting case is not the one that makes the count
wrong — it is the one that makes it wrong differently per language**, which no single
assertion can express. Three cases, each checked against the source above:

| Second file | Python | .NET | Rust | Presents as |
|---|---|---|---|---|
| `specifications/fixtures/manifest.json` (at the tree root) | counted | counted | counted | all three agree and **all three are wrong together** — one visible off-by-one, easy to diagnose |
| `specifications/fixtures/schema/manifest.json` | **excluded** | counted | counted | the count **differs by language**; two of three workflows red with no expressible expected value. This is the sharp case |
| a real fixture named `fixture.schema.json` in any other directory | counted | counted | **excluded** | **Rust alone silently drops a genuine fixture** — the exact failure `TST-010` exists to catch, defeated by the exclusion rule rather than by a missing assertion |

This is the reason to keep the manifest outside `specifications/fixtures/` (D-FC-3), and it
is a stronger reason than the off-by-one: the languages would not agree on which way the
count moved. It is also why option B's cost is higher than it looks.

**Which rule to converge on, when D-FC-5 is taken.** .NET's is the strictest and the only
one that **fails safe**: it excludes exactly one known path, so an unforeseen non-fixture
file gets *over*-counted — loud, and caught by the next regeneration — while Python's and
Rust's can *under*-count by dropping a real fixture, as rows 2 and 3 show. Converge on
.NET's shape, not Rust's. The cost of that shape is that every new excluded path must be
named explicitly, which is a hand-maintained list of the kind this record exists to
retire; D-FC-3 keeps that list at exactly one entry for as long as no non-fixture file is
added to the tree. Row 3 is the one to fix first regardless of the convergence: it is a
live `TST-010` hole in Rust today, independent of any manifest.

**Window confirmed shut from the M8 side.** The M8 owner has verified this finding
independently at source, and has briefed slices d and e that no M8 slice adds a
non-fixture JSON under `specifications/fixtures/`. D-FC-5 therefore stays a latent defect
rather than an active one for the duration of M8.

## 6. Sequencing

M8 runs as five slices (a done and unmerged on
`m8a-recognition-qualifier-alternation`; b Transit, c TOTP, d rate gate plus batch,
e pagination plus cache). Slices b–e keep touching `dotnet/` and the corpus, and this
change rewrites the same six assertions. Landing it mid-milestone guarantees a conflict in
files two sessions are both editing, which is the failure this repository has already paid
for once.

1. **All five** M8 slices close and merge, `ROADMAP.md` updated per **REC-002** by the M8
   owner, who signals when slice e lands.
2. This record goes to architecture review (§4.2 row 4) and is accepted.
3. Implementation is delegated as a single work package: the generator and gate, then the
   three harnesses, then the two prose sites. The contract is settled by this record, so it
   is **row 2** (`eng-implementation`), not row 3 — including each parity pass.
4. Handback review by the Strategic tree, parity check across all three languages
   (**VER-002**), `CHANGELOG.md` entry under **Fixed** (**REC-001**), and the
   count-derivation row opened and closed in `ROADMAP.md` §8 in the same change, under the
   number the M8 owner allocates at M8 exit.

**`ROADMAP.md` is deliberately not edited by this record, and this record does not
allocate a risk id.** The M8 owner owns `ROADMAP.md` §8 and allocates risk numbers at M8
exit (the R-16 session may want a row too), so the row below is referred to as **the
count-derivation row** until that number is issued. The M8 owner has confirmed the row is
mine to write, on the grounds that **REC-005** wants it to link to the rationale, which
lives here.

> | *(number allocated at M8 exit)* | **The conformance-fixture corpus count is a magic
> number hand-transcribed into six test assertions and two prose sites.** It has drifted on
> every corpus change since M5 and reddened `main` twice in four days (M5's 218→224 and
> M6/M7's 224→230, both `dotnet/`-only; M8a's 230→236 is the third hand transcription and
> was correct only because one session did all six by hand). The guard itself is worth
> keeping — it is the only check that fires when a fixture is silently dropped
> (`TST-010`, `FIX-001`) | R2 | **Open, designed.**
> [DR-0015](decisions/0015-fixture-corpus-count-derivation.md) chooses a generated
> committed manifest under `tools/fixtures/` — count plus sorted id list — read by all
> three harnesses and protected by generator plus `git diff --exit-code`, reusing D-M1c-1's
> gate shape. Sequenced strictly after **all five M8 slices** merge (D-FC-6). Carries a
> second finding (DR-0015 §5): the three loaders exclude non-fixture JSON by three
> *different* rules, and would not agree on which way the count moved — tracked separately
> as D-FC-5, not folded into this change |

Until step 1, the correct action on a corpus change remains: update all six assertions and
both prose sites by hand, in the same commit.

**This is the durable fix, not the outage fix.** M8 slice a is up as
[PR #2](https://github.com/ffquintella/bastionvault-integration-sdk/pull/2) and brings all
three languages to 236, so the red `main` in §1 is already being closed by the M8 owner.
Nothing here needs to race M8, which is what makes D-FC-6's "wait" cheap rather than a
tolerated outage.
