# `SealStatus` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/SealStatus.cs`](../../../dotnet/BastionVault.IntegrationSdk/SealStatus.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `SealStatus`

#### `Sealed`

The wire `sealed` field.

*Source: `dotnet/BastionVault.IntegrationSdk/SealStatus.cs:11`*

#### `T`

The wire `t` field, verbatim: the server's `secret_shares` count.

*Source: `dotnet/BastionVault.IntegrationSdk/SealStatus.cs:14`*

#### `N`

The wire `n` field, verbatim: the server's `secret_threshold` count.

*Source: `dotnet/BastionVault.IntegrationSdk/SealStatus.cs:17`*

#### `KeyShares`

`max(T, N)`: the number of key shares, correctly named regardless of which wire field carried it.

*Source: `dotnet/BastionVault.IntegrationSdk/SealStatus.cs:20`*

#### `KeyThreshold`

`min(T, N)`: the unseal threshold, correctly named — never greater than <see cref="KeyShares"/>.

*Source: `dotnet/BastionVault.IntegrationSdk/SealStatus.cs:23`*

#### `Progress`

The wire `progress` field: how many unseal keys have been supplied so far.

*Source: `dotnet/BastionVault.IntegrationSdk/SealStatus.cs:26`*

