# DR-0018 — M11: documentation and usage guides, .NET (Stage 1)

**Status:** accepted (framing), revision 4 (2026-09-22). Revision 1 was **approved with
required fixes** by Strategic-tree architecture review (`agents.md` §4.2 row 4); all five
findings are applied in this revision and are marked **[rev 2]** where they changed a
decision. Authored by the Strategic
Orchestrator as the milestone's framing record, before any slice is dispatched
(**CRS-001**). Each slice appends its own `D-M11-n` entries below rather than opening a
second record.

**Risk tier:** **R1 except slices c, e and g, which are R2** (`agents.md` §5.3), assigned
here before dispatch. §5.3 scores risk as the highest tier any dimension reaches, so
"an R1 milestone with R2 slices" would be a contradiction rather than a refinement; the tier
is stated per slice instead, and D-M11-9 gives the reason for each. The R1 slices change no
library behaviour and their blast radius is `docs/`, doc comments, and CI. Nothing here
reaches R3: `specifications/` is not amended, and DOC-030's actual publication is explicitly
*not* attempted (D-M11-8). **[rev 2]**

**Milestone:** M11, eight slices · **Date:** 2026-09-22

**Supersedes nothing. Amends nothing in `specifications/`.**

**Discharges:** `ROADMAP.md` §4's M11 row — the 21 `DOC` IDs on the traceability baseline —
**less `DOC-030`, which is held back with a named owner** (D-M11-8), so **20 of 21**. Plus
`CNF-026` (docs build succeeds; every public symbol documented; every sample compiles and
runs), which is M11's by its own text and is named in the §5 exit criteria but is not in the
21-ID count. Plus the documentation obligation M11 retains from
[DR-0014](0014-r16-srv-resolver-and-silent-discovery-degradation.md) D-R16-7 — risk
**R-26**, the macOS scoped-resolver caveat and the `DSC-050` nameserver override as its
remedy (D-M11-6).

**Inherits:** [DR-0009](0009-m4-kv-engine.md) D-M4-3 and
[DR-0013](0013-m8-transit-totp-and-efficiency.md) D-M8-6 (the R-14 conformance-declaration
history this record adds a fourth reading to), [DR-0014](0014-r16-srv-resolver-and-silent-discovery-degradation.md)
D-R16-7 (the R-26 obligation), [DR-0017](0017-m10-remaining-bindings-and-identity.md) (the
surface being documented, and the concurrency precedent its slices b/c/d set).

## Problem

`ROADMAP.md` §4 books M11 at 21 requirement IDs, **Large** size, **1 slice**. The one-slice
booking is the part that does not survive grounding. Measured against the repository as it
stands on 2026-09-22:

| What section 16 requires | What exists today |
|---|---|
| 13 mandatory documents D1–D13 | **No `docs/` directory exists.** `dotnet/README.md` exists and states at line 231 that it is *not* D1 |
| 13 usage guides of section 17, adapted and runnable (D2, D4, D5, D6, D8) | none |
| DOC-003: every sample compiled **and executed** in CI via a `docs-samples` project | **no `docs-samples` project exists**; the one README snippet is labelled "illustrative, not compiled" |
| DOC-005/DOC-006: every public operation's doc comment carries purpose, HTTP method + path, wire parameter names, return semantics, its specific error codes, conformance level, spec link, and a `<spec>` tag | **`<spec>` tags: 0 occurrences in 145 files.** HTTP method + path appears on a small minority. Requirement IDs *are* cited on 1456 lines, so the raw material is good and the gap is format and completeness, not absence |
| DOC-002/DOC-024: D3 and D7 generated from code, or drift-tested | the **error catalogue** has exactly this machinery already (`tools/error-catalogue/`, regenerate-then-`git diff --exit-code` in `repo-gates.yml`). The **configuration table has none** — no shared source between `specifications/02-client-configuration.md` and `ClientConfig.cs` |
| DOC-020…DOC-025: six CI gates | **none of the six exists.** No link checker, no spell checker, no samples job, no docs build job in any workflow |
| DOC-021: 100 % public symbols documented | **already met.** `GenerateDocumentationFile` + `TreatWarningsAsErrors` are both set and `dotnet build` is 0 warnings / 0 errors, so CS1591 is live and green |

The surface those documents describe is **273 public types and 689 public methods**, of which
**473 sit on 63 `*Operations` facades** reached from 17 top-level properties on
`BastionVaultClient`. That is the M11 sizing fact the 21-ID count is silent about, exactly as
the 9-ID count was silent about M10's eight mounts ([DR-0017](0017-m10-remaining-bindings-and-identity.md),
Problem). **A milestone booked at one slice against 473 operations and 13 documents is not a
plan; it is the absence of one.**

