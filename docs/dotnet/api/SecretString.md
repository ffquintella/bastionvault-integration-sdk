# `SecretString` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/SecretString.cs`](../../../dotnet/BastionVault.IntegrationSdk/SecretString.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `SecretString`

#### `Empty`

A <see cref="SecretString"/> holding no value.

*Source: `dotnet/BastionVault.IntegrationSdk/SecretString.cs:18`*

#### `HasValue`

Whether this instance holds a non-empty value.

*Source: `dotnet/BastionVault.IntegrationSdk/SecretString.cs:21`*

#### `Reveal()`

Returns the underlying value. Named deliberately (rather than a property) so that reading the
secret out is always a visible, searchable call site.

*Source: `dotnet/BastionVault.IntegrationSdk/SecretString.cs:27`*

#### `ToString()`

Always redacted; never includes the underlying value (CNF-031).

*Source: `dotnet/BastionVault.IntegrationSdk/SecretString.cs:33`*

#### `Equals(other)`

*(no `<summary>` doc comment.)*

*Source: `dotnet/BastionVault.IntegrationSdk/SecretString.cs:39`*

#### `Equals(obj)`

*(no `<summary>` doc comment.)*

*Source: `dotnet/BastionVault.IntegrationSdk/SecretString.cs:45`*

#### `GetHashCode()`

*(no `<summary>` doc comment.)*

*Source: `dotnet/BastionVault.IntegrationSdk/SecretString.cs:51`*

