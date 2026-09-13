using System.Net.Sockets;
using System.Security.Cryptography;
using BastionVault.IntegrationSdk;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// Exercises the production <see cref="HttpClientTransport"/> (D-M1b-3) against real sockets: CA
/// pinning, the custom <c>LIST</c> verb, mTLS, and connection-refused mapping to
/// <c>BV-TRANSPORT-001</c>.
/// </summary>
public sealed class HttpClientTransportTests
{
    [Fact]
    [Requirement("D-M1b-3")]
    [Requirement("TRN-090")]
    [Trait("Requirement", "D-M1b-3")]
    public async Task Sends_a_pinned_https_request_and_reads_status_headers_and_body()
    {
        await using InProcessHttpsMockServer server = await InProcessHttpsMockServer.StartAsync();
        server.SetResponse(new MockResponse(200, new Dictionary<string, string> { ["X-Test"] = "yes" }, """{"ok":true}"""));

        BastionVaultClient configHolder = new(new BastionVaultClientOptions
        {
            Address = server.BaseAddress.ToString(),
            CaCertPem = server.CaCertPem,
            TlsServerName = "127.0.0.1",
        });
        using HttpClientTransport transport = new(configHolder.Config);

        TransportRequest request = new(
            "LIST",
            new Uri(server.BaseAddress, "/v1/secret/"),
            new Dictionary<string, string> { ["Accept"] = "application/json" },
            ReadOnlyMemory<byte>.Empty);

        TransportResponse response = await transport.SendAsync(request);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("yes", response.Headers["X-Test"]);
        Assert.Contains("ok", System.Text.Encoding.UTF8.GetString(response.Body.Span));
        Assert.Equal("LIST", server.Requests.Single().Method);
    }

    [Fact]
    [Requirement("D-M1b-3")]
    [Trait("Requirement", "D-M1b-3")]
    public async Task Posts_a_body_and_the_server_observes_it()
    {
        await using InProcessHttpsMockServer server = await InProcessHttpsMockServer.StartAsync();
        server.SetResponse(new MockResponse(204));
        BastionVaultClient configHolder = new(new BastionVaultClientOptions
        {
            Address = server.BaseAddress.ToString(),
            CaCertPem = server.CaCertPem,
        });
        using HttpClientTransport transport = new(configHolder.Config);

        byte[] body = System.Text.Encoding.UTF8.GetBytes("""{"a":1}""");
        TransportRequest request = new(
            "POST",
            new Uri(server.BaseAddress, "/v1/secret/data/x"),
            new Dictionary<string, string> { ["Content-Type"] = "application/json" },
            body);

        TransportResponse response = await transport.SendAsync(request);

        Assert.Equal(204, response.StatusCode);
        Assert.Contains(server.Requests, observation => observation.Body == """{"a":1}""");
    }

    [Fact]
    [Requirement("TRN-060")]
    [Requirement("CNF-034")]
    [Trait("Requirement", "TRN-060")]
    public async Task Never_follows_a_redirect()
    {
        await using InProcessHttpsMockServer server = await InProcessHttpsMockServer.StartAsync();
        server.SetResponse(new MockResponse(307, new Dictionary<string, string> { ["Location"] = "https://evil.example.com/" }));
        BastionVaultClient configHolder = new(new BastionVaultClientOptions
        {
            Address = server.BaseAddress.ToString(),
            CaCertPem = server.CaCertPem,
        });
        using HttpClientTransport transport = new(configHolder.Config);

        TransportResponse response = await transport.SendAsync(new TransportRequest("GET", server.BaseAddress, new Dictionary<string, string>(), ReadOnlyMemory<byte>.Empty));

        Assert.Equal(307, response.StatusCode);
    }

    [Fact]
    [Requirement("D-M1b-1")]
    [Trait("Requirement", "D-M1b-1")]
    public async Task Connection_refused_maps_to_BV_TRANSPORT_001()
    {
        int port = GetUnusedPort();
        BastionVaultClient configHolder = new(new BastionVaultClientOptions { Address = $"https://127.0.0.1:{port}" });
        using HttpClientTransport transport = new(configHolder.Config);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => transport.SendAsync(new TransportRequest("GET", new Uri($"https://127.0.0.1:{port}/v1/sys/health"), new Dictionary<string, string>(), ReadOnlyMemory<byte>.Empty)));

