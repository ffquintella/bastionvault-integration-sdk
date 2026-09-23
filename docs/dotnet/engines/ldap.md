# LDAP / Active Directory engine (.NET)

Section 17 carries no dedicated usage guide for LDAP; this page is built directly from
[12 — Other engines and identity](../../../specifications/12-other-engines-and-identity.md)'s
LDAP / Active Directory table (`LDP-001`), following the same task → prerequisites → steps →
complete example → what can go wrong → next steps structure every other engine guide on this site
uses (`DOC-010`).

## The task

Point this SDK at an existing LDAP or Active Directory server, rotate the bind account's own
password, define a **static role** whose credential the directory rotates on a schedule, and pool
a set of shared service accounts through the **check-out library** so two callers never hold the
same account at once. **This SDK performs no directory protocol of its own** (00 §Non-goals,
OVR-002): every field below is request building, response parsing and error mapping; the LDAP bind
and the password rotation happen on the server.

Every C# block below is compiled and executed on every build by
`dotnet/BastionVault.IntegrationSdk.DocsSamples`, and checked byte-for-byte against the source
that ran (`DOC-003`). If a block here is wrong, the build is red.

## Prerequisites

| You need | Detail |
|---|---|
| A running server | `https://vault.example.com:8200` throughout this guide |
| A token | Carrying the policy below. The [authentication guide](../authentication.md) covers obtaining one |
| An LDAP mount | `openldap/` — the default `mount` on every `Client.Ldap` member |
| The package | `dotnet add package BastionVault.IntegrationSdk` |

### The policy the example needs

<!-- docs:sample ldap/policy -->
```csharp
string hcl = new PolicyBuilder()
    .AddPath("openldap/config", [Capability.Create, Capability.Read, Capability.Update])
    .AddPath("openldap/rotate-root", [Capability.Update])
    .AddPath("openldap/static-role/dbadmin", [Capability.Create, Capability.Read])
    .AddPath("openldap/static-cred/dbadmin", [Capability.Read])
    .AddPath("openldap/library/svc-accounts", [Capability.Create, Capability.Read])
    .AddPath("openldap/library/svc-accounts/check-out", [Capability.Update])
    .AddPath("openldap/library/svc-accounts/check-in", [Capability.Update])
    .Build();

Console.WriteLine(hcl);
```

which emits:

```hcl
path "openldap/config" {
  capabilities = ["create", "read", "update"]
}

path "openldap/rotate-root" {
  capabilities = ["update"]
}

path "openldap/static-role/dbadmin" {
  capabilities = ["create", "read"]
}

path "openldap/static-cred/dbadmin" {
  capabilities = ["read"]
}

path "openldap/library/svc-accounts" {
  capabilities = ["create", "read"]
}

path "openldap/library/svc-accounts/check-out" {
  capabilities = ["update"]
}

path "openldap/library/svc-accounts/check-in" {
  capabilities = ["update"]
}
```

## Step 1 — Configure the directory, rotate the root password, and probe it

<!-- docs:sample ldap/configure-and-rotate -->
```csharp
await client.Ldap.WriteConfigAsync(new LdapConfig
{
    Url = "ldaps://ldap.example.com:636",
    BindDn = "cn=vault,ou=svc,dc=example,dc=com",
    BindPass = new SecretString("bindpassword123"),
    UserDn = "ou=people,dc=example,dc=com",
    DirectoryType = "active_directory",
    StartTls = false,
    TlsMinVersion = "tls13",
});
// LDP-001: InsecureTls stays unset here; setting it true without AcknowledgeInsecureTls
// also true is refused client-side (BV-INPUT-001), before any request is sent.

await client.Ldap.RotateRootAsync();

LdapCheckConnectionResult probe = await client.Ldap.CheckConnectionAsync();
Console.WriteLine($"connection ok: {probe.Ok} (stage {probe.Stage}, {probe.LatencyMs}ms)");
```

### What goes over the wire

```http
POST /v1/openldap/config HTTP/1.1
Host: vault.example.com:8200
X-BastionVault-Token: s.FAKEtoken
Content-Type: application/json

{"url":"ldaps://ldap.example.com:636","binddn":"cn=vault,ou=svc,dc=example,dc=com","bindpass":"bindpassword123","userdn":"ou=people,dc=example,dc=com","directory_type":"active_directory","starttls":false,"tls_min_version":"tls13"}
```

```json
{
  "data": {
    "ok": true,
    "stage": "bind",
    "url": "ldaps://ldap.example.com:636",
    "bind_dn": "cn=vault,ou=svc,dc=example,dc=com",
    "host": "ldap.example.com",
    "port": 636,
    "scheme": "ldaps",
    "latency_ms": 42
  }
}
```

`LdapCheckConnectionResult`'s remark is the whole shape here: only `Ok` is guaranteed. A `bind`
stage failure would report `Stage`/`Error` but never `Host`/`Port`/`LatencyMs` — those only appear
as far as the probe actually got. `BindPass` and `ClientTlsKey` are write-only: the server never
returns either field on a subsequent `ReadConfigAsync`, so both stay `null` there by design, not by
omission.

