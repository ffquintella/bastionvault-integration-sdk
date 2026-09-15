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
/// Two members 07 names are also absent for the same reason, each with a named owner (D-M4-2):
/// <c>Kv.DetectVersion</c> (KV-001) needs <c>Sys.MountTypeOf</c> and belongs to M7, and
/// <c>Kv.ReadMany</c> (KV-010) needs <c>Sys.Batch</c> (BAT-007) and belongs to M8. Both stay on the
/// traceability baseline; a private mount lookup in their place would pin M7's contract by
/// accident.
/// </para>
/// </remarks>
public sealed class KvOperations
{
    internal KvOperations(ClientContext context, string activeNamespace)
    {
        V1 = new KvV1Operations(context, activeNamespace);
        V2 = new KvV2Operations(context, activeNamespace);
    }

    /// <summary>KV v1: flat, optional lease, no environments (KV1-001…KV1-004).</summary>
    public KvV1Operations V1 { get; }

    /// <summary>KV v2: versions, CAS, soft delete, metadata, environments, path helpers (KV2-001…KV2-030).</summary>
    public KvV2Operations V2 { get; }
}
