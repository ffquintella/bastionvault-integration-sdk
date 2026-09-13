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

## Observability (.NET, M1b)

The .NET SDK's request/response hook (`IRequestObserver.OnRequestCompleted`, CFG-080) fires
once per attempt of every `Logical.*` call. Per CFG-081, the metrics-friendly names an
application SHOULD derive from `RequestEvent` when wiring its own metrics backend are:

| Metric | Derived from |
|--------|--------------|
| `bastionvault.client.request.duration` | `RequestEvent.Duration`, per attempt |
| `bastionvault.client.request.retries` | count of `RequestEvent`s per logical operation beyond the first (same `RequestId`, `Attempt > 1`) |
| `bastionvault.client.request.errors{code}` | `RequestEvent.ErrorCode`, when present, labelled by code |

The SDK does not emit these to a metrics backend itself (no dependency is assumed); it
exposes the data the hook needs so an application's own metrics client can record them
under these names, keeping metric names comparable across the .NET, Rust and Python SDKs.

## Python transport layer (M1b)

The Python SDK is **async-only** at M1b (D-M1b-2): every `Client.logical.*` method and
`Client.logical.raw` is `async def` and needs an event loop (`asyncio.run(...)` or an
existing loop) to call. A synchronous facade is deferred, not forgotten.

`RequestObserver` (`Client(options=ClientOptions(observer=...))`) fires once per attempt
with `RequestEvent` -- no body, no token, no headers -- and follows the same
`bastionvault.client.request.*` metric-name convention documented above for .NET.

A namespace is always sent as the `X-BastionVault-Namespace` header, never as a path
prefix (TRN-023): a caller who encodes the namespace in the path themselves (for example
`dti/esi/secret/data/x`) MUST leave `ClientOptions.namespace` / `RequestOptions.namespace`
empty for that call, since the server rejects a request carrying both forms at once.

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
