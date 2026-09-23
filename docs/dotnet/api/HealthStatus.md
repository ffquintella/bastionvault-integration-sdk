# `HealthStatus` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/HealthStatus.cs`](../../../dotnet/BastionVault.IntegrationSdk/HealthStatus.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `HealthStatus`

#### `State`

The classification derived from this response's body, per SYS-001's table.

*Source: `dotnet/BastionVault.IntegrationSdk/HealthStatus.cs:28`*

#### `Initialized`

The wire `initialized` field.

*Source: `dotnet/BastionVault.IntegrationSdk/HealthStatus.cs:31`*

#### `Sealed`

The wire `sealed` field.

*Source: `dotnet/BastionVault.IntegrationSdk/HealthStatus.cs:34`*

#### `Standby`

The wire `standby` field.

*Source: `dotnet/BastionVault.IntegrationSdk/HealthStatus.cs:37`*

#### `ClusterHealthy`

The wire `cluster_healthy` field.

*Source: `dotnet/BastionVault.IntegrationSdk/HealthStatus.cs:40`*

#### `StatusCode`

The HTTP status the server actually sent (200, 429, 501 or 503 per SYS-001's table).

*Source: `dotnet/BastionVault.IntegrationSdk/HealthStatus.cs:43`*

#### `Raw`

The exact parsed body, for forward-compatible field access (TRN-043's precedent).

*Source: `dotnet/BastionVault.IntegrationSdk/HealthStatus.cs:46`*

