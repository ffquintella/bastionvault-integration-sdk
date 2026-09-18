# DR-0015 — retiring the hand-transcribed fixture-corpus count

**Number:** `0015`. `0013` is [DR-0013](0013-m8-transit-totp-and-efficiency.md) (M8, landed
in `81ecb87`) and `0014` is the R-16 session's. This record was drafted as `0013` and
renumbered before review concluded, on the M8 owner's notice — `0001` and `0011` are each
already used twice in this repository and a decision that is not citable by one stable
identifier defeats **CLA-008**.

**Status:** **accepted, revision 4** — authored by the Strategic Orchestrator acting as
Architect (`agents.md` §3.3) and **approved by a Strategic-tree Claude Opus 5 architecture
review** at round 3 (§4.2 row 4, §4.4), confidence 0.94, with two required corrections
(**[W1]**, **[W2]**) and three editorial ones applied in this revision. Accepted on the
reviewer's verdict, not the author's: §3.3 forbids an Architect approving its own design.
Three Claude Opus 5 architecture-review rounds complete (§4.2 row 4, §4.4):

- **Round 1** — approve with required fixes (0.91), eight findings, **[F1]**…**[F8]**.
  **[F1]** was the significant one: the record's central premise mis-cited `TST-010` and
  `FIX-001` as requiring the assertion.
- **Round 2** — five further findings, **[V1]**…**[V5]**, and **F4 conceded to this
  record's rule** (D-FC-1b). Two of the five were the author's: **[F1]** had survived
  verbatim in the one place it most mattered, the `ROADMAP.md` row drafted for permanence,
  and revision 2's "correction" of the reviewer on `ROADMAP.md:69` was itself wrong, made
  on a truncated `grep` (§1a).
- **Round 3** — **approve** (0.94), with **[W1]** and **[W2]**: the record's own site
  inventory still disagreed with itself in the interim instruction and the §1 exhibit, both
  undercounting by the same `ROADMAP.md:69`.
- **After approval**, the M8 owner found that the prose-gate anchor §1a proposed — endorsed
  by both reviewer and author — misses `dotnet/README.md` entirely. Recorded in §1a and
  fixed by mechanism in **D-FC-9** rather than by a better pattern. It changes no decision
  already approved, so it is carried editorially rather than re-reviewed.
**Risk tier:** **R2** (`agents.md` §5.3 — the change touches all three test harnesses, so
it is cross-language by construction; it carries no secret-material or token-lifecycle
dimension, so CRS-003 does not apply and the tier is not R3). The tier requires an
Architect decision record (this file), a parity check across all three languages, and
Engineering-Orchestrator sign-off at handback.
**Milestone:** none — cross-cutting maintenance, sequenced **after M8 closes** (§6 below).
**Date:** 2026-09-18
**Supersedes nothing. Amends:** nothing in `specifications/`. `TST-010` and `FIX-001` are
unchanged by this record, and neither asks for a count assertion (§1, **[F1]**); §3 argues
the chosen design strengthens the unrequired drop detector this repository already had.
**Inherits:** [DR-0005](0005-m1c-error-model.md) **D-M1c-1** — the generated-artefact gate
(generator plus `git diff --exit-code`) that this record reuses rather than invents;
[DR-0001](0001-m0-harness.md) **D-M0-10** — only `baseline.json` is committed, report
outputs are build artefacts, which is the precedent for a committed ratchet file under
`tools/`.

## 1. Problem

