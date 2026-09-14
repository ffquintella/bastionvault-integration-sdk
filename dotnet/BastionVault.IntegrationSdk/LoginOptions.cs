namespace BastionVault.IntegrationSdk;

/// <summary>
/// AUT-003's two opt-in settings for a <see cref="TokenSourceKind.Login"/> token source, pinned by
/// <c>decisions/0006-m2-authentication.md</c> D-M2-6.
/// </summary>
/// <remarks>
/// <para>
/// These belong to the <b>source</b>, not to a request and not to a one-shot
/// <c>Auth.Userpass.Login</c> call: re-login-and-replay only means anything when the SDK holds the
/// credentials it would re-login with, which is exactly the <see cref="TokenSourceKind.Login"/>
/// case AUT-100 carves out. A one-shot login installs a
/// <see cref="TokenSourceKind.Static"/> source and drops its credentials, so there is nothing for
/// these settings to govern there.
/// </para>
/// <para>
/// <see cref="ReloginOnPermissionDenied"/> defaults to <see langword="false"/> because AUT-003 says
/// it must: a <c>403</c> also means "policy does not allow", and re-logging-in in response to that
/// turns a clear authorization failure into two requests and a confusing one.
/// </para>
/// </remarks>
public sealed record LoginOptions
{
    /// <summary>
    /// AUT-003's opt-in. When <see langword="true"/>, a <c>BV-AUTHZ-001</c> on an idempotent
    /// request whose token is older than <see cref="MinReloginInterval"/> causes one re-login and
    /// one replay. Default <see langword="false"/>.
    /// </summary>
    public bool ReloginOnPermissionDenied { get; init; }

    /// <summary>
    /// AUT-003's floor: a token younger than this is never re-logged-in, so a genuine policy denial
    /// cannot become a login loop. Default 30 seconds.
    /// </summary>
    public TimeSpan MinReloginInterval { get; init; } = TimeSpan.FromSeconds(30);
}
