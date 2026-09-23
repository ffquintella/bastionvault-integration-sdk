using System.Diagnostics.CodeAnalysis;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// AUT-060's OIDC method (<c>05-authentication.md</c> §Method: OIDC and SAML), reached from
/// <see cref="AuthOperations.Oidc"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>This flow requires a browser, and the SDK does not open one.</b> AUT-060 asks for the two
/// endpoints and a documented loopback recipe, not for an embedded user agent: the application
/// calls <see cref="AuthUrlAsync"/>, sends the operator to that URL, runs a loopback HTTP listener
/// on the <c>redirectUri</c> it supplied, and hands the <c>state</c> and <c>code</c> it receives
/// back to <see cref="CallbackAsync"/>.
/// </para>
/// <para>
/// The loopback recipe: bind <c>http://127.0.0.1:0</c> to get an ephemeral port, build the
/// redirect URI from the bound port, pass it to <see cref="AuthUrlAsync"/>, open the returned URL,
/// accept exactly one request, read <c>state</c> and <c>code</c> from its query string, answer the
/// browser, close the listener, then call <see cref="CallbackAsync"/>. Bind to the loopback
/// interface only, and compare the returned <c>state</c> to the one the authorisation URL carried
/// before using the <c>code</c>.
/// </para>
/// </remarks>
public sealed class OidcOperations
{
    private readonly AuthEndpoint endpoint;
    private readonly LoginRunner runner;

    internal OidcOperations(ClientContext context, string activeNamespace)
    {
        endpoint = new AuthEndpoint(context, activeNamespace);
        runner = new LoginRunner(context, activeNamespace);
        Admin = new AuthRoleAdminOperations(context, activeNamespace, "oidc");
    }

    /// <summary>AUT-060's role and config administration.</summary>
    public AuthRoleAdminOperations Admin { get; }

    /// <summary>
    /// AUT-060: <c>POST auth/{mount}/auth_url</c>, unauthenticated, returning the authorisation
    /// URL to send the operator to.
    /// </summary>
    /// <param name="redirectUri">The loopback URI the provider redirects back to.</param>
    /// <param name="role">The OIDC role; the mount's default role when omitted.</param>
    /// <param name="mount">The auth mount path segment. Default <c>oidc</c>.</param>
    /// <param name="options">Per-request options (CFG-060).</param>
    /// <param name="cancellationToken">Runtime cancellation.</param>
    /// <exception cref="BastionVaultException"><c>BV-PROTOCOL-002</c> when the response carries no <c>data.auth_url</c>.</exception>
    /// <remarks>Wire params: <c>redirect_uri</c>, <c>role</c> (optional). Returns the authorisation URL as a string, never <see langword="null"/> (throws instead). Conformance: Shared (AUT-060). Errors beyond the common set (ERR-061): <c>BV-PROTOCOL-002</c>.</remarks>
    /// <spec>Auth.Oidc.AuthUrl — AUT-060</spec>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "AUT-060 names these members and the SDK treats their values as opaque text: an authorisation URL is handed to the operator's browser and a redirect URI is echoed to the server, and the SDK parses neither. System.Uri has no counterpart in the Rust and Python SDKs, so typing them here would make the .NET signature the odd one out for no behavioural gain (CLA-003).")]
    public async Task<string> AuthUrlAsync(
        string redirectUri,
        string? role = null,
        string mount = "oidc",
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);
        string path = $"auth/{AuthEndpoint.Mount(mount)}/auth_url";
        Response? response = await endpoint.WriteTokenlessAsync(
            path,
            AuthEndpoint.JsonObject(("redirect_uri", redirectUri), ("role", role)),
            options,
            cancellationToken).ConfigureAwait(false);

        return AuthEndpoint.ReadString(response, "auth_url") ?? throw endpoint.EnvelopeMismatch(path, "data.auth_url");
    }

    /// <summary>
    /// AUT-060: <c>POST auth/{mount}/callback</c>. This is the login, so the whole login response
    /// contract (AUT-010…AUT-013) applies and no token header is sent (TRN-015).
    /// </summary>
    /// <param name="state">The <c>state</c> the provider returned; compare it to the one you sent.</param>
    /// <param name="code">The authorisation code the provider returned.</param>
    /// <param name="mount">The auth mount path segment. Default <c>oidc</c>.</param>
    /// <param name="options">Per-request options (CFG-060).</param>
    /// <param name="cancellationToken">Runtime cancellation.</param>
    /// <remarks>Wire params: <c>state</c>, <c>code</c>. Returns <see cref="AuthInfo"/>, never <see langword="null"/>. Conformance: Shared (AUT-060). Errors beyond the common set (ERR-061): AUT-010…AUT-013's login-response refinements.</remarks>
    /// <spec>Auth.Oidc.Callback — AUT-060</spec>
    public Task<AuthInfo> CallbackAsync(
        string state,
        string code,
        string mount = "oidc",
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return runner.LoginAsync(
            $"auth/{AuthEndpoint.Mount(mount)}/callback",
            AuthEndpoint.JsonObject(("state", state), ("code", code)),
            install: true,
            options,
            cancellationToken);
    }
}
