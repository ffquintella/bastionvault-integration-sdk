# `KvOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/KvOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/KvOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `KvOperations`

#### `V1`

KV v1: flat, optional lease, no environments (KV1-001…KV1-004).

*Source: `dotnet/BastionVault.IntegrationSdk/KvOperations.cs:45`*

#### `V2`

KV v2: versions, CAS, soft delete, metadata, environments, path helpers (KV2-001…KV2-030).

*Source: `dotnet/BastionVault.IntegrationSdk/KvOperations.cs:48`*

#### `DetectVersionAsync(mount, options, cancellationToken)`

KV-001: which KV engine is mounted at `mount`, via
`Sys.MountTypeOf`'s cached lookup (SYS-026). `kv` is
`V1` and `kv-v2` is `V2`; any other type is
`BV-KV-010 NotAKvMount`, and a mount that does not exist is `BV-NOTFOUND-002`.

The requirement names `Sys.MountTypeOf` specifically, rather than "a mount lookup", and
that matters: the SYS-026 cache is shared per client, so a loop that detects the version of
twenty mounts issues one `sys/mounts` request rather than twenty. A private lookup here
would have been a second, uncoordinated cache.

**Spec:** `Kv.DetectVersion — KV-001`

*Source: `dotnet/BastionVault.IntegrationSdk/KvOperations.cs:68`*

#### `ReadManyAsync(mount, paths, options, cancellationToken)`

KV-010 / BAT-007: reads many KV v2 secrets in one request, by composing the
KV2-030 `{mount}/data/{path}` paths, sending them through `Sys.Batch` and
unwrapping each result. The returned map is keyed by the caller's own paths, in the order
they were given.

This is the operation section 14 exists to make people use. `map(read)` over a
list of twenty paths is twenty requests against a guard that bans at 200 in 10 s; this is
one.






The fallback (BAT-007). A server that predates batching answers `sys/batch`
with `BV-SERVER-004`, and the SDK then reads the paths one at a time through the
rate gate rather than failing. That costs `1 + N` requests and is the slow path
on purpose: it is a compatibility shim, not the intended shape. No other error is caught —
a `403` on `sys/batch` is the caller's problem to see, not something to paper
over with N more requests that will each get their own `403`.






Both paths yield the same two-state entry, which is the point of
<see cref="KvReadManyEntry"/>: a path either produced a secret or produced an error, and a
caller does not have to know which path answered. That is why the fallback reads through
`GetSecretAsync` and not
`ReadSecretAsync` — the latter returns `null`
for an absent secret, while a batch reports the same absence as a `404` result, so
using it would make "missing" mean two different things depending on the server's age.
The one place the paths legitimately differ is
`Metadata`, which the batch route may minimise away and a
standalone read always carries.

**Spec:** `Kv.ReadMany — KV-010`

*Source: `dotnet/BastionVault.IntegrationSdk/KvOperations.cs:120`*

### `KvReadManyEntry`

#### `Data`

The secret's data, or `null` when this path failed or when the version is
soft-deleted (KV2-004).

*Source: `dotnet/BastionVault.IntegrationSdk/KvOperations.cs:304`*

#### `Metadata`

The version metadata, or `null` when this path failed or when the batch
payload carried none. A fallback read (BAT-007) always carries it; a batched one may not.

*Source: `dotnet/BastionVault.IntegrationSdk/KvOperations.cs:310`*

#### `Error`

The mapped error for this path, or `null` when it resolved.

*Source: `dotnet/BastionVault.IntegrationSdk/KvOperations.cs:313`*

#### `IsSuccess`

Whether this path resolved rather than failing.

*Source: `dotnet/BastionVault.IntegrationSdk/KvOperations.cs:316`*

#### `State`

KV2-004's derived state: no data and a deletion time. `Live` for a failed path.

*Source: `dotnet/BastionVault.IntegrationSdk/KvOperations.cs:319`*

