using System.Text;
using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;
using BastionVault.IntegrationSdk.Testing;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// CCH-006's <see cref="CacheWatcher"/>, driven against the real <see cref="BastionVaultClient"/>
/// on a virtual clock — the same style <c>AutoRenewTests</c> uses for the equivalent AUT-090…AUT-094
/// loop, and for the same reason: a fixture drives one exchange, and this loop's behaviour only
/// shows up across several.
/// </summary>
public sealed class CacheWatcherTests
{
    private const string Address = "https://vault.example.com:8200";

    [Fact]
    [Requirement("CCH-006")]
    [Trait("Requirement", "CCH-006")]
    public async Task An_increase_raises_one_change_event_per_topic_and_a_first_observation_raises_none()
    {
        FakeTransport transport = new();
        Enqueue(transport, 200, """{"version":1,"topics":{"pki/":1,"auth/":5},"coarse":false}""");
        Enqueue(transport, 200, """{"version":2,"topics":{"pki/":2,"auth/":9},"coarse":false}""");
        BastionVaultClient client = BuildClient(transport, new VirtualClock());
        Changes changes = new();
        CacheWatcher watcher = new(client, ["pki/", "auth/"], changes.Attach(new CacheWatcherPolicy()));

        await RunToExhaustion(watcher).ConfigureAwait(false);

        Assert.Equal(2, changes.Changed.Count);
        Assert.Contains(changes.Changed, c => c.Topic == "pki/" && c.PreviousEpoch == 1 && c.CurrentEpoch == 2);
        Assert.Contains(changes.Changed, c => c.Topic == "auth/" && c.PreviousEpoch == 5 && c.CurrentEpoch == 9);
    }

    [Fact]
    [Requirement("CCH-002")]
    [Requirement("CCH-006")]
    [Trait("Requirement", "CCH-006")]
    public async Task A_304_raises_no_change_and_carries_the_ETag_into_the_next_If_None_Match()
    {
        FakeTransport transport = new();
        Enqueue(transport, 200, """{"version":1,"topics":{"pki/":1},"coarse":false}""", etag: "\"1\"");
        transport.EnqueueResponse(304, headers: new Dictionary<string, string> { ["ETag"] = "\"1\"" });
        BastionVaultClient client = BuildClient(transport, new VirtualClock());
        Changes changes = new();
        CacheWatcher watcher = new(client, ["pki/"], changes.Attach(new CacheWatcherPolicy()));

        await RunToExhaustion(watcher).ConfigureAwait(false);

        Assert.Empty(changes.Changed);
        // Two scripted exchanges, plus the third request that found the script exhausted
        // (FakeTransport records a request before it throws).
        Assert.Equal(3, transport.Requests.Count);
        Assert.Equal("\"1\"", transport.Requests[1].Headers["If-None-Match"]);
    }

    [Fact]
    [Requirement("CCH-004")]
    [Requirement("CCH-006")]
    [Trait("Requirement", "CCH-006")]
    public async Task A_decreased_epoch_raises_no_change_because_epochs_are_per_node_and_reset_on_restart()
    {
        FakeTransport transport = new();
        Enqueue(transport, 200, """{"version":1,"topics":{"pki/":17},"coarse":false}""");
        Enqueue(transport, 200, """{"version":2,"topics":{"pki/":3},"coarse":false}""");
        BastionVaultClient client = BuildClient(transport, new VirtualClock());
        Changes changes = new();
        CacheWatcher watcher = new(client, ["pki/"], changes.Attach(new CacheWatcherPolicy()));

        await RunToExhaustion(watcher).ConfigureAwait(false);

        Assert.Empty(changes.Changed);
    }

    [Fact]
    [Requirement("CCH-005")]
    [Requirement("CCH-006")]
    [Trait("Requirement", "CCH-006")]
    public async Task A_topic_that_disappears_and_reappears_at_the_same_epoch_raises_no_change()
    {
        FakeTransport transport = new();
        Enqueue(transport, 200, """{"version":1,"topics":{"pki/":17,"auth/":5},"coarse":false}""");
        // pki/ is absent (CCH-005: not authorised or unknown), never synthesised as 0.
        Enqueue(transport, 200, """{"version":2,"topics":{"auth/":5},"coarse":false}""");
        // pki/ reappears at the same epoch it left at — not an increase from a fabricated zero.
        Enqueue(transport, 200, """{"version":3,"topics":{"pki/":17,"auth/":5},"coarse":false}""");
        BastionVaultClient client = BuildClient(transport, new VirtualClock());
        Changes changes = new();
        CacheWatcher watcher = new(client, ["pki/", "auth/"], changes.Attach(new CacheWatcherPolicy()));

        await RunToExhaustion(watcher).ConfigureAwait(false);

        Assert.Empty(changes.Changed);
    }

