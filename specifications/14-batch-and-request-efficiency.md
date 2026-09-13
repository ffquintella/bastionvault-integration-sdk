# 14 — Batch Operations and Request Efficiency

BastionVault ships an IP-based abuse guard (default 200 requests per 10 s, then a
300 s ban answered with `429` + `Retry-After`). An SDK that fans out one request per
listed object will ban its own user. This section specifies the three mechanisms the SDK
MUST offer so that well-behaved applications never trip the guard: the client rate gate,
the batch endpoint, and the bulk `*-info` listings with cursor pagination, plus the
cache-coherence endpoint.

## Client rate gate

- **EFF-001** The transport MUST route every outgoing request through a token-bucket
  rate gate configured by `RateGate { RatePerSecond = 8, Burst = 16 }` by default.
  Setting either to `0` disables the gate.
- **EFF-002** Waiters MUST be served FIFO.
- **EFF-003** On a `429` response carrying `Retry-After`, the gate MUST pause the whole
  queue for `min(Retry-After, 30s)`, drop accumulated tokens, and then resume. Requests
  already waiting MUST NOT fail; the request that received the 429 MUST fail with
  `BV-RATE-001` (it is not replayed).
- **EFF-004** On a `429` without `Retry-After` the gate MUST pause for 1 s.
- **EFF-005** Health probes performed by cluster discovery ([13](13-cluster-discovery-and-resilience.md))
  are exempt from the gate (they are exempt server-side too).
- **EFF-006** The current gate state (`Paused`, `PausedUntil`, `AvailableTokens`) MUST be
  observable for diagnostics.

## Batch endpoint

```
POST /v2/sys/batch
```

Request:

```json
{
  "operations": [
    { "operation": "read",   "path": "secret/data/app/db" },
    { "operation": "write",  "path": "secret/data/app/ttl", "data": { "data": { "ttl": "300" } } },
    { "operation": "delete", "path": "secret/data/app/old" },
    { "operation": "list",   "path": "secret/metadata/app" }
  ]
}
```

Response:

```json
{
  "results": [
    { "status": 200, "path": "secret/data/app/db", "data": { "data": {"username":"admin"}, "metadata": {"version": 3} } },
    { "status": 204, "path": "secret/data/app/ttl", "data": { "version": 1 } },
    { "status": 204, "path": "secret/data/app/old", "data": null },
    { "status": 403, "path": "secret/metadata/app", "errors": ["permission denied"] }
  ]
}
```

### Operation surface

```
Sys.Batch(operations: BatchOperation[], options?) -> BatchResult[]

BatchOperation { Operation: Read|Write|Delete|List, Path: string, Data?: object }
BatchResult    { Status: int, Path: string, Data?: object, Errors: string[], Warnings: string[],
                 Error?: Error /* mapped per-op error, null when Status < 400 */ }
```

- **BAT-001** `Sys.Batch` MUST send exactly one HTTP request to `/v2/sys/batch` under the
  client's token and namespace.
- **BAT-002** The SDK MUST validate client-side, before sending, that the list is not
  empty (`BV-INPUT-002`) and does not exceed `BatchMaxOperations` (default 128,
  configurable; `BV-INPUT-003`).
- **BAT-003** Paths MUST be full logical paths including the mount (`secret/data/x`),
  never prefixed with `/v1/`. A leading `/` MUST be stripped.
- **BAT-004** `Data` MUST be required for `Write` and rejected (`BV-INPUT-001`) for the
  other kinds.
- **BAT-005** Each `BatchResult` with `Status >= 400` MUST carry an `Error` mapped
  through the same rules as a standalone response ([04](04-error-model.md)), using the
  per-op `status` and `errors[]`. The overall call MUST succeed even when every op failed;
  callers inspect per-op results.
- **BAT-006** The overall call fails only when the HTTP request itself fails
  (400 for an oversized/empty batch → `BV-INPUT-003`; 403 → `BV-AUTHZ-001` on
  `sys/batch`; 404 "path not supported" → `BV-SERVER-004`, meaning the server predates
  batching).
- **BAT-007** The SDK MUST provide a typed helper `Kv.ReadMany(mount, paths[]) -> Map<path, KvSecret | Error>`
  built on `Sys.Batch` that composes KV v2 `data/` paths and unwraps results; it MUST
  fall back to sequential reads through the rate gate when `BV-SERVER-004` is returned.
- **BAT-008** Batches are sequential and non-transactional on the server. The SDK MUST
  document this and MUST NOT imply atomicity in any name (`BatchWrite`, not
  `Transaction`).

## Bulk metadata listings (`*-info`) and cursor pagination

Seven endpoints share one contract (`GET`, query parameters `after` and `limit`):

