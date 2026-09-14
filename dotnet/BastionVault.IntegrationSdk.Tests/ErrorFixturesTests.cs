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
    /// The two fixtures that stay <c>pending</c> after M1c, each with a named owning milestone.
    /// They are listed, not deleted or edited to fit (D-M1c-10, CLA-004): the driver reports them
    /// pending because the typed operation they drive is not registered yet.
    /// </summary>
    private static readonly HashSet<string> Pending = new(StringComparer.Ordinal)
    {
        // Drives Auth.Token.Lookup — M2 (D-M1c-10).
        "errors.format.one-line",
        // Needs the Sys.ListMounts cache to know the mount is KV v2 — M4 (D-M1c-5).
        "errors.enrichment.404-kv2-hint",
        // ERR-022 is a typed-layer guard: the fixture drives Kv.V2.ReadSecret — M2. The logical
        // layer has no typed operation to catch a missing token before, so this fixture was
        // already pending at M1b and stays pending rather than being edited to fit (CLA-004).
        "errors.recognition.missing-token-client-side",
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
        FixtureDriver driver = new(registry);

        FixtureRunResult result = driver.Run(fixture);

        Assert.Equal(
            Pending.Contains(fixtureId) ? FixtureRunStatus.Pending : FixtureRunStatus.Passed,
            result.Status);
    }

    [Fact]
    [Requirement("FIX-001")]
    [Trait("Requirement", "FIX-001")]
    public void Every_recognition_rule_has_a_fixture_and_only_two_stay_pending()
    {
        IReadOnlyList<string> ids = LoadIds();

        // 124 generated + 4 hand-authored recognition + 5 enrichment + 1 format = 134.
        Assert.Equal(134, ids.Count);
        Assert.Equal(124, ids.Count(id => id.StartsWith("errors.recognition.bv-", StringComparison.Ordinal)));
        Assert.Equal(5, ids.Count(id => id.StartsWith("errors.enrichment.", StringComparison.Ordinal)));
        Assert.All(Pending, id => Assert.Contains(id, ids));
    }
}
