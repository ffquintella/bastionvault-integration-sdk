# Changelog

All notable changes to this repository are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the
project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html). The three
packages (`dotnet/`, `rust/`, `python/`) share one version number, because they ship one
specification; a release is cut only when all three match (CLA-003).

**Who maintains this file:** the Strategic Orchestrator (Claude), under the record-keeping
rules in [`agents.md`](agents.md) §11. Every change that a user of the SDKs, or a future
agent reading this repository, would need to know about gets a line here in the same change
that makes it. Behaviour is still decided in [`specifications/`](specifications/README.md);
this file records what happened, not what is required.

Sections used, in this order: **Added**, **Changed**, **Deprecated**, **Removed**,
**Fixed**, **Security**, **Agent architecture** (changes to `agents.md`, `claude.md`,
`skills/**` and the documents that govern agent behaviour — no package version implication).

## [Unreleased]

## [0.5.0] — 2026-09-14

> **This release is .NET only for M2a, and that deviates from the shared-version rule
> stated at the top of this file.** Rust and Python carry the regenerated error catalogue
> but none of the authentication surface below. The bump and the tag were authorised by the
> project owner; the rule is not amended, because one authorised exception should stay
> visible as an exception ([DR-0006](decisions/0006-m2-authentication.md) D-M2-15).

### Added

- **M2a — `Client.Auth`, the first sub-API grouping (OVR-008).** The token source model
  (`Static` / `Callback`, with the `Login` variant's contract in place and its public
  factory landing in M2b), the `Token` method group, and the nine token-store operations
  the server actually implements — `Create`, `Lookup`, `LookupSelf`, `Renew`, `RenewSelf`,
  `Revoke`, `RevokeOrphan`, `RevokeSelf`, `AuditLogin` (`AUT-001`, `AUT-004`, `AUT-014`,
  `AUT-020`, `AUT-080`…`AUT-085`). .NET only; Rust and Python follow from the same record.
- **The token-helper write path** — `Auth.PersistToken()` with owner-only permissions and
  `Auth.ForgetPersistedToken()` (`CFG-031`, `CFG-032`), plus `BV-CONFIG-010` for a token
  file in the CLI's encrypted `BVTOK1:` format.
- **Two error codes**, minted in Appendix B and generated into all three languages:
  `BV-AUTH-017 TokenSourceFailed` and `BV-CONFIG-011 TokenFileNotWritable`
  (D-M2-16). The catalogue is now 121 codes; the 127 recognition rules are unchanged,
  because both codes are raised client-side.
- **Two fixture-harness instruments the repository has never had.** The driver now honours
  `fixture.clock` (`clock.start` / `clock.advance`), which the fixture schema has defined
  since M0 and which no language read — so `auth.token.lookup-self-remaining-ttl` had
  never actually asserted anything. And a capturing logger plus a capturing
  `RequestObserver` now assert on **every** fixture run that no fixture token, password or
  secret-id appears in any log line, observer event, exception message or rendered error
  (`TST-051`, D-M2-7). Both were proven by seeded violation and then kept as standing
  tests.

### Changed

- **Breaking: `IClock.Now()` is now `IClock.NowUtc()`**, naming the kind of time it
  returns. Rust's `Clock::now()` returned a monotonic `Instant` while .NET's and Python's
  returned wall-clock, so one member name meant two different things across the three SDKs
  — and `AUT-014`'s `RemainingTtl` arithmetic was not merely untested on Rust but
  uncomputable (`AUT-014`, D-M2-2).
- **Breaking: token resolution is asynchronous.** The executor resolves through
  `TokenSource.ResolveAsync()` instead of reading a field (D-M2-9). A concurrent first-use
  login is single-flighted, so eight concurrent operations at startup perform one login
  rather than eight, and a *failed* login is no longer cached for the client's lifetime
  (`CFG-070`, D-M2-11, D-M2-17).
- `Auth.PersistToken` reports `BV-CONFIG-011` rather than `BV-CONFIG-005`, whose message
  describes a file that cannot be *read*.

### Fixed

- `BV-AUTH-015 TokenNotRenewable` and `BV-NOTFOUND-006 TokenNotFound` are raised only under
  the exact conditions `AUT-085` and `AUT-084` state. A `400` caused by a malformed request
  body on the renew path was reported as `TokenNotRenewable`, which could make a caller
  re-login in response to its own error; and any user path containing `auth/token/lookup`
  was refined as though it were the token-store endpoint.
