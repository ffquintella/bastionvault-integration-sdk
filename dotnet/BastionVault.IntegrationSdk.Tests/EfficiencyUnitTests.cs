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
        Assert.Equal(burst, gate.Snapshot().AvailableTokens);
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
