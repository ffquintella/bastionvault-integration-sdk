# 07 — KV Engine

Two engine types exist: `kv` (v1, flat, optional lease) and `kv-v2` (versioned, soft
delete, CAS, per-environment overrides). New `secret/` mounts are `kv-v2`.

All operations live under `Client.Kv`. Every operation takes `mount` (default `"secret"`)
and a secret `path` relative to the mount.

## Mount version detection

- **KV-001** `Kv.DetectVersion(mount)` MUST call `Sys.MountTypeOf(mount)` (cached,
  SYS-026) and return `V1` for `kv`, `V2` for `kv-v2`, and raise `BV-KV-010
  NotAKvMount` for anything else, `BV-NOTFOUND-002` when the mount does not exist.
- **KV-002** The `Kv.*` operations below are **version-explicit** (`Kv.V1.*`, `Kv.V2.*`).
  A version-agnostic façade `Kv.ReadSecret(mount, path)` MAY be offered and MUST use
  `DetectVersion`; when the detection call is not permitted (`BV-AUTHZ-001` on
  `sys/mounts`) it MUST fall back to assuming `V2` and document that.

## KV v1 (`Kv.V1`)

Path pattern: any path under the mount. Storage is the request body verbatim.

| Operation | HTTP | Body / Response |
|-----------|------|-----------------|
| `Kv.V1.Read(mount, path)` → `KvV1Secret?` | `GET {mount}/{path}` | `data` = stored object; `lease_duration` (default 3600), `renewable` when a `ttl`/`lease` field was stored |
| `Kv.V1.Write(mount, path, data, ttl?)` | `POST {mount}/{path}` | body = `data` merged with `{"ttl": "<duration>"}` when given → 204 |
| `Kv.V1.Delete(mount, path)` | `DELETE {mount}/{path}` | 204 |
| `Kv.V1.List(mount, prefix)` → `string[]` | `LIST {mount}/{prefix}/` | `{"keys": [...]}`; `404` empty → empty list |

- **KV1-001** `Write` with an empty `data` map MUST be rejected client-side
  (`BV-INPUT-001`); the server answers `Module kv data field is missing.` (400 →
  `BV-KV-002`).
- **KV1-002** `Read` of a missing path yields `404` empty body → `null`. `Kv.V1.Get`
  MUST raise `BV-KV-001 SecretNotFound` instead.
- **KV1-003** `KvV1Secret { Data: Map<string, Json>, LeaseDuration: Duration, Renewable: bool }`.
- **KV1-004** v1 ignores `?env=`; the SDK MUST NOT offer an `env` parameter on v1
  operations.

## KV v2 (`Kv.V2`)

Path groups under the mount:

| Server path | Ops | Purpose |
|-------------|-----|---------|
| `config` | read, write | engine config |
| `data/{path}` | read, write, delete | versioned data; **delete = soft delete** |
| `metadata/` and `metadata/{prefix}/` | list | key listing (trailing slash required) |
| `metadata/{path}` | read, delete | version metadata; delete = permanent removal of all versions |
| `destroy/{path}` | write | permanently destroy versions |
| `undelete/{path}` | write | recover soft-deleted versions |

⚠️ Differences from HashiCorp KV v2 the SDK MUST encode: there is **no** `delete/{path}`
route (soft delete goes through `DELETE data/{path}`); `metadata/{path}` has **no write**
(per-secret `max_versions`/`cas_required` cannot be set); there is **no** `subkeys/`,
no `patch` (HTTP `PATCH` is 405).

### Types

```
KvV2Secret {
  Data:      Map<string, Json>?      // null when the version is soft-deleted
  Metadata:  KvV2VersionMetadata
  State:     Live | SoftDeleted        // derived: Data == null && DeletionTime set
}
KvV2VersionMetadata {
  Version: int, CreatedTime: instant, DeletionTime: instant?, Destroyed: bool,
  Username: string?, Operation: create|update|restore?, ResolvedEnv: string?, AvailableEnvs: string[]
}
KvV2Metadata {
  CurrentVersion, OldestVersion, MaxVersions, CasRequired, DeleteVersionAfter: string,
  CreatedTime, UpdatedTime, Versions: Map<int, KvV2VersionMetadata>
}
KvV2Config { MaxVersions: int, CasRequired: bool, DeleteVersionAfter: string, Environments: string[] }
WriteOptions { Cas: int?, Env: string?, Envs: Map<string, Map<string, Json>>? }
```

