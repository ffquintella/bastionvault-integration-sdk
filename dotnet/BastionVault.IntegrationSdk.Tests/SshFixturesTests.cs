using BastionVault.IntegrationSdk.Tests.Harness;
using BastionVault.IntegrationSdk.Tests.Harness.Operations;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// Runs every <c>ssh.*</c> and <c>sshbroker.*</c> fixture (Appendix C) through the real
/// <see cref="BastionVault.IntegrationSdk.BastionVaultClient"/> (M9 slice d, DR-0016): the three
/// <c>ssh.*</c> fixtures this slice authors and drives, plus <c>sshbroker.effective-v2-pinned</c>,
/// which was already on disk (D-M9-4) but undriven until this slice registers
/// <c>SshBroker.Effective</c> (CNF-015).
/// </summary>
public sealed class SshFixturesTests
{
    private static readonly string[] Green =
    [
        "ssh.creds-ip-not-allowed",
        "ssh.sign",
        "ssh.verify-invalid-otp",
        "sshbroker.effective-v2-pinned",
    ];

    public static IEnumerable<object[]> Ids => LoadIds().Select(id => new object[] { id });

    private static IReadOnlyList<string> LoadIds()
    {
        FixtureRepository repository = new();
        return repository.EnumerateAll()
            .Where(fixture => fixture.Id.StartsWith("ssh.", StringComparison.Ordinal) || fixture.Id.StartsWith("sshbroker.", StringComparison.Ordinal))
            .Select(fixture => fixture.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }

    internal static FixtureDriver Driver()
    {
        OperationRegistry registry = new();
        ClientConstructOperation.Register(registry);
        SshFixtureOperations.Register(registry);
        return new FixtureDriver(registry);
    }

    [Theory]
    [Requirement("TRN-031")]
    [Requirement("ERR-020")]
    [Requirement("SSB-001")]
    [Requirement("TST-011")]
    [Trait("Requirement", "TST-011")]
    [MemberData(nameof(Ids))]
    public void Ssh_fixture_passes_against_real_sdk_code(string fixtureId)
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
    public void The_four_ssh_fixtures_are_green_and_the_list_is_exhaustive()
    {
        FixtureRepository repository = new();
        FixtureDriver driver = Driver();

        Assert.All(Green, id => Assert.Equal(FixtureRunStatus.Passed, driver.Run(repository.LoadById(id)).Status));
        Assert.Equal(4, Green.Length);

        IReadOnlyList<string> ids = LoadIds();
        Assert.Equal(Green.OrderBy(id => id, StringComparer.Ordinal), ids);
    }
}
