# `LogicalOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/LogicalOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/LogicalOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `LogicalOperations`

#### `ReadAsync(path, options, cancellationToken)`

`GET path`. Returns `null` on a `404` with an empty body (TRN-050).

Conformance: Core — this primitive underlies every typed operation (TRN-001). No error codes beyond the common set (ERR-061); the common set is documented once in <see cref="LogicalOperations"/>'s type-level remarks.

**Spec:** `Logical.Read — TRN-001`

*Source: `dotnet/BastionVault.IntegrationSdk/LogicalOperations.cs:30`*

#### `WriteAsync(path, body, options, cancellationToken)`

`POST path` (server also accepts `PUT`).

`path` is the wire-relative path; `body` is sent verbatim, `null` for no body. Returns the parsed <see cref="Response"/>, or `null` for a 204 or an empty body. Conformance: Core (TRN-001). No error codes beyond the common set (ERR-061).

**Spec:** `Logical.Write — TRN-001`

*Source: `dotnet/BastionVault.IntegrationSdk/LogicalOperations.cs:107`*

#### `DeleteAsync(path, body, options, cancellationToken)`

`DELETE path` with an optional JSON body (KV v2 `versions`).

`path` is the wire-relative path; `body` is sent verbatim, `null` for no body. Returns the parsed <see cref="Response"/>, or `null` for a 204 or an empty body. Conformance: Core (TRN-001). No error codes beyond the common set (ERR-061).

**Spec:** `Logical.Delete — TRN-001`

*Source: `dotnet/BastionVault.IntegrationSdk/LogicalOperations.cs:118`*

#### `ListAsync(path, options, cancellationToken)`

The literal `LIST` verb (TRN-010). Returns `null` on a `404` with an empty body.

Conformance: Core (TRN-010). No error codes beyond the common set (ERR-061).

**Spec:** `Logical.List — TRN-010`

*Source: `dotnet/BastionVault.IntegrationSdk/LogicalOperations.cs:133`*

#### `RawAsync(method, absolutePath, body, options, cancellationToken)`

The escape hatch (D-M1b-12): `absolutePath` starts with `/` and no
prefix is added; the body is returned unparsed. Errors still map through the same status→code
function as every other operation.

`method` is the literal HTTP verb; `absolutePath` starts with `/`, sent as-is; `body` is sent verbatim. Returns the unparsed <see cref="RawResponse"/>, never `null`. Conformance: Core (D-M1b-12). No error codes beyond the common set (ERR-061).

**Spec:** `Logical.Raw — TRN-001`

*Source: `dotnet/BastionVault.IntegrationSdk/LogicalOperations.cs:147`*

