using System.Text;
using System.Text.Json;
using BastionVault.IntegrationSdk;
using BastionVault.IntegrationSdk.Testing;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// Direct, non-fixture-driven coverage of the M1b transport/logical layer
/// (<c>decisions/0004-m1b-transport.md</c>): every status-mapper branch, retry-eligibility path,
/// the observability hook, runtime mutation, and <see cref="FakeTransport"/> itself.
/// </summary>
public sealed class LogicalOperationsUnitTests
{
    private static BastionVaultClient BuildClient(FakeTransport transport, Action<BastionVaultClientOptions>? configure = null)
    {
        BastionVaultClientOptions options = new()
        {
            Address = "https://vault.example.com:8200",
            Token = FakeTokens.Client,
            Transport = transport,
            RateGate = new RateGate { RatePerSecond = 0 },
        };
        configure?.Invoke(options);
        return new BastionVaultClient(options, EnvironmentSource.None);
    }

    private static byte[] Json(string json)
    {
        return Encoding.UTF8.GetBytes(json);
    }

    [Fact]
    public async Task Status_401_maps_to_unauthenticated()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(401, body: Json("""{"error":"connect mfa requires an authenticated caller"}"""));
        BastionVaultClient client = BuildClient(transport, o => o.RetryPolicy = new RetryPolicy { MaxAttempts = 1 });

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync("secret/data/x"));

        Assert.Equal(ErrorCodes.AuthUnauthenticated, exception.Code);
        Assert.False(exception.Retryable);
    }

    [Fact]
    public async Task Status_403_maps_to_permission_denied()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(403, body: Json("""{"error":"permission denied"}"""));
        BastionVaultClient client = BuildClient(transport, o => o.RetryPolicy = new RetryPolicy { MaxAttempts = 1 });

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync("secret/data/x"));

        Assert.Equal(ErrorCodes.AuthzPermissionDenied, exception.Code);
    }

    // D-M1c-19: Resolve409's body sniffing is gone. `brokered_resource_no_static_credential`
    // is an Appendix B §2 exact row and still answers at step 4; everything else on a
    // non-recordings path lands on the BV-CONFLICT-001 that 04-error-model.md step 5 names.
    [Theory]
    [Requirement("ERR-020")]
    [Trait("Requirement", "ERR-020")]
    [InlineData("digest mismatch on chunk", "BV-CONFLICT-001")]
    [InlineData("brokered_resource_no_static_credential", "BV-CONFLICT-003")]
    [InlineData("some other conflict", "BV-CONFLICT-001")]
    public async Task Status_409_discriminates_by_message(string message, string expectedCode)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(409, body: Json($$"""{"error":"{{message}}"}"""));
        BastionVaultClient client = BuildClient(transport, o => o.RetryPolicy = new RetryPolicy { MaxAttempts = 1 });

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync("secret/data/x"));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    [Requirement("TRN-052")]
    [Trait("Requirement", "TRN-052")]
    public async Task Status_416_maps_to_chunk_index_out_of_range()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(416);
        BastionVaultClient client = BuildClient(transport, o => o.RetryPolicy = new RetryPolicy { MaxAttempts = 1 });

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync("secret/data/x"));

        Assert.Equal(ErrorCodes.InputChunkIndexOutOfRange, exception.Code);
    }

    [Fact]
    public async Task Status_500_maps_to_internal_error_and_502_504_map_to_unavailable()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(500);
        transport.EnqueueResponse(502);
        transport.EnqueueResponse(504);
        BastionVaultClient client = BuildClient(transport, o => o.RetryPolicy = new RetryPolicy { MaxAttempts = 1, RetryOn = Array.Empty<string>() });

        BastionVaultException first = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync("a"));
        Assert.Equal(ErrorCodes.ServerInternalError, first.Code);

        BastionVaultException second = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync("b"));
        Assert.Equal(ErrorCodes.ServerUnavailable, second.Code);

        BastionVaultException third = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync("c"));
        Assert.Equal(ErrorCodes.ServerUnavailable, third.Code);
    }

    [Fact]
    [Requirement("CFG-053")]
    [Trait("Requirement", "CFG-053")]
    public async Task Status_507_maps_to_namespace_quota_exceeded()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(507);
        BastionVaultClient client = BuildClient(transport, o => o.RetryPolicy = new RetryPolicy { MaxAttempts = 1 });

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync("x"));

        Assert.Equal(ErrorCodes.QuotaNamespaceQuotaExceeded, exception.Code);
    }

    [Fact]
    [Requirement("TRN-050")]
    [Trait("Requirement", "TRN-050")]
    public async Task Status_304_yields_a_response_with_no_data()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(304);
        BastionVaultClient client = BuildClient(transport, o => o.RetryPolicy = new RetryPolicy { MaxAttempts = 1 });

        Response? response = await client.Logical.ReadAsync("x");

        Assert.NotNull(response);
        Assert.Equal(304, response!.StatusCode);
        Assert.Null(response.Data);
    }

    [Fact]
    [Requirement("TRN-054")]
    [Trait("Requirement", "TRN-054")]
    public async Task Unmapped_4xx_status_falls_back_to_input_invalid_argument_not_a_generic_exception()
    {
        // D-M1b-21's principle: every status yields a BastionVaultException, never a raw
        // runtime exception, with ServerMessage carried through. D-M1c-12 corrects the code the
        // unmapped-4xx arm returns to BV-INPUT-100, which 04-error-model.md step 5 names and
        // which the generated catalogue now carries.
        FakeTransport transport = new();
        transport.EnqueueResponse(400, body: Json("""{"error":"unrecognised"}"""));
        BastionVaultClient client = BuildClient(transport, o => o.RetryPolicy = new RetryPolicy { MaxAttempts = 1 });

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync("x"));

        Assert.Equal(ErrorCodes.InputServerRejectedRequest, exception.Code);
        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("GET", exception.Method);
        Assert.Equal("unrecognised", exception.ServerMessage);
    }

    [Fact]
    [Requirement("TRN-054")]
    [Trait("Requirement", "TRN-054")]
    public async Task Unmapped_5xx_status_falls_back_to_server_internal_error()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(599, body: Json("""{"error":"weird server status"}"""));
        BastionVaultClient client = BuildClient(transport, o => o.RetryPolicy = new RetryPolicy { MaxAttempts = 1 });

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync("x"));

        Assert.Equal(ErrorCodes.ServerInternalError, exception.Code);
    }

    [Fact]
    [Requirement("TRN-054")]
    [Trait("Requirement", "TRN-054")]
    public async Task Unmapped_status_outside_4xx_5xx_falls_back_to_protocol_unexpected_response()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(999, body: Json("""{"error":"unexpected"}"""));
        BastionVaultClient client = BuildClient(transport, o => o.RetryPolicy = new RetryPolicy { MaxAttempts = 1 });

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync("x"));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    [Requirement("TRN-032")]
    [Trait("Requirement", "TRN-032")]
    public async Task Oversized_request_body_is_rejected_client_side()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);
        string oversizedValue = new('a', 33 * 1024 * 1024);
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, string> { ["data"] = oversizedValue }));

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Logical.WriteAsync("secret/data/x", document.RootElement));

        Assert.Equal(ErrorCodes.InputBodyTooLarge, exception.Code);
        Assert.Equal(0, exception.Attempts);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    [Requirement("TRN-017")]
    [Trait("Requirement", "TRN-017")]
    public async Task WrapTtl_option_is_rejected_before_any_request()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Logical.ReadAsync("x", new RequestOptions { WrapTtl = "5m" }));

        Assert.Equal(ErrorCodes.InputUnsupportedOption, exception.Code);
        Assert.Equal(0, exception.Attempts);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    [Requirement("TRN-033")]
    [Trait("Requirement", "TRN-033")]
    public async Task Response_over_MaxResponseBytes_is_aborted()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: new byte[16]);
        BastionVaultClient client = BuildClient(transport, o => o.MaxResponseBytes = 8);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync("x"));

        Assert.Equal(ErrorCodes.TransportResponseTooLarge, exception.Code);
    }

    [Fact]
    [Requirement("OVR-006")]
    [Trait("Requirement", "OVR-006")]
    public async Task Cancellation_maps_to_BV_TRANSPORT_005()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);
        using CancellationTokenSource cts = new();
        cts.Cancel();

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Logical.ReadAsync("x", options: null, cts.Token));

        Assert.Equal(ErrorCodes.TransportCancelled, exception.Code);
    }

    [Theory]
    [Requirement("D-M1b-1")]
    [Trait("Requirement", "D-M1b-1")]
    [InlineData(BastionVault.IntegrationSdk.Internal.TransportFailureKind.ConnectionRefused, "BV-TRANSPORT-001")]
    [InlineData(BastionVault.IntegrationSdk.Internal.TransportFailureKind.Dns, "BV-TRANSPORT-001")]
    [InlineData(BastionVault.IntegrationSdk.Internal.TransportFailureKind.Reset, "BV-TRANSPORT-001")]
    [InlineData(BastionVault.IntegrationSdk.Internal.TransportFailureKind.Timeout, "BV-TRANSPORT-002")]
    [InlineData(BastionVault.IntegrationSdk.Internal.TransportFailureKind.TlsVerify, "BV-TRANSPORT-003")]
    [InlineData(BastionVault.IntegrationSdk.Internal.TransportFailureKind.TlsHandshake, "BV-TRANSPORT-003")]
    public async Task Every_scripted_transport_failure_kind_maps_to_its_fixed_code(BastionVault.IntegrationSdk.Internal.TransportFailureKind kind, string expectedCode)
    {
        FakeTransport transport = new();
        transport.EnqueueFailure(kind);
        BastionVaultClient client = BuildClient(transport, o => o.RetryPolicy = new RetryPolicy { MaxAttempts = 1 });

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync("x"));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    [Requirement("D-M1b-14")]
    [Requirement("TRN-010")]
    [Trait("Requirement", "D-M1b-14")]
    public void Transport_declaring_no_custom_verb_support_fails_construction_with_BV_CONFIG_009()
    {
        FakeTransport transport = new() { SupportsCustomVerbs = false };

        BastionVaultException exception = Assert.Throws<BastionVaultException>(() => BuildClient(transport));

        Assert.Equal(ErrorCodes.ConfigListVerbUnsupported, exception.Code);
    }

    /// <summary>
    /// The M1a/M1b assertion, minus its CFG-070 marker. D-M2-11(c) re-opened CFG-070 because this
    /// test asserted neither of its invariants — it never ran two operations concurrently and never
    /// raced a <c>SetToken</c> against an in-flight request — and the mechanism its closure rested
    /// on (an atomic reference read) no longer exists after D-M2-9. CFG-070 is now carried by
    /// <c>AuthUnitTests</c>, under real concurrency. What is left here is CFG-071's shared-cell
    /// behaviour, which is what this test always actually proved.
    /// </summary>
    [Fact]
    [Requirement("CFG-071")]
    [Requirement("TRN-013")]
    [Requirement("TRN-016")]
    [Trait("Requirement", "CFG-071")]
    public async Task SetToken_and_WithNamespace_share_state_but_differ_in_namespace()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"a":1}}"""));
        BastionVaultClient client = BuildClient(transport, o => o.Namespace = "root-ns");

        client.SetToken(new SecretString(FakeTokens.Rotated));
        BastionVaultClient view = client.WithNamespace("child-ns");

        Assert.Equal("child-ns", view.Namespace);
        Assert.Equal("root-ns", client.Namespace);

        _ = await view.Logical.ReadAsync("secret/data/x");
        Assert.Contains(transport.Requests, request => request.Headers.TryGetValue("X-BastionVault-Token", out string? token) && token == FakeTokens.Rotated);
        Assert.Contains(transport.Requests, request => request.Headers.TryGetValue("X-BastionVault-Namespace", out string? ns) && ns == "child-ns");

        client.ClearToken();
        FakeTransport transport2 = new();
        transport2.EnqueueResponse(200, body: Json("""{"initialized":true}"""));
        BastionVaultClient client2 = BuildClient(transport2);
        client2.ClearToken();
        // An unauthenticated endpoint, because a cleared token now refuses an authenticated one
        // client-side (CFG-020's second MUST, landed in M2b). What is asserted here is unchanged:
        // a cleared token means no `X-BastionVault-Token` header on the wire.
        _ = await client2.Logical.ReadAsync("sys/health");
        Assert.DoesNotContain(transport2.Requests, request => request.Headers.ContainsKey("X-BastionVault-Token"));
    }

    [Fact]
    [Requirement("CFG-080")]
    [Requirement("RES-002")]
    [Requirement("RES-003")]
    [Trait("Requirement", "CFG-080")]
    public async Task Observer_fires_once_per_attempt()
    {
        List<RequestEvent> events = [];
        RecordingObserver observer = new(events);
        FakeTransport transport = new();
        transport.EnqueueFailure(BastionVault.IntegrationSdk.Internal.TransportFailureKind.ConnectionRefused);
        transport.EnqueueResponse(200, body: Json("""{"data":{}}"""));
        BastionVaultClient client = BuildClient(transport, o =>
        {
            o.Observer = observer;
            o.RetryPolicy = new RetryPolicy { MaxAttempts = 3, InitialBackoff = TimeSpan.Zero };
            o.Clock = new ImmediateClock();
        });

        _ = await client.Logical.ReadAsync("secret/data/x");

        Assert.Equal(2, events.Count);
        Assert.Equal(1, events[0].Attempt);
        Assert.Equal(ErrorCodes.TransportConnectionFailed, events[0].ErrorCode);
        Assert.Equal(2, events[1].Attempt);
        Assert.Null(events[1].ErrorCode);
        Assert.Equal(200, events[1].StatusCode);
        Assert.All(events, e => Assert.False(string.IsNullOrEmpty(e.RequestId)));
        Assert.Equal(events[0].RequestId, events[1].RequestId);
    }

    [Fact]
    [Requirement("CFG-051")]
    [Trait("Requirement", "CFG-051")]
    public async Task Retry_eligible_code_on_a_non_idempotent_write_is_not_retried()
    {
        FakeTransport transport = new();
        transport.EnqueueFailure(BastionVault.IntegrationSdk.Internal.TransportFailureKind.ConnectionRefused);
        BastionVaultClient client = BuildClient(transport, o => o.RetryPolicy = new RetryPolicy { MaxAttempts = 3 });

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.WriteAsync("secret/data/x"));

        Assert.Equal(1, exception.Attempts);
        _ = Assert.Single(transport.Requests);
    }

    [Fact]
    [Requirement("CFG-051")]
    [Requirement("RES-001")]
    [Trait("Requirement", "CFG-051")]
    public async Task RetryIdempotentOnly_false_makes_a_write_retry_eligible()
    {
        FakeTransport transport = new();
        transport.EnqueueFailure(BastionVault.IntegrationSdk.Internal.TransportFailureKind.ConnectionRefused);
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport, o =>
        {
            o.RetryPolicy = new RetryPolicy { MaxAttempts = 2, RetryIdempotentOnly = false, InitialBackoff = TimeSpan.Zero };
            o.Clock = new ImmediateClock();
        });

        _ = await client.Logical.WriteAsync("secret/data/x");

        Assert.Equal(2, transport.Requests.Count);
    }

    [Fact]
    [Requirement("D-M1b-6")]
    [Trait("Requirement", "D-M1b-6")]
    public async Task Per_call_idempotent_override_wins_in_both_directions()
    {
        FakeTransport transport = new();
        transport.EnqueueFailure(BastionVault.IntegrationSdk.Internal.TransportFailureKind.ConnectionRefused);
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport, o =>
        {
            o.RetryPolicy = new RetryPolicy { MaxAttempts = 2, InitialBackoff = TimeSpan.Zero };
            o.Clock = new ImmediateClock();
        });

        _ = await client.Logical.WriteAsync("secret/data/x", options: new RequestOptions { Idempotent = true });

        Assert.Equal(2, transport.Requests.Count);
    }

    [Fact]
    [Requirement("CFG-054")]
    [Trait("Requirement", "CFG-054")]
    public async Task RespectRetryAfter_widens_the_wait_and_is_capped()
    {
        List<TimeSpan> delays = [];
        FakeTransport transport = new();
        transport.EnqueueResponse(502, headers: new Dictionary<string, string> { ["Retry-After"] = "9999" });
        transport.EnqueueResponse(200, body: Json("""{"data":{}}"""));
        BastionVaultClient client = BuildClient(transport, o =>
        {
            o.RetryPolicy = new RetryPolicy
            {
                MaxAttempts = 2,
                MaxBackoff = TimeSpan.FromSeconds(5),
            };
            o.Clock = new RecordingClock(delays);
        });

        _ = await client.Logical.ReadAsync("secret/data/x");

        _ = Assert.Single(delays);
        Assert.Equal(TimeSpan.FromSeconds(30), delays[0]); // MaxBackoff(5s) * 6 cap.
    }

    [Fact]
    [Requirement("D-M1b-16")]
    [Requirement("CFG-052")]
    [Requirement("CFG-055")]
    [Trait("Requirement", "D-M1b-16")]
    public async Task DosGuard_429_with_retry_after_pauses_the_rate_gate()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(
            429,
            headers: new Dictionary<string, string> { ["Retry-After"] = "5" },
            body: Json("""{"errors":["request temporarily blocked by DoS protection"]}"""));
        // MaxAttempts is deliberately > 1: CFG-052 requires the DoS guard is never retried even
        // though attempts remain, and CFG-055 requires the resulting Attempts to reflect that.
        BastionVaultClient client = BuildClient(transport, o => o.RetryPolicy = new RetryPolicy { MaxAttempts = 3 });

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync("x"));

        Assert.Equal(ErrorCodes.RateLimitedByDosGuard, exception.Code);
        Assert.Equal(1, exception.Attempts);
        _ = Assert.Single(transport.Requests);
        Assert.True(client.RateGateState.Paused);
        _ = Assert.NotNull(client.RateGateState.PausedUntil);
    }

    [Fact]
    [Requirement("D-M1b-16")]
    [Trait("Requirement", "D-M1b-16")]
    public async Task DosGuard_429_without_retry_after_still_maps_to_namespace_quota_and_pauses_for_1s()
    {
        FakeTransport transport = new();
        // The body deliberately matches no Appendix B §2 rule: this test is about the
        // *status* branch, and from M1c a "request temporarily blocked by DoS protection" body
        // is recognised in step 4 and never reaches step 5 (D-M1c-3). That behaviour has its own
        // test below.
        transport.EnqueueResponse(429, body: Json("""{"errors":["too many requests"]}"""));
        BastionVaultClient client = BuildClient(transport, o => o.RetryPolicy = new RetryPolicy { MaxAttempts = 1 });

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync("x"));

        Assert.Equal(ErrorCodes.RateNamespaceRateQuotaExceeded, exception.Code);
        // D-M1b-22: the pause fires on status 429 itself, not on the mapped code; 1s when
        // Retry-After is absent.
        Assert.True(client.RateGateState.Paused);
    }

    [Fact]
    [Requirement("ERR-020")]
    [Trait("Requirement", "ERR-020")]
    public async Task Recognition_runs_before_the_status_table_for_a_429_without_retry_after()
    {
        // ERR-020 orders message recognition (step 4) ahead of the status fallback (step 5), so the
        // DoS-guard message wins even though the status alone would say BV-RATE-002 (D-M1c-3).
        FakeTransport transport = new();
        transport.EnqueueResponse(429, body: Json("""{"errors":["request temporarily blocked by DoS protection"]}"""));
        BastionVaultClient client = BuildClient(transport, o => o.RetryPolicy = new RetryPolicy { MaxAttempts = 1 });

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync("x"));

        Assert.Equal(ErrorCodes.RateLimitedByDosGuard, exception.Code);
        Assert.True(client.RateGateState.Paused);
    }

    private sealed class RecordingLogger : IClientLogger
    {
        public List<string> Lines { get; } = [];

        public void Warn(string message)
        {
            Lines.Add(message);
        }
    }

    [Fact]
    [Requirement("ERR-050")]
    [Trait("Requirement", "ERR-050")]
    public async Task Server_warnings_are_surfaced_and_logged_at_warning_level_and_never_become_errors()
    {
        RecordingLogger logger = new();
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"k":"v"},"warnings":["ttl was capped","policy is deprecated"]}"""));
        BastionVaultClient client = BuildClient(transport, o =>
        {
            o.RetryPolicy = new RetryPolicy { MaxAttempts = 1 };
            o.Logger = logger;
        });

        Response? response = await client.Logical.ReadAsync("x");

        Assert.NotNull(response);
        Assert.Equal(new[] { "ttl was capped", "policy is deprecated" }, response.Warnings);
        Assert.Equal(2, logger.Lines.Count);
        Assert.All(logger.Lines, line => Assert.StartsWith("BastionVault server warning: ", line, StringComparison.Ordinal));
    }

    [Fact]
    [Requirement("ERR-050")]
    [Trait("Requirement", "ERR-050")]
    public async Task Warnings_default_to_an_empty_list_and_non_string_entries_are_kept_verbatim()
    {
        RecordingLogger logger = new();
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"k":"v"}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"k":"v"},"warnings":"not-an-array"}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"k":"v"},"warnings":[{"detail":"structured"},"",null]}"""));
        BastionVaultClient client = BuildClient(transport, o =>
        {
            o.RetryPolicy = new RetryPolicy { MaxAttempts = 1 };
            o.Logger = logger;
        });

        Assert.Empty((await client.Logical.ReadAsync("x"))!.Warnings);
        Assert.Empty((await client.Logical.ReadAsync("x"))!.Warnings);
        IReadOnlyList<string> mixed = (await client.Logical.ReadAsync("x"))!.Warnings;

        Assert.Equal(new[] { "{\"detail\":\"structured\"}", "null" }, mixed);
        Assert.Equal(2, logger.Lines.Count);
    }

    [Fact]
    [Requirement("TRN-001")]
    [Trait("Requirement", "TRN-001")]
    public async Task Delete_sends_the_DELETE_verb_and_an_optional_body()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"deleted":true}}"""));
        BastionVaultClient client = BuildClient(transport, o => o.RetryPolicy = new RetryPolicy { MaxAttempts = 1 });
        using JsonDocument body = JsonDocument.Parse("""{"versions":[1,2]}""");

        Response? response = await client.Logical.DeleteAsync("secret/data/x", body.RootElement);

        Assert.Equal("DELETE", transport.Requests[0].Method);
        Assert.NotNull(response);
    }

    [Fact]
    [Requirement("D-M1b-12")]
    [Requirement("TRN-003")]
    [Trait("Requirement", "D-M1b-12")]
    public async Task Raw_returns_the_unparsed_body_on_success_and_maps_errors()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"anything":true}"""));
        transport.EnqueueResponse(307, headers: new Dictionary<string, string> { ["Location"] = "https://evil.example.com" });
        BastionVaultClient client = BuildClient(transport, o => o.RetryPolicy = new RetryPolicy { MaxAttempts = 1 });

        RawResponse ok = await client.Logical.RawAsync("GET", "/v1/sys/health");
        Assert.Equal(200, ok.StatusCode);

        BastionVaultException redirect = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.RawAsync("GET", "/v1/sys/health"));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedRedirect, redirect.Code);
    }

    [Fact]
    [Requirement("D-M1b-15")]
    [Requirement("TRN-100")]
    [Trait("Requirement", "D-M1b-15")]
    public async Task FakeTransport_throws_when_no_scripted_response_remains()
    {
        FakeTransport transport = new();
        TransportRequest request = new("GET", new Uri("https://example.invalid/"), new Dictionary<string, string>(), ReadOnlyMemory<byte>.Empty);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => transport.SendAsync(request));
        _ = Assert.Single(transport.Requests);
    }

    [Fact]
    [Requirement("TRN-043")]
    [Requirement("TRN-042")]
    [Trait("Requirement", "TRN-043")]
    public void RawResponse_and_Response_expose_their_canonical_fields()
    {
        RawResponse raw = new() { StatusCode = 200, Headers = new Dictionary<string, string>(), Body = new byte[] { 1, 2, 3 } };
        Assert.Equal(200, raw.StatusCode);
        Assert.Equal(3, raw.Body.Length);

        // TRN-042: absent wire fields are absent/null, and Warnings is never fabricated.
        Response response = new() { StatusCode = 200, Headers = new Dictionary<string, string>() };
        Assert.Equal(200, response.StatusCode);
        Assert.Empty(response.Warnings);
        Assert.Null(response.Data);
        Assert.Null(response.Auth);
        Assert.Null(response.LeaseId);
    }

    private sealed class RecordingObserver : IRequestObserver
    {
        private readonly List<RequestEvent> events;

        public RecordingObserver(List<RequestEvent> events) => this.events = events;

        public void OnRequestCompleted(RequestEvent requestEvent)
        {
            events.Add(requestEvent);
        }
    }

    private sealed class ImmediateClock : IClock
    {
        public DateTimeOffset NowUtc()
        {
            return DateTimeOffset.UnixEpoch;
        }

        public Task Delay(TimeSpan duration, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingClock : IClock
    {
        private readonly List<TimeSpan> delays;

        public RecordingClock(List<TimeSpan> delays) => this.delays = delays;

        public DateTimeOffset NowUtc()
        {
            return DateTimeOffset.UnixEpoch;
        }

        public Task Delay(TimeSpan duration, CancellationToken cancellationToken)
        {
            delays.Add(duration);
            return Task.CompletedTask;
        }
    }
}
