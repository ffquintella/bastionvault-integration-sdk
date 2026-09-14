namespace BastionVault.IntegrationSdk.Internal;

/// <summary>The three rule kinds Appendix B §2 spells (D-M1c-3).</summary>
internal enum RecognitionKind
{
    /// <summary>The whole normalised message equals the rule text.</summary>
    Exact,

    /// <summary>The normalised message starts with the rule text.</summary>
    Prefix,

    /// <summary>The normalised message contains the rule text.</summary>
    Contains,
}

/// <summary>
/// One compiled Appendix B §2 rule, generated in table order by <c>tools/error-catalogue</c>.
/// First match wins (D-M1c-3).
/// </summary>
/// <param name="Kind">Exact, prefix or contains.</param>
/// <param name="Text">The rule literal, already lower-cased. A trailing space is significant.</param>
/// <param name="ContainsAll">Extra substrings the message must also contain (the appendix's <c>+ contains</c> form).</param>
/// <param name="Status">An exact status guard, or <see langword="null"/>.</param>
/// <param name="StatusClass">A status-class guard (<c>5</c> for <c>5xx</c>), or <see langword="null"/>.</param>
/// <param name="PathContains">
/// A path-scope guard, or <see langword="null"/>. Appendix B qualifies three rows with a scope
/// (<c>(409, recordings)</c>, <c>(ssh mount)</c>, <c>(policy write)</c>); only the ones decidable
/// from the request path alone are compiled, the rest stay advisory in <c>catalogue.json</c>.
/// </param>
/// <param name="Code">The code this rule produces.</param>
/// <param name="CaptureIndex">Index into <see cref="ErrorCatalogData.Captures"/>, or <c>-1</c>.</param>
internal sealed record RecognitionRule(
    RecognitionKind Kind,
    string Text,
    string[] ContainsAll,
    int? Status,
    int? StatusClass,
    string? PathContains,
    string Code,
    int CaptureIndex);

/// <summary>The closed set of D-M1c-4 capture shapes; kept regex-free so the three SDKs cannot drift.</summary>
internal enum DetailsCaptureKind
{
    /// <summary>The first token after <see cref="DetailsCapture.Prefix"/>.</summary>
    TokenAfterPrefix,

    /// <summary>The <c>N</c> of a <c>(retry after Ns)</c> suffix, as an integer.</summary>
    RetryAfterSecs,

    /// <summary>The first two integers in the message, in order.</summary>
    TwoInts,

    /// <summary>The comma/space separated tokens between <see cref="DetailsCapture.Prefix"/> and <see cref="DetailsCapture.Suffix"/>.</summary>
    TokenListBetween,
}

/// <summary>One hand-authored D-M1c-4 capture row, generated from the generator's input table.</summary>
/// <param name="Kind">Which capture shape to apply.</param>
/// <param name="Keys">The <c>Details</c> keys produced, spelled as Appendix B spells them (D-M1b-4c).</param>
/// <param name="Prefix">Text the value follows.</param>
/// <param name="Suffix">Text the value precedes.</param>
internal sealed record DetailsCapture(
    DetailsCaptureKind Kind,
    string[] Keys,
    string Prefix,
    string Suffix);
