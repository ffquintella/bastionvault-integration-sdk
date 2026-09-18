using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// The KV engine surface (07 — KV engine), reached from <see cref="BastionVaultClient.Kv"/>.
/// </summary>
/// <remarks>
/// <para>
/// KV-002: the operations are <b>version-explicit</b>. There is no version-agnostic
/// <c>Kv.ReadSecret</c> façade, and the omission is deliberate (D-M4-9): KV-002 offers one as a
/// <c>MAY</c> and makes it depend on <c>Kv.DetectVersion</c>, whose prerequisite
/// (<c>Sys.MountTypeOf</c>, SYS-026) does not exist yet. A façade that assumed <c>V2</c> whenever
/// detection was unavailable would be a guess in a public API; adding one later is not a breaking
/// change, removing one would be.
/// </para>
/// <para>
/// D-M4-2 deferred two members 07 names, each with a named owner. <c>Kv.DetectVersion</c> (KV-001)
/// was deferred <i>solely</i> because <c>Sys.MountTypeOf</c> (SYS-026) did not exist; SYS-026 now
/// exists, so <see cref="DetectVersionAsync"/> lands here and KV-001 leaves the traceability
/// baseline. <c>Kv.ReadMany</c> (KV-010) still needs <c>Sys.Batch</c> (BAT-007) and stays deferred
/// to M8.
/// </para>
/// <para>
/// KV-002's version-agnostic façade stays declined (D-M4-9), and landing KV-001 does not reopen
/// it: KV-002 offers the façade as a <c>MAY</c> and requires it to fall back to assuming
/// <c>V2</c> when detection is refused, which is a guess this SDK does not make in a public API
/// without a decision that says so. A caller who wants that behaviour can now write it in three
/// lines over <see cref="DetectVersionAsync"/>, which is the smaller surface to own.
/// </para>
/// </remarks>
public sealed class KvOperations
{
    private readonly SysOperations sys;

    internal KvOperations(ClientContext context, string activeNamespace)
    {
        V1 = new KvV1Operations(context, activeNamespace);
        V2 = new KvV2Operations(context, activeNamespace);
        sys = new SysOperations(context, activeNamespace);
    }

    /// <summary>KV v1: flat, optional lease, no environments (KV1-001…KV1-004).</summary>
    public KvV1Operations V1 { get; }

    /// <summary>KV v2: versions, CAS, soft delete, metadata, environments, path helpers (KV2-001…KV2-030).</summary>
    public KvV2Operations V2 { get; }

    /// <summary>
    /// KV-001: which KV engine is mounted at <paramref name="mount"/>, via
    /// <c>Sys.MountTypeOf</c>'s cached lookup (SYS-026). <c>kv</c> is
    /// <see cref="KvVersion.V1"/> and <c>kv-v2</c> is <see cref="KvVersion.V2"/>; any other type is
    /// <c>BV-KV-010 NotAKvMount</c>, and a mount that does not exist is <c>BV-NOTFOUND-002</c>.
    /// </summary>
    /// <remarks>
    /// The requirement names <c>Sys.MountTypeOf</c> specifically, rather than "a mount lookup", and
    /// that matters: the SYS-026 cache is shared per client, so a loop that detects the version of
    /// twenty mounts issues one <c>sys/mounts</c> request rather than twenty. A private lookup here
    /// would have been a second, uncoordinated cache.
    /// </remarks>
    public async Task<KvVersion> DetectVersionAsync(string mount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        string? type = await sys.MountTypeOfAsync(mount, options, cancellationToken).ConfigureAwait(false);
        return type switch
        {
            MountTypes.Kv => KvVersion.V1,
            MountTypes.KvV2 => KvVersion.V2,
            null => throw MountFailure(ErrorCodes.NotFoundMountNotFound, mount, type),
            _ => throw MountFailure(ErrorCodes.KvNotAKvMount, mount, type),
        };
    }

