using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// The BastionVault client: holds a resolved <see cref="ClientConfig"/> and a transport, and exposes
/// <see cref="IsInsecure"/> (CFG-018), the <see cref="Logical"/> operations (TRN-001) and the runtime
/// mutation surface (<see cref="SetToken"/>, <see cref="ClearToken"/>, <see cref="WithNamespace"/>,
/// CFG-070/071). Deliberately has no <c>SetAddress</c> method (CFG-072) — changing the server
/// requires constructing a new client.
/// </summary>
public sealed class BastionVaultClient
{
    private readonly ClientContext context;
    private readonly string namespaceOverride;

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

        if (Transport is { SupportsCustomVerbs: false })
        {
            // D-M1b-14 / TRN-010: proved by a fake transport declaring SupportsCustomVerbs == false.
            throw BastionVaultException.Config(
                ErrorCodes.ConfigListVerbUnsupported,
                "The HTTP stack cannot send the custom `LIST` method.",
                "Use the SDK's default transport or an HTTP client that allows non-standard methods; the server does not support `?list=true`.");
        }

        context = new ClientContext(
            Config,
            Transport,
            Config.Token,
            effectiveOptions.Clock ?? SystemClock.Instance,
            effectiveOptions.JitterSource ?? SystemJitterSource.Instance,
            effectiveOptions.Observer);
        namespaceOverride = Config.Namespace;
    }

    private BastionVaultClient(ClientContext context, string namespaceOverride)
    {
        this.context = context;
        this.namespaceOverride = namespaceOverride;
        Config = context.Config;
        Transport = context.Transport;
        IsInsecure = Config.IsInsecure;
    }

    /// <summary>The fully resolved, immutable configuration this client was constructed with.</summary>
    public ClientConfig Config { get; }

    /// <summary>The transport this client sends requests through (OVR-001), or <see langword="null"/> when none was supplied.</summary>
    public ITransport? Transport { get; }

    /// <summary>True when certificate verification is disabled (CFG-018); a CNF-030 warning was already logged.</summary>
    public bool IsInsecure { get; }

    /// <summary>The active namespace for this client or view (differs from <see cref="Config"/>'s only after <see cref="WithNamespace"/>).</summary>
    public string Namespace => namespaceOverride;

    /// <summary>The four logical primitives and the <c>Raw</c> escape hatch (TRN-001).</summary>
    public LogicalOperations Logical => new(context, namespaceOverride);

    /// <summary>The observable client-side rate-gate pause state (D-M1b-16).</summary>
    public RateGateState RateGateState => context.RateGate.Snapshot();

    /// <summary>
    /// Replaces the token used by this client and every view sharing its token cell (CFG-070).
    /// Thread-safe; in-flight requests keep the token they started with.
    /// </summary>
    public void SetToken(SecretString token)
    {
        ArgumentNullException.ThrowIfNull(token);
        context.SetToken(token);
    }

    /// <summary>Clears the token used by this client and every view sharing its token cell (CFG-070).</summary>
    public void ClearToken() => context.SetToken(SecretString.Empty);

    /// <summary>
    /// Returns a lightweight view sharing the transport, the configuration and the same token cell —
    /// a <see cref="SetToken"/> on this client is visible to the view (CFG-071) — differing only in
    /// namespace.
    /// </summary>
    public BastionVaultClient WithNamespace(string ns)
    {
        ArgumentNullException.ThrowIfNull(ns);
        return new BastionVaultClient(context, ns.TrimEnd('/'));
    }
}
