using System.Text.Json;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// <c>Sys.HsmStatus</c>'s result (06 — system API, "Health and status"): the seal mechanism the
/// server is actually using. <b>v2-only</b> — the SDK pins the <c>/v2</c> prefix (TRN-071), as it
/// does for <c>Sys.CapabilitiesSelf</c>.
/// </summary>
/// <remarks>
/// The specification writes the body as <c>{type, auto_unseal, sealed, initialized, …}</c>, with
/// the ellipsis meaning the shape is open. Every named field is therefore modelled optional and
/// <see cref="Raw"/> carries the whole object, so a field a later server adds is reachable without
/// a new SDK release and without this type guessing a default the wire never sent (SYS-008's rule,
/// applied to the same kind of tiered body).
/// </remarks>
public sealed class HsmStatus
{
    /// <summary>The wire <c>type</c> field: <c>shamir</c> or <c>hsm</c> on a current server, left as a string because the set is open.</summary>
    public string? Type { get; init; }

    /// <summary>The wire <c>auto_unseal</c> field, or <see langword="null"/> when the server omitted it.</summary>
    public bool? AutoUnseal { get; init; }

    /// <summary>The wire <c>sealed</c> field, or <see langword="null"/> when the server omitted it.</summary>
    public bool? Sealed { get; init; }

    /// <summary>The wire <c>initialized</c> field, or <see langword="null"/> when the server omitted it.</summary>
    public bool? Initialized { get; init; }

    /// <summary>The whole response object, verbatim, for the fields the specification's ellipsis leaves open.</summary>
    public required JsonElement Raw { get; init; }
}
