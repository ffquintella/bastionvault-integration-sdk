# `KvTypes` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/KvTypes.cs`](../../../dotnet/BastionVault.IntegrationSdk/KvTypes.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `KvV1Secret`

#### `Data`

The stored object (the wire envelope's `data`).

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:13`*

#### `LeaseDuration`

The wire's `lease_duration` in seconds, defaulted to 3600 by the server (07 §KV v1).

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:16`*

#### `Renewable`

The wire's `renewable`, true when a `ttl`/`lease` field was stored.

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:19`*

### `KvV2Config`

#### `MaxVersions`

The number of versions the engine retains per secret.

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:129`*

#### `CasRequired`

Whether the engine requires check-and-set on every write.

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:132`*

#### `DeleteVersionAfter`

KV2-010: a Go-style duration string; `"0s"` disables version expiry.

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:135`*

#### `DeleteVersionAfterDuration`

KV2-010: the parsed view of <see cref="DeleteVersionAfter"/>, or `null`
when the string is absent or is not a duration the SDK can parse. `"0s"` parses to
`Zero`, KV2-010's "disabled".

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:142`*

#### `Environments`

KV2-024: the advisory environment registry. The SDK never validates an `env` argument
against it.

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:148`*

### `KvV2ConfigPatch`

#### `MaxVersions`

The new retained-version cap, or `null` to keep the current one.

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:159`*

#### `CasRequired`

The new check-and-set requirement, or `null` to keep the current one.

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:162`*

#### `DeleteVersionAfter`

The new version-expiry duration string, or `null` to keep the current one.

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:165`*

#### `DeleteVersionAfterDuration`

KV2-010: the new version-expiry duration, formatted Go-style and sent as
<see cref="DeleteVersionAfter"/> when set. Setting both this and <see cref="DeleteVersionAfter"/>
to conflicting values is a caller error, rejected client-side as `BV-INPUT-001`.

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:172`*

#### `Environments`

The new environment registry, or `null` to keep the current one.

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:175`*

### `KvV2Metadata`

#### `CurrentVersion`

The newest version number.

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:96`*

#### `OldestVersion`

The oldest version still retained.

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:99`*

#### `MaxVersions`

The engine's retained-version cap that applies to this secret.

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:102`*

#### `CasRequired`

Whether check-and-set is required for writes.

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:105`*

#### `DeleteVersionAfter`

KV2-010: a Go-style duration string, `"0s"` when version expiry is disabled. Carried
as the wire's string (D-M4-5 pins 07 §Types' `DeleteVersionAfter: string`).

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:111`*

#### `CreatedTime`

When the secret was first written.

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:114`*

#### `UpdatedTime`

When the secret was last written.

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:117`*

#### `Versions`

Every retained version, keyed by version number.

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:120`*

### `KvV2Secret`

#### `Data`

The merged secret data, or `null` when this version is soft-deleted
(KV2-004). With `env` given, this is `merge(base, envs[env])` — the server
performs the merge, the SDK never does (KV2-021).

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:83`*

#### `Metadata`

This version's metadata.

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:86`*

#### `State`

KV2-004's derived state.

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:89`*

### `KvV2VersionMetadata`

#### `Version`

The version number.

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:42`*

#### `CreatedTime`

When this version was created.

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:45`*

#### `DeletionTime`

When this version was soft-deleted, or `null` when it is live (the wire spells "live" as `""`).

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:48`*

#### `Destroyed`

Whether this version was permanently destroyed.

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:51`*

#### `Username`

KV2-011: a BastionVault extension, optional. Absent on a server that does not record it.

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:56`*

#### `Operation`

KV2-011: a BastionVault extension, optional. A `string` and not an enum
(D-M4-5): the value set (`create`, `update`, `restore`) is the server's, and
an enum would need an invented member for a value a later server adds (D-M1c-25).

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:63`*

#### `ResolvedEnv`

KV2-020: the environment the read resolved to, or `null` when none was
requested or the secret declares no `envs` (07 §Environments).

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:69`*

#### `AvailableEnvs`

KV2-020: the environments this secret declares overrides for; empty when it declares none.

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:72`*

### `KvWriteOptions`

#### `Cas`

KV2-003's check-and-set version. `0` means "must not exist yet" and is sent as
`0`, never omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:189`*

#### `Env`

KV2-002: the single environment this write targets. Mutually exclusive with
<see cref="Envs"/>, and may contain neither `/` nor a control character.

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:195`*

#### `Envs`

KV2-021: the full per-environment override map for a multi-environment replace. Mutually
exclusive with <see cref="Env"/>.

*Source: `dotnet/BastionVault.IntegrationSdk/KvTypes.cs:201`*

