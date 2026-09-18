using System.Text.Json;

namespace BastionVault.IntegrationSdk;

/// <summary>One row of <c>GET sys/audit</c>'s <c>devices</c> array (SYS-070).</summary>
public sealed class AuditDevice
{
    /// <summary>The wire <c>path</c> field, in SYS-022's table form (a single trailing <c>/</c>).</summary>
    public required string Path { get; init; }

    /// <summary>The wire <c>type</c> field: the audit backend (<c>file</c>, <c>syslog</c>, …).</summary>
    public required string Type { get; init; }

    /// <summary>The wire <c>description</c> field; <see langword="null"/> when the server omits it.</summary>
    public string? Description { get; init; }

    /// <summary>The wire <c>namespace</c> field: the namespace the device is registered in. <c>""</c> denotes root.</summary>
    public string? Namespace { get; init; }

    /// <summary>The wire <c>mirror</c> field: whether the device mirrors another namespace's stream.</summary>
    public bool Mirror { get; init; }
}

/// <summary>The body of <c>Sys.Audit.EnableDevice</c> (SYS-070).</summary>
public sealed class AuditDeviceSpec
{
    /// <summary>The wire <c>type</c> field: the audit backend to enable. Required.</summary>
    public required string Type { get; init; }

    /// <summary>The wire <c>description</c> field; omitted from the body when unset.</summary>
    public string? Description { get; init; }

    /// <summary>The wire <c>options</c> map (backend-specific, e.g. <c>file_path</c>); omitted when unset or empty.</summary>
    public IReadOnlyDictionary<string, string>? Options { get; init; }

    /// <summary>
    /// The wire <c>mirror</c> field. Tri-state on purpose: <see langword="null"/> omits the key and
    /// lets the server default it, which is a different request from sending
    /// <see langword="false"/> explicitly.
    /// </summary>
    public bool? Mirror { get; init; }
}

/// <summary>One row of <c>GET sys/audit/events</c>'s <c>events</c> array (SYS-070).</summary>
public sealed class AuditEvent
{
    /// <summary>The wire <c>ts</c> field, parsed as RFC 3339; <see langword="null"/> when absent or unparsable.</summary>
    public DateTimeOffset? Timestamp { get; init; }

    /// <summary>The wire <c>user</c> field.</summary>
    public string? User { get; init; }

    /// <summary>The wire <c>machine</c> field. Optional on the wire and optional here (SYS-070's table marks it <c>machine?</c>).</summary>
    public string? Machine { get; init; }

    /// <summary>The wire <c>op</c> field.</summary>
    public string? Op { get; init; }

    /// <summary>The wire <c>category</c> field.</summary>
    public string? Category { get; init; }

    /// <summary>The wire <c>target</c> field.</summary>
    public string? Target { get; init; }

    /// <summary>The wire <c>changed_fields</c> array; empty when the server omits it.</summary>
    public required IReadOnlyList<string> ChangedFields { get; init; }

    /// <summary>The wire <c>summary</c> field.</summary>
    public string? Summary { get; init; }

    /// <summary>The whole event object as sent, so a field this type does not name is still reachable.</summary>
    public JsonElement Raw { get; init; }
}
