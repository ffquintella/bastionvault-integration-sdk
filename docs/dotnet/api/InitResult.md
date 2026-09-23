# `InitResult` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/InitResult.cs`](../../../dotnet/BastionVault.IntegrationSdk/InitResult.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `InitResult`

#### `Keys`

The unseal key shares, in the order the server returned them. A fresh list of fresh
<see cref="SecretString"/> instances on each read; see the type's remarks.

*Source: `dotnet/BastionVault.IntegrationSdk/InitResult.cs:55`*

#### `RootToken`

The initial root token. A fresh <see cref="SecretString"/> on each read; see the type's remarks.

*Source: `dotnet/BastionVault.IntegrationSdk/InitResult.cs:66`*

#### `KeyCount`

How many key shares the server returned. Readable after <see cref="Dispose"/>; a count is not secret material.

*Source: `dotnet/BastionVault.IntegrationSdk/InitResult.cs:76`*

#### `IsDisposed`

Whether <see cref="Dispose"/> has run, and therefore whether the owned buffers have been zeroed.

*Source: `dotnet/BastionVault.IntegrationSdk/InitResult.cs:79`*

#### `Dispose()`

SYS-011: overwrites every owned buffer with `'\0'`. Idempotent.

*Source: `dotnet/BastionVault.IntegrationSdk/InitResult.cs:82`*

#### `ToString()`

SYS-011: always redacted, and never carries a key share or the root token.

*Source: `dotnet/BastionVault.IntegrationSdk/InitResult.cs:99`*

