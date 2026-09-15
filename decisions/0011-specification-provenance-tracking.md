# DR-0011 — Specification provenance tracking

**Status:** **accepted** — authored by the Strategic Orchestrator (Claude Opus 5),
**revision 3**, accepted by the Strategic Orchestrator on 2026-09-15 after **two**
`agents.md` §4.2 row-4 architecture-review rounds, each run by a Strategic-tree agent
other than the author (**REV-002**). Round 1 reviewed the design and returned *approve
with required fixes* (F-1…F-7). F-3 required the source→specification mapping to be
authored into this record, which made D-PRV-4a new load-bearing content, so round 2
reviewed that mapping alone and returned *approve with required fixes* (RF-1…RF-4, R-1,
R-2). All thirteen are folded in; dispositions for both rounds are in the closing sections.
No third round: every round-2 fix was a correction to the mapping table or its surrounding
rationale, introducing no new design content for a gate to weigh.
**R3 acceptance covers implementation only.** Releasing anything outward-facing on top of
it still needs human confirmation (`agents.md` §5.3).
**Risk tier:** R3 (`agents.md` §5.3 — it changes `specifications/`, which CRS-004 scores
R3 outright). No secret material; the manifest pins public git object ids of a public
repository.
**Milestone:** cross-cutting, outside the M-series · **Date:** 2026-09-15
**Supersedes nothing. Amends:** `specifications/01-conformance-and-quality.md`
(adds CNF-044…CNF-047), `specifications/00-overview.md`, `specifications/README.md`.
**Inherits:** nothing behavioural. It reuses the generator-plus-CI-gate shape proven by
`tools/error-catalogue` ([DR-0005](0005-m1c-error-model.md) D-M1c-1), where a generated
artefact is regenerated in CI and a diff fails the build.

## Problem

`specifications/` is the single source of truth for SDK behaviour, but it is not itself a
primary source: it was *derived* from the BastionVault server repository. That derivation
is recorded only as prose — `specifications/README.md` said the API surface was taken from
the server repo "as of September 2026", and `00-overview.md` says "Target server ≥ 0.42".

Neither statement is checkable, and the consequence is already live. Upstream
`ffquintella/BastionVault` is at **v0.44.4** (commit `381cd3d`, 2026-09-15). The
specification is dated 2026-09-13 and declares a 0.42 floor. So the specification is
somewhere between two and eleven releases behind its own source, and **there is no
mechanical way to discover that, or to tell which of the 18 specification documents a
given server release actually touches.** Every server release therefore costs either a
manual re-read of the server's API docs, or the risk of shipping an SDK against a
specification that has silently gone stale.

The forces:

1. **Detection must be cheap**, or it will not be run on every server release.
2. **Detection must be specific.** "Something upstream changed" is nearly worthless; "the
   thing that changed feeds `07-kv-engine.md` and Appendix A" is the whole value.
3. **It must not couple this repository's CI to upstream availability** in a way that
   turns an upstream release into a red build on an unrelated pull request.
4. **The hash must be verifiable both online and offline**, because maintainers work
   against a local checkout and CI does not have one.

## Options considered

### Option A — record the upstream version and date only

Add a "derived from v0.42.0, 2026-09-13" row to `00-overview.md`, expose it from the
SDKs, stop there.

Cheap and honest, but it detects nothing. A server release that rewrites `docs/api.md`
without bumping the minor version is invisible, and the maintainer still has to re-read
the server docs to find out which specification documents are affected. It satisfies the
letter of "a version tracker" and none of the stated purpose.

### Option B — vendor a copy of the upstream sources into this repository

Copy `docs/api.md` and friends under `specifications/upstream/` and diff against them.

Detection becomes exact and fully offline. But it duplicates a large, foreign,
independently-licensed corpus into this repository, it must be refreshed by hand, and the
copies rot in exactly the way the manifest is meant to prevent. Rejected: it solves the
detection problem by creating a second, larger staleness problem.

