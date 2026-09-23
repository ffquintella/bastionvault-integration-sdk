using System.Text.Json;
using BastionVault.IntegrationSdk.DocsSamples.Infrastructure;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.DocsSamples.Samples;

/// <summary>
/// Every executed sample shown by <c>docs/dotnet/engines/resources.md</c> (D6). Section 17 carries
/// no dedicated usage guide for Resources, so this page — and these samples — are built directly
/// from <c>12-other-engines-and-identity.md</c>'s Resources table (RSC-001, RSC-002). Reuses
/// <see cref="MockVaultFixture"/>'s server: routes are bound per test on
/// <see cref="MockVaultFixture.Server"/> and cleared after, so no test leaks a route to another.
/// </summary>
public sealed class ResourcesSamples : IClassFixture<MockVaultFixture>
{
    private const string ResourceRoute = "/v1/resources/resources/db-primary";
    private const string RenameRoute = "/v1/resources/resources/db-primary/rename";
    private const string SecretRoute = "/v1/resources/secrets/db-primary/password";
    private const string MfaBeginRoute = "/v1/resources/v2/connect/mfa/begin";
    private const string MfaVerifyRoute = "/v1/resources/v2/connect/mfa/verify";
    private const string AuthorizeRoute = "/v1/resources/v2/connect/authorize";

    private readonly MockVaultFixture vault;

    public ResourcesSamples(MockVaultFixture vault)
    {
        this.vault = vault;
    }

    [Fact]
    public void The_policy_this_guide_needs_is_the_policy_PolicyBuilder_builds()
    {
        // docs:begin resources/policy
        string hcl = new PolicyBuilder()
            .AddPath("resources/resources/db-primary", [Capability.Create, Capability.Read, Capability.Update])
            .AddPath("resources/resources/db-primary/rename", [Capability.Update])
            .AddPath("resources/secrets/db-primary/password", [Capability.Create, Capability.Read])
            .AddPath("resources/v2/connect/mfa/begin", [Capability.Update])
            .AddPath("resources/v2/connect/mfa/verify", [Capability.Update])
            .AddPath("resources/v2/connect/authorize", [Capability.Update])
            .Build();

        Console.WriteLine(hcl);
        // docs:end resources/policy

        Assert.Equal(PolicyShownInTheGuide(), hcl.Trim());
    }

    private static string PolicyShownInTheGuide()
    {
        string path = Path.Combine(DocsRepository.Docs.FullName, "dotnet", "engines", "resources.md");
        string[] lines = File.ReadAllLines(path);
        int opening = Array.FindIndex(lines, line => line.TrimEnd() == "```hcl");
        Assert.True(opening >= 0, $"{path} shows no ```hcl policy block (DOC-011).");
        int closing = Array.FindIndex(lines, opening + 1, line => line.TrimEnd() == "```");
        Assert.True(closing > opening, $"{path}: the ```hcl block is never closed.");
        return string.Join('\n', lines[(opening + 1)..closing]).Trim();
    }

    [Fact]
    public async Task Step_1_write_a_resource_record_then_rename_it()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(ResourceRoute, Json(200, "{}"));
        vault.Server.SetRouteResponse(RenameRoute, Json(200, "{}"));

        // docs:begin resources/write-and-rename
        using JsonDocument record = JsonDocument.Parse("""{"type":"database","host":"db1.internal","port":5432}""");
        await client.Resources.WriteAsync("db-primary", record.RootElement.Clone());

        BastionVault.IntegrationSdk.Response? read = await client.Resources.ReadAsync("db-primary");
        // RSC-002: Resources.Read is a plain, unredacted record - unlike Resources.Secrets below.
        Console.WriteLine($"resource record: {read!.Raw}");

        // Rename migrates the resource's secrets, shares, groups and ownership records with it.
        await client.Resources.RenameAsync("db-primary", "db-primary-east");
        // docs:end resources/write-and-rename

        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task Step_2_attach_a_redacting_secret_to_a_resource()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(SecretRoute, Json(200, SecretBody()));

