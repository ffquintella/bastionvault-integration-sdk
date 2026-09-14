namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// AUT-090…AUT-094's renewal loop: one scheduled <c>RenewSelf</c> per lease, recomputed from each
/// answer, with AUT-092's own backoff and AUT-093's single re-login.
/// </summary>
/// <remarks>
/// <para>
/// <b>Shape, pinned at D-M2-27 item 7:</b> exactly one
/// <c>await Clock.Delay(wake - Clock.NowUtc(), ct)</c> per scheduled renewal, never a polling
/// <c>while</c> loop that wakes to ask whether it is time yet. A fixture's <c>clock.expectWaits</c>
/// asserts the durations this loop asks for, so a poll would turn one assertable schedule into an
/// unbounded stream of small waits that asserts nothing.
/// </para>
/// <para>
/// <b>AUT-092's backoff applies no jitter</b> (D-M2-27 item 6). The requirement names none —
/// "starting at 1 s, capped at 1/4 of the remaining TTL" — unlike the transport-level retry path
/// in <see cref="RequestExecutor"/>, which does apply <see cref="IJitterSource"/>. The two are
/// deliberately not the same backoff, and reusing the transport's here would make every
/// <c>auth.autorenew.*</c> fixture's <c>expectWaits</c> unpredictable.
/// </para>
/// </remarks>
internal sealed class TokenRenewal
{
    /// <summary>AUT-092's three stop-immediately codes. Everything else is retried with backoff.</summary>
    private static readonly string[] StopImmediately =
    [
        ErrorCodes.AuthzPermissionDenied,
        ErrorCodes.AuthTokenNotRenewable,
        ErrorCodes.ServerSealed,
    ];

    private readonly ClientContext context;
    private readonly AutoRenewPolicy policy;

    /// <summary>
    /// AUT-093's "once": spent by a fresh login, and re-armed by the next renewal that succeeds.
    /// Written only from the single loop task, so it needs no synchronisation.
    /// </summary>
    private bool reloginAvailable = true;

    public TokenRenewal(ClientContext context, AutoRenewPolicy policy)
    {
        this.context = context;
        this.policy = policy;
    }

    /// <summary>
    /// Runs until the loop stops, emitting exactly one
    /// <see cref="AutoRenewPolicy.OnStopped"/> as it does. Awaitable so AUT-094's hosting — a
    /// hosted service, or the <c>Task.Run</c> the client starts it on — can observe it; the
    /// <paramref name="cancellationToken"/> is the one <c>Client.Dispose</c> cancels.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        RenewalStoppedReason reason;
        bool tokenCleared = false;
        CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        void OnTokenCleared()
        {
            tokenCleared = true;
            Cancel(stop);
        }

        context.TokenCleared += OnTokenCleared;
        try
        {
            try
            {
                reason = await LoopAsync(stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Two cancellations reach here and they are not the same event: AUT-092's cleared
                // token cancels `stop` from `ClientContext`, and AUT-094's Dispose cancels the
                // token `stop` is linked to. They are told apart by which one fired, not by reading
                // the token cell — a client whose Login source has not logged in yet also holds no
                // token, and disposing that one is not a revocation.
                reason = tokenCleared ? RenewalStoppedReason.TokenRevoked : RenewalStoppedReason.Disposed;
            }
            finally
            {
                stop.Dispose();
            }

            // The handler is still attached here on purpose. An application that clears its token
            // *from* OnStopped — a reasonable thing to do when renewal has given up — would
            // otherwise be the one race where a cleared token reaches a source that no longer
            // exists, which is what Cancel absorbs.
            policy.OnStopped?.Invoke(reason);
        }
        finally
        {
            context.TokenCleared -= OnTokenCleared;
        }
    }

