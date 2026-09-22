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

    /// <summary>12: <c>LIST resource-group/groups</c>.</summary>
    public async Task<IReadOnlyList<string>> ListAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "LIST", Root, null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken).ConfigureAwait(false);
        return SysWire.ReadKeys(response?.Data);
    }

    /// <summary>12: <c>GET resource-group/groups/{name}</c>.</summary>
    public Task<Response?> ReadAsync(string name, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return logical.ExecuteShapedAsync(
            "GET", GroupPath(name), null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>12: <c>PUT resource-group/groups/{name}</c>.</summary>
    public Task<Response?> WriteAsync(string name, JsonElement spec, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return logical.ExecuteShapedAsync(
            "PUT", GroupPath(name), SysWire.RequireJsonBody(spec, "spec"), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>12: <c>DELETE resource-group/groups/{name}</c>.</summary>
    public async Task DeleteAsync(string name, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        _ = await logical.ExecuteShapedAsync(
            "DELETE", GroupPath(name), null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>12: <c>GET resource-group/groups/{name}/history</c>. No documented shape beyond the array itself.</summary>
    public async Task<IReadOnlyList<JsonElement>> HistoryAsync(string name, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{GroupPath(name)}/history", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return IdentityKernelWire.ReadArrayEnvelope(response);
    }

    /// <summary>12: <c>GET resource-group/by-resource/{name}</c>.</summary>
    public Task<Response?> ByResourceAsync(string name, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return logical.ExecuteShapedAsync(
            "GET", $"resource-group/by-resource/{IdentityKernelWire.EncodeSegment(name, "name")}", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>
    /// 12: <c>GET resource-group/by-secret/{b64url path}</c>. The SDK base64url-encodes
    /// <paramref name="path"/> itself, the same IDN-001 treatment <see cref="IdentitySharingOperations"/> gives its <c>target</c>.
    /// </summary>
    public Task<Response?> BySecretAsync(string path, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return logical.ExecuteShapedAsync(
            "GET", $"resource-group/by-secret/{Base64Url.Encode(path)}", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>12: <c>PUT resource-group/reindex</c>.</summary>
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
