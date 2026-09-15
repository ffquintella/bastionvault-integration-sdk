using System.Text.Json;
using BastionVault.IntegrationSdk;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk.Tests.Harness.Operations;

/// <summary>
/// The fixture <c>resolver</c> block as an <see cref="ISrvResolver"/> (DSC-014, D-M5-15): declared
/// answers are returned verbatim, and an owner name absent from the block resolves to <b>no
/// records</b>, which DSC-011 requires be indistinguishable from a resolver failure.
/// </summary>
internal sealed class FixtureSrvResolver : ISrvResolver
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<SrvRecord>> answers;
    private readonly List<string> queried = [];

    private FixtureSrvResolver(IReadOnlyDictionary<string, IReadOnlyList<SrvRecord>> answers)
    {
        this.answers = answers;
    }

    /// <summary>Every owner name the SDK actually asked for, in order — which is how "queried verbatim" is asserted.</summary>
    public IReadOnlyList<string> Queried => queried;

    public static FixtureSrvResolver From(FixtureDocument fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        Dictionary<string, IReadOnlyList<SrvRecord>> answers = new(StringComparer.Ordinal);
        if (fixture.TryGet("resolver", out JsonElement resolver) && resolver.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty owner in resolver.EnumerateObject())
            {
                answers[owner.Name] = owner.Value.EnumerateArray()
                    .Select(record => new SrvRecord(
                        record.GetProperty("target").GetString()!,
                        record.GetProperty("port").GetInt32(),
                        record.GetProperty("priority").GetInt32(),
                        record.GetProperty("weight").GetInt32()))
                    .ToArray();
            }
        }

        return new FixtureSrvResolver(answers);
    }

    public Task<IReadOnlyList<SrvRecord>> ResolveAsync(string ownerName, CancellationToken cancellationToken = default)
    {
        queried.Add(ownerName);
        return Task.FromResult(answers.TryGetValue(ownerName, out IReadOnlyList<SrvRecord>? records)
            ? records
            : (IReadOnlyList<SrvRecord>)Array.Empty<SrvRecord>());
    }
}

/// <summary>
/// Registers the M5a <c>Client.*</c> discovery operations against the real SDK (DR-0010): the pick
/// (<c>Client.Connect</c>), the diagnostics table (<c>Client.Discover</c>, DSC-036), the explicit
/// re-pin (<c>Client.Reconnect</c>) and DSC-001's classification table (<c>Client.Classify</c>).
/// </summary>
/// <remarks>
/// Every result projection here is pinned by D-M5-15. <c>expect.result</c> is schema-free and
/// compared structurally, so the JSON shape <i>is</i> the cross-language contract and an unpinned
/// projection would be an unportable fixture.
/// </remarks>
public static class ResilienceFixtureOperations
{
    public static void Register(OperationRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Register("Client.Connect", async invocation =>
        {
            BastionVaultClient client = Build(invocation);
            return await RunAsync(
                client,
                async () => new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["SelectedNode"] = Selection(await client.ConnectAsync().ConfigureAwait(false)),
                }).ConfigureAwait(false);
        });

