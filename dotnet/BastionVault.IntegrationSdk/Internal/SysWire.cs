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

    public static DateTimeOffset? ReadRfc3339(IReadOnlyDictionary<string, JsonElement> data, string name)
    {
        return data.TryGetValue(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(value.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)
            ? parsed
            : null;
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
