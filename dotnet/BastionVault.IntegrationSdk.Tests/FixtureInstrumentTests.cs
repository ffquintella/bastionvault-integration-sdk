using System.Text.Json;
using BastionVault.IntegrationSdk;
using BastionVault.IntegrationSdk.Testing;
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
            _ = invocation.Transport.SendAsync(FixtureRequest.Create("GET", "https://fixture.invalid/1")).GetAwaiter().GetResult();
            string afterFirst = clock.NowUtc().ToString("O");
            _ = invocation.Transport.SendAsync(FixtureRequest.Create("GET", "https://fixture.invalid/2")).GetAwaiter().GetResult();
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

    [Theory]
    // D-M2-27 item 1's three schema cases. The first is the one that matters most and is the
    // easiest to lose: `"required": ["delay"]` inside the `if` is what stops the subschema from
    // being vacuously true on every fixture that declares no `delay` at all, which would silently
    // forbid `clock.advance` everywhere. No fixture combines `clock` with `advance` today, so
    // FIX-001 alone would not notice until someone authored the first one (plausibly at M13).
    [InlineData("""{"start": "2026-09-13T12:00:00Z", "advance": ["PT2S"]}""", true)]
    [InlineData("""{"start": "2026-09-13T12:00:00Z", "delay": "virtual", "advance": ["PT2S"], "expectWaits": ["PT1S"]}""", false)]
    [InlineData("""{"start": "2026-09-13T12:00:00Z", "delay": "virtual"}""", false)]
    [InlineData("""{"start": "2026-09-13T12:00:00Z", "delay": "virtual", "expectWaits": []}""", true)]
    [InlineData("""{"start": "2026-09-13T12:00:00Z", "nonsense": true}""", false)]
    [Requirement("FIX-001")]
    [Trait("Requirement", "FIX-001")]
    public void The_clock_schema_admits_exactly_the_combinations_D_M2_27_allows(string clockBlock, bool expectedValid)
    {
        string json = $$"""
            {
              "id": "synthetic.clock-schema",
              "title": "Clock schema case",
              "requirements": ["FIX-001"],
              "level": "core",
              "sections": ["05"],
              "clock": {{clockBlock}},
              "operation": {"name": "Synthetic.Any"},
              "exchanges": [],
              "expect": {"result": "$any"}
            }
            """;

        using JsonDocument document = JsonDocument.Parse(json);

        Assert.Equal(expectedValid, FixtureRepository.Schema.Evaluate(document.RootElement).IsValid);
    }

    [Fact]
    [Requirement("TST-011")]
    [Trait("Requirement", "TST-011")]
    public void A_virtual_clock_fixture_whose_waits_do_not_match_expectWaits_goes_red()
    {
        // D-M2-27 item 3: the fourth honour disjunct (`expectWaits` is present) is not a loophole,
        // because the element-wise assertion then makes a positive claim. This is that claim doing
        // the work: the operation reads the clock, so the honour guard alone would pass it.
        FixtureDocument fixture = Synthetic(VirtualFixtureJson("synthetic.waits-mismatch", """["PT10S", "PT20S"]"""));
        OperationRegistry registry = new();
        registry.Register("Synthetic.Waits", async invocation =>
        {
            IClock clock = invocation.Instruments.Clock;
            await clock.Delay(TimeSpan.FromSeconds(10), CancellationToken.None);
            return new FixtureOperationResult(new Dictionary<string, object?>(StringComparer.Ordinal) { ["value"] = "ok" });
        });

        FixtureAssertionException exception = Assert.Throws<FixtureAssertionException>(
            () => new FixtureDriver(registry).Run(fixture));

        Assert.Contains("expected 2 wait(s)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("TST-011")]
    [Trait("Requirement", "TST-011")]
    public void A_virtual_clock_fixture_whose_waits_match_expectWaits_passes_and_time_moved()
    {
        FixtureDocument fixture = Synthetic(VirtualFixtureJson("synthetic.waits-match", """["PT10S", "PT20S"]"""));
        OperationRegistry registry = new();
        registry.Register("Synthetic.Waits", async invocation =>
        {
            IClock clock = invocation.Instruments.Clock;
            DateTimeOffset start = clock.NowUtc();
            await clock.Delay(TimeSpan.FromSeconds(10), CancellationToken.None);
            await clock.Delay(TimeSpan.FromSeconds(20), CancellationToken.None);

            // Virtual mode's whole point: the wait is granted without waiting, and the clock the
            // code under test reads afterwards has moved by exactly what it asked for.
            Assert.Equal(TimeSpan.FromSeconds(30), clock.NowUtc() - start);
            return new FixtureOperationResult(new Dictionary<string, object?>(StringComparer.Ordinal) { ["value"] = "ok" });
        });

        Assert.Equal(FixtureRunStatus.Passed, new FixtureDriver(registry).Run(fixture).Status);
    }

    [Fact]
    [Requirement("TST-011")]
    [Trait("Requirement", "TST-011")]
    public void A_zero_wait_fixture_declaring_an_empty_expectWaits_is_honoured_without_reading_the_clock()
    {
        // AUT-095's shape: a batch or non-renewable token makes AutoRenew do nothing, so the
        // operation grants no wait and need not read the clock at all. D-M2-27 rejected a
        // three-disjunct honour guard precisely because it false-fails this fixture — and
        // `expectWaits: []` is still a positive claim, asserted below by the mismatch case.
        FixtureDocument fixture = Synthetic(VirtualFixtureJson("synthetic.no-waits", "[]"));
        OperationRegistry registry = new();
        registry.Register("Synthetic.Waits", _ => ValueTask.FromResult(
            new FixtureOperationResult(new Dictionary<string, object?>(StringComparer.Ordinal) { ["value"] = "ok" })));

        Assert.Equal(FixtureRunStatus.Passed, new FixtureDriver(registry).Run(fixture).Status);
    }

    [Fact]
    [Requirement("TST-011")]
    [Trait("Requirement", "TST-011")]
    public void A_spinning_operation_trips_the_grant_cap_even_when_it_swallows_the_exception()
    {
        // D-M2-27 item 2's post-run latch, proven against the exact shape that motivated it:
        // AUT-092 requires the renewal loop to *absorb* failures, so an operation that catches
        // everything and reports a tidy result is not hypothetical. Throwing alone would be
        // swallowed here; the latch is what still fails the run.
        FixtureDocument fixture = Synthetic(VirtualFixtureJson("synthetic.spin", """["PT1S"]"""));
        OperationRegistry registry = new();
        registry.Register("Synthetic.Waits", async invocation =>
        {
            IClock clock = invocation.Instruments.Clock;
            for (int index = 0; index < 200; index++)
            {
                try
                {
                    await clock.Delay(TimeSpan.FromSeconds(1), CancellationToken.None);
                }
                catch (FixtureAssertionException)
                {
                    // Swallowed on purpose: this is what AUT-092's failure handling does.
                }
            }

            return new FixtureOperationResult(new Dictionary<string, object?>(StringComparer.Ordinal) { ["value"] = "ok" });
        });

        FixtureAssertionException exception = Assert.Throws<FixtureAssertionException>(
            () => new FixtureDriver(registry).Run(fixture));

        Assert.Contains("was not surfaced by the operation", exception.Message, StringComparison.Ordinal);
        Assert.Contains("spin guard of 64 grants", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("TST-011")]
    [Trait("Requirement", "TST-011")]
    public void A_swallowed_cumulative_cap_trip_still_fails_the_run_even_though_the_waits_match()
    {
        // The case that isolates D-M2-27 item 2's latch from every other assertion. The fixture
        // declares exactly the wait its operation asks for, so `expectWaits` matches and the honour
        // guard is satisfied; the run must still fail, because the harness *refused* that wait —
        // 25 days is past the 24 h runaway backstop, which is the "author a short lease" signal.
        // Without the latch this fixture would go green while the instrument was screaming.
        FixtureDocument fixture = Synthetic(VirtualFixtureJson("synthetic.cumulative-cap", """["P25D"]"""));
        OperationRegistry registry = new();
        registry.Register("Synthetic.Waits", async invocation =>
        {
            try
            {
                await invocation.Instruments.Clock.Delay(TimeSpan.FromDays(25), CancellationToken.None);
            }
            catch (FixtureAssertionException)
            {
                // Swallowed, exactly as AUT-092's failure handling would swallow it.
            }

            return new FixtureOperationResult(new Dictionary<string, object?>(StringComparer.Ordinal) { ["value"] = "ok" });
        });

        FixtureAssertionException exception = Assert.Throws<FixtureAssertionException>(
            () => new FixtureDriver(registry).Run(fixture));

        Assert.Contains("was not surfaced by the operation", exception.Message, StringComparison.Ordinal);
        // The message names both bounds, so this reads as a fixture-authoring constraint (author a
        // shorter lease) rather than as a mystery failure — D-M2-27 item 2 asks for exactly that.
        Assert.Contains("64 grants / P1D cumulative", exception.Message, StringComparison.Ordinal);
        Assert.Contains("must author its own short `lease_duration`", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("TST-011")]
    [Trait("Requirement", "TST-011")]
    public void The_fixture_driver_pins_a_midpoint_jitter_source()
    {
        // D-M2-27 item 4 makes this a *precondition* for registering any `auth.autorenew.*`
        // fixture: `expectWaits` is one ordered stream that carries CFG-051..055 transport backoff
        // waits alongside AUT-090's schedule, so an unpinned jitter source would make the stream
        // non-deterministic. Asserted rather than assumed, because the day someone removes the pin
        // the autorenew fixtures would start failing for a reason that looks like a schedule bug.
        FixtureDocument fixture = Synthetic(
            """
            {
              "id": "synthetic.jitter-pin",
              "title": "Jitter source is pinned",
              "requirements": ["TST-011"],
              "level": "core",
              "sections": ["05"],
              "client": {"address": "https://vault.example.com:8200"},
              "operation": {"name": "Synthetic.Any"},
              "exchanges": [],
              "expect": {"result": "$any"}
            }
            """);

        BastionVaultClientOptions options = FixtureClientBuilder.BuildOptions(
            FixtureConfiguration.From(fixture),
            new FakeTransport(),
            FixtureInstruments.For(fixture));

        Assert.NotNull(options.JitterSource);
        Assert.Equal(0.5, options.JitterSource!.NextDouble());
        Assert.Equal(0.5, options.JitterSource.NextDouble());
    }

    [Fact]
    [Requirement("TST-011")]
    [Trait("Requirement", "TST-011")]
    public void An_instant_clock_grants_no_waits_and_does_not_move_time()
    {
        // The other half of "no existing fixture changes meaning": `"instant"` is the default, and
        // its body is the pre-M2c one — time does not move and nothing is recorded.
        FixtureDocument fixture = Synthetic(
            """
            {
              "id": "synthetic.instant-clock",
              "title": "Instant delay is unchanged",
              "requirements": ["TST-011"],
              "level": "core",
              "sections": ["05"],
              "clock": {"start": "2026-09-13T12:00:00Z"},
              "operation": {"name": "Synthetic.Waits"},
              "exchanges": [],
              "expect": {"result": {"value": "ok"}}
            }
            """);
        OperationRegistry registry = new();
        registry.Register("Synthetic.Waits", async invocation =>
        {
            FixtureClock clock = invocation.Instruments.Clock;
            DateTimeOffset start = clock.NowUtc();
            await clock.Delay(TimeSpan.FromHours(9000), CancellationToken.None);

            Assert.Equal(start, clock.NowUtc());
            Assert.Empty(clock.GrantedWaits);
            Assert.False(clock.IsVirtual);
            return new FixtureOperationResult(new Dictionary<string, object?>(StringComparer.Ordinal) { ["value"] = "ok" });
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
            // TST-051 exists to catch. .NET's CNF-030 logger seam has two levels (Warn, Info); the
            // seed uses Warn, and the assertion searches every captured line regardless of level.
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

    private static string LeakFixtureJson(string id)
    {
        return $$$"""
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
    }

    /// <summary>A fixture in D-M2-27's virtual mode, declaring <paramref name="expectWaits"/>.</summary>
    private static string VirtualFixtureJson(string id, string expectWaits)
    {
        return $$$"""
        {
          "id": "{{{id}}}",
          "title": "Virtual clock case",
          "requirements": ["TST-011"],
          "level": "core",
          "sections": ["05"],
          "clock": {"start": "2026-09-13T12:00:00Z", "delay": "virtual", "expectWaits": {{{expectWaits}}} },
          "operation": {"name": "Synthetic.Waits"},
          "exchanges": [],
          "expect": {"result": {"value": "ok"}}
        }
        """;
    }

    private static FixtureDocument Synthetic(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return new FixtureDocument("synthetic.fixture.json", document.RootElement.Clone());
    }
}
