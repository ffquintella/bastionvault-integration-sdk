# `RustionTypes` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/RustionTypes.cs`](../../../dotnet/BastionVault.IntegrationSdk/RustionTypes.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `RustionRecordingChunk`

#### `Bytes`

The decoded `bytes_b64` payload.

*Source: `dotnet/BastionVault.IntegrationSdk/RustionTypes.cs:84`*

#### `Eof`

Whether this is the last chunk of the recording.

*Source: `dotnet/BastionVault.IntegrationSdk/RustionTypes.cs:87`*

#### `DigestVerified`

The server's own digest-verification claim, when reported.

*Source: `dotnet/BastionVault.IntegrationSdk/RustionTypes.cs:90`*

#### `Sha256`

The recording's SHA-256, when reported.

*Source: `dotnet/BastionVault.IntegrationSdk/RustionTypes.cs:93`*

### `RustionSessionKillRequest`

#### `BastionId`

The bastion identifier.

*Source: `dotnet/BastionVault.IntegrationSdk/RustionTypes.cs:71`*

#### `SessionId`

The session identifier.

*Source: `dotnet/BastionVault.IntegrationSdk/RustionTypes.cs:74`*

#### `CorrelationId`

The caller-assigned correlation identifier.

*Source: `dotnet/BastionVault.IntegrationSdk/RustionTypes.cs:77`*

### `RustionSessionOpenConnectOnlyRequest`

#### `ResourceName`

The resource being connected to.

*Source: `dotnet/BastionVault.IntegrationSdk/RustionTypes.cs:27`*

#### `SecretId`

The `credential_source.secret_id` field. `kind` is always `secret` and is not settable.

*Source: `dotnet/BastionVault.IntegrationSdk/RustionTypes.cs:30`*

#### `TargetHost`

The bastion-facing target host.

*Source: `dotnet/BastionVault.IntegrationSdk/RustionTypes.cs:33`*

#### `TargetPort`

The bastion-facing target port.

*Source: `dotnet/BastionVault.IntegrationSdk/RustionTypes.cs:36`*

#### `TargetProtocol`

The bastion-facing target protocol.

*Source: `dotnet/BastionVault.IntegrationSdk/RustionTypes.cs:39`*

#### `ProfileId`

Optional connect profile.

*Source: `dotnet/BastionVault.IntegrationSdk/RustionTypes.cs:42`*

#### `ConnectTicket`

Single-use, redacting. Never travels in a query string (R-33).

*Source: `dotnet/BastionVault.IntegrationSdk/RustionTypes.cs:45`*

### `RustionSessionRenewRequest`

#### `BastionId`

The bastion identifier.

*Source: `dotnet/BastionVault.IntegrationSdk/RustionTypes.cs:52`*

#### `SessionId`

The session identifier.

*Source: `dotnet/BastionVault.IntegrationSdk/RustionTypes.cs:55`*

#### `CorrelationId`

The caller-assigned correlation identifier.

*Source: `dotnet/BastionVault.IntegrationSdk/RustionTypes.cs:58`*

#### `ExtendSecs`

Default 1800 seconds.

*Source: `dotnet/BastionVault.IntegrationSdk/RustionTypes.cs:61`*

### `RustionSessionRequest`

#### `CredentialMaterial`

The raw v1 credential material. Never logged, never redacted away from the wire.

*Source: `dotnet/BastionVault.IntegrationSdk/RustionTypes.cs:13`*

#### `Fields`

Every other field, merged into the request body verbatim. Must not carry a `credential_material` key.

*Source: `dotnet/BastionVault.IntegrationSdk/RustionTypes.cs:16`*

