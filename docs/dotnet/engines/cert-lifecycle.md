# Cert lifecycle engine (.NET)

Section 17 carries no dedicated usage guide for Cert lifecycle; this page is built directly from
[12 — Other engines and identity](../../../specifications/12-other-engines-and-identity.md)'s Cert
lifecycle table, following the same task → prerequisites → steps → complete example → what can go
wrong → next steps structure every other engine guide on this site uses (`DOC-010`). This area
carries no requirement ID of its own — every MUST governing it is the generic Shape A envelope and
standard error mapping every route in this SDK already shares (DR-0017), except the one
message-recognition row `RenewAsync`'s remarks call out below.

## The task

Register a **target** — a place a certificate should live and how it should be issued — read back
its current renewal state, force a renewal on demand, and read every target's state in one bulk
call instead of one `State` per target. This engine automates what the [PKI engine
guide](pki.md) leaves manual: PKI issues and revokes on request, while Cert lifecycle watches
targets and renews them before `renew_before` runs out.

Every C# block below is compiled and executed on every build by
`dotnet/BastionVault.IntegrationSdk.DocsSamples`, and checked byte-for-byte against the source
that ran (`DOC-003`). If a block here is wrong, the build is red.

## Prerequisites

| You need | Detail |
|---|---|
| A running server | `https://vault.example.com:8200` throughout this guide |
| A token | Carrying the policy below. The [authentication guide](../authentication.md) covers obtaining one |
| A Cert lifecycle mount | `cert-lifecycle/` — the default `mount` on every `Client.CertLifecycle` member |
| A PKI mount to renew against | `pki/` by default (`Target.PkiMount`); see the [PKI engine guide](pki.md) |
| The package | `dotnet add package BastionVault.IntegrationSdk` |

### The policy the example needs

<!-- docs:sample cert-lifecycle/policy -->
```csharp
string hcl = new PolicyBuilder()
    .AddPath("cert-lifecycle/targets/api-example-com", [Capability.Create, Capability.Read])
    .AddPath("cert-lifecycle/state/api-example-com", [Capability.Read])
    .AddPath("cert-lifecycle/renew/api-example-com", [Capability.Update])
    .AddPath("cert-lifecycle/targets-info", [Capability.Read])
    .AddPath("cert-lifecycle/scheduler/config", [Capability.Read, Capability.Update])
    .Build();

Console.WriteLine(hcl);
```

which emits:

```hcl
path "cert-lifecycle/targets/api-example-com" {
  capabilities = ["create", "read"]
}

path "cert-lifecycle/state/api-example-com" {
  capabilities = ["read"]
}

path "cert-lifecycle/renew/api-example-com" {
  capabilities = ["update"]
}

path "cert-lifecycle/targets-info" {
  capabilities = ["read"]
}

path "cert-lifecycle/scheduler/config" {
  capabilities = ["read", "update"]
}
```

## Step 1 — Define a target, then read it back

<!-- docs:sample cert-lifecycle/write-and-read-target -->
```csharp
await client.CertLifecycle.WriteTargetAsync("api-example-com", new Target
{
    Kind = "file",
    Address = "/etc/ssl/api.example.com",
    PkiMount = "pki",
    RoleRef = "web",
    CommonName = "api.example.com",
    AltNames = ["www.example.com"],
    Ttl = TimeSpan.FromHours(72),
    KeyPolicy = "rotate",
    RenewBefore = TimeSpan.FromHours(168),
});

Target? target = await client.CertLifecycle.ReadTargetAsync("api-example-com");
Console.WriteLine($"{target!.CommonName} renews {target.RenewBefore} before expiry");
```

### What goes over the wire

```http
POST /v1/cert-lifecycle/targets/api-example-com HTTP/1.1
Host: vault.example.com:8200
X-BastionVault-Token: s.FAKEtoken
Content-Type: application/json

{"kind":"file","address":"/etc/ssl/api.example.com","pki_mount":"pki","role_ref":"web","common_name":"api.example.com","alt_names":["www.example.com"],"ttl":259200,"key_policy":"rotate","renew_before":604800}
```

```json
{
  "data": {
    "kind": "file",
    "address": "/etc/ssl/api.example.com",
    "pki_mount": "pki",
    "role_ref": "web",
    "common_name": "api.example.com",
    "alt_names": ["www.example.com"],
    "ttl": 259200,
    "key_policy": "rotate",
    "renew_before": 604800
  }
}
```