    private async Task<RenewalStoppedReason> LoopAsync(CancellationToken cancellationToken)
    {
        AuthInfo credential = await context.CredentialIssued.WaitAsync(cancellationToken).ConfigureAwait(false);

        while (true)
        {
            if (Schedule(credential) is not { } schedule)
            {
                // AUT-090's precondition ("Renewable with LeaseDuration > 0") is not met, which is
                // also AUT-095's batch and non-renewable tokens. Nothing is renewed and nothing is
                // retried; the reason is reported so the application is not left guessing why its
                // loop is silent. AUT-095 also requires one info-level log line here — never Warn,
                // which would misreport this as more severe than it is (D-M2-28 item 4).
                context.Logger.Info("BastionVault: token is not renewable (batch or non-renewable); AutoRenew is not scheduling a renewal.");
                return RenewalStoppedReason.NotRenewable;
            }

            RenewalStoppedReason outcome = await ScheduleAsync(schedule, cancellationToken).ConfigureAwait(false);
            if (outcome != RenewalStoppedReason.RenewalFailed
                || context.TokenSource.Kind != TokenSourceKind.Login
                || !reloginAvailable)
            {
                return outcome;
            }

            // AUT-093: a Login source gets one fresh login after renewal stops, and the schedule
            // resumes from whatever that login was granted. Spending the allowance here is what
            // stops a credential the server will never renew from costing an unbounded stream of
            // logins; ScheduleAsync re-arms it on the next renewal that succeeds, so a client that
            // recovers is not left with one re-login for the rest of its life.
            reloginAvailable = false;
            if (await ReloginAsync(cancellationToken).ConfigureAwait(false) is not { } relogged)
            {
                return RenewalStoppedReason.ReloginFailed;
            }

            credential = relogged;
        }
    }

    /// <summary>
    /// One credential's schedule: renew at AUT-090's instant, recompute from the answer (AUT-091),
    /// and absorb failures with AUT-092's backoff until the loop must stop.
    /// </summary>
    private async Task<RenewalStoppedReason> ScheduleAsync(RenewalSchedule schedule, CancellationToken cancellationToken)
    {
        DateTimeOffset? previousRenewal = null;

        while (true)
        {
            DateTimeOffset wake = schedule.IssuedAt + schedule.LeaseDuration * policy.RenewAtFraction;
            if (previousRenewal is { } previous && wake < previous + policy.MinInterval)
            {
                // AUT-090: "never sooner than MinInterval after the previous renewal".
                wake = previous + policy.MinInterval;
            }

            // Exactly one scheduled wait per renewal (D-M2-27 item 7). AUT-092's backoff waits
            // happen inside AttemptAsync, and re-entering this one after a backoff would add a
            // zero-length wait per retry — a second wait per renewal, which is precisely what the
            // pinned loop shape forbids.
            await DelayUntilAsync(wake, cancellationToken).ConfigureAwait(false);

            if (await AttemptAsync(schedule, cancellationToken).ConfigureAwait(false) is not { } renewed)
            {
                // AUT-092 gave up: either one of its three stop-immediately codes, or
                // MaxConsecutiveFailures consecutive failures.
                return RenewalStoppedReason.RenewalFailed;
            }

            previousRenewal = context.Clock.NowUtc();
            reloginAvailable = true;
            policy.OnRenewed?.Invoke(new RenewalEvent
            {
                At = previousRenewal.Value,
                Auth = renewed,
                ConsecutiveFailures = 0,
            });

            if (Schedule(renewed) is not { } next)
            {
                // The server answered a renewal with a credential it will not renew again.
                return RenewalStoppedReason.NotRenewable;
            }

            // AUT-091: the schedule is recomputed from the *new* lease_duration and the issue time
            // the SDK observed for the renewed credential, never carried over from the old one.
            schedule = next;
        }
    }

