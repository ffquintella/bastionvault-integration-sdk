using System.Text.Json;
using BastionVault.IntegrationSdk.DocsSamples.Infrastructure;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.DocsSamples.Samples;

/// <summary>
/// Every executed sample shown by <c>docs/dotnet/engines/notifications.md</c> (D6). Section 17
/// carries no dedicated usage guide for Notifications, so this page — and these samples — are built
/// directly from <c>12-other-engines-and-identity.md</c>'s Notifications table. Reuses
/// <see cref="MockVaultFixture"/>'s server: routes are bound per test on
/// <see cref="MockVaultFixture.Server"/> and cleared after, so no test leaks a route to another.
/// </summary>
public sealed class NotificationsSamples : IClassFixture<MockVaultFixture>
{
    private const string SendRoute = "/v1/notifications/send";
    private const string SentRoute = "/v1/notifications/sent/";
    private const string InboxRoute = "/v1/notifications/inbox";
    private const string UnreadCountRoute = "/v1/notifications/inbox/unread-count";
    private const string MarkReadRoute = "/v1/notifications/inbox/note-1/read";
    private const string ChannelsRoute = "/v1/notifications/channels";
    private const string ChannelTestRoute = "/v1/notifications/channels/email/test";
    private const string ConfigRoute = "/v1/notifications/config";

    private readonly MockVaultFixture vault;

    public NotificationsSamples(MockVaultFixture vault)
    {
        this.vault = vault;
    }

    [Fact]
    public void The_policy_this_guide_needs_is_the_policy_PolicyBuilder_builds()
    {
        // docs:begin notifications/policy
        string hcl = new PolicyBuilder()
            .AddPath("notifications/send", [Capability.Update])
            .AddPath("notifications/sent/", [Capability.List])
            .AddPath("notifications/inbox", [Capability.Read])
            .AddPath("notifications/inbox/unread-count", [Capability.Read])
            .AddPath("notifications/inbox/note-1/read", [Capability.Update])
            .AddPath("notifications/channels", [Capability.Read])
            .AddPath("notifications/channels/email/test", [Capability.Update])
            .AddPath("notifications/config", [Capability.Create, Capability.Read, Capability.Update])
            .Build();

        Console.WriteLine(hcl);
        // docs:end notifications/policy

        Assert.Equal(PolicyShownInTheGuide(), hcl.Trim());
    }

    private static string PolicyShownInTheGuide()
    {
        string path = Path.Combine(DocsRepository.Docs.FullName, "dotnet", "engines", "notifications.md");
        string[] lines = File.ReadAllLines(path);
        int opening = Array.FindIndex(lines, line => line.TrimEnd() == "```hcl");
        Assert.True(opening >= 0, $"{path} shows no ```hcl policy block (DOC-011).");
        int closing = Array.FindIndex(lines, opening + 1, line => line.TrimEnd() == "```");
        Assert.True(closing > opening, $"{path}: the ```hcl block is never closed.");
        return string.Join('\n', lines[(opening + 1)..closing]).Trim();
    }

    [Fact]
    public async Task Step_1_send_a_notification_then_list_what_has_been_sent()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(SendRoute, Json(200, "{}"));
        vault.Server.SetRouteResponse(SentRoute, Json(200, SentBody()));

        // docs:begin notifications/send-and-sent
        await client.Notifications.SendAsync(new NotificationSendRequest
        {
            Title = "Certificate renewal failed",
            Body = "cert-lifecycle could not renew api.example.com; see the target's state.",
            Severity = "critical",
            Channels = ["email", "slack-ops"],
        });

        // 12 names no per-entry field set, so each entry stays a raw JsonElement (D-M1c-25).
        IReadOnlyList<JsonElement> sent = await client.Notifications.SentAsync();
        Console.WriteLine($"{sent.Count} notification(s) sent so far");
        // docs:end notifications/send-and-sent

        Assert.Single(sent);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task Step_2_read_the_inbox_and_mark_a_notification_read()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(InboxRoute, Json(200, InboxBody()));
        vault.Server.SetRouteResponse(UnreadCountRoute, Json(200, """{"data":{"count":1}}"""));
        vault.Server.SetRouteResponse(MarkReadRoute, Json(200, "{}"));

