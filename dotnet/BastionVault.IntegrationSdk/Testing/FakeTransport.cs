using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk.Testing;

/// <summary>
/// The <see cref="ITransport"/> conformance fixtures are executed through (TRN-100, D-M1b-15).
/// Ships as public test-support surface, in the public-API baseline like any other public type
/// (CNF-027) — it is the same object the fixture driver drives, not a second implementation beside
/// it. Scripts a queue of canned responses or transport-level failures and records every request
/// sent through it (<c>method, url, headers, body</c>).
/// </summary>
public sealed class FakeTransport : ITransport
{
    private readonly Queue<Func<TransportResponse>> script = new();
    private readonly List<TransportRequest> requests = new();

    /// <summary>Every request sent through this transport, in order.</summary>
    public IReadOnlyList<TransportRequest> Requests => requests;

    /// <inheritdoc/>
    public bool SupportsCustomVerbs { get; set; } = true;

    /// <summary>Enqueues a canned response for the next request.</summary>
    public void EnqueueResponse(int statusCode, IReadOnlyDictionary<string, string>? headers = null, ReadOnlyMemory<byte> body = default)
    {
        IReadOnlyDictionary<string, string> capturedHeaders = headers is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
        script.Enqueue(() => new TransportResponse(statusCode, capturedHeaders, body));
    }

    /// <summary>Enqueues a scripted transport-level failure (connection refused, timeout, TLS error, ...) for the next request.</summary>
    public void EnqueueFailure(TransportFailureKind kind)
    {
        script.Enqueue(() => throw TransportFailureMapper.Map(kind));
    }

    /// <inheritdoc/>
    public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        requests.Add(request);
        if (script.Count == 0)
        {
            throw new InvalidOperationException($"FakeTransport has no scripted response left for {request.Method} {request.Uri}.");
        }

        Func<TransportResponse> next = script.Dequeue();
        return Task.FromResult(next());
    }
}
