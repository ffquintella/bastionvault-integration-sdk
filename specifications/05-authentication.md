# 05 — Authentication

All auth methods produce a **token** that the client sends as `X-BastionVault-Token`. This
section specifies the token source model, each login flow the server implements, the
token-store operations, and automatic renewal.

## Token source model

```
TokenSource =
  | Static(token)                        // from config / SetToken / token file
  | Login(method, credentials, options)  // performs a login on first use and on renewal failure
  | Callback(fn() -> token)              // application-provided (e.g. secrets from a KMS)
```

- **AUT-001** `Client` MUST hold exactly one `TokenSource`. `SetToken` replaces it with
  `Static`.
- **AUT-002** A `Login` source MUST perform the login lazily on the first authenticated
  request or eagerly via `Client.Auth.Authenticate()`; the SDK MUST document which.
- **AUT-003** When a `Login` source's token is rejected with `BV-AUTHZ-001` **and** the
  token is older than `MinReloginInterval` (default 30 s), the SDK MAY re-login once and
  replay the request if it was idempotent. This MUST be opt-in (`ReloginOnPermissionDenied`,
  default `false`) because 403 also means "policy does not allow".
- **AUT-004** The current token MUST be readable as `Client.Auth.CurrentToken` (a
  redacting secret type) and `Client.Auth.TokenInfo` (the last `LookupSelf` result, if
  any).

## Login response contract

Every login is `POST auth/{mount}/login[...]` with **no** token header (TRN-015). The
server's success and failure shapes are:

| Outcome | HTTP | Body |
|---------|------|------|
| Success | 200 | Shape A envelope with `auth.client_token`, `auth.policies`, `auth.metadata`, `auth.lease_duration`, `auth.renewable` |
| Rejected credentials / account state | **200** | `{"renewable":false,"lease_id":"","lease_duration":0,"auth":null,"data":{"error":"<reason>"}}` |
| Malformed request, hard failures | 400 | `{"error":"<reason>"}` |
| Gated (namespace, source IP, machine binding, token CIDR) | 403 | `{"error":"Permission denied."}` |
| Rate-limited login path (DoS guard) | 429 | `errors[]` + `Retry-After` |

- **AUT-010** ⚠️ The SDK MUST treat a 200 response **without** `auth.client_token` as a
  login failure and MUST raise `BV-AUTH-003 LoginRejected` with `ServerMessage =
  data.error`. It MUST never store an empty token.
- **AUT-011** The SDK MUST refine `BV-AUTH-003` by recognising these `data.error`
  strings (see [Appendix B](appendix-b-error-catalogue.md)):
  `invalid username or password` → `BV-AUTH-004`; `account is disabled` → `BV-AUTH-005`;
  `account temporarily locked; try again in N seconds` → `BV-AUTH-006` (`Details.retry_after_secs`);
  `a TOTP code is required for this account` → `BV-AUTH-007`; `invalid TOTP code` →
  `BV-AUTH-008`; `Password login is disabled for this account. Use your FIDO2 security key instead.`
  → `BV-AUTH-009`; `enrolment_pending*` → `BV-AUTH-012`; `enrolment_rejected` →
  `BV-AUTH-013`; `machine_revoked` → `BV-AUTH-014`; `rate_limited: …` → `BV-RATE-001`.
- **AUT-012** A `400` from `auth/approle/login` whose message starts with `invalid role_id`,
  `invalid secret_id`, `invalid secret id`, `missing role_id` → `BV-AUTH-010`; one that
  starts with `machine_token` or `machine ` → `BV-AUTH-011 AppIdMachineBinding`.
- **AUT-013** On success the SDK MUST store `client_token`, MUST record `IssuedAt`,
  `LeaseDuration`, `Renewable`, `Policies`, `Metadata`, and MUST expose them as
  `AuthInfo`.
- **AUT-014** The login response's `auth` object has only five fields (TRN §Auth
  object). `Accessor`, `EntityId`, `TokenType`, `Orphan`, `NumUses` MUST be modelled as
  optional and populated only after `LookupSelf`.

## Method: Token

```
Auth.Token.Use(token)                 -> sets Static source; no network
Auth.Token.Verify()                   -> LookupSelf; raises BV-AUTHZ-001 if invalid
```

- **AUT-020** `Use` MUST reject empty/whitespace tokens with `BV-INPUT-001`.

## Method: Userpass

```
Auth.Userpass.Login(username, password, totpCode?, mount = "userpass", options?) -> AuthInfo
```

Request: `POST auth/{mount}/login/{username}` body `{"password": "...", "totp_code": "..."?}`.

- **AUT-030** `username` MUST be URL-path-encoded (TRN-020). `totp_code` MUST be omitted
  from the body when not supplied (not sent as empty string).
