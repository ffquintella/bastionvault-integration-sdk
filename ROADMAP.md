# Roadmap — implementing the specifications

**Owner:** Strategic Orchestrator (Claude) · **Authority:** subordinate to [`agents.md`](agents.md) and [`claude.md`](claude.md)
**Source of truth for behaviour:** [`specifications/`](specifications/README.md) · **Version:** 1.26.0 · 2026-09-21

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

## 2. Current state (2026-09-21, **M9 complete** — sections 09 and 10 bound in .NET; no conformance level declared)

**M9 is complete, all four slices.** `Client.Pki` (with `.Acme`, `.Csr`, `.SignRequests`),
`Client.Ssh` and `Client.SshBroker` bind roughly **91 operations** across sections 09 and 10.
**1504 .NET tests, 99.48 % line / 95.86 % branch**; traceability **305 covered / 120
baselined** of 425; **253 fixtures on disk**, all seven of Appendix C's `pki.*`, `ssh.*` and
`sshbroker.*` green — including `sshbroker.effective-v2-pinned`, which had sat on disk
undriven since the original specification import.

**10 of the 11 booked IDs landed, and the 11th is held back rather than missed.**
`PKI-030`'s second limb requires recognising a queue-cap breach *by message*, and
`BV-QUOTA-002` has no recognition row in Appendix B and no server message in any document.
Inventing one is what R-23 already cost this project three milestones, so the ID **stays
baselined** and the gap is **R-31**, owned by M10 (D-M9-11). `TRN-031` came off the baseline
on evidence, as M7's `TRN-072` did.

**`rust/` and `python/` are unchanged** under the D-6 freeze, touched only for the
fixture-count tripwire (250 → 253). The Python suite could not be executed in this
environment — no `pytest` module — so its corpus assertion is verified by diff and by the
.NET and Rust harnesses, which both run green at 253. It is **not** reported as green.

**The review cost is the story of this milestone, and M10 should read §5 before it starts.**
The framing record was blocked **three times**; every slice was blocked or returned at least
once; and **three separate repairs introduced a new defect while closing an old one** — the
worst being a GET form, added to satisfy a "bind both verbs" finding, that put an export
password in a query string. Two of the milestone's own rulings were wrong and were reversed
by the record itself, both because a column was read without its legend.

**Four risk rows open (R-31…R-34) and one sub-question closes.** R-27's standing instruction
— assume a third section-14-versus-catalogue contradiction until all seven `*-info` rows are
checked — is discharged: all seven are checked and there is no third. The row stays open
because the specification reconciliation is still unmade.

## Previous state (2026-09-18, **M8 complete** — all five slices landed at `0.13.0`)

**M6 and M7 are both merged.** They ran in parallel on separate branches, each was blocked
once by R3 review for the same defect class, and both cleared. Combined: **1127 tests,
99.39 % line / 96.85 % branch**, traceability **256 covered / 169 baselined of 425**, all
`auth.*` and `sys.*` fixtures green with none pending, **241 fixtures on disk** (230 at
`0.11.0`; M8a's six per-alternative recognition fixtures take it to 236, slices b and c's five
`transit.*`/`totp.*` fixtures to 241, and slices d and e's six `efficiency.*`/`kv.*` fixtures to
**247**).

**M8 is complete, all five slices, released as `0.13.0`.** Landed: **a** (the R-23
recognition-qualifier fix), **b** (Transit bindings, `TRS`), **c** (TOTP bindings, `TOT`),
**d** (the `EFF` rate-gate token bucket, `Sys.Batch`, `Kv.ReadMany`, and `KV-010` which M4
parked on `BAT-007`), **e** (`PAG` cursor pagination, `CCH` cache coherence, and section 14's
documentation guidance pulled forward into `dotnet/README.md`), and — after the milestone's
close-out, at the project owner's direction — **`CCH-006`'s `CacheWatcher`**, which M8 had
declined. **All 39 of M8's IDs are in.** Traceability **295 covered / 130 baselined** of 425;
**1348 .NET tests, 99.39 % line / 96.50 % branch**; **247 fixtures on disk**. Rust 220 and Python 491 unchanged under the D-6
freeze, touched only for the fixture-count tripwire.

**The 39th was declined and then implemented, and the reversal is worth reading.** `CCH-006`
is a `MAY`, and M8 declined it on two grounds: the `MAY`, and that a long-poll helper with
backoff is a lifecycle surface earning its own design (D-M8-44). The project owner directed it
be built anyway. **The second ground turned out to be wrong** — the design already existed here.
M2c's automatic renewal is the same problem, and `TokenRenewal` solves it by exposing
`RunAsync(CancellationToken)` and letting the caller own the task, so there is no disposal or
ownership surface to design. `CacheWatcher` follows that shape exactly, and reuses the client's
own `RetryPolicy` backoff curve rather than introducing a second one (D-M8-53…D-M8-56). Slice b's **b-2** gate, left unclosed when its
reviewer hit a session rate limit, was re-run before slice d opened and returned *approve with
required fixes* — four prose inaccuracies, no code change (D-M8-27).

**Section 14 is now implemented, and it turned out to contradict the sections that own its
endpoints — twice.** Its table prefixes all seven `*-info` routes with `/v2/` while Appendix A
gives `sys/namespaces-info` and `{mount}/roles-info` as v1 (**R-27**), and it names
`Page<NamespaceSummary>` where section 06 says `Page<Namespace>` (D-M8-5). Both were resolved
in favour of the owning section and the catalogue, and both remain **specification defects for
the project owner**, not M8's to fix. A third instance should be assumed until all seven rows
are checked.

**"Engine" in this roadmap means a set of typed REST endpoint bindings — never a
cryptographic implementation.** BastionVault calls its server-side mounts "engines" (Transit,
KV, PKI, SSH, TOTP), and this roadmap inherited the word. What an "engine milestone" ships is
the client-side binding for that mount's HTTP routes: request and response types, path
construction, error mapping and tests. **The SDK performs no cryptography of its own**
(`specifications/00-overview.md`, Purpose and Non-goals) — it sends a plaintext to the
server's `transit/encrypt` route and returns what comes back.

This is stated here because the ambiguity has already cost a milestone. M8 was halted by the
project owner, who reasonably read "implement the Transit engine" as *build an encryption
engine*. Section 08 is a table of HTTP endpoints. The same reading would recur at M9
(`PKI`, `SSH`) and M10, so the wording in §4 and §5 now says "bindings" wherever it used to
say "engines" on its own.

Sections **05 and 06 now have no unimplemented MUST in .NET**, with one stated exception:
`AUT-060`'s loopback-redirect recipe belongs to the usage guides and is M11's. **No
conformance level is declared** — sections 16–17 remain M11's, so CNF-002 still forbids the
claim (R-14, §10 question 4).

**What the two milestones cost, and what that bought.** Neither passed review first time, and
both were blocked on the *same* defect: a per-client cache keyed without the namespace, found
independently by two agents who could not see each other's work (**R-21**). Three further
findings came out of review rather than out of authorship, and each is recorded rather than
fixed in place: **R-22**, where `RES-001`'s cap and `DSC-042`'s guaranteed replay cannot both
hold at `MaxAttempts = 1`; **R-23**, where the error-catalogue generator ANDs an alternation
and silently disables five recognition rules, one of them shipped since `v0.5.0`; and the
`AUT-060` exit overclaim above. The pattern worth carrying into M8: on this milestone pair the
handback gate found every defect that mattered, and the authors found none of them.

**Five** items must close **before the Rust pass opens**, because each is a forced guess or a
known contradiction that a parity transcription would freeze into three SDKs: M6's FIDO2
completion body `{"username","credential"}`; M7b's `allowed_parameters` shape and its
policy-tests `{"cases": […]}` body; the `Page<Namespace>` / `Page<NamespaceSummary>`
contradiction between `06-system-api.md:203` and `14-batch-and-request-efficiency.md:123`,
where the owning section was followed and the specification still needs correcting (R3); and
— **added at M8** — section 14's endpoint table prefixing all seven `*-info` routes with
`/v2/` where Appendix A gives at least two of them as v1 (**R-27**). The last two share one
cause: section 14 is a cross-cutting chapter whose endpoint table was never reconciled with
the sections and the catalogue that own those endpoints, so a third instance should be
assumed until all seven rows are checked.