A second problem is specific to this milestone and has already cost the project three
milestone exits: **R-14**. See D-M11-7 — the short version is that M11 landing does **not**
make any conformance level declarable either, and this record refuses to book a fourth gate
on the belief that it does.

## Decisions

### D-M11-1 — Eight slices, dispatched in three waves

M11 splits into eight `eng-*` briefs. Unlike M9 and M10, the slices do **not** all contend
for `PublicApiSurface.txt` — only the doc-comment slices touch `dotnet/**/*.cs` at all, and
they touch disjoint files — so §7.4's parallelism test is genuinely satisfiable here rather
than waived. The binding constraint is instead `skills/claude/SKILLS.md` §8: **at most 3
concurrent Codex handoffs in flight.**

| Slice | Scope | Deliverables | Rung |
|-------|-------|--------------|------|
| **a** | **Pathfinder: the docs contract.** `docs/` layout, the snippet mechanism, the `DocsSamples` project, and D2 as its first real consumer proving the machinery end to end | D2; DOC-003, DOC-004, DOC-010…DOC-015 conventions | `eng-deep` |
| **b** | Generated references, **plus guide 13's hand-written walkthrough** | D3, D7; DOC-002, DOC-007, DOC-024 | `eng-implementation` |
| **c** | Auth and secrets guides (section 17 guides 2–7) | D4, D5 | `eng-implementation`, **R2 gate** |
| **d1** | Engine guides, crypto and credential engines (guides 8, 12) | D6 pages: Transit, PKI, SSH, TOTP | `eng-implementation` |
| **d2** | Engine guides, section-12 engines | D6 pages: LDAP, Files, Resources, Identity, Notifications, Cert lifecycle, Rustion | `eng-implementation` |
| **e** | Operations, compatibility, security, contributing (guides 9, 10, 11) | D8 (**carries R-26**), D9, D10, D13 | `eng-implementation`, **R2 gate** |
| **f** | **DOC-005/DOC-006 doc-comment pass** over the 473 facade operations | `<spec>` tags, HTTP call, wire names, error codes, conformance level, spec link | `eng-implementation` ×3 |
| **g** | D1 rewrite, D11 API reference, and the six CI gates | D1, D11, DOC-020…DOC-025, DOC-031 | `eng-implementation`, **R2 gate** |

**Wave order.** `a` alone → then `b`, `c`, `d1` → then `d2`, `e`, `f1` → then `f2`, `f3`, `g`.
`a` is serial because every later slice consumes the snippet contract it defines; `g` is last
because a gate must not land red (D-M11-5). `f` splits by facade group into three disjoint
file sets: **f1** `Kv*`, `Auth*`, `Sys*`, `Logical*`; **f2** `Transit*`, `Totp*`, `Pki*`,
`Ssh*`, `SshBroker*`; **f3** `Ldap*`, `Files*`, `Identity*`, `Notifications*`, `Resources*`,
`Rustion*`, `AssetGroups*`, `CertLifecycle*`.

**Why `eng-deep` for `a` and nothing else.** `agents.md` §4.2 row 3 condition **(a)**: slice a
is the pathfinder pass that first defines a contract — how a sample is stored, compiled,
executed and kept byte-identical to the markdown that shows it — and condition **(d)**: it
spans `docs/`, a new test project, the solution file, and CI. The trigger is recorded here,
**before dispatch**, as §4.3 rule 4 requires. Every other slice implements a contract this
record and slice a have already pinned, which is row 2 by the discriminator in §4.2, however
large the slice is. **`f` is not row 1** despite looking mechanical: choosing which error
codes an operation can raise beyond the common set is a parity-and-correctness judgement, and
row 1's trigger excludes anything spanning two files.

### D-M11-2 — D1 is `dotnet/README.md`; everything else lives under `docs/dotnet/`

**Decision.** D1 stays at `dotnet/README.md` and is *rewritten* there, not copied. D2–D13 are
new files under **`docs/dotnet/`**, with `docs/README.md` as an index that reserves
`docs/rust/` and `docs/python/` for M13.

**Why.** DOC-031 requires the package-registry README to be the same D1 file, single source.
`dotnet pack` takes the project's README, so D1 must be the file that already sits beside the
`.csproj` — a `docs/dotnet/D1.md` would immediately need a copy, which is the drift DOC-031
exists to forbid. `PackageReadmeFile` is currently **unset** in the csproj, so slice g wires
it as part of DOC-031 rather than assuming `dotnet pack` picks it up.

The per-language subdirectory is chosen now, at the only point where it is free, because
section 16 binds all three SDKs and M13 will otherwise have to move .NET's files to make room.

### D-M11-3 — Samples are real compiled code; markdown embeds them by an exactness check

