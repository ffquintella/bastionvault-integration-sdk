# Authentication guide (.NET)

**Implements** [`specifications/17-usage-guides.md` guides 2-4](../../specifications/17-usage-guides.md)
(Core), adapted to .NET. The language-neutral behaviour this page relies on is specified in
[05 — authentication](../../specifications/05-authentication.md) (`AUT-001`…`AUT-101`) and
[04 — error model](../../specifications/04-error-model.md) (`ERR-002`). Read those when you want
to know what every SDK must do; read this page when you want the .NET spelling of it.

## The task

Get a token instead of being handed one: log a service in with **AppID** (the `approle` wire
type), log a human in with a username and password, keep whatever token you end up holding
healthy — by hand or with the background `AutoRenew` loop — and revoke it cleanly on shutdown.

Every C# block below is compiled and executed on every build by
`dotnet/BastionVault.IntegrationSdk.DocsSamples`, and checked byte-for-byte against the source
that ran (`DOC-003`). If a block here is wrong, the build is red.

## Prerequisites

| You need | Detail |
|---|---|
| A running server | `https://vault.example.com:8200` throughout this guide, with TLS verified by default |
| An AppID role, **or** a Userpass account | AppID: an operator created the role and delivered a `role_id` (safe to put in config) and a `secret_id` (delivered out of band, e.g. by a pipeline). Userpass: a username and password an operator provisioned |
| Machine binding, if the AppID role requires it | Satisfied by a FerroGate machine token (`bvault ferrogate token --field client_token`) or bypassed on the role (`bypass_machine_binding = true`) |
| A namespace, if the role is namespace-scoped | Set `Namespace` on the client; the SDK adds `X-BastionVault-Namespace` to the login request for you (`AUT-041`) |
| The package | `dotnet add package BastionVault.IntegrationSdk` |

### The policy the example needs

A login itself needs no policy at all — it is unauthenticated on the wire (`TRN-015`), which is
exactly what makes a rejected credential distinguishable from "the policy said no". The one
capability this guide's flows do need is the one the token-hygiene step uses to mint a narrow
child token for a subprocess:

<!-- docs:sample authentication/policy -->
```csharp
// A login is unauthenticated on the wire (TRN-015), so the only path a token in this
// guide's flows needs is the one the token-hygiene step uses to create a child token.
string hcl = new PolicyBuilder()
    .AddPath("auth/token/create", [Capability.Create, Capability.Update])
    .Build();

Console.WriteLine(hcl);
```

which emits:

```hcl
path "auth/token/create" {
  capabilities = ["create", "update"]
}
```

### Namespaces and machine identity

`Namespace` is a client-level setting (see the [configuration reference](configuration.md)); once
set, every request — including a login — carries `X-BastionVault-Namespace`, and a namespace-scoped
AppID role's `403` names the namespace in its hint. **Machine identity** is FerroGate's concern,
not this guide's: `Auth.Ferrogate.RequirementAsync()` tells you whether a role demands one
(`AUT-051`), and obtaining the child token from the local Machine Identity Agent happens outside
the SDK (`AUT-053`) — the SDK only accepts the opaque string and sends it, as `machineToken` on
`AppId.LoginAsync`, or through `Auth.Ferrogate.LoginAsync` for a FerroGate-native flow.

## Step 1 — Authenticate a service with AppID

<!-- docs:sample authentication/appid-login -->
```csharp
// No token header goes out on this call: the role_id/secret_id pair is the credential.
// A namespace-scoped role needs Namespace configured on the client too (AUT-041); the
// SDK adds X-BastionVault-Namespace for you.
AuthInfo auth;
try
{
    auth = await client.Auth.AppId.LoginAsync("app-role", new SecretString("s.FAKEsecretid"));
}
catch (BastionVaultException e) when (e.Code == ErrorCodes.AuthInvalidAppIdCredentials)
{
    Console.Error.WriteLine("bad role_id/secret_id, or the secret_id was already used up");
    throw;
}
catch (BastionVaultException e) when (e.Code == ErrorCodes.AuthAppIdMachineBinding)
{
    Console.Error.WriteLine($"machine binding: {e.Hint}");
    throw;
}
catch (BastionVaultException e) when (e.Code == ErrorCodes.AuthzPermissionDenied)
{
    // A 403 on this path is gating, never a bad credential (AUT-041): namespace,
    // source IP/CIDR, or machine binding. The hint names which.
    Console.Error.WriteLine($"credentials OK but gated: {e.Hint}");
    throw;
}

Console.WriteLine($"token TTL {auth.LeaseDuration}, policies [{string.Join(", ", auth.Policies)}]");
if (auth.EnvironmentScope.Scoped)
{
    // AUT-044: this token must pass `env` on every KV v2 call - the secrets guide's
    // BV-KV-009 - and this is where a caller learns it must.
    Console.WriteLine($"this token must pass env= one of [{string.Join(", ", auth.EnvironmentScope.SecretGlobs)}]");
}
```

