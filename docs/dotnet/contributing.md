# Contributing / testing guide (.NET)

**Implements** [`specifications/16-documentation-requirements.md` D13](../../specifications/16-documentation-requirements.md),
covering the test pyramid, fixture loading, requirement markers and coverage commands of
[15 — testing requirements](../../specifications/15-testing-requirements.md). This is a
**process document**, not a guide: it is organised by activity (build, test, add a fixture) rather
than by DOC-010's task/prerequisites/steps order, and none of its shell commands is a `csharp`
sample, so it carries no `DOC-003` compiled-sample obligation (D1–D10's rule, not D13's).

## The test pyramid

| Layer | Project | Server needed | What it proves |
|---|---|---|---|
| Unit | `dotnet/BastionVault.IntegrationSdk.Tests` (most files) | no | Pure logic: config resolution, URL encoding, envelope parsing, error mapping, ranking, backoff maths, catalog integrity |
| Conformance | `dotnet/BastionVault.IntegrationSdk.Tests` (the `*FixturesTests.cs` files) | no | Every fixture in `specifications/fixtures/**`, replayed through a fake transport: exact request bytes and typed results |
| Contract | `dotnet/BastionVault.IntegrationSdk.Tests` (the `InProcessHttpsMockServer`-based tests) | in-process | The protocol rules of [03](../../specifications/03-transport-and-protocol.md) against a real in-process HTTPS server: custom `LIST`, `204`, empty `404`, `429` + `Retry-After`, TLS, mTLS |
| Integration | `dotnet/BastionVault.IntegrationSdk.IntegrationTests` | yes (or skips) | End to end against a real BastionVault server: bootstrap, every engine, auth flows, failure modes, cluster behaviour |

Unit, conformance and contract tests all live in the same `.Tests` project and all run with
`dotnet test` on that project — the layer distinctions above are about *what a test depends on*,
not about which command runs it. Only integration tests are a separate project, because only they
need a server.

## Running the suites

```bash
# Unit, conformance and contract (no network, completes in well under a minute):
dotnet test dotnet/BastionVault.IntegrationSdk.Tests/BastionVault.IntegrationSdk.Tests.csproj

# Docs samples (compiles and executes every D1-D10 code block against the mock server):
dotnet test dotnet/BastionVault.IntegrationSdk.DocsSamples/BastionVault.IntegrationSdk.DocsSamples.csproj

# Integration, against a server this command starts and tears down itself:
BASTIONVAULT_TEST_BIN=/usr/local/bin/bvault dotnet test dotnet/BastionVault.IntegrationSdk.IntegrationTests/BastionVault.IntegrationSdk.IntegrationTests.csproj
```

The first command is also what `dotnet/BastionVault.IntegrationSdk.sln` builds as part of CI's
gate job, and it is where the 95 % coverage floor is measured (see below). The second is `DOC-022`'s
gate: it fails when a documented code sample no longer matches the source it is checked against.

### Running the integration suite (`ITG-004`)

The one documented command this section exists to pin, run exactly as written from the repository
root:

```bash
BASTIONVAULT_TEST_BIN=/usr/local/bin/bvault dotnet test dotnet/BastionVault.IntegrationSdk.IntegrationTests/BastionVault.IntegrationSdk.IntegrationTests.csproj
```

That variable names a local `bvault` binary; the harness starts a fresh single-node server from
it on a random free port with a temporary storage backend, initialises and unseals it, captures
the root token, runs every scenario, and tears the server down at the end of the run — nothing
here is left behind. Every environment variable the harness reads:

| Variable | Meaning |
|---|---|
| `BASTIONVAULT_TEST_ADDR` | Use an already-running server at this address instead of starting one (**external** mode). When set, this takes priority over every managed-mode option below |
| `BASTIONVAULT_TEST_ROOT_TOKEN` | Required in external mode (or when unsealing yourself); the admin token the harness uses to provision test mounts, policies and users |
| `BASTIONVAULT_TEST_UNSEAL_KEYS` | External mode: lets the harness unseal the server itself instead of requiring it pre-unsealed |
| `BASTIONVAULT_TEST_CACERT` | External mode: a CA bundle for a server with a certificate the platform trust store does not already cover |
| `BASTIONVAULT_TEST_NAMESPACE` | Runs the namespace-scoped scenarios against a child namespace instead of root |
| `BASTIONVAULT_TEST_TLS_SKIP_VERIFY` | Set to `1` for a dev server with a self-signed certificate you have not pinned via `_CACERT` — never needed in managed mode, which owns the CA it generated |
| `BASTIONVAULT_TEST_BIN` | Managed mode: the path to a local `bvault` binary, as shown above |
| `BASTIONVAULT_TEST_IMAGE` | Managed mode: overrides the container image tag the harness would otherwise pin from `specifications/test-matrix.json`, when running under a container runtime instead of a binary |
| `BASTIONVAULT_TEST_FORCE_UNAVAILABLE` | Harness-only escape hatch that forces the "no server" path regardless of what is actually reachable, so `ITG-003`'s skip behaviour itself has a test |

