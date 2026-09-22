using System.Text.Json;

namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// The wire reading and serialising <see cref="NotificationsOperations"/> and its nested
/// operations share (12 §Notifications), mirroring <see cref="PkiWire"/>'s precedent (D-M9-3).
/// </summary>
internal static class NotificationsWire
{
    /// <summary>Serialises a <see cref="NotificationSendRequest"/> body.</summary>
    public static ReadOnlyMemory<byte> SerialiseSendRequest(NotificationSendRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return KvWire.Serialise(writer =>
        {
            writer.WriteString("title", request.Title);
            if (request.Body is { } body)
            {
                writer.WriteString("body", body);
            }

            if (request.Severity is { } severity)
            {
                writer.WriteString("severity", severity);
            }

            WriteStringArray(writer, "channels", request.Channels);
            if (request.ActionUrl is { } actionUrl)
            {
                writer.WriteString("action_url", actionUrl);
            }

            if (request.Target is { } target)
            {
                KvWire.WriteMap(writer, "target", target);
            }

            if (request.Metadata is { } metadata)
            {
                KvWire.WriteMap(writer, "metadata", metadata);
            }
        });
    }

    /// <summary>Serialises a <see cref="NotificationsConfig"/> body (OVR-007: absent members are omitted, never sent empty).</summary>
    public static ReadOnlyMemory<byte> SerialiseConfig(NotificationsConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return KvWire.Serialise(writer =>
        {
            if (config.InboxCap is { } inboxCap)
            {
                writer.WriteNumber("inbox_cap", inboxCap);
            }

            if (config.PluginRatePerMin is { } pluginRatePerMin)
            {
                writer.WriteNumber("plugin_rate_per_min", pluginRatePerMin);
            }
        });
    }

    /// <summary>Reads a <see cref="NotificationsConfig"/> body back.</summary>
    public static NotificationsConfig ReadConfig(IReadOnlyDictionary<string, JsonElement> wire)
    {
        return new NotificationsConfig
        {
            InboxCap = KvWire.ReadInt(wire, "inbox_cap"),
            PluginRatePerMin = KvWire.ReadInt(wire, "plugin_rate_per_min"),
        };
    }

    private static void WriteStringArray(Utf8JsonWriter writer, string name, IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return;
        }

        writer.WriteStartArray(name);
        foreach (string value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }
}
