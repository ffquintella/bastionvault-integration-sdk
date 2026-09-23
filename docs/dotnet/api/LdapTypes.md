# `LdapTypes` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/LdapTypes.cs`](../../../dotnet/BastionVault.IntegrationSdk/LdapTypes.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `LdapCheckConnectionResult`

#### `Ok`

The wire `ok` field: whether the probe succeeded.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:74`*

#### `Stage`

The wire `stage` field: which step the probe reached.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:77`*

#### `Error`

The wire `error` field, present only when <see cref="Ok"/> is `false`.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:80`*

#### `Url`

The wire `url` field: the configured LDAP URL the probe used.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:84`*

#### `BindDn`

The wire `bind_dn` field.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:87`*

#### `Host`

The wire `host` field.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:90`*

#### `Port`

The wire `port` field.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:93`*

#### `Scheme`

The wire `scheme` field.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:96`*

#### `LatencyMs`

The wire `latency_ms` field.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:99`*

### `LdapConfig`

#### `Url`

The wire `url` field.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:17`*

#### `BindDn`

The wire `binddn` field.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:20`*

#### `BindPass`

The wire `bindpass` field. Write-only; never returned on read.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:23`*

#### `UserDn`

The wire `userdn` field.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:26`*

#### `DirectoryType`

The wire `directory_type` field: `openldap` or `active_directory`.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:29`*

#### `PasswordPolicy`

The wire `password_policy` field.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:32`*

#### `RequestTimeout`

The wire `request_timeout` field, in seconds. Server default 10 when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:35`*

#### `StartTls`

The wire `starttls` field.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:38`*

#### `ClientTlsCert`

The wire `client_tls_cert` field.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:41`*

#### `ClientTlsKey`

The wire `client_tls_key` field. Write-only; never returned on read.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:44`*

#### `TlsMinVersion`

The wire `tls_min_version` field: `tls12` or `tls13`.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:47`*

#### `InsecureTls`

The wire `insecure_tls` field. LDP-001: when `true`,
<see cref="AcknowledgeInsecureTls"/> must also be `true`, or
`WriteConfigAsync` refuses the write client-side
(`BV-INPUT-001`) before any request is sent, mirroring the server's own check.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:55`*

#### `AcknowledgeInsecureTls`

The wire `acknowledge_insecure_tls` field. See <see cref="InsecureTls"/> (LDP-001).

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:58`*

#### `UserAttr`

The wire `userattr` field. Server default `cn` when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:61`*

### `LdapLibraryCheckOut`

#### `ServiceAccountName`

The wire `service_account_name` field: the account checked out.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:173`*

#### `Password`

The wire `password` field, redacted.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:176`*

#### `LeaseId`

The wire `lease_id` field.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:179`*

#### `TtlSecs`

The wire `ttl_secs` field.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:182`*

### `LdapLibrarySet`

#### `ServiceAccountNames`

The wire `service_account_names` array.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:151`*

#### `Ttl`

The wire `ttl` field, in seconds. Server default 3600 when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:154`*

#### `MaxTtl`

The wire `max_ttl` field, in seconds. Server default 86400 when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:157`*

#### `DisableCheckInEnforcement`

The wire `disable_check_in_enforcement` field.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:160`*

#### `AffinityTtl`

The wire `affinity_ttl` field, in seconds.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:163`*

### `LdapLibraryStatus`

#### `CheckedOut`

The wire `checked_out` object, keyed by service account name.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:193`*

#### `Available`

The wire `available` array: service account names not currently checked out.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:196`*

### `LdapStaticCred`

#### `Username`

The wire `username` field.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:129`*

#### `Dn`

The wire `dn` field.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:132`*

#### `Password`

The wire `password` field, redacted.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:135`*

#### `LastRotated`

The wire `last_rotated` field, when the role has rotated at least once.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:138`*

#### `TtlSecs`

The wire `ttl_secs` field.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:141`*

### `LdapStaticRole`

#### `Dn`

The wire `dn` field.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:109`*

#### `Username`

The wire `username` field.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:112`*

#### `RotationPeriod`

The wire `rotation_period` field, in seconds.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:115`*

#### `PasswordPolicy`

The wire `password_policy` field.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapTypes.cs:118`*

