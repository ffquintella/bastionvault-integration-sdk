using System.Text;
using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// M5b's unit coverage for section 13's sticky session and bounded failover: DSC-040…DSC-046, plus
/// the two limbs of DSC-041 that D-M5-5 rules on separately and the accounting D-M5-7 pins.
/// </summary>
public sealed class FailoverUnitTests
{
    private const string Leader = """{"initialized":true,"sealed":false,"standby":false,"cluster_healthy":true}""";
    private const string Follower = """{"initialized":true,"sealed":false,"standby":true,"cluster_healthy":true}""";
    private const string Secret = """{"data":{"a":"1"}}""";
    private const string SealedMessage = """{"error":"BastionVault is sealed."}""";
    private const string StandbyMessage = """{"error":"node is in standby"}""";

    private const string One = "https://bv-1.corp.example:8200";
    private const string Two = "https://bv-2.corp.example:8200";
    private const string Three = "https://bv-3.corp.example:8200";

    // ---- DSC-042, DSC-044, D-M5-7: the single replay and its accounting -------------------

    [Fact]
    [Requirement("DSC-042")]
    [Requirement("RES-001")]
    [Trait("Requirement", "DSC-042")]
    public async Task A_replay_does_not_consume_a_retry_attempt_at_MaxAttempts_one()
    {
        // D-M5-7, and the assertion `resilience.failover.read-once` exists to make: at
        // `MaxAttempts: 1` the pass has no retry budget at all, so a design where the replay spends
        // an attempt cannot succeed here. The replay is the RES-001 "+1", never a retry.
        CapturingRequestObserver observer = new();
        RoutingTransport transport = new((request, _) => request.Uri.Host switch
        {
            "bv-1.corp.example" => throw TransportFailureMapper.Map(TransportFailureKind.ConnectionRefused),
            _ => IsProbe(request) ? Json(200, Leader) : Json(200, Secret),
        });
        using BastionVaultClient client = Pinned(transport, maxAttempts: 1, observer: observer);

        Response? response = await client.Logical.ReadAsync("secret/x");

        Assert.NotNull(response);
        Assert.Equal("1", response.Data!["a"].GetString());
        Assert.Equal([$"{One}/v1/secret/x", $"{Two}/v1/sys/health", $"{Two}/v1/secret/x"], Urls(transport));
        // DSC-040/DSC-042: the pin moved, and only because a failure moved it.
        Assert.Equal(Two, client.SelectedNode!.Url);

        // D-M5-7's two counters, observable on the hook: one caller-visible operation keeps one
        // request id across both passes, the probe is reported at attempt 0 (D-M5-17), and the
        // replay's reported attempt continues the accumulated count rather than restarting it.
        RequestEvent[] caller = observer.Events.Where(captured => captured.Attempt > 0).ToArray();
        Assert.Equal([1, 2], caller.Select(captured => captured.Attempt));
        _ = Assert.Single(caller.Select(captured => captured.RequestId).Distinct(StringComparer.Ordinal));
        Assert.Equal(0, Assert.Single(observer.Events.Where(captured => captured.Attempt == 0)).Attempt);
    }

    [Fact]
    [Requirement("DSC-040")]
    [Trait("Requirement", "DSC-040")]
    public async Task A_success_never_re_pins()
    {
        // DSC-040: "no transparent mid-session re-pinning on success paths". Two successful reads
        // both go to the node discovery pinned, and nothing probes in between.
        RoutingTransport transport = new((_, _) => Json(200, Secret));
        using BastionVaultClient client = Pinned(transport);

        _ = await client.Logical.ReadAsync("secret/x");
        _ = await client.Logical.ReadAsync("secret/y");

        Assert.Equal([$"{One}/v1/secret/x", $"{One}/v1/secret/y"], Urls(transport));
        Assert.Equal(One, client.SelectedNode!.Url);
    }

    [Fact]
    [Requirement("DSC-042")]
    [Requirement("DSC-044")]
    [Trait("Requirement", "DSC-044")]
    public async Task Exactly_one_replay_happens_and_a_failing_replay_surfaces_the_original_error()
    {
        // Three candidates, and both the pinned node and the node failover picks are dead. DSC-042
        // allows one replay, so the second failure is terminal; DSC-044 makes the surfaced error the
        // *original* one, which is why Details.host still names the node that died first.
        RoutingTransport transport = new((request, _) => IsProbe(request)
            ? Json(200, request.Uri.Host == "bv-2.corp.example" ? Leader : Follower)
            : throw TransportFailureMapper.Map(TransportFailureKind.ConnectionRefused));
        using BastionVaultClient client = Pinned(transport, maxAttempts: 1, candidates: [One, Two, Three]);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Logical.ReadAsync("secret/x"));

