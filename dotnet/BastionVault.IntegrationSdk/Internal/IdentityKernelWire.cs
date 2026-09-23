using System.Text.Json;

namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// Path building and wire parsing for M10 slice a's identity-kernel surface (12 — identity,
/// sharing, asset groups): <c>identity/group/*</c>, <c>identity/sharing/*</c> and
/// <c>identity/owner/*</c>. Kept separate from <see cref="IdentityWire"/>, which is SYS-080's own
/// <c>sys/identity/*</c> helper and is not touched by this slice (DR-0017 D-M10-2).
/// </summary>
internal static class IdentityKernelWire
{
    /// <summary>
    /// One caller-supplied path segment: non-empty, refused on a whole <c>..</c> segment
    /// (D-M4-11's guard, reused rather than re-derived), then percent-encoded (TRN-020).
    /// </summary>
    public static string EncodeSegment(string value, string argument)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        KvWire.RequireSafePath(value, argument, value);
        return UrlBuilder.EncodePathSegment(value);
    }

    // ---------------------------------------------------------------- groups

    public static string GroupListPath(string kind)
    {
        return $"identity/group/{EncodeSegment(kind, "kind")}";
    }

    public static string GroupPath(string kind, string name)
    {
        return $"{GroupListPath(kind)}/{EncodeSegment(name, "name")}";
    }

    public static string GroupHistoryPath(string kind, string name)
    {
        return $"{GroupPath(kind, name)}/history";
    }

    // ---------------------------------------------------------------- sharing

    public static string SharingByTargetPath(string kind, string target, string grantee)
    {
        ArgumentException.ThrowIfNullOrEmpty(target);
        return $"{SharingByTargetListPath(kind, target)}/{EncodeSegment(grantee, "grantee")}";
    }

    /// <summary>IDN-001: <paramref name="target"/> is base64url-encoded here, not by the caller.</summary>
    public static string SharingByTargetListPath(string kind, string target)
    {
        ArgumentException.ThrowIfNullOrEmpty(target);
        return $"identity/sharing/by-target/{EncodeSegment(kind, "kind")}/{Base64Url.Encode(target)}";
    }

    public static string SharingByGranteeListPath(string grantee)
    {
        return $"identity/sharing/by-grantee/{EncodeSegment(grantee, "grantee")}";
    }

    public const string SharingForMePath = "identity/sharing/for-me";

    // ---------------------------------------------------------------- owner

    public static string OwnerPath(string kind, string id)
    {
        return $"identity/owner/{EncodeSegment(kind, "kind")}/{EncodeSegment(id, "id")}";
    }

    // ---------------------------------------------------------------- parsing

    /// <summary>
    /// Any array this slice's routes return with no documented envelope beyond "an array":
    /// bare top-level (Shape B), <c>data</c> itself (Shape A), or — when <paramref name="nestedKey"/>
    /// is given — <c>data.&lt;nestedKey&gt;</c>. The key is explicit, not inferred: <c>certs-info</c>'s
    /// <c>data</c> carries two arrays, so "the one array under <c>data</c>" is not a safe guess.
    /// Each entry stays a raw <see cref="JsonElement"/> (D-M1c-25); no field inside one is named.
    /// </summary>
    public static IReadOnlyList<JsonElement> ReadArrayEnvelope(Response? response, string? nestedKey = null)
    {
        if (response is null)
        {
            return Array.Empty<JsonElement>();
        }

        JsonElement raw = response.Raw;
        if (raw.ValueKind == JsonValueKind.Array)
        {
            return raw.EnumerateArray().Select(item => item.Clone()).ToArray();
        }

        if (raw.ValueKind == JsonValueKind.Object
            && raw.TryGetProperty("data", out JsonElement dataElement))
        {
            if (dataElement.ValueKind == JsonValueKind.Array)
            {
                return dataElement.EnumerateArray().Select(item => item.Clone()).ToArray();
            }

            if (nestedKey is not null
                && dataElement.ValueKind == JsonValueKind.Object
                && dataElement.TryGetProperty(nestedKey, out JsonElement nestedElement)
                && nestedElement.ValueKind == JsonValueKind.Array)
            {
                return nestedElement.EnumerateArray().Select(item => item.Clone()).ToArray();
            }
        }

        return Array.Empty<JsonElement>();
    }

    public static IdentityGroup ReadGroup(IReadOnlyDictionary<string, JsonElement> data, JsonElement raw)
    {
        return new IdentityGroup
        {
            Description = SysWire.ReadString(data, "description"),
            Members = SysWire.ReadStringArray(data, "members"),
            Policies = SysWire.ReadStringArray(data, "policies"),
            Raw = raw,
        };
    }

    public static ReadOnlyMemory<byte> SerialiseGroupSpec(IdentityGroupSpec spec)
    {
        return KvWire.Serialise(writer =>
        {
            if (spec.Description is { } description)
            {
                writer.WriteString("description", description);
            }

            WriteStringArrayIfPresent(writer, "members", spec.Members);
            WriteStringArrayIfPresent(writer, "policies", spec.Policies);
        });
    }

    /// <summary>IDN-001's write spec: <c>target_kind</c>/<c>target_path</c> always sent, the rest omitted when unset.</summary>
    public static ReadOnlyMemory<byte> SerialiseSharingSpec(string targetKind, string targetPath, IdentitySharingSpec spec)
    {
        return KvWire.Serialise(writer =>
        {
            writer.WriteString("target_kind", targetKind);
            writer.WriteString("target_path", targetPath);
            if (spec.GranteeKind is { } granteeKind)
            {
                writer.WriteString("grantee_kind", granteeKind);
            }

            WriteStringArrayIfPresent(writer, "capabilities", spec.Capabilities);
            if (spec.ExpiresAt is { } expiresAt)
            {
                writer.WriteString("expires_at", SysWire.ToRfc3339Utc(expiresAt));
            }
        });
    }

    public static IdentitySharingForMe ReadForMe(IReadOnlyDictionary<string, JsonElement>? data, JsonElement raw)
    {
        if (data is null)
        {
            return new IdentitySharingForMe { Entries = Array.Empty<JsonElement>(), Raw = raw };
        }

        bool? groupSharedResources = data.TryGetValue("group_shared_resources", out JsonElement flag)
            && flag.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? flag.GetBoolean()
            : null;

        return new IdentitySharingForMe
        {
            EntityId = SysWire.ReadString(data, "entity_id"),
            GroupSharedResources = groupSharedResources,
            Entries = data.TryGetValue("entries", out JsonElement entries) && entries.ValueKind == JsonValueKind.Array
                ? entries.EnumerateArray().Select(item => item.Clone()).ToArray()
                : Array.Empty<JsonElement>(),
            Raw = raw,
        };
    }

    private static void WriteStringArrayIfPresent(Utf8JsonWriter writer, string name, IReadOnlyList<string>? values)
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
