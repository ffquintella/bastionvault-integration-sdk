# Transit engine (.NET)

**Implements** [`specifications/17-usage-guides.md` guide 8](../../../specifications/17-usage-guides.md),
adapted to .NET. The language-neutral behaviour this page relies on is specified in
[08 — Transit engine](../../../specifications/08-transit-engine.md) (`TRS-001`…`TRS-013`,
`TRN-050`). Read that document for what every SDK must do; read this page for the .NET spelling
of it.

## The task

Encrypt and decrypt application data with a server-managed key, rotate that key without
re-encrypting anything up front, rewrap an old ciphertext under the new version instead, and sign
and verify a digest. **This SDK performs no cryptography of its own** (00 §Non-goals, OVR-002):
every member of `Client.Transit` base64-encodes your bytes, sends one request, and parses the
response — the server does the actual encryption, signing and key management.

Every C# block below is compiled and executed on every build by
`dotnet/BastionVault.IntegrationSdk.DocsSamples`, and checked byte-for-byte against the source
that ran (`DOC-003`). If a block here is wrong, the build is red.

## Prerequisites

| You need | Detail |
|---|---|
| A running server | `https://vault.example.com:8200` throughout this guide |
| A token | Carrying the policy below. The [authentication guide](../authentication.md) covers obtaining one |
| A Transit mount | `transit/` — the default `mount` on every `Client.Transit` member |
| The package | `dotnet add package BastionVault.IntegrationSdk` |

### The policy the example needs

<!-- docs:sample transit/policy -->
```csharp
string hcl = new PolicyBuilder()
    .AddPath("transit/keys/orders", [Capability.Create, Capability.Read, Capability.Update])
    .AddPath("transit/keys/orders/rotate", [Capability.Update])
    .AddPath("transit/encrypt/orders", [Capability.Update])
    .AddPath("transit/decrypt/orders", [Capability.Update])
    .AddPath("transit/rewrap/orders", [Capability.Update])
    .AddPath("transit/sign/release-signing", [Capability.Update])
    .AddPath("transit/verify/release-signing", [Capability.Update])
    .Build();

Console.WriteLine(hcl);
```

which emits:

```hcl
path "transit/keys/orders" {
  capabilities = ["create", "read", "update"]
}

path "transit/keys/orders/rotate" {
  capabilities = ["update"]
}

path "transit/encrypt/orders" {
  capabilities = ["update"]
}

path "transit/decrypt/orders" {
  capabilities = ["update"]
}

path "transit/rewrap/orders" {
  capabilities = ["update"]
}

path "transit/sign/release-signing" {
  capabilities = ["update"]
}

path "transit/verify/release-signing" {
  capabilities = ["update"]
}
```

Transit's own operation paths — `encrypt/`, `decrypt/`, `rewrap/`, `sign/`, `verify/` — are
separate server routes from `keys/`, and each needs its own policy line even against the same key
name (08 §Operations).

## Step 1 — Create a key, then encrypt and decrypt

<!-- docs:sample transit/encrypt-decrypt -->
```csharp
TransitKey key = await client.Transit.CreateKeyAsync("orders");
// Key.Type defaults server-side to chacha20-poly1305 when KeyOptions is omitted (08 §Key types).

byte[] plaintext = "order #42: 3x widget"u8.ToArray();
vault.Server.SetRouteResponse(EncryptRoute, Json(200, EncryptBody(version: 1)));
TransitEncryptResult encrypted = await client.Transit.EncryptAsync("orders", plaintext);
Console.WriteLine($"ciphertext: {encrypted.Ciphertext} (key version {encrypted.KeyVersion})");

// TRS-002: a malformed value never reaches the server - validated client-side first.
TransitParsedCiphertext parsed = TransitOperations.ParseCiphertext(encrypted.Ciphertext);
Console.WriteLine($"framing version {parsed.Version}");

vault.Server.SetRouteResponse(DecryptRoute, Json(200, DecryptBody(plaintext)));
SecretBytes decrypted = await client.Transit.DecryptAsync("orders", encrypted.Ciphertext);
Console.WriteLine($"round-tripped: {System.Text.Encoding.UTF8.GetString(decrypted.Reveal()!) == "order #42: 3x widget"}");
```

