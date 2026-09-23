# `TotpTypes` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/TotpTypes.cs`](../../../dotnet/BastionVault.IntegrationSdk/TotpTypes.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `TotpKey`

#### `Generate`

Whether this key is generate-mode.

*Source: `dotnet/BastionVault.IntegrationSdk/TotpTypes.cs:117`*

#### `Issuer`

The configured issuer, or `null` when none was set.

*Source: `dotnet/BastionVault.IntegrationSdk/TotpTypes.cs:120`*

#### `AccountName`

The configured account name, or `null` when none was set.

*Source: `dotnet/BastionVault.IntegrationSdk/TotpTypes.cs:123`*

#### `Algorithm`

The wire's algorithm string, verbatim. A plain `string` rather than
<see cref="TotpAlgorithm"/> (D-M4-5): this is the response side, and a server value outside
the three the client validates on write is a fact to report, not a guess to make (D-M1c-25).

*Source: `dotnet/BastionVault.IntegrationSdk/TotpTypes.cs:130`*

#### `Digits`

The configured code length.

*Source: `dotnet/BastionVault.IntegrationSdk/TotpTypes.cs:133`*

#### `Period`

The configured period, in seconds.

*Source: `dotnet/BastionVault.IntegrationSdk/TotpTypes.cs:136`*

#### `Skew`

The configured clock-skew tolerance, in periods.

*Source: `dotnet/BastionVault.IntegrationSdk/TotpTypes.cs:139`*

#### `ReplayCheck`

Whether a validated code is rejected on replay.

*Source: `dotnet/BastionVault.IntegrationSdk/TotpTypes.cs:142`*

### `TotpKeyCreated`

#### `Name`

The key name it was created under.

*Source: `dotnet/BastionVault.IntegrationSdk/TotpTypes.cs:95`*

#### `Generate`

Whether this key is generate-mode.

*Source: `dotnet/BastionVault.IntegrationSdk/TotpTypes.cs:98`*

#### `Key`

The generated seed, present only for an exported generate-mode key (TOT-002).

*Source: `dotnet/BastionVault.IntegrationSdk/TotpTypes.cs:101`*

#### `Url`

The generated `otpauth://` URL, present only for an exported generate-mode key (TOT-002).

*Source: `dotnet/BastionVault.IntegrationSdk/TotpTypes.cs:104`*

#### `Barcode`

The QR barcode PNG bytes, decoded from the wire's base64 (TOT-002); present only for an exported generate-mode key.

*Source: `dotnet/BastionVault.IntegrationSdk/TotpTypes.cs:107`*

### `TotpKeySpec`

#### `Generate`

Generate-mode: the vault generates and owns the seed. Default `false`.

*Source: `dotnet/BastionVault.IntegrationSdk/TotpTypes.cs:32`*

#### `Key`

Provider-mode: the base32 seed the caller supplies. Held as a <see cref="SecretString"/>
(TOT-002) — it is seed material, not a token, but it is exactly as sensitive as one.

*Source: `dotnet/BastionVault.IntegrationSdk/TotpTypes.cs:38`*

#### `Url`

Provider-mode: an `otpauth://` URL carrying the seed (and, optionally, a label that
satisfies TOT-001's <see cref="AccountName"/> requirement in its place). Held as a
<see cref="SecretString"/> (TOT-002).

*Source: `dotnet/BastionVault.IntegrationSdk/TotpTypes.cs:45`*

#### `KeySize`

Generate-mode only: the seed size in bytes. Server default 20 when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/TotpTypes.cs:48`*

#### `Issuer`

The issuer shown in an authenticator app.

*Source: `dotnet/BastionVault.IntegrationSdk/TotpTypes.cs:51`*

#### `AccountName`

Required unless <see cref="Url"/> carries a label (TOT-001).

*Source: `dotnet/BastionVault.IntegrationSdk/TotpTypes.cs:56`*

#### `Algorithm`

TOT-001: one of `Sha1`, `Sha256`, `Sha512`. Server default `SHA1` when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/TotpTypes.cs:59`*

#### `Digits`

TOT-001: 6 or 8 when set. Server default 6 when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/TotpTypes.cs:62`*

#### `Period`

TOT-001: at least 1 second when set. Server default 30 when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/TotpTypes.cs:65`*

#### `Skew`

Generate-mode: the number of periods of clock skew tolerated. Server default 1 when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/TotpTypes.cs:68`*

#### `QrSize`

Generate-mode: the requested QR barcode size in pixels; `0` disables it. Server
default 200 when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/TotpTypes.cs:74`*

#### `Exported`

Generate-mode: whether the response carries `Key`,
`Url` and `Barcode`. Server default
`true` when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/TotpTypes.cs:81`*

#### `ReplayCheck`

Provider-mode: whether a validated code is rejected on replay. Server default `true` when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/TotpTypes.cs:84`*

