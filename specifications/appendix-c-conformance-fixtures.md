# Appendix C — Conformance Fixtures

Fixtures are language-neutral JSON documents in [`specifications/fixtures/`](fixtures/)
that every SDK replays through its fake transport (TST-010 … TST-013). They pin the wire
contract of [03](03-transport-and-protocol.md), the error mapping of [04](04-error-model.md)
and [Appendix B](appendix-b-error-catalogue.md), and the typed behaviour of 05–14.

## Layout

```
specifications/fixtures/
  schema/fixture.schema.json        JSON Schema (draft 2020-12) every fixture validates against
  transport/*.json                  headers, methods, URL encoding, envelope parsing, status handling
  errors/*.json                     server-message recognition and hint enrichment
  auth/*.json                       login flows, token store
  kv/*.json                         KV v1 and v2
  sys/*.json                        system API
  transit/*.json, totp/*.json, pki/*.json, ssh/*.json, identity/*.json
  resilience/*.json                 discovery, probe, pick, failover (resolver + probe scripting)
  efficiency/*.json                 batch, pagination, cache version, rate gate
```

## Fixture schema (summary)

```json
{
  "id": "kv.v2.read-secret.latest",             // unique, dotted
  "title": "...",
  "requirements": ["KV2-001", "TRN-040"],        // requirement IDs this fixture verifies
  "level": "core" | "standard" | "complete",
  "sections": ["07"],
  "client": {                                    // ClientConfig subset (canonical names)
    "address": "https://vault.example.com:8200",
    "token": "s.FAKEtoken…",
    "namespace": "",
    "apiPrefix": "v1",
    "settings": { "RateGate": { "RatePerSecond": 0 } }
  },
  "environment": { "BASTIONVAULT_ADDR": "..." }, // optional env vars to inject
  "operation": { "name": "Kv.V2.ReadSecret", "args": { "mount": "secret", "path": "app/db" } },
  "exchanges": [                                 // scripted transport, in order
    {
      "expectRequest": {
        "method": "GET",
        "url": "https://vault.example.com:8200/v1/secret/data/app/db",
        "headers": { "X-BastionVault-Token": "s.FAKEtoken…", "Accept": "application/json" },
        "absentHeaders": ["X-BastionVault-Namespace", "Cookie"],
        "body": null                              // canonical JSON compared structurally; null = no body
      },
      "respond": { "status": 200, "headers": { "Content-Type": "application/json" }, "body": { ... } }
      // or "fail": "connection_refused" | "timeout" | "tls_verify" | "tls_handshake" | "reset"
    }
  ],
  "expect": {
    "result": { ... }                            // typed result, canonical field names; "$absent" marks null/None
    // or
    "error": { "code": "BV-KV-003", "statusCode": 400, "retryable": false,
               "detailsKeys": ["current_version"], "hintContains": ["cas"], "serverMessage": "..." }
  },
  "resolver": { "_bvault._tcp.vault.corp.example": [ {"priority":10,"weight":50,"port":8200,"target":"bv-1.corp.example"} ] },
  "clock": { "start": "2026-09-13T12:00:00Z", "advance": ["PT2S"] }   // for auto-renew / backoff fixtures
}
```

- **FIX-001** Every fixture MUST validate against `schema/fixture.schema.json`; the
  SDK test harness MUST validate before running.
- **FIX-002** Request comparison: `method` and `url` exact; `headers` listed MUST match
  exactly (case-insensitive names); `absentHeaders` MUST be absent; `body` compared as
  canonical JSON (key order irrelevant). Headers not listed are not checked, except that
  a fixture MAY set `"strictHeaders": true`.
- **FIX-003** Result comparison: every field present in `expect.result` MUST match; extra
  fields in the actual result are ignored; `"$absent"` asserts null/None/absent;
  `"$any"` asserts presence; `"$redacted"` asserts a redacting type whose string form
  does not contain the value.
- **FIX-004** Error comparison: `code` exact; `statusCode`, `retryable` exact when given;
  `detailsKeys` all present; each `hintContains` substring present (case-insensitive);
  `serverMessage` exact when given.
- **FIX-005** Fixtures marked `"level": "standard"`/`"complete"` MAY be skipped by a
  lower-level SDK with a reason; `"core"` fixtures are mandatory for all.
- **FIX-006** Fixture tokens/passwords MUST be obviously fake (TST-050).

## Mandatory fixture set (v1.0.0)

The following fixtures ship with this specification version and MUST all pass. IDs are
stable; new fixtures append.

### transport
- `transport.headers.authenticated-read` — token header, Accept, User-Agent prefix, no cookie.
- `transport.headers.login-omits-token` — token absent on `auth/userpass/login/alice`.
- `transport.headers.namespace` — `X-BastionVault-Namespace: dti/esi`, trailing slash stripped.
- `transport.headers.reserved-rejected` — custom `X-Vault-Token` → `BV-CONFIG-008`.
- `transport.method.list-verb` — `LIST` verb on `secret/metadata/app/`.
- `transport.url.encoding-vectors` — 4 cases from bv-client tests (`web01 copy`, `db 01?env=us west&version=2`, bare `?`, absolute path).
- `transport.envelope.shape-a` / `shape-b` / `lease-id-empty` / `204` / `200-empty-body` / `non-json`.
- `transport.status.404-empty-read-returns-null`.
- `transport.status.405-empty` → `BV-PROTOCOL-001`.
- `transport.status.307-not-followed` → `BV-PROTOCOL-003`.
- `transport.status.429-dos-guard` — `errors[]` + `Retry-After: 17` → `BV-RATE-001`, `RetryAfter == 17`, gate paused.
- `transport.status.429-namespace-quota` — singular `error`, no header → `BV-RATE-002`.
- `transport.status.503-sealed` → `BV-SERVER-001`, `retryable false`, attempts 1.
- `transport.status.507-quota` → `BV-QUOTA-001`.
- `transport.retry.connection-refused-then-ok` — read retried, `Attempts == 2`.
- `transport.retry.write-not-retried` — `POST` with connection reset → `BV-TRANSPORT-001`, attempts 1.

