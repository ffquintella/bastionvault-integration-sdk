using System.Text;
using System.Text.Json;
using BastionVault.IntegrationSdk;
using BastionVault.IntegrationSdk.Testing;

namespace BastionVault.IntegrationSdk.Tests.Harness.Operations;

/// <summary>
/// Bridges the harness's fixture-scripted <see cref="ScriptedTransport"/> (built once per fixture by
/// <see cref="FixtureDriver"/>, so <c>CompareExchanges</c>/<c>AssertFullyConsumed</c> see every
/// request) to the SDK's real <see cref="ITransport"/> contract, so <see cref="LogicalOperations"/>
/// runs its actual production code — URL construction, headers, retry loop, status mapping — against
/// scripted bytes rather than a private shim (D-M0-2, D-M1a-6). A scripted transport-level failure is
/// converted to a <see cref="BastionVaultException"/> through the SDK's own public
/// <see cref="FakeTransport"/> (D-M1b-1, D-M1b-4a) rather than by duplicating that mapping here.
/// </summary>
internal sealed class ScriptedTransportAdapter : ITransport
{
    private readonly ScriptedTransport transport;

    public ScriptedTransportAdapter(ScriptedTransport transport)
    {
        this.transport = transport;
    }

    public bool SupportsCustomVerbs { get; set; } = true;

    public async Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken = default)
    {
        JsonElement? body = null;
        if (!request.Body.IsEmpty)
        {
            using JsonDocument document = JsonDocument.Parse(request.Body);
            body = document.RootElement.Clone();
        }

        // Uri.ToString() returns an unescaped display form (e.g. "%20" becomes a literal space);
        // AbsoluteUri preserves percent-encoding, which is what TRN-020/TRN-021 fixtures assert.
        FixtureRequest fixtureRequest = FixtureRequest.Create(request.Method, request.Uri.AbsoluteUri, request.Headers, body);
        FixtureTransportResponse response;
        try
        {
            response = await transport.SendAsync(fixtureRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (FixtureTransportFailureException failure)
        {
            Internal.TransportFailureKind kind = failure.Mode switch
            {
                FixtureTransportFailureMode.connection_refused => Internal.TransportFailureKind.ConnectionRefused,
                FixtureTransportFailureMode.dns => Internal.TransportFailureKind.Dns,
                FixtureTransportFailureMode.reset => Internal.TransportFailureKind.Reset,
                FixtureTransportFailureMode.timeout => Internal.TransportFailureKind.Timeout,
                FixtureTransportFailureMode.tls_verify => Internal.TransportFailureKind.TlsVerify,
                FixtureTransportFailureMode.tls_handshake => Internal.TransportFailureKind.TlsHandshake,
                _ => throw new NotSupportedException($"Unrecognised fixture failure mode '{failure.Mode}'."),
            };

            FakeTransport oneShot = new();
            oneShot.EnqueueFailure(kind);
            // Discards the placeholder request FakeTransport records; it exists only to raise the
            // correctly-coded BastionVaultException through real SDK code.
            return await oneShot.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        byte[] bytes = response.RawBody is not null
            ? Encoding.UTF8.GetBytes(response.RawBody)
            : response.Body is { } bodyElement
                ? JsonSerializer.SerializeToUtf8Bytes(bodyElement)
                : Array.Empty<byte>();
        return new TransportResponse(response.Status, response.Headers, bytes);
    }
}
