using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// The production <see cref="ITransport"/> (D-M1b-3): <see cref="SocketsHttpHandler"/> +
/// <see cref="System.Net.Http.HttpClient"/>, sending the literal <c>LIST</c> verb via
/// <c>new HttpMethod("LIST")</c>. Built once per <see cref="BastionVaultClient"/> from the TLS
/// material <c>ClientConfig</c> already materialised at construction (CFG-040..044): redirects are
/// never followed (TRN-060, CNF-034), the system proxy is off unless <see cref="ClientConfig.UseSystemProxy"/>
/// is set (TRN-091), and connections are pooled/reused across requests of one client (TRN-090).
/// </summary>
public sealed class HttpClientTransport : ITransport, IDisposable
{
    private readonly HttpClient client;

    /// <summary>Builds the transport from a resolved <see cref="ClientConfig"/>.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The handler is handed to HttpClient with disposeHandler: true; HttpClient owns and disposes it.")]
    public HttpClientTransport(ClientConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        SocketsHttpHandler handler = new()
        {
            AllowAutoRedirect = false, // TRN-060, CNF-034: the SDK maps 3xx itself and never follows it.
            UseProxy = config.UseSystemProxy, // TRN-091: disabled by default.
            UseCookies = false, // TRN-016: the SDK maintains no cookie jar.
            PooledConnectionLifetime = Timeout.InfiniteTimeSpan, // TRN-090: reuse connections across requests.
            ConnectTimeout = config.ConnectTimeout,
            SslOptions =
            {
                EnabledSslProtocols = SslProtocols.None, // let the OS negotiate >= the minimum below.
            },
        };

        if (config.UseSystemProxy)
        {
            handler.Proxy = HttpClient.DefaultProxy;
        }

        BuildTlsOptions(handler, config);
        client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan, // per-attempt timeout is enforced per-request below (RES-004).
        };
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5359:Do not disable certificate validation",
        Justification = "Gated by ClientConfig.TlsSkipVerify (CFG-018), which already logged a CNF-030 warning and set IsInsecure=true at construction; this is the documented, opt-in insecure path.")]
    private static void BuildTlsOptions(SocketsHttpHandler handler, ClientConfig config)
    {
        handler.SslOptions.AllowRenegotiation = false;
        if (config.TlsSkipVerify)
        {
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        }
        else if (config.CaCertificates is { Count: > 0 } caCertificates)
        {
            X509Certificate2Collection trusted = caCertificates;
            bool replaceSystemRoots = config.CaCertReplacesSystemRoots;
            handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
            {
                if (certificate is null || (errors & System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
                {
                    return false;
                }

                // CFG-040 "add" semantics: a certificate the platform trust store already accepts is
                // still valid. "Replace" semantics: only CaCertificates/CaCertPem are ever trusted, so
                // the platform-trusted shortcut is skipped and every certificate is checked against
                // the custom root below.
                if (!replaceSystemRoots && errors == System.Net.Security.SslPolicyErrors.None)
                {
                    return true;
                }

                using X509Chain customChain = new();
                customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                customChain.ChainPolicy.CustomTrustStore.AddRange(trusted);
                customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                using X509Certificate2 leaf = new(certificate);
                return customChain.Build(leaf);
            };
        }

        if (config.ClientCertificate is not null)
        {
            handler.SslOptions.ClientCertificates = [config.ClientCertificate]; // CFG-044: presented on every connection.
        }

        if (config.TlsServerName is { Length: > 0 } serverName)
        {
            handler.SslOptions.TargetHost = serverName; // CFG-042: drives both SNI and hostname verification.
        }
    }

    /// <inheritdoc/>
    public bool SupportsCustomVerbs => true;

    /// <inheritdoc/>
    public async Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using HttpRequestMessage message = new(new HttpMethod(request.Method), request.Uri);
        if (!request.Body.IsEmpty)
        {
            message.Content = new ByteArrayContent(request.Body.ToArray());
        }

        foreach ((string name, string value) in request.Headers)
        {
            if (message.Content is not null && IsContentHeader(name))
            {
                message.Content.Headers.TryAddWithoutValidation(name, value);
            }
            else
            {
                message.Headers.TryAddWithoutValidation(name, value);
            }
        }

        using CancellationTokenSource timeoutSource = new(request.Timeout);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw TransportFailureMapper.Map(TransportFailureKind.Timeout);
        }
        catch (System.Net.Sockets.SocketException exception)
        {
            throw TransportFailureMapper.Map(TransportFailureKind.ConnectionRefused, exception);
        }
        catch (AuthenticationException exception)
        {
            throw TransportFailureMapper.Map(TransportFailureKind.TlsVerify, exception);
        }
        catch (HttpRequestException exception)
        {
            throw TransportFailureMapper.Map(
                exception.InnerException is AuthenticationException ? TransportFailureKind.TlsVerify : TransportFailureKind.ConnectionRefused,
                exception);
        }

        using (response)
        {
            // D-M1b-20: TRN-033 says an oversized response MUST be aborted, not audited after an
            // unbounded read. A Content-Length already over the bound is rejected without reading a
            // byte; otherwise the body is read in chunks and aborted the moment the bound is passed,
            // so a malfunctioning or hostile server cannot force an unbounded allocation.
            long? contentLength = response.Content.Headers.ContentLength;
            if (contentLength is { } declared && declared > request.MaxResponseBytes)
            {
                throw TransportFailureMapper.MapResponseTooLarge(request.Method, request.Uri.AbsolutePath, request.Uri.GetLeftPart(UriPartial.Authority), attempts: 1);
            }

            byte[] body = await ReadBoundedAsync(response, request, cancellationToken).ConfigureAwait(false);
            Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }

            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Content.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }

            return new TransportResponse((int)response.StatusCode, headers, body);
        }
    }

    /// <summary>
    /// Reads the response body in bounded chunks, aborting with <c>BV-TRANSPORT-004</c> the moment
    /// more than <see cref="TransportRequest.MaxResponseBytes"/> bytes have been read — the response
    /// is never buffered past the bound (D-M1b-20, TRN-033).
    /// </summary>
    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, TransportRequest request, CancellationToken cancellationToken)
    {
        long limit = request.MaxResponseBytes;
        using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using MemoryStream buffer = new();
        byte[] chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > limit)
            {
                throw TransportFailureMapper.MapResponseTooLarge(request.Method, request.Uri.AbsolutePath, request.Uri.GetLeftPart(UriPartial.Authority), attempts: 1);
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return buffer.ToArray();
    }

    private static bool IsContentHeader(string name) => name is "Content-Type" or "Content-Length";

    /// <inheritdoc/>
    public void Dispose() => client.Dispose();
}
