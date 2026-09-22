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

    /// <summary><c>LIST {mount}/targets/</c>.</summary>
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

    /// <summary><c>GET {mount}/targets/{name}</c>.</summary>
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

    /// <summary><c>POST {mount}/targets/{name}</c>.</summary>
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

    /// <summary><c>DELETE {mount}/targets/{name}</c>.</summary>
    public async Task DeleteTargetAsync(
        string name, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "DELETE", TargetPath(mount, name), null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>GET {mount}/state/{name}</c>.</summary>
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

    /// <summary>
    /// <c>POST {mount}/renew/{name}</c>. An unknown <paramref name="name"/> reaches the caller as
    /// <c>BV-NOTFOUND-008</c> through the shared message-recognition pipeline (already catalogued;
    /// no operation-local remap is added here).
    /// </summary>
    public async Task RenewAsync(
        string name, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{Encode(mount)}/renew/{UrlBuilder.EncodePathSegment(name)}", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>GET {mount}/scheduler/config</c>.</summary>
    public async Task<SchedulerConfig?> ReadSchedulerConfigAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{Encode(mount)}/scheduler/config", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data is { } data ? CertLifecycleWire.ReadSchedulerConfig(data) : null;
    }

    /// <summary><c>POST {mount}/scheduler/config</c>.</summary>
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
