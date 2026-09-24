using BastionVault.IntegrationSdk.Tests.Harness;
using BastionVault.IntegrationSdk.Tests.Harness.Operations;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// Runs the <c>identity.*</c> fixtures (Appendix C) through the real
/// <see cref="BastionVault.IntegrationSdk.BastionVaultClient"/>: <c>identity.sharing.target-base64url</c>
/// (M10 slice a, DR-0017) and <c>identity.self</c>, captured against a live <c>bvault</c> 0.44.5
/// server and closing the R-35 gap (D-M10-4, D-M12-5 follow-up).
/// </summary>
public sealed class IdentityFixturesTests
{
    private static readonly string[] Green =
    [
        "identity.self",
        "identity.sharing.target-base64url",
    ];

    public static IEnumerable<object[]> Ids => LoadIds().Select(id => new object[] { id });

    private static IReadOnlyList<string> LoadIds()
    {
        FixtureRepository repository = new();
        return repository.EnumerateAll()
            .Where(fixture => fixture.Id.StartsWith("identity.", StringComparison.Ordinal))
            .Select(fixture => fixture.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }

    internal static FixtureDriver Driver()
    {
        OperationRegistry registry = new();
        ClientConstructOperation.Register(registry);
        IdentityFixtureOperations.Register(registry);
        return new FixtureDriver(registry);
    }

    [Theory]
    [Requirement("IDN-001")]
    [Requirement("TST-011")]
    [Trait("Requirement", "IDN-001")]
    [MemberData(nameof(Ids))]
    public void Identity_fixture_passes_against_real_sdk_code(string fixtureId)
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
    public void The_one_identity_fixture_is_green_and_the_list_is_exhaustive()
    {
        FixtureRepository repository = new();
        FixtureDriver driver = Driver();

        Assert.All(Green, id => Assert.Equal(FixtureRunStatus.Passed, driver.Run(repository.LoadById(id)).Status));
        Assert.Equal(2, Green.Length);

        IReadOnlyList<string> ids = LoadIds();
        Assert.Equal(Green.OrderBy(id => id, StringComparer.Ordinal), ids);
    }
}
