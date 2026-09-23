using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// SYS-070's audit surface, reached from <see cref="SysOperations.Audit"/>: the device registry
/// (<c>sys/audit</c>, <c>sys/audit/{path}</c>) and the event query (<c>sys/audit/events</c>).
/// </summary>
/// <remarks>
/// ⚠️ <see cref="EventsAsync"/> returns the server's order, which SYS-070 states is
/// <b>newest first</b>. The SDK does not re-sort: a client-side sort would silently disagree with
/// the server whenever two events share a timestamp, and the requirement is a statement about the
/// wire, not an instruction to the client.
/// </remarks>
public sealed class AuditOperations
{
    /// <summary>SYS-070's stated default for <c>Sys.Audit.Events</c>.</summary>
    private const int DefaultEventLimit = 500;

    private readonly LogicalOperations logical;

    internal AuditOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
    }

    /// <summary>06 — system API, "Audit": <c>GET sys/audit</c> → the <c>devices</c> array. An absent or non-array <c>devices</c> is an empty registry, not a protocol failure.</summary>
    /// <remarks>Wire params: none. Returns a list of <see cref="AuditDevice"/>, never <see langword="null"/> (empty when no devices are registered). Conformance: Complete (Appendix A groups this mount, no per-operation row; SYS-070's MUST governs <see cref="EventsAsync"/>'s from/to/limit handling, not this listing). Errors beyond the common set (ERR-061): none.</remarks>
    /// <spec>Sys.Audit.ListDevices — 06-system-api.md</spec>
    public async Task<IReadOnlyList<AuditDevice>> ListDevicesAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "GET", "sys/audit", null, options, defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken).ConfigureAwait(false);

        if (response?.Data is not { } data
            || !data.TryGetValue("devices", out JsonElement devices)
            || devices.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. devices.EnumerateArray()
            .Where(device => device.ValueKind == JsonValueKind.Object)
            .Select(device => SysWire.AsMap(device))
            .Select(device => new AuditDevice
            {
                // SYS-022's table form, applied to the audit registry for the same reason it is
                // applied to the mount table: the server keys both by a path with a trailing `/`,
                // and a caller comparing `ListDevices()` output to the argument it passed
                // `EnableDevice` must not have to know which form each end used.
                Path = MountPaths.NormaliseServerKey(SysWire.ReadString(device, "path") ?? string.Empty, stripAuthPrefix: false),
                Type = SysWire.ReadString(device, "type") ?? string.Empty,
                Description = SysWire.ReadString(device, "description"),
                Namespace = SysWire.ReadString(device, "namespace"),
                Mirror = device.TryGetValue("mirror", out JsonElement mirror) && mirror.ValueKind == JsonValueKind.True,
            })];
    }

    /// <summary>
    /// SYS-070: <c>POST sys/audit/{path}</c> → <c>204</c>. The path is accepted with or without a
    /// trailing <c>/</c> and an empty one is <c>BV-INPUT-001</c> client-side, exactly as SYS-022's
    /// mount paths are (<c>MountPaths.ToWire</c>, D-M7-7 — no second normaliser).
    /// </summary>
    /// <remarks>Wire params: <c>type</c>, <c>description</c>, <c>options</c>, <c>mirror</c>, from <paramref name="spec"/>. Returns nothing. Conformance: Complete (Appendix A groups this mount, no per-operation row; SYS-070's MUST governs <see cref="EventsAsync"/>'s from/to/limit handling, not this write). Errors beyond the common set (ERR-061): <c>BV-INPUT-001</c> for an empty <paramref name="path"/>.</remarks>
    /// <spec>Sys.Audit.EnableDevice — 06-system-api.md</spec>
    public async Task EnableDeviceAsync(string path, AuditDeviceSpec spec, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        string wire = MountPaths.ToWire(path, "path");
        _ = await logical.ExecuteShapedAsync(
            "POST", $"sys/audit/{UrlBuilder.EncodePathSegment(wire)}", SerialiseDevice(spec), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>06 — system API, "Audit": <c>DELETE sys/audit/{path}</c> → <c>204</c>.</summary>
    /// <remarks>Wire params: none. Returns nothing. Conformance: Complete (Appendix A groups this mount, no per-operation row; SYS-070's MUST governs <see cref="EventsAsync"/>'s from/to/limit handling, not this delete). Errors beyond the common set (ERR-061): <c>BV-INPUT-001</c> for an empty <paramref name="path"/>.</remarks>
    /// <spec>Sys.Audit.DisableDevice — 06-system-api.md</spec>
    public async Task DisableDeviceAsync(string path, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        string wire = MountPaths.ToWire(path, "path");
        _ = await logical.ExecuteShapedAsync(
            "DELETE", $"sys/audit/{UrlBuilder.EncodePathSegment(wire)}", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>
    /// SYS-070: <c>GET sys/audit/events?from=&amp;to=&amp;limit=</c> → the <c>events</c> array,
    /// newest first as the server orders it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="from"/> and <paramref name="to"/> are serialised as <b>RFC 3339 UTC</b> —
    /// converted to UTC first, then formatted with a literal <c>Z</c> — and percent-encoded onto
    /// the query string. Converting rather than rejecting a non-UTC
    /// <see cref="DateTimeOffset"/> is deliberate: the type carries its offset, so the conversion
    /// is lossless and information-preserving, where a refusal would make a perfectly
    /// unambiguous instant an error.
    /// </para>
    /// <para>
    /// <paramref name="limit"/> is validated <c>≥ 1</c> client-side with <c>BV-INPUT-004</c>
    /// (SYS-070). There is <b>no</b> upper bound here: PAG-001's <c>1…500</c> range governs the
    /// <c>*-info</c> cursor listings and SYS-070 names only the lower bound, so capping this one
    /// at 500 would be a client-side refusal of a request the requirement does not refuse
    /// (D-M1c-25).
    /// </para>
    /// <para>Wire params: <c>from</c>, <c>to</c> (RFC 3339 UTC query values, when supplied), <c>limit</c>. Returns a list of <see cref="AuditEvent"/>, never <see langword="null"/> (empty when there are no events). Conformance: Complete (SYS-070, PAG-001). Errors beyond the common set (ERR-061): <c>BV-INPUT-004</c> for <paramref name="limit"/> &lt; 1.</para>
    /// </remarks>
    /// <spec>Sys.Audit.Events — SYS-070</spec>
    public async Task<IReadOnlyList<AuditEvent>> EventsAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int limit = DefaultEventLimit,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (limit < 1)
        {
            throw SysWire.OutOfRange("limit", limit);
        }

        StringBuilder query = new();
        if (from is { } fromValue)
        {
            _ = query.Append("from=").Append(UrlBuilder.EncodeQueryValue(SysWire.ToRfc3339Utc(fromValue))).Append('&');
        }

        if (to is { } toValue)
        {
            _ = query.Append("to=").Append(UrlBuilder.EncodeQueryValue(SysWire.ToRfc3339Utc(toValue))).Append('&');
        }

        _ = query.Append("limit=").Append(limit.ToString(CultureInfo.InvariantCulture));

        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"sys/audit/events?{query}", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);

        if (response?.Data is not { } data
            || !data.TryGetValue("events", out JsonElement events)
            || events.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. events.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(ToEvent)];
    }

    private static AuditEvent ToEvent(JsonElement item)
    {
        Dictionary<string, JsonElement> fields = SysWire.AsMap(item);
        return new AuditEvent
        {
            Timestamp = SysWire.ReadRfc3339(fields, "ts"),
            User = SysWire.ReadString(fields, "user"),
            Machine = SysWire.ReadString(fields, "machine"),
            Op = SysWire.ReadString(fields, "op"),
            Category = SysWire.ReadString(fields, "category"),
            Target = SysWire.ReadString(fields, "target"),
            ChangedFields = SysWire.ReadStringArray(fields, "changed_fields"),
            Summary = SysWire.ReadString(fields, "summary"),
            Raw = item.Clone(),
        };
    }

    private static ReadOnlyMemory<byte> SerialiseDevice(AuditDeviceSpec spec)
    {
        ArrayBufferWriter<byte> buffer = new();
        using Utf8JsonWriter writer = new(buffer);
        writer.WriteStartObject();
        writer.WriteString("type", spec.Type);
        if (spec.Description is { } description)
        {
            writer.WriteString("description", description);
        }

        if (spec.Options is { Count: > 0 } options)
        {
            writer.WriteStartObject("options");
            foreach (KeyValuePair<string, string> option in options)
            {
                writer.WriteString(option.Key, option.Value);
            }

            writer.WriteEndObject();
        }

        if (spec.Mirror is { } mirror)
        {
            writer.WriteBoolean("mirror", mirror);
        }

        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenMemory;
    }
}
