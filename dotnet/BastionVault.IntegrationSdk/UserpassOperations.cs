using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// The Userpass auth method (<c>05-authentication.md</c> §Method: Userpass, AUT-030…AUT-032),
/// reached from <see cref="AuthOperations.Userpass"/>.
/// </summary>
/// <remarks>
/// AUT-035's FIDO2 pair (<see cref="Fido2LoginBeginAsync"/>, <see cref="Fido2LoginCompleteAsync"/>)
/// lands here in M6, on the userpass mount's own <c>auth/{mount}/fido2/login/{begin,complete}</c>
/// paths (Appendix A). The standalone <c>fido2</c> mount's identical flow is
/// <see cref="AuthOperations.Fido2"/>; one implementation drives both. Appendix A's own
/// <c>Auth.Userpass.Admin.*</c> surface — <see cref="UserpassAdminOperations.ListUsersInfoAsync"/>
/// included, moved here by R-29 (D-M10-3) — is <see cref="Admin"/>.
/// </remarks>
public sealed class UserpassOperations
{
    private readonly LoginRunner runner;
    private readonly Fido2LoginFlow fido2;

    internal UserpassOperations(ClientContext context, string activeNamespace)
    {
        runner = new LoginRunner(context, activeNamespace);
        fido2 = new Fido2LoginFlow(context, activeNamespace, standalone: false);
        Admin = new UserpassAdminOperations(context, activeNamespace);
    }

    /// <summary>Appendix A's <c>Auth.Userpass.Admin.*</c> surface.</summary>
    public UserpassAdminOperations Admin { get; }

    /// <summary>
    /// AUT-030: <c>POST auth/{mount}/login/{username}</c> with body
    /// <c>{"password": "…", "totp_code": "…"?}</c>. On success the client holds the new token; on
    /// any rejection it holds none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The username is URL-path-encoded by the one encoder every path goes through (TRN-020), and
    /// <c>totp_code</c> is <b>omitted</b> from the body when <paramref name="totpCode"/> is absent
    /// rather than sent as an empty string.
    /// </para>
    /// <para>
    /// <b>AUT-032: a locked account is not fixed by retrying, and this SDK does not retry it.</b>
    /// <c>BV-AUTH-006 AccountLocked</c> arrives as an HTTP <c>200</c> (the login-response contract),
    /// so it never reaches the CFG-051…055 retry loop's failure path at all, and the code is
    /// <c>Retryable = false</c> in the generated catalogue because ERR-006's retryable set does not
    /// contain it. Wait <c>Details.retry_after_secs</c> seconds, or ask an administrator to unlock
    /// the account; a further attempt before then extends the lockout rather than shortening it.
    /// </para>
    /// <para>
    /// <b>AUT-100: the credentials are not retained.</b> A successful login installs a
    /// <see cref="TokenSourceKind.Static"/> source holding the issued token, and
    /// <paramref name="password"/> is referenced only for the duration of the call. An application
    /// that wants the SDK to be able to log in again — AUT-002's lazy login, AUT-003's re-login —
    /// installs a <see cref="TokenSource.Login"/> source instead, which is the one place AUT-100
    /// allows credentials to be kept.
    /// </para>
    /// </remarks>
    /// <param name="username">The account name; URL-path-encoded when sent (AUT-030).</param>
    /// <param name="password">The password, in a redacting type (AUT-031).</param>
    /// <param name="totpCode">The TOTP code, when the account requires one (<c>BV-AUTH-007</c>).</param>
    /// <param name="mount">The auth mount path segment. Default <c>userpass</c>.</param>
    /// <param name="options">Per-request options (CFG-060).</param>
    /// <param name="cancellationToken">Runtime cancellation.</param>
    /// <exception cref="BastionVaultException">
    /// <c>BV-AUTH-003</c> and its AUT-011 refinements (<c>BV-AUTH-004</c>…<c>BV-AUTH-009</c>,
    /// <c>BV-AUTH-012</c>…<c>BV-AUTH-014</c>, <c>BV-RATE-001</c>) for a rejected login.
    /// </exception>
    public Task<AuthInfo> LoginAsync(
        string username,
        SecretString password,
        string? totpCode = null,
        string mount = "userpass",
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return runner.LoginAsync(
                LoginCredentials.ForUserpass(username, password, totpCode, mount),
                install: true,
                options,
                cancellationToken);
    }

    /// <summary>
    /// AUT-035: <c>POST auth/{mount}/fido2/login/begin</c>, unauthenticated, returning the
    /// server's WebAuthn assertion options uninterpreted.
    /// </summary>
    /// <remarks>
    /// An account whose password login has been disabled in favour of a security key answers a
    /// <see cref="LoginAsync"/> attempt with <c>BV-AUTH-009</c> (AUT-011); this pair is what that
    /// code points the caller at.
    /// </remarks>
    /// <param name="username">The account the assertion is being requested for.</param>
    /// <param name="mount">The auth mount path segment. Default <c>userpass</c>.</param>
    /// <param name="options">Per-request options (CFG-060).</param>
    /// <param name="cancellationToken">Runtime cancellation.</param>
    public Task<WebAuthnAssertionOptions> Fido2LoginBeginAsync(
        string username,
        string mount = "userpass",
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return fido2.BeginAsync(username, mount, options, cancellationToken);
    }

    /// <summary>
    /// AUT-035: <c>POST auth/{mount}/fido2/login/complete</c>. Completion follows the login
    /// response contract, so every AUT-010…AUT-013 rule applies unchanged.
    /// </summary>
    /// <param name="username">The account the assertion belongs to.</param>
    /// <param name="credentialJson">The authenticator's response, as opaque JSON (AUT-035).</param>
    /// <param name="mount">The auth mount path segment. Default <c>userpass</c>.</param>
    /// <param name="options">Per-request options (CFG-060).</param>
    /// <param name="cancellationToken">Runtime cancellation.</param>
    /// <exception cref="BastionVaultException">
    /// <c>BV-INPUT-001</c> when <paramref name="credentialJson"/> is not well-formed JSON;
    /// <c>BV-AUTH-003</c> and its AUT-011 refinements for a rejected assertion.
    /// </exception>
    public Task<AuthInfo> Fido2LoginCompleteAsync(
        string username,
        string credentialJson,
        string mount = "userpass",
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return fido2.CompleteAsync(username, credentialJson, mount, options, cancellationToken);
    }
}
