namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// The exponential-backoff curve, shared rather than reimplemented. <see cref="RequestExecutor"/>'s
/// transport-level retry path (D-M1b-7) and <see cref="CacheWatcher"/>'s watch loop (CCH-006) both
/// back off against the client's configured <see cref="RetryPolicy"/>; CCH-006 names no curve of
/// its own, so the client's is the right source, and two implementations that could disagree is
/// the defect this promotion exists to avoid.
/// </summary>
internal static class BackoffCalculator
{
    /// <summary>
    /// <paramref name="attempt"/> is 1-based: the first failure computes
    /// <see cref="RetryPolicy.InitialBackoff"/> itself (multiplier raised to the zeroth power),
    /// doubling (by default) from there and capped at <see cref="RetryPolicy.MaxBackoff"/>, with
    /// <paramref name="jitter"/> applied per <see cref="RetryPolicy.Jitter"/>.
    /// </summary>
    public static TimeSpan Compute(RetryPolicy policy, int attempt, IJitterSource jitter)
    {
        double raw = policy.InitialBackoff.TotalMilliseconds * Math.Pow(policy.BackoffMultiplier, attempt - 1);
        double capped = Math.Min(raw, policy.MaxBackoff.TotalMilliseconds);
        double jitterFactor = 1.0 + ((jitter.NextDouble() * 2 - 1) * policy.Jitter);
        double final = Math.Max(0, capped * jitterFactor);
        return TimeSpan.FromMilliseconds(final);
    }
}