## Step 2 — Define a static role, then read its rotated credential

<!-- docs:sample ldap/static-role-and-cred -->
```csharp
await client.Ldap.StaticRoles.WriteAsync("dbadmin", new LdapStaticRole
{
    Dn = "cn=dbadmin,ou=svc,dc=example,dc=com",
    Username = "dbadmin",
    RotationPeriod = TimeSpan.FromHours(24),
});

LdapStaticCred? cred = await client.Ldap.StaticCredAsync("dbadmin");
Console.WriteLine($"{cred!.Username}: last rotated {cred.LastRotated:O}");
// The password never touches this line unredacted: cred.Password.Reveal() is where it would.
```

`LdapStaticCred.Password` is a `SecretString` from the moment it is parsed off the wire — the same
treatment every other issued credential gets in this SDK, whether it comes from LDAP, SSH OTP or a
Transit key.

## Step 3 — Pool shared service accounts through the check-out library

<!-- docs:sample ldap/library-checkout -->
```csharp
await client.Ldap.Library.WriteAsync("svc-accounts", new LdapLibrarySet
{
    ServiceAccountNames = ["svc-1", "svc-2"],
    Ttl = TimeSpan.FromHours(1),
    MaxTtl = TimeSpan.FromHours(24),
});

LdapLibraryCheckOut checkedOut = await client.Ldap.Library.CheckOutAsync("svc-accounts", ttl: TimeSpan.FromMinutes(30));
Console.WriteLine($"checked out {checkedOut.ServiceAccountName}, lease {checkedOut.LeaseId}");

// Always check an account back in when the caller is done with it; the affinity_ttl and
// disable_check_in_enforcement fields on the set control what happens if you do not.
await client.Ldap.Library.CheckInAsync("svc-accounts", checkedOut.ServiceAccountName);
```

A library **set** names the accounts available to check out; `CheckOutAsync` hands back exactly
one, held exclusively until `CheckInAsync` releases it or its lease expires. `Ldap.Library.Status`
(not shown above) reports which accounts are `CheckedOut` and which are `Available` at any moment,
if a caller needs to poll rather than just check out and back in.

## The whole program

<!-- docs:sample ldap/complete -->
```csharp
using BastionVaultClient client = new();

try
{
    vault.Server.SetRouteResponse(ConfigRoute, Json(200, "{}"));
    await client.Ldap.WriteConfigAsync(new LdapConfig
    {
        Url = "ldaps://ldap.example.com:636",
        BindDn = "cn=vault,ou=svc,dc=example,dc=com",
        BindPass = new SecretString("bindpassword123"),
    });
    Console.WriteLine("directory configured");

    vault.Server.SetRouteResponse(StaticRoleRoute, Json(200, "{}"));
    await client.Ldap.StaticRoles.WriteAsync("dbadmin", new LdapStaticRole { Username = "dbadmin", RotationPeriod = TimeSpan.FromHours(24) });

    vault.Server.SetRouteResponse(StaticCredRoute, Json(200, StaticCredBody()));
    LdapStaticCred? cred = await client.Ldap.StaticCredAsync("dbadmin");
    Console.WriteLine($"static credential ready for {cred!.Username}");
}
catch (BastionVaultException e)
{
    Console.Error.WriteLine($"{e.Code}: {e.Message} ({e.Hint}); retryable: {e.Retryable}");
    throw;
}
```

## What can go wrong

LDAP carries one requirement of its own — `LDP-001` — and no engine-specific error codes: every
other failure on this surface reaches the caller through the same generic mapping (Appendix B)
that every other route in this SDK shares.

| Code | Meaning | Fix |
|---|---|---|
| `BV-INPUT-001` | `InsecureTls` was set without `AcknowledgeInsecureTls` (`LDP-001`), or another required field was invalid | Set `AcknowledgeInsecureTls` too, or fix the argument the exception names |
| `BV-NOTFOUND-001` | The mount has no config yet, or the named static role or library set does not exist | Check the mount and name, or write it first |
| `BV-AUTHZ-001` | The calling token's policy does not grant this path | Extend the policy shown above |

Handled completely, that is:

<!-- docs:sample ldap/handling-errors -->
```csharp
try
{
    // LDP-001: refused client-side, no request sent - the mirror of the server's own check.
    await client.Ldap.WriteConfigAsync(new LdapConfig { InsecureTls = true });
}
catch (BastionVaultException e)
{
    string remedy = e.Code switch
    {
        ErrorCodes.InputInvalidArgument => "set AcknowledgeInsecureTls too (LDP-001), or fix the argument named in the message",
        ErrorCodes.NotFoundPathNotFound => "check the mount and path; nothing is configured there yet",
        ErrorCodes.AuthzPermissionDenied => "extend the calling token's policy to cover this path",
        _ => "look the code up in the error reference",
    };
    Console.Error.WriteLine($"{e.Code}: {e.Message} - {remedy}");
    throw;
}
```

## Next steps

- **Authentication guide** — obtaining the token this guide assumes you already hold.
- **Secrets (KV) guide** — a second way to store configuration alongside a directory integration.
- **Error reference** — every code, its category, hint and retryability.