- `Auth.Token.RenewSelf` resolves the current token once, so the token in the path and the
  token in the `X-BastionVault-Token` header can no longer differ (`AUT-080`).
- A token source that fails now raises `BV-AUTH-017` instead of letting a runtime exception
  escape the SDK uncoded (`ERR-020`, `TRN-054`).

### Security

- **The `CFG-080` request observer no longer receives an unredacted request path.**
  `auth/token/renew/{token}` and `auth/token/lookup/{token}` put a live token in the path,
  so every observer was receiving one (`ERR-003`, `TST-051`). Found by the TST-051
  instrument in the milestone that built it.
- A cancelled token resolution reports `BV-TRANSPORT-005` rather than escaping as a
  runtime cancellation (`ERR-020`).

## [0.4.0] — 2026-09-14

### Added

- **M1c — the error catalogue is generated from Appendix B, in three languages.**
  `tools/error-catalogue` parses `specifications/appendix-b-error-catalogue.md` §1 and §2
  into a checked-in `catalogue.json` and emits, for .NET, Rust and Python, the 119 error
  codes, the 127 ordered message-recognition rules and every `ErrorCodes` constant — plus
  124 `errors.recognition.*` conformance fixtures. A new `repo-gates.yml` job regenerates
  and fails on any diff, so a hand edit to generated source and a specification edit
  without regeneration are both build failures. Appendix B's ~1560 strings are now
  single-sourced rather than transcribed three times (ERR-010, ERR-036, ERR-037,
  [DR-0005](decisions/0005-m1c-error-model.md) D-M1c-1).
- **Public `ErrorCatalog` / `ErrorCatalogEntry` in all three SDKs.** `Get`/`get` returns
  the entry for a code and never throws; `All`/`all` is Appendix B order, so generated
  documentation is stable (ERR-036, D-M1c-7).
- **`ERR-031` is enforced rather than eyeballed.** Generation fails if any hint exceeds two
  sentences, and the compiled catalogue is asserted against the same rule (D-M1c-13).
- **Server-message recognition (ERR-020 step 4)** now runs ahead of the status table in all
  three languages, with `Details` extraction for the seven ERR-035 rows, ERR-034 path
  interpolation, and hint enrichment for the seven client-side-decidable ERR-040 rows. The
  two enrichment rows needing server state are deferred to M3 and M4 and are absent, not
  stubbed. ERR-003 path redaction and the ERR-002 one-line form are applied in the error
  constructor, so no surface can miss them.
- Server `warnings` are surfaced and logged at warning level in all three SDKs, and are
  never converted to errors (ERR-050).

### Changed

- **Behavioural: `400` and every other unmapped 4xx now map to `BV-INPUT-100
  ServerRejectedRequest`**, the code `04-error-model.md` step 5 names. They mapped to
  `BV-INPUT-001 InvalidArgument`, which D-M1b-21 chose only because the hand-transcribed
  catalogue did not carry `BV-INPUT-100`. `BV-INPUT-001` remains the code for client-side
  argument validation (D-M1c-12).
- **Behavioural: an unrecognised `409` now maps to `BV-CONFLICT-001 Conflict`.** The M1b
  `Resolve409` heuristic, which guessed `BV-CONFLICT-002`/`003` from the response body, is
  deleted — Appendix B §2 answers the digest/sha256 and brokered-credential messages at
  step 4. Before this change the three SDKs gave three different answers for an
  unrecognised `409` and none of them was the specification's (D-M1c-19).
- **Behavioural: a `503` now maps to `BV-SERVER-002 Unavailable` unconditionally.** The M1b
  `sealed`-substring heuristic is deleted; §2's `bastionvault is sealed` and
  `(5xx) is sealed` rows answer the sealed case at step 4, leaving the heuristic to cover
  only a body containing `sealed` without `is sealed` — unspecified and untested. Because
  `BV-SERVER-001` is not retryable and `BV-SERVER-002` is, those bodies were silently
  having a permitted retry suppressed (ERR-006, D-M1c-23).
