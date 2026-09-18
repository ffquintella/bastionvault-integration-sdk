using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace BastionVault.IntegrationSdk;

/// <summary><c>Sys.Restore</c>'s result (SYS-090).</summary>
public sealed class RestoreResult
{
    /// <summary>The wire <c>entries_restored</c> field.</summary>
    public required long EntriesRestored { get; init; }

    /// <summary>The whole response body as sent.</summary>
    public JsonElement Raw { get; init; }
}

/// <summary>The DoS guard's configuration (06 — "Batch, cache version, DoS"). Every field is optional: the write is a <b>partial update</b>.</summary>
public sealed class DosConfig
{
    /// <summary>The wire <c>enabled</c> field.</summary>
    public bool? Enabled { get; init; }

    /// <summary>The wire <c>window_secs</c> field.</summary>
    public long? WindowSecs { get; init; }

    /// <summary>The wire <c>max_requests</c> field.</summary>
    public long? MaxRequests { get; init; }

    /// <summary>The wire <c>auth_max_requests</c> field.</summary>
    public long? AuthMaxRequests { get; init; }

    /// <summary>The wire <c>ban_secs</c> field.</summary>
    public long? BanSecs { get; init; }

    /// <summary>The wire <c>refresh_secs</c> field.</summary>
    public long? RefreshSecs { get; init; }

    /// <summary>The whole object as sent; <see cref="JsonValueKind.Undefined"/> on a patch the caller built.</summary>
    public JsonElement Raw { get; init; }
}

/// <summary>
/// <c>Sys.DashboardSummary()</c>'s result.
/// </summary>
/// <remarks>
/// ⚠️ <see cref="Audit24h"/> and <see cref="Attention"/> are omitted for a caller without audit
/// read, so both are optional and an absent one is <see langword="null"/> rather than an empty
/// object. The rest of the body is reachable through <see cref="Raw"/>: the specification names
/// the route and the two optional keys, and nothing else, so naming further fields would be a
/// guess about the wire (D-M1c-25).
/// </remarks>
public sealed class DashboardSummary
{
    /// <summary>The wire <c>audit_24h</c> object, or <see langword="null"/> when the caller cannot read audit.</summary>
    public JsonElement? Audit24h { get; init; }

    /// <summary>The wire <c>attention</c> value, or <see langword="null"/> when the caller cannot read audit.</summary>
    public JsonElement? Attention { get; init; }

    /// <summary>The whole summary object as sent.</summary>
    public JsonElement Raw { get; init; }
}

/// <summary>
/// RES-030's per-node outcome: one entry per discovered candidate, whether or not the call reached
/// it.
/// </summary>
/// <remarks>
/// A failure is carried as a value rather than raised, because the whole point of the
/// <c>*ClusterWide</c> variants is that unsealing must reach <b>every</b> node: an exception on the
/// first unreachable one would hide the eight that succeeded.
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1056:URI-like properties should not be strings",
    Justification = "The key is the candidate URL verbatim, as `Candidate.Url` and `NodeSelection.Url` already carry it (D-M5-8): it is the value the shared fixtures compare and the three language SDKs must agree on character-for-character, and System.Uri would re-render it.")]
public sealed class ClusterNodeResult
{
    /// <summary>The candidate's base URL, as discovery produced it.</summary>
    public required string Url { get; init; }

    /// <summary>Whether the operation completed against this node.</summary>
    public required bool Succeeded { get; init; }

    /// <summary><c>Sys.UnsealClusterWide</c>'s per-node seal status; always <see langword="null"/> for <c>Sys.SealClusterWide</c>, which has no response body.</summary>
    public SealStatus? SealStatus { get; init; }

    /// <summary>The failure this node answered with, or <see langword="null"/> when it succeeded.</summary>
    public BastionVaultException? Error { get; init; }
}
