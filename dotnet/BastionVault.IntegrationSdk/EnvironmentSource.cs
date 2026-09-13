namespace BastionVault.IntegrationSdk;

/// <summary>
/// The seam configuration resolution reads instead of calling the process environment directly
/// (<c>decisions/0003-m1a-configuration.md</c>, D-M1a-3). Serves three needs with one type: the
/// real process environment (<see cref="Process"/>, the default), an environment-free constructor
/// (<see cref="None"/>, CFG-005), and deterministic, parallel-safe test injection (<see cref="FromMap"/>,
/// OVR-004 — no test using this seam ever mutates process state).
/// </summary>
public abstract class EnvironmentSource
{
    private protected EnvironmentSource()
    {
    }

    /// <summary>Reads the real process environment. The default for <see cref="BastionVaultClient"/>'s primary constructor.</summary>
    public static EnvironmentSource Process { get; } = new ProcessEnvironmentSource();

    /// <summary>Reads nothing; only explicit values and built-in defaults apply. This is CFG-005's environment-free form.</summary>
    public static EnvironmentSource None { get; } = new MapEnvironmentSource(new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>Reads from a caller-supplied key/value map, read once at the time this call is made.</summary>
    public static EnvironmentSource FromMap(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new MapEnvironmentSource(new Dictionary<string, string>(values, StringComparer.Ordinal));
    }

    /// <summary>Attempts to read the named environment variable from this source.</summary>
    public abstract bool TryGetValue(string name, out string? value);

    private sealed class ProcessEnvironmentSource : EnvironmentSource
    {
        public override bool TryGetValue(string name, out string? value)
        {
            value = Environment.GetEnvironmentVariable(name);
            return value is not null;
        }
    }

    private sealed class MapEnvironmentSource : EnvironmentSource
    {
        private readonly IReadOnlyDictionary<string, string> values;

        public MapEnvironmentSource(IReadOnlyDictionary<string, string> values)
        {
            this.values = values;
        }

        public override bool TryGetValue(string name, out string? value)
        {
            if (values.TryGetValue(name, out string? found))
            {
                value = found;
                return true;
            }

            value = null;
            return false;
        }
    }
}
