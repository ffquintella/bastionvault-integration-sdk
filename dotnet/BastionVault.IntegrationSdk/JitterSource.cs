using System.Diagnostics.CodeAnalysis;

namespace BastionVault.IntegrationSdk;

/// <summary>The injected randomness seam for retry jitter (D-M1b-7, RES-003).</summary>
public interface IJitterSource
{
    /// <summary>Returns a value in <c>[0.0, 1.0)</c>.</summary>
    double NextDouble();
}

/// <summary>The default <see cref="IJitterSource"/>: a thread-safe wrapper over <see cref="Random.Shared"/>.</summary>
public sealed class SystemJitterSource : IJitterSource
{
    /// <summary>The shared system jitter source.</summary>
    public static SystemJitterSource Instance { get; } = new();

    private SystemJitterSource()
    {
    }

    /// <inheritdoc/>
    [SuppressMessage(
        "Security",
        "CA5394:Do not use insecure randomness",
        Justification = "Retry-backoff jitter is not security-sensitive (RES-003); a CSPRNG buys nothing here.")]
    public double NextDouble() => Random.Shared.NextDouble();
}
