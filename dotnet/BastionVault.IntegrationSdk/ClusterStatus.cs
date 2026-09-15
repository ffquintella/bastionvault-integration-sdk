using System.Text.Json;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// <c>Sys.ClusterStatus</c>'s result (SYS-006): the two fields every backend carries, plus four a
/// non-clustered backend omits. Optional fields stay <see langword="null"/> rather than being
/// defaulted when the server omits them.
/// </summary>
public sealed class ClusterStatus
{
    /// <summary>The wire <c>storage_type</c> field. Always present.</summary>
    public required string StorageType { get; init; }

    /// <summary>The wire <c>cluster</c> field. Always present.</summary>
    public required string Cluster { get; init; }

    /// <summary>The wire <c>node_id</c> field. Absent on a non-clustered backend.</summary>
    public string? NodeId { get; init; }

    /// <summary>The wire <c>is_leader</c> field. Absent on a non-clustered backend.</summary>
    public bool? IsLeader { get; init; }

    /// <summary>The wire <c>cluster_healthy</c> field. Absent on a non-clustered backend.</summary>
    public bool? ClusterHealthy { get; init; }

    /// <summary>
    /// D-M3-4: an opaque map, because the wire fixes no key set for it. Absent, not defaulted,
    /// when the backend omits it — the same optionality rule as every other field in this shape.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement>? RaftMetrics { get; init; }
}
