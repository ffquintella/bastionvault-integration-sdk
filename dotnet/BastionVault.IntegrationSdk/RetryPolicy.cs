namespace BastionVault.IntegrationSdk;

/// <summary>
/// Retry policy configuration (CFG-050). Execution (CFG-051..055) is M1b
/// (<c>decisions/0004-m1b-transport.md</c>, D-M1b-7).
/// </summary>
public sealed record RetryPolicy
{
    /// <summary>Total attempts including the first. Default 3.</summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>Default 250ms.</summary>
    public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Default 5s.</summary>
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Default 2.0.</summary>
    public double BackoffMultiplier { get; init; } = 2.0;

    /// <summary>Default ±20% (0.2).</summary>
    public double Jitter { get; init; } = 0.2;

    /// <summary>
    /// The mapped error codes eligible for automatic retry (D-M1b-7). Default
    /// <c>[BV-TRANSPORT-001, BV-TRANSPORT-002, BV-SERVER-002, BV-SERVER-003]</c>. <c>BV-SERVER-001</c>
    /// and <c>BV-RATE-001</c> are never retried even if present here (CFG-052, CFG-053).
    /// </summary>
    public IReadOnlyList<string> RetryOn { get; init; } = DefaultRetryOn;

    /// <summary>Default true.</summary>
    public bool RespectRetryAfter { get; init; } = true;

    /// <summary>Default true — only idempotent operations are retried by default (CFG-051).</summary>
    public bool RetryIdempotentOnly { get; init; } = true;

    private static IReadOnlyList<string> DefaultRetryOn { get; } =
    [
        ErrorCodes.TransportConnectionFailed,
        ErrorCodes.TransportTimeout,
        ErrorCodes.ServerUnavailable,
        ErrorCodes.ServerStandby,
    ];
}