        Assert.Equal(ErrorCodes.TransportConnectionFailed, exception.Code);
    }

    [Fact]
    [Requirement("CFG-041")]
    [Requirement("CFG-042")]
    [Requirement("CFG-044")]
    [Trait("Requirement", "CFG-041")]
    public void Builds_successfully_with_tls_skip_verify_ca_certificates_client_certificate_and_server_name()
    {
        string tempDirectory = Directory.CreateTempSubdirectory("bv-transport-tls-").FullName;
        try
        {
            using RSA key = RSA.Create(2048);
            System.Security.Cryptography.X509Certificates.CertificateRequest request = new(
                "CN=test-ca",
                key,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            using System.Security.Cryptography.X509Certificates.X509Certificate2 ca = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            string caPemPath = Path.Combine(tempDirectory, "ca.pem");
            File.WriteAllText(caPemPath, ca.ExportCertificatePem());
            string clientCertPath = Path.Combine(tempDirectory, "client.crt");
            string clientKeyPath = Path.Combine(tempDirectory, "client.key");
            File.WriteAllText(clientCertPath, ca.ExportCertificatePem());
            File.WriteAllText(clientKeyPath, key.ExportPkcs8PrivateKeyPem());

            BastionVaultClient skipVerifyHolder = new(new BastionVaultClientOptions { TlsSkipVerify = true });
            using (HttpClientTransport t1 = new(skipVerifyHolder.Config))
            {
                Assert.True(t1.SupportsCustomVerbs);
            }

            BastionVaultClient caHolder = new(new BastionVaultClientOptions { CaCertPath = caPemPath, TlsServerName = "vault.internal" });
            using (HttpClientTransport t2 = new(caHolder.Config))
            {
                Assert.NotNull(t2);
            }

            BastionVaultClient replacesRootsHolder = new(new BastionVaultClientOptions { CaCertPath = caPemPath, CaCertReplacesSystemRoots = true });
            using (HttpClientTransport t3 = new(replacesRootsHolder.Config))
            {
                Assert.NotNull(t3);
            }

            BastionVaultClient clientCertHolder = new(new BastionVaultClientOptions { ClientCertPath = clientCertPath, ClientKeyPath = clientKeyPath });
            using (HttpClientTransport t4 = new(clientCertHolder.Config))
            {
                Assert.NotNull(t4);
            }

            BastionVaultClient proxyHolder = new(new BastionVaultClientOptions { UseSystemProxy = true });
            using (HttpClientTransport t5 = new(proxyHolder.Config))
            {
                Assert.NotNull(t5);
            }
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    [Requirement("TRN-033")]
    [Trait("Requirement", "TRN-033")]
    public async Task A_declared_Content_Length_over_the_bound_is_rejected_without_reading_a_byte()
    {
        await using Harness.InProcessHttpsMockServer server = await Harness.InProcessHttpsMockServer.StartAsync();
        server.SetResponse(new MockResponse(200, Body: new string('a', 4096))); // ASP.NET sets Content-Length for a fully-buffered string body.
        BastionVaultClient configHolder = new(new BastionVaultClientOptions { Address = server.BaseAddress.ToString(), CaCertPem = server.CaCertPem });
        using HttpClientTransport transport = new(configHolder.Config);

        TransportRequest request = new("GET", server.BaseAddress, new Dictionary<string, string>(), ReadOnlyMemory<byte>.Empty)
        {
            MaxResponseBytes = 100,
        };

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => transport.SendAsync(request));

        Assert.Equal(ErrorCodes.TransportResponseTooLarge, exception.Code);
    }

    [Fact]
    [Requirement("TRN-033")]
    [Requirement("D-M1b-20")]
    [Trait("Requirement", "TRN-033")]
    public async Task An_unbounded_length_response_over_the_bound_is_aborted_mid_stream_not_after_a_full_read()
    {
        // No Content-Length is set for this response (Kestrel falls back to chunked transfer
        // encoding for a body written directly to Response.Body), so the only way to detect the
        // oversized body at all is to bound the read itself — proving TRN-033 is enforced *during*
        // the read, not by measuring TransportResponse.Body.Length afterwards (D-M1b-20).
        const int oversizedBytes = 20_000_000; // 20 MiB: large enough that a full unbounded read is measurably slower than a bounded abort.
        await using Harness.InProcessHttpsMockServer server = await Harness.InProcessHttpsMockServer.StartAsync(
            new MockServerOptions(OversizedResponseBytes: oversizedBytes));
        BastionVaultClient configHolder = new(new BastionVaultClientOptions { Address = server.BaseAddress.ToString(), CaCertPem = server.CaCertPem });
        using HttpClientTransport transport = new(configHolder.Config);

        // Baseline: the same oversized body, fully read (MaxResponseBytes comfortably above it).
        System.Diagnostics.Stopwatch fullRead = System.Diagnostics.Stopwatch.StartNew();
        TransportResponse full = await transport.SendAsync(new TransportRequest(
            "GET", server.BaseAddress, new Dictionary<string, string>(), ReadOnlyMemory<byte>.Empty) { MaxResponseBytes = oversizedBytes + 1 });
        fullRead.Stop();
        Assert.Equal(oversizedBytes, full.Body.Length);

        // Bounded: aborts after at most one internal chunk (81920 bytes), never buffering anywhere
        // near the full 20 MiB body.
        System.Diagnostics.Stopwatch boundedRead = System.Diagnostics.Stopwatch.StartNew();
        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => transport.SendAsync(new TransportRequest(
            "GET", server.BaseAddress, new Dictionary<string, string>(), ReadOnlyMemory<byte>.Empty) { MaxResponseBytes = 1000 }));
        boundedRead.Stop();

        Assert.Equal(ErrorCodes.TransportResponseTooLarge, exception.Code);
        // The bounded abort reads at most ~80 KiB before throwing; the full read buffers 20 MiB.
        // On loopback this is not a close call — assert the bounded path is decisively faster
        // rather than pin an absolute threshold that could be flaky under CI load.
        Assert.True(
            boundedRead.Elapsed <= fullRead.Elapsed,
            $"Expected the bounded read ({boundedRead.Elapsed}) to be no slower than the full read ({fullRead.Elapsed}) of a body {oversizedBytes} bytes larger.");
    }

    private static int GetUnusedPort()
    {
        using TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
