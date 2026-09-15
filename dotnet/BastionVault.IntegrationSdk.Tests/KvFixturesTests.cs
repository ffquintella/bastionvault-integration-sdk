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
    /// The nineteen <c>kv.*</c> fixtures M4a lands: the fifteen that existed before this milestone
    /// less <c>kv.read-many-batch</c>, plus the five D-M4-8 authored with it.
    /// </summary>
    /// <remarks>
    /// DR-0009's §"Verification required at handback" item 2 says "the 18 in M4's scope", and the
    /// M4a brief says 16. Both counts are arithmetic slips: 20 <c>kv.*</c> files exist on disk,
    /// <c>kv.read-many-fallback-on-unsupported</c> was never authored (D-M4-8 records it as the
    /// unauthored 21st), and only <c>kv.read-many-batch</c> is out of scope, so the true figure is
    /// <b>19</b>. Recorded here rather than silently satisfying the smaller number.
    /// </remarks>
    private static readonly string[] Green =
    [
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
    private static readonly Dictionary<string, string> Pending = new(StringComparer.Ordinal)
    {
        ["kv.read-many-batch"] = "Kv.ReadMany (KV-010) needs Sys.Batch (BAT-007) and is M8 (D-M4-2)",
    };

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
    public void The_nineteen_M4a_kv_fixtures_are_green_and_the_pending_list_is_exhaustive_and_reasoned()
    {
        FixtureRepository repository = new();
        FixtureDriver driver = Driver();

        Assert.All(Green, id => Assert.Equal(FixtureRunStatus.Passed, driver.Run(repository.LoadById(id)).Status));
        Assert.Equal(19, Green.Length);

        IReadOnlyList<string> ids = LoadIds();
        Assert.All(ids, id => Assert.True(
            Pending.ContainsKey(id) || Green.Contains(id, StringComparer.Ordinal),
            $"kv fixture '{id}' is neither green nor on the reasoned pending list."));
        Assert.All(Pending.Values, reason => Assert.NotEmpty(reason));

        // Exactly the one fixture D-M4-2 defers, and it is owned by M8.
        Assert.Equal(["kv.read-many-batch"], Pending.Keys.Order(StringComparer.Ordinal));

        // D-M4-8's 21st fixture, kv.read-many-fallback-on-unsupported, was never authored. Asserted
        // so that authoring it later cannot slip past this list unnoticed.
        Assert.DoesNotContain("kv.read-many-fallback-on-unsupported", ids);
        Assert.Equal(20, ids.Count);
    }
}