**Decision.** Every sample in D1–D10 is a real method in a new
`dotnet/BastionVault.IntegrationSdk.DocsSamples/` **test** project, executed by `dotnet test`
against the existing `Harness/InProcessHttpsMockServer.cs`. Each markdown fenced block that
shows a sample is verified **byte-identical** to its source region by a test that fails on
drift — the same regenerate-then-diff shape `repo-gates.yml` already runs for the error
catalogue, which is the pattern this repository has already proven.

**Why a test project and not `examples/`.** DOC-003 requires samples to be *compiled and
executed*; an `examples/` console app is compiled and, in practice, never run. Executing them
under the existing mock server gets DOC-003's "executed" limb honestly, and CNF-011 excludes
example programs from coverage, so this cannot inflate the coverage figure either way.

**Samples that need a live server.** DOC-003's last sentence puts them in the integration job,
which is M12's. Any sample slice a or later cannot drive against the mock server is
**listed explicitly in D13 with the reason**, not silently dropped and not faked. A sample
that is neither executed nor listed is a slice failure.

**DOC-004.** Every sample shows complete error handling, or carries a comment saying it does
not. Reviewers check this per-sample; it is the single most commonly skipped DOC requirement
and it is the one an SDK user is most directly harmed by.

### D-M11-4 — D7 is generated; D3 is drift-tested. Neither invents a source of truth

**Decision.** DOC-002 permits generation *or* a failing drift test, and the two documents get
different answers because their sources differ:

- **D7 (error reference) is generated.** `tools/error-catalogue/emitters.py` already emits
  three languages from `specifications/appendix-b-error-catalogue.md`; slice b adds a
  **markdown emitter** and puts `docs/dotnet/` D7 under the regenerate-then-diff gate that
  already exists in `repo-gates.yml`. DOC-007's "every hint reproduced verbatim" then holds
  by construction rather than by proofreading.
- **D3 (configuration reference) is drift-tested, not generated.** There is no shared source
  between `specifications/02-client-configuration.md` and `ClientConfig.cs` today, and
  **inventing one is not M11's job** — a new config manifest is a contract change, which is
  row 3 and Architect territory, for a milestone booked as documentation. Slice b instead
  adds a reflection test over `ClientConfig` and `Internal/ConfigurationResolver.cs` that
  fails when a setting, env var, default, or validation code exists in code but not in D3, or
  the reverse.

**What this gives up.** D3's prose can still be *wrong* in ways reflection cannot see — a
default documented as `30s` when the code says `30s` but the spec says `10s` is invisible to
it. That residual is accepted and named here rather than discovered at review: the drift test
covers *presence and shape*, and the spec-value check stays a human review step in slice b's
acceptance criteria.

### D-M11-5 — Gates land last, and no gate lands that only looks green

**Decision.** DOC-020…DOC-025 are wired in slice **g**, after the content they gate exists.
Each gate must be demonstrated to **fail** on a seeded violation before slice g is accepted:
a deleted document for DOC-020, a broken link for DOC-023, a misspelling for DOC-025, an
edited generated table for DOC-024, an edited snippet for DOC-022.

**Why the seeded-failure proof is mandatory.** D-M1b-19 is this repository's own precedent:
`Microsoft.CodeAnalysis.PublicApiAnalyzers` was a green gate for an entire milestone while
silently enforcing nothing, and it took a seeded violation to find out. A docs gate is
*exactly* the kind that rots this way — `lychee` with a wrong glob, `cspell` with a dictionary
that swallows every unknown word — and it fails silently in the direction that looks like
success. **A gate that has not been observed failing has not been tested.**

**Why last rather than first.** A presence gate wired before D2–D13 exist is red on `main`
for the length of the milestone, which trains every reader to ignore it.

### D-M11-6 — R-26 and `DSC-050` are D8's, and stated as an operator-visible failure

**Decision.** D8 (resilience and operations) MUST state, as running prose and not a footnote:
that default SRV resolution on **macOS** cannot see scoped resolvers, because
`GetIPProperties().DnsAddresses` returns one identical global list on every interface; that
on a split-horizon VPN this makes the resolver query the wrong nameserver; that under
`DSC-017`'s strict default the client therefore **refuses at startup rather than degrading
silently**; and that `DiscoveryConfig.Nameservers` (`DSC-050`) is the supported remedy, with a
complete example. Linux and Windows are in-platform and unaffected — say so, so a reader does
not apply the workaround where it is not needed.

This discharges D-R16-7's obligation and is the whole of what M11 owes R-26. It replaces
R-16's original M11 obligation ("a resolver is required for real discovery"), which stopped
being true when one shipped at `0.14.1`.

### D-M11-7 — No conformance level is declared at M11, and R-14 is now a fourth measurement

