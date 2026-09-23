# `Mounts` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/Mounts.cs`](../../../dotnet/BastionVault.IntegrationSdk/Mounts.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `MountDetail`

#### `Type`

The engine type, e.g. `kv-v2`.

*Source: `dotnet/BastionVault.IntegrationSdk/Mounts.cs:27`*

#### `Description`

The mount's description, or `null` when the server omitted it.

*Source: `dotnet/BastionVault.IntegrationSdk/Mounts.cs:30`*

#### `Uuid`

The mount's UUID, or `null` when the server omitted it.

*Source: `dotnet/BastionVault.IntegrationSdk/Mounts.cs:33`*

#### `Options`

The mount's `options` map, or `null` when the server omitted it. Values stay raw: the wire does not fix their types.

*Source: `dotnet/BastionVault.IntegrationSdk/Mounts.cs:36`*

### `MountInfo`

#### `Type`

The engine type, e.g. `kv-v2`. One of <see cref="MountTypes"/> on a current server (SYS-021).

*Source: `dotnet/BastionVault.IntegrationSdk/Mounts.cs:14`*

#### `Description`

The mount's description. The empty string when the server sent one; `null` when it sent no field at all.

*Source: `dotnet/BastionVault.IntegrationSdk/Mounts.cs:17`*

### `MountRequest`

#### `Type`

The engine or auth-method type. Required by the server; one of <see cref="MountTypes"/> on a current server (SYS-021).

*Source: `dotnet/BastionVault.IntegrationSdk/Mounts.cs:66`*

#### `Description`

An optional human-readable description.

*Source: `dotnet/BastionVault.IntegrationSdk/Mounts.cs:69`*

#### `Options`

The engine's own options map, sent verbatim when non-empty and omitted when `null`.

*Source: `dotnet/BastionVault.IntegrationSdk/Mounts.cs:72`*

### `MountTable`

#### `Secret`

The secrets-engine mounts, keyed by path with a trailing `/` (SYS-022).

*Source: `dotnet/BastionVault.IntegrationSdk/Mounts.cs:47`*

#### `Auth`

The auth-method mounts, keyed by path with a trailing `/` and no `auth/` prefix (SYS-022, SYS-030).

*Source: `dotnet/BastionVault.IntegrationSdk/Mounts.cs:50`*

### `MountTypes`

#### `Secret`

Every secrets-engine type SYS-021 names, in specification order.

*Source: `dotnet/BastionVault.IntegrationSdk/Mounts.cs:123`*

#### `Auth`

Every auth-method type SYS-021 names, in specification order, including `Cert`.

*Source: `dotnet/BastionVault.IntegrationSdk/Mounts.cs:130`*

