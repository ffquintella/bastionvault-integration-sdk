namespace BastionVault.IntegrationSdk;

/// <summary>Whether a <see cref="CacheVersion"/> carries a fresh answer or CCH-002's <c>304</c> short-circuit.</summary>
public enum CacheVersionState
{
    /// <summary>A <c>200</c>: <see cref="CacheVersion.Version"/>, <see cref="CacheVersion.Topics"/> and <see cref="CacheVersion.Coarse"/> are populated.</summary>
    Current,

    /// <summary>A <c>304</c> against the supplied <c>ifNoneMatch</c> (CCH-002). Nothing else on this instance is populated.</summary>
    NotModified,
}

/// <summary>
/// <c>Sys.CacheVersion</c>'s result (14 — batch and request efficiency, CCH-001…CCH-005). Modelled
/// as one type with a <see cref="State"/> discriminator, the way <c>KvV2Secret.State</c> already
/// distinguishes live from soft-deleted (KV2-004), rather than as two unrelated result types the
/// spec's <c>CacheVersion | NotModified</c> notation would otherwise force into being.
/// </summary>
/// <remarks>
/// <para>
/// <b>CCH-004: epochs are per node and reset on restart.</b> A number in <see cref="Topics"/>
/// going <i>down</i> since a previous call is not "no change" and it is not a signal to roll a
/// cache back — it is the node that answered having restarted. The only safe change signal is an
/// <i>increase</i>; a caller that invalidates on any difference, including a decrease, invalidates
/// spuriously on every restart of the node it happened to reach.
/// </para>
/// <para>
/// <b>CCH-005: a topic absent from <see cref="Topics"/> means "not authorised or unknown", never
/// <c>0</c>.</b> This type never inserts a missing key with a synthesised value; a caller that
/// indexes <see cref="Topics"/> for a topic it asked for and finds nothing has been told exactly
/// that, and a fabricated <c>0</c> would read as "definitely stale" for a topic the token cannot
/// even see.
/// </para>
/// </remarks>
public sealed class CacheVersion
{
    /// <summary>Whether this is a fresh answer or a <c>304</c> (CCH-002).</summary>
    public required CacheVersionState State { get; init; }

    /// <summary>Shorthand for <see cref="State"/> <c>== NotModified</c> — the shape a caller actually branches on.</summary>
    public bool NotModified => State == CacheVersionState.NotModified;

    /// <summary>The wire <c>version</c> aggregate. <see langword="null"/> on a <see cref="NotModified"/> result.</summary>
    public int? Version { get; init; }

    /// <summary>
    /// The wire <c>topics</c> map, one epoch per topic the server named. See this type's remarks
    /// for CCH-005 (a requested topic missing here, not <c>0</c>) and CCH-004 (only an increase is
    /// a change signal). <see langword="null"/> on a <see cref="NotModified"/> result.
    /// </summary>
    public IReadOnlyDictionary<string, int>? Topics { get; init; }

    /// <summary>The wire <c>coarse</c> flag. <see langword="null"/> on a <see cref="NotModified"/> result.</summary>
    public bool? Coarse { get; init; }

    /// <summary>The response's <c>ETag</c>, to carry into the next call's <c>ifNoneMatch</c>.</summary>
    public string? ETag { get; init; }
}