**Decision.** M11 declares **no conformance level**, and `ROADMAP.md`'s M11 row is not to be
read as making one declarable. `dotnet/README.md`'s gap list is regenerated instead — the
fourth time, after M4, M8 and M10.

**The grounding, stated once so no later milestone re-derives it.** `Core` — the *first*
declarable level — requires sections 00, 01, 02, 03, 04, 07, **15**, **16**, 17, plus subsets
of 05, 06 and 13. M11 clears 16 and 17. **Section 15 is not cleared**, and the reason is
`CNF-001`, not the baseline: section 15 requires a live-server integration suite and
**no such suite exists on disk** — that is an unimplemented MUST, which `CNF-002` forbids
claiming over. So no level is declarable at M11.

**What this record does *not* claim, and why — a correction from review. [rev 2]**
Revision 1 said "`Core` becomes declarable at M12". That inference was drawn from *baseline
membership*, and the baseline does not mean what the inference needed it to mean.
`tools/traceability/traceability.py:400` computes `missing = applicable - covered -
baseline`, and the module docstring (lines 10–31) is explicit that the parser reads **test
files only** and "does not infer IDs from production code": an ID is on the baseline when
**no test references it** (`CNF-014`), which is a different proposition from "its MUST is
unimplemented" (`CNF-001`). Applied consistently, revision 1's own rule would have made
`Core` undeclarable at M12 as well — 18 `CNF`, 3 `OVR`, 4 `TRN`, 3 `ERR` and 5 `FIX` IDs sit
inside `Core`'s sections and none of them is M12's integration work.

So: **whether `Core` is declarable at M12 is not determined by anything this record can see,
and M11 does not decide it.** It requires a per-ID audit separating "no test references it"
from "its MUST is unimplemented" across every `Core` section — which is `CNF-014` and
`CNF-001` asking two different questions of the same list. That audit is **booked to M12**
as a precondition of any declaration, and is the concrete deliverable this record hands to
the open question below. Conflating the two readings is how R-14 has survived four
milestones; a fifth recurrence would be this record's fault, not the roadmap's.

`Standard` adds nothing M11 blocks. `Complete` additionally needs section 09, which still
carries `PKI-030` (**R-31**, held back at M9 for a server message string no document
states) — that one *is* an implementation gap, verified at M9, not a coverage gap.

**Therefore §10 question 4 is now answered by exhaustion, not by choice.** Option (a) — "move
M11 ahead of M8" — required M8 and M10 to be unspent; both are spent. What is left is option
(b), which is not a decision anyone took: it is what happens when a question is not answered
for four milestones. The cost is now measured, not forecast: **four stated milestone exits,
every one of which would have been the price of answering the question once, at M4.** This
record does not re-recommend; it records that the recommendation was made three times, and
escalates the remaining question — *once M12's audit says which levels are legal, is the
first legal level declared immediately, or does the project wait for `Complete` and
`PKI-030`* — to the project owner as the outward-facing call it is (`agents.md` §5.4, last
row).

### D-M11-8 — `DOC-030` is held back with a named owner, not claimed

**Decision.** `DOC-030` (rendered documentation published per release, versioned so users of
older SDK versions can read matching docs) **stays on the traceability baseline** at M11's
close. Slice g builds the DocFX site and the versioned layout; it does **not** enable
publication.

**Why.** No release is cut in M11, so there is nothing to publish "per release", and turning
on a publishing pipeline is outward-facing and irreversible in the way `agents.md` §5.4
reserves for human confirmation. Claiming `DOC-030` on a pipeline that has never published
would be precisely the "gate that looks green" D-M11-5 exists to forbid, one level up.

M11 therefore lands **20 of 21** `DOC` IDs. This is the `PKI-030` and `R-35` handling applied
a third time: a requirement half-met is reported as not met, with the owner named, rather than
rounded up.

**`DOC-031` is different and does land**: it requires the registry README to *be* the D1 file,
which is a `PackageReadmeFile` wiring plus a test, and needs no publication to be true.

### D-M11-9 — Three slices are R2; the rest are R1

**Decision.** Slices **c**, **e** and **g** are **R2**; slices a, b, d1, d2 and f are **R1**.
Under `agents.md` §4.4 an R2 handback gate is Claude Opus 5 in the Strategic tree rather than
Claude Sonnet 5.

- **Slice c** carries **D4**, whose section-16 content is token lifecycle, auto-renew, the
  login-failure-as-200 rule, machine identity and namespaces
  (`specifications/16-documentation-requirements.md:17`). **`CRS-003` names auth and tokens
  explicitly**, and D4 is operator-followed instruction by the identical argument that raises
  slice e. Revision 1 gated e and not c, which is the inconsistency review caught; `CRS-006`
  resolves it upward, not downward. **[rev 2]**
