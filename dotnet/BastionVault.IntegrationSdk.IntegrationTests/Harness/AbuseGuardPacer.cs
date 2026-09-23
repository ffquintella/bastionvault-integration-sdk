namespace BastionVault.IntegrationSdk.IntegrationTests.Harness;

/// <summary>
/// DR-0021 F9: shared admission control in front of the managed server's abuse guard.
/// </summary>
/// <remarks>
/// EFF-001's per-client <c>ClientRateGate</c> cannot fix this alone: this suite creates one client
/// per scenario, so N independent buckets bound each client but not the aggregate the guard counts.
/// </remarks>
internal sealed class AbuseGuardPacer
{
    // Every harness-created client shares one PacedTransport/AbuseGuardPacer pair, so pacing
    // happens once per physical request regardless of how many scenarios run concurrently.
    // ITG-012 is unaffected: SDK-level client state stays per-scenario, only the wire is shared.

    private readonly SlidingWindowLimiter overall;
    private readonly SlidingWindowLimiter auth;

    public AbuseGuardPacer(DosDefaults defaults)
    {
        // Halved from the matrix's own numbers: this window is an approximation of the real
        // server, and margin absorbs that plus start-of-run bursts (D-M12 F9).
        TimeSpan window = TimeSpan.FromSeconds(Math.Max(1, defaults.WindowSecs));
        overall = new SlidingWindowLimiter(Margin(defaults.MaxRequests), window);
        auth = new SlidingWindowLimiter(Margin(defaults.AuthMaxRequests), window);
    }

    // Half the server's own ceiling; never below 1 (a 0 limit would wait forever).
    private static int Margin(long limit)
    {
        return (int)Math.Max(1, limit / 2);
    }

    // Only /login paths take the stricter budget, not every auth/* call: F9's evidence was login
    // storms from parallel scenario setup. auth/token/renew/* is exempt from both windows: it is a
    // single low-frequency call an active AutoRenewPolicy loop fires on its own schedule, it cannot
    // itself drive volume, and ITG-S11 depends on it clearing without queuing behind the very
    // scenarios that got it excused (its own real-time renewal deadline, DR-0021 F1).
    public async Task WaitForAdmissionAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (IsRenewPath(uri))
        {
            return;
        }

        await overall.AcquireAsync(cancellationToken).ConfigureAwait(false);
        if (IsLoginPath(uri))
        {
            await auth.AcquireAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsLoginPath(Uri uri)
    {
        string path = uri.AbsolutePath;
        return path.Contains("/auth/", StringComparison.Ordinal) && path.Contains("/login", StringComparison.Ordinal);
    }

    private static bool IsRenewPath(Uri uri)
    {
        return uri.AbsolutePath.Contains("/token/renew", StringComparison.Ordinal);
    }
}

/// <summary>A sliding-window counter: at most <paramref name="limit"/> admissions per trailing <paramref name="window"/>.</summary>
internal sealed class SlidingWindowLimiter(int limit, TimeSpan window)
{
    private readonly Lock sync = new();
    private readonly Queue<DateTimeOffset> hits = new();

    public async Task AcquireAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan wait;
            lock (sync)
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                while (hits.Count > 0 && now - hits.Peek() >= window)
                {
                    _ = hits.Dequeue();
                }

                if (hits.Count < limit)
                {
                    hits.Enqueue(now);
                    return;
                }

                wait = hits.Peek() + window - now;
            }

            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}

/// <summary>Wraps the one real transport every harness client shares, pacing before it sends.</summary>
internal sealed class PacedTransport(ITransport inner, AbuseGuardPacer pacer) : ITransport
{
    public bool SupportsCustomVerbs => inner.SupportsCustomVerbs;

    public async Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken = default)
    {
        await pacer.WaitForAdmissionAsync(request.Uri, cancellationToken).ConfigureAwait(false);
        return await inner.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