        Assert.Equal(ErrorCodes.DiscoveryNodeUnavailable, failure.Code);
        Assert.Equal("bv-1.corp.example", failure.Details["host"]);
        Assert.Equal("connection_refused", failure.Details["reason"]);
        // DSC-044's "with Attempts incremented": one attempt per pass, two passes.
        Assert.Equal(2, failure.Attempts);
        Assert.True(failure.Retryable);
        // The re-probe is bounded to the cached set minus the failed URL, with no second SRV
        // lookup, and D-M5-22's issue order holds for it too.
        Assert.Equal(
            [
                $"{One}/v1/secret/x",
                $"{Two}/v1/sys/health",
                $"{Three}/v1/sys/health",
                $"{Two}/v1/secret/x",
            ],
            Urls(transport));
    }

    [Fact]
    [Requirement("DSC-042")]
    [Trait("Requirement", "DSC-042")]
    public async Task A_write_is_never_replayed()
    {
        RoutingTransport transport = new((_, _) => throw TransportFailureMapper.Map(TransportFailureKind.Reset));
        using BastionVaultClient client = Pinned(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Logical.WriteAsync("secret/x", JsonDocument.Parse(Secret).RootElement));

        // An ambiguous commit is worse than a failure, so a write fails where a read would recover.
        Assert.Equal(ErrorCodes.DiscoveryNodeUnavailable, failure.Code);
        Assert.Equal(1, failure.Attempts);
        Assert.Equal([$"{One}/v1/secret/x"], Urls(transport));
    }

    [Fact]
    [Requirement("DSC-044")]
    [Trait("Requirement", "DSC-044")]
    public async Task With_nowhere_to_move_the_original_error_stands()
    {
        // Armed, but the only other candidate is unhealthy: DSC-044's second arm.
        RoutingTransport transport = new((request, _) => IsProbe(request)
            ? Json(503, """{"initialized":true,"sealed":true,"standby":false}""")
            : throw TransportFailureMapper.Map(TransportFailureKind.Timeout));
        using BastionVaultClient client = Pinned(transport, maxAttempts: 1);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Logical.ReadAsync("secret/x"));

        Assert.Equal(ErrorCodes.DiscoveryNodeUnavailable, failure.Code);
        Assert.Equal("timeout", failure.Details["reason"]);
        Assert.Equal(1, failure.Attempts);
        Assert.Equal(One, client.SelectedNode!.Url);
        Assert.Equal([$"{One}/v1/secret/x", $"{Two}/v1/sys/health"], Urls(transport));
    }

    [Fact]
    [Requirement("DSC-042")]
    [Requirement("DSC-044")]
    [Trait("Requirement", "DSC-042")]
    public async Task An_unarmed_client_has_no_failover_step_to_take()
    {
        // DSC-042's arming condition, asserted on the step itself and not only through an
        // operation: a single cached candidate means there is nowhere to move, so the step reports
        // that without probing and the pin is left alone (DSC-044's first arm).
        RoutingTransport transport = new((_, _) => Json(200, Leader));
        using BastionVaultClient client = Pinned(transport, candidates: [One]);

        Assert.False(client.Context.Discovery.IsFailoverArmed);
        Assert.Null(await client.Context.Discovery.TryFailoverAsync(One, CancellationToken.None));
        Assert.Empty(transport.Requests);
        Assert.Equal(One, client.SelectedNode!.Url);

        // And the state every discovery-mode client starts in: no cached set at all, so there is
        // nothing to re-probe even when the endpoint matches.
        using BastionVaultClient undiscovered = new(
            new BastionVaultClientOptions
            {
                Address = "vault.corp.example",
                Token = "s.FAKE-token-0000000000000000",
                Transport = transport,
            },
            EnvironmentSource.None);
        Assert.Null(undiscovered.Context.Discovery.Candidates);
        Assert.Null(await undiscovered.Context.Discovery.TryFailoverAsync("vault.corp.example", CancellationToken.None));
        Assert.Empty(transport.Requests);
    }

    // ---- DSC-041 limb (i): scope, and the literal-mode exemption --------------------------

    [Theory]
    [Requirement("DSC-041")]
    [Trait("Requirement", "DSC-041")]
    [InlineData(TransportFailureKind.ConnectionRefused, "BV-DISCOVERY-003")]
    [InlineData(TransportFailureKind.Reset, "BV-DISCOVERY-003")]
    [InlineData(TransportFailureKind.Timeout, "BV-DISCOVERY-003")]
    // Not named by DSC-041's parenthetical, so not node failures — and a DNS failure is the one
    // kind that says nothing about whether the node is alive (D-M5-5, D-M1c-25).
    [InlineData(TransportFailureKind.Dns, "BV-TRANSPORT-001")]
    [InlineData(TransportFailureKind.TlsVerify, "BV-TRANSPORT-003")]
    [InlineData(TransportFailureKind.TlsHandshake, "BV-TRANSPORT-003")]
    public async Task Only_the_three_kinds_DSC_041_names_become_node_failures(TransportFailureKind kind, string expected)
    {
        RoutingTransport transport = new((_, _) => throw TransportFailureMapper.Map(kind));
        using BastionVaultClient client = Pinned(transport, maxAttempts: 1, candidates: [One]);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Logical.ReadAsync("secret/x"));

        Assert.Equal(expected, failure.Code);
    }

    [Theory]
    [Requirement("DSC-041")]
    [Trait("Requirement", "DSC-041")]
    [InlineData(TransportFailureKind.ConnectionRefused, "BV-TRANSPORT-001")]
    [InlineData(TransportFailureKind.Reset, "BV-TRANSPORT-001")]
    [InlineData(TransportFailureKind.Timeout, "BV-TRANSPORT-002")]
    public async Task Literal_mode_keeps_the_M1b_transport_codes(TransportFailureKind kind, string expected)
    {
        // D-M5-5: the reclassification is scoped to a node *discovery chose*. This is what keeps
        // transport.retry.write-not-retried, errors.enrichment.connection-refused-default-address
        // and errors.enrichment.tls-no-ca green without amendment.
        RoutingTransport transport = new((_, _) => throw TransportFailureMapper.Map(kind));
        using BastionVaultClient client = Literal(transport, maxAttempts: 1);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Logical.ReadAsync("secret/x"));

        Assert.Equal(expected, failure.Code);
        Assert.Null(client.SelectedNode);
    }

    // ---- DSC-041 limb (ii): the code is never replaced, in either mode --------------------

    [Theory]
    [Requirement("DSC-041")]
    [Trait("Requirement", "DSC-041")]
    [InlineData(503, """{"error":"BastionVault is sealed."}""", "BV-SERVER-001")]
    [InlineData(503, """{"error":"node is in standby"}""", "BV-SERVER-003")]
    [InlineData(501, """{"error":"vault is uninitialized"}""", "BV-SERVER-007")]
    public async Task A_matching_5xx_keeps_its_appendix_b_code_in_literal_mode(int status, string body, string expected)
    {
        // D-M5-5 limb (ii): the error code is never replaced. §13:125 presupposes BV-SERVER-003
        // survives as the code, and CFG-053 makes a sealed 503 never-retryable — which
        // reclassifying it to the retryable BV-DISCOVERY-003 would invert.
        RoutingTransport transport = new((_, _) => Json(status, body));
        using BastionVaultClient client = Literal(transport, maxAttempts: 1);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Logical.ReadAsync("secret/x"));

        Assert.Equal(expected, failure.Code);
        Assert.Equal(status, failure.StatusCode);
        Assert.Equal([$"https://vault.example.com:8200/v1/secret/x"], Urls(transport));
    }

    [Fact]
    [Requirement("DSC-041")]
    [Requirement("DSC-042")]
    [Trait("Requirement", "DSC-041")]
    public async Task A_matching_5xx_triggers_the_replay_in_discovery_mode_without_becoming_a_discovery_code()
    {
        RoutingTransport transport = new((request, _) => request.Uri.Host switch
        {
            "bv-1.corp.example" => Json(503, SealedMessage),
            _ => IsProbe(request) ? Json(200, Leader) : Json(200, Secret),
        });
        using BastionVaultClient client = Pinned(transport, maxAttempts: 1);

        Response? response = await client.Logical.ReadAsync("secret/x");

        // Limb (ii) contributes the trigger and nothing else: the replay recovers, so the caller
        // sees success and never sees a code at all.
        Assert.NotNull(response);
        Assert.Equal([$"{One}/v1/secret/x", $"{Two}/v1/sys/health", $"{Two}/v1/secret/x"], Urls(transport));
        Assert.Equal(Two, client.SelectedNode!.Url);
    }

    [Fact]
    [Requirement("DSC-041")]
    [Requirement("DSC-044")]
    [Trait("Requirement", "DSC-044")]
    public async Task A_matching_5xx_whose_replay_also_fails_surfaces_the_appendix_b_code()
    {
        RoutingTransport transport = new((request, _) => IsProbe(request)
            ? Json(200, Leader)
            : Json(503, SealedMessage));
        using BastionVaultClient client = Pinned(transport, maxAttempts: 1);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Logical.ReadAsync("secret/x"));

        // Not BV-DISCOVERY-003: "where failover is unarmed or the replay also fails, the caller
        // sees the Appendix B code" (D-M5-5).
        Assert.Equal(ErrorCodes.ServerSealed, failure.Code);
        Assert.False(failure.Retryable);
        Assert.Equal(2, failure.Attempts);
    }

    [Fact]
    [Requirement("DSC-041")]
    [Trait("Requirement", "DSC-041")]
    public async Task With_failover_unarmed_a_standby_5xx_is_retried_exactly_as_CFG_050_says()
    {
        // D-M5-5's composition clause: where failover is unarmed, CFG-050 governs exactly as it
        // does today — BV-SERVER-003 is in the default RetryOn and is retried by backoff.
        RoutingTransport transport = new((_, ordinal) => ordinal == 0 ? Json(503, StandbyMessage) : Json(200, Secret));
        using BastionVaultClient client = Pinned(transport, maxAttempts: 2, candidates: [One]);

        Response? response = await client.Logical.ReadAsync("secret/x");

        Assert.NotNull(response);
        Assert.Equal([$"{One}/v1/secret/x", $"{One}/v1/secret/x"], Urls(transport));
    }

    [Theory]
    [Requirement("DSC-041")]
    [Trait("Requirement", "DSC-041")]
    [InlineData(502, """{"errors":["upstream unavailable"]}""")]
    [InlineData(500, """{"errors":["internal error"]}""")]
    public async Task An_armed_client_does_not_fail_over_on_a_5xx_whose_message_does_not_match(int status, string body)
    {
        // Limb (ii) is a *filter*, not "any 5xx on an armed client": the message must contain
        // sealed, uninitialized or standby. Anything else is an ordinary server error and CFG-050
        // governs it, so nothing is probed and the pin does not move.
        RoutingTransport transport = new((_, _) => Json(status, body));
        using BastionVaultClient client = Pinned(transport, maxAttempts: 1);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Logical.ReadAsync("secret/x"));

        Assert.Equal(status, failure.StatusCode);
        Assert.Equal([$"{One}/v1/secret/x"], Urls(transport));
        Assert.Equal(One, client.SelectedNode!.Url);
    }

    [Fact]
    [Requirement("DSC-041")]
    [Trait("Requirement", "DSC-041")]
    public async Task An_armed_client_does_not_fail_over_on_a_4xx()
    {
        // DSC-041's last sentence: "A 4xx is never a node failure." The status test is what makes
        // that true, so it is asserted rather than assumed.
        RoutingTransport transport = new((_, _) => Json(403, """{"errors":["permission denied"]}"""));
        using BastionVaultClient client = Pinned(transport, maxAttempts: 1);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Logical.ReadAsync("secret/x"));

        Assert.Equal(ErrorCodes.AuthzPermissionDenied, failure.Code);
        Assert.Equal([$"{One}/v1/secret/x"], Urls(transport));
    }

    [Fact]
    [Requirement("DSC-042")]
    [Requirement("RES-001")]
    [Trait("Requirement", "RES-001")]
    public async Task A_replayed_request_retries_on_its_own_terms_within_the_attempt_cap()
    {
        // D-M5-6: "the replayed request's own outcome classifies normally", so a post-failover
        // BV-SERVER-002 still retries under CFG-050. RES-001's cap holds exactly: one attempt in
        // the first pass plus MaxAttempts in the replay is MaxAttempts + 1.
        RoutingTransport transport = new((request, ordinal) => request.Uri.Host switch
        {
            "bv-1.corp.example" => throw TransportFailureMapper.Map(TransportFailureKind.ConnectionRefused),
            _ when IsProbe(request) => Json(200, Leader),
            _ => ordinal == 2 ? Json(502, """{"errors":["upstream unavailable"]}""") : Json(200, Secret),
        });
        using BastionVaultClient client = Pinned(transport, maxAttempts: 2);

        Response? response = await client.Logical.ReadAsync("secret/x");

        Assert.NotNull(response);
        Assert.Equal(
            [$"{One}/v1/secret/x", $"{Two}/v1/sys/health", $"{Two}/v1/secret/x", $"{Two}/v1/secret/x"],
            Urls(transport));
    }

    [Fact]
    [Requirement("RES-001")]
    [Requirement("DSC-042")]
    [Trait("Requirement", "RES-001")]
    public async Task Retries_spent_before_the_node_failure_still_leave_the_total_inside_the_cap()
    {
        // D-M5-28's standing adverse shape, and the one every other failover test omits: the node
        // does not die on attempt 1. Two 502s are retried under CFG-050 (BV-SERVER-002 is in the
        // default RetryOn), and only then does the connection fail — so the triggering pass has
        // already spent the whole budget before the replay begins. Unbounded, the replay would get
        // a fresh MaxAttempts and the caller would see six wire attempts against a cap of four.
        RoutingTransport transport = new((request, ordinal) => IsProbe(request)
            ? Json(200, Leader)
            : request.Uri.Host == "bv-1.corp.example"
                ? ordinal < 2
                    ? Json(502, """{"errors":["upstream unavailable"]}""")
                    : throw TransportFailureMapper.Map(TransportFailureKind.ConnectionRefused)
                : Json(502, """{"errors":["upstream unavailable"]}"""));
        using BastionVaultClient client = Pinned(transport, maxAttempts: 3);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Logical.ReadAsync("secret/x"));

        // The replay happened (DSC-042's MUST is not conditioned on which attempt died) and its
        // own outcome classified normally (D-M5-6) — but within the remaining budget, so it got
        // one attempt and not three.
        Assert.Equal(
            [
                $"{One}/v1/secret/x",
                $"{One}/v1/secret/x",
                $"{One}/v1/secret/x",
                $"{Two}/v1/sys/health",
                $"{Two}/v1/secret/x",
            ],
            Urls(transport));

        // RES-001, on both surfaces a caller can see: the wire, and Error.Attempts.
        const int cap = 3 + 1;
        Assert.Equal(cap, transport.Requests.Count(request => !IsProbe(request)));
        Assert.Equal(cap, failure.Attempts);
        Assert.True(failure.Attempts <= cap, $"Error.Attempts was {failure.Attempts}, cap is {cap}.");
        Assert.Equal(ErrorCodes.ServerUnavailable, failure.Code);
    }

    [Fact]
    [Requirement("DSC-042")]
    [Requirement("AUT-003")]
    [Trait("Requirement", "DSC-042")]
    public async Task A_failover_replay_does_not_mint_a_second_re_login()
    {
        // D-M5-29: failover wraps re-login, so a failover replay re-enters the re-login wrapper
        // with a fresh invocation. One caller-visible operation gets one re-login (D-M2-9), so the
        // replay must inherit it spent. MinReloginInterval is set to zero deliberately: the
        // 30-second default would refuse the second re-login for an unrelated reason and hide the
        // defect.
        MutableClock clock = new(DateTimeOffset.UnixEpoch);
        int firstNodeReads = 0;
        RoutingTransport transport = new((request, _) =>
        {
            if (request.Method == "POST")
            {
                return Json(200, LoginBody);
            }

            if (IsProbe(request))
            {
                return Json(200, Leader);
            }

            // bv-1 answers 403 once, then dies; bv-2 answers 403, which would re-login again.
            return request.Uri.Host == "bv-1.corp.example" && ++firstNodeReads > 1
                ? throw TransportFailureMapper.Map(TransportFailureKind.ConnectionRefused)
                : Json(403, """{"errors":["permission denied"]}""");
        });

        using BastionVaultClient client = new(
            new BastionVaultClientOptions
            {
                Address = "vault.corp.example",
                Transport = transport,
                Clock = clock,
                RateGate = new RateGate { RatePerSecond = 0 },
                RetryPolicy = new RetryPolicy { MaxAttempts = 1, InitialBackoff = TimeSpan.Zero },
                TokenSource = TokenSource.Login(
                    AuthMethod.Userpass,
                    LoginCredentials.ForUserpass("alice", new SecretString("password-fixture")),
                    new LoginOptions
                    {
                        ReloginOnPermissionDenied = true,
                        MinReloginInterval = TimeSpan.Zero,
                    }),
            },
            EnvironmentSource.None);
        client.Context.Discovery.Seed(
            new NodeSelection(One, NodeState.ActiveLeader, null),
            [Candidate(One), Candidate(Two)]);

        _ = await client.Auth.AuthenticateAsync();
        clock.Now += TimeSpan.FromMinutes(1);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Logical.ReadAsync("secret/x"));

        Assert.Equal(ErrorCodes.AuthzPermissionDenied, failure.Code);
        // Two logins: the AUT-002 lazy one this test forced eagerly, and AUT-003's single re-login.
        // A third would mean the failover replay minted its own.
        Assert.Equal(2, transport.Requests.Count(request => request.Method == "POST"));
        // And the failover itself still happened, so the fix constrains the re-login only.
        Assert.Equal(Two, client.SelectedNode!.Url);
    }

    // ---- DSC-043: concurrent failures serialise on one lock ------------------------------

    [Fact]
    [Requirement("DSC-043")]
    [Trait("Requirement", "DSC-043")]
    public async Task Concurrent_node_failures_re_probe_once_and_the_late_arrival_reuses_the_new_pick()
    {
        // Genuine concurrency, not a simulated sequence: both reads are held inside the transport
        // until both have reached the dead node, so both enter failover at once. D-M5-12's rule is
        // what makes the second one cheap — it compares the endpoint it failed on with the endpoint
        // now pinned, finds they differ, and reuses the pick without re-probing.
        BarrierTransport transport = new(holdUntil: 2);
        using BastionVaultClient client = Pinned(transport, maxAttempts: 1);

        Response?[] responses = await Task.WhenAll(
            client.Logical.ReadAsync("secret/x"),
            client.Logical.ReadAsync("secret/y"));

        Assert.All(responses, response => Assert.NotNull(response));
        Assert.Equal(1, transport.Probes);
        Assert.Equal(Two, client.SelectedNode!.Url);
        // Two reads on the dead node, one probe, two replays.
        Assert.Equal(5, transport.Requests.Count);
    }

    // ---- DSC-045: node-local operations are excluded -------------------------------------

    [Fact]
    [Requirement("DSC-045")]
    [Trait("Requirement", "DSC-045")]
    public async Task A_node_local_operation_is_excluded_from_failover_and_surfaces_the_node_failure()
    {
        RoutingTransport transport = new((request, _) => IsProbe(request)
            ? Json(200, Leader)
            : throw TransportFailureMapper.Map(TransportFailureKind.Reset));
        using BastionVaultClient client = Pinned(transport, maxAttempts: 1);
        // D-M5-13: the seam is internal and mints no public knob, because every operation DSC-045
        // lists — SSH/RDP sessions, watch=1 polls, FIDO2 ceremonies, plugin downloads, recording
        // streams — belongs to M6, M9 or M10. It is driven here through InternalsVisibleTo.
        RequestExecutor executor = new(client.Context, string.Empty);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => executor.ExecuteAsync(
                "GET",
                "secret/x",
                null,
                null,
                defaultIdempotent: true,
                treatNotFoundEmptyAsAbsent: false,
                CancellationToken.None,
                nodeLocal: true));

        Assert.Equal(ErrorCodes.DiscoveryNodeUnavailable, failure.Code);
        Assert.Equal(1, failure.Attempts);
        // No probe, no replay: the caller reconnects instead (DSC-045).
        Assert.Equal([$"{One}/v1/secret/x"], Urls(transport));
        Assert.Equal(One, client.SelectedNode!.Url);
    }

    [Fact]
    [Requirement("DSC-045")]
    [Trait("Requirement", "DSC-045")]
    public async Task The_same_operation_without_the_node_local_flag_does_fail_over()
    {
        // The paired half: the flag, and nothing else about the operation, is what suppresses it.
        RoutingTransport transport = new((request, _) => request.Uri.Host switch
        {
            "bv-1.corp.example" => throw TransportFailureMapper.Map(TransportFailureKind.Reset),
            _ => IsProbe(request) ? Json(200, Leader) : Json(200, Secret),
        });
        using BastionVaultClient client = Pinned(transport, maxAttempts: 1);
        RequestExecutor executor = new(client.Context, string.Empty);

        RequestExecutor.Outcome outcome = await executor.ExecuteAsync(
            "GET",
            "secret/x",
            null,
            null,
            defaultIdempotent: true,
            treatNotFoundEmptyAsAbsent: false,
            CancellationToken.None);

        Assert.False(outcome.IsEmpty);
        Assert.Equal(Two, client.SelectedNode!.Url);
    }

    // ---- DSC-046: Reconnect ---------------------------------------------------------------

    [Fact]
    [Requirement("DSC-046")]
    [Trait("Requirement", "DSC-046")]
    public async Task Reconnect_re_runs_full_discovery_and_is_safe_to_call_concurrently()
    {
        CountingResolver resolver = new(new SrvRecord("bv-2.corp.example", 8200, 10, 50));
        RoutingTransport transport = new((_, _) => Json(200, Leader));
        using BastionVaultClient client = Pinned(transport, resolver: resolver);

        // DSC-046 is the explicit recovery path, so unlike ConnectAsync it re-runs the *whole*
        // pipeline — a fresh SRV lookup included — every time it is called.
        NodeSelection?[] picks = await Task.WhenAll(
            client.ReconnectAsync(),
            client.ReconnectAsync(),
            client.ReconnectAsync());

        Assert.All(picks, pick => Assert.Equal(Two, pick!.Url));
        Assert.Equal(3, resolver.Queries);
        Assert.Equal(Two, client.SelectedNode!.Url);
        // Serialised on the one lock, so the pin is never written by two re-discoveries at once.
        Assert.Equal(3, transport.Requests.Count);
    }

    // ---- helpers ---------------------------------------------------------------------------

    /// <summary>A login response body (AUT-010): the shape the login runner parses.</summary>
    private const string LoginBody =
        """
        {"renewable":false,"lease_id":"","lease_duration":0,
         "auth":{"client_token":"s.FAKE-relogin-0000000000000","policies":["default"],
                 "metadata":{},"lease_duration":1200,"renewable":true},
         "data":{}}
        """;

    private static Candidate Candidate(string url)
    {
        Uri uri = new(url, UriKind.Absolute);
        return new Candidate(url, uri.Host, uri.Port, null, null);
    }

    /// <summary>An <see cref="IClock"/> a test can move, so AUT-003's interval can be crossed.</summary>
    private sealed class MutableClock : IClock
    {
        public MutableClock(DateTimeOffset now)
        {
            Now = now;
        }

        public DateTimeOffset Now { get; set; }

        public DateTimeOffset NowUtc()
        {
            return Now;
        }

        public Task Delay(TimeSpan duration, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private static bool IsProbe(TransportRequest request)
    {
        return request.Uri.AbsolutePath.EndsWith("/sys/health", StringComparison.Ordinal);
    }

    private static TransportResponse Json(int status, string body)
    {
        return new TransportResponse(
            status,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Encoding.UTF8.GetBytes(body));
    }

    private static IEnumerable<string> Urls(ITransportRecorder transport)
    {
        return transport.Requests.Select(request => request.Uri.AbsoluteUri);
    }

    /// <summary>
    /// A discovery-mode client seeded with an already-pinned node and a cached candidate set, which
    /// is what <c>settings.__pinned</c> / <c>settings.__candidates</c> configure for a fixture
    /// (D-M5-15).
    /// </summary>
    private static BastionVaultClient Pinned(
        ITransport transport,
        int maxAttempts = 3,
        IReadOnlyList<string>? candidates = null,
        IRequestObserver? observer = null,
        ISrvResolver? resolver = null)
    {
        BastionVaultClient client = new(
            new BastionVaultClientOptions
            {
                Address = "vault.corp.example",
                Token = "s.FAKE-token-0000000000000000",
                Transport = transport,
                Observer = observer,
                SrvResolver = resolver,
                RateGate = new RateGate { RatePerSecond = 0 },
                RetryPolicy = new RetryPolicy { MaxAttempts = maxAttempts, InitialBackoff = TimeSpan.Zero },
            },
            EnvironmentSource.None);

        IReadOnlyList<string> urls = candidates ?? [One, Two];
        client.Context.Discovery.Seed(
            new NodeSelection(One, NodeState.ActiveLeader, null),
            urls.Select(url => new Uri(url, UriKind.Absolute))
                .Select(uri => new Candidate(uri.AbsoluteUri.TrimEnd('/'), uri.Host, uri.Port, null, null))
                .ToArray());
        return client;
    }

    private static BastionVaultClient Literal(ITransport transport, int maxAttempts = 3)
    {
        return new BastionVaultClient(
            new BastionVaultClientOptions
            {
                Address = "https://vault.example.com:8200",
                Token = "s.FAKE-token-0000000000000000",
                Transport = transport,
                RateGate = new RateGate { RatePerSecond = 0 },
                RetryPolicy = new RetryPolicy { MaxAttempts = maxAttempts, InitialBackoff = TimeSpan.Zero },
            },
            EnvironmentSource.None);
    }

    private interface ITransportRecorder
    {
        public IReadOnlyList<TransportRequest> Requests { get; }
    }

    /// <summary>
    /// A transport that answers by URL rather than by queue position, because a failover test sends
    /// three different requests to two different hosts and a queue cannot express "whatever arrives
    /// at the dead node fails".
    /// </summary>
    private sealed class RoutingTransport : ITransport, ITransportRecorder
    {
        private readonly Func<TransportRequest, int, TransportResponse> answer;
        private readonly List<TransportRequest> requests = [];
        private readonly object gate = new();

        public RoutingTransport(Func<TransportRequest, int, TransportResponse> answer)
        {
            this.answer = answer;
        }

        public IReadOnlyList<TransportRequest> Requests
        {
            get
            {
                lock (gate)
                {
                    return requests.ToArray();
                }
            }
        }

        public bool SupportsCustomVerbs => true;

        public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken = default)
        {
            int ordinal;
            lock (gate)
            {
                requests.Add(request);
                ordinal = requests.Count - 1;
            }

            return Task.FromResult(answer(request, ordinal));
        }
    }

    /// <summary>
    /// A transport that holds every request to the pinned node until <c>holdUntil</c> of them have
    /// arrived, and only then fails them all — so two callers are genuinely inside failover at the
    /// same time (DSC-043).
    /// </summary>
    private sealed class BarrierTransport : ITransport, ITransportRecorder
    {
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<TransportRequest> requests = [];
        private readonly object gate = new();
        private readonly int holdUntil;
        private int arrived;

        public BarrierTransport(int holdUntil)
        {
            this.holdUntil = holdUntil;
        }

        public int Probes { get; private set; }

        public IReadOnlyList<TransportRequest> Requests
        {
            get
            {
                lock (gate)
                {
                    return requests.ToArray();
                }
            }
        }

        public bool SupportsCustomVerbs => true;

        public async Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken = default)
        {
            bool hold;
            lock (gate)
            {
                requests.Add(request);
                if (IsProbe(request))
                {
                    Probes++;
                }

                hold = request.Uri.Host == "bv-1.corp.example";
                if (hold && ++arrived >= holdUntil)
                {
                    _ = released.TrySetResult();
                }
            }

            if (hold)
            {
                await released.Task.ConfigureAwait(false);
                throw TransportFailureMapper.Map(TransportFailureKind.ConnectionRefused);
            }

            return IsProbe(request) ? Json(200, Leader) : Json(200, Secret);
        }
    }

    private sealed class CountingResolver : ISrvResolver
    {
        private readonly SrvRecord[] records;

        public CountingResolver(params SrvRecord[] records)
        {
            this.records = records;
        }

        public int Queries { get; private set; }

        public Task<IReadOnlyList<SrvRecord>> ResolveAsync(string ownerName, CancellationToken cancellationToken = default)
        {
            Queries++;
            return Task.FromResult<IReadOnlyList<SrvRecord>>(records);
        }
    }
}
