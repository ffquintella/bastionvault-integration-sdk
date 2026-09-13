# Roadmap — implementing the specifications

**Owner:** Strategic Orchestrator (Claude) · **Authority:** subordinate to [`agents.md`](agents.md) and [`claude.md`](claude.md)
**Source of truth for behaviour:** [`specifications/`](specifications/README.md) · **Version:** 1.0.0 · 2026-09-13

## 1. Objective

One verifiable outcome: **the .NET, Rust and Python SDKs each satisfy every MUST
requirement of the 388 requirements in [Appendix D](specifications/appendix-d-requirement-index.md)
at conformance level Complete (CNF-003), with the quality gates CNF-020…CNF-027 green.**

Definition of done, per language:

1. Every applicable requirement ID appears in the traceability report with at least one test (CNF-014, TST-041).
2. Every applicable fixture under `specifications/fixtures/**` is exercised (CNF-015).
3. Line **and** branch coverage ≥ 95 % on library code, no exclusion pragmas (CNF-010, TST-030).
4. The three implementations are behaviourally identical or carry a recorded parity exception (CLA-003).
5. README declares `Complete`, the spec version, and the tested server versions (CNF-041).

## 2. Current state (2026-09-13, after M0)

| Area | State |
|------|-------|
| `specifications/` | Complete: 18 documents, 4 appendices, **74 fixtures on disk**, 388 requirement IDs |
| `dotnet/` | Harness complete. Fixture loader, TST-011 driver, Kestrel HTTPS mock server, `SdkInfo` version surface. **20 tests, 100 % line and branch** |
| `rust/` | Harness complete. Fixture loader, TST-011 driver, `hyper`+`tokio-rustls` mock server, version functions. **22 tests, 100 % line and region** (D-M0-14) |
| `python/` | Harness complete. Fixture loader, TST-011 driver, `ThreadingHTTPServer`+`ssl` mock server, version functions. **32 tests, 100 % line and branch**, `mypy --strict` and `ruff` clean |
| `tools/traceability` (TST-041) | **Built.** Parses Appendix D and all three marker forms; ratchets against `baseline.json`. **19 of 420 covered, 401 baselined** |
| Fixture driver operation registry | **Empty by design** (D-M0-2). All 74 fixtures load and schema-validate; all report `pending` until M1+ registers operations |
| CI | `dotnet.yml`, `rust.yml`, `python.yml`, `repo-gates.yml` **plus** the pre-existing `build-artifacts.yml`. Every gate CNF-020…CNF-027 and TST-041 wired |
| Gate proof | **All six exit-criteria rows proven** by seeded violation and revert — see [`decisions/0001-m0-harness-gate-proof.md`](decisions/0001-m0-harness-gate-proof.md) |

**M0 is complete.** The instruments exist and each one has been made to fail on purpose.
The baseline's 401 entries are the project's remaining-work counter; it must reach zero
before the M12 release (D-M0-1).

Implementation of SDK behaviour is still greenfield: the three libraries expose only version
metadata (D-M0-12a). Nothing below is a migration; everything is a first build.

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

### D-1 — Milestones are horizontal slices across all three languages, not one language at a time

**Forces:** CLA-003 forbids changing behaviour in one language only. A language-at-a-time
build front-loads velocity but discovers parity defects at the end, when the cost of a
contract change is three rewrites instead of three edits.

**Decision:** each milestone lands in .NET, Rust and Python before it exits. The parity
check is the exit criterion, not a later phase.

**Rejected:** *.NET to Complete first, then port.* Rejected because the first port would
re-litigate every contract decision the .NET implementation made implicitly, and because
the fixture suite — the parity instrument — would be tuned to one language's idioms.

**Consequence:** milestone wall-clock is bounded by the slowest language, not the fastest.

### D-2 — Within a milestone, .NET is the pathfinder

The shared design is settled first (Claude, in the milestone brief), then .NET implements,
then Rust and Python implement **in parallel from the same brief and the same fixtures** —
not from the .NET source. The .NET pass exists to surface gaps in the brief cheaply, before
two more agents hit the same gap.

