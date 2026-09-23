# `KvV2Operations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/KvV2Operations.cs`](../../../dotnet/BastionVault.IntegrationSdk/KvV2Operations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `KvV2Operations`

#### `DataPath(path, mount)`

KV2-030: the logical `{mount}/data/{path}` path, for `Sys.Batch` and policy authoring.

No HTTP call — a client-side string helper. Never `null`. Conformance: Core (KV2-030). No error codes beyond the common set (ERR-061); throws only `BV-INPUT-001` for an unsafe `mount`/`path`.

**Spec:** `Kv.V2.DataPath — KV2-030`

*Source: `dotnet/BastionVault.IntegrationSdk/KvV2Operations.cs:45`*

#### `MetadataPath(path, mount)`

KV2-030: the logical `{mount}/metadata/{path}` path.

No HTTP call — a client-side string helper. Never `null`. Conformance: Core (KV2-030). No error codes beyond the common set (ERR-061); throws only `BV-INPUT-001` for an unsafe `mount`/`path`.

**Spec:** `Kv.V2.MetadataPath — KV2-030`

*Source: `dotnet/BastionVault.IntegrationSdk/KvV2Operations.cs:53`*

#### `DestroyPath(path, mount)`

KV2-030: the logical `{mount}/destroy/{path}` path.

No HTTP call — a client-side string helper. Never `null`. Conformance: Core (KV2-030). No error codes beyond the common set (ERR-061); throws only `BV-INPUT-001` for an unsafe `mount`/`path`.

**Spec:** `Kv.V2.DestroyPath — KV2-030`

*Source: `dotnet/BastionVault.IntegrationSdk/KvV2Operations.cs:61`*

#### `UndeletePath(path, mount)`

KV2-030: the logical `{mount}/undelete/{path}` path.

No HTTP call — a client-side string helper. Never `null`. Conformance: Core (KV2-030). No error codes beyond the common set (ERR-061); throws only `BV-INPUT-001` for an unsafe `mount`/`path`.

**Spec:** `Kv.V2.UndeletePath — KV2-030`

*Source: `dotnet/BastionVault.IntegrationSdk/KvV2Operations.cs:69`*

#### `ReadSecretAsync(path, mount, version, env, options, cancellationToken)`

KV2-001, KV2-004, KV2-006, KV2-020: `GET {mount}/data/{path}?version=N&amp;env=E`.

Both selectors travel as query parameters and never in a body (KV2-001); a
`version` of `0` or `null` means "latest" and is
omitted. A `404` with an empty body is `null` — including when
`env` was given, which is KV2-006's strict-miss row — and this method never
performs the extra metadata read that KV2-006 permits only to <see cref="GetSecretAsync"/>.
A soft-deleted version comes back as a <see cref="KvV2Secret"/> with no
`Data` and `SoftDeleted`, never as
`null` and never as an error (KV2-004).

**Spec:** `Kv.V2.ReadSecret — KV2-001`

*Source: `dotnet/BastionVault.IntegrationSdk/KvV2Operations.cs:89`*

#### `GetSecretAsync(path, mount, version, env, options, cancellationToken)`

KV2-004, KV2-006: <see cref="ReadSecretAsync"/> with the three absences turned into errors.

A soft-deleted version raises `BV-KV-007 VersionSoftDeleted`. A `404` with an
empty body raises `BV-KV-001 SecretNotFound`, except that when
`env` was given this method performs one extra <see cref="ReadMetadataAsync"/>
— the read KV2-006 permits only here and only then — and raises
`BV-KV-006 EnvironmentNotDeclared` when it shows the secret exists. "If permitted" is
read as written: a `403` on the metadata read means the SDK cannot tell the two apart,
so it falls back to `BV-KV-001` rather than guessing.

**Spec:** `Kv.V2.ReadSecret — KV2-004`

*Source: `dotnet/BastionVault.IntegrationSdk/KvV2Operations.cs:143`*

#### `WriteSecretAsync(path, data, mount, options, requestOptions, cancellationToken)`

KV2-002, KV2-003: `POST {mount}/data/{path}` with body
`{"data": {…}, "options": {"cas": N}?, "env": E?, "envs": {…}?}`.

Four client-side refusals, all before any request is sent and all `BV-INPUT-001`
(KV2-002): an empty or absent `data`, both `Env` and `Envs` set,
an `Env` containing `/` or a control character, and — RF-3, the same rule applied
to every key of `Envs`, not only to `Env`, since KV2-002's environment-name rule
applies wherever a name appears on the wire — an `Envs` key containing `/` or a
control character. `Cas = 0` is sent as `0` rather than omitted, because KV2-003
gives it the distinct meaning "must not exist yet"; `Cas = null` omits the whole
`options` object.

**Spec:** `Kv.V2.WriteSecret — KV2-002`

*Source: `dotnet/BastionVault.IntegrationSdk/KvV2Operations.cs:195`*

#### `PatchEnvironmentAsync(path, env, overrides, mount, cas, options, cancellationToken)`

