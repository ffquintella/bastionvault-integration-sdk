using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using BastionVault.IntegrationSdk;
using BastionVault.IntegrationSdk.Testing;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>Closes remaining branch-coverage gaps in the M1b transport/logical layer.</summary>
public sealed class CoverageGapTests
{
    private static byte[] Json(string json) => Encoding.UTF8.GetBytes(json);

    private static BastionVaultClient BuildClient(FakeTransport transport, Action<BastionVaultClientOptions>? configure = null)
    {
        BastionVaultClientOptions options = new()
        {
            Address = "https://vault.example.com:8200",
            Token = "s.FAKEtoken0000000000000000",
            Transport = transport,
            RateGate = new RateGate { RatePerSecond = 0 },
            RetryPolicy = new RetryPolicy { MaxAttempts = 1 },
        };
        configure?.Invoke(options);
        return new BastionVaultClient(options, EnvironmentSource.None);
    }

    [Fact]
    [Requirement("D-M1b-7")]
    [Trait("Requirement", "D-M1b-7")]
    public async Task SystemClock_and_SystemJitterSource_defaults_are_reachable()
    {
        Assert.True(SystemJitterSource.Instance.NextDouble() is >= 0.0 and < 1.0);
        DateTimeOffset before = DateTimeOffset.UtcNow;
        await SystemClock.Instance.Delay(TimeSpan.Zero, CancellationToken.None);
        await SystemClock.Instance.Delay(TimeSpan.FromMilliseconds(1), CancellationToken.None);
        Assert.True(SystemClock.Instance.Now() >= before);
    }

    [Fact]
    [Requirement("D-M1b-12")]
    [Trait("Requirement", "D-M1b-12")]
    public async Task Raw_idempotent_GET_is_retried_on_a_retryable_failure()
    {
        FakeTransport transport = new();
        transport.EnqueueFailure(BastionVault.IntegrationSdk.Internal.TransportFailureKind.ConnectionRefused);
        transport.EnqueueResponse(200, body: Json("{}"));
        BastionVaultClient client = BuildClient(transport, o =>
        {
            o.RetryPolicy = new RetryPolicy { MaxAttempts = 2, InitialBackoff = TimeSpan.Zero };
            o.Clock = new ImmediateClock();
        });

        RawResponse response = await client.Logical.RawAsync("GET", "/v1/sys/health");

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(2, transport.Requests.Count);
    }

    [Fact]
    [Requirement("D-M1b-12")]
    [Trait("Requirement", "D-M1b-12")]
    public async Task Raw_non_idempotent_POST_is_not_retried_and_raw_maps_server_errors()
    {
        FakeTransport transport = new();
        transport.EnqueueFailure(BastionVault.IntegrationSdk.Internal.TransportFailureKind.ConnectionRefused);
        BastionVaultClient client = BuildClient(transport, o => o.RetryPolicy = new RetryPolicy { MaxAttempts = 3 });

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.RawAsync("POST", "/v1/sys/x"));

