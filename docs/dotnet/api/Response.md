# `Response` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/Response.cs`](../../../dotnet/BastionVault.IntegrationSdk/Response.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `AuthInfo`

#### `ClientToken`

The issued token. Held as a <see cref="SecretString"/> so it is never logged by accident.

*Source: `dotnet/BastionVault.IntegrationSdk/Response.cs:59`*

#### `Policies`

Policies attached to the token.

*Source: `dotnet/BastionVault.IntegrationSdk/Response.cs:62`*

#### `Metadata`

Free-form metadata the server attached to the token.

*Source: `dotnet/BastionVault.IntegrationSdk/Response.cs:65`*

#### `LeaseDuration`

Seconds on the wire.

*Source: `dotnet/BastionVault.IntegrationSdk/Response.cs:68`*

#### `Renewable`

Whether the token can be renewed.

*Source: `dotnet/BastionVault.IntegrationSdk/Response.cs:71`*

#### `IssuedAt`

AUT-013: when the SDK received this credential, read from the injected clock (D-M1b-7) and
never from the local wall clock, so AUT-090's renewal schedule and AUT-003's
`MinReloginInterval` are both drivable deterministically in tests.

The wire carries no issue time — only `lease_duration` — so this is the SDK's own
observation of "now", which is exactly what AUT-090's
`IssuedAt + LeaseDuration × RenewAtFraction` needs.

*Source: `dotnet/BastionVault.IntegrationSdk/Response.cs:83`*

#### `EnvironmentScope`

AUT-044: the environment scope derived from <see cref="Metadata"/>'s `approle_env_*`
keys. Computed rather than stored, so it can never disagree with the metadata it comes from.

*Source: `dotnet/BastionVault.IntegrationSdk/Response.cs:89`*

### `RawResponse`

#### `StatusCode`

The HTTP status of the attempt.

*Source: `dotnet/BastionVault.IntegrationSdk/Response.cs:96`*

#### `Headers`

Response headers.

*Source: `dotnet/BastionVault.IntegrationSdk/Response.cs:99`*

#### `Body`

The unparsed response body.

*Source: `dotnet/BastionVault.IntegrationSdk/Response.cs:102`*

### `Response`

#### `Data`

Shape A: the wire envelope's `data` object. Shape B: the whole parsed body. Absent when
neither is present (e.g. a login response with an empty `data` object still yields an
empty, non-null map).

*Source: `dotnet/BastionVault.IntegrationSdk/Response.cs:17`*

#### `Auth`

Present on login / token-create responses (TRN-040).

*Source: `dotnet/BastionVault.IntegrationSdk/Response.cs:20`*

#### `LeaseId`

Absent when the wire value is `""` (TRN-041).

*Source: `dotnet/BastionVault.IntegrationSdk/Response.cs:23`*

#### `Renewable`

Absent when not present on the wire (TRN-042).

*Source: `dotnet/BastionVault.IntegrationSdk/Response.cs:26`*

#### `LeaseDuration`

Seconds on the wire; absent when not present (TRN-042).

*Source: `dotnet/BastionVault.IntegrationSdk/Response.cs:29`*

#### `Warnings`

Always empty against current servers (ERR-050); never fabricated.

*Source: `dotnet/BastionVault.IntegrationSdk/Response.cs:32`*

#### `StatusCode`

The HTTP status of the attempt that produced this response.

*Source: `dotnet/BastionVault.IntegrationSdk/Response.cs:35`*

#### `Headers`

Response headers (`ETag`, `Retry-After`, ...).

*Source: `dotnet/BastionVault.IntegrationSdk/Response.cs:38`*

#### `Raw`

The exact parsed body, for diagnostics and forward-compatible field access (TRN-043).

*Source: `dotnet/BastionVault.IntegrationSdk/Response.cs:41`*

