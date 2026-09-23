# Resources engine (.NET)

Section 17 carries no dedicated usage guide for Resources; this page is built directly from
[12 — Other engines and identity](../../../specifications/12-other-engines-and-identity.md)'s
Resources table (`RSC-001`, `RSC-002`), following the same task → prerequisites → steps → complete
example → what can go wrong → next steps structure every other engine guide on this site uses
(`DOC-010`).

## The task

Record a resource — a database, a host, anything worth naming — attach a redacting secret to it,
rename it without breaking the secrets, shares and ownership records that point at it, and run the
**connect-MFA** flow that gates a live connection behind a second factor. **This SDK does no MFA
verification of its own**: `MfaVerify`'s `totp_code`/`credential` travel to the server exactly as
supplied, and the server decides whether the factor is valid.

Every C# block below is compiled and executed on every build by
`dotnet/BastionVault.IntegrationSdk.DocsSamples`, and checked byte-for-byte against the source
that ran (`DOC-003`). If a block here is wrong, the build is red.

## Prerequisites

| You need | Detail |
|---|---|
| A running server | `https://vault.example.com:8200` throughout this guide |
| A token | Carrying the policy below. The [authentication guide](../authentication.md) covers obtaining one |
| A Resources mount | `resources/` — the default `mount` on every `Client.Resources` member |
| The package | `dotnet add package BastionVault.IntegrationSdk` |

### The policy the example needs

<!-- docs:sample resources/policy -->
```csharp
string hcl = new PolicyBuilder()
    .AddPath("resources/resources/db-primary", [Capability.Create, Capability.Read, Capability.Update])
    .AddPath("resources/resources/db-primary/rename", [Capability.Update])
    .AddPath("resources/secrets/db-primary/password", [Capability.Create, Capability.Read])
    .AddPath("resources/v2/connect/mfa/begin", [Capability.Update])
    .AddPath("resources/v2/connect/mfa/verify", [Capability.Update])
    .AddPath("resources/v2/connect/authorize", [Capability.Update])
    .Build();

Console.WriteLine(hcl);
```

which emits:

```hcl
path "resources/resources/db-primary" {
  capabilities = ["create", "read", "update"]
}

path "resources/resources/db-primary/rename" {
  capabilities = ["update"]
}

path "resources/secrets/db-primary/password" {
  capabilities = ["create", "read"]
}

path "resources/v2/connect/mfa/begin" {
  capabilities = ["update"]
}

path "resources/v2/connect/mfa/verify" {
  capabilities = ["update"]
}

path "resources/v2/connect/authorize" {
  capabilities = ["update"]
}
```

## Step 1 — Write a resource record, then rename it

<!-- docs:sample resources/write-and-rename -->
```csharp
using JsonDocument record = JsonDocument.Parse("""{"type":"database","host":"db1.internal","port":5432}""");
await client.Resources.WriteAsync("db-primary", record.RootElement.Clone());

BastionVault.IntegrationSdk.Response? read = await client.Resources.ReadAsync("db-primary");
// RSC-002: Resources.Read is a plain, unredacted record - unlike Resources.Secrets below.
Console.WriteLine($"resource record: {read!.Raw}");

// Rename migrates the resource's secrets, shares, groups and ownership records with it.
await client.Resources.RenameAsync("db-primary", "db-primary-east");
```

### What goes over the wire

```http
PUT /v1/resources/resources/db-primary HTTP/1.1
Host: vault.example.com:8200
X-BastionVault-Token: s.FAKEtoken
Content-Type: application/json

{"type":"database","host":"db1.internal","port":5432}
```

```json
{"data":{}}
```

`Resources.Read`'s record is a plain, unredacted `Response` — RSC-002's asymmetry with
`Resources.Secrets` below, which is redacting field-by-field. `Resources.Rename` migrates the
resource's secrets, shares, groups and ownership records to the new name in one call; nothing under
the old name is left dangling.

## Step 2 — Attach a redacting secret to the resource

<!-- docs:sample resources/secrets -->
```csharp
using JsonDocument value = JsonDocument.Parse("""{"value":"correct horse battery staple"}""");
await client.Resources.Secrets.WriteAsync("db-primary", "password", value.RootElement.Clone());

// RSC-002: unlike Resources.Read, every field value here comes back wrapped in SecretString.
ResourceSecret? secret = await client.Resources.Secrets.ReadAsync("db-primary", "password");
Console.WriteLine($"fields stored: {string.Join(", ", secret!.Data.Keys)}");
```

