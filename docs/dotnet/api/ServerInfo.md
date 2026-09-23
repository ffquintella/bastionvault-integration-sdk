# `ServerInfo` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/ServerInfo.cs`](../../../dotnet/BastionVault.IntegrationSdk/ServerInfo.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `ServerInfo`

#### `Initialized`

The wire `initialized` field. Present in the anonymous tier.

*Source: `dotnet/BastionVault.IntegrationSdk/ServerInfo.cs:12`*

#### `Sealed`

The wire `sealed` field. Present in the anonymous tier.

*Source: `dotnet/BastionVault.IntegrationSdk/ServerInfo.cs:15`*

#### `Version`

The wire `version` field. Present only with a live token.

*Source: `dotnet/BastionVault.IntegrationSdk/ServerInfo.cs:18`*

#### `StartedAt`

The wire `started_at` field, parsed from RFC 3339. Present only with a live token.

*Source: `dotnet/BastionVault.IntegrationSdk/ServerInfo.cs:21`*

#### `UptimeSeconds`

Seconds on the wire; absent when the server did not send it (anonymous tier).

*Source: `dotnet/BastionVault.IntegrationSdk/ServerInfo.cs:24`*

#### `StorageType`

The wire `storage_type` field. Present only with a live token.

*Source: `dotnet/BastionVault.IntegrationSdk/ServerInfo.cs:27`*