- **Slice e** carries **D10**, the security guide: what the SDK never logs, token file
  handling, TLS defaults, how to pin a CA, why `TlsSkipVerify` is dangerous, recommended
  least-privilege policies. `CRS-003` puts anything touching auth, tokens, TLS or secret
  material at R2 minimum. Prose is not code, but a security guide is *instructions an
  operator will follow*, and a wrong one causes the misconfiguration it was written to
  prevent. `CRS-006` resolves the ambiguity upward, not downward.
- **Slice g** wires the gates. A gate accepted wrongly is not a documentation defect; it is a
  permanently weakened quality floor, which `CLA-004` forbids and D-M1b-19 already cost this
  repository once.

**The R2 parity limb is vacuous here, and that is recorded rather than quietly skipped.
[rev 2]** `agents.md` §5.3 gates R2 at "R1 plus Architect decision record plus parity check
across all three languages". The decision-record limb is satisfied by this record. The
**parity limb has nothing to check**: M11 is .NET-only by Stage-1 sequencing, and `rust/` and
`python/` documentation is M13's, so there is no second implementation for a parity check to
compare against. Stating this is the point — an unsatisfiable limb left unstated is exactly
the R-14 failure mode, one gate down.

### D-M11-10 — D6 is one page per mount, eleven pages

**Decision.** D6's "one page per engine" means one page per **mount**, not one per class. The
eleven pages are exactly section 16's own list: Transit, PKI, SSH, TOTP, LDAP, Files,
Resources, Identity, Notifications, Cert lifecycle, Rustion. Nested administrative facades
fold into their engine's page (`Pki.Acme`, `Pki.Csr`, `Pki.SignRequests` → the PKI page;
`Rustion.Recordings`, `Rustion.Policy` → the Rustion page). `Auth.*` admin surfaces —
including `Auth.AppId.Admin` and `Auth.Userpass.Admin` — fold into **D4**, not D6, because
authentication is D4's subject. `Sys` folds into **D8** (guide 11, operating the vault), not
D6, because it is not an engine.

**Why this needs deciding here.** The survey found 63 facade classes against section 16's
11-engine list; a slice briefed without this ruling either writes 63 pages or picks a number
on its own, and the two engine slices would pick differently.

### D-M11-11 — Guide 13 belongs to D7, not D13; D13 is the contributing guide **[rev 2]**

**Decision.** Section 17's **guide 13 (Diagnosing errors)** is D7's, not slice e's. Section 16
puts the "reading an error" walkthrough and the `ERR-002` one-line format in **D7**
(`specifications/16-documentation-requirements.md:20`). **D13 is the Contributing / testing
guide** (`16:26`) and its required content is: how to run the unit, conformance, contract and
integration suites; how fixtures are loaded; how to add a requirement marker; the coverage
commands from section 15.

Revision 1's slice table read "guides 9, 10, 11, 13 → D8, D9, D10, D13", which silently
mapped guide 13 onto D13 on the strength of the number. Left uncorrected it would have put
error-diagnosis prose into the contributing guide, shipped D7 without its walkthrough, and
left D13's actual content unbriefed — with `DOC-001`'s word count passing on all three.

**Consequence for D-M11-4.** D7 is therefore **not wholly generated**: it is a generated
region plus hand-written prose. The markdown emitter MUST write only between explicit
`<!-- generated:error-catalogue:begin -->` / `:end` markers, and the regenerate-then-diff
gate MUST compare only that region, so the walkthrough is not clobbered on every
regeneration. A generator that owns the whole file would delete guide 13 the first time it
ran.

### D-M11-12 — D12 is `CHANGELOG.md`, Strategic-owned, and the presence gate points at it **[rev 2]**

**Decision.** D12 is the existing root `CHANGELOG.md`. It is **not** moved or copied under
`docs/dotnet/`, and **no Engineering-tree slice edits it** — `REC-004` makes both files
Strategic-tree owned. Slice **g**'s `DOC-020` presence check therefore takes D12's path as
the repository root, not the `docs/dotnet/` glob D-M11-2 sets for D2–D11 and D13.

Revision 1 did not mention D12 at all. `DOC-001` requires it to exist with non-placeholder
content and `DOC-020` gates its presence, so an unmentioned D12 gives slice g a choice
between a glob that misses it — a green gate checking twelve of thirteen documents, which is
what D-M11-5 exists to forbid — and one that is red on `main` forever.

**The one piece of D12 content work M11 owes**, and it is the Strategic Orchestrator's own:
section 16 requires each entry to list added/changed/removed **operations**, new **error
codes**, and **the spec version implemented**. The Strategic Orchestrator audits the existing
entries against that shape at milestone close and brings the format forward; it does not
rewrite history that predates the requirement.

