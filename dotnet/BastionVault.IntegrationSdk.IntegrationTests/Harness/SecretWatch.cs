using System.Collections.Concurrent;

namespace BastionVault.IntegrationSdk.IntegrationTests.Harness;

/// <summary>ITG-021's watchlist: every secret value the harness minted or was handed for the run.</summary>
/// <remarks>
/// Root token, unseal keys, and any token a scenario tracks via <see cref="ResourceLedger"/>.
/// </remarks>
internal sealed class SecretWatch
{
    private readonly ConcurrentDictionary<string, string> byValue = new(StringComparer.Ordinal);

    /// <summary>Registers <paramref name="secret"/> under <paramref name="label"/>. A no-op for an empty secret.</summary>
    public void Track(SecretString? secret, string label)
    {
        string? value = secret?.Reveal();
        if (!string.IsNullOrEmpty(value))
        {
            byValue[value] = label;
        }
    }

    /// <summary>ITG-021: lines containing a tracked secret, named by label only - never the value.</summary>
    public IReadOnlyList<string> FindLeaks(IEnumerable<string> lines)
    {
        if (byValue.IsEmpty)
        {
            return [];
        }

        List<string> leaks = [];
        int index = 0;
        foreach (string line in lines)
        {
            foreach (KeyValuePair<string, string> tracked in byValue)
            {
                if (line.Contains(tracked.Key, StringComparison.Ordinal))
                {
                    leaks.Add($"captured log line {index}: contains tracked secret '{tracked.Value}'");
                    break;
                }
            }

            index++;
        }

        return leaks;
    }
}
