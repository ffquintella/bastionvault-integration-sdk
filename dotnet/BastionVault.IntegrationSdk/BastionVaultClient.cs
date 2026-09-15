using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// The BastionVault client: holds a resolved <see cref="ClientConfig"/> and a transport, and exposes
/// <see cref="IsInsecure"/> (CFG-018), the <see cref="Logical"/> operations (TRN-001) and the runtime
/// mutation surface (<see cref="SetToken"/>, <see cref="ClearToken"/>, <see cref="WithNamespace"/>,
/// CFG-070/071). Deliberately has no <c>SetAddress</c> method (CFG-072) — changing the server
/// requires constructing a new client.
/// </summary>
public sealed class BastionVaultClient : IDisposable
{
    private readonly ClientContext context;
    private readonly string namespaceOverride;

    /// <summary>
    /// AUT-094's cancellation: the token the renewal loop runs under, cancelled by
    /// <see cref="Dispose"/>. Null on a client that started no loop, and on every
    /// <see cref="WithNamespace"/> view, which owns no background work of its own.
    /// </summary>
    private readonly CancellationTokenSource? renewalCancellation;

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
            effectiveOptions.Observer,
            effectiveOptions.Logger ?? NoOpClientLogger.Instance,
            effectiveOptions.TokenSource);
        namespaceOverride = Config.Namespace;

        if (Config.AutoRenew.Enabled)
        {
            // AUT-094: "the runtime's background primitive". .NET's counterpart to the
            // requirement's `tokio::spawn` and `asyncio.Task` is a thread-pool task, which is also
            // what a hosted service would ultimately start; the loop itself is an awaitable
            // RunAsync (D-M2-27 item 7), so hosting it from IHostedService instead costs the
            // application one adapter and this library no dependency.
            renewalCancellation = new CancellationTokenSource();
            TokenRenewal renewal = new(context, Config.AutoRenew);
            CancellationToken cancellationToken = renewalCancellation.Token;
            RenewalCompletion = Task.Run(() => renewal.RunAsync(cancellationToken), CancellationToken.None);
        }
    }

    private BastionVaultClient(ClientContext context, string namespaceOverride)
    {
        this.context = context;
        this.namespaceOverride = namespaceOverride;
        Config = context.Config;
        Transport = context.Transport;
        IsInsecure = Config.IsInsecure;
    }

    /// <summary>
    /// The state this client shares with its <see cref="WithNamespace"/> views. Internal, and
    /// visible to the test assembly only so D-M2-9's inbound <c>requestId</c> and accumulated
    /// attempt count can be driven directly — the AUT-003 replay that will supply them in
    /// production is M2b's, and the accounting must not go untested until then.
    /// </summary>
    internal ClientContext Context => context;

    /// <summary>
    /// AUT-094's loop, as a task. Internal, and visible to the test assembly only so a fixture can
    /// <b>await</b> the loop instead of racing it: the loop is deliberately fire-and-forget for an
    /// application, which has <see cref="AutoRenewPolicy.OnStopped"/> to learn that it ended.
    /// </summary>
    internal Task? RenewalCompletion { get; }

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

    /// <summary>
    /// The <c>Auth</c> area (OVR-008): the token source (AUT-001), the current credential
    /// (AUT-004), the token store (AUT-020, AUT-080…AUT-085) and the token-helper write path
    /// (CFG-031, CFG-032).
    /// </summary>
    public AuthOperations Auth => new(context, namespaceOverride);

    /// <summary>
    /// The Core <c>sys</c> surface (OVR-008, D-M3-1): health and status (SYS-001, SYS-002, SYS-005,
    /// SYS-006, SYS-008) and self capability introspection (SYS-050…SYS-053).
    /// </summary>
    public SysOperations Sys => new(context, namespaceOverride);

    /// <summary>The observable client-side rate-gate pause state (D-M1b-16).</summary>
    public RateGateState RateGateState => context.RateGate.Snapshot();

    /// <summary>
    /// Replaces the token used by this client and every view sharing its token cell (CFG-070),
    /// which per AUT-001 means replacing its <see cref="AuthOperations.TokenSource"/> with a
    /// <see cref="TokenSourceKind.Static"/> one. Thread-safe; in-flight requests keep the token
    /// they started with, because each pass resolved its own snapshot before entering the retry
    /// loop (D-M1b-9).
    /// </summary>
    public void SetToken(SecretString token)
    {
        ArgumentNullException.ThrowIfNull(token);
        context.SetToken(token);
    }

    /// <summary>Clears the token used by this client and every view sharing its token cell (CFG-070).</summary>
    public void ClearToken()
    {
        context.SetToken(SecretString.Empty);
    }

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

    /// <summary>
    /// AUT-094: stops this client's automatic renewal loop. Idempotent, and safe to call from
    /// inside a renewal callback.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately does <b>not</b> wait for the loop to unwind. Dispose is reachable from a
    /// renewal callback, and a Dispose that awaited the loop would then be waiting on the thread
    /// it is running on. The loop observes the cancellation and emits
    /// <see cref="AutoRenewPolicy.OnStopped"/> with
    /// <see cref="RenewalStoppedReason.Disposed"/>, which is how an application learns it has
    /// finished.
    /// </para>
    /// <para>
    /// It also does not dispose the transport: the application supplied it (OVR-001), may share it
    /// between clients, and CFG-072's "construct a new client" would otherwise tear down a
    /// connection pool the caller still owns. A <see cref="WithNamespace"/> view owns no loop and
    /// no transport, so disposing one is a no-op and leaves its parent's renewal running.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        // Cancel-then-dispose, guarded: a second Dispose must not surface an
        // ObjectDisposedException to an application that is merely shutting down twice.
        try
        {
            renewalCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed; the loop has already been told to stop.
        }

        renewalCancellation?.Dispose();
    }
}
