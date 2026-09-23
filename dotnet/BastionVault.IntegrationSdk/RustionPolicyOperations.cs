using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// <c>{mount}/policy/{global,type/{t},asset-group/{id},resource/{id},force-rustion,effective}</c>
/// (Appendix A). No field-level schema is documented; every operation carries the raw idiom.
/// </summary>
public sealed class RustionPolicyOperations
{
    private const string DefaultMount = "rustion";

    private readonly LogicalOperations logical;

    internal RustionPolicyOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
    }

    /// <summary>Reads the global policy: <c>GET {mount}/policy/global</c>.</summary>
    /// <remarks>Wire params: <paramref name="mount"/> builds the route; no body. Returns the raw response map verbatim, or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Policy.ReadGlobal — 12-other-engines-and-identity.md</spec>
    public async Task<IReadOnlyDictionary<string, JsonElement>?> ReadGlobalAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{RustionWire.Encode(mount)}/policy/global", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }

    /// <summary>Writes the global policy: <c>PUT {mount}/policy/global</c>. <paramref name="policy"/> is sent verbatim (D-M1c-25).</summary>
    /// <remarks>Wire params: <paramref name="mount"/> builds the route; body carries <paramref name="policy"/> verbatim. Returns the raw <see cref="Response"/> (no field-level schema documented), or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Policy.WriteGlobal — 12-other-engines-and-identity.md</spec>
    public Task<Response?> WriteGlobalAsync(
        JsonElement policy, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "PUT", $"{RustionWire.Encode(mount)}/policy/global", SysWire.RequireJsonBody(policy, "policy"), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>Reads a target-type policy: <c>GET {mount}/policy/type/{type}</c>.</summary>
    /// <remarks>Wire params: <paramref name="type"/>/<paramref name="mount"/> build the route; no body. Returns the raw <see cref="Response"/> (no field-level schema documented), or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Policy.ReadType — 12-other-engines-and-identity.md</spec>
    public Task<Response?> ReadTypeAsync(
        string type, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "GET", TypePath(mount, type), null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>Writes a target-type policy: <c>PUT {mount}/policy/type/{type}</c>. <paramref name="policy"/> is sent verbatim (D-M1c-25).</summary>
    /// <remarks>Wire params: <paramref name="type"/>/<paramref name="mount"/> build the route; body carries <paramref name="policy"/> verbatim. Returns the raw <see cref="Response"/> (no field-level schema documented), or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Policy.WriteType — 12-other-engines-and-identity.md</spec>
    public Task<Response?> WriteTypeAsync(
        string type, JsonElement policy, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "PUT", TypePath(mount, type), SysWire.RequireJsonBody(policy, "policy"), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>Deletes a target-type policy: <c>DELETE {mount}/policy/type/{type}</c>.</summary>
    /// <remarks>Wire params: <paramref name="type"/>/<paramref name="mount"/> build the route; no body. Returns <see langword="void"/> on success. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Policy.DeleteType — 12-other-engines-and-identity.md</spec>
    public async Task DeleteTypeAsync(
        string type, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "DELETE", TypePath(mount, type), null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>Reads an asset-group policy: <c>GET {mount}/policy/asset-group/{id}</c>.</summary>
    /// <remarks>Wire params: <paramref name="id"/>/<paramref name="mount"/> build the route; no body. Returns the raw <see cref="Response"/> (no field-level schema documented), or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Policy.ReadAssetGroup — 12-other-engines-and-identity.md</spec>
    public Task<Response?> ReadAssetGroupAsync(
        string id, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "GET", AssetGroupPath(mount, id), null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>Writes an asset-group policy: <c>PUT {mount}/policy/asset-group/{id}</c>. <paramref name="policy"/> is sent verbatim (D-M1c-25).</summary>
    /// <remarks>Wire params: <paramref name="id"/>/<paramref name="mount"/> build the route; body carries <paramref name="policy"/> verbatim. Returns the raw <see cref="Response"/> (no field-level schema documented), or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Policy.WriteAssetGroup — 12-other-engines-and-identity.md</spec>
    public Task<Response?> WriteAssetGroupAsync(
        string id, JsonElement policy, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "PUT", AssetGroupPath(mount, id), SysWire.RequireJsonBody(policy, "policy"), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>Deletes an asset-group policy: <c>DELETE {mount}/policy/asset-group/{id}</c>.</summary>
    /// <remarks>Wire params: <paramref name="id"/>/<paramref name="mount"/> build the route; no body. Returns <see langword="void"/> on success. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Policy.DeleteAssetGroup — 12-other-engines-and-identity.md</spec>
    public async Task DeleteAssetGroupAsync(
        string id, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "DELETE", AssetGroupPath(mount, id), null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>Reads a resource policy: <c>GET {mount}/policy/resource/{id}</c>.</summary>
    /// <remarks>Wire params: <paramref name="id"/>/<paramref name="mount"/> build the route; no body. Returns the raw <see cref="Response"/> (no field-level schema documented), or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Policy.ReadResource — 12-other-engines-and-identity.md</spec>
    public Task<Response?> ReadResourceAsync(
        string id, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "GET", ResourcePath(mount, id), null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>Writes a resource policy: <c>PUT {mount}/policy/resource/{id}</c>. <paramref name="policy"/> is sent verbatim (D-M1c-25).</summary>
    /// <remarks>Wire params: <paramref name="id"/>/<paramref name="mount"/> build the route; body carries <paramref name="policy"/> verbatim. Returns the raw <see cref="Response"/> (no field-level schema documented), or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Policy.WriteResource — 12-other-engines-and-identity.md</spec>
    public Task<Response?> WriteResourceAsync(
        string id, JsonElement policy, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "PUT", ResourcePath(mount, id), SysWire.RequireJsonBody(policy, "policy"), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>Deletes a resource policy: <c>DELETE {mount}/policy/resource/{id}</c>.</summary>
    /// <remarks>Wire params: <paramref name="id"/>/<paramref name="mount"/> build the route; no body. Returns <see langword="void"/> on success. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Policy.DeleteResource — 12-other-engines-and-identity.md</spec>
    public async Task DeleteResourceAsync(
        string id, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "DELETE", ResourcePath(mount, id), null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>Forces a policy re-evaluation: <c>POST {mount}/policy/force-rustion</c>. <paramref name="request"/> is sent verbatim (D-M1c-25).</summary>
    /// <remarks>Wire params: <paramref name="mount"/> builds the route; body carries <paramref name="request"/> verbatim. Returns the raw <see cref="Response"/> (no field-level schema documented), or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Policy.ForceRustion — 12-other-engines-and-identity.md</spec>
    public Task<Response?> ForceRustionAsync(
        JsonElement request, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "POST", $"{RustionWire.Encode(mount)}/policy/force-rustion", SysWire.RequireJsonBody(request, "request"), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>Evaluates effective policy: <c>POST {mount}/policy/effective</c>. <paramref name="request"/> is sent verbatim (D-M1c-25).</summary>
    /// <remarks>Wire params: <paramref name="mount"/> builds the route; body carries <paramref name="request"/> verbatim. Returns the raw <see cref="Response"/> (no field-level schema documented), or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Policy.Effective — 12-other-engines-and-identity.md</spec>
    public Task<Response?> EffectiveAsync(
        JsonElement request, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "POST", $"{RustionWire.Encode(mount)}/policy/effective", SysWire.RequireJsonBody(request, "request"), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true);
    }

    private static string TypePath(string mount, string type)
    {
        return $"{RustionWire.Encode(mount)}/policy/type/{UrlBuilder.EncodePathSegment(type)}";
    }

    private static string AssetGroupPath(string mount, string id)
    {
        return $"{RustionWire.Encode(mount)}/policy/asset-group/{UrlBuilder.EncodePathSegment(id)}";
    }

    private static string ResourcePath(string mount, string id)
    {
        return $"{RustionWire.Encode(mount)}/policy/resource/{UrlBuilder.EncodePathSegment(id)}";
    }
}

/// <summary><c>{mount}/bastion-groups[/{name}]</c> (Appendix A). No field-level schema is documented.</summary>
public sealed class RustionBastionGroupsOperations
{
    private const string DefaultMount = "rustion";

    private readonly LogicalOperations logical;

    internal RustionBastionGroupsOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
    }

    /// <summary>Lists bastion group names: <c>LIST {mount}/bastion-groups/</c>.</summary>
    /// <remarks>Wire params: <paramref name="mount"/> builds the route; no body. Returns an empty list when the backend has none, never <see langword="null"/>. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.BastionGroups.List — 12-other-engines-and-identity.md</spec>
    public async Task<IReadOnlyList<string>> ListAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "LIST", $"{RustionWire.Encode(mount)}/bastion-groups/", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return KvWire.ReadKeys(response);
    }

    /// <summary>Reads a bastion group: <c>GET {mount}/bastion-groups/{name}</c>.</summary>
    /// <remarks>Wire params: <paramref name="name"/>/<paramref name="mount"/> build the route; no body. Returns the raw <see cref="Response"/> (no field-level schema documented), or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.BastionGroups.Read — 12-other-engines-and-identity.md</spec>
    public Task<Response?> ReadAsync(
        string name, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "GET", GroupPath(mount, name), null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>Writes a bastion group: <c>PUT {mount}/bastion-groups/{name}</c>. <paramref name="group"/> is sent verbatim (D-M1c-25).</summary>
    /// <remarks>Wire params: <paramref name="name"/>/<paramref name="mount"/> build the route; body carries <paramref name="group"/> verbatim. Returns the raw <see cref="Response"/> (no field-level schema documented), or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.BastionGroups.Write — 12-other-engines-and-identity.md</spec>
    public Task<Response?> WriteAsync(
        string name, JsonElement group, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "PUT", GroupPath(mount, name), SysWire.RequireJsonBody(group, "group"), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>Deletes a bastion group: <c>DELETE {mount}/bastion-groups/{name}</c>.</summary>
    /// <remarks>Wire params: <paramref name="name"/>/<paramref name="mount"/> build the route; no body. Returns <see langword="void"/> on success. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.BastionGroups.Delete — 12-other-engines-and-identity.md</spec>
    public async Task DeleteAsync(
        string name, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "DELETE", GroupPath(mount, name), null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    private static string GroupPath(string mount, string name)
    {
        return $"{RustionWire.Encode(mount)}/bastion-groups/{UrlBuilder.EncodePathSegment(name)}";
    }
}

/// <summary><c>{mount}/dispatcher/preview</c> (Appendix A). No field-level schema is documented.</summary>
public sealed class RustionDispatcherOperations
{
    private const string DefaultMount = "rustion";

    private readonly LogicalOperations logical;

    internal RustionDispatcherOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
    }

    /// <summary>Previews dispatcher routing: <c>POST {mount}/dispatcher/preview</c>. <paramref name="request"/> is sent verbatim (D-M1c-25).</summary>
    /// <remarks>Wire params: <paramref name="mount"/> builds the route; body carries <paramref name="request"/> verbatim. Returns the raw <see cref="Response"/> (no field-level schema documented), or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Dispatcher.Preview — 12-other-engines-and-identity.md</spec>
    public Task<Response?> PreviewAsync(
        JsonElement request, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "POST", $"{RustionWire.Encode(mount)}/dispatcher/preview", SysWire.RequireJsonBody(request, "request"), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true);
    }
}

/// <summary><c>{mount}/telemetry[/poll]</c> (Appendix A). No field-level schema is documented.</summary>
public sealed class RustionTelemetryOperations
{
    private const string DefaultMount = "rustion";

    private readonly LogicalOperations logical;

    internal RustionTelemetryOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
    }

    /// <summary>Reads current telemetry: <c>GET {mount}/telemetry</c>.</summary>
    /// <remarks>Wire params: <paramref name="mount"/> builds the route; no body. Returns the raw response map verbatim, or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Telemetry.Read — 12-other-engines-and-identity.md</spec>
    public async Task<IReadOnlyDictionary<string, JsonElement>?> ReadAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{RustionWire.Encode(mount)}/telemetry", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }

    /// <summary>Polls telemetry: <c>GET {mount}/telemetry/poll</c>. Read-only inference (D-M10-e): "poll" is modelled as a side-effect-free fetch, not an acknowledging drain.</summary>
    /// <remarks>Wire params: <paramref name="mount"/> builds the route; no body. Returns the raw response map verbatim, or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Telemetry.Poll — 12-other-engines-and-identity.md</spec>
    public async Task<IReadOnlyDictionary<string, JsonElement>?> PollAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{RustionWire.Encode(mount)}/telemetry/poll", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }
}
