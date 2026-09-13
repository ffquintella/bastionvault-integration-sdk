using BastionVault.IntegrationSdk.Tests.Harness;
using BastionVault.IntegrationSdk.Tests.Harness.Operations;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// Runs every mandatory <c>transport.*</c> fixture (Appendix C) through the real
/// <see cref="BastionVault.IntegrationSdk.BastionVaultClient"/> and <see cref="BastionVault.IntegrationSdk.LogicalOperations"/>
/// (<c>decisions/0004-m1b-transport.md</c>, M1b).
/// </summary>
public sealed class TransportFixturesTests
{
    private static readonly string[] FixtureIds =
    [
        "transport.envelope.200-empty-body",
        "transport.envelope.204",
        "transport.envelope.non-json",
        "transport.envelope.shape-b",
        "transport.headers.authenticated-read",
        "transport.headers.login-omits-token",
        "transport.headers.namespace",
        "transport.headers.reserved-rejected",
        "transport.method.list-verb",
        "transport.retry.connection-refused-then-ok",
        "transport.retry.write-not-retried",
        "transport.status.307-not-followed",
        "transport.status.404-empty-read-returns-null",
        "transport.status.405-empty",
        "transport.status.429-dos-guard",
        "transport.status.429-namespace-quota",
        "transport.status.503-sealed",
        "transport.url.encoding-vectors",
    ];

    public static IEnumerable<object[]> Ids => FixtureIds.Select(id => new object[] { id });

    [Theory]
    [Requirement("TRN-001")]
    [Requirement("TST-011")]
    [Trait("Requirement", "TRN-001")]
    [MemberData(nameof(Ids))]
    public void Transport_fixture_passes_against_real_sdk_code(string fixtureId)
    {
        FixtureRepository repository = new();
        FixtureDocument fixture = repository.LoadById(fixtureId);
        OperationRegistry registry = new();
        ClientConstructOperation.Register(registry);
        LogicalFixtureOperations.Register(registry);
        FixtureDriver driver = new(registry);

        FixtureRunResult result = driver.Run(fixture);

        Assert.Equal(FixtureRunStatus.Passed, result.Status);
    }

    [Fact]
    [Requirement("TST-011")]
    [Trait("Requirement", "TST-011")]
    public void All_eighteen_mandatory_transport_fixtures_are_covered()
    {
        Assert.Equal(18, FixtureIds.Length);
        Assert.Equal(FixtureIds.Length, FixtureIds.Distinct(StringComparer.Ordinal).Count());
    }
}