### Operations

| Operation | HTTP | Notes |
|-----------|------|-------|
| `Kv.V2.ReadSecret(mount, path, version?, env?)` → `KvV2Secret?` | `GET {mount}/data/{path}?version=N&env=E` | selectors as **query parameters** only |
| `Kv.V2.GetSecret(...)` → `KvV2Secret` | same | raises `BV-KV-001` when null |
| `Kv.V2.WriteSecret(mount, path, data, WriteOptions?)` → `KvV2VersionMetadata` | `POST {mount}/data/{path}` body `{"data": {...}, "options": {"cas": N}?, "env": E?, "envs": {...}?}` | response `data` = `{version, created_time, deletion_time, destroyed}` |
| `Kv.V2.PatchEnvironment(mount, path, env, overrides, cas?)` | same as write with `env` | targeted single-environment patch |
| `Kv.V2.WriteAllEnvironments(mount, path, base, envs, cas?)` | same as write with `envs` | full multi-env replace |
| `Kv.V2.SoftDelete(mount, path, versions?)` | `DELETE {mount}/data/{path}` body `{"versions": [...]}?` | no body → latest only → 204 |
| `Kv.V2.Undelete(mount, path, versions[])` | `POST {mount}/undelete/{path}` `{"versions": [...]}` | non-empty required |
| `Kv.V2.Destroy(mount, path, versions[])` | `POST {mount}/destroy/{path}` `{"versions": [...]}` | irreversible; non-empty required |
| `Kv.V2.ReadMetadata(mount, path)` → `KvV2Metadata?` | `GET {mount}/metadata/{path}` | |
| `Kv.V2.DeleteMetadata(mount, path)` | `DELETE {mount}/metadata/{path}` | removes all versions → 204 |
| `Kv.V2.List(mount, prefix = "")` → `string[]` | `LIST {mount}/metadata/` or `LIST {mount}/metadata/{prefix}/` | trailing slash mandatory; `404` empty → `[]` |
| `Kv.V2.ReadConfig(mount)` / `WriteConfig(mount, KvV2Config)` | `GET/POST {mount}/config` | `environments` accepted on write |

- **KV2-001** `version` and `env` MUST be sent as query parameters and never in a body
  (the server ignores body-borne selectors on GET). `version = 0`/null means latest.
- **KV2-002** `WriteSecret` MUST reject client-side: empty/absent `data` (`BV-INPUT-001`;
  server: `Data field is missing from request.` → `BV-KV-002`), both `Env` and `Envs`
  set (`BV-INPUT-001`; server 400 `Request is invalid.`), an `Env` containing `/` or
  control characters (`BV-INPUT-001`).
- **KV2-003** CAS: `Check-and-set parameter did not match the current version.` →
  `BV-KV-003 CasMismatch`; `Check-and-set parameter required for this call.` →
  `BV-KV-004 CasRequired`. `Cas = 0` means "must not exist yet" and MUST be sent as `0`,
  not omitted.
- **KV2-004** Reading a soft-deleted version returns **200** with `data.data == null` and
  `data.metadata.deletion_time` set (the server also attaches a warning, which never
  reaches the wire). The SDK MUST return `KvV2Secret { Data: null, State: SoftDeleted }`
  — not null, not an error. `GetSecret` MUST raise `BV-KV-007 VersionSoftDeleted` for
  that state.
- **KV2-005** `Version has been permanently destroyed.` (404) → `BV-KV-005
  VersionDestroyed`; `Version does not exist.` (404) → `BV-KV-008 VersionNotFound`.
- **KV2-006** A `404` with **empty** body on `data/{path}` MUST be `null` from
  `ReadSecret`; with `env` set it MUST be `BV-KV-006 EnvironmentNotDeclared` from
  `GetSecret` when `ReadMetadata` (if permitted) shows the secret exists; otherwise
  `BV-KV-001`. `ReadSecret` MUST NOT perform the extra metadata read; only `GetSecret`
  MAY, and only when `Env` was given.
