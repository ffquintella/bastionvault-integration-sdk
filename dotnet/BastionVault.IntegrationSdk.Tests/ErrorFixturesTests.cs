using BastionVault.IntegrationSdk.Tests.Harness;
using BastionVault.IntegrationSdk.Tests.Harness.Operations;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// Runs every <c>errors.*</c> fixture (Appendix C) through the real
/// <see cref="BastionVault.IntegrationSdk.BastionVaultClient"/>: the generated
/// <c>errors.recognition.*</c> set (one per Appendix B §2 rule, D-M1c-10), the five hand-authored
/// <c>errors.enrichment.*</c> fixtures, and the four <c>errors.recognition.*</c> fixtures that
/// predate the generator.
/// </summary>
public sealed class ErrorFixturesTests
{
    /// <summary>
    /// <b>Empty since M7c.</b> Every <c>errors.*</c> fixture now runs against real SDK code.
    /// </summary>
    /// <remarks>
    /// <c>errors.enrichment.404-kv2-hint</c> was the last entry. DR-0009 D-M4-14 re-booked it from
    /// M4 to M7 for two reasons and both are discharged: SYS-026's mount-type cache landed in M7a,
    /// and the fixture itself — which named <c>Kv.V2.ReadSecret</c> on a route with no <c>data/</c>
    /// segment, a shape KV2-001 makes impossible for any <c>Kv.V2.*</c> call — has been re-authored
    /// against <c>Kv.V1.Read</c>, the v1-shaped read on a v2 mount that is the realistic user error
    /// ERR-040's row addresses (DR-0012 D-M7-43). The mechanism is kept rather than deleted: it is
    /// the D-M2-10 seam a future milestone will need for its own deferred row.
    /// </remarks>
    private static readonly Dictionary<string, string> Pending = new(StringComparer.Ordinal);

    public static IEnumerable<object[]> Ids => LoadIds().Select(id => new object[] { id });

    private static IReadOnlyList<string> LoadIds()
    {
        FixtureRepository repository = new();
        return repository.EnumerateAll()
            .Where(fixture => fixture.Id.StartsWith("errors.", StringComparison.Ordinal))
            .Select(fixture => fixture.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }

    [Theory]
    [Requirement("ERR-020")]
    [Requirement("ERR-021")]
    [Requirement("ERR-035")]
    [Requirement("ERR-040")]
    [Requirement("CNF-043")]
    [Trait("Requirement", "ERR-020")]
    [MemberData(nameof(Ids))]
    public void Error_fixture_passes_against_real_sdk_code(string fixtureId)
    {
        FixtureRepository repository = new();
        FixtureDocument fixture = repository.LoadById(fixtureId);
        OperationRegistry registry = new();
        ClientConstructOperation.Register(registry);
        LogicalFixtureOperations.Register(registry);
        // M2a registers Auth.Token.Lookup, which is what errors.format.one-line drives.
        AuthFixtureOperations.Register(registry);
        // M4b: errors.recognition.missing-token-client-side drives Kv.V2.ReadSecret.
        // M7c: errors.enrichment.404-kv2-hint drives Kv.V1.Read, whose 404 consults the SYS-026
        // mount-type cache — so the fixture's second exchange is that lookup, not a second read.
        KvFixtureOperations.Register(registry);
        FixtureDriver driver = new(registry, Pending);

        FixtureRunResult result = driver.Run(fixture);

        Assert.Equal(
            Pending.ContainsKey(fixtureId) ? FixtureRunStatus.Pending : FixtureRunStatus.Passed,
            result.Status);
    }

    [Fact]
    [Requirement("FIX-001")]
    [Trait("Requirement", "FIX-001")]
    public void Every_recognition_rule_has_a_fixture_and_none_stays_pending_after_M7c()
    {
        IReadOnlyList<string> ids = LoadIds();

        // 130 generated + 4 hand-authored recognition + 5 enrichment + 1 format = 140.
        // D-M8-3 (R-23): 130 = 124 + the six new single-alternative fixtures the generator
        // fix authors, one per alternative of a qualifier group (bv-input-102.2,
        // bv-input-103.{3,4,5}, bv-auth-011.3, bv-ssh-005.2, bv-transit-004.2). The old
        // one-fixture-per-rule messages carried every alternative at once, so they passed
        // under the conjunctive reading too.
        Assert.Equal(140, ids.Count);
        Assert.Equal(130, ids.Count(id => id.StartsWith("errors.recognition.bv-", StringComparison.Ordinal)));
        Assert.Equal(5, ids.Count(id => id.StartsWith("errors.enrichment.", StringComparison.Ordinal)));
        Assert.All(Pending.Keys, id => Assert.Contains(id, ids));
        // M7c: the last deferred errors.* fixture is green, so nothing here is held.
        Assert.Empty(Pending);
    }
}
