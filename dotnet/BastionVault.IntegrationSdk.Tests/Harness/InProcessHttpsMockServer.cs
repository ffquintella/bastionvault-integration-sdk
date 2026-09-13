using System.Collections.Concurrent;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace BastionVault.IntegrationSdk.Tests.Harness;

public enum MockVaultScenario
{
    Normal,
    Sealed,
    StandbyHealth,
    Uninitialized,
    DosBan,
    NamespaceQuota,
    NotFound,
    MethodNotAllowed,
    NoContent,
    LoginFailure,
}

public sealed record MockServerOptions(
    bool RequireClientCertificate = false,
    string CertificateHostName = "127.0.0.1",
    TimeSpan? ResponseDelay = null,
    int OversizedResponseBytes = 0);

public sealed record MockResponse(
    int StatusCode,
    IReadOnlyDictionary<string, string>? Headers = null,
    string? Body = null,
    bool BodyIsJson = true);

public sealed record MockRequestObservation(
    string Method,
    string Path,
    string? Body);

public sealed class InProcessHttpsMockServer : IAsyncDisposable
{
    private readonly MockServerOptions options;
    private readonly ConcurrentDictionary<string, byte> connectionIds = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<MockRequestObservation> requests = new();
    private readonly string tempDirectory;
    private readonly X509Certificate2 caCertificate;
    private readonly RSA caKey;
    private readonly X509Certificate2 serverCertificate;
    private readonly RSA serverKey;
    private readonly X509Certificate2 clientCertificate;
    private readonly RSA clientKey;
    private readonly string caPemPath;
    private readonly WebApplication app;
    private MockVaultScenario scenario = MockVaultScenario.Normal;
    private MockResponse? customResponse;
    private int disposed;

    private InProcessHttpsMockServer(
        MockServerOptions options,
        string tempDirectory,
        X509Certificate2 caCertificate,
        RSA caKey,
        X509Certificate2 serverCertificate,
        RSA serverKey,
        X509Certificate2 clientCertificate,
        RSA clientKey,
        string caPemPath,
        WebApplication app)
    {
        this.options = options;
        this.tempDirectory = tempDirectory;
        this.caCertificate = caCertificate;
        this.caKey = caKey;
        this.serverCertificate = serverCertificate;
        this.serverKey = serverKey;
        this.clientCertificate = clientCertificate;
        this.clientKey = clientKey;
        this.caPemPath = caPemPath;
        this.app = app;
    }

    public Uri BaseAddress { get; private set; } = null!;

    public string CaCertPem => File.ReadAllText(caPemPath);

    public X509Certificate2 ClientCertificate => new(clientCertificate);

    public string TempDirectoryPath => tempDirectory;

    public int AcceptedConnectionCount => connectionIds.Count;

    public bool ServerCertificateHasPrivateKey => serverCertificate.HasPrivateKey;

    public IReadOnlyList<MockRequestObservation> Requests => requests.ToArray();

    public MockVaultScenario Scenario
    {
        get => scenario;
        set => scenario = value;
    }

