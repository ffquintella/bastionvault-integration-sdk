using BastionVault.IntegrationSdk.Tests.Harness;
using BastionVault.IntegrationSdk.Tests.Harness.Operations;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// Runs every <c>pki.*</c> fixture (Appendix C) through the real
/// <see cref="BastionVault.IntegrationSdk.BastionVaultClient"/> (M9 slice a, DR-0016): the three
/// fixtures this slice authors and drives (CNF-015).
/// </summary>
public sealed class PkiFixturesTests
{
    private static readonly string[] Green =
    [
        "pki.certs-info-page",
        "pki.issue",
        "pki.role-not-found",
    ];

    public static IEnumerable<object[]> Ids => LoadIds().Select(id => new object[] { id });

    private static IReadOnlyList<string> LoadIds()
    {
        FixtureRepository repository = new();
        return repository.EnumerateAll()
            .Where(fixture => fixture.Id.StartsWith("pki.", StringComparison.Ordinal))
            .Select(fixture => fixture.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }

    internal static FixtureDriver Driver()
    {
        OperationRegistry registry = new();
        ClientConstructOperation.Register(registry);
        PkiFixtureOperations.Register(registry);
        return new FixtureDriver(registry);
    }

    [Theory]
    [Requirement("PKI-001")]
    [Requirement("PKI-002")]
    [Requirement("PKI-010")]
    [Requirement("PKI-020")]
    [Requirement("TST-011")]
    [Trait("Requirement", "PKI-001")]
    [MemberData(nameof(Ids))]
    public void Pki_fixture_passes_against_real_sdk_code(string fixtureId)
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
    public void The_three_pki_fixtures_are_green_and_the_list_is_exhaustive()
    {
        FixtureRepository repository = new();
        FixtureDriver driver = Driver();

        Assert.All(Green, id => Assert.Equal(FixtureRunStatus.Passed, driver.Run(repository.LoadById(id)).Status));
        Assert.Equal(3, Green.Length);

        IReadOnlyList<string> ids = LoadIds();
        Assert.Equal(Green.OrderBy(id => id, StringComparer.Ordinal), ids);
    }
}
