using System.Buffers;
using System.Globalization;
using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// The DoS-guard admin surface (06 — "Batch, cache version, DoS"), reached from
/// <see cref="SysOperations.Dos"/>. Root-only on the server, and every route is <c>/v2</c>-pinned
/// (Appendix A).
/// </summary>
/// <remarks>
/// ⚠️ This surface carries <b>no <c>SYS-*</c> requirement ID</b>. It is named in
/// <c>06-system-api.md</c>'s Complete-tier table and in Appendix A, both of which give the routes
/// and the field names, and neither of which states a MUST. No ID is minted for it (D-M7-10's
/// rule, applied again); the tests here are deliberately untagged.
/// </remarks>
public sealed class DosOperations
{
    private readonly LogicalOperations logical;

    internal DosOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
    }

    /// <summary><c>GET /v2/sys/dos/config</c>.</summary>
    public async Task<DosConfig> ReadConfigAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "GET", "sys/dos/config", null, IdentityWire.PinV2(options),
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, JsonElement> data = response?.Data
            ?? throw IdentityWire.EnvelopeMismatch("sys/dos/config", "enabled");

        return new DosConfig
        {
            Enabled = ReadNullableBool(data, "enabled"),
            WindowSecs = SysWire.ReadNullableLong(data, "window_secs"),
            MaxRequests = SysWire.ReadNullableLong(data, "max_requests"),
            AuthMaxRequests = SysWire.ReadNullableLong(data, "auth_max_requests"),
            BanSecs = SysWire.ReadNullableLong(data, "ban_secs"),
            RefreshSecs = SysWire.ReadNullableLong(data, "refresh_secs"),
            Raw = response.Raw,
        };
    }

    /// <summary>
    /// <c>POST /v2/sys/dos/config</c> — a <b>partial update</b>, as the table says in as many
    /// words. Only the fields <paramref name="patch"/> sets are written, so a caller changing
    /// <c>ban_secs</c> does not reset the other five. This is the opposite of SYS-060's
    /// full-replace namespace write, and the difference is the requirement's, not a choice
    /// (D-M7-19 ⇄ this).
    /// </summary>
    public async Task<DosConfig> WriteConfigAsync(DosConfig patch, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);
        Response? response = await logical.ExecuteShapedAsync(
            "POST", "sys/dos/config", SerialisePatch(patch), IdentityWire.PinV2(options),
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, JsonElement> data = response?.Data
            ?? throw IdentityWire.EnvelopeMismatch("sys/dos/config", "enabled");

        return new DosConfig
        {
            Enabled = ReadNullableBool(data, "enabled"),
            WindowSecs = SysWire.ReadNullableLong(data, "window_secs"),
            MaxRequests = SysWire.ReadNullableLong(data, "max_requests"),
            AuthMaxRequests = SysWire.ReadNullableLong(data, "auth_max_requests"),
            BanSecs = SysWire.ReadNullableLong(data, "ban_secs"),
            RefreshSecs = SysWire.ReadNullableLong(data, "refresh_secs"),
            Raw = response.Raw,
        };
    }

    /// <summary><c>GET /v2/sys/dos/stats</c>. The body's shape is not specified anywhere, so it is returned unparsed rather than modelled from a guess (D-M1c-25).</summary>
    public async Task<JsonElement> StatsAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "GET", "sys/dos/stats", null, IdentityWire.PinV2(options),
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
        return response?.Raw ?? throw IdentityWire.EnvelopeMismatch("sys/dos/stats", "body");
    }

    /// <summary>
    /// <c>POST /v2/sys/dos/bans/{ip}</c>. <paramref name="ttlSecs"/> and <paramref name="reason"/>
    /// are omitted from the body when unset rather than sent as zero and <c>""</c>, which would be
    /// two different requests from the one the caller made.
    /// </summary>
    public async Task BanAsync(string ip, long? ttlSecs = null, string? reason = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        string segment = UrlBuilder.EncodePathSegment(MountPaths.ToWire(ip, "ip"));
        _ = await logical.ExecuteShapedAsync(
            "POST", $"sys/dos/bans/{segment}", SerialiseBan(ttlSecs, reason), IdentityWire.PinV2(options),
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>DELETE /v2/sys/dos/bans/{ip}</c>.</summary>
    public async Task UnbanAsync(string ip, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        string segment = UrlBuilder.EncodePathSegment(MountPaths.ToWire(ip, "ip"));
        _ = await logical.ExecuteShapedAsync(
            "DELETE", $"sys/dos/bans/{segment}", null, IdentityWire.PinV2(options),
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    private static ReadOnlyMemory<byte> SerialisePatch(DosConfig patch)
    {
        ArrayBufferWriter<byte> buffer = new();
        using Utf8JsonWriter writer = new(buffer);
        writer.WriteStartObject();
        if (patch.Enabled is { } enabled)
        {
            writer.WriteBoolean("enabled", enabled);
        }

        WriteIfSet(writer, "window_secs", patch.WindowSecs);
        WriteIfSet(writer, "max_requests", patch.MaxRequests);
        WriteIfSet(writer, "auth_max_requests", patch.AuthMaxRequests);
        WriteIfSet(writer, "ban_secs", patch.BanSecs);
        WriteIfSet(writer, "refresh_secs", patch.RefreshSecs);
        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenMemory;
    }

    private static ReadOnlyMemory<byte> SerialiseBan(long? ttlSecs, string? reason)
    {
        ArrayBufferWriter<byte> buffer = new();
        using Utf8JsonWriter writer = new(buffer);
        writer.WriteStartObject();
        WriteIfSet(writer, "ttl_secs", ttlSecs);
        if (reason is not null)
        {
            writer.WriteString("reason", reason);
        }

        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenMemory;
    }

    private static void WriteIfSet(Utf8JsonWriter writer, string name, long? value)
    {
        if (value is { } present)
        {
            writer.WriteNumber(name, present);
        }
    }

    private static bool? ReadNullableBool(IReadOnlyDictionary<string, JsonElement> data, string name)
    {
        return data.TryGetValue(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.ValueKind == JsonValueKind.True
            : null;
    }
}

/// <summary>
/// The owner-transfer surface (06 — "Dashboard, identity self-service, owner transfers"), reached
/// from <see cref="SysOperations.OwnerTransfer"/>.
/// </summary>
/// <remarks>
/// ⚠️ <b>No <c>SYS-*</c> requirement ID and no specified request body.</b> Appendix A and
/// <c>06-system-api.md</c> name the four routes and mark them admin; neither writes the body. The
/// transfer spec is therefore taken as a <see cref="JsonElement"/> and forwarded verbatim rather
/// than modelled: a typed record here would have to invent field names, and a wrong guess on a
/// <i>write</i> body fails silently — the exact argument that kept the legacy policy write out of
/// D-M7-14. No ID is minted.
/// </remarks>
public sealed class OwnerTransferOperations
{
    private readonly LogicalOperations logical;

    internal OwnerTransferOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
    }

    /// <summary><c>POST sys/kv-owner/transfer</c>.</summary>
    public async Task<JsonElement?> KvAsync(JsonElement spec, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await TransferAsync("sys/kv-owner/transfer", spec, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary><c>POST sys/resource-owner/transfer</c>.</summary>
    public async Task<JsonElement?> ResourceAsync(JsonElement spec, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await TransferAsync("sys/resource-owner/transfer", spec, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary><c>POST sys/asset-group-owner/transfer</c>.</summary>
    public async Task<JsonElement?> AssetGroupAsync(JsonElement spec, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await TransferAsync("sys/asset-group-owner/transfer", spec, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary><c>POST sys/file-owner/transfer</c>.</summary>
    public async Task<JsonElement?> FileAsync(JsonElement spec, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await TransferAsync("sys/file-owner/transfer", spec, options, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonElement?> TransferAsync(string path, JsonElement spec, RequestOptions? options, CancellationToken cancellationToken)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, SysWire.RequireJsonBody(spec, "spec"), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
        return response?.Raw;
    }
}

/// <summary>
/// The exchange surface (06 — "Backup, restore, export, import"), reached from
/// <see cref="SysOperations.Exchange"/>: JSON in, JSON out, on four <c>sys/exchange/*</c> routes.
/// </summary>
/// <remarks>
/// ⚠️ <b>No <c>SYS-*</c> requirement ID and no specified body</b> on either half. Appendix A names
/// the four routes and says "JSON". Bodies are forwarded and returned verbatim for the same reason
/// <see cref="OwnerTransferOperations"/> does. No ID is minted.
/// </remarks>
public sealed class ExchangeOperations
{
    private readonly LogicalOperations logical;

    internal ExchangeOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
    }

    /// <summary><c>POST sys/exchange/export</c>.</summary>
    public async Task<JsonElement?> ExportAsync(JsonElement request, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await PostAsync("sys/exchange/export", request, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary><c>POST sys/exchange/import</c>.</summary>
    public async Task<JsonElement?> ImportAsync(JsonElement request, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await PostAsync("sys/exchange/import", request, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary><c>POST sys/exchange/import/preview</c> — the dry run.</summary>
    public async Task<JsonElement?> ImportPreviewAsync(JsonElement request, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await PostAsync("sys/exchange/import/preview", request, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary><c>POST sys/exchange/import/apply</c>.</summary>
    public async Task<JsonElement?> ImportApplyAsync(JsonElement request, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await PostAsync("sys/exchange/import/apply", request, options, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonElement?> PostAsync(string path, JsonElement request, RequestOptions? options, CancellationToken cancellationToken)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, SysWire.RequireJsonBody(request, "request"), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
        return response?.Raw;
    }
}

/// <summary>
/// SYS-101's "Vault compatibility gaps": the HashiCorp Vault surfaces this server does not have.
/// </summary>
/// <remarks>
/// <para>
/// SYS-100 states the absences and SYS-101 requires the SDK's documentation to list them. This type
/// is the in-SDK half of that: the list is reachable from code and from IntelliSense, not only from
/// a page a migrating user has to find. The usage-guide MUSTs themselves are M11's
/// (<c>17-usage-guides.md</c>); <c>dotnet/README.md</c> carries the prose half.
/// </para>
/// <para>
/// ⚠️ The SDK exposes <b>no</b> operation for any of these, and that absence is asserted by a test
/// (SYS-100) so a later pass cannot add one silently. <c>Response.LeaseId</c>,
/// <c>Response.LeaseDuration</c> and <c>Response.Renewable</c> remain <b>informational</b>: the
/// server sends them and the SDK surfaces them, but there is no route to renew or revoke against.
/// </para>
/// </remarks>
public static class VaultCompatibilityGaps
{
    /// <summary>
    /// SYS-100's list, as path prefixes. Each entry is a surface a HashiCorp Vault client would
    /// expect and this server does not serve; the built-in default policy mentions some of them,
    /// but the routes do not exist.
    /// </summary>
    public static IReadOnlyList<string> AbsentSurfaces { get; } =
    [
        "sys/leases/",
        "sys/renew",
        "sys/revoke",
        "sys/wrapping/",
        "cubbyhole/",
    ];
}