### Option C — pin git object ids in a manifest, compare against any upstream ref (**chosen**)

`specifications/provenance.json` pins, for the current specification version, the upstream
ref plus the **git object id** of every upstream source that feeds a specification
document, together with the documents it feeds. `tools/provenance` re-reads those ids at
any named upstream ref and reports the ones that moved.

The deciding property is the choice of hash. A git object id is what both sides already
speak: GitHub's tree API returns it for every blob and tree in one unauthenticated call,
and `git rev-parse <ref>:<path>` returns the identical value from a local checkout. This
was verified before choosing — `docs/api.md` at `v0.44.4` is
`9fe70448822414b1844302481a26572be202cc9b` from both. So one manifest serves the online
CI path and the offline maintainer path with no second hash format and no vendored copy.

A directory tree id additionally hashes the whole subtree, so pinning
`crates/bv-errors/src` costs one entry and covers every file under it.

## Decisions

- **D-PRV-1 — The manifest is `specifications/provenance.json`,** schema-validated like
  the fixtures, and it lives in `specifications/` because it is a property of the
  specification, not of any SDK. (CNF-044)

- **D-PRV-2 — Hashes are git object ids, not content digests.** Verifiable identically
  against the GitHub API and a local checkout; no second format. (CNF-045)

- **D-PRV-3 — Sources are pinned at two sensitivities, and only one of them can fail a
  build.** Upstream **documents** (`docs/*.md`) are `authoritative`: they are the surface
  the specification was written from, and a change to one is a genuine signal. Upstream
  **crate trees** (`crates/*/src`) are `corroborating`: they catch behaviour that changed
  without the docs being updated, but they also move on every internal refactor. The
  checker reports both, and by default exits non-zero only on `authoritative` drift;
  `--strict` fails on either.

  *This is the central tradeoff of the design.* Pinning crate trees alone would be precise
  about behaviour and unusably noisy; pinning documents alone would miss undocumented
  server changes, which is precisely the failure this is meant to catch. Splitting the two
  keeps the gate credible — a gate that cries wolf gets disabled, and a disabled gate
  detects nothing.

- **D-PRV-4 — Every entry names the specification documents it feeds.** The report's unit
  of output is "these specification documents need review", not "these upstream files
  changed". This is what makes triage fast, and it is the requirement the tool is judged
  against. (CNF-046)

