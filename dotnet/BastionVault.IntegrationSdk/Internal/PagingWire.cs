using System.Runtime.CompilerServices;

namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// The cursor-pagination machinery every <c>*-info</c> listing shares (14 — batch and request
/// efficiency, PAG-001…PAG-007): the client-side <c>limit</c> default and bound, and the
/// safety-capped iterator that walks pages until the server says there are no more. Written once
/// so <c>Sys.ListNamespacesInfo</c> and <c>Auth.Userpass.ListUsersInfo</c> (D-M8-7's two wired
/// areas) cannot drift, and so a third area added later reaches the same rules unchanged.
/// </summary>
internal static class PagingWire
{
    /// <summary>PAG-001's client-side default when <c>limit</c> is omitted.</summary>
    public const int DefaultPageLimit = 100;

    /// <summary>PAG-001's client-side bound; the server caps at the same value.</summary>
    public const int MaxPageLimit = 500;

    /// <summary>PAG-004's default safety cap.</summary>
    public const int DefaultMaxRecords = 5000;

    /// <summary>PAG-001: defaults and validates <c>limit</c>, raising <c>BV-INPUT-004</c> outside <c>1…500</c>.</summary>
    public static int ValidateLimit(int? limit)
    {
        int effective = limit ?? DefaultPageLimit;
        return effective is < 1 or > MaxPageLimit ? throw SysWire.OutOfRange("limit", effective) : effective;
    }

    /// <summary>
    /// PAG-004: walks <paramref name="fetchPage"/> until <see cref="Page{T}.Truncated"/> is
    /// <see langword="false"/> (PAG-007's empty last page ends the walk without error), passing
    /// each page's own <see cref="Page{T}.Next"/> back in as the next call's cursor and never
    /// computing one (PAG-002). Every fetch is an ordinary <see cref="EgressKind.Request"/> call
    /// through the operation supplied by the caller, so it is rate-gated exactly like a caller
    /// issuing the same calls by hand (D-M8-7) — this helper adds no exemption and no second gate.
    /// <c>BV-INPUT-005</c> is raised the moment yielding the next record would exceed
    /// <paramref name="maxRecords"/>, before that record is ever produced, with the last page's
    /// <see cref="Page{T}.Total"/> in <c>Details.total</c>.
    /// </summary>
    public static async IAsyncEnumerable<KeyValuePair<string, T>> IteratePagesAsync<T>(
        Func<string?, CancellationToken, Task<Page<T>>> fetchPage,
        int maxRecords,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fetchPage);
        string? after = null;
        int yielded = 0;
        while (true)
        {
            Page<T> page = await fetchPage(after, cancellationToken).ConfigureAwait(false);
            foreach (KeyValuePair<string, T> entry in page.Entries)
            {
                if (yielded >= maxRecords)
                {
                    throw IterationCapExceeded(maxRecords, page.Total);
                }

                yield return entry;
                yielded++;
            }

            if (!page.Truncated)
            {
                yield break;
            }

            after = page.Next;
        }
    }

    /// <summary><c>BV-INPUT-005</c>: <c>Details.total</c> carries the server's reported total, per the error catalogue's hint.</summary>
    private static BastionVaultException IterationCapExceeded(int cap, int total)
    {
        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.InputIterationCapExceeded);
        return BastionVaultException.Request(
            ErrorCodes.InputIterationCapExceeded,
            entry.Category,
            entry.Message,
            entry.Hint,
            retryable: entry.Retryable,
            attempts: 0,
            details: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["maxRecords"] = cap,
                ["total"] = total,
            });
    }
}