### errors
- One fixture per recognition row in Appendix B §2 (`errors.recognition.<code>.<n>`), generated: raw `Logical.Read`/`Write` with the server text → expected code. A row with a qualifier group (`+ a/b/c`, or a parenthesised list) gets **one fixture per alternative**, each exercising that alternative alone: a message carrying every alternative at once passes whether the group is read as an alternation or as a conjunction, so it could not detect R-23 (D-M8-2, D-M8-3).
- `errors.enrichment.403-no-namespace`, `errors.enrichment.404-kv2-hint`, `errors.enrichment.api-version-mismatch`, `errors.enrichment.tls-no-ca`, `errors.enrichment.connection-refused-default-address`.
- `errors.format.one-line` — exact `ToString` per ERR-002 with redacted `lookup/<token>` path.

### auth
- `auth.userpass.login-ok`, `auth.userpass.login-200-rejected` (→ `BV-AUTH-004`), `auth.userpass.locked` (`retry_after_secs`), `auth.userpass.totp-required`.
- `auth.appid.login-ok-with-machine-token-and-namespace`, `auth.appid.invalid-secret-id-400`, `auth.appid.gated-403`, `auth.appid.env-scope-derived`.
- `auth.ferrogate.requirement-unauthenticated`, `auth.ferrogate.enrolment-pending`.
- `auth.token.lookup-self-remaining-ttl`, `auth.token.renew-self-uses-renew-path`, `auth.token.create-reserved-meta-client-side`, `auth.token.revoke-self-clears-token`, `auth.token.lookup-unknown-404`.
- `auth.autorenew.schedule-and-renew` (clock-driven), `auth.autorenew.stops-on-403`.
- `auth.cert.disabled-server` → `BV-SERVER-004`.

### kv
- `kv.v1.read-with-lease`, `kv.v1.write-empty-data-rejected`, `kv.v1.list`.
- `kv.v2.read-latest`, `kv.v2.read-version-query`, `kv.v2.read-env-merged`, `kv.v2.read-env-strict-miss`, `kv.v2.read-soft-deleted-state`, `kv.v2.write-cas-ok`, `kv.v2.write-cas-mismatch`, `kv.v2.write-cas-required`, `kv.v2.write-env-and-envs-rejected`, `kv.v2.soft-delete-versions`, `kv.v2.undelete`, `kv.v2.destroy-then-read`, `kv.v2.metadata-read`, `kv.v2.list-trailing-slash`, `kv.v2.config-environments`, `kv.v2.env-scoped-token-requires-env`, `kv.read-many-batch`, `kv.read-many-fallback-on-unsupported`.

### sys
- `sys.health.active/standby/sealed/uninitialized`, `sys.seal-status.tn-swap`, `sys.info.tiers`, `sys.cluster-status.ok/forbidden`, `sys.mounts.two-fields`, `sys.mount.204`, `sys.remount.409-in-use`, `sys.policies.acl-read`, `sys.policy.legacy-rules-field`, `sys.policy.not-found`, `sys.capabilities-self.v2-pinned`, `sys.capabilities-self.namespace-not-operable`, `sys.namespaces.write-full-replace`, `sys.namespaces-info.page`, `sys.init.validation`, `sys.unseal.invalid-key`.

### transit / totp / pki / ssh / identity
- `transit.encrypt-decrypt`, `transit.unknown-key-500-mapped`, `transit.below-min-decryption`, `transit.ciphertext-format-client-side`, `transit.random-cap`.
- `totp.generate-mode-create`, `totp.wrong-mode`, `totp.validate-false`.
- `pki.issue`, `pki.certs-info-page`, `pki.role-not-found`.
- `ssh.sign`, `ssh.creds-ip-not-allowed`, `ssh.verify-invalid-otp`, `sshbroker.effective-v2-pinned`.
- `identity.sharing.target-base64url`, `identity.self`.

### resilience
- `resilience.address.classification` (table-driven), `resilience.srv.sorted-and-verbatim-underscore`, `resilience.probe.classify` (5 bodies), `resilience.pick.priority-floor`, `resilience.pick.leader-over-follower-rtt-weight`, `resilience.pick.none-healthy`, `resilience.failover.read-once`, `resilience.failover.write-never`, `resilience.failover.not-armed-single-candidate`, `resilience.backoff.math-seeded`.

### efficiency
- `efficiency.rategate.fifo-throughput`, `efficiency.rategate.pause-on-429`, `efficiency.batch.per-op-errors`, `efficiency.batch.too-large-client-side`, `efficiency.pagination.cursor-passthrough`, `efficiency.pagination.zip-mismatch-protocol-error`, `efficiency.cache-version.304-not-modified`, `efficiency.cache-version.topics-limit`.

## Authoring rules

- **FIX-010** Response bodies MUST be copied from a real server exchange (captured with
  debug logging against the version in `test-matrix.json`), then minimised; the source
  server version is recorded in `"capturedFrom"`.
- **FIX-011** A fixture MUST test one behaviour; use several fixtures rather than one
  with many exchanges, except for flows that are inherently multi-request (failover,
  auto-renew, pagination iterators, `ReadMany` fallback).
- **FIX-012** Changing an existing fixture's expectations is a specification change and
  MUST be reflected in the changelog of this spec.
