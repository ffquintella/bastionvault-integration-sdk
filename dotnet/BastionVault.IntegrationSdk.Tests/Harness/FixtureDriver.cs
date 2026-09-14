using System.Text.Json;

namespace BastionVault.IntegrationSdk.Tests.Harness;

public sealed class FixtureDriver
{
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
            CompareExchanges(fixture, transport);
            CompareExpectation(fixture, actual);
            transport.AssertFullyConsumed();
            AssertClockWasHonoured(fixture, instruments.Clock);
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
        if (clock.IsScripted && clock.Reads == 0)
        {
            throw new FixtureAssertionException(
                "declares a `clock` block but the operation never read the injected clock, so the "
                    + "scripted time was ignored and the fixture asserted nothing about it (D-M2-7).");
        }
    }

    private static void CompareExchanges(FixtureDocument fixture, ScriptedTransport transport)
    {
        JsonElement exchanges = fixture.GetRequired("exchanges");
        IReadOnlyList<FixtureRequest> requests = transport.Requests;
        List<string> failures = new();
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
