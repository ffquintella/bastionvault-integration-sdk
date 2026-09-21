using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// CCH-006's optional helper: loops <c>Sys.CacheVersionAsync(..., watch: true)</c> and raises one
/// <see cref="CacheWatcherPolicy.OnChanged"/> per topic whose epoch increased since the last
/// observation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Shape, following M2c's <see cref="TokenRenewal"/> precedent exactly</b> (the same problem: a
/// long-running loop over a client, with callbacks and a stop reason). No <see cref="IDisposable"/>,
/// no self-started <c>Task.Run</c> — <see cref="RunAsync"/> is awaitable and the caller owns the
/// task; cancelling the token passed in is the stop mechanism.
/// </para>
/// <para>
/// <b>CCH-004: only an increase is a change signal.</b> Epochs are per node and reset on restart,
/// so a decrease is never itself reported — but it <i>does</i> update the recorded baseline, to
/// exactly the value observed. A subsequent rise above that lower baseline (the node having
/// restarted and moved on) is a real change and is reported: epochs <c>5 → 2 → 3</c> raise no event
/// on the first decrease and one event (<c>2 → 3</c>) on the second observation, because
/// suppressing that rise would lose the invalidation CCH-004 exists to deliver. A topic's first
/// observation establishes its baseline silently.
/// </para>
/// <para>
/// <b>CCH-005: a topic absent from a response is "not authorised or unknown."</b> This loop never
/// synthesises a <c>0</c> for a missing topic and never touches its recorded baseline while it is
/// missing, so a later reappearance at the same epoch is correctly seen as no change — not as an
/// increase from a fabricated zero.
/// </para>
/// <para>
/// <b>Backoff is the client's own curve</b> (<see cref="BackoffCalculator"/>, shared with
/// <see cref="RequestExecutor"/>'s transport-level retry): CCH-006 names no curve of its own, and a
/// second implementation that could disagree with the configured <see cref="RetryPolicy"/> is the
/// defect avoided by promoting one.
/// </para>
/// </remarks>
public sealed class CacheWatcher
{
    private readonly BastionVaultClient client;
    private readonly IReadOnlyList<string> topics;
    private readonly CacheWatcherPolicy policy;

    /// <summary>Constructs a watcher over an existing client's <see cref="BastionVaultClient.Sys"/> surface.</summary>
    public CacheWatcher(BastionVaultClient client, IReadOnlyList<string> topics, CacheWatcherPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(topics);
        this.client = client;
        this.topics = topics;
        this.policy = policy ?? new CacheWatcherPolicy();
    }

    /// <summary>
    /// Runs until the loop stops, emitting exactly one <see cref="CacheWatcherPolicy.OnStopped"/>
    /// as it does. The caller owns the returned task and the <paramref name="cancellationToken"/>
    /// that stops it.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        CacheWatcherStoppedReason reason;
        try
        {
            reason = await LoopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            reason = CacheWatcherStoppedReason.Cancelled;
        }

        policy.OnStopped?.Invoke(reason);
    }

    private async Task<CacheWatcherStoppedReason> LoopAsync(CancellationToken cancellationToken)
    {
        Dictionary<string, int> lastSeen = new(StringComparer.Ordinal);
        string? etag = null;
        int consecutiveFailures = 0;

        while (true)
        {
            CacheVersion result;
            try
            {
                result = await client.Sys.CacheVersionAsync(
                    topics, watch: true, ifNoneMatch: etag, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (BastionVaultException failure)
            {
                // The same distinction TokenRenewal.AttemptAsync draws: a request cut short by the
                // caller's own cancellation surfaces as the executor's mapped BV-TRANSPORT-005, not
                // as an OperationCanceledException, and counting it as an ordinary watch failure
                // would emit a spurious OnFailed on the way out.
                if (string.Equals(failure.Code, ErrorCodes.TransportCancelled, StringComparison.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                // TokenRenewal.AttemptAsync's exact order (AUT-092): every failure is reported,
                // including the one that stops the loop, before the stop decision is made.
                consecutiveFailures++;
                policy.OnFailed?.Invoke(failure);

                // CCH-006: stops terminally, before the failure cap is even consulted.
                if (string.Equals(failure.Code, ErrorCodes.AuthzPermissionDenied, StringComparison.Ordinal))
                {
                    return CacheWatcherStoppedReason.PermissionDenied;
                }

                if (consecutiveFailures >= policy.MaxConsecutiveFailures)
                {
                    return CacheWatcherStoppedReason.FailureCapExceeded;
                }

                await client.Context.Clock.Delay(
                    BackoffCalculator.Compute(client.Config.RetryPolicy, consecutiveFailures, client.Context.JitterSource),
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            consecutiveFailures = 0;
            etag = result.ETag ?? etag;

            if (result.NotModified)
            {
                // CCH-002's quiet path: nothing else on this answer is populated, and nothing
                // changed.
                continue;
            }

            foreach (KeyValuePair<string, int> observed in result.Topics!)
            {
                if (lastSeen.TryGetValue(observed.Key, out int previous) && observed.Value > previous)
                {
                    policy.OnChanged?.Invoke(new CacheTopicChanged
                    {
                        Topic = observed.Key,
                        PreviousEpoch = previous,
                        CurrentEpoch = observed.Value,
                    });
                }

                lastSeen[observed.Key] = observed.Value;
            }
        }
    }
}

/// <summary>
/// <see cref="CacheWatcher"/>'s configuration: its callbacks and its failure cap, the same shape
/// <see cref="AutoRenewPolicy"/> uses for the equivalent loop (AUT-090…AUT-094).
/// </summary>
public sealed record CacheWatcherPolicy
{
    /// <summary>How many consecutive watch failures are absorbed before the loop stops. Default 5.</summary>
    public int MaxConsecutiveFailures { get; init; } = 5;

    /// <summary>CCH-004: raised once per topic whose epoch increased since the last observation.</summary>
    public Action<CacheTopicChanged>? OnChanged { get; init; }

    /// <summary>Raised on each watch failure, including the ones the loop then absorbs with backoff.</summary>
    public Action<BastionVaultException>? OnFailed { get; init; }

    /// <summary>
    /// Raised exactly once, when the loop stops for good. A loop that stops emits one reason and
    /// never runs again.
    /// </summary>
    public Action<CacheWatcherStoppedReason>? OnStopped { get; init; }
}

/// <summary>Why a <see cref="CacheWatcher"/> loop stopped.</summary>
public enum CacheWatcherStoppedReason
{
    /// <summary>CCH-006: the watch answered <c>BV-AUTHZ-001</c>, so the loop stopped immediately.</summary>
    PermissionDenied,

    /// <summary><see cref="CacheWatcherPolicy.MaxConsecutiveFailures"/> consecutive watch failures.</summary>
    FailureCapExceeded,

    /// <summary>The <see cref="CancellationToken"/> passed to <see cref="CacheWatcher.RunAsync"/> fired.</summary>
    Cancelled,
}

/// <summary>
/// One topic whose epoch increased (CCH-004), as reported to <see cref="CacheWatcherPolicy.OnChanged"/>.
/// </summary>
public sealed class CacheTopicChanged
{
    /// <summary>The topic name, as it appears in <see cref="CacheVersion.Topics"/>.</summary>
    public required string Topic { get; init; }

    /// <summary>The epoch last observed for this topic, before this change.</summary>
    public required int PreviousEpoch { get; init; }

    /// <summary>The epoch this observation reported. Always greater than <see cref="PreviousEpoch"/>.</summary>
    public required int CurrentEpoch { get; init; }
}
