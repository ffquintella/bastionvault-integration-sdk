# Roadmap — implementing the specifications

**Owner:** Strategic Orchestrator (Claude) · **Authority:** subordinate to [`agents.md`](agents.md) and [`claude.md`](claude.md)
**Source of truth for behaviour:** [`specifications/`](specifications/README.md) · **Version:** 1.11.0 · 2026-09-15

## 1. Objective

One verifiable outcome: **the .NET, Rust and Python SDKs each satisfy every MUST
requirement of the 389 requirements in [Appendix D](specifications/appendix-d-requirement-index.md)
at conformance level Complete (CNF-003), with the quality gates CNF-020…CNF-027 green.**

**The work is staged by language (D-1, amended 2026-09-14 at the project owner's
direction).** **Stage 1** takes `dotnet/` alone through every remaining milestone, M2b to
M12, until .NET is `Complete` with its integration suite green. **Stage 2** then brings
`rust/` and `python/` to the same level from the decision records and fixtures Stage 1
settled, and the shared `1.0.0` tag closes it. No Stage 2 work starts before Stage 1 exits.

Definition of done, per language:

1. Every applicable requirement ID appears in the traceability report with at least one test (CNF-014, TST-041).
2. Every applicable fixture under `specifications/fixtures/**` is exercised (CNF-015).
3. Line **and** branch coverage ≥ 95 % on library code, no exclusion pragmas (CNF-010, TST-030).
4. The three implementations are behaviourally identical or carry a recorded parity exception (CLA-003).
5. README declares `Complete`, the spec version, and the tested server versions (CNF-041).

Items 1, 2, 3 and 5 are checked for .NET at Stage 1 exit. Item 4 is a Stage 2 exit
criterion; during Stage 1 it is satisfied by the recorded exception in D-1 and D-6.

## 2. Current state (2026-09-15, after M5 .NET — cluster discovery and bounded failover exited in .NET; still no conformance level declared)

| Area | State |
|------|-------|
| `specifications/` | Complete: 18 documents, 4 appendices, **224 fixtures on disk**, 389 requirement IDs. Appendix B carries **121** codes, unchanged since M2a minted `BV-AUTH-017` and `BV-CONFIG-011` (D-M2-16). M2c adds `clock.delay`/`clock.expectWaits` to the fixture schema (D-M2-27). M3's brief (DR-0007) mints `SYS-006` (`ClusterStatus`) and authors `sys.info.tiers`, `sys.cluster-status.ok/forbidden`. M4's brief (DR-0009 D-M4-8) authors the five missing Appendix C `kv.*` fixtures, 213→218; the 21st, `kv.read-many-fallback-on-unsupported`, is M8's. **M5 authors the six missing `resilience.*` fixtures, 218→224**, completing Appendix C line 133's list, and repairs one landed fixture whose body section 07 forbids (D-M5-26, R3-authorised — R-19) |
| `dotnet/` | Harness + M1a config + M1b transport + M1c error model + M2a authentication + M2b login/Userpass/AppID + M2c automatic renewal + M3 System API Core subset + M4 KV engine + **M5 cluster discovery and resilience**. `Client.Auth` now has `Token`/`Userpass`/`AppId`, the public `TokenSource.Login` factory, `Auth.AuthenticateAsync`, the CFG-020/ERR-022 client-side preflight, and `AutoRenewPolicy`/`RenewalEvent`/`RenewalStoppedReason` (AUT-090…095). `BastionVaultClient` is now `IDisposable`; `IClientLogger` gains `Info`. M3 adds `Client.Sys`: `Health`, `SealStatus`, `ServerInfo`, `ClusterStatus`, `CapabilitiesSelf`, `Capabilities.Can`/`Sys.CanAsync` (SYS-001,002,005,006,008,050…053; [DR-0007](decisions/0007-m3-system-api-core.md)). M4 adds `Client.Kv` with the version-explicit `Kv.V1`/`Kv.V2` sub-clients, per-environment overrides, the `KV2-030` path helpers and the `WriteIfAbsent`/`UpdateWithRetry`/`ReadField` conveniences ([DR-0009](decisions/0009-m4-kv-engine.md)), plus `dotnet/README.md`, the repo's first per-language README. M5 adds cluster discovery — `ISrvResolver`, `DiscoveryConfig`/`HealthConfig`, `Candidate`/`ProbeResult`/`NodeSelection`/`DiscoveryReport`, `Client.InputLabel`/`SelectedNode`/`ConnectAsync`/`DiscoverAsync`/`ReconnectAsync` — plus sticky sessions with the single bounded failover replay, `DSC-043`'s serialising lock and `DSC-045`'s internal node-local seam ([DR-0010](decisions/0010-m5-cluster-discovery-and-resilience.md)). **No SRV resolver ships** (R-16). **850 tests, 99.13 % line / 97.02 % branch** |
| `rust/` | Harness + M1a config + M1b transport + **M1c error model**. `ErrorCatalog`, generated codes, recognition, enrichment. **219 tests, 96.55 % line / 96.32 % region** (D-M0-14) — its shared-fixture-count assertions were updated 208→210 for M2c's two new fixtures, no behavioural change. Frozen at this state for the duration of Stage 1 (D-6) |
| `python/` | Harness + M1a config + M1b transport + **M1c error model**. `ErrorCatalog`, generated codes, recognition, enrichment. **494 tests, 98.92 % line and branch**, `mypy --strict` and `ruff` clean — its shared-fixture-count assertions were updated 208→210 for the same reason as Rust's. Frozen at this state for the duration of Stage 1 (D-6) |
| `tools/traceability` (TST-041) | **Built and ratcheting.** **216 of 421 covered, 205 baselined** — M5 moved 29 IDs from baselined to covered (`DSC-001`, `DSC-002`, `DSC-010`…`014`, `DSC-020`…`022`, `DSC-030`…`036`, `DSC-040`…`046`, `RES-010`, `RES-011`, `RES-020`, `RES-021`, and `CFG-043`, which had been baselined with no owner — D-M5-16a), minting none. `RES-001`…`RES-004` were already covered from M1b, so M5 landed **29 of the 33 its row booked**, not 33; `RES-030` stays baselined with **M7** as owner, because its `*ClusterWide` variants wrap `Sys.Seal`/`Sys.Unseal` (`SYS-012`/`SYS-013`, M7) — D-M5-3. `KV-001` and `KV-010` stay baselined by design (DR-0009 D-M4-2) |
| `tools/error-catalogue` | **Appendix B is executable.** Parses §1 and §2 into `catalogue.json` and emits **121 codes**, 127 recognition rules and the code constants for all three languages, plus 124 fixtures. Unchanged by M2c. Regeneration is a CI gate, proven by seeded violation ([DR-0005](decisions/0005-m1c-error-model.md) D-M1c-1) and re-proven at the R-10 sweep ([DR-0001](decisions/0001-m0-harness-gate-proof.md) addendum, Row 15) |
| Fixture driver operation registry | `Client.Construct` plus the five `Logical.*` operations in all three, **plus all `Auth.*`, `Sys.*`, `Kv.*` and (M5) `Client.Connect`/`Discover`/`Reconnect`/`Classify` operations in .NET only**. **224 fixtures on disk**; all 18 transport fixtures pass ×3, **133 of the 134 error fixtures pass in .NET** (`errors.recognition.missing-token-client-side` went green at M4 as D-M2-10 predicted), **18 of the 19 auth fixtures** (only `auth.cert.disabled-server`, AUT-070, M6, is pending), **19 of the 20 `kv.*` fixtures** (only `kv.read-many-batch`, M8), and **all 10 `resilience.*` fixtures** (M5; six of them authored there). `errors.enrichment.404-kv2-hint` is re-booked from M4 to **M7** and needs re-authoring — it names a `Kv.V2.*` operation on a path with no `data/` segment, which section 07 makes impossible (DR-0009 D-M4-14) |
| CI | `dotnet.yml`, `rust.yml`, `python.yml`, `repo-gates.yml` **plus** the pre-existing `build-artifacts.yml`. Every gate CNF-020…CNF-027 and TST-041 wired |
| Gate proof | **The R-10 sweep is complete** ([DR-0001](decisions/0001-m0-harness-gate-proof.md) addendum, Rows 7–15): 6 of 9 previously-unproven or stale gates re-proven clean by seeded violation and revert; 3 surfaced genuine pre-existing findings, tracked as **R-11/R-12/R-13** below rather than fixed at M2c (none block M2's exit — see each row's disposition). M2a's two new instruments (fixture `clock`, TST-051) and M2c's own fixture-clock virtual-time mechanism (D-M2-27) are proven the same way and kept as standing tests |

**M5 is complete in .NET: cluster discovery, health probing, node ranking, sticky sessions
and the single bounded failover replay are in.** Its headline outcome is a scoping decision
rather than a feature: `DSC-041` reclassifies a transport failure to `BV-DISCOVERY-003`
**only in discovery mode**, and a matching `5xx` never has its Appendix B code replaced at
all, because the naive reading would have changed the observed error code of every transport
failure in the SDK and required amending twelve landed fixtures ([DR-0010](decisions/0010-m5-cluster-discovery-and-resilience.md)
D-M5-5). M5 also corrected its own booking — 29 IDs, not the 33 its row claimed — and
repaired one landed fixture that encoded a KV v2 body section 07 forbids, which is R3 and
carries a human confirmation into M12's release checklist (D-M5-26). Three new risks:
**R-16** (no SRV resolver ships, so discovery is inert until an application supplies one),
**R-17** (`DSC-033` cannot prefer healthy nodes without a `specifications/` change) and
**R-19** (fixtures can encode bodies their own section forbids, and pass — two found in one
milestone). **R-18** carries the one tension M5 declined to resolve: `AUT-003`'s relogin
replay can exceed `RES-001`'s attempt cap independently of failover, which predates M5 and
is owned by M6.

