using BastionVault.IntegrationSdk.DocsSamples.Infrastructure;

namespace BastionVault.IntegrationSdk.DocsSamples.Samples;

/// <summary>
/// Every executed sample shown by <c>docs/dotnet/getting-started.md</c> (D2), the .NET adaptation
/// of guide 1 in <c>specifications/17-usage-guides.md</c>.
/// </summary>
/// <remarks>
/// Each test method is one sample. The text between <c>// docs:begin id</c> and
/// <c>// docs:end id</c> is what the document shows, byte for byte; everything outside the
/// markers is the scaffolding a reader does not need to see - arranging the mock server and
/// asserting the sample did what the guide claims it does.
/// </remarks>
public sealed class GettingStartedSamples : IClassFixture<MockVaultFixture>
{
    private readonly MockVaultFixture vault;

    public GettingStartedSamples(MockVaultFixture vault)
    {
        this.vault = vault;
    }

    [Fact]
    public void Step_1_configure_the_client()
    {
        vault.ServeHealthyVault();

        // docs:begin getting-started/configure
        // BASTIONVAULT_ADDR, BASTIONVAULT_TOKEN and BASTIONVAULT_CACERT carry the configuration;
        // no other setting is required to reach https://vault.example.com:8200. The client
        // defaults its transport to the SDK's HTTP implementation, so this one line resolves the
        // configuration and is ready to make requests.
        using BastionVaultClient client = new();

        Console.WriteLine($"address:   {client.Config.Address}");
        Console.WriteLine($"namespace: {(client.Config.Namespace.Length == 0 ? "<root>" : client.Config.Namespace)}");
        Console.WriteLine($"token:     {(client.Config.Token.HasValue ? "configured" : "absent")}");

        // Error handling is deliberately elided here and shown complete in "The whole program"
        // below: construction raises BastionVaultException with BV-CONFIG-001 for a malformed
        // address and BV-TRANSPORT-003 for a CA bundle that cannot be read.
        // docs:end getting-started/configure

        Assert.True(client.Config.Token.HasValue);
        Assert.Empty(client.Config.Namespace);
    }

    [Fact]
    public async Task Step_2_check_the_node_is_active()
    {
        vault.ServeHealthyVault();
        using BastionVaultClient client = vault.CreateClient();

        // docs:begin getting-started/health
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
        // docs:end getting-started/health

        Assert.Equal(HealthState.Active, health.State);
        Assert.Equal(200, health.StatusCode);
    }

    [Fact]
    public async Task Step_3_read_a_kv_v2_secret()
    {
        vault.ServeHealthyVault();
        using BastionVaultClient client = vault.CreateClient();

        // docs:begin getting-started/read-secret
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
        // docs:end getting-started/read-secret

        Assert.NotNull(secret);
        Assert.Equal(KvV2SecretState.Live, secret.State);
        Assert.Equal(2, secret.Metadata.Version);
        Assert.Equal("admin", username);
        Assert.Equal("alice", secret.Metadata.Username);
    }

    [Fact]
    public async Task What_can_go_wrong_maps_every_code_to_a_remedy()
    {
        vault.ServeReadDeniedByPolicy();
        using BastionVaultClient client = vault.CreateClient();

        BastionVaultException denied = await Assert.ThrowsAsync<BastionVaultException>(async () =>
        {
            // docs:begin getting-started/handling-errors
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
            // docs:end getting-started/handling-errors
        });

        Assert.Equal(ErrorCodes.AuthzPermissionDenied, denied.Code);
        Assert.Equal(ErrorCategory.Authorization, denied.Category);
        Assert.False(denied.Retryable);
    }

    [Fact]
    public async Task The_whole_program_runs_end_to_end()
    {
        vault.ServeHealthyVault();

        // docs:begin getting-started/complete
        using BastionVaultClient client = new();

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
        // docs:end getting-started/complete

        IReadOnlyList<Tests.Harness.MockRequestObservation> requests = vault.Server.Requests;
        Assert.Equal(MockVaultFixture.SecretRoute, requests[^1].Path);
        Assert.Equal(MockVaultFixture.HealthRoute, requests[^2].Path);
    }

    [Fact]
    public void The_policy_this_guide_needs_is_the_policy_PolicyBuilder_builds()
    {
        // docs:begin getting-started/policy
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
        // docs:end getting-started/policy

        Assert.Equal(PolicyShownInTheGuide(), hcl.Trim());
    }

    /// <summary>
    /// The <c>```hcl</c> block of D2, so the rendered policy in the guide is drift-checked
    /// against <see cref="PolicyBuilder"/> exactly as the C# blocks are drift-checked against
    /// their regions. DOC-011 is a reviewer check for *correctness*; this keeps it honest about
    /// *currency*.
    /// </summary>
    private static string PolicyShownInTheGuide()
    {
        string path = Path.Combine(DocsRepository.Docs.FullName, "dotnet", "getting-started.md");
        string[] lines = File.ReadAllLines(path);
        int opening = Array.FindIndex(lines, line => line.TrimEnd() == "```hcl");
        Assert.True(opening >= 0, $"{path} shows no ```hcl policy block (DOC-011).");
        int closing = Array.FindIndex(lines, opening + 1, line => line.TrimEnd() == "```");
        Assert.True(closing > opening, $"{path}: the ```hcl block is never closed.");
        return string.Join('\n', lines[(opening + 1)..closing]).Trim();
    }

    [Fact]
    public async Task An_absent_secret_is_null_rather_than_an_error()
    {
        vault.ServeSecretAbsent();
        using BastionVaultClient client = vault.CreateClient();

        KvV2Secret? secret = await client.Kv.V2.ReadSecretAsync("app/db", mount: "secret");

        Assert.Null(secret);
    }
}
