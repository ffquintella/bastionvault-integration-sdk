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
