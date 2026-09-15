using System.Diagnostics.CodeAnalysis;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// One namespace record (SYS-060). Every quota is on <see cref="Quotas"/>, where <c>0</c> means
/// unlimited — not "denied" and not "unset".
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "06-system-api.md names this record `Namespace` and it is the cross-language contract (SYS-060). The keyword collision is Visual Basic's, and a VB consumer reaches the type as `[Namespace]`; C# and F# consumers see no collision at all. Renaming would make .NET the only SDK of the three whose type name differs from the specification's. See DR-0012 D-M7-11.")]
public sealed class Namespace
{
    /// <summary>The wire <c>uuid</c> field.</summary>
    public required string Uuid { get; init; }

    /// <summary>The wire <c>path</c> field. The empty string denotes root (SYS-061).</summary>
    public required string Path { get; init; }

    /// <summary>The wire <c>parent_uuid</c> field; <see langword="null"/> for a child of root.</summary>
    public string? ParentUuid { get; init; }

    /// <summary>The wire <c>created_at</c> field, parsed as RFC 3339 UTC; <see langword="null"/> when absent or unparseable.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>The wire <c>child_visible_default</c> field. ⚠️ SYS-060: <c>Sys.WriteNamespace</c> resets this to <see langword="false"/> when the spec omits it.</summary>
    public required bool ChildVisibleDefault { get; init; }

    /// <summary>The namespace's quotas. Never <see langword="null"/>: a server that omits the object sends all-unlimited.</summary>
    public required NamespaceQuotas Quotas { get; init; }
}

/// <summary>
/// A namespace's six quotas (SYS-060). <c>0</c> means <b>unlimited</b> in every one of them, which
/// is also what <c>Sys.WriteNamespace</c> resets an omitted quota to.
/// </summary>
public sealed class NamespaceQuotas
{
    /// <summary>The wire <c>max_storage_bytes</c> field.</summary>
    public long MaxStorageBytes { get; init; }

    /// <summary>The wire <c>max_leases</c> field.</summary>
    public long MaxLeases { get; init; }

    /// <summary>The wire <c>request_rate</c> field.</summary>
    public long RequestRate { get; init; }

    /// <summary>The wire <c>max_mounts</c> field.</summary>
    public long MaxMounts { get; init; }

    /// <summary>The wire <c>max_entities</c> field.</summary>
    public long MaxEntities { get; init; }

    /// <summary>The wire <c>max_child_namespaces</c> field.</summary>
    public long MaxChildNamespaces { get; init; }
}

/// <summary>
/// The body of <c>Sys.WriteNamespace</c> (SYS-060).
/// </summary>
/// <remarks>
/// ⚠️ This is a <b>full replace</b>, not a patch. Every field this object does not set is written
/// as its zero value: an omitted quota becomes <c>0</c> (unlimited) and an omitted
/// <see cref="ChildVisibleDefault"/> becomes <see langword="false"/>. Sending a
/// <see cref="NamespaceSpec"/> built from nothing at an existing namespace therefore clears its
/// quotas. <c>Sys.UpdateNamespace</c> is the read-merge-write form, and is the one to reach for
/// when the intent is to change one field.
/// </remarks>
public sealed class NamespaceSpec
{
    /// <summary>The wire <c>child_visible_default</c> field. Defaults to <see langword="false"/>, which is also what an omitted field writes.</summary>
    public bool ChildVisibleDefault { get; init; }

    /// <summary>The quotas to write. <see langword="null"/> writes all six as <c>0</c>.</summary>
    public NamespaceQuotas? Quotas { get; init; }
}

/// <summary>
/// The patch <c>Sys.UpdateNamespace</c> takes (SYS-060): every member is nullable, and an unset one
/// keeps the value the namespace already has rather than resetting it.
/// </summary>
public sealed class NamespacePatch
{
    /// <summary>Set <c>child_visible_default</c>, or leave it as it is.</summary>
    public bool? ChildVisibleDefault { get; init; }

    /// <summary>Set <c>max_storage_bytes</c>, or leave it as it is.</summary>
    public long? MaxStorageBytes { get; init; }

    /// <summary>Set <c>max_leases</c>, or leave it as it is.</summary>
    public long? MaxLeases { get; init; }

    /// <summary>Set <c>request_rate</c>, or leave it as it is.</summary>
    public long? RequestRate { get; init; }

    /// <summary>Set <c>max_mounts</c>, or leave it as it is.</summary>
    public long? MaxMounts { get; init; }

    /// <summary>Set <c>max_entities</c>, or leave it as it is.</summary>
    public long? MaxEntities { get; init; }

    /// <summary>Set <c>max_child_namespaces</c>, or leave it as it is.</summary>
    public long? MaxChildNamespaces { get; init; }
}

/// <summary><c>Sys.NamespacesSelf</c>'s result: the namespaces this token can see, and where it sits.</summary>
public sealed class NamespacesSelf
{
    /// <summary>The wire <c>namespaces</c> array. <c>""</c> denotes root.</summary>
    public required IReadOnlyList<string> Namespaces { get; init; }

    /// <summary>The wire <c>token_namespace</c> field. <c>""</c> denotes root.</summary>
    public required string TokenNamespace { get; init; }

    /// <summary>The wire <c>root</c> field: whether the token is a root-namespace token.</summary>
    public required bool Root { get; init; }
}

/// <summary>
/// One page of a cursor-paginated <c>*-info</c> listing (14 — batch and request efficiency,
/// PAG-003, PAG-005). <see cref="Keys"/> and <see cref="Records"/> are index-aligned, and
/// <see cref="Entries"/> is the zipped view.
/// </summary>
/// <typeparam name="T">The record type the listing returns.</typeparam>
public sealed class Page<T>
{
    /// <summary>The page's keys, in server order.</summary>
    public required IReadOnlyList<string> Keys { get; init; }

    /// <summary>The page's records; <c>Records[i]</c> belongs to <c>Keys[i]</c> (PAG-005).</summary>
    public required IReadOnlyList<T> Records { get; init; }

    /// <summary>The total number of records behind the cursor, not the page size.</summary>
    public required int Total { get; init; }

    /// <summary>The cursor to pass as the next call's <c>after</c>; <see langword="null"/> on the last page (PAG-003).</summary>
    public string? Next { get; init; }

    /// <summary>Whether a further page exists.</summary>
    public required bool Truncated { get; init; }

    /// <summary>PAG-005's zipped view: each record beside the key it belongs to.</summary>
    public IReadOnlyList<KeyValuePair<string, T>> Entries =>
        [.. Keys.Select((key, index) => new KeyValuePair<string, T>(key, Records[index]))];
}
