namespace BastionVault.IntegrationSdk;

/// <summary>
/// The injected time seam (D-M1b-7, RES-003): every backoff and every rate-gate pause wait goes
/// through <see cref="Delay"/>, so no test in any language needs to sleep in real time.
/// </summary>
public interface IClock
{
    /// <summary>
    /// The current <b>wall-clock</b> instant, in UTC. Named for the kind of time it returns
    /// (D-M2-2): AUT-014's <c>RemainingTtl</c> and AUT-090's renewal schedule are unix-epoch
    /// arithmetic, while a backoff delay is a duration, and <c>Now()</c> meant wall-clock on two
    /// of the three SDKs and monotonic on the third. The RES-004 total-timeout deadline in
    /// <c>RequestExecutor</c> stays on this member, unchanged (D-M2-2).
    /// </summary>
    public DateTimeOffset NowUtc();

    /// <summary>Asynchronously waits for <paramref name="duration"/>, honouring <paramref name="cancellationToken"/>.</summary>
    public Task Delay(TimeSpan duration, CancellationToken cancellationToken);
}

/// <summary>The default <see cref="IClock"/>: the real system clock and a real asynchronous delay.</summary>
public sealed class SystemClock : IClock
{
    /// <summary>The shared system-clock instance.</summary>
    public static SystemClock Instance { get; } = new();

    private SystemClock()
    {
    }

    /// <inheritdoc/>
    public DateTimeOffset NowUtc()
    {
        return DateTimeOffset.UtcNow;
    }

    /// <inheritdoc/>
    public Task Delay(TimeSpan duration, CancellationToken cancellationToken)
    {
        return duration <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(duration, cancellationToken);
    }
}