### 2.1 State by component

**Maintained forward, not frozen.** This table was written at M5 and has been updated
milestone by milestone since; it was headed "State as of M5" until M8's close-out, which was
wrong in the way that matters — a reader discounted current rows as historical. Where a row
still carries an older figure it says which milestone it is from.

| Area | State |
|------|-------|
| `specifications/` | Complete: 18 documents, 4 appendices, **247 fixtures on disk**, 389 requirement IDs. Appendix B carries **121** codes, unchanged since M2a minted `BV-AUTH-017` and `BV-CONFIG-011` (D-M2-16). M2c adds `clock.delay`/`clock.expectWaits` to the fixture schema (D-M2-27). M3's brief (DR-0007) mints `SYS-006` (`ClusterStatus`) and authors `sys.info.tiers`, `sys.cluster-status.ok/forbidden`. M4's brief (DR-0009 D-M4-8) authors the five missing Appendix C `kv.*` fixtures, 213→218; the 21st, `kv.read-many-fallback-on-unsupported`, is M8's. **M5 authors the six missing `resilience.*` fixtures, 218→224**, completing Appendix C line 133's list, and repairs one landed fixture whose body section 07 forbids (D-M5-26, R3-authorised — R-19) |
| `dotnet/` | Harness + M1a config + M1b transport + M1c error model + M2a authentication + M2b login/Userpass/AppID + M2c automatic renewal + M3 System API Core subset + M4 KV engine + **M5 cluster discovery and resilience**. `Client.Auth` now has `Token`/`Userpass`/`AppId`, the public `TokenSource.Login` factory, `Auth.AuthenticateAsync`, the CFG-020/ERR-022 client-side preflight, and `AutoRenewPolicy`/`RenewalEvent`/`RenewalStoppedReason` (AUT-090…095). `BastionVaultClient` is now `IDisposable`; `IClientLogger` gains `Info`. M3 adds `Client.Sys`: `Health`, `SealStatus`, `ServerInfo`, `ClusterStatus`, `CapabilitiesSelf`, `Capabilities.Can`/`Sys.CanAsync` (SYS-001,002,005,006,008,050…053; [DR-0007](decisions/0007-m3-system-api-core.md)). M4 adds `Client.Kv` with the version-explicit `Kv.V1`/`Kv.V2` sub-clients, per-environment overrides, the `KV2-030` path helpers and the `WriteIfAbsent`/`UpdateWithRetry`/`ReadField` conveniences ([DR-0009](decisions/0009-m4-kv-engine.md)), plus `dotnet/README.md`, the repo's first per-language README. M5 adds cluster discovery — `ISrvResolver`, `DiscoveryConfig`/`HealthConfig`, `Candidate`/`ProbeResult`/`NodeSelection`/`DiscoveryReport`, `Client.InputLabel`/`SelectedNode`/`ConnectAsync`/`DiscoverAsync`/`ReconnectAsync` — plus sticky sessions with the single bounded failover replay, `DSC-043`'s serialising lock and `DSC-045`'s internal node-local seam ([DR-0010](decisions/0010-m5-cluster-discovery-and-resilience.md)). **No SRV resolver ships** (R-16). **850 tests, 99.13 % line / 97.02 % branch** |
| `rust/` | Harness + M1a config + M1b transport + **M1c error model**. `ErrorCatalog`, generated codes, recognition, enrichment. **220 tests, 96.55 % line / 96.32 % region** (D-M0-14) — its shared-fixture-count assertions were updated 208→210 at M2c and then **stood still through M5, M6 and M7 while the corpus moved to 230, which left `main` red on the Rust workflow from `0.10.0` (2026-09-15) until M8a**; they now read **236**. Regenerated catalogue artefacts and those count assertions are the only things that move here during Stage 1, no behavioural change. Frozen at this state for the duration of Stage 1 (D-6) |
| `python/` | Harness + M1a config + M1b transport + **M1c error model**. `ErrorCatalog`, generated codes, recognition, enrichment. **491 tests, 98.93 % line and branch**, `mypy --strict` and `ruff` clean — its shared-fixture-count assertions carried the same stale 208→210 as Rust's, with the same consequence for the Python workflow, and now read **236**. Frozen at this state for the duration of Stage 1 (D-6) |
| `tools/traceability` (TST-041) | **Built and ratcheting.** **295 of 425 covered, 130 baselined** as of M8's close-out — M8 moved 39 IDs off the baseline: 38 across its five slices, plus `CCH-006` implemented after the milestone at the project owner's direction. Earlier movement, for the history: M5 moved 29 IDs from baselined to covered (`DSC-001`, `DSC-002`, `DSC-010`…`014`, `DSC-020`…`022`, `DSC-030`…`036`, `DSC-040`…`046`, `RES-010`, `RES-011`, `RES-020`, `RES-021`, and `CFG-043`, which had been baselined with no owner — D-M5-16a), minting none. `RES-001`…`RES-004` were already covered from M1b, so M5 landed **29 of the 33 its row booked**, not 33; `RES-030` stays baselined with **M7** as owner, because its `*ClusterWide` variants wrap `Sys.Seal`/`Sys.Unseal` (`SYS-012`/`SYS-013`, M7) — D-M5-3. `KV-001` and `KV-010` stay baselined by design (DR-0009 D-M4-2) |
| `tools/provenance` (CNF-044…CNF-046) | **Built.** `specifications/provenance.json` pins **35 upstream sources** at baseline `v0.42.0` — 12 `authoritative` documents that can block a release, and 23 `corroborating` sources that cannot (21 crate trees plus two documents demoted because their signal is noisy — sensitivity is an explicit judgement, not a function of file type) — each naming the specification documents it feeds. `--check` reports drift grouped by specification document, `--verify-local` proves the same manifest validates offline, and `unknown` (upstream unreachable) exits non-zero rather than reading as clean. 18 tests. CI runs it non-blocking; the binding use is release-checklist item 6. See **R-20** for the open reconciliation backlog ([DR-0011](decisions/0011-specification-provenance-tracking.md)) |
| `tools/error-catalogue` | **Appendix B is executable.** Parses §1 and §2 into `catalogue.json` and emits **121 codes**, 127 recognition rules and the code constants for all three languages, plus 124 fixtures. Unchanged by M2c. Regeneration is a CI gate, proven by seeded violation ([DR-0005](decisions/0005-m1c-error-model.md) D-M1c-1) and re-proven at the R-10 sweep ([DR-0001](decisions/0001-m0-harness-gate-proof.md) addendum, Row 15) |
| Fixture driver operation registry | `Client.Construct` plus the five `Logical.*` operations in all three, **plus all `Auth.*`, `Sys.*`, `Kv.*` and (M5) `Client.Connect`/`Discover`/`Reconnect`/`Classify` operations in .NET only**, **plus all `Transit.*` and `Totp.*` operations (M8 b and c) and `Sys.Batch`, `Kv.ReadMany`, `Sys.ListNamespacesInfoAll` and `Sys.CacheVersion` (M8 d and e)**. **247 fixtures on disk**; all 18 transport fixtures pass ×3; in .NET **all 140 `errors.*`**, **all 21 `auth.*`** (M6 emptied the pending list), **all 24 `sys.*`** (M7), **all 10 `resilience.*`** (M5), **all 5 `transit.*`** and **all 3 `totp.*`** (M8 b and c) and **all 21 `kv.*`** (M8 d authored `kv.read-many-fallback-on-unsupported` and drove `kv.read-many-batch`, emptying that pending list) pass. **`efficiency.*` is 7 of 8** — the eighth, `efficiency.pagination.zip-mismatch-protocol-error`, drives `Pki.ListCertificatesInfo` and is **owned by M9** (D-M8-48); re-pointing it at an endpoint that exists would have been a `FIX-012` specification change, not a test fix. *(The auth, error and `kv` figures in this row were stale from before M6, M7 and M8a until 2026-09-18; the count and the narrative are now taken from one place.)* `errors.enrichment.404-kv2-hint` is re-booked from M4 to **M7** and needs re-authoring — it names a `Kv.V2.*` operation on a path with no `data/` segment, which section 07 makes impossible (DR-0009 D-M4-14) |
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
| **M6** ✅ | Authentication — remaining methods | `AUT` (FerroGate, Certificate, OIDC/SAML, FIDO2) | ~12 booked, **9 landed** | Large | R3 | **1 done** | **Met, with one stated exception.** All 21 `auth.*` fixtures green — the pending list is empty for the first time since M2a — and 9 IDs off the baseline. Section 05 has no unimplemented MUST **except `AUT-060`'s usage-guide recipe, which is M11's**; the milestone's first exit claim omitted that qualification and review caught it. Blocked once on R-21's `AUT-051` cache and cleared. Also closes **R-18** with the **R-22** residual named ([DR-0011](decisions/0011-m6-authentication-remainder.md)) |
| **M7** ✅ | System API — remainder | `SYS` (init/seal/unseal, mounts, auth methods, policies, namespaces, audit, backup/restore) + `RES-030` | 27 booked, **30 landed** (+`KV-001`, `TRN-071`, `TRN-072`) | Large | R3 | **3 done** (slices a, b, c) | **Met.** All 24 `sys.*` fixtures green with none pending, and Appendix C line 123's list has no gap. Ran as three slices because 27 IDs exceeds one Large brief (TOK-011); slice a was blocked on R-21's `SYS-026` cache and cleared. Landed three IDs more than booked: `KV-001`, stuck since M4 waiting on `SYS-026`, and `TRN-071`/`TRN-072`, cleared on evidence — `TRN-072` had no test asserting it anywhere in the tree until M7b wrote one ([DR-0012](decisions/0012-m7-system-api-remainder.md)) |
| **M8** ✅ | Transit, TOTP and request-efficiency bindings | `TRS`, `TOT`, `BAT`, `PAG`, `CCH`, `EFF` (+`KV-010`) | 39 booked, **39 landed** | Enterprise | R3 | **1 done** | **Met on requirement content; the booked gate was unsatisfiable.** All five slices in, then `CCH-006` after the close-out at the project owner's direction: **39 of 39** IDs off the baseline (158→130 across the milestone), 1348 .NET tests at 99.39 % line / 96.50 % branch, 247 fixtures. **No conformance level declared:** the booked exit "declare `Standard`" is forbidden by `CNF-002` while sections 16–17 are M11's (**R-14**), so `dotnet/README.md`'s gap list is updated instead, as M4 did. Resequencing is §10 question 4, untaken. Every slice was gated and **none passed first time** — see §5 for what the five gates found ([DR-0013](decisions/0013-m8-transit-totp-and-efficiency.md), D-M8-1…D-M8-52) |
| **M9** ✅ | PKI and SSH endpoint bindings | `PKI`, `SSH`, `SSB` (+`TRN-031`) | 11 booked, **11 landed** (10 of the 11 booked, `PKI-030` held back; `TRN-031` cleared on evidence) | Large | R2 | **4 done** (slices a–d) | **Met.** Sections 09 and 10 are bound in .NET — roughly 91 operations across `Client.Pki`, `Client.Pki.Acme`, `Client.Pki.Csr`, `Client.Pki.SignRequests`, `Client.Ssh` and `Client.SshBroker` — with all seven Appendix C `pki.*`/`ssh.*`/`sshbroker.*` fixtures green and the corpus at **253**. **`PKI-030` is held back, not missed**: its queue-cap limb needs a server message string no document states, so the ID stays baselined rather than reporting half a requirement as covered (D-M9-11, **R-31**). `TRN-031` came off the baseline on evidence, as M7's `TRN-072` did. **The framing record was blocked three times before acceptance and every slice was blocked at least once** — see §5. No conformance level declared: section 09 still carries `PKI-030`, and sections 16–17 are M11's (**R-14**) ([DR-0016](decisions/0016-m9-pki-and-ssh.md), D-M9-1…D-M9-30) |
| **M10** | Remaining engine bindings and identity → **declare Complete** | `IDN`, `RSC`, `FIL`, `LDP`, `RUS` | 9 | Large | R2 | **1** | **Conformance level `Complete` declared in .NET (CNF-003 satisfied)** — subject to R-14 being answered. **Inherited from M8:** **`Auth.Userpass.ListUsersInfo`** ships under section 14's name while Appendix A nests it as `Auth.Userpass.Admin.*`; M10 builds the rest of that surface and must decide whether the member moves — cheap now, breaking once published (**R-29**, D-M8-46) |
| **M11** | Documentation and usage guides | `DOC` | 21 | Large | R1 | **1** | Every .NET doc sample compiles/runs (CNF-026); documents R-26's macOS scoped-resolver caveat and the `DSC-050` nameserver override |
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
engine binding. Budget explicit design time before any code.

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

