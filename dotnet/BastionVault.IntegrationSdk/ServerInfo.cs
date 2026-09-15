namespace BastionVault.IntegrationSdk;

/// <summary>
/// <c>Sys.ServerInfo</c>'s result (SYS-008): the anonymous tier's two fields, plus the four the
/// server adds only when the caller carries a live token. The tiered fields are optional and are
/// never defaulted when the server omits them — a caller with no token sees them as
/// <see langword="null"/>, not as <c>0</c>/<c>""</c>.
/// </summary>
public sealed class ServerInfo
{
    /// <summary>The wire <c>initialized</c> field. Present in the anonymous tier.</summary>
    public required bool Initialized { get; init; }

    /// <summary>The wire <c>sealed</c> field. Present in the anonymous tier.</summary>
    public required bool Sealed { get; init; }

    /// <summary>The wire <c>version</c> field. Present only with a live token.</summary>
    public string? Version { get; init; }

    /// <summary>The wire <c>started_at</c> field, parsed from RFC 3339. Present only with a live token.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>Seconds on the wire; absent when the server did not send it (anonymous tier).</summary>
    public long? UptimeSeconds { get; init; }

    /// <summary>The wire <c>storage_type</c> field. Present only with a live token.</summary>
    public string? StorageType { get; init; }
}
