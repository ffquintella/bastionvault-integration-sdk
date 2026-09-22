# Getting started — read your first secret (.NET)

**Implements** [`specifications/17-usage-guides.md` § Guide 1](../../specifications/17-usage-guides.md)
(Core), adapted to .NET. The language-neutral behaviour this page relies on is specified in
[07 — KV engine](../../specifications/07-kv-engine.md) (`KV2-001`, `KV2-004`, `KV2-006`,
`KV2-020`, `KV2-030`), [06 — system API](../../specifications/06-system-api.md) (`SYS-001`),
[02 — client configuration](../../specifications/02-client-configuration.md) (`CFG-001`,
`CFG-002`, `CFG-013`) and [04 — error model](../../specifications/04-error-model.md)
(`ERR-002`, `ERR-005`). Read those when you want to know what every SDK must do; read this page
when you want the .NET spelling of it.

## The task

Point a .NET application at a BastionVault server, confirm the node is Active, read the
username and password stored at `secret/app/db`, and fail usefully when any of that goes wrong.

Every C# block below is compiled and executed on every build by
`dotnet/BastionVault.IntegrationSdk.DocsSamples`, and checked byte-for-byte against the source
that ran (`DOC-003`). If a block here is wrong, the build is red.

## Prerequisites

| You need | Detail |
|---|---|
| A running server | `bvault server --config config/dev.hcl` for a dev node. This guide uses `https://vault.example.com:8200` throughout |
| The server's CA certificate | The SDK verifies TLS by default and there is no supported way to reach a server over plain HTTP in production. Point `BASTIONVAULT_CACERT` at the PEM bundle |
| A token | A service token such as `s.FAKEtoken`, carrying the policy below. This guide assumes you already hold one; obtaining one from an **AppID** login (wire type `approle`), from Userpass, or from a machine identity is the authentication guide's subject |
| A KV v2 mount | `secret/`, holding `app/db` with `username` and `password` keys |
| The package | `dotnet add package BastionVault.IntegrationSdk` |

### The policy the example needs

`secret/data/app/*` is the *logical path* of a KV v2 secret: the `data/` infix is part of the
path the policy must name, not part of the path you pass to the SDK. `sys/health` is listed
separately because the health call is unauthenticated on the wire but still appears in a
capability check.

Build it with `PolicyBuilder` rather than by hand:

<!-- docs:sample getting-started/policy -->
```csharp
// The least-privilege policy this guide's token needs. PolicyBuilder emits the HCL
// deterministically, so the document below, the policy you review, and the policy you
// upload with Sys.WritePolicy cannot drift apart.
string hcl = new PolicyBuilder()
    .AddPath("secret/data/app/*", [Capability.Read])
    .AddPath("sys/health", [Capability.Read])
    .Build();

Console.WriteLine(hcl);

// No error handling to show: PolicyBuilder is pure client-side string building and
// performs no I/O. Uploading the result with Sys.WritePolicy can fail; that call, and
// its error handling, belong to the security guide.
```

which emits:

```hcl
path "secret/data/app/*" {
  capabilities = ["read"]
}

path "sys/health" {
  capabilities = ["read"]
}
```

Upload it with `Sys.WritePolicy`, then issue the token against it. A token that is missing the
first block fails the read with `BV-AUTHZ-001`, which the last section explains.

## Step 1 — configure the client

<!-- docs:sample getting-started/configure -->
```csharp
// BASTIONVAULT_ADDR, BASTIONVAULT_TOKEN and BASTIONVAULT_CACERT carry the configuration;
// no other setting is required to reach https://vault.example.com:8200.
//
// Building the client is two steps, and both are deliberate. The first client resolves
// and validates the configuration - address, CA bundle, token, namespace - without
// sending anything. The transport is then constructed from that resolved ClientConfig,
// and the second client, the one you keep, is the one that can make requests. A client
// built with no transport raises InvalidOperationException on its first call, not a
// BV-* error, so do not skip the second step.
using BastionVaultClient configuration = new();
using HttpClientTransport transport = new(configuration.Config);
using BastionVaultClient client = new(new BastionVaultClientOptions { Transport = transport });

Console.WriteLine($"address:   {client.Config.Address}");
Console.WriteLine($"namespace: {(client.Config.Namespace.Length == 0 ? "<root>" : client.Config.Namespace)}");
Console.WriteLine($"token:     {(client.Config.Token.HasValue ? "configured" : "absent")}");

// Error handling is deliberately elided here and shown complete in "The whole program"
// below: construction raises BastionVaultException with BV-CONFIG-001 for a malformed
// address and BV-TRANSPORT-003 for a CA bundle that cannot be read.
```

