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
    public void ForgetPersistedToken() => TokenFiles.Delete(context.Config.TokenFile);
}
