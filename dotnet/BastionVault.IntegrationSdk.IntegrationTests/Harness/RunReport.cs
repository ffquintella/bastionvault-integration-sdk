using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
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
    private readonly ConcurrentQueue<string> protocolViolations = new();
    private readonly ConcurrentQueue<string> observabilityDefects = new();
    private readonly ConcurrentQueue<string> secretLeaks = new();
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "integration-run.log");
    private IReadOnlyDictionary<string, int> operationsBySection = new Dictionary<string, int>();

    public int VersionSkipCount => versionSkips.Count;

    public void RecordVersionSkip(string test, string required, string actual)
    {
        versionSkips.Enqueue($"{test}: requires server >= {required}, server is {actual}");
    }

    public void RecordWarning(string warning)
    {
        warnings.Enqueue(warning);
    }

    /// <summary>
    /// Records one scope's ITG-020/021/022 result for the Final() tally. <see cref="RunAssertions.Enforce"/>
    /// is what fails the scope; this is bookkeeping only, called whether or not it did.
    /// </summary>
    public void RecordGlobalViolations(
        IReadOnlyList<string> newProtocolViolations,
        IReadOnlyList<string> newObservabilityDefects,
        IReadOnlyList<string> newSecretLeaks)
    {
        foreach (string violation in newProtocolViolations)
        {
            protocolViolations.Enqueue(violation);
        }

        foreach (string defect in newObservabilityDefects)
        {
            observabilityDefects.Enqueue(defect);
        }

        foreach (string leak in newSecretLeaks)
        {
            secretLeaks.Enqueue(leak);
        }
    }

    /// <summary>ITG-023: a summary, not an assertion. Recorded once, at the end of the run.</summary>
    public void RecordOperationsBySection(IReadOnlyDictionary<string, int> counts)
    {
        operationsBySection = counts;
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
            _ = text.AppendLine("  !! CONFORMANCE: NOT CLAIMED. The server is below the matrix minimum;");
            _ = text.AppendLine("  !! this run is about to refuse to continue (ITG-002, DR-0019 D-M12-9).");
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

        AppendGlobalAssertions(text);

        _ = text.AppendLine("---------------------------------------------------------");
        Emit(text.ToString());
    }

    private void AppendGlobalAssertions(StringBuilder text)
    {
        _ = text.AppendLine(CultureInfo.InvariantCulture, $"  ITG-020 protocol shape violations: {protocolViolations.Count}");
        foreach (string violation in protocolViolations)
        {
            _ = text.AppendLine(CultureInfo.InvariantCulture, $"      {violation}");
        }

        _ = text.AppendLine(CultureInfo.InvariantCulture, $"  ITG-021 secret material in logs   : {secretLeaks.Count}");
        foreach (string leak in secretLeaks)
        {
            _ = text.AppendLine(CultureInfo.InvariantCulture, $"      {leak}");
        }

        _ = text.AppendLine(CultureInfo.InvariantCulture, $"  ITG-022 observability defects      : {observabilityDefects.Count}");
        foreach (string defect in observabilityDefects)
        {
            _ = text.AppendLine(CultureInfo.InvariantCulture, $"      {defect}");
        }

        _ = text.AppendLine("  ITG-023 typed operations exercised, by specification section:");
        foreach (KeyValuePair<string, int> entry in operationsBySection.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            _ = text.AppendLine(CultureInfo.InvariantCulture, $"      {entry.Key}: {entry.Value}");
        }
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
