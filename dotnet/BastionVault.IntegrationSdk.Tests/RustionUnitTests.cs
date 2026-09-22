using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;
using BastionVault.IntegrationSdk.Testing;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// 12 §Rustion (M10 slice e, DR-0017): the bastion-integration mount, reached from
/// <c>Client.Rustion</c>. RUS-001 (<c>Recordings.Download</c>'s chunk loop and digest
/// verification), RUS-002 (chunk/blob node-locality) and RUS-003 (the seven error-token mappings).
/// </summary>
public sealed class RustionUnitTests
{
    private const string Address = "https://vault.example.com:8200";
    private const string Rid = "rec_abc123";

    // ---------------------------------------------------------------- Targets / Master / Authority / DeploymentId

    [Fact]
    public async Task Targets_list_create_read_write_delete_probe_probeAll_health_and_refreshListeners_round_trip()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"keys":["t-1"]}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"id":"t-1"}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"id":"t-1","kind":"ssh"}}"""));
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(200, body: Json("""{"data":{"reachable":true}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"reachable":true}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"healthy":true}}"""));
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        Assert.Equal(["t-1"], await client.Rustion.Targets.ListAsync());
        Assert.Equal("LIST", transport.Requests[0].Method);
        Assert.EndsWith("/v1/rustion/targets/", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        using JsonDocument target = JsonDocument.Parse("""{"kind":"ssh"}""");
        Response? created = await client.Rustion.Targets.CreateAsync(target.RootElement);
        Assert.Equal("t-1", created!.Data!["id"].GetString());
        Assert.Equal("POST", transport.Requests[1].Method);
        Assert.EndsWith("/v1/rustion/targets/", transport.Requests[1].Uri.AbsoluteUri, StringComparison.Ordinal);

        Response? read = await client.Rustion.Targets.ReadAsync("t-1");
        Assert.Equal("ssh", read!.Data!["kind"].GetString());
        Assert.EndsWith("/v1/rustion/targets/t-1", transport.Requests[2].Uri.AbsoluteUri, StringComparison.Ordinal);

        _ = await client.Rustion.Targets.WriteAsync("t-1", target.RootElement);
        Assert.Equal("PUT", transport.Requests[3].Method);

        await client.Rustion.Targets.DeleteAsync("t-1");
        Assert.Equal("DELETE", transport.Requests[4].Method);

        Response? probe = await client.Rustion.Targets.ProbeAsync("t-1");
        Assert.True(probe!.Data!["reachable"].GetBoolean());
        Assert.EndsWith("/v1/rustion/targets/t-1/probe", transport.Requests[5].Uri.AbsoluteUri, StringComparison.Ordinal);

        _ = await client.Rustion.Targets.ProbeAllAsync();
        Assert.EndsWith("/v1/rustion/targets/probe", transport.Requests[6].Uri.AbsoluteUri, StringComparison.Ordinal);

        Response? health = await client.Rustion.Targets.HealthAsync();
        Assert.True(health!.Data!["healthy"].GetBoolean());
        Assert.EndsWith("/v1/rustion/targets/health", transport.Requests[7].Uri.AbsoluteUri, StringComparison.Ordinal);

        await client.Rustion.Targets.RefreshListenersAsync("t-1");
        Assert.EndsWith("/v1/rustion/targets/t-1/listeners/refresh", transport.Requests[8].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Master_readConfig_writeConfig_pubKey_issue_and_rotate_round_trip()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"enabled":true}}"""));
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(200, body: Json("""{"data":{"pubkey":"-----BEGIN PUBLIC KEY-----"}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"issued":true}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"rotated":true}}"""));
        BastionVaultClient client = BuildClient(transport);

        IReadOnlyDictionary<string, JsonElement>? config = await client.Rustion.Master.ReadConfigAsync();
        Assert.True(config!["enabled"].GetBoolean());
        Assert.EndsWith("/v1/rustion/master/config", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        using JsonDocument writeBody = JsonDocument.Parse("""{"enabled":false}""");
        _ = await client.Rustion.Master.WriteConfigAsync(writeBody.RootElement);
        Assert.Equal("POST", transport.Requests[1].Method);

        IReadOnlyDictionary<string, JsonElement>? pubKey = await client.Rustion.Master.PubKeyAsync();
        Assert.Contains("BEGIN PUBLIC KEY", pubKey!["pubkey"].GetString(), StringComparison.Ordinal);
        Assert.EndsWith("/v1/rustion/master/pubkey", transport.Requests[2].Uri.AbsoluteUri, StringComparison.Ordinal);

        Response? issued = await client.Rustion.Master.IssueAsync();
        Assert.True(issued!.Data!["issued"].GetBoolean());
        Assert.EndsWith("/v1/rustion/master/issue", transport.Requests[3].Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Empty(transport.Requests[3].Body.Span.ToArray());

        Response? rotated = await client.Rustion.Master.RotateAsync();
        Assert.True(rotated!.Data!["rotated"].GetBoolean());
        Assert.EndsWith("/v1/rustion/master/rotate", transport.Requests[4].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport issueWithBodyTransport = new();
        issueWithBodyTransport.EnqueueResponse(200, body: Json("""{"data":{"issued":true}}"""));
        BastionVaultClient issueWithBodyClient = BuildClient(issueWithBodyTransport);
        using JsonDocument issueRequest = JsonDocument.Parse("""{"reason":"scheduled"}""");
        _ = await issueWithBodyClient.Rustion.Master.IssueAsync(issueRequest.RootElement);
        Assert.Contains("\"reason\":\"scheduled\"", Encoding.UTF8.GetString(issueWithBodyTransport.Requests[0].Body.Span), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authority_attest_and_deploymentId_round_trip()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"attested":true}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"deployment_id":"dep-1"}}"""));
        BastionVaultClient client = BuildClient(transport);

        using JsonDocument attestBody = JsonDocument.Parse("""{"signature":"abc"}""");
        Response? attested = await client.Rustion.Authority.AttestAsync(attestBody.RootElement);
        Assert.True(attested!.Data!["attested"].GetBoolean());
        Assert.EndsWith("/v1/rustion/authority/attest", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        IReadOnlyDictionary<string, JsonElement>? deploymentId = await client.Rustion.DeploymentIdAsync();
        Assert.Equal("dep-1", deploymentId!["deployment_id"].GetString());
        Assert.EndsWith("/v1/rustion/deployment-id", transport.Requests[1].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Absent_reads_return_null_across_the_raw_idiom_surfaces()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(404);
        transport.EnqueueResponse(404);
        transport.EnqueueResponse(404);
        transport.EnqueueResponse(404);
        transport.EnqueueResponse(404);
        transport.EnqueueResponse(404);
        BastionVaultClient client = BuildClient(transport);

        Assert.Null(await client.Rustion.Master.ReadConfigAsync());
        Assert.Null(await client.Rustion.Master.PubKeyAsync());
        Assert.Null(await client.Rustion.DeploymentIdAsync());
        Assert.Null(await client.Rustion.Policy.ReadGlobalAsync());
        Assert.Null(await client.Rustion.Telemetry.ReadAsync());
        Assert.Null(await client.Rustion.Telemetry.PollAsync());
    }

    [Fact]
    public async Task Blob_and_chunk_raise_an_envelope_mismatch_on_an_empty_response()
    {
        FakeTransport blobTransport = new();
        blobTransport.EnqueueResponse(204);
        BastionVaultClient blobClient = BuildClient(blobTransport);
        BastionVaultException blobError = await Assert.ThrowsAsync<BastionVaultException>(() => blobClient.Rustion.Recordings.BlobAsync(Rid));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, blobError.Code);

        FakeTransport chunkTransport = new();
        chunkTransport.EnqueueResponse(204);
        BastionVaultClient chunkClient = BuildClient(chunkTransport);
        BastionVaultException chunkError = await Assert.ThrowsAsync<BastionVaultException>(() => chunkClient.Rustion.Recordings.ChunkAsync(Rid, 0));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, chunkError.Code);
    }

    // ---------------------------------------------------------------- Session

    [Fact]
    public async Task Session_open_merges_opaque_fields_and_rejects_a_shadowing_or_non_object_bag()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"session_id":"s-1"}}"""));
        BastionVaultClient client = BuildClient(transport);

        using JsonDocument fields = JsonDocument.Parse("""{"resource_name":"db-1"}""");
        Response? opened = await client.Rustion.Session.OpenAsync(new RustionSessionRequest
        {
            CredentialMaterial = new SecretString("cred-v1"),
            Fields = fields.RootElement,
        });

        Assert.Equal("s-1", opened!.Data!["session_id"].GetString());
        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains("\"credential_material\":\"cred-v1\"", body, StringComparison.Ordinal);
        Assert.Contains("\"resource_name\":\"db-1\"", body, StringComparison.Ordinal);

        FakeTransport guardTransport = new();
        BastionVaultClient guardClient = BuildClient(guardTransport);

        using JsonDocument shadowing = JsonDocument.Parse("""{"credential_material":"sneaky"}""");
        BastionVaultException shadowError = await Assert.ThrowsAsync<BastionVaultException>(() => guardClient.Rustion.Session.OpenAsync(
            new RustionSessionRequest { CredentialMaterial = new SecretString("cred-v1"), Fields = shadowing.RootElement }));
        Assert.Equal(ErrorCodes.InputInvalidArgument, shadowError.Code);

        using JsonDocument nonObject = JsonDocument.Parse("""[1,2,3]""");
        BastionVaultException nonObjectError = await Assert.ThrowsAsync<BastionVaultException>(() => guardClient.Rustion.Session.OpenAsync(
            new RustionSessionRequest { CredentialMaterial = new SecretString("cred-v1"), Fields = nonObject.RootElement }));
        Assert.Equal(ErrorCodes.InputInvalidArgument, nonObjectError.Code);

        BastionVaultException emptyCredentialError = await Assert.ThrowsAsync<BastionVaultException>(
            () => guardClient.Rustion.Session.OpenAsync(new RustionSessionRequest { CredentialMaterial = new SecretString(null) }));
        Assert.Equal(ErrorCodes.InputInvalidArgument, emptyCredentialError.Code);

        Assert.Empty(guardTransport.Requests);

        FakeTransport noFieldsTransport = new();
        noFieldsTransport.EnqueueResponse(200, body: Json("""{"data":{"session_id":"s-3"}}"""));
        BastionVaultClient noFieldsClient = BuildClient(noFieldsTransport);
        _ = await noFieldsClient.Rustion.Session.OpenAsync(new RustionSessionRequest { CredentialMaterial = new SecretString("cred-v1") });
        Assert.Equal("{\"credential_material\":\"cred-v1\"}", Encoding.UTF8.GetString(noFieldsTransport.Requests[0].Body.Span));
    }

    [Fact]
    [Requirement("R-33")]
    [Trait("Requirement", "R-33")]
    public async Task Session_openConnectOnly_is_v2_pinned_and_never_leaks_secrets_into_the_query_string()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"session_id":"s-2"}}"""));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Rustion.Session.OpenConnectOnlyAsync(new RustionSessionOpenConnectOnlyRequest
        {
            ResourceName = "db-1",
            SecretId = "secret-abc",
            TargetHost = "10.0.0.5",
            TargetPort = 5432,
            TargetProtocol = "postgres",
            ProfileId = "admin",
            ConnectTicket = new SecretString("tick-xyz"),
        });

        Uri uri = transport.Requests[0].Uri;
        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.StartsWith("/v2/rustion/session/open", uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"secret\"", body, StringComparison.Ordinal);
        Assert.Contains("\"secret_id\":\"secret-abc\"", body, StringComparison.Ordinal);
        Assert.Contains("\"connect_ticket\":\"tick-xyz\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-abc", uri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("tick-xyz", uri.Query, StringComparison.Ordinal);

        FakeTransport minimalTransport = new();
        minimalTransport.EnqueueResponse(200, body: Json("""{"data":{"session_id":"s-3"}}"""));
        BastionVaultClient minimalClient = BuildClient(minimalTransport);
        _ = await minimalClient.Rustion.Session.OpenConnectOnlyAsync(new RustionSessionOpenConnectOnlyRequest
        {
            ResourceName = "db-1",
            SecretId = "secret-abc",
            TargetHost = "10.0.0.5",
            TargetPort = 5432,
            TargetProtocol = "postgres",
        });
        string minimalBody = Encoding.UTF8.GetString(minimalTransport.Requests[0].Body.Span);
        Assert.DoesNotContain("profile_id", minimalBody, StringComparison.Ordinal);
        Assert.DoesNotContain("connect_ticket", minimalBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Session_renew_and_kill_round_trip()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"extended":true}}"""));
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        Response? renewed = await client.Rustion.Session.RenewAsync(new RustionSessionRenewRequest
        {
            BastionId = "b-1",
            SessionId = "s-1",
            CorrelationId = "corr-1",
        });
        Assert.True(renewed!.Data!["extended"].GetBoolean());
        string renewBody = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains("\"extend_secs\":1800", renewBody, StringComparison.Ordinal);
        Assert.EndsWith("/v1/rustion/session/renew", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        _ = await client.Rustion.Session.KillAsync(new RustionSessionKillRequest
        {
            BastionId = "b-1",
            SessionId = "s-1",
            CorrelationId = "corr-1",
        });
        string killBody = Encoding.UTF8.GetString(transport.Requests[1].Body.Span);
        Assert.DoesNotContain("extend_secs", killBody, StringComparison.Ordinal);
        Assert.EndsWith("/v1/rustion/session/kill", transport.Requests[1].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- Recordings: metadata / actions

    [Fact]
    public async Task Recordings_list_read_keystrokes_and_the_action_only_routes_round_trip()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("{\"data\":{\"keys\":[\"" + Rid + "\"]}}"));
        transport.EnqueueResponse(200, body: Json("""{"data":{"duration":120}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"events":[]}}"""));
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        Assert.Equal([Rid], await client.Rustion.Recordings.ListAsync());

        Response? read = await client.Rustion.Recordings.ReadAsync(Rid);
        Assert.Equal(120, read!.Data!["duration"].GetInt32());
        Assert.EndsWith($"/v1/rustion/recordings/{Rid}", transport.Requests[1].Uri.AbsoluteUri, StringComparison.Ordinal);

        Response? keystrokes = await client.Rustion.Recordings.KeystrokesAsync(Rid);
        Assert.Equal(JsonValueKind.Array, keystrokes!.Data!["events"].ValueKind);
        Assert.EndsWith($"/v1/rustion/recordings/{Rid}/keystrokes", transport.Requests[2].Uri.AbsoluteUri, StringComparison.Ordinal);

        await client.Rustion.Recordings.PullAsync();
        Assert.EndsWith("/v1/rustion/recordings/pull", transport.Requests[3].Uri.AbsoluteUri, StringComparison.Ordinal);

        await client.Rustion.Recordings.ReconcileAsync();
        Assert.EndsWith("/v1/rustion/recordings/reconcile", transport.Requests[4].Uri.AbsoluteUri, StringComparison.Ordinal);

        await client.Rustion.Recordings.ReplayLogAsync();
        Assert.EndsWith("/v1/rustion/recordings/replay-log", transport.Requests[5].Uri.AbsoluteUri, StringComparison.Ordinal);

        await client.Rustion.Recordings.IndexKeystrokesAsync();
        Assert.EndsWith("/v1/rustion/recordings/keystrokes/index", transport.Requests[6].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recordings_keystrokeSearch_sends_query_in_the_body_not_a_query_string()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"matches":[]}}"""));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Rustion.Recordings.KeystrokeSearchAsync("password", 25);

        Uri uri = transport.Requests[0].Uri;
        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.EndsWith("/v1/rustion/recordings/keystroke-search", uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.DoesNotContain("password", uri.Query, StringComparison.Ordinal);
        Assert.Contains("\"query\":\"password\"", body, StringComparison.Ordinal);
        Assert.Contains("\"limit\":25", body, StringComparison.Ordinal);

        FakeTransport noLimitTransport = new();
        noLimitTransport.EnqueueResponse(200, body: Json("""{"data":{"matches":[]}}"""));
        BastionVaultClient noLimitClient = BuildClient(noLimitTransport);
        _ = await noLimitClient.Rustion.Recordings.KeystrokeSearchAsync("password");
        Assert.Equal("{\"query\":\"password\"}", Encoding.UTF8.GetString(noLimitTransport.Requests[0].Body.Span));
    }

    [Fact]
    public async Task Recordings_rid_format_is_validated_client_side_before_any_request()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException readError = await Assert.ThrowsAsync<BastionVaultException>(() => client.Rustion.Recordings.ReadAsync("not-a-rid"));
        Assert.Equal(ErrorCodes.InputInvalidArgument, readError.Code);

        BastionVaultException chunkError = await Assert.ThrowsAsync<BastionVaultException>(() => client.Rustion.Recordings.ChunkAsync("not-a-rid", 0));
        Assert.Equal(ErrorCodes.InputInvalidArgument, chunkError.Code);

        BastionVaultException downloadError = await Assert.ThrowsAsync<BastionVaultException>(() => client.Rustion.Recordings.DownloadAsync("not-a-rid"));
        Assert.Equal(ErrorCodes.InputInvalidArgument, downloadError.Code);

        BastionVaultException emptyError = await Assert.ThrowsAsync<BastionVaultException>(() => client.Rustion.Recordings.ReadAsync(string.Empty));
        Assert.Equal(ErrorCodes.InputInvalidArgument, emptyError.Code);

        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task Recordings_chunk_reads_a_single_chunk_and_rejects_a_missing_eof()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: ChunkJson("hello", eof: true));
        BastionVaultClient client = BuildClient(transport);

        RustionRecordingChunk chunk = await client.Rustion.Recordings.ChunkAsync(Rid, 0);
        Assert.Equal("hello", Encoding.UTF8.GetString(chunk.Bytes.Span));
        Assert.True(chunk.Eof);
        Assert.EndsWith($"/v1/rustion/recordings/{Rid}/chunk/0", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport missingEofTransport = new();
        missingEofTransport.EnqueueResponse(200, body: ChunkJson("x"));
        BastionVaultClient missingEofClient = BuildClient(missingEofTransport);

        BastionVaultException error = await Assert.ThrowsAsync<BastionVaultException>(() => missingEofClient.Rustion.Recordings.ChunkAsync(Rid, 0));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, error.Code);
    }

    [Fact]
    public async Task Recordings_chunk_rejects_missing_bytes_b64_a_non_boolean_eof_and_reads_an_explicit_false_digest_verified()
    {
        FakeTransport missingBytesTransport = new();
        missingBytesTransport.EnqueueResponse(200, body: Json("""{"data":{"eof":true}}"""));
        BastionVaultClient missingBytesClient = BuildClient(missingBytesTransport);
        BastionVaultException missingBytesError = await Assert.ThrowsAsync<BastionVaultException>(() => missingBytesClient.Rustion.Recordings.ChunkAsync(Rid, 0));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, missingBytesError.Code);

        FakeTransport nonBooleanEofTransport = new();
        nonBooleanEofTransport.EnqueueResponse(200, body: Json("""{"data":{"bytes_b64":"aGk=","eof":"yes"}}"""));
        BastionVaultClient nonBooleanEofClient = BuildClient(nonBooleanEofTransport);
        BastionVaultException nonBooleanEofError = await Assert.ThrowsAsync<BastionVaultException>(() => nonBooleanEofClient.Rustion.Recordings.ChunkAsync(Rid, 0));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, nonBooleanEofError.Code);

        FakeTransport explicitFalseDigestTransport = new();
        explicitFalseDigestTransport.EnqueueResponse(200, body: ChunkJson("hi", eof: true, digestVerified: false));
        BastionVaultClient explicitFalseDigestClient = BuildClient(explicitFalseDigestTransport);
        RustionRecordingChunk chunk = await explicitFalseDigestClient.Rustion.Recordings.ChunkAsync(Rid, 0);
        Assert.False(chunk.DigestVerified);
    }

    // ---------------------------------------------------------------- Recordings.Download (RUS-001)

    [Fact]
    [Requirement("RUS-001")]
    [Trait("Requirement", "RUS-001")]
    public async Task Download_assembles_multiple_chunks_in_order_and_verifies_a_reported_digest()
    {
        byte[] expected = Encoding.UTF8.GetBytes("hello world");
        string expectedSha256 = Convert.ToHexStringLower(SHA256.HashData(expected));

        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: ChunkJson("hello ", eof: false));
        transport.EnqueueResponse(200, body: ChunkJson("world", eof: true, digestVerified: true, sha256: expectedSha256));
        BastionVaultClient client = BuildClient(transport);

        byte[] downloaded = await client.Rustion.Recordings.DownloadAsync(Rid);

        Assert.Equal(expected, downloaded);
        Assert.EndsWith($"/v1/rustion/recordings/{Rid}/chunk/0", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.EndsWith($"/v1/rustion/recordings/{Rid}/chunk/1", transport.Requests[1].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("RUS-001")]
    [Trait("Requirement", "RUS-001")]
    public async Task Download_skips_verification_when_no_digest_was_ever_reported()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: ChunkJson("no-digest-here", eof: true));
        BastionVaultClient client = BuildClient(transport);

        byte[] downloaded = await client.Rustion.Recordings.DownloadAsync(Rid);

        Assert.Equal("no-digest-here", Encoding.UTF8.GetString(downloaded));
    }

    [Fact]
    [Requirement("RUS-001")]
    [Trait("Requirement", "RUS-001")]
    public async Task Download_raises_DigestMismatch_when_the_assembled_bytes_do_not_match_the_reported_sha256()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: ChunkJson("hello", eof: true, sha256: "0000000000000000000000000000000000000000000000000000000000000000"));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException error = await Assert.ThrowsAsync<BastionVaultException>(() => client.Rustion.Recordings.DownloadAsync(Rid));

        Assert.Equal(ErrorCodes.ProtocolDigestMismatch, error.Code);
    }

    [Fact]
    [Requirement("RUS-001")]
    [Trait("Requirement", "RUS-001")]
    public async Task Download_falls_back_to_blob_when_the_chunk_route_is_unsupported()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(500, body: Json("""{"error":"logical backend path not supported"}"""));
        transport.EnqueueResponse(200, body: ChunkJson("fallback"));
        BastionVaultClient client = BuildClient(transport);

        byte[] downloaded = await client.Rustion.Recordings.DownloadAsync(Rid);

        Assert.Equal("fallback", Encoding.UTF8.GetString(downloaded));
        Assert.EndsWith($"/v1/rustion/recordings/{Rid}/chunk/0", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.EndsWith($"/v1/rustion/recordings/{Rid}/blob", transport.Requests[1].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("RUS-001")]
    [Trait("Requirement", "RUS-001")]
    public async Task Download_blob_fallback_raises_TransportResponseTooLarge_over_the_configured_bound()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(500, body: Json("""{"error":"logical backend path not supported"}"""));
        transport.EnqueueResponse(200, body: ChunkJson(new string('a', 64)));
        // 64 sits above the 46-byte 500 response (so the chunk request's own fallback-triggering
        // failure is unaffected by the bound) and below the ~113-byte blob response the oversized
        // payload produces once base64-encoded — so it is the blob fetch, not the chunk fetch,
        // that trips the bound.
        BastionVaultClient client = BuildClient(transport, o => o.MaxResponseBytes = 64);

        BastionVaultException error = await Assert.ThrowsAsync<BastionVaultException>(() => client.Rustion.Recordings.DownloadAsync(Rid));

        Assert.Equal(ErrorCodes.TransportResponseTooLarge, error.Code);
        Assert.Equal(2, transport.Requests.Count);
        Assert.EndsWith($"/v1/rustion/recordings/{Rid}/blob", transport.Requests[1].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("RUS-001")]
    [Trait("Requirement", "RUS-001")]
    public async Task Chunk_maps_a_416_to_ChunkIndexOutOfRange_with_chunk_count_and_a_409_to_a_non_retryable_ConflictRecordingDigestMismatch()
    {
        FakeTransport rangeTransport = new();
        rangeTransport.EnqueueResponse(416, body: Json("""{"error":"chunk index out of range","chunk_count":3}"""));
        BastionVaultClient rangeClient = BuildClient(rangeTransport);

        BastionVaultException rangeError = await Assert.ThrowsAsync<BastionVaultException>(() => rangeClient.Rustion.Recordings.ChunkAsync(Rid, 9));
        Assert.Equal(ErrorCodes.InputChunkIndexOutOfRange, rangeError.Code);
        Assert.Equal(3L, rangeError.Details["chunk_count"]);

        FakeTransport conflictTransport = new();
        conflictTransport.EnqueueResponse(409, body: Json("""{"error":"recording digest mismatch: sha256 does not match"}"""));
        BastionVaultClient conflictClient = BuildClient(conflictTransport);

        BastionVaultException conflictError = await Assert.ThrowsAsync<BastionVaultException>(() => conflictClient.Rustion.Recordings.ChunkAsync(Rid, 0));
        Assert.Equal(ErrorCodes.ConflictRecordingDigestMismatch, conflictError.Code);
        Assert.False(conflictError.Retryable);
    }

    [Fact]
    [Requirement("RUS-002")]
    [Trait("Requirement", "RUS-002")]
    public async Task Chunk_and_blob_reads_are_excluded_from_failover()
    {
        foreach (bool useBlob in new[] { true, false })
        {
            RecordingTransport transport = new(_ => throw TransportFailureMapper.Map(TransportFailureKind.ConnectionRefused));
            BastionVaultClient client = ArmedClient(transport);

            BastionVaultException failure = useBlob
                ? await Assert.ThrowsAsync<BastionVaultException>(() => client.Rustion.Recordings.BlobAsync(Rid))
                : await Assert.ThrowsAsync<BastionVaultException>(() => client.Rustion.Recordings.ChunkAsync(Rid, 0));

            // One request, to the pinned node only: no health probe, no replay (DSC-045).
            _ = Assert.Single(transport.Urls);
            Assert.Equal(ErrorCodes.DiscoveryNodeUnavailable, failure.Code);
        }
    }

    // ---------------------------------------------------------------- RUS-003

    [Theory]
    [Requirement("RUS-003")]
    [Trait("Requirement", "RUS-003")]
    [InlineData("authority_pending_approval", 403, ErrorCodes.RustionAuthorityPendingApproval)]
    [InlineData("authority_tombstoned", 403, ErrorCodes.RustionAuthorityTombstoned)]
    [InlineData("attestation_mismatch", 403, ErrorCodes.RustionAttestationMismatch)]
    [InlineData("unknown_authority", 401, ErrorCodes.RustionUnknownAuthority)]
    [InlineData("signature_invalid", 401, ErrorCodes.RustionSignatureInvalid)]
    [InlineData("envelope_replay", 409, ErrorCodes.RustionEnvelopeReplay)]
    [InlineData("policy_denied", 403, ErrorCodes.RustionPolicyDenied)]
    public async Task Rustion_error_tokens_map_to_their_BV_RUSTION_code(string serverMessage, int statusCode, string expectedCode)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(statusCode, body: Json($$"""{"error":"{{serverMessage}}"}"""));
        BastionVaultClient client = BuildClient(transport);

        using JsonDocument request = JsonDocument.Parse("""{"signature":"abc"}""");
        BastionVaultException error = await Assert.ThrowsAsync<BastionVaultException>(() => client.Rustion.Authority.AttestAsync(request.RootElement));

        Assert.Equal(expectedCode, error.Code);
    }

    // ---------------------------------------------------------------- Policy / BastionGroups / Dispatcher / Telemetry

    [Fact]
    public async Task Policy_bastionGroups_dispatcher_and_telemetry_round_trip()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"enabled":true}}"""));
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(200, body: Json("""{"data":{"names":["prod"]}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"decision":"allow"}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"uptime":42}}"""));
        BastionVaultClient client = BuildClient(transport);

        IReadOnlyDictionary<string, JsonElement>? global = await client.Rustion.Policy.ReadGlobalAsync();
        Assert.True(global!["enabled"].GetBoolean());
        Assert.EndsWith("/v1/rustion/policy/global", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        await client.Rustion.BastionGroups.DeleteAsync("prod");
        Assert.EndsWith("/v1/rustion/bastion-groups/prod", transport.Requests[1].Uri.AbsoluteUri, StringComparison.Ordinal);

        Assert.Equal(["prod"], (await client.Rustion.Telemetry.PollAsync())!["names"].EnumerateArray().Select(e => e.GetString()));
        Assert.EndsWith("/v1/rustion/telemetry/poll", transport.Requests[2].Uri.AbsoluteUri, StringComparison.Ordinal);

        using JsonDocument previewRequest = JsonDocument.Parse("""{"resource":"db-1"}""");
        Response? preview = await client.Rustion.Dispatcher.PreviewAsync(previewRequest.RootElement);
        Assert.Equal("allow", preview!.Data!["decision"].GetString());
        Assert.EndsWith("/v1/rustion/dispatcher/preview", transport.Requests[3].Uri.AbsoluteUri, StringComparison.Ordinal);

        IReadOnlyDictionary<string, JsonElement>? telemetry = await client.Rustion.Telemetry.ReadAsync();
        Assert.Equal(42, telemetry!["uptime"].GetInt32());
        Assert.EndsWith("/v1/rustion/telemetry", transport.Requests[4].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Policy_writeGlobal_type_assetGroup_resource_forceRustion_and_effective_round_trip()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(200, body: Json("""{"data":{"type":"ssh"}}"""));
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(200, body: Json("""{"data":{"group":"prod"}}"""));
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(200, body: Json("""{"data":{"resource":"db-1"}}"""));
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(200, body: Json("""{"data":{"forced":true}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"decision":"allow"}}"""));
        BastionVaultClient client = BuildClient(transport);

        using JsonDocument policy = JsonDocument.Parse("""{"enabled":true}""");
        _ = await client.Rustion.Policy.WriteGlobalAsync(policy.RootElement);
        Assert.Equal("PUT", transport.Requests[0].Method);
        Assert.EndsWith("/v1/rustion/policy/global", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        Response? readType = await client.Rustion.Policy.ReadTypeAsync("ssh");
        Assert.Equal("ssh", readType!.Data!["type"].GetString());
        Assert.EndsWith("/v1/rustion/policy/type/ssh", transport.Requests[1].Uri.AbsoluteUri, StringComparison.Ordinal);

        _ = await client.Rustion.Policy.WriteTypeAsync("ssh", policy.RootElement);
        Assert.Equal("PUT", transport.Requests[2].Method);
        Assert.EndsWith("/v1/rustion/policy/type/ssh", transport.Requests[2].Uri.AbsoluteUri, StringComparison.Ordinal);

        await client.Rustion.Policy.DeleteTypeAsync("ssh");
        Assert.Equal("DELETE", transport.Requests[3].Method);
        Assert.EndsWith("/v1/rustion/policy/type/ssh", transport.Requests[3].Uri.AbsoluteUri, StringComparison.Ordinal);

        Response? readAssetGroup = await client.Rustion.Policy.ReadAssetGroupAsync("prod");
        Assert.Equal("prod", readAssetGroup!.Data!["group"].GetString());
        Assert.EndsWith("/v1/rustion/policy/asset-group/prod", transport.Requests[4].Uri.AbsoluteUri, StringComparison.Ordinal);

        _ = await client.Rustion.Policy.WriteAssetGroupAsync("prod", policy.RootElement);
        Assert.Equal("PUT", transport.Requests[5].Method);
        Assert.EndsWith("/v1/rustion/policy/asset-group/prod", transport.Requests[5].Uri.AbsoluteUri, StringComparison.Ordinal);

        await client.Rustion.Policy.DeleteAssetGroupAsync("prod");
        Assert.Equal("DELETE", transport.Requests[6].Method);
        Assert.EndsWith("/v1/rustion/policy/asset-group/prod", transport.Requests[6].Uri.AbsoluteUri, StringComparison.Ordinal);

        Response? readResource = await client.Rustion.Policy.ReadResourceAsync("db-1");
        Assert.Equal("db-1", readResource!.Data!["resource"].GetString());
        Assert.EndsWith("/v1/rustion/policy/resource/db-1", transport.Requests[7].Uri.AbsoluteUri, StringComparison.Ordinal);

        _ = await client.Rustion.Policy.WriteResourceAsync("db-1", policy.RootElement);
        Assert.Equal("PUT", transport.Requests[8].Method);
        Assert.EndsWith("/v1/rustion/policy/resource/db-1", transport.Requests[8].Uri.AbsoluteUri, StringComparison.Ordinal);

        await client.Rustion.Policy.DeleteResourceAsync("db-1");
        Assert.Equal("DELETE", transport.Requests[9].Method);
        Assert.EndsWith("/v1/rustion/policy/resource/db-1", transport.Requests[9].Uri.AbsoluteUri, StringComparison.Ordinal);

        using JsonDocument forceRequest = JsonDocument.Parse("""{"resource":"db-1"}""");
        Response? forced = await client.Rustion.Policy.ForceRustionAsync(forceRequest.RootElement);
        Assert.True(forced!.Data!["forced"].GetBoolean());
        Assert.EndsWith("/v1/rustion/policy/force-rustion", transport.Requests[10].Uri.AbsoluteUri, StringComparison.Ordinal);

        using JsonDocument effectiveRequest = JsonDocument.Parse("""{"resource":"db-1"}""");
        Response? effective = await client.Rustion.Policy.EffectiveAsync(effectiveRequest.RootElement);
        Assert.Equal("allow", effective!.Data!["decision"].GetString());
        Assert.EndsWith("/v1/rustion/policy/effective", transport.Requests[11].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BastionGroups_list_read_and_write_round_trip()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"keys":["prod"]}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"members":["bastion-1"]}}"""));
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        Assert.Equal(["prod"], await client.Rustion.BastionGroups.ListAsync());
        Assert.Equal("LIST", transport.Requests[0].Method);
        Assert.EndsWith("/v1/rustion/bastion-groups/", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        Response? read = await client.Rustion.BastionGroups.ReadAsync("prod");
        Assert.Equal(["bastion-1"], read!.Data!["members"].EnumerateArray().Select(e => e.GetString()));
        Assert.EndsWith("/v1/rustion/bastion-groups/prod", transport.Requests[1].Uri.AbsoluteUri, StringComparison.Ordinal);

        using JsonDocument group = JsonDocument.Parse("""{"members":["bastion-1"]}""");
        _ = await client.Rustion.BastionGroups.WriteAsync("prod", group.RootElement);
        Assert.Equal("PUT", transport.Requests[2].Method);
        Assert.EndsWith("/v1/rustion/bastion-groups/prod", transport.Requests[2].Uri.AbsoluteUri, StringComparison.Ordinal);
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

    /// <summary>A discovery-mode client with the pin and the candidate set already seeded, as <c>SysAdminUnitTests.ArmedClient</c> does.</summary>
    private static BastionVaultClient ArmedClient(ITransport transport)
    {
        BastionVaultClient client = new(
            new BastionVaultClientOptions
            {
                Address = "vault.corp.example",
                Token = "s.FAKE-token-0000000000000000",
                Transport = transport,
                RateGate = new RateGate { RatePerSecond = 0 },
                RetryPolicy = new RetryPolicy { MaxAttempts = 3, InitialBackoff = TimeSpan.Zero },
            },
            EnvironmentSource.None);

        string[] urls = ["https://bv-1.corp.example:8200", "https://bv-2.corp.example:8200"];
        client.Context.Discovery.Seed(
            new NodeSelection(urls[0], NodeState.ActiveLeader, null),
            urls.Select(url => new Uri(url, UriKind.Absolute))
                .Select(uri => new Candidate(uri.AbsoluteUri.TrimEnd('/'), uri.Host, uri.Port, null, null))
                .ToArray());
        return client;
    }

    private static string ToBase64(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    /// <summary>
    /// Builds a chunk response body via concatenation rather than raw-string interpolation — a JSON
    /// body's nested closing braces collide with the raw string's own <c>{{</c>/<c>}}</c> hole
    /// markers once an interpolated value sits next to them.
    /// </summary>
    private static ReadOnlyMemory<byte> ChunkJson(string text, bool? eof = null, bool? digestVerified = null, string? sha256 = null)
    {
        StringBuilder builder = new();
        _ = builder.Append("{\"data\":{\"bytes_b64\":\"").Append(ToBase64(text)).Append('"');
        if (eof is { } eofValue)
        {
            _ = builder.Append(",\"eof\":").Append(eofValue ? "true" : "false");
        }

        if (digestVerified is { } digestVerifiedValue)
        {
            _ = builder.Append(",\"digest_verified\":").Append(digestVerifiedValue ? "true" : "false");
        }

        if (sha256 is not null)
        {
            _ = builder.Append(",\"sha256\":\"").Append(sha256).Append('"');
        }

        _ = builder.Append("}}");
        return Json(builder.ToString());
    }

    private static ReadOnlyMemory<byte> Json(string json)
    {
        return Encoding.UTF8.GetBytes(json);
    }

    /// <summary>A transport that answers from a delegate and records the URLs it was asked for.</summary>
    private sealed class RecordingTransport : ITransport
    {
        private readonly Func<TransportRequest, TransportResponse> answer;
        private readonly List<string> urls = [];

        public RecordingTransport(Func<TransportRequest, TransportResponse> answer)
        {
            this.answer = answer;
        }

        public IReadOnlyList<string> Urls => urls.ToArray();

        public bool SupportsCustomVerbs => true;

        public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            urls.Add(request.Uri.ToString());
            return Task.FromResult(answer(request));
        }
    }
}
