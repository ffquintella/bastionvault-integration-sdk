namespace BastionVault.IntegrationSdk;

/// <summary>Which login flow a <see cref="TokenSourceKind.Login"/> source performs (AUT-001, D-M2-6).</summary>
/// <remarks>
/// Only the two methods M2b implements are members. The remaining flows of
/// <c>05-authentication.md</c> (FIDO2, FerroGate, OIDC, SAML, Cert) are deferred to M6 by D-M2-5,
/// and an enum member with no login behind it would be a stub (D-M1c-25). Adding a member later is
/// not a breaking change; shipping one that throws would be.
/// </remarks>
public enum AuthMethod
{
    /// <summary>Username and password, optionally with a TOTP code (AUT-030…AUT-032).</summary>
    Userpass,

    /// <summary>AppID (wire type <c>approle</c>): role id, secret id, machine token (AUT-040…AUT-042).</summary>
    AppId,
}

/// <summary>
/// The credentials a <see cref="TokenSourceKind.Login"/> source logs in with (AUT-001, D-M2-6),
/// built through <see cref="ForUserpass"/> or <see cref="ForAppId"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the one type in the SDK that <b>retains</b> credential material, and it does so only
/// because AUT-100 says it may: "credentials MUST NOT be retained after the login completes unless
/// the source is <c>Login</c> (needed for re-login), in which case they MUST be held in redacting
/// types". Every secret member is therefore a <see cref="SecretString"/>, and
/// <see cref="ToString"/> is redacted (CNF-031, CNF-032) so an instance cannot reach a log through
/// string interpolation.
/// </para>
/// <para>
/// A one-shot <c>Auth.Userpass.Login</c> / <c>Auth.AppId.Login</c> call does not construct one of
/// these: it passes its arguments straight to the request and installs a
/// <see cref="TokenSourceKind.Static"/> source, which is AUT-100's default arm.
/// </para>
/// </remarks>
public sealed class LoginCredentials
{
    /// <remarks>
    /// Get-only properties assigned from one private constructor, rather than <c>private init</c>
    /// accessors: <c>PublicApiSurfaceScanner</c> reports a property with any setter as
    /// <c>{get/set}</c>, so an <c>init</c> the caller cannot reach would still read as a settable
    /// public member in the CNF-027 baseline. These credentials are immutable once built and the
    /// baseline now says so.
    /// </remarks>
    private LoginCredentials(
        AuthMethod method,
        string mount,
        string? username = null,
        SecretString? password = null,
        string? totpCode = null,
        string? roleId = null,
        SecretString? secretId = null,
        SecretString? machineToken = null)
    {
        Method = method;
        Mount = mount;
        Username = username;
        Password = password;
        TotpCode = totpCode;
        RoleId = roleId;
        SecretId = secretId;
        MachineToken = machineToken;
    }

    /// <summary>Which flow these credentials are for.</summary>
    public AuthMethod Method { get; }

    /// <summary>The auth mount path segment (<c>userpass</c>, <c>approle</c>, or a non-default mount).</summary>
    public string Mount { get; }

    /// <summary><see cref="AuthMethod.Userpass"/>: the username, URL-path-encoded when sent (AUT-030).</summary>
    public string? Username { get; }

    /// <summary><see cref="AuthMethod.Userpass"/>: the password, in a redacting type (AUT-031).</summary>
    public SecretString? Password { get; }

    /// <summary><see cref="AuthMethod.Userpass"/>: the TOTP code, omitted from the body when absent (AUT-030).</summary>
    public string? TotpCode { get; }

    /// <summary><see cref="AuthMethod.AppId"/>: the role id.</summary>
    public string? RoleId { get; }

    /// <summary><see cref="AuthMethod.AppId"/>: the secret id, in a redacting type (CNF-031).</summary>
    public SecretString? SecretId { get; }

    /// <summary><see cref="AuthMethod.AppId"/>: the FerroGate machine token, sent only when supplied (AUT-040).</summary>
    public SecretString? MachineToken { get; }

    /// <summary>Credentials for <c>POST auth/{mount}/login/{username}</c> (AUT-030).</summary>
    /// <exception cref="ArgumentException"><paramref name="username"/> or <paramref name="mount"/> is empty or whitespace.</exception>
    public static LoginCredentials ForUserpass(
        string username,
        SecretString password,
        string? totpCode = null,
        string mount = "userpass")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(mount);
        return new LoginCredentials(AuthMethod.Userpass, mount, username: username, password: password, totpCode: totpCode);
    }

    /// <summary>Credentials for <c>POST auth/{mount}/login</c> (AUT-040).</summary>
    /// <exception cref="ArgumentException"><paramref name="roleId"/> or <paramref name="mount"/> is empty or whitespace.</exception>
    public static LoginCredentials ForAppId(
        string roleId,
        SecretString? secretId = null,
        SecretString? machineToken = null,
        string mount = "approle")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mount);
        return new LoginCredentials(AuthMethod.AppId, mount, roleId: roleId, secretId: secretId, machineToken: machineToken);
    }

    /// <summary>
    /// Always redacted (CNF-031, CNF-032). The method and mount are not secret, but they are
    /// withheld anyway: a partially revealing <c>ToString</c> invites the next reader to add one
    /// more field to it, and D-M2-3 fixed <c>[REDACTED]</c> as the marker in all three languages.
    /// </summary>
    public override string ToString() => "[REDACTED]";
}
