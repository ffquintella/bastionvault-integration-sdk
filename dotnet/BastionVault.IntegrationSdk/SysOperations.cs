using System.Buffers;
using System.Globalization;
using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// The Core <c>sys</c> surface (OVR-008), reached from <see cref="BastionVaultClient.Sys"/>: health
/// and status (SYS-001, SYS-002, SYS-005, SYS-006, SYS-008) and self capability introspection
/// (SYS-050…SYS-053). M3's scope only (D-M3-1); every other <c>sys/*</c> operation
/// <c>06-system-api.md</c> names belongs to a later milestone.
/// </summary>
public sealed class SysOperations
{
    private readonly ClientContext context;
    private readonly LogicalOperations logical;
    private readonly string activeNamespace;

    internal SysOperations(ClientContext context, string activeNamespace)
    {
        this.context = context;
        this.activeNamespace = activeNamespace;
        logical = new LogicalOperations(context, activeNamespace);
    }

    /// <summary>
    /// SYS-001, SYS-002: <c>GET sys/health</c>, unauthenticated (CFG-020's exemption list already
    /// carries it). Never raises for a <c>200</c>/<c>429</c>/<c>501</c>/<c>503</c> response — those
    /// four are folded into <see cref="HealthStatus.State"/> instead — and raises only on a
    /// transport failure or a non-JSON body (<c>BV-PROTOCOL-002</c>). No <c>472</c>/<c>473</c> code
    /// and no <c>standbyok</c>-style query parameter exists on this endpoint or is ever sent.
    /// </summary>
    public async Task<HealthStatus> HealthAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        RequestExecutor executor = new(context, activeNamespace);
        RequestExecutor.Outcome outcome = await executor.ExecuteHealthAsync("GET", "sys/health", options, cancellationToken).ConfigureAwait(false);

        // SYS-001 promises a body for every one of the four statuses this call ever returns
        // without raising; ExecuteHealthAsync raises before returning one of the other arms
        // (IsEmpty/IsNotFoundEmpty/no Body), so a missing body here is unreachable (D-M1c-25).
        JsonElement body = outcome.Body ?? throw EnvelopeMismatch("sys/health", "body");
        bool initialized = ReadBool(body, "initialized");
        bool sealedValue = ReadBool(body, "sealed");
        bool standby = ReadBool(body, "standby");
        bool clusterHealthy = ReadBool(body, "cluster_healthy");

