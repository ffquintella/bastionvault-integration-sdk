namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// What kind of outgoing work is asking the gate for permission. EFF-001 says <i>every</i>
/// outgoing request is gated, so every exemption is named here rather than expressed as a code
/// path that quietly never calls the gate.
/// </summary>
/// <remarks>
/// <para>
/// The distinction this enum exists to preserve is <b>"never gated" versus "not yet gated"</b>. An
/// egress path that simply does not call <see cref="ClientRateGate.AcquireAsync"/> reads to a
/// later auditor as an EFF-001 violation, and cannot be told apart from one that was forgotten.
/// Every exempt path therefore calls the gate and says which exemption it is claiming.
/// </para>
/// </remarks>
internal enum EgressKind
{
    /// <summary>
    /// An ordinary caller-visible request through <see cref="RequestExecutor"/>. Gated (EFF-001).
    /// </summary>
    Request,

    /// <summary>
    /// A cluster-discovery health probe (<c>GET {candidate}/v1/sys/health</c>, section 13).
    /// <b>Exempt by EFF-005</b> — "they are exempt server-side too", so gating them client-side
    /// would slow discovery down to protect a guard that is not watching.
    /// </summary>
    DiscoveryProbe,

    /// <summary>
    /// A DNS SRV lookup through the DSC-014 resolver seam. <b>Exempt permanently</b>: it is DNS
    /// I/O and not an HTTP request to the cluster at all, so EFF-001's "every outgoing request"
    /// does not reach it and the server's own HTTP abuse guard never sees it.
    /// </summary>
    SrvResolution,
}

/// <summary>
/// EFF-001…EFF-006: the client-side token bucket, its FIFO queue, its <c>429</c> pause, and the
/// <see cref="RateGateState"/> snapshot. One instance per client, shared by every
/// <c>WithNamespace</c> view (D-M1b-9), because the server's guard counts per client and not per
/// view.
/// </summary>
/// <remarks>
/// <para>
/// <b>The bucket is kept as a schedule, not as a counter that is polled.</b> One field,
/// <c>nextFree</c>, holds the instant at which the next token becomes available; a waiter reserves
/// that instant, advances <c>nextFree</c> by one token-interval, and sleeps for the difference.
/// That is arithmetically the same bucket as "refill by elapsed × rate, capped at burst, then
/// decrement" — the clamp <c>nextFree ≥ now − (Burst−1) × interval</c> <i>is</i> the cap — but it
/// has two properties the counter form does not:
/// </para>
/// <list type="number">
/// <item>
/// It never re-reads the clock after waiting, so it cannot spin. Every test clock in this
/// repository (<c>FixtureClock</c>, and the unit-test clocks) completes <see cref="IClock.Delay"/>
/// without moving wall time; a poll loop would therefore never observe the refill it just waited
/// for and would loop for ever. D-M1b-7's "no test sleeps in real time" only holds for a gate
/// written this way.
/// </item>
/// <item>
/// The wait one request is granted is a pure function of the reservations before it, so a fixture
/// can assert the whole schedule through <c>clock.expectWaits</c> (D-M2-27).
/// </item>
/// </list>
/// <para>
/// <b>FIFO (EFF-002) is a chain, not a priority queue.</b> Each acquirer atomically swaps its own
/// completion into <c>tail</c> and awaits the one it displaced, so waiters are served in the order
/// they arrived at the gate and no ordering is left to the thread pool's choice of which
/// <see cref="Task.Delay(TimeSpan)"/> to resume first. A cancelled waiter still releases its
/// successor (the <c>finally</c>), so one cancellation cannot wedge the queue.
/// </para>
/// </remarks>
internal sealed class ClientRateGate
{
    private readonly object gate = new();
    private readonly IClock clock;
    private readonly bool disabled;
    private readonly int burst;
    private readonly TimeSpan interval;

    /// <summary>The tail of the EFF-002 FIFO chain: the completion the next arrival must wait on.</summary>
    private Task tail = Task.CompletedTask;

    /// <summary>The instant the next token becomes available. <see cref="DateTimeOffset.MinValue"/> means "a full bucket, whenever you ask".</summary>
    private DateTimeOffset nextFree = DateTimeOffset.MinValue;

    private DateTimeOffset? pausedUntil;

    public ClientRateGate(RateGate configuration, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        this.clock = clock;
        disabled = configuration.IsDisabled;
        burst = configuration.Burst;

        // Guarded by `disabled`: RatePerSecond is 0 exactly when the gate is off, so this never
        // divides by zero. Ticks rather than seconds so the schedule is exact for the rates the
        // fixtures use (8/s is 1 250 000 ticks, no rounding).
        interval = disabled
            ? TimeSpan.Zero
            : TimeSpan.FromTicks(Math.Max(1L, TimeSpan.TicksPerSecond / configuration.RatePerSecond));
    }

