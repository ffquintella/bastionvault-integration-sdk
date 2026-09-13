# 04 — Error Model

The error model is the part of the SDK developers meet first and most often. Its goal is
that an integration mistake can be diagnosed **from the error alone**: a stable code, a
plain-language default message, and a *hint* naming the most likely cause and the fix.

The complete code catalogue (code → default message → hint → retryable → HTTP triggers →
server strings) is in [Appendix B](appendix-b-error-catalogue.md). This section defines
the structure and the rules.

## The `Error` type

Every failure surfaced by the SDK MUST be an instance of one error type (or a hierarchy
rooted at one type) with the following canonical fields:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `Code` | string | yes | Stable identifier, `BV-<CATEGORY>-<NNN>`. Never localised, never changed. |
| `Category` | enum | yes | One of the categories below. |
| `Message` | string | yes | Human-readable default message (English). May be overridden by the caller's locale layer, never by the server. |
| `Hint` | string | yes | Actionable guidance. Empty string is **not** allowed; every code has a hint. |
| `ServerMessage` | string? | when from server | The raw server `error` string or joined `errors[]`. |
| `ServerErrors` | string[] | when from server | The raw `errors[]` array when present. |
| `StatusCode` | int? | when from server | HTTP status. |
| `RetryAfter` | duration? | when present | Parsed `Retry-After`. |
| `Retryable` | bool | yes | Whether an identical retry may succeed without operator/developer action. |
| `Method` | string? | when a request was made | `GET`, `POST`, `LIST`, … |
| `Path` | string? | when a request was made | Logical path, **with** namespace prefix for display (`[ns=dti/esi] secret/data/x`). |
| `Address` | string? | when a request was made | Server host (no credentials, no query string). |
| `Attempts` | int | yes | Number of attempts made (≥ 1 if any request was sent). |
| `Details` | map | yes (may be empty) | Structured extras: `cas_expected`, `chunk_count`, `total`, `missing_policies`, `namespace_operable`, etc. |
| `Cause` | error? | when wrapping | Underlying runtime exception (IO, TLS, JSON). |
| `Timestamp` | instant | yes | When the error was created (UTC). |

- **ERR-001** All fields above MUST be present with these canonical names (after idiom
  mapping). Implementations MAY add fields.
- **ERR-002** The default string representation MUST be exactly:
  `"<Code>: <Message> — <Hint>"` optionally followed by ` [HTTP <status> <METHOD> <path>]`
  and ` (server: "<ServerMessage>")` when available. Newlines are not permitted in the
  one-line form. A multi-line "verbose" form MAY be offered separately.
- **ERR-003** The string representation MUST NOT contain a token, password, secret ID,
  key material, or KV data. The `Path` MUST have `lookup/<token>`, `renew/<token>`,
  `revoke/<token>`, `revoke-orphan/<token>` segments redacted to `<redacted>`.
- **ERR-004** Errors MUST be matchable by `Code` and by `Category` without string
  comparison (constants/enum members).
- **ERR-005** Each language MUST expose the codes as constants (e.g.
  `ErrorCodes.AuthPermissionDenied`) **and** as the literal string so logs from
  different SDKs can be correlated.
- **ERR-006** `Retryable` MUST be `true` only for `BV-TRANSPORT-001/002/003`,
  `BV-SERVER-002`, `BV-SERVER-003`, `BV-RATE-002`, and `BV-DISCOVERY-003`. Everything
  else is `false`.

## Categories and code ranges

| Category | Prefix | Covers |
|----------|--------|--------|
| Configuration | `BV-CONFIG-*` | Invalid or incomplete client configuration; detected before any request. |
| Input | `BV-INPUT-*` | Invalid arguments to an SDK operation; detected before any request. |
| Transport | `BV-TRANSPORT-*` | Network, TLS, timeout, cancellation, response too large. |
| Protocol | `BV-PROTOCOL-*` | Server answered with something the SDK cannot interpret. |
| Authentication | `BV-AUTH-*` | No token, invalid credentials, login failures, expired token. |
| Authorization | `BV-AUTHZ-*` | Token valid but not permitted (policy, namespace binding, CIDR, gates). |
| Not found | `BV-NOTFOUND-*` | Path, mount, secret, version, lease, user, role missing. |
| Conflict | `BV-CONFLICT-*` | CAS mismatch, already exists/initialised, digest mismatch, brokered credential. |
| Rate limit | `BV-RATE-*` | DoS guard ban, namespace request quota, client rate gate. |
| Quota | `BV-QUOTA-*` | Namespace capacity quota (507). |
| Server state | `BV-SERVER-*` | Sealed, uninitialised, unhealthy cluster, standby, unsupported endpoint, internal error, API version mismatch. |
| Discovery | `BV-DISCOVERY-*` | SRV resolution, no healthy node, pinned node unavailable. |
| Engine | `BV-KV-*`, `BV-TRANSIT-*`, `BV-PKI-*`, `BV-SSH-*`, `BV-TOTP-*`, `BV-IDENTITY-*`, `BV-RUSTION-*` | Engine-specific server messages worth a dedicated code. |