KV2-021: a targeted single-environment patch. Sends the same write with `env` set; the
server carries the base set and the other environments forward, so the SDK performs
no read-merge-write for this.

Wire body: `data` (from `overrides`), `env`, `options.cas`. Never returns `null`. Conformance: Core (KV2-021). Errors beyond the common set (ERR-061): `BV-INPUT-001`, `BV-KV-003 CasMismatch`.

**Spec:** `Kv.V2.WriteSecret — KV2-021`

*Source: `dotnet/BastionVault.IntegrationSdk/KvV2Operations.cs:239`*

#### `WriteAllEnvironmentsAsync(path, baseData, envs, mount, cas, options, cancellationToken)`

KV2-021: a full multi-environment replace. Sends the base set as `data` and the whole
override map as `envs`; again no client-side read-merge-write.

Wire body: `data` (from `baseData`), `envs`, `options.cas`. Never returns `null`. Conformance: Core (KV2-021, KV2-022). Errors beyond the common set (ERR-061): `BV-INPUT-001`, `BV-KV-003 CasMismatch`.

**Spec:** `Kv.V2.WriteSecret — KV2-021`

*Source: `dotnet/BastionVault.IntegrationSdk/KvV2Operations.cs:265`*

#### `SoftDeleteAsync(path, mount, versions, options, cancellationToken)`

KV2-004: `DELETE {mount}/data/{path}`, the soft delete — 07 mandates that no
`delete/{path}` route exists. With no `versions` the server deletes the
latest version only and no body is sent.

An explicitly supplied empty list is refused with `BV-INPUT-002` rather than
silently treated as "no body": the two mean different things to the caller, and the silent
reading would widen a delete the caller narrowed. KV2-007 states this for
<see cref="UndeleteAsync"/> and <see cref="DestroyAsync"/>; applying it to an explicit empty
list here is ruled in DR-0009's addendum, D-M4-10.

**Spec:** `Kv.V2.SoftDelete — KV2-004`

*Source: `dotnet/BastionVault.IntegrationSdk/KvV2Operations.cs:309`*

#### `UndeleteAsync(path, versions, mount, options, cancellationToken)`

KV2-007: `POST {mount}/undelete/{path}`. A non-empty version list is required (`BV-INPUT-002`).

Wire: `versions` array in the body. Returns nothing. Conformance: Core (KV2-007). Errors beyond the common set (ERR-061): `BV-INPUT-002`.

**Spec:** `Kv.V2.Undelete — KV2-007`

*Source: `dotnet/BastionVault.IntegrationSdk/KvV2Operations.cs:342`*

#### `DestroyAsync(path, versions, mount, options, cancellationToken)`

KV2-005, KV2-007: `POST {mount}/destroy/{path}`. Irreversible; a non-empty version list
is required (`BV-INPUT-002`).

Wire: `versions` array in the body. Returns nothing. Conformance: Core (KV2-005, KV2-007). Errors beyond the common set (ERR-061): `BV-INPUT-002`.

**Spec:** `Kv.V2.Destroy — KV2-005`

*Source: `dotnet/BastionVault.IntegrationSdk/KvV2Operations.cs:358`*

#### `ReadMetadataAsync(path, mount, options, cancellationToken)`

KV2-010, KV2-011: `GET {mount}/metadata/{path}`. `null` on a `404`
with an empty body.

Wire params: `path`/`mount` build the route. Conformance: Core (KV2-010, KV2-011). No error codes beyond the common set (ERR-061).

**Spec:** `Kv.V2.ReadMetadata — KV2-010`

*Source: `dotnet/BastionVault.IntegrationSdk/KvV2Operations.cs:374`*

#### `DeleteMetadataAsync(path, mount, options, cancellationToken)`

KV-002: `DELETE {mount}/metadata/{path}` — removes all versions permanently.

Returns nothing. Conformance: Core (KV2-011). No error codes beyond the common set (ERR-061).

**Spec:** `Kv.V2.DeleteMetadata — KV2-011`

*Source: `dotnet/BastionVault.IntegrationSdk/KvV2Operations.cs:433`*

#### `ListAsync(prefix, mount, options, cancellationToken)`

KV2-008: `LIST {mount}/metadata/` or `LIST {mount}/metadata/{prefix}/`. The
trailing slash is mandatory; a prefix containing `..` is refused with
`BV-INPUT-001`; a `404` with an empty body is an empty list.

Never returns `null`. Conformance: Core (KV2-008). Errors beyond the common set (ERR-061): `BV-INPUT-001`.

**Spec:** `Kv.V2.List — KV2-008`

*Source: `dotnet/BastionVault.IntegrationSdk/KvV2Operations.cs:460`*

#### `ReadConfigAsync(mount, options, cancellationToken)`

KV2-009, KV2-024: `GET {mount}/config`. `null` when the mount answers a `404` with an empty body.

Conformance: Core (KV2-009, KV2-024). No error codes beyond the common set (ERR-061).

**Spec:** `Kv.V2.ReadConfig — KV2-009`

*Source: `dotnet/BastionVault.IntegrationSdk/KvV2Operations.cs:484`*