- **AUT-031** The password MUST be held in a redacting secret type.
- **AUT-032** The SDK MUST document that a locked account (`BV-AUTH-006`) is not fixed by
  retrying and MUST NOT auto-retry it.

### User administration and policy attachment

The `Auth.Userpass.Admin.*` surface is Standard-level and catalogued in
[Appendix A](appendix-a-endpoint-catalogue.md#userpass-authmount--authuserpass).
`WriteUser` takes the user document as raw JSON, so the SDK does not fix its field set;
the field that decides authorisation is named here because getting it wrong produces a
user who authenticates and then can do nothing.

> **Measured — `bvault` 0.44.5, 2026-09-23** ([DR-0021](../decisions/0021-live-server-findings.md)
> F5): the policies a userpass user's token receives are taken from **`token_policies`**.
> A `policies` key on the same document is **not** honoured — it is accepted on write and
> read back, but the issued token carries only `default`. Documentation and samples MUST
> write `token_policies`; `policies` MUST NOT be described as an alias.
>
> **Not established by this measurement:** whether `policies` is inert everywhere or
> merely unread on this path, and whether the server intends it as a deprecated alias.
> Only the userpass user document was exercised.

### FIDO2 (userpass mount) and standalone `fido2` mount

```
Auth.Userpass.Fido2LoginBegin(username, mount)             -> WebAuthnAssertionOptions (raw JSON)
Auth.Userpass.Fido2LoginComplete(username, credentialJson, mount) -> AuthInfo
Auth.Fido2.LoginBegin / LoginComplete                       (mount default "fido2")
```

- **AUT-035** WebAuthn payloads MUST be passed through as opaque JSON; the SDK MUST NOT
  attempt to interpret them. Completion follows the login response contract.

## Method: AppID (wire type `approle`)

```
Auth.AppId.Login(roleId, secretId?, machineToken?, mount = "approle", options?) -> AuthInfo
Auth.AppId.ReadRoleId(roleName, mount)                       -> string
Auth.AppId.GenerateSecretId(roleName, SecretIdOptions?, mount) -> SecretIdInfo
```

Request: `POST auth/{mount}/login` body `{"role_id": "...", "secret_id": "...",
"machine_token": "..."?}`.

- **AUT-040** `machine_token` MUST be sent only when supplied. The SDK MUST document that
  the server's machine-identity gate (`auth/approle/config.require_machine`, default
  **on**) requires it unless the role has `bypass_machine_binding = true`.
- **AUT-041** Namespace-scoped roles: the login MUST carry `X-BastionVault-Namespace`
  when `Namespace` is set; the hint for a `403` on this path MUST mention the namespace
  header (ERR enrichment table).
- **AUT-042** `SecretIdOptions` MUST support `Metadata` (map → JSON string on the wire),
  `CidrList`, `TokenBoundCidrs`, `NumUses`, `Ttl`, `Environments` (glob list). The
  response `SecretIdInfo` MUST expose `secret_id`, `secret_id_accessor`,
  `secret_id_ttl`, `secret_id_num_uses`, `environments`.
- **AUT-043** The full AppID role administration surface (`role/{name}` and its
  sub-paths, `secret-id/lookup|destroy`, `secret-id-accessor/lookup|destroy`,
  `custom-secret-id`, `machine`, `machine/{id}`, `config`, `tidy/secret-id`) is listed in
  [Appendix A](appendix-a-endpoint-catalogue.md) and MUST be exposed at the **Complete**
  level under `Auth.AppId.Admin`.
