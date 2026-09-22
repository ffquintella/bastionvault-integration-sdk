using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// 12 §Files: the <c>files</c> engine, reached from <see cref="BastionVaultClient.Files"/>.
/// <c>mount</c> defaults to <c>"files"</c>. FIL-001: every content parameter is <c>byte[]</c>; the
/// SDK does the base64 encoding/decoding, and the 32 MiB request-body limit
/// (<see cref="Internal.RequestExecutor"/>'s TRN-032 check) runs on the encoded body.
/// </summary>
public sealed class FilesOperations
{
    private const string DefaultMount = "files";

    private readonly LogicalOperations logical;

    internal FilesOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
        Sync = new FilesSyncOperations(context, activeNamespace);
    }

    /// <summary>12 §Files: <c>{mount}/files/{id}/sync[/{name}[/push]]</c>, <c>POST {mount}/sync-tick</c>.</summary>
    public FilesSyncOperations Sync { get; }

    /// <summary><c>LIST {mount}/files/</c>.</summary>
    public async Task<IReadOnlyList<string>> ListAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "LIST", $"{FileWire.Encode(mount)}/files/", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return SysWire.ReadKeys(response?.Data);
    }

    /// <summary>FIL-001: <c>POST {mount}/files/</c>. <see cref="FileCreateRequest.Content"/> is base64-encoded here, into <c>content_base64</c>.</summary>
    public async Task<string> CreateAsync(
        FileCreateRequest request, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(request.Name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{FileWire.Encode(mount)}/files/";
        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, FileWire.SerialiseCreate(request), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        IReadOnlyDictionary<string, JsonElement> data = response?.Data ?? throw KvWire.EnvelopeMismatch(path, "id");
        return SysWire.ReadString(data, "id") ?? throw KvWire.EnvelopeMismatch(path, "id");
    }

    /// <summary>12 §Files: <c>GET {mount}/files/{id}</c>. No field set is named beyond <c>Files.Create</c>'s own request fields.</summary>
    public Task<Response?> ReadAsync(
        string id, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "GET", FileWire.FilePath(mount, id), null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>12 §Files: <c>PUT {mount}/files/{id}</c>, a patch-shaped update (OVR-007).</summary>
    public Task<Response?> UpdateAsync(
        string id, FileUpdateRequest spec, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "PUT", FileWire.FilePath(mount, id), FileWire.SerialiseUpdate(spec), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>12 §Files: <c>DELETE {mount}/files/{id}</c>.</summary>
    public async Task DeleteAsync(
        string id, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "DELETE", FileWire.FilePath(mount, id), null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>FIL-001: <c>GET {mount}/files/{id}/content</c> → <c>bytes</c>. The SDK decodes <c>content_base64</c> itself.</summary>
    public async Task<byte[]> ContentAsync(
        string id, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{FileWire.FilePath(mount, id)}/content";
        Response? response = await logical.ExecuteShapedAsync(
            "GET", path, null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return FileWire.ReadContent(response?.Data, path);
    }

    /// <summary>12 §Files: <c>GET {mount}/files/{id}/history</c>. No shape beyond the array itself.</summary>
    public async Task<IReadOnlyList<JsonElement>> HistoryAsync(
        string id, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{FileWire.FilePath(mount, id)}/history", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return IdentityKernelWire.ReadArrayEnvelope(response);
    }

    /// <summary>12 §Files: <c>GET {mount}/files/{id}/versions</c>. No shape beyond the array itself.</summary>
    public async Task<IReadOnlyList<JsonElement>> VersionsAsync(
        string id, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{FileWire.FilePath(mount, id)}/versions", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return IdentityKernelWire.ReadArrayEnvelope(response);
    }

    /// <summary>12 §Files: <c>GET {mount}/files/{id}/versions/{n}</c>. No field set is named for a version record.</summary>
    public Task<Response?> ReadVersionAsync(
        string id, int version, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "GET", VersionPath(mount, id, version), null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>FIL-001: <c>GET {mount}/files/{id}/versions/{n}/content</c> → <c>bytes</c>.</summary>
    public async Task<byte[]> VersionContentAsync(
        string id, int version, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{VersionPath(mount, id, version)}/content";
        Response? response = await logical.ExecuteShapedAsync(
            "GET", path, null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return FileWire.ReadContent(response?.Data, path);
    }

    /// <summary>12 §Files: <c>POST {mount}/files/{id}/versions/{n}/restore</c>.</summary>
    public async Task RestoreVersionAsync(
        string id, int version, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{VersionPath(mount, id, version)}/restore", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>
    /// 12 §Files: <c>POST {mount}/files/repoint-resource</c>. No wire field name is given
    /// (Level X); <c>old_resource</c>/<c>new_resource</c> follow the operation's own name.
    /// </summary>
    public async Task RepointResourceAsync(
        string oldResource, string newResource, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(oldResource);
        ArgumentException.ThrowIfNullOrEmpty(newResource);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{FileWire.Encode(mount)}/files/repoint-resource", FileWire.SerialiseRepointResource(oldResource, newResource), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    private static string VersionPath(string mount, string id, int version)
    {
        return $"{FileWire.FilePath(mount, id)}/versions/{version.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }
}

/// <summary>12 §Files: <c>{mount}/files/{id}/sync[/{name}[/push]]</c>, <c>POST {mount}/sync-tick</c>.</summary>
public sealed class FilesSyncOperations
{
    private const string DefaultMount = "files";

    private readonly LogicalOperations logical;

    internal FilesSyncOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
    }

    /// <summary><c>LIST {mount}/files/{id}/sync/</c>.</summary>
    public async Task<IReadOnlyList<string>> ListAsync(
        string id, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "LIST", $"{FileWire.FilePath(mount, id)}/sync/", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return SysWire.ReadKeys(response?.Data);
    }

    /// <summary><c>PUT {mount}/files/{id}/sync/{name}</c>. See <see cref="SyncTarget"/>'s remarks for the credential-field gap.</summary>
    public Task<Response?> WriteAsync(
        string id, string name, SyncTarget target, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = SyncPath(mount, id, name);
        return logical.ExecuteShapedAsync(
            "PUT", path, FileWire.SerialiseSyncTarget(target, path), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true);
    }

    /// <summary><c>DELETE {mount}/files/{id}/sync/{name}</c>.</summary>
    public async Task DeleteAsync(
        string id, string name, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "DELETE", SyncPath(mount, id, name), null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>POST {mount}/files/{id}/sync/{name}/push</c>.</summary>
    public async Task PushAsync(
        string id, string name, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{SyncPath(mount, id, name)}/push", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>POST {mount}/sync-tick</c>. Mount-wide, not scoped to one file.</summary>
    public async Task TickAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{FileWire.Encode(mount)}/sync-tick", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    private static string SyncPath(string mount, string id, string name)
    {
        return $"{FileWire.FilePath(mount, id)}/sync/{UrlBuilder.EncodePathSegment(name)}";
    }
}
