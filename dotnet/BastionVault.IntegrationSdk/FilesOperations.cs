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

    /// <summary>Lists file ids: <c>LIST {mount}/files/</c>.</summary>
    /// <remarks>Wire params: none beyond <paramref name="mount"/>. Returns the file ids, empty (never <see langword="null"/>) when none exist or the path is absent. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Files.List — 12-other-engines-and-identity.md</spec>
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
    /// <remarks>Wire params: name, resource?, mime_type?, tags[]?, notes?, content_base64 from <see cref="FileCreateRequest.Content"/> (<see cref="FileCreateRequest"/>). Returns the new file's id, never <see langword="null"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): <c>BV-PROTOCOL-002</c> when the response carries no usable id.</remarks>
    /// <spec>Files.Create — FIL-001</spec>
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

    /// <summary>Reads a file record: <c>GET {mount}/files/{id}</c>. No field set is named beyond <c>Files.Create</c>'s own request fields.</summary>
    /// <remarks>Wire params: none beyond <paramref name="id"/>/<paramref name="mount"/>. Returns the raw <see cref="Response"/>, or <see langword="null"/> when <paramref name="id"/> is not found (404 treated as absent). Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Files.Read — 12-other-engines-and-identity.md</spec>
    public Task<Response?> ReadAsync(
        string id, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "GET", FileWire.FilePath(mount, id), null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>Updates a file record: <c>PUT {mount}/files/{id}</c>, a patch-shaped update.</summary>
    /// <remarks>Wire params (patch-shaped, omitted members untouched): name, resource, mime_type, tags[], notes (<see cref="FileUpdateRequest"/>); wire field names are never renamed on the wire (OVR-007), only the language-facing accessor is. Returns the raw <see cref="Response"/>, which may be <see langword="null"/> for an empty body. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Files.Update — 12-other-engines-and-identity.md</spec>
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

    /// <summary>Deletes a file: <c>DELETE {mount}/files/{id}</c>.</summary>
    /// <remarks>Wire params: none beyond <paramref name="id"/>/<paramref name="mount"/>. Returns <see langword="void"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Files.Delete — 12-other-engines-and-identity.md</spec>
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
    /// <remarks>Wire params: none beyond <paramref name="id"/>/<paramref name="mount"/>. Returns the decoded file content as bytes, never <see langword="null"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): <c>BV-PROTOCOL-002</c> when <c>content_base64</c> is missing or not valid base64.</remarks>
    /// <spec>Files.Content — FIL-001</spec>
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

    /// <summary>Reads a file's change history: <c>GET {mount}/files/{id}/history</c>. No shape beyond the array itself.</summary>
    /// <remarks>Wire params: none beyond <paramref name="id"/>/<paramref name="mount"/>. Returns an empty list when there is no history, never <see langword="null"/>; each entry is a raw <see cref="JsonElement"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Files.History — 12-other-engines-and-identity.md</spec>
    public async Task<IReadOnlyList<JsonElement>> HistoryAsync(
        string id, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{FileWire.FilePath(mount, id)}/history", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return IdentityKernelWire.ReadArrayEnvelope(response, nestedKey: "entries");
    }

    /// <summary>Lists a file's version records: <c>GET {mount}/files/{id}/versions</c>. No shape beyond the array itself.</summary>
    /// <remarks>Wire params: none beyond <paramref name="id"/>/<paramref name="mount"/>. Returns an empty list when there are no versions, never <see langword="null"/>; each entry is a raw <see cref="JsonElement"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Files.Versions — 12-other-engines-and-identity.md</spec>
    public async Task<IReadOnlyList<JsonElement>> VersionsAsync(
        string id, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{FileWire.FilePath(mount, id)}/versions", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return IdentityKernelWire.ReadArrayEnvelope(response, nestedKey: "versions");
    }

    /// <summary>Reads one version record: <c>GET {mount}/files/{id}/versions/{n}</c>. No field set is named for a version record.</summary>
    /// <remarks>Wire params: <paramref name="version"/> builds the route, no body. Returns the raw <see cref="Response"/>, or <see langword="null"/> when the version is not found (404 treated as absent). Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Files.ReadVersion — 12-other-engines-and-identity.md</spec>
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
    /// <remarks>Wire params: <paramref name="version"/> builds the route, no body. Returns the decoded content of that version as bytes, never <see langword="null"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): <c>BV-PROTOCOL-002</c> when <c>content_base64</c> is missing or not valid base64.</remarks>
    /// <spec>Files.VersionContent — FIL-001</spec>
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

    /// <summary>Restores a version as the current content: <c>POST {mount}/files/{id}/versions/{n}/restore</c>.</summary>
    /// <remarks>Wire params: <paramref name="version"/> builds the route, no body. Returns <see langword="void"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Files.RestoreVersion — 12-other-engines-and-identity.md</spec>
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
    /// Repoints a file to a different resource: <c>POST {mount}/files/repoint-resource</c>. No wire
    /// field name is given (Level X); <c>old_resource</c>/<c>new_resource</c> follow the
    /// operation's own name.
    /// </summary>
    /// <remarks>Wire params: old_resource from <paramref name="oldResource"/>, new_resource from <paramref name="newResource"/>. Returns <see langword="void"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Files.RepointResource — 12-other-engines-and-identity.md</spec>
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

    /// <summary>Lists a file's sync target names: <c>LIST {mount}/files/{id}/sync/</c>.</summary>
    /// <remarks>Wire params: none beyond <paramref name="id"/>/<paramref name="mount"/>. Returns the sync target names, empty (never <see langword="null"/>) when none exist or the path is absent. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Files.Sync.List — 12-other-engines-and-identity.md</spec>
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

    /// <summary>Creates or replaces a sync target: <c>PUT {mount}/files/{id}/sync/{name}</c>. See <see cref="SyncTarget"/>'s remarks for the credential-field gap.</summary>
    /// <remarks>Wire params: kind from <see cref="SyncTarget.Kind"/> plus whatever <see cref="SyncTarget.Fields"/> carries verbatim (<see cref="SyncTarget"/>). R-36 (open risk): credential fields for the sync target ship through <see cref="SyncTarget.Fields"/> as an opaque JSON bag — section 12 names no wire field for them, so this SDK documents no field name here and never places an example credential value in this comment. Returns the raw <see cref="Response"/>, which may be <see langword="null"/> for an empty body. Conformance: Level X (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): <c>BV-INPUT-001</c> when <see cref="SyncTarget.Fields"/> is not a JSON object or contains a <c>kind</c> key.</remarks>
    /// <spec>Files.Sync.Write — 12-other-engines-and-identity.md</spec>
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

    /// <summary>Removes a sync target: <c>DELETE {mount}/files/{id}/sync/{name}</c>.</summary>
    /// <remarks>Wire params: none beyond <paramref name="id"/>/<paramref name="name"/>/<paramref name="mount"/>. Returns <see langword="void"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Files.Sync.Delete — 12-other-engines-and-identity.md</spec>
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

    /// <summary>Pushes a file to one sync target: <c>POST {mount}/files/{id}/sync/{name}/push</c>.</summary>
    /// <remarks>Wire params: none beyond <paramref name="id"/>/<paramref name="name"/>/<paramref name="mount"/>. Returns <see langword="void"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Files.Sync.Push — 12-other-engines-and-identity.md</spec>
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

    /// <summary>Ticks the sync scheduler: <c>POST {mount}/sync-tick</c>. Mount-wide, not scoped to one file.</summary>
    /// <remarks>Wire params: none beyond <paramref name="mount"/>. Returns <see langword="void"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Files.Sync.Tick — 12-other-engines-and-identity.md</spec>
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
