# TOTP engine (.NET)

Section 17 carries no dedicated usage guide for TOTP; this page is built directly from
[11 — TOTP engine](../../../specifications/11-totp-engine.md) (`TOT-001`…`TOT-004`), following the
same task → prerequisites → steps → complete example → what can go wrong → next steps structure
every other engine guide on this site uses (`DOC-010`).

## The task

Create a **generate-mode** TOTP key (the server owns the seed and produces codes for your own
authenticator flows) and a **provider-mode** key (the seed came from elsewhere — an existing
authenticator app, say — and the server only validates codes against it). **The SDK generates and
validates no TOTP codes itself** (00 §Non-goals, OVR-002): every member of `Client.Totp` is
request building, response parsing and error mapping; the HOTP/TOTP algorithm runs on the server.

Every C# block below is compiled and executed on every build by
`dotnet/BastionVault.IntegrationSdk.DocsSamples`, and checked byte-for-byte against the source
that ran (`DOC-003`). If a block here is wrong, the build is red.

## Prerequisites

| You need | Detail |
|---|---|
| A running server | `https://vault.example.com:8200` throughout this guide |
| A token | Carrying the policy below. The [authentication guide](../authentication.md) covers obtaining one |
| A TOTP mount | `totp/` — the default `mount` on every `Client.Totp` member |
| The package | `dotnet add package BastionVault.IntegrationSdk` |

### The policy the example needs

<!-- docs:sample totp/policy -->
```csharp
string hcl = new PolicyBuilder()
    .AddPath("totp/keys/alice", [Capability.Create, Capability.Read, Capability.Delete])
    .AddPath("totp/code/alice", [Capability.Read, Capability.Update])
    .AddPath("totp/keys/vendor-app", [Capability.Create, Capability.Read])
    .AddPath("totp/code/vendor-app", [Capability.Update])
    .Build();

Console.WriteLine(hcl);
```

which emits:

```hcl
path "totp/keys/alice" {
  capabilities = ["create", "read", "delete"]
}

path "totp/code/alice" {
  capabilities = ["read", "update"]
}

path "totp/keys/vendor-app" {
  capabilities = ["create", "read"]
}

path "totp/code/vendor-app" {
  capabilities = ["update"]
}
```

`totp/code/{name}` is read with `GET` in generate-mode and written with `POST` in provider-mode
(11 §Operations), so a role serving both kinds of key needs both capabilities on that path, exactly
as shown above.

## Step 1 — Create a generate-mode key and read a code back

<!-- docs:sample totp/generate-mode -->
```csharp
// TOT-001: exactly one of Generate/Key/Url; AccountName is required unless Url carries a label.
TotpKeyCreated created = await client.Totp.CreateKeyAsync("alice", new TotpKeySpec
{
    Generate = true,
    AccountName = "alice",
    Issuer = "Corp Vault",
});
Console.WriteLine($"seed exported: {created.Key!.HasValue}");
// created.Key/Url/Barcode are present because CreateKeyAsync's Exported defaulted to true.

string code = await client.Totp.GenerateCodeAsync("alice");
Console.WriteLine($"current code: {code}");
// TOT-004: code is a string, never parsed as a number - a leading zero would otherwise be lost.
```

### What goes over the wire

```http
POST /v1/totp/keys/alice HTTP/1.1
Host: vault.example.com:8200
X-BastionVault-Token: s.FAKEtoken
Content-Type: application/json

{"generate":true,"account_name":"alice","issuer":"Corp Vault"}
```

```json
{
  "data": {
    "name": "alice",
    "generate": true,
    "key": "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP",
    "url": "otpauth://totp/issuer:alice?secret=JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP&issuer=issuer",
    "barcode": "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="
  }
}
```

`created.Key` and `created.Url` are `SecretString` (TOT-002) even though they are seed material
rather than a bearer token — they are exactly as sensitive as one. `created.Barcode` is the QR PNG,
already decoded from the wire's base64 into bytes.

## Step 2 — Create a provider-mode key and validate a code

<!-- docs:sample totp/provider-mode -->
```csharp
TotpKeyCreated vendor = await client.Totp.CreateKeyAsync("vendor-app", new TotpKeySpec
{
    Key = new SecretString("JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP"),
    AccountName = "vendor-app",
    Digits = 6,
});
Console.WriteLine($"provider-mode key created: generate={vendor.Generate}");

bool valid = await client.Totp.ValidateCodeAsync("vendor-app", "123456");
// TOT-003: false covers both a wrong code and a replayed one when replay_check is on - the
// two are indistinguishable at this API, by server design, not an SDK gap.
Console.WriteLine($"code accepted: {valid}");
```

## The whole program

<!-- docs:sample totp/complete -->
```csharp
using BastionVaultClient client = new();

try
{
    vault.Server.SetRouteResponse(AliceKeyRoute, Json(200, GenerateModeCreatedBody()));
    TotpKeyCreated created = await client.Totp.CreateKeyAsync("alice", new TotpKeySpec { Generate = true, AccountName = "alice" });
    Console.WriteLine($"created key, generate-mode={created.Generate}");

    vault.Server.SetRouteResponse(AliceCodeRoute, Json(200, """{"data":{"code":"045678"}}"""));
    string code = await client.Totp.GenerateCodeAsync("alice");
    Console.WriteLine($"code: {code}");

    vault.Server.SetRouteResponse(AliceKeyRoute, Json(200, ReadKeyBody()));
    TotpKey? readBack = await client.Totp.ReadKeyAsync("alice");
    Console.WriteLine($"digits configured: {readBack!.Digits}");
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
| `BV-TOTP-001` | The named key does not exist | Check the name, or `CreateKeyAsync` it first |
| `BV-TOTP-002` | The operation does not match the key's mode (`GET code` on provider-mode, or `POST code` on generate-mode) | Use `GenerateCodeAsync` for generate-mode, `ValidateCodeAsync` for provider-mode |
| `BV-INPUT-001` | `CreateKey`'s exclusivity rule was violated, or `account_name`/`code` was missing | TOT-001 checks this client-side before any request is sent |

Handled completely, that is:

<!-- docs:sample totp/handling-errors -->
```csharp
try
{
    await client.Totp.GenerateCodeAsync("vendor-app");
}
catch (BastionVaultException e)
{
    string remedy = e.Code switch
    {
        ErrorCodes.TotpKeyNotFound => "check the name, or create the key first",
        ErrorCodes.TotpWrongModeForOperation => "use GenerateCode for generate-mode, ValidateCode for provider-mode",
        ErrorCodes.InputInvalidArgument => "check TOT-001's exclusivity and required-field rules",
        _ => "look the code up in the error reference",
    };
    Console.Error.WriteLine($"{e.Code}: {e.Message} - {remedy}");
    throw;
}
```

## Next steps

- **Authentication guide** — obtaining the token this guide assumes you already hold.
- **Error reference** — every code, its category, hint and retryability.
