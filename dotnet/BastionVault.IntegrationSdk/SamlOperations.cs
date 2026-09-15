using System.Diagnostics.CodeAnalysis;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>AUT-060's <c>Auth.Saml.Login</c> answer: where to send the operator, and what to correlate on.</summary>
public sealed class SamlLoginRequest
{
    /// <summary>The identity provider's single sign-on URL to open in a browser.</summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "AUT-060 names these members and the SDK treats their values as opaque text: an authorisation URL is handed to the operator's browser and a redirect URI is echoed to the server, and the SDK parses neither. System.Uri has no counterpart in the Rust and Python SDKs, so typing them here would make the .NET signature the odd one out for no behavioural gain (CLA-003).")]
    public required string SsoUrl { get; init; }

    /// <summary>The relay state to hand back to <see cref="SamlOperations.CallbackAsync"/>.</summary>
    public string? RelayState { get; init; }

    /// <summary>The SAML request id, for correlating the assertion with this request.</summary>
    public string? RequestId { get; init; }
}

/// <summary>
/// AUT-060's SAML method, reached from <see cref="AuthOperations.Saml"/>.
/// </summary>
/// <remarks>
/// Browser-mediated exactly as <see cref="OidcOperations"/> is, and with the same loopback recipe:
/// <see cref="LoginAsync"/> returns the identity provider's URL, the application opens it and
/// listens on its own loopback <c>redirectUri</c>, and the <c>SAMLResponse</c> and
/// <c>RelayState</c> the provider posts back go to <see cref="CallbackAsync"/>. The SDK opens no
/// browser and runs no listener.
/// </remarks>
public sealed class SamlOperations
{
    private readonly AuthEndpoint endpoint;
    private readonly LoginRunner runner;

    internal SamlOperations(ClientContext context, string activeNamespace)
    {
        endpoint = new AuthEndpoint(context, activeNamespace);
        runner = new LoginRunner(context, activeNamespace);
        Admin = new AuthRoleAdminOperations(context, activeNamespace, "saml");
    }

    /// <summary>AUT-060's role and config administration.</summary>
    public AuthRoleAdminOperations Admin { get; }

    /// <summary>
    /// AUT-060: <c>POST auth/{mount}/login</c>, unauthenticated. Despite the path, this is
    /// <b>not</b> the login — it starts the browser round trip and returns no token.
    /// <see cref="CallbackAsync"/> is the login.
    /// </summary>
    /// <param name="redirectUri">The loopback URI the provider posts the assertion back to.</param>
    /// <param name="role">The SAML role; the mount's default role when omitted.</param>
    /// <param name="mount">The auth mount path segment. Default <c>saml</c>.</param>
    /// <param name="options">Per-request options (CFG-060).</param>
    /// <param name="cancellationToken">Runtime cancellation.</param>
    /// <exception cref="BastionVaultException"><c>BV-PROTOCOL-002</c> when the response carries no <c>data.sso_url</c>.</exception>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "AUT-060 names these members and the SDK treats their values as opaque text: an authorisation URL is handed to the operator's browser and a redirect URI is echoed to the server, and the SDK parses neither. System.Uri has no counterpart in the Rust and Python SDKs, so typing them here would make the .NET signature the odd one out for no behavioural gain (CLA-003).")]
    public async Task<SamlLoginRequest> LoginAsync(
        string? redirectUri = null,
        string? role = null,
        string mount = "saml",
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        string path = $"auth/{AuthEndpoint.Mount(mount)}/login";
        Response? response = await endpoint.WriteTokenlessAsync(
            path,
            AuthEndpoint.JsonObject(("redirect_uri", redirectUri), ("role", role)),
            options,
            cancellationToken).ConfigureAwait(false);

        return new SamlLoginRequest
        {
            SsoUrl = AuthEndpoint.ReadString(response, "sso_url") ?? throw endpoint.EnvelopeMismatch(path, "data.sso_url"),
            RelayState = AuthEndpoint.ReadString(response, "relay_state"),
            RequestId = AuthEndpoint.ReadString(response, "request_id"),
        };
    }

    /// <summary>
    /// AUT-060: <c>POST auth/{mount}/callback</c>. This is the login, so the whole login response
    /// contract (AUT-010…AUT-013) applies and no token header is sent (TRN-015).
    /// </summary>
    /// <param name="samlResponse">The base64 <c>SAMLResponse</c> the provider posted back.</param>
    /// <param name="relayState">The relay state from <see cref="LoginAsync"/>.</param>
    /// <param name="mount">The auth mount path segment. Default <c>saml</c>.</param>
    /// <param name="options">Per-request options (CFG-060).</param>
    /// <param name="cancellationToken">Runtime cancellation.</param>
    public Task<AuthInfo> CallbackAsync(
        string samlResponse,
        string relayState,
        string mount = "saml",
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(samlResponse);
        ArgumentException.ThrowIfNullOrWhiteSpace(relayState);
        return runner.LoginAsync(
            $"auth/{AuthEndpoint.Mount(mount)}/callback",
            AuthEndpoint.JsonObject(("saml_response", samlResponse), ("relay_state", relayState)),
            install: true,
            options,
            cancellationToken);
    }
}
