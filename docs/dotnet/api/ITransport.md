# `ITransport` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/ITransport.cs`](../../../dotnet/BastionVault.IntegrationSdk/ITransport.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `TransportRequest`

#### `Timeout`

The per-attempt budget (RES-004); enforcement is the transport's responsibility.

*Source: `dotnet/BastionVault.IntegrationSdk/ITransport.cs:32`*

#### `ConnectTimeout`

The TCP/TLS handshake budget for this attempt.

*Source: `dotnet/BastionVault.IntegrationSdk/ITransport.cs:35`*

#### `MaxResponseBytes`

Responses larger than this MUST be aborted by the transport while reading, before the full
body is buffered (TRN-033, D-M1b-20) — not audited afterwards. Default 128 MiB.

*Source: `dotnet/BastionVault.IntegrationSdk/ITransport.cs:41`*

