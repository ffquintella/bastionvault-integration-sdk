using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// AUT-070's certificate (mTLS) auth method, reached from <see cref="AuthOperations.Cert"/>.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>The <c>cert</c> backend is disabled in current server builds: it registers no paths.</b>
/// <see cref="LoginAsync"/> exists because AUT-070 requires it to, and because the backend may be
/// enabled in a future build — but against every server shipping today it fails, deliberately and
/// legibly, with <c>BV-SERVER-004 UnsupportedByServer</c> rather than a bare not-found.
/// </para>
/// <para>
/// The <i>other</i> half of certificate authentication is not disabled and is not this method:
/// presenting a client certificate at the TLS layer is CFG-044's <c>ClientCertPath</c> /
/// <c>ClientKeyPath</c>, which are honoured on every connection the SDK makes, to every endpoint,
/// whether or not this login is ever called. An operator whose deployment authenticates by client
/// certificate configures those two and uses the token the server issues by its own means; they do
/// not need this method at all.
/// </para>
/// </remarks>
public sealed class CertOperations
{
    private readonly LoginRunner runner;
    private readonly ClientContext context;

    internal CertOperations(ClientContext context, string activeNamespace)
    {
        this.context = context;
        runner = new LoginRunner(context, activeNamespace);
    }

    /// <summary>
    /// AUT-070: <c>POST auth/{mount}/login</c>, with the client certificate presented at the TLS
    /// layer by CFG-044 rather than in the body.
    /// </summary>
    /// <param name="mount">The auth mount path segment. Default <c>cert</c>.</param>
    /// <param name="options">Per-request options (CFG-060).</param>
    /// <param name="cancellationToken">Runtime cancellation.</param>
    /// <exception cref="BastionVaultException">
    /// <c>BV-SERVER-004 UnsupportedByServer</c> on every current server, with a hint naming the
    /// disabled backend (AUT-070).
    /// </exception>
    public async Task<AuthInfo> LoginAsync(
        string mount = "cert",
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        string encoded = AuthEndpoint.Mount(mount);
        try
        {
            return await runner.LoginAsync(
                $"auth/{encoded}/login",
                AuthEndpoint.JsonObject(),
                install: true,
                options,
                cancellationToken).ConfigureAwait(false);
        }
        catch (BastionVaultException failure) when (IsDisabledBackend(failure))
        {
            throw Unsupported(failure, mount);
        }
    }

    /// <summary>
    /// AUT-070's two server answers, recognised by the <b>code the shared table already produced</b>
    /// rather than by re-matching the message text here (D-M6-8).
    /// </summary>
    /// <remarks>
    /// Appendix B §2 maps <c>logical backend path not supported</c> → <c>BV-SERVER-004</c> and
    /// <c>router mount not found</c> → <c>BV-NOTFOUND-002</c>. The second is the right answer
    /// everywhere else in the SDK — a missing mount <i>is</i> a not-found — and AUT-070 overrides it
    /// only on this path, where the mount is missing because the backend is disabled rather than
    /// because the operator mistyped it. The override is therefore scoped to
    /// <c>Auth.Cert.Login</c> and the generated table is untouched: widening the table would
    /// mis-map every other <c>router mount not found</c> in the SDK, and it is a regenerated
    /// artefact this tree does not author.
    /// </remarks>
    private static bool IsDisabledBackend(BastionVaultException failure)
    {
        return failure.Code is ErrorCodes.NotFoundMountNotFound or ErrorCodes.ServerUnsupportedByServer;
    }

    private BastionVaultException Unsupported(BastionVaultException failure, string mount)
    {
        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.ServerUnsupportedByServer);
        Dictionary<string, object?> details = new(failure.Details, StringComparer.Ordinal)
        {
            ["backend"] = mount,
        };

        return BastionVaultException.Request(
            ErrorCodes.ServerUnsupportedByServer,
            entry.Category,
            entry.Message,
            $"{entry.Hint} The `{mount}` auth backend is disabled in current server builds — it registers no "
                + "paths, so this login cannot succeed. Present the client certificate at the TLS layer with "
                + "`ClientCertPath`/`ClientKeyPath` (CFG-044) and obtain a token by another auth method.",
            retryable: entry.Retryable,
            attempts: failure.Attempts,
            serverMessage: failure.ServerMessage,
            serverErrors: failure.ServerErrors,
            statusCode: failure.StatusCode,
            method: failure.Method,
            path: failure.Path,
            address: context.Config.Address,
            details: details).MarkRecognizedAtSource();
    }
}
