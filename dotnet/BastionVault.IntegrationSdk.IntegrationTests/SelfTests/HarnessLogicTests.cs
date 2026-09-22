using BastionVault.IntegrationSdk.IntegrationTests.Harness;

namespace BastionVault.IntegrationSdk.IntegrationTests.SelfTests;

/// <summary>
/// Server-free tests of the harness's own decision logic. Plain <c>[Fact]</c> on purpose: the
/// version comparison ITG-002 turns on, and the mount-dating rule ITG-013 turns on, must be
/// checkable on a machine with no BastionVault at all.
/// </summary>
public sealed class HarnessLogicTests
{
    [Theory]
    [InlineData("0.38.3", "0.42.0", false)]
    [InlineData("0.42.0", "0.42.0", true)]
    [InlineData("0.42.1", "0.42.0", true)]
    [InlineData("1.0.0", "0.42.0", true)]
    [InlineData("0.42.0-rc1", "0.42.0", false)]
    [InlineData("v0.43.0", "0.42.0", true)]
    public void Version_comparison_decides_support(string actual, string minimum, bool supported)
    {
        Assert.True(ServerVersion.TryParse(actual, out ServerVersion? parsed));
        Assert.Equal(supported, parsed >= ServerVersion.Parse(minimum));
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("main")]
    [InlineData("")]
    [InlineData("not-a-version")]
    public void Moving_tags_are_not_versions(string text)
    {
        Assert.False(ServerVersion.TryParse(text, out _));
    }

    [Fact]
    public void Matrix_minimum_is_read_from_the_specification()
    {
        TestMatrix matrix = TestMatrix.Load();

        Assert.Equal(1, matrix.ManagedServer.InitShares);
        Assert.Equal(1, matrix.ManagedServer.InitThreshold);
        Assert.Equal(TimeSpan.FromSeconds(60), matrix.ManagedServer.StartupTimeout);
        Assert.Equal("file", matrix.ManagedServer.Storage);
        Assert.Equal(200, matrix.ManagedServer.DosDefaults.MaxRequests);
    }

    [Fact]
    public void Run_id_round_trips_its_clock()
    {
        string runId = TestServer.NewRunId();
        long? epoch = TestServer.EpochFromRunId(runId);

        _ = Assert.NotNull(epoch);
        Assert.InRange(
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() - epoch!.Value,
            0,
            60);
    }

    [Fact]
    public void Orphan_dating_prefers_the_path_then_the_description()
    {
        string fresh = $"it-{TestServer.NewRunId()}-someclass-scratch";
        _ = Assert.NotNull(OrphanCleaner.AgeOf(fresh, description: null));

        // Old run id in the path: 2021-01-01.
        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(1_609_459_200),
            OrphanCleaner.AgeOf("it-" + ToBase36(1_609_459_200) + "abcd-x", null));

        // Path unusable, description carries the stamp.
        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(1_609_459_200),
            OrphanCleaner.AgeOf("it-zzz", "integration test; created=1609459200"));

        // Neither: left alone rather than deleted.
        Assert.Null(OrphanCleaner.AgeOf("it-zzz", "no stamp here"));

        static string ToBase36(long value)
        {
            const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
            Stack<char> chars = new Stack<char>();
            while (value > 0)
            {
                chars.Push(Alphabet[(int)(value % 36)]);
                value /= 36;
            }

            return new string([.. chars]);
        }
    }

    [Fact]
    public void Version_gate_refuses_a_server_below_the_matrix_minimum()
    {
        // ITG-002's loud outcome, made observable without an unsupported server to hand: the
        // installed binary was 0.38.3 while M12 was being written and is 0.44.5 now, so the
        // refusal path can no longer be demonstrated by running the suite. It is demonstrated
        // here instead, against the real matrix file.
        TestMatrix matrix = TestMatrix.Load();
        RunReport report = new();

        TestServer old = Fake("0.38.3");
        Assert.False(VersionGate.Evaluate(old, matrix, report));

        ServerVersionNotSupportedException refusal = Assert.Throws<ServerVersionNotSupportedException>(
            () => VersionGate.Enforce(old, matrix, conformant: false));
        Assert.Contains("0.38.3", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(matrix.MinimumVersion.ToString(), refusal.Message, StringComparison.Ordinal);

        // A supported server passes the same gate untouched.
        TestServer current = Fake("0.44.5");
        Assert.True(VersionGate.Evaluate(current, matrix, report));
        VersionGate.Enforce(current, matrix, conformant: true);
    }

    [Fact]
    public void Unparseable_server_version_is_not_treated_as_supported()
    {
        TestMatrix matrix = TestMatrix.Load();
        RunReport report = new();

        Assert.False(VersionGate.Evaluate(Fake("unknown"), matrix, report));
    }

    [Fact]
    public void Itg003_reason_string_is_the_specified_one()
    {
        Assert.Equal("no BastionVault test server available", ServerAvailability.UnavailableReason);
    }

    private static TestServer Fake(string version)
    {
        return new TestServer(
            TestServerMode.External,
            "http://127.0.0.1:1",
            new SecretString("not-a-real-token"),
            caCertPem: null,
            version,
            @namespace: null,
            tlsSkipVerify: false,
            [],
            TestServer.NewRunId());
    }
}
