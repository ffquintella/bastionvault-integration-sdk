# `TransitOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/TransitOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/TransitOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `TransitByokOperations`

#### `WrappingKeyAsync(mount, options, cancellationToken)`

Reads the BYOK wrapping key so a caller can wrap an external key for import: `GET {mount}/wrapping_key`.

Wire params: `mount` builds the route; no body. Returns the wrapping-key response map, or `null` on a `404` empty body (TRN-050). Conformance: Standard (CNF-043). Errors beyond the common set (ERR-061): `BV-SERVER-004 UnsupportedByServer` when the `transit_byok` feature is absent.

**Spec:** `Transit.Byok.WrappingKey — CNF-043`

*Source: `dotnet/BastionVault.IntegrationSdk/TransitOperations.cs:644`*

#### `ImportKeyAsync(name, body, mount, options, cancellationToken)`

Imports an externally-wrapped key as a new Transit key: `POST {mount}/keys/{name}/import`.

Wire params: `name`/`mount` build the route; `body`'s entries are written as the request body's top-level fields verbatim — section 08 names only the routes, not a typed body (D-M1c-25). Returns the server's response map, or `null` per the shared envelope rules. Conformance: Standard (CNF-043). Errors beyond the common set (ERR-061): `BV-SERVER-004 UnsupportedByServer` when the `transit_byok` feature is absent.

**Spec:** `Transit.Byok.ImportKey — CNF-043`

*Source: `dotnet/BastionVault.IntegrationSdk/TransitOperations.cs:657`*

#### `ImportVersionAsync(name, body, mount, options, cancellationToken)`

Imports a new version of an existing BYOK key: `POST {mount}/keys/{name}/import_version`.

Wire params: as <see cref="ImportKeyAsync"/>, at `{mount}/keys/{name}/import_version`. Returns the server's response map, or `null` per the shared envelope rules. Conformance: Standard (CNF-043). Errors beyond the common set (ERR-061): `BV-SERVER-004 UnsupportedByServer` when the `transit_byok` feature is absent.

**Spec:** `Transit.Byok.ImportVersion — CNF-043`

*Source: `dotnet/BastionVault.IntegrationSdk/TransitOperations.cs:675`*

### `TransitOperations`

#### `Byok`

