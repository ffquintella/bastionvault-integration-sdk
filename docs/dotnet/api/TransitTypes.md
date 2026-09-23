# `TransitTypes` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/TransitTypes.cs`](../../../dotnet/BastionVault.IntegrationSdk/TransitTypes.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `TransitDataKeyResult`

#### `Ciphertext`

The wrapped (encrypted) datakey.

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:195`*

#### `KeyVersion`

The key version the operation used.

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:198`*

#### `Plaintext`

TRS-013: the raw datakey, present only when `Plaintext` was
requested, held in a redacting type so it never reaches a log by accident.

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:204`*

### `TransitEncryptResult`

#### `Ciphertext`

The `bvault:`-framed ciphertext (see `ParseCiphertext`).

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:155`*

#### `KeyVersion`

The key version the operation used.

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:158`*

### `TransitHmacResult`

#### `Hmac`

The `bvault:`-framed HMAC.

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:175`*

#### `KeyVersion`

The key version the operation used.

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:178`*

### `TransitKey`

#### `Name`

The key's name.

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:107`*

#### `Type`

One of <see cref="TransitKeyTypes"/>, or any other string the server reports (TRS-001).

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:110`*

#### `LatestVersion`

The newest key version.

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:113`*

#### `MinDecryptionVersion`

Versions older than this cannot decrypt (`BV-TRANSIT-004`).

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:116`*

#### `MinAvailableVersion`

Versions older than this cannot rewrap.

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:119`*

#### `DeletionAllowed`

Whether `Transit.DeleteKey` is currently allowed.

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:122`*

#### `Exportable`

Whether the raw key material may be exported.

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:125`*

#### `Derived`

Whether this key is derived per `Context`.

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:128`*

#### `ConvergentEncryption`

Whether convergent encryption is enabled.

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:131`*

#### `Keys`

TRS-010: every retained version's metadata, normalised from the wire's
`{version: creation_time}` (symmetric) or `{version: {public_key, creation_time}}`
(asymmetric) shape into one map either way.

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:138`*

### `TransitKeyConfig`

#### `MinDecryptionVersion`

The new minimum decryptable version, or `null` to leave it unchanged.

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:84`*

#### `MinAvailableVersion`

The new minimum available (rewrap) version, or `null` to leave it unchanged.

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:87`*

#### `DeletionAllowed`

The new deletion-allowed flag, or `null` to leave it unchanged.

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:90`*

### `TransitKeyOptions`

#### `KeyType`

One of <see cref="TransitKeyTypes"/>, or any other string the server accepts (TRS-001). Server-defaulted to `ChaCha20Poly1305` when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:53`*

#### `Exportable`

Whether the raw key material may ever be exported.

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:56`*

#### `DeletionAllowed`

Whether `Transit.DeleteKey` is allowed for this key without first flipping it here.

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:59`*

#### `Derived`

Whether this key is derived per `Context`. TRS-013: `EncryptAsync`
accepts `context` only for a key created with this set.

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:65`*

#### `ConvergentEncryption`

TRS-013: convergent encryption (the same plaintext and context always produce the same
ciphertext). Requires <see cref="Derived"/> to also be set; the server rejects the
combination otherwise (08 §Server error strings, `BV-INPUT-100`).

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:72`*

### `TransitKeyVersionInfo`

#### `CreationTime`

When this version was created.

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:97`*

#### `PublicKey`

The public key material, present only for an asymmetric (signature or KEM) key type.

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:100`*

### `TransitParsedCiphertext`

#### `Version`

The framing version (the `v&lt;N&gt;` segment).

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:211`*

#### `Algo`

The post-quantum algorithm tag, present only on a `pqc`-framed value.

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:214`*

#### `Bytes`

The decoded payload bytes.

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:217`*

### `TransitSignResult`

#### `Signature`

The `bvault:`-framed signature.

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:165`*

#### `KeyVersion`

The key version the operation used.

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:168`*

### `TransitTrimResult`

#### `Key`

The key's metadata after trimming.

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:145`*

#### `DroppedVersions`

The version numbers the trim removed.

*Source: `dotnet/BastionVault.IntegrationSdk/TransitTypes.cs:148`*

