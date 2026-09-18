using System.Buffers;
using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// AUT-035's WebAuthn assertion options, exactly as the server sent them.
/// </summary>
/// <remarks>
/// <para>
/// AUT-035: "WebAuthn payloads MUST be passed through as opaque JSON; the SDK MUST NOT attempt to
/// interpret them." This type therefore models <b>no</b> WebAuthn field. It carries the server's
/// JSON text verbatim, for the application's WebAuthn library to parse — a typed model here would
/// be the SDK interpreting the payload, and would go stale the moment the server tracks a new
/// level of the WebAuthn specification.
/// </para>
/// <para>
/// <see cref="Json"/> is a string rather than a parsed document because it is the one
/// representation all three SDKs can promise identically (D-M6-3).
/// </para>
/// </remarks>
public sealed class WebAuthnAssertionOptions
{
    /// <summary>The server's assertion-options JSON, verbatim and uninterpreted (AUT-035).</summary>
    public required string Json { get; init; }
}

/// <summary>
/// AUT-035's standalone <c>fido2</c> auth mount, reached from <see cref="AuthOperations.Fido2"/>.
/// </summary>
/// <remarks>
/// The same two-step flow as <see cref="UserpassOperations.Fido2LoginBeginAsync"/>, on the
/// standalone mount's own paths (<c>auth/{mount}/login/{begin,complete}</c>, Appendix A) rather
/// than the userpass mount's <c>auth/{mount}/fido2/login/{begin,complete}</c>. Both are driven by
/// the one implementation in <see cref="Fido2LoginFlow"/>, so the two surfaces cannot diverge.
/// </remarks>
public sealed class Fido2Operations
{
    private readonly Fido2LoginFlow flow;

    internal Fido2Operations(ClientContext context, string activeNamespace)
    {
        flow = new Fido2LoginFlow(context, activeNamespace, standalone: true);
    }

    /// <summary>
    /// AUT-035: <c>POST auth/{mount}/login/begin</c>, unauthenticated, returning the server's
    /// WebAuthn assertion options uninterpreted.
    /// </summary>
    /// <param name="username">The account the assertion is being requested for.</param>
    /// <param name="mount">The auth mount path segment. Default <c>fido2</c>.</param>
    /// <param name="options">Per-request options (CFG-060).</param>
    /// <param name="cancellationToken">Runtime cancellation.</param>
    public Task<WebAuthnAssertionOptions> LoginBeginAsync(
        string username,
        string mount = "fido2",
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return flow.BeginAsync(username, mount, options, cancellationToken);
    }

    /// <summary>
    /// AUT-035: <c>POST auth/{mount}/login/complete</c>. Completion follows the login response
    /// contract, so every AUT-010…AUT-013 rule applies unchanged.
    /// </summary>
    /// <param name="username">The account the assertion belongs to.</param>
    /// <param name="credentialJson">The authenticator's response, as opaque JSON (AUT-035).</param>
    /// <param name="mount">The auth mount path segment. Default <c>fido2</c>.</param>
    /// <param name="options">Per-request options (CFG-060).</param>
    /// <param name="cancellationToken">Runtime cancellation.</param>
    /// <exception cref="BastionVaultException">
    /// <c>BV-INPUT-001</c> when <paramref name="credentialJson"/> is not well-formed JSON;
    /// <c>BV-AUTH-003</c> and its AUT-011 refinements for a rejected assertion.
    /// </exception>
    public Task<AuthInfo> LoginCompleteAsync(
        string username,
        string credentialJson,
        string mount = "fido2",
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return flow.CompleteAsync(username, credentialJson, mount, options, cancellationToken);
    }
}
