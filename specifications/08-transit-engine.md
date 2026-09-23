# 08 — Transit Engine (`transit`)

Encryption as a service with a post-quantum-capable key set. Operations live under
`Client.Transit` and take `mount` (default `"transit"`).

## Key types

| Type | Kind | Operations |
|------|------|------------|
| `chacha20-poly1305` (default) | symmetric AEAD | encrypt, decrypt, rewrap, datakey (AEAD-wrapped) |
| `hmac` | symmetric | hmac, verify/hmac |
| `ed25519` | signature | sign, verify |
| `ml-dsa-44`, `ml-dsa-65`, `ml-dsa-87` | post-quantum signature | sign, verify |
| `ml-kem-768` | post-quantum KEM | datakey plaintext/wrapped/unwrap |
| hybrid types (feature-gated on the server) | — | as reported by the server |

- **TRS-001** The SDK MUST expose the types as constants and MUST pass unknown strings
  through (`Other(string)`) so new server key types work without an SDK release.

## Ciphertext framing

`bvault:v<N>:<base64>` for symmetric output and `bvault:v<N>:pqc:<algo>:<base64>` for
post-quantum outputs; signatures and HMACs use the same prefix scheme.

- **TRS-002** The SDK MUST provide `Transit.ParseCiphertext(str) -> { Version, Algo?,
  Bytes }` and MUST validate the `bvault:` prefix client-side before `Decrypt`/`Rewrap`
  (`BV-INPUT-011 InvalidCiphertextFormat`), mirroring the server strings
  `not a bvault ciphertext: missing `bvault:` prefix`, `malformed ciphertext: …`.
- **TRS-003** All binary inputs (`plaintext`, `context`, `input`) are **base64** on the
  wire. The SDK MUST accept bytes and encode; it MUST also accept a pre-encoded string
  through an explicit `*Base64` parameter to avoid double encoding.

## Operations

| Operation | HTTP | Body | Response |
|-----------|------|------|----------|
| `Transit.ListKeys(mount)` | `LIST {mount}/keys/` | — | `{keys}` |
| `Transit.CreateKey(mount, name, KeyOptions)` | `POST {mount}/keys/{name}` | `key_type`, `exportable`, `deletion_allowed`, `derived`, `convergent_encryption` | key metadata |
| `Transit.ReadKey(mount, name)` → `TransitKey?` | `GET {mount}/keys/{name}` | — | `{name, type, latest_version, min_decryption_version, min_available_version, deletion_allowed, exportable, derived, convergent_encryption, keys}` |
| `Transit.DeleteKey(mount, name)` | `DELETE {mount}/keys/{name}` | — | 204 |
| `Transit.RotateKey(mount, name)` | `POST {mount}/keys/{name}/rotate` | — | metadata |
| `Transit.ConfigureKey(mount, name, KeyConfig)` | `POST {mount}/keys/{name}/config` | `min_decryption_version`, `min_available_version`, `deletion_allowed` (0 = unchanged) | metadata |
| `Transit.TrimKey(mount, name)` | `POST {mount}/keys/{name}/trim` | — | metadata + `dropped_versions` |
| `Transit.Encrypt(mount, name, plaintext, context?)` → `{Ciphertext, KeyVersion}` | `POST {mount}/encrypt/{name}` | `plaintext` b64, `context` b64 | `{ciphertext, key_version}` |
| `Transit.Decrypt(mount, name, ciphertext, context?)` → `bytes` | `POST {mount}/decrypt/{name}` | | `{plaintext}` b64 |
| `Transit.Rewrap(mount, name, ciphertext, context?)` | `POST {mount}/rewrap/{name}` | | `{ciphertext, key_version}` |
| `Transit.Sign(mount, name, input)` → `{Signature, KeyVersion}` | `POST {mount}/sign/{name}` | `input` b64 | `{signature, key_version}` |
| `Transit.Verify(mount, name, input, signature)` → `bool` | `POST {mount}/verify/{name}` | | `{valid}` |
| `Transit.Hmac(mount, name, input, algorithm = sha2-256)` | `POST {mount}/hmac/{name}` | | `{hmac, key_version}` |
| `Transit.VerifyHmac(mount, name, input, hmac, algorithm)` → `bool` | `POST {mount}/verify/{name}/hmac` | | `{valid}` |
| `Transit.GenerateDataKey(mount, name, mode = Wrapped)` | `POST {mount}/datakey/{plaintext|wrapped}/{name}` | — | `{ciphertext, key_version, plaintext?}` |
| `Transit.UnwrapDataKey(mount, name, ciphertext)` → `bytes` | `POST {mount}/datakey/unwrap/{name}` | | `{plaintext}` |
| `Transit.Random(mount, bytes = 32)` → `bytes` | `POST {mount}/random` | `bytes` ≤ 4096 | `{random_bytes}` |
| `Transit.Hash(mount, input, algorithm = sha2-256)` → `bytes` | `POST {mount}/hash` | `sha2-256/384/512` | `{sum}` |
| `Transit.Byok.*` (`wrapping_key`, `keys/{name}/import`, `import_version`) | feature `transit_byok` | | `BV-SERVER-004` when absent |

