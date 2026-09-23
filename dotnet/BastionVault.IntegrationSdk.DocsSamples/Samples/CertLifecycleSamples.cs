using System.Text.Json;
using BastionVault.IntegrationSdk.DocsSamples.Infrastructure;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.DocsSamples.Samples;

/// <summary>
/// Every executed sample shown by <c>docs/dotnet/engines/cert-lifecycle.md</c> (D6). Section 17
/// carries no dedicated usage guide for Cert lifecycle, so this page — and these samples — are
/// built directly from <c>12-other-engines-and-identity.md</c>'s Cert lifecycle table. Reuses
/// <see cref="MockVaultFixture"/>'s server: routes are bound per test on
/// <see cref="MockVaultFixture.Server"/> and cleared after, so no test leaks a route to another.
/// </summary>
public sealed class CertLifecycleSamples : IClassFixture<MockVaultFixture>
{
    private const string TargetRoute = "/v1/cert-lifecycle/targets/api-example-com";
    private const string StateRoute = "/v1/cert-lifecycle/state/api-example-com";
    private const string RenewRoute = "/v1/cert-lifecycle/renew/api-example-com";
    private const string TargetsInfoRoute = "/v2/cert-lifecycle/targets-info";
    private const string SchedulerConfigRoute = "/v1/cert-lifecycle/scheduler/config";

    private readonly MockVaultFixture vault;

    public CertLifecycleSamples(MockVaultFixture vault)
    {
        this.vault = vault;
    }

    [Fact]
    public void The_policy_this_guide_needs_is_the_policy_PolicyBuilder_builds()
    {
        // docs:begin cert-lifecycle/policy
        string hcl = new PolicyBuilder()
            .AddPath("cert-lifecycle/targets/api-example-com", [Capability.Create, Capability.Read])
            .AddPath("cert-lifecycle/state/api-example-com", [Capability.Read])
            .AddPath("cert-lifecycle/renew/api-example-com", [Capability.Update])
            .AddPath("cert-lifecycle/targets-info", [Capability.Read])
            .AddPath("cert-lifecycle/scheduler/config", [Capability.Read, Capability.Update])
            .Build();

        Console.WriteLine(hcl);
        // docs:end cert-lifecycle/policy

        Assert.Equal(PolicyShownInTheGuide(), hcl.Trim());
    }

    private static string PolicyShownInTheGuide()
    {
        string path = Path.Combine(DocsRepository.Docs.FullName, "dotnet", "engines", "cert-lifecycle.md");
        string[] lines = File.ReadAllLines(path);
        int opening = Array.FindIndex(lines, line => line.TrimEnd() == "```hcl");
        Assert.True(opening >= 0, $"{path} shows no ```hcl policy block (DOC-011).");
        int closing = Array.FindIndex(lines, opening + 1, line => line.TrimEnd() == "```");
        Assert.True(closing > opening, $"{path}: the ```hcl block is never closed.");
        return string.Join('\n', lines[(opening + 1)..closing]).Trim();
    }

    [Fact]
    public async Task Step_1_define_a_target_and_read_it_back()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(TargetRoute, Json(200, TargetBody()));

        // docs:begin cert-lifecycle/write-and-read-target
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
        // docs:end cert-lifecycle/write-and-read-target

        Assert.Equal("api.example.com", target!.CommonName);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task Step_2_check_a_targets_state_then_force_a_renewal()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(StateRoute, Json(200, StateBody()));
        vault.Server.SetRouteResponse(RenewRoute, Json(200, "{}"));

        // docs:begin cert-lifecycle/state-and-renew
        TargetState? state = await client.CertLifecycle.StateAsync("api-example-com");
        Console.WriteLine($"current serial {state!.CurrentSerial}, next attempt {state.NextAttempt:O}");

        // An unknown target name reaches the caller as BV-NOTFOUND-008 (the shared mapping).
        await client.CertLifecycle.RenewAsync("api-example-com");
        Console.WriteLine("renewal requested");
        // docs:end cert-lifecycle/state-and-renew

        Assert.Equal(0, state!.FailureCount);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task Step_3_list_every_targets_state_in_bulk_and_configure_the_scheduler()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(TargetsInfoRoute, Json(200, TargetsInfoBody()));
        vault.Server.SetRouteResponse(SchedulerConfigRoute, Json(200, "{}"));

        // docs:begin cert-lifecycle/bulk-and-scheduler
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
        // docs:end cert-lifecycle/bulk-and-scheduler

        Assert.Single(page.Records);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task The_whole_program_defines_a_target_and_renews_it()
    {
        vault.Server.ClearRouteResponses();

        // docs:begin cert-lifecycle/complete
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
        // docs:end cert-lifecycle/complete

        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task What_can_go_wrong_maps_every_cert_lifecycle_code_to_a_remedy()
    {
        vault.Server.ClearRouteResponses();
        vault.Server.SetRouteResponse(
            "/v1/cert-lifecycle/renew/missing-target",
            Json(500, """{"error":"cert-lifecycle: target `missing-target` not found"}"""));
        using BastionVaultClient client = vault.CreateClient();

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(async () =>
        {
            // docs:begin cert-lifecycle/handling-errors
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
            // docs:end cert-lifecycle/handling-errors
        });

        Assert.Equal(ErrorCodes.NotFoundResourceNotFound, failure.Code);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    private static MockResponse Json(int status, string body) => new(status, Body: Compact(body));

    private static string Compact(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    private static string TargetBody() => Compact("""
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
        """);

    private static string StateBody() => Compact("""
        {
          "data": {
            "current_serial": "aa:bb:cc",
            "current_not_after": "2027-01-01T00:00:00Z",
            "last_renewal": "2026-09-01T00:00:00Z",
            "next_attempt": "2026-12-25T00:00:00Z",
            "failure_count": 0
          }
        }
        """);

    private static string TargetsInfoBody() => Compact("""
        {
          "data": {
            "keys": ["api-example-com"],
            "records": [{"kind":"file","common_name":"api.example.com","renew_before":604800}],
            "total": 1,
            "next": null,
            "truncated": false
          }
        }
        """);
}
