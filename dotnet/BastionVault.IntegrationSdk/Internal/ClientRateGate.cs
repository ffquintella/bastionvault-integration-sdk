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
/// <para>
/// The chain has a second consequence the pause rule depends on: because a waiter holds its link
/// from before <see cref="Reserve"/> until after its delay, <b>at most one reservation is
/// outstanding at any instant</b>. A later arrival cannot even reach <see cref="Reserve"/> while
/// an earlier one is waiting.
/// </para>
/// <para>
/// <b>A reservation is re-validated against the pause, and only against the pause
/// (EFF-003).</b> The instant a waiter is granted is decided before it sleeps, so a <c>429</c>
/// that arrives <i>while</i> it sleeps would otherwise let exactly one request out during the
/// window the server is banning the client for — the precise failure section 14 exists to
/// prevent, and one that earns a second <c>429</c> and a longer pause. So after its delay a
/// waiter re-enters the lock and asks one question: <b>has a pause been declared that reaches
/// past the instant I was granted?</b> If so it re-queues behind the pause and sleeps again.
/// </para>
/// <para>
/// This is emphatically <b>not</b> the refill poll the first note rules out, and the difference
/// is what makes it terminate. It never re-reads the clock hoping time has passed; it compares
/// two absolute instants that only an external event can move. <c>pausedUntil</c> never moves
/// backwards (see <see cref="Pause"/>), and a re-reservation is taken from a <c>nextFree</c> that
/// <see cref="Pause"/> has already clamped to at least <c>pausedUntil</c> — so each pass leaves
/// the waiter with <c>grantAt &gt;= pausedUntil</c>, and a further pass requires a <i>strictly
/// later</i> pause, which requires another <c>429</c> from another in-flight request. Iterations
/// are therefore bounded by the number of <c>429</c>s actually received, never by the clock: with
/// no new pause the loop runs exactly once, and it runs exactly once under a test clock whose
/// <see cref="IClock.Delay"/> completes without moving wall time.
/// </para>
/// <para>
/// <b>Class invariant, and the one a future edit is most likely to break silently:
/// <c>nextFree &gt;= pausedUntil</c> at all times.</b> It is what makes every argument above
/// work — it is why a live pause always yields <c>wait &gt; 0</c> (so the re-validation loop can
/// never be skipped on entry), and why falling out of that loop on <c>wait &lt;= 0</c> carries the
/// same postcondition as the explicit <c>break</c>. It holds inductively because
/// <see cref="Pause"/> sets both fields from one <c>until</c>, and because every other write to
/// <c>nextFree</c> (<see cref="Reserve"/>'s floor clamp and its <c>grantAt + interval</c>) only
/// ever raises it. <b>A new write to <c>nextFree</c> outside <see cref="Reserve"/> and
/// <see cref="Pause"/>, or one that can lower it, invalidates the pause guarantee of EFF-003
/// without failing any test that exists today.</b>
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

            DateTimeOffset grantAt;
            TimeSpan wait;
            lock (gate)
            {
                (grantAt, wait) = Reserve(clock.NowUtc());
            }

            while (wait > TimeSpan.Zero)
            {
                await clock.Delay(wait, cancellationToken).ConfigureAwait(false);

                lock (gate)
                {
                    // EFF-003's "pause the whole queue", applied to a waiter that was already in
                    // the queue when the pause was declared. Re-queueing rather than simply
                    // sleeping to `pausedUntil` is what makes the resumption obey the rate: the
                    // pause dropped the accumulated tokens, so this waiter takes a fresh slot
                    // from a `nextFree` that is at or past `pausedUntil` (the class invariant).
                    // When the pause landed strictly between this waiter's `grantAt` and
                    // `nextFree`, that fresh slot is `nextFree` rather than `pausedUntil`, so the
                    // waiter is delayed by up to one extra interval. That is conservative — it
                    // can only release later than the pause requires, never earlier — and it is
                    // the price of taking a slot from the schedule instead of special-casing one.
                    // Re-queueing cannot cost this waiter its place, because the chain means no
                    // later arrival has reserved anything (see the class remarks).
                    if (pausedUntil is not { } until || until <= grantAt)
                    {
                        break;
                    }

                    (grantAt, wait) = Reserve(clock.NowUtc());
                }
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
    /// Reserves the next slot and returns the instant it was granted for together with how long
    /// the caller must wait for it. Called under <see cref="gate"/>.
    /// </summary>
    /// <remarks>
    /// The grant instant is returned, and not only the wait, because EFF-003's re-validation in
    /// <see cref="AcquireAsync"/> has to compare a later pause against <i>when this waiter was
    /// let through</i>. A remaining duration cannot answer that question: it is relative to a
    /// <c>now</c> that has moved by the time the answer is needed.
    /// </remarks>
    private (DateTimeOffset GrantAt, TimeSpan Wait) Reserve(DateTimeOffset now)
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
        return (grantAt, grantAt - now);
    }

    /// <summary>EFF-006's third field. Called under <see cref="gate"/>.</summary>
    /// <remarks>
    /// A disabled gate reports <see cref="int.MaxValue"/>, not the configured <c>Burst</c>. The
    /// <c>Burst</c> reading was wrong in exactly the case the second <c>IsDisabled</c> limb
    /// creates: <c>RateGate { RatePerSecond = 8, Burst = 0 }</c> — reachable from
    /// <c>BASTIONVAULT_RATE_BURST=0</c> — disables the gate and would then have reported
    /// <c>Paused = false, AvailableTokens = 0</c>, which is the one pair a diagnostics consumer
    /// reads as "fully throttled", for a gate that withholds nothing. The sentinel cannot be
    /// misread: <c>AvailableTokens &gt; 0</c> means "may proceed now" on both settings.
    /// <para>
    /// <b>The sentinel value is .NET-only today, and deliberately not claimed as cross-language.</b>
    /// EFF-006's third field does not exist in the other two SDKs — <c>rust/…/rate.rs</c>'s
    /// <c>RateGateState</c> carries <c>paused_until</c> and no available-tokens accessor, and
    /// Python has none either — so there is nothing to be consistent with yet. What the parity
    /// pass must carry across is the <i>invariant</i> (<c>AvailableTokens &gt; 0</c> means "may
    /// proceed without waiting"), not the literal <c>2147483647</c>: Rust's natural sentinel is
    /// <c>u32::MAX</c>. See DR-0013 D-M8-35.
    /// </para>
    /// </remarks>
    private int AvailableTokens(DateTimeOffset now, bool paused)
    {
        if (disabled)
        {
            return int.MaxValue;
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
