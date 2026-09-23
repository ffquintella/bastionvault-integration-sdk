using System.Text.Json;
using BastionVault.IntegrationSdk.DocsSamples.Infrastructure;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.DocsSamples.Samples;

/// <summary>
/// Every executed sample shown by <c>docs/dotnet/engines/identity.md</c> (D6). Section 17 carries
/// no dedicated usage guide for this <c>identity/</c>-mount surface, so this page — and these
/// samples — are built directly from <c>12-other-engines-and-identity.md</c>'s Identity table
/// (IDN-001, IDN-002). Reuses <see cref="MockVaultFixture"/>'s server: routes are bound per test on
/// <see cref="MockVaultFixture.Server"/> and cleared after, so no test leaks a route to another.
/// </summary>
public sealed class IdentitySamples : IClassFixture<MockVaultFixture>
{
    private const string SelfRoute = "/v1/identity/entity/self";
    private const string AliasesRoute = "/v1/identity/entity/aliases";
    private const string GroupRoute = "/v1/identity/group/user/db-admins";

    // base64url("secret/app/db"), no padding (IDN-001: the SDK encodes this, never the caller).
    private const string SharingRoute = "/v1/identity/sharing/by-target/kv-secret/c2VjcmV0L2FwcC9kYg/alice";
    private const string SharingListRoute = "/v1/identity/sharing/by-target/kv-secret/c2VjcmV0L2FwcC9kYg";
    private const string ForMeRoute = "/v1/identity/sharing/for-me";

    private readonly MockVaultFixture vault;

    public IdentitySamples(MockVaultFixture vault)
    {
        this.vault = vault;
    }

    [Fact]
    public void The_policy_this_guide_needs_is_the_policy_PolicyBuilder_builds()
    {
        // docs:begin identity/policy
        string hcl = new PolicyBuilder()
            .AddPath("identity/entity/self", [Capability.Read])
            .AddPath("identity/entity/aliases", [Capability.Read])
            .AddPath("identity/group/user/db-admins", [Capability.Create, Capability.Read])
            .AddPath("identity/sharing/by-target/*", [Capability.Read, Capability.Create, Capability.Update, Capability.List])
            .AddPath("identity/sharing/for-me", [Capability.List])
            .Build();

        Console.WriteLine(hcl);
        // docs:end identity/policy

        Assert.Equal(PolicyShownInTheGuide(), hcl.Trim());
    }

    private static string PolicyShownInTheGuide()
    {
        string path = Path.Combine(DocsRepository.Docs.FullName, "dotnet", "engines", "identity.md");
        string[] lines = File.ReadAllLines(path);
        int opening = Array.FindIndex(lines, line => line.TrimEnd() == "```hcl");
        Assert.True(opening >= 0, $"{path} shows no ```hcl policy block (DOC-011).");
        int closing = Array.FindIndex(lines, opening + 1, line => line.TrimEnd() == "```");
        Assert.True(closing > opening, $"{path}: the ```hcl block is never closed.");
        return string.Join('\n', lines[(opening + 1)..closing]).Trim();
    }

    [Fact]
    public async Task Step_1_read_the_calling_tokens_own_entity_and_aliases()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(SelfRoute, Json(200, SelfBody()));
        vault.Server.SetRouteResponse(AliasesRoute, Json(200, AliasesBody()));

        // docs:begin identity/self-and-aliases
        // R-35: no fixture captures this route against a real server, so EntitySelf/Aliases ship
        // on ordinary unit coverage rather than fixture conformance (see "What can go wrong" below).
        EntitySelf self = await client.Identity.SelfAsync();
        Console.WriteLine($"{self.Username} via {self.MountPath}, entity {self.EntityId}");

        IReadOnlyList<JsonElement> aliases = await client.Identity.AliasesAsync();
        Console.WriteLine($"{aliases.Count} alias(es) on this entity");
        // docs:end identity/self-and-aliases

        Assert.Equal("alice", self.Username);
        Assert.Single(aliases);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task Step_2_write_and_read_a_user_group()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(GroupRoute, Json(200, "{}"));

        // docs:begin identity/groups
        await client.Identity.Groups.WriteAsync("user", "db-admins", new IdentityGroupSpec
        {
            Description = "Database administrators",
            Members = ["alice", "bob"],
            Policies = ["db-admin"],
        });