    /// <summary>
    /// KV-010 / BAT-007: reads many KV v2 secrets in <b>one</b> request, by composing the
    /// KV2-030 <c>{mount}/data/{path}</c> paths, sending them through <c>Sys.Batch</c> and
    /// unwrapping each result. The returned map is keyed by the caller's own paths, in the order
    /// they were given.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the operation section 14 exists to make people use.</b> <c>map(read)</c> over a
    /// list of twenty paths is twenty requests against a guard that bans at 200 in 10 s; this is
    /// one.
    /// </para>
    /// <para>
    /// <b>The fallback (BAT-007).</b> A server that predates batching answers <c>sys/batch</c>
    /// with <c>BV-SERVER-004</c>, and the SDK then reads the paths one at a time <i>through the
    /// rate gate</i> rather than failing. That costs <c>1 + N</c> requests and is the slow path
    /// on purpose: it is a compatibility shim, not the intended shape. No other error is caught —
    /// a <c>403</c> on <c>sys/batch</c> is the caller's problem to see, not something to paper
    /// over with N more requests that will each get their own <c>403</c>.
    /// </para>
    /// <para>
    /// Both paths yield the same two-state entry, which is the point of
    /// <see cref="KvReadManyEntry"/>: a path either produced a secret or produced an error, and a
    /// caller does not have to know which path answered. That is why the fallback reads through
    /// <see cref="KvV2Operations.GetSecretAsync"/> and not
    /// <see cref="KvV2Operations.ReadSecretAsync"/> — the latter returns <see langword="null"/>
    /// for an absent secret, while a batch reports the same absence as a <c>404</c> result, so
    /// using it would make "missing" mean two different things depending on the server's age.
    /// The one place the paths legitimately differ is
    /// <see cref="KvReadManyEntry.Metadata"/>, which the batch route may minimise away and a
    /// standalone read always carries.
    /// </para>
    /// </remarks>
    /// <param name="mount">The KV v2 mount, e.g. <c>secret</c>.</param>
    /// <param name="paths">The secret paths within the mount, without the <c>data/</c> group. Must be non-empty and free of duplicates.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<IReadOnlyDictionary<string, KvReadManyEntry>> ReadManyAsync(
        string mount,
        IReadOnlyList<string> paths,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ArgumentNullException.ThrowIfNull(paths);

        // A duplicate would silently collapse two requested paths into one map entry, so the
        // caller would get fewer answers than questions with nothing to say so.
        if (paths.Distinct(StringComparer.Ordinal).Count() != paths.Count)
        {
            throw KvWire.InvalidArgument("paths", "contains duplicate entries", KvV2Operations.DataPath(string.Empty, mount));
        }

        BatchOperation[] operations = [.. paths.Select(path => new BatchOperation
        {
            Operation = BatchOperationKind.Read,
            Path = KvV2Operations.DataPath(path, mount),
        })];

        IReadOnlyList<BatchResult> results;
        try
        {
            results = await sys.BatchAsync(operations, options, cancellationToken).ConfigureAwait(false);
        }
        catch (BastionVaultException failure) when (failure.Code == ErrorCodes.ServerUnsupportedByServer)
        {
            // BAT-007's 1 + N fallback. Each read goes through the ordinary request path, so it
            // is gated by EFF-001 exactly like any other read — which is the clause BAT-007
            // spells out, and the reason this loop is sequential rather than a Task.WhenAll.
            return await ReadManySequentiallyAsync(mount, paths, options, cancellationToken).ConfigureAwait(false);
        }

        if (results.Count != paths.Count)
        {
            // The server answered a different number of operations than it was asked; zipping by
            // index would silently attribute one path's answer to another.
            throw KvWire.EnvelopeMismatch(KvV2Operations.DataPath(string.Empty, mount), "results");
        }

        Dictionary<string, KvReadManyEntry> map = new(paths.Count, StringComparer.Ordinal);
        for (int index = 0; index < paths.Count; index++)
        {
            map[paths[index]] = Unwrap(results[index], KvV2Operations.DataPath(paths[index], mount));
        }

        return map;
    }

    /// <summary>BAT-007's fallback: <c>1 + N</c> requests, one per path, each through the rate gate.</summary>
    private async Task<IReadOnlyDictionary<string, KvReadManyEntry>> ReadManySequentiallyAsync(
        string mount,
        IReadOnlyList<string> paths,
        RequestOptions? options,
        CancellationToken cancellationToken)
    {
        Dictionary<string, KvReadManyEntry> map = new(paths.Count, StringComparer.Ordinal);
        foreach (string path in paths)
        {
            try
            {
                KvV2Secret secret = await V2.GetSecretAsync(path, mount, null, null, options, cancellationToken).ConfigureAwait(false);
                map[path] = KvReadManyEntry.Found(secret.Data, secret.Metadata);
            }
            catch (BastionVaultException failure)
            {
                map[path] = KvReadManyEntry.Failed(failure);
            }
        }

        return map;
    }

    /// <summary>
    /// BAT-007's "unwraps results": one <see cref="BatchResult"/> becomes either the secret it
    /// carried or the error BAT-005 already mapped for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A batched read's payload has a KV v2 read's <i>shape</i> — <c>{"data": …, "metadata": …}</c>
    /// — but not its guarantees, which is why this does not build a
    /// <see cref="KvV2Secret"/>. <see cref="KvV2VersionMetadata.CreatedTime"/> is
    /// <c>required</c>, and the server's batch payload minimises the metadata down to what the
    /// operation produced: <c>kv.read-many-batch</c>, captured under FIX-010, carries
    /// <c>"metadata": {"version": 1}</c> with no <c>created_time</c>. Reusing that type would
    /// make the SDK raise <c>BV-PROTOCOL-002</c> on a response the server really sends — a
    /// stricter envelope contract than the route has — so <see cref="KvReadManyEntry.Metadata"/>
    /// is nullable and absent metadata is reported as absent rather than invented (D-M1c-25).
    /// </para>
    /// </remarks>
    private static KvReadManyEntry Unwrap(BatchResult result, string logicalPath)
    {
        if (result.Error is { } error)
        {
            return KvReadManyEntry.Failed(error);
        }

        if (result.Data is not { ValueKind: JsonValueKind.Object } payload)
        {
            return KvReadManyEntry.Failed(KvWire.EnvelopeMismatch(logicalPath, "data"));
        }

        IReadOnlyDictionary<string, JsonElement> body = KvWire.AsMap(payload);
        IReadOnlyDictionary<string, JsonElement> metadataWire = body.TryGetValue("metadata", out JsonElement metadataElement)
            ? KvWire.AsMap(metadataElement)
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        // `ReadVersionMetadata` raises BV-PROTOCOL-002 when `created_time` is missing, which is
        // right for a standalone read and wrong here (see the remarks). Asked first, so the
        // minimised form is absence rather than a failure.
        KvV2VersionMetadata? metadata = KvWire.ReadOptionalInstant(metadataWire, "created_time") is null
            ? null
            : KvWire.ReadVersionMetadata(metadataWire, logicalPath);

        return KvReadManyEntry.Found(KvWire.ReadDataMap(body, "data"), metadata);
    }

    private static BastionVaultException MountFailure(string code, string mount, string? type)
    {
        ErrorCatalogEntry entry = ErrorCatalog.Require(code);
        Dictionary<string, object?> details = new(StringComparer.Ordinal) { ["mount"] = mount };
        if (type is not null)
        {
            details["type"] = type;
        }

        return BastionVaultException.Request(
            code,
            entry.Category,
            entry.Message,
            entry.Hint,
            retryable: entry.Retryable,
            attempts: 0,
            path: "sys/mounts",
            details: details);
    }
}