- **D-PRV-4a — The source→specification mapping, recorded here because it is the
  judgement, not the transcription.** Revision 1 stated *that* entries name what they feed
  and never said *what* feeds *what*, which would have left the highest-judgement artefact
  in this design to be authored by whoever built the tool. Deciding which upstream sources
  a specification document rests on is requirement interpretation, which **FAM-002** puts
  in the Strategic tree and CRS-004 scores R3. So it is fixed here and transcribed there.

  Every path below was verified to exist at `v0.42.0`. `feeds` names specification
  documents, which is the unit the report groups by (D-PRV-4).

  **`authoritative`** — upstream prose, type `blob`:

  | Upstream path | Feeds |
  |---------------|-------|
  | `docs/api.md` | 03, 04, 05, 06, 14, Appendix A, Appendix B |
  | `docs/authentication.md` | 05 |
  | `docs/secret-engines.md` | 07, 08, 09, 10, 11, 12 |
  | `docs/kv-environments.md` | 07 |
  | `docs/cluster-client-discovery.md` | 13 |
  | `docs/configuration.md` | 02 |
  | `docs/crypto.md` | 08 |
  | `docs/ssh-secret-engine.md` | 10 |
  | `docs/ssh-login-brokering.md` | 10 |
  | `docs/rustion-integration.md` | 12 |
  | `docs/ferrogate-machine-auth.md` | 05 |
  | `docs/policy-builder-validator.md` | 06 |
  
  **`corroborating`** — upstream implementation, type `tree`:

  | Upstream path | Feeds |
  |---------------|-------|
  | `crates/bv-errors/src` | 04, Appendix B |
  | `crates/bv-client/src` | 02, 03, 13 |
  | `crates/bv-server/src` | 03, 06, 14, Appendix A |
  | `crates/bv-logical/src` | 03, 06 |
  | `crates/bv-kernel-api/src` | 06, Appendix A |
  | `crates/bv-engine-kv/src` | 07 |
  | `crates/bv-engine-transit/src` | 08 |
  | `crates/bv-engine-pki/src` | 09 |
  | `crates/bv-engine-ssh/src`, `crates/bv-engine-ssh-broker/src` | 10 |
  | `crates/bv-engine-totp/src` | 11 |
  | `crates/bv-engine-files/src`, `crates/bv-engine-ldap/src`, `crates/bv-engine-notifications/src`, `crates/bv-engine-resource/src`, `crates/bv-engine-cert-lifecycle/src`, `crates/bv-engine-rustion/src` | 12 |
  | `crates/bv-auth-userpass/src`, `crates/bv-auth-approle/src`, `crates/bv-auth-cert/src`, `crates/bv-auth-ferrogate/src` | 05 |
  | `docs/security-structure.md` | 02, 03 |
  | `docs/cli-reference.md` | 02 |

  This deliberately supersedes the prose list at `specifications/README.md:13-15`, which
  named only `docs/api.md`, `docs/authentication.md` and the `bv-client`/`bv-server`/
  `bv-errors` crates. That list omitted `docs/cluster-client-discovery.md` — the upstream
  document named for exactly the subject of specification 13, which M5 has just been
  implemented against — along with `secret-engines.md`, `kv-environments.md`, `crypto.md`,
  `configuration.md` and both SSH documents. An unpinned `cluster-client-discovery.md` is
  the sharpest illustration of why prose provenance does not work.

  **Two entries sit on the `corroborating` tier despite being documents**, which is the
  point of making sensitivity an explicit field rather than deriving it from file type.
  `docs/security-structure.md` is overwhelmingly a *desktop-GUI* threat model (§2 keystore,
  §3 YubiKey failsafe, §4 GUI token cache); only its §1 TLS/PQC posture has an SDK-visible
  target, and that target is 02/03. Revision 2 had it on the blocking tier feeding `01`,
  which was simply wrong: `01`'s security baseline is CNF-030…CNF-035, all client-side SDK
  policy with no upstream derivation at all. `docs/cli-reference.md` is pinned for its
  `VAULT_*` environment-variable table, which genuinely informs `02`, but it is a CLI
  reference that moves for CLI reasons — blocking a release on it would be noise.

  **Deliberately excluded, so a reader can tell considered from overlooked (RF-4).**
  `docs/gui.md` — feeds nothing; it changed between `v0.42.0` and `v0.44.4`, so pinning it
  would manufacture exactly the false signal D-PRV-3 exists to suppress (the schema's
  `feeds: minItems 1` enforces this). `docs/hsm.md` — its one SDK-visible endpoint
  (`sys/hsm/status`, spec 06) is also documented in `docs/api.md`, which is pinned.
  `docs/policies/README.md` and `docs/policies/*.hcl` — operator policy samples, not an API
  surface; noted explicitly because both changed between the two refs and their absence
  would otherwise read as an oversight. `docs/administration.md`, `docs/design.md`,
  `docs/install.md`, `docs/quick-start.md`, `docs/req.md`, `docs/NOTES.md`,
  `docs/publishing-crates.md` and `docs/backend/**` — operator and contributor narrative
  with no SDK-observable surface.

  **Specification documents deliberately unfed (R-1):** `00`, `15`, `16`, `17`, Appendix C
  and Appendix D. Each is SDK-internal — scope and glossary, testing, documentation and
  fixture policy — and derives from no upstream source, so nothing upstream can invalidate
  them.

  **A known coarseness (R-2):** `docs/secret-engines.md` fans out to six specification
  documents, but only its §6 engine table is specification-bearing; the rest is a
  contributor guide on adding an engine. A §7 edit will therefore flag six documents on the
  blocking tier. It did not change between the two refs, so it costs nothing today; if the
  tool ever supports section anchors, narrow it.

  The mapping is a standing judgement, not a derivation. A new upstream engine or
  auth method means a new entry, and that is a Strategic-tree edit to this record.