        // docs:begin notifications/inbox
        IReadOnlyList<JsonElement> inbox = await client.Notifications.Inbox.ListAsync();
        Console.WriteLine($"{inbox.Count} notification(s) in the inbox");

        IReadOnlyDictionary<string, JsonElement>? unread = await client.Notifications.Inbox.UnreadCountAsync();
        Console.WriteLine($"unread: {unread!["count"].GetInt32()}");

        await client.Notifications.Inbox.MarkReadAsync("note-1");
        // docs:end notifications/inbox

        Assert.Single(inbox);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task Step_3_list_channels_send_a_test_and_set_the_mount_config()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(ChannelsRoute, Json(200, ChannelsBody()));
        vault.Server.SetRouteResponse(ChannelTestRoute, Json(200, "{}"));
        vault.Server.SetRouteResponse(ConfigRoute, Json(200, "{}"));

        // docs:begin notifications/channels-and-config
        IReadOnlyList<JsonElement> channels = await client.Notifications.Channels.ListAsync();
        Console.WriteLine($"{channels.Count} channel(s) configured");

        await client.Notifications.Channels.TestAsync("email", to: "oncall@example.com");

        await client.Notifications.WriteConfigAsync(new NotificationsConfig
        {
            InboxCap = 500,
            PluginRatePerMin = 60,
        });
        // docs:end notifications/channels-and-config

        Assert.Single(channels);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task The_whole_program_sends_a_notification_and_checks_the_inbox()
    {
        vault.Server.ClearRouteResponses();

        // docs:begin notifications/complete
        using BastionVaultClient client = new();

        try
        {
            vault.Server.SetRouteResponse(SendRoute, Json(200, "{}"));
            await client.Notifications.SendAsync(new NotificationSendRequest { Title = "Certificate renewal failed", Severity = "critical" });
            Console.WriteLine("notification sent");

            vault.Server.SetRouteResponse(UnreadCountRoute, Json(200, """{"data":{"count":1}}"""));
            IReadOnlyDictionary<string, JsonElement>? unread = await client.Notifications.Inbox.UnreadCountAsync();
            Console.WriteLine($"unread: {unread!["count"].GetInt32()}");
        }
        catch (BastionVaultException e)
        {
            Console.Error.WriteLine($"{e.Code}: {e.Message} ({e.Hint}); retryable: {e.Retryable}");
            throw;
        }
        // docs:end notifications/complete

        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task What_can_go_wrong_maps_every_notifications_code_to_a_remedy()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        Exception failure = await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            // docs:begin notifications/handling-errors
            try
            {
                // Notifications carries no requirement ID of its own (DR-0017), so an empty
                // required `title` is a plain ArgumentException, not an invented BV-INPUT-001.
                await client.Notifications.SendAsync(new NotificationSendRequest { Title = string.Empty });
            }
            catch (ArgumentException)
            {
                Console.Error.WriteLine("title is required and cannot be empty or whitespace");
                throw;
            }
            catch (BastionVaultException e)
            {
                string remedy = e.Code switch
                {
                    ErrorCodes.NotFoundPathNotFound => "check the notification id, or Inbox.List() for what exists",
                    ErrorCodes.AuthzPermissionDenied => "extend the calling token's policy to cover this path",
                    _ => "look the code up in the error reference",
                };
                Console.Error.WriteLine($"{e.Code}: {e.Message} - {remedy}");
                throw;
            }
            // docs:end notifications/handling-errors
        });

        Assert.IsType<ArgumentException>(failure);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    private static MockResponse Json(int status, string body) => new(status, Body: Compact(body));

    private static string Compact(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    private static string SentBody() => Compact("""
        {"data":[{"title":"Certificate renewal failed","severity":"critical","sent_at":"2026-09-20T00:00:00Z"}]}
        """);

    private static string InboxBody() => Compact("""
        {"data":[{"id":"note-1","title":"Certificate renewal failed","read":false}]}
        """);

    private static string ChannelsBody() => Compact("""
        {"data":[{"channel":"email","enabled":true}]}
        """);
}
