using System.Text;
using BastionVault.IntegrationSdk.Testing;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>12 §Cert lifecycle (M10 slice c, DR-0017): the <c>cert-lifecycle</c> mount, reached from <c>Client.CertLifecycle</c>. No requirement ID of its own (catalogue-only surface).</summary>
public sealed class CertLifecycleUnitTests
{
    private const string Address = "https://vault.example.com:8200";

    [Fact]
    public async Task ListTargets_and_ListTargetsInfo_and_the_all_iterator_round_trip()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"keys":["target-1","target-2"]}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"keys":["target-1"],"records":[{"kind":"file","address":"/etc/tls/a.pem","pki_mount":"pki","role_ref":"web","common_name":"a.example.com","ttl":86400,"key_policy":"rotate","renew_before":604800}],"total":1,"truncated":false}}"""));
        BastionVaultClient client = BuildClient(transport);

        Assert.Equal(["target-1", "target-2"], await client.CertLifecycle.ListTargetsAsync());
        Assert.Equal("LIST", transport.Requests[0].Method);
        Assert.EndsWith("/v1/cert-lifecycle/targets/", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        Page<Target> page = await client.CertLifecycle.ListTargetsInfoAsync();
        Assert.Equal(["target-1"], page.Keys);
        Assert.Equal("a.example.com", page.Records[0].CommonName);
        Assert.Equal(TimeSpan.FromDays(1), page.Records[0].Ttl);
        Assert.Equal(TimeSpan.FromDays(7), page.Records[0].RenewBefore);
        Assert.Contains("/v2/cert-lifecycle/targets-info", transport.Requests[1].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport allTransport = new();
        allTransport.EnqueueResponse(200, body: Json("""{"data":{"keys":["target-1"],"records":[{"kind":"file"}],"total":1,"truncated":false}}"""));
        BastionVaultClient allClient = BuildClient(allTransport);
        List<KeyValuePair<string, Target>> all = [];
        await foreach (KeyValuePair<string, Target> entry in allClient.CertLifecycle.ListTargetsInfoAllAsync())
        {
            all.Add(entry);
        }

        _ = Assert.Single(all);
        Assert.Equal("target-1", all[0].Key);

        // A page with no `total` field falls back to the keys count, and a truncated page
        // carries its cursor through to `Next`.
        FakeTransport nextPageTransport = new();
        nextPageTransport.EnqueueResponse(200, body: Json("""{"data":{"keys":["target-1"],"records":[{"kind":"file"}],"next":"target-2","truncated":true}}"""));
        BastionVaultClient nextPageClient = BuildClient(nextPageTransport);
        Page<Target> nextPage = await nextPageClient.CertLifecycle.ListTargetsInfoAsync();
        Assert.Equal(1, nextPage.Total);
        Assert.Equal("target-2", nextPage.Next);
        Assert.True(nextPage.Truncated);

        // A cursor request encodes `after`, and a page with no `records` key and no keys is an
        // empty, non-mismatched page.
        FakeTransport cursorTransport = new();
        cursorTransport.EnqueueResponse(200, body: Json("""{"data":{"keys":[],"truncated":false}}"""));
        BastionVaultClient cursorClient = BuildClient(cursorTransport);
        Page<Target> emptyPage = await cursorClient.CertLifecycle.ListTargetsInfoAsync(after: "target-1");
        Assert.Empty(emptyPage.Records);
        Assert.Equal(0, emptyPage.Total);
        Assert.Contains("after=target-1", cursorTransport.Requests[0].Uri.Query, StringComparison.Ordinal);

        FakeTransport bodylessTransport = new();
        bodylessTransport.EnqueueResponse(204);
        BastionVaultException bodylessException = await Assert.ThrowsAsync<BastionVaultException>(
            () => BuildClient(bodylessTransport).CertLifecycle.ListTargetsInfoAsync());
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, bodylessException.Code);
    }

    [Fact]
    public async Task ListTargetsInfo_raises_a_protocol_error_when_a_record_is_not_an_object()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"keys":["target-1"],"records":[1],"total":1,"truncated":false}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.CertLifecycle.ListTargetsInfoAsync());

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    public async Task ListTargetsInfo_raises_a_protocol_error_when_keys_and_records_lengths_differ()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"keys":["target-1","target-2"],"records":[{"kind":"file"}],"total":2,"truncated":false}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.CertLifecycle.ListTargetsInfoAsync());

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
        Assert.Equal(200, exception.StatusCode);
    }

    [Fact]
    public async Task ReadTarget_WriteTarget_and_DeleteTarget_round_trip()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"kind":"file","address":"/etc/tls/a.pem","common_name":"a.example.com","alt_names":["a.internal"],"ip_sans":["10.0.0.1"],"ttl":3600,"key_policy":"rotate"}}"""));
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        Target? target = await client.CertLifecycle.ReadTargetAsync("target-1");
        Assert.Equal("a.example.com", target!.CommonName);
        Assert.Equal(["a.internal"], target.AltNames);
        Assert.Equal(["10.0.0.1"], target.IpSans);
        Assert.EndsWith("/v1/cert-lifecycle/targets/target-1", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        await client.CertLifecycle.WriteTargetAsync("target-1", new Target
        {
            Kind = "file",
            Address = "/etc/tls/a.pem",
            PkiMount = "pki",
            RoleRef = "web",
            CommonName = "a.example.com",
            AltNames = ["a.internal"],
            IpSans = ["10.0.0.1"],
            Ttl = TimeSpan.FromHours(1),
            KeyPolicy = "rotate",
            KeyRef = "key-1",
            RenewBefore = TimeSpan.FromDays(7),
        });
        string body = Encoding.UTF8.GetString(transport.Requests[1].Body.Span);
        Assert.Contains("\"pki_mount\":\"pki\"", body, StringComparison.Ordinal);
        Assert.Contains("\"key_policy\":\"rotate\"", body, StringComparison.Ordinal);
        Assert.Contains("\"renew_before\":604800", body, StringComparison.Ordinal);
        Assert.Contains("\"alt_names\":[\"a.internal\"]", body, StringComparison.Ordinal);
        Assert.Contains("\"ip_sans\":[\"10.0.0.1\"]", body, StringComparison.Ordinal);

        await client.CertLifecycle.DeleteTargetAsync("target-1");
        Assert.Equal("DELETE", transport.Requests[2].Method);

        FakeTransport missingTransport = new();
        missingTransport.EnqueueResponse(404);
        Assert.Null(await BuildClient(missingTransport).CertLifecycle.ReadTargetAsync("ghost"));

        // Every member absent: nothing is written, and reading an empty record back yields an
        // all-null Target rather than a guessed default.
        FakeTransport emptyWriteTransport = new();
        emptyWriteTransport.EnqueueResponse(204);
        await BuildClient(emptyWriteTransport).CertLifecycle.WriteTargetAsync("target-1", new Target());
        Assert.Equal("{}", Encoding.UTF8.GetString(emptyWriteTransport.Requests[0].Body.Span));

        FakeTransport emptyReadTransport = new();
        emptyReadTransport.EnqueueResponse(200, body: Json("""{"data":{}}"""));
        Target? emptyTarget = await BuildClient(emptyReadTransport).CertLifecycle.ReadTargetAsync("target-1");
        Assert.Null(emptyTarget!.Kind);
        Assert.Null(emptyTarget.AltNames);
        Assert.Null(emptyTarget.Ttl);
    }

    [Fact]
    public async Task State_returns_null_on_404_and_typed_fields_otherwise()
    {
        FakeTransport missingTransport = new();
        missingTransport.EnqueueResponse(404);
        Assert.Null(await BuildClient(missingTransport).CertLifecycle.StateAsync("ghost"));

        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"current_serial":"11:22","current_not_after":"2025-01-01T00:00:00Z","last_renewal":"2024-06-01T00:00:00Z","failure_count":0}}"""));
        BastionVaultClient client = BuildClient(transport);
        TargetState? state = await client.CertLifecycle.StateAsync("target-1");
        Assert.Equal("11:22", state!.CurrentSerial);
        Assert.Equal(0, state.FailureCount);
        Assert.EndsWith("/v1/cert-lifecycle/state/target-1", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Renew_sends_a_bodyless_POST()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        await client.CertLifecycle.RenewAsync("target-1");

        Assert.Equal("POST", transport.Requests[0].Method);
        Assert.EndsWith("/v1/cert-lifecycle/renew/target-1", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal(0, transport.Requests[0].Body.Length);
    }

    [Fact]
    public async Task SchedulerConfig_read_returns_client_token_set_and_write_serialises_client_token()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"enabled":true,"tick_interval_seconds":60,"client_token_set":true,"base_backoff_seconds":5,"max_backoff_seconds":300}}"""));
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        SchedulerConfig? config = await client.CertLifecycle.ReadSchedulerConfigAsync();
        Assert.True(config!.Enabled);
        Assert.Equal(60L, config.TickIntervalSeconds);
        Assert.True(config.ClientTokenSet);
        Assert.Null(config.ClientToken);

        await client.CertLifecycle.WriteSchedulerConfigAsync(new SchedulerConfig
        {
            Enabled = true,
            TickIntervalSeconds = 60,
            ClientToken = new SecretString("tok-abc"),
            BaseBackoffSeconds = 5,
            MaxBackoffSeconds = 300,
        });
        string body = Encoding.UTF8.GetString(transport.Requests[1].Body.Span);
        Assert.Contains("\"client_token\":\"tok-abc\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("client_token_set", body, StringComparison.Ordinal);

        FakeTransport missingTransport = new();
        missingTransport.EnqueueResponse(404);
        Assert.Null(await BuildClient(missingTransport).CertLifecycle.ReadSchedulerConfigAsync());

        // Every member absent: nothing is written and no field of `enabled` is read as false.
        FakeTransport emptyWriteTransport = new();
        emptyWriteTransport.EnqueueResponse(204);
        await BuildClient(emptyWriteTransport).CertLifecycle.WriteSchedulerConfigAsync(new SchedulerConfig());
        Assert.Equal("{}", Encoding.UTF8.GetString(emptyWriteTransport.Requests[0].Body.Span));

        FakeTransport emptyReadTransport = new();
        emptyReadTransport.EnqueueResponse(200, body: Json("""{"data":{}}"""));
        SchedulerConfig? emptyConfig = await BuildClient(emptyReadTransport).CertLifecycle.ReadSchedulerConfigAsync();
        Assert.Null(emptyConfig!.Enabled);
        Assert.Null(emptyConfig.ClientTokenSet);
    }

    [Fact]
    public async Task Deliverers_returns_the_raw_map_and_null_on_404()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"email":{"configured":true}}}"""));
        BastionVaultClient client = BuildClient(transport);

        IReadOnlyDictionary<string, System.Text.Json.JsonElement>? deliverers = await client.CertLifecycle.DeliverersAsync();

        Assert.True(deliverers!.ContainsKey("email"));
        Assert.EndsWith("/v1/cert-lifecycle/sys/deliverers", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport missingTransport = new();
        missingTransport.EnqueueResponse(404);
        Assert.Null(await BuildClient(missingTransport).CertLifecycle.DeliverersAsync());
    }

    [Fact]
    public async Task Every_operation_rejects_empty_arguments_before_any_request_is_sent()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.CertLifecycle.ReadTargetAsync(string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => client.CertLifecycle.WriteTargetAsync("target-1", null!));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.CertLifecycle.DeleteTargetAsync(string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.CertLifecycle.StateAsync(string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.CertLifecycle.RenewAsync(string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => client.CertLifecycle.WriteSchedulerConfigAsync(null!));
        Assert.Empty(transport.Requests);
    }

    private static BastionVaultClient BuildClient(ITransport transport)
    {
        BastionVaultClientOptions options = new()
        {
            Address = Address,
            Token = "s.FAKE-token-0000000000000000",
            Transport = transport,
            RateGate = new RateGate { RatePerSecond = 0 },
            RetryPolicy = new RetryPolicy { MaxAttempts = 1 },
        };
        return new BastionVaultClient(options, EnvironmentSource.None);
    }

    private static ReadOnlyMemory<byte> Json(string json)
    {
        return Encoding.UTF8.GetBytes(json);
    }
}
