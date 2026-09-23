using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// 12 §Notifications (OVR-008), reached from <see cref="BastionVaultClient.Notifications"/>.
/// <c>mount</c> defaults to <c>"notifications"</c>. This area carries no requirement ID of its
/// own; every MUST governing it is the generic Shape A envelope and standard error mapping
/// (03/04) that already binds every route in this SDK (DR-0017).
/// </summary>
public sealed class NotificationsOperations
{
    private const string DefaultMount = "notifications";

    private readonly LogicalOperations logical;

    internal NotificationsOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
        Inbox = new NotificationsInboxOperations(logical);
        Channels = new NotificationsChannelsOperations(logical);
    }

    /// <summary>12 §Notifications: <c>{mount}/inbox/*</c>.</summary>
    public NotificationsInboxOperations Inbox { get; }

    /// <summary>12 §Notifications: <c>{mount}/channels/*</c>.</summary>
    public NotificationsChannelsOperations Channels { get; }

    /// <summary><c>POST {mount}/send</c>. <c>title</c> is required; a plain <see cref="ArgumentException"/> (Notifications carries no requirement ID of its own, DR-0017), not an invented <c>BV-INPUT-001</c> recognition.</summary>
    /// <remarks>Wire params: title (req), body, severity, channels[], action_url, target{}, metadata{}. Returns <see langword="void"/>. <see cref="NotificationSendRequest.Target"/>/<see cref="NotificationSendRequest.ActionUrl"/> can carry a per-send destination the channel plugin dispatches to — an egress surface the SDK never inspects or logs. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Notifications.Send — 12-other-engines-and-identity.md</spec>
    public async Task SendAsync(
        NotificationSendRequest request, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Title);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{Encode(mount)}/send";
        _ = await logical.ExecuteShapedAsync(
            "POST", path, NotificationsWire.SerialiseSendRequest(request), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>GET {mount}/sent/</c>. 12 names no per-entry field set, so each entry stays a raw <see cref="JsonElement"/> (D-M1c-25).</summary>
    /// <remarks>Wire params: none. Returns the sent-notification entries, empty (never <see langword="null"/>) when none exist or the path is absent; entries are untyped and may echo a prior send's target/action_url — treat as sensitive. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Notifications.Sent — 12-other-engines-and-identity.md</spec>
    public async Task<IReadOnlyList<JsonElement>> SentAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{Encode(mount)}/sent/", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return IdentityKernelWire.ReadArrayEnvelope(response, nestedKey: "notifications");
    }

    /// <summary><c>GET {mount}/config</c>.</summary>
    /// <remarks>Wire params: none. Returns <see cref="NotificationsConfig"/>, or <see langword="null"/> when no config is set (404 treated as absent). Carries no credential or destination material (inbox_cap, plugin_rate_per_min only). Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Notifications.ReadConfig — 12-other-engines-and-identity.md</spec>
    public async Task<NotificationsConfig?> ReadConfigAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{Encode(mount)}/config", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data is { } data ? NotificationsWire.ReadConfig(data) : null;
    }

    /// <summary><c>POST {mount}/config</c>.</summary>
    /// <remarks>Wire params (patch-shaped, omitted members untouched): inbox_cap, plugin_rate_per_min. Returns <see langword="void"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Notifications.WriteConfig — 12-other-engines-and-identity.md</spec>
    public async Task WriteConfigAsync(
        NotificationsConfig config, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{Encode(mount)}/config", NotificationsWire.SerialiseConfig(config), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    private static string Encode(string mount)
    {
        return UrlBuilder.EncodePathFragment(mount.Trim('/'));
    }
}

/// <summary>12 §Notifications: <c>{mount}/inbox/*</c>, reached from <see cref="NotificationsOperations.Inbox"/>.</summary>
public sealed class NotificationsInboxOperations
{
    private const string DefaultMount = "notifications";

    private readonly LogicalOperations logical;

    internal NotificationsInboxOperations(LogicalOperations logical)
    {
        this.logical = logical;
    }

