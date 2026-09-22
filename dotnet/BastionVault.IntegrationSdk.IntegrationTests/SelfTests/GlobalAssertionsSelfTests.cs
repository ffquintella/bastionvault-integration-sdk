using BastionVault.IntegrationSdk.IntegrationTests.Harness;

namespace BastionVault.IntegrationSdk.IntegrationTests.SelfTests;

/// <summary>
/// Server-free proof that ITG-020/021/022/023's detection logic actually catches what it claims
/// to, the same way <see cref="HarnessLogicTests"/> proves ITG-002/013 without a live server.
/// Each "flags a violation" test seeds one on purpose and shows it caught; each paired "clean"
/// test shows the same input, minus the seed, reporting nothing - the revert half of the proof.
/// </summary>
public sealed class GlobalAssertionsSelfTests
{
    [Fact]
    public void Itg021_flags_a_log_line_that_carries_a_tracked_secret()
    {
        SecretWatch secrets = new();
        secrets.Track(new SecretString("s.a-real-looking-root-token"), "root token");
        LogCapture logs = new();
        logs.Info("normal informational line");
        logs.Warn("BastionVault server warning: something happened");
        // The seeded violation: a debug line that leaked the tracked secret verbatim.
        logs.Info("oops, forgot to redact: token=s.a-real-looking-root-token");

        IReadOnlyList<string> leaks = secrets.FindLeaks(logs.Lines);

        _ = Assert.Single(leaks);
        Assert.Contains("root token", leaks[0], StringComparison.Ordinal);
        Assert.DoesNotContain("a-real-looking-root-token", leaks[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Itg021_finds_nothing_once_the_leaking_line_is_reverted()
    {
        SecretWatch secrets = new();
        secrets.Track(new SecretString("s.a-real-looking-root-token"), "root token");
        LogCapture logs = new();
        logs.Info("normal informational line");
        logs.Warn("BastionVault server warning: something happened");

        Assert.Empty(secrets.FindLeaks(logs.Lines));
    }

    [Fact]
    public void Itg020_flags_an_unrecognised_response_shape()
    {
        RequestEvent bad = new("GET", "sys/health", "", 200, TimeSpan.FromMilliseconds(5), "req-1", 1, ErrorCodes.ProtocolUnexpectedResponse);
        RequestEvent good = new("GET", "sys/health", "", 200, TimeSpan.FromMilliseconds(5), "req-2", 1, null);

        IReadOnlyList<string> violations = RunAssertions.ProtocolViolations([good, bad]);

        _ = Assert.Single(violations);
        Assert.Contains("sys/health", violations[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Itg020_finds_nothing_when_every_response_was_recognised()
    {
        RequestEvent good = new("GET", "sys/health", "", 200, TimeSpan.FromMilliseconds(5), "req-2", 1, null);

        Assert.Empty(RunAssertions.ProtocolViolations([good]));
    }

    [Fact]
    public void Itg022_flags_an_event_missing_a_mandated_field()
    {
        RequestEvent missingPath = new("GET", "", "", 200, TimeSpan.FromMilliseconds(1), "req", 1, null);
        RequestEvent missingStatusAndCode = new("POST", "sys/mounts/x", "", null, TimeSpan.FromMilliseconds(1), "req", 1, null);
        RequestEvent ok = new("GET", "sys/health", "", 200, TimeSpan.FromMilliseconds(1), "req", 1, null);

        IReadOnlyList<string> defects = RunAssertions.ObservabilityDefects([missingPath, missingStatusAndCode, ok]);

        Assert.Equal(2, defects.Count);
    }

    [Fact]
    public void Itg022_finds_nothing_when_every_event_carries_its_fields()
    {
        RequestEvent ok = new("GET", "sys/health", "", 200, TimeSpan.FromMilliseconds(1), "req", 1, null);
        RequestEvent errored = new("GET", "sys/seal-status", "", null, TimeSpan.FromMilliseconds(1), "req", 1, "BV-TRANSPORT-001");

        Assert.Empty(RunAssertions.ObservabilityDefects([ok, errored]));
    }

    [Fact]
    public void Itg023_tally_classifies_by_static_prefix_and_registered_mount()
    {
        SectionTally tally = new();
        tally.RegisterMount("it-xyz-kv", "kv-v2");

        tally.Record("GET", "sys/health");
        tally.Record("GET", "sys/health");
        tally.Record("GET", "it-xyz-kv/data/foo");
        tally.Record("GET", "auth/token/lookup-self");
        tally.Record("GET", "unknown-root/thing");

        Assert.Equal(1, tally.Counts["06-system-api"]);
        Assert.Equal(1, tally.Counts["07-kv-engine"]);
        Assert.Equal(1, tally.Counts["05-authentication"]);
        Assert.Equal(1, tally.Counts["unclassified"]);
    }
}