The feature-gated BYOK surface (08 §Operations' `Transit.Byok.*` row).

*Source: `dotnet/BastionVault.IntegrationSdk/TransitOperations.cs:36`*

#### `ParseCiphertext(ciphertext)`

TRS-002: parses a `bvault:`-framed value without making a request.

HTTP call: none — a client-side parser. Wire params: none; `ciphertext`
is parsed locally. Returns a <see cref="TransitParsedCiphertext"/>, never
`null`; throws `BV-INPUT-011` for a malformed prefix or version.
Conformance: Standard (TRS-002). Error codes beyond the common set (ERR-061):
`BV-INPUT-011 InvalidCiphertextFormat`.

**Spec:** `Transit.ParseCiphertext — TRS-002`

*Source: `dotnet/BastionVault.IntegrationSdk/TransitOperations.cs:47`*

#### `ListKeysAsync(mount, options, cancellationToken)`

Lists the key names under `mount`: `LIST {mount}/keys/`.

Wire params: `mount` builds the route; no query or body params. Returns an
empty list when the backend has none (TRN-050), never `null`.
Conformance: Standard (TRN-050). No error codes beyond the common set (ERR-061).

**Spec:** `Transit.ListKeys — TRN-050`

*Source: `dotnet/BastionVault.IntegrationSdk/TransitOperations.cs:62`*

#### `CreateKeyAsync(name, keyOptions, mount, options, cancellationToken)`

Creates a named Transit key: `POST {mount}/keys/{name}`.

Wire params: `name`/`mount` build the route; body carries `key_type`, `exportable`, `deletion_allowed`, `derived`, `convergent_encryption`, each omitted when unset.
Returns the created <see cref="TransitKey"/>, never `null`, with per-version metadata normalised per TRS-010.
Conformance: Standard (TRS-010). Errors beyond the common set (ERR-061): `BV-TRANSIT-002 KeyTypeConflict`, `BV-INPUT-100` (derived / convergent_encryption validation).

**Spec:** `Transit.CreateKey — TRS-010`

*Source: `dotnet/BastionVault.IntegrationSdk/TransitOperations.cs:79`*

#### `ReadKeyAsync(name, mount, options, cancellationToken)`

Reads a Transit key's metadata: `GET {mount}/keys/{name}`.

Wire params: `name`/`mount` build the route; no body. A missing key is `null` (TRN-050), never an exception.
Conformance: Standard (TRN-050). No error codes beyond the common set (ERR-061).

**Spec:** `Transit.ReadKey — TRN-050`

*Source: `dotnet/BastionVault.IntegrationSdk/TransitOperations.cs:131`*

#### `DeleteKeyAsync(name, mount, options, cancellationToken)`

Deletes a Transit key: `DELETE {mount}/keys/{name}` → `204`.

Wire params: `name`/`mount` build the route; no body. Returns `void` on the server's `204`.
Conformance: Standard (every typed operation is built on `Logical.Delete` per TRN-001, but that primitive-exposure MUST is TRN-001's own, not this operation's; 08 states no delete-specific behaviour beyond the route). Errors beyond the common set (ERR-061): `BV-TRANSIT-001 KeyNotFound`, `BV-TRANSIT-003 DeletionNotAllowed`.

**Spec:** `Transit.DeleteKey — 08-transit-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/TransitOperations.cs:149`*

#### `RotateKeyAsync(name, mount, options, cancellationToken)`

Rotates a Transit key to a new version: `POST {mount}/keys/{name}/rotate`.

Wire params: `name`/`mount` build the route; no body. Returns the rotated <see cref="TransitKey"/>, never `null`, normalised per TRS-010.
Conformance: Standard (TRS-010). Errors beyond the common set (ERR-061): `BV-TRANSIT-001 KeyNotFound`.

**Spec:** `Transit.RotateKey — TRS-010`

*Source: `dotnet/BastionVault.IntegrationSdk/TransitOperations.cs:165`*

#### `ConfigureKeyAsync(name, config, mount, options, cancellationToken)`

Reconfigures a Transit key's version bounds and deletability: `POST {mount}/keys/{name}/config`.

Wire params: `name`/`mount` build the route; body carries `min_decryption_version`, `min_available_version`, `deletion_allowed`, each omitted when unset (`0` = unchanged per 08).
Returns the updated <see cref="TransitKey"/>, never `null`. Conformance: Standard (TRS-010). Errors beyond the common set (ERR-061): `BV-TRANSIT-001 KeyNotFound`, `BV-INPUT-100` (bounds violation).

**Spec:** `Transit.ConfigureKey — TRS-010`

*Source: `dotnet/BastionVault.IntegrationSdk/TransitOperations.cs:183`*

#### `TrimKeyAsync(name, mount, options, cancellationToken)`

Discards old key versions below `min_available_version`: `POST {mount}/keys/{name}/trim`.

Wire params: `name`/`mount` build the route; no body. Returns the trimmed <see cref="TransitTrimResult"/> (key metadata plus `dropped_versions`), never `null`.
Conformance: Standard (TRS-010). Errors beyond the common set (ERR-061): `BV-TRANSIT-001 KeyNotFound`, `BV-INPUT-100` (would leave the key with no versions).

**Spec:** `Transit.TrimKey — TRS-010`

*Source: `dotnet/BastionVault.IntegrationSdk/TransitOperations.cs:221`*

#### `EncryptAsync(name, plaintext, plaintextBase64, context, contextBase64, mount, options, cancellationToken)`

`POST {mount}/encrypt/{name}`. TRS-013: `context`/`contextBase64`
is accepted only for a key created with `Derived = true` — the server rejects it
otherwise (08 §Server error strings, `BV-INPUT-100`); convergent encryption further
requires `Derived`, which this method does not itself check (D-M1c-25: the server
already names the rejection, so the SDK does not guess it client-side).

Wire params: `name`/`mount` build the route; body carries base64 `plaintext` (required) and `context` (optional). Returns <see cref="TransitEncryptResult"/>, never `null`. Conformance: Standard (TRS-013). Errors beyond the common set (ERR-061): `BV-TRANSIT-001 KeyNotFound`, `BV-TRANSIT-005 OperationNotSupportedByKeyType`, `BV-INPUT-012 NotBase64`.

**Spec:** `Transit.Encrypt — TRS-013`

*Source: `dotnet/BastionVault.IntegrationSdk/TransitOperations.cs:249`*

#### `DecryptAsync(name, ciphertext, context, contextBase64, mount, options, cancellationToken)`

`POST {mount}/decrypt/{name}`. TRS-002: `ciphertext` is validated for
the `bvault:` prefix before the request is sent (`BV-INPUT-011`). TRS-013: the
plaintext is returned in <see cref="SecretBytes"/> so it never reaches a log by accident.

Wire params: `name`/`mount` build the route; body carries `ciphertext` and optional base64 `context`. Returns the plaintext as <see cref="SecretBytes"/>, never `null`. Conformance: Standard (TRS-002). Errors beyond the common set (ERR-061): `BV-INPUT-011 InvalidCiphertextFormat`, `BV-TRANSIT-001 KeyNotFound`, `BV-TRANSIT-004 VersionBelowMinDecryption`, `BV-TRANSIT-005 OperationNotSupportedByKeyType`, `BV-INPUT-012 NotBase64`.

**Spec:** `Transit.Decrypt — TRS-002`

*Source: `dotnet/BastionVault.IntegrationSdk/TransitOperations.cs:291`*

#### `RewrapAsync(name, ciphertext, context, contextBase64, mount, options, cancellationToken)`

`POST {mount}/rewrap/{name}`. TRS-002: the same client-side `bvault:` validation as <see cref="DecryptAsync"/>.

Wire params: `name`/`mount` build the route; body carries `ciphertext` and optional base64 `context`. Returns <see cref="TransitEncryptResult"/> re-wrapped under the latest key version, never `null`. Conformance: Standard (TRS-002). Errors beyond the common set (ERR-061): `BV-INPUT-011 InvalidCiphertextFormat`, `BV-TRANSIT-001 KeyNotFound`, `BV-TRANSIT-004 VersionBelowMinDecryption`, `BV-INPUT-012 NotBase64`.

**Spec:** `Transit.Rewrap — TRS-002`

*Source: `dotnet/BastionVault.IntegrationSdk/TransitOperations.cs:325`*

#### `SignAsync(name, input, inputBase64, mount, options, cancellationToken)`

Signs `input` with a signature-type Transit key: `POST {mount}/sign/{name}`.

Wire params: `name`/`mount` build the route; body carries base64 `input` (required). Returns <see cref="TransitSignResult"/>, never `null`. Conformance: Standard (TRS-003). Errors beyond the common set (ERR-061): `BV-TRANSIT-001 KeyNotFound`, `BV-TRANSIT-005 OperationNotSupportedByKeyType`, `BV-INPUT-012 NotBase64`.

**Spec:** `Transit.Sign — TRS-003`

*Source: `dotnet/BastionVault.IntegrationSdk/TransitOperations.cs:362`*

#### `VerifyAsync(name, signature, input, inputBase64, mount, options, cancellationToken)`

`POST {mount}/verify/{name}`. TRS-012: a wire `{valid: false}` becomes
`false`; a framing or algorithm error (`BV-INPUT-011`,
`BV-TRANSIT-005`/`006`) still raises, exactly as any other server error does —
nothing here catches it. An envelope missing `valid` entirely (or carrying a
non-boolean) also raises rather than reporting a false "signature invalid".

Wire params: `name`/`mount` build the route; body carries base64 `input` and `signature`. Returns `bool`, never `null`. Conformance: Standard (TRS-012). Errors beyond the common set (ERR-061): `BV-TRANSIT-001 KeyNotFound`, `BV-INPUT-011`, `BV-TRANSIT-005 OperationNotSupportedByKeyType`, `BV-TRANSIT-006 AlgorithmMismatch`, `BV-INPUT-012 NotBase64`.

**Spec:** `Transit.Verify — TRS-012`

*Source: `dotnet/BastionVault.IntegrationSdk/TransitOperations.cs:396`*

#### `HmacAsync(name, input, inputBase64, algorithm, mount, options, cancellationToken)`

Computes an HMAC over `input` with an `hmac`-type Transit key: `POST {mount}/hmac/{name}`.

Wire params: `name`/`mount` build the route; body carries base64 `input` and `algorithm` (default `sha2-256`). Returns <see cref="TransitHmacResult"/>, never `null`. Conformance: Standard (TRS-003). Errors beyond the common set (ERR-061): `BV-TRANSIT-001 KeyNotFound`, `BV-TRANSIT-005 OperationNotSupportedByKeyType`, `BV-INPUT-012 NotBase64`.

**Spec:** `Transit.Hmac — TRS-003`

*Source: `dotnet/BastionVault.IntegrationSdk/TransitOperations.cs:425`*

#### `VerifyHmacAsync(name, hmac, input, inputBase64, algorithm, mount, options, cancellationToken)`

`POST {mount}/verify/{name}/hmac`. TRS-012: the same false-vs-raise split as
<see cref="VerifyAsync"/>, including the missing/non-boolean `valid` raise.

Wire params: `name`/`mount` build the route; body carries base64 `input`, `hmac`, `algorithm`. Returns `bool`, never `null`. Conformance: Standard (TRS-012). Errors beyond the common set (ERR-061): `BV-TRANSIT-001 KeyNotFound`, `BV-TRANSIT-006 AlgorithmMismatch` (a pqc-tagged HMAC framing), `BV-INPUT-012 NotBase64`.

**Spec:** `Transit.VerifyHmac — TRS-012`

*Source: `dotnet/BastionVault.IntegrationSdk/TransitOperations.cs:462`*

#### `GenerateDataKeyAsync(name, mode, context, contextBase64, mount, options, cancellationToken)`

Generates a datakey wrapped (or plaintext-and-wrapped) by an AEAD or KEM Transit key: `POST {mount}/datakey/{plaintext|wrapped}/{name}`.

Wire params: `name`/`mount`/`mode` build the route (`plaintext` or `wrapped` segment); body carries optional base64 `context`. Returns <see cref="TransitDataKeyResult"/>, never `null`; `Plaintext` is `null` in `Wrapped` mode. Conformance: Standard (TRS-013). Errors beyond the common set (ERR-061): `BV-TRANSIT-001 KeyNotFound`, `BV-TRANSIT-005 OperationNotSupportedByKeyType`, `BV-TRANSIT-006 AlgorithmMismatch`, `BV-INPUT-012 NotBase64`.

**Spec:** `Transit.GenerateDataKey — TRS-013`

*Source: `dotnet/BastionVault.IntegrationSdk/TransitOperations.cs:496`*

#### `UnwrapDataKeyAsync(name, ciphertext, mount, options, cancellationToken)`

`POST {mount}/datakey/unwrap/{name}`. TRS-013: the plaintext is held in <see cref="SecretBytes"/>.

Wire params: `name`/`mount` build the route; body carries `ciphertext`. Returns the unwrapped plaintext as <see cref="SecretBytes"/>, never `null`. Conformance: Standard (TRS-013). Errors beyond the common set (ERR-061): `BV-INPUT-011 InvalidCiphertextFormat`, `BV-TRANSIT-001 KeyNotFound`.

**Spec:** `Transit.UnwrapDataKey — TRS-013`

*Source: `dotnet/BastionVault.IntegrationSdk/TransitOperations.cs:534`*

#### `RandomAsync(bytesCount, mount, options, cancellationToken)`

`POST {mount}/random`. TRS-011: `bytesCount` above 4096 is refused client-side (`BV-INPUT-004`).

Wire params: `mount` builds the route; body carries `bytes` (default 32). Returns the random bytes, never `null`. Conformance: Standard (TRS-011). Errors beyond the common set (ERR-061): `BV-INPUT-004` (server-side cap, mirrored client-side per TRS-011).

**Spec:** `Transit.Random — TRS-011`

*Source: `dotnet/BastionVault.IntegrationSdk/TransitOperations.cs:560`*

#### `HashAsync(input, inputBase64, algorithm, mount, options, cancellationToken)`

Hashes `input` server-side without a key: `POST {mount}/hash`.

Wire params: `mount` builds the route; body carries base64 `input` and `algorithm` (`sha2-256/384/512`). Returns the digest bytes, never `null`. Conformance: Standard (TRS-003). Errors beyond the common set (ERR-061): `BV-INPUT-012 NotBase64`.

**Spec:** `Transit.Hash — TRS-003`

*Source: `dotnet/BastionVault.IntegrationSdk/TransitOperations.cs:579`*