### D-M11-13 — DOC-006's tags get a consumer in the same milestone that writes them **[rev 2]**

**Decision.** Slice **g** adds a gate that reads the `<spec>` tags slice f writes: for every
public operation on the 63 facades, the tag MUST exist, MUST parse as
`<spec>Canonical.Operation.Name — AREA-NNN</spec>`, and its requirement ID MUST exist in
`specifications/appendix-d-requirement-index.md`. Like every other gate it ships with a
seeded-failure proof (D-M11-5).

**Why this is not optional polish.** `DOC-006`'s stated purpose is "so the traceability tool
can link docs to requirements" (`16:44`), and `tools/traceability/traceability.py` reads test
files only — by its own docstring it "does not infer IDs from production code". Without the
gate, slice f emits 473 tags that **nothing reads**, `DOC-021` checks only that a doc comment
is *present*, and the first M12 operation to ship with no tag does so with every gate green.
That is a requirement satisfied once and decaying immediately, which is the same defect as a
gate that looks green.

**Scope guard.** The gate is a check, not an extension of the traceability tool. Teaching
`traceability.py` to consume production-code tags would change what the baseline *means* —
the precise conflation D-M11-7 was corrected for — and is not M11's to do.

## Slice a — accepted 2026-09-22

Handback reviewed and **verified independently** by the Strategic Orchestrator rather than
accepted on the delegate's report (**CCF-002**): `PublicApiSurface.txt` diff empty; unit
suite 1652/1652 with coverage 99.17 % line / 95.06 % branch, identical to the pre-slice
baseline; DocsSamples 9/9; and the drift check re-seeded by the reviewer with a *different*
one-word edit inside a fence, which it caught with file:line on both sides before being
reverted. The harness change was read in full and is purely additive — an empty route table
consulted ahead of the pre-existing single-slot response, so every existing test takes the
identical path.

- **D-M11-14 — a sample is an xunit test method; markdown embeds a marked region of it.**
  `// docs:begin <document>/<name>` … `// docs:end` in C#; `<!-- docs:sample id -->` at
  column 0 immediately above a `csharp` fence. Byte-for-byte after exactly two
  normalisations (strip common leading indentation, strip per-line trailing whitespace).
  Rejected: `#region` (invisible to a plain-text tool, collides with style analyzers) and
  line-range references (rot on every edit above the range).
- **D-M11-15 — the check compares and fails; regeneration is opt-in** via
  `BASTIONVAULT_DOCS_SAMPLES=update`, never on CI. The error-catalogue gate may regenerate
  because its output is code no human edits; here **both sides are hand-written**, so silent
  regeneration would resolve every disagreement in favour of the code and quietly rewrite a
  guide's prose-adjacent example.
- **D-M11-16 — DocsSamples reaches the harness by `ProjectReference` to the test project.**
  Rejected: linking the harness files (would force an `InternalsVisibleTo` addition to the
  *library* csproj, since `LoginRejectionMessages` reaches `ErrorCatalogData` through a grant
  to the test assembly name only) and extracting a shared harness library (moves files out
  of a `.Tests` directory, which is exactly `traceability.py`'s file filter — a documentation
  slice must not perturb the `TST-041` ratchet).
- **D-M11-17 — two sample kinds, not three.** Kind 1 constructs its own client and shows
  configuration; kind 2 takes a fixture-supplied `client`. **Consequence:** the explicit
  `BastionVaultClientOptions` form cannot execute in D2 (a literal `CaCertPath` is
  materialised at construction and throws), so **D3 owns it** and slice b must write a CA
  file to a temp path to execute that sample.
- **D-M11-18 — only `csharp` fences may carry SDK code, and every one is checked.** An
  unanchored `csharp` fence fails the build, so escaping the mechanism is a visible act
  rather than an accident. Where the SDK can generate what a guide shows, assert it
  directly: D2's `hcl` policy block is asserted equal to `PolicyBuilder.Build()`.
- **D-M11-19 — assembly-wide `DisableTestParallelization`** in DocsSamples, because process
  environment is global and kind-1 samples mutate it.

**Carried forward from the handback, for slice g:** DocsSamples is compiled by CI but not
executed by it (`dotnet.yml` tests only the Tests project), so `DOC-022` must add the step or
`DOC-003`'s executed limb is green locally and absent in CI. And the `DOC` ids must leave
`baseline.json` in the same commit that adds their `[Requirement]` markers, since the ratchet
fails when a baselined id becomes covered.

### D-M11-20 — Slice f is re-planned: the three-way split was undersized, and the brief was ambiguous **[rev 3]**

