using System.Text.Json;

namespace BastionVault.IntegrationSdk.Tests.Harness;

public sealed class FixtureDriver
{
    private readonly OperationRegistry registry;

    public FixtureDriver(OperationRegistry? registry = null)
    {
        this.registry = registry ?? new OperationRegistry();
    }

    public int PendingCount { get; private set; }

    public FixtureRunResult Run(FixtureDocument fixture)
    {
        FixtureConfiguration configuration = FixtureConfiguration.From(fixture);
        string operationName = fixture.GetRequired("operation").GetProperty("name").GetString()!;
        if (!registry.TryResolve(operationName, out FixtureOperationHandler? handler) || handler is null)
        {
            PendingCount++;
            Console.WriteLine($"PENDING fixture '{fixture.Id}' operation '{operationName}' is not registered.");
            return new FixtureRunResult(fixture.Id, operationName, FixtureRunStatus.Pending, configuration);
        }

        JsonElement operation = fixture.GetRequired("operation");
        JsonElement arguments = operation.TryGetProperty("args", out JsonElement args) ? args.Clone() : default;
        JsonElement options = operation.TryGetProperty("options", out JsonElement operationOptions) ? operationOptions.Clone() : default;
        ScriptedTransport transport = ScriptedTransport.From(fixture);
        FixtureOperationResult actual;
        try
        {
            actual = handler(new FixtureInvocation(fixture, configuration, transport, arguments, options)).GetAwaiter().GetResult();
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
        }
        catch (FixtureAssertionException exception)
        {
            throw new FixtureAssertionException($"Fixture '{fixture.Id}' failed: {exception.Message}", exception);
        }

        return new FixtureRunResult(fixture.Id, operationName, FixtureRunStatus.Passed, configuration);
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
