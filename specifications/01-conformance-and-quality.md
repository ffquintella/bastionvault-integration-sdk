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

### Specification provenance

This specification is *derived* from a BastionVault server release. When that server
changes, the specification may need to change with it, and today nothing says which
release the derivation was made from. CNF-044…CNF-047 make that link explicit and
machine-checkable, so a server release can be triaged against this specification without
re-reading either.

- **CNF-044** The repository MUST carry a machine-readable **specification provenance
  manifest** at `specifications/provenance.json`, recording the upstream BastionVault
  release this specification version was derived from and the pinned content hash of
  every upstream source that feeds a specification document.
- **CNF-045** Each manifest entry MUST name the upstream path, its **git object id** at
  the pinned ref, whether that object is a blob or a tree, and the specification
  documents it feeds. Git object ids are used so that one hash verifies identically
  against the GitHub API and against a local checkout.
- **CNF-046** A provenance tool MUST compare the manifest against a named upstream ref
  and report every pinned source whose object id changed, naming the specification
  documents that need review. It MUST work **without a local checkout** of the server.
  It MUST classify each pinned source into exactly one of four outcomes, and its exit
  status MUST distinguish them:

  | Outcome | Meaning | Exit status |
  |---------|---------|-------------|
  | `unchanged` | Object id matches the pin | contributes 0 |
  | `changed` | Object id differs at the compared ref | non-zero for an `authoritative` source; 0 for a `corroborating` one unless `--strict` is given |
  | `missing` | The pinned path no longer exists upstream | as `changed`, and MUST be reported distinctly from it — a renamed source needs the manifest repointed, which is not the same act as reviewing a changed one |
  | `unknown` | The comparison could not be performed (upstream unreachable, or the upstream API truncated its response) | non-zero, and distinct from both of the above |

  `unknown` MUST NOT be reported as an absence of drift. A checker that reports clean
  when it could not look is worse than no checker.
- **CNF-047** Each SDK MUST expose, as public metadata, both the specification version it
  implements and the upstream BastionVault release that specification version was derived
  from. This is distinct from CNF-041, which governs what a *release* records; CNF-047
  governs what the *library* exposes to the application that embeds it, so a deployed
  application can report what it was built against without the repository in hand.

## Release checklist

Every release MUST attach to its tag or release notes evidence that:

1. All quality gates (CNF-020 … CNF-027) passed on the release commit.
2. Coverage figures (line and branch) are stated.
3. The requirement traceability report ([15](15-testing-requirements.md#traceability))
   shows no untested applicable requirement.
4. The changelog lists added/changed/removed operations and any new error codes.
5. The README states conformance level, spec version, and tested server version(s).
6. A provenance check (CNF-046) was run against the upstream ref the release targets, and
   **no `authoritative` drift is left unreconciled** — each reported source has either
   been reviewed into the specification or been recorded as deliberately not applicable.
   `corroborating` drift does not block a release, but the release evidence MUST show the
   corroborating section, because a `corroborating`-only change is by construction
   invisible to exit status.
