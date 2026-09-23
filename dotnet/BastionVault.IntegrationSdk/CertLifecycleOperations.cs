using System.Globalization;
using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// 12 §Cert lifecycle (OVR-008), reached from <see cref="BastionVaultClient.CertLifecycle"/>.
/// <c>mount</c> defaults to <c>"cert-lifecycle"</c>. This area carries no requirement ID of its
/// own; every MUST governing it is the generic Shape A envelope and standard error mapping
/// (03/04) that already binds every route in this SDK (DR-0017).
/// </summary>
public sealed class CertLifecycleOperations
{
    private const string DefaultMount = "cert-lifecycle";

    private readonly LogicalOperations logical;

    internal CertLifecycleOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
    }

    /// <summary>Lists the renewal-target names under <paramref name="mount"/>: <c>LIST {mount}/targets/</c>.</summary>
    /// <remarks>
    /// Wire params: <paramref name="mount"/> builds the route; no query or body params. Returns an
    /// empty list when the backend has none, never <see langword="null"/>. Conformance: Complete.
    /// No error codes beyond the common set (ERR-061).
    /// </remarks>
    /// <spec>CertLifecycle.ListTargets — 12-other-engines-and-identity.md</spec>
    public async Task<IReadOnlyList<string>> ListTargetsAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "LIST", $"{Encode(mount)}/targets/", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return KvWire.ReadKeys(response);
    }

    /// <summary>
    /// 14 §Bulk metadata listings: <c>GET /v2/{mount}/targets-info?after=&amp;limit=</c>. Pinned to
    /// <c>/v2</c> per <c>14-batch-and-request-efficiency.md:102</c>, which names this exact route.
    /// </summary>
    /// <remarks>
    /// Wire params: <paramref name="mount"/> builds the route; query carries <paramref name="after"/>
    /// (cursor, PAG-002) and <paramref name="limit"/> (defaulted to 100 and validated to
    /// <c>1-500</c>, PAG-001). Returns <see cref="Page{T}"/> of <see cref="Target"/>, never
    /// <see langword="null"/>. Conformance: Complete (PAG-001). Errors beyond the common set
    /// (ERR-061): <c>BV-INPUT-004</c> (limit out of range, PAG-001); <c>BV-PROTOCOL-002</c>
    /// (records/keys length mismatch, PAG-005).
    /// </remarks>
    /// <spec>CertLifecycle.ListTargetsInfo — PAG-001</spec>
    public async Task<Page<Target>> ListTargetsInfoAsync(
        string mount = DefaultMount, string? after = null, int? limit = null,
        RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        int effectiveLimit = PagingWire.ValidateLimit(limit);
        string query = after is null
            ? $"limit={effectiveLimit.ToString(CultureInfo.InvariantCulture)}"
            : $"after={UrlBuilder.EncodeQueryValue(after)}&limit={effectiveLimit.ToString(CultureInfo.InvariantCulture)}";
        string path = $"{Encode(mount)}/targets-info?{query}";

        Response? response = await logical.ExecuteShapedAsync(
            "GET", path, null, IdentityWire.PinV2(options),
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        IReadOnlyDictionary<string, JsonElement> data = response?.Data ?? throw KvWire.EnvelopeMismatch(path, "keys");

        IReadOnlyList<string> keys = SysWire.ReadKeys(data);

        // D-M9-30/PAG-005's ordering: the structural keys/records length check runs before any
        // per-record decode, mirroring PkiOperations.ListCertificatesInfoAsync.
        bool hasRecordsArray = data.TryGetValue("records", out JsonElement recordsElement) && recordsElement.ValueKind == JsonValueKind.Array;
        int recordCount = hasRecordsArray ? recordsElement.GetArrayLength() : 0;
        if (recordCount != keys.Count)
        {
            throw KvWire.EnvelopeMismatch(path, "records", response.StatusCode);
        }

        List<Target> records = [];
        if (hasRecordsArray)
        {
            foreach (JsonElement record in recordsElement.EnumerateArray())
            {
                records.Add(record.ValueKind == JsonValueKind.Object
                    ? CertLifecycleWire.ReadTarget(SysWire.AsMap(record))
                    : throw KvWire.EnvelopeMismatch(path, "records[]"));
            }
        }

        string? next = SysWire.ReadString(data, "next");
        return new Page<Target>
        {
            Keys = keys,
            Records = records,
            Total = SysWire.ReadNullableLong(data, "total") is { } total ? (int)total : keys.Count,
            Next = string.IsNullOrEmpty(next) ? null : next,
            Truncated = data.TryGetValue("truncated", out JsonElement truncated) && truncated.ValueKind == JsonValueKind.True,
        };
    }

    /// <summary>D-M9-8: PAG-004's iterator for <see cref="ListTargetsInfoAsync"/>, following <see cref="PkiOperations.ListCertificatesInfoAllAsync"/>'s exact shape.</summary>
    /// <remarks>
    /// HTTP call: none directly — walks <see cref="ListTargetsInfoAsync"/> pages via
    /// <see cref="PagingWire.IteratePagesAsync{T}"/>. Wire params: as <see cref="ListTargetsInfoAsync"/>,
    /// plus <paramref name="maxRecords"/> (client-side cap, no wire effect). Returns each record
    /// keyed by its name, never <see langword="null"/>. Conformance: Complete (PAG-004). Errors
    /// beyond the common set (ERR-061): <c>BV-INPUT-005</c> when the walk would exceed
    /// <paramref name="maxRecords"/>.
    /// </remarks>
    /// <spec>CertLifecycle.ListTargetsInfoAll — PAG-004</spec>
    public IAsyncEnumerable<KeyValuePair<string, Target>> ListTargetsInfoAllAsync(
        string mount = DefaultMount,
        int? limit = null,
        int maxRecords = PagingWire.DefaultMaxRecords,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return PagingWire.IteratePagesAsync(
            (after, token) => ListTargetsInfoAsync(mount, after, limit, options, token),
            maxRecords,
            cancellationToken);
    }

    /// <summary>Reads a renewal target's configuration: <c>GET {mount}/targets/{name}</c>.</summary>
    /// <remarks>
    /// Wire params: <paramref name="name"/>/<paramref name="mount"/> build the route; no body.
    /// A missing target is <see langword="null"/>, never an exception. Conformance: Complete. No
    /// error codes beyond the common set (ERR-061).
    /// </remarks>
    /// <spec>CertLifecycle.ReadTarget — 12-other-engines-and-identity.md</spec>
    public async Task<Target?> ReadTargetAsync(
        string name, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", TargetPath(mount, name), null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data is { } data ? CertLifecycleWire.ReadTarget(data) : null;
    }

    /// <summary>Creates or replaces a renewal target's configuration: <c>POST {mount}/targets/{name}</c>.</summary>
    /// <remarks>
    /// Wire params: <paramref name="name"/>/<paramref name="mount"/> build the route; body carries
    /// <paramref name="target"/>'s fields (<c>kind</c>, <c>address</c>, <c>pki_mount</c>,
    /// <c>role_ref</c>, <c>common_name</c>, <c>alt_names</c>, <c>ip_sans</c>, <c>ttl</c>,
    /// <c>key_policy</c>, <c>key_ref</c>, <c>renew_before</c>). Returns <see langword="void"/> on
    /// success. Conformance: Complete. No error codes beyond the common set (ERR-061).
    /// </remarks>
    /// <spec>CertLifecycle.WriteTarget — 12-other-engines-and-identity.md</spec>
    public async Task WriteTargetAsync(
        string name, Target target, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", TargetPath(mount, name), CertLifecycleWire.SerialiseTarget(target), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>Deletes a renewal target: <c>DELETE {mount}/targets/{name}</c>.</summary>
    /// <remarks>
    /// Wire params: <paramref name="name"/>/<paramref name="mount"/> build the route; no body.
    /// Returns <see langword="void"/> on success. Conformance: Complete. No error codes beyond
    /// the common set (ERR-061).
    /// </remarks>
    /// <spec>CertLifecycle.DeleteTarget — 12-other-engines-and-identity.md</spec>
    public async Task DeleteTargetAsync(
        string name, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "DELETE", TargetPath(mount, name), null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>Reads a renewal target's renewer state: <c>GET {mount}/state/{name}</c>.</summary>
    /// <remarks>
    /// Wire params: <paramref name="name"/>/<paramref name="mount"/> build the route; no body.
    /// Returns <see cref="TargetState"/>, or <see langword="null"/> when the target (or its
    /// state) is absent. Conformance: Complete. No error codes beyond the common set (ERR-061).
    /// </remarks>
    /// <spec>CertLifecycle.State — 12-other-engines-and-identity.md</spec>
    public async Task<TargetState?> StateAsync(
        string name, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{Encode(mount)}/state/{UrlBuilder.EncodePathSegment(name)}", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data is { } data ? CertLifecycleWire.ReadTargetState(data) : null;
    }

    /// <summary>Triggers an out-of-cycle renewal for a target: <c>POST {mount}/renew/{name}</c>.</summary>
    /// <remarks>
    /// An unknown <paramref name="name"/> reaches the caller as <c>BV-NOTFOUND-008</c> through the
    /// shared message-recognition pipeline (already catalogued; no operation-local remap is added
    /// here). Wire params: <paramref name="name"/>/<paramref name="mount"/> build the route; no
    /// body. Returns <see langword="void"/> on success. Conformance: Complete. Errors beyond the
    /// common set (ERR-061): <c>BV-NOTFOUND-008 ResourceNotFound</c> for an unknown target.
    /// </remarks>
    /// <spec>CertLifecycle.Renew — 12-other-engines-and-identity.md</spec>
    public async Task RenewAsync(
        string name, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{Encode(mount)}/renew/{UrlBuilder.EncodePathSegment(name)}", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>Reads the renewal scheduler's configuration: <c>GET {mount}/scheduler/config</c>.</summary>
    /// <remarks>
    /// Wire params: <paramref name="mount"/> builds the route; no body. Returns
    /// <see cref="SchedulerConfig"/> (<c>client_token_set</c> replaces the write-only
    /// <c>client_token</c> on read), or <see langword="null"/> when unset. Conformance:
    /// Complete. No error codes beyond the common set (ERR-061).
    /// </remarks>
    /// <spec>CertLifecycle.ReadSchedulerConfig — 12-other-engines-and-identity.md</spec>
    public async Task<SchedulerConfig?> ReadSchedulerConfigAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{Encode(mount)}/scheduler/config", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data is { } data ? CertLifecycleWire.ReadSchedulerConfig(data) : null;
    }

    /// <summary>Writes the renewal scheduler's configuration: <c>POST {mount}/scheduler/config</c>.</summary>
    /// <remarks>
    /// Wire params: <paramref name="mount"/> builds the route; body carries <paramref name="config"/>'s
    /// <c>enabled</c>, <c>tick_interval_seconds</c> (server-enforced &#8805; 30),
    /// <c>client_token</c> (write-only), <c>base_backoff_seconds</c>, <c>max_backoff_seconds</c>.
    /// Returns <see langword="void"/> on success. Conformance: Complete. No error codes beyond
    /// the common set (ERR-061).
    /// </remarks>
    /// <spec>CertLifecycle.WriteSchedulerConfig — 12-other-engines-and-identity.md</spec>
    public async Task WriteSchedulerConfigAsync(
        SchedulerConfig config, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{Encode(mount)}/scheduler/config", CertLifecycleWire.SerialiseSchedulerConfig(config), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>GET {mount}/sys/deliverers</c>. 12 names no response shape, so the untyped map fallback applies (D-M1c-25).</summary>
    /// <remarks>
    /// Wire params: <paramref name="mount"/> builds the route; no body. Returns the server's
    /// response map verbatim, or <see langword="null"/> per the shared envelope rules — no typed
    /// contract is invented (D-M1c-25). Conformance: Complete. No error codes beyond the common
    /// set (ERR-061).
    /// </remarks>
    /// <spec>CertLifecycle.Deliverers — 12-other-engines-and-identity.md</spec>
    public async Task<IReadOnlyDictionary<string, JsonElement>?> DeliverersAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{Encode(mount)}/sys/deliverers", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }

    private static string TargetPath(string mount, string name)
    {
        return $"{Encode(mount)}/targets/{UrlBuilder.EncodePathSegment(name)}";
    }

    private static string Encode(string mount)
    {
        return UrlBuilder.EncodePathFragment(mount.Trim('/'));
    }
}
