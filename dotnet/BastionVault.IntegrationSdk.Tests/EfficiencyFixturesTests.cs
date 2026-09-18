using BastionVault.IntegrationSdk.Tests.Harness;
using BastionVault.IntegrationSdk.Tests.Harness.Operations;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// Runs every <c>efficiency.*</c> fixture (Appendix C) through the real
/// <see cref="BastionVault.IntegrationSdk.BastionVaultClient"/> (DR-0013 slice d, M8d): section
/// 14's client rate gate (EFF-001…EFF-006) and batch endpoint (BAT-001…BAT-008).
/// </summary>
public sealed class EfficiencyFixturesTests
{
    /// <summary>
    /// The four <c>efficiency.*</c> fixtures slice d makes green: the two <c>rategate</c> ones and
    /// <c>batch.per-op-errors</c> it authors, plus <c>batch.too-large-client-side</c>, which has
    /// existed since M0 and has been <b>unreachable</b> ever since — no driver in any language
    /// registered <c>Sys.Batch</c>, so it reported <c>pending</c> rather than failing.
    /// </summary>
    private static readonly string[] Green =
    [
        "efficiency.batch.per-op-errors",
        "efficiency.batch.too-large-client-side",
        "efficiency.rategate.fifo-throughput",
        "efficiency.rategate.pause-on-429",
    ];

    /// <summary>
    /// The <c>efficiency.*</c> fixtures that stay <c>pending</c> after slice d, with the milestone
    /// that owns them (D-M2-10: a pending fixture's owner is the milestone that lands its
    /// <b>operation</b>). Both belong to slice e, which lands <c>PAG-*</c> and <c>CCH-*</c>.
    /// </summary>
    private static readonly Dictionary<string, string> Pending = new(StringComparer.Ordinal)
    {
        ["efficiency.cache-version.304-not-modified"] = "Sys.CacheVersion (CCH-001…CCH-003) is M8e",
        ["efficiency.pagination.zip-mismatch-protocol-error"] = "the *-info pages (PAG-005) are M8e",
    };

    public static IEnumerable<object[]> Ids => LoadIds().Select(id => new object[] { id });

    private static IReadOnlyList<string> LoadIds()
    {
        FixtureRepository repository = new();
        return repository.EnumerateAll()
            .Where(fixture => fixture.Id.StartsWith("efficiency.", StringComparison.Ordinal))
            .Select(fixture => fixture.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }

    internal static FixtureDriver Driver()
    {
        OperationRegistry registry = new();
        ClientConstructOperation.Register(registry);
        LogicalFixtureOperations.Register(registry);
        // `efficiency.rategate.fifo-throughput` drives the gate through BAT-007's fallback, which
        // is a `Kv.ReadMany`; slice e's pagination fixtures will need the Sys family the same way.
        KvFixtureOperations.Register(registry);
        SysFixtureOperations.Register(registry);
        EfficiencyFixtureOperations.Register(registry);
        return new FixtureDriver(registry, Pending);
    }

    [Theory]
    [Requirement("EFF-001")]
    [Requirement("EFF-002")]
    [Requirement("EFF-003")]
    [Requirement("EFF-006")]
    [Requirement("BAT-001")]
    [Requirement("BAT-002")]
    [Requirement("BAT-003")]
    [Requirement("BAT-004")]
    [Requirement("BAT-005")]
    [Requirement("TST-011")]
    [Trait("Requirement", "EFF-001")]
    [MemberData(nameof(Ids))]
    public void Efficiency_fixture_passes_against_real_sdk_code(string fixtureId)
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
    public void The_four_slice_d_efficiency_fixtures_are_green_and_the_pending_list_is_exhaustive_and_reasoned()
    {
        FixtureRepository repository = new();
        FixtureDriver driver = Driver();

        Assert.All(Green, id => Assert.Equal(FixtureRunStatus.Passed, driver.Run(repository.LoadById(id)).Status));
        Assert.Equal(4, Green.Length);

        IReadOnlyList<string> ids = LoadIds();
        Assert.All(ids, id => Assert.True(
            Pending.ContainsKey(id) || Green.Contains(id, StringComparer.Ordinal),
            $"efficiency fixture '{id}' is neither green nor on the reasoned pending list."));
        Assert.All(Pending.Values, reason => Assert.NotEmpty(reason));

        // Appendix C names eight efficiency fixtures; six exist on disk today. The two Appendix C
        // names with no file — `efficiency.pagination.cursor-passthrough` and
        // `efficiency.cache-version.topics-limit` — are slice e's to author, and are asserted
        // absent here so authoring them cannot slip past this list unnoticed.
        Assert.DoesNotContain("efficiency.pagination.cursor-passthrough", ids);
        Assert.DoesNotContain("efficiency.cache-version.topics-limit", ids);
        Assert.Equal(6, ids.Count);
    }
}
