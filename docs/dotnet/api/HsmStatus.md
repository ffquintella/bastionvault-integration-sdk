# `HsmStatus` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/HsmStatus.cs`](../../../dotnet/BastionVault.IntegrationSdk/HsmStatus.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `HsmStatus`

#### `Type`

The wire `type` field: `shamir` or `hsm` on a current server, left as a string because the set is open.

*Source: `dotnet/BastionVault.IntegrationSdk/HsmStatus.cs:20`*

#### `AutoUnseal`

The wire `auto_unseal` field, or `null` when the server omitted it.

*Source: `dotnet/BastionVault.IntegrationSdk/HsmStatus.cs:23`*

#### `Sealed`

The wire `sealed` field, or `null` when the server omitted it.

*Source: `dotnet/BastionVault.IntegrationSdk/HsmStatus.cs:26`*

#### `Initialized`

The wire `initialized` field, or `null` when the server omitted it.

*Source: `dotnet/BastionVault.IntegrationSdk/HsmStatus.cs:29`*

#### `Raw`

The whole response object, verbatim, for the fields the specification's ellipsis leaves open.

*Source: `dotnet/BastionVault.IntegrationSdk/HsmStatus.cs:32`*

