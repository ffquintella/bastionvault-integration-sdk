using System.Buffers;
using System.Globalization;
using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// The Core <c>sys</c> surface (OVR-008), reached from <see cref="BastionVaultClient.Sys"/>: health
/// and status (SYS-001, SYS-002, SYS-005, SYS-006, SYS-008, plus M7's <c>HsmStatus</c>), self
/// capability introspection (SYS-050…SYS-053), initialisation/seal/unseal (SYS-010…SYS-013),
/// mounts (SYS-020…SYS-026) and auth-method administration (SYS-030). Policies, namespaces, audit,
/// identity and backup/restore are M7's later slices; every operation
/// <c>06-system-api.md</c> names and this class does not carry belongs to one of them.
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
    /// SYS-026's cache key: the namespace the request <i>actually carries</i>, which is
    /// <see cref="RequestOptions.Namespace"/> when the caller overrides it for this one call and
    /// this view's active namespace otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This must be the same expression <c>RequestExecutor.EffectiveNamespace</c> uses to build the
    /// <c>X-BastionVault-Namespace</c> header, including the trailing-slash trim, or the key and
    /// the wire disagree. Keying on the <i>view's</i> namespace while the request goes out under
    /// the <i>call's</i> is not a staleness bug but a correctness one: it lets one tenant's mount
    /// table be stored under another tenant's key (poisoning), lets a cached entry answer a lookup
    /// the caller explicitly directed elsewhere (cross-tenant read), and lets a mutation invalidate
    /// a namespace it did not mutate while leaving the one it did mutate stale (missed
    /// invalidation). The trim is what keeps <c>"tenant-a"</c> and <c>"tenant-a/"</c> from becoming
    /// two keys over one table.
    /// </para>
    /// <para>D-M7-25 records the ruling that the <i>call's</i> namespace is the key, not the view's.</para>
    /// </remarks>
    private string CacheNamespace(RequestOptions? options)
    {
        return (options?.Namespace ?? activeNamespace).TrimEnd('/');
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
        return ToSealStatus(data);
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

        RequestOptions pinned = PinV2(options);
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


    /// <summary>
    /// 06 — system API, "Health and status": <c>GET /v2/sys/hsm/status</c>. <b>v2-only</b>, and the
    /// <c>/v2</c> prefix is pinned here (TRN-071) exactly as it is for
    /// <see cref="CapabilitiesSelfAsync"/>, so a <c>v1</c> client and a
    /// <see cref="RequestOptions.ApiVersion"/> override both still reach the v2 handler. Every
    /// named field stays optional and <see cref="HsmStatus.Raw"/> carries the whole object,
    /// because the specification's body ends in an ellipsis.
    /// </summary>
    public async Task<HsmStatus> HsmStatusAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        RequestOptions pinned = PinV2(options);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", "sys/hsm/status", null, pinned, defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, JsonElement> data = response?.Data ?? throw EnvelopeMismatch("sys/hsm/status", "data");

        return new HsmStatus
        {
            Type = ReadString(data, "type"),
            AutoUnseal = ReadNullableBool(data, "auto_unseal"),
            Sealed = ReadNullableBool(data, "sealed"),
            Initialized = ReadNullableBool(data, "initialized"),
            Raw = response.Raw,
        };
    }

    /// <summary>SYS-010's status probe: <c>GET sys/init</c> → <c>{"initialized": bool}</c>. Unauthenticated (CFG-020 lists the path).</summary>
    public async Task<bool> InitStatusAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "GET", "sys/init", null, options, defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, JsonElement> data = response?.Data ?? throw EnvelopeMismatch("sys/init", "data");
        return ReadBool(data, "initialized");
    }

    /// <summary>
    /// SYS-010, SYS-011: <c>PUT sys/init</c>, which a vault answers exactly once. Both
    /// <paramref name="shares"/> and <paramref name="threshold"/> or neither (omitted is the HSM
    /// auto-unseal case), and <c>1 ≤ threshold ≤ shares ≤ 255</c>; either rule broken is
    /// <c>BV-INPUT-001</c> refused client-side, before a request is sent.
    /// </summary>
    /// <remarks>
    /// The returned <see cref="InitResult"/> is <see cref="IDisposable"/> and holds the only copy
    /// of the unseal keys and the root token that will ever exist: the server keeps none. Dispose
    /// it once the material has been stored (SYS-011).
    /// </remarks>
    public async Task<InitResult> InitAsync(int? shares = null, int? threshold = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ValidateInitArguments(shares, threshold);

        Response? response = await logical.ExecuteShapedAsync(
            "PUT", "sys/init", SerialiseInit(shares, threshold), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, JsonElement> data = response?.Data ?? throw EnvelopeMismatch("sys/init", "data");

        List<string> keys = [];
        if (data.TryGetValue("keys", out JsonElement keysElement) && keysElement.ValueKind == JsonValueKind.Array)
        {
            keys.AddRange(keysElement.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!));
        }

        // D-M1c-25: `root_token` is what SYS-010's response table names, so an absent one is an
        // envelope mismatch rather than an empty token a caller might try to use.
        string rootToken = ReadString(data, "root_token") ?? throw EnvelopeMismatch("sys/init", "root_token");
        return new InitResult(keys, rootToken);
    }

    /// <summary>
    /// SYS-013: <c>PUT sys/seal</c> → <c>204</c>, sudo-gated. Flagged <b>non-retryable</b> and
    /// excluded from failover: sealing is the one operation whose success removes the node's
    /// ability to answer, so a replay against a second node would seal a second node, and a replay
    /// against the same one would report a failure the first attempt had already completed.
    /// </summary>
    public async Task SealAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        _ = await logical.ExecuteShapedAsync(
            "PUT", "sys/seal", null, options, defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken,
            nodeLocal: true, nonRetryable: true).ConfigureAwait(false);
    }

    /// <summary>
    /// SYS-012, SYS-013: <c>PUT sys/unseal</c> → <see cref="SealStatus"/>, idempotent on the server
    /// when the vault is already unsealed. An invalid key answers <c>BV-INPUT-101</c> and an
    /// uninitialised vault <c>BV-SERVER-007</c>, both through the shared Appendix B recognition.
    /// Flagged non-retryable and excluded from failover (SYS-013): unseal progress is
    /// <i>per node</i>, so a replay elsewhere would spend a share against a different node's
    /// counter.
    /// </summary>
    public async Task<SealStatus> UnsealAsync(string key, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        Response? response = await logical.ExecuteShapedAsync(
            "PUT", "sys/unseal", SerialiseField("key", key), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken,
            nodeLocal: true, nonRetryable: true).ConfigureAwait(false);
        IReadOnlyDictionary<string, JsonElement> data = response?.Data ?? throw EnvelopeMismatch("sys/unseal", "data");
        return ToSealStatus(data);
    }

    /// <summary>
    /// SYS-020, SYS-022: <c>GET sys/mounts</c> → the mount table, keyed by path with a trailing
    /// <c>/</c>. ⚠️ Two fields per entry and no more; see <see cref="MountInfo"/>.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, MountInfo>> ListMountsAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await FetchMountTableAsync("sys/mounts", stripAuthPrefix: false, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// SYS-020, SYS-022, SYS-024: <c>POST sys/mounts/{path}</c> → <c>204</c>. The path is accepted
    /// with or without a trailing <c>/</c>; an empty one is <c>BV-INPUT-001</c> client-side.
    /// A mount-quota breach is <c>507</c> → <c>BV-QUOTA-001</c> through the shared mapping.
    /// Invalidates this client's SYS-026 mount-type cache for the active namespace.
    /// </summary>
    public async Task MountAsync(string path, MountRequest request, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string wire = MountPaths.ToWire(path, "path");
        _ = await logical.ExecuteShapedAsync(
            "POST", $"sys/mounts/{wire}", SerialiseMountRequest(request), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
        context.MountTypes.Invalidate(CacheNamespace(options));
    }

    /// <summary>SYS-022, SYS-026: <c>DELETE sys/mounts/{path}</c> → <c>204</c>, and invalidates the mount-type cache.</summary>
    public async Task UnmountAsync(string path, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        string wire = MountPaths.ToWire(path, "path");
        _ = await logical.ExecuteShapedAsync(
            "DELETE", $"sys/mounts/{wire}", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
        context.MountTypes.Invalidate(CacheNamespace(options));
    }

    /// <summary>
    /// SYS-022, SYS-023, SYS-026: <c>POST sys/remount</c> with <c>{"from", "to"}</c> → <c>204</c>.
    /// Both paths are sent in the table form the server's own error text uses (<c>kv/</c>), and
    /// both are accepted from the caller with or without the slash. Invalidates the mount-type
    /// cache.
    /// </summary>
    public async Task RemountAsync(string from, string to, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        string fromPath = MountPaths.ToWire(from, "from") + "/";
        string toPath = MountPaths.ToWire(to, "to") + "/";

        try
        {
            _ = await logical.ExecuteShapedAsync(
                "POST", "sys/remount", SerialiseRemount(fromPath, toPath), options,
                defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
        }
        catch (BastionVaultException failure) when (IsUnknownMountTableType(failure))
        {
            throw RemapUnknownMountTableType(failure);
        }

        context.MountTypes.Invalidate(CacheNamespace(options));
    }

    /// <summary>
    /// 06 — system API, "Mounts": <c>GET sys/internal/ui/mounts</c>, the ACL-filtered table, split
    /// into its <c>secret</c> and <c>auth</c> halves. Auth keys are relative (SYS-030) and every
    /// key carries its trailing <c>/</c> (SYS-022).
    /// </summary>
    public async Task<MountTable> ListMountsDetailedAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "GET", "sys/internal/ui/mounts", null, options, defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, JsonElement> data = response?.Data ?? throw EnvelopeMismatch("sys/internal/ui/mounts", "data");

        return new MountTable
        {
            Secret = ReadDetailMap(data, "secret", stripAuthPrefix: false),
            Auth = ReadDetailMap(data, "auth", stripAuthPrefix: true),
        };
    }

    /// <summary>
    /// SYS-025: there is no per-mount read on the server — <c>GET sys/mounts/{path}</c> returns the
    /// whole table — so this filters <see cref="ListMountsAsync"/> client-side and returns
    /// <see langword="null"/> when the mount is absent. Deliberately <b>not</b> served from the
    /// SYS-026 cache: SYS-026 scopes the cache to <see cref="MountTypeOfAsync"/>, and a
    /// <c>ReadMount</c> that could be up to 60 seconds stale is a different contract from the one
    /// the specification writes.
    /// </summary>
    public async Task<MountInfo?> ReadMountAsync(string path, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        string key = MountPaths.ToTable(path);
        IReadOnlyDictionary<string, MountInfo> table = await ListMountsAsync(options, cancellationToken).ConfigureAwait(false);
        return table.TryGetValue(key, out MountInfo? info) ? info : null;
    }

    /// <summary>
    /// SYS-026: the mount's type, or <see langword="null"/> when there is no such mount, from a
    /// per-client cache with a 60-second TTL that <see cref="MountAsync"/>,
    /// <see cref="UnmountAsync"/> and <see cref="RemountAsync"/> invalidate. This is the lookup
    /// <c>Kv.DetectVersion</c> is built on (KV-001).
    /// </summary>
    public async Task<string?> MountTypeOfAsync(string path, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        string key = MountPaths.ToTable(path);
        string cacheNamespace = CacheNamespace(options);
        IReadOnlyDictionary<string, MountInfo>? table = context.MountTypes.TryGet(cacheNamespace, context.Clock.NowUtc());
        if (table is null)
        {
            table = await ListMountsAsync(options, cancellationToken).ConfigureAwait(false);
            context.MountTypes.Store(cacheNamespace, context.Clock.NowUtc(), table);
        }

        return table.TryGetValue(key, out MountInfo? info) ? info.Type : null;
    }

    /// <summary>SYS-030: <c>GET sys/auth</c>, the same two-field shape as <see cref="ListMountsAsync"/>, with relative keys (<c>userpass/</c>, never <c>auth/userpass/</c>).</summary>
    public async Task<IReadOnlyDictionary<string, MountInfo>> ListAuthMethodsAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await FetchMountTableAsync("sys/auth", stripAuthPrefix: true, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>SYS-030: <c>POST sys/auth/{path}</c> → <c>204</c>. <c>auth/userpass/</c> and <c>userpass</c> are both accepted and normalise to the same mount.</summary>
    public async Task EnableAuthMethodAsync(string path, MountRequest request, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string wire = MountPaths.ToAuthWire(path, "path");
        _ = await logical.ExecuteShapedAsync(
            "POST", $"sys/auth/{wire}", SerialiseMountRequest(request), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>SYS-030: <c>DELETE sys/auth/{path}</c> → <c>204</c>. ⚠️ This revokes every token the method issued.</summary>
    public async Task DisableAuthMethodAsync(string path, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        string wire = MountPaths.ToAuthWire(path, "path");
        _ = await logical.ExecuteShapedAsync(
            "DELETE", $"sys/auth/{wire}", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// SYS-040: the legacy <c>sys/policy</c> surface, which the specification exposes as a
    /// <b>MAY</b>. Reads only — see DR-0012 D-M7-14 for why the legacy write and delete are not
    /// here.
    /// </summary>
    public LegacyPolicyOperations Legacy => new(context, activeNamespace);

    /// <summary>SYS-040: <c>GET sys/policies/acl</c> → <c>{"keys": [...]}</c>. In the root namespace the server appends <c>root</c> to the list; the SDK passes the list through as sent.</summary>
    public async Task<IReadOnlyList<string>> ListPoliciesAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "GET", "sys/policies/acl", null, options, defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken).ConfigureAwait(false);
        return SysWire.ReadKeys(response?.Data);
    }

    /// <summary>
    /// SYS-040: <c>GET sys/policies/acl/{name}</c> → <see cref="Policy"/>, or <see langword="null"/>
    /// when there is no such policy. The server's <c>404 No policy named: X</c> is Appendix B's
    /// <c>BV-NOTFOUND-005</c> rule, and is turned into absence here rather than raised, because the
    /// specification's response column writes the two together ("→ null / <c>BV-NOTFOUND-005</c>")
    /// and a reader is the one operation where "not there" is an answer rather than a failure.
    /// </summary>
    public async Task<Policy?> ReadPolicyAsync(string name, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        string policyName = MountPaths.ToWire(name, "name");
        Response? response;
        try
        {
            response = await logical.ExecuteShapedAsync(
                "GET", $"sys/policies/acl/{UrlBuilder.EncodePathSegment(policyName)}", null, options,
                defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        }
        catch (BastionVaultException failure) when (string.Equals(failure.Code, ErrorCodes.NotFoundPolicyNotFound, StringComparison.Ordinal))
        {
            return null;
        }

        return response?.Data is { } data ? SysWire.ToPolicy(data, policyName) : null;
    }

    /// <summary>
    /// SYS-040, SYS-041, SYS-042: <c>POST sys/policies/acl/{name}</c> with <c>{"policy": hcl}</c>
    /// → <c>204</c>. <c>root</c> and <c>test</c> are refused client-side with
    /// <c>BV-INPUT-010</c> (SYS-041; <c>test</c> is the dry-run route's own segment, so writing it
    /// would collide with <see cref="TestPolicyAsync"/>). The two SYS-042 server strings —
    /// sentinel policies inside a namespace, and the cross-namespace path refusal — are Appendix B
    /// §2 recognition rules and reach the caller as <c>BV-INPUT-100</c> and
    /// <c>BV-INPUT-102 CrossNamespacePolicyPath</c> through the shared mapping, with no
    /// operation-local remap.
    /// </summary>
    public async Task WritePolicyAsync(string name, string hcl, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hcl);
        string policyName = MountPaths.ToWire(name, "name");
        if (WriteReservedNames.Contains(policyName))
        {
            throw SysWire.ReservedPolicyName(policyName, "name");
        }

        _ = await logical.ExecuteShapedAsync(
            "POST", $"sys/policies/acl/{UrlBuilder.EncodePathSegment(policyName)}", SerialiseField("policy", hcl), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>
    /// SYS-041: <c>DELETE sys/policies/acl/{name}</c> → <c>204</c>. <c>default</c> and <c>root</c>
    /// are refused client-side with <c>BV-INPUT-010</c>. ⚠️ The reserved set differs from
    /// <see cref="WritePolicyAsync"/>'s and that is not an oversight: SYS-041 reserves <c>test</c>
    /// against <i>writes</i> (the dry-run route owns the segment) and <c>default</c> against
    /// <i>deletes</i> (it is the policy every token carries).
    /// </summary>
    public async Task DeletePolicyAsync(string name, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        string policyName = MountPaths.ToWire(name, "name");
        if (DeleteReservedNames.Contains(policyName))
        {
            throw SysWire.ReservedPolicyName(policyName, "name");
        }

        _ = await logical.ExecuteShapedAsync(
            "DELETE", $"sys/policies/acl/{UrlBuilder.EncodePathSegment(policyName)}", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>SYS-040: <c>GET sys/policies/acl/{name}/history</c> → the <c>entries</c> array, newest-first as the server orders it.</summary>
    public async Task<IReadOnlyList<PolicyHistoryEntry>> PolicyHistoryAsync(string name, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        string policyName = MountPaths.ToWire(name, "name");
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"sys/policies/acl/{UrlBuilder.EncodePathSegment(policyName)}/history", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);

        if (response?.Data is not { } data
            || !data.TryGetValue("entries", out JsonElement entries)
            || entries.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. entries.EnumerateArray()
            .Where(entry => entry.ValueKind == JsonValueKind.Object)
            .Select(entry => SysWire.AsMap(entry))
            .Select(entry => new PolicyHistoryEntry
            {
                Timestamp = SysWire.ReadRfc3339(entry, "ts"),
                User = SysWire.ReadString(entry, "user"),
                Op = SysWire.ReadString(entry, "op"),
                BeforeRaw = SysWire.ReadString(entry, "before_raw"),
                AfterRaw = SysWire.ReadString(entry, "after_raw"),
            })];
    }

    /// <summary>
    /// SYS-045: the policy dry-run, <c>POST /v2/sys/policies/acl/test</c>. The <c>/v2</c> prefix is
    /// pinned (TRN-071, Appendix A), so neither <c>ApiPrefix</c> nor
    /// <see cref="RequestOptions.ApiVersion"/> can route it at a handler that does not exist.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <see cref="PolicyTestCase.Policies"/> is tri-state and the three states are preserved
    /// onto the wire: <see langword="null"/> omits the key (server default <c>["default"]</c>),
    /// <c>[]</c> is sent as an empty array (the draft alone), and a list is sent as itself (the
    /// draft plus those).
    /// </para>
    /// <para>
    /// <paramref name="name"/> follows <paramref name="cases"/> rather than sitting between
    /// <paramref name="draft"/> and it, as <c>SYS-045</c>'s signature writes it: C# has no
    /// optional parameter before a required one. The wire order is unaffected.
    /// </para>
    /// <para>
    /// Naming <c>root</c> is <b>sent</b>, and the server's <c>400</c> is remapped to
    /// <c>BV-INPUT-010</c>, the code SYS-045 names. D-M7-26 overturned slice b's client-side
    /// refusal: SYS-045 writes the refusal as an HTTP status code and pairs it with an
    /// unreadable-policy <c>403</c> that cannot be known client-side, where SYS-041 says
    /// "client-side" in as many words. <see cref="BastionVaultException.Attempts"/> and
    /// <see cref="BastionVaultException.StatusCode"/> are therefore both observable. The remap is
    /// scoped to a call that actually named <c>root</c>, so every <i>other</i> <c>400</c> this
    /// route answers — a malformed draft first among them — keeps the shared mapping's answer.
    /// Naming a policy the token cannot read is the server's call and reaches the caller as the
    /// <c>403</c> → <c>BV-AUTHZ-001</c> the shared mapping already produces.
    /// </para>
    /// </remarks>
    public async Task<PolicyTestResult> TestPolicyAsync(
        string draft,
        IReadOnlyList<PolicyTestCase> cases,
        string? name = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(cases);
        string trimmedName = name?.Trim() ?? string.Empty;
        bool namedRoot = name is not null && string.Equals(trimmedName, RootPolicyName, StringComparison.Ordinal);

        Response? response;
        try
        {
            response = await logical.ExecuteShapedAsync(
                // The *trimmed* name is what travels, for D-M7-16's reason applied to this route:
                // the remap below keys on the trimmed value, so sending the untrimmed one would
                // let `" root "` be checked as `root` and sent as something else.
                "POST", "sys/policies/acl/test", SerialisePolicyTest(draft, name is null ? null : trimmedName, cases), PinV2(options),
                defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
        }
        catch (BastionVaultException failure) when (namedRoot && failure.StatusCode == 400)
        {
            throw RemapReservedDryRunName(failure, trimmedName);
        }

        IReadOnlyDictionary<string, JsonElement> data = response?.Data ?? throw EnvelopeMismatch("sys/policies/acl/test", "results");

        return new PolicyTestResult
        {
            ParseOk = ReadBool(data, "parse_ok"),
            Errors = SysWire.ReadStringArray(data, "errors"),
            Results = ReadPolicyTestResults(data),
        };
    }

    /// <summary>SYS-045: <c>GET /v2/sys/policy-tests/{name}</c> — the saved effectivity cases, <c>/v2</c>-pinned (TRN-071, Appendix A).</summary>
    public async Task<IReadOnlyList<PolicyTestCase>> ReadPolicyTestsAsync(string name, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        string policyName = MountPaths.ToWire(name, "name");
        RequestOptions pinned = PinV2(options);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"sys/policy-tests/{UrlBuilder.EncodePathSegment(policyName)}", null, pinned,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);

        if (response?.Data is not { } data
            || !data.TryGetValue("cases", out JsonElement element)
            || element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. element.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => SysWire.AsMap(item))
            .Select(ToPolicyTestCase)];
    }

    /// <summary>SYS-045: <c>POST /v2/sys/policy-tests/{name}</c> with <c>{"cases": [...]}</c>, <c>/v2</c>-pinned. The tri-state is preserved here too — the cases are serialised by the same writer the dry-run uses.</summary>
    public async Task WritePolicyTestsAsync(string name, IReadOnlyList<PolicyTestCase> cases, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cases);
        string policyName = MountPaths.ToWire(name, "name");
        RequestOptions pinned = PinV2(options);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"sys/policy-tests/{UrlBuilder.EncodePathSegment(policyName)}", SerialisePolicyTestCases(cases), pinned,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>SYS-060: <c>LIST sys/namespaces</c> → the children of the active namespace.</summary>
    public async Task<IReadOnlyList<string>> ListNamespacesAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "LIST", "sys/namespaces", null, options, defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken).ConfigureAwait(false);
        return SysWire.ReadKeys(response?.Data);
    }

    /// <summary>
    /// SYS-060, SYS-061: <c>GET sys/namespaces/{path}</c> → the record, or <see langword="null"/>
    /// when there is no such namespace (the server's <c>404 no such namespace: "x"</c> is
    /// Appendix B's <c>BV-NOTFOUND-007</c> rule). An empty <paramref name="path"/> is
    /// <c>BV-INPUT-001</c> client-side: it would address the root record, which SYS-061 says is not
    /// reachable over HTTP at all.
    /// </summary>
    public async Task<Namespace?> ReadNamespaceAsync(string path, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        string wire = MountPaths.ToWire(path, "path");
        Response? response;
        try
        {
            response = await logical.ExecuteShapedAsync(
                "GET", $"sys/namespaces/{UrlBuilder.EncodePathFragment(wire)}", null, options,
                defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        }
        catch (BastionVaultException failure) when (string.Equals(failure.Code, ErrorCodes.NotFoundNamespaceNotFound, StringComparison.Ordinal))
        {
            return null;
        }

        return response?.Data is { } data ? SysWire.ToNamespace(data, wire) : null;
    }

    /// <summary>
    /// SYS-060, SYS-061, SYS-062: <c>POST sys/namespaces/{path}</c> → <c>200</c> with the record.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is an <b>upsert</b> and a <b>full replace</b>. Every quota
    /// <paramref name="spec"/> omits is written as <c>0</c> and an omitted
    /// <see cref="NamespaceSpec.ChildVisibleDefault"/> is written as <see langword="false"/>, so
    /// writing a freshly constructed spec at an existing namespace <i>clears</i> its quotas rather
    /// than leaving them alone. <see cref="UpdateNamespaceAsync"/> is the read-merge-write form and
    /// is the one to use to change a single field.
    /// <para>
    /// SYS-061: <c>WriteNamespace("")</c> is refused client-side with <c>BV-INPUT-001</c>. The root
    /// namespace record cannot be written over HTTP and no operation for it exists on this class.
    /// </para>
    /// </remarks>
    public async Task<Namespace> WriteNamespaceAsync(string path, NamespaceSpec spec, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        string wire = MountPaths.ToWire(path, "path");
        Response? response = await logical.ExecuteShapedAsync(
            "POST", $"sys/namespaces/{UrlBuilder.EncodePathFragment(wire)}", SerialiseNamespaceSpec(spec), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        IReadOnlyDictionary<string, JsonElement> data = response?.Data ?? throw EnvelopeMismatch($"sys/namespaces/{wire}", "uuid");
        return SysWire.ToNamespace(data, wire);
    }

    /// <summary>
    /// SYS-060: the read-merge-write form of <see cref="WriteNamespaceAsync"/>. Reads the current
    /// record, overlays only the members <paramref name="patch"/> sets, and writes the result back
    /// — two round trips, and deliberately so, because the server has no partial update on this
    /// route. A namespace that does not exist raises <c>BV-NOTFOUND-007</c> rather than creating
    /// one: a patch has nothing to merge into, and an upsert here would silently write the patch's
    /// unset members as zeroes.
    /// </summary>
    public async Task<Namespace> UpdateNamespaceAsync(string path, NamespacePatch patch, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);
        string wire = MountPaths.ToWire(path, "path");
        Namespace current = await ReadNamespaceAsync(wire, options, cancellationToken).ConfigureAwait(false)
            ?? throw NamespaceNotFound(wire);

        NamespaceSpec merged = new()
        {
            ChildVisibleDefault = patch.ChildVisibleDefault ?? current.ChildVisibleDefault,
            Quotas = new NamespaceQuotas
            {
                MaxStorageBytes = patch.MaxStorageBytes ?? current.Quotas.MaxStorageBytes,
                MaxLeases = patch.MaxLeases ?? current.Quotas.MaxLeases,
                RequestRate = patch.RequestRate ?? current.Quotas.RequestRate,
                MaxMounts = patch.MaxMounts ?? current.Quotas.MaxMounts,
                MaxEntities = patch.MaxEntities ?? current.Quotas.MaxEntities,
                MaxChildNamespaces = patch.MaxChildNamespaces ?? current.Quotas.MaxChildNamespaces,
            },
        };

        return await WriteNamespaceAsync(wire, merged, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// SYS-062: <c>DELETE sys/namespaces/{path}</c> → <c>204</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>This destroys tenant data.</b> The delete <b>cascades</b>: every mount inside the
    /// namespace is unmounted and its stored secrets go with it. The server refuses while child
    /// namespaces exist, so a tenant tree is removed leaves-first; there is no recursive form and
    /// the SDK does not synthesise one. The operation is named <c>DeleteNamespace</c> and never
    /// <c>Remove…</c>, because SYS-062 requires the destructive word.
    /// </remarks>
    public async Task DeleteNamespaceAsync(string path, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        string wire = MountPaths.ToWire(path, "path");
        _ = await logical.ExecuteShapedAsync(
            "DELETE", $"sys/namespaces/{UrlBuilder.EncodePathFragment(wire)}", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>SYS-060: <c>GET sys/namespaces-self</c>. <c>""</c> denotes root in both <see cref="NamespacesSelf.Namespaces"/> and <see cref="NamespacesSelf.TokenNamespace"/>.</summary>
    public async Task<NamespacesSelf> NamespacesSelfAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "GET", "sys/namespaces-self", null, options, defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, JsonElement> data = response?.Data ?? throw EnvelopeMismatch("sys/namespaces-self", "namespaces");

        return new NamespacesSelf
        {
            Namespaces = SysWire.ReadStringArray(data, "namespaces"),
            TokenNamespace = ReadString(data, "token_namespace") ?? string.Empty,
            Root = ReadBool(data, "root"),
        };
    }

    /// <summary>
    /// SYS-060: <c>GET sys/namespaces-info?after=&amp;limit=</c>, the cursor-paginated bulk listing
    /// (14 — batch and request efficiency). <paramref name="limit"/> defaults to 100 and is
    /// validated to <c>1 ≤ limit ≤ 500</c> client-side (<c>BV-INPUT-004</c>);
    /// <paramref name="after"/> is the previous page's <see cref="Page{T}.Next"/>, passed verbatim
    /// and never computed. An empty <c>next</c> is exposed as <see langword="null"/>, and a page
    /// whose <c>keys</c> and <c>records</c> differ in length is <c>BV-PROTOCOL-002</c>.
    /// </summary>
    public async Task<Page<Namespace>> ListNamespacesInfoAsync(
        string? after = null,
        int? limit = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        int effectiveLimit = limit ?? DefaultPageLimit;
        if (effectiveLimit is < 1 or > MaxPageLimit)
        {
            throw SysWire.OutOfRange("limit", effectiveLimit);
        }

        string query = after is null
            ? $"limit={effectiveLimit.ToString(CultureInfo.InvariantCulture)}"
            : $"after={UrlBuilder.EncodeQueryValue(after)}&limit={effectiveLimit.ToString(CultureInfo.InvariantCulture)}";

        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"sys/namespaces-info?{query}", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        IReadOnlyDictionary<string, JsonElement> data = response?.Data ?? throw EnvelopeMismatch("sys/namespaces-info", "keys");

        IReadOnlyList<string> keys = SysWire.ReadKeys(data);
        List<Namespace> records = [];
        if (data.TryGetValue("records", out JsonElement recordsElement) && recordsElement.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement record in recordsElement.EnumerateArray())
            {
                string fallback = index < keys.Count ? keys[index] : string.Empty;
                records.Add(record.ValueKind == JsonValueKind.Object
                    ? SysWire.ToNamespace(SysWire.AsMap(record), fallback)
                    : throw EnvelopeMismatch("sys/namespaces-info", "records[]"));
                index++;
            }
        }

        if (records.Count != keys.Count)
        {
            throw EnvelopeMismatch("sys/namespaces-info", "records");
        }

        string? next = ReadString(data, "next");
        return new Page<Namespace>
        {
            Keys = keys,
            Records = records,
            Total = ReadInt(data, "total") ?? keys.Count,
            Next = string.IsNullOrEmpty(next) ? null : next,
            Truncated = ReadBool(data, "truncated"),
        };
    }

    // ---- M7c: audit, the Complete-tier admin surfaces, backup/restore, RES-030 -------------

    /// <summary>SYS-070: the audit device registry and the audit event query.</summary>
    public AuditOperations Audit => new(context, activeNamespace);

    /// <summary>The DoS-guard admin surface (06 — "Batch, cache version, DoS"). Root-only, <c>/v2</c>-pinned, and carrying no <c>SYS-*</c> id.</summary>
    public DosOperations Dos => new(context, activeNamespace);

    /// <summary>The four admin owner-transfer routes. No <c>SYS-*</c> id and no specified body — see <see cref="OwnerTransferOperations"/>.</summary>
    public OwnerTransferOperations OwnerTransfer => new(context, activeNamespace);

    /// <summary>The four <c>sys/exchange/*</c> routes. No <c>SYS-*</c> id and no specified body — see <see cref="ExchangeOperations"/>.</summary>
    public ExchangeOperations Exchange => new(context, activeNamespace);

    /// <summary>
    /// 06 — "Dashboard…": <c>GET sys/dashboard/summary</c>. ⚠️ <c>audit_24h</c> and
    /// <c>attention</c> are omitted for a caller without audit read and stay optional here; the
    /// rest of the body is on <see cref="DashboardSummary.Raw"/>, because the specification names
    /// only those two keys.
    /// </summary>
    public async Task<DashboardSummary> DashboardSummaryAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "GET", "sys/dashboard/summary", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, JsonElement> data = response?.Data ?? throw EnvelopeMismatch("sys/dashboard/summary", "data");

        return new DashboardSummary
        {
            Audit24h = data.TryGetValue("audit_24h", out JsonElement audit) ? audit : null,
            Attention = data.TryGetValue("attention", out JsonElement attention) ? attention : null,
            Raw = response.Raw,
        };
    }

    /// <summary>06 — "Dashboard…": <c>GET sys/sso/settings</c>. Returned unparsed: the specification names the route and no field of the body (D-M1c-25).</summary>
    public async Task<JsonElement> SsoSettingsAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "GET", "sys/sso/settings", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
        return response?.Raw ?? throw EnvelopeMismatch("sys/sso/settings", "body");
    }

    /// <summary>06 — "Dashboard…": <c>GET sys/sso/providers</c>. Returned unparsed, for the same reason as <see cref="SsoSettingsAsync"/>.</summary>
    public async Task<JsonElement> SsoProvidersAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "GET", "sys/sso/providers", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
        return response?.Raw ?? throw EnvelopeMismatch("sys/sso/providers", "body");
    }

    /// <summary>
    /// SYS-090: <c>POST sys/backup</c> → the raw <c>.bvbk</c> bytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Excluded from retry and from failover</b>, through the two flags SYS-013 and DSC-045
    /// already own — <c>nonRetryable</c> short-circuits the retry predicate ahead of every policy
    /// term, and <c>nodeLocal</c> removes the call from <c>WillFailover</c>. No third mechanism
    /// was added (D-M7-35).
    /// </para>
    /// <para>
    /// The response is <b>not buffered above <c>MaxResponseBytes</c></b>: the transport bounds the
    /// read and aborts past the limit (TRN-033, D-M1b-20), so a backup larger than the configured
    /// bound raises <c>BV-TRANSPORT-004</c> rather than landing in memory. Raise
    /// <c>MaxResponseBytes</c> to take a larger one. See DR-0012 D-M7-34 for why this satisfies
    /// SYS-090's "MUST stream" and what a <c>Stream</c>-returning overload would have cost.
    /// </para>
    /// <para>
    /// <b>The two bounds are not symmetric, and this one is not configurable.</b>
    /// <see cref="RestoreAsync"/> is capped at <c>MaxRequestBodyBytes</c> (32 MiB, TRN-032), which
    /// no option raises. A vault whose backup exceeds 32 MiB therefore backs up successfully — the
    /// response bound above is both larger by default and adjustable — and cannot be restored
    /// through this SDK at all. Spec-correct, but the asymmetry is real, so do not read the advice
    /// above as implying the restore path will accept whatever the backup path produced (D-M7-46).
    /// </para>
    /// </remarks>
    public async Task<byte[]> BackupAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        RawResponse response = await logical.ExecuteBinaryAsync(
            "POST", "sys/backup", null, RequestExecutor.BinaryShape.Response, options, cancellationToken).ConfigureAwait(false);
        return response.Body.ToArray();
    }

    /// <summary>
    /// SYS-090, SYS-091: <c>POST sys/restore</c> with the raw <c>.bvbk</c> bytes as the request
    /// body (<c>Content-Type: application/octet-stream</c>).
    /// </summary>
    /// <remarks>
    /// Excluded from retry and failover exactly as <see cref="BackupAsync"/> is, and for a stronger
    /// reason: a replayed restore would re-apply a whole vault image, and a failover would apply it
    /// to a node the caller did not choose.
    /// <para>
    /// SYS-091: an HMAC, magic-number, version or corruption failure is a <c>500</c> whose message
    /// Appendix B §2 already recognises (<c>backup hmac verification failed</c>,
    /// <c>hmac verification failed</c>, and the <c>backup</c> + <c>invalid magic</c> /
    /// <c>unsupported version</c> / <c>corrupted</c> prefix rule), so it reaches the caller as
    /// <c>BV-INPUT-103 BackupFileInvalid</c>, non-retryable, and no code was minted.
    /// <b>An operation-local remap IS installed below</b> (D-M7-36), because the generated rule for
    /// the three-token form can never fire: the generator compiles Appendix B's <c>+ a/b/c</c>
    /// alternation into a <c>ContainsAll</c> conjunction, so only the <c>hmac verification failed</c>
    /// arm is reachable through the shared table (R-23). The paragraph above previously claimed no
    /// remap existed, which contradicted the code three lines below it and would have led a Rust or
    /// Python transcriber to omit the remap and ship the retryable <c>BV-SERVER-005</c> for three of
    /// SYS-091's four named failures. The remap deletes when R-23 is fixed, and is a no-op before
    /// then if the generator is corrected first.
    /// </para>
    /// </remarks>
    public async Task<RestoreResult> RestoreAsync(ReadOnlyMemory<byte> backup, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        RawResponse response;
        try
        {
            response = await logical.ExecuteBinaryAsync(
                "POST", "sys/restore", backup, RequestExecutor.BinaryShape.Request, options, cancellationToken).ConfigureAwait(false);
        }
        catch (BastionVaultException failure) when (IsBackupIntegrityFailure(failure))
        {
            throw RemapBackupIntegrityFailure(failure);
        }

        if (response.Body.Length == 0)
        {
            throw EnvelopeMismatch("sys/restore", "entries_restored");
        }

        using JsonDocument document = JsonDocument.Parse(response.Body);
        JsonElement root = document.RootElement.Clone();
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw EnvelopeMismatch("sys/restore", "entries_restored");
        }

        Dictionary<string, JsonElement> data = SysWire.AsMap(root);
        return new RestoreResult
        {
            EntriesRestored = ReadLong(data, "entries_restored")
                ?? throw EnvelopeMismatch("sys/restore", "entries_restored"),
            Raw = root,
        };
    }

    /// <summary>
    /// RES-030: <c>Sys.Seal</c> against <b>every</b> discovered candidate, including the sealed and
    /// the unreachable ones, returning one result per node keyed by its URL.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deferred from M5 by D-M5-3 only because <c>SYS-012</c>/<c>SYS-013</c> did not exist; slice a
    /// landed them, so the deferral is discharged here.
    /// </para>
    /// <para>
    /// ⚠️ <b>Not subject to failover or retry</b> (RES-030 says so explicitly, and SYS-013 already
    /// does for the base operations). A node that refuses the connection becomes a
    /// <see cref="ClusterNodeResult"/> with <see cref="ClusterNodeResult.Succeeded"/> false and its
    /// error attached; it does not abort the fan-out, because the point of the variant is to reach
    /// every node.
    /// </para>
    /// <para>
    /// Candidates are <b>not probed and not filtered</b>: RES-030 says "all discovered candidates
    /// (including sealed/unreachable)", so the DSC-030…033 eligibility rules that pick <i>one</i>
    /// node deliberately do not apply. On a literal-address client the candidate set is the single
    /// configured address, which is what "all discovered candidates" means when discovery did not
    /// run (DSC-001).
    /// </para>
    /// <para>
    /// Nodes are visited <b>sequentially</b>, in candidate order. A fan-out in parallel would be
    /// faster and is what a first draft reaches for, but unsealing is a per-node share counter and
    /// a caller reading a partial result while the operation is still running has no way to tell a
    /// slow node from a failed one.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyDictionary<string, ClusterNodeResult>> SealClusterWideAsync(
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return await FanOutAsync(
            async (endpoint, ct) =>
            {
                _ = await logical.ExecuteShapedAsync(
                    "PUT", "sys/seal", null, options, defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, ct,
                    nodeLocal: true, nonRetryable: true, endpointOverride: endpoint).ConfigureAwait(false);
                return (SealStatus?)null;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// RES-030: <c>Sys.Unseal</c> against every discovered candidate, returning each node's
    /// <see cref="SealStatus"/> — which is what makes the variant necessary, since unseal progress
    /// is counted per node. Same exclusions, same ordering and same candidate rule as
    /// <see cref="SealClusterWideAsync"/>.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, ClusterNodeResult>> UnsealClusterWideAsync(
        string key,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return await FanOutAsync(
            async (endpoint, ct) =>
            {
                Response? response = await logical.ExecuteShapedAsync(
                    "PUT", "sys/unseal", SerialiseField("key", key), options,
                    defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, ct,
                    nodeLocal: true, nonRetryable: true, endpointOverride: endpoint).ConfigureAwait(false);
                IReadOnlyDictionary<string, JsonElement> data = response?.Data ?? throw EnvelopeMismatch("sys/unseal", "data");
                return ToSealStatus(data);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyDictionary<string, ClusterNodeResult>> FanOutAsync(
        Func<string, CancellationToken, Task<SealStatus?>> call,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> endpoints = await context.Discovery.ClusterWideEndpointsAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, ClusterNodeResult> results = new(StringComparer.Ordinal);
        foreach (string endpoint in endpoints)
        {
            try
            {
                SealStatus? status = await call(endpoint, cancellationToken).ConfigureAwait(false);
                results[endpoint] = new ClusterNodeResult { Url = endpoint, Succeeded = true, SealStatus = status };
            }
            catch (BastionVaultException failure)
            {
                // RES-030's own words: the variants iterate *all* candidates "including
                // sealed/unreachable". A node that answered with an error is a result, not the end
                // of the fan-out.
                results[endpoint] = new ClusterNodeResult { Url = endpoint, Succeeded = false, Error = failure };
            }
        }

        return results;
    }

    /// <summary>SYS-005's parse, shared by <see cref="SealStatusAsync"/> and <see cref="UnsealAsync"/> so the <c>t</c>/<c>n</c> swap is applied in one place.</summary>
    private static SealStatus ToSealStatus(IReadOnlyDictionary<string, JsonElement> data)
    {
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
    /// SYS-010's two client-side rules: both arguments or neither, and
    /// <c>1 ≤ threshold ≤ shares ≤ 255</c>. Refused before any request is sent, which is what makes
    /// <c>attempts</c> zero on the resulting error.
    /// </summary>
    private static void ValidateInitArguments(int? shares, int? threshold)
    {
        if (shares is null && threshold is null)
        {
            return;
        }

        if (shares is null || threshold is null)
        {
            throw InvalidInitArgument(
                shares is null ? "shares" : "threshold",
                "secret_shares and secret_threshold must be given together or both omitted");
        }

        if (shares.Value is < 1 or > 255)
        {
            throw InvalidInitArgument("shares", "shares must be between 1 and 255");
        }

        if (threshold.Value < 1 || threshold.Value > shares.Value)
        {
            throw InvalidInitArgument("threshold", "threshold must be between 1 and shares");
        }
    }

    private static BastionVaultException InvalidInitArgument(string argument, string reason)
    {
        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.InputInvalidArgument);
        return BastionVaultException.Request(
            ErrorCodes.InputInvalidArgument,
            entry.Category,
            entry.Message,
            entry.Hint,
            retryable: false,
            attempts: 0,
            details: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["argument"] = argument,
                ["reason"] = reason,
            });
    }

    /// <summary>
    /// SYS-023's third row. <c>no matching mount at</c> and <c>path already in use at</c> are
    /// Appendix B §2 recognition rules and need nothing here; <c>Unknown mount table type.</c> is
    /// not, and a <c>409</c> the recogniser does not match defaults to <c>BV-CONFLICT-001</c>
    /// (D-M1c-19), which is the wrong code. Remapped at this operation rather than by adding an
    /// Appendix B row: the catalogue is generated and a new row is an R3 specification change
    /// (D-M1c-1), and the message is meaningful only on this endpoint anyway.
    /// </summary>
    private static bool IsUnknownMountTableType(BastionVaultException failure)
    {
        return failure.StatusCode == 409
            && failure.ServerMessage is { } message
            && message.Trim().TrimEnd('.').Equals("Unknown mount table type", StringComparison.OrdinalIgnoreCase);
    }

    private static BastionVaultException RemapUnknownMountTableType(BastionVaultException failure)
    {
        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.InputServerRejectedRequest);
        return new BastionVaultException(
            ErrorCodes.InputServerRejectedRequest,
            entry.Category,
            entry.Message,
            entry.Hint,
            retryable: entry.Retryable,
            attempts: failure.Attempts,
            serverMessage: failure.ServerMessage,
            serverErrors: failure.ServerErrors,
            statusCode: failure.StatusCode,
            retryAfter: failure.RetryAfter,
            method: failure.Method,
            path: failure.Path,
            address: failure.Address,
            details: failure.Details);
    }

    private async Task<IReadOnlyDictionary<string, MountInfo>> FetchMountTableAsync(
        string path, bool stripAuthPrefix, RequestOptions? options, CancellationToken cancellationToken)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "GET", path, null, options, defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, JsonElement> data = response?.Data ?? throw EnvelopeMismatch(path, "data");

        Dictionary<string, MountInfo> table = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, JsonElement> entry in data)
        {
            if (entry.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            // Shape B (raw) means the table *is* the top-level object, so a non-entry property the
            // server may add alongside it (it sends none today) must not become a mount with no
            // type rather than being skipped.
            if (!entry.Value.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            table[MountPaths.NormaliseServerKey(entry.Key, stripAuthPrefix)] = new MountInfo
            {
                Type = type.GetString()!,
                Description = entry.Value.TryGetProperty("description", out JsonElement description) && description.ValueKind == JsonValueKind.String
                    ? description.GetString()
                    : null,
            };
        }

        return table;
    }

    private static Dictionary<string, MountDetail> ReadDetailMap(
        IReadOnlyDictionary<string, JsonElement> data, string half, bool stripAuthPrefix)
    {
        Dictionary<string, MountDetail> result = new(StringComparer.Ordinal);
        if (!data.TryGetValue(half, out JsonElement element) || element.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object
                || !property.Value.TryGetProperty("type", out JsonElement type)
                || type.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            result[MountPaths.NormaliseServerKey(property.Name, stripAuthPrefix)] = new MountDetail
            {
                Type = type.GetString()!,
                Description = property.Value.TryGetProperty("description", out JsonElement description) && description.ValueKind == JsonValueKind.String
                    ? description.GetString()
                    : null,
                Uuid = property.Value.TryGetProperty("uuid", out JsonElement uuid) && uuid.ValueKind == JsonValueKind.String
                    ? uuid.GetString()
                    : null,
                Options = property.Value.TryGetProperty("options", out JsonElement optionsElement) && optionsElement.ValueKind == JsonValueKind.Object
                    ? optionsElement.EnumerateObject().ToDictionary(option => option.Name, option => option.Value.Clone(), StringComparer.Ordinal)
                    : null,
            };
        }

        return result;
    }

    /// <summary>SYS-010's body: both fields or an empty object (the HSM auto-unseal case).</summary>
    private static ReadOnlyMemory<byte> SerialiseInit(int? shares, int? threshold)
    {
        ArrayBufferWriter<byte> buffer = new();
        using Utf8JsonWriter writer = new(buffer);
        writer.WriteStartObject();
        if (shares is { } sharesValue && threshold is { } thresholdValue)
        {
            writer.WriteNumber("secret_shares", sharesValue);
            writer.WriteNumber("secret_threshold", thresholdValue);
        }

        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenMemory;
    }

    private static ReadOnlyMemory<byte> SerialiseField(string name, string value)
    {
        ArrayBufferWriter<byte> buffer = new();
        using Utf8JsonWriter writer = new(buffer);
        writer.WriteStartObject();
        writer.WriteString(name, value);
        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenMemory;
    }

    private static ReadOnlyMemory<byte> SerialiseRemount(string from, string to)
    {
        ArrayBufferWriter<byte> buffer = new();
        using Utf8JsonWriter writer = new(buffer);
        writer.WriteStartObject();
        writer.WriteString("from", from);
        writer.WriteString("to", to);
        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenMemory;
    }

    /// <summary>SYS-020's body: <c>type</c>, an optional <c>description</c> and an optional <c>options</c> map. No <c>config</c>, ever.</summary>
    private static ReadOnlyMemory<byte> SerialiseMountRequest(MountRequest request)
    {
        ArrayBufferWriter<byte> buffer = new();
        using Utf8JsonWriter writer = new(buffer);
        writer.WriteStartObject();
        writer.WriteString("type", request.Type);
        if (request.Description is { } description)
        {
            writer.WriteString("description", description);
        }

        if (request.Options is { Count: > 0 } options)
        {
            writer.WriteStartObject("options");
            foreach (KeyValuePair<string, string> option in options)
            {
                writer.WriteString(option.Key, option.Value);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenMemory;
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

    /// <summary>
    /// TRN-071's pin, in one place. Slice a and slice b each wrote this expression inline at their
    /// own call sites; slice c adds fourteen more <c>/v2</c>-only routes (SYS-080, the DoS surface),
    /// and fifteen copies of a pin is fifteen chances for one of them to be written as a
    /// <i>default</i> — <c>options?.ApiVersion ?? "v2"</c> — which a per-call
    /// <see cref="RequestOptions.ApiVersion"/> would then override away, at a handler that does not
    /// exist. Pinning means the override loses.
    /// </summary>
    private static RequestOptions PinV2(RequestOptions? options)
    {
        return (options ?? new RequestOptions()) with { ApiVersion = "v2" };
    }

    /// <summary>
    /// SYS-091's three non-HMAC failure modes, remapped at this operation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This exists because the generated catalogue cannot currently express the rule, and it
    /// deletes cleanly when it can.</b> Appendix B §2 writes the row as
    /// <c>prefix `backup hmac verification failed` / `backup` + `invalid magic`/`unsupported
    /// version`/`corrupted` → BV-INPUT-103</c>. The generator splits the <i>top-level</i> <c>/</c>
    /// into two rules correctly, but renders the second rule's <c>+ a/b/c</c> as a
    /// <b>ContainsAll</b> — an <c>AND</c> over all three tokens — where the appendix means an
    /// alternation. No real message contains "invalid magic" <i>and</i> "unsupported version"
    /// <i>and</i> "corrupted", so three of SYS-091's four named failures fall through to the
    /// status table as <c>BV-SERVER-005</c> instead of the <c>BV-INPUT-103</c> the requirement
    /// names. The HMAC arm is unaffected: it is its own prefix rule, plus a
    /// <c>contains (500) hmac verification failed</c> row.
    /// </para>
    /// <para>
    /// Remapped here rather than by changing the generator, on D-M7-6's grounds and one more:
    /// recognition semantics are a <i>cross-language</i> contract and the same defect reaches the
    /// <c>BV-INPUT-102</c> cross-namespace row, so the fix is an Appendix B / generator change and
    /// therefore R3 and the Strategic tree's (CRS-004). Reported, not silently corrected. See
    /// DR-0012 D-M7-33.
    /// </para>
    /// </remarks>
    private static bool IsBackupIntegrityFailure(BastionVaultException failure)
    {
        if (failure.StatusCode != 500 || failure.ServerMessage is not { } message)
        {
            return false;
        }

        // OrdinalIgnoreCase rather than a lower-cased copy: MessageRecognition normalises by
        // lower-casing, but CA1308 forbids that here and the comparison is the same either way.
        string normalised = message.Trim();
        return normalised.StartsWith("backup", StringComparison.OrdinalIgnoreCase)
            && BackupIntegrityTokens.Any(token => normalised.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Appendix B §2's own three alternatives for the non-HMAC arm of SYS-091.</summary>
    private static readonly string[] BackupIntegrityTokens = ["invalid magic", "unsupported version", "corrupted"];

    private static BastionVaultException RemapBackupIntegrityFailure(BastionVaultException failure)
    {
        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.InputBackupFileInvalid);
        return new BastionVaultException(
            ErrorCodes.InputBackupFileInvalid,
            entry.Category,
            entry.Message,
            entry.Hint,
            retryable: entry.Retryable,
            attempts: failure.Attempts,
            serverMessage: failure.ServerMessage,
            serverErrors: failure.ServerErrors,
            statusCode: failure.StatusCode,
            retryAfter: failure.RetryAfter,
            method: failure.Method,
            path: failure.Path,
            address: failure.Address,
            details: failure.Details);
    }

    /// <summary>
    /// SYS-045 row 1, as ruled by D-M7-26: <c>Naming root → 400 BV-INPUT-010</c>. The message text
    /// is unspecified, so there is nothing for an Appendix B §2 recognition rule to match on and an
    /// unrecognised <c>400</c> would reach the caller as <c>BV-INPUT-100</c>. Remapped at the
    /// operation, as D-M7-6 remaps SYS-023's third row, and scoped to a call that actually named
    /// <c>root</c> so the route's other <c>400</c>s are untouched. Everything a caller diagnoses
    /// with — attempts, status code, server message — is carried through unchanged.
    /// </summary>
    private static BastionVaultException RemapReservedDryRunName(BastionVaultException failure, string name)
    {
        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.InputReservedPolicyName);
        Dictionary<string, object?> details = new(failure.Details, StringComparer.Ordinal)
        {
            ["argument"] = "name",
            ["name"] = name,
        };

        return new BastionVaultException(
            ErrorCodes.InputReservedPolicyName,
            entry.Category,
            entry.Message,
            entry.Hint,
            retryable: entry.Retryable,
            attempts: failure.Attempts,
            serverMessage: failure.ServerMessage,
            serverErrors: failure.ServerErrors,
            statusCode: failure.StatusCode,
            retryAfter: failure.RetryAfter,
            method: failure.Method,
            path: failure.Path,
            address: failure.Address,
            details: details);
    }

    /// <summary>SYS-041: the names <see cref="WritePolicyAsync"/> refuses.</summary>
    private static readonly HashSet<string> WriteReservedNames = new(StringComparer.Ordinal) { "root", "test" };

    /// <summary>SYS-041: the names <see cref="DeletePolicyAsync"/> refuses. Deliberately a different set — see that member's remarks.</summary>
    private static readonly HashSet<string> DeleteReservedNames = new(StringComparer.Ordinal) { "root", "default" };

    private const string RootPolicyName = "root";

    /// <summary>PAG-001's client-side default and cap.</summary>
    private const int DefaultPageLimit = 100;
    private const int MaxPageLimit = 500;

    /// <summary>
    /// SYS-045's request body. The tri-state lives here and nowhere else: <c>policies</c> is
    /// written only when the case's list is non-<see langword="null"/>, and an empty list is
    /// written as an empty array rather than skipped.
    /// </summary>
    private static ReadOnlyMemory<byte> SerialisePolicyTest(string draft, string? name, IReadOnlyList<PolicyTestCase> cases)
    {
        ArrayBufferWriter<byte> buffer = new();
        using Utf8JsonWriter writer = new(buffer);
        writer.WriteStartObject();
        writer.WriteString("policy", draft);
        if (name is not null)
        {
            writer.WriteString("name", name);
        }

        writer.WriteStartArray("cases");
        foreach (PolicyTestCase testCase in cases)
        {
            WritePolicyTestCase(writer, testCase);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenMemory;
    }

    /// <summary>SYS-045: <c>WritePolicyTests</c>' body, using the same case writer so the tri-state cannot be preserved on one route and lost on the other.</summary>
    private static ReadOnlyMemory<byte> SerialisePolicyTestCases(IReadOnlyList<PolicyTestCase> cases)
    {
        ArrayBufferWriter<byte> buffer = new();
        using Utf8JsonWriter writer = new(buffer);
        writer.WriteStartObject();
        writer.WriteStartArray("cases");
        foreach (PolicyTestCase testCase in cases)
        {
            WritePolicyTestCase(writer, testCase);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenMemory;
    }

    private static void WritePolicyTestCase(Utf8JsonWriter writer, PolicyTestCase testCase)
    {
        writer.WriteStartObject();
        writer.WriteString("path", testCase.Path);
        writer.WriteString("capability", testCase.Capability.WireValue);

        // SYS-045's tri-state, in the one place it is decided.
        if (testCase.Policies is { } policies)
        {
            writer.WriteStartArray("policies");
            foreach (string policy in policies)
            {
                writer.WriteStringValue(policy);
            }

            writer.WriteEndArray();
        }

        if (testCase.Env is { } env)
        {
            writer.WriteString("env", env);
        }

        writer.WriteEndObject();
    }

    private static IReadOnlyList<PolicyTestCaseResult> ReadPolicyTestResults(IReadOnlyDictionary<string, JsonElement> data)
    {
        if (!data.TryGetValue("results", out JsonElement results) || results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. results.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => SysWire.AsMap(item))
            .Select(item => new PolicyTestCaseResult
            {
                Path = SysWire.ReadString(item, "path") ?? string.Empty,
                Capability = Capability.FromWire(SysWire.ReadString(item, "capability") ?? string.Empty),
                Allowed = ReadBool(item, "allowed"),
                MatchedPath = SysWire.ReadString(item, "matched_path"),
                MatchKind = PolicyMatchKind.FromWire(SysWire.ReadString(item, "match_kind")),
                DeniedByDeny = ReadBool(item, "denied_by_deny"),
                GrantingPolicies = SysWire.ReadStringArray(item, "granting_policies"),
                EvaluatedPolicies = SysWire.ReadStringArray(item, "evaluated_policies"),
                MissingPolicies = SysWire.ReadStringArray(item, "missing_policies"),
                DraftOnlyAllowed = ReadBool(item, "draft_only_allowed"),
            })];
    }

    /// <summary>SYS-045: reading a saved case back preserves the tri-state too — an absent <c>policies</c> key is <see langword="null"/>, an empty array is an empty list.</summary>
    private static PolicyTestCase ToPolicyTestCase(IReadOnlyDictionary<string, JsonElement> item)
    {
        return new PolicyTestCase
        {
            Path = SysWire.ReadString(item, "path") ?? string.Empty,
            Capability = Capability.FromWire(SysWire.ReadString(item, "capability") ?? string.Empty),
            Policies = item.TryGetValue("policies", out JsonElement policies) && policies.ValueKind == JsonValueKind.Array
                ? SysWire.ReadStringArray(item, "policies")
                : null,
            Env = SysWire.ReadString(item, "env"),
        };
    }

    /// <summary>SYS-060's full-replace body. Every field is always written, because an omitted one is a reset and the caller is entitled to see that on the wire.</summary>
    private static ReadOnlyMemory<byte> SerialiseNamespaceSpec(NamespaceSpec spec)
    {
        NamespaceQuotas quotas = spec.Quotas ?? new NamespaceQuotas();
        ArrayBufferWriter<byte> buffer = new();
        using Utf8JsonWriter writer = new(buffer);
        writer.WriteStartObject();
        writer.WriteBoolean("child_visible_default", spec.ChildVisibleDefault);
        writer.WriteStartObject("quotas");
        writer.WriteNumber("max_storage_bytes", quotas.MaxStorageBytes);
        writer.WriteNumber("max_leases", quotas.MaxLeases);
        writer.WriteNumber("request_rate", quotas.RequestRate);
        writer.WriteNumber("max_mounts", quotas.MaxMounts);
        writer.WriteNumber("max_entities", quotas.MaxEntities);
        writer.WriteNumber("max_child_namespaces", quotas.MaxChildNamespaces);
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenMemory;
    }

    private static BastionVaultException NamespaceNotFound(string path)
    {
        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.NotFoundNamespaceNotFound);
        return BastionVaultException.Request(
            ErrorCodes.NotFoundNamespaceNotFound,
            entry.Category,
            entry.Message,
            entry.Hint,
            retryable: false,
            attempts: 0,
            path: $"sys/namespaces/{path}",
            details: new Dictionary<string, object?>(StringComparer.Ordinal) { ["namespace"] = path });
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
