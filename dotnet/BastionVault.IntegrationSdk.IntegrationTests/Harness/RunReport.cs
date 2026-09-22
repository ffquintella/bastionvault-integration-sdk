using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace BastionVault.IntegrationSdk.IntegrationTests.Harness;

/// <summary>
/// ITG-031's "the skip MUST be counted and reported", and the place ITG-002's resolved version is
/// announced. One instance per run; written to the console and to <c>integration-run.log</c> in
/// the test output directory so a CI job can pick it up as an artefact without parsing TRX.
/// </summary>
internal sealed class RunReport
{
    private readonly ConcurrentQueue<string> versionSkips = new();
    private readonly ConcurrentQueue<string> warnings = new();
    private readonly ConcurrentQueue<string> teardownFailures = new();
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "integration-run.log");

    public int VersionSkipCount => versionSkips.Count;

    public void RecordVersionSkip(string test, string required, string actual)
    {
        versionSkips.Enqueue($"{test}: requires server >= {required}, server is {actual}");
    }

    public void RecordWarning(string warning)
    {
        warnings.Enqueue(warning);
    }

    public void RecordTeardownFailure(string test, IReadOnlyList<string> failures)
    {
        foreach (string failure in failures)
        {
            teardownFailures.Enqueue($"{test}: {failure}");
        }
    }

    public void Banner(TestServer server, TestMatrix matrix, OrphanCleaner.Result orphans, bool conformant)
    {
        StringBuilder text = new StringBuilder();
        _ = text.AppendLine("================ BastionVault SDK integration run ================");
        _ = text.AppendLine(CultureInfo.InvariantCulture, $"  server version : {server.Version}   <- ITG-002 resolved version");
        _ = text.AppendLine(CultureInfo.InvariantCulture, $"  matrix minimum : {matrix.MinimumVersion}   (specifications/test-matrix.json)");
        _ = text.AppendLine(CultureInfo.InvariantCulture, $"  mode           : {server.Mode}");
        _ = text.AppendLine(CultureInfo.InvariantCulture, $"  address        : {server.Address}");
        _ = text.AppendLine(CultureInfo.InvariantCulture, $"  run id         : {server.RunId}   (resources are named it-{server.RunId}-<test>-…)");
        _ = text.AppendLine(CultureInfo.InvariantCulture, $"  namespace      : {server.Namespace ?? "(root)"}");
        _ = text.AppendLine(CultureInfo.InvariantCulture, $"  tls skip verify: {server.TlsSkipVerify}");
        _ = text.AppendLine(CultureInfo.InvariantCulture,
            $"  orphan sweep   : removed {orphans.Removed.Count}, left {orphans.Skipped.Count}, failed {orphans.Failed.Count}   <- ITG-013");
        foreach (string removed in orphans.Removed)
        {
            _ = text.AppendLine(CultureInfo.InvariantCulture, $"      removed: {removed}");
        }

        foreach (string failure in orphans.Failed)
        {
            _ = text.AppendLine(CultureInfo.InvariantCulture, $"      FAILED : {failure}");
        }

        if (!conformant)
        {
            _ = text.AppendLine("  !! CONFORMANCE: NOT CLAIMED. The server is below the matrix minimum and");
            _ = text.AppendLine(CultureInfo.InvariantCulture,
                $"  !! {TestEnvironment.AllowUnsupportedVersion}=1 is in effect. Results of this run");
            _ = text.AppendLine("  !! do not demonstrate conformance against any supported server version.");
        }

        while (warnings.TryDequeue(out string? warning))
        {
            _ = text.AppendLine(CultureInfo.InvariantCulture, $"  warning        : {warning}");
        }

        _ = text.AppendLine("==================================================================");
        Emit(text.ToString());
    }

    public void Final()
    {
        StringBuilder text = new StringBuilder();
        _ = text.AppendLine("---------------- integration run summary ----------------");
        _ = text.AppendLine(CultureInfo.InvariantCulture, $"  version skips (ITG-031): {versionSkips.Count}");
        foreach (string skip in versionSkips)
        {
            _ = text.AppendLine(CultureInfo.InvariantCulture, $"      {skip}");
        }

        _ = text.AppendLine(CultureInfo.InvariantCulture, $"  teardown failures (ITG-010): {teardownFailures.Count}");
        foreach (string failure in teardownFailures)
        {
            _ = text.AppendLine(CultureInfo.InvariantCulture, $"      {failure}");
        }

        _ = text.AppendLine("---------------------------------------------------------");
        Emit(text.ToString());
    }

    private static void Emit(string text)
    {
        // Three destinations on purpose: stdout is what a developer sees, stderr survives a
        // runner that swallows out-of-test stdout, and the file is what CI can attach.
        Console.Out.Write(text);
        Console.Out.Flush();
        Console.Error.Write(text);
        Console.Error.Flush();
        try
        {
            File.AppendAllText(LogPath, text);
        }
        catch (IOException)
        {
            // A run that cannot write its own log still runs.
        }
    }
}
