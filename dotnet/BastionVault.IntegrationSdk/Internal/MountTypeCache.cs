namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// SYS-026's per-client mount-type cache: a 60-second snapshot of the <c>sys/mounts</c> table,
/// invalidated by <c>Mount</c>, <c>Unmount</c> and <c>Remount</c>. It lives on
/// <see cref="ClientContext"/> rather than on <c>SysOperations</c> because
/// <see cref="BastionVaultClient.Sys"/> builds a fresh <c>SysOperations</c> on every property read
/// — a cache held there would have a lifetime of one call and would satisfy SYS-026 in name only.
/// </summary>
/// <remarks>
/// <para>
/// Keyed by the <b>effective namespace</b> of the call — <see cref="RequestOptions.Namespace"/>
/// when the caller overrides it, else the view's active namespace — which the requirement does not
/// spell out and which is not optional: the mount table is namespace-scoped, every
/// <see cref="BastionVaultClient.WithNamespace"/> view shares this one
/// <see cref="ClientContext"/> (D-M1b-9), and a single un-keyed entry would let a lookup in one
/// namespace answer with another namespace's table. That is a wrong answer, not a stale one.
/// </para>
/// <para>
/// The key is the namespace the request <i>goes out under</i>, never the view's, because
/// <c>RequestOptions.Namespace</c> is a documented per-call override: keying on the view while
/// sending on the override poisons one tenant's entry with another's table, serves one tenant's
/// answer to a call explicitly aimed at another, and invalidates a namespace the mutation did not
/// touch. D-M7-25.
/// </para>
/// <para>
/// Invalidation is likewise scoped to the namespace that mutated — the <i>effective</i> one, so a
/// mutation under a per-call override clears the entry it actually changed: a <c>Mount</c> in one
/// tenant says nothing about another tenant's table, and dropping every entry would turn one
/// tenant's write into a cache stampede for all of them.
/// </para>
/// <para>
/// The whole table is cached, not one path, because SYS-025 already establishes that a per-mount
/// read does not exist — the server returns the whole table for any mount question — so a per-path
/// cache would buy nothing and would issue N requests where one suffices.
/// </para>
/// </remarks>
internal sealed class MountTypeCache
{
    /// <summary>SYS-026's TTL, fixed by the requirement rather than configurable.</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);

    /// <summary>The cached table for <paramref name="effectiveNamespace"/>, or <see langword="null"/> when absent or expired.</summary>
    public IReadOnlyDictionary<string, MountInfo>? TryGet(string effectiveNamespace, DateTimeOffset now)
    {
        lock (gate)
        {
            if (!entries.TryGetValue(effectiveNamespace, out Entry entry))
            {
                return null;
            }

            if (now >= entry.ExpiresAt)
            {
                _ = entries.Remove(effectiveNamespace);
                return null;
            }

            return entry.Table;
        }
    }

    /// <summary>Stores <paramref name="table"/> for <see cref="Ttl"/> from <paramref name="now"/>.</summary>
    public void Store(string effectiveNamespace, DateTimeOffset now, IReadOnlyDictionary<string, MountInfo> table)
    {
        lock (gate)
        {
            entries[effectiveNamespace] = new Entry(now + Ttl, table);
        }
    }

    /// <summary>SYS-026's invalidation, on <c>Mount</c>, <c>Unmount</c> and <c>Remount</c>.</summary>
    public void Invalidate(string effectiveNamespace)
    {
        lock (gate)
        {
            _ = entries.Remove(effectiveNamespace);
        }
    }

    private readonly record struct Entry(DateTimeOffset ExpiresAt, IReadOnlyDictionary<string, MountInfo> Table);
}
