using System.Text.Json;
using BastionVault.IntegrationSdk;
using BastionVault.IntegrationSdk.Tests.Harness;
using BastionVault.IntegrationSdk.Tests.Harness.Operations;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// The two D-M2-7 harness instruments, each proven by a seeded violation. R-10 and DR-0001 both
/// say it: an instrument that has never failed has not been tested, and this project has shipped
/// four gates that were trusted on their record and were doing less than they claimed.
/// </summary>
/// <remarks>
/// These are the <i>standing</i> proofs — they seed the violation inside the test, so the proof is
/// re-run on every build rather than recorded once in a delegation report and then rotting. The
/// one-off seeded violations against the real library code (breaking <c>RemainingTtl</c>; logging a
/// fixture token) are recorded in the M2a handback; these tests are what keeps them honest.
/// </remarks>
public sealed class FixtureInstrumentTests
{
    [Fact]
    [Requirement("TST-011")]
    [Trait("Requirement", "TST-011")]
    public void A_fixture_that_declares_a_clock_fails_when_the_operation_ignores_it()
    {
        // The exact failure mode that hid auth.token.lookup-self-remaining-ttl since M0: the
        // fixture declares a clock, the driver injects nothing, the operation never asks the time,
        // and the fixture passes while asserting nothing about the scripted instant.
        FixtureDocument fixture = Synthetic(
            """
            {
              "id": "synthetic.clock-ignored",
              "title": "Clock declared but never read",
              "requirements": ["TST-011"],
              "level": "core",
              "sections": ["05"],
              "clock": {"start": "2026-09-13T12:00:00Z"},
              "operation": {"name": "Synthetic.IgnoresClock"},
              "exchanges": [],
              "expect": {"result": {"value": "ok"}}
            }
            """);
        OperationRegistry registry = new();
        registry.Register("Synthetic.IgnoresClock", _ => ValueTask.FromResult(
            new FixtureOperationResult(new Dictionary<string, object?>(StringComparer.Ordinal) { ["value"] = "ok" })));

        FixtureAssertionException exception = Assert.Throws<FixtureAssertionException>(
            () => new FixtureDriver(registry).Run(fixture));

        Assert.Contains("never read the injected clock", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("TST-011")]
    [Trait("Requirement", "TST-011")]
    public void A_fixture_that_declares_a_clock_passes_when_the_operation_reads_the_scripted_time()
    {
        FixtureDocument fixture = Synthetic(
            """
            {
              "id": "synthetic.clock-honoured",
              "title": "Clock start and advances are honoured",
              "requirements": ["TST-011"],
              "level": "core",
              "sections": ["05"],
              "clock": {"start": "2026-09-13T12:00:00Z", "advance": ["PT30S", "PT1M"]},
              "operation": {"name": "Synthetic.ReadsClock"},
              "exchanges": [
                {"expectRequest": {"method": "GET", "url": "https://fixture.invalid/1"}, "respond": {"status": 200}},
                {"expectRequest": {"method": "GET", "url": "https://fixture.invalid/2"}, "respond": {"status": 200}}
              ],
              "expect": {
                "result": {
                  "start": "2026-09-13T12:00:00.0000000+00:00",
                  "afterFirst": "2026-09-13T12:00:30.0000000+00:00",
                  "afterSecond": "2026-09-13T12:01:30.0000000+00:00"
                }
              }
            }
            """);
        OperationRegistry registry = new();
        registry.Register("Synthetic.ReadsClock", invocation =>
        {
            IClock clock = invocation.Instruments.Clock;
            string start = clock.NowUtc().ToString("O");
            invocation.Transport.SendAsync(FixtureRequest.Create("GET", "https://fixture.invalid/1")).GetAwaiter().GetResult();
            string afterFirst = clock.NowUtc().ToString("O");
            invocation.Transport.SendAsync(FixtureRequest.Create("GET", "https://fixture.invalid/2")).GetAwaiter().GetResult();
            string afterSecond = clock.NowUtc().ToString("O");
            return ValueTask.FromResult(new FixtureOperationResult(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["start"] = start,
                ["afterFirst"] = afterFirst,
                ["afterSecond"] = afterSecond,
            }));
        });

        Assert.Equal(FixtureRunStatus.Passed, new FixtureDriver(registry).Run(fixture).Status);
    }

    [Fact]
    [Requirement("TST-051")]
    [Trait("Requirement", "TST-051")]
    public void A_fixture_run_that_logs_a_fixture_token_goes_red()
    {
        FixtureDocument fixture = Synthetic(LeakFixtureJson("synthetic.leak-log"));
        OperationRegistry registry = new();
        registry.Register("Synthetic.Leaks", invocation =>
        {
            // The seeded violation: a debug-ish line carrying the live token, which is exactly what
            // TST-051 exists to catch. .NET's CNF-030 logger seam has one level (Warn), so the
            // seed uses it; the assertion searches every captured line regardless of level.
            invocation.Instruments.Logger.Warn($"resolved token {FakeTokens.Client} for this request");
            return ValueTask.FromResult(new FixtureOperationResult(
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["value"] = "ok" }));
        });