**What happened.** Slice f1 was briefed as "every `*Operations` class whose name begins with
`Kv`, `Auth`, `Sys` or `Logical` (including nested admin facades such as `Auth.AppId.Admin`
…)". **Those two clauses contradict each other** — `AppIdAdminOperations` does not begin with
`Auth`; it is reachable *through* `client.Auth`. Read literally the slice was ~40 operations;
read by access path it was ~176. **The ambiguity was the Strategic Orchestrator's, not the
delegate's.**

The delegate did the right thing and it is worth recording as precedent: it completed the
unambiguous core (40 operations, all verified), **stopped at the tier budget** rather than
silently expanding it (**TOK-011**: over budget means decompose, never raise the cap), and
explicitly refused to author ~136 further error-code lists it could not trace to source —
naming `R-23`, the incident this project already has from guessing one. It returned
confidence 0.72 and said why. **A delegate that stops at a budget with a stated reason is
behaving correctly; one that silently delivers 176 half-researched operations is not.**

**The arithmetic the original plan got wrong.** 473 facade operations, split three ways, is
~158 per slice. The measured rate for accurate DOC-005 work is **~40 operations per
Large-tier slice**. So slice f is not three slices; it is **roughly twelve**. Three was not a
tight estimate, it was an unexamined one.

**Decision — f0 first, then transcription slices.** Rather than book eleven more research
slices, the mechanical half of DOC-005 is extracted once:

- **f0 (new, `eng-implementation`)** builds a worksheet generator under `tools/` that emits,
  per public operation: the canonical name and conformance level from
  `appendix-a-endpoint-catalogue.md`, the requirement IDs already cited in its existing doc
  comment, and **the HTTP verb and path template read from the implementation**. Four of
  DOC-005's seven elements are mechanically derivable; only purpose, return/null semantics
  and the error-code list need judgement, and purpose largely exists already (2497
  `<summary>` blocks).
- **f1b…fN** then become transcription against a settled worksheet rather than research,
  which is both cheaper and more accurate — the HTTP path comes from the code instead of
  from a delegate's reading of it.

**Second benefit, which is why this is worth a slice of its own.** The same extraction gives
slice g's D-M11-13 gate a stronger check: it can verify that the HTTP verb and path a doc
comment *states* still match the code, turning DOC-005 from a one-time authoring exercise
into a drift-checked invariant. Without it the gate can only confirm a `<spec>` tag exists
and parses.

**Adopted from f1, binding on every later f-slice:** the tag format
`<spec>Canonical.Operation.Name — AREA-NNN</spec>` on its own line; pure client-side helpers
are tagged and state "HTTP call: none" rather than inventing one; `ERR-061`'s common set is
stated once per type, never repeated per member; and **no error code is cited that cannot be
traced to a throw site or existing prose** — an untraceable one is reported, not guessed.

### D-M11-21 — A `<spec>` tag cites a section when the specification has no per-operation ID, and the fallbacks are counted **[rev 4]**

**The problem, found by slice f2a and structural rather than local.** `DOC-006`'s example
pairs a canonical operation name with a requirement ID. For KV that works — sections 07
gives KV1/KV2 per-operation IDs. For other engines it does not: **Transit has 7 requirement
IDs for 19 methods, TOTP has 4 for 6.** `Transit.DeleteKey` and `Totp.DeleteKey` have no
operation-specific MUST anywhere; deletion-then-`204` appears only in an untitled status-code
table.

Slice f2a tagged both `TRN-001` — "every typed operation is built on `Logical.*`" — as the
least-inaccurate anchor, and said so **in the prose**. That was the right call with the rules
as they stood, and it exposes the flaw: the caveat is human-readable, the tag is not. A
machine reading `<spec>Transit.DeleteKey — TRN-001</spec>` is told TRN-001 governs
`DeleteKey`. It does not. Slice g's gate would pass it, because `TRN-001` is a real ID in the
index — **a gate confirming a true fact about a misleading claim.**

**Decision.** Where a per-operation requirement ID exists, cite it — unchanged. Where none
exists, the tag cites the **governing specification section** instead of an ID:

```
<spec>Transit.DeleteKey — 08-transit-engine.md</spec>
```

This is machine-distinguishable by inspection (a `.md` suffix, never an `AREA-NNN`), so
slice g's gate accepts both forms and **counts the fallbacks separately**. It satisfies
`DOC-006` — whose MUST is on the canonical operation name, with the ID shown by example —
and it keeps the requirement's stated purpose, linking documentation to the specification
text that governs it.

**The reason this is worth a decision rather than a convention.** The fallback count is not
a blemish to minimise; it is **a measurement the project does not currently have**. "These N
operations have no requirement ID of their own" is a worklist for a future specification
revision, and it is invisible while every operation is tagged with a plausible-looking ID.
Reusing a near-miss ID hides the gap; the fallback form publishes it.

