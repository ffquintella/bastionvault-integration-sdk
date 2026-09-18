using System.Text.Json;

namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// The wire parsing M7b's policy and namespace surfaces share, written once so the
/// <c>policies/acl</c> surface and the legacy <c>sys/policy</c> surface cannot drift apart on
/// SYS-040's "whichever key is present", and so the <c>Namespace</c> record is read the same way
/// whether it arrives alone (<c>Sys.ReadNamespace</c>) or inside a page
/// (<c>Sys.ListNamespacesInfo</c>).
/// </summary>
internal static class SysWire
{
    /// <summary>
    /// SYS-040: <see cref="Policy.Hcl"/> comes from <c>policy</c> when the <c>policies/acl</c>
    /// surface answered and from <c>rules</c> when the legacy one did. Both are checked, in that
    /// order, because a caller must not have to know which surface it read from. A body carrying
    /// neither key is an empty document, not a <see langword="null"/> one.
    /// </summary>
    public static Policy ToPolicy(IReadOnlyDictionary<string, JsonElement> data, string requestedName)
    {
        return new Policy
        {
            Name = ReadString(data, "name") ?? requestedName,
            Hcl = ReadString(data, "policy") ?? ReadString(data, "rules") ?? string.Empty,
        };
    }

    /// <summary>The <c>{"keys": [...]}</c> shape every <c>sys</c> listing uses. An absent or non-array <c>keys</c> is an empty listing, matching TRN-050's treatment of an empty list.</summary>
    public static IReadOnlyList<string> ReadKeys(IReadOnlyDictionary<string, JsonElement>? data)
    {
        if (data is null || !data.TryGetValue("keys", out JsonElement keys) || keys.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. keys.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)];
    }

    /// <summary>Reads an array of strings, returning an empty list when the field is absent or is not an array.</summary>
    public static IReadOnlyList<string> ReadStringArray(IReadOnlyDictionary<string, JsonElement> data, string name)
    {
        if (!data.TryGetValue(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)];
    }

    /// <summary>SYS-060's record. A missing <c>quotas</c> object is all-unlimited, which is what <c>0</c> means in every one of the six.</summary>
    public static Namespace ToNamespace(IReadOnlyDictionary<string, JsonElement> data, string fallbackPath)
    {
        IReadOnlyDictionary<string, JsonElement> quotas = data.TryGetValue("quotas", out JsonElement element) && element.ValueKind == JsonValueKind.Object
            ? AsMap(element)
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        return new Namespace
        {
            Uuid = ReadString(data, "uuid") ?? string.Empty,
            Path = ReadString(data, "path") ?? fallbackPath,
            ParentUuid = ReadString(data, "parent_uuid"),
            CreatedAt = ReadRfc3339(data, "created_at"),
            ChildVisibleDefault = data.TryGetValue("child_visible_default", out JsonElement visible) && visible.ValueKind == JsonValueKind.True,
            Quotas = new NamespaceQuotas
            {
                MaxStorageBytes = ReadLong(quotas, "max_storage_bytes"),
                MaxLeases = ReadLong(quotas, "max_leases"),
                RequestRate = ReadLong(quotas, "request_rate"),
                MaxMounts = ReadLong(quotas, "max_mounts"),
                MaxEntities = ReadLong(quotas, "max_entities"),
                MaxChildNamespaces = ReadLong(quotas, "max_child_namespaces"),
            },
        };
    }

    public static Dictionary<string, JsonElement> AsMap(JsonElement element)
    {
        return element.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
    }

    public static string? ReadString(IReadOnlyDictionary<string, JsonElement> data, string name)
    {
        return data.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    public static long ReadLong(IReadOnlyDictionary<string, JsonElement> data, string name)
    {
        return data.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : 0L;
    }

    /// <summary>A number that may be absent, where <c>0</c> is a meaningful value and must not stand in for "unset".</summary>
    public static long? ReadNullableLong(IReadOnlyDictionary<string, JsonElement> data, string name)
    {
        return data.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : null;
    }

    public static DateTimeOffset? ReadRfc3339(IReadOnlyDictionary<string, JsonElement> data, string name)
    {
        return data.TryGetValue(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(value.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// SYS-070's <c>from</c>/<c>to</c>: RFC 3339, in UTC, with a literal <c>Z</c>. Converted rather
    /// than refused when the caller's offset is not UTC — a <see cref="DateTimeOffset"/> names an
    /// unambiguous instant, so the conversion loses nothing that the wire format could carry.
    /// Invariant culture, so a client running under a non-Gregorian calendar sends the same bytes.
    /// </summary>
    public static string ToRfc3339Utc(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Serialises a caller-supplied <see cref="JsonElement"/> request body, refusing a
    /// <see cref="JsonValueKind.Undefined"/> one client-side with <c>BV-INPUT-001</c>.
    /// </summary>
    /// <remarks>
    /// <c>default(JsonElement)</c> is <c>Undefined</c>, and <c>JsonSerializer</c> answers it with
    /// an <see cref="InvalidOperationException"/> — a bare runtime exception, which ERR-020 and
    /// TRN-054 forbid a caller from ever having to catch. Every operation that forwards an
    /// unmodelled body (the owner-transfer and exchange surfaces) goes through here, so the
    /// refusal cannot be present on one route and missing on another.
    /// </remarks>
    public static ReadOnlyMemory<byte> RequireJsonBody(JsonElement body, string argument)
    {
        if (body.ValueKind == JsonValueKind.Undefined)
        {
            ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.InputInvalidArgument);
            throw BastionVaultException.Request(
                ErrorCodes.InputInvalidArgument,
                entry.Category,
                entry.Message,
                entry.Hint,
                retryable: false,
                attempts: 0,
                details: new Dictionary<string, object?>(StringComparer.Ordinal) { ["argument"] = argument });
        }

        return JsonSerializer.SerializeToUtf8Bytes(body);
    }

    /// <summary>
    /// <c>BV-INPUT-004 OutOfRange</c>, raised client-side with zero attempts. Shared by SYS-070's
    /// <c>limit ≥ 1</c> and PAG-001's <c>1…500</c> so the two cannot report the same kind of
    /// mistake with two different shapes of <c>Details</c>.
    /// </summary>
    public static BastionVaultException OutOfRange(string argument, int value)
    {
        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.InputOutOfRange);
        return BastionVaultException.Request(
            ErrorCodes.InputOutOfRange,
            entry.Category,
            entry.Message,
            entry.Hint,
            retryable: false,
            attempts: 0,
            details: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["argument"] = argument,
                [argument] = value,
            });
    }

    /// <summary>
    /// SYS-041's refusal, and SYS-045's for the dry-run's <c>name</c>. Client-side, zero attempts:
    /// the point of the requirement is that the request is never sent.
    /// </summary>
    public static BastionVaultException ReservedPolicyName(string name, string argument)
    {
        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.InputReservedPolicyName);
        return BastionVaultException.Request(
            ErrorCodes.InputReservedPolicyName,
            entry.Category,
            entry.Message,
            entry.Hint,
            retryable: false,
            attempts: 0,
            details: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["argument"] = argument,
                ["name"] = name,
            });
    }
}
