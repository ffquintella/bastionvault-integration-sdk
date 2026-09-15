namespace BastionVault.IntegrationSdk;

/// <summary>
/// <c>Sys.SealStatus</c>'s result (SYS-005). The server's wire fields <c>t</c>/<c>n</c> carry
/// <c>secret_shares</c>/<c>secret_threshold</c> — the reverse of the usual naming — so both the raw
/// values and the derived, correctly-named ones are exposed rather than only one or the other.
/// </summary>
public sealed class SealStatus
{
    /// <summary>The wire <c>sealed</c> field.</summary>
    public required bool Sealed { get; init; }

    /// <summary>The wire <c>t</c> field, verbatim: the server's <c>secret_shares</c> count.</summary>
    public required int T { get; init; }

    /// <summary>The wire <c>n</c> field, verbatim: the server's <c>secret_threshold</c> count.</summary>
    public required int N { get; init; }

    /// <summary><c>max(T, N)</c>: the number of key shares, correctly named regardless of which wire field carried it.</summary>
    public required int KeyShares { get; init; }

    /// <summary><c>min(T, N)</c>: the unseal threshold, correctly named — never greater than <see cref="KeyShares"/>.</summary>
    public required int KeyThreshold { get; init; }

    /// <summary>The wire <c>progress</c> field: how many unseal keys have been supplied so far.</summary>
    public required int Progress { get; init; }
}
