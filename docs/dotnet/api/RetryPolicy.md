# `RetryPolicy` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/RetryPolicy.cs`](../../../dotnet/BastionVault.IntegrationSdk/RetryPolicy.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `RetryPolicy`

#### `MaxAttempts`

Total attempts including the first. Default 3.

*Source: `dotnet/BastionVault.IntegrationSdk/RetryPolicy.cs:10`*

#### `InitialBackoff`

Default 250ms.

*Source: `dotnet/BastionVault.IntegrationSdk/RetryPolicy.cs:13`*

#### `MaxBackoff`

Default 5s.

*Source: `dotnet/BastionVault.IntegrationSdk/RetryPolicy.cs:16`*

#### `BackoffMultiplier`

Default 2.0.

*Source: `dotnet/BastionVault.IntegrationSdk/RetryPolicy.cs:19`*

#### `Jitter`

Default ±20% (0.2).

*Source: `dotnet/BastionVault.IntegrationSdk/RetryPolicy.cs:22`*

#### `RetryOn`

The mapped error codes eligible for automatic retry (D-M1b-7). Default
`[BV-TRANSPORT-001, BV-TRANSPORT-002, BV-SERVER-002, BV-SERVER-003]`. `BV-SERVER-001`
and `BV-RATE-001` are never retried even if present here (CFG-052, CFG-053).

*Source: `dotnet/BastionVault.IntegrationSdk/RetryPolicy.cs:29`*

#### `RespectRetryAfter`

Default true.

*Source: `dotnet/BastionVault.IntegrationSdk/RetryPolicy.cs:32`*

#### `RetryIdempotentOnly`

Default true — only idempotent operations are retried by default (CFG-051).

*Source: `dotnet/BastionVault.IntegrationSdk/RetryPolicy.cs:35`*

