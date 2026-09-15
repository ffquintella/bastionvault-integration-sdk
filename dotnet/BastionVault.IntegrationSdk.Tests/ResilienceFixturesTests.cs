using BastionVault.IntegrationSdk.Tests.Harness;
using BastionVault.IntegrationSdk.Tests.Harness.Operations;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// Runs every <c>resilience.*</c> fixture (Appendix C line 133) through the real
/// <see cref="BastionVault.IntegrationSdk.BastionVaultClient"/> (DR-0010, M5a): address
/// classification, SRV discovery, health probing, node ranking and the diagnostics table.
/// </summary>
public sealed class ResilienceFixturesTests
{
    /// <summary>
    /// All ten fixtures Appendix C line 133 names: the four that were on disk before M5, the four
    /// M5a authored, and the two M5b authored (D-M5-4). Nothing in this family is pending.
    /// </summary>
    private static readonly string[] Green =
    [
        "resilience.address.classification",
        "resilience.srv.sorted-and-verbatim-underscore",
        "resilience.probe.classify",
        "resilience.pick.leader-over-follower-rtt-weight",
        "resilience.pick.priority-floor",
        "resilience.pick.none-healthy",
        "resilience.failover.read-once",
        "resilience.failover.write-never",
        "resilience.failover.not-armed-single-candidate",
        "resilience.backoff.math-seeded",
    ];

    public static IEnumerable<object[]> Ids => LoadIds().Select(id => new object[] { id });

    private static IReadOnlyList<string> LoadIds()
    {
        FixtureRepository repository = new();
        return repository.EnumerateAll()
            .Where(fixture => fixture.Id.StartsWith("resilience.", StringComparison.Ordinal))
            .Select(fixture => fixture.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }

    internal static FixtureDriver Driver()
    {
        OperationRegistry registry = new();
        ClientConstructOperation.Register(registry);
        LogicalFixtureOperations.Register(registry);
        // The failover fixtures drive Kv.V2.ReadSecret/WriteSecret, and the backoff fixture drives
        // Logical.Read: the failover contract is asserted through ordinary operations, which is the
        // point — every operation in the SDK goes through the loop M5b changed.
        KvFixtureOperations.Register(registry);
        ResilienceFixtureOperations.Register(registry);
        return new FixtureDriver(registry);
    }

    [Theory]
    [Requirement("DSC-001")]
    [Requirement("DSC-010")]
    [Requirement("DSC-012")]
    [Requirement("DSC-013")]
    [Requirement("DSC-020")]
    [Requirement("DSC-021")]
    [Requirement("DSC-022")]
    [Requirement("DSC-030")]
    [Requirement("DSC-031")]
    [Requirement("DSC-032")]
    [Requirement("DSC-033")]
    [Requirement("DSC-034")]
    [Requirement("DSC-035")]
    [Requirement("DSC-036")]
    [Requirement("RES-021")]
    [Requirement("DSC-041")]
    [Requirement("DSC-042")]
    [Requirement("DSC-043")]
    [Requirement("DSC-044")]
    [Requirement("TST-011")]
    [Trait("Requirement", "DSC-001")]
    [MemberData(nameof(Ids))]
    public void Resilience_fixture_passes_against_real_sdk_code(string fixtureId)
    {
        FixtureRepository repository = new();
        FixtureDocument fixture = repository.LoadById(fixtureId);

        FixtureRunResult result = Driver().Run(fixture);

        Assert.Equal(FixtureRunStatus.Passed, result.Status);
    }

    [Fact]
    [Requirement("TST-011")]
    [Requirement("TST-013")]
    [Trait("Requirement", "TST-013")]
    public void All_ten_resilience_fixtures_are_green_and_none_is_pending()
    {
        FixtureRepository repository = new();
        FixtureDriver driver = Driver();

        Assert.All(Green, id => Assert.Equal(FixtureRunStatus.Passed, driver.Run(repository.LoadById(id)).Status));
        Assert.Equal(0, driver.PendingCount);

        // Appendix C line 133's list is fully realised and every id on disk is green, so a fixture
        // added later cannot slip past this family unrun. `resilience.failover.read-once` is green
        // as of D-M5-26, which repaired the one field its third response body was missing:
        // section 07:83 declares the metadata object as {version, created_time, deletion_time,
        // destroyed} and D-M4-12 makes an absent `created_time` a BV-PROTOCOL-002, so the fixture —
        // authored before M4 — contradicted the specification it encodes.
        IReadOnlyList<string> ids = LoadIds();
        Assert.Equal(10, ids.Count);
        Assert.Equal(ids.Order(StringComparer.Ordinal), Green.Order(StringComparer.Ordinal));
    }
}
