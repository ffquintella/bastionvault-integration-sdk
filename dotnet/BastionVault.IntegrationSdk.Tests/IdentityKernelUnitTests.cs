using System.Text;
using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;
using BastionVault.IntegrationSdk.Testing;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// 12 — identity kernel (M10 slice a, DR-0017): <c>Identity.Self/Aliases/Groups/Sharing/Owner</c>,
/// against hand-built responses (D-M10-4, R-35: no <c>identity.self</c> fixture exists).
/// </summary>
public sealed class IdentityKernelUnitTests
{
    private const string Address = "https://vault.example.com:8200";

    // ---------------------------------------------------------------- Self / Aliases

    [Fact]
    public async Task Self_returns_the_wire_fields_is_v1_and_never_pins_v2()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"entity_id":"e-1","username":"alice","mount_path":"auth/userpass/","role_name":"default","primary_mount":"auth/userpass/","primary_name":"alice","created_at":"2026-01-01T00:00:00Z","aliases":[{"mount_path":"auth/userpass/"}]}}"""));
        BastionVaultClient client = BuildClient(transport);

        EntitySelf self = await client.Identity.SelfAsync();

        Assert.Equal("e-1", self.EntityId);
        Assert.Equal("alice", self.Username);
        Assert.Equal("auth/userpass/", self.MountPath);
        Assert.Equal("default", self.RoleName);
        Assert.Equal("auth/userpass/", self.PrimaryMount);
        Assert.Equal("alice", self.PrimaryName);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), self.CreatedAt);
        _ = Assert.Single(self.Aliases!);
        Assert.Equal("GET", transport.Requests[0].Method);
        Assert.EndsWith("/v1/identity/entity/self", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Self_with_no_data_raises_a_protocol_error_rather_than_an_empty_record()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":null}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Identity.SelfAsync());

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    public async Task Aliases_reads_a_bare_array_and_a_data_wrapped_array_and_is_empty_on_404()
    {
        FakeTransport bareTransport = new();
        bareTransport.EnqueueResponse(200, body: Json("""[{"mount_path":"auth/userpass/"},{"mount_path":"auth/ldap/"}]"""));
        IReadOnlyList<JsonElement> bare = await BuildClient(bareTransport).Identity.AliasesAsync();
        Assert.Equal(2, bare.Count);
        Assert.EndsWith("/v1/identity/entity/aliases", bareTransport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport wrappedTransport = new();
        wrappedTransport.EnqueueResponse(200, body: Json("""{"data":[{"mount_path":"auth/userpass/"}]}"""));
        IReadOnlyList<JsonElement> wrapped = await BuildClient(wrappedTransport).Identity.AliasesAsync();
        _ = Assert.Single(wrapped);

        FakeTransport emptyTransport = new();
        emptyTransport.EnqueueResponse(404);
        Assert.Empty(await BuildClient(emptyTransport).Identity.AliasesAsync());
    }

    [Fact]
    public async Task Aliases_reads_the_measured_data_dot_aliases_nested_shape()
    {
        // Measured against bvault 0.44.5: identity/entity/aliases wraps its array one level
        // deeper than the generic Shape A envelope, as `data.aliases`.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"aliases":[{"mount_path":"auth/userpass/"},{"mount_path":"auth/ldap/"}]}}"""));

        IReadOnlyList<JsonElement> aliases = await BuildClient(transport).Identity.AliasesAsync();

        Assert.Equal(2, aliases.Count);
    }

    [Fact]
    public async Task Aliases_is_empty_when_data_is_an_object_but_the_nested_key_is_absent_or_not_an_array()
    {
        FakeTransport absentKeyTransport = new();
        absentKeyTransport.EnqueueResponse(200, body: Json("""{"data":{"other":"field"}}"""));
        Assert.Empty(await BuildClient(absentKeyTransport).Identity.AliasesAsync());

        FakeTransport notArrayTransport = new();
        notArrayTransport.EnqueueResponse(200, body: Json("""{"data":{"aliases":"not-an-array"}}"""));
        Assert.Empty(await BuildClient(notArrayTransport).Identity.AliasesAsync());
    }

    // ---------------------------------------------------------------- Groups

    [Fact]
    public async Task Groups_list_read_write_delete_and_history_round_trip()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"keys":["platform","ops"]}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"description":"platform team","members":["alice","bob"],"policies":["default"]}}"""));
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(200, body: Json("""{"data":[{"members":["alice"]}]}"""));
        BastionVaultClient client = BuildClient(transport);

        IReadOnlyList<string> names = await client.Identity.Groups.ListAsync("user");
        Assert.Equal(["platform", "ops"], names);
        Assert.Equal("LIST", transport.Requests[0].Method);
        Assert.EndsWith("/v1/identity/group/user", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        IdentityGroup? group = await client.Identity.Groups.ReadAsync("user", "platform");
        Assert.Equal("platform team", group!.Description);
        Assert.Equal(["alice", "bob"], group.Members);
        Assert.Equal(["default"], group.Policies);
        Assert.EndsWith("/v1/identity/group/user/platform", transport.Requests[1].Uri.AbsoluteUri, StringComparison.Ordinal);

        await client.Identity.Groups.WriteAsync(
            "user", "platform", new IdentityGroupSpec { Description = "platform team", Members = ["alice", "bob"], Policies = ["default"] });
        Assert.Equal("PUT", transport.Requests[2].Method);
        string writeBody = Encoding.UTF8.GetString(transport.Requests[2].Body.Span);
        Assert.Contains("\"description\":\"platform team\"", writeBody, StringComparison.Ordinal);
        Assert.Contains("\"members\":[\"alice\",\"bob\"]", writeBody, StringComparison.Ordinal);

        await client.Identity.Groups.DeleteAsync("user", "platform");
        Assert.Equal("DELETE", transport.Requests[3].Method);

        IReadOnlyList<JsonElement> history = await client.Identity.Groups.HistoryAsync("user", "platform");
        _ = Assert.Single(history);
        Assert.EndsWith("/v1/identity/group/user/platform/history", transport.Requests[4].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Groups_read_and_history_are_null_or_empty_on_404()
    {
        FakeTransport readTransport = new();
        readTransport.EnqueueResponse(404);
        Assert.Null(await BuildClient(readTransport).Identity.Groups.ReadAsync("user", "ghost"));

        FakeTransport historyTransport = new();
        historyTransport.EnqueueResponse(404);
        Assert.Empty(await BuildClient(historyTransport).Identity.Groups.HistoryAsync("user", "ghost"));
    }

    [Fact]
    public async Task Groups_reject_an_empty_kind_or_name_before_sending()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Identity.Groups.ListAsync(string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Identity.Groups.ReadAsync("user", string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Identity.Groups.ReadAsync(string.Empty, "platform"));
        Assert.Empty(transport.Requests);
    }

    // ---------------------------------------------------------------- Sharing (IDN-001)

    [Fact]
    [Requirement("IDN-001")]
    [Trait("Requirement", "IDN-001")]
    public async Task Sharing_get_delete_and_list_by_target_base64url_encode_the_plain_target()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"target_kind":"kv-secret"}"""));
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(200, body: Json("""{"data":{"keys":["platform"]}}"""));
        BastionVaultClient client = BuildClient(transport);
        const string encodedTarget = "c2VjcmV0L2FwcC9kYg";

        _ = await client.Identity.Sharing.GetAsync("kv-secret", "secret/app/db", "platform");
        Assert.EndsWith(
            $"/v1/identity/sharing/by-target/kv-secret/{encodedTarget}/platform", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        await client.Identity.Sharing.DeleteAsync("kv-secret", "secret/app/db", "platform");
        Assert.Equal("DELETE", transport.Requests[1].Method);
        Assert.EndsWith(
            $"/v1/identity/sharing/by-target/kv-secret/{encodedTarget}/platform", transport.Requests[1].Uri.AbsoluteUri, StringComparison.Ordinal);

        IReadOnlyList<string> grantees = await client.Identity.Sharing.ListByTargetAsync("kv-secret", "secret/app/db");
        Assert.Equal(["platform"], grantees);
        Assert.Equal("LIST", transport.Requests[2].Method);
        Assert.EndsWith(
            $"/v1/identity/sharing/by-target/kv-secret/{encodedTarget}", transport.Requests[2].Uri.AbsoluteUri, StringComparison.Ordinal);

        // No literal '/' survives the encoding, so a plain multi-segment path cannot change how
        // many path segments the request has.
        Assert.DoesNotContain('/', encodedTarget);
    }

    [Fact]
    [Requirement("IDN-001")]
    [Trait("Requirement", "IDN-001")]
    public async Task Sharing_put_rejects_a_null_spec_and_an_empty_kind_or_target_before_sending()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.Identity.Sharing.PutAsync("kv-secret", "secret/app/db", "platform", null!));
        _ = await Assert.ThrowsAsync<ArgumentException>(
            () => client.Identity.Sharing.PutAsync(string.Empty, "secret/app/db", "platform", new IdentitySharingSpec()));
        _ = await Assert.ThrowsAsync<ArgumentException>(
            () => client.Identity.Sharing.PutAsync("kv-secret", string.Empty, "platform", new IdentitySharingSpec()));
        Assert.Empty(transport.Requests);
    }

    // ---------------------------------------------------------------- Sharing (IDN-002)

    [Fact]
    [Requirement("IDN-002")]
    [Trait("Requirement", "IDN-002")]
    public async Task Sharing_for_me_and_list_by_grantee_round_trip_and_the_group_filter_is_documented()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"keys":["kv-secret/secret/app/db"]}}"""));
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"entity_id":"e-1","group_shared_resources":true,"entries":[{"target_path":"secret/app/db"}]}}"""));
        BastionVaultClient client = BuildClient(transport);

        IReadOnlyList<string> targets = await client.Identity.Sharing.ListByGranteeAsync("platform");
        Assert.Equal(["kv-secret/secret/app/db"], targets);
        Assert.EndsWith("/v1/identity/sharing/by-grantee/platform", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        IdentitySharingForMe forMe = await client.Identity.Sharing.ForMeAsync();
        Assert.Equal("e-1", forMe.EntityId);
        Assert.True(forMe.GroupSharedResources);
        _ = Assert.Single(forMe.Entries);
        Assert.Equal("LIST", transport.Requests[1].Method);
        Assert.EndsWith("/v1/identity/sharing/for-me", transport.Requests[1].Uri.AbsoluteUri, StringComparison.Ordinal);

        // IDN-002 is a documentation MUST with nothing for the SDK to enforce at runtime, so the
        // MUST is discharged by the doc comment on the result type; its presence is asserted here.
        string typesSource = File.ReadAllText(Path.Combine(RepositoryRoot(), "dotnet", "BastionVault.IntegrationSdk", "IdentityKernelTypes.cs"));
        Assert.Contains("metadata.group_shared_resources", typesSource, StringComparison.Ordinal);
        Assert.Contains("IdentitySharingForMe", typesSource, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- Owner

    [Fact]
    public async Task Owner_read_write_and_delete_exchange_an_opaque_body()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"owner":"alice"}}"""));
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        Response? read = await client.Identity.Owner.ReadAsync("kv", "secret/app/db");
        Assert.Equal("alice", read!.Data!["owner"].GetString());
        Assert.EndsWith("/v1/identity/owner/kv/secret%2Fapp%2Fdb", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        using JsonDocument spec = JsonDocument.Parse("""{"owner":"bob"}""");
        _ = await client.Identity.Owner.WriteAsync("kv", "secret/app/db", spec.RootElement);
        Assert.Equal("PUT", transport.Requests[1].Method);
        Assert.Contains("\"owner\":\"bob\"", Encoding.UTF8.GetString(transport.Requests[1].Body.Span), StringComparison.Ordinal);

        await client.Identity.Owner.DeleteAsync("kv", "secret/app/db");
        Assert.Equal("DELETE", transport.Requests[2].Method);
    }

    [Fact]
    public async Task Owner_write_rejects_an_undefined_JsonElement_client_side()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Identity.Owner.WriteAsync("kv", "secret/app/db", default));

        Assert.Equal(ErrorCodes.InputInvalidArgument, exception.Code);
        Assert.Empty(transport.Requests);
    }

    // ---------------------------------------------------------------- branch coverage

    [Fact]
    public async Task Self_with_an_empty_response_raises_a_protocol_error()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Identity.SelfAsync());

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    public async Task Self_without_an_aliases_field_leaves_Aliases_null()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"entity_id":"e-1"}}"""));
        BastionVaultClient client = BuildClient(transport);

        EntitySelf self = await client.Identity.SelfAsync();

        Assert.Null(self.Aliases);
    }

    [Fact]
    public async Task Groups_list_is_empty_on_404()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(404);

        Assert.Empty(await BuildClient(transport).Identity.Groups.ListAsync("user"));
    }

    [Fact]
    public async Task Groups_history_falls_through_to_empty_when_the_body_is_an_object_with_no_data_array()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"note":"no data key here"}"""));

        Assert.Empty(await BuildClient(transport).Identity.Groups.HistoryAsync("user", "platform"));
    }

    [Fact]
    public async Task Groups_history_reads_the_measured_data_dot_entries_nested_shape()
    {
        // Measured against bvault 0.44.5: identity/group/{kind}/{name}/history wraps its array
        // as `data.entries`, not `data` itself.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"entries":[{"op":"create"},{"op":"update"}]}}"""));

        IReadOnlyList<JsonElement> history = await BuildClient(transport).Identity.Groups.HistoryAsync("user", "platform");

        Assert.Equal(2, history.Count);
    }

    [Fact]
    public async Task Sharing_get_list_by_target_list_by_grantee_and_for_me_are_absent_or_empty_on_404()
    {
        FakeTransport getTransport = new();
        getTransport.EnqueueResponse(404);
        Assert.Null(await BuildClient(getTransport).Identity.Sharing.GetAsync("kv-secret", "secret/app/db", "platform"));

        FakeTransport byTargetTransport = new();
        byTargetTransport.EnqueueResponse(404);
        Assert.Empty(await BuildClient(byTargetTransport).Identity.Sharing.ListByTargetAsync("kv-secret", "secret/app/db"));

        FakeTransport byGranteeTransport = new();
        byGranteeTransport.EnqueueResponse(404);
        Assert.Empty(await BuildClient(byGranteeTransport).Identity.Sharing.ListByGranteeAsync("platform"));

        FakeTransport forMeTransport = new();
        forMeTransport.EnqueueResponse(404);
        IdentitySharingForMe forMe = await BuildClient(forMeTransport).Identity.Sharing.ForMeAsync();
        Assert.Null(forMe.EntityId);
        Assert.Null(forMe.GroupSharedResources);
        Assert.Empty(forMe.Entries);
    }

    [Fact]
    public async Task Sharing_for_me_reads_a_false_flag_and_defaults_entries_to_empty_when_absent()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"entity_id":"e-1","group_shared_resources":false}}"""));

        IdentitySharingForMe forMe = await BuildClient(transport).Identity.Sharing.ForMeAsync();

        Assert.False(forMe.GroupSharedResources);
        Assert.Empty(forMe.Entries);
    }

    [Fact]
    [Requirement("IDN-001")]
    [Trait("Requirement", "IDN-001")]
    public async Task Sharing_put_sends_expires_at_when_set_and_omits_capabilities_when_unset()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        await client.Identity.Sharing.PutAsync(
            "kv-secret", "secret/app/db", "platform",
            new IdentitySharingSpec { ExpiresAt = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero) });

        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains("\"expires_at\":\"2027-01-01T00:00:00Z\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("capabilities", body, StringComparison.Ordinal);
        Assert.DoesNotContain("grantee_kind", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Base64Url_round_trips_and_rejects_a_null_argument()
    {
        Assert.Equal("c2VjcmV0L2FwcC9kYg", Base64Url.Encode("secret/app/db"));
        Assert.Equal("secret/app/db", Base64Url.Decode("c2VjcmV0L2FwcC9kYg"));
        Assert.Equal("YXBw", Base64Url.Encode("app"));
        Assert.Equal("app", Base64Url.Decode("YXBw"));

        _ = Assert.Throws<ArgumentNullException>(() => Base64Url.Encode(null!));
        _ = Assert.Throws<ArgumentNullException>(() => Base64Url.Decode(null!));
    }

    // ---------------------------------------------------------------- helpers

    private static string RepositoryRoot()
    {
        return new FixtureRepository().RepositoryRoot;
    }

    private static BastionVaultClient BuildClient(ITransport transport, Action<BastionVaultClientOptions>? configure = null)
    {
        BastionVaultClientOptions options = new()
        {
            Address = Address,
            Token = "s.FAKE-token-0000000000000000",
            Transport = transport,
            RateGate = new RateGate { RatePerSecond = 0 },
            RetryPolicy = new RetryPolicy { MaxAttempts = 1 },
        };
        configure?.Invoke(options);
        return new BastionVaultClient(options, EnvironmentSource.None);
    }

    private static ReadOnlyMemory<byte> Json(string json)
    {
        return Encoding.UTF8.GetBytes(json);
    }
}