### M8 — Transit, TOTP and request-efficiency bindings ✅

Bindings for the Transit routes — keys, encrypt/decrypt, rewrap, sign/verify, HMAC, random,
hash, datakeys — and for TOTP; plus all of section 14: the client rate gate, the batch
endpoint, `*-info` cursor pagination and `sys/cache/version` coherence epochs. **No
cryptography is performed in the SDK**; Transit is a set of HTTP routes (see §2's definition
of "engine"). Section 14 mandates its own documentation guidance (14 § "Guidance the SDK
documentation MUST include"), so that slice of M11 was pulled forward into this milestone and
is in `dotnet/README.md`.

Ran as five slices, because 39 IDs exceeds one Large brief (**TOK-011**): **a** the R-23
recognition-qualifier fix, **b** Transit, **c** TOTP, **d** the rate gate + `Sys.Batch` +
`Kv.ReadMany`, **e** pagination + cache coherence + the guidance. Rulings:
[DR-0013](decisions/0013-m8-transit-totp-and-efficiency.md), D-M8-1…D-M8-52.

**Exit, as met:** all 39 of the milestone's IDs landed — 38 across the five slices, and
`CCH-006` after the close-out when the project owner directed that the declined `MAY` be built
after all (D-M8-53…D-M8-56).

**No conformance level is declared, and the booked gate was unsatisfiable as written.** §4
booked M8's exit as "declare `Standard`"; `CNF-002` forbids claiming a level whose sections
carry unimplemented MUSTs, and sections 16–17 are M11's (**R-14**). M8 therefore exits on
requirement content and updates `dotnet/README.md`'s `CNF-002` gap list instead, exactly as M4
did (D-M8-6, DR-0009 D-M4-3). Resequencing remains §10 question 4 — a project-owner decision
this milestone did not take.

**What review cost, and what it bought.** Every slice was gated in the Strategic tree and
**not one passed first time.** The gates found: a false unreachability claim in an R3 decision
record, falsified by a passing test in a sibling slice (D-M8-22, D-M8-27); a rate gate that let
exactly one request out *into* a server ban window (D-M8-43); a guessed public API shape that
would have silently returned `0` (D-M8-47); an unverified cross-language claim in shipped source
(D-M8-35); and an unbounded paging loop (D-M8-50). **The pattern worth carrying to M9: five
times, an author was right about what to do and wrong about why — and every one was caught by
checking the artefact rather than the sentence describing it.**

### M9 — PKI, SSH and the SSH broker ✅

Typed bindings for the PKI routes — CA management, roles, issue/sign, revoke, CRL, bulk
listings, managed keys, tidy, ACME config, and both queues — and for the SSH routes — CA,
roles, signing, OTP, brokering policy. **No certificate is signed, and no key is generated,
inside the SDK**: `Pki.Issue` posts to the server's issue route and returns the certificate
the server minted (D-M9-1).

Four slices, dispatched serially because all four regenerate one `PublicApiSurface.txt`.

**What the gates found, and why this milestone is worth reading before M10 starts.** The
framing record was **blocked three times** before acceptance, and **every slice was blocked
or returned at least once**. Almost every finding came from reading an artefact against a
document rather than from reading the code's own account of itself:

- **Twice, secret material reached a container that does not redact.** `Pki.ExportCertificate`
  returned an untyped map although its own `includePrivateKey` and `mode=backup` parameters
  guarantee a private key — while `PKI-002` was coming off the baseline in the same diff, and
  its `PKI-002`-tagged test drove exactly that path and asserted only non-null. **The repair
  then introduced a worse leak than it closed**: the GET form it added put the export password
  in a query string, which `ErrorPaths.Redact` does not cover (**R-33**), so it reached the
  request observer, the exception path and hint enrichment. Both export routes are POST-only
  as a result (D-M9-16, D-M9-19, D-M9-22).
- **A privilege-escalation path in an accepted design.** `SignRequests.Approve` splatted the
  caller's untyped `overrides` map beside the named `role` field, and `Utf8JsonWriter` does not
  reject duplicate property names — so `overrides["role"]` emitted two `role` keys and every
  mainstream decoder takes the last, on the route that authorises an issuance (D-M9-24).
- **A wire-format defect found in the specification's punctuation.** Three durations went out
  as integer seconds where their endpoints declare Go-style strings; §09 **quotes** exactly
  those three defaults and leaves `not_before_duration` (30) unquoted, and two of the three are
  the mount `config` case `TRN-031` names. The read half accepted only JSON numbers, so a
  read-modify-write of `config/crl` would have silently reverted the CRL expiry.
- **A false explanation of a coverage figure.** Slice d attributed a drop to a uniform coverlet
  async-state-machine artefact; the file it blamed contains no `async` method at all, and all
  the misses were its own untested arms. The margin was **2 branch outcomes**; it is now 31.
- **A requirement tag that asserts something else, in all four slices** — found by four
  separate gates, now the standing rule **D-M9-30**. `traceability.py` machine-reads those
  tags, so a mis-tag feeds the gate a false positive.

**Two of the milestone's own rulings were wrong and were reversed by the record itself.** The
`*-info` prefix question was decided, twice, against Appendix A's legend at `:3-5` — which
defines `v1` as "uses `ApiPrefix`, do not pin" rather than "lives at `/v1`" — and against
R-27 and D-M8-45, which had already settled it at M8. A defect was booked against
`Sys.ListNamespacesInfo`, which is correct as shipped; the booking was withdrawn (D-M9-7,
D-M9-13). **Both errors had one cause: a column read without its legend.**

**Exit:** sections 09–10 bound, seven fixtures green, corpus 253, 10 of 11 booked IDs off the
baseline plus `TRN-031`. **No conformance level declared** — section 09 still carries
`PKI-030` (**R-31**), and sections 16–17 are M11's (**R-14**).

### M10 — the remaining engine bindings and identity → **Complete**

Identity, asset groups, Resources, Files, LDAP, cert lifecycle, notifications, Rustion.

**M10 inherits four rows from M9, and three of them need input this repository does not
hold** — the server's queue-cap message (**R-31**), the PKI queue response shapes and the
`exportable` read-route question (**R-32**), and `Pki.GenerateIntermediate`'s `issuer_name`.
Those belong in M10's planning input, not its dispatch: a capture or the server source is a
dependency outside the SDK's control. **R-33** gates any return of the two export routes'
GET forms.

**Inherited from M8, and neither is optional bookkeeping.** M9 takes
`efficiency.pagination.zip-mismatch-protocol-error`, which sits on disk **pending** because it
drives `Pki.ListCertificatesInfo` — an area M8 could not build, so re-pointing the fixture
would have been a `FIX-012` specification change rather than a test fix (D-M8-48). It goes
green with the PKI bindings. M10 takes the **`Auth.Userpass.ListUsersInfo` naming** question (**R-29**, D-M8-46).
`CCH-006`, which M8 had handed it, is no longer M10's — it was implemented after the close-out. Both M9 and M10 reuse M8's `PagingWire` machinery for the five
`*-info` endpoints M8 left unwired, rather than re-implementing cursor paging (D-M8-7).

These are written into the milestone rows and here, not only into
[DR-0013](decisions/0013-m8-transit-totp-and-efficiency.md), because **R-16 is this roadmap's
own evidence that a gap recorded somewhere a briefer will not look survives a milestone** — it
was raised at M5 and reached M8 untouched.

**M10 exit:** `Complete` declared in the .NET README — CNF-003 satisfied for .NET, **subject to
R-14**, which as sequenced forbids the claim. This is **Stage 1's conformance target**; Rust and
Python reach `Complete` only inside M13.

### M11 — Documentation and usage guides

Section 16 requirements plus the guides of section 17, for .NET (Stage 1). Every sample
compiles and runs in CI (CNF-026); every public symbol carries a doc comment. Rust and
Python guides are authored in M13 once each language's surface is implemented, not adapted
speculatively ahead of it.

M11 also carries **R-26**: the documentation MUST state that default SRV resolution cannot see
macOS scoped resolvers, so on a split-horizon VPN the resolver queries the wrong nameserver and
`DSC-017`'s strict default refuses rather than degrades, and MUST document the `DSC-050`
nameserver override as the remedy. This replaces R-16's original M11 obligation — "a resolver is
required for real discovery" — which stops being true once one ships.

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

**R-16 no longer constrains M12.** The live suite previously could not reach a cluster without
an application-supplied resolver; [DR-0014](decisions/0014-r16-srv-resolver-and-silent-discovery-degradation.md)
ships one, so cluster scenarios need no fixture substitute. M12's R3 human-confirmation gate is
unchanged, and it still carries D-M5-26's `resilience.failover.read-once` confirmation — that is a
separate R3 carry-forward which DR-0014 does not touch, so "R-16 no longer constrains M12" must
not be read as M12's gate having loosened. It has not.

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
                             │                             ├──▶ M8 ✅ ═══ STANDARD ⛔ (R-14)
                             │                             │
                             └─────────────────────────────┴──▶ M9 ──▶ M10 ═══ COMPLETE ⛔ (R-14)
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
| R-14 | **The conformance-level declaration schedule is unsatisfiable, and three milestone gates are stated in terms of it.** `Core` (M4), `Standard` (M8) and `Complete` (M10) each require sections 16–17, which are M11's. Found at M4 by grounding the gate against CNF-001/CNF-002 rather than against the KV work | R2 | **Open — project-owner decision** (§10 question 4): move M11 ahead of M8, or move all three declarations to the end. Meanwhile the control is honesty, not a claim: `dotnet/README.md` declares no level and lists the gaps by requirement ID, so no release can imply a conformance level it does not hold. **Hit a second time at M8 (2026-09-18), exactly as predicted at M4** — M8's booked exit was "declare `Standard`", it exited declaring nothing, and the gap list was regenerated instead (D-M8-6). **That is two of the three blocked gates now spent**, and the third is M10's `Complete`. The row has therefore stopped being a forecast and become a measurement: the question has cost two milestones their stated exit criterion, and answering it is cheaper than a third. Evidence: [DR-0009](decisions/0009-m4-kv-engine.md) D-M4-3, [DR-0013](decisions/0013-m8-transit-totp-and-efficiency.md) D-M8-6 |
| R-15 | ~~**A gate that passes only on the CI matrix's single version.**~~ **Closed 2026-09-15.** Python's suite had 12 failures under Python 3.14 and none under 3.12: all three in-process mock servers issued CA and leaf certificates with no Subject Key Identifier and no Authority Key Identifier, which OpenSSL 3.5+ rejects during chain verification. CI pinned 3.12, so it was green on a harness that did not work. This is the R-10 shape one layer out — the gate is green because of what CI does not run. Found at M4 by running the Python suite locally while verifying an unrelated fixture-count change | — | Closed — both extensions are now issued in .NET, Rust and Python, `KeyUsage` is explicit, and `python.yml` runs a 3.12 **and** 3.14 matrix so a version-only failure cannot hide again. Python 484/484 green on 3.14 (was 472 passed / 12 failed), .NET 741/741, Rust 219/219. The Stage-1 freeze (D-1/D-6) does not cover a test harness that does not run; `rust/` and `python/` library code is untouched |
| R-16 | **Cluster discovery ships inert** — no `ISrvResolver` implementation ships, so an application that supplies none takes `DSC-011`'s "no records" path and gets literal single-address behaviour where it asked for discovery, silently; `DSC-042` also leaves failover unarmed at one candidate, so it loses fail detection too. **Framed and ruled by [DR-0014](decisions/0014-r16-srv-resolver-and-silent-discovery-degradation.md); re-tiered R2 → R3 under `CRS-004`** | **R3** | **No longer M11/M12's.** The project owner ruled a hand-rolled zero-dependency SRV resolver in core plus the `DSC-015`…`DSC-019` loudness contract, with strict mode **defaulting to strict**. Closes on implementation. **The M8 blocker is discharged (2026-09-21): `0.13.0` merged at `e44ce2a`**, so `DSC-019`'s error-catalogue regeneration no longer collides with M8a's uncommitted generator change, and the seam slice d was expected to provide now exists — `EgressKind { Request, DiscoveryProbe, SrvResolution }`, where **`SrvResolution` is already a live, exercised call site**, so a shipped resolver's DNS I/O has a named exemption waiting for it rather than an un-gated path someone must justify later (D-M8-25, D-M8-26, D-M8-30). Implementation is now unblocked and unscheduled. The `specifications/` §13 edit carries §5.3's R3 human confirmation before release. M11 retains only the narrowed documentation obligation in D-R16-7. **The lesson this row is itself the evidence for:** a gap booked with no owner survives a milestone — it was raised at M5 and reached M8 untouched | 
| R-17 | **`DSC-033` cannot prefer healthy nodes without a `specifications/` change.** `DSC-022` says surfacing `cluster_healthy` lets ranking "prefer healthy nodes as a tiebreak after RTT", but `DSC-033`'s rank list is closed, exhaustive and explicitly deterministic, and does not contain it. At equal RTT and weight an unhealthy node can therefore be picked over a healthy one on the lexical-URL tiebreak | R3 | **Open, unowned.** Ruled surfacing-only at M5 (D-M5-20): adding a fifth sort key would widen a list the specification closes and would diverge from any Stage 2 implementation reading `DSC-033` literally. Changing it is a `specifications/` edit, R3 by CRS-004. The control meanwhile is that the behaviour is pinned and fixture-asserted, so all three languages will at least be wrong identically |
| R-18 | **`AUT-003`'s relogin replay can exceed `RES-001`'s attempt cap on its own.** D-M2-9 deliberately gives the relogin replay a fresh `MaxAttempts`, which is correct for `AUT-003` but means `AttemptsBefore` can reach `2 × MaxAttempts` with no failover involved. `RES-001`'s cap is written about the failover replay and says nothing about a relogin replay, so the two accepted rulings are in tension | R2 | **Open, owned by M6**, where `AUT-003`'s replay is actually exercised. Not M5's to resolve: it predates the milestone and fixing it means reopening an accepted M2 ruling (R3). M5 guarantees only that *failover* never causes the cap to be exceeded — D-M5-28's clamp is what stops the two mechanisms compounding multiplicatively. Evidence: [DR-0010](decisions/0010-m5-cluster-discovery-and-resilience.md) D-M5-29 |
| R-19 | **Fixtures can encode response bodies their own specification section forbids, and pass.** M5 found two instances of one defect class: `resilience.failover.read-once` (landed since before M4) and `resilience.backoff.math-seeded` (authored *inside* M5, one addendum after the rule against it) both returned KV v2 metadata without `created_time`, which section 07 declares non-optional and D-M4-12 maps to `BV-PROTOCOL-002`. Both passed for incidental reasons — the first because the reader ran before M4 existed, the second because `Logical.Read` never invokes the KV v2 reader | R2 | **Open, unowned; a sweep is §10 question 5.** Two instances in one milestone implies more across the 224-fixture corpus, and each is a latent Stage 2 trap: Rust and Python must reproduce these bodies exactly, and will fail on the ones whose reader they implement. Mechanical Engineering-tree work — validate every fixture body against its section's type block — but it needs a milestone slot before M13, not an ad-hoc pass. Evidence: [DR-0010](decisions/0010-m5-cluster-discovery-and-resilience.md) D-M5-26, D-M5-30 |
| R-20 | **`specifications/` is derived from a server that moves independently, and until now nothing tracked the link.** The specification is dated 2026-09-13 and declares a `≥ 0.42` floor; upstream `ffquintella/BastionVault` was already at `v0.44.4` on 2026-09-15. The gap was real and undetectable: `docs/api.md` had gained the `<list>-info` and `sys/cache/version` sections that `14-batch-and-request-efficiency.md` exists to specify, and no mechanism in this repository could surface that. Everything downstream inherits it — an SDK can be perfectly conformant to a specification that is itself stale | **R3** | `specifications/provenance.json` pins the upstream ref and the git object id of all 35 sources feeding the specification, and `tools/provenance` reports drift grouped by the specification document needing review ([DR-0011](decisions/0011-specification-provenance-tracking.md), CNF-044…CNF-047). Baseline deliberately pinned at the declared `v0.42.0` floor, so nothing is assumed reviewed. CI runs non-blocking (an upstream release must not redden an unrelated PR); the binding use is release-checklist item 6, which forbids releasing with unreconciled `authoritative` drift. **Open backlog: the first report names 8 specification documents** — `03`, `04`, `05`, `06`, `12`, `14`, Appendix A, Appendix B — which is now visible, enumerable work rather than an unknown |
| R-21 | **A per-client cache keyed without the effective namespace serves one tenant's answer to another.** Two independent Engineering-tree agents produced this same defect in one milestone pair, in isolated worktrees, neither aware of the other: M7a's `SYS-026` mount-type cache (keyed on the `activeNamespace` field while the request went out under `options.Namespace ?? activeNamespace`, poisoning and cross-reading across tenants and feeding a wrong `KvVersion` to `KV-001`) and M6's `AUT-051` machine-identity cache (keyed on mount alone, on a `ClientContext` shared across `WithNamespace` views, with no TTL — so permanent, and failing **open**). Both were caught by R3 handback review, neither by the author, and neither by any test | **R3** | **M7a's is fixed on `m7-sys-remainder` and unverified; M6's is unfixed on `m6-auth-remainder`.** The generalisation is the point: namespace is a request-scoping dimension (`AUT-041`, `CFG-041`) and every memoised value on `ClientContext` must include the effective namespace in its key. A tree-wide audit of every cache and memoised value was briefed into both fix passes and neither completed it — **it is the first thing to finish when work resumes**. **The tree-wide audit is now complete** (2026-09-18, read-only survey of `main` and both branches) and found **no third instance** — every other retained value on `ClientContext` is either keyed correctly or genuinely namespace-invariant. `SYS-026`'s fix is confirmed complete at all four call sites, using textually the same expression as `RequestExecutor.EffectiveNamespace` so key and wire header cannot disagree. Two residues: `ClientContext.TokenInfo` is judged safe only on the strength of `05-authentication.md:38` (a namespace-mismatched request is gated to 403 rather than answered with another namespace's data), which is a server-behaviour claim this repository cannot verify from its own source; and six of the seven safe verdicts rest on reasoning rather than on a test that would fail if they were wrong. **The inspection rule, which catches both known instances without running anything:** a value fetched by a namespace-header request and stored on `ClientContext` — which is what `WithNamespace` views share — must be keyed by exactly `(options.Namespace ?? activeNamespace).TrimEnd('/')`, never by `activeNamespace` alone, the view's namespace, or the mount alone; and where a correct key helper exists, verify it is actually *called*, because an unused correct helper reads as a landed fix in review when it is not one |
| R-22 | **`RES-001`'s attempt cap and `DSC-042`'s guaranteed failover replay cannot both hold at `MaxAttempts = 1`.** M6 implemented R-18's Option A clamp on the relogin replay, matching D-M5-28's clamp on the failover replay. Both carry a `Math.Max(1, …)` floor, and the floors **stack**: a call that fires both replays reaches `MaxAttempts + 2`, one over `RES-001`'s unqualified "total attempts". Measured at handback review against `FailoverUnitTests.A_failover_replay_does_not_mint_a_second_re_login` with `MaxAttempts = 1` (cap 2), observed `Attempts` **3** | R2 | **Open, unowned.** Not a regression — the pre-clamp code produced the same 3 at that setting — and Option A still strictly improves the single-mechanism case (7 → 4 in DR-0011's worked example), so R-18 is **closed with this residual named**, not closed outright. The floor must stay: removing it breaches `DSC-042`'s MUST that the failover replay happen at least once. This is therefore a tension between two requirements rather than a defect in either clamp, and resolving it means amending `RES-001` to say what it means by "total" when two bounded replays compose — an R3 `specifications/` change, Architect queue. Carried into the Rust and Python briefs so no parity pass transcribes DR-0011's original, false "composes with no interaction" claim. Evidence: [DR-0011](decisions/0011-m6-authentication-remainder.md) D-M6-21 |
| R-23 | **`tools/error-catalogue` compiled Appendix B §2's `+ a/b/c` form as a conjunction where the appendix means alternation, so five recognition rules could never fire.** `/` is alternation everywhere else in the §2 table, but the generator emitted the alternatives following a `+` into `RecognitionRule.ContainsAll` — "extra substrings the message must **also** contain". Row 283 settled the intent past argument: a message cannot be both `is below min_decryption_version` and `not found on key`. Affected: **`BV-INPUT-102`** (SYS-042, landed M7b), **`BV-INPUT-103`** (SYS-091, landed M7c), **`BV-AUTH-011`** (machine binding — landed in M2 and **shipped since `v0.5.0`**), `BV-TRANSIT-004` and `BV-SSH-005` | **R3** | **Closed at M8a (2026-09-18), with two of its own statements corrected.** The generator now compiles a qualifier group's items as alternatives into a new `containsAny` slot in all three languages, and M7c's `RestoreAsync` remap (D-M7-36) deleted with the defect, as designed. Consequences (3) and (4) are discharged with it: M8 and M10 no longer inherit a latent defect each, and the corpus no longer masks the bug — the generator emits **one fixture per alternative** (124→130 generated, corpus 230→236), so each affected rule has a fixture the pre-fix generator fails. Eleven do, and the whole pre-change corpus replays against the new table with zero regressions. **Two corrections to this row's original text, both found at the handback gate.** (a) It claimed `BV-SERVER-005` is retryable and that a corrupt-restore rejection therefore looked retryable; `appendix-b-error-catalogue.md:138` reads `R = no`, and every code on every side of the change is non-retryable — the blast radius was an error **code** change, never a retryability one, and the claim had already propagated into a downstream brief. (b) It quoted `errors.recognition.bv-input-102.1` as answering `namespace refuse cross-namespace`; that fixture now answers `namespace refuse`, with `.2` carrying the other alternative. The R3 tier was not lowered, but it rests on a different limb of CRS-004 than first recorded: no workflow publishes to a package registry (`build-artifacts.yml` builds, never pushes), so `v0.5.0` and its successors are repository tags rather than released packages — the limb that bites is the `specifications/` one, since the slice edited Appendix C. Evidence: [DR-0013](decisions/0013-m8-transit-totp-and-efficiency.md) D-M8-2, D-M8-3, D-M8-8…D-M8-13 |
| R-24 | **Rust does not discharge `FIX-001`: its fixture "validation" validates nothing.** `rust/bastionvault-integration-sdk/tests/harness/fixture.rs:227-236` loads `schema/fixture.schema.json` — but only to locate the repository root — and then checks solely that the fixture's root is a JSON object. It never validates the document against the schema. Python uses `Draft202012Validator` and .NET a shared `JsonSchema`; Rust has a stub. `FIX-001` (`appendix-c-conformance-fixtures.md:65`) is a **MUST**: every fixture MUST validate against the schema | **R2** | **Open, unowned. Verified at source, not inferred.** Four Rust call sites (`fixture_harness.rs:25`, `:60`, `error_fixtures.rs:33`, `transport_fixtures.rs:17`) carry the message `all repository fixtures must validate` against an implementation that does not, so **Rust's 220 green tests prove less than they appear to** — a malformed fixture that Python and .NET would reject passes in Rust. This is R-19's and R-23's shape a third time, now in the harness rather than in a fixture or a generator: a check that passes for a reason unrelated to what it claims. **Consequences.** (1) Any parity evidence resting on "all three languages validate the corpus" is overstated for Rust, including in earlier milestone records. (2) The exposure grows at **Stage 2**, when Rust starts consuming the whole corpus in earnest rather than the transport and error subsets. (3) It compounds the three-language exclusion-rule divergence in the count-derivation work (R-25): Rust both under-validates and under-counts by a different rule than the other two. **Not fixed in M8**: `rust/` is frozen for Stage 1 (D-6), the defect predates this milestone and is no part of its content, and folding it in would give one defect two owners (CLA-008). Found by the count-derivation session's architecture review, referred rather than folded in — the correct call — and verified here before recording | 
| R-25 | **The conformance-fixture corpus count is hand-transcribed into three languages, and the three fixture loaders exclude non-fixture JSON by three different rules.** The count went stale twice and left `main` red on the Rust and Python workflows from `0.10.0` (2026-09-15) until M8a. Separately, Python excludes any path with a `schema` directory component, .NET excludes exactly `<root>/schema/fixture.schema.json` by full path, and Rust excludes any file *named* `fixture.schema.json` anywhere; they agree today only because exactly one such file exists at exactly that path | **R2** | **Open, owned by the count-derivation session** (`decisions/0015-fixture-corpus-count-derivation.md`, proposed). The count half is a generated, committed manifest outside `specifications/fixtures/`, read by all three harnesses and gated by regenerate-and-diff — the shape the error catalogue already uses (D-M1c-1). The exclusion half is latent, not active: a *second* JSON file under `specifications/fixtures/schema/` (a schema revision beside the current one is the ordinary way it happens) would be dropped by Python and counted by .NET and Rust, so **the three languages would disagree on which way the count moved** — which no single assertion can express, and which would present as an unexplainable red in two of three workflows. **Note for any later reader:** the count assertion is *unrequired* defence-in-depth. `TST-010` requires fixtures be loaded from the repository rather than copied and `FIX-001` requires schema validation; neither mandates a count, and `specifications/` states no corpus count anywhere. Do not cite either as requiring it. M8's slices are briefed not to add a non-fixture JSON under `specifications/fixtures/`, which keeps this latent for the milestone's duration. **The count half stopped being a prediction at M9's merge.** M9 updated the three transcription sites it knew about (250 → 253); a **fourth** — `rust/bastionvault-integration-sdk/tests/fixture_instruments.rs:156` — was added on `main` by the M2a parity pass while M9 was in flight, and went red the moment the two branches met. Neither branch was careless: each updated every site it could see. That is the failure this row predicts, demonstrated **across branches** rather than within one tree, and it is an argument the generated manifest cannot come soon enough — a branch cannot enumerate transcription sites that do not exist yet on its own base | 
| R-26 | **The shipped SRV resolver cannot see macOS scoped resolvers.** `GetIPProperties().DnsAddresses` was *measured* returning one identical global list on all 27 interfaces, including down ones, which collapses macOS's scoped resolvers — so on a split-horizon VPN the resolver queries the wrong nameserver and gets NXDOMAIN. Linux and Windows resolution is in-platform and unaffected | R1 | **Accepted knowingly** ([DR-0014](decisions/0014-r16-srv-resolver-and-silent-discovery-degradation.md) D-R16-7), not discovered late: under `DSC-017`'s strict default the failure is **loud rather than silent**, and D-R16-6 makes the nameserver list an injected input, so an operator has a remedy. Documentation obligation held by **M11**. Strictly narrower than the R-16 it replaces — a diagnosable failure in one environment instead of an undetectable one in all of them | 
| R-27 | **Section 14's endpoint table writes `/v2/` uniformly and contradicts the endpoint catalogue.** `14-batch-and-request-efficiency.md:98-107` prefixes all seven `*-info` rows with `/v2/`, but `appendix-a-endpoint-catalogue.md` gives `Sys.ListNamespacesInfo` as **v1** (`:40`) and `Ssh.ListRolesInfo` as **v1** (`:221`), while agreeing on v2 for `Sys.CacheVersion` (`:43`) and "v2 recommended" for `ListUsersInfo` (`:87`). Found at M8e when the slice pinned the two new routes to `/v2` per section 14 and had to leave the already-shipped `sys/namespaces-info` on `/v1` to match its accepted fixture — the apparent inconsistency is the specification's, not the SDK's | **R3** (the `specifications/` limb of CRS-004; **not** the published-artefact limb, which is vacuous — `build-artifacts.yml` builds and never pushes) | **Open — project-owner decision.** M8 shipped every pin matching Appendix A, which is also what D-M8-5 ruled for the `Page<Namespace>` contradiction: the owning section and the catalogue win over section 14's cross-cutting table. **This is the second place section 14 disagrees with the section that owns the endpoint, and that is the actual finding** — section 14 was written as a cross-cutting chapter and its endpoint table was never reconciled with Appendix A, so a third instance should be assumed until someone checks all seven rows. Resolving it is a `specifications/` edit. Until then the control is that .NET follows the catalogue and the fixtures pin it, so Stage 2 will transcribe the same choice rather than diverge. **The row's own instruction — "a third instance should be assumed until someone checks all seven rows" — is now discharged at M9: all seven were checked and there is no third.** `certs-info`, `csr-info` and `sign-request-info` carry no Prefix column at all (Appendix A's PKI table has none), `roles-info` and `namespaces-info` read `v1`, `users-info` reads "v2 recommended", `targets-info` is M10's. The disagreement is uniform and is section 14's alone. M9 also recovered the reading that makes the rows consistent: **Appendix A's legend (`:3-5`) defines `v1` as "uses `ApiPrefix`, both serve it" — an instruction *not to pin*, not a claim that the route lives at `/v1`** — so the catalogue and section 14 were never in conflict about *where* a route is, only about whether the SDK pins. **Two independent agents misread that legend in the same session (2026-09-21) and each booked a specification correction against a document that is not wrong**, which is why the legend is quoted here rather than cited. The row stays open: the §14 reconciliation is still unmade. Evidence: [DR-0013](decisions/0013-m8-transit-totp-and-efficiency.md) D-M8-45, [DR-0016](decisions/0016-m9-pki-and-ssh.md) D-M9-7 |
| R-28 | **The client rate gate is .NET-only, and one of its semantics contradicts shipped Rust.** M8d landed the `EFF-001`…`EFF-006` token bucket in .NET alone (D-1/D-6 freeze Stage 2). Four behaviours the parity pass must match or consciously overturn: (a) **a pause is never shortened by a nearer one** — .NET and Python take the maximum, but `rust/…/rate.rs:42` assigns `paused_until = Some(now + bounded)` **unconditionally**, so a second `429` carrying a shorter `Retry-After` moves Rust's resume instant *backwards*; (b) exactly one reservation is grantable at the pause end, not a full burst; (c) **a pause holds a waiter that was already in the queue** — the leak M8d's own R3 gate found, where a `429` arriving mid-sleep let one request out *into the ban window*; (d) a disabled gate reports a maximum sentinel, and what crosses languages is the invariant `AvailableTokens > 0` means "may proceed", never the literal `int.MaxValue`. **(a) is a pre-existing Rust defect this milestone exposed, not one it created** — two of three languages already agreed. **(c) is a behavioural MUST, not a refinement:** whichever language implements the bucket next will reproduce the leak if it reserves before sleeping | R2 | **Open, owned by M13.** Cannot be fixed now — `rust/` and `python/` are frozen for Stage 1 and this is library code, not a harness gap. Carried here rather than in the decision record alone so the M13 brief inherits it as behaviour-to-avoid, which is the mechanism D-2 credits with stopping M1a's defects from recurring. Related: `EFF-002`'s FIFO ordering is **not expressible in the shared fixture corpus** (a fixture drives one operation and the harness transport is sequential), so each language needs its own concurrency test; pinning it portably would need a concurrency primitive in the fixture schema, which is a `specifications/` change. Evidence: [DR-0013](decisions/0013-m8-transit-totp-and-efficiency.md) D-M8-33, D-M8-34, D-M8-42, D-M8-43 |
| R-29 | **`Auth.Userpass.ListUsersInfo` is named for section 14, not for the catalogue that will own its neighbours.** Section 14:123 names it `Auth.Userpass.ListUsersInfo`; `appendix-a-endpoint-catalogue.md:87` nests it as `Auth.Userpass.Admin.ListUsersInfo`. M8e shipped section 14's name because no `Auth.Userpass.Admin` sub-client exists and creating a one-member one speculatively is the anticipatory structure CLA-007 rules out | R1 | **Open, owned by M10**, which builds the rest of `Userpass.Admin.*` and must then decide whether the member moves. The move is a rename on an **unpublished** API — nothing in this repository publishes to a package registry — so it is cheap *if taken deliberately at M10* and a surprise if not. Recorded for exactly that reason. Evidence: [DR-0013](decisions/0013-m8-transit-totp-and-efficiency.md) D-M8-46 |

