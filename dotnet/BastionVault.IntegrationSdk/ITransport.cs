namespace BastionVault.IntegrationSdk;

/// <summary>
/// The transport injection point (OVR-001): replaceable through an interface so tests can inject a
/// fake that returns canned responses without opening a socket. No production HTTP implementation
/// ships at milestone M1a — request execution is out of scope
/// (<c>decisions/0003-m1a-configuration.md</c>).
/// </summary>
public interface ITransport
{
    /// <summary>Sends one logical request and returns its response.</summary>
    Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken = default);
}

/// <summary>A single logical HTTP request, as seen by an <see cref="ITransport"/>.</summary>
public sealed record TransportRequest(
    string Method,
    Uri Uri,
    IReadOnlyDictionary<string, string> Headers,
    ReadOnlyMemory<byte> Body);

/// <summary>A single logical HTTP response, as returned by an <see cref="ITransport"/>.</summary>
public sealed record TransportResponse(
    int StatusCode,
    IReadOnlyDictionary<string, string> Headers,
    ReadOnlyMemory<byte> Body);
