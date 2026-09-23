using System.Globalization;
using System.Security.Cryptography;

namespace BastionVault.IntegrationSdk.IntegrationTests.Harness;

/// <summary>
/// ITG-001: the shared server object every integration scenario is handed.
/// <para>
/// <see cref="RootToken"/> is a <see cref="SecretString"/> and not a <see cref="string"/> on
/// purpose: <see cref="SecretString.ToString"/> redacts, so a scenario that interpolates the
/// server into an assertion message cannot leak the root token into a test report (ITG-021,
/// TST-051). Reaching the value takes an explicit <c>Reveal()</c>.
/// </para>
/// </summary>
public sealed class TestServer
{
    internal TestServer(
        TestServerMode mode,
        string address,
        SecretString rootToken,
        string? caCertPem,
        string version,
        string? @namespace,
        bool tlsSkipVerify,
        IReadOnlyList<SecretString> unsealKeys,
        string runId)
    {
        Mode = mode;
        Address = address;
        RootToken = rootToken;
        CaCertPem = caCertPem;
        Version = version;
        Namespace = @namespace;
        TlsSkipVerify = tlsSkipVerify;
        UnsealKeys = unsealKeys;
        RunId = runId;
    }

    /// <summary>The address the SDK connects to, e.g. <c>http://127.0.0.1:53211</c>.</summary>
    public string Address { get; }

    /// <summary>The administrative token every scenario starts from.</summary>
    public SecretString RootToken { get; }

    /// <summary>The CA bundle to trust, when the server presents a certificate the harness owns.</summary>
    public string? CaCertPem { get; }

    /// <summary>The version reported by <c>sys/info</c> (ITG-002).</summary>
    public string Version { get; }

    /// <summary>Which row of the mode table provisioned this server.</summary>
    public TestServerMode Mode { get; }

    /// <summary><c>BASTIONVAULT_TEST_NAMESPACE</c>, when tenant-scoped scenarios are requested.</summary>
    public string? Namespace { get; }

    /// <summary>
    /// ITG-005. True only when the operator asked for it: managed mode never sets it, because the
    /// managed server is plain HTTP on loopback (allowed by CNF-035).
    /// </summary>
    public bool TlsSkipVerify { get; }

    /// <summary>
    /// Unseal keys, when this run owns them (managed mode, or <c>BASTIONVAULT_TEST_UNSEAL_KEYS</c>).
    /// Empty means the seal/unseal scenarios must skip rather than seal a server they cannot reopen.
    /// </summary>
    public IReadOnlyList<SecretString> UnsealKeys { get; }

    /// <summary>
    /// ITG-010's <c>&lt;run-id&gt;</c>. Time-ordered by construction: the first field is the start
    /// instant in base-36 seconds, which is what lets <see cref="OrphanCleaner"/> date a leftover
    /// mount from its path alone (ITG-013).
    /// </summary>
    public string RunId { get; }

    /// <summary>The parsed <see cref="Version"/>, or <see langword="null"/> if it is not a version.</summary>
    internal ServerVersion? ParsedVersion => ServerVersion.TryParse(Version, out ServerVersion? v) ? v : null;

    internal static string NewRunId()
    {
        long seconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return string.Create(CultureInfo.InvariantCulture, $"{ToBase36(seconds)}{Suffix()}");

        static string Suffix() => RandomNumberGenerator.GetHexString(4, lowercase: true);
    }

    internal static long? EpochFromRunId(string runId)
    {
        // A run id is <base36 seconds><4 hex>. Everything but the last four characters is the clock.
        if (runId.Length <= 4)
        {
            return null;
        }

        return FromBase36(runId[..^4]);
    }

    private static string ToBase36(long value)
    {
        const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
        if (value <= 0)
        {
            return "0";
        }

        Stack<char> buffer = new Stack<char>();
        while (value > 0)
        {
            buffer.Push(Alphabet[(int)(value % 36)]);
            value /= 36;
        }

        return new string([.. buffer]);
    }

    private static long? FromBase36(string text)
    {
        long value = 0;
        foreach (char c in text)
        {
            int digit = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'z' => c - 'a' + 10,
                _ => -1,
            };

            if (digit < 0)
            {
                return null;
            }

            value = (value * 36) + digit;
        }

        return value;
    }

    /// <summary>
    /// DR-0021 F9: every client built here shares one paced transport unless <paramref
    /// name="configure"/> sets its own, so the shared server's abuse guard sees one metered stream
    /// regardless of how many clients a scenario builds (login, child tokens, revocation - see
    /// <see cref="AbuseGuardPacer"/>). Set once, after provisioning, by <c>IntegrationHarness</c>.
    /// </summary>
    internal ITransport? SharedTransport { get; set; }

    /// <summary>
    /// Builds a client against this server. Every scenario gets its own client rather than sharing
    /// one, because several ITG-S scenarios swap the token (login, child tokens, revocation) and a
    /// shared client would make that shared mutable state across parallel tests (ITG-012). The
    /// underlying transport is shared regardless - see <see cref="SharedTransport"/>.
    /// </summary>
    public BastionVaultClient CreateClient(Action<BastionVaultClientOptions>? configure = null)
    {
        BastionVaultClientOptions options = new BastionVaultClientOptions
        {
            Address = Address,
            Token = RootToken.Value("root token"),
            ClusterDiscovery = false,
            Timeout = TimeSpan.FromSeconds(30),
        };

        if (CaCertPem is not null)
        {
            options.CaCertPem = CaCertPem;
        }

        if (TlsSkipVerify)
        {
            options.TlsSkipVerify = true;
        }

        if (Namespace is not null)
        {
            options.Namespace = Namespace;
        }

        configure?.Invoke(options);
        options.Transport ??= SharedTransport;
        return new BastionVaultClient(options);
    }

    /// <summary>Redacting by construction: no member printed here carries secret material.</summary>
    public override string ToString()
    {
        return $"TestServer(mode={Mode}, address={Address}, version={Version}, run={RunId})";
    }
}
