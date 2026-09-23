# Secrets (KV) guide (.NET)

**Implements** [`specifications/17-usage-guides.md` guides 5-7](../../specifications/17-usage-guides.md)
(Core), adapted to .NET. The language-neutral behaviour this page relies on is specified in
[07 — KV engine](../../specifications/07-kv-engine.md) (`KV-001`…`KV-013`, `KV2-001`…`KV2-030`)
and [14 — batch and request efficiency](../../specifications/14-batch-and-request-efficiency.md)
(`BAT-001`…`BAT-008`). Read those when you want to know what every SDK must do; read this page
when you want the .NET spelling of it.

## The task

Write a KV v2 secret, version it with check-and-set, soft-delete and destroy it, tell the
difference between the two, keep a different value per environment on the same secret, and read
several secrets in one request instead of fanning out one at a time.

Every C# block below is compiled and executed on every build by
`dotnet/BastionVault.IntegrationSdk.DocsSamples`, and checked byte-for-byte against the source
that ran (`DOC-003`). If a block here is wrong, the build is red.

## Prerequisites

| You need | Detail |
|---|---|
| A running server | `https://vault.example.com:8200` throughout this guide |
| A token | Carrying the policy below. The [authentication guide](authentication.md) covers obtaining one |
| A KV v2 mount | `secret/` — every new `secret/`-style mount is `kv-v2`; see below if yours is a v1 mount instead |
| The package | `dotnet add package BastionVault.IntegrationSdk` |

### v1 vs v2, and which mount you have

`Kv.V1` is a flat store: `Write` overwrites the whole object, there is no version history, no
check-and-set, and no per-environment overrides (`KV1-004`). `Kv.V2` — this guide's subject — adds
versioning, soft delete, CAS, and environments, at the cost of a `data/` infix in every logical
path. If you are not sure which one a mount is, `Kv.DetectVersionAsync(mount)` calls
`Sys.MountTypeOf` and tells you (`KV-001`); a version-agnostic `Kv.ReadSecret`-style façade is not
what this SDK offers, so a v1 mount always goes through `Kv.V1`, not through a fallback path of
`Kv.V2`.

### The policy the example needs