**Rejected:** *all three in parallel from the brief.* Rejected because an under-specified
brief then produces three divergent readings that must be reconciled after the fact.

**Consequence:** roughly one extra serialisation step per milestone, paid back in avoided rework.

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

### D-5 — Fixtures are loaded from the repository, never copied

Per TST-010. Each language gets a loader that reads `specifications/fixtures/**` by relative
path. A fixture edit must break all three suites simultaneously, or the parity instrument is
worthless.

## 4. Milestone plan

`Reqs` = requirement IDs whose implementation lands in that milestone. Tiers per
[`agents.md`](agents.md) §5.3 (risk) and §7.1 (size).

| # | Milestone | Reqs | Count | Size | Risk | Gate at exit |
|---|-----------|------|-------|------|------|--------------|
| **M0** | Harness, gates and traceability | `CNF`, `FIX`, `TST` | 54 | Large | R2 | CI fails on a seeded coverage/traceability regression |
| **M1** | Client skeleton: config, transport, error model | `OVR`, `CFG`, `TRN`, `ERR` | 105 | Enterprise | R3 | All transport + error fixtures green in all three languages |
| **M2** | Authentication — Core methods | `AUT` (token, userpass, AppID, token store, auto-renew, security) | ~28 | Large | R3 | Auth fixtures green; no token in any captured log (TST-051) |
| **M3** | System API — Core subset | `SYS` (health, seal-status, server/cluster info, capabilities) | ~16 | Large | R2 | `sys` fixtures green in all three languages |
| **M4** | KV v1 + KV v2 → **declare Core** | `KV`, `KV1`, `KV2` | 27 | Large | R2 | **Conformance level `Core` declared in all three READMEs** |
| **M5** | Cluster discovery and resilience | `DSC`, `RES` | 33 | Large | R2 | Failover + sticky-session fixtures green in all three languages |
| **M6** | Authentication — remaining methods | `AUT` (FerroGate, Certificate, OIDC/SAML, FIDO2) | ~12 | Large | R3 | Section 05 has zero unimplemented MUSTs |
| **M7** | System API — remainder | `SYS` (init/seal/unseal, mounts, auth methods, policies, namespaces, audit, backup/restore) | ~18 | Large | R3 | Section 06 has zero unimplemented MUSTs |
| **M8** | Transit, TOTP, batch/pagination/cache → **declare Standard** | `TRS`, `TOT`, `BAT`, `PAG`, `CCH`, `EFF` | 38 | Enterprise | R3 | **Conformance level `Standard` declared** |
| **M9** | PKI and SSH | `PKI`, `SSH`, `SSB` | 11 | Large | R2 | Sections 09–10 complete |
| **M10** | Other engines and identity → **declare Complete** | `IDN`, `RSC`, `FIL`, `LDP`, `RUS` | 9 | Large | R2 | **Conformance level `Complete` declared (CNF-003 satisfied)** |
| **M11** | Documentation and usage guides | `DOC` | 21 | Large | R1 | Every doc sample compiles/runs (CNF-026) |
| **M12** | Live-server integration suite and 1.0.0 release | `ITG` | 16 | Enterprise | R3 | Release checklist (01 § Release checklist) evidenced on the tag |

**Total: 388.** The `AUT` and `SYS` splits (M2/M6, M3/M7) are estimates against the section
headings; the exact ID lists are fixed when each milestone brief is authored, and the two
halves always sum to 40 and 34 respectively.

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

1. **M1a — Configuration (`CFG`, `OVR`).** Configuration model, environment-variable
   precedence, TLS options, timeouts, token helper. Includes the security baseline that is
   configuration-shaped: CNF-030, CNF-033, CNF-035.
2. **M1b — Transport (`TRN`).** HTTP mapping, headers, envelope, status-code handling, the
   `LIST` verb, redirects (same-cluster only, CNF-034), rate-limit handling, and the retry
   policy subset of section 13 that Core needs.