        // docs:begin resources/secrets
        using JsonDocument value = JsonDocument.Parse("""{"value":"correct horse battery staple"}""");
        await client.Resources.Secrets.WriteAsync("db-primary", "password", value.RootElement.Clone());

        // RSC-002: unlike Resources.Read, every field value here comes back wrapped in SecretString.
        ResourceSecret? secret = await client.Resources.Secrets.ReadAsync("db-primary", "password");
        Console.WriteLine($"fields stored: {string.Join(", ", secret!.Data.Keys)}");
        // docs:end resources/secrets

        Assert.Contains("value", secret!.Data.Keys);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task Step_3_run_the_connect_mfa_flow_before_authorizing()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(MfaBeginRoute, Json(200, "{}"));
        vault.Server.SetRouteResponse(MfaVerifyRoute, Json(200, MfaVerifyBody()));
        vault.Server.SetRouteResponse(AuthorizeRoute, Json(200, "{}"));

        // docs:begin resources/connect-mfa
        await client.Resources.Connect.MfaBeginAsync(new ConnectMfaBeginRequest
        {
            Resource = "db-primary",
            ProfileId = "profile-1",
        });

        ConnectMfaVerifyResult verified = await client.Resources.Connect.MfaVerifyAsync(new ConnectMfaVerifyRequest
        {
            Resource = "db-primary",
            ProfileId = "profile-1",
            Method = "totp",
            TotpCode = "045678",
        });
        // The ticket is single-use and redacting from the moment it is parsed off the wire (RSC-001).

        await client.Resources.Connect.AuthorizeAsync(new ConnectAuthorizeRequest
        {
            Resource = "db-primary",
            ProfileId = "profile-1",
            ConnectTicket = verified.ConnectTicket,
        });
        Console.WriteLine("connect authorized");
        // docs:end resources/connect-mfa

        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task The_whole_program_writes_a_resource_and_attaches_a_secret()
    {
        vault.Server.ClearRouteResponses();

        // docs:begin resources/complete
        using BastionVaultClient client = new();

        try
        {
            vault.Server.SetRouteResponse(ResourceRoute, Json(200, "{}"));
            using JsonDocument record = JsonDocument.Parse("""{"type":"database","host":"db1.internal"}""");
            await client.Resources.WriteAsync("db-primary", record.RootElement.Clone());
            Console.WriteLine("resource written");

            vault.Server.SetRouteResponse(SecretRoute, Json(200, SecretBody()));
            ResourceSecret? secret = await client.Resources.Secrets.ReadAsync("db-primary", "password");
            Console.WriteLine($"secret fields: {string.Join(", ", secret!.Data.Keys)}");
        }
        catch (BastionVaultException e)
        {
            Console.Error.WriteLine($"{e.Code}: {e.Message} ({e.Hint}); retryable: {e.Retryable}");
            throw;
        }
        // docs:end resources/complete

        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task What_can_go_wrong_maps_every_resources_code_to_a_remedy()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(async () =>
        {
            // docs:begin resources/handling-errors
            try
            {
                // RSC-001: an empty resource is refused client-side, before any request is sent.
                await client.Resources.Connect.MfaBeginAsync(new ConnectMfaBeginRequest { Resource = string.Empty, ProfileId = "profile-1" });
            }
            catch (BastionVaultException e)
            {
                string remedy = e.Code switch
                {
                    ErrorCodes.InputInvalidArgument => "`resource` is required (RSC-001); supply the resource's name",
                    ErrorCodes.AuthUnauthenticated => "connect MFA requires an authenticated caller (RSC-001)",
                    ErrorCodes.AuthSecondFactorFailed => "the second factor was rejected; retry MfaVerify with a fresh code",
                    ErrorCodes.NotFoundPathNotFound => "check the resource name, or write it first",
                    _ => "look the code up in the error reference",
                };
                Console.Error.WriteLine($"{e.Code}: {e.Message} - {remedy}");
                throw;
            }
            // docs:end resources/handling-errors
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

    private static string SecretBody() => Compact("""{"data":{"value":"correct horse battery staple"}}""");

    private static string MfaVerifyBody() => Compact("""{"data":{"connect_ticket":"ticket-abc123"}}""");
}
