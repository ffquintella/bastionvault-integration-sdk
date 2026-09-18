using BastionVault.IntegrationSdk.Tests.Harness;
using BastionVault.IntegrationSdk.Tests.Harness.Operations;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// Runs every <c>sys.*</c> fixture (Appendix C) through the real
/// <see cref="BastionVault.IntegrationSdk.BastionVaultClient"/> (DR-0007, M3): health and status
/// (SYS-001, SYS-002, SYS-005, SYS-006, SYS-008) and self capability introspection
/// (SYS-050…SYS-053), and M7a's initialisation/seal/unseal (SYS-010…SYS-013), mounts
/// (SYS-020…SYS-026) and auth methods (SYS-030).
/// </summary>
public sealed class SysFixturesTests
{
    /// <summary>
    /// Every <c>sys.*</c> fixture Appendix C line 123 names, all twenty green after M7b: M3's ten
    /// (D-M3-1, D-M3-2, D-M3-5), M7a's five (D-M7-9), and M7b's five — the three that were on disk
    /// and pending for want of a policy or namespace operation
    /// (<c>sys.policy.legacy-rules-field</c>, <c>sys.policy.not-found</c>,
    /// <c>sys.namespaces-info.page</c>) and the two D-M7-24 authored
    /// (<c>sys.policies.acl-read</c>, <c>sys.namespaces.write-full-replace</c>). Appendix C line
    /// 123's list is now closed: no <c>sys.*</c> fixture it names is absent from disk, and none on
    /// disk is pending.
    /// </summary>
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
        "sys.init.validation",
        "sys.mount.204",
        "sys.mounts.two-fields",
        "sys.namespaces-info.page",
        "sys.namespaces.write-full-replace",
        "sys.policies.acl-read",
        "sys.policy.legacy-rules-field",
        "sys.policy.not-found",
        "sys.remount.409-in-use",
        "sys.unseal.invalid-key",
    ];

    /// <summary>
    /// No <c>sys.*</c> fixture is pending after M7b. The map is kept — rather than deleted — so
    /// that the exhaustiveness assertion below still has both halves to check, and so slice c can
    /// add an entry beside a reason rather than reintroducing the mechanism (D-M2-10: a pending
    /// fixture's owner is the slice that lands its <b>operation</b>).
    /// </summary>
    private static readonly Dictionary<string, string> Pending = new(StringComparer.Ordinal);

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
    [Requirement("SYS-010")]
    [Requirement("SYS-012")]
    [Requirement("SYS-013")]
    [Requirement("SYS-020")]
    [Requirement("SYS-022")]
    [Requirement("SYS-023")]
    [Requirement("SYS-040")]
    [Requirement("SYS-050")]
    [Requirement("SYS-051")]
    [Requirement("SYS-052")]
    [Requirement("SYS-053")]
    [Requirement("SYS-060")]
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
    public void Every_sys_fixture_on_disk_is_green_and_the_pending_list_is_exhaustive_and_reasoned()
    {
        FixtureRepository repository = new();
        FixtureDriver driver = Driver();

        Assert.All(Green, id => Assert.Equal(FixtureRunStatus.Passed, driver.Run(repository.LoadById(id)).Status));
        Assert.Equal(20, Green.Length);

        // Every sys.* fixture is accounted for: green, or pending with a stated reason.
        IReadOnlyList<string> ids = LoadIds();
        Assert.All(ids, id => Assert.True(
            Pending.ContainsKey(id) || Green.Contains(id, StringComparer.Ordinal),
            $"sys fixture '{id}' is neither green nor on the reasoned pending list."));
        Assert.All(Pending.Values, reason => Assert.NotEmpty(reason));

        // M7b closes the list: nothing pending, and every id on disk is in Green.
        Assert.Empty(Pending);
        Assert.Equal(Green.Order(StringComparer.Ordinal), ids.Order(StringComparer.Ordinal));
    }
}