### What goes over the wire

```http
POST /v1/auth/approle/login HTTP/1.1
Host: vault.example.com:8200
X-BastionVault-Namespace: dti/esi
Content-Type: application/json

{"role_id":"app-role","secret_id":"s.FAKEsecretid"}
```

```json
{
  "renewable": true,
  "lease_id": "",
  "lease_duration": 1200,
  "auth": {
    "client_token": "s.FAKEchildtoken",
    "policies": ["default"],
    "metadata": {},
    "lease_duration": 1200,
    "renewable": true
  },
  "data": null
}
```

Notice there is no `X-BastionVault-Token` request header (`TRN-015`): a login authenticates
*with* credentials, it does not need to already hold one. `AuthInfo.EnvironmentScope` is derived
from `auth.metadata`'s `approle_env_*` keys (`AUT-044`); when it is scoped, every KV v2 call this
token makes must pass `env`, or the SDK refuses client-side with `BV-KV-009` before a request ever
goes out.

## Step 2 — Human login with username and password

<!-- docs:sample authentication/userpass-login -->
```csharp
// AUT-011: a rejected credential comes back as HTTP 200 with data.error, never
// 401/403. Switch on Code below - never parse ServerMessage yourself.
AuthInfo auth;
try
{
    auth = await client.Auth.Userpass.LoginAsync("alice", new SecretString("s.FAKEpassword"));
}
catch (BastionVaultException e)
{
    string reason = e.Code switch
    {
        ErrorCodes.AuthInvalidCredentials => "wrong username or password",
        ErrorCodes.AuthAccountDisabled => "account disabled - contact an admin",
        ErrorCodes.AuthAccountLocked => $"locked; retry in {e.Details["retry_after_secs"]}s",
        ErrorCodes.AuthTotpRequired => "a TOTP code is required; prompt for it and retry",
        ErrorCodes.AuthInvalidTotp => "invalid TOTP code",
        ErrorCodes.AuthPasswordLoginDisabled => "use your FIDO2 security key instead",
        _ => throw e,
    };
    Console.Error.WriteLine(reason);
    throw;
}

Console.WriteLine($"logged in as alice, policies [{string.Join(", ", auth.Policies)}]");
```

A rejected credential is not a transport failure: the server answers with **HTTP 200** and
`data.error` (`AUT-011`), and the SDK is what turns that into a distinct, switchable code for
each of the six account states the specification recognises — wrong password, disabled, locked,
TOTP required, invalid TOTP, and password login disabled in favour of a FIDO2 key. A locked
account (`BV-AUTH-006`) is not fixed by retrying; the SDK never retries it automatically, and
neither should you.

Optional: `client.Auth.PersistToken()` writes `~/.vault-token` in plaintext, so the `bvault` CLI
can reuse the session — but only the plaintext form; the CLI's own encrypted format
(`BVTOK1:`-prefixed) is not one the SDK can read (`BV-CONFIG-010`).

## Step 3 — Keep the token healthy: lookup and renew

<!-- docs:sample authentication/token-hygiene -->
```csharp
// RemainingTtl is computed by the SDK; the wire's own `ttl` field is always 0 and is
// never exposed (AUT-014).
TokenInfo info = await client.Auth.Token.LookupSelfAsync();
Console.WriteLine($"{info.DisplayName}: policies [{string.Join(", ", info.Policies)}], remaining {info.RemainingTtl}");

if (info.RemainingTtl is { } remaining && remaining < TimeSpan.FromMinutes(5))
{
    // AUT-080: there is no `renew-self` path; RenewSelf posts to renew/{currentToken}.
    AuthInfo renewed = await client.Auth.Token.RenewSelfAsync(increment: 3600);
    Console.WriteLine($"renewed: new lease {renewed.LeaseDuration}");
}
```

