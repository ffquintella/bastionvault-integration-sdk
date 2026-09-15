using BastionVault.IntegrationSdk.Tests.Harness;
using BastionVault.IntegrationSdk.Tests.Harness.Operations;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// Runs every <c>sys.*</c> fixture (Appendix C) through the real
/// <see cref="BastionVault.IntegrationSdk.BastionVaultClient"/> (DR-0007, M3): health and status
/// (SYS-001, SYS-002, SYS-005, SYS-006, SYS-008) and self capability introspection
/// (SYS-050…SYS-053).
/// </summary>
public sealed class SysFixturesTests
{
    /// <summary>The ten fixtures M3 lands (D-M3-1, D-M3-2), including the three D-M3-5 authored this milestone.</summary>
    private static readonly string[] Green =
    [
        "sys.health.active",
        "sys.health.sealed",
        "sys.health.standby",
        "sys.health.uninitialized",
        "sys.seal-status.tn-swap",
        "sys.info.tiers",
        "sys.cluster-status.ok",
        "sys.cluster-status.forbidden",
        "sys.capabilities-self.v2-pinned",
        "sys.capabilities-self.namespace-not-operable",
    ];

    /// <summary>
    /// Every <c>sys.*</c> fixture that stays <c>pending</c> after M3, with the milestone that owns
    /// it. D-M3-1 scopes M3 to exactly six operations; the operations these six fixtures need
    /// (<c>Init</c>, <c>ListMounts</c>, <c>ListNamespacesInfo</c>, the legacy policy surface,
    /// <c>ReadPolicy</c>, <c>Remount</c>) are all later milestones, per D-M2-10's rule that a
    /// pending fixture's owner is the milestone that lands its <b>operation</b>.
    /// </summary>
    private static readonly Dictionary<string, string> Pending = new(StringComparer.Ordinal)
    {
        ["sys.init.validation"] = "Sys.Init is M7 (06-system-api.md §Initialisation, seal, unseal)",
        ["sys.mounts.two-fields"] = "Sys.ListMounts is M7 (06-system-api.md §Mounts)",
        ["sys.namespaces-info.page"] = "Sys.ListNamespacesInfo is M7 (06-system-api.md §Namespaces)",
        ["sys.policy.legacy-rules-field"] = "Sys.Legacy policy surface is M7 (06-system-api.md §Policies)",
        ["sys.policy.not-found"] = "Sys.ReadPolicy is M7 (06-system-api.md §Policies)",
        ["sys.remount.409-in-use"] = "Sys.Remount is M7 (06-system-api.md §Mounts)",
    };

    public static IEnumerable<object[]> Ids => LoadIds().Select(id => new object[] { id });

    private static IReadOnlyList<string> LoadIds()
    {
        FixtureRepository repository = new();
        return repository.EnumerateAll()
            .Where(fixture => fixture.Id.StartsWith("sys.", StringComparison.Ordinal))
            .Select(fixture => fixture.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }

    internal static FixtureDriver Driver()
    {
        OperationRegistry registry = new();
        ClientConstructOperation.Register(registry);
        LogicalFixtureOperations.Register(registry);
        SysFixtureOperations.Register(registry);
        return new FixtureDriver(registry);
    }

    [Theory]
    [Requirement("SYS-001")]
    [Requirement("SYS-002")]
    [Requirement("SYS-005")]
    [Requirement("SYS-006")]
    [Requirement("SYS-008")]
    [Requirement("SYS-050")]
    [Requirement("SYS-051")]
    [Requirement("SYS-052")]
    [Requirement("SYS-053")]
    [Requirement("TST-011")]
    [Trait("Requirement", "SYS-001")]
    [MemberData(nameof(Ids))]
    public void Sys_fixture_passes_against_real_sdk_code(string fixtureId)
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
    public void The_ten_M3_sys_fixtures_are_green_and_the_pending_list_is_exhaustive_and_reasoned()
    {
        FixtureRepository repository = new();
        FixtureDriver driver = Driver();

        Assert.All(Green, id => Assert.Equal(FixtureRunStatus.Passed, driver.Run(repository.LoadById(id)).Status));
        Assert.Equal(10, Green.Length);

        // Every sys.* fixture is accounted for: green, or pending with a stated reason.
        IReadOnlyList<string> ids = LoadIds();
        Assert.All(ids, id => Assert.True(
            Pending.ContainsKey(id) || Green.Contains(id, StringComparer.Ordinal),
            $"sys fixture '{id}' is neither green nor on the reasoned pending list."));
        Assert.All(Pending.Values, reason => Assert.NotEmpty(reason));

        // Exactly the six fixtures D-M3-1 scopes out of M3, all owned by M7.
        Assert.Equal(
            [
                "sys.init.validation",
                "sys.mounts.two-fields",
                "sys.namespaces-info.page",
                "sys.policy.legacy-rules-field",
                "sys.policy.not-found",
                "sys.remount.409-in-use",
            ],
            Pending.Keys.Order(StringComparer.Ordinal));
    }
}