- **KV2-007** `Undelete`/`Destroy` with an empty version list → `BV-INPUT-002`
  client-side (server: `Request is invalid.`). Unknown secret → `Version does not exist.`
  → `BV-KV-008`.
- **KV2-008** `List` prefixes containing `..` → `BV-INPUT-001`.
- **KV2-009** `WriteConfig` MUST send only the fields set by the caller (server treats
  omitted `environments` as unchanged? — no: the server rewrites the full config; the SDK
  MUST therefore read-merge-write in `UpdateConfig(mount, patch)` and document that
  `WriteConfig` replaces).
- **KV2-010** `DeleteVersionAfter` is a duration string (`"0s"` = disabled); the SDK MUST
  accept a duration type and serialise Go-style.
- **KV2-011** Version metadata `username`/`operation` are BastionVault extensions and
  MUST be optional.

### Environments

A KV v2 secret stores a shared **base** set plus `envs: { name → overrides }`. A read with
`?env=E` returns `merge(base, envs[E])` (shallow, override wins) and metadata
`resolved_env`, `available_envs`.

| Request | Secret has `envs`? | Result |
|---------|--------------------|--------|
| no `env` | — | base only, `resolved_env = null` |
| `env=E`, declared | yes | merged |
| `env=E`, not declared | yes | **404 empty** (strict miss) |
| `env=E` | no `envs` | base; `env` ignored |

- **KV2-020** The SDK MUST expose `ResolvedEnv` and `AvailableEnvs` on every v2 read.
- **KV2-021** `PatchEnvironment` carries base and other envs forward server-side; the SDK
  MUST NOT read-merge-write for it.
- **KV2-022** Tokens whose `AuthInfo.EnvironmentScope.Scoped` is true (AppID env scoping,
  AUT-044) **must** supply `env` on every v2 read/write or the server answers `403
  Permission denied.`. The SDK MUST fail fast client-side with `BV-KV-009
  EnvironmentRequired` when such a token performs a v2 data operation without `env`, and
  MUST include the allowed globs in the hint.
- **KV2-023** Policies may set `required_parameters = ["env"]`; a 403 on a v2 read without
  `env` MUST get the enrichment note "the policy may require `env`".
- **KV2-024** The `config.environments` registry is advisory; the SDK MUST NOT validate
  `env` values against it.

### Path helpers

- **KV2-030** The SDK MUST offer `Kv.V2.DataPath(mount, path)`, `MetadataPath`,
  `DestroyPath`, `UndeletePath` returning the full logical path (for `Sys.Batch` and
  policy authoring) and MUST strip leading/trailing slashes from `path`.

## Convenience helpers (all levels)

- **KV-010** `Kv.ReadMany(mount, paths[], env?)` — batch read via `Sys.Batch`
  ([14 — BAT-007](14-batch-and-request-efficiency.md#batch-endpoint)).
- **KV-011** `Kv.V2.WriteIfAbsent(mount, path, data)` = `WriteSecret` with `Cas = 0`,
  translating `BV-KV-003` into `BV-CONFLICT-006 SecretAlreadyExists`.
- **KV-012** `Kv.V2.UpdateWithRetry(mount, path, fn(current) -> next, maxAttempts = 3)`
  — read, apply, CAS-write, retry on `BV-KV-003`. The SDK MUST document the read-modify-
  write race semantics.
- **KV-013** `Kv.V2.ReadField(mount, path, field, env?)` → `Json?` (the `--field`
  equivalent), raising `BV-KV-011 FieldNotFound` from the `Get` variant.

## Server error strings (recognition)

| Server message | HTTP | Code |
|----------------|------|------|
| `Module kv data field is missing.` | 400 | `BV-KV-002` |
| `Data field is missing from request.` | 400 | `BV-KV-002` |
| `Check-and-set parameter did not match the current version.` | 400 | `BV-KV-003` |
| `Check-and-set parameter required for this call.` | 400 | `BV-KV-004` |
| `Version has been permanently destroyed.` | 404 | `BV-KV-005` |
| `Version does not exist.` | 404 | `BV-KV-008` |
| `Request is invalid.` on `data/` write | 400 | `BV-INPUT-100` (with env/envs hint) |
| `Permission denied.` on `data/` with env-scoped token | 403 | `BV-AUTHZ-001` + KV2-023 note |