### What goes over the wire

```http
POST /v1/transit/encrypt/orders HTTP/1.1
Host: vault.example.com:8200
X-BastionVault-Token: s.FAKEtoken
Content-Type: application/json

{"plaintext":"b3JkZXIgIzQyOiAzeCB3aWRnZXQ="}
```

```json
{
  "data": {
    "ciphertext": "bvault:v1:c2VhbGVkLWJ5dGVz",
    "key_version": 1
  }
}
```

`TransitEncryptResult.KeyVersion` is the version the server actually used, which matters once the
key has been rotated more than once — a caller does not choose the version, the server reports it
(TRS-010). `SecretBytes` holds the decrypted plaintext so it never reaches a log by accident
(TRS-013); call `.Reveal()` only where you are about to use the bytes.

## Step 2 — Rotate the key and rewrap existing ciphertext

<!-- docs:sample transit/rotate-and-rewrap -->
```csharp
vault.Server.SetRouteResponse(RotateRoute, Json(200, KeyBody(latestVersion: 2)));
TransitKey rotated = await client.Transit.RotateKeyAsync("orders");
Console.WriteLine($"latest version is now {rotated.LatestVersion}");

// Rewrap re-encrypts under the new version server-side; the plaintext never leaves the
// server and this SDK never sees it, unlike a decrypt-then-encrypt round trip you might
// write instead.
vault.Server.SetRouteResponse(RewrapRoute, Json(200, EncryptBody(version: 2)));
TransitEncryptResult rewrapped = await client.Transit.RewrapAsync("orders", "bvault:v1:c2VhbGVkLWJ5dGVz");
Console.WriteLine($"now under key version {rewrapped.KeyVersion}");

// A version below MinDecryptionVersion answers BV-TRANSIT-004; ConfigureKey moves that floor.
vault.Server.SetRouteResponse(ConfigRoute, Json(200, KeyBody(latestVersion: 2, minDecryptionVersion: 2)));
TransitKey trimmed = await client.Transit.ConfigureKeyAsync("orders", new TransitKeyConfig { MinDecryptionVersion = 2 });
Console.WriteLine($"versions below {trimmed.MinDecryptionVersion} can no longer decrypt");
```

Rotation creates a new key version without touching any ciphertext already written under an older
one (08 §Operations); `Rewrap` is how you move existing data forward, one ciphertext at a time,
without ever exposing the plaintext to this SDK or its caller.

## Step 3 — Sign and verify with an asymmetric key

<!-- docs:sample transit/sign-and-verify -->
```csharp
await client.Transit.CreateKeyAsync("release-signing", new TransitKeyOptions { KeyType = TransitKeyTypes.Ed25519 });

byte[] digest = System.Security.Cryptography.SHA256.HashData("release-1.4.0.tar.gz"u8.ToArray());
vault.Server.SetRouteResponse(SignRoute, Json(200, SignBody()));
TransitSignResult signature = await client.Transit.SignAsync("release-signing", digest);

vault.Server.SetRouteResponse(VerifyRoute, Json(200, """{"valid":true}"""));
bool valid = await client.Transit.VerifyAsync("release-signing", signature.Signature, digest);
Console.WriteLine($"signature valid: {valid}");
```

`VerifyAsync` returns `false` only for a server-confirmed `{valid: false}` — a framing or
algorithm mismatch still raises `BV-INPUT-011`/`BV-TRANSIT-005`/`BV-TRANSIT-006` rather than
silently reporting "invalid" (TRS-012); do not treat a caught exception here the same way as a
`false` return.

## The whole program