    /// <summary>
    /// EFF-005 and the SRV limb: whether <paramref name="kind"/> never passes through the bucket.
    /// Exhaustive by construction — a new <see cref="EgressKind"/> is gated unless it is added
    /// here with the requirement that exempts it.
    /// </summary>
    public static bool IsExempt(EgressKind kind)
    {
        return kind is EgressKind.DiscoveryProbe or EgressKind.SrvResolution;
    }

    /// <summary>
    /// EFF-001/EFF-002: waits until this egress may proceed. Returns immediately when the gate is
    /// disabled, when <paramref name="kind"/> is exempt, or when a token is already available.
    /// </summary>
    public async Task AcquireAsync(EgressKind kind, CancellationToken cancellationToken)
    {
        if (disabled || IsExempt(kind))
        {
            return;
        }

        TaskCompletionSource mine = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task predecessor;
        lock (gate)
        {
            predecessor = tail;
            tail = mine.Task;
        }

        try
        {
            // The chain is what makes service order arrival order (EFF-002). `predecessor` is only
            // ever completed by the `finally` below, so it cannot fault and needs no catch.
            await predecessor.ConfigureAwait(false);

            TimeSpan wait;
            lock (gate)
            {
                wait = Reserve(clock.NowUtc());
            }

            if (wait > TimeSpan.Zero)
            {
                await clock.Delay(wait, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            // Releases the successor even when this waiter was cancelled, so one cancellation
            // costs one slot rather than the whole queue.
            _ = mine.TrySetResult();
        }
    }

    /// <summary>
    /// EFF-003/EFF-004: pauses the whole queue until <paramref name="until"/> and drops the
    /// accumulated tokens. The caller has already applied <c>min(Retry-After, 30s)</c> / the 1 s
    /// default (<see cref="RequestExecutor"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Drop accumulated tokens" and "pause the queue" are the <i>same</i> assignment here:
    /// moving <c>nextFree</c> forward to <paramref name="until"/> means no reservation can be
    /// granted before then (the pause) and that the bucket restarts with no credit at then (the
    /// drop) — the very next reservation is granted at <paramref name="until"/> and the one after
    /// it a full interval later, rather than a burst arriving the instant the pause lifts.
    /// </para>
    /// <para>
    /// A pause is never <b>shortened</b> by a later, nearer one. Both halves of the state would
    /// otherwise disagree: <c>nextFree</c> is a high-water mark and cannot move backwards without
    /// releasing tokens EFF-003 says were dropped, so a <c>PausedUntil</c> that moved back would
    /// report the queue resuming while it is still held. Python's client takes the same maximum;
    /// <c>rust/.../rate.rs</c> overwrites, which is recorded as an open parity question rather
    /// than changed here (Stage 2 is frozen).
    /// </para>
    /// </remarks>
    public void Pause(DateTimeOffset until)
    {
        lock (gate)
        {
            if (pausedUntil is not { } current || until > current)
            {
                pausedUntil = until;
            }

            if (nextFree < until)
            {
                nextFree = until;
            }
        }
    }

    /// <summary>EFF-006: the current state, for diagnostics.</summary>
    public RateGateState Snapshot()
    {
        DateTimeOffset now = clock.NowUtc();
        lock (gate)
        {
            bool paused = pausedUntil is { } until && until > now;
            return new RateGateState(paused, pausedUntil, AvailableTokens(now, paused));
        }
    }

    /// <summary>
    /// Reserves the next slot and returns how long the caller must wait for it. Called under
    /// <see cref="gate"/>.
    /// </summary>
    private TimeSpan Reserve(DateTimeOffset now)
    {
        // The burst cap, expressed as a floor on the schedule: an idle client may take `Burst`
        // reservations at or before `now`, and no more. `Burst` is at least 1 here, because 0
        // disables the gate (EFF-001) and this method is unreachable then.
        DateTimeOffset floor = now - TimeSpan.FromTicks((burst - 1) * interval.Ticks);
        if (nextFree < floor)
        {
            nextFree = floor;
        }

        DateTimeOffset grantAt = nextFree;
        nextFree = grantAt + interval;
        return grantAt - now;
    }

    /// <summary>EFF-006's third field. Called under <see cref="gate"/>.</summary>
    private int AvailableTokens(DateTimeOffset now, bool paused)
    {
        if (disabled)
        {
            return burst;
        }

        if (paused || now < nextFree)
        {
            return 0;
        }

        // How many reservations would be granted with no wait: `nextFree`, `nextFree + interval`,
        // … up to `now`, capped at the burst.
        long credits = ((now - nextFree).Ticks / interval.Ticks) + 1;
        return (int)Math.Min(credits, burst);
    }
}
