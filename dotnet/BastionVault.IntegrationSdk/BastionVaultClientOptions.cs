namespace BastionVault.IntegrationSdk;

/// <summary>
/// The mutable options an application fills in to construct a <see cref="BastionVaultClient"/>.
/// Resolved exactly once, at construction, into an immutable <see cref="ClientConfig"/>
/// (CFG-001, CFG-002; <c>decisions/0003-m1a-configuration.md</c>, D-M1a-2). Every property left
/// <see langword="null"/> falls back to its environment variable(s) and then its built-in default,
/// per the settings table in <c>specifications/02-client-configuration.md</c>.
/// </summary>
public sealed class BastionVaultClientOptions
{
    /// <summary>A literal node URL (<c>https://host:8200</c>) or a bare DNS name for cluster discovery. Default <c>https://127.0.0.1:8200</c>.</summary>
    public string? Address { get; set; }

    /// <summary>The initial token. May be replaced later by a login or <c>SetToken</c> (M1b).</summary>
    public string? Token { get; set; }

    /// <summary>Path read for the token when <see cref="Token"/> is unset and <see cref="UseTokenHelper"/> is true. Default <c>~/.vault-token</c>.</summary>
    public string? TokenFile { get; set; }

    /// <summary>Opt-in to reading/writing the token file. Default <see langword="false"/>.</summary>
    public bool? UseTokenHelper { get; set; }

    /// <summary>Slash-delimited namespace path, no leading slash. Default <c>""</c> (root).</summary>
    public string? Namespace { get; set; }

    /// <summary>PEM bundle path to trust instead of/in addition to system roots.</summary>
    public string? CaCertPath { get; set; }

    /// <summary>Inline PEM alternative to <see cref="CaCertPath"/>. Takes precedence when both are set.</summary>
    public string? CaCertPem { get; set; }

    /// <summary>When true, <see cref="CaCertPath"/>/<see cref="CaCertPem"/> replace the platform trust store instead of being added to it (CFG-040). Default <see langword="false"/>.</summary>
    public bool? CaCertReplacesSystemRoots { get; set; }

    /// <summary>mTLS client certificate (PEM). Must be set together with <see cref="ClientKeyPath"/>.</summary>
    public string? ClientCertPath { get; set; }

    /// <summary>mTLS client private key (PEM). Must be set together with <see cref="ClientCertPath"/>.</summary>
    public string? ClientKeyPath { get; set; }

    /// <summary>Disables certificate verification. Default <see langword="false"/>. See CNF-030.</summary>
    public bool? TlsSkipVerify { get; set; }

    /// <summary>SNI / hostname to verify against (CFG-042).</summary>
    public string? TlsServerName { get; set; }

    /// <summary>Required for non-loopback <c>http://</c> addresses (CNF-035). Default <see langword="false"/>.</summary>
    public bool? AllowInsecureHttp { get; set; }

    /// <summary>Per-request total timeout (connect + headers + body). Default 30s.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>TCP + TLS handshake timeout. Default 10s.</summary>
    public TimeSpan? ConnectTimeout { get; set; }

    /// <summary>Retry policy defaults (CFG-050). Retry execution is M1b.</summary>
    public RetryPolicy? RetryPolicy { get; set; }

    /// <summary>Client-side token bucket (<c>specifications/14-batch-and-request-efficiency.md#client-rate-gate</c>). Resolved and validated only; execution is M8.</summary>
    public RateGate? RateGate { get; set; }

    /// <summary>See <c>specifications/13-cluster-discovery-and-resilience.md</c>. Default <see langword="true"/>.</summary>
    public bool? ClusterDiscovery { get; set; }

    /// <summary>Health probe timeout per candidate. Default 1500ms.</summary>
    public TimeSpan? DiscoveryProbeTimeout { get; set; }

    /// <summary>Extra headers added to every request. MUST NOT override reserved headers (CFG-017).</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; set; }

    /// <summary>Appended, never replacing, the SDK's own user agent. Default <c>bastionvault-sdk-dotnet/&lt;version&gt;</c>.</summary>
    public string? UserAgent { get; set; }

    /// <summary>Default prefix for raw logical operations (<c>v1</c> or <c>v2</c>). Default <c>v1</c>.</summary>
    public string? ApiPrefix { get; set; }

    /// <summary>Background token renewal (<c>specifications/05-authentication.md#automatic-renewal</c>). Default disabled; the renewal loop is M2.</summary>
    public AutoRenewPolicy? AutoRenew { get; set; }

    /// <summary>The runtime-idiomatic logging hook (CNF-030). Defaults to <see cref="NoOpClientLogger"/>.</summary>
    public IClientLogger? Logger { get; set; }

    /// <summary>The transport injection point (OVR-001). No default is provided.</summary>
    public ITransport? Transport { get; set; }

    /// <summary>Responses larger than this are aborted with <c>BV-TRANSPORT-004</c> (TRN-033, D-M1b-13). Default 128 MiB.</summary>
    public long? MaxResponseBytes { get; set; }

    /// <summary>Proxies are disabled by default; set <see langword="true"/> to opt in to environment and OS proxy settings (TRN-091, D-M1b-13).</summary>
    public bool? UseSystemProxy { get; set; }

    /// <summary>The injected time seam (D-M1b-7). Defaults to <see cref="SystemClock"/>.</summary>
    public IClock? Clock { get; set; }

    /// <summary>The injected randomness seam for retry jitter (D-M1b-7). Defaults to <see cref="SystemJitterSource"/>.</summary>
    public IJitterSource? JitterSource { get; set; }

    /// <summary>The request/response observability hook (CFG-080). No default.</summary>
    public IRequestObserver? Observer { get; set; }
}