    /// <summary>
    /// One renewal, retried with AUT-092's backoff until it succeeds or the loop must stop. Returns
    /// the renewed credential, or <see langword="null"/> when AUT-092 has given up.
    /// </summary>
    private async Task<AuthInfo?> AttemptAsync(RenewalSchedule schedule, CancellationToken cancellationToken)
    {
        int consecutiveFailures = 0;
        while (true)
        {
            try
            {
                return await RenewAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (BastionVaultException failure)
            {
                // AUT-094: a renewal cut short by Dispose surfaces as the executor's mapped
                // BV-TRANSPORT-005, not as an OperationCanceledException, and counting it as an
                // AUT-092 renewal failure would emit a spurious OnFailed on the way out. Keyed on
                // the mapped code and not merely on "the token is now cancelled": a real failure
                // that arrives in the same instant as a Dispose is still a real failure, and
                // AUT-092 must report it.
                if (string.Equals(failure.Code, ErrorCodes.TransportCancelled, StringComparison.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                consecutiveFailures++;
                policy.OnFailed?.Invoke(new RenewalEvent
                {
                    At = context.Clock.NowUtc(),
                    Error = failure,
                    ConsecutiveFailures = consecutiveFailures,
                });

                if (StopImmediately.Contains(failure.Code, StringComparer.Ordinal)
                    || consecutiveFailures >= policy.MaxConsecutiveFailures)
                {
                    return null;
                }

                await BackoffAsync(consecutiveFailures, schedule, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// AUT-092's backoff: <c>1 s</c> doubling per consecutive failure, capped at a quarter of the
    /// credential's remaining TTL, and with <b>no jitter</b> (D-M2-27 item 6).
    /// </summary>
    private Task BackoffAsync(int consecutiveFailures, RenewalSchedule schedule, CancellationToken cancellationToken)
    {
        // Doubling is computed on ticks from a fixed base rather than by repeated multiplication,
        // and the exponent is bounded by MaxConsecutiveFailures, so no overflow is reachable.
        TimeSpan backoff = TimeSpan.FromTicks(TimeSpan.TicksPerSecond * (1L << Math.Min(consecutiveFailures - 1, 32)));
        TimeSpan remaining = schedule.IssuedAt + schedule.LeaseDuration - context.Clock.NowUtc();
        TimeSpan cap = remaining > TimeSpan.Zero ? remaining / 4 : TimeSpan.Zero;
        if (backoff > cap)
        {
            backoff = cap;
        }

        return context.Clock.Delay(backoff, cancellationToken);
    }

    /// <summary>
    /// AUT-093's single fresh login: the cached credential is discarded and the source resolves
    /// again, which for a <see cref="TokenSourceKind.Login"/> source performs one login.
    /// </summary>
    private async Task<AuthInfo?> ReloginAsync(CancellationToken cancellationToken)
    {
        try
        {
            context.TokenSource.Invalidate();
            await context.ResolveTokenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (BastionVaultException)
        {
            return null;
        }

        return context.LastLogin;
    }

    /// <summary>
    /// AUT-080's <c>RenewSelf</c>, at the client's own namespace and with no per-request options:
    /// the loop belongs to the client, not to any one call (the same reasoning
    /// <see cref="ClientContext.PerformLoginAsync"/> records for a Login source's own login).
    /// </summary>
    private Task<AuthInfo> RenewAsync(CancellationToken cancellationToken)
    {
        TokenOperations tokens = new(context, new LogicalOperations(context, context.Config.Namespace));
        // `Increment = null` is D-M2-6's "server default", and Appendix A makes `increment` a
        // required body field, so the request asks for no particular TTL rather than omitting it.
        int increment = policy.Increment is { } configured ? (int)configured.TotalSeconds : 0;
        return tokens.RenewSelfAsync(increment, options: null, cancellationToken);
    }

    /// <summary>
    /// One <c>Clock.Delay</c> per scheduled renewal (D-M2-27 item 7). A wake that is already in the
    /// past waits zero rather than a negative duration, which is what a schedule computed from a
    /// credential that was issued before the loop started produces.
    /// </summary>
    private Task DelayUntilAsync(DateTimeOffset wake, CancellationToken cancellationToken)
    {
        TimeSpan delay = wake - context.Clock.NowUtc();
        return context.Clock.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, cancellationToken);
    }

    /// <summary>
    /// AUT-090's precondition, as a value: the credential is renewable and carries a positive
    /// lease. Anything else yields <see langword="null"/>, which the loop reports as
    /// <see cref="RenewalStoppedReason.NotRenewable"/> rather than renewing on a guessed lease.
    /// </summary>
    private static RenewalSchedule? Schedule(AuthInfo credential)
        => credential is { Renewable: true, LeaseDuration: { } lease } && lease > TimeSpan.Zero
            ? new RenewalSchedule(credential.IssuedAt, lease)
            : null;

    private static void Cancel(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The loop already finished and disposed its source; there is nothing left to stop.
        }
    }

    /// <summary>AUT-090's two operands: when the credential was issued, and for how long.</summary>
    private readonly record struct RenewalSchedule(DateTimeOffset IssuedAt, TimeSpan LeaseDuration);
}
