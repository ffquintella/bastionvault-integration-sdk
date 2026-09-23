using System.Text;
using System.Text.Json;
using BastionVault.IntegrationSdk.Testing;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// 12 — asset groups (M10 slice a, DR-0017): the <c>resource-group/</c> mount, reached from
/// <c>Client.AssetGroups</c>. No requirement ID of its own (catalogue-only surface).
/// </summary>
public sealed class AssetGroupsUnitTests
{
    private const string Address = "https://vault.example.com:8200";

    [Fact]
    public async Task List_read_write_delete_and_history_round_trip()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"keys":["prod-db","prod-web"]}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"resources":["db-1","db-2"]}}"""));
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(200, body: Json("""{"data":[{"resources":["db-1"]}]}"""));
        BastionVaultClient client = BuildClient(transport);

        IReadOnlyList<string> names = await client.AssetGroups.ListAsync();
        Assert.Equal(["prod-db", "prod-web"], names);
        Assert.Equal("LIST", transport.Requests[0].Method);
        Assert.EndsWith("/v1/resource-group/groups", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        Response? read = await client.AssetGroups.ReadAsync("prod-db");
        Assert.Equal(["db-1", "db-2"], read!.Data!["resources"].EnumerateArray().Select(e => e.GetString()));
        Assert.EndsWith("/v1/resource-group/groups/prod-db", transport.Requests[1].Uri.AbsoluteUri, StringComparison.Ordinal);

        using JsonDocument spec = JsonDocument.Parse("""{"resources":["db-1","db-2"]}""");
        _ = await client.AssetGroups.WriteAsync("prod-db", spec.RootElement);
        Assert.Equal("PUT", transport.Requests[2].Method);
        Assert.EndsWith("/v1/resource-group/groups/prod-db", transport.Requests[2].Uri.AbsoluteUri, StringComparison.Ordinal);

        await client.AssetGroups.DeleteAsync("prod-db");
        Assert.Equal("DELETE", transport.Requests[3].Method);

        IReadOnlyList<JsonElement> history = await client.AssetGroups.HistoryAsync("prod-db");
        _ = Assert.Single(history);
        Assert.EndsWith("/v1/resource-group/groups/prod-db/history", transport.Requests[4].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task History_reads_the_measured_data_dot_entries_nested_shape()
    {
        // Measured against bvault 0.44.5: resource-group/groups/{name}/history wraps its array
        // as `data.entries`, not `data` itself.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"entries":[{"op":"create"},{"op":"update"}]}}"""));

        IReadOnlyList<JsonElement> history = await BuildClient(transport).AssetGroups.HistoryAsync("prod-db");

        Assert.Equal(2, history.Count);
    }

    [Fact]
    public async Task ByResource_reads_a_plain_segment_and_BySecret_base64url_encodes_the_plain_path()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"name":"prod-db"}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"name":"prod-db"}}"""));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.AssetGroups.ByResourceAsync("db-1");
        Assert.EndsWith("/v1/resource-group/by-resource/db-1", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        _ = await client.AssetGroups.BySecretAsync("secret/app/db");
        Assert.EndsWith("/v1/resource-group/by-secret/c2VjcmV0L2FwcC9kYg", transport.Requests[1].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reindex_sends_a_bodyless_PUT()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        await client.AssetGroups.ReindexAsync();

        Assert.Equal("PUT", transport.Requests[0].Method);
        Assert.EndsWith("/v1/resource-group/reindex", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal(0, transport.Requests[0].Body.Length);
    }

    [Fact]
    public async Task Read_and_history_are_null_or_empty_on_404_and_every_operation_rejects_empty_arguments()
    {
        FakeTransport listTransport = new();
        listTransport.EnqueueResponse(404);
        Assert.Empty(await BuildClient(listTransport).AssetGroups.ListAsync());

        FakeTransport readTransport = new();
        readTransport.EnqueueResponse(404);
        Assert.Null(await BuildClient(readTransport).AssetGroups.ReadAsync("ghost"));

        FakeTransport historyTransport = new();
        historyTransport.EnqueueResponse(404);
        Assert.Empty(await BuildClient(historyTransport).AssetGroups.HistoryAsync("ghost"));

        FakeTransport guardTransport = new();
        BastionVaultClient client = BuildClient(guardTransport);
        using JsonDocument spec = JsonDocument.Parse("{}");

        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.AssetGroups.ReadAsync(string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.AssetGroups.WriteAsync(string.Empty, spec.RootElement));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.AssetGroups.DeleteAsync(string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.AssetGroups.ByResourceAsync(string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.AssetGroups.BySecretAsync(string.Empty));
        Assert.Empty(guardTransport.Requests);
    }

    private static BastionVaultClient BuildClient(ITransport transport, Action<BastionVaultClientOptions>? configure = null)
    {
        BastionVaultClientOptions options = new()
        {
            Address = Address,
            Token = "s.FAKE-token-0000000000000000",
            Transport = transport,
            RateGate = new RateGate { RatePerSecond = 0 },
            RetryPolicy = new RetryPolicy { MaxAttempts = 1 },
        };
        configure?.Invoke(options);
        return new BastionVaultClient(options, EnvironmentSource.None);
    }

    private static ReadOnlyMemory<byte> Json(string json)
    {
        return Encoding.UTF8.GetBytes(json);
    }
}