    public static async Task<InProcessHttpsMockServer> StartAsync(MockServerOptions? options = null)
    {
        MockServerOptions actualOptions = options ?? new MockServerOptions();
        string tempDirectory = Directory.CreateTempSubdirectory("bastionvault-dotnet-harness-").FullName;
        (X509Certificate2 ca, RSA caKey, X509Certificate2 server, RSA serverKey, X509Certificate2 client, RSA clientKey, string caPemPath) = CreateCertificates(tempDirectory, actualOptions.CertificateHostName);

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(InProcessHttpsMockServer).Assembly.GetName().Name,
            EnvironmentName = "Testing",
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Listen(IPAddress.Loopback, 0, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http1;
                listenOptions.UseHttps(server, https =>
                {
                    https.SslProtocols = SslProtocols.None;
                    https.ClientCertificateMode = actualOptions.RequireClientCertificate
                        ? ClientCertificateMode.RequireCertificate
                        : ClientCertificateMode.NoCertificate;
                    https.ClientCertificateValidation = (certificate, chain, _) => certificate is not null && IsIssuedBy(certificate, ca);
                });
            });
        });

        WebApplication app = builder.Build();
        InProcessHttpsMockServer mockServer = new(actualOptions, tempDirectory, ca, caKey, server, serverKey, client, clientKey, caPemPath, app);
        app.Run(mockServer.HandleRequestAsync);
        await app.StartAsync().ConfigureAwait(false);

        IServerAddressesFeature? addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        string? address = addresses?.Addresses.SingleOrDefault();
        if (address is null)
        {
            await app.StopAsync().ConfigureAwait(false);
            await app.DisposeAsync().ConfigureAwait(false);
            mockServer.DisposeCertificates();
            throw new InvalidOperationException("The HTTPS mock server did not expose a bound loopback address.");
        }

        mockServer.BaseAddress = new Uri(address, UriKind.Absolute);
        return mockServer;
    }

    public void SetResponse(MockResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        customResponse = response;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await app.StopAsync().ConfigureAwait(false);
        await app.DisposeAsync().ConfigureAwait(false);
        DisposeCertificates();
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private async Task HandleRequestAsync(HttpContext context)
    {
        connectionIds.TryAdd(context.Connection.Id, 0);
        string? requestBody = await ReadRequestBodyAsync(context.Request).ConfigureAwait(false);
        requests.Enqueue(new MockRequestObservation(context.Request.Method, context.Request.Path.Value ?? string.Empty, requestBody));

        try
        {
            if (options.ResponseDelay is TimeSpan delay && delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, context.RequestAborted).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            return;
        }

        MockResponse response = customResponse ?? ResponseFor(scenario);
        context.Response.StatusCode = response.StatusCode;
        if (response.Headers is not null)
        {
            foreach ((string name, string value) in response.Headers)
            {
                context.Response.Headers[name] = value;
            }
        }

        if (options.OversizedResponseBytes > 0)
        {
            response = response with
            {
                Body = new string('x', options.OversizedResponseBytes),
                BodyIsJson = false,
            };
        }

        if (response.Body is null)
        {
            return;
        }

        if (response.BodyIsJson)
        {
            context.Response.ContentType = "application/json";
        }

        byte[] bytes = Encoding.UTF8.GetBytes(response.Body);
        await context.Response.Body.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task<string?> ReadRequestBodyAsync(HttpRequest request)
    {
        if (request.ContentLength is not > 0)
        {
            return null;
        }

        using StreamReader reader = new(request.Body, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync(request.HttpContext.RequestAborted).ConfigureAwait(false);
    }

    private static MockResponse ResponseFor(MockVaultScenario scenario)
    {
        return scenario switch
        {
            MockVaultScenario.Sealed => JsonResponse(503, new { error = "BastionVault is sealed." }),
            MockVaultScenario.StandbyHealth => JsonResponse(429, new Dictionary<string, object?>
            {
                ["initialized"] = true,
                ["sealed"] = false,
                ["standby"] = true,
                ["cluster_healthy"] = true,
            }),
            MockVaultScenario.Uninitialized => JsonResponse(501, new Dictionary<string, object?>
            {
                ["initialized"] = false,
                ["sealed"] = false,
                ["standby"] = false,
                ["cluster_healthy"] = true,
            }),
            MockVaultScenario.DosBan => JsonResponse(429, new { errors = new[] { "request temporarily blocked by DoS protection: request rate exceeded: >200 req/10s" } }, new Dictionary<string, string> { ["Retry-After"] = "17" }),
            MockVaultScenario.NamespaceQuota => JsonResponse(429, new { error = "namespace request-rate quota exceeded: 5 req/s for \"eng\"" }),
            MockVaultScenario.NotFound => new MockResponse(404, Body: string.Empty, BodyIsJson: false),
            MockVaultScenario.MethodNotAllowed => new MockResponse(405, Body: string.Empty, BodyIsJson: false),
            MockVaultScenario.NoContent => new MockResponse(204, Body: null, BodyIsJson: false),
            MockVaultScenario.LoginFailure => JsonResponse(200, new { renewable = false, lease_id = "", lease_duration = 0, auth = (object?)null, data = new { error = "invalid username or password" } }),
            _ => JsonResponse(200, new { ok = true }),
        };
    }

    // The default JsonSerializerOptions escape characters like '>' as ">" for safe embedding
    // in HTML/script contexts. The mock BastionVault responses are compared byte-for-byte against
    // fixture bodies that contain raw '>' (e.g. the DoS-protection message "rate exceeded: >200
    // req/10s"), so the harness must serialize with the relaxed (still-JSON-conformant) escaper.
    private static readonly JsonSerializerOptions ResponseSerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static MockResponse JsonResponse(int statusCode, object body, IReadOnlyDictionary<string, string>? headers = null)
    {
        return new MockResponse(statusCode, headers, JsonSerializer.Serialize(body, ResponseSerializerOptions));
    }

    private static (X509Certificate2 Ca, RSA CaKey, X509Certificate2 Server, RSA ServerKey, X509Certificate2 Client, RSA ClientKey, string CaPemPath) CreateCertificates(string directory, string serverHostName)
    {
        RSA caKey = RSA.Create(2048);
        CertificateRequest caRequest = new("CN=BastionVault Test CA", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
            true));
        caRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(caRequest.PublicKey, false));
        // Reloaded via X509CertificateLoader.LoadPkcs12(..., X509KeyStorageFlags.Exportable)
        // rather than kept as the CreateSelfSigned result directly. On Windows, SChannel refuses
        // to use a certificate backed by an ephemeral CNG key as a TLS credential
        // ("AuthenticationException: the platform does not support ephemeral keys"), and every RSA
        // key produced by RSA.Create() on Windows is ephemeral; that "ephemeral-ness" survives
        // CopyWithPrivateKey/CreateSelfSigned regardless of which storage flags are used later on
        // the same in-memory object. Reloading from a fresh PFX export gives the certificate its
        // own non-ephemeral key copy, which both SChannel (server credential) and OpenSSL (Linux)
        // accept. See the identical reasoning in CreateSignedCertificate below.
        using X509Certificate2 generatedCa = caRequest.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(2));
        X509Certificate2 ca = X509CertificateLoader.LoadPkcs12(
            generatedCa.Export(X509ContentType.Pfx),
            string.Empty,
            X509KeyStorageFlags.Exportable);

        (X509Certificate2 server, RSA serverKey) = CreateSignedCertificate(
            ca,
            serverHostName,
            ServerAuthenticationOid);
        (X509Certificate2 client, RSA clientKey) = CreateSignedCertificate(
            ca,
            "BastionVault Test Client",
            ClientAuthenticationOid);

        string caPemPath = Path.Combine(directory, "ca.pem");
        File.WriteAllText(caPemPath, ca.ExportCertificatePem());
        File.WriteAllText(Path.Combine(directory, "ca-key.pem"), caKey.ExportRSAPrivateKeyPem());
        File.WriteAllBytes(Path.Combine(directory, "server.pfx"), server.Export(X509ContentType.Pfx));
        File.WriteAllText(Path.Combine(directory, "server-key.pem"), serverKey.ExportRSAPrivateKeyPem());
        File.WriteAllBytes(Path.Combine(directory, "client.pfx"), client.Export(X509ContentType.Pfx));
        File.WriteAllText(Path.Combine(directory, "client-key.pem"), clientKey.ExportRSAPrivateKeyPem());
        return (ca, caKey, server, serverKey, client, clientKey, caPemPath);
    }

    private static (X509Certificate2 Certificate, RSA Key) CreateSignedCertificate(
        X509Certificate2 ca,
        string commonName,
        string ekuOid)
    {
        RSA key = RSA.Create(2048);
        CertificateRequest request = new($"CN={commonName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            true));
        OidCollection enhancedKeyUsages = new();
        enhancedKeyUsages.Add(new Oid(ekuOid));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(enhancedKeyUsages, true));
        if (ekuOid == ServerAuthenticationOid)
        {
            SubjectAlternativeNameBuilder san = new();
            if (IPAddress.TryParse(commonName, out IPAddress? ip))
            {
                san.AddIpAddress(ip);
            }
            else
            {
                san.AddDnsName(commonName);
            }

            request.CertificateExtensions.Add(san.Build());
        }

        DateTimeOffset notBefore = DateTimeOffset.UtcNow.AddMinutes(-1);
        DateTimeOffset notAfter = ca.NotAfter.ToUniversalTime().AddMinutes(-1);
        X509Certificate2 publicCertificate = request.Create(
            ca,
            notBefore,
            notAfter,
            RandomNumberGenerator.GetBytes(16));
        X509Certificate2 withPrivateKey = publicCertificate.CopyWithPrivateKey(key);
        publicCertificate.Dispose();

        // See the comment in CreateCertificates: reload without leaving the certificate tied to an
        // ephemeral CNG key, so it is a valid Kestrel/SslStream TLS credential on Windows as well as
        // Linux/macOS. "key" itself is returned separately (for the *-key.pem export) and is left
        // untouched here.
        X509Certificate2 reloaded = X509CertificateLoader.LoadPkcs12(
            withPrivateKey.Export(X509ContentType.Pfx),
            string.Empty,
            X509KeyStorageFlags.Exportable);
        withPrivateKey.Dispose();
        return (reloaded, key);
    }

    private static bool IsIssuedBy(X509Certificate2 certificate, X509Certificate2 ca)
    {
        using X509Chain chain = new();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(ca);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.Build(certificate);
    }

    private void DisposeCertificates()
    {
        clientCertificate.Dispose();
        clientKey.Dispose();
        serverCertificate.Dispose();
        serverKey.Dispose();
        caCertificate.Dispose();
        caKey.Dispose();
    }

    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";
    private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";
}