Every field `Resources.Secrets.Read`/`ReadVersion` returns comes back as a `SecretString`
(RSC-002) — the opposite of `Resources.Read`'s plain record in Step 1. Reach for `Resources.Read`
when you need the resource's own metadata, and `Resources.Secrets` only for values that are
themselves secret.

## Step 3 — Run the connect-MFA flow before authorizing a connection

<!-- docs:sample resources/connect-mfa -->
```csharp
await client.Resources.Connect.MfaBeginAsync(new ConnectMfaBeginRequest
{
    Resource = "db-primary",
    ProfileId = "profile-1",
});

ConnectMfaVerifyResult verified = await client.Resources.Connect.MfaVerifyAsync(new ConnectMfaVerifyRequest
{
    Resource = "db-primary",
    ProfileId = "profile-1",
    Method = "totp",
    TotpCode = "045678",
});
// The ticket is single-use and redacting from the moment it is parsed off the wire (RSC-001).

await client.Resources.Connect.AuthorizeAsync(new ConnectAuthorizeRequest
{
    Resource = "db-primary",
    ProfileId = "profile-1",
    ConnectTicket = verified.ConnectTicket,
});
Console.WriteLine("connect authorized");
```

`MfaVerify`'s `ConnectTicket` is single-use and redacting from the moment it is parsed off the wire
(RSC-001); `Authorize` consumes it in the POST body, never a query string, so it never appears in a
server access log's URL.

## The whole program

<!-- docs:sample resources/complete -->
```csharp
using BastionVaultClient client = new();

try
{
    vault.Server.SetRouteResponse(ResourceRoute, Json(200, "{}"));
    using JsonDocument record = JsonDocument.Parse("""{"type":"database","host":"db1.internal"}""");
    await client.Resources.WriteAsync("db-primary", record.RootElement.Clone());
    Console.WriteLine("resource written");

    vault.Server.SetRouteResponse(SecretRoute, Json(200, SecretBody()));
    ResourceSecret? secret = await client.Resources.Secrets.ReadAsync("db-primary", "password");
    Console.WriteLine($"secret fields: {string.Join(", ", secret!.Data.Keys)}");
}
catch (BastionVaultException e)
{
    Console.Error.WriteLine($"{e.Code}: {e.Message} ({e.Hint}); retryable: {e.Retryable}");
    throw;
}
```

## What can go wrong

Resources carries two requirements of its own — `RSC-001` and `RSC-002` — and no other
engine-specific error codes: every other failure on this surface reaches the caller through the
same generic mapping (Appendix B) that every other route in this SDK shares.

| Code | Meaning | Fix |
|---|---|---|
| `BV-INPUT-001` | `resource` was empty on a Connect-MFA call (RSC-001) | Supply the resource's name; checked client-side before any request is sent |
| `BV-AUTH-002` | Connect-MFA requires an authenticated caller, and none was found (RSC-001) | Authenticate first; this is not a permissions problem |
| `BV-AUTH-016` | The second-factor code or assertion was rejected (RSC-001) | Retry `MfaVerify` with a fresh TOTP code or FIDO2 assertion |
| `BV-NOTFOUND-001` | The named resource, secret key, or version does not exist | Check the name, or write it first |

Handled completely, that is:

<!-- docs:sample resources/handling-errors -->
```csharp
try
{
    // RSC-001: an empty resource is refused client-side, before any request is sent.
    await client.Resources.Connect.MfaBeginAsync(new ConnectMfaBeginRequest { Resource = string.Empty, ProfileId = "profile-1" });
}
catch (BastionVaultException e)
{
    string remedy = e.Code switch
    {
        ErrorCodes.InputInvalidArgument => "`resource` is required (RSC-001); supply the resource's name",
        ErrorCodes.AuthUnauthenticated => "connect MFA requires an authenticated caller (RSC-001)",
        ErrorCodes.AuthSecondFactorFailed => "the second factor was rejected; retry MfaVerify with a fresh code",
        ErrorCodes.NotFoundPathNotFound => "check the resource name, or write it first",
        _ => "look the code up in the error reference",
    };
    Console.Error.WriteLine($"{e.Code}: {e.Message} - {remedy}");
    throw;
}
```

## Next steps

- **Authentication guide** — obtaining the token this guide assumes you already hold.
- **Identity engine guide** — sharing a resource with another entity or group once it exists.
- **Error reference** — every code, its category, hint and retryability.