`RemainingTtl` is the SDK's own computation — `creation_time + creation_ttl − now` — because the
wire's `ttl` field is always `0` and carries no information (`AUT-014`). There is no
`renew-self` endpoint on this server: `RenewSelf` posts to `auth/token/renew/{currentToken}` with
the live token in the path, and that path is redacted everywhere the SDK reports it (`AUT-080`).

### Auto-renew, for a service that runs longer than one TTL

<!-- docs:sample authentication/auto-renew -->
```csharp
// AUT-090..AUT-095: configured once, at construction. The SDK schedules RenewSelf at
// IssuedAt + LeaseDuration x RenewAtFraction, retries a failure with backoff up to
// MaxConsecutiveFailures, and gives a Login-sourced token one fresh login before
// giving up.

// Nothing runs until a login issues a credential, so this sample shows configuration
// only; the schedule itself runs on real wall-clock time and is exercised by the SDK's
// own test suite, which drives it with a clock this public API does not expose.
using BastionVaultClient client = new(new BastionVaultClientOptions
{
    AutoRenew = new AutoRenewPolicy
    {
        Enabled = true,
        RenewAtFraction = 0.66,
        MaxConsecutiveFailures = 5,
        OnRenewed = renewal => Console.WriteLine($"renewed, new TTL {renewal.Auth?.LeaseDuration}"),
        OnFailed = failure => Console.Error.WriteLine($"renewal attempt failed: {failure.Error?.Code}"),
        OnStopped = reason => Console.Error.WriteLine($"AutoRenew stopped: {reason}"),
    },
});

Console.WriteLine(
    $"AutoRenew enabled: {client.Config.AutoRenew.Enabled}, "
    + $"renews at {client.Config.AutoRenew.RenewAtFraction:P0} of the lease");
```

Enabling `AutoRenew` at construction hands the schedule to the SDK: once a login (any of the
above, or a declarative `TokenSource.Login`) issues a credential, a background loop renews at
`IssuedAt + LeaseDuration × RenewAtFraction`, never sooner than `MinInterval` after the previous
renewal (`AUT-090`, `AUT-091`). A failed renewal retries with exponential backoff — starting at
1 s, capped at a quarter of the remaining TTL — up to `MaxConsecutiveFailures`, then the loop
stops and calls `OnStopped` (`AUT-092`); it stops immediately, with no retry, on
`BV-AUTHZ-001`, `BV-AUTH-015`, or a sealed server. A `Login`-sourced token gets exactly one fresh
login after the retries are exhausted before the loop truly gives up (`AUT-093`). Disposing the
client cancels the loop; a batch or non-renewable token makes it log once, at info level, and do
nothing (`AUT-095`).

⚠️ **A live server has shown one gap in this contract, not this guide's to fix.** Renewing a
token that came from `auth/approle/login` currently answers `204 No Content` on some servers
instead of the `200` with a full `auth` envelope this section describes (`DR-0021` finding F1);
the fix is tracked and landing in its own change. The sample above only configures `AutoRenew`
and shuts down cleanly — it does not depend on that fix, because it never drives a renewal to
completion against a real server; the schedule's actual firing is exercised by the SDK's own test
suite under an injectable clock, not by a documentation sample.

## Step 4 — Issue a narrow child token, then revoke on shutdown

<!-- docs:sample authentication/token-hygiene-create-and-revoke -->
```csharp
// AUT-081: a reserved meta key (spiffe_id, machine_id, entity_id, ...) is refused
// client-side before anything is sent; "purpose" below is not reserved.
AuthInfo child = await client.Auth.Token.CreateAsync(new CreateTokenRequest
{
    Policies = ["app-readonly"],
    Ttl = TimeSpan.FromMinutes(15),
    NumUses = 20,
    Meta = new Dictionary<string, string> { ["purpose"] = "batch-job" },
});

Console.WriteLine($"child token TTL {child.LeaseDuration}, num_uses limited");

// ... hand child.ClientToken to the subprocess. Then, on this process's own shutdown:
await client.Auth.Token.RevokeSelfAsync();
```

