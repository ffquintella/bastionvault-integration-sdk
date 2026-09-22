using BastionVault.IntegrationSdk.IntegrationTests.Harness;

namespace BastionVault.IntegrationSdk.IntegrationTests.SelfTests;

/// <summary>
/// Tests of the <b>harness</b>, not of the SDK. These are deliberately not ITG-S scenarios
/// (DR-0019 D-M12-1 gives those to slices 2-6): they exist so that the contract slices 2-6 build
/// on - a live server, an isolated resource scope, a teardown that runs on failure, a counted
/// version skip - is demonstrated rather than asserted.
/// </summary>
public sealed class HarnessSelfTests : IntegrationTest
{
    [IntegrationFact]
    public async Task Live_server_answers_sys_health_through_the_sdk()
    {
        HealthStatus health = await Client.Sys.HealthAsync();

        Assert.Equal(HealthState.Active, health.State);
        Assert.False(health.Sealed);
        Assert.True(health.Initialized);
        Assert.Equal(200, health.StatusCode);

        // ITG-001: the server object carries the resolved version, and printing it cannot leak
        // the root token because SecretString is what holds it.
        Assert.False(string.IsNullOrWhiteSpace(Server.Version));
        Assert.Contains("REDACTED", Server.RootToken.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [IntegrationFact]
    public async Task Ledger_mount_is_uniquely_prefixed_and_visible_on_the_server()
    {
        string path = await Resources.MountAsync("kv-v2", "scratch");

        Assert.StartsWith($"it-{Server.RunId}-", path, StringComparison.Ordinal);

        IReadOnlyDictionary<string, MountInfo> mounts = await Client.Sys.ListMountsAsync();
        Assert.Contains(mounts, m => m.Key.TrimEnd('/') == path);
    }

    [IntegrationFact]
    public async Task Default_mounts_are_readable_without_being_touched()
    {
        // ITG-010's read-only clause: the harness never writes to secret/, resources/, files/ or
        // identity/, and this is the check that the defaults are present to read.
        IReadOnlyDictionary<string, MountInfo> mounts = await Client.Sys.ListMountsAsync();

        Assert.Contains(mounts, m => m.Key.TrimEnd('/') == "secret");
        Assert.DoesNotContain(mounts, m => m.Key.StartsWith("it-", StringComparison.Ordinal) && !m.Key.Contains(Server.RunId, StringComparison.Ordinal));
    }

    [IntegrationFact]
    public void Version_gate_skips_a_scenario_that_needs_a_newer_server()
    {
        // ITG-031: the skip reason is exact and the skip is counted in the run report.
        RequireServerVersion("99.0.0");

        Assert.Fail("unreachable: RequireServerVersion must have skipped this test");
    }

    [IntegrationFact]
    public async Task Teardown_runs_even_when_the_test_fails()
    {
        // Acceptance criterion 4, run on demand: BASTIONVAULT_TEST_PROVE_FAILURE_TEARDOWN=1 makes
        // this test create a mount and then fail. Its mount must still be gone afterwards. It is
        // opt-in because a suite that always contains a failing test cannot be green.
        Skip.IfNot(
            Environment.GetEnvironmentVariable("BASTIONVAULT_TEST_PROVE_FAILURE_TEARDOWN") == "1",
            "set BASTIONVAULT_TEST_PROVE_FAILURE_TEARDOWN=1 to exercise the failure-teardown path");

        string path = await Resources.MountAsync("kv-v2", "failure-teardown");
        Assert.Fail($"deliberate failure with mount '{path}' still mounted");
    }
}

/// <summary>A second class, so the parallel path (two collections, one server) is exercised.</summary>
public sealed class HarnessParallelSelfTests : IntegrationTest
{
    [IntegrationFact]
    public async Task Second_class_gets_its_own_prefix_against_the_same_server()
    {
        string path = await Resources.MountAsync("kv-v2", "scratch");

        Assert.Contains("harnessparallelselftests", path, StringComparison.Ordinal);
        IReadOnlyDictionary<string, MountInfo> mounts = await Client.Sys.ListMountsAsync();
        Assert.Contains(mounts, m => m.Key.TrimEnd('/') == path);
    }
}

/// <summary>The <c>serial</c> side of ITG-012: exclusive while it holds the gate.</summary>
public sealed class HarnessSerialSelfTests : SerialIntegrationTest
{
    [IntegrationFact]
    public async Task Serial_scenario_runs_with_exclusive_access()
    {
        SealStatus status = await Client.Sys.SealStatusAsync();

        Assert.False(status.Sealed);
        Assert.NotEmpty(Server.UnsealKeys);
    }
}
