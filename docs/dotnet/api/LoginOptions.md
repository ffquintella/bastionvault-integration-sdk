# `LoginOptions` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/LoginOptions.cs`](../../../dotnet/BastionVault.IntegrationSdk/LoginOptions.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `LoginOptions`

#### `ReloginOnPermissionDenied`

AUT-003's opt-in. When `true`, a `BV-AUTHZ-001` on an idempotent
request whose token is older than <see cref="MinReloginInterval"/> causes one re-login and
one replay. Default `false`.

*Source: `dotnet/BastionVault.IntegrationSdk/LoginOptions.cs:29`*

#### `MinReloginInterval`

AUT-003's floor: a token younger than this is never re-logged-in, so a genuine policy denial
cannot become a login loop. Default 30 seconds.

*Source: `dotnet/BastionVault.IntegrationSdk/LoginOptions.cs:35`*

