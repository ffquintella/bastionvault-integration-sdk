# Resilience and operations guide (.NET)

**Implements** [`specifications/17-usage-guides.md` guides 9-11](../../specifications/17-usage-guides.md)
(Standard for guides 9-10, Complete for guide 11), adapted to .NET. The language-neutral behaviour
this page relies on is specified in
[13 — cluster discovery and resilience](../../specifications/13-cluster-discovery-and-resilience.md)
(`DSC-001`…`DSC-050`, `RES-001`…`RES-030`) and
[06 — system API](../../specifications/06-system-api.md) (`SYS-020`…`SYS-062`, the mount, policy
and namespace surface — `Sys` lives here rather than on a page of its own, per DR-0018 D-M11-10,
because it is operator-facing configuration rather than a secrets or crypto engine).

## The task

Connect to an HA cluster by cluster name rather than a literal address, read the ranked candidate
table `Client.Discover()` returns, recover from a node failure with `Client.Reconnect()`,
configure a client the way a production deployment should (timeouts, retries, the client-side rate
gate, an observability hook), and operate the vault itself: create a mount, write a least-privilege
policy for it, dry-run that policy, and create a namespace.

Every C# block below is compiled and executed on every build by
`dotnet/BastionVault.IntegrationSdk.DocsSamples`, and checked byte-for-byte against the source
that ran (`DOC-003`). If a block here is wrong, the build is red.

## Prerequisites

| You need | Detail |
|---|---|
| A running server, or a cluster of them | `https://vault.example.com:8200` for the single-node examples; a bare cluster name (`vault.corp.example`) for the discovery examples |
| A token with `sudo` or an equivalent operator policy | Mount, policy and namespace operations are administrative; the [authentication guide](authentication.md) covers obtaining a token |
| The package | `dotnet add package BastionVault.IntegrationSdk` |

### The policy the example needs

<!-- docs:sample resilience-and-operations/policy -->
```csharp
string hcl = new PolicyBuilder()
    .AddPath(
        "team-a/data/*",
        [Capability.Create, Capability.Read, Capability.Update, Capability.Delete],
        requiredParameters: ["env"],
        allowedParameters: ["env"])
    .AddPath("team-a/metadata/*", [Capability.Read, Capability.List])
    .Build();

Console.WriteLine(hcl);
```

which emits:

```hcl
path "team-a/data/*" {
  capabilities = ["create", "read", "update", "delete"]
  required_parameters = ["env"]
  allowed_parameters = ["env"]
}

path "team-a/metadata/*" {
  capabilities = ["read", "list"]
}
```

## Step 1 — Connect to an HA cluster and discover its nodes