**Reusing one ID across several operations stays legitimate** where the ID genuinely governs
them all — f2a's `TRN-050` for `ListKeys`/`ReadKey` (the list-miss-returns-empty contract
literally applies) follows the KV2 precedent of `KV2-009` across Read/WriteConfig. The
fallback is only for operations no ID governs.

**Applies from now on**, and a single normalisation sweep over the ~71 tags f1 and f2a
already wrote is folded into slice g, driven by `tools/doc-worksheet` rather than by hand —
the worksheet already knows which operations have no Appendix A match. **No slice re-opens
files to fix this one tag at a time.**

## Rejected alternatives

| Option | Why rejected |
|---|---|
| **One slice, as `ROADMAP.md` books it** | 13 documents, 13 guides, 473 operations' doc comments, a new test project and six CI gates cannot fit any tier's brief budget (**TOK-011**), and TOK-011's answer to that is decomposition, not a bigger cap |
| **Do the DOC-005/006 doc-comment pass first**, then write the guides from the improved comments | Inverts the dependency. The comments need the *error-code-per-operation* judgement the guides' "what can go wrong" tables force you to make; doing comments first means making those calls twice |
| **Generate D3 from a new machine-readable configuration manifest** | A new manifest is a cross-cutting contract — row 3, Architect, and a `specifications/` conversation. Booking it inside a documentation milestone is how a Large milestone becomes an Enterprise one after dispatch. The drift test buys most of the protection for none of that scope (D-M11-4) |
| **Wire the CI gates first so the milestone is gated throughout** | Leaves `main` red for the whole milestone; a permanently red gate is ignored, which is worse than a late one (D-M11-5) |
| **Declare `Core` at M11** | `CNF-002` forbids it: section 15 carries 48 unimplemented `ITG` MUSTs. This is the fourth milestone at which the tempting answer is also the illegal one (D-M11-7) |
| **Claim `DOC-030` on the built-but-unpublished site** | A publishing requirement satisfied by a pipeline that has never published is a false green (D-M11-8) |
| **Run all eight slices concurrently**, since the files are disjoint | `skills/claude/SKILLS.md` §8 caps Codex handoffs in flight at 3, and slice a's contract is an input to all seven others. Three waves of three is the most parallelism the rules and the dependency allow |

## Acceptance criteria for the milestone

1. D1–D13 exist with non-placeholder content; every one of D2–D10 over 200 words (DOC-001).
2. Every sample compiles **and executes** in `dotnet test`; every markdown fenced sample is
   byte-identical to its source (DOC-003, DOC-004, D-M11-3). Live-server samples are listed
   in D13 with reasons.
3. Each of the six gates DOC-020…DOC-025 has been **observed failing** on a seeded violation
   (D-M11-5).
4. Every public operation on the 63 facades carries a DOC-006 `<spec>` tag and the seven
   DOC-005 elements.
5. D8 states the R-26 macOS caveat and the `DSC-050` remedy as D-M11-6 specifies.
6. `dotnet/README.md` is D1, is wired as `PackageReadmeFile`, declares no conformance level,
   and carries the regenerated gap list showing `DOC-030` held back with its owner.
7. `tools/traceability/baseline.json` drops 20 `DOC` IDs **and** `CNF-026`, each covered by a
   referencing test (**CNF-014**): **110 → 89**. `DOC-021`'s guard test asserts the csproj
   properties it depends on, so the requirement cannot silently regress. `DOC-030` remains,
   with its owner named in `dotnet/README.md`'s gap list.
8. All existing gates stay green: 1652+ .NET tests, coverage above the 95 % floor
   (**CNF-010**, **VER-004**), `PublicApiSurface.txt` unchanged by any slice except where a
   slice legitimately adds a public symbol.
9. `CHANGELOG.md` carries the milestone's entries (**REC-001**) and `ROADMAP.md` §2, §4, §5
   and §8 reflect the close, including the R-14 fourth reading and R-26's discharge
   (**REC-002**).
10. **DOC-011, DOC-012 and DOC-014 are checked per guide, not assumed. [rev 2]** Every guide
    carries the policy HCL its example needs (`DOC-011`, using `PolicyBuilder` where the SDK
    offers one); shows wire-level JSON for at least one request/response (`DOC-012`); and
    contains no real token or hostname — `s.FAKE…` and `https://vault.example.com` only
    (`DOC-014`). Slice a fixes these as conventions and slice g gates `DOC-014` by extending
    the existing `CNF-025` secret scan to `docs/`; `DOC-011` and `DOC-012` are reviewer
    checks per guide, because no gate can tell a *correct* policy snippet from a plausible
    one. Revision 1 left all three as conventions with no criterion, which is how a
    convention becomes a suggestion.
