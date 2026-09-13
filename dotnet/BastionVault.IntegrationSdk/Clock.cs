namespace BastionVault.IntegrationSdk;

/// <summary>
/// The injected time seam (D-M1b-7, RES-003): every backoff and every rate-gate pause wait goes
/// through <see cref="Delay"/>, so no test in any language needs to sleep in real time.
/// </summary>
public interface IClock
{
    /// <summary>The current instant.</summary>
    DateTimeOffset Now();

    /// <summary>Asynchronously waits for <paramref name="duration"/>, honouring <paramref name="cancellationToken"/>.</summary>
    Task Delay(TimeSpan duration, CancellationToken cancellationToken);
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
    public DateTimeOffset Now() => DateTimeOffset.UtcNow;

    /// <inheritdoc/>
    public Task Delay(TimeSpan duration, CancellationToken cancellationToken)
        => duration <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(duration, cancellationToken);
}
