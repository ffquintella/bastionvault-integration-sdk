namespace BastionVault.IntegrationSdk;

/// <summary>
/// The transport injection point (OVR-001), redesigned once at M1b to the single canonical shape
/// every logical operation and every language now shares (<c>decisions/0004-m1b-transport.md</c>,
/// D-M1b-1). A transport-level failure MUST be raised as a <see cref="BastionVaultException"/>
/// carrying <c>BV-TRANSPORT-001/002/003/005</c>, never as a raw runtime exception, so the retry
/// classifier and the fixture harness can both read a code instead of matching on exception types.
/// </summary>
public interface ITransport
{
    /// <summary>
    /// Whether this transport can send a literal, non-standard HTTP method such as <c>LIST</c>
    /// (TRN-010). Defaults to <see langword="true"/> for every shipped implementation; a
    /// <see langword="false"/> value causes <see cref="BastionVaultClient"/> construction to fail
    /// with <c>BV-CONFIG-009</c> (D-M1b-14).
    /// </summary>
    public bool SupportsCustomVerbs => true;

    /// <summary>Sends one logical request and returns its response, or throws a <see cref="BastionVaultException"/>.</summary>
    public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken = default);
}

/// <summary>A single logical HTTP request, as seen by an <see cref="ITransport"/> (D-M1b-1).</summary>
public sealed record TransportRequest(
    string Method,
    Uri Uri,
    IReadOnlyDictionary<string, string> Headers,
    ReadOnlyMemory<byte> Body)
{
    /// <summary>The per-attempt budget (RES-004); enforcement is the transport's responsibility.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The TCP/TLS handshake budget for this attempt.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Responses larger than this MUST be aborted by the transport while reading, before the full
    /// body is buffered (TRN-033, D-M1b-20) — not audited afterwards. Default 128 MiB.
    /// </summary>
    public long MaxResponseBytes { get; init; } = 134217728L;
}

/// <summary>A single logical HTTP response, as returned by an <see cref="ITransport"/>.</summary>
public sealed record TransportResponse(
    int StatusCode,
    IReadOnlyDictionary<string, string> Headers,
    ReadOnlyMemory<byte> Body);