- **Behavioural: `Error.Path` carries the `[ns=…]` display prefix in .NET**, as the `Error`
  field table in `04-error-model.md` defines it and as Rust and Python already did. .NET
  also prepended a leading `/` the other two do not; both are corrected, in `Error.Path`,
  `Details["path"]`, the ERR-034 hint note and `RequestEvent.Path`. Redaction still applies
  through the prefix (ERR-001, D-M1c-17, D-M1c-24).
- **Breaking (pre-1.0, CNF-040):** `RateNamespaceQuotaExceeded` is renamed
  `RateNamespaceRateQuotaExceeded` in all three SDKs, and Rust and Python additionally
  rename `NOTFOUND_PATH_NOT_FOUND` to `NOT_FOUND_PATH_NOT_FOUND`, following the generated
  naming rule. The code strings are unchanged (D-M1c-2, D-M1c-21).
- **Breaking (pre-1.0, CNF-040):** Rust's `DetailValue` gains a `List(Vec<String>)` variant
  so the ordered `keys` capture keeps its order, as .NET's `string[]` does (D-M1c-20).
- **Python's `CNF-027` public-API gate is now member-level, matching .NET and Rust.**
  `python/tests/_api_surface_extractor.py` replaces the 34 name-only lines of
  `python/api_surface.txt` with 360 lines covering every public class, method signature,
  property, dataclass field, enum member, constant *value* and public instance attribute
  assigned in `__init__`. Renaming a constant, removing a method, or renaming an attribute
  users read — `BastionVaultError.code` among them — now fails the gate rather than passing
  it silently. The baseline is mechanically generated and byte-identical on Python 3.11 and
  3.12; both the gap and its closure are proven by seeded violation (CNF-027, D-M1c-22,
  [DR-0001](decisions/0001-m0-harness-gate-proof.md)).

### Fixed

- **The `CNF-025` secret scan was red on `main` and is green again.** Eleven literal
  `s.<20+ alnum>` fake tokens sat in tracked test sources outside
  `specifications/fixtures/**`, so the gate exited 1 at `HEAD` — M1a and M1b both exited
  with it failing. The tokens are now assembled at runtime; the scan pattern and its
  whitelist are byte-for-byte unchanged (D-M1c-15, CLA-004).
- **The `CNF-010` coverage floor was red on `main` in the Python job and is green again.**
  `python.yml` runs `pytest -m "not integration"`, and `test_httpx_transport.py` is
  entirely integration-marked, so `httpx_transport.py` was covered at 29 % in CI and
  `--cov-fail-under=95` failed the job. It now has unit coverage through
  `httpx.MockTransport` alongside the untouched integration suite: 29 % → 100 %, and the
  job's own invocation passes at 98.92 %. No omit rule, no pragma, floor unchanged
  (D-M1c-16, CLA-004).
- A namespaced login would have sent a token once the display path landed, because .NET
  matched the anchored login pattern against it; token resolution reads the raw path, as
  Rust already did (CFG-020, D-M1c-24).

### Agent architecture

- **The orchestration documents are now bound to the harness.** `agents.md` §4.1 and §4.2
  described a model-routing policy that nothing executed: the harness loads only
  `claude.md`, so the routing matrix, the scoring bands and the token policy were never in
  context; `skills/claude/SKILLS.md` and `skills/codex/SKILLS.md` sat outside
  `.claude/skills/**` and in a format the harness does not discover, so neither was ever
  loaded; no agent definitions existed, so the Claude Haiku 4.5 / Claude Sonnet 5 /
  Claude Opus 5 rungs had nothing to instantiate; and no `model` setting existed, so the
  session ran whatever model the client happened to select. Adds `.claude/settings.json`
  (session default Claude Sonnet 5, per `claude.md` §4), five agent definitions under
  `.claude/agents/` covering routing-matrix rows 1–6, and two discoverable skills under
  `.claude/skills/` that route to the canonical `SKILLS.md` files rather than restating
  them. `claude.md` gains §0, which imports `agents.md` and `skills/claude/SKILLS.md` so
  the policy is in context from the first turn.
- **`agents.md` §4.1.2 — harness bindings**, recording each binding once (**CLA-008**) with
  three rules: an agent definition may only name a model its own tree may run
  (**BND-001**), a routing change lands with its `.claude/` change in the same commit
  (**BND-002**), and tiers bind by alias rather than by dated identifier so a registry
  version bump cannot silently repoint an agent (**BND-003**).
