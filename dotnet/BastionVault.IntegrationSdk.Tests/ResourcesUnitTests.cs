using System.Text;
using System.Text.Json;
using BastionVault.IntegrationSdk.Testing;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// 12 §Resources (M10 slice b, DR-0017): the <c>resource</c> engine, reached from
/// <c>Client.Resources</c>. RSC-001 (connect-MFA error mapping and the client-side <c>resource</c>
/// guard) and RSC-002 (the secrets-vs-record redaction asymmetry).
/// </summary>
public sealed class ResourcesUnitTests
{
    private const string Address = "https://vault.example.com:8200";

    [Fact]
    public async Task Types_list_search_read_write_delete_history_and_rename_round_trip()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"types":["database"]}}"""));
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(200, body: Json("""{"data":{"keys":["prod-db"]}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"resources":["prod-db"]}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"type":"database"}}"""));
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(200, body: Json("""{"data":[{"type":"database"}]}"""));
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        IReadOnlyDictionary<string, JsonElement>? types = await client.Resources.ReadTypesAsync();
        Assert.Equal(["database"], types!["types"].EnumerateArray().Select(e => e.GetString()));
        Assert.EndsWith("/v1/resources/config/types", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        using JsonDocument schema = JsonDocument.Parse("""{"type":"database"}""");
        await client.Resources.WriteTypesAsync(schema.RootElement);
        Assert.Equal("POST", transport.Requests[1].Method);

        IReadOnlyList<string> names = await client.Resources.ListAsync();
        Assert.Equal(["prod-db"], names);
        Assert.Equal("LIST", transport.Requests[2].Method);
        Assert.EndsWith("/v1/resources/resources/", transport.Requests[2].Uri.AbsoluteUri, StringComparison.Ordinal);

        Response? search = await client.Resources.SearchAsync(new ResourceSearchQuery { Q = "db", Type = "database", Offset = 0, Limit = 10 });
        Assert.Equal(["prod-db"], search!.Data!["resources"].EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("POST", transport.Requests[3].Method);
        Assert.EndsWith("/v1/resources/resources/search", transport.Requests[3].Uri.AbsoluteUri, StringComparison.Ordinal);
        string searchBody = Encoding.UTF8.GetString(transport.Requests[3].Body.Span);
        Assert.Contains("\"q\":\"db\"", searchBody, StringComparison.Ordinal);
        Assert.Contains("\"offset\":0", searchBody, StringComparison.Ordinal);
        Assert.DoesNotContain("q=db", transport.Requests[3].Uri.Query, StringComparison.Ordinal);

        // RSC-002: the plain record is not wrapped in SecretString.
        Response? read = await client.Resources.ReadAsync("prod-db");
        Assert.Equal("database", read!.Data!["type"].GetString());
        Assert.EndsWith("/v1/resources/resources/prod-db", transport.Requests[4].Uri.AbsoluteUri, StringComparison.Ordinal);

        using JsonDocument record = JsonDocument.Parse("""{"type":"database"}""");
        _ = await client.Resources.WriteAsync("prod-db", record.RootElement);
        Assert.Equal("PUT", transport.Requests[5].Method);

        await client.Resources.DeleteAsync("prod-db");
        Assert.Equal("DELETE", transport.Requests[6].Method);

        IReadOnlyList<JsonElement> history = await client.Resources.HistoryAsync("prod-db");
        _ = Assert.Single(history);
        Assert.EndsWith("/v1/resources/resources/prod-db/history", transport.Requests[7].Uri.AbsoluteUri, StringComparison.Ordinal);

        await client.Resources.RenameAsync("prod-db", "prod-db-2");
        Assert.Equal("POST", transport.Requests[8].Method);
        Assert.EndsWith("/v1/resources/resources/prod-db/rename", transport.Requests[8].Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("\"new_name\":\"prod-db-2\"", Encoding.UTF8.GetString(transport.Requests[8].Body.Span), StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_read_and_history_are_empty_or_null_on_404_and_every_operation_rejects_empty_arguments()
    {
        FakeTransport listTransport = new();
        listTransport.EnqueueResponse(404);
        Assert.Empty(await BuildClient(listTransport).Resources.ListAsync());

        FakeTransport readTransport = new();
        readTransport.EnqueueResponse(404);
        Assert.Null(await BuildClient(readTransport).Resources.ReadAsync("ghost"));

        FakeTransport historyTransport = new();
        historyTransport.EnqueueResponse(404);
        Assert.Empty(await BuildClient(historyTransport).Resources.HistoryAsync("ghost"));

        FakeTransport typesTransport = new();
        typesTransport.EnqueueResponse(404);
        Assert.Null(await BuildClient(typesTransport).Resources.ReadTypesAsync());

        FakeTransport secretsListTransport = new();
        secretsListTransport.EnqueueResponse(404);
        Assert.Empty(await BuildClient(secretsListTransport).Resources.Secrets.ListAsync("prod-db"));

        FakeTransport guardTransport = new();
        BastionVaultClient client = BuildClient(guardTransport);
        using JsonDocument record = JsonDocument.Parse("{}");

        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Resources.ReadAsync(string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Resources.WriteAsync(string.Empty, record.RootElement));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Resources.DeleteAsync(string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Resources.RenameAsync("x", string.Empty));
        Assert.Empty(guardTransport.Requests);
    }

    [Fact]
    public async Task Search_with_every_field_unset_sends_an_empty_body()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"resources":[]}}"""));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Resources.SearchAsync(new ResourceSearchQuery());

        Assert.Equal("{}", Encoding.UTF8.GetString(transport.Requests[0].Body.Span));
    }

    // ---------------------------------------------------------------- Secrets (RSC-002)

    [Fact]
    [Requirement("RSC-002")]
    [Trait("Requirement", "RSC-002")]
    public async Task Secrets_list_read_write_delete_history_and_read_version_round_trip_and_the_value_is_redacting()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"keys":["db-password"]}}"""));
        // The value carries an embedded quote and backslash so a `GetRawText`-style leak (the
        // JSON-quoted-and-escaped text) is distinguishable from the actual decoded value.
        transport.EnqueueResponse(200, body: Json("""{"lease_id":"not-secret","data":{"secret":"hunt\"er2\\path"}}"""));
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(200, body: Json("""{"data":[{"version":1}]}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"secret":"older"}}"""));
        BastionVaultClient client = BuildClient(transport);

        IReadOnlyList<string> keys = await client.Resources.Secrets.ListAsync("prod-db");
        Assert.Equal(["db-password"], keys);
        Assert.Equal("LIST", transport.Requests[0].Method);
        Assert.EndsWith("/v1/resources/secrets/prod-db/", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        // RSC-002/F3: only the field's own value is redacting, decoded — not the JSON-quoted
        // wire text (`GetRawText` would leak `"hunt\"er2\\path"`, 17 characters, quotes and all)
        // — and envelope metadata (`lease_id`) sitting alongside `data` is not in the map at all.
        ResourceSecret? secret = await client.Resources.Secrets.ReadAsync("prod-db", "db-password");
        Assert.NotNull(secret);
        Assert.Equal("hunt\"er2\\path", secret!.Data["secret"].Reveal());
        Assert.Equal("[REDACTED]", secret.Data["secret"].ToString());
        Assert.DoesNotContain("lease_id", secret.Data.Keys);
        Assert.EndsWith("/v1/resources/secrets/prod-db/db-password", transport.Requests[1].Uri.AbsoluteUri, StringComparison.Ordinal);

        using JsonDocument value = JsonDocument.Parse("""{"secret":"new-value"}""");
        _ = await client.Resources.Secrets.WriteAsync("prod-db", "db-password", value.RootElement);
        Assert.Equal("PUT", transport.Requests[2].Method);
        Assert.Contains("\"secret\":\"new-value\"", Encoding.UTF8.GetString(transport.Requests[2].Body.Span), StringComparison.Ordinal);

        await client.Resources.Secrets.DeleteAsync("prod-db", "db-password");
        Assert.Equal("DELETE", transport.Requests[3].Method);

        IReadOnlyList<JsonElement> history = await client.Resources.Secrets.HistoryAsync("prod-db", "db-password");
        _ = Assert.Single(history);
        Assert.EndsWith("/v1/resources/secrets/prod-db/db-password/history", transport.Requests[4].Uri.AbsoluteUri, StringComparison.Ordinal);

        ResourceSecret? version = await client.Resources.Secrets.ReadVersionAsync("prod-db", "db-password", 1);
        Assert.Equal("older", version!.Data["secret"].Reveal());
        Assert.EndsWith("/v1/resources/secrets/prod-db/db-password/version/1", transport.Requests[5].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("RSC-002")]
    [Trait("Requirement", "RSC-002")]
    public async Task Secrets_read_falls_back_to_raw_text_for_a_non_string_field_value()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"rotation_count":42}}"""));
        BastionVaultClient client = BuildClient(transport);

        ResourceSecret? secret = await client.Resources.Secrets.ReadAsync("prod-db", "db-password");

        Assert.Equal("42", secret!.Data["rotation_count"].Reveal());
    }

    [Fact]
    public async Task Secrets_read_and_read_version_are_null_on_404_and_reject_empty_arguments()
    {
        FakeTransport readTransport = new();
        readTransport.EnqueueResponse(404);
        Assert.Null(await BuildClient(readTransport).Resources.Secrets.ReadAsync("prod-db", "ghost"));

        FakeTransport versionTransport = new();
        versionTransport.EnqueueResponse(404);
        Assert.Null(await BuildClient(versionTransport).Resources.Secrets.ReadVersionAsync("prod-db", "ghost", 1));

        FakeTransport guardTransport = new();
        BastionVaultClient client = BuildClient(guardTransport);
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Resources.Secrets.ReadAsync(string.Empty, "key"));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Resources.Secrets.ReadAsync("prod-db", string.Empty));
        Assert.Empty(guardTransport.Requests);
    }

    [Fact]
    [Requirement("RSC-002")]
    [Trait("Requirement", "RSC-002")]
    public async Task Secrets_read_raises_a_protocol_error_when_the_response_carries_no_data_object()
    {
        // A non-object body (here a bare array) resolves to a present Response with a null Data
        // (TRN-040's Shape A/B split) — RSC-002 requires the field map, so this is a protocol
        // error, not an absent secret.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""[1,2,3]"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Resources.Secrets.ReadAsync("prod-db", "db-password"));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    // ---------------------------------------------------------------- Connect (RSC-001)

    [Fact]
    [Requirement("RSC-001")]
    [Trait("Requirement", "RSC-001")]
    public async Task Connect_mfa_begin_verify_and_authorize_round_trip_and_the_ticket_is_redacting()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"factors":["totp"]}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"connect_ticket":"tick-abc123"}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"authorized":true}}"""));
        BastionVaultClient client = BuildClient(transport);

        Response? begin = await client.Resources.Connect.MfaBeginAsync(new ConnectMfaBeginRequest { Resource = "prod-db", ProfileId = "admin" });
        Assert.Equal(["totp"], begin!.Data!["factors"].EnumerateArray().Select(e => e.GetString()));
        Assert.EndsWith("/v1/resources/v2/connect/mfa/begin", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        ConnectMfaVerifyResult verified = await client.Resources.Connect.MfaVerifyAsync(
            new ConnectMfaVerifyRequest { Resource = "prod-db", ProfileId = "admin", Method = "totp", TotpCode = "123456" });
        Assert.Equal("tick-abc123", verified.ConnectTicket.Reveal());
        Assert.Equal("[REDACTED]", verified.ConnectTicket.ToString());
        Assert.EndsWith("/v1/resources/v2/connect/mfa/verify", transport.Requests[1].Uri.AbsoluteUri, StringComparison.Ordinal);

        Response? authorized = await client.Resources.Connect.AuthorizeAsync(
            new ConnectAuthorizeRequest { Resource = "prod-db", ProfileId = "admin", ConnectTicket = verified.ConnectTicket });
        Assert.True(authorized!.Data!["authorized"].GetBoolean());

        // R-33 guard: the ticket travels in the POST body only, never a query string or path.
        Uri authorizeUri = transport.Requests[2].Uri;
        string authorizeBody = Encoding.UTF8.GetString(transport.Requests[2].Body.Span);
        Assert.DoesNotContain("tick-abc123", authorizeUri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("\"connect_ticket\":\"tick-abc123\"", authorizeBody, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("RSC-001")]
    [Trait("Requirement", "RSC-001")]
    public async Task Connect_mfa_verify_accepts_a_fido2_credential_and_authorize_omits_an_absent_ticket()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"connect_ticket":"tick-fido"}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"authorized":true}}"""));
        BastionVaultClient client = BuildClient(transport);

        ConnectMfaVerifyResult verified = await client.Resources.Connect.MfaVerifyAsync(
            new ConnectMfaVerifyRequest { Resource = "prod-db", ProfileId = "admin", Method = "fido2", Credential = "assertion-blob" });
        Assert.Equal("tick-fido", verified.ConnectTicket.Reveal());
        Assert.Contains("\"credential\":\"assertion-blob\"", Encoding.UTF8.GetString(transport.Requests[0].Body.Span), StringComparison.Ordinal);

        _ = await client.Resources.Connect.AuthorizeAsync(new ConnectAuthorizeRequest { Resource = "prod-db", ProfileId = "admin" });
        string authorizeBody = Encoding.UTF8.GetString(transport.Requests[1].Body.Span);
        Assert.DoesNotContain("connect_ticket", authorizeBody, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("RSC-001")]
    [Trait("Requirement", "RSC-001")]
    public async Task Connect_mfa_verify_raises_a_protocol_error_when_the_response_carries_no_ticket()
    {
        FakeTransport noDataTransport = new();
        noDataTransport.EnqueueResponse(204);
        BastionVaultException noDataError = await Assert.ThrowsAsync<BastionVaultException>(
            () => BuildClient(noDataTransport).Resources.Connect.MfaVerifyAsync(
                new ConnectMfaVerifyRequest { Resource = "prod-db", ProfileId = "admin", Method = "totp" }));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, noDataError.Code);

        FakeTransport missingTicketTransport = new();
        missingTicketTransport.EnqueueResponse(200, body: Json("""{"data":{"factors":["totp"]}}"""));
        BastionVaultException missingTicketError = await Assert.ThrowsAsync<BastionVaultException>(
            () => BuildClient(missingTicketTransport).Resources.Connect.MfaVerifyAsync(
                new ConnectMfaVerifyRequest { Resource = "prod-db", ProfileId = "admin", Method = "totp" }));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, missingTicketError.Code);
    }

    [Fact]
    [Requirement("RSC-001")]
    [Trait("Requirement", "RSC-001")]
    public async Task Connect_operations_reject_an_empty_resource_before_sending()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException beginError = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Resources.Connect.MfaBeginAsync(new ConnectMfaBeginRequest { Resource = string.Empty, ProfileId = "admin" }));
        Assert.Equal(ErrorCodes.InputInvalidArgument, beginError.Code);

        BastionVaultException verifyError = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Resources.Connect.MfaVerifyAsync(
                new ConnectMfaVerifyRequest { Resource = string.Empty, ProfileId = "admin", Method = "totp" }));
        Assert.Equal(ErrorCodes.InputInvalidArgument, verifyError.Code);

        BastionVaultException authorizeError = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Resources.Connect.AuthorizeAsync(new ConnectAuthorizeRequest { Resource = string.Empty, ProfileId = "admin" }));
        Assert.Equal(ErrorCodes.InputInvalidArgument, authorizeError.Code);

        Assert.Empty(transport.Requests);
    }

    [Theory]
    [InlineData(401, "no authenticated caller", ErrorCodes.AuthUnauthenticated)]
    [InlineData(403, "second-factor verification failed", ErrorCodes.AuthSecondFactorFailed)]
    [Requirement("RSC-001")]
    [Trait("Requirement", "RSC-001")]
    public async Task Connect_mfa_verify_maps_server_errors_through_the_standard_recognition_pipeline(
        int statusCode, string serverMessage, string expectedCode)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(statusCode, body: Json($$"""{"errors":["{{serverMessage}}"]}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Resources.Connect.MfaVerifyAsync(
                new ConnectMfaVerifyRequest { Resource = "prod-db", ProfileId = "admin", Method = "totp" }));

        Assert.Equal(expectedCode, exception.Code);
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
