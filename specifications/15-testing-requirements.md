# 15 — Testing Requirements

Testing is a first-class part of the specification: an SDK that passes its tests but does
not follow this section is non-conformant. The 95 % coverage rule is defined in
[01 — CNF-010](01-conformance-and-quality.md#test-coverage--the-95--rule).

## Test pyramid

| Layer | Purpose | Server needed | Share of suite |
|-------|---------|---------------|----------------|
| **Unit** | Pure logic: config resolution, URL encoding, envelope parsing, error mapping, hint enrichment, ranking, backoff maths, catalog integrity | no | ~45 % |
| **Conformance** | Replay the shared fixtures in [Appendix C](appendix-c-conformance-fixtures.md) through the fake transport; assert request bytes and typed results | no | ~30 % |
| **Contract (mock server)** | Run the SDK against an in-process HTTP server that implements the protocol rules of [03](03-transport-and-protocol.md) (custom `LIST`, 204, empty 404, 429 + `Retry-After`, TLS) | in-process | ~10 % |
| **Integration (live server)** | Run the SDK end to end against a real BastionVault server: bootstrap, every engine, auth flows, failure modes, cluster behaviour | yes | ~15 % |

- **TST-001** Unit, conformance and contract tests MUST run with no network access and
  MUST complete in under 60 s on CI hardware.
- **TST-002** Integration tests MUST be tagged (`[Trait("Category","Integration")]`,
  `#[ignore]` + feature flag, `@pytest.mark.integration`) and MUST be skipped with an
  explicit reason unless `BASTIONVAULT_TEST_ADDR` is set or the harness can start a
  server itself (see [Integration tests](#integration-tests-against-a-live-server)).
- **TST-003** Every test MUST be deterministic: no wall-clock sleeps (inject a clock) in
  unit/conformance/contract tests, no random without a seeded source, no dependence on
  test order. Integration tests MAY wait on real time but MUST use bounded polling with
  a timeout, never fixed sleeps longer than 1 s.
- **TST-004** Coverage (CNF-010) is computed on unit + conformance + contract tests so
  the figure is reproducible offline. Integration tests MUST additionally be run on the
  default branch and MUST pass; their coverage MAY be published as a separate
  informational figure.

## What MUST be tested — by area (offline layers)

### Configuration ([02](02-client-configuration.md))
- Precedence table (explicit > `BASTIONVAULT_*` > `VAULT_*` > token file > default) for
  every setting that has an env var.
- Every `BV-CONFIG-*` code is produced by at least one invalid configuration.
- Boolean and duration parsing accept/reject sets (CFG-003, CFG-004).
- `TlsSkipVerify` logs exactly one warning and sets `IsInsecure`.
- `WithNamespace` views share token, differ in namespace; `SetToken` visible to views.
- `BVTOK1:` token file → `BV-CONFIG-010`.

### Transport ([03](03-transport-and-protocol.md))
- Method mapping incl. literal `LIST`.
- Header set for: authenticated request, unauthenticated request, login path (token
  omitted), namespace set/unset, custom headers, reserved-header rejection, `DPoP`.
- URL encoding vectors (at minimum the ones in Appendix C §URL).
- Envelope shape A / shape B detection; `lease_id: ""` → absent; 204 short-circuit;
  200 with empty/whitespace body → null; non-JSON body → `BV-PROTOCOL-002`.
- Every row of the status table, each error-body shape (singular `error`, `errors[]`,
  empty), `Retry-After` capture.
- 3xx (non-304) → `BV-PROTOCOL-003`, not followed.
- Proxy disabled by default; enabled honours env.

### Error model ([04](04-error-model.md))
- Catalog integrity: every code has message + hint, unique messages, `Retryable` matches
  ERR-006, every category prefix used only by its category.
- The mapping function (steps 1–5) for every row of Appendix B §2 (server-message
  recognition) — generated test from the table.
- Redaction: token-bearing paths and secret values never appear in `ToString`.
- Each hint-enrichment rule fires and does not fire.

### Authentication ([05](05-authentication.md))
- Each login flow builds the right request and stores the token; `200` without `auth`
  → `BV-AUTH-003` and refinements (AUT-011, AUT-012).
- `LookupSelf` fills `TokenInfo` and computes `RemainingTtl`; `RenewSelf` uses
  `renew/<own token>` with `increment`; `RevokeSelf` clears the token.
- Reserved `meta` keys rejected client-side.
- Auto-renew: schedules at the configured fraction, backs off on failure, stops on
  non-renewable, stops on `BV-AUTHZ-001`, re-logins for `Login` sources, emits events —
  all with an injected clock.
- Machine-token / namespace header behaviour for AppID login.

### KV ([07](07-kv-engine.md))
- v1 and v2 CRUD, list, version selector, CAS success/mismatch/required, soft-delete
  (via `DELETE data/` with `versions`), soft-deleted read → `SoftDeleted` state,
  undelete, destroy, metadata read/delete, config, environments (base, targeted patch,
  full multi-env, strict-miss 404, `resolved_env`/`available_envs`), env+envs rejection,
  env-scoped token guard.
- Mount-version detection and path rewriting.

### Every other engine (06, 08–12)
- One positive and one negative (error-mapped) test per typed operation, plus request
  body assertions for every field the operation can send.

### Resilience ([13](13-cluster-discovery-and-resilience.md))
- Address classification table.
- SRV: sorted by priority, verbatim `_` names, resolver failure → no records, fallback
  literal, empty list for SRV-shaped names.
- Probe classification for each body; non-JSON → Unreachable.
- Picker: priority floor, leader > follower, RTT, weight, dominant cluster id, none.
- Failover: exactly one replay for read/list, none for write/delete, lock coalescing,
  excluded operations, `Reconnect`.
- Backoff maths with seeded jitter; `Retry-After` interplay; total attempt cap.

### Efficiency ([14](14-batch-and-request-efficiency.md))
- Rate gate: throughput, burst, FIFO, pause on 429 with/without `Retry-After`, cap 30 s,
  token drop on pause, disabled when 0.
- Batch: validation, per-op error mapping, overall success with all ops failed,
  `ReadMany` fallback on `BV-SERVER-004`.
- Pagination: limit validation, cursor passthrough, zip check, iterator stop, cap.
- Cache version: topics limit, `If-None-Match`/304, watch timeout, no synthetic zero.

## Conformance fixtures

- **TST-010** Fixtures are JSON files in `specifications/fixtures/**` (Appendix C defines
  the schema). Each SDK MUST load them at test time from the repository (not copy them),
  so a fixture change is picked up by every implementation.
- **TST-011** A fixture test MUST: (1) configure the client as the fixture says,
  (2) script the fake transport with the fixture's responses, (3) invoke the named
  operation with the fixture's arguments, (4) assert the exact request(s) (method, URL,
  headers subset, body as canonical JSON), (5) assert the typed result **or** the error
  code, status, `Retryable`, and `Details` keys.
- **TST-012** Fixtures MUST be run for every conformance level's applicable sections; a
  fixture tagged with an unsupported section is reported as skipped with reason.
- **TST-013** New behaviour MUST be accompanied by a fixture in the same change.

## Mock server contract

- **TST-020** The contract suite MUST start an in-process HTTPS server with a
  self-signed certificate and verify: CA pinning via `CaCertPem`, hostname mismatch →
  `BV-TRANSPORT-003`, mTLS client certificate presented, `TlsSkipVerify` bypass, custom
  `LIST` verb reaching the handler, keep-alive reuse (connection count), timeouts
  (`ConnectTimeout` and `Timeout`), cancellation, response size cap.
- **TST-021** The mock server MUST be able to simulate: sealed (503 + message), standby
  health (429), uninitialised (501), DoS ban (429 + `Retry-After` + `errors[]`),
  namespace quota (429 without header), 404 empty body, 405 empty body, 204, login
  failure as 200 + `data.error`.

## Integration tests against a live server

Integration tests prove that the offline layers model the real server correctly. They are
**mandatory** for every SDK (they are not optional extras), but they are skippable per
run when no server is available.

### Server provisioning

The integration harness MUST support two ways of obtaining a server, selected by
environment:

| Mode | Trigger | Behaviour |
|------|---------|-----------|
| **External** | `BASTIONVAULT_TEST_ADDR` is set | Use that server. `BASTIONVAULT_TEST_ROOT_TOKEN` MUST be set (or `BASTIONVAULT_TEST_UNSEAL_KEYS` so the harness can unseal and `BASTIONVAULT_TEST_ROOT_TOKEN` for admin). Optional: `BASTIONVAULT_TEST_CACERT`, `BASTIONVAULT_TEST_NAMESPACE`. |
| **Managed** | `BASTIONVAULT_TEST_ADDR` unset and a container runtime (`docker`/`podman`) or a `bvault` binary (`BASTIONVAULT_TEST_BIN`) is available | The harness starts a fresh single-node server on a random free port with a file or Hiqlite storage backend in a temp dir, initialises it (`secret_shares = 1`, `secret_threshold = 1`), unseals it, captures the root token, and tears it down at the end of the run. |

- **ITG-001** The harness MUST publish a shared `TestServer` object exposing `Address`,
  `RootToken`, `CaCertPem?`, `Version` (from `sys/info`) and `Mode`.
- **ITG-002** The managed mode MUST use the server image/binary version pinned in
  `specifications/test-matrix.json` (see [CI matrix](#ci-matrix)). The harness MUST
  print the resolved server version at the start of the run.
- **ITG-003** When neither mode is possible the whole integration suite MUST be
  **skipped** with the reason `no BastionVault test server available`, never silently
  passed and never failed.
- **ITG-004** Integration tests MUST be runnable locally with one documented command
  per SDK (README → "Running integration tests").
- **ITG-005** The harness MUST support a `BASTIONVAULT_TEST_TLS_SKIP_VERIFY=1` override
  for dev servers with self-signed certificates and MUST NOT need it in managed mode
  (the harness owns the CA it generated, or uses plain HTTP on loopback, which is allowed
  by CNF-035).

### Isolation and cleanup

- **ITG-010** Every test (or test class) MUST create its own mounts and auth mounts
  under a unique prefix (`it-<run-id>-<test>-…/`) and MUST delete them in teardown,
  even on failure. Tests MUST NOT touch the default `secret/`, `resources/`, `files/`,
  or `identity/` mounts except read-only checks.
- **ITG-011** Tests that need policies, users, roles, namespaces, or tokens MUST create
  uniquely named ones and revoke/delete them in teardown.
- **ITG-012** Tests MUST run correctly in parallel against one server (no shared mutable
  server state other than the root token) except tests explicitly marked `serial`
  (seal/unseal, DoS config, namespace quotas, `require_machine_identity`).
- **ITG-013** Tests MUST be idempotent: a rerun after an aborted run MUST not fail because
  of leftover state; a `Cleanup-Orphans` helper MUST remove any `it-*` mounts older than
  one hour at suite start.

### Required scenarios

The following scenarios MUST exist in every SDK's integration suite (Core level unless
marked). Each scenario MUST assert the typed result **and**, for negative cases, the
exact `Error.Code`.

**Bootstrap and system**
1. `Sys.Health` on the live node returns `Active` (200) and `Sys.SealStatus` reports
   `Sealed == false`; `Sys.ServerInfo` unauthenticated omits `Version`, authenticated
   includes it, and `Client.ServerVersion()` caches it.
2. *(serial, managed mode only)* `Sys.Seal` → health `Sealed`; any KV read →
   `BV-SERVER-001`; `Sys.Unseal(key)` → `Active` again; unseal on an unsealed vault is a
   no-op 200.
3. Mount lifecycle: `Sys.Mount(kv-v2)` → appears in `Sys.ListMounts` with only `type` and
   `description`; `Sys.Remount` → old path gone, new path present; remount to an existing
   path → `BV-CONFLICT-004`; `Sys.Unmount`.
4. Policy lifecycle: write (HCL), read (`policy` field), list contains it, history has one
   `write` entry, delete → read returns `BV-NOTFOUND-005`; legacy `sys/policy/{name}`
   returns the `rules` field.
5. `Sys.CapabilitiesSelf` for a token with a restrictive policy returns exactly the
   granted capabilities and `namespace_operable == true`; `deny` path returns `["deny"]`.
6. *(Standard)* Namespaces: create `it-ns` with quotas, read it back (full replace
   semantics: omitted quota resets to 0), `ListNamespacesInfo` pages it, `NamespacesSelf`
   for a root token lists it, delete; a token bound to the namespace reading a root mount
   → `BV-AUTHZ-001` with `namespace_operable == false` in `Details` after
   `CapabilitiesSelf`.

**Authentication**
7. Userpass: enable mount, create user with policy, login OK (`AuthInfo` populated,
   token works); wrong password → `BV-AUTH-004`; disabled user → `BV-AUTH-005`; after
   `max_failed_attempts` wrong passwords → `BV-AUTH-006` with `retry_after_secs`;
   `Unlock` restores login.
8. AppID: enable mount, set `config.require_machine = false` (serial), create role with
   `bypass_machine_binding` or gate off, read role-id, generate secret-id (with
   `num_uses = 1`), login OK, second login with the same secret-id → `BV-AUTH-010`;
   wrong role-id → `BV-AUTH-010`.
9. *(Standard)* AppID with `bound_source_ips` excluding the test client → `BV-AUTHZ-001`.
10. Token store: `Create` child with subset policies; `Lookup` child; `RenewSelf` on a
    renewable token extends `RemainingTtl`; `Revoke` child → `Lookup` → `BV-NOTFOUND-006`;
    `RevokeSelf` on a non-root token → subsequent request `BV-AUTHZ-001`; `Create` with a
    reserved `meta` key is refused client-side and, when bypassed via `Logical.Write`,
    server-side (400).
11. Auto-renew (real clock, short TTL ≈ 4 s, `RenewAtFraction = 0.5`): at least one
    `OnRenewed` event within 6 s and the token still valid at 7 s.
12. *(Complete)* FerroGate: `Requirement` is readable unauthenticated on a fresh mount;
    `Enroll` with `self_enroll_enabled = true` creates a `pending` machine visible to
    `Admin.ListMachines`; `Status` for an unknown token reports `unknown` or a verify
    error (no crash).

**KV**
13. KV v1: write, read (flat data), list, delete → read returns null; write with `ttl`
    yields `LeaseDuration` and `Renewable == true`.
14. KV v2 lifecycle: write v1 → `version == 1`; write v2; read latest = v2; read
    `version = 1`; CAS write with `cas = 2` OK, `cas = 1` → `BV-KV-003`; enable
    `cas_required` on config → write without cas → `BV-KV-004`; soft-delete latest → read
    returns `SoftDeleted` state with `DeletionTime`; `Undelete` → readable; `Destroy(1)`
    → read v1 → `BV-KV-005`; metadata lists both versions with `destroyed` flags; list
    `metadata/` shows the key; delete metadata → read → null.
15. KV v2 `max_versions = 2` on config → after three writes, version 1 is gone from
    metadata.
16. KV v2 environments: full multi-env write, read base, read `env=prod` → merged +
    `resolved_env`/`available_envs`; targeted `env=staging` patch preserves prod; read
    `env=dev` (undeclared) → `BV-KV-006`; body with both `env` and `envs` → `BV-INPUT-001`
    client-side and `400` server-side via `Logical.Write`; config `environments` registry
    round-trips.
17. `Kv.ReadMany` over 5 secrets via `Sys.Batch` returns all five; one path the token
    cannot read comes back as `BV-AUTHZ-001` in its slot while the others succeed.

**Transit (Standard)**
18. Create `chacha20-poly1305` key, encrypt/decrypt round trip, rotate, rewrap yields
    `key_version == 2`, decrypt of old ciphertext still works, `min_decryption_version =
    2` → decrypt v1 → `BV-TRANSIT-004`; delete without `deletion_allowed` →
    `BV-TRANSIT-003`; config `deletion_allowed` then delete OK.
19. `ed25519` sign/verify true, tampered input false; `ml-dsa-65` sign/verify; `hmac`
    key hmac/verify; `ml-kem-768` datakey plaintext → unwrap equals plaintext; `random`
    32 bytes; `hash sha2-512`.

**TOTP (Standard)**
20. Generate-mode key returns `key`, `url`, `barcode`; `Code(name)` returns 6 digits;
    provider-mode key from `url`; validate the code produced by a local RFC 6238
    implementation → `valid == true`; replay → `valid == false`; GET on a provider key →
    `BV-TOTP-002`.

**PKI (Complete)**
21. Generate internal root (`ec`), create role, issue cert → parses, chain verifies to
    the root; sign a CSR; read cert by serial; `ListCertificatesInfo` pages it with
    `common_name`; revoke → CRL contains serial; `crl/rotate`; tidy.
22. Intermediate flow: `intermediate/generate/internal` → CSR; `root/sign-intermediate`;
    `intermediate/set-signed`; issue from the intermediate with `issuer_ref`.

**SSH (Complete)**
23. `config/ca` generate → `public_key`; CA role; sign a local ed25519 public key →
    `signed_key` parses as an OpenSSH certificate with the requested principals; OTP
    role with `cidr_list` → `creds` for an in-range IP → `verify` returns the role; IP
    out of range → `BV-SSH-003`; `verify` with a bogus OTP → `BV-SSH-004`.

**Files / Resources / Identity (Complete)**
24. Files: create with `content_base64`, read content equals bytes, second write creates
    a version, restore version 1, delete.
25. Resources: create a resource and a secret under it, list secrets, secret history has
    one version, rename resource → secrets follow, delete.
26. Identity: create a user group with the test user and a policy; user login policies
    include it; `identity/entity/self` for the user token returns `username`; share a KV
    secret to the group via `Sharing.Put`; `Sharing.ForMe` for the user lists it.

**Efficiency and resilience**
27. Rate gate: issue 300 reads in a tight loop against a server with default DoS config;
    no `BV-RATE-001` occurs and total time ≥ (300 − 16)/8 seconds (proves the gate
    engaged).
28. *(serial)* Set `sys/dos/config` `max_requests = 20`, disable the client gate, fire 40
    requests → at least one `BV-RATE-001` with `RetryAfter > 0`; gate re-enabled →
    queue pauses and drains; restore config and unban the test IP.
29. `Sys.CacheVersion(["<test-mount>/"])` epoch increases after a write to that mount;
    `If-None-Match` with the current ETag → `NotModified`.
30. Pagination: create 12 userpass users, `ListUsersInfo(limit = 5)` walks 3 pages, keys
    strictly increasing, `Truncated` false on the last, iterator yields 12.
31. `Logical.Raw` to an unknown `sys` path → `BV-NOTFOUND-001` with `HTTP 404 (no body)`;
    `PATCH` via `Logical.Raw` → `BV-PROTOCOL-001` (405).
32. *(managed multi-node, optional but MUST exist as a test that skips when unavailable)*
    Start three nodes with an injected SRV resolver → `Client.Discover()` ranks a leader
    first; stop the pinned node → next read fails over exactly once and succeeds; a
    write during the outage → `BV-DISCOVERY-003` with no replay.

### Assertions the integration harness MUST make globally

- **ITG-020** Every response the SDK returned during the run had a recognised shape (no
  `BV-PROTOCOL-002`) — a protocol error against a supported server version is a suite
  failure, not a test skip.
- **ITG-021** No secret material appeared in the captured SDK logs at debug level
  (TST-051 applied to the whole run).
- **ITG-022** The observability hook received one event per attempt with non-empty
  `request_id`-independent fields (method, path, status, duration).
- **ITG-023** A summary lists, per specification section, how many typed operations
  were exercised against the live server; the release checklist requires ≥ 90 % of the
  conformance level's typed operations to appear in that summary.

### CI matrix

- **ITG-030** `specifications/test-matrix.json` MUST list the server versions every SDK
  runs integration tests against: the **minimum supported** version and the **latest
  released** version (and optionally `main`). CI MUST run the integration suite against
  each listed version at least nightly and on release branches; pull requests MUST run
  it against the latest released version.
- **ITG-031** A test that depends on an endpoint introduced after the minimum version
  MUST check `TestServer.Version` and skip with reason `requires server >= X` rather than
  fail; the skip MUST be counted and reported.
- **ITG-032** Results per server version MUST be published as CI artifacts (JUnit or
  equivalent) alongside the coverage report.

## Coverage measurement

| Language | Tool | Command (reference) |
|----------|------|---------------------|
| .NET | coverlet + ReportGenerator | `dotnet test --collect:"XPlat Code Coverage"` with `Threshold=95`, `ThresholdType=line,branch`, `ThresholdStat=total` |
| Rust | `cargo-llvm-cov` | `cargo llvm-cov --lib --branch --fail-under-lines 95 --fail-under-branches 95` (when branch coverage is unavailable on the toolchain, line and region coverage MUST both be ≥ 95) |
| Python | `coverage.py` via pytest-cov | `pytest -m "not integration" --cov=bastionvault_integration_sdk --cov-branch --cov-fail-under=95` |

- **TST-030** Coverage MUST include line and branch (or the tool's closest equivalent,
  documented). Exclusion pragmas (`[ExcludeFromCodeCoverage]`, `# pragma: no cover`,
  `#[cfg(not(coverage))]`) are **forbidden** in library code except on platform-specific
  blocks that cannot execute on the CI OS, each with a justification comment.
- **TST-031** CI MUST publish the HTML report and a machine-readable summary
  (Cobertura/LCOV) as artifacts.

## Traceability

- **TST-040** Every test MUST reference the requirement IDs it verifies through a
  machine-readable marker: an attribute/trait (`[Requirement("KV2-004")]`), a test-name
  suffix (`_kv2_004`), or a docstring tag (`@req KV2-004`). Integration scenarios use
  `ITG-S<nn>` (e.g. `ITG-S14`) in addition to the requirement IDs they cover.
- **TST-041** A script in the repository (`tools/traceability`) MUST produce a report
  mapping every applicable requirement ID (from Appendix D) to the tests that cover it
  and MUST fail CI when an applicable MUST requirement has zero tests or a required
  integration scenario is missing.
- **TST-042** The report MUST be attached to each release (CNF release checklist).

## Test data hygiene

- **TST-050** Tokens, passwords, and keys in fixtures MUST be obviously fake
  (`s.FAKE…`, `password-fixture`) and the secret-scanning gate (CNF-025) MUST whitelist
  only the fixtures directory. Integration tests MUST generate credentials at run time
  and never commit them.
- **TST-051** Tests MUST assert that secret material does not appear in logs captured
  during the test (attach a capturing logger in the fixture harness).

## Mutation testing (recommended)

- **TST-060** Implementations SHOULD run mutation testing (Stryker.NET, cargo-mutants,
  mutmut) on the error-mapping and envelope-parsing modules at least per release and
  SHOULD document the mutation score.
