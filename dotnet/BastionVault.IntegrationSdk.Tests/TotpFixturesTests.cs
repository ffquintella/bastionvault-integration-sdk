using BastionVault.IntegrationSdk.Tests.Harness;
using BastionVault.IntegrationSdk.Tests.Harness.Operations;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// Runs every <c>totp.*</c> fixture (Appendix C) through the real
/// <see cref="BastionVault.IntegrationSdk.BastionVaultClient"/> (DR-0013 slice c, M8c): 11's whole
/// operations table, key CRUD plus code generation and validation (TOT-001…TOT-004).
/// </summary>
public sealed class TotpFixturesTests
{
    /// <summary>
    /// The three <c>totp.*</c> fixtures Appendix C names: the pre-existing
    /// <c>totp.wrong-mode</c> plus the two M8c authors (D-M8-c1).
    /// </summary>
    private static readonly string[] Green =
    [
        "totp.generate-mode-create",
        "totp.validate-false",
        "totp.wrong-mode",
    ];

    public static IEnumerable<object[]> Ids => LoadIds().Select(id => new object[] { id });

    private static IReadOnlyList<string> LoadIds()
    {
        FixtureRepository repository = new();
        return repository.EnumerateAll()
            .Where(fixture => fixture.Id.StartsWith("totp.", StringComparison.Ordinal))
            .Select(fixture => fixture.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }

    internal static FixtureDriver Driver()
    {
        OperationRegistry registry = new();
        ClientConstructOperation.Register(registry);
        TotpFixtureOperations.Register(registry);
        return new FixtureDriver(registry);
    }

    [Theory]
    [Requirement("TOT-001")]
    [Requirement("TOT-002")]
    [Requirement("TOT-003")]
    [Requirement("TOT-004")]
    [Requirement("TST-011")]
    [Trait("Requirement", "TOT-001")]
    [MemberData(nameof(Ids))]
    public void Totp_fixture_passes_against_real_sdk_code(string fixtureId)
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
    public void The_three_totp_fixtures_are_green_and_the_list_is_exhaustive()
    {
        FixtureRepository repository = new();
        FixtureDriver driver = Driver();

        Assert.All(Green, id => Assert.Equal(FixtureRunStatus.Passed, driver.Run(repository.LoadById(id)).Status));
        Assert.Equal(3, Green.Length);

        IReadOnlyList<string> ids = LoadIds();
        Assert.Equal(Green.OrderBy(id => id, StringComparer.Ordinal), ids);
    }
}