        return new HealthStatus
        {
            State = DetermineHealthState(initialized, sealedValue, clusterHealthy, standby),
            Initialized = initialized,
            Sealed = sealedValue,
            Standby = standby,
            ClusterHealthy = clusterHealthy,
            StatusCode = outcome.StatusCode,
            Raw = body,
        };
    }

    /// <summary>
    /// SYS-005: <c>GET sys/seal-status</c>, unauthenticated, always <c>200</c>. The server's wire
    /// <c>t</c>/<c>n</c> carry <c>secret_shares</c>/<c>secret_threshold</c> — reversed from the
    /// usual naming — so both the raw fields and the correctly-named derived ones are exposed.
    /// </summary>
    public async Task<SealStatus> SealStatusAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "GET", "sys/seal-status", null, options, defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, JsonElement> data = response?.Data ?? throw EnvelopeMismatch("sys/seal-status", "data");

        int t = ReadInt(data, "t") ?? 0;
        int n = ReadInt(data, "n") ?? 0;
        return new SealStatus
        {
            Sealed = ReadBool(data, "sealed"),
            T = t,
            N = n,
            KeyShares = Math.Max(t, n),
            KeyThreshold = Math.Min(t, n),
            Progress = ReadInt(data, "progress") ?? 0,
        };
    }

    /// <summary>
    /// SYS-008: <c>GET sys/info</c>. The anonymous tier carries <see cref="ServerInfo.Initialized"/>
    /// and <see cref="ServerInfo.Sealed"/> only; a live token adds the other four, which stay
    /// <see langword="null"/> — never defaulted — when the server omits them.
    /// </summary>
    public async Task<ServerInfo> ServerInfoAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "GET", "sys/info", null, options, defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, JsonElement> data = response?.Data ?? throw EnvelopeMismatch("sys/info", "data");

        return new ServerInfo
        {
            Initialized = ReadBool(data, "initialized"),
            Sealed = ReadBool(data, "sealed"),
            Version = ReadString(data, "version"),
            StartedAt = ReadRfc3339(data, "started_at"),
            UptimeSeconds = ReadLong(data, "uptime_seconds"),
            StorageType = ReadString(data, "storage_type"),
        };
    }

    /// <summary>
    /// SYS-006: <c>GET sys/cluster-status</c>. Requires a live token (CFG-020/ERR-022's ordinary
    /// client-side refusal applies — this path is not on the unauthenticated list); the documented
    /// <c>403</c> is not special-cased here and surfaces as the ordinary typed
    /// <c>BV-AUTHZ-003</c> exception through the shared status/message mapping, like any other
    /// mapped error. Optional fields stay <see langword="null"/> on a non-clustered backend.
    /// </summary>
    public async Task<ClusterStatus> ClusterStatusAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "GET", "sys/cluster-status", null, options, defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, JsonElement> data = response?.Data ?? throw EnvelopeMismatch("sys/cluster-status", "data");

        return new ClusterStatus
        {
            StorageType = ReadString(data, "storage_type") ?? string.Empty,
            Cluster = ReadString(data, "cluster") ?? string.Empty,
            NodeId = ReadString(data, "node_id"),
            IsLeader = ReadNullableBool(data, "is_leader"),
            ClusterHealthy = ReadNullableBool(data, "cluster_healthy"),
            RaftMetrics = ReadRawMap(data, "raft_metrics"),
        };
    }

    /// <summary>
    /// SYS-050…SYS-053: <c>POST /v2/sys/capabilities-self</c> (TRN-071 — the <c>/v2</c> prefix is
    /// pinned and cannot be overridden away by <see cref="RequestOptions.ApiVersion"/>). An empty
    /// <paramref name="paths"/> is refused client-side with <c>BV-INPUT-002</c> before any request
    /// is sent (SYS-052). Reads the <c>capabilities</c> map, never the duplicated top-level keys.
    /// </summary>
    public async Task<Capabilities> CapabilitiesSelfAsync(IReadOnlyList<string> paths, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
        {
            ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.InputEmptyCollection);
            throw BastionVaultException.Request(
                ErrorCodes.InputEmptyCollection,
                entry.Category,
                entry.Message,
                entry.Hint,
                retryable: false,
                attempts: 0,
                details: new Dictionary<string, object?>(StringComparer.Ordinal) { ["argument"] = "paths" });
        }

        RequestOptions pinned = (options ?? new RequestOptions()) with { ApiVersion = "v2" };
        Response? response = await logical.ExecuteShapedAsync(
            "POST", "sys/capabilities-self", SerialisePaths(paths), pinned,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, JsonElement> data = response?.Data ?? throw EnvelopeMismatch("sys/capabilities-self", "data");

        return new Capabilities
        {
            ByPath = ReadCapabilitiesMap(data),
            NamespaceOperable = ReadBool(data, "namespace_operable"),
            TokenNamespace = ReadString(data, "token_namespace") ?? string.Empty,
            ActiveNamespace = ReadString(data, "active_namespace") ?? string.Empty,
        };
    }

    /// <summary>
    /// SYS-053: the spec-named <c>Client.Sys.Can</c> convenience (":9 — All operations live under
    /// <c>Client.Sys</c>"), one round trip. Fetches <paramref name="path"/>'s capabilities via
    /// <see cref="CapabilitiesSelfAsync"/> and delegates the boolean check to
    /// <see cref="Capabilities.Can"/> — the no-round-trip form, kept for a caller that already
    /// holds a fetched <see cref="Capabilities"/> result.
    /// </summary>
    public async Task<bool> CanAsync(string path, Capability capability, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Capabilities capabilities = await CapabilitiesSelfAsync([path], options, cancellationToken).ConfigureAwait(false);
        return capabilities.Can(path, capability);
    }

    /// <summary>SYS-001's table, applied in the priority order the table lists its rows in.</summary>
    private static HealthState DetermineHealthState(bool initialized, bool sealedValue, bool clusterHealthy, bool standby)
    {
        if (!initialized)
        {
            return HealthState.Uninitialized;
        }

        if (sealedValue)
        {
            return HealthState.Sealed;
        }

        if (!clusterHealthy)
        {
            return HealthState.Unhealthy;
        }

        return standby ? HealthState.Standby : HealthState.Active;
    }

    private static ReadOnlyMemory<byte> SerialisePaths(IReadOnlyList<string> paths)
    {
        ArrayBufferWriter<byte> buffer = new();
        using Utf8JsonWriter writer = new(buffer);
        writer.WriteStartObject();
        writer.WriteStartArray("paths");
        foreach (string path in paths)
        {
            writer.WriteStringValue(path);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenMemory;
    }

    /// <summary>SYS-050: builds <see cref="Capabilities.ByPath"/> from the wire's nested <c>capabilities</c> map only.</summary>
    private static Dictionary<string, IReadOnlyList<Capability>> ReadCapabilitiesMap(IReadOnlyDictionary<string, JsonElement> data)
    {
        Dictionary<string, IReadOnlyList<Capability>> result = new(StringComparer.Ordinal);
        if (!data.TryGetValue("capabilities", out JsonElement capabilities) || capabilities.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (JsonProperty property in capabilities.EnumerateObject())
        {
            // The Where above already guarantees ValueKind == String, so GetString() cannot
            // return null here; the null-forgiving read is D-M1c-25's rule — an unreachable
            // fallback arm gets no branch to (not) cover.
            List<Capability> values = property.Value.ValueKind == JsonValueKind.Array
                ? property.Value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => Capability.FromWire(item.GetString()!))
                    .ToList()
                : [];
            result[property.Name] = values;
        }

        return result;
    }

    private static Dictionary<string, JsonElement>? ReadRawMap(IReadOnlyDictionary<string, JsonElement> data, string name)
    {
        return data.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.Object
            ? value.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal)
            : null;
    }

    private static bool ReadBool(IReadOnlyDictionary<string, JsonElement> data, string name)
    {
        return data.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
    }

    private static bool ReadBool(JsonElement body, string name)
    {
        return body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.True;
    }

    private static bool? ReadNullableBool(IReadOnlyDictionary<string, JsonElement> data, string name)
    {
        return data.TryGetValue(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.ValueKind == JsonValueKind.True
            : null;
    }

    private static string? ReadString(IReadOnlyDictionary<string, JsonElement> data, string name)
    {
        return data.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static int? ReadInt(IReadOnlyDictionary<string, JsonElement> data, string name)
    {
        return data.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
    }

    private static long? ReadLong(IReadOnlyDictionary<string, JsonElement> data, string name)
    {
        return data.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : null;
    }

    /// <summary>SYS-008: <c>started_at</c> is RFC 3339 on the wire; absent or unparsable yields no value rather than a throw.</summary>
    private static DateTimeOffset? ReadRfc3339(IReadOnlyDictionary<string, JsonElement> data, string name)
    {
        return data.TryGetValue(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)
            ? parsed
            : null;
    }

    private static BastionVaultException EnvelopeMismatch(string path, string field)
    {
        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.ProtocolUnexpectedResponse);
        return BastionVaultException.Request(
            ErrorCodes.ProtocolUnexpectedResponse,
            entry.Category,
            entry.Message,
            entry.Hint,
            retryable: entry.Retryable,
            attempts: 0,
            path: path,
            details: new Dictionary<string, object?>(StringComparer.Ordinal) { ["expectedField"] = field });
    }
}