**M4 exited without declaring a conformance level, and M5 does not change that.** That is
the milestone's most important outcome, and it is not a shortfall in the KV work: `Core`
requires every MUST of sections **16** and **17**, the documentation and usage-guide
requirements, which are booked to **M11** — after the milestones meant to declare
`Standard` (M8) and `Complete` (M10). `CNF-002` forbids claiming a level whose sections
carry unimplemented MUSTs, so `dotnet/README.md` claims none and lists the gaps by
requirement ID instead. **D-4's declaration schedule is unsatisfiable as written at all
three levels** and needs resequencing — a project-owner decision, recorded as §10
question 4, R-14 below, and [DR-0009](decisions/0009-m4-kv-engine.md) D-M4-3. Two of
section 07's 27 IDs are deferred with named owners rather than guessed at: `KV-001` waits
on `SYS-026` (M7) and `KV-010` on `BAT-007` (M8), so M4 landed **25**.

**M0 and M1 are complete in all three languages; M2, M3, M4 and M5 are complete in .NET.**
The login response contract, Userpass, AppID, the client-side missing-token preflight, the
section-05 security requirements, the System API Core subset, the KV engine and cluster
discovery with bounded failover are in. The baseline is down to **205** entries — the project's remaining-work counter; it must reach zero before
the M12 release (D-M0-1). M2b's handback also corrected two of D-M2-6's public-API pins and
one of D-M2-25's own rulings — see
[`decisions/0006-m2-authentication.md`](decisions/0006-m2-authentication.md) D-M2-26 — and
found and fixed a path-injection defect in Userpass login that no requirement ID named
directly (AUT-030/TRN-020).

**M2a was the first slice to exit in one language, and that is now the standing plan
rather than an exception; M2b is the second data point.** `v0.5.0` was tagged on M2a at the
project owner's direction, and `v0.6.0` on M2b under the same standing direction (§10
question 2). At the time of M2a, D-1 still called for horizontal slices and the .NET-only
exit was recorded as a deviation in `CHANGELOG.md` and
[DR-0006](decisions/0006-m2-authentication.md) D-M2-15. **D-1 is now superseded (below):
the project owner has directed .NET to run to completion first, as Stage 1, with Rust and
Python deferred to Stage 2.** Until Stage 2 starts, **`rust/` and `python/` stay at the
catalogue-only state M2a left them in — no further parity passes land until Stage 1
exits.**

The public API shape is now fixed for everything downstream: options-in / resolved-config-out,
an injected `EnvironmentSource`, a redacting `SecretString`
([`decisions/0003-m1a-configuration.md`](decisions/0003-m1a-configuration.md)), and — settled
at M1b after M1a's three incompatible placeholders were found — one asynchronous transport
seam, the logical layer above it, and the single status→code mapping function M1c extends
([`decisions/0004-m1b-transport.md`](decisions/0004-m1b-transport.md)).

**Known follow-ups carried out of M2a** (DR-0006 addendum)

- **Rust and Python have none of M2a.** **Deferred to Stage 2 (M13, D-6)** rather than being
  the next unit of work. When M13 reaches this block, DR-0006 D-M2-18 carries three things
  into its brief. The sharpest: the exception-filter
  ordering in `BV-AUTH-017`'s guard is **load-bearing**, and neither Rust nor Python has
  exception filters — .NET reads `catch (OperationCanceledException)` before
  `catch (Exception) when (exception is not BastionVaultException)`, and a naive
  transcription inverts it, sending a cancellation to `BV-AUTH-017` instead of
  `BV-TRANSPORT-005`.
- **The R-10 gate sweep is now formally M2c's exit condition** and carries no requirement
  ID by design — it is a proof obligation, and minting an ID would put a non-specification
  entry on the baseline (D-M2-1). M2 does not exit until it is done.
- **`AUT-085` is expressed as a hand-written predicate where an Appendix B row scoped by
  `PathContains` would delete it.** The mechanism already exists. It is a specification
  change and therefore R3 — **Architect queue**, not a delegate's (D-M2-18 item 2).
- **Python's suite was not runnable in the M2a environment** (no `httpx`, no `pytest`), so
  its fixture-count assertion was updated mechanically to 208 and **verified only by CI**,
  not locally. The .NET and Rust equivalents were run.
- **M2b must decide whether to distinguish a token source's own coded failure from one it
  leaked.** A `BastionVaultException` from a source is deliberately not wrapped, because
  `AUT-003`'s replay keys on `BV-AUTHZ-001` and wrapping would break it. The residue is
  that a `Callback` leaking an unrelated coded error surfaces with the source's internal
  `Path` and `Method` (D-M2-18 item 3).
- **`IClientLogger` has only `Warn`.** `CNF-031`'s debug-token carve-out is what implies a
  level, and `CNF-031`/`CNF-032` are M2b's IDs, so M2b decides whether the level exists
  (D-M2-14).

**Known follow-ups carried out of M1a**

- `CNF-002` gap lists do not exist and cannot until a README makes a conformance claim. The
  traceability baseline serves as the gap list until **M4**, where the three READMEs are
  authored and the baseline is rendered into CNF-002 prose (D-M1a-22).
- Python gained a runtime dependency on `cryptography` for PEM parsing; .NET and Rust parse
  PEM without adding one. First divergence in the dependency surface — watch it at CNF-024.
  **Updated at M1b (DR-0004 D-M1b-3):** all three now carry a TLS/HTTP stack — Python
  `httpx`, Rust `hyper`/`hyper-util`/`rustls`/`tokio`/`rustls-native-certs`/`tower-service`,
  .NET in-box. Rust's `cargo audit` surface went 131 → 143 crates, clean. The divergence is
  now in *size*, not in existence.

**Known follow-ups carried out of M1c** (DR-0005 exit record)

- **Three of M1c's defects were the same shape, and it is now a rule.** The `400` code, the
  `409`/`503` heuristics and .NET's `Error.Path` were each a branch written to a
  *milestone* rather than to a *requirement*, with a comment promising a later pass.
  Nothing watched that promise: no fixture reached the branch (that is why it was
  deferred), coverage could not tell "executed" from "correct", and the baseline recorded
  the requirement as uncovered, which made the wrongness look expected. **From M2, a
  deferred branch returns the value the specification names, never a plausible guess**
  (DR-0005 D-M1c-25). The `503` case was not cosmetic: `BV-SERVER-001` is not retryable
  and `BV-SERVER-002` is, so the guess was suppressing a permitted retry.
- **Two gates were red on `main` and nobody knew.** `CNF-025` had been failing since M1a
  and `CNF-010` since M1b, in the Python job's own invocation. Both are fixed inside M1c
  (D-M1c-15, D-M1c-16). With D-M1b-19 that is three gates this project has certified by a
  record rather than by an execution — **the M1b instruction to re-prove every gate by
  seeded violation is now overdue, not optional**, and M2 does not exit before it is done.
- ~~**Python's `CNF-027` gate is materially weaker than .NET's and Rust's**~~ **Closed at
  0.4.0.** `python/tests/_api_surface_extractor.py` replaced the 34 name-only lines with a
  360-line member-level baseline covering classes, methods with full signatures,
  properties, dataclass fields, enum members, constant *values* and public instance
  attributes assigned in `__init__`. The two constant renames this milestone missed, and an
  attribute rename, now each fail the gate — proven by seeded violation (D-M1c-22).
- **The traceability tool's own tests were Windows-only and ran in no CI job.** Fixed
  during M1c, outside the milestone's scope, by the carried-forward M0 harness item.
- Deferred with named owners: `ERR-022` (M2), `ERR-032` and `ERR-060`/`ERR-061` (M11), the
  `namespace_operable` enrichment row (M3), the KV v2 mount-hint row (M4). Four
  best-effort branches in .NET's request path are listed in the DR-0005 addendum and go to
  the M2 brief; `map_status_to_code`'s now-unread `server_message` parameter goes with them.

**Known follow-ups carried out of M1b** (DR-0004 exit record)

- **The .NET CNF-027 gate had never been enforced** (D-M1b-19): `AnalysisLevel=latest-all`
  silently disabled the public-API analyzer, so M0 and M1a certified a baseline nothing
  checked. Replaced with an executing surface diff and proven by a seeded violation. **Every
  gate in this repository should be re-proven the same way** — DR-0001 required it and only
  the seeded-violation gates were ever actually proven.
- Two of M1b's six review defects (Rust's empty root store, Rust's missing connection
  pooling) sat **below the fixture seam**, where fixtures, coverage and traceability are all
  blind. Milestones that build real I/O need a review pass that reads the transport, not
  only one that runs the suite.
- `BV-CONFIG-009` (`ListVerbUnsupported`) and `BV-CONFIG-010` (`EncryptedTokenFile`) are in
  Appendix B but have no `CFG-*` requirement behind them. They are **M1b** and **M2**
  respectively: the first needs the `LIST` verb, the second needs the token helper's
  encrypted-format path.

**Known follow-ups carried out of M0**

- **66 of Appendix C's ~140 mandatory fixtures are unwritten** (D-M0-7). Claude-owned; each is
  authored in the milestone that implements its area. FIX-010 requires capture from a real
  server, so this **pulls risk R-6 forward to M1** — see §8.
- Two gate mechanisms were rejected on evidence and must not be reintroduced: `coverlet.collector`
  for .NET, which reports coverage but does not enforce the threshold (D-M0-15), and Rust branch
  coverage, which needs a nightly toolchain (D-M0-14).
- The .NET mock server's TLS fix is verified on Windows only; the first `ubuntu-latest` run is
  its first Linux verification.

## 3. Sequencing decisions

These are decided. No delegate reopens them (TOK-008).

### D-1 — Superseded 2026-09-14: .NET runs to completion first (Stage 1), then Rust and Python (Stage 2)

**Original decision (2026-09-13, retained below for the record):** each milestone lands in
.NET, Rust and Python before it exits, so the parity check is the exit criterion rather than
a later phase. Rejected alternative at the time was *.NET to Complete first, then port*,
on the grounds that a first port re-litigates every contract decision .NET made implicitly
and tunes the fixture suite to one language's idioms.