- **TRS-010** Key metadata `keys` is `{version: creation_time}` for symmetric keys and
  `{version: {public_key, creation_time}}` for asymmetric keys; the SDK MUST normalise to
  `Map<int, KeyVersionInfo { CreationTime, PublicKey? }>`. `creation_time` MUST be read
  through the tolerant timestamp rule in
  [03 — Timestamp encoding](03-transport-and-protocol.md#timestamp-encoding), in **both**
  shapes: the bare value of the symmetric form and the nested field of the asymmetric one.

  > **Measured — `bvault` 0.44.5, 2026-09-23** ([DR-0021](../decisions/0021-live-server-findings.md)
  > F8): this server sends `creation_time` as a **Unix-epoch number** in both shapes,
  > captured with `curl` outside the SDK:
  >
  > ```
  > POST transit/keys/testkey {"key_type":"chacha20-poly1305"}
  > → "data":{"keys":{"1":1790163936}, ...}
  > POST transit/keys/edkey  {"key_type":"ed25519"}
  > → "data":{"keys":{"1":{"creation_time":1790163936,"public_key":"..."}}, ...}
  > ```
  >
  > A string-only reader rejects every one of `CreateKey`, `ReadKey`, `RotateKey`,
  > `ConfigureKey` and `TrimKey` — the whole key-metadata surface — which is why the rule
  > is stated as tolerance rather than as a change of declared type.
- **TRS-011** `Random.bytes > 4096` → `BV-INPUT-004` client-side.
- **TRS-012** `Verify`/`VerifyHmac` MUST return `false` from `{valid: false}` and MUST
  raise for framing/algorithm errors (`BV-INPUT-011`, `BV-TRANSIT-005`).
- **TRS-013** `Decrypt` output MUST be returned as bytes in a type that redacts in
  logs; `Encrypt` MUST accept `Context` only for `derived` keys and MUST document that
  `convergent_encryption` requires `derived`.

## Server error strings (recognition)

All are `500` on the wire (generic `ErrString`), so recognition is by message:

| Server message (prefix/pattern) | Code |
|---------------------------------|------|
| ``unknown key `<name>` `` | `BV-TRANSIT-001 KeyNotFound` |
| ``key `<name>` already exists with type …`` | `BV-TRANSIT-002 KeyTypeConflict` |
| `deletion_allowed is false; flip it via /keys/<name>/config first` | `BV-TRANSIT-003 DeletionNotAllowed` |
| `version <v> is below min_decryption_version <m>` | `BV-TRANSIT-004 VersionBelowMinDecryption` |
| `version <v> not found on key …` | `BV-TRANSIT-004` (Details.version) |
| `… does not support /encrypt`, `/encrypt is symmetric-AEAD only …`, `/decrypt is symmetric only …`, `… do not support /sign`, `/verify`, `/hmac`, `/datakey` | `BV-TRANSIT-005 OperationNotSupportedByKeyType` |
| `signature algorithm mismatch …`, `datakey algorithm mismatch …`, `HMAC framing must not carry a pqc tag` | `BV-TRANSIT-006 AlgorithmMismatch` |
| `derived / convergent_encryption are symmetric-AEAD only …`, `convergent_encryption=true requires derived=true …` | `BV-INPUT-100` |
| `min_decryption_version <n> exceeds latest_version <m>`, `min_available_version … would discard …`, `trim would leave the key with no versions …` | `BV-INPUT-100` |
| `plaintext: not base64 (…)`, `context: not base64 (…)`, `input: not base64 (…)` | `BV-INPUT-012 NotBase64` |
| `not a bvault ciphertext …`, `malformed ciphertext …`, `ciphertext version must be >= 1`, `malformed pqc ciphertext …` | `BV-INPUT-011` |
| `bytes capped at 4096, got <n>` | `BV-INPUT-004` |
| ``datakey mode must be `plaintext` or `wrapped` …`` | `BV-INPUT-001` |
| `name is required`, `key has no versions` | `BV-INPUT-100` |