#### `WriteConfigAsync(config, mount, options, cancellationToken)`

KV2-009: `POST {mount}/config`. Replaces the whole configuration — all four
fields are sent, because the server rewrites the full config and an omitted
`environments` would therefore clear the registry rather than leave it alone. Use
<see cref="UpdateConfigAsync"/> to change one field.

Wire body: `max_versions`, `cas_required`, `delete_version_after`, `environments` (all sent). Returns nothing. Conformance: Core (KV2-009). No error codes beyond the common set (ERR-061).

**Spec:** `Kv.V2.WriteConfig — KV2-009`

*Source: `dotnet/BastionVault.IntegrationSdk/KvV2Operations.cs:522`*

#### `UpdateConfigAsync(patch, mount, options, cancellationToken)`

KV2-009's mandated read-merge-write: reads the current configuration, overlays the fields
`patch` sets (a `null` field is left alone), writes the full
configuration back, and returns what it wrote.

Two round trips, by requirement rather than by choice: the server rewrites the whole config
on every write, so changing `environments` without first reading would clear
`max_versions` and the rest. A mount whose config read comes back absent raises
`BV-NOTFOUND-002 MountNotFound` — there is no configuration to merge into, and
inventing a default one to write would be the plausible guess D-M1c-25 forbids.

**Spec:** `Kv.V2.WriteConfig — KV2-009`

*Source: `dotnet/BastionVault.IntegrationSdk/KvV2Operations.cs:569`*

#### `WriteIfAbsentAsync(path, data, mount, options, cancellationToken)`

KV-011: <see cref="WriteSecretAsync"/> with `Cas = 0` (KV2-003's "must not exist yet"),
translating the server's `BV-KV-003 CasMismatch` into `BV-CONFLICT-006
SecretAlreadyExists`. The caller asked for "create, don't overwrite", so the conflict is
reported in those terms rather than the generic CAS mismatch a caller retrying with a real
version number would expect.

Wire as <see cref="WriteSecretAsync"/> with `options.cas = 0`. Never returns `null`. Conformance: Core (KV-011). Errors beyond the common set (ERR-061): `BV-CONFLICT-006 SecretAlreadyExists` (translated from the server's `BV-KV-003`).

**Spec:** `Kv.WriteIfAbsent — KV-011`

*Source: `dotnet/BastionVault.IntegrationSdk/KvV2Operations.cs:629`*

#### `UpdateWithRetryAsync(path, transform, mount, maxAttempts, options, cancellationToken)`

KV-012: reads the latest version, applies `transform`, writes it back with
`Cas` set to the version just read (`0` when the secret does not exist yet), and
retries on the server's `BV-KV-003 CasMismatch` up to `maxAttempts`
attempts in total.

Race semantics. This is a read-modify-write, not a transaction: between the read this
method performs and the write it sends, another writer may change the secret. A CAS
mismatch means someone else won that race; the loop then re-reads the now-current version
and re-applies `transform` to it. `transform` may
therefore run more than once and must be safe to re-run — a transform with an external side
effect (sending a notification, incrementing an outside counter) is not safe to pass here.






`transform` receives the whole <see cref="KvV2Secret"/>, not a bare data
map, so the caller can see the version and the soft-delete `State`;
it receives `null` when the secret does not exist yet. On exhaustion this
method surfaces the server's own `BV-KV-003` rather than inventing a code for "retries
exhausted" (D-M1c-25).

**Spec:** `Kv.UpdateWithRetry — KV-012`

*Source: `dotnet/BastionVault.IntegrationSdk/KvV2Operations.cs:683`*

#### `ReadFieldAsync(path, field, mount, version, env, options, cancellationToken)`

KV-013: <see cref="ReadSecretAsync"/> narrowed to one field (the `--field` equivalent).
`null` for both "no secret" and "no such field" — use
<see cref="GetFieldAsync"/> to tell them apart.

Wire as <see cref="ReadSecretAsync"/>; `field` selects a key from the response's `data` object client-side. Conformance: Core (KV-013). No error codes beyond the common set (ERR-061).

**Spec:** `Kv.ReadField — KV-013`

*Source: `dotnet/BastionVault.IntegrationSdk/KvV2Operations.cs:727`*

#### `GetFieldAsync(path, field, mount, version, env, options, cancellationToken)`

KV-013: <see cref="GetSecretAsync"/> narrowed to one field, raising `BV-KV-011
FieldNotFound` — with the secret's available field names in `Details.fields` — when
the field is absent. `BV-KV-001` and `BV-KV-007` propagate from
<see cref="GetSecretAsync"/> unchanged.

Wire as <see cref="GetSecretAsync"/>; `field` selects a key client-side. Never returns `null`. Conformance: Core (KV-013). Errors beyond the common set (ERR-061): `BV-KV-011 FieldNotFound`, `BV-KV-001`, `BV-KV-007`.

**Spec:** `Kv.GetField — KV-013`

*Source: `dotnet/BastionVault.IntegrationSdk/KvV2Operations.cs:749`*