The conformance-fixture corpus count is a magic number hand-transcribed into seven code
sites across three languages, plus four prose sites (§1a enumerates them). It has drifted on every corpus
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
python/tests/test_fixture_loader.py:17                         assert len(fixtures) == 218
```

The prose sites have drifted further, and disagree with each other and with the corpus:

| Site | Reads |
|---|---|
| `ROADMAP.md:34` (§2 current-state line) | 230 |
| `ROADMAP.md:62` (§2 `specifications/` row) | 224 |
| `ROADMAP.md:69` (§2 fixture-driver row) | 224 |
| `dotnet/README.md:133` | 218 |

`ROADMAP.md` carries the stale 224 **twice**, and three different numbers appear across the
four sites.

**The defect is not the assertion.** The assertion is the only check that fails when a
fixture file disappears, and it is worth keeping. The defect is that its expected value is
maintained by hand in eleven places (§1a), so the guard's correctness depends on an author
remembering ten sites they are not editing.

**[F1] What the assertion is, and is not.** Revision 1 of this record called it "the
`TST-010` / `FIX-001` guard". That was wrong, and the first review round caught it as a
**CLA-002** traceability failure. Read at source:

- `specifications/15-testing-requirements.md:105-107` — **TST-010** requires fixtures be
  *loaded from the repository at test time, not copied*.
- `specifications/appendix-c-conformance-fixtures.md:65-66` — **FIX-001** requires every
  fixture *validate against the schema before running*.

Both are discharged by the loaders' enumerate-and-validate path, which no option in this
record changes. And `specifications/` states no corpus count anywhere — grepped, not
assumed. So **no requirement in `specifications/` mandates a corpus-count or
corpus-composition assertion.** It is unrequired defence-in-depth that this repository
chose, that has caught nothing yet because nothing has been dropped, and that this record
chooses to keep on engineering grounds rather than on a requirement. Everything downstream
of this record — including the risk-register row — states it that way, because a row citing
TST-010 for a count assertion would land the mis-citation permanently in `ROADMAP.md` §8
against **REC-005**.

## 1a. [F6] The site inventory, in full

Revision 1 said "six assertions and two prose sites" and briefed the interim instruction
that way. The first review round found it short. Verified against the tree at 42dd778, the
full list is:

**Code — seven sites, not six.** The six assertions
(`HarnessTests.cs:98`, `:153`; `fixture_harness.rs:19`, `:25`;
`test_fixture_loader.py:17`, `:54`), **plus** the .NET test *method name*
`Repository_loads_and_validates_all_230_fixtures` (`HarnessTests.cs:66`) and the
hand-maintained running derivation in its comment block (`HarnessTests.cs:80-97`, which
walks 213 → 218 → 222 → 224 → 226 → 228 → 230). The method name matters
disproportionately: **no gate can see it**, so after the next corpus change
`..._all_230_fixtures` is simply a lie in the test's own name. The implementation must make
the name count-free; the derivation comment is the historical record of *why* the corpus
grew and is kept, with its running total left as history rather than as a live count.

**Prose — four live sites.** `ROADMAP.md:34` (§2 current-state, 230), `ROADMAP.md:62`
(§2 `specifications/` row, 224), **`ROADMAP.md:69`** (§2 fixture-driver row, 224) and
`dotnet/README.md:133` (218). `ROADMAP.md` therefore carries the stale 224 **twice**, and
three different numbers appear across the four sites.

**Deliberately excluded as historical narrative, not live counts** — these describe what a
past milestone did and must **not** be rewritten by any gate: `ROADMAP.md:602`
("218→224 on disk"), `:855` (R-19's "the 224-fixture corpus"), `:926` ("about 224 files").
An anchored pattern is therefore mandatory, which is the second reason D-FC-8 splits the
prose gate out: a bare three-digit match would rewrite project history.

**Correction to revision 2, which got this wrong.** Revision 2 asserted that
`ROADMAP.md:69` was *not* a count site and told the reader not to look for it. That was
wrong, and wrong in a way worth recording: the check behind it was a `grep` whose output
was truncated at 110 columns, which cut the line before its `**224 fixtures on disk**`.
Verified properly now:

```
$ grep -nE '\*\*[0-9]+ fixtures on disk\*\*' ROADMAP.md
34: … **230 fixtures on disk**
62: … **224 fixtures on disk**
69: … **224 fixtures on disk**
```

A truncated command output is not evidence of absence. This is the same failure the record
documents elsewhere — a check that passes for a reason unrelated to what it claims to
prove — committed by the author of the record while correcting a reviewer.

**An anchor for D-FC-8 — and the anchor this record first proposed was wrong too.** Within
`ROADMAP.md`, `\*\*[0-9]+ fixtures on disk\*\*` is clean: it matches all three live
`ROADMAP.md` sites and **none** of the three historical ones, whose phrasings differ
entirely (`:602` "218→224 on disk", `:855` "the 224-fixture corpus", `:926` "about 224
files"). Revision 3 then called it "a sufficient anchor for the prose gate". It is not.
`dotnet/README.md:133` reads `… 389 requirement IDs, 218 conformance fixtures.` — different
noun phrase, **no bold** — so the pattern matches it **zero** times, verified:

```
$ grep -cE '\*\*[0-9]+ fixtures on disk\*\*' dotnet/README.md
0
```

The M8 owner caught this. A gate covering three of four sites would pass while blind to the
site that rotted longest — 218 across M5, M6 and M7 — and the only one an external reader
sees, since `dotnet/README.md` is the package README carrying the `CNF-002` conformance
statement. **That is R-24's shape a fourth time**: a check that passes for a reason
unrelated to what it claims to prove. It is recorded here rather than quietly repaired
because the claim had already been through architecture review and was approved — including
by this record's author, who endorsed it — which is precisely why the *mechanism* below is
the fix and a better-written pattern is not.

## 2. Forces

1. **The guard must survive.** A design that no longer fails when a fixture is dropped is
   a regression, not a fix (**CLA-004**) — not because a requirement compels the check
   (**[F1]**: none does), but because deleting a working detector to solve a maintenance
   problem trades a real failure mode for an author's convenience.
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

**What it has going for it, stated first.** It is the only option that adds *no new
artefact*: no generated file, no CI coupling, nothing further that a contributor can
hand-edit into a lie. It was the incumbent suggestion and it deserves that credit.

**Tradeoff.** The mechanism is a tautology: `assert len(fixtures) == count_files()`
compares the corpus to itself and passes for every corpus, including a depleted one. What
is supposed to restore the guard — "assert the delta in the commit" — is a *process* rule
with no enforcement point: nothing in CI can fail when an author changes the corpus and
does not assert a delta. It deletes the only check that fires automatically when a fixture
disappears in a bad merge, a rename, or a stray `.gitignore` entry, and replaces it with
one that fires when a reviewer remembers. Against force 1 that is a regression, and that
is the whole of the rejection.

**[F2] What the rejection does *not* rest on.** Revision 1 said A "discharges `TST-010` in
name only". That was false, and follows from the same mis-citation **[F1]** corrects: A
discharges TST-010 exactly as well as D does, because TST-010 is about loading fixtures
from the repository and A still does that. The rejection is an engineering judgement about
a detector, not a conformance finding.

**[F2] And it is repairable, which the record should say.** A's tautology can be turned
into a monotonic floor — assert the derived count never *decreases* against a committed
high-water mark — which does detect a drop with no per-language number. That is a real
design, and it is strictly weaker than option D only in that it cannot detect a swap or a
rename, and needs a committed file anyway, at which point it is option D with less
information in it. Noted so the record is not read as dismissing the direction.

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

### Option E — **[F3]** delete the six assertions; keep only the generator and the gate

Once **[F1]** establishes that no requirement mandates the assertion, a strictly smaller
option exists and revision 1 failed to consider it: drop the six assertions entirely, ship
only `tools/fixtures/manifest.py` plus `git diff --exit-code`. A dropped fixture still
fails CI, because regeneration changes the manifest. One language touched, one workflow
file, **no harness change at all — which makes it R1, not R2.**

**Tradeoff, and why it is rejected.** It is smaller on every axis **CLA-007** cares about,
so it needs a real reason to lose, and it has one: the signal becomes **CI-only and
Python-only**. A developer working in `rust/` or `dotnet/` gets no local failure — they
learn about a dropped fixture from a workflow written in a language they are not editing,
which is how the two historical reds were experienced in the first place. It also deletes
the in-suite cross-language check that **VER-002** wants each language to carry for itself.
The extra cost of keeping the assertions is now near zero, because after D-FC-1 they read a
file instead of carrying a number. Rejected for the locality of the signal, not for its
strength.

### Option D — **chosen** — generate a committed manifest, gate it with `git diff --exit-code`

1. `tools/fixtures/manifest.py` enumerates the corpus with the rule **D-FC-1a** names
   literally — there is no singular "the loaders' rule" to inherit, as §5 proves — and
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
tracked latent defect instead of being dragged into an R2 change. It also happens to keep the change out of
`specifications/`, so **CRS-004** does not reach it and the tier stays R2 — recorded as a
*consequence* of a placement chosen on the two reasons above, not as a third reason for it.
Stating tier avoidance as a design motive reads as tier-shopping even where the placement
is independently correct.

**Tradeoffs accepted.** A generated committed file is one more artefact a contributor can
hand-edit — mitigated by the same gate that protects the error catalogue, and by the
comment convention that gate already uses (`CLA-004`: regenerate, never add an exclusion).
A second cost is that the harnesses gain a dependency on a file outside their own language
tree; all three already depend on `specifications/fixtures/` at test time, so this widens
an existing dependency rather than introducing a kind.

**Prose sites — not in this change. [V4]** A prose gate of the same shape closes the
second drift class, and revision 1 folded it in here and called it "near-zero marginal
cost". **D-FC-8 repudiates that**: on `pull_request` it would make every corpus change edit
the one file the milestone-exit session also edits under **REC-002**, which is the highest
risk per unit of value in the whole change. It is split into its own follow-up, with §1a's
anchor ready for it — subject to D-FC-9, since the anchor as first proposed missed one of
the four sites. Until then the four prose sites stay hand-maintained and the
disagreement in §1 persists — a stated, accepted residue of this record, not an oversight.

## 4. Decisions

- **D-FC-1.** The expected corpus composition is a **generated, committed** artefact at
  `tools/fixtures/manifest.json`, emitted by `tools/fixtures/manifest.py`. **[F7]** After
  this change **no count is maintained without a gate that fails when it is stale** — which
  is weaker than revision 1's "no site carries a hand-maintained count", and is the true
  claim: the prose in `ROADMAP.md` and `dotnet/README.md` stays hand-written and becomes
  *gated*, not generated. The generator must **not** rewrite those files in place: they are
  Strategic-tree-owned records and an Engineering-tree tool editing them breaches
  **REC-004**.
- **D-FC-1a. [F4]** The generator is **normative for the exclusion rule**, and the rule is
  named literally rather than described, **down to the comparison**: *take each candidate's
  path relative to `specifications/fixtures/`, render it `/`-separated, and exclude it when
  that string is **byte-exactly, case-sensitively** equal to `schema/fixture.schema.json`;
  include every other `*.json` under `specifications/fixtures/`.* **[V3]** The comparison is
  part of the rule, not an implementation detail: .NET compares with
  `StringComparison.OrdinalIgnoreCase` on an absolute path today
  (`FixtureRepository.cs:103`), so a case-sensitive Python or Rust implementation of
  "the same" rule would diverge — a file at `schema/Fixture.Schema.json` is excluded by
  .NET and counted by the other two, which is §5 row 2's per-language disagreement
  **recreated by the rule meant to end it**, invisible on a case-insensitive macOS checkout
  and live on Linux CI. §5 proves there is no singular "the loaders' rule" to
  inherit — there are three — so leaving this implicit would make the generator a **fourth**
  implementation pinned to none of them. Concrete failure that forbids: D-FC-5 later
  converges the loaders on one rule while the generator kept another, and the disagreement
  surfaces as three red suites whose cause is a `tools/` script. D-FC-5 is bound to
  converge on **this** named rule, and D-FC-5's scope includes changing .NET's
  `OrdinalIgnoreCase` at `FixtureRepository.cs:103` to an ordinal case-sensitive comparison.
  D-FC-3 correctly keeps that edit out of *this* change.
- **D-FC-1b. [F4, partly declined]** The review recommended Python's component rule
  (exclude any path containing a `schema` component) as "the safest superset". This record
  names .NET's exact-path rule instead. The requirement behind the finding — name it, make
  the generator normative, bind D-FC-5 to it — is accepted in full; only the choice of rule
  differs, and the reason is the direction of the failure. Python's rule silently **drops**
  a real fixture that lands under any directory named `schema`; the exact-path rule can only
  ever **over**-count an unforeseen non-fixture file, which under D-FC-2 surfaces as an
  unexpected id in the manifest diff and in all three suites — loud, and diagnosable. A
  detector whose exclusion rule can silently hide its own subject is the wrong trade at any
  superset size.
- **D-FC-1c. [F6]** The change's scope is §1a's enumerated site list, and the
  implementation brief carries that list verbatim rather than a count of sites.
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
  rules converge on the rule D-FC-1a names — .NET's exact-path shape, the only one of the
  three that cannot silently drop a real fixture. Convergence does more than unify the
  rules: it **collapses §5 rows 1 and 2 into each other**, because under one exact-path rule
  a stray file anywhere in the tree is counted by all three languages and so fails loudly
  and identically. The sharp per-language case stops existing rather than being made less
  likely, which is the strongest argument for D-FC-1b's choice of rule.
  Neither divergence is live today (§5), so
  this is a latent-defect fix with no outage behind it, and it stays with this record's
  owner rather than being half-done inside an M8 slice: one defect, one owner, one record
  (**CLA-008**).
- **D-FC-5a.** The maintenance tension in the exclusion list is resolved, not merely noted.
  Two ways to keep an exclusion rule from becoming the hand-maintained list this record
  exists to retire:
  - **Chosen — exclude by the explicit named path (D-FC-1a), and let D-FC-2 make growth
    loud.** The list has exactly one entry. A second non-fixture file appearing in the tree
    does not silently join the corpus: it appears as an unexpected id in the manifest diff
    and fails all three suites until someone decides deliberately whether it is a fixture.
    The manifest is what makes a one-entry explicit list safe, so the tension largely
    dissolves.
  - **Rejected — make manifest membership itself the exclusion rule**, so the list is
    derived and nothing is hand-maintained. Attractive, and proposed by the M8 owner. It is
    rejected because it inverts the guard: a file present on disk but absent from the
    manifest becomes *by definition* a non-fixture, which converts "unexpected file" from a
    loud failure into a silent exclusion, and — per D-FC-7's second clause — leaves that
    file un-validated, so **FIX-001** stops covering it. The mechanism that was supposed to
    remove a hand-maintained list would remove a detector instead.
- **D-FC-6.** Timing: this change starts only after **all five** M8 slices merge, not
  after slice a (§6) — b and c are in flight, d and e follow, and each authors fixtures and
  so rewrites the same assertions. It does not land as part of any M8 slice. No date: the
  M8 owner signals when slice e lands.
- **D-FC-7. [F5]** A **missing or unparseable manifest is a hard test failure** with a
  named error, in all three languages. Never "no manifest, no expectation" — that is the
  one way this design can fail silently and stay green. It is reachable, not theoretical:
  all three harnesses locate the repository root by probing for
  `specifications/fixtures/schema/fixture.schema.json`
  (`python/tests/harness/fixture_loader.py:33,51-60`;
  `rust/…/tests/harness/fixture.rs:239-244`;
  `dotnet/…/Harness/FixtureRepository.cs:77`), so a checkout or test sandbox carrying
  `specifications/` but not `tools/` finds a root and then finds no manifest.
  **Second clause, binding on the implementation:** the comparison direction is
  **enumerate the directory, then compare to the manifest** — never iterate manifest ids
  and load each by id. The loaders validate each document as a side effect of enumeration
  (`fixture_loader.py:63-70`→`116-124`; `FixtureRepository.cs:102-105`→`135`;
  `fixture.rs:181-186`), so manifest-driven iteration would silently skip **FIX-001**
  validation of any file present but unexpected — the fixture most likely to be malformed.
- **D-FC-10. Addendum, post-acceptance.** The implementation must ship a **proof that each
  new assertion can fail**. Concretely: for each of the three languages, a test that
  perturbs the expectation — a manifest with one id removed, one added, and one renamed —
  and shows the corpus assertion failing in each case; plus D-FC-7's missing and
  unparseable cases. Not a style preference: this record's whole subject is a detector, and
  an undetectable detector is worse than none, because it reads as coverage. The
  perturbation test is what would have caught every instance of the shape §1a now tracks,
  including this record's own prose-gate defect.
  **Why this is a decision and not a note.** The shape recurred a fifth time during M8,
  inside the milestone's own test code and in its purest form: a `TST-051` log-hygiene
  assertion that scans a capture which is empty on that path, so it cannot fail — reported
  by the M8 owner as required fix c-1 of its slice-b/c review. Not verified here: those
  files are on an unmerged M8 branch and are not present in this worktree. What makes it
  bear on *this* record is the M8 owner's sibling comparison — the neighbouring slice
  asserts its capture is non-empty *before* scanning it, so the defence is already known
  and available in this codebase and simply is not systematic. `ROADMAP.md` R-10 records
  `TST-051` as having been defined and never executed once before, at M2a. This record adds
  assertions in three languages; without D-FC-10 it is a candidate for instance six, and it
  would be a poor record that documented the shape four times and then shipped it.
- **D-FC-9. [M8]** The prose gate must be **self-verifying**: it takes §1a's site list as
  data and **fails when any listed site is not matched by its pattern**, separately from
  whether the matched number is current. Without that, a gate can silently cover a subset —
  which the anchor proposed in §1a already did, missing `dotnet/README.md` entirely (§1a).
  A pattern is auditable only if something fails when it stops matching. This is the
  generalisable fix and it outranks the choice of pattern.
  Given D-FC-9, the phrasing question is secondary, and the record takes the M8 owner's
  preference: **normalise `dotnet/README.md` to the other three sites' phrasing** and keep
  one tight pattern, rather than widening to an alternation such as
  `[0-9]+ (conformance fixtures|fixtures on disk)`. One phrasing for one fact is easier to
  keep true than a pattern with branches, and the README is where phrasing drift is most
  likely to recur. That makes the prose follow-up **five** items: four counts plus one
  phrasing change.
- **D-FC-8. [F8]** The **prose gate is split into its own change** and is not part of this
  one. `repo-gates.yml` runs on `pull_request`, so gating `ROADMAP.md` §2 would make every
  mid-milestone corpus change require an edit to the single file the milestone-exit session
  also edits under **REC-002** — importing exactly the collision D-FC-6 exists to avoid.
  Revision 1 called this "near-zero marginal cost"; it is the highest risk per unit of
  value in the change. When taken, the gate anchors to §2's specific rows rather than
  matching a bare three-digit number anywhere in the file.

## 5. Finding: the three loaders exclude non-fixture JSON by three different rules

Not a design option — a latent parity defect this analysis surfaced, which any future file
added under `specifications/fixtures/` will trip:

| Loader | Rule |
|---|---|
| `python/tests/harness/fixture_loader.py:63-70` | excludes any path with a component named `schema` |
| `dotnet/.../Harness/FixtureRepository.cs:101-103` | excludes exactly `<root>/schema/fixture.schema.json`, by full path |
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

**Neither divergence is live today, and row 2 is the plausible one.** An earlier draft of
this section called row 3 an active hole in Rust. That overstated it, and both peer
sessions corrected it independently: `find specifications/fixtures -name
'fixture.schema.json'` returns exactly one path, the canonical one, so **no fixture is
being dropped by any of the three rules today**. Ranking what could change that:

- **Row 2 is ordinary.** It needs only a second file added to the existing `schema/`
  directory — a schema revision beside `fixture.schema.json` is a normal thing to do, and
  the corpus has already been regenerated once during M8.
- **Row 3 is remote.** It needs a fixture whose id is `fixture.schema`, since files are
  named `<id>.json`. One nuance worth recording rather than inheriting: the M8 owner's
  reason — that the naming convention forbids the id — does not quite hold. The schema's id
  pattern is `^[a-z0-9]+(\.[a-z0-9-]+)+$` (`specifications/fixtures/schema/fixture.schema.json:18-21`),
  which `fixture.schema` **matches**. What actually makes it implausible is that no fixture
  *area* is called `fixture` — a convention nothing asserts, not a constraint. The rule is
  wrong in kind and should still be fixed; it is simply not urgent.

So D-FC-5 is a latent-defect fix with no outage behind it, which is what makes it safe to
sequence separately rather than squeeze into an M8 slice. Which rule the three converge on
is settled by **D-FC-1a**, and the maintenance objection to it by **D-FC-5a**.

**Window confirmed shut from the M8 side.** The M8 owner has verified this finding
independently at source, and has briefed slices d and e that no M8 slice adds a
non-fixture JSON under `specifications/fixtures/`. D-FC-5 therefore stays a latent defect
rather than an active one for the duration of M8.

## 5a. Referred out and confirmed: Rust does not discharge FIX-001

Raised by the first architecture-review round, and since confirmed independently by both
the reviewer and the M8 owner. `rust/…/tests/harness/fixture.rs:227-236` checks only
`document.is_object()` and `self.schema.is_object()` — no schema evaluation occurs at all —
where Python uses `Draft202012Validator` and .NET a shared `JsonSchema`. **FIX-001**
(`appendix-c-conformance-fixtures.md:65`) is a MUST, so Rust does not meet it. Entirely
pre-existing, and well outside this record's scope.

It bears on this record in one narrow way, which is why it is recorded here rather than
dropped: if Rust performs no schema validation, the corpus-count assertion is currently
Rust's *only* real cross-check on the corpus, which is an additional argument against
option E in Rust specifically.

**Action: referred out, and now owned as R-25's sibling.** I deliberately did not verify it
myself — verifying and then folding it in would repeat the mistake D-FC-5 exists to avoid:
one defect, one owner, one record (**CLA-008**). The M8 owner has since verified it and
booked it as **R-24** — *pending*, not done: the row sits on an unmerged M8 branch and is
**not** observable in this worktree (`grep '^| R-2[4-9]' ROADMAP.md` returns nothing here;
R-23 is the last visible row). That is consistent with §6 step 4, but it is a file state
this record cannot assert as landed. The finding is wider than the review's reading:
Rust loads the schema only to locate the repository root and then checks solely that the
document's root is a JSON object, and **four** Rust call sites carry the message
`all repository fixtures must validate` against that implementation
(`fixture_harness.rs:18`, `:52`, `error_fixtures.rs:33`, `transport_fixtures.rs:17` —
verified by `grep`, after this record cited two of the four wrongly for one revision. R-24
inherits its evidence from this paragraph, and line numbers have now drifted in three
consecutive rounds of this record, which is an argument for citing by symbol wherever one
exists).
`FIX-001` is a MUST, so Rust does not currently meet it.

The consequence bears on this record's own reasoning, which is why it stays recorded here
as well as in R-24: **a malformed fixture that Python and .NET reject passes in Rust**, so
Rust's green suite proves less than it appears to, and the corpus-count assertion is
currently Rust's only real cross-check on the corpus. That is a third instance of this
repository's recurring shape — R-19's fixture, R-23's generator, now the harness itself:
a check that passes for a reason unrelated to what it claims to prove. It also raises
option E's cost in Rust specifically (§3), and the exposure grows at Stage 2 when Rust
starts consuming the whole corpus rather than the transport and error subsets.

## 6. Sequencing

M8 runs as five slices (a done and unmerged on
`m8a-recognition-qualifier-alternation`; b Transit, c TOTP, d rate gate plus batch,
e pagination plus cache). Slices b–e keep touching `dotnet/` and the corpus, and this
change rewrites the same assertions. Landing it mid-milestone guarantees a conflict in
files two sessions are both editing, which is the failure this repository has already paid
for once.

1. **All five** M8 slices close and merge, `ROADMAP.md` updated per **REC-002** by the M8
   owner, who signals when slice e lands.
2. This record goes to architecture review (§4.2 row 4) and is accepted.
3. Implementation is delegated as a single work package: the generator and gate, then the
   seven code sites in §1a — including making the .NET method name count-free. **The four
   prose sites are not in this package** (D-FC-8, **[V4]**); they follow as their own
   change. The contract is settled by this record, so it is **row 2**
   (`eng-implementation`), not row 3 — including each parity pass. The brief carries §1a's
   site list verbatim per D-FC-1c, not a count of sites.
4. Handback review by the Strategic tree, parity check across all three languages
   (**VER-002**), `CHANGELOG.md` entry under **Fixed** (**REC-001**), and the
   **R-25** row opened and closed in `ROADMAP.md` §8 in the same change.

**`ROADMAP.md` is deliberately not edited by this record. The risk id is issued: R-25.** The M8 owner owns `ROADMAP.md` §8 and allocates risk numbers at M8
exit (the R-16 session may want a row too), so the row below is referred to as **the
count-derivation row** until the M8 owner issued **R-25** for it, taking **R-24** for the
Rust `FIX-001` gap in §5a. The M8 owner has confirmed the row is
mine to write, on the grounds that **REC-005** wants it to link to the rationale, which
lives here.

> | R-25 | **The conformance-fixture corpus count is a magic
> number hand-transcribed into seven code sites and four prose sites** (DR-0015 §1a). It has
> drifted on
> every corpus change since M5 and reddened `main` twice in four days (M5's 218→224 and
> M6/M7's 224→230, both `dotnet/`-only; M8a's 230→236 is the third hand transcription and
> was correct only because one session did every site by hand). The guard is worth keeping,
> but **no requirement mandates it**: `TST-010` is about loading fixtures from the
> repository rather than copying them and `FIX-001` about validating each against the
> schema, both discharged by the loader path, and `specifications/` states no corpus count
> anywhere — so this is unrequired drop detection kept on engineering grounds
> ([DR-0015](decisions/0015-fixture-corpus-count-derivation.md) §1) | R2 |
> **Open, designed.**
> [DR-0015](decisions/0015-fixture-corpus-count-derivation.md) chooses a generated
> committed manifest under `tools/fixtures/` — count plus sorted id list — read by all
> three harnesses and protected by generator plus `git diff --exit-code`, reusing D-M1c-1's
> gate shape. Sequenced strictly after **all five M8 slices** merge (D-FC-6). Carries a
> second finding (DR-0015 §5): the three loaders exclude non-fixture JSON by three
> *different* rules, and would not agree on which way the count moved — tracked separately
> as D-FC-5, not folded into this change |

Until step 1, the correct action on a corpus change remains hand transcription — but of
**§1a's full list**: seven code sites including the .NET method name, and **four** prose
sites — `ROADMAP.md:34`, `:62`, `:69` and `dotnet/README.md:133`.
Revision 1's "six assertions and both prose sites" would have left a fourth transcription
incomplete in exactly the way the first three were.

**This is the durable fix, not the outage fix.** M8 slice a is up as
[PR #2](https://github.com/ffquintella/bastionvault-integration-sdk/pull/2) and brings all
three languages to 236, so the red `main` in §1 is already being closed by the M8 owner.
Nothing here needs to race M8, which is what makes D-FC-6's "wait" cheap rather than a
tolerated outage.
