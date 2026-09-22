using System.Text.Json;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.DocsSamples.Infrastructure;

/// <summary>
/// The server a documentation sample runs against: the test project's existing
/// <see cref="InProcessHttpsMockServer"/>, with the routes a guide's example touches bound to
/// fixture-shaped bodies, and the <c>BASTIONVAULT_*</c> environment variables pointed at it.
/// </summary>
/// <remarks>
/// <para>
/// DOC-003 requires every sample in D1-D10 to be compiled <b>and executed</b>. This fixture is
/// how "executed" is honoured without a live server; a sample that genuinely needs one belongs in
/// the integration job (M12) and is listed in D13 with its reason, never faked here
/// (DR-0018 D-M11-3).
/// </para>
/// <para>
/// The environment variables are what let a sample show the <i>real</i> configuration call -
/// <c>new BastionVaultClient()</c>, exactly as a reader would write it - instead of a
/// test-only seam threaded through the sample's own text. They are restored on dispose, and the
/// assembly runs serially (<c>AssemblyInfo.cs</c>) because process environment is global.
/// </para>
/// </remarks>
public sealed class MockVaultFixture : IAsyncLifetime
{
    private readonly Dictionary<string, string?> savedEnvironment = [];
    private InProcessHttpsMockServer server = null!;

    /// <summary>The mount and path every getting-started sample reads.</summary>
    public const string SecretRoute = "/v1/secret/data/app/db";

    /// <summary>The unauthenticated health route (SYS-001).</summary>
    public const string HealthRoute = "/v1/sys/health";

    /// <summary>The running mock server.</summary>
    public InProcessHttpsMockServer Server => server;

    /// <summary>
    /// A client configured the way a sample's own code configures one: from the environment.
    /// Samples that show configuration construct their own; samples that show one operation take
    /// this one, so the operation is the only thing the reader has to read. DR-0020 D-1 means this
    /// is now the one-line construction the guide itself shows; the client owns and disposes its
    /// own transport (D-2), so the caller's `using` is all the cleanup this needs.
    /// </summary>
    public BastionVaultClient CreateClient()
    {
        return new BastionVaultClient();
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        server = await InProcessHttpsMockServer.StartAsync();

        SetEnvironment("BASTIONVAULT_ADDR", server.BaseAddress.ToString().TrimEnd('/'));
        SetEnvironment("BASTIONVAULT_TOKEN", "s.FAKEtoken");
        SetEnvironment("BASTIONVAULT_CACERT", Path.Combine(server.TempDirectoryPath, "ca.pem"));
        SetEnvironment("BASTIONVAULT_NAMESPACE", string.Empty);

        // VAULT_* are the documented fallbacks (CFG-002). A developer's own shell may have them
        // set; they must not reach a sample run.
        SetEnvironment("VAULT_ADDR", null);
        SetEnvironment("VAULT_TOKEN", null);
        SetEnvironment("VAULT_CACERT", null);
        SetEnvironment("VAULT_NAMESPACE", null);

        ServeHealthyVault();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        foreach ((string name, string? value) in savedEnvironment)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        await server.DisposeAsync();
    }

    /// <summary>An Active node holding <c>secret/app/db</c> at version 2 (fixture kv.v2.read-latest).</summary>
    public void ServeHealthyVault()
    {
        server.ClearRouteResponses();
        server.SetRouteResponse(HealthRoute, Json(200, ActiveHealthBody));
        server.SetRouteResponse(SecretRoute, Json(200, ReadLatestBody));
    }

    /// <summary>An Active node whose policy denies the read: 403, which maps to BV-AUTHZ-001.</summary>
    public void ServeReadDeniedByPolicy()
    {
        server.ClearRouteResponses();
        server.SetRouteResponse(HealthRoute, Json(200, ActiveHealthBody));
        server.SetRouteResponse(SecretRoute, Json(403, """{"errors":["permission denied"]}"""));
    }

    /// <summary>An Active node on which the path holds nothing: 404 with an empty body (KV2-004).</summary>
    public void ServeSecretAbsent()
    {
        server.ClearRouteResponses();
        server.SetRouteResponse(HealthRoute, Json(200, ActiveHealthBody));
        server.SetRouteResponse(SecretRoute, new MockResponse(404, Body: string.Empty, BodyIsJson: false));
    }

    private static MockResponse Json(int status, string body) =>
        new(status, Headers: null, Body: Compact(body));

    // The bodies below are copied from the conformance fixtures named in each constant, so a
    // sample's wire traffic is the wire traffic the specification captured, and D2's DOC-012
    // JSON block can be read straight off them.
    private const string ActiveHealthBody = /* specifications/fixtures/sys/sys.health.active.json */ """
        {
          "initialized": true,
          "sealed": false,
          "standby": false,
          "cluster_healthy": true
        }
        """;

    private const string ReadLatestBody = /* specifications/fixtures/kv/kv.v2.read-latest.json */ """
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
        """;

    private static string Compact(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    private void SetEnvironment(string name, string? value)
    {
        if (!savedEnvironment.ContainsKey(name))
        {
            savedEnvironment[name] = Environment.GetEnvironmentVariable(name);
        }

        Environment.SetEnvironmentVariable(name, value);
    }
}