When neither an external address nor a way to start a managed server is available, the **whole
suite skips** with the exact reason `no BastionVault test server available` (`ITG-003`) — it is
never silently reported as passed and never failed for an environment that simply has no server.
Running the command above with no container runtime, no `bvault` on `PATH`, and an unreachable
path in `BASTIONVAULT_TEST_BIN` demonstrates exactly that skip.

## How fixtures are loaded

Every conformance fixture lives under `specifications/fixtures/**` as a JSON file matching the
schema in [Appendix C](../../specifications/appendix-c-conformance-fixtures.md). `FixtureRepository`
(`dotnet/BastionVault.IntegrationSdk.Tests/Harness/FixtureRepository.cs`) finds the repository root
by walking upward from the test assembly's own location until it finds a directory holding both
`specifications/` and `agents.md`, then reads fixtures **from that path at test time** — not from a
copy embedded in the project. A fixture change under `specifications/fixtures/` is therefore picked
up by every SDK's test suite the next time it runs, with nothing to regenerate or resync.

A fixture test built on `FixtureRepository` does, per fixture (`TST-011`): configures a client the
way the fixture's `client` block says, scripts a fake transport with the fixture's `exchanges`,
invokes the named operation with the fixture's `args`, asserts the exact request(s) the operation
sent (method, URL, headers subset, body as canonical JSON), and asserts either the typed result or
the error code, status, `Retryable` and `Details` keys the fixture's `expect` block names.

## Adding a requirement marker

Every test references the requirement IDs it verifies, so `tools/traceability` can report which
`AREA-NNN` IDs have coverage and which do not (`TST-040`, `TST-041`). In this SDK that marker is
the `[Requirement("...")]` attribute
(`dotnet/BastionVault.IntegrationSdk.Tests/Harness/RequirementAttribute.cs`), applied once per ID a
test method covers:

```text
[Fact]
[Requirement("KV2-004")]
public async Task Soft_deleted_read_is_success_with_no_data()
{
    // ...
}
```

The attribute allows multiple applications on one method, so a test that proves several
requirements at once — a fixture replay checking both the request shape and the error mapping,
say — names all of them rather than picking one arbitrarily. An integration scenario additionally
carries its `ITG-S<nn>` identifier (for example `ITG-S14`) alongside the requirement IDs it covers,
because section 15's "required scenarios" list is tracked separately from individual requirement
coverage.

## Coverage measurement

```bash
dotnet test dotnet/BastionVault.IntegrationSdk.Tests/BastionVault.IntegrationSdk.Tests.csproj
```

Coverage is wired directly into that project via `coverlet.msbuild` (not a separate `--collect`
flag): `Threshold=95`, `ThresholdType=line,branch`, `ThresholdStat=total`, so a coverage regression
fails the same `dotnet test` invocation everything else runs, rather than a separate, easy-to-skip
job. The HTML report and the Cobertura summary land under
`dotnet/BastionVault.IntegrationSdk.Tests/coverage/` and are what CI uploads as artifacts. The
docs-samples project collects **no** coverage of its own (`CNF-011` excludes example programs) —
do not add a second coverage mechanism there; it would measure the same assembly twice and could
disagree with the number above.

## Test data hygiene

Every token, password and key in a fixture is obviously fake (`s.FAKE…`, `password-fixture`); the
secret-scanning gate whitelists only the fixtures directory for that reason. Integration tests
generate their own credentials at run time and never commit one. Do not copy a fixture's fake
token into a new fixture without checking it still reads as fake — the string after `s.` must stay
under twenty alphanumeric characters, or the secret-scanning gate treats it as a real one.

## Next steps

- **Resilience and operations guide** — the production checklist that names running this suite
  against your own server version before every release.
- **Error reference** — every code a fixture or an integration scenario can assert against.