- **ERR-010** Codes are three-digit within a category. New codes append; deleted codes are
  reserved forever.

## Mapping algorithm (server response → code)

The SDK MUST apply the following steps in order and stop at the first match:

1. **Transport failure** (no HTTP response): connection refused/reset/DNS → `BV-TRANSPORT-001`;
   timeout → `BV-TRANSPORT-002`; TLS handshake/verification → `BV-TRANSPORT-003`;
   body over limit → `BV-TRANSPORT-004`; cancelled → `BV-TRANSPORT-005`.
2. **Health endpoint** (`sys/health`) never produces an error for 200/429/501/503; it
   produces a `HealthStatus` ([06](06-system-api.md#health)).
3. **Extract server message**: `error` string, or `errors[]` joined, or empty.
4. **Exact/prefix match on server message** using the recognition table in
   [Appendix B §2](appendix-b-error-catalogue.md#2-server-message-recognition) — this
   is necessary because the server maps many distinct conditions to 400 or 500.
   Matching MUST be case-insensitive, on the trimmed message, and MUST tolerate a
   trailing period and a `(retry after Ns)` suffix.
5. **Status fallback** when no message matched:

   | Status | Code |
   |--------|------|
   | 400 | `BV-INPUT-100 ServerRejectedRequest` |
   | 401 | `BV-AUTH-002` |
   | 403 | `BV-AUTHZ-001` |
   | 404 | `BV-NOTFOUND-001` |
   | 405 | `BV-PROTOCOL-001` |
   | 409 | `BV-CONFLICT-001` |
   | 416 | `BV-INPUT-008` |
   | 429 with `Retry-After` | `BV-RATE-001` |
   | 429 without `Retry-After` | `BV-RATE-002` |
   | 500 | `BV-SERVER-005` |
   | 502/504 | `BV-SERVER-002` |
   | 503 | `BV-SERVER-002` |
   | 507 | `BV-QUOTA-001` |
   | other 4xx | `BV-INPUT-100` |
   | other 5xx | `BV-SERVER-005` |
   | 3xx (not 304) | `BV-PROTOCOL-003` |

6. **Contextual refinement**: the typed layer MAY replace a generic code with a more
   specific one using knowledge of the operation (e.g. a `404` empty body from
   `Kv.ReadSecret` → `BV-KV-001 SecretNotFound`; a `403` from `auth/approle/login` after a
   200-shaped role check → `BV-AUTH-011 AppIdLoginGated`). Refinement MUST keep the
   original status and server message.

- **ERR-020** Steps 1–5 MUST live in one function in the logical layer so that every
  typed operation gets identical mapping. Fixtures in Appendix C test that function
  directly.
- **ERR-021** A `403` MUST default to *authorization* (`BV-AUTHZ-001`), never
  *authentication*. ⚠️ The server answers `Permission denied.` (403) for an invalid,
  expired, or revoked token as well; the SDK cannot distinguish these and the hint for
  `BV-AUTHZ-001` MUST say so.
- **ERR-022** A missing token MUST be caught client-side (`BV-AUTH-001`) so the server's
  inconsistent behaviour (400 on logical paths, 403 on inline sys handlers, 401 inside
  batch results) is never visible to callers of typed operations.

## Message and hint style rules

- **ERR-030** `Message` states **what happened** in one sentence, present tense, no
  jargon the developer cannot act on: "The token does not have permission for this
  path."
- **ERR-031** `Hint` states **what to check or do**, in imperative mood, ≤ 2 sentences,
  naming the concrete configuration key, header, policy capability or endpoint
  involved: "Check the token's policies grant `read` on `secret/data/app` (use
  `Sys.CapabilitiesSelf`). If the token came from another namespace, set
  `Namespace` or use a child-visible token."
- **ERR-032** Hints MUST use canonical setting/operation names from this specification
  (idiom-mapped by each implementation) so they stay accurate across languages.
- **ERR-033** Hints MUST NOT tell the user to disable security controls (e.g. "set
  `TlsSkipVerify`") as the first suggestion. They MAY mention it as a last-resort
  diagnostic step with an explicit warning.
- **ERR-034** Hints for path-related errors MUST include the path as the SDK sent it (with
  namespace), because most "not found" problems are mount or prefix mistakes
  (`secret/app` vs `secret/data/app`).
- **ERR-035** Where the server message adds information (`cannot assign policy <name>`,
  `batch has N operations, exceeds max M`, `chunk_count`), the SDK MUST extract the
  variable parts into `Details` and MAY interpolate them into the hint.
- **ERR-036** Messages and hints MUST be stored in one table (code → message → hint) that
  is loaded once and can be inspected programmatically (`ErrorCatalog.Get(code)`), so
  documentation and tests can be generated from it and so a translation layer can wrap
  it.
- **ERR-037** The catalog MUST be exhaustive over all codes and the test suite MUST assert
  every code has a non-empty message and hint and that no two codes share a message.

## Hint enrichment from context

The SDK MUST add these context-aware notes to `Hint` when the condition is detectable
client-side (they are appended after the catalogue hint):

| Condition | Appended note |
|-----------|---------------|
| `403` and `Namespace` is empty and the path is under `auth/` or `secret/` | "No namespace is set; if the credential is scoped to a namespace, set `Namespace`." |
| `403` and `Details.namespace_operable == false` (from `capabilities-self`) | "The token is bound to namespace `<token_namespace>` and is not child-visible for `<active_namespace>`." |
| `404` and path is `<mount>/<name>` on a KV v2 mount (from `Sys.ListMounts` cache) | "This mount is KV v2; use `<mount>/data/<name>` or `Kv.ReadSecret`." |
| `400 API version mismatch` | "Pin this call to `/v2` (RequestOptions.ApiVersion = 2)." |
| `429` with `Retry-After` | "The client rate gate is now paused for `<n>`s; reduce request fan-out (use `Sys.Batch` or `*-info` pages)." |
| `503 sealed` | "Run `bvault operator unseal` on the node or wait for auto-unseal; the SDK will not retry." |
| `500 Logical backend path not supported.` | "This server version does not have this endpoint; check `Client.ServerVersion()` and use the documented fallback." |
| TLS verification failure and `CaCertPath` unset | "Provide the server's CA bundle via `CaCertPath` or `BASTIONVAULT_CACERT`." |
| Connection refused to default address | "No `Address` was configured; the default is `https://127.0.0.1:8200`. Set `Address` or `BASTIONVAULT_ADDR`." |

- **ERR-040** Enrichment MUST be deterministic and covered by fixtures.

## Warnings

- **ERR-050** ⚠️ Current servers never emit `warnings`. The SDK MUST still surface a
  `Response.Warnings` list (empty) and MUST log warnings at *warning* level if a future
  server sends them. Warnings MUST NOT be turned into errors.

## Error documentation

- **ERR-060** Every SDK MUST publish an *Error Reference* page generated from the
  catalog (see [16](16-documentation-requirements.md)) listing every code with message,
  hint, retryability, and the HTTP conditions that trigger it.
- **ERR-061** Each typed operation's API documentation MUST list the specific codes it
  can raise beyond the common set (`BV-CONFIG-*`, `BV-TRANSPORT-*`, `BV-AUTH-001`,
  `BV-AUTHZ-001`, `BV-SERVER-*`, `BV-RATE-*`).

## Example (canonical one-line form)

```
BV-KV-003: The check-and-set version did not match the current version of the secret. — Re-read the secret to get the current version (it is in Details.current_version when the server reports it) and retry with `cas` set to that value; pass cas=0 only when the secret must not exist yet. [HTTP 400 POST [ns=dti/esi] secret/data/app/db] (server: "Check-and-set parameter did not match the current version.")
```