- **`scripts/validate-agent-docs.py` check C7** verifies the filesystem against that
  table — settings default, agent frontmatter and rungs, tree permissions, skill
  discoverability, and the `claude.md` import chain — so the documents can no longer drift
  ahead of the configuration. Proven by seeded violation and revert. The check runs in the
  existing `repo-gates.yml` agent-docs step; no workflow change was needed.

## [0.3.0] — 2026-09-13

### Added

- **M0 — conformance harness, quality gates and traceability.** Test harness in all three
  languages, the CNF-020…CNF-027 gate set wired into `dotnet.yml`, `rust.yml`, `python.yml`
  and `repo-gates.yml`, and the ratcheting traceability tool (`tools/traceability`, TST-041)
  with a 374-entry baseline as the remaining-work counter. Every gate was proven by seeded
  violation and revert — [`decisions/0001-m0-harness-gate-proof.md`](decisions/0001-m0-harness-gate-proof.md),
  [`decisions/0001-m0-harness.md`](decisions/0001-m0-harness.md).
- **M1a — client configuration, error skeleton and transport seam.** `BastionVaultClient` /
  `Client`, `ClientConfig`, the error base type and the transport abstraction in .NET, Rust
  and Python, with a redacting secret type, an injected environment source and the
  options-in / resolved-config-out shape the rest of the SDK inherits —
  [`decisions/0003-m1a-configuration.md`](decisions/0003-m1a-configuration.md).
- **M1b — transport, logical layer and retry.** The SDKs can now talk to a server. Adds the
  logical layer (`Logical.Read` / `Write` / `Delete` / `List` / `Raw`, TRN-001…003) over a
  production HTTP transport in all three languages, with URL encoding (TRN-020/021), the
  custom `LIST` verb (TRN-010), the reserved-header and login-path token rules
  (TRN-012…017), both response envelope shapes (TRN-040…043), status-code handling
  (TRN-050…054), and redirects refused rather than followed (TRN-060, CNF-034) —
  [`decisions/0004-m1b-transport.md`](decisions/0004-m1b-transport.md).
- **Retry policy execution** (CFG-051…055, RES-001…004): idempotency classification,
  exponential backoff with injectable jitter, `Retry-After` handling, and `Error.Attempts`.
  Sealed vaults (`BV-SERVER-001`) and abuse-guard rate limits (`BV-RATE-001`) are never
  retried automatically.
- **Runtime mutation and observability**: `Client.SetToken` / `ClearToken` /
  `WithNamespace` (CFG-070/071), and a request/response hook carrying method, path,
  namespace, status, duration, attempt and error code — and never bodies or tokens
  (CFG-080/081).
- **`FakeTransport`** as public test-support surface in each package (TRN-100), so
  applications can test against the SDK without a server. It is the same object the
  conformance fixture driver uses.
- `CHANGELOG.md` (this file) and the record-keeping rules **REC-001…REC-006**
  (`agents.md` §11) that require it and `ROADMAP.md` to be kept current.

### Changed

- **The transport seam is a new shape** (`Transport`, `TransportRequest`,
  `TransportResponse`). The M1a placeholders had diverged into three incompatible
  definitions — Rust's had no method at all — and were replaced by one asynchronous
  contract. Transport failures now surface as SDK errors with `BV-TRANSPORT-*` codes
  rather than as each runtime's native exception.
- `RequestOptions.Idempotent` is now optional (tri-state) rather than a plain boolean, so a
  caller can mark a write retryable *or* a read not-retryable.
- `RetryPolicy` gained the `RetryOn` field it was specified to have and shipped without.
- `RequestOptions` gained `ApiVersion` and `TotalTimeout`; the settings table gained
  `MaxResponseBytes` and `UseSystemProxy`. All four are required by section 03/13 text that
  section 02's tables had omitted.
- The SDKs are **asynchronous-only**; Rust and Python callers need a runtime or event loop.
  A synchronous facade is additive and deferred.
- Model registry narrowed to Claude models only, in both agent trees —
  [`decisions/0002-all-claude-registry.md`](decisions/0002-all-claude-registry.md).

### Fixed

- An unmapped HTTP status — including `400`, the most common error the server returns —
  raised a generic runtime exception instead of a coded SDK error (TRN-054). Unmapped
  statuses now fall back by class and carry the server message through.
- `MaxResponseBytes` was checked only *after* the whole response had been buffered, so the
  limit reported a violation without preventing one. It is now enforced while reading, and
  an over-limit `Content-Length` is refused unread (TRN-033).
