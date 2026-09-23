# `Identity` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/Identity.cs`](../../../dotnet/BastionVault.IntegrationSdk/Identity.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `DefaultAccount`

#### `Username`

The wire `username` field.

*Source: `dotnet/BastionVault.IntegrationSdk/Identity.cs:42`*

#### `Domain`

The wire `domain` field.

*Source: `dotnet/BastionVault.IntegrationSdk/Identity.cs:45`*

#### `WindowsPassword`

The wire `windows_password` field, wrapped so it never reaches a log through
`ToString()` (CFG-080, DR-0003's <see cref="SecretString"/>).

⚠️ SYS-080: the server returns this only on a GET by the record's own owner. It is
therefore `null` on every admin read and on every write response, and an
SDK that defaulted it to an empty string would make "the server withheld it" look like
"the account has no password".

*Source: `dotnet/BastionVault.IntegrationSdk/Identity.cs:57`*

#### `Raw`

The whole record as sent.

*Source: `dotnet/BastionVault.IntegrationSdk/Identity.cs:60`*

### `DefaultAccountSpec`

#### `Username`

The wire `username` field; omitted from the body when unset.

*Source: `dotnet/BastionVault.IntegrationSdk/Identity.cs:67`*

#### `Domain`

The wire `domain` field; omitted from the body when unset.

*Source: `dotnet/BastionVault.IntegrationSdk/Identity.cs:70`*

#### `WindowsPassword`

The wire `windows_password` field; omitted from the body when unset.

*Source: `dotnet/BastionVault.IntegrationSdk/Identity.cs:73`*

### `IdentityProfile`

#### `Username`

The wire `username` field.

*Source: `dotnet/BastionVault.IntegrationSdk/Identity.cs:18`*

#### `DisplayName`

The wire `display_name` field.

*Source: `dotnet/BastionVault.IntegrationSdk/Identity.cs:21`*

#### `Email`

The wire `email` field. `""` is a cleared contact, not an absent one.

*Source: `dotnet/BastionVault.IntegrationSdk/Identity.cs:24`*

#### `Phone`

The wire `phone` field. `""` is a cleared contact, not an absent one.

*Source: `dotnet/BastionVault.IntegrationSdk/Identity.cs:27`*

#### `Mount`

The wire `mount` field: the auth mount the identity belongs to.

*Source: `dotnet/BastionVault.IntegrationSdk/Identity.cs:30`*

#### `Raw`

The whole profile object as sent, so a field this type does not name is still reachable.

*Source: `dotnet/BastionVault.IntegrationSdk/Identity.cs:33`*

### `NamespaceAssignment`

#### `Namespaces`

The wire `namespaces` array; empty when the server omits it.

*Source: `dotnet/BastionVault.IntegrationSdk/Identity.cs:109`*

#### `DefaultNamespace`

The wire `default_namespace` field.

*Source: `dotnet/BastionVault.IntegrationSdk/Identity.cs:112`*

#### `Raw`

The whole record as sent.

*Source: `dotnet/BastionVault.IntegrationSdk/Identity.cs:115`*

### `SshSecurityKey`

#### `Name`

The wire `name` field.

*Source: `dotnet/BastionVault.IntegrationSdk/Identity.cs:80`*

#### `PublicKey`

The wire `public_key` field.

*Source: `dotnet/BastionVault.IntegrationSdk/Identity.cs:83`*

#### `Fingerprint`

The wire `fingerprint` field.

*Source: `dotnet/BastionVault.IntegrationSdk/Identity.cs:86`*

#### `Raw`

The whole record as sent.

*Source: `dotnet/BastionVault.IntegrationSdk/Identity.cs:89`*

### `SshSecurityKeySpec`

#### `Name`

The wire `name` field; omitted when unset.

*Source: `dotnet/BastionVault.IntegrationSdk/Identity.cs:96`*

#### `PublicKey`

The wire `public_key` field; omitted when unset.

*Source: `dotnet/BastionVault.IntegrationSdk/Identity.cs:99`*

