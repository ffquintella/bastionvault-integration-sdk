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
- `CHANGELOG.md` (this file) and the record-keeping rules **REC-001…REC-006**
  (`agents.md` §11) that require it and `ROADMAP.md` to be kept current.

### Changed

- Model registry narrowed to Claude models only, in both agent trees —
  [`decisions/0002-all-claude-registry.md`](decisions/0002-all-claude-registry.md).

### Agent architecture

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

[Unreleased]: https://github.com/ffquintella/bastionvault-integration-sdk/compare/v0.2.1...HEAD
[0.2.1]: https://github.com/ffquintella/bastionvault-integration-sdk/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/ffquintella/bastionvault-integration-sdk/releases/tag/v0.2.0
