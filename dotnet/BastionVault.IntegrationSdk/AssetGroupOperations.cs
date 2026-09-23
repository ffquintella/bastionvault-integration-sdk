using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>12 — Asset groups (<c>resource-group/</c> mount), reached from <see cref="BastionVaultClient.AssetGroups"/>.</summary>
/// <remarks>
/// No <c>Page&lt;T&gt;</c> (neither list route gives a cursor) and no accompanying type file:
/// section 12 names no field set for a group record, so bodies stay raw <see cref="JsonElement"/>/<see cref="Response"/> (D-M1c-25).
/// </remarks>
public sealed class AssetGroupOperations
{
    private const string Root = "resource-group/groups";

    private readonly LogicalOperations logical;

    internal AssetGroupOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
    }

    /// <summary>Lists asset group names: <c>LIST resource-group/groups</c>.</summary>
    /// <remarks>Wire params: none. Returns an empty list when there are none, never <see langword="null"/>. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>AssetGroups.List — 12-other-engines-and-identity.md</spec>
    public async Task<IReadOnlyList<string>> ListAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "LIST", Root, null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken).ConfigureAwait(false);
        return SysWire.ReadKeys(response?.Data);
    }

    /// <summary>Reads an asset group by name: <c>GET resource-group/groups/{name}</c>.</summary>
    /// <remarks>Wire params: <c>name</c> builds the route. Returns the raw <see cref="Response"/>, or <see langword="null"/> when the group does not exist. Conformance: Complete. Errors beyond the common set (ERR-061): <c>BV-INPUT-001</c> for a <c>..</c> segment in <paramref name="name"/>.</remarks>
    /// <spec>AssetGroups.Read — 12-other-engines-and-identity.md</spec>
    public Task<Response?> ReadAsync(string name, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return logical.ExecuteShapedAsync(
            "GET", GroupPath(name), null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>Creates or replaces an asset group: <c>PUT resource-group/groups/{name}</c>.</summary>
    /// <remarks>Wire params: <c>name</c> builds the route; body is the caller-supplied <paramref name="spec"/> verbatim. Returns the raw <see cref="Response"/>, which may be <see langword="null"/> for an empty body. Conformance: Complete. Errors beyond the common set (ERR-061): <c>BV-INPUT-001</c> for a <c>..</c> segment in <paramref name="name"/> or an undefined <paramref name="spec"/>.</remarks>
    /// <spec>AssetGroups.Write — 12-other-engines-and-identity.md</spec>
    public Task<Response?> WriteAsync(string name, JsonElement spec, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return logical.ExecuteShapedAsync(
            "PUT", GroupPath(name), SysWire.RequireJsonBody(spec, "spec"), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>Deletes an asset group: <c>DELETE resource-group/groups/{name}</c>.</summary>
    /// <remarks>Wire params: <c>name</c> builds the route; no body. Returns no value; deleting an absent group is not an error. Conformance: Complete. Errors beyond the common set (ERR-061): <c>BV-INPUT-001</c> for a <c>..</c> segment in <paramref name="name"/>.</remarks>
    /// <spec>AssetGroups.Delete — 12-other-engines-and-identity.md</spec>
    public async Task DeleteAsync(string name, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        _ = await logical.ExecuteShapedAsync(
            "DELETE", GroupPath(name), null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>Reads a group's change history: <c>GET resource-group/groups/{name}/history</c>. No documented shape beyond the array itself.</summary>
    /// <remarks>Wire params: <c>name</c> builds the route. Returns an empty list when there is no history, never <see langword="null"/>; each entry is a raw <see cref="JsonElement"/> (D-M1c-25). Conformance: Complete. Errors beyond the common set (ERR-061): <c>BV-INPUT-001</c> for a <c>..</c> segment in <paramref name="name"/>.</remarks>
    /// <spec>AssetGroups.History — 12-other-engines-and-identity.md</spec>
    public async Task<IReadOnlyList<JsonElement>> HistoryAsync(string name, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{GroupPath(name)}/history", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return IdentityKernelWire.ReadArrayEnvelope(response, nestedKey: "entries");
    }

    /// <summary>Finds the asset group(s) that contain a resource by name: <c>GET resource-group/by-resource/{name}</c>.</summary>
    /// <remarks>Wire params: <c>name</c> builds the route. Returns the raw <see cref="Response"/>, or <see langword="null"/> when no group contains the resource. Conformance: Complete. Errors beyond the common set (ERR-061): <c>BV-INPUT-001</c> for an empty <paramref name="name"/> or a <c>..</c> segment in it.</remarks>
    /// <spec>AssetGroups.ByResource — 12-other-engines-and-identity.md</spec>
    public Task<Response?> ByResourceAsync(string name, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return logical.ExecuteShapedAsync(
            "GET", $"resource-group/by-resource/{IdentityKernelWire.EncodeSegment(name, "name")}", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>Finds the asset group(s) that contain a secret by path: <c>GET resource-group/by-secret/{b64url path}</c>. The SDK base64url-encodes <paramref name="path"/> itself, an analogous but not IDN-001-governed treatment (that requirement is <c>identity/sharing/*</c>'s own).</summary>
    /// <remarks>Wire params: <paramref name="path"/> is base64url-encoded client-side to build the route. Returns the raw <see cref="Response"/>, or <see langword="null"/> when no group contains the secret. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>AssetGroups.BySecret — 12-other-engines-and-identity.md</spec>
    public Task<Response?> BySecretAsync(string path, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return logical.ExecuteShapedAsync(
            "GET", $"resource-group/by-secret/{Base64Url.Encode(path)}", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>Triggers a full reindex of the asset-group engine: <c>PUT resource-group/reindex</c>.</summary>
    /// <remarks>Wire params: none. Returns no value. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>AssetGroups.Reindex — 12-other-engines-and-identity.md</spec>
    public async Task ReindexAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        _ = await logical.ExecuteShapedAsync(
            "PUT", "resource-group/reindex", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
    }

    private static string GroupPath(string name)
    {
        return $"{Root}/{IdentityKernelWire.EncodeSegment(name, "name")}";
    }
}
