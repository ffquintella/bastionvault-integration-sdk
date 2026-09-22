using System.Text.Json;

namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// Path building, base64 handling (FIL-001) and serialisation for M10 slice b's <c>Files</c>
/// surface (12 §Files): <c>{mount}/files/*</c> and <c>{mount}/sync-tick</c>.
/// </summary>
internal static class FileWire
{
    public static string Encode(string mount)
    {
        return UrlBuilder.EncodePathFragment(mount.Trim('/'));
    }

    public static string FilePath(string mount, string id)
    {
        return $"{Encode(mount)}/files/{UrlBuilder.EncodePathSegment(id)}";
    }

    public static ReadOnlyMemory<byte> SerialiseCreate(FileCreateRequest request)
    {
        return KvWire.Serialise(writer =>
        {
            writer.WriteString("name", request.Name);
            if (request.Resource is { } resource)
            {
                writer.WriteString("resource", resource);
            }

            if (request.MimeType is { } mimeType)
            {
                writer.WriteString("mime_type", mimeType);
            }

            WriteTagsIfPresent(writer, request.Tags);
            if (request.Notes is { } notes)
            {
                writer.WriteString("notes", notes);
            }

            // FIL-001: encoded here, into the same body TRN-032's 32 MiB check already measures
            // (Internal/RequestExecutor.cs's GuardInputPreflight) — the limit is enforced on the
            // encoded bytes because the check runs on this body, not on request.Content.Length.
            writer.WriteString("content_base64", Convert.ToBase64String(request.Content.Span));
        });
    }

    public static ReadOnlyMemory<byte> SerialiseUpdate(FileUpdateRequest request)
    {
        return KvWire.Serialise(writer =>
        {
            if (request.Name is { } name)
            {
                writer.WriteString("name", name);
            }

            if (request.Resource is { } resource)
            {
                writer.WriteString("resource", resource);
            }

            if (request.MimeType is { } mimeType)
            {
                writer.WriteString("mime_type", mimeType);
            }

            WriteTagsIfPresent(writer, request.Tags);
            if (request.Notes is { } notes)
            {
                writer.WriteString("notes", notes);
            }
        });
    }

    private static void WriteTagsIfPresent(Utf8JsonWriter writer, IReadOnlyList<string>? tags)
    {
        if (tags is null)
        {
            return;
        }

        writer.WriteStartArray("tags");
        foreach (string tag in tags)
        {
            writer.WriteStringValue(tag);
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// FIL-001: the SDK decodes <c>content_base64</c> itself; the caller only ever sees bytes. A
    /// missing, null or non-string field, and malformed base64, all raise the same
    /// <c>BV-PROTOCOL-002</c> envelope mismatch every neighbouring reader raises — never a bare
    /// <see cref="FormatException"/> escaping the error model (ERR-020, mirrors
    /// <see cref="TransitWire.RequireBase64Decoded"/>).
    /// </summary>
    public static byte[] ReadContent(IReadOnlyDictionary<string, JsonElement>? data, string path)
    {
        if (data is null
            || !data.TryGetValue("content_base64", out JsonElement element)
            || element.ValueKind != JsonValueKind.String)
        {
            throw KvWire.EnvelopeMismatch(path, "content_base64");
        }

        return TransitWire.RequireBase64Decoded(element.GetString() ?? string.Empty, path, "content_base64");
    }

    public static ReadOnlyMemory<byte> SerialiseRepointResource(string oldResource, string newResource)
    {
        return KvWire.Serialise(writer =>
        {
            writer.WriteString("old_resource", oldResource);
            writer.WriteString("new_resource", newResource);
        });
    }

    /// <summary>
    /// A non-object <see cref="SyncTarget.Fields"/> is refused rather than silently dropped, and a
    /// <c>kind</c> key inside it is refused rather than silently shadowing
    /// <see cref="SyncTarget.Kind"/> on the wire (<see cref="Utf8JsonWriter"/> does not deduplicate
    /// property names) — both <c>BV-INPUT-001</c>, matching <see cref="SysWire.RequireJsonBody"/>'s
    /// client-side-refusal idiom.
    /// </summary>
    public static void RequireValidSyncFields(JsonElement? fields, string path)
    {
        if (fields is not { } value)
        {
            return;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            throw KvWire.InvalidArgument("target.fields", "must be a JSON object when set", path);
        }

        if (value.TryGetProperty("kind", out _))
        {
            throw KvWire.InvalidArgument("target.fields", "must not contain a `kind` key; it would shadow SyncTarget.Kind on the wire", path);
        }
    }

    public static ReadOnlyMemory<byte> SerialiseSyncTarget(SyncTarget target, string path)
    {
        RequireValidSyncFields(target.Fields, path);
        return KvWire.Serialise(writer =>
        {
            writer.WriteString("kind", target.Kind);
            if (target.Fields is { } fields)
            {
                foreach (JsonProperty property in fields.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    property.Value.WriteTo(writer);
                }
            }
        });
    }
}