3. **M1c — Error model (`ERR` + Appendix B).** Taxonomy, stable codes, hints, retryability,
   server-message recognition, CNF-043 (`BV-SERVER-004 UnsupportedByServer`).
   Appendix B is a 30 KB table — the code → message → hint → retryability mapping is
   generated or table-driven in all three languages, never hand-transcribed.

**R3 because** this milestone fixes the public API shape and the TLS/redirect security
posture. Architecture review by Claude Opus 5 before implementation; decision record required.

**Risk.** Getting the error taxonomy wrong here is the most expensive defect available in
this project — it is public API, it is cross-language, and it is load-bearing for every
engine. Budget explicit design time before any code.

### M2 — Authentication, Core methods

Token source model, login response contract, Token / Userpass / AppID, token store
operations, automatic renewal, and the section-05 security requirements.

**R3, non-negotiable checks:** CNF-031/032 (no secret material in logs, `ToString`, `Debug`
or `repr` at any level; redaction in the default string representation), TST-051 (tests
assert the captured log is clean), and auto-renew lifecycle correctness under concurrency.

### M3 / M4 — System API Core subset, then KV → **Core**

M3 is the smallest useful `sys` surface: health, seal-status, server info, cluster status,
capabilities. M4 is the whole of section 07 — KV v1, KV v2 versions, CAS, soft delete,
destroy, metadata.

**M4 exit is the first externally meaningful gate:** all three READMEs declare conformance
level `Core`, list known gaps by ID (CNF-002), and state the spec version. From this point
the SDKs are usable for application integration.

### M5 — Cluster discovery and resilience

SRV discovery, health probing, node ranking, sticky sessions, bounded failover, retry
interaction, TLS with discovery, diagnostics, operator fan-out.

**Dependency.** Consumes the retry primitives built in M1b; do not rebuild them.

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

**M10 exit:** `Complete` declared in all three READMEs — CNF-003 satisfied.

### M11 — Documentation and usage guides

Section 16 requirements plus the guides of section 17, adapted per language. Every sample
compiles and runs in CI (CNF-026); every public symbol carries a doc comment.

### M12 — Live-server integration suite and 1.0.0

The 16 `ITG` requirements and the `ITG-S<nn>` scenarios of 15 § Required scenarios, against
the server versions in [`test-matrix.json`](specifications/test-matrix.json), with the
provisioning, isolation and cleanup rules of 15. Then the release checklist: gates green,
coverage stated, traceability report clean, changelog, README conformance statement.

**Human confirmation required before the tag** (R3 rule, `agents.md` §5.3).

## 6. Dependency graph

```
M0 ──▶ M1a ──▶ M1b ──▶ M1c ──┬──▶ M2 ──▶ M3 ──▶ M4 ═══ CORE
                             │                   │
                             │                   ├──▶ M5 ──┐
                             │                   ├──▶ M6 ──┤
                             │                   └──▶ M7 ──┤
                             │                             ├──▶ M8 ═══ STANDARD
                             │                             │
                             └─────────────────────────────┴──▶ M9 ──▶ M10 ═══ COMPLETE
                                                                        │
                                                              M11 ──────┴──▶ M12 ═══ 1.0.0
```

**Serial by necessity:** M0 → M1a → M1b → M1c → M2 → M3 → M4.
**Parallel after M4:** M5, M6 and M7 are independent of one another; M9 and M10 depend only
on M1 and M4. M11 can start as soon as the surface it documents is frozen — per section, not
as one block at the end.

**Hard constraint:** at most 10 concurrent agents system-wide (`agents.md` §7.2). Three
milestones × three languages already saturates that ceiling, so run at most two milestones
concurrently.

## 7. Delegation shape

Per `claude.md` §1.1 and §5, implementation does not start with Claude. The repeating unit
of work is:

