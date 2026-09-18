using System.Text.Json;
using BastionVault.IntegrationSdk;

namespace BastionVault.IntegrationSdk.Tests.Harness.Operations;

/// <summary>
/// Registers the M8d <c>efficiency.*</c> fixture operations against the real SDK (14 — batch
/// operations and request efficiency), following <see cref="TotpFixtureOperations"/> exactly.
/// </summary>
/// <remarks>
/// <para>
/// Slice d registers <c>Sys.Batch</c> (BAT-001…BAT-006). Slice e adds the <c>*-info</c> pages
/// (<c>PAG-*</c>) and <c>Sys.CacheVersion</c> (<c>CCH-*</c>), which is why
/// <see cref="ClientState"/> is one shared projection here rather than one per operation: the
/// <c>PAG</c>/<c>CCH</c> fixtures will assert the same <c>RateGate.*</c> keys and should not each
/// grow their own copy.
/// </para>
/// </remarks>
public static class EfficiencyFixtureOperations
{
    public static void Register(OperationRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Register("Sys.Batch", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => BatchResults(
                await client.Sys.BatchAsync(Operations(args), options).ConfigureAwait(false))).ConfigureAwait(false);
        });
    }

    /// <summary>
    /// EFF-006's observable state, shared by every operation in this family so a fixture can
    /// assert the gate's tokens as well as its pause.
    /// </summary>
    internal static IReadOnlyDictionary<string, object?> ClientState(BastionVaultClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        RateGateState state = client.RateGateState;
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["RateGate.Paused"] = state.Paused,
            ["RateGate.AvailableTokens"] = state.AvailableTokens,
        };
    }

    /// <summary>Projects a <see cref="BatchResult"/> list onto the shape a fixture's <c>expect.result</c> addresses.</summary>
    internal static object BatchResults(IReadOnlyList<BatchResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        return results.Select(result => (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Status"] = result.Status,
            ["Path"] = result.Path,
            ["Data"] = result.Data is { } data ? FixtureJson.ToPlainValue(data) : null,
            ["Errors"] = result.Errors.Select(item => (object?)item).ToList(),
            ["Warnings"] = result.Warnings.Select(item => (object?)item).ToList(),
            // BAT-005: present exactly when Status >= 400, and carrying the code a standalone
            // response of that status and those errors[] would have produced.
            ["Error"] = result.Error is { } error
                ? new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["Code"] = error.Code,
                    ["StatusCode"] = error.StatusCode,
                    ["Retryable"] = error.Retryable,
                }
                : null,
        }).ToList();
    }

    private static IReadOnlyList<BatchOperation> Operations(JsonElement args)
    {
        if (!args.TryGetProperty("operations", out JsonElement operations) || operations.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. operations.EnumerateArray().Select(item => new BatchOperation
        {
            Operation = Kind(item),
            Path = item.TryGetProperty("Path", out JsonElement path) && path.ValueKind == JsonValueKind.String
                ? path.GetString()!
                : string.Empty,
            Data = item.TryGetProperty("Data", out JsonElement data) ? data.Clone() : null,
        })];
    }

    private static BatchOperationKind Kind(JsonElement item)
    {
        return item.TryGetProperty("Operation", out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() switch
            {
                "Write" => BatchOperationKind.Write,
                "Delete" => BatchOperationKind.Delete,
                "List" => BatchOperationKind.List,
                _ => BatchOperationKind.Read,
            }
            : BatchOperationKind.Read;
    }

    private static (BastionVaultClient Client, RequestOptions Options) Build(FixtureInvocation invocation)
    {
        ScriptedTransportAdapter adapter = new(invocation.Transport);
        BastionVaultClientOptions clientOptions = FixtureClientBuilder.BuildOptions(invocation.Configuration, adapter, invocation.Instruments);
        EnvironmentSource environment = invocation.Configuration.Environment.Count > 0
            ? EnvironmentSource.FromMap(invocation.Configuration.Environment)
            : EnvironmentSource.None;
        BastionVaultClient client = new(clientOptions, environment);
        FixtureClientBuilder.ApplyAuthInfoInstrument(client, invocation.Configuration.Settings);
        FixtureClientBuilder.ApplyDiscoveryInstruments(client, invocation.Configuration.Settings);
        return (client, new RequestOptions());
    }

    private static async ValueTask<FixtureOperationResult> RunAsync(BastionVaultClient client, Func<Task<object?>> call)
    {
        try
        {
            object? result = await call().ConfigureAwait(false);
            return new FixtureOperationResult(Result: result, ClientState: ClientState(client));
        }
        catch (BastionVaultException exception)
        {
            return new FixtureOperationResult(Error: FixtureErrors.From(exception), ClientState: ClientState(client));
        }
    }
}

/// <summary>
/// The one JSON-to-plain-value projection the fixture comparer understands, shared by the
/// operation families that return raw server payloads.
/// </summary>
public static class FixtureJson
{
    /// <summary>Projects a <see cref="JsonElement"/> onto the dictionaries, lists and scalars <c>FixtureComparisons</c> compares.</summary>
    public static object? ToPlainValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(property => property.Name, property => ToPlainValue(property.Value), StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(ToPlainValue).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out long integer) ? integer : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }
}
