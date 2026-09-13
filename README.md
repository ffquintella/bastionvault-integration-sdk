# bastionvault-integration-sdk

Base repository for the BastionVault Integration SDK with libraries in:

- .NET (`/dotnet/BastionVault.IntegrationSdk`)
- Rust (`/rust/bastionvault-integration-sdk`)
- Python (`/python`)

## Specifications and roadmap

The behaviour of every SDK is defined in [`specifications/`](specifications/README.md)
(388 requirement IDs, indexed in
[Appendix D](specifications/appendix-d-requirement-index.md)). The delivery plan that takes
the three implementations from scaffold to conformance level Complete is
[`ROADMAP.md`](ROADMAP.md). Shipped changes are recorded in [`CHANGELOG.md`](CHANGELOG.md);
the rules that keep both current are in [`agents.md`](agents.md) §11.

## Build artifacts locally

### .NET

```bash
dotnet pack ./dotnet/BastionVault.IntegrationSdk/BastionVault.IntegrationSdk.csproj -p:ContinuousIntegrationBuild=true
```

### Rust

```bash
cargo package --manifest-path ./rust/bastionvault-integration-sdk/Cargo.toml
```

### Python

```bash
python -m pip install --upgrade build
python -m build ./python --outdir ./artifacts/python
```

## Tests

- .NET: `dotnet test ./dotnet/BastionVault.IntegrationSdk.Tests/BastionVault.IntegrationSdk.Tests.csproj`
- Rust: `cargo test --manifest-path ./rust/bastionvault-integration-sdk/Cargo.toml`
- Python: `PYTHONPATH=./python/src python -m unittest discover -s ./python/tests`

## Agent orchestration

This repository is worked on by a dual-orchestrator agent system: a Claude strategic
orchestrator and a Codex engineering orchestrator.

| File | Purpose |
|------|---------|
| [`agents.md`](agents.md) | Normative specification: hierarchy, responsibility boundaries, model routing, confidence and risk scoring, escalation, token policy |
| [`claude.md`](claude.md) | Claude-specific behaviour (CTO, architect, reviewer, strategic planner) |
| [`skills/claude/SKILLS.md`](skills/claude/SKILLS.md) | Claude routing rules, conflict resolution, max 8 parallel agents |
| [`skills/codex/SKILLS.md`](skills/codex/SKILLS.md) | Codex routing rules, engineering and review workflow, hard limit 10 agents |

Check the four documents stay consistent:

```bash
python scripts/validate-agent-docs.py
```
