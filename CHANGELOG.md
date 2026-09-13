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

[Unreleased]: https://github.com/ffquintella/bastionvault-integration-sdk/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/ffquintella/bastionvault-integration-sdk/compare/v0.2.1...v0.3.0
[0.2.1]: https://github.com/ffquintella/bastionvault-integration-sdk/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/ffquintella/bastionvault-integration-sdk/releases/tag/v0.2.0
