using System.Globalization;

namespace BastionVault.IntegrationSdk.IntegrationTests.Harness;

/// <summary>
/// ITG-013's <c>Cleanup-Orphans</c>: at suite start, remove <c>it-*</c> mounts left behind by an
/// aborted earlier run, so a rerun cannot fail on leftover state.
/// <para>
/// <b>How a mount's age is known.</b> <c>sys/mounts</c> returns only <c>type</c> and
/// <c>description</c> (MNT-002), so there is no server-side creation time to read. Two
/// independent sources are used, in this order:
/// </para>
/// <list type="number">
/// <item>the run id embedded in the path - <c>it-&lt;base36 seconds&gt;&lt;rand&gt;-…</c>;</item>
/// <item>the <c>created=&lt;unix seconds&gt;</c> stamp <see cref="ResourceLedger"/> writes into
/// the description.</item>
/// </list>
/// <para>
/// A mount that matches <c>it-*</c> but yields no age from either source is <b>left alone</b> and
/// reported. Deleting an undateable mount would be the one way this helper could destroy data a
/// developer cared about, and a reported orphan costs a person thirty seconds.
/// </para>
/// </summary>
internal static class OrphanCleaner
{
    public static readonly TimeSpan MinimumAge = TimeSpan.FromHours(1);

    public sealed record Result(IReadOnlyList<string> Removed, IReadOnlyList<string> Skipped, IReadOnlyList<string> Failed);

    public static async Task<Result> RunAsync(BastionVaultClient client, CancellationToken cancellationToken)
    {
        List<string> removed = [];
        List<string> skipped = [];
        List<string> failed = [];

        IReadOnlyDictionary<string, MountInfo> mounts = await client.Sys.ListMountsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        await SweepAsync(mounts, "mount", p => client.Sys.UnmountAsync(p, cancellationToken: cancellationToken)).ConfigureAwait(false);

        IReadOnlyDictionary<string, MountInfo> auth = await client.Sys.ListAuthMethodsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        await SweepAsync(auth, "auth", p => client.Sys.DisableAuthMethodAsync(p, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return new Result(removed, skipped, failed);

        async Task SweepAsync(
            IReadOnlyDictionary<string, MountInfo> table,
            string kind,
            Func<string, Task> delete)
        {
            foreach ((string? rawPath, MountInfo? info) in table)
            {
                string path = rawPath.TrimEnd('/');
                if (!path.StartsWith("it-", StringComparison.Ordinal))
                {
                    continue;
                }

                DateTimeOffset? created = AgeOf(path, info.Description);
                if (created is null)
                {
                    skipped.Add($"{kind}:{path} (no creation time in path or description)");
                    continue;
                }

                if (DateTimeOffset.UtcNow - created.Value < MinimumAge)
                {
                    // Younger than the floor: it may belong to a run happening right now.
                    skipped.Add($"{kind}:{path} (younger than {MinimumAge.TotalHours:0} h)");
                    continue;
                }

                try
                {
                    await delete(path).ConfigureAwait(false);
                    removed.Add($"{kind}:{path}");
                }
                catch (BastionVaultException ex)
                {
                    failed.Add($"{kind}:{path} ({ex.Code})");
                }
            }
        }
    }

    internal static DateTimeOffset? AgeOf(string path, string? description)
    {
        string[] fields = path.Split('-');
        if (fields.Length >= 2 && TestServer.EpochFromRunId(fields[1]) is { } epoch && Plausible(epoch))
        {
            return DateTimeOffset.FromUnixTimeSeconds(epoch);
        }

        const string Marker = "created=";
        int index = description?.IndexOf(Marker, StringComparison.Ordinal) ?? -1;
        if (index >= 0 && description is not null)
        {
            string tail = description[(index + Marker.Length)..];
            string digits = new string([.. tail.TakeWhile(char.IsAsciiDigit)]);
            if (long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long stamped) && Plausible(stamped))
            {
                return DateTimeOffset.FromUnixTimeSeconds(stamped);
            }
        }

        return null;

        // Guards against a path field that happens to decode as base-36 but is not a clock:
        // anything before 2020 or more than a day in the future is not a run id.
        static bool Plausible(long epoch) =>
            epoch > 1_577_836_800 && epoch < DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 86_400;
    }
}
