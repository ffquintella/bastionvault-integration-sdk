using System.Text;
using System.Text.Json;
using BastionVault.IntegrationSdk.Testing;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>12 §Notifications (M10 slice c, DR-0017): the <c>notifications</c> mount, reached from <c>Client.Notifications</c>. No requirement ID of its own (catalogue-only surface).</summary>
public sealed class NotificationsUnitTests
{
    private const string Address = "https://vault.example.com:8200";

    [Fact]
    public async Task Send_serialises_every_field_and_requires_a_title_client_side()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);
        using JsonDocument target = JsonDocument.Parse("""{"entity_id":"entity-1"}""");
        using JsonDocument metadata = JsonDocument.Parse("""{"trace_id":"abc"}""");

        await client.Notifications.SendAsync(new NotificationSendRequest
        {
            Title = "Renewal failed",
            Body = "target-1 could not renew",
            Severity = "critical",
            Channels = ["email", "slack"],
            ActionUrl = "https://vault.example.com/targets/target-1",
            Target = AsMap(target.RootElement),
            Metadata = AsMap(metadata.RootElement),
        });

        Assert.Equal("POST", transport.Requests[0].Method);
        Assert.EndsWith("/v1/notifications/send", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains("\"title\":\"Renewal failed\"", body, StringComparison.Ordinal);
        Assert.Contains("\"severity\":\"critical\"", body, StringComparison.Ordinal);
        Assert.Contains("\"channels\":[\"email\",\"slack\"]", body, StringComparison.Ordinal);
        Assert.Contains("\"action_url\"", body, StringComparison.Ordinal);
        Assert.Contains("\"target\":{\"entity_id\":\"entity-1\"}", body, StringComparison.Ordinal);
        Assert.Contains("\"metadata\":{\"trace_id\":\"abc\"}", body, StringComparison.Ordinal);

        FakeTransport guardTransport = new();
        BastionVaultClient guardClient = BuildClient(guardTransport);
        _ = await Assert.ThrowsAsync<ArgumentException>(
            () => guardClient.Notifications.SendAsync(new NotificationSendRequest { Title = "   " }));
        Assert.Empty(guardTransport.Requests);

        // Every optional member absent: only `title` is written.
        FakeTransport minimalTransport = new();
        minimalTransport.EnqueueResponse(204);
        await BuildClient(minimalTransport).Notifications.SendAsync(new NotificationSendRequest { Title = "Renewal failed" });
        Assert.Equal("{\"title\":\"Renewal failed\"}", Encoding.UTF8.GetString(minimalTransport.Requests[0].Body.Span));
    }

    [Fact]
    public async Task Inbox_list_unread_count_mark_read_read_all_and_dismiss_round_trip()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":[{"id":"n-1","title":"Renewal failed"}]}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"count":3}}"""));
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        IReadOnlyList<JsonElement> entries = await client.Notifications.Inbox.ListAsync();
        _ = Assert.Single(entries);
        Assert.EndsWith("/v1/notifications/inbox", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        IReadOnlyDictionary<string, JsonElement>? unread = await client.Notifications.Inbox.UnreadCountAsync();
        Assert.Equal(3, unread!["count"].GetInt32());
        Assert.EndsWith("/v1/notifications/inbox/unread-count", transport.Requests[1].Uri.AbsoluteUri, StringComparison.Ordinal);

        await client.Notifications.Inbox.MarkReadAsync("n-1");
        Assert.EndsWith("/v1/notifications/inbox/n-1/read", transport.Requests[2].Uri.AbsoluteUri, StringComparison.Ordinal);

        await client.Notifications.Inbox.ReadAllAsync();
        Assert.EndsWith("/v1/notifications/inbox/read-all", transport.Requests[3].Uri.AbsoluteUri, StringComparison.Ordinal);

        await client.Notifications.Inbox.DismissAsync("n-1");
        Assert.Equal("DELETE", transport.Requests[4].Method);
        Assert.EndsWith("/v1/notifications/inbox/n-1", transport.Requests[4].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport missingTransport = new();
        missingTransport.EnqueueResponse(404);
        Assert.Null(await BuildClient(missingTransport).Notifications.Inbox.UnreadCountAsync());
    }

    [Fact]
    public async Task Channels_list_and_test_round_trip()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":[{"channel":"email"}]}"""));
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        IReadOnlyList<JsonElement> channels = await client.Notifications.Channels.ListAsync();
        _ = Assert.Single(channels);
        Assert.EndsWith("/v1/notifications/channels", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        await client.Notifications.Channels.TestAsync("email", "ops@example.com");
        Assert.EndsWith("/v1/notifications/channels/email/test", transport.Requests[1].Uri.AbsoluteUri, StringComparison.Ordinal);
        string body = Encoding.UTF8.GetString(transport.Requests[1].Body.Span);
        Assert.Contains("\"to\":\"ops@example.com\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sent_and_Config_round_trip()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":[{"id":"n-1"}]}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"inbox_cap":500,"plugin_rate_per_min":60}}"""));
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        IReadOnlyList<JsonElement> sent = await client.Notifications.SentAsync();
        _ = Assert.Single(sent);
        Assert.EndsWith("/v1/notifications/sent/", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        NotificationsConfig? config = await client.Notifications.ReadConfigAsync();
        Assert.Equal(500, config!.InboxCap);
        Assert.Equal(60, config.PluginRatePerMin);

        await client.Notifications.WriteConfigAsync(new NotificationsConfig { InboxCap = 1000, PluginRatePerMin = 120 });
        string body = Encoding.UTF8.GetString(transport.Requests[2].Body.Span);
        Assert.Contains("\"inbox_cap\":1000", body, StringComparison.Ordinal);
        Assert.Contains("\"plugin_rate_per_min\":120", body, StringComparison.Ordinal);

        FakeTransport missingTransport = new();
        missingTransport.EnqueueResponse(404);
        Assert.Null(await BuildClient(missingTransport).Notifications.ReadConfigAsync());

        // Every member absent: nothing is written.
        FakeTransport emptyWriteTransport = new();
        emptyWriteTransport.EnqueueResponse(204);
        await BuildClient(emptyWriteTransport).Notifications.WriteConfigAsync(new NotificationsConfig());
        Assert.Equal("{}", Encoding.UTF8.GetString(emptyWriteTransport.Requests[0].Body.Span));
    }

    [Fact]
    public async Task Every_operation_rejects_empty_arguments_before_any_request_is_sent()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => client.Notifications.SendAsync(null!));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Notifications.Inbox.MarkReadAsync(string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Notifications.Inbox.DismissAsync(string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Notifications.Channels.TestAsync(string.Empty, "ops@example.com"));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Notifications.Channels.TestAsync("email", string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => client.Notifications.WriteConfigAsync(null!));
        Assert.Empty(transport.Requests);
    }

    private static BastionVaultClient BuildClient(ITransport transport)
    {
        BastionVaultClientOptions options = new()
        {
            Address = Address,
            Token = "s.FAKE-token-0000000000000000",
            Transport = transport,
            RateGate = new RateGate { RatePerSecond = 0 },
            RetryPolicy = new RetryPolicy { MaxAttempts = 1 },
        };
        return new BastionVaultClient(options, EnvironmentSource.None);
    }

    private static IReadOnlyDictionary<string, JsonElement> AsMap(JsonElement element)
    {
        return element.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
    }

    private static ReadOnlyMemory<byte> Json(string json)
    {
        return Encoding.UTF8.GetBytes(json);
    }
}