- **D-PRV-5 — The baseline is pinned at `v0.42.0`, not at today's upstream.** The
  derivation was never recorded, so the only defensible baseline is the one the
  specification itself declares: the 0.42 floor in `00-overview.md`. Pinning at v0.44.2
  (the newest release predating the specification's own date) would *assume* the
  intervening changes were reviewed, and a provenance record whose first act is an
  unverified assumption is worth less than no record.

  **Measured, not assumed (F-6).** Revision 1 asserted this would produce "substantial
  drift". That was an unverified assumption and it was wrong; the architecture review
  measured it. Between `v0.42.0` and `v0.44.4`, **7 upstream `docs/` files changed**
  (`api.md`, `authentication.md`, `ferrogate-machine-auth.md`, `gui.md`,
  `policies/README.md`, `policies/pki-exporter.hcl`, `rustion-integration.md`), of which
  **exactly four are pinned** by the D-PRV-4a mapping — `api.md`, `authentication.md`,
  `ferrogate-machine-auth.md` and `rustion-integration.md`; the other three are
  deliberately excluded there. **15 crate trees** moved, all on the `corroborating` tier.

  Those four upstream documents resolve to **eight specification documents** to review —
  `03`, `04`, `05`, `06`, `12`, `14`, Appendix A and Appendix B. Note the unit: D-PRV-4
  makes the *specification document* the reporting unit, so "four" counts the wrong thing
  and revision 2 conflated the two. The cost of the conservative baseline is an
  eight-document review, not a backlog. This figure
  is recorded so a later agent sizes the reconciliation from the measurement rather than
  re-deriving it (TOK-008). Once walked, `--update --ref <ref>` re-baselines in one
  command.

- **D-PRV-6 — CI runs the check in non-blocking mode against `main`.** `repo-gates.yml`
  reports drift as a warning annotation and uploads the report; it does not fail the
  build. Force 3 above: an upstream release must not redden an unrelated pull request in
  this repository. The blocking use of the tool is the release checklist, where a stale
  specification genuinely should stop a release.

- **D-PRV-7 — The GitHub call is unauthenticated, with `GITHUB_TOKEN` honoured when
  present.** `ffquintella/BastionVault` is public (verified: `"private": false`), one
  recursive tree call covers all 1476 entries untruncated, and 60 requests/hour
  unauthenticated is ample for a tool that makes one call per run. In CI the ambient
  token lifts the limit. Network failure is reported as *unknown*, never as *no drift* —
  a checker that reports clean when it could not look is worse than no checker.

- **D-PRV-8 — Each SDK exposes the pinned upstream release next to the specification
  version** it already exposes, as a sibling of `specification_version()`
  (`SdkInfo.SpecificationVersion` in .NET, `specification_version()` in Rust and Python).
  A deployed application can then report what it was built against without the repository
  in hand. (CNF-047)

- **D-PRV-9 — All three languages get the accessor in this change,** which is a recorded
  exception to the Stage 1 freeze on `rust/` and `python/` (`ROADMAP.md` D-6). The freeze
  exists to stop parity work competing with Stage 1's .NET milestones; this is
  non-behavioural metadata, it costs one accessor and one baseline line per language, and
  splitting it would leave CLA-003 parity knowingly broken for the whole of Stage 1 on a
  surface whose entire purpose is to be read by operators of all three SDKs.

- **D-PRV-10 — This change bumps the specification to `1.1.0`, on acceptance and not
  before.** CNF-044…CNF-047 are four new MUST requirements, one of which (CNF-047) binds
  the SDKs; additively, so a minor bump under the rule in `specifications/README.md`.
  Leaving the version at `1.0.0` would let two materially different specification contents
  both claim `1.0.0` — which is exactly the failure mode this whole change exists to
  remove, and it would be incoherent to introduce it here of all places.

  The bump is sequenced **after** the implementation handback rather than before it, so it
  lands in one place across `00-overview.md`, `specifications/test-matrix.json`,
  `provenance.json`, the three SDK `specification_version` accessors and their tests. That
  ordering is deliberate: a partially-applied version bump is worse than a late one.

## Consequences

**Gained.** A server release is triaged with one command that names the affected
specification documents. The `≥ 0.42` claim becomes a pinned, checkable fact. The
existing 0.42→0.44.4 gap becomes visible and enumerable for the first time.

**Given up.** A manifest to maintain: re-baselining is a deliberate act, and an entry
whose upstream path is renamed reports as *missing* until someone repoints it (the tool
must distinguish *missing* from *changed*, because they mean different things). Crate-tree
entries will produce known-noisy signals, which D-PRV-3 contains rather than eliminates.
The mapping from upstream source to specification document is a human judgement recorded
once; it is not derived, and it will need revisiting when a new engine appears upstream.

**A limitation of D-PRV-3, stated plainly.** A `corroborating`-only change is by
construction invisible to exit status. Anything that consumes this tool by branching on
the exit code alone will not see it. That is why the release-checklist item added under
F-2 requires the release evidence to *display* the corroborating section rather than
merely branch on the tool's status.

**Not addressed.** This tracks the specification against the server. It does not track the
three SDKs against the specification — that is the traceability tool's job (TST-041), and
the two are deliberately separate.

## Architecture-review dispositions (revision 1 → revision 2)

Revision 1 was reviewed at the `agents.md` §4.2 row-4 gate by a Strategic-tree agent other
than the author (**REV-002**). Verdict: *approve with required fixes*. All seven are
applied.

| Finding | Disposition |
|---------|-------------|
| **F-1** CNF-046's unqualified "exit non-zero when drift is found" contradicted D-PRV-3, and named only one of the outcomes | **Applied.** CNF-046 now specifies four outcomes — `unchanged`, `changed`, `missing`, `unknown` — with the exit status of each, and states that `unknown` must never read as an absence of drift |
| **F-2** Nothing obliged anyone to read the report, making the gate ceremonial | **Applied.** A sixth release-checklist item forbids releasing with unreconciled `authoritative` drift, and requires the corroborating section to be shown. This is the obligation D-PRV-6 assumed existed and did not |
| **F-3** The source→specification mapping existed nowhere reviewable, leaving an R3 judgement to be taken by an Engineering-tree agent, contrary to **FAM-002** | **Applied as D-PRV-4a**, and the finding was correct: the mapping had been written into a delegation brief rather than into this record. A brief is transient and unreviewable; the record is neither. **This is new content and returns for a second review round** |
| **F-4** CNF-047 cited CNF-041 as if it mandated a public accessor; no requirement did | **Applied.** CNF-047 now mints the obligation for both values in its own text and states how it differs from CNF-041 |
| **F-5** D-PRV-10 invoked a minor-bump rule that `specifications/README.md` did not contain | **Applied.** That paragraph now carries the full major/minor/patch table |
| **F-6** The "substantial drift" assumption was unverified and, when measured, wrong | **Applied**, and worth recording as a lesson rather than a correction: the review measured what the record had asserted. The figure is now in D-PRV-5 so the next agent reads it instead of re-deriving it (**TOK-008**) |
| **F-7** No `CHANGELOG.md` entry, which **CLA-009** makes a blocking review finding | **Applied.** Entry added under **Added**, citing CNF-044…CNF-047 and this record. No version heading created (**REC-003**) |

The review also verified, independently rather than on the author's assertion, that a git
object id from the GitHub tree API equals the one from a local checkout
(`docs/api.md` at `v0.44.4` = `9fe70448822414b1844302481a26572be202cc9b` by both routes),
that `crates/bv-errors/src` agrees as a tree, and that `v0.42.0` is a repository-level tag
rather than one of the 357 crate-scoped tags. The design's deciding property holds.

## Round-2 dispositions (revision 2 → revision 3)

Round 2 was scoped to D-PRV-4a. Verdict: *approve with required fixes*. The structural
claims held — all 34 paths resolved at `v0.42.0`, none duplicated, none in both tiers — but
the mapping failed a completeness test against the very change it was measured on.

| Finding | Disposition |
|---------|-------------|
| **RF-1** `14-batch-and-request-efficiency.md` was fed by nothing, on either tier | **Applied**, and this finding alone repays the gate. Verified independently: the `docs/api.md` diff `v0.42.0..v0.44.4` adds exactly `### Bulk metadata listings (<list>-info)` and `### Cache coherence (sys/cache/version)` — two of the three mechanisms spec 14 exists to specify. The first report would have named 03/06/A/B, the maintainer would have found nothing about `*-info` there, and closed the item. `docs/api.md` and `crates/bv-server/src` now both feed `14` |
| **RF-2** `docs/api.md` did not feed `04` or `05` | **Applied.** The consequence for `04` was the sharper half: its only feed was `crates/bv-errors/src`, which is `corroborating`, so **no error-model drift could ever block a release** — and an error-model change is a stable-code change |
| **RF-3** `docs/security-structure.md → 01, 05` was wrong, on the blocking tier | **Applied.** Verified: the document is §2 desktop keystore, §3 YubiKey failsafe, §4 GUI token cache. `01`'s security baseline (CNF-030…CNF-035) is client-side SDK policy derived from nothing upstream. Entry demoted to `corroborating` and retargeted to `02, 03`, the only defensible home for its §1 TLS/PQC posture |
| **RF-4** Only `docs/gui.md` was recorded as a deliberate exclusion | **Applied.** Every remaining upstream document is now either pinned or excluded with a reason. `docs/cli-reference.md` is pinned as `corroborating` for its `VAULT_*` table; `docs/hsm.md`, `docs/policies/**` and the narrative set are excluded on the record |
| **R-1** No statement of which *specification* documents are deliberately unfed | **Applied** — `00`, `15`, `16`, `17`, Appendix C, Appendix D |
| **R-2** `docs/secret-engines.md` fans out to six documents though only its §6 is specification-bearing | **Recorded as a known coarseness**, not fixed. It costs nothing at this baseline and the fix needs section anchors the tool does not have |
| **Figures** D-PRV-5's "16 crate trees" and "four-document read" | **Both corrected.** Independently recounted: **15** crate `src` trees moved. And "four" counted upstream documents while D-PRV-4 makes the *specification document* the reporting unit — the real cost is an **eight-document** review |

**RF-3 established something worth keeping.** Sensitivity is an explicit field, not a
function of file type: two upstream *documents* now sit on the `corroborating` tier because
what makes a source blocking is the quality of its signal, not whether it is prose.

The corrected mapping was re-run before acceptance. The first `--check --ref v0.44.4` now
names **eight** specification documents — `03`, `04`, `05`, `06`, `12`, `14`, Appendix A,
Appendix B — against the six it named before the fixes. Exit 1 on authoritative drift,
exit 0 at the baseline, `--verify-local` agreeing with the online pin, 18 unit tests green.

**The lesson this record should carry forward.** Both rounds found the same class of
defect: a claim asserted rather than measured. Round 1 found "substantial drift" (it was
four documents); round 2 found "16 crate trees" (it was 15) and a mapping whose gaps were
invisible until someone diffed the upstream document against it. Provenance tooling is
exactly the wrong place to assert an unverified number.
