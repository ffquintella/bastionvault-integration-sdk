using BastionVault.IntegrationSdk;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// DR-0020: <see cref="BastionVaultClient"/> defaults <see cref="BastionVaultClientOptions.Transport"/>
/// to the HTTP transport (D-1), and ownership of that transport follows construction (D-2) — a
/// transport the client created is disposed by the client, one the caller injected never is.
/// </summary>
public sealed class BastionVaultClientDefaultTransportTests
{
    [Fact]
    [Requirement("DR-0020")]
    [Trait("Requirement", "DR-0020")]
    public async Task Constructing_with_only_address_and_token_performs_a_real_request()
    {
        await using InProcessHttpsMockServer server = await InProcessHttpsMockServer.StartAsync();
        server.SetResponse(new MockResponse(200, Body: """{"initialized":true,"sealed":false,"standby":false,"cluster_healthy":true}"""));

        // Deliberately only Address, Token, and the two TLS-pinning knobs the in-process mock
        // server requires to be trusted at all — no Transport is supplied, which is the whole
        // point of DR-0020 D-1.
        using BastionVaultClient client = new(new BastionVaultClientOptions
        {
            Address = server.BaseAddress.ToString(),
            Token = FakeTokens.Client,
            CaCertPem = server.CaCertPem,
            TlsServerName = "127.0.0.1",
        });

        HealthStatus health = await client.Sys.HealthAsync();

        Assert.True(health.Initialized);
        Assert.False(health.Sealed);
        _ = Assert.IsType<HttpClientTransport>(client.Transport);
        _ = Assert.Single(server.Requests);
    }

    [Fact]
    [Requirement("DR-0020")]
    [Trait("Requirement", "DR-0020")]
    public void Disposing_a_client_does_not_dispose_an_injected_transport()
    {
        RecordingDisposableTransport transport = new();
        BastionVaultClient client = new(new BastionVaultClientOptions
        {
            Address = "https://vault.example.com:8200",
            Token = FakeTokens.Client,
            Transport = transport,
        });

        client.Dispose();

        Assert.False(transport.Disposed);
    }

    [Fact]
    [Requirement("DR-0020")]
    [Trait("Requirement", "DR-0020")]
    public void Disposing_a_client_disposes_a_defaulted_transport()
    {
        BastionVaultClient client = new(new BastionVaultClientOptions
        {
            Address = "https://vault.example.com:8200",
            Token = FakeTokens.Client,
        });
        HttpClientTransport transport = Assert.IsType<HttpClientTransport>(client.Transport);

        client.Dispose();

        // HttpClientTransport disposes the HttpClient it owns; sending through a disposed
        // HttpClient throws ObjectDisposedException, which is the only externally observable
        // signal that the client tore it down rather than leaking it (DR-0020 D-2).
        _ = Assert.ThrowsAsync<ObjectDisposedException>(() => transport.SendAsync(
            new TransportRequest("GET", new Uri("https://vault.example.com:8200/v1/sys/health"), new Dictionary<string, string>(), ReadOnlyMemory<byte>.Empty)));
    }

    [Fact]
    [Requirement("DR-0020")]
    [Trait("Requirement", "DR-0020")]
    public void A_WithNamespace_view_never_owns_the_transport_it_shares()
    {
        RecordingDisposableTransport transport = new();
        BastionVaultClient client = new(new BastionVaultClientOptions
        {
            Address = "https://vault.example.com:8200",
            Token = FakeTokens.Client,
            Transport = transport,
        });
        BastionVaultClient view = client.WithNamespace("team-a");

        view.Dispose();
        client.Dispose();

        Assert.False(transport.Disposed);
    }

    private sealed class RecordingDisposableTransport : ITransport, IDisposable
    {
        public bool Disposed { get; private set; }

        public bool SupportsCustomVerbs => true;

        public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TransportResponse(200, new Dictionary<string, string>(), ReadOnlyMemory<byte>.Empty));
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
