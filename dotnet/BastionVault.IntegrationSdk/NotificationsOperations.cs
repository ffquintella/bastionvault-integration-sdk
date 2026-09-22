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

    /// <summary>
    /// <c>POST {mount}/send</c>. <c>title</c> is required; Notifications carries no requirement
    /// ID of its own (DR-0017), so this follows <see cref="PkiOperations.SignAsync"/>'s precedent
    /// for a spec <c>(req)</c> field with no ID behind it — a plain <see cref="ArgumentException"/>,
    /// not an invented <c>BV-INPUT-001</c> recognition.
    /// </summary>
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
    public async Task<IReadOnlyList<JsonElement>> SentAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{Encode(mount)}/sent/", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return IdentityKernelWire.ReadArrayEnvelope(response);
    }

    /// <summary><c>GET {mount}/config</c>.</summary>
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
    public async Task<IReadOnlyList<JsonElement>> ListAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{Encode(mount)}/inbox", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return IdentityKernelWire.ReadArrayEnvelope(response);
    }

    /// <summary><c>GET {mount}/inbox/unread-count</c>. 12 names no field for the count itself, so the untyped map fallback applies (D-M1c-25).</summary>
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
    public async Task ReadAllAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{Encode(mount)}/inbox/read-all", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>DELETE {mount}/inbox/{id}</c>.</summary>
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
    public async Task<IReadOnlyList<JsonElement>> ListAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{Encode(mount)}/channels", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return IdentityKernelWire.ReadArrayEnvelope(response);
    }

    /// <summary><c>POST {mount}/channels/{channel}/test</c> with <c>{"to": to}</c>.</summary>
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
