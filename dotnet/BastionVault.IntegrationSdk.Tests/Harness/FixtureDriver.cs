using System.Text.Json;
using System.Xml;

namespace BastionVault.IntegrationSdk.Tests.Harness;

public sealed class FixtureDriver
{
    /// <summary>D-M2-27 item 3's ±1 ms tolerance on every <c>expectWaits</c> comparison.</summary>
    private static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(1);

    private readonly OperationRegistry registry;
    private readonly IReadOnlyDictionary<string, string> heldPending;

    /// <summary>
    /// Builds a driver. <paramref name="heldPending"/> names fixtures whose operation <b>is</b>
    /// registered but whose asserted behaviour belongs to a later slice, each with the reason
    /// (D-M2-10). Held here rather than as an omission in a test, so "this fixture is pending and
    /// why" is one visible decision instead of a fixture that quietly never runs.
    /// </summary>
    public FixtureDriver(OperationRegistry? registry = null, IReadOnlyDictionary<string, string>? heldPending = null)
    {
        this.registry = registry ?? new OperationRegistry();
        this.heldPending = heldPending ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public int PendingCount { get; private set; }

    public FixtureRunResult Run(FixtureDocument fixture)
    {
        FixtureConfiguration configuration = FixtureConfiguration.From(fixture);
        string operationName = fixture.GetRequired("operation").GetProperty("name").GetString()!;
        if (heldPending.TryGetValue(fixture.Id, out string? heldReason))
        {
            PendingCount++;
            Console.WriteLine($"PENDING fixture '{fixture.Id}' is held: {heldReason}");
            return new FixtureRunResult(fixture.Id, operationName, FixtureRunStatus.Pending, configuration);
        }

        if (!registry.TryResolve(operationName, out FixtureOperationHandler? handler) || handler is null)
        {
            PendingCount++;
            Console.WriteLine($"PENDING fixture '{fixture.Id}' operation '{operationName}' is not registered.");
            return new FixtureRunResult(fixture.Id, operationName, FixtureRunStatus.Pending, configuration);
        }

        JsonElement operation = fixture.GetRequired("operation");
        JsonElement arguments = operation.TryGetProperty("args", out JsonElement args) ? args.Clone() : default;
        JsonElement options = operation.TryGetProperty("options", out JsonElement operationOptions) ? operationOptions.Clone() : default;
        // D-M2-7: both instruments are attached to *every* fixture run, not opted into per
        // fixture, which is what makes TST-051 a whole-suite assertion rather than a checkbox.
        FixtureInstruments instruments = FixtureInstruments.For(fixture);
        ScriptedTransport transport = ScriptedTransport.From(fixture, instruments.Clock);
        FixtureOperationResult actual;
        try
        {
            actual = handler(new FixtureInvocation(fixture, configuration, transport, arguments, options, instruments)).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is FixtureAssertionException or FixtureTransportFailureException)
        {
            throw new FixtureAssertionException($"Fixture '{fixture.Id}' failed while running operation '{operationName}': {exception.Message}", exception);
        }

        try
        {
            // D-M2-27 item 2: checked *first*, and before any expectation comparison. A harness
            // failure raised inside the operation may have been swallowed by the code under test —
            // AUT-092's renewal loop absorbs failures by design — and reporting an expectation
            // mismatch instead would describe the symptom while hiding the cause.
            AssertNoHarnessFailure(instruments.Clock);
            CompareExchanges(fixture, transport);
            CompareExpectation(fixture, actual);
            transport.AssertFullyConsumed();
            AssertClockWasHonoured(fixture, instruments.Clock);
            AssertWaitsMatched(fixture, instruments.Clock);
        }
        catch (FixtureAssertionException exception)
        {
            throw new FixtureAssertionException($"Fixture '{fixture.Id}' failed: {exception.Message}", exception);
        }

        FixtureSecrets.AssertNoLeak(fixture, FixtureSecrets.Surfaced(instruments.Logger, instruments.Observer, actual));

        return new FixtureRunResult(fixture.Id, operationName, FixtureRunStatus.Passed, configuration);
    }

    /// <summary>
    /// D-M2-7: "a fixture that declares <c>clock</c> and runs against a driver that ignores it
    /// must <b>fail</b>, not pass". The driver cannot know whether the operation used the time
    /// correctly, but it can know whether it asked — and a fixture whose whole point is the clock
    /// and which never reads it is the failure mode that hid
    /// <c>auth.token.lookup-self-remaining-ttl</c> since M0.
    /// </summary>
    private static void AssertClockWasHonoured(FixtureDocument fixture, FixtureClock clock)
    {
        // D-M2-27 item 3 redefines "honoured" with a fourth disjunct. It is deliberate, not a
        // loophole: AUT-095's batch/non-renewable fixture legitimately grants zero waits and may
        // read the clock zero times, and a three-disjunct guard would false-fail it. The disjunct
        // costs nothing because AssertWaitsMatched then makes a *positive* claim about the waits —
        // `expectWaits: []` asserts "nothing was granted", which is not silence.
        bool honoured = !clock.IsScripted
            || clock.Reads > 0
            || clock.GrantedWaits.Count > 0
            || FixtureClock.ExpectedWaits(fixture) is not null;

        if (!honoured)
        {
            throw new FixtureAssertionException(
                "declares a `clock` block but the operation never read the injected clock, so the "
                    + "scripted time was ignored and the fixture asserted nothing about it (D-M2-7).");
        }
    }

    /// <summary>
    /// D-M2-27 item 3: whenever <c>clock.expectWaits</c> is present, the granted waits must match
    /// it — ordered, element-wise, at ±1 ms. Independent of the honour predicate, and the reason
    /// <c>delay: "virtual"</c> requires <c>expectWaits</c> by schema: a virtual fixture with no
    /// declared waits would be honoured by any incidental <c>NowUtc()</c> call on the request path
    /// while asserting nothing about the wait it exists to test — D-M2-7's failure mode, one layer
    /// down, on the new field.
    /// </summary>
    /// <remarks>
    /// The tolerance is what makes the field portable: durations are authored in whole
    /// milliseconds, and a whole millisecond is representable exactly in .NET's 100 ns ticks,
    /// Rust's nanoseconds and Python's microseconds alike, so ±1 ms absorbs the rounding of all
    /// three without admitting a wrong schedule.
    /// </remarks>
    private static void AssertWaitsMatched(FixtureDocument fixture, FixtureClock clock)
    {
        if (FixtureClock.ExpectedWaits(fixture) is not { } expected)
        {
            return;
        }

        IReadOnlyList<TimeSpan> granted = clock.GrantedWaits;
        List<string> failures = [];
        if (expected.Count != granted.Count)
        {
            failures.Add($"expected {expected.Count} wait(s), the operation asked for {granted.Count}");
        }

        for (int index = 0; index < Math.Min(expected.Count, granted.Count); index++)
        {
            TimeSpan difference = expected[index] - granted[index];
            if (difference.Duration() > Tolerance)
            {
                failures.Add(
                    $"wait[{index}]: expected {XmlConvert.ToString(expected[index])}, "
                        + $"the operation asked for {XmlConvert.ToString(granted[index])}");
            }
        }

        if (failures.Count > 0)
        {
            throw clock.Latch(
                "declares `clock.expectWaits` and the waits it granted do not match it: "
                    + string.Join("; ", failures)
                    + $". Granted, in order: [{string.Join(", ", granted.Select(XmlConvert.ToString))}] (D-M2-27).");
        }
    }

    /// <summary>
    /// D-M2-27 item 2's post-run latch check: a spin-guard trip or a wait mismatch that the
    /// operation swallowed still fails the run. Throwing alone is not a gate when the code under
    /// test is required to absorb exceptions.
    /// </summary>
    private static void AssertNoHarnessFailure(FixtureClock clock)
    {
        if (clock.HarnessFailure is { } failure)
        {
            throw new FixtureAssertionException(
                $"a harness assertion failed during the run and was not surfaced by the operation: {failure}");
        }
    }

    private static void CompareExchanges(FixtureDocument fixture, ScriptedTransport transport)
    {
        JsonElement exchanges = fixture.GetRequired("exchanges");
        IReadOnlyList<FixtureRequest> requests = transport.Requests;
        List<string> failures = [];
        JsonElement[] expected = exchanges.EnumerateArray().ToArray();
        if (expected.Length != requests.Count)
        {
            failures.Add($"expected {expected.Length} request(s), received {requests.Count}");
        }

        int count = Math.Min(expected.Length, requests.Count);
        bool strictHeaders = fixture.TryGet("strictHeaders", out JsonElement strict) && strict.GetBoolean();
        for (int index = 0; index < count; index++)
        {
            if (expected[index].TryGetProperty("expectRequest", out JsonElement expectedRequest))
            {
                failures.AddRange(FixtureComparisons.CompareRequest(expectedRequest, requests[index], strictHeaders)
                    .Select(failure => $"exchange[{index}]: {failure}"));
            }
        }

        if (failures.Count > 0)
        {
            throw new FixtureAssertionException(string.Join("; ", failures));
        }
    }

    private static void CompareExpectation(FixtureDocument fixture, FixtureOperationResult actual)
    {
        JsonElement expect = fixture.GetRequired("expect");
        if (expect.TryGetProperty("result", out JsonElement expectedResult))
        {
            if (actual.Error is not null)
            {
                throw new FixtureAssertionException($"expected a result but received error '{actual.Error.Code}'.");
            }

            FixtureComparisons.AssertResult(expectedResult, actual.Result);
        }

        if (expect.TryGetProperty("error", out JsonElement expectedError))
        {
            FixtureComparisons.AssertError(expectedError, actual.Error);
        }

        if (expect.TryGetProperty("clientState", out JsonElement expectedClientState))
        {
            FixtureComparisons.AssertResult(expectedClientState, actual.ClientState);
        }
    }
}