| R-30 | **`BV-TRANSPORT-005` means "something cancelled", not "the caller cancelled", and two call sites now compensate for it locally.** `Internal/RequestExecutor.cs:948` catches `OperationCanceledException` with **no** `when (cancellationToken.IsCancellationRequested)` filter and maps it through `TransportFailureMapper.MapCancelled`, so the code does not record *which* token fired. `ITransport` is public API and settable via `BastionVaultClientOptions.Transport`, so a user transport carrying its own internal deadline — the exact pattern `HttpClientTransport.cs:130-131` uses — produces `BV-TRANSPORT-005` while the caller's token is still live. `Internal/RequestExecutor.cs:829` is a second unfiltered catch on the token-resolution path. Found at `CCH-006`'s R2 gate, which refuted an author claim that the resulting fall-through was unreachable | R2 | **Open, unowned; an M9/M10 candidate.** Not a defect today: both consumers handle it correctly — `Internal/TokenRenewal.cs:210` and `CacheWatcher.cs:91` each re-check the caller's token and treat a live one as an ordinary failure to back off from. **The smell is that there are two of them.** A compensation repeated at every call site is a contract the callee should be stating instead, and the third consumer is the one that will forget. `HttpClientTransport.cs:138` already disambiguates correctly (`when (!cancellationToken.IsCancellationRequested)` → `Timeout`), which shows the filter is expressible — it is simply not applied at the executor. Fixing it means deciding whether a transport-internal cancellation deserves its own code rather than sharing the caller's, which is an error-model change (section 04) and therefore Architect work, not a local edit. Evidence: [DR-0013](decisions/0013-m8-transit-totp-and-efficiency.md) D-M8-55 |
| R-31 | **`PKI-030`'s second limb is unimplementable as specified: `BV-QUOTA-002` has no recognition row and no server message anywhere.** `09-pki-engine.md:107-108` requires a queue-cap breach (500 pending) to map to `BV-QUOTA-002 QueueFull` **"by message"**. The *code* exists (`appendix-b-error-catalogue.md:128`, generated into `ErrorCatalogData.g.cs`), but Appendix B §2 carries **no recognition row** for it — only `BV-QUOTA-001` (`namespace quota exceeded`, `:245`) — and §09's own recognition table omits the message too. Recognition by message requires the message, and no document states it. Found at M9 framing, before any slice was dispatched | **R3** (the `specifications/` limb of CRS-004; the published-artefact limb stays vacuous) | **Open, owned by M10**, which builds the rest of the PKI queue surface. `PKI-030` **stays on the traceability baseline** and M9 landed 10 of its 11 IDs rather than reporting a half-covered requirement as covered ([DR-0016](decisions/0016-m9-pki-and-ssh.md) D-M9-11). Closing it needs the server's actual message text — the same provenance every Appendix B row has — and then an Appendix B row, a regenerated catalogue, and a fixture, since Appendix B §3 requires every recognition row to produce its code for at least one fixture. **Do not guess the string:** R-23 is the precedent for a recognition rule that passes its own fixture and cannot fire in production |
| R-32 | **Neither PKI queue table defines any response shape, so eight routes return untyped maps.** `09-pki-engine.md:86-108` is two columns — operation and HTTP — for all fifteen queue routes. `Pki.Csr.Read`, `Generate`, `SetSigned`, `SignRequests.Read`, `Preflight`, `Approve`, `ApproveVerbatim` and both `*-info` listings have no documented response; §14:118-119 names `CsrSummary` and `SignRequestSummary` and defines neither. M9 bound them to the raw response map and `Page<IReadOnlyDictionary<string, JsonElement>>` rather than invent public records (D-M9-10, D-M9-21) | R2 | **Open, owned by M10.** Replacing a map with a typed record is a breaking change on an **unpublished** API — nothing here publishes to a registry — so it is cheap *if taken deliberately at M10* and a surprise if not, which is R-29's reasoning applied again. **Two soft edges ride on this row**, both booked to M12's integration suite because no document answers them: whether `Pki.ReadKey(ref)` or `Pki.Csr.Read(id)` returns private material for an object created `exportable: true` — D-M9-16's rule keys on *request parameters*, the only signal §09 gives, so a route that returns a secret without a parameter announcing it is invisible to it — and whether `Pki.GenerateIntermediate` accepts `issuer_name` at all (D-M9-23 keeps it on whole-set-reuse grounds while recording that §09 points the other way) |
| R-33 | **`ErrorPaths.Redact` inspects path segments and not query strings, so a query-borne secret would reach three logging surfaces.** `Internal/ErrorPaths.cs:9,25-45` rewrites only the segment following `lookup`, `renew`, `revoke` and `revoke-orphan`. A query string is not a segment it looks at, so a secret in one flows unredacted into `RequestEvent.Path` via `RequestExecutor.cs:892` — the `CFG-080`/`TST-051` observer, which a consumer wires to a logger and which **fires on success** — plus `BastionVaultException.Path` / `Details["path"]` and `HintEnrichment`'s "The path as sent was …" | R2 | **Open, unowned — and latent, not active.** No shipped route places secret material in a query. M9 came within one handback of shipping two that would have: `Pki.ExportCertificate`'s and `Pki.ExportIssuer`'s GET forms both carry a `password`, and both are bound **POST only** for exactly this reason (D-M9-19, D-M9-22). **Those two routes stay POST-only until this row closes** — that is the dependency worth carrying, since a later reader will otherwise read the restriction as arbitrary and "fix" it. Closing it is a transport-layer change, not an engine milestone's |
| R-34 | **§10 states the CSV wire form for one of its two list-valued request fields and not the other.** `10-ssh-engine.md:38` says "`valid_principals` is CSV on the wire"; `:81`'s `asset_group_ids[]` says nothing about encoding, and the accepted fixture `sshbroker.effective-v2-pinned.json` — which predates the SDK — sends `"asset_group_ids": "g1"`, a CSV string. The `[]` is parameter cardinality in an operation signature, the same notation position as `:12`'s `private_key?`, not a wire statement; `:38` existing at all is the proof that CSV is not §10's default | R1 | **Open, owned by the Architect queue beside R-27.** The fix is a four-word §10 edit, which is R3. **The control already exists**, which is what keeps this R1: the fixture is driven in all three languages, so a Stage 2 pass that transcribed a JSON array from §10 alone would go red rather than diverge silently. Settled for M9 by [DR-0016](decisions/0016-m9-pki-and-ssh.md) **D-M9-28**, which states the general rule this row is an instance of: **a fixture is authoritative on wire encoding where its section states none; a section is authoritative on operation shape, element type and name, and a fixture may not contradict it** — the same "each artefact answers the question it owns" principle as D-M9-7 (Appendix A owns the prefix notation) and D-M9-9 (the area section owns element types) |

