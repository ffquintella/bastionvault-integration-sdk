namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// State shared by a <see cref="BastionVaultClient"/> and every view returned by
/// <see cref="BastionVaultClient.WithNamespace"/> (D-M1b-9): the transport, the token cell, the
/// injected seams, and the rate-gate pause state. Only the active namespace differs between a
/// client and its views.
/// </summary>
internal sealed class ClientContext
{
    private SecretString token;

    public ClientContext(
        ClientConfig config,
        ITransport? transport,
        SecretString initialToken,
        IClock clock,
        IJitterSource jitterSource,
        IRequestObserver? observer)
    {
        Config = config;
        Transport = transport;
        token = initialToken;
        Clock = clock;
        JitterSource = jitterSource;
        Observer = observer;
    }

    public ClientConfig Config { get; }

    public ITransport? Transport { get; }

    public IClock Clock { get; }

    public IJitterSource JitterSource { get; }

    public IRequestObserver? Observer { get; }

    public RateGateStateHolder RateGate { get; } = new();

    /// <summary>Thread-safe token read/write (CFG-070). A reference assignment/read is already atomic in .NET; no lock is needed.</summary>
    public SecretString GetToken() => token;

    /// <summary>Thread-safe token write (CFG-070).</summary>
    public void SetToken(SecretString value) => token = value;
}

/// <summary>The mutable, thread-safe backing store for <see cref="RateGateState"/> (D-M1b-16).</summary>
internal sealed class RateGateStateHolder
{
    private readonly object gate = new();
    private bool paused;
    private DateTimeOffset? pausedUntil;

    public RateGateState Snapshot()
    {
        lock (gate)
        {
            return new RateGateState(paused, pausedUntil);
        }
    }

    public void Pause(DateTimeOffset until)
    {
        lock (gate)
        {
            paused = true;
            pausedUntil = until;
        }
    }
}