| Step | Owner | Output |
|------|-------|--------|
| 1. Frame and ground | Claude Sonnet 5 | Milestone objective, exact requirement ID list, risk tier |
| 2. Design | Claude Sonnet 5 (Claude Opus 5 for R3) | Decision record: API shape, type names per the 00 mapping rules, parity contract |
| 3. Brief | Claude | One four-section brief (TOK-004) per language, budget per tier (TOK-011) |
| 4. Implement | Codex Engineering Orchestrator | .NET first (D-2), then Rust and Python in parallel |
| 5. Review | Claude | Verdict per `claude.md` §3.1, findings tied to file, line and requirement ID |
| 6. Parity check | Claude | Same fixtures, three suites, identical assertions |
| 7. Accept | Claude (Strategic Orchestrator for R3) | Milestone closed, README gap list updated |

Briefs cite requirement IDs and `file:line` ranges — never spec prose (TOK-005, TOK-006).
A milestone that cannot fit its tier budget is decomposed, not granted a bigger budget.

## 8. Risk register

| # | Risk | Tier | Mitigation |
|---|------|------|------------|
| R-1 | Error taxonomy (M1c) is wrong; it is public API and cross-language | R3 | Opus architecture review plus decision record before code; Appendix B is table-driven, not transcribed |
| R-2 | 95 % branch coverage (CNF-010) is expensive on error paths, which CNF-011 explicitly puts in scope | R2 | Write the failure-path fixture with the feature (TST-013); never weaken the floor (CLA-004) |
| R-3 | Rust branch coverage may be unavailable on the toolchain | R1 | 15 § Coverage permits line and region ≥ 95 as the documented substitute — record the substitution once |
| R-4 | Three languages drift silently | R2 | Shared fixtures loaded from the repo (D-5, TST-010); parity is a milestone exit criterion |
| R-5 | Secret material leaks into logs or `Debug`/`repr` | R3 | CNF-031/032 asserted by capturing-logger tests (TST-051) in every auth and KV suite |
| R-6 | No live BastionVault server available. **Re-tiered R3 and pulled forward to M1 by DR-0001 D-M0-7** — FIX-010 requires fixture response bodies to be captured from a real server exchange, and 66 of Appendix C's ~140 mandatory fixtures are unwritten, so fixture authoring is blocked from M1 rather than M12 | R3 | Integration tests are skippable per run but mandatory in the CI matrix. **Provisioning a server matching `specifications/test-matrix.json` is now an M1 entry condition, not an M12 one** — escalated to the project owner at M0 exit (§10 question 3) |
| R-7 | M1 is 105 requirements — too large to review as one unit | R2 | Already split into M1a/M1b/M1c; each sub-slice reviews and exits independently |
| R-8 | Appendix A lists 167 endpoints; mechanical volume swamps design attention | R1 | Endpoint plumbing is Engineering-tree bulk work — route it, do not hand-write it in the Claude tree |

## 9. Tracking

- **Per-milestone truth:** the traceability report (TST-041). A milestone is done when its
  requirement IDs move from uncovered to covered and stay there.
- **Per-commit truth:** the CI gate set from M0. A red gate is never weakened (CLA-004).
- **Known gaps:** each README carries the CNF-002 gap list, updated at every milestone exit.
- **Decisions:** recorded once, where they belong, and linked thereafter (CLA-008). This file
  records only the sequencing decisions D-1…D-5; per-milestone design decisions belong in
  their own decision records.

## 10. Open questions for the project owner

1. **Target date and cadence.** This plan is sequenced but not calendared. Milestone
   durations depend on delegate throughput, which has no baseline yet — M0 will produce the
   first one.
2. **Release strategy.** Ship `Core` as a `0.x` preview at M4, or hold everything to a single
   `1.0.0` at M12? The roadmap supports either; the gap lists (CNF-002) exist to make early
   shipping honest.
3. **Live server access for M12.** R-6 assumes a provisionable BastionVault instance matching
   `test-matrix.json`. If none exists, the integration suite needs a plan of its own.