| Endpoint | Area | Records |
|----------|------|---------|
| `GET /v2/{mount}/certs-info` | PKI | issued certificates (no PEM) |
| `GET /v2/{mount}/csr-info` | PKI | outgoing CSRs |
| `GET /v2/{mount}/sign-request-info` | PKI | inbound sign-request queue |
| `GET /v2/{mount}/roles-info` | SSH | role configurations |
| `GET /v2/{mount}/targets-info` | Cert lifecycle | targets + renewer `state` |
| `GET /v2/auth/{mount}/users-info` | Userpass | users + `registered_keys`, `fido2_enabled` |
| `GET /v2/sys/namespaces-info` | Sys | child namespaces |

Response:

```json
{ "keys": ["a", "b"], "records": [ {...}, {...} ], "total": 1200, "next": "b", "truncated": true }
```

### Operation surface

```
Page<T> { Keys: string[], Records: T[], Total: int, Next: string?, Truncated: bool }

Pki.ListCertificatesInfo(mount, after?, limit?)      -> Page<CertificateSummary>
Pki.ListCsrInfo(mount, after?, limit?)               -> Page<CsrSummary>
Pki.ListSignRequestsInfo(mount, after?, limit?)      -> Page<SignRequestSummary>
Ssh.ListRolesInfo(mount, after?, limit?)             -> Page<SshRoleSummary>
CertLifecycle.ListTargetsInfo(mount, after?, limit?) -> Page<TargetSummary>
Auth.Userpass.ListUsersInfo(mount, after?, limit?)   -> Page<UserSummary>
Sys.ListNamespacesInfo(after?, limit?)               -> Page<NamespaceSummary>
```

- **PAG-001** `limit` MUST default to 100 client-side when omitted and MUST be validated
  to `1 ≤ limit ≤ 500` (`BV-INPUT-004`); the server caps at 500.
- **PAG-002** `after` is a **key**, not an offset. The SDK MUST pass the previous page's
  `Next` verbatim and MUST NOT attempt arithmetic on it.
- **PAG-003** `Next` MUST be exposed as absent/null when the server returns an empty
  string; `Truncated == false` on the last page.
- **PAG-004** Each area MUST offer an iterator/stream helper (`ListCertificatesInfoAll`,
  `IterCertificatesInfo`, …) that walks pages until `Truncated == false`, honouring the
  rate gate, with an optional `MaxRecords` safety cap (default 5000 → `BV-INPUT-005`
  when exceeded, with `Total` in the error details).
- **PAG-005** `Records[i]` MUST correspond to `Keys[i]`; the SDK MUST expose them zipped
  (record carries its key) and MUST fail with `BV-PROTOCOL-002` if lengths differ.
- **PAG-006** On `BV-SERVER-004` (server predates the route) the `*Info` helpers MUST
  raise that error unchanged; a higher-level "list with fallback" helper MAY be offered
  and MUST document that fallback costs `1 + N` requests.
- **PAG-007** A cursor past the end yields an empty, non-truncated page, not an error.

## Cache coherence — `sys/cache/version`

```
GET /v2/sys/cache/version?topics=pki/,auth/userpass/
GET /v2/sys/cache/version?topics=pki/&watch=1        (long-poll ≈25 s)
→ 200 { "version": 412, "topics": { "pki/": 17, "auth/userpass/": 3 }, "coarse": false }
→ 304 when If-None-Match matches the current aggregate
```

### Operation surface

```
Sys.CacheVersion(topics: string[], watch?: bool, ifNoneMatch?: string) -> CacheVersion | NotModified
CacheVersion { Version: int, Topics: Map<string,int>, Coarse: bool, ETag: string }
```

- **CCH-001** The SDK MUST send at most 64 topics (`BV-INPUT-004` otherwise) and MUST
  send them comma-joined in a single `topics` parameter.
- **CCH-002** The SDK MUST send `If-None-Match` when `ifNoneMatch` is given and MUST map
  a `304` to a distinct `NotModified` result, not an error.
- **CCH-003** When `watch` is true the per-call timeout MUST be raised to at least 40 s
  regardless of `Timeout`.
- **CCH-004** The SDK MUST document that epochs are per node and reset on restart; only
  an *increase* is a change signal. The SDK MUST NOT invalidate on a decrease.
- **CCH-005** A topic absent from the response means "not authorised or unknown"; the SDK
  MUST NOT synthesise a `0`.
- **CCH-006** An optional `CacheWatcher` helper that loops `watch=1` and raises change
  events per topic MAY be provided; if provided it MUST back off exponentially on
  transport errors and stop on `BV-AUTHZ-001`.

## Guidance the SDK documentation MUST include

- Never `map(read)` over a list; use `Kv.ReadMany`, `Sys.Batch`, or the `*-info` pages.
- Cache reads locally with a TTL and use `Sys.CacheVersion` to invalidate early.
- A `429` from the guard means the *client* misbehaved; the fix is fewer requests, not
  more retries.