`BASTIONVAULT_ADDR`, `BASTIONVAULT_TOKEN`, `BASTIONVAULT_CACERT` and `BASTIONVAULT_NAMESPACE`
are the environment variables the client reads; `VAULT_*` are accepted as fallbacks. An empty
namespace means the root namespace. The full precedence order, every setting, and its validation
error code are the configuration reference's subject, not this page's.

## Step 2 — check the node is Active

<!-- docs:sample getting-started/health -->
```csharp
// Health is unauthenticated, so it answers even before a token is configured, and it
// reports a standby, sealed or uninitialised node as a *state* rather than as a failure:
// the request succeeded, and the node told you what it is.
HealthStatus health;
try
{
    health = await client.Sys.HealthAsync();
}
catch (BastionVaultException e)
{
    Console.Error.WriteLine($"{e.Code}: {e.Message} ({e.Hint}); retryable: {e.Retryable}");
    throw;
}

if (health.State != HealthState.Active)
{
    Console.Error.WriteLine($"vault at {client.Config.Address} is {health.State}, not Active");
    return;
}

Console.WriteLine($"node is Active (HTTP {health.StatusCode})");
```

Health is the one call that answers without a token. A standby, sealed or uninitialised node is
reported to you as a `HealthState`, not as a thrown error — the request *succeeded*, and the
node told you what it is. Only a node that cannot be reached at all raises.

## Step 3 — read the secret

<!-- docs:sample getting-started/read-secret -->
```csharp
// GET /v1/secret/data/app/db. `mount` is the mount point and `path` is the path inside
// it; the SDK inserts KV v2's `data/` infix for you.
KvV2Secret? secret;
try
{
    secret = await client.Kv.V2.ReadSecretAsync("app/db", mount: "secret");
}
catch (BastionVaultException e)
{
    Console.Error.WriteLine($"{e.Code}: {e.Message} ({e.Hint}); retryable: {e.Retryable}");
    throw;
}

if (secret is null)
{
    // Nothing at that path, or every version of it destroyed. An absence, not an error.
    Console.Error.WriteLine("secret/app/db holds nothing");
    return;
}

if (secret.State == KvV2SecretState.SoftDeleted || secret.Data is null)
{
    // The version exists but carries no data: it was soft-deleted, and stays that way
    // until someone undeletes it.
    Console.Error.WriteLine($"secret/app/db was deleted at {secret.Metadata.DeletionTime:O}");
    return;
}

string username = secret.Data["username"].GetString()!;
string password = secret.Data["password"].GetString()!;

// Log what you read, never what you read out: this SDK never writes secret material to a
// log, and the value you just pulled out of `Data` is now yours to keep out of one.
Console.WriteLine($"version {secret.Metadata.Version}, written by {secret.Metadata.Username}");
Console.WriteLine($"username {username}, password {password.Length} characters");
```

Two absences are deliberately not errors, and both must be handled:

- **`null`** — nothing at that logical path, or every version of it destroyed.
- **`KvV2SecretState.SoftDeleted`** — the version exists, but its data is withheld until someone
  undeletes it. `Metadata.DeletionTime` tells you when it went.

`Data` is a dictionary of `JsonElement`, so a value keeps the JSON type the server stored;
`GetString()` is the right accessor for the string fields this example reads.

### What goes over the wire

The read above is one request. Correlate it with your server's audit log by this shape:

```http
GET /v1/secret/data/app/db HTTP/1.1
Host: vault.example.com:8200
X-BastionVault-Token: s.FAKEtoken
Accept: application/json
```

```json
{
  "renewable": false,
  "lease_id": "",
  "lease_duration": 0,
  "auth": null,
  "data": {
    "data": {
      "username": "admin",
      "password": "p2"
    },
    "metadata": {
      "version": 2,
      "created_time": "2026-09-13T10:00:00Z",
      "deletion_time": "",
      "destroyed": false,
      "username": "alice",
      "operation": "update",
      "resolved_env": null,
      "available_envs": ["prod"]
    }
  }
}
```

The outer `data` is the response envelope; the inner `data` is your secret and `metadata`
describes the version. That double nesting is KV v2's, not the SDK's, and it is why the
mount (`secret`) and the path inside it (`app/db`) are separate arguments: the SDK inserts
`data/` between them for you (`KV2-030`).

## The whole program

