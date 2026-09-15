using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// The <c>Auth</c> area (OVR-008), reached from <see cref="BastionVaultClient.Auth"/>: the token
/// source (AUT-001), the current credential (AUT-004), the token-store operations
/// (<see cref="Token"/>) and the token-helper write path (CFG-031, CFG-032).
/// </summary>
/// <remarks>
/// This is the project's first sub-API grouping, so it fixes the shape every later engine grouping
/// copies. It is a lightweight view over the shared <c>ClientContext</c>, exactly as
/// <see cref="BastionVaultClient.Logical"/> is: constructing one allocates nothing that outlives
/// the call and reads no state, so a <see cref="BastionVaultClient.WithNamespace"/> view's
/// <c>Auth</c> sees the same token cell as its parent's (CFG-071).
/// </remarks>
public sealed class AuthOperations
{
    private readonly ClientContext context;

    internal AuthOperations(ClientContext context, string activeNamespace)
    {
        this.context = context;
        Token = new TokenOperations(context, new LogicalOperations(context, activeNamespace));
        Userpass = new UserpassOperations(context, activeNamespace);
        AppId = new AppIdOperations(context, activeNamespace);
    }

    /// <summary>
    /// AUT-001: the one <see cref="IntegrationSdk.TokenSource"/> this client holds.
    /// <see cref="BastionVaultClient.SetToken"/> and <see cref="TokenOperations.Use"/> replace it
    /// with a <see cref="TokenSourceKind.Static"/> one.
    /// </summary>
    public TokenSource TokenSource => context.TokenSource;

    /// <summary>
    /// AUT-004: the current token as a redacting secret type, or <see langword="null"/> when the
    /// client holds none. Reading this never performs a resolution, so it can neither trigger a
    /// login nor call an application callback.
    /// </summary>
    public SecretString? CurrentToken => context.CurrentToken;

    /// <summary>AUT-004: the last <see cref="TokenOperations.LookupSelfAsync"/> result, if any.</summary>
    public TokenInfo? TokenInfo => context.TokenInfo;

    /// <summary>The token-store operations (AUT-020, AUT-080…AUT-085).</summary>
    public TokenOperations Token { get; }

    /// <summary>The Userpass auth method (AUT-030…AUT-032).</summary>
    public UserpassOperations Userpass { get; }

    /// <summary>The AppID auth method, wire type <c>approle</c> (AUT-040…AUT-042, AUT-044).</summary>
    public AppIdOperations AppId { get; }

    /// <summary>
    /// AUT-002's <b>eager</b> login: forces a <see cref="TokenSourceKind.Login"/> source to log in
    /// now rather than on the first authenticated request, so a bad credential surfaces at startup
    /// instead of inside the first operation that needs a token.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The documented answer to AUT-002's "the SDK MUST document which" is lazy</b> (D-M2-9):
    /// a <see cref="TokenSource.Login"/> source performs its login on the first authenticated
    /// request. This method exists only to force it, and it is single-flighted with that lazy
    /// path, so calling it concurrently with a request performs <i>one</i> login (D-M2-11(a)).
    /// </para>
    /// <para>
    /// Calling it repeatedly does not log in repeatedly: the source caches its token, and only
    /// <c>Invalidate</c> (AUT-003's re-login, AUT-093's renewal recovery) discards it.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Runtime cancellation. Cancelling abandons only this caller's wait, never the shared login.</param>
    /// <exception cref="BastionVaultException">
    /// <c>BV-INPUT-001</c> when the client's source is not a <see cref="TokenSourceKind.Login"/>
    /// one — there is no login to force — and the login's own coded failure otherwise
    /// (AUT-010…AUT-012).
    /// </exception>
    public async Task<AuthInfo> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        if (context.TokenSource.Kind != TokenSourceKind.Login)
        {
            ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.InputInvalidArgument);
            throw BastionVaultException.Request(
                ErrorCodes.InputInvalidArgument,
                entry.Category,
                entry.Message,
                entry.Hint,
                retryable: false,
                attempts: 0,
                details: new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["argument"] = "TokenSource",
                    ["reason"] = "Authenticate forces a Login token source to log in; this client's source is "
                        + $"{context.TokenSource.Kind}, which has no login to perform.",
                });
        }

        _ = await context.ResolveTokenAsync(cancellationToken).ConfigureAwait(false);

        // The resolution above is what performed (or awaited) the login, and the login recorded its
        // own result. Non-null here because a Login resolution either records an AuthInfo or throws;
        // the null-forgiving read rather than a fallback arm is D-M1c-25's rule — an unreachable
        // branch the CNF-010 floor allows no pragma to excuse.
        return context.LastLogin!;
    }

    /// <summary>
    /// CFG-031: writes the current token to <c>TokenFile</c> with owner-only permissions. This is
    /// the <b>only</b> way the SDK ever writes that file — a login never does, which is why the
    /// requirement makes it an explicit call rather than a side effect.
    /// </summary>
    /// <exception cref="BastionVaultException">
    /// <c>BV-INPUT-001</c> when the client holds no token, because persisting "no token" would
    /// silently leave a stale one on disk; <c>BV-CONFIG-005</c> when the file cannot be written.
    /// </exception>
    public void PersistToken()
    {
        if (context.CurrentToken is not { } token)
        {
            ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.InputInvalidArgument);
            throw BastionVaultException.Request(
                ErrorCodes.InputInvalidArgument,
                entry.Category,
                entry.Message,
                entry.Hint,
                retryable: false,
                attempts: 0,
                details: new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["argument"] = "CurrentToken",
                    ["reason"] = "The client holds no token to persist.",
                });
        }

        // CurrentToken only ever yields a SecretString with a value, so Reveal() is non-null here.
        TokenFiles.Write(context.Config.TokenFile, token.Reveal()!);
    }

    /// <summary>
    /// CFG-032: deletes <c>TokenFile</c> if it is present, and does not fail if it is absent.
    /// </summary>
    public void ForgetPersistedToken()
    {
        TokenFiles.Delete(context.Config.TokenFile);
    }
}
