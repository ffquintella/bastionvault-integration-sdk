using System.Collections.Concurrent;

namespace BastionVault.IntegrationSdk.IntegrationTests.Harness;

/// <summary>
/// The whole-run <see cref="IClientLogger"/> ITG-021 scans. One instance is shared by every
/// client the harness creates (<see cref="RunCapture"/>), because ITG-021 is "no secret material
/// appeared in the captured SDK logs at debug level" over the <b>whole run</b>, not per scenario.
/// </summary>
public sealed class LogCapture : IClientLogger
{
    private readonly ConcurrentQueue<string> lines = new();

    /// <inheritdoc/>
    public void Info(string message)
    {
        lines.Enqueue(message);
    }

    /// <inheritdoc/>
    public void Warn(string message)
    {
        lines.Enqueue(message);
    }

    /// <summary>Every line captured so far, in the order it arrived.</summary>
    public IReadOnlyList<string> Lines => [.. lines];
}
