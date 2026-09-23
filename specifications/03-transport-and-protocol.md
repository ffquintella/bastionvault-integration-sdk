# 03 — Transport and Protocol

This section fixes the wire contract between the SDK and a BastionVault server. It was
derived from the server's HTTP layer (`crates/bv-server`) and the reference Rust client
(`crates/bv-client`). Where the public documentation and the server code disagree, the
**server code wins** and the difference is called out with ⚠️.

## The logical layer

Every typed operation in sections 05–12 is built on four logical primitives:

```
Logical.Read  (path, options?) -> Response?          // GET
Logical.Write (path, body?, options?) -> Response?   // POST
Logical.Delete(path, body?, options?) -> Response?   // DELETE
Logical.List  (path, options?) -> Response?          // LIST verb
Logical.Raw   (method, absolutePath, body?, options?) -> RawResponse   // escape hatch
```

- **TRN-001** The SDK MUST expose these primitives publicly so applications can reach
  paths the typed surface does not cover (plugins, future engines).
- **TRN-002** `path` is a logical path relative to the API prefix, e.g. `secret/data/db`.
  A leading `/` MUST be stripped. The prefix (`v1` or `v2`) comes from `ApiPrefix` or the
  per-call `ApiVersion` option.
- **TRN-003** `Logical.Raw` MUST accept an absolute path (starting with `/`) and MUST NOT
  add the prefix — this is how version-pinned endpoints such as `/v2/sys/hsm/status` are
  reached.

## HTTP method mapping

| Operation | HTTP method | Body |
|-----------|-------------|------|
| Read | `GET` | none |
| Write | `POST` (server also accepts `PUT`) | JSON object or none |
| Delete | `DELETE` | optional JSON object (KV v2 `versions`) |
| List | **`LIST`** (custom verb) | none |

- **TRN-010** ⚠️ The server does **not** support `GET ...?list=true`; the query allowlist
  drops the parameter and the request becomes a plain read. The SDK MUST issue the
  literal HTTP method `LIST`. If the runtime's HTTP stack cannot send a custom verb, the
  SDK MUST fail at construction with `BV-CONFIG-009` rather than silently degrade.
- **TRN-011** List paths conventionally end with `/` (`secret/metadata/app/`). The SDK
  MUST preserve a trailing slash through URL encoding and MUST add one for typed list
  operations where the server expects it.

## Request headers

| Header | When | Value |
|--------|------|-------|
| `X-BastionVault-Token` | every authenticated request | the token |
| `X-BastionVault-Namespace` | when namespace is non-empty | slash path, trimmed |
| `Accept` | always | `application/json` (`application/octet-stream` for asset/blob downloads) |
| `Content-Type` | when a body is present | `application/json` |
| `User-Agent` | always | `bastionvault-sdk-<lang>/<version> (<runtime>)` plus application suffix |
| `If-None-Match` | cache-version / surface polling | quoted ETag |
| `DPoP` | FerroGate login only | DPoP proof (see [05](05-authentication.md)) |
| custom | from `Headers` | as configured |

- **TRN-012** Reserved headers that `Headers`/`RequestOptions.Headers` MUST NOT set:
  `X-BastionVault-Token`, `X-Vault-Token`, `Authorization`, `Cookie`,
  `X-BastionVault-Namespace`, `Content-Length`, `Host`. Violations → `BV-CONFIG-008`.
- **TRN-013** The token header MUST be `X-BastionVault-Token`. ⚠️ `X-Vault-Token`,
  `Authorization: Bearer` and the `token` cookie are accepted by the server as fallbacks
  only; the SDK MUST NOT rely on them and MUST NOT send cookies.
- **TRN-014** ⚠️ The namespace header is `X-BastionVault-Namespace`. `X-Vault-Namespace`
  is ignored by the server and MUST NOT be sent. The SDK MUST NOT combine the header with
  a namespace path prefix in the same request (the server refuses that).
- **TRN-015** The token header MUST be omitted on paths whose last segment is `login`
  (`auth/*/login`, `auth/*/login/{user}`) unless `RequestOptions.Token` is given
  explicitly, and on the anonymous endpoints listed in CFG-020 when no token is set.
