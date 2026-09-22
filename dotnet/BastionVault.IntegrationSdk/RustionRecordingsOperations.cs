using System.Globalization;
using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// <c>{mount}/recordings/*</c> (12 §Rustion). <see cref="BlobAsync"/>/<see cref="ChunkAsync"/> (and
/// therefore <see cref="DownloadAsync"/>'s chunk loop and blob fallback) are node-local (RUS-002,
/// DSC-045).
/// </summary>
public sealed class RustionRecordingsOperations
{
    private const string DefaultMount = "rustion";

    private readonly LogicalOperations logical;

    internal RustionRecordingsOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
    }

    /// <summary><c>LIST {mount}/recordings/</c>.</summary>
    public async Task<IReadOnlyList<string>> ListAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "LIST", $"{RustionWire.Encode(mount)}/recordings/", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return KvWire.ReadKeys(response);
    }

    /// <summary><c>GET {mount}/recordings/{rid}</c>. <paramref name="rid"/> is validated against <c>rec_[A-Za-z0-9_-]+</c> before any request is sent.</summary>
    public Task<Response?> ReadAsync(
        string rid, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "GET", RustionWire.RecordingPath(mount, rid), null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>
    /// <c>GET {mount}/recordings/{rid}/blob</c>. RUS-002: node-local. <see cref="DownloadAsync"/>'s
    /// fallback payload when the chunk route is unsupported; carries the same <c>bytes_b64</c>
    /// field a chunk does.
    /// </summary>
    public async Task<byte[]> BlobAsync(
        string rid, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{RustionWire.RecordingPath(mount, rid)}/blob";
        Response? response = await logical.ExecuteShapedAsync(
            "GET", path, null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true, nodeLocal: true).ConfigureAwait(false);
        return RustionWire.ReadBytesB64(response?.Data, path);
    }

    /// <summary><c>GET {mount}/recordings/{rid}/chunk/{index}</c>. RUS-002: node-local. See <see cref="RustionRecordingChunk"/>.</summary>
    public Task<RustionRecordingChunk> ChunkAsync(
        string rid, int index, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return ChunkCoreAsync(rid, index, mount, options, cancellationToken);
    }

    private async Task<RustionRecordingChunk> ChunkCoreAsync(
        string rid, int index, string mount, RequestOptions? options, CancellationToken cancellationToken)
    {
        string path = $"{RustionWire.RecordingPath(mount, rid)}/chunk/{index.ToString(CultureInfo.InvariantCulture)}";
        Response? response = await logical.ExecuteShapedAsync(
            "GET", path, null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true, nodeLocal: true).ConfigureAwait(false);
        return RustionWire.ReadChunk(response?.Data ?? throw KvWire.EnvelopeMismatch(path, "bytes_b64"), path);
    }

    /// <summary><c>GET {mount}/recordings/{rid}/keystrokes</c>. No response shape is documented (D-M1c-25).</summary>
    public Task<Response?> KeystrokesAsync(
        string rid, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "GET", $"{RustionWire.RecordingPath(mount, rid)}/keystrokes", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>
    /// RUS-001: reads chunk 0, then 1, 2, … until <c>eof</c> is <see langword="true"/>, concatenating
    /// each <c>bytes_b64</c> payload and verifying SHA-256 when a digest was reported
    /// (<c>BV-PROTOCOL-004</c> on mismatch). Falls back to <see cref="BlobAsync"/> when the chunk
    /// route answers <c>BV-SERVER-004</c>. A <c>416</c>/<c>409</c> reaches the caller unchanged
    /// through the standard error-mapping pipeline.
    /// </summary>
    public async Task<byte[]> DownloadAsync(
        string rid, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = RustionWire.RecordingPath(mount, rid);

        RustionRecordingChunk chunk;
        try
        {
            chunk = await ChunkCoreAsync(rid, 0, mount, options, cancellationToken).ConfigureAwait(false);
        }
        catch (BastionVaultException ex) when (ex.Code == ErrorCodes.ServerUnsupportedByServer)
        {
            return await BlobAsync(rid, mount, options, cancellationToken).ConfigureAwait(false);
        }

        List<ReadOnlyMemory<byte>> segments = [chunk.Bytes];
        string? expectedSha256 = chunk.Sha256;
        int index = 0;
        while (!chunk.Eof)
        {
            index++;
            chunk = await ChunkCoreAsync(rid, index, mount, options, cancellationToken).ConfigureAwait(false);
            segments.Add(chunk.Bytes);
            if (!string.IsNullOrEmpty(chunk.Sha256))
            {
                expectedSha256 = chunk.Sha256;
            }
        }

        byte[] assembled = RustionWire.Concatenate(segments);
        RustionWire.RequireDigestMatch(assembled, expectedSha256, path);
        return assembled;
    }

    /// <summary><c>POST {mount}/recordings/pull</c>. No request/response shape is documented (D-M1c-25).</summary>
    public async Task PullAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{RustionWire.Encode(mount)}/recordings/pull", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>POST {mount}/recordings/reconcile</c>. No request/response shape is documented (D-M1c-25).</summary>
    public async Task ReconcileAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{RustionWire.Encode(mount)}/recordings/reconcile", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>POST {mount}/recordings/replay-log</c>. No request/response shape is documented (D-M1c-25).</summary>
    public async Task ReplayLogAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{RustionWire.Encode(mount)}/recordings/replay-log", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>POST {mount}/recordings/keystrokes/index</c>. No request/response shape is documented (D-M1c-25).</summary>
    public async Task IndexKeystrokesAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{RustionWire.Encode(mount)}/recordings/keystrokes/index", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>POST {mount}/recordings/keystroke-search</c>. <paramref name="query"/> travels in the POST body, never a query string (12's own note).</summary>
    public Task<Response?> KeystrokeSearchAsync(
        string query, int? limit = null, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(query);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "POST", $"{RustionWire.Encode(mount)}/recordings/keystroke-search", RustionWire.SerialiseKeystrokeSearch(query, limit), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true);
    }
}