    /// <summary><c>GET {mount}/inbox</c>. 12 names no per-entry field set, so each entry stays a raw <see cref="JsonElement"/> (D-M1c-25).</summary>
    /// <remarks>Wire params: none. Returns the inbox entries, empty (never <see langword="null"/>) when none exist or the path is absent. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Notifications.Inbox.List — 12-other-engines-and-identity.md</spec>
    public async Task<IReadOnlyList<JsonElement>> ListAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{Encode(mount)}/inbox", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return IdentityKernelWire.ReadArrayEnvelope(response, nestedKey: "notifications");
    }

    /// <summary><c>GET {mount}/inbox/unread-count</c>. 12 names no field for the count itself, so the untyped map fallback applies (D-M1c-25).</summary>
    /// <remarks>Wire params: none. Returns the raw wire map, or <see langword="null"/> when the path is absent (404 treated as absent) or the body is empty. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Notifications.Inbox.UnreadCount — 12-other-engines-and-identity.md</spec>
    public async Task<IReadOnlyDictionary<string, JsonElement>?> UnreadCountAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{Encode(mount)}/inbox/unread-count", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }

    /// <summary><c>POST {mount}/inbox/{id}/read</c>.</summary>
    /// <remarks>Wire params: none beyond <paramref name="id"/>/<paramref name="mount"/>. Returns <see langword="void"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Notifications.Inbox.MarkRead — 12-other-engines-and-identity.md</spec>
    public async Task MarkReadAsync(
        string id, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{ItemPath(mount, id)}/read", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>POST {mount}/inbox/read-all</c>.</summary>
    /// <remarks>Wire params: none. Returns <see langword="void"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Notifications.Inbox.ReadAll — 12-other-engines-and-identity.md</spec>
    public async Task ReadAllAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{Encode(mount)}/inbox/read-all", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>DELETE {mount}/inbox/{id}</c>.</summary>
    /// <remarks>Wire params: none beyond <paramref name="id"/>/<paramref name="mount"/>. Returns <see langword="void"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Notifications.Inbox.Dismiss — 12-other-engines-and-identity.md</spec>
    public async Task DismissAsync(
        string id, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "DELETE", ItemPath(mount, id), null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    private static string ItemPath(string mount, string id)
    {
        return $"{Encode(mount)}/inbox/{UrlBuilder.EncodePathSegment(id)}";
    }

    private static string Encode(string mount)
    {
        return UrlBuilder.EncodePathFragment(mount.Trim('/'));
    }
}

/// <summary>12 §Notifications: <c>{mount}/channels/*</c>, reached from <see cref="NotificationsOperations.Channels"/>.</summary>
public sealed class NotificationsChannelsOperations
{
    private const string DefaultMount = "notifications";

    private readonly LogicalOperations logical;

    internal NotificationsChannelsOperations(LogicalOperations logical)
    {
        this.logical = logical;
    }

    /// <summary><c>GET {mount}/channels</c>. 12 names no per-entry field set, so each entry stays a raw <see cref="JsonElement"/> (D-M1c-25).</summary>
    /// <remarks>Wire params: none. Returns the configured channels, empty (never <see langword="null"/>) when none exist or the path is absent; an entry can carry the channel's configured destination (email/webhook/etc.) — treat as sensitive, do not log unredacted. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Notifications.Channels.List — 12-other-engines-and-identity.md</spec>
    public async Task<IReadOnlyList<JsonElement>> ListAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{Encode(mount)}/channels", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return IdentityKernelWire.ReadArrayEnvelope(response, nestedKey: "channels");
    }

    /// <summary><c>POST {mount}/channels/{channel}/test</c> with <c>{"to": to}</c>.</summary>
    /// <remarks>Wire params: to (req). Returns <see langword="void"/>. <paramref name="to"/> is the destination address this call sends a live test notification to — an egress surface; the SDK never inspects or logs its value. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Notifications.Channels.Test — 12-other-engines-and-identity.md</spec>
    public async Task TestAsync(
        string channel, string to, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(channel);
        ArgumentException.ThrowIfNullOrEmpty(to);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer => writer.WriteString("to", to));
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{Encode(mount)}/channels/{UrlBuilder.EncodePathSegment(channel)}/test", body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    private static string Encode(string mount)
    {
        return UrlBuilder.EncodePathFragment(mount.Trim('/'));
    }
}
