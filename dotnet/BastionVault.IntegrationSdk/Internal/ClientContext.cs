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

    /// <summary>
    /// The <see cref="AuthInfo"/> of the most recent login this client performed, which is what
    /// <c>Auth.AuthenticateAsync</c> hands back after forcing AUT-002's lazy login eagerly.
    /// </summary>
    private volatile AuthInfo? lastLogin;

    /// <summary>
    /// AUT-003's serialisation point. The re-login decision reads two fields and writes one, and a
    /// <c>403</c> storm hits it from N threads at once, so the whole decision is taken under this
    /// lock rather than assembled from three volatile reads.
    /// </summary>
    private readonly object reloginGate = new();

    /// <summary>
    /// When the token the client currently holds was issued (AUT-003's "older than
    /// <c>MinReloginInterval</c>"), from the injected clock. Null until a login has happened: a
    /// configured or <c>SetToken</c> token has no issue time the SDK observed, and guessing one
    /// would make AUT-003 fire on a token whose age it does not know.
    /// </summary>
    private DateTimeOffset? tokenIssuedAt;

    /// <summary>
    /// When the last AUT-003 re-login was <i>started</i>. Separate from
    /// <see cref="tokenIssuedAt"/> so a re-login that itself fails still counts against the
    /// interval — otherwise every request in a 403 storm would start its own login.
    /// </summary>
    private DateTimeOffset? reloginStartedAt;

    /// <summary>
    /// AUT-090's starting point: completes with the first credential this client observed being
    /// <i>issued</i> (a login, whether AUT-002's lazy one or a one-shot <c>Auth.*.Login</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The renewal loop needs three facts AUT-090 names — <c>Renewable</c>, <c>LeaseDuration</c>
    /// and <c>IssuedAt</c> — and only a login produces them. Waiting on this rather than forcing a
    /// login keeps D-M2-9's ruling intact: enabling <c>AutoRenew</c> does not turn AUT-002's
    /// documented <b>lazy</b> login into an eager one.
    /// </para>
    /// <para>
    /// <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/> is load-bearing, not
    /// decoration: without it the loop's whole first scheduling pass would run <i>inline</i> on the
    /// thread that is still inside <see cref="RecordLogin"/>, i.e. inside the login's own response
    /// mapping, and would re-enter the transport from there.
    /// </para>
    /// </remarks>
    private readonly TaskCompletionSource<AuthInfo> credentialIssued =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ClientContext(
        ClientConfig config,
        ITransport? transport,
        SecretString initialToken,
        IClock clock,
        IJitterSource jitterSource,
        IRequestObserver? observer,
        IClientLogger logger,
        TokenSource? explicitSource = null,
        ISrvResolver? srvResolver = null)
    {
        Config = config;
        Transport = transport;
        Clock = clock;
        // AUT-001: exactly one source. An application-supplied one is it; otherwise the resolved
        // token becomes a Static source, which is byte-for-byte the pre-M2a behaviour.
        //
        // A declarative `TokenSource.Login(...)` is bound to its performer here, which is the only
        // moment a client and a source both exist. The bound twin is the client's one source
        // (D-M2-6's `Auth.TokenSource`); the application's own instance stays inert.
        tokenSource = explicitSource is { Descriptor: { } descriptor }
            ? explicitSource.BindTo(token => PerformLoginAsync(descriptor, token))
            : explicitSource ?? TokenSource.Static(initialToken);
        // A Static source's token is already known, so Auth.CurrentToken can report it without
        // resolving. Any other source has resolved nothing yet, and reading CurrentToken must not
        // be what triggers the first resolution (AUT-004).
        lastResolved = explicitSource is null ? initialToken : null;
        JitterSource = jitterSource;
        Observer = observer;
        Logger = logger;
        // The classification the resolver already took (CFG-001's one resolution pass), not a
        // second call over the same inputs.
        Discovery = new DiscoveryEngine(this, config.Classification, srvResolver);
    }

    public ClientConfig Config { get; }

    /// <summary>
    /// Section 13's discovery pipeline and the pin it produces (DSC-035). Shared with every
    /// <see cref="BastionVaultClient.WithNamespace"/> view, like the token cell.
    /// </summary>
    public DiscoveryEngine Discovery { get; }

    /// <summary>
    /// D-M5-11's per-attempt endpoint read. In literal mode this is
    /// <see cref="ClientConfig.Address"/> verbatim and never changes, which is what keeps literal
    /// request URIs byte-identical to M1b's.
    /// </summary>
    public string Endpoint => Discovery.Endpoint;

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
    public void SetTokenInfo(TokenInfo value)
    {
        tokenInfo = value;
    }

    /// <summary>The most recent login's result, for AUT-002's eager <c>Auth.AuthenticateAsync</c>.</summary>
    public AuthInfo? LastLogin => lastLogin;

    /// <summary>
    /// AUT-090: the first credential this client observed being issued. Already completed when a
    /// login has happened; otherwise completes when one does.
    /// </summary>
    public Task<AuthInfo> CredentialIssued => credentialIssued.Task;

    /// <summary>
    /// AUT-092's "on <c>RevokeSelf</c>/<c>ClearToken</c>": raised when the client's token is
    /// replaced by an empty one, so a sleeping renewal loop stops at once rather than at its next
    /// scheduled wake.
    /// </summary>
    public event Action? TokenCleared;

    /// <summary>
    /// AUT-013: records a successful login. <paramref name="install"/> distinguishes a one-shot
    /// <c>Auth.*.Login</c> call, which replaces the source with a
    /// <see cref="TokenSourceKind.Static"/> one and so drops the credentials (AUT-100), from a
    /// <see cref="TokenSourceKind.Login"/> source resolving itself, whose token is published by
    /// <see cref="ResolveTokenAsync"/> and whose source must survive.
    /// </summary>
    public void RecordLogin(AuthInfo auth, bool install)
    {
        lastLogin = auth;
        lock (reloginGate)
        {
            tokenIssuedAt = auth.IssuedAt;
            // The interval is measured from the token's issue time from here on, so a re-login
            // that succeeded stops also counting as a re-login that was *started*.
            reloginStartedAt = null;
        }

        if (install)
        {
            SetToken(auth.ClientToken);
        }
        else
        {
            lastResolved = auth.ClientToken;
        }

        // Published after the token is installed, so a renewal loop released by this call already
        // sees the credential it is about to renew (AUT-090).
        _ = credentialIssued.TrySetResult(auth);
    }

    /// <summary>
    /// AUT-003's gate: whether this caller may re-login once and replay. Returns
    /// <see langword="true"/> for at most one caller per <paramref name="minReloginInterval"/>, and
    /// invalidates the source's cached token so the next resolution logs in again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A caller that loses the race does <b>not</b> replay. AUT-003 is a <c>MAY</c>, so declining
    /// is conformant, and the alternative — every one of N concurrent <c>403</c>s replaying behind
    /// one re-login — doubles the request count of a permission failure that may well still be a
    /// permission failure. The winner's replay is the probe; if the re-login fixed the problem the
    /// losers' own next call succeeds without a second round trip.
    /// </para>
    /// <para>
    /// A token with no recorded issue time (configured, <c>SetToken</c>, or a
    /// <see cref="TokenSourceKind.Callback"/> result) is never re-logged-in: "older than
    /// <c>MinReloginInterval</c>" is a question about a token the SDK issued, and answering it for
    /// one it did not would be a guess (D-M1c-25).
    /// </para>
    /// </remarks>
    public bool TryBeginRelogin(TimeSpan minReloginInterval)
    {
        lock (reloginGate)
        {
            DateTimeOffset now = Clock.NowUtc();
            DateTimeOffset? reference = reloginStartedAt ?? tokenIssuedAt;
            if (reference is not { } since || now - since < minReloginInterval)
            {
                return false;
            }

            reloginStartedAt = now;
            tokenSource.Invalidate();
            return true;
        }
    }

    /// <summary>
    /// The performer bound to a declarative <see cref="TokenSource.Login"/> source: one login,
    /// returning the token the single-flight cell caches (D-M2-11(a)).
    /// </summary>
    /// <remarks>
    /// Runs at the client's own namespace and with no per-request options: the source is the
    /// client's, not one call's, and AUT-041's namespace header therefore comes from
    /// <c>Config.Namespace</c>. <c>install: false</c>, because replacing the source mid-resolution
    /// would discard the source performing the resolution.
    /// </remarks>
    private async Task<SecretString> PerformLoginAsync(LoginDescriptor descriptor, CancellationToken cancellationToken)
    {
        LoginRunner runner = new(this, Config.Namespace);
        AuthInfo auth = await runner
            .LoginAsync(descriptor.Credentials, install: false, options: null, cancellationToken)
            .ConfigureAwait(false);
        return auth.ClientToken;
    }

    /// <summary>
    /// AUT-001: a token write replaces the source with a <see cref="TokenSourceKind.Static"/> one.
    /// Thread-safe (CFG-070): one reference assignment to a <c>volatile</c> field, and in-flight
    /// requests are unaffected because each pass already resolved its own snapshot.
    /// </summary>
    public void SetToken(SecretString value)
    {
        tokenSource = TokenSource.Static(value);
        lastResolved = value;
        if (!value.HasValue)
        {
            // AUT-083's RevokeSelf and CFG-070's ClearToken both land here with an empty token,
            // and AUT-092 requires a running renewal loop to stop on either.
            TokenCleared?.Invoke();
        }
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
