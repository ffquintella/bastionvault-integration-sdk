using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// The minimal client that lands at milestone M1a: holds a resolved <see cref="ClientConfig"/> and a
/// transport, and exposes <see cref="IsInsecure"/> (CFG-018). It has no operations
/// (<c>decisions/0003-m1a-configuration.md</c>, D-M1a-6) and, deliberately, no <c>SetAddress</c>
/// method (CFG-072) — changing the server requires constructing a new client.
/// </summary>
public sealed class BastionVaultClient
{
    /// <summary>
    /// Constructs a client, reading the real process environment for any setting not given
    /// explicitly in <paramref name="options"/> (CFG-001, CFG-002). Use the
    /// <see cref="BastionVaultClient(BastionVaultClientOptions?, EnvironmentSource)"/> overload with
    /// <see cref="EnvironmentSource.None"/> for CFG-005's environment-free construction.
    /// </summary>
    public BastionVaultClient(BastionVaultClientOptions? options = null)
        : this(options, EnvironmentSource.Process)
    {
    }

    /// <summary>Constructs a client, resolving settings against the given <paramref name="environmentSource"/>.</summary>
    public BastionVaultClient(BastionVaultClientOptions? options, EnvironmentSource environmentSource)
    {
        ArgumentNullException.ThrowIfNull(environmentSource);
        BastionVaultClientOptions effectiveOptions = options ?? new BastionVaultClientOptions();
        Config = ConfigurationResolver.Resolve(effectiveOptions, environmentSource);
        Transport = effectiveOptions.Transport;
        IsInsecure = Config.IsInsecure;
    }

    /// <summary>The fully resolved, immutable configuration this client was constructed with.</summary>
    public ClientConfig Config { get; }

    /// <summary>The transport this client sends requests through (OVR-001), or <see langword="null"/> when none was supplied.</summary>
    public ITransport? Transport { get; }

    /// <summary>True when certificate verification is disabled (CFG-018); a CNF-030 warning was already logged.</summary>
    public bool IsInsecure { get; }
}
