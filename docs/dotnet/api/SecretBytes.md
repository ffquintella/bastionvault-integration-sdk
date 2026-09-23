# `SecretBytes` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/SecretBytes.cs`](../../../dotnet/BastionVault.IntegrationSdk/SecretBytes.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `SecretBytes`

#### `Empty`

A <see cref="SecretBytes"/> holding no value.

*Source: `dotnet/BastionVault.IntegrationSdk/SecretBytes.cs:20`*

#### `HasValue`

Whether this instance holds a non-empty value.

*Source: `dotnet/BastionVault.IntegrationSdk/SecretBytes.cs:23`*

#### `Reveal()`

Returns the underlying bytes. Named deliberately (rather than a property) so that reading
the secret out is always a visible, searchable call site.

*Source: `dotnet/BastionVault.IntegrationSdk/SecretBytes.cs:29`*

#### `ToString()`

Always redacted; never includes the underlying value (CNF-031).

*Source: `dotnet/BastionVault.IntegrationSdk/SecretBytes.cs:35`*

#### `Equals(other)`

*(no `<summary>` doc comment.)*

*Source: `dotnet/BastionVault.IntegrationSdk/SecretBytes.cs:41`*

#### `Equals(obj)`

*(no `<summary>` doc comment.)*

*Source: `dotnet/BastionVault.IntegrationSdk/SecretBytes.cs:57`*

#### `GetHashCode()`

*(no `<summary>` doc comment.)*

*Source: `dotnet/BastionVault.IntegrationSdk/SecretBytes.cs:63`*

