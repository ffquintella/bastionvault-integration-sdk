# `BatchTypes` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/BatchTypes.cs`](../../../dotnet/BastionVault.IntegrationSdk/BatchTypes.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `BatchOperation`

#### `Operation`

Which of the four kinds this is.

*Source: `dotnet/BastionVault.IntegrationSdk/BatchTypes.cs:36`*

#### `Path`

BAT-003: the full logical path including the mount (`secret/data/x`), never
prefixed with `/v1/`. A leading `/` is stripped by the SDK before sending.

*Source: `dotnet/BastionVault.IntegrationSdk/BatchTypes.cs:42`*

#### `Data`

BAT-004: required for `Write` and rejected
(`BV-INPUT-001`) for the other three. Sent verbatim as the operation's `data`
member, so a KV v2 write carries the engine's own `{"data": {…}}` envelope.

*Source: `dotnet/BastionVault.IntegrationSdk/BatchTypes.cs:49`*

### `BatchResult`

#### `Status`

The per-operation HTTP status the server reported.

*Source: `dotnet/BastionVault.IntegrationSdk/BatchTypes.cs:59`*

#### `Path`

The logical path the server echoed for this operation.

*Source: `dotnet/BastionVault.IntegrationSdk/BatchTypes.cs:62`*

#### `Data`

The operation's payload, or `null` when it carried none (a `204`, or a failure).

*Source: `dotnet/BastionVault.IntegrationSdk/BatchTypes.cs:65`*

#### `Errors`

The server's `errors[]` for this operation; empty when it succeeded.

*Source: `dotnet/BastionVault.IntegrationSdk/BatchTypes.cs:68`*

#### `Warnings`

The server's `warnings[]` for this operation; empty when it emitted none.

*Source: `dotnet/BastionVault.IntegrationSdk/BatchTypes.cs:71`*

#### `Error`

BAT-005: the per-operation error, mapped through the same rules as a standalone response
(section 04) from this operation's <see cref="Status"/> and <see cref="Errors"/>.
`null` when <see cref="Status"/> is below 400.

It is a <see cref="BastionVaultException"/> carried as a value rather than thrown: BAT-005
makes a failed operation a result, and a caller that wants to raise it can, while a
caller reading a partially-successful batch is not forced into exception control flow for
the normal case.

*Source: `dotnet/BastionVault.IntegrationSdk/BatchTypes.cs:84`*