<!-- docs:sample getting-started/complete -->
```csharp
using BastionVaultClient configuration = new();
using HttpClientTransport transport = new(configuration.Config);
using BastionVaultClient client = new(new BastionVaultClientOptions { Transport = transport });

try
{
    HealthStatus health = await client.Sys.HealthAsync();
    if (health.State != HealthState.Active)
    {
        Console.Error.WriteLine($"vault is {health.State}, not Active");
        return;
    }

    KvV2Secret? secret = await client.Kv.V2.ReadSecretAsync("app/db", mount: "secret");
    if (secret is null)
    {
        Console.Error.WriteLine("secret/app/db holds nothing");
        return;
    }

    if (secret.State == KvV2SecretState.SoftDeleted || secret.Data is null)
    {
        Console.Error.WriteLine($"secret/app/db was deleted at {secret.Metadata.DeletionTime:O}");
        return;
    }

    string username = secret.Data["username"].GetString()!;
    Console.WriteLine($"version {secret.Metadata.Version} of secret/app/db, username {username}");
}
catch (BastionVaultException e)
{
    // Every failure this program can meet arrives as this one type, carrying a stable
    // code, a hint, and whether retrying could ever help.
    Console.Error.WriteLine($"{e.Code}: {e.Message} ({e.Hint}); retryable: {e.Retryable}");
    throw;
}
```

## What can go wrong

Every failure arrives as one type, `BastionVaultException`, carrying a stable `Code`, a `Hint`,
and `Retryable`. Switch on the code; never parse the message.

| Code | Meaning | Fix |
|---|---|---|
| `BV-CONFIG-001` | The address is missing or malformed | Set `BASTIONVAULT_ADDR` to `https://host:8200`. IPv6 literals must be bracketed |
| `BV-TRANSPORT-003` | TLS verification failed | Point `BASTIONVAULT_CACERT` at the server's CA bundle. Do not reach for `TlsSkipVerify` |
| `BV-AUTH-001` | No token is configured | Set `BASTIONVAULT_TOKEN`, or log in |
| `BV-AUTHZ-001` | The policy denies `read` on `secret/data/app/db`, or the token is invalid or expired | Check the policy above is attached, then `Sys.CapabilitiesSelf(["secret/data/app/db"])` |
| `BV-SERVER-001` | The server is sealed | An operator must unseal it. `Retryable` is false: retrying cannot help |
| `BV-NOTFOUND-002` | The `secret/` mount does not exist | `Sys.ListMounts()` to see what is mounted |

Handled completely, that is:

<!-- docs:sample getting-started/handling-errors -->
```csharp
try
{
    KvV2Secret? secret = await client.Kv.V2.ReadSecretAsync("app/db", mount: "secret");
    Console.WriteLine(secret is null ? "no such secret" : $"version {secret.Metadata.Version}");
}
catch (BastionVaultException e)
{
    // One exception type, one stable code, one hint. Switch on `Code`, never on the
    // message: the code is the contract and is identical across all three SDKs.
    string remedy = e.Code switch
    {
        ErrorCodes.ConfigInvalidAddress => "set BASTIONVAULT_ADDR to https://host:8200",
        ErrorCodes.TransportTlsError => "point BASTIONVAULT_CACERT at the server's CA bundle",
        ErrorCodes.AuthNoToken => "set BASTIONVAULT_TOKEN, or log in first",
        ErrorCodes.AuthzPermissionDenied => "grant read on secret/data/app/db, or renew an expired token",
        ErrorCodes.ServerSealed => "an operator must unseal the vault",
        ErrorCodes.NotFoundMountNotFound => "the secret/ mount does not exist on this server",
        _ => "look the code up in the error reference",
    };

    Console.Error.WriteLine($"{e.Code}: {e.Message} - {remedy} (retryable: {e.Retryable})");
    throw;
}
```

One trap worth naming: a client constructed without a transport raises
`InvalidOperationException`, not a `BV-*` code, on its first request. That is a construction
mistake rather than a server condition — see step 1.

## Next steps

- **Authentication guide** — obtaining a token instead of being handed one: AppID, Userpass,
  machine identity, token renewal.
- **Secrets (KV) guide** — writing, versions, check-and-set, soft delete versus destroy,
  per-environment overrides, and reading many secrets in one request.
- **Configuration reference** — every setting, its environment variable, default, precedence and
  validation error code.
- **Error reference** — every code, its category, hint and retryability.
- **Resilience and operations guide** — cluster discovery, retries, the rate gate, and why a
  fan-out of small reads is the one thing to avoid.
