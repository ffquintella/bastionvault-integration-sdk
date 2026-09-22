
namespace BastionVault.IntegrationSdk.IntegrationTests.Harness;

/// <summary>
/// The one shared thing in the suite: the server, the matrix, the serial gate and the run report.
/// <para>
/// It is a process-wide lazy rather than an xUnit collection fixture because a collection fixture
/// shared by every class would put every class in one collection, and xUnit parallelises
/// <i>between</i> collections - ITG-012 would be lost to the very mechanism meant to share the
/// server. Teardown is driven by <see cref="IntegrationTestFramework"/>, which the runner disposes
/// after the assembly finishes, with a process-exit hook behind it so an aborted run cannot leave
/// a <c>bvault</c> process alive.
/// </para>
/// </summary>
internal static class IntegrationHarness
{
    private static readonly SemaphoreSlim Lock = new(1, 1);
    private static Context? current;
    private static bool exitHookInstalled;

    internal sealed record Context(
        TestServer Server,
        TestMatrix Matrix,
        SerialGate Gate,
        RunReport Report,
        RunCapture Capture,
        bool Conformant)
    {
        public ManagedServer? Owned { get; init; }
    }

    public static async Task<Context> GetAsync(CancellationToken cancellationToken)
    {
        if (current is not null)
        {
            return current;
        }

        await Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return current ??= await StartAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = Lock.Release();
        }
    }

    private static async Task<Context> StartAsync(CancellationToken cancellationToken)
    {
        if (!ServerAvailability.Current.IsAvailable)
        {
            // Defence in depth. IntegrationFactAttribute skips at discovery, so reaching here means
            // something bypassed the attribute; fail rather than invent a server.
            throw new InvalidOperationException(ServerAvailability.UnavailableReason);
        }

        TestMatrix matrix = TestMatrix.Load();
        string runId = TestServer.NewRunId();
        RunReport report = new RunReport();

        (TestServer? server, ManagedServer? owned, IReadOnlyList<string>? warnings) = await TestServerProvisioner.ProvisionAsync(matrix, runId, cancellationToken)
            .ConfigureAwait(false);

        InstallExitHook();

        foreach (string warning in warnings)
        {
            report.RecordWarning(warning);
        }

        bool conformant = VersionGate.Evaluate(server, matrix, report);

        // ITG-021/023's whole-run state. Tracking the root token and unseal keys here, before any
        // client exists, means even the orphan sweep below is covered by ITG-021.
        RunCapture capture = new();
        capture.Secrets.Track(server.RootToken, "root token");
        for (int i = 0; i < server.UnsealKeys.Count; i++)
        {
            capture.Secrets.Track(server.UnsealKeys[i], $"unseal key {i + 1}");
        }

        LogCapture orphanLogs = new();
        RequestCapture orphanRequests = new(capture.Sections);
        OrphanCleaner.Result orphans;
        using (BastionVaultClient client = server.CreateClient(o =>
        {
            o.Logger = orphanLogs;
            o.Observer = orphanRequests;
        }))
        {
            orphans = await OrphanCleaner.RunAsync(client, cancellationToken).ConfigureAwait(false);
        }

        report.Banner(server, matrix, orphans, conformant);

        // ITG-002 is enforced after the banner, never before it: the operator must be told which
        // version was resolved even - especially - on the run that then refuses to continue.
        VersionGate.Enforce(server, matrix, conformant);

        // ITG-020/021/022 apply to the harness's own bookkeeping calls too. Throwing here fails
        // whichever test's InitializeAsync triggered this start-up (xUnit sees a real exception,
        // unlike a process exit code set later - see RunAssertions).
        RunAssertions.Enforce("orphan sweep", orphanRequests.Events, capture.Secrets, orphanLogs.Lines, report);

        return new Context(server, matrix, new SerialGate(), report, capture, conformant) { Owned = owned };
    }

    private static void InstallExitHook()
    {
        if (exitHookInstalled)
        {
            return;
        }

        exitHookInstalled = true;

        // The end of the run. It is also the only end of the run: xUnit 2.9.3 exposes no virtual
        // dispose on ITestFramework or its executor (see AssemblyInfo.cs), so this is where the
        // summary is emitted and where an owned server is killed. Installed for external mode too
        // - that run owns no server, but it still owes ITG-031 its skip count.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => ShutdownAsync().GetAwaiter().GetResult();
    }

    public static async Task ShutdownAsync()
    {
        Context? context = current;
        current = null;
        if (context is null)
        {
            return;
        }

        // ITG-023 is a summary, not an assertion (D-M12-15 Ruling C): recorded once, here, from
        // the tally every scope's RunAssertions.Enforce call fed as it ran.
        context.Report.RecordOperationsBySection(context.Capture.Sections.Counts);
        context.Report.Final();
        if (context.Owned is not null)
        {
            await context.Owned.DisposeAsync().ConfigureAwait(false);
        }
    }
}
