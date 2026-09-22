namespace BastionVault.IntegrationSdk.IntegrationTests.Harness;

/// <summary>
/// The base class every integration scenario derives from. It exists so that ITG-010's "unique
/// prefix, deleted in teardown, even on failure" is what a scenario gets for free, and leaking is
/// what would take extra effort.
/// <para>
/// What a derived class is handed: <see cref="Server"/> (ITG-001), a root <see cref="Client"/> of
/// its own, and <see cref="Resources"/>, a ledger already scoped to
/// <c>it-&lt;run-id&gt;-&lt;test-class&gt;</c>. What it must not do: touch <c>secret/</c>,
/// <c>resources/</c>, <c>files/</c> or <c>identity/</c> other than read-only (ITG-010).
/// </para>
/// </summary>
public abstract class IntegrationTest : IAsyncLifetime
{
    private IAsyncDisposable? gateHold;
    private IntegrationHarness.Context? context;

    /// <summary>Exclusive access to the server (ITG-012's <c>serial</c>). See <see cref="SerialIntegrationTest"/>.</summary>
    protected virtual bool RunsSerially => false;

    /// <summary>ITG-001's shared server object.</summary>
    protected TestServer Server => Context.Server;

    /// <summary>A root-token client owned by this test class and disposed with it.</summary>
    protected BastionVaultClient Client { get; private set; } = null!;

    /// <summary>This test class's isolated, self-deleting resource scope (ITG-010, ITG-011).</summary>
    protected ResourceLedger Resources { get; private set; } = null!;

    internal IntegrationHarness.Context Context =>
        context ?? throw new InvalidOperationException("the harness is only available after InitializeAsync");

    /// <summary>
    /// ITG-031: skip this scenario, with the mandated <c>requires server &gt;= X</c> reason, when
    /// the live server predates the endpoint under test. The skip is counted in the run report.
    /// </summary>
    protected void RequireServerVersion(string minimumVersion)
    {
        VersionGate.Require(Context, GetType().Name, minimumVersion);
    }

    /// <summary>
    /// A unique name in this test's namespace, for anything the ledger has no typed helper for.
    /// Every name a scenario invents MUST come from here or from <see cref="Resources"/>.
    /// </summary>
    protected string Unique(string label)
    {
        return Resources.Name(label);
    }

    public virtual async Task InitializeAsync()
    {
        using CancellationTokenSource startup = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        context = await IntegrationHarness.GetAsync(startup.Token).ConfigureAwait(false);

        gateHold = RunsSerially
            ? await Context.Gate.AcquireExclusiveAsync(startup.Token).ConfigureAwait(false)
            : await Context.Gate.AcquireSharedAsync(startup.Token).ConfigureAwait(false);

        Client = Server.CreateClient();
        Resources = new ResourceLedger(Client, $"it-{Server.RunId}-{ResourceLedger.Sanitise(GetType().Name)}");
    }

    public virtual async Task DisposeAsync()
    {
        try
        {
            if (Resources is not null)
            {
                await Resources.DisposeAsync().ConfigureAwait(false);
                if (Resources.TeardownFailures.Count > 0)
                {
                    Context.Report.RecordTeardownFailure(GetType().Name, Resources.TeardownFailures);
                }
            }
        }
        finally
        {
            Client?.Dispose();
            if (gateHold is not null)
            {
                await gateHold.DisposeAsync().ConfigureAwait(false);
                gateHold = null;
            }
        }
    }
}

/// <summary>
/// ITG-012's <c>serial</c> marker: a scenario that mutates server-wide state - seal/unseal,
/// <c>sys/dos/config</c>, namespace quotas, <c>require_machine_identity</c> - derives from this
/// instead of <see cref="IntegrationTest"/> and is the only test running while it holds the gate.
/// </summary>
public abstract class SerialIntegrationTest : IntegrationTest
{
    protected sealed override bool RunsSerially => true;
}
