# `SshTypes` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/SshTypes.cs`](../../../dotnet/BastionVault.IntegrationSdk/SshTypes.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `SignedSshCertificate`

#### `SignedKey`

The signed OpenSSH certificate line, verbatim (SSH-002).

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:130`*

#### `SerialNumber`

The certificate's serial number, exactly as the server returned it. Bound as a string
rather than a numeric type: 10 states no type for this field, and the PKI serial-number
precedent (`SerialNumber`) is to forward the server's bytes
rather than assume a numeric range that later overflows or loses precision (D-M1c-25).

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:138`*

#### `Algorithm`

The wire `algorithm` field, when the server returned one.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:141`*

### `SshBrokerAssetGroupPolicy`

#### `LoginClass`

The wire `login_class` field.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:223`*

#### `Priority`

The wire `priority` field, resolving most-restrictive-wins across tiers.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:226`*

#### `Lock`

The wire `lock` field.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:229`*

### `SshBrokerEffectivePolicy`

#### `LoginClass`

The wire `login_class` field: the resolved `shared-credential` or `brokered` class.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:249`*

#### `Source`

The wire `source` field: which tier the resolution came from.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:252`*

### `SshBrokerGlobalPolicy`

#### `LoginClassDefault`

The wire `login_class_default` field: `"shared-credential"` or `"brokered"`.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:197`*

#### `LoginClassLock`

The wire `login_class_lock` field.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:200`*

### `SshBrokerResourcePolicy`

#### `LoginClass`

The wire `login_class` field.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:239`*

### `SshBrokerTypePolicy`

#### `LoginClass`

The wire `login_class` field.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:210`*

#### `Lock`

The wire `lock` field.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:213`*

### `SshCaKey`

#### `PublicKey`

The wire `public_key` field, authorized-keys form, verbatim.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:15`*

#### `Algorithm`

The wire `algorithm` field. PQC-gated: `"" | "ed25519" | "mldsa65"`.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:18`*

### `SshCredentials`

#### `Key`

The generated one-time password, redacted (D-M9-3).

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:151`*

#### `KeyType`

The wire `key_type` field, always `"otp"` for this route.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:154`*

#### `Username`

The wire `username` field.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:157`*

#### `Ip`

The wire `ip` field.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:160`*

#### `Port`

The wire `port` field.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:163`*

#### `Ttl`

The wire `ttl` field, in seconds.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:166`*

### `SshOtpVerification`

#### `Username`

The wire `username` field.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:176`*

#### `Ip`

The wire `ip` field.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:179`*

#### `RoleName`

The wire `role_name` field.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:182`*

#### `Port`

The wire `port` field.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:185`*

### `SshRole`

#### `KeyType`

The wire `key_type` field: `"ca"` or `"otp"`.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:44`*

#### `AlgorithmSigner`

The wire `algorithm_signer` field, e.g. `"ssh-ed25519"`.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:47`*

#### `CertType`

The wire `cert_type` field: `"user"` or `"host"`.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:50`*

#### `AllowedUsers`

The wire's CSV-typed `allowed_users`.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:53`*

#### `DefaultUser`

The wire `default_user` field.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:56`*

#### `AllowedExtensions`

The wire's CSV-typed `allowed_extensions`.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:59`*

#### `DefaultExtensions`

The wire's map-typed `default_extensions`.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:62`*

#### `AllowedCriticalOptions`

The wire's CSV-typed `allowed_critical_options`.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:65`*

#### `DefaultCriticalOptions`

The wire's map-typed `default_critical_options`.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:68`*

#### `Ttl`

The wire `ttl` field, in seconds (TRN-031: unquoted, integer seconds).

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:71`*

#### `MaxTtl`

The wire `max_ttl` field, in seconds.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:74`*

#### `NotBeforeDuration`

The wire `not_before_duration` field, in seconds.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:77`*

#### `KeyIdFormat`

The wire `key_id_format` template string.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:80`*

#### `CidrList`

The wire's CSV-typed `cidr_list`.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:83`*

#### `ExcludeCidrList`

The wire's CSV-typed `exclude_cidr_list`.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:86`*

#### `Port`

The wire `port` field. Server default 22 when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:89`*

#### `PqcOnly`

The wire `pqc_only` field. Server default `false` when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:92`*

### `SshSignRequest`

#### `PublicKey`

SSH-001: the wire `public_key` field. Required; refused client-side when empty or whitespace-only.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:101`*

#### `ValidPrincipals`

The wire's CSV-typed `valid_principals` (10-ssh-engine.md:38).

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:104`*

#### `Ttl`

The wire `ttl` field, in seconds.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:107`*

#### `CertType`

The wire `cert_type` field: `"user"` or `"host"`.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:110`*

#### `KeyId`

The wire `key_id` field.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:113`*

#### `Extensions`

The wire's map-typed `extensions` override.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:116`*

#### `CriticalOptions`

The wire's map-typed `critical_options` override.

*Source: `dotnet/BastionVault.IntegrationSdk/SshTypes.cs:119`*