        vault.Server.SetRouteResponse(GroupRoute, Json(200, GroupBody()));
        IdentityGroup? group = await client.Identity.Groups.ReadAsync("user", "db-admins");
        Console.WriteLine($"{group!.Members.Count} member(s), policies: {string.Join(", ", group.Policies)}");
        // docs:end identity/groups

        Assert.Equal(2, group!.Members.Count);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task Step_3_share_a_secret_directly_then_check_who_shared_with_me()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(SharingRoute, Json(200, "{}"));
        vault.Server.SetRouteResponse(SharingListRoute, Json(200, """{"data":{"keys":["alice"]}}"""));
        vault.Server.SetRouteResponse(ForMeRoute, Json(200, ForMeBody()));

        // docs:begin identity/sharing
        // IDN-001: the SDK base64url-encodes `target` itself; pass the plain path, never a
        // pre-encoded string.
        await client.Identity.Sharing.PutAsync("kv-secret", "secret/app/db", "alice", new IdentitySharingSpec
        {
            GranteeKind = "entity",
            Capabilities = ["read"],
            ExpiresAt = DateTimeOffset.Parse("2027-01-01T00:00:00Z"),
        });

        IReadOnlyList<string> grantees = await client.Identity.Sharing.ListByTargetAsync("kv-secret", "secret/app/db");
        Console.WriteLine($"shared with: {string.Join(", ", grantees)}");

        // IDN-002: a group share appears here only when its policy carries
        // metadata.group_shared_resources = "true" server-side - nothing client-side enforces that.
        IdentitySharingForMe forMe = await client.Identity.Sharing.ForMeAsync();
        Console.WriteLine($"{forMe.Entries.Count} share(s) visible to entity {forMe.EntityId}");
        // docs:end identity/sharing

        Assert.Contains("alice", grantees);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task The_whole_program_reads_self_and_lists_shares()
    {
        vault.Server.ClearRouteResponses();

        // docs:begin identity/complete
        using BastionVaultClient client = new();

        try
        {
            vault.Server.SetRouteResponse(SelfRoute, Json(200, SelfBody()));
            EntitySelf self = await client.Identity.SelfAsync();
            Console.WriteLine($"logged in as {self.Username}, entity {self.EntityId}");

            vault.Server.SetRouteResponse(ForMeRoute, Json(200, ForMeBody()));
            IdentitySharingForMe forMe = await client.Identity.Sharing.ForMeAsync();
            Console.WriteLine($"{forMe.Entries.Count} resource(s) shared with me");
        }
        catch (BastionVaultException e)
        {
            Console.Error.WriteLine($"{e.Code}: {e.Message} ({e.Hint}); retryable: {e.Retryable}");
            throw;
        }
        // docs:end identity/complete

        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task What_can_go_wrong_maps_every_identity_code_to_a_remedy()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(async () =>
        {
            // docs:begin identity/handling-errors
            try
            {
                // A `..` path segment is refused client-side in `kind`/`name`/`target`/`grantee`,
                // no request sent - the same guard `Logical.*` uses everywhere else in this SDK.
                await client.Identity.Groups.ReadAsync("..", "db-admins");
            }
            catch (BastionVaultException e)
            {
                string remedy = e.Code switch
                {
                    ErrorCodes.InputInvalidArgument => "`kind`/`name`/`target`/`grantee` was empty or contained a `..` segment; fix the argument named in the message",
                    ErrorCodes.AuthzPermissionDenied => "extend the calling token's policy to cover this path",
                    ErrorCodes.NotFoundPathNotFound => "check the group, sharing target, or owner id; write it first",
                    _ => "look the code up in the error reference",
                };
                Console.Error.WriteLine($"{e.Code}: {e.Message} - {remedy}");
                throw;
            }
            // docs:end identity/handling-errors
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

    private static string SelfBody() => Compact("""
        {
          "data": {
            "entity_id": "entity-1",
            "username": "alice",
            "mount_path": "auth/appid/",
            "role_name": "ops",
            "aliases": [{"mount_path": "auth/appid/", "name": "alice"}]
          }
        }
        """);

    private static string AliasesBody() => Compact("""
        {"data":[{"mount_path":"auth/appid/","name":"alice"}]}
        """);

    private static string GroupBody() => Compact("""
        {"data":{"description":"Database administrators","members":["alice","bob"],"policies":["db-admin"]}}
        """);

    private static string ForMeBody() => Compact("""
        {"data":{"entity_id":"entity-1","group_shared_resources":false,"entries":[{"target_kind":"kv-secret","target_path":"secret/app/db"}]}}
        """);
}