- **TRN-016** `Set-Cookie` headers in responses MUST be ignored; the SDK maintains no
  cookie jar.
- **TRN-017** ⚠️ `X-Vault-Request`, `X-Vault-Wrap-TTL`, `X-Vault-Index` are not
  implemented by the server. The SDK MUST NOT send them; `RequestOptions.WrapTtl` MUST
  raise `BV-INPUT-006 UnsupportedOption` until the server implements wrapping.

## URL construction

```
url = address (trailing '/' stripped)
    + '/' + prefix                       // omitted when path is absolute
    + '/' + encode_path(path)            // percent-encode per segment, keep '/' and trailing '/'
    + ('?' + encode_query(query))?       // split on '&' then '=', encode each side
```

- **TRN-020** Path segments MUST be percent-encoded for: controls, space, `"`, `#`, `%`,
  `/` (inside a segment), `<`, `>`, `?`, `` ` ``, `\`, `^`, `{`, `}`, `|`, `[`, `]`.
  Segment separators MUST be preserved.
- **TRN-021** Query components additionally encode `&`, `+`, `=` and MUST NOT encode `/`.
- **TRN-022** The server lifts only these query keys into the request: `env`, `version`,
  `after`, `limit`, `topics` (`version` and `limit` coerced to integers). The typed
  surface MUST send selectors as query parameters, never in a body, for `GET` requests.
  The SDK SHOULD warn (debug log) when `Logical.Read` is called with other query keys.
- **TRN-023** A namespace MAY alternatively be encoded as a path prefix
  (`dti/esi/secret/data/x`). The SDK MUST use the header form and MUST document that
  callers who put a namespace in the path must leave `Namespace` empty.

## Body serialization

- **TRN-030** Request bodies MUST be a single flat JSON object; there is no request-side
  `data` wrapper except where an engine defines one (KV v2 `{"data": {...}}`).
- **TRN-031** A duration MUST be sent in the form **the endpoint's own row in this
  specification declares**, and in no other. Typed operations MUST convert from the
  language's duration type. **The server does not use one encoding for all durations**,
  so an SDK MUST NOT infer an endpoint's form from a neighbour's, and an implementer
  MUST NOT add a duration to an endpoint whose form is unrecorded without measuring it.

  > **Measured — `bvault` 0.44.5, 2026-09-23** ([DR-0021](../decisions/0021-live-server-findings.md)):
  > the encoding is genuinely per-endpoint. A JSON **number** is *rejected* with a `serde`
  > error by `auth/token/create` `ttl` ([05](05-authentication.md#token-store-operations-authtoken))
  > and by every measured `pki/*` duration ([09](09-pki-engine.md)), and *accepted* by
  > `ssh/roles/{name}` `ttl`/`max_ttl` ([10](10-ssh-engine.md)) and `totp` `period`
  > ([11](11-totp-engine.md)). KV v1 `ttl` round-trips as a Go-style duration string
  > ([07](07-kv-engine.md)). Endpoints not in that list are **unmeasured, not known-good**;
  > the residual exposure is tracked as R-37.
- **TRN-032** The server body limit is 32 MiB. The SDK MUST reject larger bodies
  client-side with `BV-INPUT-007` before sending.
- **TRN-033** Responses larger than `MaxResponseBytes` (default 128 MiB) MUST be aborted
  with `BV-TRANSPORT-004`.

## Response envelope

Two body shapes exist. ⚠️ Neither matches HashiCorp Vault exactly.

### Shape A — logical envelope (`/v1|v2/{path}` catch-all)

```json
{
  "renewable": false,
  "lease_id": "",
  "lease_duration": 0,
  "auth": null,
  "data": { ... }
}
```

All five fields are always present. `request_id`, `warnings`, `wrap_info`, `mount_type`
are **not** emitted.

### Shape B — raw (dedicated `sys/*` handlers)

The handler's data is the top-level object, e.g. `GET /v1/sys/mounts` returns the mount
map directly, `GET /v1/sys/health` returns `{"initialized":..., "sealed":..., ...}`.

### Auth object

```json
"auth": {
  "client_token": "s.…",
  "policies": ["default", "app"],
  "metadata": { "username": "alice" },
  "lease_duration": 3600,
  "renewable": true
}
```

Only these five fields are emitted. `accessor`, `token_policies`, `identity_policies`,
`entity_id`, `token_type`, `orphan`, `num_uses` are absent; an SDK that wants them MUST
call `auth/token/lookup-self` afterwards ([05](05-authentication.md#lookup)).

### Canonical `Response` type

```
Response {
  Data:          Map<string, Json>?    // Shape A: "data"; Shape B: whole body
  Auth:          AuthInfo?             // present on login / token create
  LeaseId:       string?               // empty string → absent
  Renewable:     bool?
  LeaseDuration: Duration?             // seconds on the wire
  Warnings:      string[]              // always empty against current servers; kept for forward-compat
  StatusCode:    int
  Headers:       Map<string,string>    // response headers (ETag, Retry-After, ...)
  Raw:           Json                  // the exact parsed body, for diagnostics
}
```

- **TRN-040** Shape detection MUST be: the body is Shape A when it is an object containing
  a `data` key **or** an `auth` object containing `client_token`. Otherwise the whole body
  is `Data` (Shape B).
- **TRN-041** `lease_id == ""` MUST be surfaced as absent.
- **TRN-042** Fields absent on the wire MUST be absent/null in `Response`; the SDK MUST
  NOT fabricate `request_id` or `warnings`.
- **TRN-043** The exact parsed body MUST be retained in `Raw` so applications can read
  fields introduced by newer servers.

### Timestamp encoding

A timestamp field MUST be accepted in **both** encodings: an RFC 3339 /
ISO-8601 string, and a JSON number read as **seconds since the Unix epoch**. Any other
JSON kind MUST raise `BV-PROTOCOL-002` through the SDK's error model, never escape as a
raw parse exception from the JSON library. Tolerant reading is required in both
directions because the encoding is a server-release property the SDK cannot negotiate.
This rule carries no requirement ID of its own: it constrains the parsing of fields the
engine sections already require, and minting an ID would need Appendix D and the
traceability baseline to move with it. Promoting it to `TRN-044` is proposed separately.

> **Measured — `bvault` 0.44.5, 2026-09-23** ([DR-0021](../decisions/0021-live-server-findings.md)):
> this server sends **Unix-epoch numbers**, not strings, for `creation_time`
> ([08](08-transit-engine.md)), and `issued_at`, `not_after` and `expiration`
> ([09](09-pki-engine.md)). `auth/token/lookup*` `creation_time` was already documented
> as unix. No measured endpoint has yet returned a timestamp as a string; the string
> limb is retained because it is what the upstream documentation describes, and dropping
> it would narrow the SDK on one release's evidence.

## Status-code handling

| Status | Meaning | SDK behaviour |
|--------|---------|---------------|
| `200` | success with body | parse envelope |
| `204` | success, no content | return `null`/`None` **before** reading the body |
| `200` with empty/whitespace body | treat as `null`, not a parse error | return `null` |
| `304` | not modified (`If-None-Match`) | return `NotModified` result (not an error) |
| `400` | invalid request, missing token (logical path), CAS mismatch, not-initialised, … | map by message ([04](04-error-model.md)) |
| `401` | only inside batch per-op results and connect-MFA | `BV-AUTH-002` |
| `403` | permission denied, invalid/revoked token, CIDR-bound token, gated endpoints | `BV-AUTHZ-001` unless message maps more specifically |
| `404` | read/list miss (**empty body**), mount not found, KV v2 version missing, unrouted path (**empty body**) | `BV-NOTFOUND-*` by message; empty body → `BV-NOTFOUND-001` |
| `405` | unsupported verb (**empty body**) | `BV-PROTOCOL-001` |
| `409` | recording digest mismatch, brokered-resource static credential | `BV-CONFLICT-002`/`-003` |
| `416` | recording chunk index past end | `BV-INPUT-008` |
| `429` | DoS guard (`Retry-After`, `errors[]`), namespace rate quota (`error`, no header), **health: standby** | `BV-RATE-001` / `BV-RATE-002` / health classification |
| `500` | unsupported path/operation, lease errors, mount conflicts, generic | map by message; default `BV-SERVER-005` |
| `501` | **health: not initialised** | health classification only |
| `503` | sealed, cluster no leader/quorum/unhealthy, HSM unavailable | `BV-SERVER-001` (sealed) / `BV-SERVER-002` |
| `507` | namespace capacity quota | `BV-QUOTA-001` |

- **TRN-050** A read or list whose backend returned nothing yields `404` with an **empty
  body**. The typed surface MUST convert this to a typed "not found" result: for `Read`
  the SDK MUST return `null`/`None` (not throw) from `Logical.Read`, and typed operations
  MUST document whether they return optional or raise `BV-NOTFOUND-001`. Convention:
  `Kv.ReadSecret` returns optional; `Kv.GetSecret` raises.
- **TRN-051** The SDK MUST read `Retry-After` **before** consuming the body and attach it
  to the error as `Error.RetryAfter` (integer seconds; HTTP-date form MAY be parsed).
- **TRN-052** Error bodies come in three shapes and MUST all be parsed:
  1. `{"error": "<string>"}` — the normal server shape (singular). ⚠️ Public docs show
     `errors[]`; the server emits `error`.
  2. `{"errors": ["<string>", ...]}` — DoS guard 429 and HashiCorp compatibility; join
     with `; ` for the message and keep the array in `Error.ServerErrors`.
  3. Empty body — synthesize `HTTP <status> (no body)` and map by status alone.
- **TRN-053** A response with `Content-Type` other than JSON on a JSON endpoint MUST be
  surfaced as `BV-PROTOCOL-002` with the first 256 bytes of the body (sanitised) in
  `Error.Details`, not as a JSON parse exception.
- **TRN-054** Every error MUST carry `StatusCode`, `Method`, `Path` (with namespace), and
  the mapped `Code` ([04](04-error-model.md)).

## Redirects

- **TRN-060** ⚠️ The server never emits `307`/`Location` leader redirects. The SDK MUST
  treat any `3xx` other than `304` as `BV-PROTOCOL-003` and MUST NOT follow it (CNF-034).
  Standby handling is done client-side via health probing ([13](13-cluster-discovery-and-resilience.md)).

## API versions `/v1` and `/v2`

- **TRN-070** Both prefixes share one handler; the prefix only sets `api_version` on the
  request. Engines that are v2-only return `400 API version mismatch: this engine is not
  available on the requested API version.` → `BV-SERVER-006`.
- **TRN-071** The SDK MUST hard-pin the prefix for endpoints that exist only on `/v2`
  (see [Appendix A](appendix-a-endpoint-catalogue.md), column "Prefix"), regardless of
  `ApiPrefix`.
- **TRN-072** The entire `sys/*` surface is duplicated on both prefixes; typed sys
  operations MUST use `ApiPrefix` unless pinned.

## Server version discovery

- **TRN-080** ⚠️ There is no `/sys/version` endpoint and no version header. The SDK MUST
  implement `Sys.ServerInfo()` → `GET sys/info` and expose `Version`, `StartedAt`,
  `UptimeSeconds`, `StorageType` as optional (they are only returned to a caller with a
  live token).
- **TRN-081** The SDK MUST provide `Client.ServerVersion()` that caches the `sys/info`
  version for the `Client` lifetime and returns `null` when unauthenticated.

## Concurrency and connection reuse

- **TRN-090** The transport MUST reuse connections (keep-alive / HTTP/1.1 pooling or
  HTTP/2) across requests of one `Client`.
- **TRN-091** Proxies MUST be **disabled by default**; `UseSystemProxy = true` opts in to
  environment (`HTTPS_PROXY`, `ALL_PROXY`, …) and OS proxy settings.
- **TRN-092** IPv6 literal addresses MUST be bracketed (`https://[::1]:8200`); an
  unbracketed IPv6 with port MUST be rejected with `BV-CONFIG-001`.

## Fake transport contract (for tests)

- **TRN-100** The SDK MUST ship (in its test utilities or a test-support package) a
  `FakeTransport` that records requests (`method, url, headers, body`) and replays
  scripted responses (`status, headers, body`), including the ability to return a custom
  `LIST` method, `204` without body, `404` with empty body, `429` with `Retry-After`, and
  transport-level failures (connection refused, timeout, TLS error). Conformance fixtures
  ([Appendix C](appendix-c-conformance-fixtures.md)) are executed through it.
