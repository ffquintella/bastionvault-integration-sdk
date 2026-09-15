using System.Text.Json;
using BastionVault.IntegrationSdk;

namespace BastionVault.IntegrationSdk.Tests.Harness.Operations;

/// <summary>
/// Registers the M3 <c>Sys.*</c> fixture operations against the real SDK (DR-0007): health and
/// status (SYS-001, SYS-002, SYS-005, SYS-006, SYS-008) and self capability introspection
/// (SYS-050…SYS-053), driven through the real <see cref="BastionVaultClient"/> and the fixture's
/// <see cref="ScriptedTransport"/>, never a test-only shim (D-M0-2, D-M1a-6).
/// </summary>
public static class SysFixtureOperations
{
    public static void Register(OperationRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Register("Sys.Health", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => HealthResult(await client.Sys.HealthAsync(options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Sys.SealStatus", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => SealStatusResult(await client.Sys.SealStatusAsync(options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Sys.ServerInfo", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => ServerInfoResult(await client.Sys.ServerInfoAsync(options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Sys.ClusterStatus", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => ClusterStatusResult(await client.Sys.ClusterStatusAsync(options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Sys.CapabilitiesSelf", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            string[] paths = args.TryGetProperty("paths", out JsonElement pathsElement) && pathsElement.ValueKind == JsonValueKind.Array
                ? pathsElement.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray()
                : [];
            return await RunAsync(client, async () => CapabilitiesResult(await client.Sys.CapabilitiesSelfAsync(paths, options).ConfigureAwait(false))).ConfigureAwait(false);
        });
    }

    private static (BastionVaultClient Client, RequestOptions Options) Build(FixtureInvocation invocation)
    {
        ScriptedTransportAdapter adapter = new(invocation.Transport);
        BastionVaultClientOptions clientOptions = FixtureClientBuilder.BuildOptions(invocation.Configuration, adapter, invocation.Instruments);
        EnvironmentSource environment = invocation.Configuration.Environment.Count > 0
            ? EnvironmentSource.FromMap(invocation.Configuration.Environment)
            : EnvironmentSource.None;
        return (new BastionVaultClient(clientOptions, environment), new RequestOptions());
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

    private static object HealthResult(HealthStatus health)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["State"] = health.State.ToString(),
            ["Initialized"] = health.Initialized,
            ["Sealed"] = health.Sealed,
            ["Standby"] = health.Standby,
            ["ClusterHealthy"] = health.ClusterHealthy,
            ["StatusCode"] = health.StatusCode,
        };
    }

    private static object SealStatusResult(SealStatus status)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Sealed"] = status.Sealed,
            ["T"] = status.T,
            ["N"] = status.N,
            ["KeyShares"] = status.KeyShares,
            ["KeyThreshold"] = status.KeyThreshold,
            ["Progress"] = status.Progress,
        };
    }

    private static object ServerInfoResult(ServerInfo info)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Initialized"] = info.Initialized,
            ["Sealed"] = info.Sealed,
            ["Version"] = info.Version,
            ["StartedAt"] = info.StartedAt?.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
            ["UptimeSeconds"] = info.UptimeSeconds,
            ["StorageType"] = info.StorageType,
        };
    }

    private static object ClusterStatusResult(ClusterStatus status)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["StorageType"] = status.StorageType,
            ["Cluster"] = status.Cluster,
            ["NodeId"] = status.NodeId,
            ["IsLeader"] = status.IsLeader,
            ["ClusterHealthy"] = status.ClusterHealthy,
            ["RaftMetrics"] = status.RaftMetrics?.ToDictionary(pair => pair.Key, pair => ToPlainValue(pair.Value), StringComparer.Ordinal),
        };
    }

    private static object CapabilitiesResult(Capabilities capabilities)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["ByPath"] = capabilities.ByPath.ToDictionary(
                pair => pair.Key,
                pair => (object?)pair.Value.Select(capability => (object?)capability.WireValue).ToList(),
                StringComparer.Ordinal),
            ["NamespaceOperable"] = capabilities.NamespaceOperable,
            ["TokenNamespace"] = capabilities.TokenNamespace,
            ["ActiveNamespace"] = capabilities.ActiveNamespace,
        };
    }

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

    private static IReadOnlyDictionary<string, object?> ClientState(BastionVaultClient client)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["RateGate.Paused"] = client.RateGateState.Paused,
        };
    }
}
