namespace BastionVault.IntegrationSdk;

/// <summary>
/// Retry policy configuration (CFG-050). Only the defaults land at milestone M1a; retry
/// <em>execution</em> (CFG-051..055) is M1b (<c>decisions/0003-m1a-configuration.md</c>, D-M1a-10).
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

    /// <summary>Default true.</summary>
    public bool RespectRetryAfter { get; init; } = true;

    /// <summary>Default true — only idempotent operations are retried by default (CFG-051, M1b).</summary>
    public bool RetryIdempotentOnly { get; init; } = true;
}
