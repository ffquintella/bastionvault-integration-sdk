using BastionVault.IntegrationSdk.Tests.Harness;
using BastionVault.IntegrationSdk.Tests.Harness.Operations;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// Runs every <c>efficiency.*</c> fixture (Appendix C) through the real
/// <see cref="BastionVault.IntegrationSdk.BastionVaultClient"/>: section 14's client rate gate
/// (EFF-001…EFF-006), batch endpoint (BAT-001…BAT-008, slice d, M8d), cursor pagination
/// (PAG-001…PAG-005, slice e, M8e) and cache coherence (CCH-001…CCH-005, slice e, M8e).
/// </summary>
public sealed class EfficiencyFixturesTests
{
    /// <summary>
    /// Every <c>efficiency.*</c> fixture that is green: slice d's four plus slice e's three — the
    /// two it authors (<c>cursor-passthrough</c>, <c>topics-limit</c>) and
    /// <c>cache-version.304-not-modified</c>, which existed on disk undriven since it was authored
    /// ahead of <c>Sys.CacheVersion</c> — plus <c>zip-mismatch-protocol-error</c>, un-pended by
    /// D-M9-31 now that <c>Pki.ListCertificatesInfo</c> exists and pins <c>/v2</c> (DR-0013
    /// D-M8-48).
    /// </summary>
    private static readonly string[] Green =
    [
        "efficiency.batch.per-op-errors",
        "efficiency.batch.too-large-client-side",
        "efficiency.cache-version.304-not-modified",
        "efficiency.cache-version.topics-limit",
        "efficiency.pagination.cursor-passthrough",
        "efficiency.pagination.zip-mismatch-protocol-error",
        "efficiency.rategate.fifo-throughput",
        "efficiency.rategate.pause-on-429",
    ];

    /// <summary>
    /// No <c>efficiency.*</c> fixture is pending as of M9: <c>zip-mismatch-protocol-error</c>, the
    /// last one, is un-pended by D-M9-31 now that <c>Pki.ListCertificatesInfo</c> is built and
    /// pinned to <c>/v2</c>, discharging DR-0013 D-M8-48. Kept as a typed, empty dictionary rather
    /// than removed outright, so a future milestone that must re-pend a fixture has the pattern
    /// (D-M2-10: a pending fixture's owner is the milestone that lands its <b>operation</b>).
    /// </summary>
    private static readonly Dictionary<string, string> Pending = new(StringComparer.Ordinal);

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
        // D-M9-31 un-pends `efficiency.pagination.zip-mismatch-protocol-error`, driven by
        // `Pki.ListCertificatesInfo` now that M9 has built the Pki area.
        PkiFixtureOperations.Register(registry);
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
    public void All_eight_efficiency_fixtures_are_green_and_the_pending_list_is_exhaustive_and_reasoned()
    {
        FixtureRepository repository = new();
        FixtureDriver driver = Driver();

        Assert.All(Green, id => Assert.Equal(FixtureRunStatus.Passed, driver.Run(repository.LoadById(id)).Status));
        Assert.Equal(8, Green.Length);

        IReadOnlyList<string> ids = LoadIds();
        Assert.All(ids, id => Assert.True(
            Pending.ContainsKey(id) || Green.Contains(id, StringComparer.Ordinal),
            $"efficiency fixture '{id}' is neither green nor on the reasoned pending list."));
        Assert.All(Pending.Values, reason => Assert.NotEmpty(reason));

        // Appendix C names eight efficiency fixtures; all eight exist on disk, and all eight are
        // now green (D-M9-31 un-pends the last one, discharging DR-0013 D-M8-48).
        Assert.Equal(8, ids.Count);
    }
}
