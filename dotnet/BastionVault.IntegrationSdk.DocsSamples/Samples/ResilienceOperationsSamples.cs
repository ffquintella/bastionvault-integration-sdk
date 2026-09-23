using System.Net;
using System.Text.Json;
using BastionVault.IntegrationSdk.DocsSamples.Infrastructure;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.DocsSamples.Samples;

/// <summary>
/// Every executed sample shown by <c>docs/dotnet/resilience-and-operations.md</c> (D8), the .NET
/// adaptation of guides 9-11 in <c>specifications/17-usage-guides.md</c>. Reuses
/// <see cref="MockVaultFixture"/>'s server for the health-probe traffic discovery generates as
/// well as the ordinary request/response traffic every other sample class exercises.
/// </summary>
public sealed class ResilienceOperationsSamples : IClassFixture<MockVaultFixture>
{
    private const string MountRoute = "/v1/sys/mounts/team-a";
    private const string KvConfigRoute = "/v1/team-a/config";
    private const string PolicyRoute = "/v1/sys/policies/acl/team-a-rw";
    private const string PolicyTestRoute = "/v2/sys/policies/acl/test";
    private const string NamespaceRoute = "/v1/sys/namespaces/engineering";

    private readonly MockVaultFixture vault;

    public ResilienceOperationsSamples(MockVaultFixture vault)
    {
        this.vault = vault;
    }

    [Fact]
    public void The_policy_this_guide_needs_is_the_policy_PolicyBuilder_builds()
    {
        // docs:begin resilience-and-operations/policy
        string hcl = new PolicyBuilder()
            .AddPath(
                "team-a/data/*",
                [Capability.Create, Capability.Read, Capability.Update, Capability.Delete],
                requiredParameters: ["env"],
                allowedParameters: ["env"])
            .AddPath("team-a/metadata/*", [Capability.Read, Capability.List])
            .Build();

        Console.WriteLine(hcl);
        // docs:end resilience-and-operations/policy

        Assert.Equal(PolicyShownInTheGuide(), hcl.Trim());
    }

    [Fact]
    public async Task Step_1_connect_to_an_HA_cluster_and_discover()
    {
        vault.ServeHealthyVault();

        // docs:begin resilience-and-operations/connect-and-discover
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
        // docs:end resilience-and-operations/connect-and-discover

        Assert.NotNull(picked);
        Assert.Equal(NodeState.ActiveLeader, picked!.State);
        Assert.Equal(picked.Url, table.Picked?.Url);
    }

    [Fact]
    public void The_default_resolver_on_macOS_takes_an_explicit_nameserver_list()
    {
        // docs:begin resilience-and-operations/macos-nameservers
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
        // docs:end resilience-and-operations/macos-nameservers

        Assert.Equal(2, nameservers!.Count);
        Assert.True(client.Config.Discovery.StrictDiscovery);
    }

    [Fact]
    public async Task Step_2_configure_for_production()
    {
        vault.ServeHealthyVault();
        CapturingRequestObserver observer = new();

        // docs:begin resilience-and-operations/production-config
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
        // docs:end resilience-and-operations/production-config

        Assert.NotEmpty(observer.Events);
        RequestEvent first = observer.Events[0];
        Assert.Equal("GET", first.Method);
        Assert.Equal(200, first.StatusCode);
    }

    [Fact]
    public async Task Step_3_operate_the_vault_mounts_policies_namespaces()
    {
        vault.ServeHealthyVault();
        using BastionVaultClient client = vault.CreateClient();

        // docs:begin resilience-and-operations/operate-the-vault
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
        // docs:end resilience-and-operations/operate-the-vault

        Assert.True(result.ParseOk);
        Assert.True(result.Results[0].Allowed);
        Assert.Equal(20, ns.Quotas.MaxMounts);
        Assert.Equal("engineering", tenant.Namespace);
    }

    [Fact]
    public async Task What_can_go_wrong_maps_every_code_to_a_remedy()
    {
        vault.Server.ClearRouteResponses();
        vault.Server.SetRouteResponse(
            MockVaultFixture.SecretRoute,
            new MockResponse(
                429,
                Headers: new Dictionary<string, string> { ["Retry-After"] = "17" },
                Body: """{"errors":["request temporarily blocked by dos protection: request rate exceeded: >200 req/10s"]}"""));
        using BastionVaultClient client = vault.CreateClient();

        BastionVaultException caught = await Assert.ThrowsAsync<BastionVaultException>(async () =>
        {
            // docs:begin resilience-and-operations/handling-errors
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
            // docs:end resilience-and-operations/handling-errors
        });

        Assert.Equal(ErrorCodes.RateLimitedByDosGuard, caught.Code);
        Assert.False(caught.Retryable);
        Assert.Equal(TimeSpan.FromSeconds(17), caught.RetryAfter);
    }

    /// <summary>
    /// A fake DNS SRV answer resolving straight to the mock server, standing in for the real
    /// resolver an application never has to supply itself (DSC-050 ships one by default).
    /// </summary>
    private sealed class SingleNodeSrvResolver : ISrvResolver
    {
        private readonly SrvRecord record;

        public SingleNodeSrvResolver(Uri address)
        {
            record = new SrvRecord(address.Host, address.Port, Priority: 10, Weight: 50);
        }

        public Task<IReadOnlyList<SrvRecord>> ResolveAsync(string ownerName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<SrvRecord>>([record]);
        }
    }

    private static string PolicyShownInTheGuide()
    {
        string path = Path.Combine(DocsRepository.Docs.FullName, "dotnet", "resilience-and-operations.md");
        string[] lines = File.ReadAllLines(path);
        int opening = Array.FindIndex(lines, line => line.TrimEnd() == "```hcl");
        Assert.True(opening >= 0, $"{path} shows no ```hcl policy block (DOC-011).");
        int closing = Array.FindIndex(lines, opening + 1, line => line.TrimEnd() == "```");
        Assert.True(closing > opening, $"{path}: the ```hcl block is never closed.");
        return string.Join('\n', lines[(opening + 1)..closing]).Trim();
    }

    private static MockResponse Json(int status, string body) => new(status, Body: Compact(body));

    private static string Compact(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    private static string PolicyTestBody() => Compact("""
        {
          "parse_ok": true,
          "errors": [],
          "results": [
            {
              "path": "team-a/data/x",
              "capability": "read",
              "allowed": true,
              "matched_path": "team-a/data/*",
              "match_kind": "prefix",
              "denied_by_deny": false,
              "granting_policies": ["team-a-rw"],
              "evaluated_policies": ["default", "team-a-rw"],
              "missing_policies": [],
              "draft_only_allowed": false
            }
          ]
        }
        """);

    private static string NamespaceBody(int maxMounts) => Compact($$"""
        {
          "uuid": "ns-1",
          "path": "engineering",
          "parent_uuid": null,
          "created_at": "2026-01-02T03:04:05Z",
          "child_visible_default": false,
          "quotas": {
            "max_storage_bytes": 0,
            "max_leases": 0,
            "request_rate": 0,
            "max_mounts": {{maxMounts}},
            "max_entities": 0,
            "max_child_namespaces": 0
          }
        }
        """);
}