**Superseding decision, directed by the project owner (2026-09-14):** the rejected
alternative is now the plan. **Stage 1** takes `dotnet/` through every remaining milestone
— M2b, M2c, M3 … M12 — to `Complete` conformance and a green integration suite, entirely on
its own. **Stage 2** starts only once Stage 1 exits, and brings `rust/` and `python/` to
parity from the decision records, fixtures and .NET behaviour Stage 1 fixed, milestone by
milestone, closing with the shared `1.0.0` tag.

**Forces re-examined:** CLA-003 (no behaviour change in one language only) is not violated
by staging, because CLA-003 governs a *shipped* behavioural claim, and Stage 1's releases
are recorded as `.NET`-only per the D-M2-15 exception pattern, exactly as `0.5.0` already
was. The parity risk the original D-1 was written against (R-4, R-9, R-9a) does not
disappear — it is deferred to Stage 2 and must be paid there in full, milestone by
milestone, using the same fixtures and public-surface diff this file already requires.

**Rejected (2026-09-14 revisit):** *keep horizontal slicing and simply move faster.*
Rejected because the project owner's direction is explicit and is a scope decision the
Strategic Orchestrator accepts rather than re-argues (`claude.md` §1); Claude's role here is
to record the consequence honestly, not to relitigate the choice a second time.

**Consequence.** Stage 1 wall-clock is bounded by .NET alone, which is faster per milestone.
The cost moves to Stage 2: every contract .NET settles implicitly — not only the ones an
explicit decision record captured — is a candidate defect for Rust and Python to inherit
silently, and Stage 2 has no .NET-review round-trip to catch it before it lands in two
languages at once, because there is no longer a same-milestone .NET pathfinder pass ahead
of it. D-6 below is the mitigation: Stage 2 re-opens the review rigor D-2 used to buy one
milestone at a time, applied instead across the whole backlog Stage 1 leaves behind.

### D-2 — Within Stage 1, .NET is simply the only lane; within Stage 2, .NET is the settled reference

**Original decision (2026-09-13):** within a milestone, the shared design is settled first,
then .NET implements as pathfinder, then Rust and Python implement in parallel from the
same brief and fixtures — not from the .NET source — so an under-specified brief produces
one divergent reading to fix, not two.

**Amended for staging (2026-09-14).** During **Stage 1** this decision is dormant: there is
only one lane, so "pathfinder" and "parity pass" collapse into the same .NET-only step, and
its design-review round-trip is the sole gate (§7). During **Stage 2**, D-2's mechanism is
restored but its source changes: Rust and Python implement in parallel from the same
decision records and fixtures **and from .NET's already-reviewed behaviour**, which is now
the working reference in the way "the brief" was during horizontal slicing. Stage 2 does not
re-run .NET as a pathfinder a second time; it treats every Stage-1 decision record as
already settled (TOK-008) and every Stage-1 fixture pass as the target, not a hint.

**Rejected:** *all three in parallel from the brief, disregarding .NET's behaviour, during
Stage 2.* Rejected for the same reason as before — an under-specified brief still produces
divergent readings — and because it would throw away the twelve milestones of .NET-only
review Stage 1 paid for.

**Consequence:** Stage 1 has no serialisation cost (one lane, moving alone). Stage 2 inherits
the parity risk in full (see D-1) and must budget a genuine cross-language review pass per
milestone, not a lighter one, because it is verifying against eleven milestones of
accumulated .NET-only decisions rather than one.

### D-6 — Stage 2 entry gate

**Decision:** Stage 2 does not start milestone-by-milestone alongside Stage 1, and does not
start on any single .NET milestone's exit. It starts once **all** of M2a through M12 have
exited in .NET — i.e. once §1's Stage 1 definition of done holds for `dotnet/` in full,
including the live-server integration suite (M12) and the `Complete` conformance
declaration.

