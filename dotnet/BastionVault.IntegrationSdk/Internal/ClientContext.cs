namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// State shared by a <see cref="BastionVaultClient"/> and every view returned by
/// <see cref="BastionVaultClient.WithNamespace"/> (D-M1b-9): the transport, the token cell, the
/// injected seams, and the rate-gate pause state. Only the active namespace differs between a
/// client and its views.
/// </summary>
internal sealed class ClientContext
{
    /// <summary>
    /// AUT-001's "exactly one <see cref="TokenSource"/>" cell, shared with every
    /// <see cref="BastionVaultClient.WithNamespace"/> view (CFG-071).
    /// </summary>
    /// <remarks>
    /// <para>
    /// CFG-070's thread-safety now rests on two facts rather than one. The <i>write</i> is still a
    /// single reference assignment, which is atomic in .NET, so <see cref="SetToken"/> needs no
    /// lock; <c>volatile</c> is what makes the new value visible to a reader on another core
    /// rather than merely eventually. The <i>read</i> is no longer the whole story, because
    /// D-M2-9 made resolution asynchronous and side-effecting: the load-bearing rule is that each
    /// pass resolves exactly once, above the retry loop (D-M1b-9), which is what "in-flight
    /// requests keep the token they started with" means, and that a concurrent first-use
    /// resolution of a <see cref="TokenSourceKind.Login"/> source is single-flighted inside
    /// <see cref="TokenSource"/> (D-M2-11(a)).
    /// </para>
    /// <para>
    /// The M1a comment this replaces — "a reference assignment/read is already atomic in .NET; no
    /// lock is needed" — was CFG-070's entire justification, and an async cache-filling resolve
    /// invalidated it. That is why D-M2-11(c) re-opened CFG-070 onto the traceability baseline and
    /// gave it to M2a.
    /// </para>
    /// </remarks>
    private volatile TokenSource tokenSource;

    /// <summary>
    /// The most recent value <see cref="ResolveTokenAsync"/> produced (or the configured token,
    /// before the first resolution), which is what AUT-004's <c>Auth.CurrentToken</c> reads.
    /// Separate from <see cref="tokenSource"/> because reading the current token must never
    /// <i>perform</i> a resolution: a <see cref="TokenSourceKind.Callback"/> source would
    /// otherwise call into the application every time a property was inspected.
    /// </summary>
    private volatile SecretString? lastResolved;

    private volatile TokenInfo? tokenInfo;

    public ClientContext(
        ClientConfig config,
        ITransport? transport,
        SecretString initialToken,
        IClock clock,
        IJitterSource jitterSource,
        IRequestObserver? observer,
        IClientLogger logger,
        TokenSource? explicitSource = null)
    {
        Config = config;
        Transport = transport;
        // AUT-001: exactly one source. An application-supplied one is it; otherwise the resolved
        // token becomes a Static source, which is byte-for-byte the pre-M2a behaviour.
        tokenSource = explicitSource ?? TokenSource.Static(initialToken);
        // A Static source's token is already known, so Auth.CurrentToken can report it without
        // resolving. Any other source has resolved nothing yet, and reading CurrentToken must not
        // be what triggers the first resolution (AUT-004).
        lastResolved = explicitSource is null ? initialToken : null;
        Clock = clock;
        JitterSource = jitterSource;
        Observer = observer;
        Logger = logger;
    }

    public ClientConfig Config { get; }

    public ITransport? Transport { get; }

    public IClock Clock { get; }

    public IJitterSource JitterSource { get; }

    public IRequestObserver? Observer { get; }

    /// <summary>The CNF-030 logging seam, defaulted to <see cref="NoOpClientLogger"/>. ERR-050 logs server warnings through it.</summary>
    public IClientLogger Logger { get; }

    public RateGateStateHolder RateGate { get; } = new();

    /// <summary>AUT-001's single source, and AUT-004's <c>Auth.TokenSource</c>.</summary>
    public TokenSource TokenSource => tokenSource;

    /// <summary>
    /// D-M2-9's resolution call: what replaced <c>GetToken()</c>'s field read. The executor calls
    /// this once per pass, above the retry loop, and threads the result through every attempt of
    /// that pass (D-M1b-9, CFG-070).
    /// </summary>
    public async Task<SecretString?> ResolveTokenAsync(CancellationToken cancellationToken)
    {
        SecretString? resolved = await tokenSource.ResolveAsync(cancellationToken).ConfigureAwait(false);
        lastResolved = resolved;
        return resolved;
    }

    /// <summary>
    /// AUT-004's <c>Auth.CurrentToken</c>: the token the client currently holds, or
    /// <see langword="null"/> when it holds none. Never resolves, so reading it can neither log in
    /// nor call an application callback.
    /// </summary>
    public SecretString? CurrentToken => lastResolved is { HasValue: true } resolved ? resolved : null;

    /// <summary>AUT-004's <c>Auth.TokenInfo</c>: the most recent <c>LookupSelf</c> result, if any.</summary>
    public TokenInfo? TokenInfo => tokenInfo;

    /// <summary>Records a <c>LookupSelf</c> result for <see cref="TokenInfo"/> (AUT-004).</summary>
    public void SetTokenInfo(TokenInfo value) => tokenInfo = value;

    /// <summary>
    /// AUT-001: a token write replaces the source with a <see cref="TokenSourceKind.Static"/> one.
    /// Thread-safe (CFG-070): one reference assignment to a <c>volatile</c> field, and in-flight
    /// requests are unaffected because each pass already resolved its own snapshot.
    /// </summary>
    public void SetToken(SecretString value)
    {
        tokenSource = TokenSource.Static(value);
        lastResolved = value;
    }
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