- Unbracketed IPv6 literals with a port were accepted instead of rejected with
  `BV-CONFIG-001` (TRN-092).

### Security

- **Rust trusted no certificate authorities.** Its root store was seeded only from an
  explicitly configured CA, so with the default configuration every TLS connection would
  have failed — and any deployment that worked around it by configuring one CA silently
  lost the platform trust store. The platform store is now loaded and configured CA
  material is *added* to it, never substituted, unless `CaCertReplacesSystemRoots` is set
  (CFG-040).
- Client certificates are now presented on every connection where configured (CFG-044), TLS
  1.2 is the enforced floor with 1.3 offered (CFG-041), and `TlsServerName` drives both SNI
  and hostname verification (CFG-042). M1a parsed this material; M1b is where it reaches
  the wire.
- Rust and Python gained connection pooling (TRN-090) and have proxies disabled by default
  (TRN-091).

### Dependencies

- Python adds `httpx`. Rust adds `hyper`, `hyper-util`, `rustls`, `rustls-native-certs`,
  `tokio`, `tower-service` and supporting crates, taking its audited dependency set from
  131 to 143 crates. .NET adds nothing (in-box `SocketsHttpHandler`). `cargo audit`,
  `pip-audit` and `dotnet list package --vulnerable` are clean (CNF-024).

### Agent architecture

- **The .NET public-API gate (CNF-027) had never been enforced.** `AnalysisLevel=latest-all`
  imports an MSBuild props file that reassigns `$(CodeAnalysisRuleIds)`, silently discarding
  the `RS00xx` rules `Microsoft.CodeAnalysis.PublicApiAnalyzers` registers — so an
  undeclared public type compiled with zero warnings, and the M0 and M1a exits certified a
  baseline nothing had checked. The analyzer and its `PublicAPI.*.txt` files are replaced by
  an executing surface diff (`PublicApiSurface.txt`), matching the mechanism Rust and Python
  already used, and the new gate was proven by seeding a violation and watching it fail.
  `.github/workflows/dotnet.yml`'s claim to the contrary is corrected.
- `agents.md` §11 added: changelog and roadmap upkeep is now a normative obligation with an
  owner, a trigger and a definition of done, referenced from `claude.md`,
  `skills/claude/SKILLS.md` and `skills/codex/SKILLS.md`. Claude gains responsibility 7
  (project record upkeep) and rules CLA-009/CLA-010; Codex gains workflow step 8 and
  ENG-008 (propose the entry, never edit the file); `agents.md` gains VER-006. All four
  agent documents and `ROADMAP.md` bumped to 1.2.0.

## [0.2.1] — 2026-09-13

### Changed

- Agent trees restricted to the Claude and GPT model families; every other family removed
  from the registry and the routing tables. (Superseded in `Unreleased` by the all-Claude
  registry.)

## [0.2.0] — 2026-09-13

### Added

- **Technology-agnostic SDK specifications** under `specifications/`: 18 documents, 4
  appendices, 388 requirement IDs (`AREA-NNN`) and the shared fixture corpus. This is the
  single source of truth for behaviour in all three languages.
- **Dual-orchestrator agent architecture**: `agents.md`, `claude.md`,
  `skills/claude/SKILLS.md`, `skills/codex/SKILLS.md`, the token-optimization policy
  (TOK-001…TOK-012) and `scripts/validate-agent-docs.py` to keep the four documents
  consistent.
- `ROADMAP.md`: milestones M0…M12, sequencing decisions D-1…D-5 and the risk register.

## [0.1.0] — 2026-09-13

Untagged. Recorded here for completeness from the repository history.

### Added

- Base .NET, Rust and Python SDK projects, the shared solution layout, and the
  `build-artifacts.yml` workflow that builds and validates artifacts for all three.

[Unreleased]: https://github.com/ffquintella/bastionvault-integration-sdk/compare/v0.4.0...HEAD
[0.4.0]: https://github.com/ffquintella/bastionvault-integration-sdk/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/ffquintella/bastionvault-integration-sdk/compare/v0.2.1...v0.3.0
[0.2.1]: https://github.com/ffquintella/bastionvault-integration-sdk/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/ffquintella/bastionvault-integration-sdk/releases/tag/v0.2.0
