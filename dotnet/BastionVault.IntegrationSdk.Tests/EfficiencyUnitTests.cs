using System.Text;
using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;
using BastionVault.IntegrationSdk.Testing;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// Section 14's client rate gate (EFF-001…EFF-006), batch endpoint (BAT-001…BAT-008) and
/// <c>Kv.ReadMany</c> (KV-010), at the level the shared fixtures cannot reach: concurrency,
/// exemptions, and the client-side refusals that have no wire exchange.
/// </summary>
public sealed class EfficiencyUnitTests
{
    private const string Address = "https://vault.example.com:8200";

    // ------------------------------------------------------------------ rate gate

    [Fact]
    [Requirement("EFF-001")]
    [Requirement("EFF-002")]
    [Trait("Requirement", "EFF-002")]
    public async Task Waiters_are_served_in_arrival_order_and_a_later_one_cannot_overtake_an_earlier_one()
    {
        // EFF-002 is the requirement no fixture can assert: a fixture drives one operation, and
        // "FIFO" is only observable when two waiters are queued at once. Burst 1 so the second
        // arrival must queue, and a clock whose Delay is released by hand so the ordering is
        // decided by the gate rather than by the thread pool.
        HeldClock clock = new();
        ClientRateGate gate = new(new RateGate { RatePerSecond = 1, Burst = 1 }, clock);
        List<string> completed = [];

        Task first = Complete(gate.AcquireAsync(EgressKind.Request, default), "first", completed);
        Task second = Complete(gate.AcquireAsync(EgressKind.Request, default), "second", completed);
        Task third = Complete(gate.AcquireAsync(EgressKind.Request, default), "third", completed);

        // The burst token goes to the first arrival, with no wait at all.
        Assert.True(first.IsCompletedSuccessfully);
        Assert.Equal(["first"], completed);

        // The second is holding the only outstanding Delay; the third is behind it in the chain
        // and has not even reserved a slot yet, which is what makes the order arrival order.
        await WaitUntil(() => clock.Outstanding == 1).ConfigureAwait(false);
        Assert.False(second.IsCompleted);
        Assert.False(third.IsCompleted);

        clock.ReleaseNext();
        await second.ConfigureAwait(false);
        Assert.Equal(["first", "second"], completed);

        await WaitUntil(() => clock.Outstanding == 1).ConfigureAwait(false);
        clock.ReleaseNext();
        await third.ConfigureAwait(false);
        Assert.Equal(["first", "second", "third"], completed);

        // One token per second, and the reservations are a schedule rather than a poll: the
        // second waiter is granted at T+1s and the third at T+2s, measured from one unmoved clock.
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], clock.Waits);
    }

    [Fact]
    [Requirement("EFF-001")]
    [Trait("Requirement", "EFF-001")]
    public async Task Burst_requests_pass_immediately_and_the_next_one_waits_one_interval()
    {
        VirtualClock clock = new();
        ClientRateGate gate = new(new RateGate { RatePerSecond = 8, Burst = 3 }, clock);

        for (int index = 0; index < 4; index++)
        {
            await gate.AcquireAsync(EgressKind.Request, default).ConfigureAwait(false);
        }

        // Exactly Burst grants are free; the fourth pays one interval (1s / 8).
        Assert.Equal([TimeSpan.FromMilliseconds(125)], clock.Waits);
    }

    [Theory]
    [Requirement("EFF-001")]
    [Trait("Requirement", "EFF-001")]
    [InlineData(0, 16)]
    [InlineData(8, 0)]
    public async Task Either_field_at_zero_disables_the_gate_entirely(int ratePerSecond, int burst)
    {
        // EFF-001 reads "setting either to 0 disables the gate". Before M8d the .NET IsDisabled
        // read only RatePerSecond, which with a live bucket would have made Burst = 0 mean
        // "nothing may ever pass" — the opposite of the requirement. rust/.../rate.rs has read
        // both limbs since M1a.
        RateGate configuration = new() { RatePerSecond = ratePerSecond, Burst = burst };
        Assert.True(configuration.IsDisabled);

        VirtualClock clock = new();
        ClientRateGate gate = new(configuration, clock);
        for (int index = 0; index < 50; index++)
        {
            await gate.AcquireAsync(EgressKind.Request, default).ConfigureAwait(false);
        }

        Assert.Empty(clock.Waits);

        // EFF-006 for a gate that is off. `Burst = 0` is itself a disabling value, so reporting
        // the configured burst would report `Paused = false, AvailableTokens = 0` — the pair a
        // diagnostics consumer reads as "fully throttled" — for a gate that withheld nothing
        // across the fifty acquisitions above.
        RateGateState state = gate.Snapshot();
        Assert.False(state.Paused);
        Assert.Null(state.PausedUntil);
        Assert.Equal(int.MaxValue, state.AvailableTokens);
    }

    [Theory]
    [Requirement("EFF-005")]
    [Trait("Requirement", "EFF-005")]
    [InlineData("DiscoveryProbe")]
    [InlineData("SrvResolution")]
    public async Task An_exempt_egress_never_waits_even_when_the_bucket_is_empty(string kindName)
    {
        // The parameter is a string because `EgressKind` is internal and an xUnit theory method
        // is public; the enum is parsed here rather than widened for a test.
        EgressKind kind = Enum.Parse<EgressKind>(kindName);
        VirtualClock clock = new();
        ClientRateGate gate = new(new RateGate { RatePerSecond = 1, Burst = 1 }, clock);
        await gate.AcquireAsync(EgressKind.Request, default).ConfigureAwait(false); // drains the burst

        await gate.AcquireAsync(kind, default).ConfigureAwait(false);

        Assert.True(ClientRateGate.IsExempt(kind));
        Assert.False(ClientRateGate.IsExempt(EgressKind.Request));
        Assert.Empty(clock.Waits);
    }

    [Fact]
    [Requirement("EFF-006")]
    [Trait("Requirement", "EFF-006")]
    public async Task Available_tokens_falls_with_each_grant_and_refills_with_elapsed_time()
    {
        VirtualClock clock = new();
        ClientRateGate gate = new(new RateGate { RatePerSecond = 4, Burst = 2 }, clock);

        Assert.Equal(2, gate.Snapshot().AvailableTokens);
        await gate.AcquireAsync(EgressKind.Request, default).ConfigureAwait(false);
        Assert.Equal(1, gate.Snapshot().AvailableTokens);
        await gate.AcquireAsync(EgressKind.Request, default).ConfigureAwait(false);
        Assert.Equal(0, gate.Snapshot().AvailableTokens);

        // One interval of real time buys exactly one token back, and the cap holds at Burst.
        clock.Advance(TimeSpan.FromMilliseconds(250));
        Assert.Equal(1, gate.Snapshot().AvailableTokens);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(2, gate.Snapshot().AvailableTokens);
    }

    [Fact]
    [Requirement("EFF-003")]
    [Requirement("EFF-006")]
    [Trait("Requirement", "EFF-003")]
    public void A_pause_drops_the_accumulated_tokens_holds_the_queue_and_is_never_shortened()
    {
        VirtualClock clock = new();
        ClientRateGate gate = new(new RateGate { RatePerSecond = 4, Burst = 8 }, clock);
        DateTimeOffset start = clock.NowUtc();

        // A full bucket, then a 30 s pause: EFF-003's "drop accumulated tokens" means the eight
        // tokens do not survive the pause, so nothing is available during it.
        Assert.Equal(8, gate.Snapshot().AvailableTokens);
        gate.Pause(start + TimeSpan.FromSeconds(30));
        RateGateState paused = gate.Snapshot();
        Assert.True(paused.Paused);
        Assert.Equal(start + TimeSpan.FromSeconds(30), paused.PausedUntil);
        Assert.Equal(0, paused.AvailableTokens);

        // A second, nearer 429 does not release the queue early. `nextFree` is a high-water mark
        // and cannot move backwards without handing back tokens EFF-003 says were dropped, so a
        // PausedUntil that moved back would report a resumption that is not going to happen.
        gate.Pause(start + TimeSpan.FromSeconds(1));
        Assert.Equal(start + TimeSpan.FromSeconds(30), gate.Snapshot().PausedUntil);

        // The pause expires by the clock, not by a flag: Paused goes false while PausedUntil
        // keeps its value so a caller can still see when it ended.
        clock.Advance(TimeSpan.FromSeconds(31));
        RateGateState resumed = gate.Snapshot();
        Assert.False(resumed.Paused);
        Assert.Equal(start + TimeSpan.FromSeconds(30), resumed.PausedUntil);

        // And the bucket restarts empty rather than bursting the instant the pause lifts: the
        // first waiter is released *at* the pause end, which is one token, and 4/s buys four
        // more in the second that follows — five, not the eight that were dropped.
        Assert.Equal(5, resumed.AvailableTokens);
    }

    [Fact]
    [Requirement("EFF-003")]
    [Trait("Requirement", "EFF-003")]
    public async Task A_paused_gate_holds_a_waiter_until_the_pause_ends_rather_than_failing_it()
    {
        // EFF-003: "Requests already waiting MUST NOT fail." The waiter is delayed to the end of
        // the pause and then proceeds.
        VirtualClock clock = new();
        ClientRateGate gate = new(new RateGate { RatePerSecond = 8, Burst = 16 }, clock);
        gate.Pause(clock.NowUtc() + TimeSpan.FromSeconds(5));

        await gate.AcquireAsync(EgressKind.Request, default).ConfigureAwait(false);

        Assert.Equal([TimeSpan.FromSeconds(5)], clock.Waits);
    }

    [Fact]
    [Requirement("EFF-003")]
    [Trait("Requirement", "EFF-003")]
    public async Task A_pause_declared_while_a_waiter_sleeps_still_holds_that_waiter()
    {
        // The leak this closes: a waiter is granted an instant *before* it sleeps, so a 429 that
        // arrives while it sleeps used to let exactly one request out inside the ban window —
        // the failure section 14 exists to prevent, and one that earns a second 429. EFF-003
        // says "pause the whole queue" without qualification, and a waiter already in the queue
        // is in the queue.
        HeldClock clock = new();
        ClientRateGate gate = new(new RateGate { RatePerSecond = 1, Burst = 1 }, clock);
        DateTimeOffset start = clock.NowUtc();

        await gate.AcquireAsync(EgressKind.Request, default).ConfigureAwait(false); // takes the burst token
        Task held = gate.AcquireAsync(EgressKind.Request, default);

        // The waiter is asleep on a grant of start + 1 s.
        await WaitUntil(() => clock.Outstanding == 1).ConfigureAwait(false);
        Assert.Equal([TimeSpan.FromSeconds(1)], clock.Waits);

        // A concurrent request now takes a 429 with Retry-After: 5.
        gate.Pause(start + TimeSpan.FromSeconds(5));

        // Its original grant arrives — and must not release it, because the pause reaches past it.
        clock.ReleaseNext();
        await WaitUntil(() => clock.Outstanding == 1).ConfigureAwait(false);
        Assert.False(held.IsCompleted);

        // It re-queued behind the pause rather than sleeping the remainder blindly, so the
        // resumption obeys the rate: the new grant is the pause end, not the old instant.
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)], clock.Waits);

        // And the re-validation terminates: the second grant is at or past the pause, so the
        // waiter is released on the next pass rather than looping.
        clock.ReleaseNext();
        await held.ConfigureAwait(false);
        Assert.Equal(2, clock.Waits.Count);
    }

    [Fact]
    [Requirement("EFF-003")]
    [Trait("Requirement", "EFF-003")]
    public async Task A_second_later_pause_holds_the_waiter_again_and_an_earlier_one_does_not()
    {
        // Termination depends on `pausedUntil` never moving backwards: each extra pass needs a
        // strictly later pause, which needs another 429. A nearer pause must therefore cost the
        // waiter nothing, and a later one must cost it exactly one more pass.
        HeldClock clock = new();
        ClientRateGate gate = new(new RateGate { RatePerSecond = 1, Burst = 1 }, clock);
        DateTimeOffset start = clock.NowUtc();

        await gate.AcquireAsync(EgressKind.Request, default).ConfigureAwait(false);
        Task held = gate.AcquireAsync(EgressKind.Request, default);
        await WaitUntil(() => clock.Outstanding == 1).ConfigureAwait(false);

        gate.Pause(start + TimeSpan.FromSeconds(5));
        clock.ReleaseNext();
        await WaitUntil(() => clock.Outstanding == 1).ConfigureAwait(false);

        // A later 429 arrives during the second sleep: one more pass, held to the new end.
        gate.Pause(start + TimeSpan.FromSeconds(30));
        clock.ReleaseNext();
        await WaitUntil(() => clock.Outstanding == 1).ConfigureAwait(false);
        Assert.False(held.IsCompleted);

        // A *nearer* 429 during the third sleep is a no-op: it cannot move `pausedUntil` back,
        // so it cannot add a pass, and the waiter is released.
        gate.Pause(start + TimeSpan.FromSeconds(2));
        clock.ReleaseNext();
        await held.ConfigureAwait(false);

        Assert.Equal(
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)],
            clock.Waits);
    }

    [Fact]
    [Requirement("EFF-002")]
    [Trait("Requirement", "EFF-002")]
    public async Task A_cancelled_waiter_releases_the_queue_behind_it()
    {
        // The chain is the FIFO mechanism, so a waiter that throws must still complete its own
        // link: one cancellation costs one slot, never the whole queue.
        HeldClock clock = new();
        ClientRateGate gate = new(new RateGate { RatePerSecond = 1, Burst = 1 }, clock);
        using CancellationTokenSource cancellation = new();

        await gate.AcquireAsync(EgressKind.Request, default).ConfigureAwait(false);
        Task cancelled = gate.AcquireAsync(EgressKind.Request, cancellation.Token);
        Task behind = gate.AcquireAsync(EgressKind.Request, default);

        await WaitUntil(() => clock.Outstanding == 1).ConfigureAwait(false);
        cancellation.Cancel();
        clock.ReleaseNext(cancel: true);
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled).ConfigureAwait(false);

        await WaitUntil(() => clock.Outstanding == 1).ConfigureAwait(false);
        clock.ReleaseNext();
        await behind.ConfigureAwait(false);
    }

    [Fact]
    [Requirement("EFF-004")]
    [Trait("Requirement", "EFF-004")]
    public async Task A_429_without_retry_after_pauses_for_exactly_one_second()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(429, body: Json("""{"errors":["too many requests"]}"""));
        BastionVaultClient client = BuildClient(transport, options =>
        {
            options.RateGate = new RateGate();
            options.Clock = new VirtualClock();
        });

        _ = await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync("x")).ConfigureAwait(false);

        RateGateState state = client.RateGateState;
        Assert.True(state.Paused);
        Assert.Equal(DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(1), state.PausedUntil);
        Assert.Equal(0, state.AvailableTokens);
    }

    [Fact]
    [Requirement("EFF-005")]
    [Trait("Requirement", "EFF-005")]
    public async Task Discovery_probes_are_not_gated_even_with_a_bucket_that_admits_one_request()
    {
        // The end-to-end half of EFF-005: three probes go out with no gate wait at all, on a
        // client whose bucket would have spaced them a second apart.
        VirtualClock clock = new();
        FakeTransport transport = new();
        for (int index = 0; index < 3; index++)
        {
            transport.EnqueueResponse(200, body: Json("""{"initialized":true,"sealed":false,"standby":false,"cluster_healthy":true}"""));
        }

        BastionVaultClient client = new(
            new BastionVaultClientOptions
            {
                Address = "vault.example.com",
                Transport = transport,
                Clock = clock,
                RateGate = new RateGate { RatePerSecond = 1, Burst = 1 },
                RetryPolicy = new RetryPolicy { MaxAttempts = 1 },
                SrvResolver = new StaticSrvResolver(
                [
                    new SrvRecord("a.example.com", 8200, 0, 0),
                    new SrvRecord("b.example.com", 8200, 0, 0),
                    new SrvRecord("c.example.com", 8200, 0, 0),
                ]),
            },
            EnvironmentSource.None);

        DiscoveryReport report = await client.DiscoverAsync().ConfigureAwait(false);

        Assert.Equal(3, report.Ranked.Count);
        Assert.Equal(3, transport.Requests.Count);
        Assert.Empty(clock.Waits);
    }

    // ------------------------------------------------------------------ Sys.Batch

    [Fact]
    [Requirement("BAT-002")]
    [Trait("Requirement", "BAT-002")]
    public async Task An_empty_batch_is_refused_client_side()
    {
        BastionVaultClient client = BuildClient(new FakeTransport());

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Sys.BatchAsync([])).ConfigureAwait(false);

        Assert.Equal(ErrorCodes.InputEmptyCollection, failure.Code);
        Assert.Equal(0, failure.Attempts);
    }

    [Fact]
    [Requirement("BAT-002")]
    [Trait("Requirement", "BAT-002")]
    public async Task BatchMaxOperations_is_configurable_and_names_the_cap_it_enforced()
    {
        BastionVaultClient client = BuildClient(new FakeTransport(), options => options.BatchMaxOperations = 2);
        BatchOperation[] operations = [Read("a"), Read("b"), Read("c")];

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Sys.BatchAsync(operations)).ConfigureAwait(false);

        Assert.Equal(ErrorCodes.InputBatchTooLarge, failure.Code);
        Assert.Equal(2, failure.Details["max"]);
        Assert.Equal(3, failure.Details["count"]);
        Assert.Equal(128, BuildClient(new FakeTransport()).Config.BatchMaxOperations);
        Assert.Equal(2, client.Config.BatchMaxOperations);
    }

    [Fact]
    [Requirement("CFG-003")]
    [Trait("Requirement", "CFG-003")]
    public void BatchMaxOperations_below_one_is_refused_at_construction()
    {
        BastionVaultException failure = Assert.Throws<BastionVaultException>(() => new BastionVaultClient(
            new BastionVaultClientOptions { Address = Address, Transport = new FakeTransport(), BatchMaxOperations = 0 },
            EnvironmentSource.None));

        Assert.Equal(ErrorCodes.ConfigInvalidSettingValue, failure.Code);
        Assert.Equal("BatchMaxOperations", failure.Details["setting"]);
    }

    [Theory]
    [Requirement("BAT-004")]
    [Trait("Requirement", "BAT-004")]
    [InlineData(BatchOperationKind.Read, true)]
    [InlineData(BatchOperationKind.Delete, true)]
    [InlineData(BatchOperationKind.List, true)]
    [InlineData(BatchOperationKind.Write, false)]
    public async Task Data_is_required_for_a_write_and_rejected_for_every_other_kind(BatchOperationKind kind, bool withData)
    {
        BastionVaultClient client = BuildClient(new FakeTransport());
        BatchOperation operation = new()
        {
            Operation = kind,
            Path = "secret/data/x",
            Data = withData ? Payload() : null,
        };

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Sys.BatchAsync([operation])).ConfigureAwait(false);

        Assert.Equal(ErrorCodes.InputInvalidArgument, failure.Code);
        Assert.Equal(0, failure.Details["index"]);
    }

    [Theory]
    [Requirement("BAT-003")]
    [Trait("Requirement", "BAT-003")]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("v1/secret/data/x")]
    [InlineData("/v2/secret/data/x")]
    public async Task An_api_prefixed_or_empty_path_is_refused_rather_than_silently_rewritten(string path)
    {
        BastionVaultClient client = BuildClient(new FakeTransport());

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Sys.BatchAsync([new BatchOperation { Operation = BatchOperationKind.Read, Path = path }])).ConfigureAwait(false);

        Assert.Equal(ErrorCodes.InputInvalidArgument, failure.Code);
        Assert.Equal(path, failure.Details["path"]);
    }

    [Fact]
    [Requirement("BAT-003")]
    [Trait("Requirement", "BAT-003")]
    public async Task The_batch_prefix_refusal_is_stricter_than_every_other_operation_not_consistent_with_them()
    {
        // Pins the *true* reason BAT-003's refusal is worth having, because the reason first
        // recorded for it was false. No operation refuses an API-prefixed path client-side: the
        // executor appends `ApiVersion` and then the caller's path verbatim, so the same string
        // that `Sys.Batch` rejects before sending goes out doubled on `Logical.Read` and fails
        // at the server. Asserted rather than asserted-about, so the remark on
        // `SysOperations.NormaliseBatchPath` cannot drift back into a comfortable fiction.
        FakeTransport transport = new();
        transport.EnqueueResponse(404, body: Json("""{"errors":["no handler for route"]}"""));
        BastionVaultClient client = BuildClient(transport);

        _ = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Logical.ReadAsync("v1/secret/x")).ConfigureAwait(false);

        TransportRequest sent = Assert.Single(transport.Requests);
        Assert.Equal("https://vault.example.com:8200/v1/v1/secret/x", sent.Uri.ToString());
    }

    [Fact]
    [Requirement("BAT-003")]
    [Trait("Requirement", "BAT-003")]
    public async Task A_kind_outside_the_four_is_refused_rather_than_sent_as_a_number()
    {
        // Reachable only by casting an out-of-range value onto the enum, which C# permits. The
        // arm exists so such a value can never reach the wire as `"operation": "42"`.
        BastionVaultClient client = BuildClient(new FakeTransport());

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Sys.BatchAsync([new BatchOperation { Operation = (BatchOperationKind)42, Path = "secret/data/x" }])).ConfigureAwait(false);

        Assert.Equal(ErrorCodes.InputInvalidArgument, failure.Code);
        Assert.Equal("42", failure.Details["operation"]);
    }

    [Theory]
    [Requirement("BAT-001")]
    [Trait("Requirement", "BAT-001")]
    [InlineData(204, "")]
    [InlineData(200, """{"warnings":["nothing here"]}""")]
    public async Task A_batch_response_with_no_results_array_is_a_protocol_error(int status, string body)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(status, body: Json(body));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Sys.BatchAsync([Read("secret/data/x")])).ConfigureAwait(false);

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, failure.Code);
        Assert.Equal("results", failure.Details["expectedField"]);
    }

    [Fact]
    [Requirement("BAT-005")]
    [Requirement("BAT-008")]
    [Trait("Requirement", "BAT-008")]
    public async Task Every_operation_failing_still_succeeds_overall_and_no_member_implies_atomicity()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"results":[{"status":403,"path":"a","errors":["permission denied"]},{"status":500,"path":"b","warnings":["degraded"]}]}"""));
        BastionVaultClient client = BuildClient(transport);

        IReadOnlyList<BatchResult> results = await client.Sys.BatchAsync([Read("a"), Read("b")]).ConfigureAwait(false);

        Assert.Equal(ErrorCodes.AuthzPermissionDenied, results[0].Error!.Code);
        Assert.Equal(403, results[0].Error!.StatusCode);
        Assert.Equal(["degraded"], results[1].Warnings);
        Assert.NotNull(results[1].Error);

        // BAT-008: the word "Transaction" appears nowhere in the batch surface.
        Assert.DoesNotContain(
            typeof(SysOperations).GetMethods().Select(method => method.Name),
            name => name.Contains("Transaction", StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------ Kv.ReadMany

    [Fact]
    [Requirement("KV-010")]
    [Trait("Requirement", "KV-010")]
    public async Task Duplicate_paths_are_refused_because_the_map_could_not_answer_both()
    {
        BastionVaultClient client = BuildClient(new FakeTransport());

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Kv.ReadManyAsync("secret", ["a", "b", "a"])).ConfigureAwait(false);

        Assert.Equal(ErrorCodes.InputInvalidArgument, failure.Code);
    }

    [Fact]
    [Requirement("BAT-007")]
    [Trait("Requirement", "BAT-007")]
    public async Task A_result_count_that_does_not_match_the_request_is_a_protocol_error()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"results":[{"status":200,"path":"secret/data/a","data":{"data":{"k":"v"}}}]}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Kv.ReadManyAsync("secret", ["a", "b"])).ConfigureAwait(false);

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, failure.Code);
    }

    [Fact]
    [Requirement("BAT-007")]
    [Trait("Requirement", "BAT-007")]
    public async Task An_unwrappable_payload_fails_only_its_own_path()
    {
        // One malformed operation must not take the whole map down with it — that is the
        // difference between a per-operation result and a request-scoped error.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"results":[{"status":200,"path":"secret/data/a","data":"not-an-object"},{"status":200,"path":"secret/data/b","data":{"data":null,"metadata":{"version":2,"created_time":"2026-09-13T09:00:00Z","deletion_time":"2026-09-13T11:00:00Z","destroyed":false}}}]}"""));
        BastionVaultClient client = BuildClient(transport);

        IReadOnlyDictionary<string, KvReadManyEntry> map =
            await client.Kv.ReadManyAsync("secret", ["a", "b"]).ConfigureAwait(false);

        Assert.False(map["a"].IsSuccess);
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, map["a"].Error!.Code);

        // KV2-004's soft-deleted version is a successful read with no data, in the batch route
        // exactly as in a standalone one.
        Assert.True(map["b"].IsSuccess);
        Assert.Null(map["b"].Data);
        Assert.Equal(KvV2SecretState.SoftDeleted, map["b"].State);
        Assert.Equal(2, map["b"].Metadata!.Version);

        // KV2-004's rule is "no data *and* a deletion time", both limbs: a version with no data
        // and no deletion time is Live (an empty write), not soft-deleted.
        transport.EnqueueResponse(200, body: Json(
            """{"results":[{"status":200,"path":"secret/data/c","data":{"data":null,"metadata":{"version":3,"created_time":"2026-09-13T09:00:00Z","deletion_time":"","destroyed":false}}}]}"""));
        IReadOnlyDictionary<string, KvReadManyEntry> live =
            await client.Kv.ReadManyAsync("secret", ["c"]).ConfigureAwait(false);
        Assert.Null(live["c"].Data);
        Assert.Equal(KvV2SecretState.Live, live["c"].State);

        // And with no metadata at all there is nothing to derive a deletion from, so the state
        // is Live rather than a null dereference.
        transport.EnqueueResponse(200, body: Json(
            """{"results":[{"status":204,"path":"secret/data/d","data":{"version":1}}]}"""));
        IReadOnlyDictionary<string, KvReadManyEntry> bare =
            await client.Kv.ReadManyAsync("secret", ["d"]).ConfigureAwait(false);
        Assert.Null(bare["d"].Data);
        Assert.Null(bare["d"].Metadata);
        Assert.Equal(KvV2SecretState.Live, bare["d"].State);
    }

    [Fact]
    [Requirement("BAT-006")]
    [Requirement("BAT-007")]
    [Trait("Requirement", "BAT-006")]
    public async Task Only_an_unsupported_server_triggers_the_fallback()
    {
        // BAT-006's other two arms are the caller's to see. A 403 on sys/batch means the token
        // lacks the capability; retrying it as N reads would earn N more 403s and hide the cause.
        FakeTransport transport = new();
        transport.EnqueueResponse(403, body: Json("""{"errors":["permission denied"]}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Kv.ReadManyAsync("secret", ["a"])).ConfigureAwait(false);

        Assert.Equal(ErrorCodes.AuthzPermissionDenied, failure.Code);
        _ = Assert.Single(transport.Requests);
    }

    [Fact]
    [Requirement("BAT-007")]
    [Trait("Requirement", "BAT-007")]
    public async Task A_minimised_batch_payload_yields_the_data_with_the_metadata_reported_absent()
    {
        // The batch route minimises: `kv.read-many-batch`, captured under FIX-010, carries a
        // `metadata` with only `version`, and a server may omit the object entirely. Neither is
        // a protocol error and neither may be filled in with an invented `created_time`
        // (D-M1c-25) — a KvV2Secret would have forced one or the other.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"results":[{"status":200,"path":"secret/data/a","data":{"data":{"k":"v"}}},{"status":200,"path":"secret/data/b","data":{"data":{"k":"w"},"metadata":{"version":9}}}]}"""));
        BastionVaultClient client = BuildClient(transport);

        IReadOnlyDictionary<string, KvReadManyEntry> map =
            await client.Kv.ReadManyAsync("secret", ["a", "b"]).ConfigureAwait(false);

        foreach (string path in (string[])["a", "b"])
        {
            Assert.True(map[path].IsSuccess);
            Assert.Null(map[path].Metadata);
            Assert.Equal(KvV2SecretState.Live, map[path].State);
        }

        Assert.Equal("v", map["a"].Data!["k"].GetString());
        Assert.Equal("w", map["b"].Data!["k"].GetString());
    }

    [Fact]
    [Requirement("BAT-005")]
    [Trait("Requirement", "BAT-005")]
    public async Task A_result_item_missing_its_status_and_path_is_read_as_a_zero_status_success_rather_than_throwing()
    {
        // The wire shape is the server's, and BAT-005 gives the SDK no licence to reject a
        // result it did not expect: an item with neither `status` nor `path` yields
        // Status 0 / Path "" — below 400, so no mapped error — and the caller sees the anomaly
        // instead of an exception that loses the other results with it.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"results":[{},{"status":"200","path":7}]}"""));
        BastionVaultClient client = BuildClient(transport);

        IReadOnlyList<BatchResult> results = await client.Sys.BatchAsync([Read("a"), Read("b")]).ConfigureAwait(false);

        Assert.All(results, result =>
        {
            Assert.Equal(0, result.Status);
            Assert.Equal(string.Empty, result.Path);
            Assert.Null(result.Data);
            Assert.Null(result.Error);
            Assert.Empty(result.Errors);
        });
    }

    [Fact]
    [Requirement("BAT-004")]
    [Trait("Requirement", "BAT-004")]
    public async Task A_null_operation_or_a_null_path_is_refused_before_anything_is_sent()
    {
        BastionVaultClient client = BuildClient(new FakeTransport());

        BastionVaultException nullOperation = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Sys.BatchAsync([null!])).ConfigureAwait(false);
        Assert.Equal(ErrorCodes.InputInvalidArgument, nullOperation.Code);
        Assert.Equal(0, nullOperation.Details["index"]);

        BastionVaultException nullPath = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Sys.BatchAsync([new BatchOperation { Operation = BatchOperationKind.Read, Path = null! }])).ConfigureAwait(false);
        Assert.Equal(ErrorCodes.InputInvalidArgument, nullPath.Code);
        Assert.Equal(string.Empty, nullPath.Details["path"]);
    }

    [Fact]
    [Requirement("BAT-007")]
    [Trait("Requirement", "BAT-007")]
    public async Task A_successful_read_operation_that_carried_no_payload_fails_its_own_path()
    {
        // A read that comes back below 400 with no `data` is not a secret, and unwrapping it as
        // one would hand the caller an entry claiming success with nothing in it.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"results":[{"status":204,"path":"secret/data/a","data":null}]}"""));
        BastionVaultClient client = BuildClient(transport);

        IReadOnlyDictionary<string, KvReadManyEntry> map =
            await client.Kv.ReadManyAsync("secret", ["a"]).ConfigureAwait(false);

        Assert.False(map["a"].IsSuccess);
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, map["a"].Error!.Code);
        Assert.Equal("data", map["a"].Error!.Details["expectedField"]);
    }

    // ------------------------------------------------------------------ pagination (PAG-*)

    [Theory]
    [Requirement("PAG-001")]
    [Trait("Requirement", "PAG-001")]
    [InlineData(0)]
    [InlineData(501)]
    public async Task Sys_ListNamespacesInfo_rejects_a_limit_outside_1_500_before_sending(int limit)
    {
        BastionVaultClient client = BuildClient(new FakeTransport());

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Sys.ListNamespacesInfoAsync(limit: limit)).ConfigureAwait(false);

        Assert.Equal(ErrorCodes.InputOutOfRange, failure.Code);
        Assert.Equal(0, failure.Attempts);
        Assert.Equal(limit, failure.Details["limit"]);
    }

    [Theory]
    [Requirement("PAG-001")]
    [Trait("Requirement", "PAG-001")]
    [InlineData(0)]
    [InlineData(501)]
    public async Task Auth_Userpass_ListUsersInfo_rejects_a_limit_outside_1_500_before_sending(int limit)
    {
        BastionVaultClient client = BuildClient(new FakeTransport());

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.Userpass.ListUsersInfoAsync(limit: limit)).ConfigureAwait(false);

        Assert.Equal(ErrorCodes.InputOutOfRange, failure.Code);
        Assert.Equal(0, failure.Attempts);
    }

    [Fact]
    [Requirement("PAG-001")]
    [Trait("Requirement", "PAG-001")]
    public async Task ListNamespacesInfo_defaults_the_limit_to_100_when_omitted()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"keys":[],"records":[],"total":0,"next":"","truncated":false}"""));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Sys.ListNamespacesInfoAsync().ConfigureAwait(false);

        Assert.EndsWith("sys/namespaces-info?limit=100", transport.Requests[0].Uri.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("PAG-005")]
    [Trait("Requirement", "PAG-005")]
    public async Task ListNamespacesInfo_fails_with_the_protocol_error_when_keys_and_records_disagree_in_length()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"keys":["a","b"],"records":[{"path":"a"}],"total":2,"next":"","truncated":false}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Sys.ListNamespacesInfoAsync()).ConfigureAwait(false);

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, failure.Code);
        Assert.Equal("records", failure.Details["expectedField"]);
    }

    [Fact]
    [Requirement("PAG-005")]
    [Trait("Requirement", "PAG-005")]
    public async Task ListUsersInfo_zips_keys_and_records_and_fails_the_protocol_error_on_a_length_mismatch()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"keys":["alice","bob"],"records":[{"username":"alice","fido2_enabled":true},{"username":"bob"}],"total":2,"next":"","truncated":false}"""));
        BastionVaultClient client = BuildClient(transport);

        Page<UserSummary> page = await client.Auth.Userpass.ListUsersInfoAsync().ConfigureAwait(false);

        Assert.Equal(["alice", "bob"], page.Keys);
        Assert.Equal("alice", page.Entries[0].Value.Username);
        Assert.True(page.Entries[0].Value.Fido2Enabled);
        Assert.Equal("bob", page.Entries[1].Value.Username);
        Assert.False(page.Entries[1].Value.Fido2Enabled);
        Assert.Equal("https://vault.example.com:8200/v2/auth/userpass/users-info?limit=100", transport.Requests[0].Uri.ToString());

        transport.EnqueueResponse(200, body: Json(
            """{"keys":["alice","bob"],"records":[{"username":"alice"}],"total":2,"next":"","truncated":false}"""));
        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.Userpass.ListUsersInfoAsync()).ConfigureAwait(false);
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, failure.Code);

        // `after` given (rather than omitted), `total` absent (keys.Count is the fallback), and a
        // `records` element that is not an object — each a branch the calls above never take.
        transport.EnqueueResponse(200, body: Json(
            """{"keys":["carol"],"records":[{"username":"carol"}],"next":"","truncated":false}"""));
        Page<UserSummary> afterPage = await client.Auth.Userpass.ListUsersInfoAsync(after: "bob").ConfigureAwait(false);
        Assert.Equal(1, afterPage.Total);
        Assert.EndsWith("after=bob&limit=100", transport.Requests[^1].Uri.ToString(), StringComparison.Ordinal);

        transport.EnqueueResponse(200, body: Json("""{"keys":["carol"],"records":["not-an-object"],"total":1}"""));
        BastionVaultException notAnObject = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.Userpass.ListUsersInfoAsync()).ConfigureAwait(false);
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, notAnObject.Code);

        // No `records` key at all, and `records` present but not an array: zero keys is the only
        // shape that does not then fail the length check, so both are the "absent/non-array"
        // branch's only honest cases.
        transport.EnqueueResponse(200, body: Json("""{"keys":[]}"""));
        Page<UserSummary> empty = await client.Auth.Userpass.ListUsersInfoAsync().ConfigureAwait(false);
        Assert.Empty(empty.Keys);
        Assert.Equal(0, empty.Total);
        Assert.Null(empty.Next);
        Assert.False(empty.Truncated);

        transport.EnqueueResponse(200, body: Json("""{"keys":[],"records":"not-an-array"}"""));
        Page<UserSummary> emptyNonArray = await client.Auth.Userpass.ListUsersInfoAsync().ConfigureAwait(false);
        Assert.Empty(emptyNonArray.Records);

        // An explicit `fido2_enabled: false`, distinct from the field being absent (`bob`, above).
        transport.EnqueueResponse(200, body: Json(
            """{"keys":["dave"],"records":[{"username":"dave","fido2_enabled":false}],"total":1,"truncated":false}"""));
        Page<UserSummary> daveOnly = await client.Auth.Userpass.ListUsersInfoAsync().ConfigureAwait(false);
        Assert.False(daveOnly.Entries[0].Value.Fido2Enabled);

        // A record with no `username` at all: falls back to the key it is zipped with (`erin`),
        // the other half of `ReadString(...) ?? fallback`.
        transport.EnqueueResponse(200, body: Json(
            """{"keys":["erin"],"records":[{"fido2_enabled":true}],"total":1,"truncated":false}"""));
        Page<UserSummary> fallbackUsername = await client.Auth.Userpass.ListUsersInfoAsync().ConfigureAwait(false);
        Assert.Equal("erin", fallbackUsername.Entries[0].Value.Username);

        // `records` longer than `keys` mid-loop: the second record's index (1) is past the end of
        // a one-element `keys`, so its fallback is the empty string, before the length check below
        // it fails the call overall.
        transport.EnqueueResponse(200, body: Json(
            """{"keys":["frank"],"records":[{"username":"frank"},{"username":"ghost"}],"total":1}"""));
        BastionVaultException tooManyRecords = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.Userpass.ListUsersInfoAsync()).ConfigureAwait(false);
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, tooManyRecords.Code);

        // A 204 (no body at all): `response` itself is null, the other half of the
        // `response?.Data ?? throw` at the top of the method.
        transport.EnqueueResponse(204);
        BastionVaultException noResponse = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.Userpass.ListUsersInfoAsync()).ConfigureAwait(false);
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, noResponse.Code);
        Assert.Equal("keys", noResponse.Details["expectedField"]);
    }

    [Fact]
    [Requirement("PAG-004")]
    [Trait("Requirement", "PAG-004")]
    public async Task The_Userpass_iterator_shares_the_same_paging_machinery_and_walks_every_page()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"keys":["alice"],"records":[{"username":"alice"}],"total":2,"next":"alice","truncated":true}"""));
        transport.EnqueueResponse(200, body: Json(
            """{"keys":["bob"],"records":[{"username":"bob"}],"total":2,"next":"","truncated":false}"""));
        BastionVaultClient client = BuildClient(transport);

        List<string> usernames = [];
        await foreach (KeyValuePair<string, UserSummary> entry in client.Auth.Userpass.ListUsersInfoAllAsync())
        {
            usernames.Add(entry.Value.Username);
        }

        Assert.Equal(["alice", "bob"], usernames);
        Assert.EndsWith("after=alice&limit=100", transport.Requests[1].Uri.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("CCH-003")]
    [Trait("Requirement", "CCH-003")]
    public async Task CacheVersion_raises_the_watch_timeout_when_no_options_are_supplied_at_all()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"version":1,"topics":{},"coarse":false}"""));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Sys.CacheVersionAsync(["pki/"], watch: true).ConfigureAwait(false);

        Assert.True(transport.Requests[0].Timeout >= TimeSpan.FromSeconds(40));
    }

    [Fact]
    [Requirement("PAG-003")]
    [Requirement("PAG-007")]
    [Trait("Requirement", "PAG-007")]
    public async Task A_cursor_past_the_end_is_an_empty_non_truncated_page_not_an_error()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"keys":[],"records":[],"total":2,"next":"","truncated":false}"""));
        BastionVaultClient client = BuildClient(transport);

        Page<Namespace> page = await client.Sys.ListNamespacesInfoAsync(after: "past-the-end").ConfigureAwait(false);

        Assert.Empty(page.Keys);
        Assert.Empty(page.Records);
        Assert.False(page.Truncated);
        Assert.Null(page.Next);
    }

    [Fact]
    [Requirement("PAG-006")]
    [Trait("Requirement", "PAG-006")]
    public async Task ListNamespacesInfo_and_ListUsersInfo_surface_BV_SERVER_004_unchanged_on_an_unsupported_server()
    {
        FakeTransport namespaces = new();
        namespaces.EnqueueResponse(500, body: Json("""{"error":"Logical backend path not supported."}"""));
        BastionVaultException namespacesFailure = await Assert.ThrowsAsync<BastionVaultException>(
            () => BuildClient(namespaces).Sys.ListNamespacesInfoAsync()).ConfigureAwait(false);
        Assert.Equal(ErrorCodes.ServerUnsupportedByServer, namespacesFailure.Code);

        FakeTransport users = new();
        users.EnqueueResponse(500, body: Json("""{"error":"Logical backend path not supported."}"""));
        BastionVaultException usersFailure = await Assert.ThrowsAsync<BastionVaultException>(
            () => BuildClient(users).Auth.Userpass.ListUsersInfoAsync()).ConfigureAwait(false);
        Assert.Equal(ErrorCodes.ServerUnsupportedByServer, usersFailure.Code);
    }

    [Fact]
    [Requirement("PAG-002")]
    [Requirement("PAG-004")]
    [Requirement("PAG-007")]
    [Trait("Requirement", "PAG-004")]
    public async Task The_iterator_passes_next_verbatim_stops_on_a_non_truncated_page_and_honours_the_rate_gate()
    {
        // A cursor that is not an offset: if the iterator computed the next `after` instead of
        // passing the page's own `Next` through, this value could never come back on the wire.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"keys":["a"],"records":[{"path":"a"}],"total":2,"next":"not-an-offset","truncated":true}"""));
        transport.EnqueueResponse(200, body: Json(
            """{"keys":["b"],"records":[{"path":"b"}],"total":2,"next":"","truncated":false}"""));
        // Burst 1 forces the second page's fetch to wait one interval — PAG-004's "honouring the
        // rate gate" is only observable with more than one outstanding request.
        VirtualClock clock = new();
        BastionVaultClient client = BuildClient(transport, options =>
        {
            options.RateGate = new RateGate { RatePerSecond = 1, Burst = 1 };
            options.Clock = clock;
        });

        List<string> keys = [];
        await foreach (KeyValuePair<string, Namespace> entry in client.Sys.ListNamespacesInfoAllAsync())
        {
            keys.Add(entry.Key);
        }

        Assert.Equal(["a", "b"], keys);
        Assert.EndsWith("after=not-an-offset&limit=100", transport.Requests[1].Uri.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("PAG-004")]
    [Trait("Requirement", "PAG-004")]
    public async Task The_iterator_can_be_abandoned_mid_walk_without_fetching_a_page_it_will_never_consume()
    {
        // A caller that stops early (`break`) disposes the enumerator between pages rather than
        // running it to the natural `Truncated == false` exit — a different resumption path
        // through the same state machine than the two tests above, which both run to completion.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"keys":["a"],"records":[{"path":"a"}],"total":3,"next":"cursor-1","truncated":true}"""));
        transport.EnqueueResponse(200, body: Json(
            """{"keys":["b"],"records":[{"path":"b"}],"total":3,"next":"cursor-2","truncated":true}"""));
        BastionVaultClient client = BuildClient(transport);

        List<string> keys = [];
        await foreach (KeyValuePair<string, Namespace> entry in client.Sys.ListNamespacesInfoAllAsync())
        {
            keys.Add(entry.Key);
            if (keys.Count == 2)
            {
                break;
            }
        }

        Assert.Equal(["a", "b"], keys);
        // The third page was never asked for: only two exchanges were ever scripted, and both were
        // consumed, so a third fetch would have thrown "no scripted response left" instead.
        Assert.Equal(2, transport.Requests.Count);
    }

    [Fact]
    [Requirement("PAG-004")]
    [Trait("Requirement", "PAG-004")]
    public async Task IteratePagesAsync_refuses_a_null_fetchPage_delegate()
    {
        _ = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (KeyValuePair<string, int> _ in PagingWire.IteratePagesAsync<int>(null!, 10))
            {
            }
        }).ConfigureAwait(false);
    }

    [Fact]
    [Requirement("PAG-004")]
    [Trait("Requirement", "PAG-004")]
    public async Task The_iterator_stops_at_MaxRecords_with_BV_INPUT_005_naming_the_total_rather_than_paging_without_bound()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"keys":["a","b","c"],"records":[{"path":"a"},{"path":"b"},{"path":"c"}],"total":100,"next":"x","truncated":true}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(async () =>
        {
            await foreach (KeyValuePair<string, Namespace> _ in client.Sys.ListNamespacesInfoAllAsync(maxRecords: 2))
            {
            }
        }).ConfigureAwait(false);

        Assert.Equal(ErrorCodes.InputIterationCapExceeded, failure.Code);
        Assert.Equal(100, failure.Details["total"]);
        Assert.Equal(2, failure.Details["maxRecords"]);
    }

    [Fact]
    [Requirement("PAG-004")]
    [Trait("Requirement", "PAG-004")]
    public async Task The_iterator_is_bounded_even_when_no_page_ever_yields_a_record()
    {
        // The record cap cannot bound this walk. A server answering
        // `{"keys":[],"records":[],"truncated":true}` never increments the yielded count, so the
        // cap inside the foreach is unreachable, and a null `Next` restarts the cursor at page
        // one. Every turn is a real rate-gated request, so before the fetch bound this looped
        // for ever against the server rather than spinning the CPU.
        int fetches = 0;
        Page<int> emptyButTruncated = new()
        {
            Keys = [],
            Records = [],
            Total = 7,
            Next = null,
            Truncated = true,
        };

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(async () =>
        {
            await foreach (KeyValuePair<string, int> _ in PagingWire.IteratePagesAsync<int>(
                (_, _) =>
                {
                    fetches++;
                    return Task.FromResult(emptyButTruncated);
                },
                maxRecords: 3))
            {
            }
        }).ConfigureAwait(false);

        Assert.Equal(ErrorCodes.InputIterationCapExceeded, failure.Code);
        Assert.Equal(7, failure.Details["total"]);

        // maxRecords + 1 fetches are permitted, because a walk yielding one record per page needs
        // exactly that many to reach the cap legitimately. The next fetch trips the bound.
        Assert.Equal(5, fetches);
    }

    [Fact]
    [Requirement("PAG-004")]
    [Trait("Requirement", "PAG-004")]
    public async Task The_iterator_propagates_cancellation_raised_while_fetching_a_later_page()
    {
        // Cancelled between the first and second page fetch: the exception comes out of the
        // `await fetchPage(after, cancellationToken)` call itself on a loop iteration reached by
        // looping at least once, not out of a `yield break` or the cap check.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"keys":["a"],"records":[{"path":"a"}],"total":2,"next":"x","truncated":true}"""));
        BastionVaultClient client = BuildClient(transport);
        using CancellationTokenSource cts = new();

        _ = await Assert.ThrowsAsync<BastionVaultException>(async () =>
        {
            await foreach (KeyValuePair<string, Namespace> entry in client.Sys.ListNamespacesInfoAllAsync(cancellationToken: cts.Token))
            {
                cts.Cancel();
            }
        }).ConfigureAwait(false);
    }

    [Fact]
    [Requirement("PAG-004")]
    [Trait("Requirement", "PAG-004")]
    public async Task The_iterator_stops_at_MaxRecords_on_a_later_page_reached_by_looping_at_least_once()
    {
        // Distinct from the test above: the cap is exceeded only after the walk has already
        // looped back for a second page (`after = page.Next` on the first page), so this is the
        // "exit via exception" path reached with the loop already once around, not on its first
        // pass.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"keys":["a"],"records":[{"path":"a"}],"total":100,"next":"x","truncated":true}"""));
        transport.EnqueueResponse(200, body: Json(
            """{"keys":["b","c"],"records":[{"path":"b"},{"path":"c"}],"total":100,"next":"y","truncated":true}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(async () =>
        {
            await foreach (KeyValuePair<string, Namespace> _ in client.Sys.ListNamespacesInfoAllAsync(maxRecords: 2))
            {
            }
        }).ConfigureAwait(false);

        Assert.Equal(ErrorCodes.InputIterationCapExceeded, failure.Code);
        Assert.Equal(100, failure.Details["total"]);
        Assert.Equal(2, transport.Requests.Count);
    }

    // ------------------------------------------------------------------ cache coherence (CCH-*)

    [Fact]
    [Requirement("CCH-001")]
    [Trait("Requirement", "CCH-001")]
    public async Task CacheVersion_joins_topics_into_one_comma_separated_parameter_and_pins_v2()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"version":412,"topics":{"pki/":17},"coarse":false}"""));
        BastionVaultClient client = BuildClient(transport);

        CacheVersion result = await client.Sys.CacheVersionAsync(["pki/", "auth/userpass/"]).ConfigureAwait(false);

        Assert.False(result.NotModified);
        Assert.Equal(412, result.Version);
        Assert.Equal(17, result.Topics!["pki/"]);
        Assert.Equal("https://vault.example.com:8200/v2/sys/cache/version?topics=pki/,auth/userpass/", transport.Requests[0].Uri.ToString());
    }

    [Fact]
    [Requirement("CCH-001")]
    [Trait("Requirement", "CCH-001")]
    public async Task CacheVersion_rejects_more_than_64_topics_before_sending()
    {
        BastionVaultClient client = BuildClient(new FakeTransport());
        string[] topics = [.. Enumerable.Range(0, 65).Select(index => $"t{index}/")];

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Sys.CacheVersionAsync(topics)).ConfigureAwait(false);

        Assert.Equal(ErrorCodes.InputOutOfRange, failure.Code);
        Assert.Equal(0, failure.Attempts);
    }

    [Fact]
    [Requirement("CCH-002")]
    [Trait("Requirement", "CCH-002")]
    public async Task CacheVersion_sends_If_None_Match_and_maps_304_to_NotModified_rather_than_an_error()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(304, headers: new Dictionary<string, string> { ["ETag"] = "\"412\"" });
        BastionVaultClient client = BuildClient(transport);

        CacheVersion result = await client.Sys.CacheVersionAsync(["pki/"], ifNoneMatch: "\"412\"").ConfigureAwait(false);

        Assert.True(result.NotModified);
        Assert.Null(result.Version);
        Assert.Null(result.Topics);
        Assert.Equal("\"412\"", result.ETag);
        Assert.Equal("\"412\"", transport.Requests[0].Headers["If-None-Match"]);

        // A 304 with no ETag header at all: the fallback to the caller's own ifNoneMatch, the
        // other half of that ternary.
        transport.EnqueueResponse(304);
        CacheVersion noEtagHeader = await client.Sys.CacheVersionAsync(["pki/"], ifNoneMatch: "\"412\"").ConfigureAwait(false);
        Assert.Equal("\"412\"", noEtagHeader.ETag);
    }

    [Fact]
    [Requirement("CCH-002")]
    [Trait("Requirement", "CCH-002")]
    public async Task CacheVersion_merges_If_None_Match_into_a_callers_own_existing_headers_rather_than_replacing_them()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"version":1,"topics":{},"coarse":false}"""));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Sys.CacheVersionAsync(
            ["pki/"],
            ifNoneMatch: "\"412\"",
            options: new RequestOptions { Headers = new Dictionary<string, string> { ["X-Caller"] = "kept" } })
            .ConfigureAwait(false);

        Assert.Equal("kept", transport.Requests[0].Headers["X-Caller"]);
        Assert.Equal("\"412\"", transport.Requests[0].Headers["If-None-Match"]);
    }

    [Fact]
    [Requirement("CCH-003")]
    [Trait("Requirement", "CCH-003")]
    public async Task CacheVersion_raises_the_watch_timeout_to_at_least_40s_regardless_of_a_shorter_caller_timeout()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"version":1,"topics":{},"coarse":false}"""));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Sys.CacheVersionAsync(["pki/"], watch: true, options: new RequestOptions { Timeout = TimeSpan.FromSeconds(5) })
            .ConfigureAwait(false);

        Assert.True(transport.Requests[0].Timeout >= TimeSpan.FromSeconds(40));
        Assert.Contains("watch=1", transport.Requests[0].Uri.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("CCH-003")]
    [Trait("Requirement", "CCH-003")]
    public async Task CacheVersion_keeps_a_caller_timeout_that_already_exceeds_the_watch_floor()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"version":1,"topics":{},"coarse":false}"""));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Sys.CacheVersionAsync(["pki/"], watch: true, options: new RequestOptions { Timeout = TimeSpan.FromSeconds(90) })
            .ConfigureAwait(false);

        Assert.Equal(TimeSpan.FromSeconds(90), transport.Requests[0].Timeout);
    }

    [Fact]
    [Requirement("CCH-003")]
    [Trait("Requirement", "CCH-003")]
    public async Task CacheVersion_does_not_touch_the_timeout_when_watch_is_false()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"version":1,"topics":{},"coarse":false}"""));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Sys.CacheVersionAsync(["pki/"]).ConfigureAwait(false);

        Assert.Equal(TimeSpan.FromSeconds(30), transport.Requests[0].Timeout);
    }

    [Fact]
    [Requirement("CCH-005")]
    [Trait("Requirement", "CCH-005")]
    public async Task CacheVersion_never_synthesises_a_zero_for_a_topic_the_server_omitted()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"version":412,"topics":{"pki/":17},"coarse":false}"""));
        BastionVaultClient client = BuildClient(transport);

        CacheVersion result = await client.Sys.CacheVersionAsync(["pki/", "auth/userpass/"]).ConfigureAwait(false);

        Assert.True(result.Topics!.ContainsKey("pki/"));
        Assert.False(result.Topics!.ContainsKey("auth/userpass/"));
    }

    [Fact]
    [Requirement("CCH-004")]
    [Trait("Requirement", "CCH-004")]
    public async Task CacheVersion_returns_a_decreased_epoch_verbatim_rather_than_treating_it_as_a_change_signal()
    {
        // CCH-004: only an *increase* is a real change signal, because epochs are per node and
        // reset on restart. The SDK's job is to report exactly what the wire sent — never to
        // clamp, hide, or otherwise interpret a decrease on the caller's behalf; the decision
        // belongs to whoever compares two `CacheVersion.Topics` snapshots, not to this call.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"version":412,"topics":{"pki/":17},"coarse":false}"""));
        BastionVaultClient client = BuildClient(transport);
        CacheVersion first = await client.Sys.CacheVersionAsync(["pki/"]).ConfigureAwait(false);

        // A restarted node's epoch for the same topic, lower than what was seen before.
        transport.EnqueueResponse(200, body: Json("""{"version":413,"topics":{"pki/":3},"coarse":false}"""));
        CacheVersion second = await client.Sys.CacheVersionAsync(["pki/"]).ConfigureAwait(false);

        Assert.Equal(17, first.Topics!["pki/"]);
        Assert.Equal(3, second.Topics!["pki/"]);
        Assert.True(second.Topics!["pki/"] < first.Topics!["pki/"]);
    }

    [Fact]
    [Requirement("CCH-005")]
    [Trait("Requirement", "CCH-005")]
    public async Task CacheVersion_ignores_a_non_number_topic_entry_and_skips_a_missing_topics_object()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"version":1,"topics":{"pki/":17,"weird/":"not-a-number"},"coarse":false}"""));
        BastionVaultClient client = BuildClient(transport);

        CacheVersion withWeirdEntry = await client.Sys.CacheVersionAsync(["pki/", "weird/"]).ConfigureAwait(false);
        Assert.Equal(17, withWeirdEntry.Topics!["pki/"]);
        Assert.False(withWeirdEntry.Topics!.ContainsKey("weird/"));

        transport.EnqueueResponse(200, body: Json("""{"version":1,"coarse":false}"""));
        CacheVersion withNoTopics = await client.Sys.CacheVersionAsync(["pki/"]).ConfigureAwait(false);
        Assert.Empty(withNoTopics.Topics!);
    }

    [Fact]
    [Requirement("CCH-002")]
    [Trait("Requirement", "CCH-002")]
    public async Task CacheVersion_treats_a_204_as_the_protocol_error_it_is_rather_than_a_304()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Sys.CacheVersionAsync(["pki/"])).ConfigureAwait(false);

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, failure.Code);
        Assert.Equal("version", failure.Details["expectedField"]);

        // A non-object 200 body: `response` is present but carries no `Data` at all, the other
        // half of the same `response?.Data ?? throw` this class's 204 case exercises.
        transport.EnqueueResponse(200, body: Json("[]"));
        BastionVaultException nonObjectBody = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Sys.CacheVersionAsync(["pki/"])).ConfigureAwait(false);
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, nonObjectBody.Code);
    }

    [Fact]
    [Requirement("CCH-001")]
    [Trait("Requirement", "CCH-001")]
    public async Task CacheVersion_carries_the_response_ETag_for_the_next_calls_ifNoneMatch()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, headers: new Dictionary<string, string> { ["ETag"] = "\"999\"" }, body: Json(
            """{"version":999,"topics":{},"coarse":true}"""));
        BastionVaultClient client = BuildClient(transport);

        CacheVersion result = await client.Sys.CacheVersionAsync(["pki/"]).ConfigureAwait(false);

        Assert.Equal("\"999\"", result.ETag);
        Assert.True(result.Coarse);
    }

    // ------------------------------------------------------------------ helpers

    private static BatchOperation Read(string path)
    {
        return new BatchOperation { Operation = BatchOperationKind.Read, Path = path };
    }

    private static JsonElement Payload()
    {
        using JsonDocument document = JsonDocument.Parse("""{"data":{"k":"v"}}""");
        return document.RootElement.Clone();
    }

    private static BastionVaultClient BuildClient(ITransport transport, Action<BastionVaultClientOptions>? configure = null)
    {
        BastionVaultClientOptions options = new()
        {
            Address = Address,
            Token = FakeTokens.Client,
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

    private static async Task Complete(Task acquisition, string name, List<string> completed)
    {
        await acquisition.ConfigureAwait(false);
        lock (completed)
        {
            completed.Add(name);
        }
    }

    /// <summary>Polls a condition the gate reaches on another continuation, with a bound so a regression fails rather than hangs.</summary>
    private static async Task WaitUntil(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 500 && !condition(); attempt++)
        {
            await Task.Delay(10).ConfigureAwait(false);
        }

        Assert.True(condition(), "the gate did not reach the expected state within 5 s.");
    }

    /// <summary>A clock whose <see cref="Delay"/> advances virtual time immediately and records what was asked for.</summary>
    private sealed class VirtualClock : IClock
    {
        private readonly List<TimeSpan> waits = [];
        private DateTimeOffset now = DateTimeOffset.UnixEpoch;

        public IReadOnlyList<TimeSpan> Waits => waits;

        public void Advance(TimeSpan duration)
        {
            now += duration;
        }

        public DateTimeOffset NowUtc()
        {
            return now;
        }

        public Task Delay(TimeSpan duration, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            waits.Add(duration);
            now += duration;
            return Task.CompletedTask;
        }
    }

    /// <summary>A clock whose <see cref="Delay"/> stays pending until the test releases it, so two waiters can be observed queued at once.</summary>
    private sealed class HeldClock : IClock
    {
        private readonly object gate = new();
        private readonly List<TimeSpan> waits = [];
        private readonly Queue<TaskCompletionSource> pending = new();

        public IReadOnlyList<TimeSpan> Waits
        {
            get
            {
                lock (gate)
                {
                    return waits.ToArray();
                }
            }
        }

        public int Outstanding
        {
            get
            {
                lock (gate)
                {
                    return pending.Count;
                }
            }
        }

        public DateTimeOffset NowUtc()
        {
            return DateTimeOffset.UnixEpoch;
        }

        public Task Delay(TimeSpan duration, CancellationToken cancellationToken)
        {
            TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (gate)
            {
                waits.Add(duration);
                pending.Enqueue(completion);
            }

            return completion.Task;
        }

        public void ReleaseNext(bool cancel = false)
        {
            TaskCompletionSource completion;
            lock (gate)
            {
                completion = pending.Dequeue();
            }

            if (cancel)
            {
                _ = completion.TrySetCanceled();
                return;
            }

            _ = completion.TrySetResult();
        }
    }

    /// <summary>DSC-014's seam with a fixed answer, so the probe path can be driven without DNS.</summary>
    private sealed class StaticSrvResolver : ISrvResolver
    {
        private readonly IReadOnlyList<SrvRecord> records;

        public StaticSrvResolver(IReadOnlyList<SrvRecord> records)
        {
            this.records = records;
        }

        public Task<IReadOnlyList<SrvRecord>> ResolveAsync(string ownerName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(records);
        }
    }
}
