# `CertLifecycleTypes` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/CertLifecycleTypes.cs`](../../../dotnet/BastionVault.IntegrationSdk/CertLifecycleTypes.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `SchedulerConfig`

#### `Enabled`

The wire `enabled` field.

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleTypes.cs:78`*

#### `TickIntervalSeconds`

The wire `tick_interval_seconds` field. The server enforces a minimum of 30; no client-side cap is added (D-M1c-25).

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleTypes.cs:81`*

#### `ClientToken`

The wire `client_token` field. Write-only; never returned on read.

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleTypes.cs:84`*

#### `ClientTokenSet`

The wire `client_token_set` field: whether a client token is configured. Populated only on read.

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleTypes.cs:87`*

#### `BaseBackoffSeconds`

The wire `base_backoff_seconds` field.

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleTypes.cs:90`*

#### `MaxBackoffSeconds`

The wire `max_backoff_seconds` field.

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleTypes.cs:93`*

### `Target`

#### `Kind`

The wire `kind` field. Server default `file` when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleTypes.cs:12`*

#### `Address`

The wire `address` field.

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleTypes.cs:15`*

#### `PkiMount`

The wire `pki_mount` field. Server default `pki` when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleTypes.cs:18`*

#### `RoleRef`

The wire `role_ref` field.

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleTypes.cs:21`*

#### `CommonName`

The wire `common_name` field.

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleTypes.cs:24`*

#### `AltNames`

The wire `alt_names` array.

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleTypes.cs:27`*

#### `IpSans`

The wire `ip_sans` array.

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleTypes.cs:30`*

#### `Ttl`

The wire `ttl` field, in seconds.

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleTypes.cs:33`*

#### `KeyPolicy`

The wire `key_policy` field: `rotate`, `reuse`, or `agent-generates`.

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleTypes.cs:36`*

#### `KeyRef`

The wire `key_ref` field.

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleTypes.cs:39`*

#### `RenewBefore`

The wire `renew_before` field, in seconds. Server default 168h (604800s) when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleTypes.cs:42`*

### `TargetState`

#### `CurrentSerial`

The wire `current_serial` field, when a certificate has been issued for this target.

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleTypes.cs:49`*

#### `CurrentNotAfter`

The wire `current_not_after` field.

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleTypes.cs:52`*

#### `LastRenewal`

The wire `last_renewal` field.

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleTypes.cs:55`*

#### `LastAttempt`

The wire `last_attempt` field.

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleTypes.cs:58`*

#### `LastError`

The wire `last_error` field, when the last attempt failed.

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleTypes.cs:61`*

#### `NextAttempt`

The wire `next_attempt` field.

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleTypes.cs:64`*

#### `FailureCount`

The wire `failure_count` field.

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleTypes.cs:67`*

