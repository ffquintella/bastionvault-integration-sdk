using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// KV v1 (KV-002's version-explicit <c>Kv.V1</c>): a flat engine whose storage is the request body
/// verbatim, with an optional lease. Reached from <see cref="KvOperations.V1"/>.
/// </summary>
/// <remarks>
/// KV1-004: v1 ignores <c>?env=</c>, so <b>no operation here takes an <c>env</c> parameter</b> and
/// KV2-022's environment fail-fast never applies to this sub-client. That is an absence by
/// requirement, not an omission.
/// <para>
/// Parameter order is D-M4-4's: <c>path</c> first, <c>mount</c> next with its <c>"secret"</c>
/// default, then the <see cref="RequestOptions"/>/<see cref="CancellationToken"/> tail every
/// operation in this SDK carries. 07 writes the operations as <c>(mount, path, …)</c>, but C#
/// cannot default a leading parameter and the fixture driver binds by name, so the wire contract is
/// indifferent to the order. Arguments without a default necessarily precede <c>mount</c>.
/// </para>
/// </remarks>
public sealed class KvV1Operations
{
    private const string DefaultMount = "secret";

    private readonly LogicalOperations logical;
    private readonly SysOperations sys;

    internal KvV1Operations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
        sys = new SysOperations(context, activeNamespace);
    }

    /// <summary>
    /// KV1-002, KV1-003: <c>GET {mount}/{path}</c>. Returns <see langword="null"/> for a
    /// <c>404</c> with an empty body; use <see cref="GetAsync"/> to raise <c>BV-KV-001</c> instead.
    /// </summary>
    /// <remarks><c>path</c>/<c>mount</c> are wire path segments. Conformance: Core (KV1-002, KV1-003). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Kv.V1.Read — KV1-002</spec>
    public async Task<KvV1Secret?> ReadAsync(
        string path,
        string mount = DefaultMount,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        KvWire.RequireSafePath(path, "path", KvWire.LogicalPath(mount, string.Empty, path));
        Response? response;
        try
        {
            response = await logical.ExecuteShapedAsync(
                "GET",
                KvWire.EncodedRoute(mount, string.Empty, path),
                null,
                options,
                defaultIdempotent: true,
                treatNotFoundEmptyAsAbsent: true,
                cancellationToken,
                pathIsEncoded: true).ConfigureAwait(false);
        }
        catch (BastionVaultException failure) when (failure.StatusCode == 404)
        {
            throw await EnrichForKvV2MountAsync(failure, mount, path, options, cancellationToken).ConfigureAwait(false);
        }

        if (response is null)
        {
            return null;
        }

        return new KvV1Secret
        {
            Data = response.Data ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal),
            // 07 §KV v1: the server defaults `lease_duration` to 3600. TRN-042 leaves the field
            // absent when the wire omits it, so the default named by the specification is applied
            // here rather than a zero that no server sends.
            LeaseDuration = response.LeaseDuration ?? TimeSpan.FromSeconds(3600),
            Renewable = response.Renewable ?? false,
        };
    }

    /// <summary>KV1-002: <see cref="ReadAsync"/>, raising <c>BV-KV-001 SecretNotFound</c> instead of returning <see langword="null"/>.</summary>
    /// <remarks>Never returns <see langword="null"/>. Conformance: Core (KV1-002). Errors beyond the common set (ERR-061): <c>BV-KV-001 SecretNotFound</c>.</remarks>
    /// <spec>Kv.V1.Read — KV1-002</spec>
    public async Task<KvV1Secret> GetAsync(
        string path,
        string mount = DefaultMount,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return await ReadAsync(path, mount, options, cancellationToken).ConfigureAwait(false)
            ?? throw KvWire.Engine(ErrorCodes.KvSecretNotFound, KvWire.LogicalPath(mount, string.Empty, path));
    }

    /// <summary>
    /// KV1-001: <c>POST {mount}/{path}</c> with the body stored verbatim, plus
    /// <c>{"ttl": "&lt;duration&gt;"}</c> when <paramref name="ttl"/> is given. An empty
    /// <paramref name="data"/> is refused client-side with <c>BV-INPUT-001</c>, and so is a
    /// negative <paramref name="ttl"/> (D-M4-13): section 07 is silent on one, <see cref="GoDuration"/>
    /// would happily emit <c>"-1h"</c>, and a negative lease has no meaning the server defines, so
    /// passing it through would be the plausible guess D-M1c-25 forbids.
    /// </summary>
    /// <remarks>Returns nothing (server answers <c>204</c>/<c>200</c> with no data used). Conformance: Core (KV1-001). Errors beyond the common set (ERR-061): <c>BV-INPUT-001</c> for empty <c>data</c> or a negative <c>ttl</c>.</remarks>
    /// <spec>Kv.V1.Write — KV1-001</spec>
    public async Task WriteAsync(
        string path,
        IReadOnlyDictionary<string, JsonElement> data,
        string mount = DefaultMount,
        TimeSpan? ttl = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        KvWire.RequireSafePath(path, "path", KvWire.LogicalPath(mount, string.Empty, path));
        KvWire.RequireData(data, "data", KvWire.LogicalPath(mount, string.Empty, path));
        if (ttl is { Ticks: < 0 })
        {
            throw KvWire.InvalidArgument("ttl", "must not be negative (D-M4-13)", KvWire.LogicalPath(mount, string.Empty, path));
        }

        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            foreach ((string key, JsonElement value) in data)
            {
                writer.WritePropertyName(key);
                value.WriteTo(writer);
            }

            if (ttl is { } lease)
            {
                // 07 §KV v1 spells the merged field as a duration string; KV2-010 fixes the
                // spelling as Go-style for this engine.
                writer.WriteString("ttl", GoDuration.Format(lease));
            }
        });

        _ = await logical.ExecuteShapedAsync(
            "POST",
            KvWire.EncodedRoute(mount, string.Empty, path),
            body,
            options,
            defaultIdempotent: false,
            treatNotFoundEmptyAsAbsent: false,
            cancellationToken,
            pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>KV-002: <c>DELETE {mount}/{path}</c> → <c>204</c>.</summary>
    /// <remarks>Returns nothing; a missing secret is not an error (idempotent delete). Conformance: Core (KV1-004). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Kv.V1.Delete — KV1-004</spec>
    public async Task DeleteAsync(
        string path,
        string mount = DefaultMount,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        KvWire.RequireSafePath(path, "path", KvWire.LogicalPath(mount, string.Empty, path));
        _ = await logical.ExecuteShapedAsync(
            "DELETE",
            KvWire.EncodedRoute(mount, string.Empty, path),
            null,
            options,
            defaultIdempotent: false,
            treatNotFoundEmptyAsAbsent: false,
            cancellationToken,
            pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>
    /// KV-002: <c>LIST {mount}/{prefix}/</c> (TRN-011's literal verb). A <c>404</c> with an empty
    /// body is an empty list, never an error.
    /// </summary>
    /// <remarks>Never returns <see langword="null"/>; an absent prefix is an empty list. Conformance: Core (KV1-003). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Kv.V1.List — KV1-003</spec>
    public async Task<IReadOnlyList<string>> ListAsync(
        string prefix = "",
        string mount = DefaultMount,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        KvWire.RequireSafePrefix(prefix, KvWire.LogicalPath(mount, string.Empty, prefix));
        Response? response = await logical.ExecuteShapedAsync(
            "LIST",
            KvWire.EncodedListRoute(mount, string.Empty, prefix),
            null,
            options,
            defaultIdempotent: true,
            treatNotFoundEmptyAsAbsent: true,
            cancellationToken,
            pathIsEncoded: true).ConfigureAwait(false);
        return KvWire.ReadKeys(response);
    }
    /// <summary>
    /// ERR-040's KV-v2 row, re-booked from M4 to M7 by D-M4-14 and landed here:
    /// "<c>404</c> and path is <c>&lt;mount&gt;/&lt;name&gt;</c> on a KV v2 mount (from the
    /// <c>Sys.ListMounts</c> cache)" → the note naming the <c>data/</c> route.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Scoped to this operation, not to every <c>404</c> in the SDK.</b> The row's condition is
    /// a v1-shaped read against a v2 mount, which is reachable only from here: the mount and the
    /// name arrive as separate arguments (so nothing is guessed by splitting a path), and
    /// <c>Kv.V2.*</c> cannot produce the shape at all because KV2-001 puts <c>data/</c> in every
    /// one of its routes. Enriching in <c>RequestExecutor</c> instead would have put a
    /// mount-table lookup behind every <c>404</c> the SDK can raise.
    /// </para>
    /// <para>
    /// The lookup goes through <c>Sys.MountTypeOf</c> (SYS-026), so it is free on a warm cache and
    /// costs at most one <c>sys/mounts</c> request per 60 seconds per namespace on a cold one. If
    /// the lookup <i>itself</i> fails — no permission on <c>sys/mounts</c> is the ordinary case —
    /// the caller's original error is returned unchanged: an enrichment must never replace the
    /// failure it was trying to explain.
    /// </para>
    /// </remarks>
    private async Task<BastionVaultException> EnrichForKvV2MountAsync(
        BastionVaultException failure,
        string mount,
        string path,
        RequestOptions? options,
        CancellationToken cancellationToken)
    {
        string? type;
        try
        {
            type = await sys.MountTypeOfAsync(mount, options, cancellationToken).ConfigureAwait(false);
        }
        catch (BastionVaultException)
        {
            return failure;
        }

        if (!string.Equals(type, MountTypes.KvV2, StringComparison.Ordinal))
        {
            return failure;
        }

        return failure.WithHint(HintEnrichment.AppendNote(
            failure.Hint,
            HintEnrichment.KvV2MountNote(mount.Trim('/'), path.Trim('/'))));
    }
}
