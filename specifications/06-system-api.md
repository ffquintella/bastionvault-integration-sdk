# 06 — System API (`sys/*`)

The system API is served by dedicated handlers under both `/v1/sys` and `/v2/sys`. ⚠️
Responses from these handlers are **Shape B (raw)** — the payload is the top-level JSON
object, not wrapped in `data` ([03](03-transport-and-protocol.md#response-envelope)).
Unregistered `sys` paths return `404` with an empty body; they do not fall through to the
logical router.

All operations live under `Client.Sys` unless stated otherwise.

## Health and status

### `Sys.Health()` → `HealthStatus`

`GET sys/health` — unauthenticated, exempt from the DoS guard.

```json
{ "initialized": true, "sealed": false, "standby": false, "cluster_healthy": true }
```

| Body | HTTP | `HealthStatus.State` |
|------|------|----------------------|
| `initialized == false` | 501 | `Uninitialized` |
| `sealed == true` or `cluster_healthy == false` | 503 | `Sealed` / `Unhealthy` |
| `standby == true` | 429 | `Standby` |
| otherwise | 200 | `Active` |

- **SYS-001** `Health` MUST NOT raise for 200/429/501/503; it MUST return `HealthStatus
  { State, Initialized, Sealed, Standby, ClusterHealthy, StatusCode, Raw }`. It raises
  only on transport failure or a non-JSON body (`BV-PROTOCOL-002`).
- **SYS-002** ⚠️ There are no `472`/`473` codes and no `standbyok`-style query parameters;
  the SDK MUST NOT send them.

### `Sys.SealStatus()` → `SealStatus`

`GET sys/seal-status` → `{"sealed": bool, "t": int, "n": int, "progress": int}`, always 200.

- **SYS-005** ⚠️ The server populates `t` with `secret_shares` and `n` with
  `secret_threshold` — the reverse of HashiCorp's convention. The SDK MUST expose the raw
  `T` and `N` **and** derived `KeyShares = max(t, n)`, `KeyThreshold = min(t, n)` (the
  threshold can never exceed the shares), and MUST document the discrepancy.

### `Sys.ServerInfo()` → `ServerInfo`

`GET sys/info` — anonymous tier `{initialized, sealed}`; with a live token adds `version`,
`started_at` (RFC 3339), `uptime_seconds`, `storage_type`.

- **SYS-008** Tiered fields MUST be optional; absent MUST NOT be defaulted.

### `Sys.ClusterStatus()` → `ClusterStatus`

`GET sys/cluster-status` — requires a live token or a cluster-local peer; otherwise 403
`cluster status requires a valid token, or a request from a cluster node` →
`BV-AUTHZ-003`.

`{ storage_type, cluster, node_id?, is_leader?, cluster_healthy?, raft_metrics? }` —
optional fields are omitted on non-clustered backends and MUST stay optional.

- **SYS-006** `ClusterStatus` MUST surface the documented 403 as the typed `BV-AUTHZ-003`
  error (a permission refusal, unlike `Health`'s 503, is not itself the state this
  operation exists to report, so it raises like any other mapped error). `RaftMetrics`
  MUST be modelled as an opaque map (the wire does not fix its keys) and, like every
  other field in this shape, MUST stay
  absent rather than defaulted when the backend omits it.

### `Sys.HsmStatus()` → `HsmStatus` — **v2-only**

`GET /v2/sys/hsm/status` → `{type: "shamir"|"hsm", auto_unseal, sealed, initialized, …}`.
The SDK MUST pin the `/v2` prefix (TRN-071).

## Initialisation, seal, unseal

| Operation | HTTP | Body | Response |
|-----------|------|------|----------|
| `Sys.InitStatus()` | `GET sys/init` | — | `{"initialized": bool}` |
| `Sys.Init(shares?, threshold?)` | `PUT sys/init` | `{"secret_shares": n, "secret_threshold": t}` (both omitted for HSM auto-unseal) | `{"keys": ["hex"…], "root_token": "…"}` |
| `Sys.Seal()` | `PUT sys/seal` | — | 204 (sudo-gated) |
| `Sys.Unseal(key)` | `PUT sys/unseal` | `{"key": "hex"}` | `SealStatus` (200; idempotent when already unsealed) |

- **SYS-010** `Init` MUST validate client-side that both or neither of `shares`/`threshold`
  are given and that `1 ≤ threshold ≤ shares ≤ 255` (`BV-INPUT-001`). Server strings
  `secret_shares and secret_threshold are required when the vault uses the shamir seal` /
  `… must be provided together` → `BV-INPUT-100`; `BastionVault is already initialized.`
  → `BV-CONFLICT-005`.
- **SYS-011** `InitResult` MUST use redacting types for `Keys` and `RootToken`, MUST
  zero them on dispose where possible, and MUST NOT be logged.
- **SYS-012** `Unseal` with an invalid key → `BastionVault unseal key is invalid.` →
  `BV-INPUT-101`. `Unseal` on an uninitialised vault → `BastionVault is not initialized.`
  → `BV-SERVER-007`.
- **SYS-013** `Seal` and `Unseal` MUST be flagged non-retryable and excluded from
  failover; cluster-wide variants per RES-030.

## Mounts (secrets engines)

| Operation | HTTP | Notes |
|-----------|------|-------|
| `Sys.ListMounts()` → `Map<path, MountInfo>` | `GET sys/mounts` | ⚠️ Only `type` and `description` per entry. No `config`, `uuid`, `options`, `accessor`. |
| `Sys.Mount(path, MountRequest)` | `POST sys/mounts/{path}` | body `{type (required), description?, options?}` → 204 |
| `Sys.Unmount(path)` | `DELETE sys/mounts/{path}` | 204 |
| `Sys.Remount(from, to)` | `POST sys/remount` | `{"from": "kv/", "to": "kv2/"}` → 204 |
| `Sys.ListMountsDetailed()` → `MountTable` | `GET sys/internal/ui/mounts` | ACL-filtered; entries carry `type, description, uuid, options`; split into `secret` and `auth` maps |

- **SYS-020** ⚠️ `POST sys/mounts/{path}` ignores `config` (`default_lease_ttl`,
  `max_lease_ttl`). The SDK's `MountRequest` MUST NOT offer a `Config` field; KV v2
  tuning goes through the engine's own `config` path ([07](07-kv-engine.md#engine-config)).
- **SYS-021** Mount type strings the SDK MUST expose as constants: `kv`, `kv-v2`,
  `transit`, `pki`, `ssh`, `ssh-broker`, `totp`, `openldap`, `files`, `resource`,
  `rustion`, `notifications`, `cert-lifecycle`; auth types: `userpass`, `approle`,
  `ferrogate`, `fido2`, `oidc`, `saml` (and `cert`, disabled on current servers).
- **SYS-022** Mount paths MUST be normalised to end with `/` in results and accepted with
  or without it as input. Empty path → `BV-INPUT-001` client-side (server answers 404
  empty).
- **SYS-023** Remount failures are `409`: `no matching mount at <from>` →
  `BV-NOTFOUND-002`; `path already in use at <to>` → `BV-CONFLICT-004`; `Unknown mount
  table type.` → `BV-INPUT-100`.
- **SYS-024** Mount quota breach `507 namespace quota exceeded: mounts limit of N reached`
  → `BV-QUOTA-001`.
- **SYS-025** `GET sys/mounts/{path}` returns the whole table (no per-mount read). The
  SDK MUST implement `Sys.ReadMount(path)` client-side by filtering `ListMounts`, and
  return null when absent.
- **SYS-026** The SDK MUST provide `Sys.MountTypeOf(path) -> string?` with a per-client
  cache (TTL 60 s, invalidated by `Mount`/`Unmount`/`Remount`) used by KV path detection.

## Auth methods

| Operation | HTTP |
|-----------|------|
| `Sys.ListAuthMethods()` → `Map<path, MountInfo>` | `GET sys/auth` (same two-field shape) |
| `Sys.EnableAuthMethod(path, MountRequest)` | `POST sys/auth/{path}` → 204 |
| `Sys.DisableAuthMethod(path)` | `DELETE sys/auth/{path}` → 204 (revokes its tokens) |

- **SYS-030** Auth mount paths in results are relative (`userpass/`), never prefixed
  with `auth/`; the SDK MUST accept both forms as input and normalise.

## Policies

Two parallel surfaces share handlers; the SDK MUST use the `policies/acl` surface and MAY
expose the legacy one as `Sys.Legacy.*`.

| Operation | HTTP | Response |
|-----------|------|----------|
| `Sys.ListPolicies()` → `string[]` | `GET sys/policies/acl` | `{"keys": [...]}` (`root` appended in root namespace) |
| `Sys.ReadPolicy(name)` → `Policy?` | `GET sys/policies/acl/{name}` | `{"name": "...", "policy": "<hcl>"}`; 404 `No policy named: X` → null / `BV-NOTFOUND-005` |
| `Sys.WritePolicy(name, hcl)` | `POST sys/policies/acl/{name}` | body `{"policy": "<hcl or base64>"}` → 204 |
| `Sys.DeletePolicy(name)` | `DELETE sys/policies/acl/{name}` | 204 |
| `Sys.PolicyHistory(name)` → `PolicyHistoryEntry[]` | `GET sys/policies/acl/{name}/history` | `{"entries": [{ts, user, op, before_raw, after_raw}]}` |
| `Sys.TestPolicy(draft, name?, cases[])` → `PolicyTestResult` | `POST /v2/sys/policies/acl/test` | see [Policy dry-run](#policy-dry-run) |
| `Sys.ReadPolicyTests(name)` / `Sys.WritePolicyTests(name, cases[])` | `GET/POST /v2/sys/policy-tests/{name}` | saved effectivity cases |

- **SYS-040** ⚠️ The legacy `GET sys/policy/{name}` returns the document under `rules`;
  `policies/acl` returns it under `policy`. The typed `Policy` MUST expose `Hcl` filled
  from whichever key is present.
- **SYS-041** `WritePolicy` MUST reject the reserved names `root` (`BV-INPUT-010`) and
  `test` (reserved by the dry-run route). Deleting `default` or `root` → `BV-INPUT-010`
  client-side.
- **SYS-042** Server errors: `sentinel (RGP/EGP) policies cannot be created inside a
  namespace` → `BV-INPUT-100`; cross-namespace path refusal → `BV-INPUT-102
  CrossNamespacePolicyPath`.
- **SYS-043** The SDK MUST ship a `PolicyBuilder` helper that emits HCL `path "…" {
  capabilities = [...] }` blocks with optional `required_parameters`, `allowed_parameters`,
  `scopes`, `groups`, and a top-level `metadata {}` block. It MUST escape quotes and MUST
  be tested with round-trip fixtures.

### Policy dry-run

Request `{ policy, name?, cases: [{ path, capability, policies?: string[] | null, env? }] }`.
`policies` is tri-state: absent → `["default"]`; `[]` → draft alone; list → draft + those.
Response `{ parse_ok, errors[], results: [{ path, capability, allowed, matched_path,
match_kind, denied_by_deny, granting_policies[], evaluated_policies[], missing_policies[],
draft_only_allowed }] }`. `match_kind ∈ exact | prefix | segment_wildcard | none`.

- **SYS-045** The SDK MUST preserve the tri-state (`null` vs empty list) on the wire.
  Naming `root` → 400 `BV-INPUT-010`; naming an unreadable policy → 403 `BV-AUTHZ-001`.

## Capabilities — **v2-only**

`Sys.CapabilitiesSelf(paths[])` → `POST /v2/sys/capabilities-self` body `{"paths": [...]}`.

```json
{ "secret/data/app": ["read","list"], "capabilities": { "secret/data/app": ["read","list"] },
  "namespace_operable": true, "token_namespace": "", "active_namespace": "" }
```

- **SYS-050** Result type `Capabilities { ByPath: Map<string, Capability[]>,
  NamespaceOperable: bool, TokenNamespace: string, ActiveNamespace: string }`. The SDK
  MUST read from the `capabilities` map (not the duplicated top-level keys).
- **SYS-051** `Capability` enum: `root, deny, read, list, create, update, delete, sudo,
  connect`; unknown strings MUST be preserved as `Other(string)`.
- **SYS-052** Empty `paths` → `BV-INPUT-002` client-side. ⚠️ There is no `sys/capabilities`
  (non-self) or accessor variant; the SDK MUST NOT expose one.
- **SYS-053** `Sys.Can(path, capability)` convenience MUST be provided (`root` implies
  all; `read`/`root` imply `connect`; `deny` overrides).

## Namespaces

| Operation | HTTP | Notes |
|-----------|------|-------|
| `Sys.ListNamespaces()` → `string[]` | `LIST sys/namespaces` | children of the active namespace; `{"keys": [...]}` |
| `Sys.ReadNamespace(path)` → `Namespace?` | `GET sys/namespaces/{path}` | 404 `no such namespace: "x"` → null / `BV-NOTFOUND-007` |
| `Sys.WriteNamespace(path, NamespaceSpec)` → `Namespace` | `POST sys/namespaces/{path}` | **upsert, full replace** → 200 with the record |
| `Sys.DeleteNamespace(path)` | `DELETE sys/namespaces/{path}` | 204; cascades unmounts; blocked while children exist |
| `Sys.NamespacesSelf()` → `{ Namespaces[], TokenNamespace, Root }` | `GET sys/namespaces-self` | `""` denotes root |
| `Sys.ListNamespacesInfo(after?, limit?)` → `Page<Namespace>` | `GET sys/namespaces-info` | see [14](14-batch-and-request-efficiency.md#bulk-metadata-listings-info-and-cursor-pagination) |

`Namespace { Uuid, Path, ParentUuid?, CreatedAt, ChildVisibleDefault, Quotas {
MaxStorageBytes, MaxLeases, RequestRate, MaxMounts, MaxEntities, MaxChildNamespaces } }`
— `0` means unlimited.

- **SYS-060** ⚠️ `WriteNamespace` resets omitted quotas to `0` and `child_visible_default`
  to `false`. The SDK MUST document this and MUST provide `Sys.UpdateNamespace(path,
  patch)` that reads, merges, and writes.
- **SYS-061** The root namespace record cannot be read or written over HTTP; the SDK
  MUST NOT expose an operation for it and MUST document that `WriteNamespace("")` is
  rejected client-side (`BV-INPUT-001`).
- **SYS-062** Deleting a namespace destroys tenant data; the SDK MUST name the operation
  `DeleteNamespace` (never `Remove…`) and document the cascade.

## Batch, cache version, DoS

Specified in [14](14-batch-and-request-efficiency.md). DoS admin (`Complete`):

| Operation | HTTP |
|-----------|------|
| `Sys.Dos.ReadConfig()` / `WriteConfig(patch)` | `GET/POST /v2/sys/dos/config` — partial update; fields `enabled, window_secs, max_requests, auth_max_requests, ban_secs, refresh_secs` |
| `Sys.Dos.Stats()` | `GET /v2/sys/dos/stats` |
| `Sys.Dos.Ban(ip, ttlSecs?, reason?)` / `Unban(ip)` | `POST/DELETE /v2/sys/dos/bans/{ip}` |

## Audit

| Operation | HTTP | Response |
|-----------|------|----------|
| `Sys.Audit.ListDevices()` | `GET sys/audit` | `{"devices": [{path, type, description, namespace, mirror}]}` |
| `Sys.Audit.EnableDevice(path, {type, description?, options?, mirror?})` | `POST sys/audit/{path}` → 204 |
| `Sys.Audit.DisableDevice(path)` | `DELETE sys/audit/{path}` → 204 |
| `Sys.Audit.Events(from?, to?, limit = 500)` | `GET sys/audit/events?from=&to=&limit=` | `{"events": [{ts, user, machine?, op, category, target, changed_fields[], summary}]}` newest first |

- **SYS-070** `from`/`to` MUST be serialised as RFC 3339 UTC; the SDK MUST send them as
  query parameters (percent-encoded) and MUST validate `limit ≥ 1` (`BV-INPUT-004`).

## Dashboard, identity self-service, owner transfers (Complete)

| Operation | HTTP | Notes |
|-----------|------|-------|
| `Sys.DashboardSummary()` | `GET sys/dashboard/summary` | `audit_24h` and `attention` omitted for callers without audit read; MUST be optional |
| `Identity.Profile.Read()` | `GET /v2/sys/identity/profile/self` | never 404 |
| `Identity.Profile.ChangePassword(current, new)` | `POST /v2/sys/identity/profile/self/password` | 400 → `BV-INPUT-100`; 403 → `BV-AUTHZ-001` |
| `Identity.Profile.UpdateContact(email?, phone?)` | `POST /v2/sys/identity/profile/self/contact` | write-preserve: omit = keep, `""` = clear |
| `Identity.DefaultAccount.ReadSelf()` / `WriteSelf(spec)` | `GET/POST /v2/sys/identity/default-account/self` | `windows_password` returned only on GET to owner |
| `Identity.DefaultAccount.Read(mount, name)` / `Write` | `GET/POST /v2/sys/identity/default-account/{mount}/{name}` | admin |
| `Identity.SshSecurityKey.*` | `/v2/sys/identity/ssh-security-key[/self|/{mount}/{name}]` | |
| `Identity.NamespaceAssignment.Read/Write/List(mount, name)` | `/v2/sys/identity/ns-assignment/…` | login-restriction |
| `Sys.OwnerTransfer.Kv/Resource/AssetGroup/File(spec)` | `POST sys/{kv,resource,asset-group,file}-owner/transfer` | admin |
| `Sys.SsoSettings()` / `SsoProviders()` | `GET sys/sso/settings`, `GET sys/sso/providers` | |

- **SYS-080** All `/v2/sys/identity/*` paths MUST be pinned to `/v2` (TRN-071).

## Backup, restore, export, import (Complete)

| Operation | HTTP | Content |
|-----------|------|---------|
| `Sys.Backup()` → `bytes` | `POST sys/backup` | response `application/octet-stream`, `Content-Disposition: attachment; filename="backup.bvbk"` |
| `Sys.Restore(bytes)` → `{ EntriesRestored }` | `POST sys/restore` | request body raw `.bvbk` bytes |
| `Sys.Export(mountAndPrefix)` → `ExportData` | `GET sys/export/{path}` | `{version, created_at, mount, prefix, entries: [{key, value}]}` |
| `Sys.Import(mount, ExportData, force?)` → `{ Imported, Skipped }` | `POST sys/import/{mount}` | `{version, entries, force}` |
| `Sys.Exchange.Export/Import/ImportPreview/ImportApply` | `POST sys/exchange/*` | JSON; see Appendix A |

- **SYS-090** `Backup`/`Restore` MUST stream bodies (no full buffering above
  `MaxResponseBytes`) and MUST be excluded from retry and failover.
- **SYS-091** HMAC/magic/version/corruption failures on restore are `500` with
  `Backup HMAC verification failed: file may be tampered.` etc. → `BV-INPUT-103
  BackupFileInvalid` (non-retryable).

## Leases, wrapping, cubbyhole — not available

- **SYS-100** ⚠️ The server exposes **no** `sys/leases/*`, `sys/renew`, `sys/revoke`,
  `sys/wrapping/*`, or `cubbyhole/` surface over HTTP (the built-in default policy
  mentions them, but the routes do not exist). The SDK MUST NOT expose operations for
  them. `Response.LeaseId`/`LeaseDuration`/`Renewable` remain informational.
- **SYS-101** The SDK documentation MUST include a "Vault compatibility gaps" page listing
  these absences so users migrating from HashiCorp Vault clients are not surprised.

## Plugins and scheduled exports (Complete, informative)

`sys/plugins/…`, `sys/plugins/active-surfaces[?watch=1]`,
`sys/plugins/{plugin}/versions/{version}/asset/{sha256}`, and `sys/scheduled-exports/…`
are listed in Appendix A. The SDK MUST expose them through `Logical.*` at minimum and MAY
type them. Asset downloads MUST verify the SHA-256 of the received bytes against the path
parameter and fail with `BV-PROTOCOL-004 DigestMismatch` otherwise.
