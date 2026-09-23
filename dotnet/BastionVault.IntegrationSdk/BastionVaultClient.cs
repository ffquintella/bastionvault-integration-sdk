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
    /// True when this client constructed its own <see cref="Transport"/> (DR-0020 D-2), because
    /// none was injected. Such a transport is disposed by <see cref="Dispose"/>; an injected one
    /// never is, since the caller may share it across clients (OVR-001).
    /// </summary>
    private readonly bool ownsTransport;

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

        // DR-0020 D-1: `02-client-configuration.md:37` makes HTTP the default transport, not merely
        // a test seam. Built only when the caller supplied none, from the ClientConfig just
        // resolved above, so it inherits every TLS/proxy/timeout setting that config carries.
        ownsTransport = effectiveOptions.Transport is null;
        Transport = effectiveOptions.Transport ?? new HttpClientTransport(Config);
        IsInsecure = Config.IsInsecure;

        if (Transport is { SupportsCustomVerbs: false })
        {
            // D-M1b-14 / TRN-010: proved by a fake transport declaring SupportsCustomVerbs == false.
            throw BastionVaultException.Config(
                ErrorCodes.ConfigListVerbUnsupported,
                "The HTTP stack cannot send the custom `LIST` method.",
                "Use the SDK's default transport or an HTTP client that allows non-standard methods; the server does not support `?list=true`.");
        }

        // DSC-050: an injected resolver always wins; when none was supplied the built-in default
        // resolver ships instead of leaving discovery permanently unable to run. Constructed here,
        // never earlier, so it only exists for a client that could actually use it, and reads
        // Config.Discovery's nameserver override (D-R16-6/D-R16-7) rather than a second setting.
        ISrvResolver srvResolver = effectiveOptions.SrvResolver ?? new DnsSrvResolver(Config.Discovery.Nameservers);

        context = new ClientContext(
            Config,
            Transport,
            Config.Token,
            effectiveOptions.Clock ?? SystemClock.Instance,
            effectiveOptions.JitterSource ?? SystemJitterSource.Instance,
            effectiveOptions.Observer,
            effectiveOptions.Logger ?? NoOpClientLogger.Instance,
            effectiveOptions.TokenSource,
            srvResolver);
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

        // A WithNamespace view owns no transport of its own (see Dispose's remarks); the parent
        // that created or received it is the sole owner, so ownsTransport stays false here.
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

    /// <summary>
    /// The <c>Kv</c> area (OVR-008, 07 — KV engine): the version-explicit <c>Kv.V1</c> and
    /// <c>Kv.V2</c> sub-clients (KV-002).
    /// </summary>
    public KvOperations Kv => new(context, namespaceOverride);

    /// <summary>
    /// SYS-080's identity self-service surface: the calling token's profile, default account, SSH
    /// security keys and namespace assignment. Every route is <c>/v2</c>-pinned (TRN-071).
    /// </summary>
    /// <remarks>
    /// Named <c>Identity</c> rather than <c>Sys.Identity</c> because that is the operation name
    /// <c>06-system-api.md</c> and Appendix A both write, and it is the cross-language contract
    /// Rust and Python transcribe (DR-0012 D-M7-27). <c>12-other-engines-and-identity.md</c>'s
    /// wider <c>Identity.*</c> surface is a later milestone's, and is additive to this class.
    /// </remarks>
    public IdentityOperations Identity => new(context, namespaceOverride);

    /// <summary>
    /// The Transit engine surface (08 — Transit engine): encryption, signing, HMAC and datakeys as
    /// a service. <c>mount</c> defaults to <c>"transit"</c>. The SDK performs no cryptography
    /// itself (00 §Purpose, §Non-goals) — every member base64-encodes, sends one request, and
    /// parses the response.
    /// </summary>
    public TransitOperations Transit => new(context, namespaceOverride);

    /// <summary>
    /// 11 — TOTP engine (OVR-008): key CRUD plus code generation and validation. The SDK
    /// generates and validates no codes itself (OVR-002); see <see cref="TotpOperations"/>.
    /// </summary>
    public TotpOperations Totp => new(context, namespaceOverride);

    /// <summary>
    /// 09 — PKI engine (OVR-008): roles, issuance, certificates and the CRL. <c>mount</c> defaults
    /// to <c>"pki"</c>. The SDK performs no cryptography and parses no certificate itself (00
    /// §Purpose, §Non-goals); every PEM field is the server's bytes, unmodified (PKI-001). CA
    /// lifecycle, managed keys, tidy, ACME and the two queues are later slices (D-M9-5).
    /// </summary>
    public PkiOperations Pki => new(context, namespaceOverride);

    /// <summary>
    /// 10 — SSH engine (OVR-008): CA configuration, roles, CA-mode signing and OTP-mode
    /// credentials. <c>mount</c> defaults to <c>"ssh"</c>. The SDK performs no cryptography (00
    /// §Purpose, §Non-goals, D-M9-1); every OpenSSH key and certificate line is the server's bytes,
    /// unmodified.
    /// </summary>
    public SshOperations Ssh => new(context, namespaceOverride);

    /// <summary>
    /// 10 — SSH broker (OVR-008): login-brokering policy across the global, type, asset-group and
    /// resource tiers, and the effective-policy resolution. Every route is <c>/v2</c>-pinned
    /// (SSB-001) against the fixed <c>ssh-broker</c> logical mount.
    /// </summary>
    public SshBrokerOperations SshBroker => new(context, namespaceOverride);

    /// <summary>
    /// 12 — Asset groups (OVR-008): the <c>resource-group/</c> mount's group CRUD, history,
    /// resource/secret lookups (IDN-001's base64url treatment for the latter) and reindex.
    /// </summary>
    public AssetGroupOperations AssetGroups => new(context, namespaceOverride);

    /// <summary>
    /// 12 — Resources (OVR-008): the <c>resource</c> engine's records, attached secrets
    /// (<see cref="ResourcesOperations.Secrets"/>, RSC-002) and connect-MFA flow
    /// (<see cref="ResourcesOperations.Connect"/>, RSC-001). <c>mount</c> defaults to <c>"resources"</c>.
    /// </summary>
    public ResourcesOperations Resources => new(context, namespaceOverride);

    /// <summary>
    /// 12 — Files (OVR-008): the <c>files</c> engine's metadata, content and sync targets
    /// (<see cref="FilesOperations.Sync"/>). <c>mount</c> defaults to <c>"files"</c>. FIL-001: every
    /// content parameter is <c>byte[]</c>; the SDK performs the base64 encoding/decoding.
    /// </summary>
    public FilesOperations Files => new(context, namespaceOverride);

    /// <summary>
    /// 12 — LDAP / Active Directory (OVR-008): config, root rotation, connection check, static
    /// roles and the service-account library. <c>mount</c> defaults to <c>"openldap"</c>.
    /// LDP-001's insecure-TLS acknowledgement is enforced client-side before any request is sent.
    /// </summary>
    public LdapOperations Ldap => new(context, namespaceOverride);

    /// <summary>
    /// 12 — Cert lifecycle (OVR-008): renewal targets, their renewer state, the scheduler config
    /// and the deliverer registry. <c>mount</c> defaults to <c>"cert-lifecycle"</c>. Carries no
    /// requirement ID of its own (DR-0017); every MUST is the generic Shape A envelope and
    /// standard error mapping (03/04).
    /// </summary>
    public CertLifecycleOperations CertLifecycle => new(context, namespaceOverride);

    /// <summary>
    /// 12 — Notifications (OVR-008): sending, the inbox, channels and configuration. <c>mount</c>
    /// defaults to <c>"notifications"</c>. Carries no requirement ID of its own (DR-0017); every
    /// MUST is the generic Shape A envelope and standard error mapping (03/04).
    /// </summary>
    public NotificationsOperations Notifications => new(context, namespaceOverride);

    /// <summary>
    /// 12 — Rustion (OVR-008): the bastion-integration mount's operator-facing surface (targets,
    /// master key, authority attestation, sessions, recordings, policy, bastion groups, dispatcher
    /// and telemetry). <c>mount</c> defaults to <c>"rustion"</c>. RUS-001..003.
    /// </summary>
    public RustionOperations Rustion => new(context, namespaceOverride);

    /// <summary>The observable client-side rate-gate pause state (D-M1b-16).</summary>
    public RateGateState RateGateState => context.RateGate.Snapshot();

    /// <summary>TRN-081: the server's <c>sys/info</c> <c>version</c>, cached for this client's lifetime.</summary>
    /// <remarks>
    /// Delegates to <see cref="SysOperations.ServerInfoAsync"/> (TRN-080). Returns the cached or
    /// freshly fetched version, or <see langword="null"/> with no live token (anonymous tier omits
    /// <c>version</c>) — never cached, so a later authenticated call still fetches the real value.
    /// Conformance: Core (TRN-081).
    /// </remarks>
    /// <spec>Client.ServerVersion — TRN-081</spec>
    public async Task<string?> ServerVersionAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (context.ServerVersion is { } cached)
        {
            return cached;
        }

        ServerInfo info = await Sys.ServerInfoAsync(options, cancellationToken).ConfigureAwait(false);
        if (info.Version is { } fetched)
        {
            context.ServerVersion = fetched;
        }

        return info.Version;
    }

    /// <summary>DSC-035's <c>Client.InputLabel</c>: the address as configured, verbatim, in both literal and discovery mode.</summary>
    public string InputLabel => context.Discovery.InputLabel;

    /// <summary>
    /// DSC-035's <c>Client.SelectedNode</c>: the node discovery pinned, or <see langword="null"/>
    /// before discovery has run and in literal mode, where nothing was probed and nothing was
    /// chosen (D-M5-10).
    /// </summary>
    public NodeSelection? SelectedNode => context.Discovery.SelectedNode;

    /// <summary>
    /// Runs cluster discovery and pins a node, returning the pick. Idempotent: a second call on a
    /// pinned client returns the existing pick without re-probing (D-M5-9).
    /// </summary>
    /// <returns>
    /// The pinned node, or <see langword="null"/> on a literal-mode client — an address with
    /// <c>://</c>, an explicit <c>:port</c>, an IP literal, or any address under
    /// <c>ClusterDiscovery = false</c>. DSC-001 makes literal mode "no DNS, no probing", so there is
    /// nothing to run; use <see cref="DiscoverAsync"/> to probe such a client deliberately.
    /// </returns>
    /// <remarks>
    /// Discovery cannot run in the constructor — it is asynchronous and it can fail, and CFG-005's
    /// construction must stay synchronous and non-networking — so it is either this method or the
    /// first operation, which runs it lazily (D-M5-8 ruling 3, D-M5-9).
    /// <para>HTTP call: none of a single fixed shape — delegates to cluster discovery, which (in discovery mode) issues an SRV lookup and then <c>GET {candidate}/v1/sys/health</c> against every candidate in parallel (section 13, DSC-020). Wire params: none of the operation's own. Conformance: Core (D-M5-8, D-M5-9). Errors beyond the common set (ERR-061): <c>BV-DISCOVERY-002</c> (DSC-034) when no candidate survives ranking.</para>
    /// </remarks>
    /// <spec>Client.Connect — 13-cluster-discovery-and-resilience.md</spec>
    public Task<NodeSelection?> ConnectAsync(CancellationToken cancellationToken = default)
    {
        return context.Discovery.ConnectAsync(cancellationToken);
    }

    /// <summary>
    /// DSC-036's diagnostics: the full ranked candidate table, without changing the pinned node.
    /// </summary>
    /// <remarks>HTTP call: none of a single fixed shape — delegates to cluster discovery, which issues <c>GET {candidate}/v1/sys/health</c> against every candidate in parallel (section 13, DSC-020), including the single candidate on a literal-mode client (D-M5-10). Wire params: none. Returns the ranked table, never <see langword="null"/>; never throws <c>BV-DISCOVERY-002</c> even when nothing is picked, since a pick is not this operation's contract. Conformance: Core (DSC-036). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Client.Discover — DSC-036</spec>
    public Task<DiscoveryReport> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        return context.Discovery.DiscoverAsync(cancellationToken);
    }

    /// <summary>
    /// Re-runs full discovery (SRV plus probe) and re-pins. Safe to call concurrently.
    /// <see langword="null"/> on a literal-mode client, for the reason
    /// <see cref="ConnectAsync"/> gives.
    /// </summary>
    /// <remarks>
    /// The member is pinned by D-M5-8 and lands with the rest of the surface so the shape is
    /// reviewed once. DSC-046 — which is the requirement this member exists for, including its
    /// concurrency clause under an in-flight failover — stays baselined for M5b, the slice that owns
    /// the failover lock it has to interact with.
    /// <para>HTTP call: none of a single fixed shape — delegates to cluster discovery, which issues an SRV lookup and then <c>GET {candidate}/v1/sys/health</c> against every candidate in parallel (section 13, DSC-020). Wire params: none. Conformance: Core (DSC-046). Errors beyond the common set (ERR-061): <c>BV-DISCOVERY-002</c> (DSC-034) when no candidate survives ranking.</para>
    /// </remarks>
    /// <spec>Client.Reconnect — DSC-046</spec>
    public Task<NodeSelection?> ReconnectAsync(CancellationToken cancellationToken = default)
    {
        return context.Discovery.ReconnectAsync(cancellationToken);
    }

    /// <summary>
    /// Replaces the token used by this client and every view sharing its token cell (CFG-070),
    /// which per AUT-001 means replacing its <see cref="AuthOperations.TokenSource"/> with a
    /// <see cref="TokenSourceKind.Static"/> one. Thread-safe; in-flight requests keep the token
    /// they started with, because each pass resolved its own snapshot before entering the retry
    /// loop (D-M1b-9).
    /// </summary>
    /// <remarks>HTTP call: none — client-side assignment only, one reference write to a <c>volatile</c> field (CFG-070). Wire params: none. Returns nothing. Conformance: Core (CFG-070). No error codes beyond the common set (ERR-061). <paramref name="token"/> is a redacting <see cref="SecretString"/> and is never logged.</remarks>
    /// <spec>Client.SetToken — CFG-070</spec>
    public void SetToken(SecretString token)
    {
        ArgumentNullException.ThrowIfNull(token);
        context.SetToken(token);
    }

    /// <summary>Clears the token used by this client and every view sharing its token cell (CFG-070).</summary>
    /// <remarks>HTTP call: none — client-side assignment only, equivalent to <see cref="SetToken"/> with an empty token. Wire params: none. Returns nothing. Conformance: Core (CFG-070). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Client.ClearToken — CFG-070</spec>
    public void ClearToken()
    {
        context.SetToken(SecretString.Empty);
    }

    /// <summary>
    /// Returns a lightweight view sharing the transport, the configuration and the same token cell —
    /// a <see cref="SetToken"/> on this client is visible to the view (CFG-071) — differing only in
    /// namespace.
    /// </summary>
    /// <remarks>HTTP call: none — a client-side view constructor. Wire params: <paramref name="ns"/> builds the <c>X-BastionVault-Namespace</c> header on the view's own requests, with a trailing <c>/</c> trimmed. Returns a new <see cref="BastionVaultClient"/>, never <see langword="null"/>. Conformance: Core (CFG-071). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Client.WithNamespace — CFG-071</spec>
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
    /// It disposes the transport only when this client created it (DR-0020 D-2,
    /// <see cref="ownsTransport"/>). An injected transport is the application's (OVR-001), may be
    /// shared between clients, and CFG-072's "construct a new client" would otherwise tear down a
    /// connection pool the caller still owns. A <see cref="WithNamespace"/> view owns no loop and
    /// never owns a transport, so disposing one is a no-op and leaves its parent's renewal and
    /// transport running.
    /// </para>
    /// <para>HTTP call: none — client-side cancellation and cleanup only. Wire params: none. Returns nothing. Conformance: Core (AUT-094, DR-0020 D-2). No error codes beyond the common set (ERR-061); a second call is a safe no-op, not a re-thrown <see cref="ObjectDisposedException"/>.</para>
    /// </remarks>
    /// <spec>Client.Dispose — AUT-094</spec>
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

        if (ownsTransport && Transport is IDisposable disposableTransport)
        {
            disposableTransport.Dispose();
        }
    }
}