Every duration field on `Target` — `Ttl` and `RenewBefore` included — is unquoted integer seconds
on the wire, never a Go-style duration string; the C# side is `TimeSpan`, and the SDK does the
conversion both ways.

## Step 2 — Check a target's state, then force a renewal

<!-- docs:sample cert-lifecycle/state-and-renew -->
```csharp
TargetState? state = await client.CertLifecycle.StateAsync("api-example-com");
Console.WriteLine($"current serial {state!.CurrentSerial}, next attempt {state.NextAttempt:O}");

// An unknown target name reaches the caller as BV-NOTFOUND-008 (the shared mapping).
await client.CertLifecycle.RenewAsync("api-example-com");
Console.WriteLine("renewal requested");
```

`TargetState.LastError`/`FailureCount` are what to check after a renewal you expected to happen did
not: a target that has never been attempted reports every timestamp `null` and `FailureCount` `0`,
not an exception.

## Step 3 — List every target's state in bulk, and configure the scheduler

<!-- docs:sample cert-lifecycle/bulk-and-scheduler -->
```csharp
// One request, however many targets exist - never one State() call per target.
Page<Target> page = await client.CertLifecycle.ListTargetsInfoAsync(limit: 100);
foreach (Target target in page.Records)
{
    Console.WriteLine($"{target.CommonName}: renews before {target.RenewBefore}");
}

await client.CertLifecycle.WriteSchedulerConfigAsync(new SchedulerConfig
{
    Enabled = true,
    TickIntervalSeconds = 60,
    BaseBackoffSeconds = 30,
    MaxBackoffSeconds = 3600,
});
```

`ListTargetsInfoAsync` is pinned to `/v2` regardless of `mount` (matching `Pki.ListCertificatesInfoAsync`'s
own pinning); `ListTargetsInfoAllAsync` (not shown above) iterates every page for you when the
mount holds more targets than fit in one call. `SchedulerConfig.ClientToken` is write-only — a
subsequent `ReadSchedulerConfigAsync` reports whether one is configured through `ClientTokenSet`,
never the token itself.

## The whole program

<!-- docs:sample cert-lifecycle/complete -->
```csharp
using BastionVaultClient client = new();

try
{
    vault.Server.SetRouteResponse(TargetRoute, Json(200, TargetBody()));
    await client.CertLifecycle.WriteTargetAsync("api-example-com", new Target
    {
        Address = "/etc/ssl/api.example.com",
        RoleRef = "web",
        CommonName = "api.example.com",
    });
    Console.WriteLine("target defined");

    vault.Server.SetRouteResponse(RenewRoute, Json(200, "{}"));
    await client.CertLifecycle.RenewAsync("api-example-com");
    Console.WriteLine("renewal requested");
}
catch (BastionVaultException e)
{
    Console.Error.WriteLine($"{e.Code}: {e.Message} ({e.Hint}); retryable: {e.Retryable}");
    throw;
}
```

## What can go wrong

Cert lifecycle carries no requirement ID of its own (DR-0017) and no engine-specific error codes:
every failure on this surface reaches the caller through the same generic mapping (Appendix B) that
every other route in this SDK shares, with one named exception below.

| Code | Meaning | Fix |
|---|---|---|
| `BV-NOTFOUND-008` | `Renew` named a target that does not exist (the exact server message `` cert-lifecycle: target `x` not found `` is catalogued) | Check the target name, or `WriteTargetAsync` it first |
| `BV-NOTFOUND-001` | The target, or the scheduler config, does not exist yet | Write it first |
| `BV-AUTHZ-001` | The calling token's policy does not grant this path | Extend the policy shown above |

Handled completely, that is:

<!-- docs:sample cert-lifecycle/handling-errors -->
```csharp
try
{
    await client.CertLifecycle.RenewAsync("missing-target");
}
catch (BastionVaultException e)
{
    string remedy = e.Code switch
    {
        ErrorCodes.NotFoundResourceNotFound => "check the target name, or WriteTargetAsync it first",
        ErrorCodes.AuthzPermissionDenied => "extend the calling token's policy to cover this path",
        _ => "look the code up in the error reference",
    };
    Console.Error.WriteLine($"{e.Code}: {e.Message} - {remedy}");
    throw;
}
```

## Next steps

- **Authentication guide** — obtaining the token this guide assumes you already hold.
- **PKI engine guide** — the certificate authority `RoleRef`/`PkiMount` point back into.
- **Notifications engine guide** — a typical destination for a renewal failure alert.
- **Error reference** — every code, its category, hint and retryability.
