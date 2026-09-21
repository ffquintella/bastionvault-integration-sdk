using System.Buffers;
using System.Globalization;
using System.Text.Json;

namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// The wire reading, path building and client-side rejection shared by <see cref="KvV1Operations"/>
/// and <see cref="KvV2Operations"/> (07 — KV engine). One copy, so the two version-explicit
/// sub-clients (KV-002) cannot disagree about how a field is read or an argument refused.
/// </summary>
internal static class KvWire
{
    /// <summary>
    /// KV2-030: the logical path form. Leading and trailing slashes are stripped from
    /// <paramref name="path"/> so <c>"/app/db/"</c> and <c>"app/db"</c> address the same secret,
    /// and so a caller cannot produce the empty trailing segment that would change the route.
    /// </summary>
    public static string LogicalPath(string mount, string group, string path)
    {
        string trimmedMount = mount.Trim('/');
        string trimmedPath = path.Trim('/');
        return group.Length == 0
            ? $"{trimmedMount}/{trimmedPath}"
            : $"{trimmedMount}/{group}/{trimmedPath}";
    }

    /// <summary>
    /// The same route, percent-encoded per segment (TRN-020), for the request itself.
    /// </summary>
    /// <remarks>
    /// A KV <c>path</c> is caller-supplied and multi-segment, so the separators must survive while
    /// every segment's own <c>/</c>, <c>?</c> and <c>%</c> must not. That is exactly
    /// <see cref="UrlBuilder.EncodePathFragment"/>, and it is why the request carries
    /// <c>pathIsEncoded: true</c>. M2b found a real path-injection defect from doing this the other
    /// way round on AUT-030's <c>username</c>: an unencoded <c>/</c> or <c>?</c> moved the request
    /// to a different route.
    /// <para>
    /// <c>mount</c> is equally caller-supplied and equally multi-segment, so it carries the same
    /// <c>..</c>-segment guard as <c>path</c> (D-M4-11, RF-1): a mount of
    /// <c>secret/../auth/token/lookup-self</c> would otherwise build a route outside the KV engine
    /// entirely.
    /// </para>
    /// </remarks>
    public static string EncodedRoute(string mount, string group, string path)
    {
        RequireSafePath(mount, "mount", LogicalPath(mount, group, path));
        string encodedMount = UrlBuilder.EncodePathFragment(mount.Trim('/'));
        string encodedPath = UrlBuilder.EncodePathFragment(path.Trim('/'));
        return group.Length == 0
            ? $"{encodedMount}/{encodedPath}"
            : $"{encodedMount}/{group}/{encodedPath}";
    }

    /// <summary>
    /// The <c>LIST</c> route for a prefix. The trailing slash is mandatory (TRN-011, KV2-008's
    /// fixture), and an empty prefix yields the group root rather than a doubled slash — which is
    /// what 07 states literally for v2 (<c>LIST {mount}/metadata/</c>) and is the only reading of
    /// v1's <c>LIST {mount}/{prefix}/</c> that does not send an empty segment.
    /// </summary>
    /// <remarks>
    /// <c>mount</c> carries the same <c>..</c>-segment guard as <see cref="EncodedRoute"/> (D-M4-11,
    /// RF-1); <c>prefix</c> keeps its own, wider <see cref="RequireSafePrefix"/> check at the call
    /// site (KV2-008).
    /// </remarks>
    public static string EncodedListRoute(string mount, string group, string prefix)
    {
        RequireSafePath(mount, "mount", LogicalPath(mount, group, prefix));
        string encodedMount = UrlBuilder.EncodePathFragment(mount.Trim('/'));
        string root = group.Length == 0 ? encodedMount : $"{encodedMount}/{group}";
        string trimmed = prefix.Trim('/');
        return trimmed.Length == 0
            ? $"{root}/"
            : $"{root}/{UrlBuilder.EncodePathFragment(trimmed)}/";
    }

