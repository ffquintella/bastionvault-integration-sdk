using System.Text.Json;
using BastionVault.IntegrationSdk;

namespace BastionVault.IntegrationSdk.Tests.Harness.Operations;

/// <summary>
/// Registers <c>Logical.Read</c>, <c>Logical.Write</c>, <c>Logical.Delete</c>, <c>Logical.List</c>
/// and <c>Logical.Raw</c> against the real SDK (<c>decisions/0004-m1b-transport.md</c>): each fixture's
/// <c>client</c> block is resolved through the real <see cref="BastionVaultClient"/> constructor and
/// every exchange is driven through the fixture's <see cref="ScriptedTransport"/> via
/// <see cref="ScriptedTransportAdapter"/>, never a test-only shim.
/// </summary>
public static class LogicalFixtureOperations
{
    public static void Register(OperationRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Register("Logical.Read", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            string path = invocation.Arguments.GetProperty("path").GetString()!;
            return await RunAsync(client, () => client.Logical.ReadAsync(path, options)).ConfigureAwait(false);
        });

        registry.Register("Logical.Write", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            string path = invocation.Arguments.GetProperty("path").GetString()!;
            JsonElement? body = invocation.Arguments.TryGetProperty("body", out JsonElement bodyElement) ? bodyElement.Clone() : null;
            return await RunAsync(client, () => client.Logical.WriteAsync(path, body, options)).ConfigureAwait(false);
        });

        registry.Register("Logical.Delete", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            string path = invocation.Arguments.GetProperty("path").GetString()!;
            JsonElement? body = invocation.Arguments.TryGetProperty("body", out JsonElement bodyElement) ? bodyElement.Clone() : null;
            return await RunAsync(client, () => client.Logical.DeleteAsync(path, body, options)).ConfigureAwait(false);
        });

        registry.Register("Logical.List", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            string path = invocation.Arguments.GetProperty("path").GetString()!;
            return await RunAsync(client, () => client.Logical.ListAsync(path, options)).ConfigureAwait(false);
        });

        registry.Register("Logical.Raw", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            string method = invocation.Arguments.GetProperty("method").GetString()!;
            string path = invocation.Arguments.GetProperty("path").GetString()!;
            JsonElement? body = invocation.Arguments.TryGetProperty("body", out JsonElement bodyElement) ? bodyElement.Clone() : null;
            try
            {
                RawResponse response = await client.Logical.RawAsync(method, path, body, options).ConfigureAwait(false);
                Dictionary<string, object?> result = new(StringComparer.Ordinal)
                {
                    ["StatusCode"] = response.StatusCode,
                };
                return new FixtureOperationResult(Result: result, ClientState: ClientState(client));
            }
            catch (BastionVaultException exception)
            {
                return new FixtureOperationResult(Error: ToFixtureError(exception), ClientState: ClientState(client));
            }
        });
    }

    private static (BastionVaultClient Client, RequestOptions Options) Build(FixtureInvocation invocation)
    {
        ScriptedTransportAdapter adapter = new(invocation.Transport);
        BastionVaultClientOptions clientOptions = FixtureClientBuilder.BuildOptions(invocation.Configuration, adapter, invocation.Instruments);
        EnvironmentSource environment = invocation.Configuration.Environment.Count > 0
            ? EnvironmentSource.FromMap(invocation.Configuration.Environment)
            : EnvironmentSource.None;
        BastionVaultClient client = new(clientOptions, environment);
        RequestOptions options = ParseOptions(invocation.Options);
        return (client, options);
    }

    private static RequestOptions ParseOptions(JsonElement options)
    {
        if (options.ValueKind != JsonValueKind.Object)
        {
            return new RequestOptions();
        }

        RequestOptions result = new();
        if (options.TryGetProperty("namespace", out JsonElement ns) && ns.ValueKind == JsonValueKind.String)
        {
            result = result with { Namespace = ns.GetString() };
        }

        if (options.TryGetProperty("idempotent", out JsonElement idempotent) && idempotent.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            result = result with { Idempotent = idempotent.GetBoolean() };
        }

        if (options.TryGetProperty("wrapTtl", out JsonElement wrapTtl) && wrapTtl.ValueKind == JsonValueKind.String)
        {
            result = result with { WrapTtl = wrapTtl.GetString() };
        }

        if (options.TryGetProperty("apiVersion", out JsonElement apiVersion) && apiVersion.ValueKind == JsonValueKind.String)
        {
            result = result with { ApiVersion = apiVersion.GetString() };
        }

        return result;
    }

    private static async ValueTask<FixtureOperationResult> RunAsync(BastionVaultClient client, Func<Task<Response?>> call)
    {
        try
        {
            Response? response = await call().ConfigureAwait(false);
            return new FixtureOperationResult(Result: response is null ? null : ToResult(response), ClientState: ClientState(client));
        }
        catch (BastionVaultException exception)
        {
            return new FixtureOperationResult(Error: ToFixtureError(exception), ClientState: ClientState(client));
        }
    }

    /// <summary>
    /// Converts a <see cref="JsonElement"/> into a plain CLR object graph (nested
    /// <see cref="Dictionary{TKey,TValue}"/>/<see cref="List{T}"/>/<see cref="string"/>/number/bool/null)
    /// so <c>FixtureComparisons.ScalarMatches</c> — which only recognises raw CLR scalars, not a
    /// <see cref="JsonElement"/> wrapping one — can compare leaf values.
    /// </summary>
    private static object? ToPlainValue(JsonElement element)
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

    private static Dictionary<string, object?> ToResult(Response response)
    {
        Dictionary<string, object?> result = new(StringComparer.Ordinal)
        {
            ["Data"] = response.Data?.ToDictionary(pair => pair.Key, pair => ToPlainValue(pair.Value), StringComparer.Ordinal),
            ["LeaseId"] = response.LeaseId,
            ["Renewable"] = response.Renewable,
            ["LeaseDuration"] = response.LeaseDuration is { } duration ? (int)duration.TotalSeconds : null,
            ["Warnings"] = response.Warnings,
        };

        if (response.Auth is { } auth)
        {
            result["Auth"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["ClientToken"] = new RedactedValue(auth.ClientToken.Reveal() ?? string.Empty),
                ["Policies"] = auth.Policies,
                ["LeaseDuration"] = auth.LeaseDuration is { } authLease ? (int)authLease.TotalSeconds : null,
                ["Renewable"] = auth.Renewable,
            };
        }
        else
        {
            result["Auth"] = null;
        }

        return result;
    }

    private static FixtureError ToFixtureError(BastionVaultException exception)
    {
        return FixtureErrors.From(exception);
    }

    private static IReadOnlyDictionary<string, object?> ClientState(BastionVaultClient client)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["RateGate.Paused"] = client.RateGateState.Paused,
        };
    }
}