        // DSC-046's member, same projection as Client.Connect (D-M5-15).
        registry.Register("Client.Reconnect", async invocation =>
        {
            BastionVaultClient client = Build(invocation);
            return await RunAsync(
                client,
                async () => new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["SelectedNode"] = Selection(await client.ReconnectAsync().ConfigureAwait(false)),
                }).ConfigureAwait(false);
        });

        registry.Register("Client.Discover", async invocation =>
        {
            BastionVaultClient client = Build(invocation);
            return await RunAsync(
                client,
                async () => Report(await client.DiscoverAsync().ConfigureAwait(false))).ConfigureAwait(false);
        });

        // Exists because the schema admits one operation per fixture while Appendix C names the
        // classification table as a *single* fixture id. It reports what DSC-001's table specifies
        // and mints no public API — it reads the same internal classifier the resolver does
        // (D-M5-15).
        registry.Register("Client.Classify", invocation =>
        {
            JsonElement args = invocation.Arguments;
            bool clusterDiscovery = !args.TryGetProperty("clusterDiscovery", out JsonElement declared)
                || declared.ValueKind != JsonValueKind.False;
            DiscoveryConfig discovery = new();
            List<object?> rows = [];
            try
            {
                foreach (JsonElement address in args.GetProperty("addresses").EnumerateArray())
                {
                    string input = address.GetString()!;
                    AddressClassifier.Classification classification =
                        AddressClassifier.Classify(input, discovery, clusterDiscovery);
                    rows.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["Input"] = input,
                        ["Mode"] = classification.IsDiscovery ? "Discovery" : "Literal",
                        ["Candidates"] = classification.Literal is { } literal
                            ? (List<object?>)[CandidateMap(literal)]
                            : [],
                    });
                }
            }
            catch (BastionVaultException exception)
            {
                // An input that raises BV-CONFIG-001 (DSC-002's unbracketed IPv6) appears as an
                // expect.error, not as a row (D-M5-15).
                return ValueTask.FromResult(new FixtureOperationResult(Error: FixtureErrors.From(exception)));
            }

            return ValueTask.FromResult(new FixtureOperationResult(
                Result: new Dictionary<string, object?>(StringComparer.Ordinal) { ["Rows"] = rows }));
        });
    }

    private static BastionVaultClient Build(FixtureInvocation invocation)
    {
        ScriptedTransportAdapter adapter = new(invocation.Transport);
        BastionVaultClientOptions options = FixtureClientBuilder.BuildOptions(
            invocation.Configuration,
            adapter,
            invocation.Instruments,
            autoRenew: null,
            FixtureSrvResolver.From(invocation.Fixture));
        EnvironmentSource environment = invocation.Configuration.Environment.Count > 0
            ? EnvironmentSource.FromMap(invocation.Configuration.Environment)
            : EnvironmentSource.None;
        BastionVaultClient client = new(options, environment);
        FixtureClientBuilder.ApplyDiscoveryInstruments(client, invocation.Configuration.Settings);
        return client;
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

    private static async ValueTask<FixtureOperationResult> RunAsync(
        BastionVaultClient client,
        Func<Task<Dictionary<string, object?>>> call)
    {
        return await RunAsync(client, async () => (object?)await call().ConfigureAwait(false)).ConfigureAwait(false);
    }

    private static object? Selection(NodeSelection? selection)
    {
        // Null on a literal-mode client, which DSC-001 forbids probing (D-M5-8 at revision 3).
        return selection is null
            ? null
            : new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Url"] = selection.Url,
                ["State"] = selection.State.ToString(),
                ["RttMs"] = selection.RttMs,
                ["ClusterId"] = selection.ClusterId,
                ["Version"] = selection.Version,
            };
    }

    private static object Report(DiscoveryReport report)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["InputLabel"] = report.InputLabel,
            // Flattens ProbeResult over its Candidate, so one row renders one line of RES-020's table.
            ["Ranked"] = report.Ranked
                .Select(probe => (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["Url"] = probe.Candidate.Url,
                    ["Target"] = probe.Candidate.Target,
                    ["Port"] = probe.Candidate.Port,
                    ["Priority"] = probe.Candidate.Priority,
                    ["Weight"] = probe.Candidate.Weight,
                    ["State"] = probe.State.ToString(),
                    ["RttMs"] = probe.RttMs,
                    ["ClusterHealthy"] = probe.ClusterHealthy,
                    ["ClusterId"] = probe.ClusterId,
                    ["Version"] = probe.Version,
                })
                .ToList(),
            ["Picked"] = report.Picked is { } picked
                ? new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["Url"] = picked.Url,
                    ["State"] = picked.State.ToString(),
                    ["RttMs"] = picked.RttMs,
                }
                : null,
        };
    }

    private static object CandidateMap(Candidate candidate)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Url"] = candidate.Url,
            ["Target"] = candidate.Target,
            ["Port"] = candidate.Port,
            ["Priority"] = candidate.Priority,
            ["Weight"] = candidate.Weight,
        };
    }

    /// <summary>D-M5-15 adds <c>SelectedNode.Url</c> to what <c>expect.clientState</c> can address.</summary>
    private static IReadOnlyDictionary<string, object?> ClientState(BastionVaultClient client)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["RateGate.Paused"] = client.RateGateState.Paused,
            ["SelectedNode.Url"] = client.SelectedNode?.Url,
        };
    }
}
