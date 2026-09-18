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
    /// Every <c>efficiency.*</c> fixture this slice makes green: slice d's four plus slice e's
    /// three — the two it authors (<c>cursor-passthrough</c>, <c>topics-limit</c>) and
    /// <c>cache-version.304-not-modified</c>, which existed on disk undriven since it was authored
    /// ahead of <c>Sys.CacheVersion</c>.
    /// </summary>
    private static readonly string[] Green =
    [
        "efficiency.batch.per-op-errors",
        "efficiency.batch.too-large-client-side",
        "efficiency.cache-version.304-not-modified",
        "efficiency.cache-version.topics-limit",
        "efficiency.pagination.cursor-passthrough",
        "efficiency.rategate.fifo-throughput",
        "efficiency.rategate.pause-on-429",
    ];

    /// <summary>
    /// The one <c>efficiency.*</c> fixture that stays <c>pending</c> after slice e (D-M2-10: a
    /// pending fixture's owner is the milestone that lands its <b>operation</b>). Its operation is
    /// <c>Pki.ListCertificatesInfo</c>, not <c>Sys.ListNamespacesInfo</c> or
    /// <c>Auth.Userpass.ListUsersInfo</c> — D-M8-7 wires only those two areas in M8, so this
    /// fixture cannot be honestly driven without building the Pki area itself. Its
    /// <c>BV-PROTOCOL-002</c> requirement (PAG-005) is already demonstrated on the two areas M8
    /// does wire (see <c>efficiency.pagination.cursor-passthrough</c>'s sibling unit coverage in
    /// <c>EfficiencyUnitTests</c>); this fixture stays pending for the Pki listing itself, owned by
    /// whichever milestone builds <c>Pki.ListCertificatesInfo</c> (M9, per the roadmap).
    /// </summary>
    private static readonly Dictionary<string, string> Pending = new(StringComparer.Ordinal)
    {
        ["efficiency.pagination.zip-mismatch-protocol-error"] = "Pki.ListCertificatesInfo is M9 (D-M8-7 wires only Sys and Userpass in M8)",
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
    public void The_seven_slice_e_efficiency_fixtures_are_green_and_the_pending_list_is_exhaustive_and_reasoned()
    {
        FixtureRepository repository = new();
        FixtureDriver driver = Driver();

        Assert.All(Green, id => Assert.Equal(FixtureRunStatus.Passed, driver.Run(repository.LoadById(id)).Status));
        Assert.Equal(7, Green.Length);

        IReadOnlyList<string> ids = LoadIds();
        Assert.All(ids, id => Assert.True(
            Pending.ContainsKey(id) || Green.Contains(id, StringComparer.Ordinal),
            $"efficiency fixture '{id}' is neither green nor on the reasoned pending list."));
        Assert.All(Pending.Values, reason => Assert.NotEmpty(reason));

        // Appendix C names eight efficiency fixtures; all eight now exist on disk. Seven are
        // green; the eighth (`zip-mismatch-protocol-error`) is Pki's and stays pending per D-M8-7.
        Assert.Equal(8, ids.Count);
    }
}
