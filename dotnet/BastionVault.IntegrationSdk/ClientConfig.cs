using System.Diagnostics.CodeAnalysis;
using BastionVault.IntegrationSdk.Internal;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// The fully resolved, immutable result of construction-time configuration resolution and
/// validation (CFG-001..018). No setting is ever "unset" here — every default has been
/// materialised (<c>decisions/0003-m1a-configuration.md</c>, D-M1a-2). Only <see cref="BastionVaultClient"/>
/// constructs one of these; there is no public constructor.
/// </summary>
public sealed class ClientConfig
{
    internal ClientConfig(
        string address,
        AddressClassifier.Classification classification,
        SecretString token,
        string tokenFile,
        bool useTokenHelper,
        string @namespace,
        string? caCertPath,
        string? caCertPem,
        bool caCertReplacesSystemRoots,
        string? clientCertPath,
        string? clientKeyPath,
        bool tlsSkipVerify,
        string? tlsServerName,
        bool allowInsecureHttp,
        TimeSpan timeout,
        TimeSpan connectTimeout,
        RetryPolicy retryPolicy,
        RateGate rateGate,
        bool clusterDiscovery,
        TimeSpan discoveryProbeTimeout,
        DiscoveryConfig discovery,
        HealthConfig health,
        IReadOnlyDictionary<string, string> headers,
        string userAgent,
        string apiPrefix,
        AutoRenewPolicy autoRenew,
        bool isInsecure,
        X509Certificate2Collection? caCertificates,
        X509Certificate2? clientCertificate,
        long maxResponseBytes,
        bool useSystemProxy)
    {
        Address = address;
        Classification = classification;
        AddressUri = classification.Uri;
        AddressIsClusterName = classification.IsDiscovery;
        Token = token;
        TokenFile = tokenFile;
        UseTokenHelper = useTokenHelper;
        Namespace = @namespace;
        CaCertPath = caCertPath;
        CaCertPem = caCertPem;
        CaCertReplacesSystemRoots = caCertReplacesSystemRoots;
        ClientCertPath = clientCertPath;
        ClientKeyPath = clientKeyPath;
        TlsSkipVerify = tlsSkipVerify;
        TlsServerName = tlsServerName;
        AllowInsecureHttp = allowInsecureHttp;
        Timeout = timeout;
        ConnectTimeout = connectTimeout;
        RetryPolicy = retryPolicy;
        RateGate = rateGate;
        ClusterDiscovery = clusterDiscovery;
        DiscoveryProbeTimeout = discoveryProbeTimeout;
        Discovery = discovery;
        Health = health;
        Headers = headers;
        UserAgent = userAgent;
        ApiPrefix = apiPrefix;
        AutoRenew = autoRenew;
        IsInsecure = isInsecure;
        CaCertificates = caCertificates;
        ClientCertificate = clientCertificate;
        MaxResponseBytes = maxResponseBytes;
        UseSystemProxy = useSystemProxy;
    }

    /// <summary>The literal address value as resolved (a URL or a bare cluster name).</summary>
    public string Address { get; }

    /// <summary>The parsed URL form of <see cref="Address"/>, or <see langword="null"/> when it is a bare cluster name.</summary>
    public Uri? AddressUri { get; }

    /// <summary>Whether <see cref="Address"/> is a bare DNS name that triggers cluster discovery rather than a literal node URL.</summary>
    public bool AddressIsClusterName { get; }

    /// <summary>
    /// DSC-001's full classification of <see cref="Address"/>, taken once during resolution.
    /// Internal, because <see cref="AddressUri"/> and <see cref="AddressIsClusterName"/> are the
    /// public projection of it; carried here so <c>ClientContext</c> reads the resolver's answer
    /// instead of classifying the same inputs a second time.
    /// </summary>
    internal AddressClassifier.Classification Classification { get; }

    /// <summary>The initial token (CFG-020: absence is not an error).</summary>
    public SecretString Token { get; }

    /// <summary>Path read for the token when <see cref="Token"/> is unset and <see cref="UseTokenHelper"/> is true.</summary>
    public string TokenFile { get; }

    /// <summary>Opt-in to reading/writing the token file.</summary>
    public bool UseTokenHelper { get; }

    /// <summary>Slash-delimited path, no leading slash, trailing slash silently stripped (CFG-015).</summary>
    public string Namespace { get; }

    /// <summary>PEM bundle path to trust instead of/in addition to system roots.</summary>
    public string? CaCertPath { get; }

    /// <summary>Inline PEM alternative to <see cref="CaCertPath"/>. Takes precedence when both are set.</summary>
    public string? CaCertPem { get; }

