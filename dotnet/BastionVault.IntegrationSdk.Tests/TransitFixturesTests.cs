using BastionVault.IntegrationSdk.Tests.Harness;
using BastionVault.IntegrationSdk.Tests.Harness.Operations;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// Runs every <c>transit.*</c> fixture (Appendix C) through the real
/// <see cref="BastionVault.IntegrationSdk.BastionVaultClient"/> (DR-0013 slice b): the two fixtures
/// that predate this slice (<c>transit.ciphertext-format-client-side</c>,
/// <c>transit.unknown-key-500-mapped</c>, both already driven through <c>Logical.*</c>/generic
/// wiring) plus the three this slice authors.
/// </summary>
public sealed class TransitFixturesTests
{
    public static IEnumerable<object[]> Ids => LoadIds().Select(id => new object[] { id });

    private static IReadOnlyList<string> LoadIds()
    {
        FixtureRepository repository = new();
        return repository.EnumerateAll()
            .Where(fixture => fixture.Id.StartsWith("transit.", StringComparison.Ordinal))
            .Select(fixture => fixture.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }

    internal static FixtureDriver Driver()
    {
        OperationRegistry registry = new();
        ClientConstructOperation.Register(registry);
        LogicalFixtureOperations.Register(registry);
        TransitFixtureOperations.Register(registry);
        return new FixtureDriver(registry);
    }

    [Theory]
    [Requirement("TRS-002")]
    [Requirement("TRS-003")]
    [Requirement("TRS-010")]
    [Requirement("TRS-011")]
    [Requirement("TRS-013")]
    [Requirement("TST-011")]
    [Trait("Requirement", "TRS-002")]
    [MemberData(nameof(Ids))]
    public void Transit_fixture_passes_against_real_sdk_code(string fixtureId)
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
    public void All_five_appendix_C_transit_fixtures_exist_and_are_green()
    {
        IReadOnlyList<string> ids = LoadIds();

        Assert.Equal(
            new[]
            {
                "transit.below-min-decryption",
                "transit.ciphertext-format-client-side",
                "transit.encrypt-decrypt",
                "transit.random-cap",
                "transit.unknown-key-500-mapped",
            },
            ids);
    }
}