- **AUT-044** The `AuthInfo.Metadata` from an AppID login MAY carry `approle_env_scoped`,
  `approle_env_secret`, `approle_env_machine`. The SDK MUST expose a derived
  `EnvironmentScope { Scoped: bool, SecretGlobs: string[], MachineGlobs: string[] }` so
  KV callers know an `env` is mandatory ([07](07-kv-engine.md#environments)).

## Method: FerroGate machine identity

```
Auth.Ferrogate.Requirement(mount = "ferrogate")                 -> Requirement   (unauthenticated GET)
Auth.Ferrogate.Login(childToken, dpopProof?, userToken?, mount) -> AuthInfo
Auth.Ferrogate.Status(childToken, dpopProof?, mount)            -> MachineStatus (unauthenticated POST)
Auth.Ferrogate.Enroll(spiffeId, comment?, mount)                -> EnrollResult  (unauthenticated POST)
```

- **AUT-050** `Login` is `POST auth/{mount}/login` with body `{"token": childToken,
  "dpop": proof?, "user_token": userToken?}`. When `dpopProof` is given the SDK MUST send
  it both as the `DPoP` header and the `dpop` body field.
- **AUT-051** `Requirement` returns `{require_machine_identity, expected_audience,
  trust_domain, mia_environment}` and MUST be callable without a token. The SDK MUST
  offer `Client.Auth.Ferrogate.IsMachineIdentityRequired()` as a cached convenience.
- **AUT-052** `Status` maps `status` ∈ `pending | approved | rejected | revoked | unknown`
  to an enum. `Enroll` never returns a token; the SDK MUST document that.
- **AUT-053** Obtaining the FerroGate child token from the local Machine Identity Agent
  is **out of scope**; the SDK MUST accept it as an opaque string and MUST document the
  `bvault ferrogate token --format json` bridge for applications.
- **AUT-054** Admin operations (`config`, `register`, `machines`, `machines/{id}`,
  `machines/{id}/approve|reject|revoke`) are Complete-level under
  `Auth.Ferrogate.Admin`.

## Method: OIDC and SAML (browser-mediated)

```
Auth.Oidc.AuthUrl(redirectUri, role?, mount = "oidc")       -> string (unauthenticated)
Auth.Oidc.Callback(state, code, mount)                        -> AuthInfo (unauthenticated)
Auth.Saml.Login(redirectUri?, role?, mount = "saml")          -> { SsoUrl, RelayState, RequestId } (unauthenticated)
Auth.Saml.Callback(samlResponse, relayState, mount)           -> AuthInfo (unauthenticated)
```

- **AUT-060** These flows require a browser; the SDK MUST implement the two endpoints
  per method and MUST document a loopback-redirect recipe in the usage guides. Role and
  config administration is Complete-level under `Auth.Oidc.Admin` / `Auth.Saml.Admin`.

## Method: Certificate (mTLS)

- **AUT-070** ⚠️ The `cert` auth backend is **disabled** in current server builds (it
  registers no paths). The SDK MUST still support presenting a client certificate at the
  TLS layer (CFG-044) and MUST expose `Auth.Cert.Login(mount = "cert")` as
  `POST auth/{mount}/login`, but its documentation MUST state that current servers
  answer `Router mount not found.` / `Logical backend path not supported.` and the SDK
  MUST map that to `BV-SERVER-004 UnsupportedByServer` with a hint naming the disabled
  backend.

## Token store operations (`auth/token/*`)

⚠️ Only the following paths exist on the server. `lookup-accessor`, `renew-self`,
`renew-accessor`, `revoke-accessor`, `create-orphan` (as a distinct path), `roles`, and
`tidy` do **not** exist; the SDK MUST NOT expose operations for them.

| Canonical operation | HTTP | Body | Response |
|---------------------|------|------|----------|
| `Auth.Token.Create(CreateTokenRequest)` | `POST auth/token/create` | `policies[]`, `ttl` (**duration string**, e.g. `"1h"` — *not* a number, see below), `period`, `num_uses`, `renewable` (default true), `meta{}`, `display_name`, `explicit_max_ttl`, `no_default_policy`, `no_parent` (root only), `id` (root only), `type`, `child_visible` | envelope with `auth` |
| `Auth.Token.Lookup(token)` | `GET auth/token/lookup/{token}` | — | `TokenInfo` |
| `Auth.Token.LookupSelf()` | `GET auth/token/lookup-self` | — | `TokenInfo` |
| `Auth.Token.Renew(token, increment)` | `POST auth/token/renew/{token}` | `{"increment": seconds}` (**required**) | envelope with `auth` |
| `Auth.Token.RenewSelf(increment)` | `POST auth/token/renew/{currentToken}` | `{"increment": seconds}` | envelope with `auth` |
| `Auth.Token.Revoke(token)` | `POST auth/token/revoke/{token}` | — | 204 |
| `Auth.Token.RevokeOrphan(token)` | `POST auth/token/revoke-orphan/{token}` (sudo) | — | 204 |
| `Auth.Token.RevokeSelf()` | `POST auth/token/revoke-self` | — | 204; SDK clears the token |
| `Auth.Token.AuditLogin()` | `POST auth/token/audit-login` | — | 204 (records a login event for a token sign-in) |

`TokenInfo` (from lookup `data`): `id`, `policies[]`, `path`, `meta{}`, `display_name`,
`num_uses`, `creation_time` (unix), `creation_ttl` (seconds), `explicit_max_ttl`,
`period?`. ⚠️ `ttl` is always `0` on the wire; the SDK MUST compute
`RemainingTtl = creation_time + creation_ttl − now` (null when `creation_ttl == 0`) and
MUST NOT expose the wire `ttl`.

> **Measured — `bvault` 0.44.5, 2026-09-23** ([DR-0021](../decisions/0021-live-server-findings.md)
> F2): `auth/token/create` **rejects** a numeric `ttl`, failing server-side with a `serde`
> deserialisation error before the token is created. The specification previously declared
> `ttl` as integer seconds and the SDK obeyed it, so `Create` with a `Ttl` could not
> succeed against this server at all. **The Go-style duration string is measured accepted,
> not inferred:** `{"ttl":"1h","policies":["default"]}` creates a token and returns
> `lease_duration: 3600`, while `{"ttl":3600}` fails with
> `invalid type: integer 3600, expected a string`.
>
> **Scope of this measurement.** Only `ttl` was exercised. `period` and `explicit_max_ttl`
> on the same request are duration-shaped and **unmeasured**; their rows are left as
> written rather than amended by analogy (TRN-031). `increment` on `renew/{token}` stays
> a **number**: DR-0021 F1 shows the server accepting those renewals (it answers `204` or
> `200`, never a `serde` error), so that row is measured good and is not changed here.

- **AUT-080** `RenewSelf` MUST be implemented via `renew/{token}` with the current token
  in the path (there is no `renew-self`). The path MUST be redacted in errors (ERR-003).
- **AUT-081** `Create` MUST reject client-side (`BV-INPUT-009`) any `meta` key in the
  reserved set: `spiffe_id`, `machine_id`, `username`, `entity_id`, `mount_path`,
  `role_name`, `role`, `namespace_path`, `namespace_id`, `child_visible`, `auth_method`,
  `groups`, `subject`, `name_id`, `name_id_format`, `ferrogate_kid`, `session_id`,
  `approle_machine_bypass`, `machine_identity_exempt`, and any key starting with
  `approle_env_`. The server also refuses them (`meta key(s) ... are reserved`).
- **AUT-082** `Create` responses are envelope `auth` objects; `Create` MUST return
  `AuthInfo` and MUST NOT switch the client's token unless the caller asks
  (`UseResult = true`).
- **AUT-083** `RevokeSelf` on a root-policy token is accepted by the server but does not
  revoke (logout recorded only). The SDK MUST document this and MUST still clear its
  local token.
- **AUT-084** A `Lookup` of an unknown token yields `404` empty body → `BV-NOTFOUND-006
  TokenNotFound`.
- **AUT-085** `Renew` of an unknown/expired token yields `400 Request is invalid.` →
  `BV-AUTH-015 TokenNotRenewable`.

## Automatic renewal

```
AutoRenew { Enabled = false, RenewAtFraction = 0.66, MinInterval = 10s, Increment = null /* server default */,
            MaxConsecutiveFailures = 5, OnRenewed(event), OnFailed(event), OnStopped(reason) }
```

- **AUT-090** When enabled and the token is `Renewable` with `LeaseDuration > 0`, the SDK
  MUST schedule `RenewSelf` at `IssuedAt + LeaseDuration × RenewAtFraction`, never
  sooner than `MinInterval` after the previous renewal.
- **AUT-091** On success the schedule MUST be recomputed from the new `lease_duration`.
- **AUT-092** On failure the SDK MUST retry with exponential backoff (starting at 1 s,
  capped at 1/4 of the remaining TTL) up to `MaxConsecutiveFailures`, then stop and emit
  `OnStopped(RenewalFailed)`. It MUST stop immediately on `BV-AUTHZ-001`,
  `BV-AUTH-015`, or `BV-SERVER-001` (sealed) and on `RevokeSelf`/`ClearToken`.
- **AUT-093** When the token source is `Login`, after renewal stops the SDK MUST attempt a
  fresh login once and resume the schedule; on login failure it emits
  `OnStopped(ReloginFailed)`.
- **AUT-094** The renewal loop MUST run on the runtime's background primitive (hosted
  service / `tokio::spawn` / `asyncio.Task`), MUST be cancellable via `Client.Dispose/Close`,
  and MUST use the injectable clock so tests can drive it deterministically.
- **AUT-095** Batch tokens and non-renewable tokens MUST cause `AutoRenew` to log once at
  info level and do nothing.

## Persisted token helper

See [02 — Token helper](02-client-configuration.md#token-helper-file). ⚠️ The `bvault`
CLI stores its token encrypted under a machine-derived key with a `BVTOK1:` prefix. The
SDK MUST read a plaintext file and MUST treat a `BVTOK1:`-prefixed file as unreadable
(`BV-CONFIG-010 EncryptedTokenFile`) with a hint pointing to `bvault ferrogate token` or
`BASTIONVAULT_TOKEN`.

## Security requirements

- **AUT-100** Credentials passed to login operations MUST NOT be retained after the login
  completes unless the source is `Login` (needed for re-login), in which case they MUST
  be held in redacting types.
- **AUT-101** The SDK MUST NOT log the `auth` object; the observability hook receives
  only `policies.length`, `lease_duration`, `renewable`.
