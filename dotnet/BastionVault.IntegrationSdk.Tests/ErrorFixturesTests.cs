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
    /// The one fixture that stays <c>pending</c> after M4b, with its owning milestone. It is
    /// listed, not deleted or edited to fit (D-M1c-10, CLA-004): the driver reports it pending
    /// because its fixture is internally inconsistent, not because its operation is unregistered.
    /// </summary>
    /// <remarks>
    /// <c>errors.recognition.missing-token-client-side</c> left this set in M4b: its driving
    /// operation, <c>Kv.V2.ReadSecret</c>, now exists, and it already asserts <c>BV-AUTH-001</c>
    /// at <c>attempts: 0</c>, which CFG-020/ERR-022 already implement.
    /// </remarks>
    private static readonly Dictionary<string, string> Pending = new(StringComparer.Ordinal)
    {
        // Re-booked from M4 to M7 by DR-0009's addendum, D-M4-14: needs the Sys.ListMounts cache
        // (SYS-026) for the mount-type check, which is M7's, and the fixture itself is internally
        // inconsistent (it names Kv.V2.ReadSecret but expects a route KV2-001 makes impossible for
        // any Kv.V2.* call) — Strategic's to re-author, not a delegate's to edit to fit (CLA-004).
        // Held explicitly (FixtureDriver's D-M2-10 mechanism) rather than left to an unregistered
        // operation: Kv.V2.ReadSecret is registered now, so the driver would otherwise actually
        // run it and fail on the fixture's own inconsistency instead of reporting it pending.
        ["errors.enrichment.404-kv2-hint"] = "Needs Sys.ListMounts (SYS-026) and fixture re-authoring — M7 (D-M4-14).",
    };

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
    public void Every_recognition_rule_has_a_fixture_and_only_one_stays_pending_after_M4b()
    {
        IReadOnlyList<string> ids = LoadIds();

        // 124 generated + 4 hand-authored recognition + 5 enrichment + 1 format = 134.
        Assert.Equal(134, ids.Count);
        Assert.Equal(124, ids.Count(id => id.StartsWith("errors.recognition.bv-", StringComparison.Ordinal)));
        Assert.Equal(5, ids.Count(id => id.StartsWith("errors.enrichment.", StringComparison.Ordinal)));
        Assert.All(Pending.Keys, id => Assert.Contains(id, ids));
    }
}