    /// <summary>
    /// The wire's <c>{"keys": [...]}</c> as a list, and an empty list for the absent response a
    /// <c>404</c> with an empty body produces (07 §KV v1 and §Operations: "<c>404</c> empty →
    /// empty list").
    /// </summary>
    public static IReadOnlyList<string> ReadKeys(Response? response)
    {
        if (response?.Data is not { } data
            || !data.TryGetValue("keys", out JsonElement keys)
            || keys.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return keys.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .ToArray();
    }

    /// <summary>KV2-011, KV2-020: one version's metadata, from the wire object that carries it.</summary>
    public static KvV2VersionMetadata ReadVersionMetadata(IReadOnlyDictionary<string, JsonElement> wire, string path)
    {
        return new KvV2VersionMetadata
        {
            Version = ReadInt(wire, "version") ?? 0,
            CreatedTime = RequireInstant(wire, "created_time", path),
            DeletionTime = ReadOptionalInstant(wire, "deletion_time"),
            Destroyed = ReadBool(wire, "destroyed"),
            Username = ReadString(wire, "username"),
            Operation = ReadString(wire, "operation"),
            ResolvedEnv = ReadString(wire, "resolved_env"),
            AvailableEnvs = ReadStringList(wire, "available_envs"),
        };
    }

    /// <summary>A wire object as a property map, so one metadata reader serves a nested and a top-level shape.</summary>
    public static IReadOnlyDictionary<string, JsonElement> AsMap(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Object
            ? element.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    }

    /// <summary>Reads a nested wire object as a data map, or <see langword="null"/> when it is absent or JSON <c>null</c> (KV2-004).</summary>
    public static IReadOnlyDictionary<string, JsonElement>? ReadDataMap(IReadOnlyDictionary<string, JsonElement> wire, string name)
    {
        return wire.TryGetValue(name, out JsonElement element) && element.ValueKind == JsonValueKind.Object
            ? AsMap(element)
            : null;
    }

    public static bool ReadBool(IReadOnlyDictionary<string, JsonElement> wire, string name)
    {
        return wire.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
    }

    public static int? ReadInt(IReadOnlyDictionary<string, JsonElement> wire, string name)
    {
        return wire.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
    }

    public static string? ReadString(IReadOnlyDictionary<string, JsonElement> wire, string name)
    {
        return wire.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    public static IReadOnlyList<string> ReadStringList(IReadOnlyDictionary<string, JsonElement> wire, string name)
    {
        return wire.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToArray()
            : Array.Empty<string>();
    }

    /// <summary>
    /// An instant 07 §Types declares non-optional (<c>CreatedTime: instant</c>, no <c>?</c>). Its
    /// absence is a protocol violation, which the catalogue already names — not a defaulted
    /// timestamp, which would be the plausible guess D-M1c-25 forbids. Ruled in DR-0009's
    /// addendum, D-M4-12.
    /// </summary>
    public static DateTimeOffset RequireInstant(IReadOnlyDictionary<string, JsonElement> wire, string name, string path)
    {
        return ReadOptionalInstant(wire, name) ?? throw EnvelopeMismatch(path, name);
    }

    /// <summary>
    /// An optional instant. The wire spells "no deletion" as <c>""</c> rather than <c>null</c>
    /// (07 §Types' <c>DeletionTime: instant?</c>), so an empty string is absence.
    /// </summary>
    public static DateTimeOffset? ReadOptionalInstant(IReadOnlyDictionary<string, JsonElement> wire, string name)
    {
        if (!wire.TryGetValue(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? text = value.GetString();
        return !string.IsNullOrEmpty(text)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)
            ? parsed
            : null;
    }

    /// <summary>Serialises a JSON object body from a writer callback, so no operation builds its own buffer.</summary>
    public static ReadOnlyMemory<byte> Serialise(Action<Utf8JsonWriter> write)
    {
        ArrayBufferWriter<byte> buffer = new();
        using Utf8JsonWriter writer = new(buffer);
        writer.WriteStartObject();
        write(writer);
        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenMemory;
    }

    /// <summary>Writes a <c>data</c>-shaped map under <paramref name="name"/>.</summary>
    public static void WriteMap(Utf8JsonWriter writer, string name, IReadOnlyDictionary<string, JsonElement> map)
    {
        writer.WriteStartObject(name);
        foreach ((string key, JsonElement value) in map)
        {
            writer.WritePropertyName(key);
            value.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    /// <summary>KV1-001, KV2-002: a write with an empty or absent <c>data</c> map is refused before a request is sent.</summary>
    public static void RequireData(IReadOnlyDictionary<string, JsonElement>? data, string argument, string path)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Count == 0)
        {
            throw InvalidArgument(argument, "must contain at least one entry; an empty `data` map is rejected client-side", path);
        }
    }

    /// <summary>KV2-007: <c>Undelete</c> and <c>Destroy</c> need at least one version.</summary>
    public static void RequireVersions(IReadOnlyList<int>? versions, string path)
    {
        ArgumentNullException.ThrowIfNull(versions);
        if (versions.Count == 0)
        {
            ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.InputEmptyCollection);
            throw BastionVaultException.Request(
                ErrorCodes.InputEmptyCollection,
                entry.Category,
                entry.Message,
                entry.Hint,
                retryable: entry.Retryable,
                attempts: 0,
                path: path,
                details: new Dictionary<string, object?>(StringComparer.Ordinal) { ["argument"] = "versions" });
        }
    }

    /// <summary>
    /// KV2-008: a list prefix <b>containing</b> <c>..</c> is refused, exactly as written — the
    /// requirement is deliberately broader than traversal, so <c>a..b</c> is refused as a prefix too.
    /// </summary>
    public static void RequireSafePrefix(string prefix, string path)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (prefix.Contains("..", StringComparison.Ordinal))
        {
            throw InvalidArgument("prefix", "must not contain `..` (KV2-008)", path);
        }
    }

    /// <summary>
    /// The same refusal, narrowed to a whole <c>..</c> <i>segment</i>, for every caller-supplied
    /// <c>path</c> and <c>mount</c> of every other KV operation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// KV2-008 states this only for a list prefix, but the reason it states it is not specific to
    /// listing: percent-encoding cannot neutralise <c>..</c>, because <c>.</c> is unreserved in
    /// TRN-020's set, so a path segment of <c>..</c> survives encoding intact and a normalising
    /// HTTP stack can then collapse <c>{mount}/data/a/../../auth/token/lookup</c> onto a different
    /// route entirely. That is the M2b path-injection defect (an AUT-030 <c>username</c>
    /// containing <c>/</c> or <c>?</c> reaching an unauthenticated route) in its one remaining
    /// form, and a KV <c>path</c> — and a KV <c>mount</c>, equally caller-supplied and
    /// multi-segment (RF-1) — is caller-supplied and multi-segment.
    /// </para>
    /// <para>
    /// Narrowed to a whole segment rather than KV2-008's <c>Contains</c> because a secret named
    /// <c>release..candidate</c> is a legitimate key and nothing in 07 forbids it; only the
    /// segment form can change the route. Ruled a deliberate extension of KV2-008 beyond its
    /// literal scope in DR-0009's addendum, D-M4-11.
    /// </para>
    /// </remarks>
    public static void RequireSafePath(string value, string argument, string path)
    {
        ArgumentNullException.ThrowIfNull(value);
        foreach (string segment in value.Split('/'))
        {
            if (string.Equals(segment, "..", StringComparison.Ordinal))
            {
                throw InvalidArgument(argument, "must not contain a `..` path segment", path);
            }
        }
    }

    /// <summary>ERR-001's shape for a client-side <c>BV-INPUT-001</c>: no request, so <c>attempts: 0</c>.</summary>
    public static BastionVaultException InvalidArgument(string argument, string reason, string path)
    {
        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.InputInvalidArgument);
        return BastionVaultException.Request(
            ErrorCodes.InputInvalidArgument,
            entry.Category,
            entry.Message,
            entry.Hint,
            retryable: entry.Retryable,
            attempts: 0,
            path: path,
            details: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["argument"] = argument,
                ["reason"] = reason,
            });
    }

    /// <summary>A catalogue error raised from client-side state, costing no request (KV1-002, KV2-004…007).</summary>
    public static BastionVaultException Engine(string code, string path, IReadOnlyDictionary<string, object?>? details = null)
    {
        ErrorCatalogEntry entry = ErrorCatalog.Require(code);
        Dictionary<string, object?> merged = details is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(details, StringComparer.Ordinal);
        merged["path"] = path;
        return BastionVaultException.Request(
            code,
            entry.Category,
            entry.Message,
            HintEnrichment.InterpolatePath(entry.Hint, path),
            retryable: entry.Retryable,
            attempts: 0,
            path: path,
            details: merged);
    }

    /// <summary>
    /// A response that does not carry a field 07 §Types declares (<c>BV-PROTOCOL-002</c>).
    /// <paramref name="statusCode"/> is the HTTP status of the response that failed to shape, when
    /// the caller has it to hand at the point the guard actually fires — e.g. PAG-005's
    /// keys/records length check, which runs before any per-record decode precisely so this is
    /// available (D-M9-30, D-M9-31: see <see cref="PkiOperations.ListCertificatesInfoAsync"/>).
    /// Optional because most callers reach this before a <see cref="Response"/> exists at all, or
    /// at a guard where no fixture or requirement names the status.
    /// </summary>
    public static BastionVaultException EnvelopeMismatch(string path, string field, int? statusCode = null)
    {
        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.ProtocolUnexpectedResponse);
        return BastionVaultException.Request(
            ErrorCodes.ProtocolUnexpectedResponse,
            entry.Category,
            entry.Message,
            entry.Hint,
            retryable: entry.Retryable,
            attempts: 0,
            statusCode: statusCode,
            path: path,
            details: new Dictionary<string, object?>(StringComparer.Ordinal) { ["expectedField"] = field });
    }
}
