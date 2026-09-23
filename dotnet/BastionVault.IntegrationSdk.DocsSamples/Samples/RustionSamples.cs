using System.Text.Json;
using BastionVault.IntegrationSdk.DocsSamples.Infrastructure;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.DocsSamples.Samples;

/// <summary>
/// Every executed sample shown by <c>docs/dotnet/engines/rustion.md</c> (D6), built directly from
/// <c>12-other-engines-and-identity.md</c>'s Rustion table (RUS-001…RUS-003) — only the
/// operator-facing subset this SDK types; the rest is <c>Client.Logical</c>. Reuses
/// <see cref="MockVaultFixture"/>'s server; routes are bound per test and cleared after.
/// </summary>
public sealed class RustionSamples : IClassFixture<MockVaultFixture>
{
    private const string TargetsRoute = "/v1/rustion/targets/";
    private const string TargetRoute = "/v1/rustion/targets/bastion-1";
    private const string ProbeRoute = "/v1/rustion/targets/bastion-1/probe";
    private const string SessionOpenRoute = "/v2/rustion/session/open";
    private const string RecordingsRoute = "/v1/rustion/recordings/";
    private const string RecordingRoute = "/v1/rustion/recordings/rec_abc123";

    private readonly MockVaultFixture vault;

    public RustionSamples(MockVaultFixture vault)
    {
        this.vault = vault;
    }

    [Fact]
    public void The_policy_this_guide_needs_is_the_policy_PolicyBuilder_builds()
    {
        // docs:begin rustion/policy
        string hcl = new PolicyBuilder()
            .AddPath("rustion/targets/", [Capability.Create, Capability.List])
            .AddPath("rustion/targets/bastion-1", [Capability.Read])
            .AddPath("rustion/targets/bastion-1/probe", [Capability.Update])
            .AddPath("rustion/session/open", [Capability.Update])
            .AddPath("rustion/recordings/", [Capability.List])
            .AddPath("rustion/recordings/rec_abc123", [Capability.Read])
            .Build();

        Console.WriteLine(hcl);
        // docs:end rustion/policy

        Assert.Equal(PolicyShownInTheGuide(), hcl.Trim());
    }

    private static string PolicyShownInTheGuide()
    {
        string path = Path.Combine(DocsRepository.Docs.FullName, "dotnet", "engines", "rustion.md");
        string[] lines = File.ReadAllLines(path);
        int opening = Array.FindIndex(lines, line => line.TrimEnd() == "```hcl");
        Assert.True(opening >= 0, $"{path} shows no ```hcl policy block (DOC-011).");
        int closing = Array.FindIndex(lines, opening + 1, line => line.TrimEnd() == "```");
        Assert.True(closing > opening, $"{path}: the ```hcl block is never closed.");
        return string.Join('\n', lines[(opening + 1)..closing]).Trim();
    }

    [Fact]
    public async Task Step_1_register_a_bastion_target_and_probe_it()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(TargetsRoute, Json(200, TargetsListBody()));
        vault.Server.SetRouteResponse(TargetRoute, Json(200, TargetBody()));
        vault.Server.SetRouteResponse(ProbeRoute, Json(200, ProbeBody()));

        // docs:begin rustion/targets
        // 12 names no field-level schema for a target, so it travels as an opaque bag (D-M1c-25).
        using JsonDocument target = JsonDocument.Parse("""{"name":"bastion-1","host":"bastion1.internal","port":22}""");
        await client.Rustion.Targets.CreateAsync(target.RootElement.Clone());

        IReadOnlyList<string> targets = await client.Rustion.Targets.ListAsync();
        Console.WriteLine($"{targets.Count} target(s) registered");

        BastionVault.IntegrationSdk.Response? probe = await client.Rustion.Targets.ProbeAsync("bastion-1");
        Console.WriteLine($"probe result: {probe!.Raw}");
        // docs:end rustion/targets

        Assert.Single(targets);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task Step_2_open_a_connect_only_session_against_a_shared_secret()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(SessionOpenRoute, Json(200, SessionOpenBody()));

        // docs:begin rustion/session
        // v2-pinned (Appendix A): the server resolves the credential from secret_id itself - the
        // caller never sees or forwards the raw credential material.
        BastionVault.IntegrationSdk.Response? session = await client.Rustion.Session.OpenConnectOnlyAsync(new RustionSessionOpenConnectOnlyRequest
        {
            ResourceName = "db-primary",
            SecretId = "secret-abc123",
            TargetHost = "db1.internal",
            TargetPort = 5432,
            TargetProtocol = "postgres",
        });
        Console.WriteLine($"session opened: {session!.Raw}");
        // docs:end rustion/session

        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task Step_3_list_and_read_session_recordings()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(RecordingsRoute, Json(200, "{}"));
        vault.Server.SetRouteResponse(RecordingRoute, Json(200, RecordingBody()));