        FixtureAssertionException exception = Assert.Throws<FixtureAssertionException>(
            () => new FixtureDriver(registry).Run(fixture));

        Assert.Contains("TST-051", exception.Message, StringComparison.Ordinal);
        Assert.Contains("leaked secret material", exception.Message, StringComparison.Ordinal);
        // The failure message itself does not reprint the secret in full (CNF-031).
        Assert.DoesNotContain(FakeTokens.Client, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("TST-051")]
    [Requirement("CFG-080")]
    [Trait("Requirement", "TST-051")]
    public void A_fixture_run_whose_observer_event_carries_a_token_goes_red()
    {
        // D-M2-7 names the observer specifically: RequestEvent.Path is where AUT-080's
        // `auth/token/renew/{token}` leaks. This seeds that exact shape.
        FixtureDocument fixture = Synthetic(LeakFixtureJson("synthetic.leak-observer"));
        OperationRegistry registry = new();
        registry.Register("Synthetic.Leaks", invocation =>
        {
            invocation.Instruments.Observer.OnRequestCompleted(new RequestEvent(
                Method: "POST",
                Path: $"auth/token/renew/{FakeTokens.Client}",
                Namespace: string.Empty,
                StatusCode: 200,
                Duration: TimeSpan.Zero,
                RequestId: "id",
                Attempt: 1,
                ErrorCode: null));
            return ValueTask.FromResult(new FixtureOperationResult(
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["value"] = "ok" }));
        });

        FixtureAssertionException exception = Assert.Throws<FixtureAssertionException>(
            () => new FixtureDriver(registry).Run(fixture));

        Assert.Contains("TST-051", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("TST-051")]
    [Trait("Requirement", "TST-051")]
    public void A_fixture_run_that_surfaces_nothing_secret_passes()
    {
        FixtureDocument fixture = Synthetic(LeakFixtureJson("synthetic.no-leak"));
        OperationRegistry registry = new();
        registry.Register("Synthetic.Leaks", invocation =>
        {
            invocation.Instruments.Logger.Warn("resolved token [REDACTED] for this request");
            invocation.Instruments.Observer.OnRequestCompleted(new RequestEvent(
                "POST", "auth/token/renew/<redacted>", string.Empty, 200, TimeSpan.Zero, "id", 1, null));
            return ValueTask.FromResult(new FixtureOperationResult(
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["value"] = "ok" }));
        });

        Assert.Equal(FixtureRunStatus.Passed, new FixtureDriver(registry).Run(fixture).Status);
    }

    [Fact]
    [Requirement("TST-050")]
    [Trait("Requirement", "TST-050")]
    public void The_harvester_finds_tokens_passwords_and_secret_ids_wherever_a_fixture_spells_them()
    {
        FixtureDocument fixture = Synthetic(
            $$"""
            {
              "id": "synthetic.harvest",
              "title": "Secret harvesting",
              "requirements": ["TST-050"],
              "level": "core",
              "sections": ["05"],
              "client": {"token": "{{FakeTokens.Client}}"},
              "operation": {
                "name": "Synthetic.Harvest",
                "args": {"password": "password-fixture", "secret_id": "opaque-not-conventional"}
              },
              "exchanges": [],
              "expect": {"result": "$any"}
            }
            """);

        IReadOnlyList<string> secrets = FixtureSecrets.Harvest(fixture);

        Assert.Contains(FakeTokens.Client, secrets);
        Assert.Contains("password-fixture", secrets);
        // Caught by property name, not by convention: a fixture that spells a secret some other
        // way is still covered, which is what keeps the assertion from silently narrowing.
        Assert.Contains("opaque-not-conventional", secrets);
        // Non-secret strings are not harvested, or every assertion would trivially fail.
        Assert.DoesNotContain("Secret harvesting", secrets);
    }

    private static string LeakFixtureJson(string id) =>
        $$$"""
        {
          "id": "{{{id}}}",
          "title": "Seeded TST-051 violation",
          "requirements": ["TST-051"],
          "level": "core",
          "sections": ["05"],
          "client": {"token": "{{{FakeTokens.Client}}}"},
          "operation": {"name": "Synthetic.Leaks"},
          "exchanges": [],
          "expect": {"result": {"value": "ok"}}
        }
        """;

    private static FixtureDocument Synthetic(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return new FixtureDocument("synthetic.fixture.json", document.RootElement.Clone());
    }
}
