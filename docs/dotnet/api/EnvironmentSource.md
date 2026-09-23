# `EnvironmentSource` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/EnvironmentSource.cs`](../../../dotnet/BastionVault.IntegrationSdk/EnvironmentSource.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `EnvironmentSource`

#### `Process`

Reads the real process environment. The default for <see cref="BastionVaultClient"/>'s primary constructor.

*Source: `dotnet/BastionVault.IntegrationSdk/EnvironmentSource.cs:17`*

#### `None`

Reads nothing; only explicit values and built-in defaults apply. This is CFG-005's environment-free form.

*Source: `dotnet/BastionVault.IntegrationSdk/EnvironmentSource.cs:20`*

#### `FromMap(values)`

Reads from a caller-supplied key/value map, read once at the time this call is made.

*Source: `dotnet/BastionVault.IntegrationSdk/EnvironmentSource.cs:23`*

#### `TryGetValue(name, value)`

Attempts to read the named environment variable from this source.

*Source: `dotnet/BastionVault.IntegrationSdk/EnvironmentSource.cs:30`*

#### `TryGetValue(name, value)`

*(no `<summary>` doc comment.)*

*Source: `dotnet/BastionVault.IntegrationSdk/EnvironmentSource.cs:34`*

#### `TryGetValue(name, value)`

*(no `<summary>` doc comment.)*

*Source: `dotnet/BastionVault.IntegrationSdk/EnvironmentSource.cs:50`*