/// <summary>KV-001's answer: which KV engine is mounted at a path.</summary>
public enum KvVersion
{
    /// <summary>The <c>kv</c> engine: flat, optional lease (KV1-001…KV1-004).</summary>
    V1,

    /// <summary>The <c>kv-v2</c> engine: versioned, soft delete, CAS, environments (KV2-001…KV2-030).</summary>
    V2,
}

/// <summary>
/// KV-010 / BAT-007's <c>KvSecret | Error</c> union: one path's answer from
/// <see cref="KvOperations.ReadManyAsync"/>. Either <see cref="Error"/> is set, or the path
/// resolved and <see cref="Data"/>/<see cref="Metadata"/> describe what it resolved to.
/// </summary>
/// <remarks>
/// <para>
/// A closed two-state type rather than a nullable secret plus a nullable error that a caller has
/// to reason about: BAT-005 makes a failed operation a <i>result</i> rather than a throw, so the
/// failure has to be representable, and a third "neither" state would be a way for a bug in one
/// of the two answering paths to reach the caller as silence.
/// </para>
/// <para>
/// It is deliberately not a <see cref="KvV2Secret"/>. Section 14 writes the success side as
/// <c>KvSecret</c>, and the batch route's per-operation payload does not carry everything
/// <see cref="KvV2VersionMetadata"/> requires — see <c>KvOperations.Unwrap</c>'s remarks.
/// </para>
/// </remarks>
public sealed class KvReadManyEntry
{
    private KvReadManyEntry(
        IReadOnlyDictionary<string, JsonElement>? data,
        KvV2VersionMetadata? metadata,
        BastionVaultException? error)
    {
        Data = data;
        Metadata = metadata;
        Error = error;
    }

    /// <summary>
    /// The secret's data, or <see langword="null"/> when this path failed or when the version is
    /// soft-deleted (KV2-004).
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement>? Data { get; }

    /// <summary>
    /// The version metadata, or <see langword="null"/> when this path failed or when the batch
    /// payload carried none. A fallback read (BAT-007) always carries it; a batched one may not.
    /// </summary>
    public KvV2VersionMetadata? Metadata { get; }

    /// <summary>The mapped error for this path, or <see langword="null"/> when it resolved.</summary>
    public BastionVaultException? Error { get; }

    /// <summary>Whether this path resolved rather than failing.</summary>
    public bool IsSuccess => Error is null;

    /// <summary>KV2-004's derived state: no data and a deletion time. <see cref="KvV2SecretState.Live"/> for a failed path.</summary>
    public KvV2SecretState State => Data is null && Metadata?.DeletionTime is not null
        ? KvV2SecretState.SoftDeleted
        : KvV2SecretState.Live;

    internal static KvReadManyEntry Found(IReadOnlyDictionary<string, JsonElement>? data, KvV2VersionMetadata? metadata)
    {
        return new KvReadManyEntry(data, metadata, null);
    }

    internal static KvReadManyEntry Failed(BastionVaultException error)
    {
        return new KvReadManyEntry(null, null, error);
    }
}