## 9. Tracking

- **Per-milestone truth:** the traceability report (TST-041). A milestone is done when its
  requirement IDs move from uncovered to covered and stay there.
- **Per-commit truth:** the CI gate set from M0. A red gate is never weakened (CLA-004).
- **Known gaps:** `tools/traceability/baseline.json` remains the authoritative, machine-readable
  gap list. **Done at M4:** `dotnet/README.md` renders it into CNF-002 prose — the two
  section-07 gaps by requirement ID, the rest by section with counts and a pointer to the
  baseline. **Regenerate it from `baseline.json`, never hand-count it** — at M8 it was
  rewritten from the file and went 234 → **131**, and a hand-maintained count had already
  drifted. Note that a per-section count can *rise* while the total falls, because the
  specification gains requirement IDs (DR-0011 added `CNF-044`…`CNF-047`); say so in the
  README, or the number reads as the ratchet running backwards. It is updated at every Stage-1 milestone exit, and a milestone that
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
   claims it, and **two milestones have now exited declaring nothing — M4 and, on 2026-09-18,
   M8** (R-14; DR-0009 D-M4-3, DR-0013 D-M8-6). Only M10's `Complete` is left to spend. Two
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

6. **Should section 14's endpoint table be reconciled with the sections that own its
   endpoints? — new at M8, and it has now bitten twice.** Implementing section 14 surfaced two
   places where it disagrees with the owning section or the endpoint catalogue: it writes
   `Page<NamespaceSummary>` where `06-system-api.md:203` says `Page<Namespace>` (D-M8-5), and
   it prefixes all seven `*-info` routes with `/v2/` where
   `appendix-a-endpoint-catalogue.md` gives `sys/namespaces-info` (`:40`) and
   `{mount}/roles-info` (`:221`) as **v1** (**R-27**). Both were resolved the same way — the
   owning section and the catalogue win — and both shipped that way in `0.13.0`.

   **Why this needs an answer rather than another per-instance ruling:** the two instances
   share one cause. Section 14 is a cross-cutting chapter describing endpoints that other
   sections define, and its table was never reconciled with them. Two of the seven `*-info`
   rows are known wrong; the remaining five are simply unchecked, and M9 and M10 build exactly
   those five. Each unreconciled row is also a forced guess a Stage 2 parity transcription
   would freeze into three SDKs, which is why it now sits in §2's list of items to close
   before the Rust pass opens.

   **(a) reconcile section 14's table against Appendix A and the owning sections now**, as one
   specification edit covering all seven rows — cheap while nothing is published, and it
   removes the question from M9's and M10's path. **(b) keep ruling per instance** as each row
   is implemented, accepting that M9 and M10 each spend review time rediscovering the same
   class of defect. Claude's recommendation is **(a)**: the cost is one pass over seven table
   rows, and under (b) the third instance is found by whoever is least expecting it. **This is
   a `specifications/` change and therefore R3 — it is the project owner's, not Claude's**
   (CRS-004, `agents.md` §5.4).