`Meta` keys are checked client-side before anything is sent: `spiffe_id`, `machine_id`,
`entity_id`, and a dozen others the server itself reserves are rejected with `BV-INPUT-009`
rather than reaching the wire and being refused there (`AUT-081`). `RevokeSelf` always clears the
local token, even for a root-policy token, whose revoke the server accepts but only logs as a
logout (`AUT-083`) — the guide's client is left with no token either way.

## The whole program

<!-- docs:sample authentication/complete -->
```csharp
using BastionVaultClient client = new(new BastionVaultClientOptions { Namespace = "dti/esi" });

try
{
    AuthInfo auth = await client.Auth.AppId.LoginAsync("app-role", new SecretString("s.FAKEsecretid"));
    Console.WriteLine($"authenticated, TTL {auth.LeaseDuration}");

    TokenInfo info = await client.Auth.Token.LookupSelfAsync();
    if (info.RemainingTtl is { } remaining && remaining < TimeSpan.FromMinutes(5))
    {
        _ = await client.Auth.Token.RenewSelfAsync(increment: 3600);
        Console.WriteLine("renewed");
    }

    // ... do the service's actual work here, using `client` for every call ...

    await client.Auth.Token.RevokeSelfAsync();
    Console.WriteLine("revoked on shutdown");
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
| `BV-AUTH-010` | AppID: bad `role_id`/`secret_id`, or the `secret_id` was already used up | Issue a fresh `secret_id` with `Auth.AppId.GenerateSecretIdAsync` |
| `BV-AUTH-011` | AppID: the role requires machine binding and none was presented | Supply a FerroGate machine token, or set `bypass_machine_binding` on the role |
| `BV-AUTHZ-001` | AppID login gated (namespace, source IP/CIDR, machine binding); or a later request's policy denies it | On a login, check the hint for which gate; otherwise check the token's policy |
| `BV-AUTH-004`…`BV-AUTH-009` | Userpass: wrong password, disabled, locked, TOTP required, invalid TOTP, or FIDO2-only | Switch on `Code`; never retry a locked account automatically |
| `BV-AUTH-015` | `RenewSelf`/`Renew` on a token the server will not extend | The token is unrenewable or already expired; log in again |
| `BV-NOTFOUND-006` | `Lookup` of a token that no longer exists | It was revoked or expired; obtain a new one |

Handled completely, that is:

<!-- docs:sample authentication/handling-errors -->
```csharp
try
{
    _ = await client.Auth.AppId.LoginAsync("app-role", new SecretString("s.FAKEsecretid"));
}
catch (BastionVaultException e)
{
    string remedy = e.Code switch
    {
        ErrorCodes.AuthInvalidAppIdCredentials => "check role_id/secret_id; a secret_id may be single-use and already spent",
        ErrorCodes.AuthAppIdMachineBinding => "supply a FerroGate machine token, or set bypass_machine_binding on the role",
        ErrorCodes.AuthzPermissionDenied => "gated: check namespace, source IP/CIDR, and machine binding",
        ErrorCodes.AuthInvalidCredentials => "wrong username or password",
        ErrorCodes.AuthAccountLocked => "locked; do not retry automatically",
        ErrorCodes.AuthTokenNotRenewable => "the token is not renewable, or has already expired",
        ErrorCodes.NotFoundTokenNotFound => "the token being looked up no longer exists",
        _ => "look the code up in the error reference",
    };
    Console.Error.WriteLine($"{e.Code}: {e.Message} - {remedy}");
    throw;
}
```

## Next steps

- **Secrets (KV) guide** — what to do with the token this guide just got you: writing, versions,
  check-and-set, soft delete versus destroy, per-environment secrets, and batch reads.
- **Getting started** — the shortest path from a token to a secret.
- **Configuration reference** — every setting, including `Namespace` and `AutoRenew`, its
  environment variable, default, precedence and validation error code.
- **Error reference** — every code, its category, hint and retryability.
