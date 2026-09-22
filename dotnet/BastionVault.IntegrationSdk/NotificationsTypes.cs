using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// 12 §Notifications: <c>Notifications.Send</c>'s request body (<c>POST {mount}/send</c>).
/// <see cref="Target"/> and <see cref="Metadata"/> carry no documented field set, so both stay
/// raw wire maps rather than a guessed shape (D-M1c-25).
/// </summary>
public sealed class NotificationSendRequest
{
    /// <summary>The wire <c>title</c> field. Required; an empty or whitespace-only value is refused client-side with an <see cref="ArgumentException"/> before any request is sent (Notifications carries no requirement ID, DR-0017).</summary>
    public required string Title { get; init; }

    /// <summary>The wire <c>body</c> field.</summary>
    public string? Body { get; init; }

    /// <summary>The wire <c>severity</c> field: <c>info</c>, <c>success</c>, <c>warning</c>, or <c>critical</c>.</summary>
    public string? Severity { get; init; }

    /// <summary>The wire <c>channels</c> array.</summary>
    public IReadOnlyList<string>? Channels { get; init; }

    /// <summary>The wire <c>action_url</c> field.</summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "The SDK treats this value as opaque text sent to the server, not parsed. System.Uri has no counterpart in the Rust and Python SDKs, so typing it here would make the .NET signature the odd one out for no behavioural gain (CLA-003).")]
    public string? ActionUrl { get; init; }

    /// <summary>The wire <c>target</c> object.</summary>
    public IReadOnlyDictionary<string, JsonElement>? Target { get; init; }

    /// <summary>The wire <c>metadata</c> object.</summary>
    public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }
}

/// <summary>
/// 12 §Notifications: <c>Notifications.ReadConfig</c>/<c>WriteConfig</c>'s shared field list
/// (<c>{mount}/config</c>). Patch-shaped (OVR-007), like <see cref="PkiRole"/>.
/// </summary>
public sealed class NotificationsConfig
{
    /// <summary>The wire <c>inbox_cap</c> field.</summary>
    public int? InboxCap { get; init; }

    /// <summary>The wire <c>plugin_rate_per_min</c> field.</summary>
    public int? PluginRatePerMin { get; init; }
}