`secret/data/app/*` is the *logical path* the read/write/delete capabilities need; the `metadata/`,
`undelete/` and `destroy/` groups are separate paths with separate capabilities, because they are
separate server routes (`07 — KV v2`'s path groups table):

<!-- docs:sample secrets-kv/policy -->
```csharp
// "data/" is part of the logical path a policy names; the SDK inserts it for you when
// you call Kv.V2 with just the mount and the path inside it.
string hcl = new PolicyBuilder()
    .AddPath("secret/data/app/*", [Capability.Create, Capability.Read, Capability.Update, Capability.Delete])
    .AddPath("secret/metadata/app/*", [Capability.Read, Capability.List, Capability.Delete])
    .AddPath("secret/undelete/app/*", [Capability.Update])
    .AddPath("secret/destroy/app/*", [Capability.Update])
    .Build();

Console.WriteLine(hcl);
```

which emits:

```hcl
path "secret/data/app/*" {
  capabilities = ["create", "read", "update", "delete"]
}

path "secret/metadata/app/*" {
  capabilities = ["read", "list", "delete"]
}

path "secret/undelete/app/*" {
  capabilities = ["update"]
}

path "secret/destroy/app/*" {
  capabilities = ["update"]
}
```

## Step 1 — Write, version, and check-and-set a secret

<!-- docs:sample secrets-kv/write-and-cas -->
```csharp
KvV2VersionMetadata v1 = await client.Kv.V2.WriteSecretAsync("app/db", Data(("username", "admin"), ("password", "p1")));

vault.Server.SetRouteResponse(DataRoute, Json(200, WriteBody(version: 2)));
// Cas: v1.Version means "only write if the current version is still v1" - optimistic
// concurrency, no server-side lock held between the read and this write.
KvV2VersionMetadata v2 = await client.Kv.V2.WriteSecretAsync(
    "app/db", Data(("username", "admin"), ("password", "p2")), options: new KvWriteOptions { Cas = v1.Version });

vault.Server.SetRouteResponse(DataRoute, Json(400, """{"error":"Check-and-set parameter did not match the current version."}"""));
try
{
    await client.Kv.V2.WriteSecretAsync("app/db", Data(("username", "admin")), options: new KvWriteOptions { Cas = 1 });
}
catch (BastionVaultException e) when (e.Code == ErrorCodes.KvCasMismatch)
{
    Console.WriteLine("someone else wrote first; re-read and retry");
}

vault.Server.SetRouteResponse(DataRoute, Json(200, ReadVersionBody(version: 1, username: "admin", password: "p1")));
KvV2Secret? old = await client.Kv.V2.ReadSecretAsync("app/db", version: 1);
Console.WriteLine($"v1 password was {old!.Data!["password"].GetString()}");
```

### What goes over the wire

```http
POST /v1/secret/data/app/db HTTP/1.1
Host: vault.example.com:8200
X-BastionVault-Token: s.FAKEtoken
Content-Type: application/json

{"data":{"username":"admin","password":"p2"},"options":{"cas":2}}
```

```json
{
  "data": {
    "version": 3,
    "created_time": "2026-09-13T12:00:00Z",
    "deletion_time": "",
    "destroyed": false
  }
}
```

`Cas: v1.Version` means "only write if the current version is still `v1`" — optimistic
concurrency, not a server-side lock held between the read and the write. `Cas = 0` means "must not
exist yet" and is always sent as the literal `0`, never omitted (`KV2-003`); a mismatch on either
form is `BV-KV-003`, never a generic write failure.

## Step 2 — Soft delete, undelete, and destroy

<!-- docs:sample secrets-kv/soft-delete-and-destroy -->
```csharp
vault.Server.SetRouteResponse(DataRoute, new MockResponse(204, BodyIsJson: false));
await client.Kv.V2.SoftDeleteAsync("app/db"); // DELETE data/app/db, latest version only

vault.Server.SetRouteResponse(DataRoute, Json(200, SoftDeletedBody(version: 2)));
KvV2Secret? deleted = await client.Kv.V2.ReadSecretAsync("app/db");
// Soft-deleted is a successful read with no data, not null and not an error (KV2-004).
Console.WriteLine($"version {deleted!.Metadata.Version} deleted at {deleted.Metadata.DeletionTime:O}");

vault.Server.SetRouteResponse(UndeleteRoute, new MockResponse(204, BodyIsJson: false));
await client.Kv.V2.UndeleteAsync("app/db", [2]);

vault.Server.SetRouteResponse(DestroyRoute, new MockResponse(204, BodyIsJson: false));
await client.Kv.V2.DestroyAsync("app/db", [1]); // irreversible

vault.Server.SetRouteResponse(DataRoute, Json(404, """{"error":"Version has been permanently destroyed."}"""));
try
{
    await client.Kv.V2.ReadSecretAsync("app/db", version: 1);
}
catch (BastionVaultException e) when (e.Code == ErrorCodes.KvVersionDestroyed)
{
    Console.WriteLine("v1 is gone for good");
}
```

A soft-deleted read is **success with no data** — `KvV2Secret { Data: null, State: SoftDeleted }`
— not `null` and not an exception (`KV2-004`); `GetSecret` is the variant that turns that state
into `BV-KV-007` for you. `Undelete` brings versions back; `Destroy` does not; a version that has
been destroyed answers `BV-KV-005` forever afterwards, and there is no path that reverses it.
Neither `Undelete` nor `Destroy` accepts an empty version list — that is refused client-side
before anything is sent (`KV2-007`).

## Step 3 — Per-environment secrets

<!-- docs:sample secrets-kv/environments -->
```csharp
await client.Kv.V2.WriteAllEnvironmentsAsync(
    "app/db",
    baseData: Data(("host", "db.internal"), ("port", "5432"), ("pool", "10")),
    envs: new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>(StringComparer.Ordinal)
    {
        ["prod"] = Data(("host", "db.prod.internal"), ("pool", "50")),
        ["staging"] = Data(("host", "db.staging.internal")),
    });

vault.Server.SetRouteResponse(DataRoute, Json(200, ProdEnvironmentBody()));
KvV2Secret prod = await client.Kv.V2.GetSecretAsync("app/db", env: "prod");
Console.WriteLine($"prod host {prod.Data!["host"].GetString()}, resolved from [{string.Join(", ", prod.Metadata.AvailableEnvs)}]");

vault.Server.SetRouteResponse(DataRoute, Json(200, WriteBody(version: 2)));
await client.Kv.V2.PatchEnvironmentAsync("app/db", "staging", Data(("pool", "5"))); // prod untouched

vault.Server.SetRouteResponse(DataRoute, new MockResponse(404, Body: string.Empty, BodyIsJson: false));
vault.Server.SetRouteResponse(MetadataRoute, Json(200, MetadataBody()));
try
{
    await client.Kv.V2.GetSecretAsync("app/db", env: "dev");
}
catch (BastionVaultException e) when (e.Code == ErrorCodes.KvEnvironmentNotDeclared)
{
    Console.WriteLine("dev is not declared on this secret");
}
```

A v2 secret can carry a shared **base** plus `envs: { name → overrides }`; a read with `env=E`
returns `merge(base, envs[E])` and reports `ResolvedEnv`/`AvailableEnvs` on every v2 read
(`KV2-020`). `PatchEnvironment` writes one environment's overrides without disturbing the others
or re-reading first (`KV2-021`) — it is not a read-modify-write, it is one request. `env=E` for an
environment the secret never declared is a strict miss: the server's `404` carries no body, and
`GetSecretAsync` is what turns that into `BV-KV-006` — but only after confirming the secret itself
exists, so a genuinely absent secret still reads as `BV-KV-001`, not as a misleading
"wrong environment" (`KV2-006`). A token whose `AuthInfo.EnvironmentScope.Scoped` is true (see the
[authentication guide](authentication.md)) must pass `env` on every v2 call or the SDK refuses
client-side with `BV-KV-009` before a request goes out (`KV2-022`).

## Step 4 — Read many secrets efficiently

<!-- docs:sample secrets-kv/read-many -->
```csharp
// One POST /v2/sys/batch, not three requests (KV-010, BAT-007). A failing path does not
// fail the others (BAT-005/BAT-008): never `for path in list: await Read(path)`.
IReadOnlyDictionary<string, KvReadManyEntry> results =
    await client.Kv.ReadManyAsync("secret", ["app/db", "app/cache", "app/missing"]);

foreach ((string path, KvReadManyEntry entry) in results)
{
    if (!entry.IsSuccess)
    {
        Console.Error.WriteLine($"{path}: {entry.Error!.Code}");
    }
    else
    {
        Console.WriteLine($"{path}: {entry.Data!.Count} field(s)");
    }
}
```

### What goes over the wire

```json
{
  "results": [
    {"status": 200, "path": "secret/data/app/db", "data": {"data": {"username": "admin", "password": "p2"}}},
    {"status": 200, "path": "secret/data/app/cache", "data": {"data": {"ttl": "300"}}},
    {"status": 403, "path": "secret/data/app/missing", "errors": ["permission denied"]}
  ]
}
```

One request, three answers, and one denial that does not take the other two down with it
(`BAT-005`, `BAT-008`). The server bans a source IP above 200 requests per 10 seconds
(`14 — Batch and request efficiency`), and the SDK's own rate gate (8 req/s, burst 16) protects a
well-behaved caller from itself — but batching is faster regardless, because it is one round trip
instead of many. Never write `for path in list: await ReadSecretAsync(path)`.

## The whole program

<!-- docs:sample secrets-kv/complete -->
```csharp
using BastionVaultClient client = new();

try
{
    vault.Server.SetRouteResponse(DataRoute, Json(200, WriteBody(version: 1)));
    KvV2VersionMetadata written = await client.Kv.V2.WriteSecretAsync("app/db", Data(("username", "admin"), ("password", "p1")));
    Console.WriteLine($"wrote version {written.Version}");

    vault.Server.SetRouteResponse(DataRoute, Json(200, ReadVersionBody(version: 1, username: "admin", password: "p1")));
    KvV2Secret? secret = await client.Kv.V2.ReadSecretAsync("app/db");
    Console.WriteLine($"read back: username {secret!.Data!["username"].GetString()}");

    vault.Server.SetRouteResponse(DataRoute, new MockResponse(204, BodyIsJson: false));
    await client.Kv.V2.SoftDeleteAsync("app/db");

    vault.Server.SetRouteResponse(UndeleteRoute, new MockResponse(204, BodyIsJson: false));
    await client.Kv.V2.UndeleteAsync("app/db", [written.Version]);
    Console.WriteLine("undeleted");
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
| `BV-KV-002` | `data` was empty or missing from the write | Send at least one field |
| `BV-KV-003` | Check-and-set did not match the current version | Re-read the secret and retry with the current version |
| `BV-KV-004` | This mount requires CAS and none was sent | Always send `Cas` against this mount |
| `BV-KV-005` | The version was permanently destroyed | Irreversible; pick a different version, or write a new one |
| `BV-KV-006` | The secret exists, but not for the requested `env` | Check `AvailableEnvs`, or write that environment first |
| `BV-KV-008` | That version never existed | Check `ReadMetadataAsync`'s version table |
| `BV-KV-009` | This token is environment-scoped | Pass `env` on every v2 call |
| `BV-KV-011` | The requested field is not present in this version | Check the field name, or read the whole secret |

Handled completely, that is:

<!-- docs:sample secrets-kv/handling-errors -->
```csharp
try
{
    await client.Kv.V2.WriteSecretAsync("app/db", Data(("k", "v")), options: new KvWriteOptions { Cas = 1 });
}
catch (BastionVaultException e)
{
    string remedy = e.Code switch
    {
        ErrorCodes.KvCasMismatch => "re-read the secret and retry with the current version",
        ErrorCodes.KvCasRequired => "this mount requires cas_required; always send Cas",
        ErrorCodes.KvVersionDestroyed => "that version is gone for good; pick another",
        ErrorCodes.KvVersionNotFound => "that version never existed",
        ErrorCodes.KvEnvironmentNotDeclared => "the secret exists, but not for this env",
        ErrorCodes.KvEnvironmentRequired => "this token is env-scoped; pass env= on every call",
        ErrorCodes.KvFieldNotFound => "that field is not present in this version",
        _ => "look the code up in the error reference",
    };
    Console.Error.WriteLine($"{e.Code}: {e.Message} - {remedy}");
    throw;
}
```

## Next steps

- **Authentication guide** — obtaining the token this guide assumes you already hold, and what an
  environment-scoped token changes about every call above.
- **Getting started** — the shortest path from a token to a secret.
- **Configuration reference** — every setting, its environment variable, default, precedence and
  validation error code.
- **Error reference** — every code, its category, hint and retryability.
