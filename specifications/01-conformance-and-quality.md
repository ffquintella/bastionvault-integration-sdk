# 01 — Conformance and Quality

## Conformance levels

An implementation declares one of three conformance levels in its README.

| Level | Required sections | Intended for |
|-------|-------------------|--------------|
| **Core** | 00, 01, 02, 03, 04, 05 (Token + AppID + Userpass), 06 (health, seal-status, capabilities, token ops), 07 (KV v1 + v2), 13 (retry policy only), 15, 16, 17 (guides 1–5) | Application integration: read/write secrets, authenticate |
| **Standard** | Core + 05 (all methods), 06 (all), 08, 11, 13 (full), 14 | Platform teams, CI/CD tooling, encryption-as-a-service |
| **Complete** | Standard + 09, 10, 12 | Administration tooling, GUIs, operators |

- **CNF-001** An SDK MUST implement every MUST requirement of every section in its
  declared level.
- **CNF-002** An SDK MUST NOT claim a level whose sections contain unimplemented MUST
  requirements. Partially implemented sections MUST be listed under a "Known gaps"
  heading in the README with the requirement IDs that are missing.
- **CNF-003** The three reference SDKs in this repository (.NET, Rust, Python) target
  **Complete**.

## Test coverage — the 95 % rule

- **CNF-010** Every SDK MUST achieve **at least 95 % line coverage and 95 % branch
  coverage** of its shipped library code, measured by the coverage tool listed in
  [15 — Coverage measurement](15-testing-requirements.md#coverage-measurement).
- **CNF-011** Coverage MUST be measured over the library package(s) only. Test code,
  generated code explicitly marked as generated, and example programs are excluded.
  No other exclusions are permitted; in particular, error-handling branches and
  hint-generation code are **in scope**.
- **CNF-012** The CI pipeline MUST fail when either coverage figure falls below 95 %.
- **CNF-013** The coverage report MUST be published as a CI artifact for every build of
  the default branch and every pull request.
- **CNF-014** Every requirement ID in this specification that applies to the SDK's
  conformance level MUST be referenced by at least one test (see
  [15 — Traceability](15-testing-requirements.md#traceability)).
- **CNF-015** All fixtures in [Appendix C](appendix-c-conformance-fixtures.md) applicable
  to the conformance level MUST be exercised by the test suite.

## Quality gates

An SDK release MUST pass all of the following in CI:

| Gate | Requirement |
|------|-------------|
| Build | **CNF-020** Builds with zero warnings under the language's strict/pedantic mode (`TreatWarningsAsErrors`, `#![deny(warnings)]` in CI, `mypy --strict` + `ruff`). |
| Unit + conformance tests | **CNF-021** 100 % pass; no skipped tests except those tagged as requiring a live server. |
| Coverage | **CNF-022** ≥ 95 % line and branch (CNF-010). |
| Lint / static analysis | **CNF-023** Language-standard linter passes with the project's rule set committed to the repo. |
| Dependency audit | **CNF-024** No known critical/high vulnerabilities in dependencies (`dotnet list package --vulnerable`, `cargo audit`, `pip-audit`). |
| Secret scanning | **CNF-025** No token-looking strings (`s.[A-Za-z0-9]{20,}`, `hvs.`) committed outside fixture files that are explicitly labelled as fake. |
| Docs | **CNF-026** Documentation build succeeds; every public symbol has a doc comment; every code sample in the docs compiles/runs (see [16](16-documentation-requirements.md)). |
| Public API diff | **CNF-027** A breaking public-API change requires a major version bump; CI MUST run an API-compatibility check against the previous release. |

## Security baseline

- **CNF-030** TLS certificate verification MUST be enabled by default. Disabling it MUST
  require an explicit configuration flag and MUST emit a warning-level log line once per
  `Client` instance.
- **CNF-031** Secret material (tokens, passwords, secret IDs, unseal keys, private keys,
  KV data) MUST NOT appear in logs, exception messages, or `ToString`/`Debug`/`repr`
  output at any log level. The token MAY be shown redacted as the first 4 characters
  followed by `…` when debug logging is explicitly enabled.
- **CNF-032** Types holding secret material MUST redact in their default string
  representation and SHOULD zero memory on disposal where the runtime permits.
- **CNF-033** The SDK MUST NOT write tokens to disk unless the application explicitly
  enables the token-helper file (see [02 — Token helper](02-client-configuration.md#token-helper-file)).
- **CNF-034** The SDK MUST NOT follow redirects to a different host, scheme, or port than
  the configured address except the leader redirect described in
  [03](03-transport-and-protocol.md#redirects), which is same-cluster only.
- **CNF-035** The SDK MUST reject plaintext `http://` addresses unless `AllowInsecureHttp`
  is set, except for loopback addresses (`127.0.0.1`, `::1`, `localhost`), which MAY use
  `http://` without the flag.

## Versioning and compatibility

- **CNF-040** SDK releases MUST follow Semantic Versioning 2.0.0.
- **CNF-041** Each release MUST record the specification version it implements and the
  minimum server version it was tested against.
- **CNF-042** New server endpoints MAY be added in a minor release. Removing or changing
  the signature of a public operation is a major release.
- **CNF-043** When the server lacks an endpoint (`404` with the router's
  "path not supported" message, see [04](04-error-model.md)), the SDK MUST surface
  `BV-SERVER-004 UnsupportedByServer` rather than a generic not-found, so callers can
  implement fallbacks (e.g. `*-info` → per-object reads).

## Release checklist

Every release MUST attach to its tag or release notes evidence that:

1. All quality gates (CNF-020 … CNF-027) passed on the release commit.
2. Coverage figures (line and branch) are stated.
3. The requirement traceability report ([15](15-testing-requirements.md#traceability))
   shows no untested applicable requirement.
4. The changelog lists added/changed/removed operations and any new error codes.
5. The README states conformance level, spec version, and tested server version(s).
