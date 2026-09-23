# `SysComplete` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/SysComplete.cs`](../../../dotnet/BastionVault.IntegrationSdk/SysComplete.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `ClusterNodeResult`

#### `Url`

The candidate's base URL, as discovery produced it.

*Source: `dotnet/BastionVault.IntegrationSdk/SysComplete.cs:79`*

#### `Succeeded`

Whether the operation completed against this node.

*Source: `dotnet/BastionVault.IntegrationSdk/SysComplete.cs:82`*

#### `SealStatus`

`Sys.UnsealClusterWide`'s per-node seal status; always `null` for `Sys.SealClusterWide`, which has no response body.

*Source: `dotnet/BastionVault.IntegrationSdk/SysComplete.cs:85`*

#### `Error`

The failure this node answered with, or `null` when it succeeded.

*Source: `dotnet/BastionVault.IntegrationSdk/SysComplete.cs:88`*

### `DashboardSummary`

#### `Audit24h`

The wire `audit_24h` object, or `null` when the caller cannot read audit.

*Source: `dotnet/BastionVault.IntegrationSdk/SysComplete.cs:54`*

#### `Attention`

The wire `attention` value, or `null` when the caller cannot read audit.

*Source: `dotnet/BastionVault.IntegrationSdk/SysComplete.cs:57`*

#### `Raw`

The whole summary object as sent.

*Source: `dotnet/BastionVault.IntegrationSdk/SysComplete.cs:60`*

### `DosConfig`

#### `Enabled`

The wire `enabled` field.

*Source: `dotnet/BastionVault.IntegrationSdk/SysComplete.cs:20`*

#### `WindowSecs`

The wire `window_secs` field.

*Source: `dotnet/BastionVault.IntegrationSdk/SysComplete.cs:23`*

#### `MaxRequests`

The wire `max_requests` field.

*Source: `dotnet/BastionVault.IntegrationSdk/SysComplete.cs:26`*

#### `AuthMaxRequests`

The wire `auth_max_requests` field.

*Source: `dotnet/BastionVault.IntegrationSdk/SysComplete.cs:29`*

#### `BanSecs`

The wire `ban_secs` field.

*Source: `dotnet/BastionVault.IntegrationSdk/SysComplete.cs:32`*

#### `RefreshSecs`

The wire `refresh_secs` field.

*Source: `dotnet/BastionVault.IntegrationSdk/SysComplete.cs:35`*

#### `Raw`

The whole object as sent; `Undefined` on a patch the caller built.

*Source: `dotnet/BastionVault.IntegrationSdk/SysComplete.cs:38`*

### `RestoreResult`

#### `EntriesRestored`

The wire `entries_restored` field.

*Source: `dotnet/BastionVault.IntegrationSdk/SysComplete.cs:10`*

#### `Raw`

The whole response body as sent.

*Source: `dotnet/BastionVault.IntegrationSdk/SysComplete.cs:13`*

