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
/// <param name="ContainsAll">
/// Extra substrings the message must <b>all</b> contain. Reserved for a genuine conjunction:
/// every Appendix B row today carries an empty array here (D-M8-2).
/// </param>
/// <param name="ContainsAny">
/// One qualifier group (the appendix's <c>+ a/b/c</c> and <c>(`a`, `b`)</c> forms). ANDed with
/// <see cref="Text"/>, alternation <i>within</i> the group. An empty array imposes nothing; a
/// non-empty one is satisfied by <b>any</b> hit — inclusive OR, matching this type's
/// <c>Any(...)</c> matcher and Rust's and Python's (D-M8-2).
/// <para>
/// Do not tighten this to a single-hit test. Appendix B's alternatives are mutually exclusive
/// in practice, so a real server message carries one of them and never two — but that is a
/// property of the corpus, not an invariant the matcher may assume. A single-hit test would
/// still accept every message the corpus contains, pass every fixture, and diverge from the
/// other two languages only on a message carrying two alternatives: a silent cross-language
/// parity break (CLA-003) that no existing test would catch.
/// </para>
/// </param>
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
    string[] ContainsAny,
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

    /// <summary>
    /// The retry delay in seconds, as an integer, in either spelling the server uses: a trailing
    /// <c>(retry after Ns)</c> suffix (Appendix B §2's normalisation rule) or an inline
    /// <c>try again in N seconds</c> clause (AUT-011's <c>BV-AUTH-006</c> message).
    /// </summary>
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

/// <summary>
/// One <c>data.error</c> message a <c>200</c> login rejection can carry, with the code AUT-011 maps
/// it to. Generated from the same Appendix B §2 table as <see cref="RecognitionRule"/>
/// (D-M2-25 item 3), so the mock server's <c>login-failure-as-200</c> simulation (TST-021) cannot
/// assert a message the recogniser does not know or produce one it maps elsewhere.
/// </summary>
/// <param name="Code">The code the recogniser assigns this message at status <c>200</c>.</param>
/// <param name="Message">The server message, in the appendix's own spelling.</param>
internal sealed record LoginRejection(string Code, string Message);
