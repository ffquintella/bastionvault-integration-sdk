# `RequestOptions` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/RequestOptions.cs`](../../../dotnet/BastionVault.IntegrationSdk/RequestOptions.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `RequestOptions`

#### `Namespace`

Override the client namespace for this call only.

*Source: `dotnet/BastionVault.IntegrationSdk/RequestOptions.cs:12`*

#### `Headers`

Additional headers for this call (same reserved-header rule as `Headers`, CFG-017).

*Source: `dotnet/BastionVault.IntegrationSdk/RequestOptions.cs:15`*

#### `Timeout`

Override the client's `Timeout` (per attempt) for this call.

*Source: `dotnet/BastionVault.IntegrationSdk/RequestOptions.cs:18`*

#### `Idempotent`

Tri-state (D-M1b-6): unset defers to the idempotency table; when set, wins in both
directions — a caller may mark a write retryable and may mark a read not-retryable.

*Source: `dotnet/BastionVault.IntegrationSdk/RequestOptions.cs:24`*

#### `WrapTtl`

Request response wrapping (`X-Vault-Wrap-TTL`). Unimplemented by the server; raises `BV-INPUT-006` (TRN-017).

*Source: `dotnet/BastionVault.IntegrationSdk/RequestOptions.cs:27`*

#### `Token`

Use a different token for this call only (e.g. a machine token).

*Source: `dotnet/BastionVault.IntegrationSdk/RequestOptions.cs:30`*

#### `ApiVersion`

Override `ApiPrefix` (`v1`/`v2`) for this call only (TRN-002, D-M1b-13).

*Source: `dotnet/BastionVault.IntegrationSdk/RequestOptions.cs:33`*

#### `TotalTimeout`

Bounds attempts <em>and</em> backoff together, where <see cref="Timeout"/> bounds each attempt (RES-004, D-M1b-13).

*Source: `dotnet/BastionVault.IntegrationSdk/RequestOptions.cs:36`*

#### `CancellationToken`

Runtime cancellation primitive for this call.

*Source: `dotnet/BastionVault.IntegrationSdk/RequestOptions.cs:39`*