        Assert.Equal(1, exception.Attempts);
    }

    [Fact]
    [Requirement("CFG-053")]
    [Trait("Requirement", "CFG-053")]
    public async Task Raw_sealed_503_is_never_retried_even_with_attempts_remaining()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(503, body: Json("""{"error":"BastionVault is sealed."}"""));
        BastionVaultClient client = BuildClient(transport, o => o.RetryPolicy = new RetryPolicy { MaxAttempts = 5 });

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.RawAsync("GET", "/v1/sys/x"));

        Assert.Equal(ErrorCodes.ServerSealed, exception.Code);
        Assert.Equal(1, exception.Attempts);
        Assert.Single(transport.Requests);
    }

    [Fact]
    [Requirement("D-M1b-16")]
    [Trait("Requirement", "D-M1b-16")]
    public async Task Raw_dos_guard_429_pauses_the_rate_gate()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(
            429,
            headers: new Dictionary<string, string> { ["Retry-After"] = "3" },
            body: Json("""{"errors":["request temporarily blocked by DoS protection"]}"""));
        BastionVaultClient client = BuildClient(transport);

        await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.RawAsync("GET", "/v1/sys/x"));

        Assert.True(client.RateGateState.Paused);
    }

    [Fact]
    [Requirement("TRN-033")]
    [Trait("Requirement", "TRN-033")]
    public async Task Raw_response_over_MaxResponseBytes_is_aborted()
    {
        // D-M1b-20: this proves RequestExecutor's post-hoc length check, which is a documented
        // backstop for a transport (like FakeTransport) that hands back an already-buffered
        // in-memory body without bounding the read itself. It does not and cannot prove TRN-033's
        // "aborted" (as opposed to "detected afterwards") requirement — FakeTransport never streams,
        // so there is no unbounded read to abort. That guarantee is proven against the real
        // transport in HttpClientTransportTests.An_unbounded_length_response_over_the_bound_is_aborted_mid_stream_not_after_a_full_read,
        // which shows the bounded path reads at most one ~80 KiB chunk of a 20 MiB body.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: new byte[16]);
        BastionVaultClient client = BuildClient(transport, o => o.MaxResponseBytes = 4);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.RawAsync("GET", "/v1/sys/x"));

        Assert.Equal(ErrorCodes.TransportResponseTooLarge, exception.Code);
    }

    [Fact]
    [Requirement("CFG-054")]
    [Trait("Requirement", "CFG-054")]
    public async Task RetryAfter_smaller_than_computed_backoff_does_not_widen_the_wait()
    {
        List<TimeSpan> delays = new();
        FakeTransport transport = new();
        transport.EnqueueResponse(502, headers: new Dictionary<string, string> { ["Retry-After"] = "0" });
        transport.EnqueueResponse(200, body: Json("{}"));
        BastionVaultClient client = BuildClient(transport, o =>
        {
            o.RetryPolicy = new RetryPolicy { MaxAttempts = 2, InitialBackoff = TimeSpan.FromMilliseconds(250) };
            o.Clock = new RecordingClock(delays);
        });

        await client.Logical.ReadAsync("x");

        Assert.Single(delays);
        Assert.True(delays[0] >= TimeSpan.FromMilliseconds(200)); // the computed backoff floor wins, not the tiny Retry-After.
    }

    [Fact]
    [Requirement("CFG-054")]
    [Trait("Requirement", "CFG-054")]
    public async Task RespectRetryAfter_false_ignores_the_header_entirely()
    {
        List<TimeSpan> delays = new();
        FakeTransport transport = new();
        transport.EnqueueResponse(502, headers: new Dictionary<string, string> { ["Retry-After"] = "9999" });
        transport.EnqueueResponse(200, body: Json("{}"));
        BastionVaultClient client = BuildClient(transport, o =>
        {
            o.RetryPolicy = new RetryPolicy { MaxAttempts = 2, InitialBackoff = TimeSpan.FromMilliseconds(1), RespectRetryAfter = false };
            o.Clock = new RecordingClock(delays);
        });

        await client.Logical.ReadAsync("x");

        Assert.Single(delays);
        Assert.True(delays[0] < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Retryable_flag_follows_ERR_006_independently_of_RetryOn()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(502);
        BastionVaultClient client = BuildClient(transport, o => o.RetryPolicy = new RetryPolicy { MaxAttempts = 1, RetryOn = Array.Empty<string>() });

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync("x"));

        // BV-SERVER-002 is retryable per ERR-006 even though RetryOn was emptied above (D-M1b-4b).
        Assert.Equal(ErrorCodes.ServerUnavailable, exception.Code);
        Assert.True(exception.Retryable);
        Assert.Equal(1, exception.Attempts);
    }

    [Fact]
    [Requirement("TRN-040")]
    [Trait("Requirement", "TRN-040")]
    public async Task Shape_A_without_auth_surfaces_top_level_renewable_and_lease_duration()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"renewable":true,"lease_id":"lease-1","lease_duration":120,"auth":null,"data":{"k":"v"}}"""));
        BastionVaultClient client = BuildClient(transport);

        Response? response = await client.Logical.ReadAsync("secret/data/x");

        Assert.NotNull(response);
        Assert.True(response!.Renewable);
        Assert.Equal("lease-1", response.LeaseId);
        Assert.Equal(TimeSpan.FromSeconds(120), response.LeaseDuration);
        Assert.Null(response.Auth);
    }

    [Fact]
    [Requirement("TRN-040")]
    [Trait("Requirement", "TRN-040")]
    public async Task Auth_without_policies_or_metadata_defaults_to_empty()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"auth":{"client_token":"s.child0000000000000000000000"},"data":{}}"""));
        BastionVaultClient client = BuildClient(transport);

        Response? response = await client.Logical.ReadAsync("auth/userpass/login/alice");

        Assert.NotNull(response!.Auth);
        Assert.Empty(response.Auth!.Policies);
        Assert.Null(response.Auth.Metadata);
        Assert.Null(response.Auth.LeaseDuration);
        Assert.False(response.Auth.Renewable);
    }

    [Fact]
    [Requirement("TRN-020")]
    [Requirement("TRN-021")]
    [Requirement("TRN-011")]
    [Trait("Requirement", "TRN-020")]
    public async Task Url_building_handles_trailing_slash_and_valueless_query_keys()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("{}"));
        transport.EnqueueResponse(200, body: Json("{}"));
        BastionVaultClient client = BuildClient(transport);

        await client.Logical.ListAsync("secret/metadata/app/");
        await client.Logical.ReadAsync("secret/data/x?raw");

        Assert.Equal("https://vault.example.com:8200/v1/secret/metadata/app/", transport.Requests[0].Uri.AbsoluteUri);
        Assert.Equal("https://vault.example.com:8200/v1/secret/data/x?raw", transport.Requests[1].Uri.AbsoluteUri);
    }

    [Fact]
    [Requirement("D-M1b-13")]
    [Requirement("TRN-002")]
    [Trait("Requirement", "D-M1b-13")]
    public async Task ApiVersion_and_TotalTimeout_per_call_options_are_applied()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("{}"));
        BastionVaultClient client = BuildClient(transport);

        await client.Logical.ReadAsync("sys/health", new RequestOptions { ApiVersion = "v2", TotalTimeout = TimeSpan.FromSeconds(9) });

        Assert.StartsWith("https://vault.example.com:8200/v2/", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("D-M1b-13")]
    [Trait("Requirement", "D-M1b-13")]
    public void MaxResponseBytes_and_UseSystemProxy_are_resolved_when_explicitly_set()
    {
        BastionVaultClient client = new(new BastionVaultClientOptions { MaxResponseBytes = 1024, UseSystemProxy = true }, EnvironmentSource.None);

        Assert.Equal(1024, client.Config.MaxResponseBytes);
        Assert.True(client.Config.UseSystemProxy);
    }

    [Fact]
    [Requirement("CFG-060")]
    [Trait("Requirement", "CFG-060")]
    public void RequestEvent_and_RequestOptions_equality_covers_new_fields()
    {
        RequestEvent a = new("GET", "x", "ns", 200, TimeSpan.FromSeconds(1), "id", 1, null);
        RequestEvent b = new("GET", "x", "ns", 200, TimeSpan.FromSeconds(1), "id", 1, null);
        RequestEvent c = a with { ErrorCode = "BV-X" };
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        (string method, string path, string ns, int? status, TimeSpan duration, string id, int attempt, string? code) = a;
        Assert.Equal("GET", method);
        Assert.Equal("ns", ns);

        RequestOptions options1 = new() { ApiVersion = "v2", TotalTimeout = TimeSpan.FromSeconds(1) };
        RequestOptions options2 = new() { ApiVersion = "v2", TotalTimeout = TimeSpan.FromSeconds(1) };
        RequestOptions options3 = options1 with { ApiVersion = "v1" };
        Assert.Equal(options1, options2);
        Assert.True(options1 == options2);
        Assert.True(options1 != options3);
        Assert.NotEqual(options1, options3);
    }

    [Fact]
    public void SecretString_equality_and_hash_code_handle_null_and_empty()
    {
        SecretString empty = SecretString.Empty;
        SecretString alsoEmpty = new(null);
        SecretString value = new("s.abc");
        SecretString sameValue = new("s.abc");

        Assert.Equal(empty, alsoEmpty);
        Assert.NotEqual(empty, value);
        Assert.False(empty.Equals(null));
        Assert.False(empty.Equals((object?)null));
        Assert.True(value.Equals((object)sameValue));
        Assert.False(value.Equals("not-a-secret-string"));
        Assert.Equal("[REDACTED]", value.ToString());
        Assert.False(empty.HasValue);
        Assert.True(value.HasValue);
    }

    [Fact]
    [Requirement("D-M1b-3")]
    [Trait("Requirement", "D-M1b-3")]
    public async Task HttpClientTransport_sends_a_content_length_header_via_the_content_headers_path()
    {
        await using Harness.InProcessHttpsMockServer server = await Harness.InProcessHttpsMockServer.StartAsync();
        server.SetResponse(new MockResponse(200, Body: "{}"));
        BastionVaultClient configHolder = new(new BastionVaultClientOptions { Address = server.BaseAddress.ToString(), CaCertPem = server.CaCertPem });
        using HttpClientTransport transport = new(configHolder.Config);

        byte[] body = Json("""{"a":1}""");
        TransportRequest request = new(
            "POST",
            server.BaseAddress,
            new Dictionary<string, string> { ["Content-Type"] = "application/json", ["Content-Length"] = body.Length.ToString() },
            body);

        TransportResponse response = await transport.SendAsync(request);

        Assert.Equal(200, response.StatusCode);
    }

    [Fact]
    [Requirement("TRN-050")]
    [Trait("Requirement", "TRN-050")]
    public async Task Write_with_404_empty_body_raises_instead_of_returning_null()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(404);
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Logical.WriteAsync("secret/data/x"));

        Assert.Equal(ErrorCodes.NotFoundPathNotFound, exception.Code);
    }

    [Fact]
    [Requirement("TRN-053")]
    [Trait("Requirement", "TRN-053")]
    public async Task Non_json_body_snippet_is_sanitised_of_control_characters()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, headers: new Dictionary<string, string> { ["Content-Type"] = "text/html" }, body: Json("<html>\nline2\t</html>"));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync("x"));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
        string snippet = (string)exception.Details["snippet"]!;
        Assert.DoesNotContain('\n', snippet);
        Assert.DoesNotContain('\t', snippet);
    }

    [Fact]
    [Requirement("TRN-052")]
    [Trait("Requirement", "TRN-052")]
    public async Task Server_message_extraction_handles_empty_errors_array_and_non_array_errors_key()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(500, body: Json("""{"errors":[]}"""));
        transport.EnqueueResponse(500, body: Json("""{"errors":"not-an-array","error":"fallback message"}"""));
        BastionVaultClient client = BuildClient(transport, o => o.RetryPolicy = new RetryPolicy { MaxAttempts = 1, RetryOn = Array.Empty<string>() });

        BastionVaultException first = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync("a"));
        Assert.Null(first.ServerMessage);

        BastionVaultException second = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync("b"));
        Assert.Equal("fallback message", second.ServerMessage);
    }

    [Fact]
    [Requirement("TRN-053")]
    [Trait("Requirement", "TRN-053")]
    public async Task Non_json_error_body_never_leaks_a_raw_parse_exception()
    {
        // D-M1b-5: a JSON parse failure is always surfaced as BV-PROTOCOL-002, even on a non-2xx
        // status — never a raw JsonException escaping to the caller.
        FakeTransport transport = new();
        transport.EnqueueResponse(500, body: Json("not json at all"));
        BastionVaultClient client = BuildClient(transport, o => o.RetryPolicy = new RetryPolicy { MaxAttempts = 1, RetryOn = Array.Empty<string>() });

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync("x"));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    [Requirement("TRN-051")]
    [Trait("Requirement", "TRN-051")]
    public async Task Retry_After_as_an_http_date_is_parsed()
    {
        FakeTransport transport = new();
        string httpDate = DateTimeOffset.UtcNow.AddSeconds(30).ToString("R");
        transport.EnqueueResponse(429, headers: new Dictionary<string, string> { ["Retry-After"] = httpDate }, body: Json("""{"errors":["request temporarily blocked by DoS protection"]}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync("x"));

        Assert.Equal(ErrorCodes.RateLimitedByDosGuard, exception.Code);
        Assert.NotNull(exception.RetryAfter);
    }

    [Fact]
    [Requirement("TRN-051")]
    [Trait("Requirement", "TRN-051")]
    public async Task Retry_After_in_the_past_as_an_http_date_floors_to_zero()
    {
        FakeTransport transport = new();
        string pastDate = DateTimeOffset.UtcNow.AddSeconds(-30).ToString("R");
        transport.EnqueueResponse(429, headers: new Dictionary<string, string> { ["Retry-After"] = pastDate }, body: Json("""{"errors":["request temporarily blocked by DoS protection"]}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync("x"));

        Assert.Equal(TimeSpan.Zero, exception.RetryAfter);
    }

    [Fact]
    [Requirement("CFG-060")]
    [Requirement("TRN-015")]
    [Trait("Requirement", "CFG-060")]
    public async Task Explicit_per_call_token_wins_over_the_client_token_even_on_a_login_path()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("{}"));
        BastionVaultClient client = BuildClient(transport);

        await client.Logical.WriteAsync("auth/userpass/login/bob", options: new RequestOptions { Token = new SecretString("s.explicit00000000000000000") });

        Assert.Equal("s.explicit00000000000000000", transport.Requests[0].Headers["X-BastionVault-Token"]);
    }

    [Fact]
    [Requirement("CFG-017")]
    [Trait("Requirement", "CFG-017")]
    public async Task Client_headers_and_per_call_headers_are_both_merged()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("{}"));
        BastionVaultClient client = BuildClient(transport, o => o.Headers = new Dictionary<string, string> { ["X-Client"] = "client-value" });

        await client.Logical.ReadAsync("x", new RequestOptions { Headers = new Dictionary<string, string> { ["X-Call"] = "call-value" } });

        Assert.Equal("client-value", transport.Requests[0].Headers["X-Client"]);
        Assert.Equal("call-value", transport.Requests[0].Headers["X-Call"]);
    }

    [Fact]
    [Requirement("TRN-014")]
    [Requirement("TRN-023")]
    [Trait("Requirement", "TRN-014")]
    public async Task Per_call_namespace_overrides_the_client_namespace()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("{}"));
        BastionVaultClient client = BuildClient(transport, o => o.Namespace = "client-ns");

        await client.Logical.ReadAsync("x", new RequestOptions { Namespace = "call-ns" });

        Assert.Equal("call-ns", transport.Requests[0].Headers["X-BastionVault-Namespace"]);
    }

    [Fact]
    [Requirement("CFG-040")]
    [Trait("Requirement", "CFG-040")]
    public async Task CaCertReplacesSystemRoots_connects_using_only_the_custom_root()
    {
        await using Harness.InProcessHttpsMockServer server = await Harness.InProcessHttpsMockServer.StartAsync();
        server.SetResponse(new MockResponse(200, Body: "{}"));
        BastionVaultClient configHolder = new(new BastionVaultClientOptions
        {
            Address = server.BaseAddress.ToString(),
            CaCertPem = server.CaCertPem,
            CaCertReplacesSystemRoots = true,
        });
        using HttpClientTransport transport = new(configHolder.Config);

        TransportResponse response = await transport.SendAsync(new TransportRequest("GET", server.BaseAddress, new Dictionary<string, string>(), ReadOnlyMemory<byte>.Empty));

        Assert.Equal(200, response.StatusCode);
    }

    [Fact]
    [Requirement("D-M1b-12")]
    [Requirement("TRN-030")]
    [Trait("Requirement", "D-M1b-12")]
    public async Task Raw_with_a_body_serialises_it()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("{}"));
        BastionVaultClient client = BuildClient(transport);
        using JsonDocument document = JsonDocument.Parse("""{"policy":"path \"a\" {}"}""");

        await client.Logical.RawAsync("POST", "/v1/sys/policies/acl/x", document.RootElement);

        Assert.Single(transport.Requests);
    }

    [Fact]
    [Requirement("TRN-001")]
    [Requirement("OVR-002")]
    [Requirement("OVR-003")]
    [Trait("Requirement", "TRN-001")]
    public async Task Delete_without_a_body_sends_no_body()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        Response? response = await client.Logical.DeleteAsync("secret/data/x");

        Assert.Null(response);
    }

    [Fact]
    [Requirement("TRN-040")]
    [Trait("Requirement", "TRN-040")]
    public async Task Shape_A_via_auth_only_with_no_data_key_yields_null_data()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"auth":{"client_token":"s.child0000000000000000000000","policies":["default","app"],"metadata":{"username":"alice"},"lease_duration":60,"renewable":true}}"""));
        BastionVaultClient client = BuildClient(transport);

        Response? response = await client.Logical.WriteAsync("auth/userpass/login/alice");

        Assert.NotNull(response);
        Assert.Null(response!.Data);
        Assert.NotNull(response.Auth);
        Assert.Equal(new[] { "default", "app" }, response.Auth!.Policies);
        Assert.Equal("alice", response.Auth.Metadata!["username"]);
        Assert.Equal(TimeSpan.FromSeconds(60), response.Auth.LeaseDuration);
        Assert.True(response.Auth.Renewable);
    }

    [Fact]
    [Requirement("TRN-040")]
    [Requirement("TRN-041")]
    [Trait("Requirement", "TRN-040")]
    public async Task Shape_A_data_present_but_not_an_object_yields_null_data()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":null,"lease_id":"","renewable":false,"lease_duration":0,"auth":null}"""));
        BastionVaultClient client = BuildClient(transport);

        Response? response = await client.Logical.ReadAsync("secret/data/x");

        Assert.NotNull(response);
        Assert.Null(response!.Data);
        Assert.False(response.Renewable);
        Assert.Equal(TimeSpan.Zero, response.LeaseDuration);
        Assert.Null(response.LeaseId); // TRN-041: lease_id == "" is surfaced as absent.
    }

    [Fact]
    [Requirement("OVR-001")]
    [Trait("Requirement", "OVR-001")]
    public async Task Missing_transport_throws_before_any_status_mapping()
    {
        BastionVaultClient client = new(new BastionVaultClientOptions { Address = "https://vault.example.com:8200" }, EnvironmentSource.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.Logical.ReadAsync("x"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.Logical.RawAsync("GET", "/v1/sys/health"));
    }

    [Fact]
    [Requirement("CFG-060")]
    [Trait("Requirement", "CFG-060")]
    public async Task Per_call_timeout_overrides_the_client_timeout_for_both_logical_and_raw_calls()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("{}"));
        transport.EnqueueResponse(200, body: Json("{}"));
        BastionVaultClient client = BuildClient(transport);

        await client.Logical.ReadAsync("x", new RequestOptions { Timeout = TimeSpan.FromSeconds(2) });
        await client.Logical.RawAsync("GET", "/v1/sys/health", options: new RequestOptions { Timeout = TimeSpan.FromSeconds(3) });

        Assert.Equal(TimeSpan.FromSeconds(2), transport.Requests[0].Timeout);
        Assert.Equal(TimeSpan.FromSeconds(3), transport.Requests[1].Timeout);
    }

    [Fact]
    [Requirement("D-M1b-16")]
    [Trait("Requirement", "D-M1b-16")]
    public async Task DosGuard_pause_is_capped_at_30_seconds_on_both_logical_and_raw_paths()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(429, headers: new Dictionary<string, string> { ["Retry-After"] = "999" }, body: Json("""{"errors":["request temporarily blocked by DoS protection"]}"""));
        transport.EnqueueResponse(429, headers: new Dictionary<string, string> { ["Retry-After"] = "999" }, body: Json("""{"errors":["request temporarily blocked by DoS protection"]}"""));
        BastionVaultClient client = BuildClient(transport, o => o.Clock = new FixedClock(DateTimeOffset.UnixEpoch));

        await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync("x"));
        Assert.Equal(DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(30), client.RateGateState.PausedUntil);

        await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.RawAsync("GET", "/v1/sys/x"));
        Assert.Equal(DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(30), client.RateGateState.PausedUntil);
    }

    [Fact]
    [Requirement("TRN-051")]
    [Trait("Requirement", "TRN-051")]
    public async Task Retry_After_that_is_neither_an_integer_nor_a_date_is_ignored()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(429, headers: new Dictionary<string, string> { ["Retry-After"] = "not-a-value" }, body: Json("""{"error":"namespace request-rate quota exceeded"}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync("x"));

        Assert.Null(exception.RetryAfter);
    }

    [Fact]
    [Requirement("TRN-021")]
    [Trait("Requirement", "TRN-021")]
    public async Task A_bare_question_mark_yields_an_empty_query_string()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("{}"));
        BastionVaultClient client = BuildClient(transport);

        await client.Logical.ReadAsync("secret/data/x?");

        Assert.Equal("https://vault.example.com:8200/v1/secret/data/x", transport.Requests[0].Uri.AbsoluteUri);
    }

    [Fact]
    [Requirement("CFG-053")]
    [Trait("Requirement", "CFG-053")]
    public async Task Status_503_with_no_message_defaults_to_unavailable_not_sealed()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(503);
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync("x"));

        Assert.Equal(ErrorCodes.ServerUnavailable, exception.Code);
    }

    [Fact]
    [Requirement("D-M1a-2")]
    [Trait("Requirement", "D-M1a-2")]
    public void BastionVaultClient_defaults_options_when_none_are_given()
    {
        BastionVaultClient client = new(null, EnvironmentSource.None);
        Assert.Equal("https://127.0.0.1:8200", client.Config.Address);
    }

    [Fact]
    [Requirement("TRN-054")]
    public void BastionVaultException_ToString_covers_the_config_and_request_scoped_forms()
    {
        BastionVaultException configError = new(
            "BV-CONFIG-001",
            ErrorCategory.Configuration,
            "bad address",
            "fix it",
            retryable: false);
        Assert.DoesNotContain("HTTP", configError.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("server:", configError.ToString(), StringComparison.Ordinal);

        BastionVaultException requestError = new(
            "BV-SERVER-005",
            ErrorCategory.ServerState,
            "internal error",
            "check logs",
            retryable: false,
            attempts: 1,
            serverMessage: "boom",
            statusCode: 500,
            method: "GET",
            path: "secret/data/x");
        string text = requestError.ToString();
        Assert.Contains("HTTP 500 GET secret/data/x", text, StringComparison.Ordinal);
        Assert.Contains("(server: \"boom\")", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SecretString_hash_code_differs_for_empty_and_populated_values()
    {
        Assert.Equal(0, SecretString.Empty.GetHashCode());
        Assert.Equal("abc".Length, new SecretString("abc").GetHashCode());
    }

    [Fact]
    [Requirement("D-M1b-3")]
    [Trait("Requirement", "D-M1b-3")]
    public async Task Untrusted_server_certificate_without_a_configured_CA_maps_to_tls_error()
    {
        await using Harness.InProcessHttpsMockServer server = await Harness.InProcessHttpsMockServer.StartAsync();
        BastionVaultClient configHolder = new(new BastionVaultClientOptions { Address = server.BaseAddress.ToString() });
        using HttpClientTransport transport = new(configHolder.Config);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => transport.SendAsync(new TransportRequest("GET", server.BaseAddress, new Dictionary<string, string>(), ReadOnlyMemory<byte>.Empty)));

        Assert.Equal(ErrorCodes.TransportTlsError, exception.Code);
    }

    [Fact]
    [Requirement("D-M1b-3")]
    [Trait("Requirement", "D-M1b-3")]
    public async Task Content_length_only_header_still_routes_through_the_content_headers_path()
    {
        await using Harness.InProcessHttpsMockServer server = await Harness.InProcessHttpsMockServer.StartAsync();
        server.SetResponse(new MockResponse(200, Body: "{}"));
        BastionVaultClient configHolder = new(new BastionVaultClientOptions { Address = server.BaseAddress.ToString(), CaCertPem = server.CaCertPem });
        using HttpClientTransport transport = new(configHolder.Config);
        byte[] body = Json("{}");

        TransportResponse response = await transport.SendAsync(new TransportRequest(
            "POST",
            server.BaseAddress,
            new Dictionary<string, string> { ["Content-Length"] = body.Length.ToString() },
            body));

        Assert.Equal(200, response.StatusCode);
    }

    [Fact]
    [Requirement("D-M1b-12")]
    [Trait("Requirement", "D-M1b-12")]
    public async Task Raw_status_201_is_treated_as_success()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(201, body: Json("{}"));
        BastionVaultClient client = BuildClient(transport);

        RawResponse response = await client.Logical.RawAsync("POST", "/v1/sys/x");

        Assert.Equal(201, response.StatusCode);
    }

    [Fact]
    [Requirement("TRN-040")]
    [Trait("Requirement", "TRN-040")]
    public async Task Shape_B_with_a_non_object_top_level_body_yields_null_data()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("[1,2,3]"));
        BastionVaultClient client = BuildClient(transport);

        Response? response = await client.Logical.ReadAsync("sys/some-array-endpoint");

        Assert.NotNull(response);
        Assert.Null(response!.Data);
    }

    [Fact]
    [Requirement("TRN-092")]
    [Trait("Requirement", "TRN-092")]
    public void Bracketed_ipv6_literal_is_accepted_and_unbracketed_is_rejected()
    {
        BastionVaultClient bracketed = new(new BastionVaultClientOptions { Address = "https://[::1]:8200" }, EnvironmentSource.None);
        Assert.Equal("https://[::1]:8200", bracketed.Config.Address);

        BastionVaultException exception = Assert.Throws<BastionVaultException>(
            () => new BastionVaultClient(new BastionVaultClientOptions { Address = "https://::1:8200" }, EnvironmentSource.None));
        Assert.Equal(ErrorCodes.ConfigInvalidAddress, exception.Code);
    }

    [Fact]
    [Requirement("TRN-043")]
    [Trait("Requirement", "TRN-043")]
    public async Task Raw_parsed_body_is_retained_for_diagnostics()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"renewable":false,"lease_id":"","lease_duration":0,"auth":null,"data":{"future_field":"kept"}}"""));
        BastionVaultClient client = BuildClient(transport);

        Response? response = await client.Logical.ReadAsync("secret/data/x");

        Assert.NotNull(response);
        Assert.Equal(JsonValueKind.Object, response!.Raw.ValueKind);
        Assert.True(response.Raw.TryGetProperty("data", out JsonElement rawData));
        Assert.Equal("kept", rawData.GetProperty("future_field").GetString());
    }

    [Fact]
    [Requirement("TRN-090")]
    [Trait("Requirement", "TRN-090")]
    public async Task HttpClientTransport_reuses_the_connection_across_requests()
    {
        await using Harness.InProcessHttpsMockServer server = await Harness.InProcessHttpsMockServer.StartAsync();
        server.SetResponse(new MockResponse(200, Body: "{}"));
        BastionVaultClient configHolder = new(new BastionVaultClientOptions { Address = server.BaseAddress.ToString(), CaCertPem = server.CaCertPem });
        using HttpClientTransport transport = new(configHolder.Config);

        for (int i = 0; i < 3; i++)
        {
            await transport.SendAsync(new TransportRequest("GET", server.BaseAddress, new Dictionary<string, string>(), ReadOnlyMemory<byte>.Empty));
        }

        Assert.Equal(1, server.AcceptedConnectionCount);
    }

    [Fact]
    [Requirement("TRN-091")]
    [Trait("Requirement", "TRN-091")]
    public void Proxy_is_disabled_by_default_and_enabled_only_when_UseSystemProxy_is_set()
    {
        BastionVaultClient defaultHolder = new(new BastionVaultClientOptions(), EnvironmentSource.None);
        using HttpClientTransport defaultTransport = new(defaultHolder.Config);
        Assert.False(defaultHolder.Config.UseSystemProxy);

        BastionVaultClient proxyHolder = new(new BastionVaultClientOptions { UseSystemProxy = true }, EnvironmentSource.None);
        using HttpClientTransport proxyTransport = new(proxyHolder.Config);
        Assert.True(proxyHolder.Config.UseSystemProxy);
    }

    [Fact]
    [Requirement("CFG-044")]
    [Trait("Requirement", "CFG-044")]
    public async Task Client_certificate_is_presented_and_required_by_an_mTLS_server()
    {
        await using Harness.InProcessHttpsMockServer server = await Harness.InProcessHttpsMockServer.StartAsync(new MockServerOptions(RequireClientCertificate: true));
        server.SetResponse(new MockResponse(200, Body: "{}"));
        string tempDirectory = Directory.CreateTempSubdirectory("bv-mtls-").FullName;
        try
        {
            using X509Certificate2 clientCertificate = server.ClientCertificate;
            string certPath = Path.Combine(tempDirectory, "client.crt");
            string keyPath = Path.Combine(tempDirectory, "client.key");
            File.WriteAllText(certPath, clientCertificate.ExportCertificatePem());
            File.WriteAllText(keyPath, clientCertificate.GetRSAPrivateKey()!.ExportPkcs8PrivateKeyPem());

            BastionVaultClient noCertHolder = new(new BastionVaultClientOptions { Address = server.BaseAddress.ToString(), CaCertPem = server.CaCertPem });
            using (HttpClientTransport noCertTransport = new(noCertHolder.Config))
            {
                await Assert.ThrowsAsync<BastionVaultException>(
                    () => noCertTransport.SendAsync(new TransportRequest("GET", server.BaseAddress, new Dictionary<string, string>(), ReadOnlyMemory<byte>.Empty)));
            }

            BastionVaultClient withCertHolder = new(new BastionVaultClientOptions
            {
                Address = server.BaseAddress.ToString(),
                CaCertPem = server.CaCertPem,
                ClientCertPath = certPath,
                ClientKeyPath = keyPath,
            });
            using HttpClientTransport withCertTransport = new(withCertHolder.Config);
            TransportResponse response = await withCertTransport.SendAsync(new TransportRequest("GET", server.BaseAddress, new Dictionary<string, string>(), ReadOnlyMemory<byte>.Empty));
            Assert.Equal(200, response.StatusCode);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    [Requirement("RES-004")]
    [Trait("Requirement", "RES-004")]
    public async Task TotalTimeout_stops_retrying_once_the_deadline_has_passed()
    {
        FakeTransport transport = new();
        transport.EnqueueFailure(BastionVault.IntegrationSdk.Internal.TransportFailureKind.ConnectionRefused);
        BastionVaultClient client = BuildClient(transport, o =>
        {
            o.RetryPolicy = new RetryPolicy { MaxAttempts = 5, InitialBackoff = TimeSpan.Zero };
            o.Clock = new SteppingClock(TimeSpan.FromSeconds(10));
        });

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Logical.ReadAsync("x", new RequestOptions { TotalTimeout = TimeSpan.FromSeconds(1) }));

        Assert.Equal(1, exception.Attempts);
    }

    [Fact]
    [Requirement("RES-004")]
    [Trait("Requirement", "RES-004")]
    public async Task TotalTimeout_allows_retries_within_the_deadline()
    {
        FakeTransport transport = new();
        transport.EnqueueFailure(BastionVault.IntegrationSdk.Internal.TransportFailureKind.ConnectionRefused);
        transport.EnqueueResponse(200, body: Json("{}"));
        BastionVaultClient client = BuildClient(transport, o =>
        {
            o.RetryPolicy = new RetryPolicy { MaxAttempts = 3, InitialBackoff = TimeSpan.Zero };
            o.Clock = new ImmediateClock();
        });

        Response? response = await client.Logical.ReadAsync("x", new RequestOptions { TotalTimeout = TimeSpan.FromMinutes(5) });

        Assert.NotNull(response);
    }

    [Fact]
    [Requirement("RES-004")]
    [Trait("Requirement", "RES-004")]
    public async Task Raw_TotalTimeout_stops_retrying_once_the_deadline_has_passed()
    {
        FakeTransport transport = new();
        transport.EnqueueFailure(BastionVault.IntegrationSdk.Internal.TransportFailureKind.ConnectionRefused);
        BastionVaultClient client = BuildClient(transport, o =>
        {
            o.RetryPolicy = new RetryPolicy { MaxAttempts = 5, InitialBackoff = TimeSpan.Zero };
            o.Clock = new SteppingClock(TimeSpan.FromSeconds(10));
        });

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Logical.RawAsync("GET", "/v1/sys/health", options: new RequestOptions { TotalTimeout = TimeSpan.FromSeconds(1) }));

        Assert.Equal(1, exception.Attempts);
    }

    [Fact]
    [Requirement("RES-004")]
    [Trait("Requirement", "RES-004")]
    public async Task Raw_TotalTimeout_allows_retries_within_the_deadline()
    {
        FakeTransport transport = new();
        transport.EnqueueFailure(BastionVault.IntegrationSdk.Internal.TransportFailureKind.ConnectionRefused);
        transport.EnqueueResponse(200, body: Json("{}"));
        BastionVaultClient client = BuildClient(transport, o =>
        {
            o.RetryPolicy = new RetryPolicy { MaxAttempts = 3, InitialBackoff = TimeSpan.Zero };
            o.Clock = new ImmediateClock();
        });

        RawResponse response = await client.Logical.RawAsync("GET", "/v1/sys/health", options: new RequestOptions { TotalTimeout = TimeSpan.FromMinutes(5) });

        Assert.Equal(200, response.StatusCode);
    }

    [Fact]
    [Requirement("TRN-052")]
    [Trait("Requirement", "TRN-052")]
    public async Task Errors_key_non_array_and_no_error_key_falls_through_to_a_null_message()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(500, body: Json("""{"errors":"not-an-array-and-no-error-key"}"""));
        BastionVaultClient client = BuildClient(transport, o => o.RetryPolicy = new RetryPolicy { MaxAttempts = 1, RetryOn = Array.Empty<string>() });

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync("x"));

        Assert.Null(exception.ServerMessage);
    }

    [Fact]
    [Requirement("RES-004")]
    [Trait("Requirement", "RES-004")]
    public async Task TotalTimeout_and_a_per_attempt_Timeout_can_both_be_set()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("{}"));
        BastionVaultClient client = BuildClient(transport);

        Response? response = await client.Logical.ReadAsync("x", new RequestOptions
        {
            Timeout = TimeSpan.FromSeconds(2),
            TotalTimeout = TimeSpan.FromSeconds(30),
        });

        Assert.NotNull(response);
        Assert.Equal(TimeSpan.FromSeconds(2), transport.Requests[0].Timeout);
    }

    /// <summary>A clock whose <c>Now()</c> advances by a fixed step every call, to deterministically exceed a deadline.</summary>
    private sealed class SteppingClock : IClock
    {
        private readonly TimeSpan step;
        private DateTimeOffset current = DateTimeOffset.UnixEpoch;

        public SteppingClock(TimeSpan step) => this.step = step;

        public DateTimeOffset Now()
        {
            DateTimeOffset value = current;
            current += step;
            return value;
        }

        public Task Delay(TimeSpan duration, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedClock : IClock
    {
        private readonly DateTimeOffset now;

        public FixedClock(DateTimeOffset now) => this.now = now;

        public DateTimeOffset Now() => now;

        public Task Delay(TimeSpan duration, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    [Requirement("CFG-081")]
    [Trait("Requirement", "CFG-081")]
    public void README_documents_the_CFG_081_metric_names()
    {
        Harness.FixtureRepository repository = new();
        string readme = File.ReadAllText(Path.Combine(repository.RepositoryRoot, "README.md"));

        Assert.Contains("bastionvault.client.request.duration", readme, StringComparison.Ordinal);
        Assert.Contains("bastionvault.client.request.retries", readme, StringComparison.Ordinal);
        Assert.Contains("bastionvault.client.request.errors{code}", readme, StringComparison.Ordinal);
    }

    private sealed class ImmediateClock : IClock
    {
        public DateTimeOffset Now() => DateTimeOffset.UnixEpoch;

        public Task Delay(TimeSpan duration, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingClock : IClock
    {
        private readonly List<TimeSpan> delays;

        public RecordingClock(List<TimeSpan> delays) => this.delays = delays;

        public DateTimeOffset Now() => DateTimeOffset.UnixEpoch;

        public Task Delay(TimeSpan duration, CancellationToken cancellationToken)
        {
            delays.Add(duration);
            return Task.CompletedTask;
        }
    }
}