<!-- docs:sample transit/complete -->
```csharp
using BastionVaultClient client = new();

try
{
    vault.Server.SetRouteResponse(KeyRoute, Json(200, KeyBody(latestVersion: 1)));
    await client.Transit.CreateKeyAsync("orders");

    byte[] plaintext = "order #42"u8.ToArray();
    vault.Server.SetRouteResponse(EncryptRoute, Json(200, EncryptBody(version: 1)));
    TransitEncryptResult encrypted = await client.Transit.EncryptAsync("orders", plaintext);
    Console.WriteLine($"encrypted under key version {encrypted.KeyVersion}");

    vault.Server.SetRouteResponse(DecryptRoute, Json(200, DecryptBody(plaintext)));
    SecretBytes decrypted = await client.Transit.DecryptAsync("orders", encrypted.Ciphertext);
    Console.WriteLine($"decrypted length: {decrypted.Reveal()!.Length}");

    vault.Server.SetRouteResponse(RotateRoute, Json(200, KeyBody(latestVersion: 2)));
    await client.Transit.RotateKeyAsync("orders");

    vault.Server.SetRouteResponse(RewrapRoute, Json(200, EncryptBody(version: 2)));
    TransitEncryptResult rewrapped = await client.Transit.RewrapAsync("orders", encrypted.Ciphertext);
    Console.WriteLine($"rewrapped to key version {rewrapped.KeyVersion}");
}
catch (BastionVaultException e)
{
    Console.Error.WriteLine($"{e.Code}: {e.Message} ({e.Hint}); retryable: {e.Retryable}");
    throw;
}
```

## What can go wrong

| Code | Meaning | Fix |
|---|---|---|
| `BV-TRANSIT-001` | The named key does not exist on this mount | Check the name, or `CreateKeyAsync` it first |
| `BV-TRANSIT-002` | `CreateKey` named a key that already exists with a different type | Pick a new name, or read the existing key's type first |
| `BV-TRANSIT-003` | `DeleteKey` was called but `deletion_allowed` is `false` | `ConfigureKeyAsync` with `DeletionAllowed = true` first |
| `BV-TRANSIT-004` | The ciphertext's version is below `min_decryption_version` | Use a version still within the decryptable range, or lower the floor |
| `BV-TRANSIT-005` | This operation is not supported by the key's type | Check `08 §Key types` for what each type supports |
| `BV-TRANSIT-006` | A signature, HMAC or datakey algorithm mismatch | Match the algorithm the key was created with |
| `BV-INPUT-011` | The ciphertext is not a valid `bvault:` framed value | Pass the exact string `Encrypt`/`Sign` returned, unmodified |
| `BV-INPUT-012` | A binary argument was not valid base64 | Pass raw `byte[]` and let the SDK encode it, or fix your own encoding |

Handled completely, that is:

<!-- docs:sample transit/handling-errors -->
```csharp
try
{
    await client.Transit.DecryptAsync("orders", "bvault:v1:not-really-base64");
}
catch (BastionVaultException e)
{
    string remedy = e.Code switch
    {
        ErrorCodes.TransitKeyNotFound => "check the name, or create the key first",
        ErrorCodes.TransitDeletionNotAllowed => "flip deletion_allowed via ConfigureKey first",
        ErrorCodes.TransitVersionNotDecryptable => "use a version still above min_decryption_version",
        ErrorCodes.TransitOperationNotSupportedByKeyType => "check 08 for what this key type supports",
        ErrorCodes.TransitAlgorithmMismatch => "match the algorithm the key was created with",
        ErrorCodes.InputInvalidCiphertextFormat => "pass the exact ciphertext Encrypt/Sign returned",
        ErrorCodes.InputNotBase64 => "pass raw bytes and let the SDK encode them",
        _ => "look the code up in the error reference",
    };
    Console.Error.WriteLine($"{e.Code}: {e.Message} - {remedy}");
    throw;
}
```

## Next steps

- **Authentication guide** — obtaining the token this guide assumes you already hold.
- **Secrets (KV) guide** — storing configuration and non-cryptographic secrets alongside Transit
  keys.
- **Error reference** — every code, its category, hint and retryability.
