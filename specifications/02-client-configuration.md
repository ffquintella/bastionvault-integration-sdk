# 02 — Client Configuration

## Configuration model

A `Client` is created from a `ClientConfig` (builder, options object, or keyword
arguments, per language idiom). The following table lists every setting, its canonical
name, type, default, and the environment variable(s) that may supply it.

| Canonical setting | Type | Default | Environment variable(s) | Notes |
|-------------------|------|---------|--------------------------|-------|
| `Address` | URL or cluster name | `https://127.0.0.1:8200` | `BASTIONVAULT_ADDR`, `VAULT_ADDR` | A value containing `://` is a literal node URL. A bare DNS name triggers cluster discovery ([13](13-cluster-discovery-and-resilience.md)). |
| `Token` | secret string | none | `BASTIONVAULT_TOKEN`, `VAULT_TOKEN` | Initial token. May be replaced later by a login or `SetToken`. |
| `TokenFile` | path | `~/.vault-token` | `BASTIONVAULT_TOKEN_FILE` | Only read when `Token` is unset and `UseTokenHelper` is true. |
| `UseTokenHelper` | bool | `false` | `BASTIONVAULT_USE_TOKEN_HELPER` | Opt-in to reading/writing the token file. |
| `Namespace` | string | `""` (root) | `BASTIONVAULT_NAMESPACE`, `VAULT_NAMESPACE` | Sent as `X-BastionVault-Namespace`. Slash-delimited path, no leading slash. |
| `CaCertPath` | path | none | `BASTIONVAULT_CACERT`, `VAULT_CACERT` | PEM bundle to trust instead of/in addition to system roots. |
| `CaCertPem` | string | none | — | Inline PEM alternative to `CaCertPath`. Takes precedence when both are set. |
| `ClientCertPath` | path | none | `BASTIONVAULT_CLIENT_CERT`, `VAULT_CLIENT_CERT` | mTLS client certificate (PEM). |
| `ClientKeyPath` | path | none | `BASTIONVAULT_CLIENT_KEY`, `VAULT_CLIENT_KEY` | mTLS client private key (PEM). Must be set together with `ClientCertPath`. |
| `TlsSkipVerify` | bool | `false` | `BASTIONVAULT_SKIP_VERIFY`, `VAULT_SKIP_VERIFY` | Disables certificate verification. See CNF-030. |
| `TlsServerName` | string | none | `BASTIONVAULT_TLS_SERVER_NAME`, `VAULT_TLS_SERVER_NAME` | SNI / hostname to verify against. |
| `AllowInsecureHttp` | bool | `false` | `BASTIONVAULT_ALLOW_INSECURE_HTTP` | Required for non-loopback `http://` addresses (CNF-035). |
| `Timeout` | duration | `30s` | `BASTIONVAULT_TIMEOUT`, `VAULT_CLIENT_TIMEOUT` | Per-request total timeout (connect + headers + body). |
| `ConnectTimeout` | duration | `10s` | `BASTIONVAULT_CONNECT_TIMEOUT` | TCP + TLS handshake. |
| `RetryPolicy` | object | see [Retry policy](#retry-policy) | `BASTIONVAULT_MAX_RETRIES`, `VAULT_MAX_RETRIES` | Env var sets `MaxAttempts − 1`. |
| `RateGate` | object | `8 req/s, burst 16` | `BASTIONVAULT_RATE_PER_SEC`, `BASTIONVAULT_RATE_BURST` | Client-side token bucket ([14](14-batch-and-request-efficiency.md#client-rate-gate)). `0` disables. |
| `ClusterDiscovery` | bool | `true` | `BASTIONVAULT_NO_CLUSTER_DISCOVERY`, `VAULT_NO_CLUSTER_DISCOVERY` (set ⇒ `false`) | See [13](13-cluster-discovery-and-resilience.md). |
| `DiscoveryProbeTimeout` | duration | `1500ms` | `BASTIONVAULT_DISCOVERY_PROBE_TIMEOUT` | Health probe timeout per candidate. |
| `Headers` | map | `{}` | — | Extra headers added to every request. MUST NOT override reserved headers ([03](03-transport-and-protocol.md#request-headers)). |
| `UserAgent` | string | `bastionvault-sdk-<lang>/<version>` | — | Appended, never replaced; see TRN-013. |
| `ApiPrefix` | `v1` \| `v2` | `v1` | — | Default prefix for raw logical operations. Typed operations choose their own prefix. |
| `AutoRenew` | object | disabled | — | Background token renewal ([05](05-authentication.md#automatic-renewal)). |
| `Logger` | sink | no-op | — | Runtime-idiomatic logging hook. |
| `Transport` | implementation | HTTP | — | Injection point for tests (OVR-001). |

### Precedence

- **CFG-001** Settings MUST be resolved in this order, first match wins:
  1. Value passed explicitly in code.
  2. `BASTIONVAULT_*` environment variable.
  3. `VAULT_*` environment variable (compatibility alias).
  4. Token helper file (for `Token` only, and only when `UseTokenHelper` is true).
  5. Built-in default.
- **CFG-002** Environment variables MUST be read once, at `Client` construction, not on
  every request.
- **CFG-003** Boolean environment variables MUST accept `1`, `true`, `yes`, `on`
  (case-insensitive) as true and `0`, `false`, `no`, `off`, empty as false. Any other
  value MUST raise `BV-CONFIG-003`.
- **CFG-004** Duration values from environment variables MUST accept Go/Vault-style
  strings (`30s`, `1m30s`, `500ms`, `2h`) and plain integers interpreted as seconds.
  Anything else raises `BV-CONFIG-003`.
- **CFG-005** An implementation MAY provide a `FromEnvironment()` convenience that
  performs the resolution above and a `ClientConfig` constructor that ignores the
  environment entirely. The README MUST state which is the default for the language's
  primary constructor.

### Validation at construction

Construction MUST fail fast with a `BV-CONFIG-*` error (never a generic exception) when:

- **CFG-010** `Address` is empty, is not a valid URL, or uses a scheme other than
  `http`/`https` → `BV-CONFIG-001`.
- **CFG-011** `Address` uses `http://` to a non-loopback host and `AllowInsecureHttp` is
  false → `BV-CONFIG-002`.
- **CFG-012** Exactly one of `ClientCertPath` / `ClientKeyPath` is set → `BV-CONFIG-004`.
- **CFG-013** A referenced file (`CaCertPath`, `ClientCertPath`, `ClientKeyPath`,
  `TokenFile` when `UseTokenHelper`) does not exist or cannot be read → `BV-CONFIG-005`
  (except `TokenFile`, whose absence is silently treated as "no token").
- **CFG-014** A PEM value cannot be parsed → `BV-CONFIG-006`.
- **CFG-015** `Namespace` contains a leading `/`, `//`, whitespace, or control characters
  → `BV-CONFIG-007`. A trailing `/` MUST be stripped silently.
- **CFG-016** `Timeout` or `ConnectTimeout` is zero or negative → `BV-CONFIG-003`.
- **CFG-017** `Headers` contains a reserved header name (TRN-012) → `BV-CONFIG-008`.
- **CFG-018** `TlsSkipVerify` is true → construction succeeds but MUST log one warning
  (CNF-030) and the `Client` MUST expose `IsInsecure == true`.

### Token absence is not an error

- **CFG-020** A `Client` MUST be constructible without a token. Unauthenticated endpoints
  (`sys/health`, `sys/seal-status`, `sys/init`, `sys/unseal`, anonymous `sys/info`,
  `auth/*/login`, `auth/ferrogate/requirement`, `auth/ferrogate/enroll`) work without
  one. An authenticated operation attempted with no token MUST fail client-side with
  `BV-AUTH-001` *before* any network call.

## Token helper file

- **CFG-030** When `UseTokenHelper` is true and no explicit token or env token exists, the
  SDK MUST read `TokenFile`, trim whitespace, and use the contents as the token.
- **CFG-031** `Client.Auth.Login*` operations MUST NOT write the token file unless the
  application calls `Client.Auth.PersistToken()` (explicit opt-in), which writes the
  current token with owner-only permissions (`0600` or the platform equivalent).
- **CFG-032** `Client.Auth.ForgetPersistedToken()` MUST delete the file if present and
  MUST NOT fail if it is absent.

## TLS

- **CFG-040** The default trust store MUST be the operating system / runtime store.
  `CaCertPath`/`CaCertPem` MUST be *added* to it, not replace it, unless
  `CaCertReplacesSystemRoots` is set to true.
- **CFG-041** Minimum TLS version MUST be 1.2; TLS 1.3 MUST be offered.
- **CFG-042** When `TlsServerName` is set it MUST be used for both SNI and hostname
  verification.
- **CFG-043** When cluster discovery selects a node, the SRV target hostname MUST be used
  for SNI and verification unless `TlsServerName` overrides it
  ([13](13-cluster-discovery-and-resilience.md)).
- **CFG-044** Client-certificate authentication (`ClientCertPath`/`ClientKeyPath`) MUST be
  presented on every connection when configured; it is also what `Auth.LoginCert` relies on.

## Retry policy

```
RetryPolicy {
  MaxAttempts:        3          // total attempts including the first
  InitialBackoff:     250ms
  MaxBackoff:         5s
  BackoffMultiplier:  2.0
  Jitter:             0.2        // ±20 %
  RetryOn:            [BV-TRANSPORT-001, BV-TRANSPORT-002, BV-SERVER-002 (502/503 non-sealed), BV-SERVER-003 (standby 429/472/473)]
  RespectRetryAfter:  true
  RetryIdempotentOnly: true
}
```

- **CFG-050** Defaults MUST be as above.
- **CFG-051** Only operations flagged idempotent (`Read`, `List`, `sys/health`,
  `seal-status`, and typed operations documented as idempotent) MAY be retried by
  default. `Write`/`Delete` MUST NOT be retried unless the caller sets
  `RetryIdempotentOnly = false` or passes a per-call `Idempotent = true` option.
- **CFG-052** A `429` with `Retry-After` from the abuse guard (`BV-RATE-001`) MUST NOT
  be retried automatically by the retry policy; it is handled by the rate gate
  ([14](14-batch-and-request-efficiency.md#client-rate-gate)) so that a ban is not
  extended by retry storms.
- **CFG-053** A `503` whose body indicates *sealed* (`BV-SERVER-001`) MUST NOT be retried:
  unsealing is an operator action.
- **CFG-054** When `Retry-After` is present on a retryable response and
  `RespectRetryAfter` is true, the wait MUST be `max(Retry-After, computed backoff)`
  capped at `MaxBackoff × 6`.
- **CFG-055** The total number of attempts MUST be observable on the resulting error
  (`Error.Attempts`).

## Per-operation options

Every operation MUST accept an optional `RequestOptions`:

| Option | Meaning |
|--------|---------|
| `Namespace` | Override the client namespace for this call only. |
| `Headers` | Additional headers for this call (same reserved-header rule). |
| `Timeout` | Override `Timeout`. |
| `Idempotent` | Mark a write as safe to retry. |
| `WrapTtl` | Request response wrapping (`X-Vault-Wrap-TTL`) where the server supports it. |
| `Token` | Use a different token for this call only (e.g. a machine token). |
| `CancellationToken` / equivalent | Runtime cancellation primitive. |

- **CFG-060** `RequestOptions` MUST be optional in every signature (default instance when
  omitted).
- **CFG-061** Per-call options MUST NOT mutate the `Client`.

## Runtime mutation

- **CFG-070** `Client.SetToken(token)` and `Client.ClearToken()` MUST be provided and
  MUST be thread-safe. In-flight requests keep the token they started with.
- **CFG-071** `Client.WithNamespace(ns)` MUST return a lightweight view sharing the
  transport and token but scoped to another namespace. Views MUST be independent for
  namespace only; a `SetToken` on the parent is visible to the view.
- **CFG-072** `Client.SetAddress` MUST NOT exist; changing the server requires a new
  `Client` (sticky-node semantics, [13](13-cluster-discovery-and-resilience.md)).

## Observability hooks

- **CFG-080** The SDK MUST expose a request/response hook (middleware, event, callback)
  receiving: method, path, namespace, status code, duration, request_id, attempt number,
  error code. It MUST NOT receive bodies or tokens.
- **CFG-081** The SDK SHOULD emit metrics-friendly names for the hook payload
  (`bastionvault.client.request.duration`, `...retries`, `...errors{code}`) and MUST
  document them.
