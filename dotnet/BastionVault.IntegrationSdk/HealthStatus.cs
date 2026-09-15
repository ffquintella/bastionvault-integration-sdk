using System.Text.Json;

namespace BastionVault.IntegrationSdk;

/// <summary>The classification <c>Sys.Health</c> derives from the response body (SYS-001).</summary>
public enum HealthState
{
    /// <summary>Body: initialized, unsealed, cluster healthy, not standby. HTTP 200.</summary>
    Active,

    /// <summary>Body: <c>standby == true</c> (checked after the initialized/sealed/cluster-health rows). HTTP 429.</summary>
    Standby,

    /// <summary>Body: <c>sealed == true</c>. HTTP 503.</summary>
    Sealed,

    /// <summary>Body: <c>cluster_healthy == false</c> (and not sealed). HTTP 503.</summary>
    Unhealthy,

    /// <summary>Body: <c>initialized == false</c>. HTTP 501.</summary>
    Uninitialized,
}

/// <summary><c>Sys.Health</c>'s result (SYS-001, SYS-002). Never constructed from an error; see the operation's own doc comment.</summary>
public sealed class HealthStatus
{
    /// <summary>The classification derived from this response's body, per SYS-001's table.</summary>
    public required HealthState State { get; init; }

    /// <summary>The wire <c>initialized</c> field.</summary>
    public required bool Initialized { get; init; }

    /// <summary>The wire <c>sealed</c> field.</summary>
    public required bool Sealed { get; init; }

    /// <summary>The wire <c>standby</c> field.</summary>
    public required bool Standby { get; init; }

    /// <summary>The wire <c>cluster_healthy</c> field.</summary>
    public required bool ClusterHealthy { get; init; }

    /// <summary>The HTTP status the server actually sent (200, 429, 501 or 503 per SYS-001's table).</summary>
    public required int StatusCode { get; init; }

    /// <summary>The exact parsed body, for forward-compatible field access (TRN-043's precedent).</summary>
    public JsonElement Raw { get; init; }
}