    [Fact]
    [Requirement("CCH-006")]
    [Trait("Requirement", "CCH-006")]
    public async Task Transport_errors_back_off_exponentially_from_the_clients_retry_policy_and_then_recover()
    {
        FakeTransport transport = new();
        transport.EnqueueFailure(TransportFailureKind.ConnectionRefused);
        transport.EnqueueFailure(TransportFailureKind.ConnectionRefused);
        Enqueue(transport, 200, """{"version":1,"topics":{"pki/":1},"coarse":false}""");
        VirtualClock clock = new();
        BastionVaultClient client = BuildClient(transport, clock);
        Changes changes = new();
        CacheWatcher watcher = new(client, ["pki/"], changes.Attach(new CacheWatcherPolicy()));

        await RunToExhaustion(watcher).ConfigureAwait(false);

        // RetryPolicy defaults: 250ms initial, ×2 multiplier, no cap reached. FixedJitterSource
        // returns 0.5, which is the jitter factor's own midpoint (±20% of zero), so the values are
        // exact rather than merely bounded.
        Assert.Equal([TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500)], clock.Waits);
        Assert.Equal(2, changes.Failed.Count);
        Assert.All(changes.Failed, code => Assert.Equal(ErrorCodes.TransportConnectionFailed, code));
        // Recovery: the third request succeeded, so nothing stopped the loop before the transport
        // ran out of script on the fourth request.
        Assert.Null(changes.Stopped);
    }

    [Fact]
    [Requirement("CCH-006")]
    [Trait("Requirement", "CCH-006")]
    public async Task Consecutive_failures_exhaust_the_cap_and_stop_the_loop()
    {
        FakeTransport transport = new();
        transport.EnqueueFailure(TransportFailureKind.ConnectionRefused);
        transport.EnqueueFailure(TransportFailureKind.ConnectionRefused);
        transport.EnqueueFailure(TransportFailureKind.ConnectionRefused);
        VirtualClock clock = new();
        BastionVaultClient client = BuildClient(transport, clock);
        Changes changes = new();
        CacheWatcher watcher = new(client, ["pki/"], changes.Attach(new CacheWatcherPolicy { MaxConsecutiveFailures = 3 }));

        await watcher.RunAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.Equal(3, changes.Failed.Count);
        // No backoff wait follows the failure that hits the cap.
        Assert.Equal(2, clock.Waits.Count);
        Assert.Equal(CacheWatcherStoppedReason.FailureCapExceeded, changes.Stopped);
    }

    [Fact]
    [Requirement("CCH-006")]
    [Trait("Requirement", "CCH-006")]
    public async Task BV_AUTHZ_001_stops_the_loop_immediately_rather_than_after_the_cap()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(403, body: Json("""{"error":"permission denied"}"""));
        VirtualClock clock = new();
        BastionVaultClient client = BuildClient(transport, clock);
        Changes changes = new();
        CacheWatcher watcher = new(client, ["pki/"], changes.Attach(new CacheWatcherPolicy { MaxConsecutiveFailures = 5 }));

        await watcher.RunAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.Equal(CacheWatcherStoppedReason.PermissionDenied, changes.Stopped);
        Assert.Equal([ErrorCodes.AuthzPermissionDenied], changes.Failed);
        Assert.Empty(clock.Waits);
        Assert.Equal(1, transport.Requests.Count);
    }

    [Fact]
    [Requirement("CCH-006")]
    [Trait("Requirement", "CCH-006")]
    public async Task A_cancelled_token_stops_the_loop_and_reports_Cancelled()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport, new VirtualClock());
        Changes changes = new();
        CacheWatcher watcher = new(client, ["pki/"], changes.Attach(new CacheWatcherPolicy()));
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await watcher.RunAsync(cts.Token).ConfigureAwait(false);

        Assert.Equal(CacheWatcherStoppedReason.Cancelled, changes.Stopped);
    }

    [Fact]
    [Requirement("CCH-006")]
    [Trait("Requirement", "CCH-006")]
    public async Task Omitting_a_policy_defaults_to_one_whose_null_callbacks_are_safe_no_ops()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(403, body: Json("""{"error":"permission denied"}"""));
        BastionVaultClient client = BuildClient(transport, new VirtualClock());
        // No policy argument at all: the constructor's own default applies, and OnFailed and
        // OnStopped are both null on it — this must not throw.
        CacheWatcher watcher = new(client, ["pki/"]);

        await watcher.RunAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.Equal(1, transport.Requests.Count);
    }

    [Fact]
    [Requirement("CCH-006")]
    [Trait("Requirement", "CCH-006")]
    public async Task OnChanged_is_a_safe_no_op_when_the_caller_did_not_wire_it()
    {
        FakeTransport transport = new();
        Enqueue(transport, 200, """{"version":1,"topics":{"pki/":1},"coarse":false}""");
        Enqueue(transport, 200, """{"version":2,"topics":{"pki/":2},"coarse":false}""");
        BastionVaultClient client = BuildClient(transport, new VirtualClock());
        // An explicit policy with every callback left null: the second response is a real
        // increase, and processing it without an OnChanged wired must not throw.
        CacheWatcher watcher = new(client, ["pki/"], new CacheWatcherPolicy());

        await RunToExhaustion(watcher).ConfigureAwait(false);
    }

    [Fact]
    [Requirement("CCH-006")]
    [Trait("Requirement", "CCH-006")]
    public void The_constructor_rejects_a_null_client_or_a_null_topic_list()
    {
        BastionVaultClient client = BuildClient(new FakeTransport(), new VirtualClock());

        _ = Assert.Throws<ArgumentNullException>(() => new CacheWatcher(null!, ["pki/"]));
        _ = Assert.Throws<ArgumentNullException>(() => new CacheWatcher(client, null!));
    }

    private static void Enqueue(FakeTransport transport, int statusCode, string body, string? etag = null)
    {
        Dictionary<string, string>? headers = etag is null ? null : new Dictionary<string, string> { ["ETag"] = etag };
        transport.EnqueueResponse(statusCode, headers: headers, body: Json(body));
    }

    private static ReadOnlyMemory<byte> Json(string json)
    {
        return Encoding.UTF8.GetBytes(json);
    }

    private static async Task RunToExhaustion(CacheWatcher watcher)
    {
        // The transport is scripted with exactly as many exchanges as a test needs; the loop
        // asking for one more is FakeTransport's own signal that this test has seen everything it
        // scripted, without a second concurrency mechanism to bound an otherwise-infinite watch
        // loop.
        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => watcher.RunAsync(CancellationToken.None)).ConfigureAwait(false);
    }

    private static BastionVaultClient BuildClient(ITransport transport, IClock clock)
    {
        BastionVaultClientOptions options = new()
        {
            Address = Address,
            Token = FakeTokens.Client,
            Transport = transport,
            Clock = clock,
            JitterSource = new FixedJitterSource(),
            RateGate = new RateGate { RatePerSecond = 0 },
            RetryPolicy = new RetryPolicy { MaxAttempts = 1 },
        };
        return new BastionVaultClient(options, EnvironmentSource.None);
    }

    /// <summary>What the loop reported, collected through <see cref="CacheWatcherPolicy"/>'s three callbacks.</summary>
    private sealed class Changes
    {
        public List<CacheTopicChanged> Changed { get; } = [];

        public List<string> Failed { get; } = [];

        public CacheWatcherStoppedReason? Stopped { get; set; }

        public CacheWatcherPolicy Attach(CacheWatcherPolicy policy)
        {
            return policy with
            {
                OnChanged = policy.OnChanged ?? Changed.Add,
                OnFailed = policy.OnFailed ?? (failure => Failed.Add(failure.Code)),
                OnStopped = policy.OnStopped ?? (reason => Stopped = reason),
            };
        }
    }

    /// <summary>A midpoint jitter source: <c>±20%</c> of zero, so every backoff value is exact.</summary>
    private sealed class FixedJitterSource : IJitterSource
    {
        public double NextDouble()
        {
            return 0.5;
        }
    }

    /// <summary>A clock that makes time pass by being asked to wait, and records what it was asked for.</summary>
    private sealed class VirtualClock : IClock
    {
        private readonly List<TimeSpan> waits = [];
        private DateTimeOffset now = DateTimeOffset.Parse("2026-09-21T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        public IReadOnlyList<TimeSpan> Waits => waits;

        public DateTimeOffset NowUtc()
        {
            return now;
        }

        public Task Delay(TimeSpan duration, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            now += duration;
            waits.Add(duration);
            if (waits.Count > 64)
            {
                throw new InvalidOperationException("The watch loop asked for more than 64 waits; it is spinning.");
            }

            return Task.CompletedTask;
        }
    }
}