A **bare cluster name** (no `://`, no explicit port, not an IP literal) puts the client into DNS
SRV discovery mode instead of literal mode (`DSC-001`): the SDK queries
`_bvault._tcp.<name>`, health-probes every candidate, and pins the best one by the ranking in
[13](../../specifications/13-cluster-discovery-and-resilience.md#picking-a-node). An application
supplies no resolver of its own unless it wants to override the default — `DSC-050`'s built-in
resolver ships with the SDK, so cluster discovery works out of the box.

<!-- docs:sample resilience-and-operations/connect-and-discover -->
```csharp
// "vault.corp.example" is a bare cluster name, so the client resolves it through DNS SRV
// instead of treating it as a literal host (13 - cluster discovery). An application
// supplies no ISrvResolver in production; here a fake one stands in for DNS so the sample
// runs against the mock server instead of a real network.
using BastionVaultClient client = new(new BastionVaultClientOptions
{
    Address = "vault.corp.example",
    Token = "s.FAKEtoken",
    CaCertPath = Path.Combine(vault.Server.TempDirectoryPath, "ca.pem"),
    SrvResolver = new SingleNodeSrvResolver(vault.Server.BaseAddress),
});

NodeSelection? picked = await client.ConnectAsync();
Console.WriteLine($"picked {picked?.Url} ({picked?.State}, {picked?.RttMs} ms)");

// Client.Discover() is diagnostics only - it never changes the pinned node - so it is
// safe to call at any time to see the whole ranked candidate table.
DiscoveryReport table = await client.DiscoverAsync();
Console.WriteLine(table.Render());

try
{
    KvV2Secret? secret = await client.Kv.V2.ReadSecretAsync("app/db", mount: "secret");
    Console.WriteLine(secret is null ? "no such secret" : $"read version {secret.Metadata.Version}");
}
catch (BastionVaultException e) when (e.Code == ErrorCodes.DiscoveryNodeUnavailable)
{
    // Reads and lists already failed over once automatically when >= 2 candidates exist;
    // this catch is what is left after that has already happened, or for a node-local
    // operation and a write, which never fail over. Reconnect() re-runs discovery (SRV +
    // probe) and re-pins, and the caller decides whether to retry.
    await client.ReconnectAsync();
    KvV2Secret? secret = await client.Kv.V2.ReadSecretAsync("app/db", mount: "secret");
    Console.WriteLine($"reconnected; read version {secret?.Metadata.Version}");
}
```

`Client.SelectedNode` is the node discovery pinned; `Client.Discover()` is a diagnostic that
returns the **full ranked table** — survivors first, in rank order, then every candidate discovery
dropped and why — without ever moving the pin (`DSC-036`). Reads and lists fail over **once**
automatically when discovery produced two or more candidates; writes and node-local operations
(connect sessions, watchers) never do, because replaying them could double-apply an ambiguous
commit. `Client.Reconnect()` is the explicit recovery path: it re-runs discovery in full (a fresh
SRV lookup, not just a re-probe of the cached candidates) and re-pins, and it is always safe to
call, including concurrently from more than one caller.

### What goes over the wire

Each candidate is probed with an unauthenticated `GET /v1/sys/health`:

```http
GET /v1/sys/health HTTP/1.1
Host: 127.0.0.1:8200
Accept: application/json
```

```json
{ "initialized": true, "sealed": false, "standby": false, "cluster_healthy": true }
```

### The default resolver on macOS

`DSC-050`'s built-in SRV resolver queries the platform's configured nameservers by default. **On
macOS this default cannot see scoped resolvers**: the .NET API it calls,
`NetworkInterface.GetIPProperties().DnsAddresses`, returns one identical global nameserver list for
every interface on macOS — including interfaces that are down — instead of the per-interface
scoped list macOS itself uses. On a split-horizon VPN, where the corporate nameserver is bound to
the VPN's scoped interface and is not in that global list, the resolver ends up querying the wrong
nameserver and gets no answer for the cluster's SRV record. Because `DiscoveryConfig.StrictDiscovery`
defaults to `true` (`DSC-017`), the client does **not** silently fall back to guessing a single
address in that situation: it **refuses at startup** with `BV-DISCOVERY-004`, the same as it would
for a genuinely unpublished SRV record, rather than degrading in silence to a node that may not be
reachable at all. **Linux and Windows nameserver discovery is in-platform and unaffected by this**
— the workaround below is for macOS behind a split-horizon VPN specifically, and applying it
elsewhere only removes a remedy path you do not need.

The supported remedy is `DiscoveryConfig.Nameservers` (`DSC-050`): an explicit nameserver list
bypasses the platform call entirely and is used in its place, so discovery reaches the resolver
that actually knows the cluster's SRV record.

<!-- docs:sample resilience-and-operations/macos-nameservers -->
```csharp
// See "The default resolver on macOS" above. Naming the nameservers explicitly bypasses
// the platform call that collapses macOS's scoped resolvers into one list, so discovery
// reaches the right resolver even on a split-horizon VPN. No SrvResolver is set here: this
// still uses DSC-050's built-in default resolver, only pointed at an explicit list instead
// of the platform's.
using BastionVaultClient client = new(new BastionVaultClientOptions
{
    Address = "vault.corp.example",
    Token = "s.FAKEtoken",
    AllowInsecureHttp = true,
    Discovery = new DiscoveryConfig
    {
        Nameservers =
        [
            IPEndPoint.Parse("10.0.0.53:53"),
            IPEndPoint.Parse("10.0.0.54:53"),
        ],
    },
});

// Construction resolves configuration only and performs no I/O (CFG-005): nothing has been
// queried yet, so it is safe to inspect the setting without a network or a DNS server.
IReadOnlyList<IPEndPoint>? nameservers = client.Config.Discovery.Nameservers;
Console.WriteLine($"configured nameservers: {string.Join(", ", nameservers!)}");
```

## Step 2 — Configure for production

<!-- docs:sample resilience-and-operations/production-config -->
```csharp
using BastionVaultClient client = new(new BastionVaultClientOptions
{
    Address = vault.Server.BaseAddress.ToString(),
    Token = "s.FAKEtoken",
    CaCertPath = Path.Combine(vault.Server.TempDirectoryPath, "ca.pem"),
    Timeout = TimeSpan.FromSeconds(10),
    ConnectTimeout = TimeSpan.FromSeconds(3),
    RetryPolicy = new RetryPolicy { MaxAttempts = 3, InitialBackoff = TimeSpan.FromMilliseconds(200), MaxBackoff = TimeSpan.FromSeconds(2) },
    RateGate = new RateGate { RatePerSecond = 8, Burst = 16 },
    Observer = observer,
});

await client.Sys.HealthAsync();
```

The checklist this setup follows: pin a CA (`CaCertPath`/`CaCertPem`), never set `TlsSkipVerify` in
production (see the [security guide](security.md)), use a `Login`-based token source with
`AutoRenew` rather than a long-lived static token, write the least-privilege policy each service
account needs, cache reads locally with a TTL and invalidate early on `Sys.CacheVersion`, treat
`BV-RATE-001` as a client bug to fix by batching or caching rather than by retrying, and run the
integration suite (see the [contributing guide](contributing.md)) against your own server version
before every release. `RetryPolicy` and `RateGate` above are the client-side defaults (3 attempts,
250ms initial backoff, 8 requests/second with a burst of 16); `Observer` is `IRequestObserver`,
called once per attempt with the method, path, status, duration and error code — never a body and
never a token — under the metric name convention `bastionvault.client.request.duration`, tagged by
`method`, `status` and `code`.

## Step 3 — Operate the vault: mounts, policies, namespaces

<!-- docs:sample resilience-and-operations/operate-the-vault -->
```csharp
vault.Server.SetRouteResponse(MountRoute, new MockResponse(204, BodyIsJson: false));
await client.Sys.MountAsync("team-a", new MountRequest { Type = "kv-v2", Description = "team A secrets" });

vault.Server.SetRouteResponse(KvConfigRoute, new MockResponse(204, BodyIsJson: false));
await client.Kv.V2.WriteConfigAsync(
    new KvV2Config { MaxVersions = 10, CasRequired = true, DeleteVersionAfter = "0s" }, mount: "team-a");

string hcl = new PolicyBuilder()
    .AddPath(
        "team-a/data/*",
        [Capability.Create, Capability.Read, Capability.Update, Capability.Delete],
        requiredParameters: ["env"],
        allowedParameters: ["env"])
    .AddPath("team-a/metadata/*", [Capability.Read, Capability.List])
    .Build();
vault.Server.SetRouteResponse(PolicyRoute, new MockResponse(204, BodyIsJson: false));
await client.Sys.WritePolicyAsync("team-a-rw", hcl);

vault.Server.SetRouteResponse(PolicyTestRoute, Json(200, PolicyTestBody()));
PolicyTestResult result = await client.Sys.TestPolicyAsync(
    hcl, [new PolicyTestCase { Path = "team-a/data/x", Capability = Capability.Read }], name: "team-a-rw");
Console.WriteLine($"parse_ok={result.ParseOk}, allowed={result.Results[0].Allowed}");

// WriteNamespace is a full replace (SYS-060): every quota this call omits is written as 0,
// and an omitted ChildVisibleDefault is written as false. UpdateNamespace is the
// read-merge-write form for changing one field without resetting the rest.
vault.Server.SetRouteResponse(NamespaceRoute, Json(200, NamespaceBody(maxMounts: 20)));
Namespace ns = await client.Sys.WriteNamespaceAsync(
    "engineering", new NamespaceSpec { Quotas = new NamespaceQuotas { MaxMounts = 20 } });
Console.WriteLine($"namespace {ns.Path}, max mounts {ns.Quotas.MaxMounts}");

// A view sharing this client's token and transport, scoped to the new namespace. Sys
// calls made through it carry X-BastionVault-Namespace: engineering.
BastionVaultClient tenant = client.WithNamespace("engineering");
Console.WriteLine($"tenant namespace: {tenant.Namespace}");
```

Four things about this surface are easy to get wrong, so name them here: `Sys.ListMounts` returns
**only `type` and `description`** per entry — no `config`, `uuid`, `options` or `accessor`
(`SYS-020`); `POST sys/mounts/{path}` **ignores** `config` (`default_lease_ttl`, `max_lease_ttl`),
so KV v2 tuning goes through `Kv.V2.WriteConfig` instead; `Sys.WriteNamespace` is an **upsert and a
full replace** — every quota the call omits is written as `0` (unlimited) and an omitted
`ChildVisibleDefault` is written as `false` — so `Sys.UpdateNamespace` is the read-merge-write form
to reach for when only one field should change; and remounting to a path already in use, or
deleting a namespace, are both destructive in ways the SDK cannot undo (`Sys.DeleteNamespace`
cascades unmounts of everything beneath it).

## The whole program

This page adapts three guides, and each step above is already a complete, runnable program in its
own right (construction through the operation and back). "The whole program" is therefore the
connect-and-discover step repeated here rather than a fourth variant invented for this section
alone — it is the one of the three whose error handling this page has not already shown in full:

<!-- docs:sample resilience-and-operations/connect-and-discover -->
```csharp
// "vault.corp.example" is a bare cluster name, so the client resolves it through DNS SRV
// instead of treating it as a literal host (13 - cluster discovery). An application
// supplies no ISrvResolver in production; here a fake one stands in for DNS so the sample
// runs against the mock server instead of a real network.
using BastionVaultClient client = new(new BastionVaultClientOptions
{
    Address = "vault.corp.example",
    Token = "s.FAKEtoken",
    CaCertPath = Path.Combine(vault.Server.TempDirectoryPath, "ca.pem"),
    SrvResolver = new SingleNodeSrvResolver(vault.Server.BaseAddress),
});

NodeSelection? picked = await client.ConnectAsync();
Console.WriteLine($"picked {picked?.Url} ({picked?.State}, {picked?.RttMs} ms)");

// Client.Discover() is diagnostics only - it never changes the pinned node - so it is
// safe to call at any time to see the whole ranked candidate table.
DiscoveryReport table = await client.DiscoverAsync();
Console.WriteLine(table.Render());

try
{
    KvV2Secret? secret = await client.Kv.V2.ReadSecretAsync("app/db", mount: "secret");
    Console.WriteLine(secret is null ? "no such secret" : $"read version {secret.Metadata.Version}");
}
catch (BastionVaultException e) when (e.Code == ErrorCodes.DiscoveryNodeUnavailable)
{
    // Reads and lists already failed over once automatically when >= 2 candidates exist;
    // this catch is what is left after that has already happened, or for a node-local
    // operation and a write, which never fail over. Reconnect() re-runs discovery (SRV +
    // probe) and re-pins, and the caller decides whether to retry.
    await client.ReconnectAsync();
    KvV2Secret? secret = await client.Kv.V2.ReadSecretAsync("app/db", mount: "secret");
    Console.WriteLine($"reconnected; read version {secret?.Metadata.Version}");
}
```

## What can go wrong

| Code | Meaning | Fix |
|---|---|---|
| `BV-DISCOVERY-001` | Discovery found no candidates at all | Check the SRV record `_bvault._tcp.<name>` exists, or use a literal `https://host:port` address |
| `BV-DISCOVERY-002` | No candidate probed as healthy | Read `Details.candidates` for each node's state; unseal or start the nodes, check TLS SANs cover the SRV targets |
| `BV-DISCOVERY-003` | The pinned node became unavailable | Reads/lists already failed over once automatically; for a write or a node-local session call `Client.Reconnect()` and retry |
| `BV-DISCOVERY-004` | Strict discovery found no SRV records | Publish the SRV record, set `DiscoveryConfig.Nameservers` if the default resolver cannot see it, or set `StrictDiscovery = false` to accept a single literal candidate instead |
| `BV-RATE-001` | The server's DoS guard temporarily blocked this client IP | This is a client bug, not a server problem: batch or cache instead of retrying, and wait `RetryAfter` seconds |

Handled completely, that is:

<!-- docs:sample resilience-and-operations/handling-errors -->
```csharp
try
{
    await client.Kv.V2.ReadSecretAsync("app/db", mount: "secret");
}
catch (BastionVaultException e)
{
    string remedy = e.Code switch
    {
        ErrorCodes.DiscoveryNoHealthyNode => "check Details.candidates for each node's state; unseal or start nodes, check TLS SANs cover SRV targets",
        ErrorCodes.DiscoveryStrictDiscoveryRefused => "publish the SRV record, or set DiscoveryConfig.StrictDiscovery = false to accept a single candidate",
        ErrorCodes.DiscoveryNodeUnavailable => "reads/lists already failed over once; for a write or a node-local session call Client.Reconnect() and retry",
        // A DoS-guard 429 is a client bug, not a server problem, and CFG-052/CFG-053
        // make it non-retryable: fewer requests, never a retry loop.
        ErrorCodes.RateLimitedByDosGuard => $"reduce request fan-out (batch/cache); wait {e.RetryAfter?.TotalSeconds}s before trying again",
        _ => "look the code up in the error reference",
    };
    Console.Error.WriteLine($"{e.Code}: {e.Message} - {remedy}");
    throw;
}
```

## Next steps

- **Authentication guide** — the token source and `AutoRenew` the production checklist assumes.
- **Security guide** — TLS defaults, why `TlsSkipVerify` is dangerous, and least-privilege policies
  for a service account.
- **Vault compatibility gaps** — the absences (leases, wrapping, cubbyhole) that a HashiCorp Vault
  migration would otherwise discover as a `404`.
- **Contributing / testing guide** — running the integration suite the production checklist asks
  for against your own server version.
