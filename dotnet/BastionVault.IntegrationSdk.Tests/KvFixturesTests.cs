using BastionVault.IntegrationSdk.Tests.Harness;
using BastionVault.IntegrationSdk.Tests.Harness.Operations;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// Runs every <c>kv.*</c> fixture (Appendix C) through the real
/// <see cref="BastionVault.IntegrationSdk.BastionVaultClient"/> (DR-0009, M4a): KV v1 in full
/// (KV1-001…KV1-004) and KV v2's data, metadata, config, environment and path-helper surface
/// (KV2-001…KV2-030).
/// </summary>
public sealed class KvFixturesTests
{
    /// <summary>
    /// Every <c>kv.*</c> fixture, all green as of M8d. M4a landed nineteen of them and left
    /// <c>kv.read-many-batch</c> pending on <c>Sys.Batch</c>; slice d lands <c>Sys.Batch</c> and
    /// <c>Kv.ReadMany</c>, so that fixture joins the list and so does
    /// <c>kv.read-many-fallback-on-unsupported</c>, which D-M4-8 recorded as the unauthored
    /// twenty-first and which slice d authors.
    /// </summary>
    /// <remarks>
    /// The count history is kept because two earlier documents got it wrong and the wrong numbers
    /// are still in them: DR-0009's §"Verification required at handback" item 2 says "the 18 in
    /// M4's scope" and the M4a brief says 16, where the true M4a figure was 19 of the 20 files
    /// then on disk. M8d authors the 21st, so the figure is now <b>21</b>, and no <c>kv.*</c>
    /// fixture is pending.
    /// </remarks>
    private static readonly string[] Green =
    [
        "kv.read-many-batch",
        "kv.read-many-fallback-on-unsupported",
        "kv.v1.list",
        "kv.v1.read-with-lease",
        "kv.v1.write-empty-data-rejected",
        "kv.v2.config-environments",
        "kv.v2.destroy-then-read",
        "kv.v2.env-scoped-token-requires-env",
        "kv.v2.list-trailing-slash",
        "kv.v2.metadata-read",
        "kv.v2.read-env-merged",
        "kv.v2.read-env-strict-miss",
        "kv.v2.read-latest",
        "kv.v2.read-soft-deleted-state",
        "kv.v2.read-version-query",
        "kv.v2.soft-delete-versions",
        "kv.v2.undelete",
        "kv.v2.write-cas-mismatch",
        "kv.v2.write-cas-ok",
        "kv.v2.write-cas-required",
        "kv.v2.write-env-and-envs-rejected",
    ];

    /// <summary>
    /// Every <c>kv.*</c> fixture that stays <c>pending</c> after M4a, with the milestone that owns
    /// it (D-M2-10's rule: a pending fixture's owner is the milestone that lands its
    /// <b>operation</b>).
    /// </summary>
    private static readonly Dictionary<string, string> Pending = new(StringComparer.Ordinal);

    public static IEnumerable<object[]> Ids => LoadIds().Select(id => new object[] { id });

    private static IReadOnlyList<string> LoadIds()
    {
        FixtureRepository repository = new();
        return repository.EnumerateAll()
            .Where(fixture => fixture.Id.StartsWith("kv.", StringComparison.Ordinal))
            .Select(fixture => fixture.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }

    internal static FixtureDriver Driver()
    {
        OperationRegistry registry = new();
        ClientConstructOperation.Register(registry);
        LogicalFixtureOperations.Register(registry);
        SysFixtureOperations.Register(registry);
        KvFixtureOperations.Register(registry);
        return new FixtureDriver(registry);
    }

    [Theory]
    [Requirement("KV-002")]
    [Requirement("KV1-001")]
    [Requirement("KV1-003")]
    [Requirement("KV2-001")]
    [Requirement("KV2-002")]
    [Requirement("KV2-003")]
    [Requirement("KV2-004")]
    [Requirement("KV2-005")]
    [Requirement("KV2-006")]
    [Requirement("KV2-007")]
    [Requirement("KV2-008")]
    [Requirement("KV2-009")]
    [Requirement("KV2-010")]
    [Requirement("KV2-011")]
    [Requirement("KV2-020")]
    [Requirement("KV2-022")]
    [Requirement("TST-011")]
    [Trait("Requirement", "KV-002")]
    [MemberData(nameof(Ids))]
    public void Kv_fixture_passes_against_real_sdk_code(string fixtureId)
    {
        FixtureRepository repository = new();
        FixtureDocument fixture = repository.LoadById(fixtureId);

        FixtureRunResult result = Driver().Run(fixture);

        Assert.Equal(
            Pending.ContainsKey(fixtureId) ? FixtureRunStatus.Pending : FixtureRunStatus.Passed,
            result.Status);
    }

    [Fact]
    [Requirement("TST-011")]
    [Requirement("TST-013")]
    [Trait("Requirement", "TST-013")]
    public void Every_kv_fixture_is_green_and_nothing_is_left_pending()
    {
        FixtureRepository repository = new();
        FixtureDriver driver = Driver();

        Assert.All(Green, id => Assert.Equal(FixtureRunStatus.Passed, driver.Run(repository.LoadById(id)).Status));
        Assert.Equal(21, Green.Length);

        IReadOnlyList<string> ids = LoadIds();
        Assert.All(ids, id => Assert.True(
            Pending.ContainsKey(id) || Green.Contains(id, StringComparer.Ordinal),
            $"kv fixture '{id}' is neither green nor on the reasoned pending list."));
        Assert.All(Pending.Values, reason => Assert.NotEmpty(reason));

        // Nothing is deferred any more: D-M4-2's one pending fixture depended on Sys.Batch, which
        // M8d lands.
        Assert.Empty(Pending);

        // D-M4-8's 21st fixture, kv.read-many-fallback-on-unsupported, is authored by M8d.
        Assert.Contains("kv.read-many-fallback-on-unsupported", ids);
        Assert.Equal(21, ids.Count);
    }
}