**Rejected:** *Start Stage 2 on Rust/Python as soon as each .NET milestone exits,
milestone-by-milestone.* This is closer to the original horizontal-slice plan under a
different name and reintroduces the parallel-milestone cost the project owner's direction
was meant to remove. It also risks a Rust/Python pass built on a .NET contract that a later
.NET milestone still revises (as M1a's transport seam was revised at M1b) — paying the
rework cost D-1's original rejection warned about, just deferred rather than avoided.

**Consequence:** `rust/` and `python/` stay frozen at the M2a/`0.5.0` state — catalogue
regenerated, no authentication surface — for the full duration of Stage 1. This is a
known, accepted gap, not a defect to fix mid-stage.

### D-3 — Harness before features

The traceability tool, fixture loader, mock server and CI gates (M0) precede all feature
work. Coverage and traceability are pass/fail gates on every later milestone; building them
late means every earlier milestone is re-opened to satisfy them.

**Rejected:** *ship KV first to prove value, add gates after.* Rejected because CNF-014 and
TST-041 are themselves MUST requirements — deferring them just hides the debt.

### D-4 — Conformance level is declared incrementally

`Core` is declared at M4, `Standard` at M8, `Complete` at M10. Until a level's sections are
complete, the README lists known gaps by requirement ID (CNF-002). This keeps the repo
honest at every commit rather than silent until the end.

**Amended at M1a (D-M1a-22):** no per-language README exists yet and none makes a conformance
claim, so there is nothing for CNF-002 to qualify. `tools/traceability/baseline.json` is the
gap list until M4, where the READMEs are authored and the baseline is rendered into prose.
**Discharged at M4:** `dotnet/README.md` now exists and carries the gap list.

**Broken, and found so at M4 (DR-0009 D-M4-3):** the schedule above cannot be executed as
written. `Core` requires every MUST of sections **16** and **17** as well as 07; those are
the `DOC` requirements, booked to **M11**, which falls *after* M8's `Standard` and M10's
`Complete`. So none of the three declarations is legal at the milestone that claims it, and
M4 exits declaring nothing. The fix is a **resequencing decision for the project owner**
(§10 question 4): either M11 moves ahead of M8, or all three declarations move to the end
and the `0.x` previews stay level-less until then. Claude has not chosen for them, because
it changes milestone order rather than milestone content; either answer leaves the work of
M4–M10 unchanged.

### D-5 — Fixtures are loaded from the repository, never copied

Per TST-010. Each language gets a loader that reads `specifications/fixtures/**` by relative
path. A fixture edit must break all three suites simultaneously, or the parity instrument is
worthless.

## 4. Milestone plan

`Reqs` = requirement IDs whose implementation lands in that milestone. Tiers per
[`agents.md`](agents.md) §5.3 (risk) and §7.1 (size). `Stage` marks which side of D-1's
2026-09-14 supersession a milestone falls on: **1** = .NET only, exits without Rust/Python;
**2** = Rust and Python parity pass over a Stage-1 milestone, using its decision records
and fixtures. M0 and M1 predate the staging decision and already exited in all three
languages, so they carry no stage marker.

| # | Milestone | Reqs | Count | Size | Risk | Stage | Gate at exit |
|---|-----------|------|-------|------|------|-------|--------------|
| **M0** ✅ | Harness, gates and traceability | `CNF`, `FIX`, `TST` | 54 | Large | R2 | — | CI fails on a seeded coverage/traceability regression |
| **M1** ✅ | Client skeleton: config, transport, error model | `OVR`, `CFG`, `TRN`, `ERR` | 105 | Enterprise | R3 | — | **Met** — all transport and error fixtures green in all three languages |
| ├ **M1a** ✅ | Configuration, error skeleton, transport seam | `CFG`, `OVR` | 27 | Large | R3 | — | **Met** — 27 IDs off the baseline, `Client.Construct` fixture green ×3 |
| ├ **M1b** ✅ | Transport, logical layer, retry | `TRN` | 50 | Enterprise | R3 | — | **Met** — 50 IDs off the baseline, all 18 transport fixtures green ×3 |
| └ **M1c** ✅ | Error model | `ERR` + Appendix B | 19 | Large | R3 | — | **Met** — 19 IDs off the baseline, 131 of 134 error fixtures green ×3, Appendix B generated |
| **M2** ✅ | Authentication — Core methods | `AUT` (token, userpass, AppID, token store, auto-renew, security) | **39** | Enterprise | R3 | — | **Met in .NET** — all 39 IDs off the baseline (305→267), auth fixtures green, no token in any captured log (TST-051), R-10 gate sweep done. Parity (×3): deferred to M13/Stage 2 (D-6) |
| ├ **M2a** 🔶 | Token source, token store, harness instruments | `AUT`, `CFG`, `TST` | 14 | Large | R3 | **1** done | **.NET met** — 14 IDs off the baseline, six auth fixtures green, both instruments proven. Rust and Python deferred to Stage 2 |
| ├ **M2b** ✅ | Login response contract, Userpass, AppID, security | `AUT`, `CFG-020`, `CNF`, `ERR-022` | 19 | Large | R3 | **1 done** | **Met** — .NET login fixtures green (10/10), CNF-031/032 asserted (TST-051 extended), 19 IDs off the baseline |
| └ **M2c** ✅ | Automatic renewal + **R-10 gate re-proof sweep** | `AUT-090`…`AUT-095` | 6 | Large | R3 | — | **Met** — 6 IDs off the baseline (267 total for M2), .NET clock-driven auto-renew fixtures green, R-10 sweep complete (3 findings tracked as R-11/R-12/R-13, none blocking) |
| **M3** ✅ | System API — Core subset | `SYS-001,002,005,006,008,050,051,052,053` (health, seal-status, server/cluster info, capabilities; fixed off the `~16` estimate by DR-0007) | 9 | Large | R2 | **1 done** | **Met** — .NET `sys` fixtures green (10/10 of M3's slice; the other 6 `sys.*` fixtures on disk stay pending, owned by M7), 9 IDs off the baseline |
| **M4** ✅ | KV v1 + KV v2; **`Core` found undeclarable** | `KV`, `KV1`, `KV2` | 27 booked, **25 landed** (`KV-001`→M7, `KV-010`→M8) | Large | R2 | **1 done** | **Met, with the gate corrected** — .NET `kv` fixtures green (19/20; `kv.read-many-batch` is M8), 25 IDs off the baseline (259→234), and `dotnet/README.md` authored with the CNF-002 gap list. `Core` is **not** declared: sections 16–17 are unimplemented, so CNF-002 forbids the claim (DR-0009 D-M4-3, R-14) |
| **M5** ✅ | Cluster discovery and resilience | `DSC`, `RES` (+`CFG-043`) | 33 booked, **29 landed** (`RES-001`…`004` were M1b's; `RES-030`→M7; `CFG-043` booked in) | Large | R2 | **1 done** | **Met** — all ten `resilience.*` fixtures green, 29 IDs off the baseline (234→205), `DSC-041`'s scope ruled in [DR-0010](decisions/0010-m5-cluster-discovery-and-resilience.md) D-M5-5 |
| **M6** | Authentication — remaining methods | `AUT` (FerroGate, Certificate, OIDC/SAML, FIDO2) | ~12 | Large | R3 | **1** | Section 05 has zero unimplemented MUSTs in .NET |
| **M7** | System API — remainder | `SYS` (init/seal/unseal, mounts, auth methods, policies, namespaces, audit, backup/restore) | ~18 | Large | R3 | **1** | Section 06 has zero unimplemented MUSTs in .NET |
| **M8** | Transit, TOTP, batch/pagination/cache → **declare Standard** | `TRS`, `TOT`, `BAT`, `PAG`, `CCH`, `EFF` | 38 | Enterprise | R3 | **1** | **Conformance level `Standard` declared** (.NET) |
| **M9** | PKI and SSH | `PKI`, `SSH`, `SSB` | 11 | Large | R2 | **1** | Sections 09–10 complete in .NET |
| **M10** | Other engines and identity → **declare Complete** | `IDN`, `RSC`, `FIL`, `LDP`, `RUS` | 9 | Large | R2 | **1** | **Conformance level `Complete` declared in .NET (CNF-003 satisfied)** |
| **M11** | Documentation and usage guides | `DOC` | 21 | Large | R1 | **1** | Every .NET doc sample compiles/runs (CNF-026) |
| **M12** | Live-server integration suite, closing Stage 1 | `ITG` | 16 | Enterprise | R3 | **1** | .NET release checklist (01 § Release checklist) evidenced; **Stage 1 exit** |
| **M13** | Rust and Python parity — M2a through M12 | *(same IDs as M2a–M12)* | ~229 | Enterprise | R3 | **2** | All Stage-1 gates re-met in Rust and Python; parity check across all three; shared `1.0.0` tag |

**Total: 389.** The `AUT` and `SYS` splits (M2/M6, M3/M7) are estimates against the section
headings; the exact ID lists are fixed when each milestone brief is authored, and the two
halves always sum to 40 and 35 respectively (`SYS` gained `SYS-006` at M3's brief, DR-0007).
**M13's ~229 is likewise an estimate**, summing M2a's 14 plus M3's now-fixed 9 plus the `~`
counts of M6 and M7 as booked; the exact list is fixed per source milestone as each M13
block is briefed, same as every earlier estimate in this table.
**M13 is Stage 2 in full** — see §5 for why it is tracked as one long-running milestone with
per-source-milestone exit criteria rather than re-split into M2a′…M12′, and D-6 for its
entry gate.

## 5. Milestone detail

### M0 — Harness, gates and traceability *(blocks everything)*

**Deliverables**

- `tools/traceability` — parses Appendix D, scans the three test suites for requirement
  markers (TST-040), emits the report, exits non-zero on an uncovered applicable MUST (TST-041).
- Fixture loader per language reading `specifications/fixtures/**` in place (TST-010/012, `FIX-*`).
- Fixture test driver implementing the five steps of TST-011.
- In-process HTTPS mock server per language with the TST-020 capability list and the
  TST-021 simulation list (sealed, standby, uninitialised, DoS ban, quota, 404/405/204,
  login-failure-as-200).
- CI gates: build-warnings-as-errors (CNF-020), tests (CNF-021), coverage ≥ 95 %
  line and branch with artifact publication (CNF-022, CNF-013, TST-031), lint (CNF-023),
  dependency audit (CNF-024), secret scan whitelisting only `specifications/fixtures/**`
  (CNF-025, TST-050), public-API diff (CNF-027).
- Scaffold cleanup: remove `Class1.cs`, `UnitTest1.cs`, placeholder `lib.rs` contents.

**Exit criterion.** Seed a deliberate violation of each gate (a dropped requirement marker,
a 94 % coverage file, a fake token outside fixtures) and prove CI fails on each. A gate that
has never failed has not been tested.

**Note.** The mock server and traceability tool are free of SDK types by design, so M0 does
not presuppose M1's API shape.

### M1 — Client skeleton *(the contract everything else inherits)*

Sections 00, 02, 03, 04. This is the single highest-leverage milestone: 105 requirements,
and every later milestone is expressed in terms of the types it defines.

**Sub-slices, in order:**

1. ~~**M1a — Configuration (`CFG`, `OVR`).**~~ **Complete (2026-09-13).** Configuration model,
   environment-variable precedence, TLS options, timeouts, token helper, plus the
   configuration-shaped security baseline CNF-030, CNF-033, CNF-035. Design and exit record:
   [`decisions/0003-m1a-configuration.md`](decisions/0003-m1a-configuration.md). 27 IDs left
   the baseline; every deferred `CFG-*`/`OVR-*` ID now names its owning milestone (D-M1a-10).
2. ~~**M1b — Transport (`TRN`).**~~ **Complete (2026-09-13).** HTTP mapping, headers,
   envelope, status-code handling, the `LIST` verb, redirects refused (CNF-034), rate-limit
   handling and the retry policy subset section 13 needs, plus the M1a deferrals `CFG-044`,
   `CFG-051…055`, `CFG-070/071`, `CFG-080/081`, `OVR-002/003/005/006` and `BV-CONFIG-009`.
   Design and exit record: [`decisions/0004-m1b-transport.md`](decisions/0004-m1b-transport.md).
   50 IDs left the baseline.

   **The inheritance clause did not hold, and that is the milestone's main lesson.** M1b was
   told it "may not quietly redesign the transport seam (OVR-001)" — but M1a had shipped
   *three* incompatible seams, Rust's with no method at all, so there was no single seam to
   inherit. The redesign was taken as an amendment (DR-0004 D-M1b-1), which is the process
   working; what failed was M1a's exit certifying a cross-language seam nobody had compared.
3. ~~**M1c — Error model (`ERR` + Appendix B).**~~ **Complete (2026-09-14).** Taxonomy,
   stable codes, hints, retryability, server-message recognition, hint enrichment and
   CNF-043. Design and exit record:
   [`decisions/0005-m1c-error-model.md`](decisions/0005-m1c-error-model.md). 19 IDs left
   the baseline.

   **The "generated, never hand-transcribed" instruction was the milestone's best
   decision, and it was nearly not taken.** M1a and M1b had each transcribed a slice of
   Appendix B by hand, three times over, and the natural move was to keep going. Instead
   `tools/error-catalogue` makes Appendix B executable: one parser, one intermediate,
   three emitters, and a CI gate that fails on a hand edit to generated source or a
   specification edit without regeneration. Every defect the milestone found afterwards
   was in *behaviour*, not in a string — which is the point.

   **The milestone's other lesson is that four gates were doing less than their record
   claimed.** Two were outright red on `main`, one was inert, one was too coarse to see a
   rename. None of that was visible from inside a milestone; all of it was visible the
   moment someone ran the gate instead of reading about it.

**R3 because** this milestone fixes the public API shape and the TLS/redirect security
posture. Architecture review by Claude Opus 5 before implementation; decision record required.

**Risk.** Getting the error taxonomy wrong here is the most expensive defect available in
this project — it is public API, it is cross-language, and it is load-bearing for every
engine. Budget explicit design time before any code.

### M2 — Authentication, Core methods

Token source model, login response contract, Token / Userpass / AppID, token store
operations, automatic renewal, and the section-05 security requirements. Design and rulings:
[`decisions/0006-m2-authentication.md`](decisions/0006-m2-authentication.md).

**Decomposed into three sub-slices (D-M2-1).** M2 was booked as Large / ~28 requirements;
its real content is **39** requirement IDs, a new public subsystem in three languages, two
net-new harness capabilities and the R-10 sweep. That does not fit a Large brief, and
**TOK-011** decomposes rather than raising a budget.

1. **M2a — token source, token store, harness instruments.** **Complete in .NET
   (2026-09-14), `v0.5.0`.** 14 IDs off the baseline; Rust and Python outstanding.
2. **M2b — login response contract, Userpass, AppID, security.** **Complete in .NET
   (2026-09-14), `v0.6.0`.** 19 IDs off the baseline, including the `CFG-020`/`ERR-022`
   preflight and the `AUT-002` lazy login, which shared a slice because all three turn on
   the same resolution-order rule. Design and exit record:
   [`decisions/0006-m2-authentication.md`](decisions/0006-m2-authentication.md) D-M2-25,
   D-M2-26. Rust and Python outstanding, deferred to M13 (D-6).
3. **M2c — automatic renewal, plus the R-10 gate re-proof sweep.** **Complete in .NET
   (2026-09-14), `v0.7.0`.** 6 IDs off the baseline (`AUT-090`…`AUT-095`), closing M2's
   full 39-ID removal (305→267). Design and exit record:
   [`decisions/0006-m2-authentication.md`](decisions/0006-m2-authentication.md) D-M2-27,
   D-M2-28. The R-10 sweep is recorded in
   [`decisions/0001-m0-harness-gate-proof.md`](decisions/0001-m0-harness-gate-proof.md)'s
   addendum; three findings it surfaced are tracked as risk-register rows R-11/R-12/R-13
   (§8), none blocking this exit. Rust and Python outstanding, deferred to M13 (D-6).

**M2 is now fully exited in .NET.** All three sub-slices (M2a/M2b/M2c) are complete.

**R3, non-negotiable checks:** CNF-031/032 (no secret material in logs, `ToString`, `Debug`
or `repr` at any level; redaction in the default string representation), TST-051 (tests
assert the captured log is clean), and auto-renew lifecycle correctness under concurrency.

**M2a's lesson is that the instruments were worth more than the requirements.** Two
capabilities the repository had been missing since M0 landed here: the fixture `clock`,
which the schema had defined and **no language read** — so `auth.token.lookup-self-remaining-ttl`
had never asserted anything — and the TST-051 capturing logger and observer, which had
**never been asserted in any language**. The logger immediately found a real leak: the
CFG-080 observer was receiving unredacted paths, and `auth/token/renew/{token}` puts a live
token in the path.

**The four design-review rounds and the one blocked handback were not overhead.** Every
block was a seam pinned on one side only — the executor-to-`TokenSource` direction, AUT-011
never reaching recognition from a 200, `CFG-020` dropped from the allocation, and
`Resolve()`'s concurrency voiding `CFG-070`'s thread-safety basis. Two of the four handback
defects were consequences of D-M2-9, a ruling in this very record, found by reading the
call path rather than running the suite. **A milestone that changes a seam reviews the
path, not only the results.**

**M2b's lesson is that a pin can be wrong, and the delegate that catches it should not be
the one that overrides it.** The .NET pathfinder deviated from two of D-M2-6's public-API
pins — `RequestOptions?` instead of `LoginOptions?` on the one-shot login methods (the
latter is unobservable on a call that never retains credentials to re-login with), and
`AuthInfo` dropping AUT-014's five optionals (D-M2-6 had specified they populate "after
`LookupSelf`", but nothing was ever specified to merge a `LookupSelf` result into an
`AuthInfo` — the pin could never have been satisfied by any implementation). Both
deviations were correct, and both were flagged as open questions for ratification rather
than decided unilaterally — exactly the R3 discipline `agents.md` §4.2/FAM-002 asks for:
public API shape is Claude's to decide, not a delegate's, even when the delegate is the one
that found the defect. Ratified at
[`decisions/0006-m2-authentication.md`](decisions/0006-m2-authentication.md) D-M2-26, which
also corrects a genuine mistake in this project's own D-M2-25 ruling — a code-whitelist
design for the token-source-failure marker that would have silently broken `AUT-003`'s
re-login/replay for a gated `AppId` login, caught by the R3 handback review before the
Rust/Python brief could inherit it.

**A real security defect was found and fixed in the same pass, unprompted by any
requirement ID.** A Userpass username containing `/` or `?` was sent as a different,
unauthenticated request path — path injection via unescaped path segments. `AUT-030` names
URL-path-encoding but no fixture exercised a hostile username, so this shipped invisibly
until the pathfinder read the code rather than the fixture, continuing this project's
pattern (M1b's Rust pooling, M1c's `409`/`503`, M2a's observer leak) of defects sitting
below the fixture seam.

**Verification, quoted:** `dotnet test` 533/533 passing, 98.9 % line / 96.02 % branch
(≥ 95 % floor held, no exclusion pragma); traceability 147/273/420, baseline delta exactly
the 19 IDs; error-catalogue regeneration byte-identical on `rust/`/`python/`; both untouched
along with `specifications/`. Independently re-run by the Strategic-tree R3 reviewer, not
merely quoted from the delegate.

### M3 / M4 — System API Core subset, then KV → **Core**

M3 is the smallest useful `sys` surface: health, seal-status, server info, cluster status,
capabilities. M4 is the whole of section 07 — KV v1, KV v2 versions, CAS, soft delete,
destroy, metadata.

**M3 is met. Complete in .NET (2026-09-15), `v0.8.0`.** `Client.Sys` ships `Health`, `SealStatus`, `ServerInfo`, `ClusterStatus`,
`CapabilitiesSelf`, `Capabilities.Can`/`Sys.CanAsync` — the nine IDs DR-0007 fixed off the
`~16` estimate. `HsmStatus` stays out of scope, booked to M7. The routing classification
itself is the notable part: DR-0007 found the contract already settled by existing
fixtures, the M1b Shape-B seam and the M2b sub-client precedent, so this landed as a
row-2 implementation pass rather than the row-3 pathfinder the roadmap's `Large`/R2 booking
might suggest. The mandatory R2 handback review (Opus) blocked once on a genuine
`specifications/` self-contradiction the drafting introduced (SYS-006's "MUST NOT raise"
text, worked out against SYS-001's actual pattern) — corrected in `06-system-api.md` and
recorded as DR-0007's addendum, not carried as a lingering caveat — plus two required
Engineering-tree fixes (a false `RateGateState.Paused` signal on a standby health probe,
and the spec-named `Sys.CanAsync` member, which the first pass had built only as
`Capabilities.Can`). Both closed in the same decision; nothing carries into M4.

**M4 is met. Complete in .NET (2026-09-15).** `Client.Kv` ships the version-explicit
`Kv.V1`/`Kv.V2` sub-clients, per-environment overrides, the `KV2-030` path helpers and the
`WriteIfAbsent`/`UpdateWithRetry`/`ReadField` conveniences — 25 of the 27 booked IDs, with
`KV-001` and `KV-010` deferred to M7 and M8 because `SYS-026` and `BAT-007` do not exist
yet and D-M1c-25 forbids guessing in their place. Routed as a **row-3 pathfinder**, unlike
M3: no decision record pinned a KV name, a third of the surface was unfixtured, and
KV2-009's prose argues with itself, so the contract was genuinely unsettled until
[DR-0009](decisions/0009-m4-kv-engine.md) settled it.

**M4 exit was supposed to be the first externally meaningful gate** — the README declaring
`Core`. It is not, and the reason is a roadmap defect rather than a KV shortfall: `Core`
requires sections 16 and 17, which are M11's. The README therefore declares no level and
lists the gaps by ID (CNF-002, CNF-041). What *is* true from this point is the substance:
the .NET SDK can read and write secrets and authenticate, which is what `Core` was chosen
to mean. Rust and Python reach the same point only at Stage 2's M13 pass over M2b–M4
(D-1, D-6).

**Three defects were found by reading the code rather than running the suite**, continuing
the project's pattern (M1b's Rust pooling, M1c's `409`/`503`, M2a's observer leak, M2b's
path injection). The transport's path builder never split the query off a pre-encoded path
— harmless only because login was the single pre-encoded caller, and KV v2 puts `?version=`
and `?env=` on exactly that path. The `..`-segment guard the pathfinder added covered
`path` and `prefix` but not `mount`, so `mount: "secret/../auth/token/lookup-self"` still
re-routed a request and `DataPath` propagated the traversal into a string KV2-030 exists to
hand to `Sys.Batch`. And `WriteSecret` validated `Env` but not the keys of `Envs`. All three
are fixed inside M4; the second was found by the mandatory R2 handback review, which is the
gate working as designed.

### M5 — Cluster discovery and resilience

SRV discovery, health probing, node ranking, sticky sessions, bounded failover, retry
interaction, TLS with discovery, diagnostics, operator fan-out.

**Dependency.** Consumes the retry primitives built in M1b; do not rebuild them.

**M5 is met. Complete in .NET (2026-09-15)**, in two sequential slices over one contract
([DR-0010](decisions/0010-m5-cluster-discovery-and-resilience.md)). **850 tests,
99.13 % line / 97.02 % branch.** All ten of Appendix C's `resilience.*` fixtures are green;
six of them did not exist and were authored here (218→224 on disk).

**The booking was wrong in three places, and grounding it before briefing is what found
them.** `RES-001`…`RES-004` had already left the baseline at M1b with the retry loop,
`IJitterSource` and `RequestOptions.TotalTimeout`. `RES-030`'s `*ClusterWide` variants wrap
`Sys.Seal`/`Sys.Unseal`, which are `SYS-012`/`SYS-013` and booked to **M7** — implementing a
cluster-wide wrapper around operations that do not exist would have meant inventing M7's
contract, and `RES-030` is a SHOULD, so the deferral breaks no MUST (D-M5-3, the
`KV-001`→M7 shape). Against that, `CFG-043` — §02's twin of `RES-010`, the same code path
and the same test — was baselined with **no owner**, and M5 implements it either way, so it
is booked in (D-M5-16a). Net: **29 landed, not 33**, and the baseline is at **205**.

**The milestone's load-bearing decision was scoping `DSC-041`, not writing the failover.**
Read naively, "a transport-level failure … is a node failure → `BV-DISCOVERY-003`" would
have changed the observed error code of every transport failure in the SDK. D-M5-5 rules it
in two limbs: transport failures reclassify **only in discovery mode** and only for the
three kinds `DSC-041` names, so literal mode stays byte-identical to M1b; and a matching
`5xx` **never** has its code replaced, in either mode, because §13:125 presupposes
`BV-SERVER-003` surviving, `CFG-053` makes a sealed 503 never-retryable, and nine landed
fixtures assert those codes. Twelve landed fixtures would otherwise have needed amending —
the fixture corpus is the evidence, and it reads one way only.

**Two handback reviews blocked, and both blocks were correct.** M5a's was blocked on
`DSC-022`: ruled surfacing-only, because `DSC-033`'s rank list is closed and explicitly
deterministic, so adding a fifth sort key would diverge from any Stage 2 implementation
reading `DSC-033` literally (D-M5-20; preferring healthy nodes is now risk **R-17**). M5b's
was blocked on a **proven `RES-001` breach**: an idempotent read at `MaxAttempts = 3`
produced six wire attempts against a cap of four, because the failover replay restarted with
a fresh retry budget. The cause was a conflict between two decisions of DR-0010 itself —
D-M5-7's cap and D-M5-6's "the replay retries normally" are jointly satisfiable only when
the first pass ends at its first attempt, which is the one shape every failover test happened
to script. Corrected by bounding the replay to `max(1, MaxAttempts + 1 - AttemptsBefore)`,
failover-only (D-M5-28). The alternative — let a late node failure forfeit failover — would
have satisfied `RES-001` by violating `DSC-042`, whose MUST is conditioned only on arming
and idempotency. Both fixes are proven by seeded violation and revert, the R-10 pattern.

**One `specifications/` change, authorised and R3.** `resilience.failover.read-once`
returned KV v2 metadata as `{"version": 1}`, which section 07's type block forbids and
D-M4-12 maps to `BV-PROTOCOL-002`. The fixture predates M4 and contradicted the section it
exercises, so it is repaired rather than the ruling relaxed (D-M5-26) — relaxing D-M4-12
would have been CLA-004's "weaken a gate to make something pass". Its §5.3 human
confirmation is carried to the release-checklist line below. A second instance of the same
defect class was then found in a fixture authored *inside* this milestone (D-M5-30), which
is why the corpus sweep is recorded as risk **R-19**.

**Deliberately not shipped: an `ISrvResolver`.** The .NET BCL exposes no DNS SRV API and
`DSC-014` requires the resolver be injectable, not shipped, so `DSC-010` is covered by the
seam — but an application that supplies none silently gets literal behaviour where it asked
for discovery. Risk **R-16**, owned by M11 (documentation) and M12 (the live suite cannot
reach a cluster without one). Adding a DNS dependency is an R2 call with a supply-chain
dimension and belongs to those milestones.

**M5 exit criteria, all met:** ten of ten `resilience.*` fixtures green; the 192 previously
landed fixtures green with **none amended** except the one authorised repair; 29 IDs off the
baseline; coverage above the 95 % floor on both axes; no conformance level claimed or
affected (sections 16–17 are still M11's — R-14).

**Release-checklist line carried forward (D-M5-26, `agents.md` §5.3).** Before the M12
release is cut, the project owner must confirm the `resilience.failover.read-once` repair:
it is a `specifications/` change, which CRS-004 makes R3, and §5.3's R3 gate ends in human
confirmation before release. Rust and Python inherit the corrected body at Stage 2.

### M6 / M7 — Auth and Sys to completion

FerroGate machine identity, Certificate (mTLS), OIDC/SAML (browser-mediated), FIDO2; then
init/seal/unseal, mounts, auth-method administration, policies plus dry-run, namespaces,
audit, dashboard, backup/restore/export/import.

**R3** — unseal keys and init responses are the most sensitive payloads in the API.

### M8 — Transit, TOTP, efficiency → **Standard**

Transit keys, encrypt/decrypt, rewrap, sign/verify, HMAC, random, hash, datakeys; TOTP; and
all of section 14 — client rate gate, batch endpoint, `*-info` cursor pagination,
`sys/cache/version` coherence epochs. Section 14 mandates its own documentation guidance
(14 § "Guidance the SDK documentation MUST include"), so that slice of M11 is pulled forward
into this milestone.

**Exit:** conformance level `Standard` declared.

### M9 / M10 — PKI, SSH, then remaining engines → **Complete**

PKI CA management, roles, issue/sign, revoke, CRL, bulk listings; SSH CA, roles, signing,
OTP, brokering policy; then Identity, asset groups, Resources, Files, LDAP, cert lifecycle,
notifications, Rustion.

**M10 exit:** `Complete` declared in the .NET README — CNF-003 satisfied for .NET. This is
**Stage 1's conformance target**; Rust and Python reach `Complete` only inside M13.

### M11 — Documentation and usage guides

Section 16 requirements plus the guides of section 17, for .NET (Stage 1). Every sample
compiles and runs in CI (CNF-026); every public symbol carries a doc comment. Rust and
Python guides are authored in M13 once each language's surface is implemented, not adapted
speculatively ahead of it.

### M12 — Live-server integration suite and 1.0.0-dotnet, closing Stage 1

The 16 `ITG` requirements and the `ITG-S<nn>` scenarios of 15 § Required scenarios, against
the server versions in [`test-matrix.json`](specifications/test-matrix.json), with the
provisioning, isolation and cleanup rules of 15, **run against .NET only**. Then the
Stage-1 exit checklist: every .NET gate green, coverage stated, traceability report clean
for the .NET-applicable requirement set, changelog, README conformance statement.

**M12 does not cut the shared `1.0.0` tag.** CLA-003 and the versioning rule at the top of
`CHANGELOG.md` still require all three packages to match before a release is called
`1.0.0`; M12 closes Stage 1 and hands the project to Stage 2 (M13). If an interim tag is
wanted for the .NET-only milestone, it follows the `0.5.0` precedent — an explicit,
recorded exception (D-M2-15), never a silent redefinition of what `1.0.0` means.

**Human confirmation required before any tag** (R3 rule, `agents.md` §5.3).

### M13 — Stage 2: Rust and Python parity, M2a through M12, then the shared 1.0.0

**Entry gate:** D-6 — all of M2b…M12 exited in .NET first. Nothing here starts early.

**Shape.** M13 is tracked as one milestone rather than re-split into M2b′…M12′ because its
unit of work is no longer "design, then implement" (the design is already settled) — it is
"transcribe and verify against an already-reviewed reference," which is the row-2 shape
`agents.md` §4.2 describes, run once per source milestone's requirement-ID block — M2a included, since Rust and Python never got that pass either — in Rust
and Python **in parallel with each other** (not with .NET — D-2 amended). Each source
milestone's block is its own exit criterion inside M13:

- Same requirement IDs as the .NET milestone it mirrors.
- Same fixtures, run to green, not a re-derived assertion (D-5).
- The same public-surface diff this file has required since M1b (R-9), now run three ways
  for the first time since M2a.
- The parity exception carried in D-M2-15 / D-M2-18 (the `catch` ordering in `BV-AUTH-017`,
  named there as the sharpest known trap) is checked explicitly, not assumed closed by
  "the tests pass."

**Risk.** M13 carries the deferred cost of D-1's supersession: eleven milestones' worth of
.NET-only implicit decisions, not one, land on Rust and Python at once, with no same-cycle
.NET review to catch a gap before two languages inherit it. Budget the review pass
accordingly — this is not a mechanical port (§7, D-2).

**Exit and close:** every Stage-1 gate re-met in Rust and Python, the three-way parity check
green, `CHANGELOG.md` records the parity pass per source milestone, `ROADMAP.md` §2 and §4
reflect `Complete` in all three languages, and the shared `1.0.0` tag is cut per the release
checklist — with human confirmation before the tag (R3, `agents.md` §5.3).

## 6. Dependency graph

```
STAGE 1 — .NET only
M0 ✅ ▶ M1a ✅ ▶ M1b ✅ ▶ M1c ✅ ─┬──▶ M2a 🔶 ▶ M2b ✅ ▶ M2c ✅ ──▶ M3 ✅ ──▶ M4 ✅ ═══ CORE ⛔ (R-14)
                             │                   │
                             │                   ├──▶ M5 ──┐
                             │                   ├──▶ M6 ──┤
                             │                   └──▶ M7 ──┤
                             │                             ├──▶ M8 ═══ STANDARD (.NET)
                             │                             │
                             └─────────────────────────────┴──▶ M9 ──▶ M10 ═══ COMPLETE (.NET)
                                                                        │
                                                              M11 ──────┴──▶ M12 ═══ STAGE 1 EXIT
                                                                                      │
                                                                                      ▼
                                                              STAGE 2 — Rust + Python parity
                                                                        M13 (M2a…M12, parallel
                                                                        Rust ∥ Python per block)
                                                                                      │
                                                                                      ▼
                                                                              1.0.0 (all three)
```

**Serial by necessity within Stage 1:** M0 → M1a → M1b → M1c → M2a → M2b → M2c → M3 → M4 →
… → M12, entirely in .NET.
**⛔ The three level markers above are blocked, not reached.** `CORE`, `STANDARD` and
`COMPLETE` each require sections 16–17, which are M11's, so no declaration is legal until
M11 lands. M4 exited with the KV work done and the level undeclared. The graph keeps the
markers where the milestones are so the discrepancy stays visible (R-14, §10 question 4).
**🔶 M2a is .NET only, and — since D-1's supersession — stays that way for the whole of
Stage 1.** Its Rust and Python pass no longer blocks M2b; it is folded into M13 (D-6) and
does not start until M12 exits.
**Parallel within Stage 1, after M4:** M5, M6 and M7 are independent of one another; M9 and
M10 depend only on M1 and M4. M11 can start as soon as the .NET surface it documents is
frozen — per section, not as one block at the end.
**M13 (Stage 2) is serial after M12, but parallel inside itself:** Rust and Python run
against each other, not against a further .NET pass (D-2 amended), and per-source-milestone
blocks inside M13 follow the same M2a→…→M12 dependency order .NET already proved.

**Hard constraint:** at most 10 concurrent agents system-wide (`agents.md` §7.2). During
Stage 1 this is far less binding than under horizontal slicing — one language at a time
does not saturate the ceiling the way three did. During M13, two languages × up to several
parallel source-milestone blocks can approach it again; run at most two M13 blocks
concurrently per the same rule.

## 7. Delegation shape

Per `claude.md` §1.1 and §5, implementation does not start with Claude. The repeating unit
of work is:

| Step | Owner | Output |
|------|-------|--------|
| 1. Frame and ground | Claude Sonnet 5 | Milestone objective, exact requirement ID list, risk tier |
| 2. Design | Claude Sonnet 5 (Claude Opus 5 for R3) | Decision record: API shape, type names per the 00 mapping rules, parity contract |
| 3. Brief | Claude | One four-section brief (TOK-004) per language, budget per tier (TOK-011) |
| 4. Implement | Codex Engineering Orchestrator | Stage 1 (M2b–M12): .NET only. Stage 2 (M13): Rust and Python in parallel with each other, against .NET's already-reviewed behaviour (D-1, D-2 amended) |
| 5. Review | Claude | Verdict per `claude.md` §3.1, findings tied to file, line and requirement ID |
| 6. Parity check | Claude | Same fixtures, three suites, identical assertions |
| 7. Accept | Claude (Strategic Orchestrator for R3) | Milestone closed, README gap list updated, `CHANGELOG.md` entry written, this file updated (`agents.md` §11) |

Briefs cite requirement IDs and `file:line` ranges — never spec prose (TOK-005, TOK-006).
A milestone that cannot fit its tier budget is decomposed, not granted a bigger budget.

**Brief rule added after M1a: pin every public name, not only the behaviour.** Three of
M1a's five review findings were the same shape — three agents independently invented three
names for one spec concept (`Details.setting`, `RateGate`'s fields), or one agent omitted a
public setter the other two provided. A brief that fixes the error *code* for every path but
leaves the developer-facing *string* and the *member names* to the implementer will get three
defensible, mutually incompatible answers. This costs one line per member in the brief and is
the cheapest defect prevention available; it matters most in **M1c**, where Appendix B is a
30 KB table of codes, messages and hints that all three languages must expose identically.

**What the M2a pathfinder pass bought (D-2 evidence, second data point).** The .NET slice
surfaced three defects itself — an unredacted observer path, a cancelled resolve escaping
uncoded, and a test reading the developer's real `~/.vault-token` — and the handback gate
found four more, including a faulted single-flight that bricked the client permanently and
an `AUT-085` refinement firing on any unmapped 4xx on the renew path. **All seven were in
code Rust and Python would have transcribed verbatim**, and two were consequences of a
ruling in the milestone's own decision record. D-2 is now supported by two milestones rather
than one; the extra serialisation step keeps paying.

**What the M1a pathfinder pass actually bought (D-2 evidence).** The .NET slice surfaced one
scope error in the design (`RateGate`/`AutoRenew` omitted from the settings table, which would
have made `CFG-001` a false positive on the ratchet) and two under-specified boundaries, all
corrected in the decision record *before* Rust and Python started. Both defects found in .NET
review were written into the Rust and Python briefs as behaviour-to-avoid, and **neither
recurred**. The extra serialisation step is paid back; D-2 stands.

## 8. Risk register

| # | Risk | Tier | Mitigation |
|---|------|------|------------|
| R-1 | ~~Error taxonomy (M1c) is wrong; it is public API and cross-language~~ **Retired at M1c.** The taxonomy is generated from Appendix B and regeneration is a CI gate, so the catalogue cannot drift from the specification or between languages. What remains is behavioural, and is covered by R-9 | — | Closed — [DR-0005](decisions/0005-m1c-error-model.md) D-M1c-1 |
| R-2 | 95 % branch coverage (CNF-010) is expensive on error paths, which CNF-011 explicitly puts in scope | R2 | Write the failure-path fixture with the feature (TST-013); never weaken the floor (CLA-004) |
| R-3 | Rust branch coverage may be unavailable on the toolchain | R1 | 15 § Coverage permits line and region ≥ 95 as the documented substitute — record the substitution once |
| R-4 | Three languages drift silently | R2 | Shared fixtures loaded from the repo (D-5, TST-010); parity is a milestone exit criterion |
| R-5 | Secret material leaks into logs or `Debug`/`repr` | R3 | CNF-031/032 asserted by capturing-logger tests (TST-051) in every auth and KV suite. M2b extended the assertion to the Userpass/AppID login paths and found a real path-injection defect (not a leak) while doing so — see M2b's milestone-detail note |
| R-6 | No live BastionVault server available. **Re-tiered R3 and pulled forward to M1 by DR-0001 D-M0-7** — FIX-010 requires fixture response bodies to be captured from a real server exchange, and 66 of Appendix C's ~140 mandatory fixtures are unwritten, so fixture authoring is blocked from M1 rather than M12 | R3 | Integration tests are skippable per run but mandatory in the CI matrix. **Provisioning a server matching `specifications/test-matrix.json` is now an M1 entry condition, not an M12 one** — escalated to the project owner at M0 exit (§10 question 3) |
| R-7 | ~~M1 is 105 requirements — too large to review as one unit~~ **Retired at M1c.** All three sub-slices exited independently; the split did what it was for | — | Closed |
| R-9a | **A public name that reads the same and behaves differently.** M2a found the worst instance yet: `Clock.now()` returned wall-clock in .NET and Python and a monotonic `Instant` in Rust, so `AUT-014`'s `RemainingTtl` was not merely untested on Rust but **uncomputable** — invisible to fixtures, coverage and traceability alike, and to the public-surface diff, which sees the name and not the contract | **R2** | Renamed to `NowUtc`/`now_utc`/`now_utc` in all three (D-M2-2). The general control: when a member's *kind* is ambiguous, the kind goes in the name. Watch for the same shape wherever two languages agree and the third is idiomatic |
| R-9 | **Cross-language drift that no gate can see.** Fixtures pin wire behaviour, coverage pins executed lines, traceability pins requirement IDs. None of the three sees a differing public *name*, a differing developer-facing *string*, or a *capability present in two SDKs and absent in the third* — M1a shipped all three of those defects at 98–100 % coverage with every gate green, and `CFG-050` was legitimately "covered" the whole time Rust could not set `InitialBackoff` | **R2** | Three controls now. From M1b: the brief pins every public member name (§7), and milestone exit includes an explicit **public-surface diff across the three languages**. Added at M1c: **a deferred branch returns the specification's answer, never a plausible guess** (D-M1c-25) — M1c found three divergences that were all plausible guesses on paths no fixture reaches, one of which silently suppressed a permitted retry. Caveat on the second control: Python's `CNF-027` baseline is names-only, so the three-way diff is member-level for .NET and Rust and name-level for Python until D-M1c-22 is done. **M2b shows the control working one layer up:** a pin itself (D-M2-6's `LoginOptions?` on one-shot logins, and its `AuthInfo` AUT-014 optionals) was wrong, and a wrong code-whitelist design in this project's own D-M2-25 ruling would have silently broken `AUT-003` for a gated login — both caught by the R3 handback review **before** the Rust/Python brief could inherit them (D-M2-26). Stage 1's single-lane structure means this catch happens once, in .NET, instead of three times independently |
| R-10 | **A gate's record is trusted instead of its execution.** M1c found four: `CNF-025` red on `main` since M1a, `CNF-010` red in the Python job since M1b, `CNF-027` inert in .NET (D-M1b-19) and names-only in Python (D-M1c-22). M2a found a fifth shape — two *instruments* the schema and the spec had defined that no language executed at all: the fixture `clock`, unread since M0, and `TST-051`, never asserted anywhere | **R2** | Re-prove every gate by seeded violation and revert, as DR-0001 required and only partly delivered. **Formally M2c's exit condition** (D-M2-1) with its evidence recorded once, in one place. M2a proved its own two instruments this way and kept both as standing tests, which is the pattern the sweep should follow |
| R-8 | Appendix A lists 167 endpoints; mechanical volume swamps design attention | R1 | Endpoint plumbing is Engineering-tree bulk work — route it, do not hand-write it in the Claude tree |
| R-11 | ~~**CNF-023 (.NET analyzer/style diagnostics) is inert.**~~ **Closed 2026-09-15.** M2c's R-10 sweep found `dotnet/.editorconfig`'s bulk `dotnet_analyzer_diagnostic.category-<X>.severity = none` lines silently defeated every `dotnet_style_*_ = *:error` option-embedded severity beneath them — only the literal `dotnet_diagnostic.<ID>.severity` form survived. Fixed by adding a literal `dotnet_diagnostic.<ID>.severity` override for every already-declared option (no rule added or dropped), plus correcting two option keys that were not valid Roslyn keys. All 97+~150 violations the fix surfaced across both .NET projects were fixed in code, not suppressed (CLA-004); the gate-fires proof was re-run by seeded violation and revert | — | Closed — [DR-0008](decisions/0008-r11-cnf-023-remediation.md). Evidence: `decisions/0001-m0-harness-gate-proof.md` addendum, Row 7 |
| R-12 | ~~**`cargo audit` is genuinely red on `main`, independent of anything M2c did.**~~ **Closed 2026-09-15.** The pinned `rustls = "=0.23.40"` (`rust/bastionvault-integration-sdk/Cargo.toml`) was named in RUSTSEC-2026-0285 (TLS 1.3 handshake messages accepted across encryption-level boundaries, medium 5.3), fix `>=0.23.45`. CRS-003: TLS surface, R2 minimum | — | Closed — the pin is now `=0.23.45`. Brought forward from its M13/Stage 2 entry gate because the R-15 harness fix re-ran `rust.yml` on `main` and the CNF-024 gate failed there: a red gate on `main` is not something a freeze can hold open. The Stage-1 freeze (D-1/D-6) is intact — `rust/` library code is untouched, the crate's public API is unchanged (CNF-027, `rustls` is not re-exported), and Rust is 219/219 green with clippy (CNF-023) and `cargo audit` (CNF-024) both clean. `rust/*/Cargo.lock` is gitignored, so the exact pin in `Cargo.toml` is the whole fix |
| R-13 | **`python -m pip_audit` is genuinely red on `main`**, for an unrelated reason: a fresh `pip install -e ".[dev]"` pulls `requests 2.32.5` as a transitive dependency of `pip-audit` itself (not a direct or shipped project dependency), named in PYSEC-2026-2275, fix `2.33.0` | R1 | **Not fixed at M2c** — a dev-tooling transitive finding, not a shipped-artifact one, but `python.yml`'s `pip_audit` invocation has no scope restriction, so Python's CI job fails on it today regardless of Stage 1 focus. Owner: whoever next touches `python/` (M13 at the latest); a `pip-audit`/`requests` version bump is expected to be sufficient. Evidence: `decisions/0001-m0-harness-gate-proof.md` addendum, Row 13 |
| R-14 | **The conformance-level declaration schedule is unsatisfiable, and three milestone gates are stated in terms of it.** `Core` (M4), `Standard` (M8) and `Complete` (M10) each require sections 16–17, which are M11's. Found at M4 by grounding the gate against CNF-001/CNF-002 rather than against the KV work | R2 | **Open — project-owner decision** (§10 question 4): move M11 ahead of M8, or move all three declarations to the end. Meanwhile the control is honesty, not a claim: `dotnet/README.md` declares no level and lists the gaps by requirement ID, so no release can imply a conformance level it does not hold. Evidence: [DR-0009](decisions/0009-m4-kv-engine.md) D-M4-3 |
| R-15 | ~~**A gate that passes only on the CI matrix's single version.**~~ **Closed 2026-09-15.** Python's suite had 12 failures under Python 3.14 and none under 3.12: all three in-process mock servers issued CA and leaf certificates with no Subject Key Identifier and no Authority Key Identifier, which OpenSSL 3.5+ rejects during chain verification. CI pinned 3.12, so it was green on a harness that did not work. This is the R-10 shape one layer out — the gate is green because of what CI does not run. Found at M4 by running the Python suite locally while verifying an unrelated fixture-count change | — | Closed — both extensions are now issued in .NET, Rust and Python, `KeyUsage` is explicit, and `python.yml` runs a 3.12 **and** 3.14 matrix so a version-only failure cannot hide again. Python 484/484 green on 3.14 (was 472 passed / 12 failed), .NET 741/741, Rust 219/219. The Stage-1 freeze (D-1/D-6) does not cover a test harness that does not run; `rust/` and `python/` library code is untouched |
| R-16 | **Cluster discovery ships inert.** No `ISrvResolver` implementation ships, because the .NET BCL exposes no DNS SRV API and `DSC-014` requires only that the resolver be injectable. An application that supplies none takes `DSC-011`'s "no records" path and therefore gets **literal behaviour where it asked for discovery** — silently, since `DSC-012` makes that a legal outcome rather than an error | R2 | Owned jointly by **M11** (documentation MUST state that a resolver is required for real discovery) and **M12** (the live suite cannot reach a cluster without one). Shipping one means taking a DNS dependency — an R2 call with a supply-chain dimension, which is why M5 did not absorb it. Evidence: [DR-0010](decisions/0010-m5-cluster-discovery-and-resilience.md) D-M5-23 |
| R-17 | **`DSC-033` cannot prefer healthy nodes without a `specifications/` change.** `DSC-022` says surfacing `cluster_healthy` lets ranking "prefer healthy nodes as a tiebreak after RTT", but `DSC-033`'s rank list is closed, exhaustive and explicitly deterministic, and does not contain it. At equal RTT and weight an unhealthy node can therefore be picked over a healthy one on the lexical-URL tiebreak | R3 | **Open, unowned.** Ruled surfacing-only at M5 (D-M5-20): adding a fifth sort key would widen a list the specification closes and would diverge from any Stage 2 implementation reading `DSC-033` literally. Changing it is a `specifications/` edit, R3 by CRS-004. The control meanwhile is that the behaviour is pinned and fixture-asserted, so all three languages will at least be wrong identically |
| R-18 | **`AUT-003`'s relogin replay can exceed `RES-001`'s attempt cap on its own.** D-M2-9 deliberately gives the relogin replay a fresh `MaxAttempts`, which is correct for `AUT-003` but means `AttemptsBefore` can reach `2 × MaxAttempts` with no failover involved. `RES-001`'s cap is written about the failover replay and says nothing about a relogin replay, so the two accepted rulings are in tension | R2 | **Open, owned by M6**, where `AUT-003`'s replay is actually exercised. Not M5's to resolve: it predates the milestone and fixing it means reopening an accepted M2 ruling (R3). M5 guarantees only that *failover* never causes the cap to be exceeded — D-M5-28's clamp is what stops the two mechanisms compounding multiplicatively. Evidence: [DR-0010](decisions/0010-m5-cluster-discovery-and-resilience.md) D-M5-29 |
| R-19 | **Fixtures can encode response bodies their own specification section forbids, and pass.** M5 found two instances of one defect class: `resilience.failover.read-once` (landed since before M4) and `resilience.backoff.math-seeded` (authored *inside* M5, one addendum after the rule against it) both returned KV v2 metadata without `created_time`, which section 07 declares non-optional and D-M4-12 maps to `BV-PROTOCOL-002`. Both passed for incidental reasons — the first because the reader ran before M4 existed, the second because `Logical.Read` never invokes the KV v2 reader | R2 | **Open, unowned; a sweep is §10 question 5.** Two instances in one milestone implies more across the 224-fixture corpus, and each is a latent Stage 2 trap: Rust and Python must reproduce these bodies exactly, and will fail on the ones whose reader they implement. Mechanical Engineering-tree work — validate every fixture body against its section's type block — but it needs a milestone slot before M13, not an ad-hoc pass. Evidence: [DR-0010](decisions/0010-m5-cluster-discovery-and-resilience.md) D-M5-26, D-M5-30 |

## 9. Tracking

- **Per-milestone truth:** the traceability report (TST-041). A milestone is done when its
  requirement IDs move from uncovered to covered and stay there.
- **Per-commit truth:** the CI gate set from M0. A red gate is never weakened (CLA-004).
- **Known gaps:** `tools/traceability/baseline.json` remains the authoritative, machine-readable
  gap list. **Done at M4:** `dotnet/README.md` renders it into CNF-002 prose — the two
  section-07 gaps by requirement ID, the remaining 234 by section with counts and a pointer
  to the baseline. It is updated at every Stage-1 milestone exit, and a milestone that
  changes the baseline and not the README has left the README wrong. Rust and Python gain their own READMEs and gap lists inside M13.
- **Parity:** during Stage 1 there is only one language to compare, so the three-way
  public-surface comparison (R-9) is dormant — it resumes as M13's per-block exit criterion,
  where it carries more weight than it ever did under horizontal slicing (D-1). Same names,
  same reachable capabilities, checked against .NET as the reference.
- **Decisions:** recorded once, where they belong, and linked thereafter (CLA-008). This file
  records only the sequencing decisions D-1…D-6; per-milestone design decisions belong in
  their own decision records — M0 in [`decisions/0001-m0-harness.md`](decisions/0001-m0-harness.md),
  M1a in [`decisions/0003-m1a-configuration.md`](decisions/0003-m1a-configuration.md).
- **Shipped truth:** [`CHANGELOG.md`](CHANGELOG.md). Every user-visible change gets an entry
  as it lands (REC-001); the roadmap says what is next, the changelog says what is done.
- **Keeping this file honest (REC-002).** A milestone exit updates §2 current state, its row
  in §4, its exit criteria in §5, and §8 where the milestone changed a risk. The milestone is
  not closed until that edit lands in the same change as the work. Claude owns both files;
  Engineering-tree agents propose entries, they do not edit them (REC-004, ENG-008).

## 10. Open questions for the project owner

1. **Target date and cadence.** This plan is sequenced but not calendared. Milestone
   durations depend on delegate throughput. Two datapoints now exist: M0, and M1a at 27
   requirement IDs across three languages in one session — design, pathfinder pass, two
   parallel passes, five review round-trips. M1b is roughly 45 IDs and carries more
   behavioural surface, so it is not a simple multiple of M1a; treat any estimate built on
   these two points as an order of magnitude, not a schedule.
2. **Release strategy — answered 2026-09-14.** Ship `Core` as a `0.x` preview at M4, or hold
   everything to a single `1.0.0` at M12 (now M13)? The project owner directed the staged
   plan in D-1: .NET ships `0.x` previews milestone by milestone through Stage 1 (M2b–M12),
   each an explicit exception to the shared-version rule per the `0.5.0` precedent
   (D-M2-15), and the shared `1.0.0` waits for M13. The gap lists (CNF-002) make each .NET
   preview honest about what it does not yet cover, including "Rust and Python" itself.
3. **Live server access for M12.** R-6 assumes a provisionable BastionVault instance matching
   `test-matrix.json`. If none exists, the integration suite needs a plan of its own.
4. **When is a conformance level declared? — new at M4, and blocking three gates.** `Core`,
   `Standard` and `Complete` each require specification sections 16 and 17 (the `DOC`
   requirements), which are booked to **M11** — after M8's `Standard` and M10's `Complete`
   declarations. As sequenced, none of the three declarations is legal at the milestone that
   claims it, and M4 has already exited declaring nothing (R-14, DR-0009 D-M4-3). Two
   answers, and it is a scope call rather than a design one:
   **(a) move M11 ahead of M8** — `Core` becomes declarable as soon as the documentation and
   guides 1–5 land, which is also when an external consumer can actually adopt the SDK;
   documentation is written against a smaller, more stable surface, and the cost is a
   milestone of prose before the next feature.
   **(b) move all three declarations to the end**, after M11, and let the `0.x` previews stay
   level-less with gap lists. Nothing is re-sequenced and the previews stay honest, but the
   SDK ships usable for a long stretch with no formal conformance claim, which is precisely
   what a level exists to communicate.
   Claude's recommendation is **(a)**: a conformance level whose documentation requirements
   are unmet is not a level, and deferring it to the end concentrates the one milestone whose
   content is hardest to parallelise at the point where the release pressure is highest.

5. **Does the fixture corpus need a conformance sweep before Stage 2? — new at M5.** M5 found
   two fixtures encoding a KV v2 response body that section 07 forbids (R-19), one landed
   since before M4 and one authored inside M5 itself. Both passed, for different incidental
   reasons. Fixtures are the mechanism that makes three languages agree, so a fixture that
   contradicts its own section is a defect that propagates: Rust and Python must reproduce
   these bodies exactly, and will fail on whichever ones their reader implements. Two
   instances in one milestone is weak evidence about 224 files, which is the question —
   **(a) sweep now**, as a small Engineering-tree milestone validating every fixture body
   against its section's type block, paying a slot before M13 to find the rest; or
   **(b) let M13 find them**, treating each as a Stage 2 parity defect at the point it fires.
   Claude's recommendation is **(a)**, and narrowly: the work is mechanical and cheap at the
   Haiku rung, whereas under (b) each instance surfaces as a confusing Rust or Python failure
   whose cause is in a shared artefact rather than in the code being written, which is the
   most expensive place to discover it. The counter-argument is real — (a) spends a slot on a
   corpus that may hold no further instances.