    /// <summary>When true, <see cref="CaCertPath"/>/<see cref="CaCertPem"/> replace the platform trust store instead of being added to it (CFG-040).</summary>
    public bool CaCertReplacesSystemRoots { get; }

    /// <summary>mTLS client certificate path (PEM).</summary>
    public string? ClientCertPath { get; }

    /// <summary>mTLS client private key path (PEM).</summary>
    public string? ClientKeyPath { get; }

    /// <summary>Disables certificate verification. See CNF-030.</summary>
    public bool TlsSkipVerify { get; }

    /// <summary>SNI / hostname to verify against (CFG-042).</summary>
    public string? TlsServerName { get; }

    /// <summary>Required for non-loopback <c>http://</c> addresses (CNF-035).</summary>
    public bool AllowInsecureHttp { get; }

    /// <summary>Per-request total timeout (connect + headers + body).</summary>
    public TimeSpan Timeout { get; }

    /// <summary>TCP + TLS handshake timeout.</summary>
    public TimeSpan ConnectTimeout { get; }

    /// <summary>Retry policy defaults (CFG-050). Retry execution is M1b.</summary>
    public RetryPolicy RetryPolicy { get; }

    /// <summary>Client-side rate gate values (D-M1a-13). Resolved and validated only; the token bucket itself is M8.</summary>
    public RateGate RateGate { get; }

    /// <summary>See <c>specifications/13-cluster-discovery-and-resilience.md</c>.</summary>
    public bool ClusterDiscovery { get; }

    /// <summary>Health probe timeout per candidate.</summary>
    public TimeSpan DiscoveryProbeTimeout { get; }

    /// <summary>DNS SRV discovery settings (DSC-010…014). No environment variable (D-M5-19).</summary>
    public DiscoveryConfig Discovery { get; }

    /// <summary>
    /// Health-probe settings (DSC-020). <see cref="HealthConfig.ProbeTimeout"/> is populated from
    /// <see cref="DiscoveryProbeTimeout"/> unless an explicit
    /// <see cref="BastionVaultClientOptions.Health"/> overrides it (D-M5-8, ruling 4).
    /// </summary>
    public HealthConfig Health { get; }

    /// <summary>Extra headers added to every request. Never contains a reserved header (CFG-017).</summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>Appended, never replacing, the SDK's own user agent.</summary>
    public string UserAgent { get; }

    /// <summary>Default prefix for raw logical operations (<c>v1</c> or <c>v2</c>).</summary>
    public string ApiPrefix { get; }

    /// <summary>Background token renewal (D-M1a-13). Always disabled at milestone M1a; the renewal loop is M2.</summary>
    public AutoRenewPolicy AutoRenew { get; }

    /// <summary>
    /// True when <see cref="TlsSkipVerify"/> is set (CFG-018). A client with <c>IsInsecure == true</c>
    /// has already logged one CNF-030 warning by the time construction returns.
    /// </summary>
    public bool IsInsecure { get; }

    /// <summary>
    /// The parsed CA certificate(s) from <see cref="CaCertPem"/> or <see cref="CaCertPath"/>
    /// (CFG-013, CFG-014), or <see langword="null"/> when neither was configured. Materialised at
    /// construction; wiring into the platform trust store (CFG-040) is transport work (M1b).
    /// </summary>
    public X509Certificate2Collection? CaCertificates { get; }

    /// <summary>
    /// The parsed mTLS client certificate/key pair (CFG-012, CFG-013, CFG-014), or
    /// <see langword="null"/> when neither <see cref="ClientCertPath"/> nor <see cref="ClientKeyPath"/>
    /// was configured.
    /// </summary>
    public X509Certificate2? ClientCertificate { get; }

    /// <summary>Responses larger than this are aborted with <c>BV-TRANSPORT-004</c> (TRN-033, D-M1b-13). Default 128 MiB.</summary>
    public long MaxResponseBytes { get; }

    /// <summary>Proxies are disabled by default; <see langword="true"/> opts in to environment and OS proxy settings (TRN-091, D-M1b-13).</summary>
    public bool UseSystemProxy { get; }

    /// <summary>The minimum TLS protocol version the SDK ever negotiates (CFG-041): always TLS 1.2.</summary>
    [SuppressMessage(
        "Security",
        "CA5398:Avoid hardcoded SslProtocols values",
        Justification = "CFG-041 normatively fixes the minimum at TLS 1.2; this is not an application choice.")]
    public static SslProtocols MinimumTlsProtocol => SslProtocols.Tls12;
}
