# `Capabilities` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/Capabilities.cs`](../../../dotnet/BastionVault.IntegrationSdk/Capabilities.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `Capabilities`

#### `ByPath`

SYS-050: read from the wire's `capabilities` map, never the duplicated top-level keys.

*Source: `dotnet/BastionVault.IntegrationSdk/Capabilities.cs:85`*

#### `NamespaceOperable`

The wire `namespace_operable` field.

*Source: `dotnet/BastionVault.IntegrationSdk/Capabilities.cs:88`*

#### `TokenNamespace`

The wire `token_namespace` field: the namespace the token is bound to.

*Source: `dotnet/BastionVault.IntegrationSdk/Capabilities.cs:91`*

#### `ActiveNamespace`

The wire `active_namespace` field: the namespace the request was made against.

*Source: `dotnet/BastionVault.IntegrationSdk/Capabilities.cs:94`*

#### `Can(path, capability)`

SYS-053's convenience: whether `capability` is granted on
`path` among this result's already-fetched capabilities. `Deny`
overrides every other entry for the path; `Root` implies every
capability including `capability` itself. A path this result did not ask
about, or one the server answered with no capabilities at all, is never operable.

*Source: `dotnet/BastionVault.IntegrationSdk/Capabilities.cs:103`*

