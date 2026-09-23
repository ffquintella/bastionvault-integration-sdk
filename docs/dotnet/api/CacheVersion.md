# `CacheVersion` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/CacheVersion.cs`](../../../dotnet/BastionVault.IntegrationSdk/CacheVersion.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `CacheVersion`

#### `State`

Whether this is a fresh answer or a `304` (CCH-002).

*Source: `dotnet/BastionVault.IntegrationSdk/CacheVersion.cs:38`*

#### `NotModified`

Shorthand for <see cref="State"/> `== NotModified` — the shape a caller actually branches on.

*Source: `dotnet/BastionVault.IntegrationSdk/CacheVersion.cs:41`*

#### `Version`

The wire `version` aggregate. `null` on a <see cref="NotModified"/> result.

*Source: `dotnet/BastionVault.IntegrationSdk/CacheVersion.cs:44`*

#### `Topics`

The wire `topics` map, one epoch per topic the server named. See this type's remarks
for CCH-005 (a requested topic missing here, not `0`) and CCH-004 (only an increase is
a change signal). `null` on a <see cref="NotModified"/> result.

*Source: `dotnet/BastionVault.IntegrationSdk/CacheVersion.cs:51`*

#### `Coarse`

The wire `coarse` flag. `null` on a <see cref="NotModified"/> result.

*Source: `dotnet/BastionVault.IntegrationSdk/CacheVersion.cs:54`*

#### `ETag`

The response's `ETag`, to carry into the next call's `ifNoneMatch`.

*Source: `dotnet/BastionVault.IntegrationSdk/CacheVersion.cs:57`*

