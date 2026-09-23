# `ResourceTypes` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/ResourceTypes.cs`](../../../dotnet/BastionVault.IntegrationSdk/ResourceTypes.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `ConnectAuthorizeRequest`

#### `Resource`

The wire `resource` field. RSC-001: empty raises `BV-INPUT-001` client-side.

*Source: `dotnet/BastionVault.IntegrationSdk/ResourceTypes.cs:82`*

#### `ProfileId`

The wire `profile_id` field.

*Source: `dotnet/BastionVault.IntegrationSdk/ResourceTypes.cs:85`*

#### `ConnectTicket`

The wire `connect_ticket` field; omitted from the body when unset.

*Source: `dotnet/BastionVault.IntegrationSdk/ResourceTypes.cs:88`*

### `ConnectMfaBeginRequest`

#### `Resource`

The wire `resource` field. RSC-001: empty raises `BV-INPUT-001` client-side.

*Source: `dotnet/BastionVault.IntegrationSdk/ResourceTypes.cs:36`*

#### `ProfileId`

The wire `profile_id` field.

*Source: `dotnet/BastionVault.IntegrationSdk/ResourceTypes.cs:39`*

### `ConnectMfaVerifyRequest`

#### `Resource`

The wire `resource` field. RSC-001: empty raises `BV-INPUT-001` client-side.

*Source: `dotnet/BastionVault.IntegrationSdk/ResourceTypes.cs:50`*

#### `ProfileId`

The wire `profile_id` field.

*Source: `dotnet/BastionVault.IntegrationSdk/ResourceTypes.cs:53`*

#### `Method`

The wire `method` field: `totp` or `fido2`.

*Source: `dotnet/BastionVault.IntegrationSdk/ResourceTypes.cs:56`*

#### `TotpCode`

The wire `totp_code` field; omitted from the body when unset.

*Source: `dotnet/BastionVault.IntegrationSdk/ResourceTypes.cs:59`*

#### `Credential`

The wire `credential` field (a FIDO2 assertion); omitted from the body when unset.

*Source: `dotnet/BastionVault.IntegrationSdk/ResourceTypes.cs:62`*

### `ConnectMfaVerifyResult`

#### `ConnectTicket`

The wire `connect_ticket` field, consumed by `ConnectTicket`.

*Source: `dotnet/BastionVault.IntegrationSdk/ResourceTypes.cs:72`*

### `ResourceSearchQuery`

#### `Q`

The wire `q` field; omitted from the body when unset.

*Source: `dotnet/BastionVault.IntegrationSdk/ResourceTypes.cs:7`*

#### `Type`

The wire `type` field; omitted from the body when unset.

*Source: `dotnet/BastionVault.IntegrationSdk/ResourceTypes.cs:10`*

#### `Offset`

The wire `offset` field; omitted from the body when unset.

*Source: `dotnet/BastionVault.IntegrationSdk/ResourceTypes.cs:13`*

#### `Limit`

The wire `limit` field; omitted from the body when unset.

*Source: `dotnet/BastionVault.IntegrationSdk/ResourceTypes.cs:16`*

### `ResourceSecret`

#### `Data`

The server's own field names; each value held redacting, never the server's own field.

*Source: `dotnet/BastionVault.IntegrationSdk/ResourceTypes.cs:29`*

