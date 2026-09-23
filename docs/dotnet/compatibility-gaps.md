# Vault compatibility gaps (.NET)

**Implements** [`specifications/16-documentation-requirements.md` D9](../../specifications/16-documentation-requirements.md).
The language-neutral behaviour this page reports is specified in
[03 — transport and protocol](../../specifications/03-transport-and-protocol.md) and
[06 — system API](../../specifications/06-system-api.md); every ⚠️ item in those two sections is
listed here. This is a **reference document**, not a guide: DOC-010's task/prerequisites/steps
order does not apply (DR-0018 D-M11-9's carve-out), and it is organised by wire-behaviour category
instead.

BastionVault's server implements the HashiCorp Vault wire *shape* closely enough that a client
built against public Vault documentation will compile against this SDK, but the two are **not**
protocol-identical. Where the two disagree, the server code wins and this page is where the
difference is recorded, so it is discovered here rather than as an unexplained `404` in production.

## Response shapes and headers

| Area | HashiCorp Vault | BastionVault |
|---|---|---|
| Error body | `{"errors": [...]}` on almost every error | `{"error": "<string>"}` is the **normal** shape (singular). `{"errors": [...]}` is used only by the DoS guard's `429` and for HashiCorp-client compatibility (`TRN-052`) |
| Response envelope | `request_id`, `warnings`, `wrap_info`, `mount_type` present | **Not emitted.** The logical envelope carries exactly `renewable`, `lease_id`, `lease_duration`, `auth`, `data` — no more, no less (`TRN-040`) |
| Token header | `X-Vault-Token` | `X-BastionVault-Token`. `X-Vault-Token`, `Authorization: Bearer` and the `token` cookie are accepted **only** as server-side fallbacks; the SDK never sends them (`TRN-013`) |
| Namespace header | `X-Vault-Namespace` | `X-BastionVault-Namespace`. `X-Vault-Namespace` is **ignored** by the server (`TRN-014`) |
| Wrapping headers | `X-Vault-Wrap-TTL`, `X-Vault-Index`, `X-Vault-Request` | **Not implemented.** `RequestOptions.WrapTtl` raises `BV-INPUT-006` client-side rather than being silently dropped (`TRN-017`) |
| `LIST` | `GET ...?list=true` also works | The server does **not** support the query-parameter form; the SDK always issues the literal HTTP verb `LIST` (`TRN-010`) |
| Leader redirects | `307`/`Location` | **Never emitted.** The SDK treats any `3xx` other than `304` as `BV-PROTOCOL-003` and does not follow it; standby handling is client-side health probing instead (`TRN-060`) |
| Server version | `GET sys/version`, a version response header | **Neither exists.** `Sys.ServerInfo()` (`GET sys/info`) and `Client.ServerVersion()` are the SDK's only way to learn the running version, and both require a live token (`TRN-080`, `TRN-081`) |

## Health and seal status

| Area | HashiCorp Vault | BastionVault |
|---|---|---|
| Standby/DR codes | `472`, `473`, `standbyok` query parameters | **Do not exist.** The SDK never sends them (`SYS-002`) |
| `sys/seal-status` `t`/`n` | `t` = threshold, `n` = shares | **Reversed**: the server populates `t` with `secret_shares` and `n` with `secret_threshold`. The SDK exposes the raw `T`/`N` **and** derived `KeyShares = max(t, n)` / `KeyThreshold = min(t, n)`, so a caller who wants "shares" and "threshold" never has to remember which wire field means which (`SYS-005`) |

## Mounts, policies, namespaces

| Area | HashiCorp Vault | BastionVault |
|---|---|---|
| `sys/mounts` entries | `type`, `description`, `config`, `uuid`, `options`, `accessor` | **Two fields only**: `type` and `description`. `MountInfo` has no `Config`, `Uuid`, `Options` or `Accessor` member, because the wire never fills them (`SYS-020`) |
| Mount `config` | `POST sys/mounts/{path}` honours `default_lease_ttl`/`max_lease_ttl` | **Ignored outright.** `MountRequest` deliberately has no `Config` field — offering one the server silently discards would be worse than not offering it. KV v2 tuning goes through the engine's own `config` path instead |
| KV v2 routes | `delete/{path}`, writable `metadata/{path}`, `subkeys/`, `PATCH` | **`delete/{path}` does not exist** (soft delete is `DELETE data/{path}`); `metadata/{path}` has **no write** (no per-secret `max_versions`/`cas_required`); there is **no `subkeys/`**; HTTP `PATCH` is `405` everywhere |
| `sys/capabilities` | Self and non-self (by accessor) variants | **Only `Sys.CapabilitiesSelf` exists.** There is no non-self `sys/capabilities` and no accessor variant; the SDK exposes none (`SYS-052`) |
| `WriteNamespace` | Partial update semantics in some client wrappers | **Full replace, always.** Every quota the call omits is written as `0` (unlimited) and an omitted `child_visible_default` is written as `false`. `Sys.UpdateNamespace` is the SDK's read-merge-write helper for changing one field without resetting the rest (`SYS-060`) |

## Authentication

| Area | HashiCorp Vault | BastionVault |
|---|---|---|
| Certificate (mTLS) auth | `cert` backend registers `auth/cert/login` and role management | **Disabled on current server builds** — it registers no paths at all. The SDK still supports presenting a client certificate at the TLS layer and still exposes `Auth.Cert.Login`, but a call to it answers `Router mount not found.` / `Logical backend path not supported.`, which the SDK maps to `BV-SERVER-004 UnsupportedByServer` with a hint naming the disabled backend, rather than a bare `500` (`AUT-070`) |
| Token store paths | `lookup-accessor`, `renew-self`, `renew-accessor`, `revoke-accessor`, `create-orphan`, `roles`, `tidy` | **None of these exist.** The SDK exposes no operation for any of them. `RenewSelf` is implemented over `renew/{token}` with the caller's own token in the path, because there is no dedicated `renew-self` route |
| Login object fields | `accessor`, `token_policies`, `identity_policies`, `entity_id`, `token_type`, `orphan`, `num_uses` on the login response | **Only** `client_token`, `policies`, `metadata`, `lease_duration`, `renewable` are emitted on login. An SDK that wants the rest calls `auth/token/lookup-self` afterwards |

## Leases, wrapping, cubbyhole — not available

The server exposes **no** HTTP surface for the routes below (`SYS-100`). The built-in default
policy mentions some of them; the routes themselves do not exist, so a caller reaches a `404`
rather than a permission error. This SDK exposes **no** operation for any of them, and a test
asserts that no public member of the assembly ever adds one silently.

| Absent surface | HashiCorp Vault equivalent | What to use instead |
|---|---|---|
| `sys/leases/*` | lease lookup, list, renew, revoke | Nothing: BastionVault does not track leases over HTTP |
| `sys/renew` | `POST sys/leases/renew` | `Auth.Token.RenewSelf` for a **token**; there is no lease renewal |
| `sys/revoke` | `POST sys/leases/revoke` | `Auth.Token.Revoke*` for a **token**; there is no lease revocation |
| `sys/wrapping/*` | `wrap`, `unwrap`, `lookup`, `rewrap` | Nothing. `RequestOptions.WrapTtl` raises `BV-INPUT-006` client-side |
| `cubbyhole/` | the per-token cubbyhole engine | A namespaced KV mount |

`Response.LeaseId`, `Response.LeaseDuration` and `Response.Renewable` stay **informational**: the
server sends them on some responses and the SDK surfaces them, but there is no route to renew or
revoke against.

<!-- docs:sample compatibility-gaps/absent-surfaces -->
```csharp
// The same list this page prints in prose, reachable from code so a migration script can
// check it too (SYS-100, SYS-101).
foreach (string surface in VaultCompatibilityGaps.AbsentSurfaces)
{
    Console.WriteLine($"not served by this server: {surface}");
}
```

<!-- docs:sample compatibility-gaps/cert-auth-disabled -->
```csharp
// AUT-070: the cert auth backend registers no paths on current servers. The SDK still
// exposes Auth.Cert.Login and still presents the client certificate at the TLS layer
// (CFG-044); the server's "path not supported" answer is mapped to BV-SERVER-004 rather
// than surfacing as a generic 500, so a caller can tell "this backend is not enabled"
// apart from "this backend is broken".
try
{
    await client.Auth.Cert.LoginAsync();
}
catch (BastionVaultException e) when (e.Code == ErrorCodes.ServerUnsupportedByServer)
{
    Console.Error.WriteLine($"{e.Code}: {e.Hint}");
}
```

## Next steps

- **Error reference** — every code named on this page (`BV-SERVER-004`, `BV-PROTOCOL-003`,
  `BV-INPUT-006`), its category, hint and retryability.
- **Security guide** — TLS defaults, since certificate auth's TLS-layer presentation still applies
  even though the `cert` backend itself is disabled.
- **Getting started** — the operations this page's absences do not affect.
