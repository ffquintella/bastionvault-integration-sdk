# `LoginCredentials` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/LoginCredentials.cs`](../../../dotnet/BastionVault.IntegrationSdk/LoginCredentials.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `LoginCredentials`

#### `Method`

Which flow these credentials are for.

*Source: `dotnet/BastionVault.IntegrationSdk/LoginCredentials.cs:68`*

#### `Mount`

The auth mount path segment (`userpass`, `approle`, or a non-default mount).

*Source: `dotnet/BastionVault.IntegrationSdk/LoginCredentials.cs:71`*

#### `Username`

`Userpass`: the username, URL-path-encoded when sent (AUT-030).

*Source: `dotnet/BastionVault.IntegrationSdk/LoginCredentials.cs:74`*

#### `Password`

`Userpass`: the password, in a redacting type (AUT-031).

*Source: `dotnet/BastionVault.IntegrationSdk/LoginCredentials.cs:77`*

#### `TotpCode`

`Userpass`: the TOTP code, omitted from the body when absent (AUT-030).

*Source: `dotnet/BastionVault.IntegrationSdk/LoginCredentials.cs:80`*

#### `RoleId`

`AppId`: the role id.

*Source: `dotnet/BastionVault.IntegrationSdk/LoginCredentials.cs:83`*

#### `SecretId`

`AppId`: the secret id, in a redacting type (CNF-031).

*Source: `dotnet/BastionVault.IntegrationSdk/LoginCredentials.cs:86`*

#### `MachineToken`

`AppId`: the FerroGate machine token, sent only when supplied (AUT-040).

*Source: `dotnet/BastionVault.IntegrationSdk/LoginCredentials.cs:89`*

#### `ForUserpass(username, password, totpCode, mount)`

Credentials for `POST auth/{mount}/login/{username}` (AUT-030).

*Source: `dotnet/BastionVault.IntegrationSdk/LoginCredentials.cs:93`*

#### `ForAppId(roleId, secretId, machineToken, mount)`

Credentials for `POST auth/{mount}/login` (AUT-040).

*Source: `dotnet/BastionVault.IntegrationSdk/LoginCredentials.cs:107`*

#### `ToString()`

Always redacted (CNF-031, CNF-032). The method and mount are not secret, but they are
withheld anyway: a partially revealing `ToString` invites the next reader to add one
more field to it, and D-M2-3 fixed `[REDACTED]` as the marker in all three languages.

*Source: `dotnet/BastionVault.IntegrationSdk/LoginCredentials.cs:123`*

