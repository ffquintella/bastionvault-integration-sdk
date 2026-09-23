using System.Text;
using System.Text.Json;
using BastionVault.IntegrationSdk.Testing;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// 12 §Files (M10 slice b, DR-0017): the <c>files</c> engine, reached from <c>Client.Files</c>.
/// FIL-001: content is <c>byte[]</c> in the public API; the SDK base64-encodes/decodes it and the
/// existing TRN-032 body-size guard runs on the encoded body.
/// </summary>
public sealed class FilesUnitTests
{
    private const string Address = "https://vault.example.com:8200";

    [Fact]
    [Requirement("FIL-001")]
    [Trait("Requirement", "FIL-001")]
    public async Task List_create_read_update_delete_and_content_round_trip_bytes_never_a_base64_string()
    {
        byte[] original = [0x00, 0x01, 0xFF, 0x10, 0x20];
        string encoded = Convert.ToBase64String(original);

        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"keys":["f-1"]}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"id":"f-1"}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"name":"report.pdf"}}"""));
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(200, body: Json($$$"""{"data":{"content_base64":"{{{encoded}}}"}}"""));
        BastionVaultClient client = BuildClient(transport);

        IReadOnlyList<string> names = await client.Files.ListAsync();
        Assert.Equal(["f-1"], names);
        Assert.Equal("LIST", transport.Requests[0].Method);
        Assert.EndsWith("/v1/files/files/", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        string id = await client.Files.CreateAsync(new FileCreateRequest
        {
            Name = "report.pdf",
            Resource = "prod-db",
            MimeType = "application/pdf",
            Tags = ["quarterly"],
            Notes = "Q1 report",
            Content = original,
        });
        Assert.Equal("f-1", id);
        Assert.Equal("POST", transport.Requests[1].Method);
        string createBody = Encoding.UTF8.GetString(transport.Requests[1].Body.Span);
        Assert.Contains($"\"content_base64\":\"{encoded}\"", createBody, StringComparison.Ordinal);
        Assert.DoesNotContain("\"content\":", createBody, StringComparison.Ordinal);

        Response? read = await client.Files.ReadAsync("f-1");
        Assert.Equal("report.pdf", read!.Data!["name"].GetString());
        Assert.EndsWith("/v1/files/files/f-1", transport.Requests[2].Uri.AbsoluteUri, StringComparison.Ordinal);

        _ = await client.Files.UpdateAsync("f-1", new FileUpdateRequest
        {
            Name = "report-v2.pdf",
            Resource = "prod-db-2",
            MimeType = "application/pdf",
            Tags = ["quarterly", "final"],
            Notes = "Updated",
        });
        Assert.Equal("PUT", transport.Requests[3].Method);
        string updateBody = Encoding.UTF8.GetString(transport.Requests[3].Body.Span);
        Assert.Contains("\"name\":\"report-v2.pdf\"", updateBody, StringComparison.Ordinal);
        Assert.Contains("\"resource\":\"prod-db-2\"", updateBody, StringComparison.Ordinal);
        Assert.Contains("\"mime_type\":\"application/pdf\"", updateBody, StringComparison.Ordinal);
        Assert.Contains("\"notes\":\"Updated\"", updateBody, StringComparison.Ordinal);

        await client.Files.DeleteAsync("f-1");
        Assert.Equal("DELETE", transport.Requests[4].Method);

        byte[] content = await client.Files.ContentAsync("f-1");
        Assert.Equal(original, content);
        Assert.EndsWith("/v1/files/files/f-1/content", transport.Requests[5].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task History_versions_read_version_version_content_restore_and_repoint_resource_round_trip()
    {
        byte[] original = [0x01, 0x02, 0x03];
        string encoded = Convert.ToBase64String(original);

        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":[{"version":1}]}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":[{"version":1},{"version":2}]}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"version":1}}"""));
        transport.EnqueueResponse(200, body: Json($$$"""{"data":{"content_base64":"{{{encoded}}}"}}"""));
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        IReadOnlyList<JsonElement> history = await client.Files.HistoryAsync("f-1");
        _ = Assert.Single(history);
        Assert.EndsWith("/v1/files/files/f-1/history", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        IReadOnlyList<JsonElement> versions = await client.Files.VersionsAsync("f-1");
        Assert.Equal(2, versions.Count);
        Assert.EndsWith("/v1/files/files/f-1/versions", transport.Requests[1].Uri.AbsoluteUri, StringComparison.Ordinal);

        Response? version = await client.Files.ReadVersionAsync("f-1", 1);
        Assert.Equal(1, version!.Data!["version"].GetInt32());
        Assert.EndsWith("/v1/files/files/f-1/versions/1", transport.Requests[2].Uri.AbsoluteUri, StringComparison.Ordinal);

        byte[] versionContent = await client.Files.VersionContentAsync("f-1", 1);
        Assert.Equal(original, versionContent);
        Assert.EndsWith("/v1/files/files/f-1/versions/1/content", transport.Requests[3].Uri.AbsoluteUri, StringComparison.Ordinal);

        await client.Files.RestoreVersionAsync("f-1", 1);
        Assert.Equal("POST", transport.Requests[4].Method);
        Assert.EndsWith("/v1/files/files/f-1/versions/1/restore", transport.Requests[4].Uri.AbsoluteUri, StringComparison.Ordinal);

        await client.Files.RepointResourceAsync("old-db", "new-db");
        Assert.Equal("POST", transport.Requests[5].Method);
        Assert.EndsWith("/v1/files/files/repoint-resource", transport.Requests[5].Uri.AbsoluteUri, StringComparison.Ordinal);
        string repointBody = Encoding.UTF8.GetString(transport.Requests[5].Body.Span);
        Assert.Contains("\"old_resource\":\"old-db\"", repointBody, StringComparison.Ordinal);
        Assert.Contains("\"new_resource\":\"new-db\"", repointBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task History_reads_the_measured_data_dot_entries_nested_shape()
    {
        // Measured against bvault 0.44.5: {mount}/files/{id}/history wraps its array as
        // `data.entries`, not `data` itself.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"entries":[{"op":"create"}]}}"""));

        IReadOnlyList<JsonElement> history = await BuildClient(transport).Files.HistoryAsync("f-1");

        _ = Assert.Single(history);
    }

    [Fact]
    public async Task Versions_reads_the_measured_data_dot_versions_nested_shape()
    {
        // Measured against bvault 0.44.5 (ITG-S24): {mount}/files/{id}/versions wraps its array
        // as `data.versions`, not `data` itself.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"versions":[{"version":1}]}}"""));

        IReadOnlyList<JsonElement> versions = await BuildClient(transport).Files.VersionsAsync("f-1");

        _ = Assert.Single(versions);
    }

    [Fact]
    public async Task Read_history_and_versions_are_null_or_empty_on_404_and_every_operation_rejects_empty_arguments()
    {
        FakeTransport readTransport = new();
        readTransport.EnqueueResponse(404);
        Assert.Null(await BuildClient(readTransport).Files.ReadAsync("ghost"));

        FakeTransport historyTransport = new();
        historyTransport.EnqueueResponse(404);
        Assert.Empty(await BuildClient(historyTransport).Files.HistoryAsync("ghost"));

        FakeTransport versionsTransport = new();
        versionsTransport.EnqueueResponse(404);
        Assert.Empty(await BuildClient(versionsTransport).Files.VersionsAsync("ghost"));

        FakeTransport listTransport = new();
        listTransport.EnqueueResponse(404);
        Assert.Empty(await BuildClient(listTransport).Files.ListAsync());

        FakeTransport syncListTransport = new();
        syncListTransport.EnqueueResponse(404);
        Assert.Empty(await BuildClient(syncListTransport).Files.Sync.ListAsync("f-1"));

        FakeTransport guardTransport = new();
        BastionVaultClient client = BuildClient(guardTransport);
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Files.ReadAsync(string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Files.DeleteAsync(string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Files.RepointResourceAsync(string.Empty, "new-db"));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Files.RepointResourceAsync("old-db", string.Empty));
        Assert.Empty(guardTransport.Requests);
    }

    [Fact]
    [Requirement("FIL-001")]
    [Trait("Requirement", "FIL-001")]
    public async Task Oversized_content_is_rejected_client_side_after_base64_encoding()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);
        // Raw bytes chosen so the base64-encoded body (≈4/3 the raw length, plus the JSON
        // envelope) exceeds the 32 MiB limit even though the raw content alone would not.
        byte[] rawContent = new byte[26_000_000];

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Files.CreateAsync(new FileCreateRequest { Name = "big.bin", Content = rawContent }));

        Assert.Equal(ErrorCodes.InputBodyTooLarge, exception.Code);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    [Requirement("FIL-001")]
    [Trait("Requirement", "FIL-001")]
    public async Task Create_and_content_raise_a_protocol_error_when_the_response_carries_no_body_or_no_id()
    {
        FakeTransport noDataTransport = new();
        noDataTransport.EnqueueResponse(204);
        BastionVaultException noDataError = await Assert.ThrowsAsync<BastionVaultException>(
            () => BuildClient(noDataTransport).Files.CreateAsync(new FileCreateRequest { Name = "f", Content = new byte[] { 1 } }));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, noDataError.Code);

        FakeTransport missingIdTransport = new();
        missingIdTransport.EnqueueResponse(200, body: Json("""{"data":{"name":"f"}}"""));
        BastionVaultException missingIdError = await Assert.ThrowsAsync<BastionVaultException>(
            () => BuildClient(missingIdTransport).Files.CreateAsync(new FileCreateRequest { Name = "f", Content = new byte[] { 1 } }));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, missingIdError.Code);

        FakeTransport noContentTransport = new();
        noContentTransport.EnqueueResponse(204);
        BastionVaultException noContentError = await Assert.ThrowsAsync<BastionVaultException>(
            () => BuildClient(noContentTransport).Files.ContentAsync("f-1"));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, noContentError.Code);

        FakeTransport noVersionContentTransport = new();
        noVersionContentTransport.EnqueueResponse(200, body: Json("""{"data":{"other":"field"}}"""));
        BastionVaultException noVersionContentError = await Assert.ThrowsAsync<BastionVaultException>(
            () => BuildClient(noVersionContentTransport).Files.VersionContentAsync("f-1", 1));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, noVersionContentError.Code);
    }

    [Fact]
    [Requirement("FIL-001")]
    [Trait("Requirement", "FIL-001")]
    public async Task Content_maps_malformed_or_null_base64_to_a_coded_error_never_a_raw_exception()
    {
        // F1: malformed base64 must never escape as a bare FormatException.
        FakeTransport malformedTransport = new();
        malformedTransport.EnqueueResponse(200, body: Json("""{"data":{"content_base64":"not valid base64!!"}}"""));
        BastionVaultException malformedError = await Assert.ThrowsAsync<BastionVaultException>(
            () => BuildClient(malformedTransport).Files.ContentAsync("f-1"));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, malformedError.Code);

        // F1 secondary: a present-but-null content_base64 is an envelope mismatch, not "".
        FakeTransport nullFieldTransport = new();
        nullFieldTransport.EnqueueResponse(200, body: Json("""{"data":{"content_base64":null}}"""));
        BastionVaultException nullFieldError = await Assert.ThrowsAsync<BastionVaultException>(
            () => BuildClient(nullFieldTransport).Files.ContentAsync("f-1"));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, nullFieldError.Code);
    }

    // ---------------------------------------------------------------- Sync

    [Fact]
    public async Task Sync_list_write_delete_push_and_tick_round_trip()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"keys":["nightly"]}}"""));
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        IReadOnlyList<string> names = await client.Files.Sync.ListAsync("f-1");
        Assert.Equal(["nightly"], names);
        Assert.Equal("LIST", transport.Requests[0].Method);
        Assert.EndsWith("/v1/files/files/f-1/sync/", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        using JsonDocument credentials = JsonDocument.Parse("""{"host":"fs.example.com","share":"backups"}""");
        _ = await client.Files.Sync.WriteAsync(
            "f-1", "nightly", new SyncTarget { Kind = "smb", Fields = credentials.RootElement });
        Assert.Equal("PUT", transport.Requests[1].Method);
        string writeBody = Encoding.UTF8.GetString(transport.Requests[1].Body.Span);
        Assert.Contains("\"kind\":\"smb\"", writeBody, StringComparison.Ordinal);
        Assert.Contains("\"host\":\"fs.example.com\"", writeBody, StringComparison.Ordinal);

        await client.Files.Sync.DeleteAsync("f-1", "nightly");
        Assert.Equal("DELETE", transport.Requests[2].Method);

        await client.Files.Sync.PushAsync("f-1", "nightly");
        Assert.Equal("POST", transport.Requests[3].Method);
        Assert.EndsWith("/v1/files/files/f-1/sync/nightly/push", transport.Requests[3].Uri.AbsoluteUri, StringComparison.Ordinal);

        await client.Files.Sync.TickAsync();
        Assert.Equal("POST", transport.Requests[4].Method);
        Assert.EndsWith("/v1/files/sync-tick", transport.Requests[4].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sync_write_without_fields_sends_only_kind_and_operations_reject_empty_arguments()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Files.Sync.WriteAsync("f-1", "nightly", new SyncTarget { Kind = "local-fs" });
        Assert.Equal("""{"kind":"local-fs"}""", Encoding.UTF8.GetString(transport.Requests[0].Body.Span));

        FakeTransport guardTransport = new();
        BastionVaultClient guardClient = BuildClient(guardTransport);
        _ = await Assert.ThrowsAsync<ArgumentException>(() => guardClient.Files.Sync.ListAsync(string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(
            () => guardClient.Files.Sync.WriteAsync("f-1", string.Empty, new SyncTarget { Kind = "local-fs" }));
        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => guardClient.Files.Sync.WriteAsync("f-1", "nightly", null!));
        Assert.Empty(guardTransport.Requests);
    }

    [Fact]
    public async Task Sync_write_rejects_a_non_object_fields_and_a_kind_key_inside_fields()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        // F2: a non-object Fields is refused, not silently dropped.
        using JsonDocument arrayFields = JsonDocument.Parse("""["not", "an", "object"]""");
        BastionVaultException nonObjectError = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Files.Sync.WriteAsync("f-1", "nightly", new SyncTarget { Kind = "smb", Fields = arrayFields.RootElement }));
        Assert.Equal(ErrorCodes.InputInvalidArgument, nonObjectError.Code);

        // F2: a `kind` key inside Fields would silently shadow SyncTarget.Kind on the wire
        // (Utf8JsonWriter does not deduplicate property names) — refused instead.
        using JsonDocument shadowingFields = JsonDocument.Parse("""{"kind":"local-fs","host":"fs.example.com"}""");
        BastionVaultException shadowError = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Files.Sync.WriteAsync("f-1", "nightly", new SyncTarget { Kind = "smb", Fields = shadowingFields.RootElement }));
        Assert.Equal(ErrorCodes.InputInvalidArgument, shadowError.Code);

        Assert.Empty(transport.Requests);
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