        // docs:begin rustion/recordings
        IReadOnlyList<string> recordings = await client.Rustion.Recordings.ListAsync();
        Console.WriteLine($"{recordings.Count} recording(s) on record");

        // `rid` must match `rec_[A-Za-z0-9_-]+`, checked client-side before any request is sent.
        BastionVault.IntegrationSdk.Response? recording = await client.Rustion.Recordings.ReadAsync("rec_abc123");
        Console.WriteLine($"recording: {recording!.Raw}");

        // Recordings.Download (not shown here) reads chunk 0, 1, 2, ... until eof, verifying the
        // recording's SHA-256 when the server reports one (RUS-001) - both are node-local (RUS-002).
        // docs:end rustion/recordings

        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task The_whole_program_registers_a_target_and_opens_a_session()
    {
        vault.Server.ClearRouteResponses();

        // docs:begin rustion/complete
        using BastionVaultClient client = new();

        try
        {
            vault.Server.SetRouteResponse(TargetsRoute, Json(200, "{}"));
            using JsonDocument target = JsonDocument.Parse("""{"name":"bastion-1","host":"bastion1.internal","port":22}""");
            await client.Rustion.Targets.CreateAsync(target.RootElement.Clone());
            Console.WriteLine("target registered");

            vault.Server.SetRouteResponse(SessionOpenRoute, Json(200, SessionOpenBody()));
            BastionVault.IntegrationSdk.Response? session = await client.Rustion.Session.OpenConnectOnlyAsync(new RustionSessionOpenConnectOnlyRequest
            {
                ResourceName = "db-primary",
                SecretId = "secret-abc123",
                TargetHost = "db1.internal",
                TargetPort = 5432,
                TargetProtocol = "postgres",
            });
            Console.WriteLine("session opened");
        }
        catch (BastionVaultException e)
        {
            Console.Error.WriteLine($"{e.Code}: {e.Message} ({e.Hint}); retryable: {e.Retryable}");
            throw;
        }
        // docs:end rustion/complete

        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task What_can_go_wrong_maps_every_rustion_code_to_a_remedy()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(async () =>
        {
            // docs:begin rustion/handling-errors
            try
            {
                // A malformed rid never reaches the server - refused client-side (BV-INPUT-001).
                await client.Rustion.Recordings.ReadAsync("not-a-recording-id");
            }
            catch (BastionVaultException e)
            {
                string remedy = e.Code switch
                {
                    ErrorCodes.InputInvalidArgument => "`rid` must match `rec_[A-Za-z0-9_-]+`",
                    ErrorCodes.RustionAuthorityPendingApproval => "wait for the pending authority to be approved, or approve it",
                    ErrorCodes.RustionUnknownAuthority => "the bastion's authority is not registered; attest it first",
                    ErrorCodes.RustionPolicyDenied => "the Rustion policy itself denies this action; check policy.effective",
                    ErrorCodes.ProtocolDigestMismatch => "Download's assembled bytes did not match the reported SHA-256 (RUS-001); retry the download",
                    ErrorCodes.TransportResponseTooLarge => "the blob fallback exceeded MaxResponseBytes; fetch chunks individually instead",
                    _ => "look the code up in the error reference",
                };
                Console.Error.WriteLine($"{e.Code}: {e.Message} - {remedy}");
                throw;
            }
            // docs:end rustion/handling-errors
        });

        Assert.Equal(ErrorCodes.InputInvalidArgument, failure.Code);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    private static MockResponse Json(int status, string body) => new(status, Body: Compact(body));

    private static string Compact(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    private static string TargetsListBody() => Compact("""{"data":{"keys":["bastion-1"]}}""");

    private static string TargetBody() => Compact("""{"data":{"name":"bastion-1","host":"bastion1.internal","port":22}}""");

    private static string ProbeBody() => Compact("""{"data":{"reachable":true,"latency_ms":12}}""");

    private static string SessionOpenBody() => Compact("""{"data":{"bastion_id":"bastion-1","session_id":"sess-1","correlation_id":"corr-1"}}""");

    private static string RecordingBody() => Compact("""{"data":{"rid":"rec_abc123","target":"bastion-1","started_at":"2026-09-20T00:00:00Z"}}""");
}
