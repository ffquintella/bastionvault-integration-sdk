using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// The programmatically inspectable code → message → hint table ERR-036 requires. Spelled
/// <c>ErrorCatalog</c>, not <c>Catalogue</c>, because ERR-036 names <c>ErrorCatalog.Get(code)</c>
/// (D-M1c-7).
/// </summary>
/// <remarks>
/// The message-recognition and hint-enrichment rules that consume this table stay internal: they
/// implement ERR-020 and are not a supported extension point (D-M1c-7).
/// </remarks>
public static class ErrorCatalog
{
    private static readonly Dictionary<string, ErrorCatalogEntry> Index =
        ErrorCatalogData.Entries.ToDictionary(entry => entry.Code, StringComparer.Ordinal);

    /// <summary>Every catalogue row, in Appendix B order, so generated documentation is stable (D-M1c-7).</summary>
    public static IReadOnlyList<ErrorCatalogEntry> All { get; } = ErrorCatalogData.Entries;

    /// <summary>
    /// The entry for <paramref name="code"/>, or <see langword="null"/> when no such code exists.
    /// Never throws (D-M1c-7).
    /// </summary>
    public static ErrorCatalogEntry? Get(string code)
    {
        return code is not null && Index.TryGetValue(code, out ErrorCatalogEntry? entry) ? entry : null;
    }

    /// <summary>
    /// The entry for a code this assembly raises. Unlike <see cref="Get"/> this is an internal
    /// contract: a missing code is a generator or wiring defect, not a caller error.
    /// </summary>
    internal static ErrorCatalogEntry Require(string code)
    {
        return Index[code];
    }
}
