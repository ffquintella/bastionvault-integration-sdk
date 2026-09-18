using System.Buffers;
using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// The AppID auth method — wire type <c>approle</c> — of <c>05-authentication.md</c> §Method: AppID
/// (AUT-040…AUT-042, AUT-044), reached from <see cref="AuthOperations.AppId"/>.
/// </summary>
/// <remarks>
/// AUT-043's full role-administration surface (<c>role/{name}</c> and its sub-paths,
/// <c>secret-id/lookup|destroy</c>, <c>secret-id-accessor/*</c>, <c>custom-secret-id</c>,
/// <c>machine</c>, <c>config</c>, <c>tidy/secret-id</c>) lands in M6 under
/// <see cref="Admin"/>. The three Core-level operations stay here, where an application that never
/// administers a role finds them without meeting the twenty-five that it does not need.
/// </remarks>
public sealed class AppIdOperations
{
    private readonly ClientContext context;
    private readonly LoginRunner runner;
    private readonly LogicalOperations logical;

    internal AppIdOperations(ClientContext context, string activeNamespace)
    {
        this.context = context;
        runner = new LoginRunner(context, activeNamespace);
        logical = new LogicalOperations(context, activeNamespace);
        Admin = new AppIdAdminOperations(context, activeNamespace);
    }

    /// <summary>AUT-043's Complete-level role administration surface.</summary>
    public AppIdAdminOperations Admin { get; }

    /// <summary>
    /// AUT-040: <c>POST auth/{mount}/login</c> with body
    /// <c>{"role_id": "…", "secret_id": "…", "machine_token": "…"?}</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The machine-identity gate is on by default.</b> The server's
    /// <c>auth/approle/config.require_machine</c> defaults to <b>on</b>, so a login without
    /// <paramref name="machineToken"/> is refused — <c>BV-AUTH-011 AppIdMachineBinding</c> — unless
    /// the role has <c>bypass_machine_binding = true</c> (which administrators normally pair with
    /// <c>bound_source_ips</c>). Obtain the FerroGate machine token from the local Machine Identity
    /// Agent; <c>bvault ferrogate token --format json</c> is the documented bridge (AUT-053).
    /// <c>machine_token</c> is sent <b>only</b> when supplied, never as an empty string.
    /// </para>
    /// <para>
    /// <b>AUT-041: namespace-scoped roles.</b> When the client or view has a <c>Namespace</c>, the
    /// login carries <c>X-BastionVault-Namespace</c> — it is built by the same header builder every
    /// request uses, so a login cannot silently omit it. A <c>403</c> on this path with no
    /// namespace set is enriched with a hint naming the <c>Namespace</c> setting (ERR-040).
    /// </para>
    /// <para>
    /// AUT-044's <see cref="EnvironmentScope"/> is derived from the returned
    /// <see cref="AuthInfo.Metadata"/>; a role that is environment-scoped reports
    /// <see cref="EnvironmentScope.Scoped"/> <see langword="true"/>, which tells a KV caller an
    /// <c>env</c> is mandatory.
    /// </para>
    /// </remarks>
    /// <param name="roleId">The role id (see <see cref="ReadRoleIdAsync"/>).</param>
    /// <param name="secretId">The secret id, in a redacting type (CNF-031).</param>
    /// <param name="machineToken">The FerroGate machine token, when the role requires one (AUT-040).</param>
    /// <param name="mount">The auth mount path segment. Default <c>approle</c>.</param>
    /// <param name="options">Per-request options (CFG-060).</param>
    /// <param name="cancellationToken">Runtime cancellation.</param>
    /// <exception cref="BastionVaultException">
    /// <c>BV-AUTH-010</c> for an invalid or exhausted <c>role_id</c>/<c>secret_id</c>,
    /// <c>BV-AUTH-011</c> for the machine-identity gate (AUT-012), <c>BV-AUTHZ-001</c> for a gated
    /// login (AUT-041).
    /// </exception>
    public Task<AuthInfo> LoginAsync(
        string roleId,
        SecretString? secretId = null,
        SecretString? machineToken = null,
        string mount = "approle",
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return runner.LoginAsync(
            LoginCredentials.ForAppId(roleId, secretId, machineToken, mount),
            install: true,
            options,
            cancellationToken);
    }

    /// <summary>
    /// AUT-042: <c>GET auth/{mount}/role/{roleName}/role-id</c>, returning <c>data.role_id</c>.
    /// </summary>
    /// <param name="roleName">The role's name (not its id).</param>
    /// <param name="mount">The auth mount path segment. Default <c>approle</c>.</param>
    /// <param name="options">Per-request options (CFG-060).</param>
    /// <param name="cancellationToken">Runtime cancellation.</param>
    /// <exception cref="BastionVaultException"><c>BV-PROTOCOL-002</c> when the response carries no <c>data.role_id</c>.</exception>
    public async Task<string> ReadRoleIdAsync(
        string roleName,
        string mount = "approle",
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"auth/{mount}/role/{roleName}/role-id", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);

        return ReadString(response, "role_id")
            ?? throw EnvelopeMismatch($"auth/{mount}/role/{roleName}/role-id", "data.role_id");
    }

    /// <summary>
    /// AUT-042: <c>POST auth/{mount}/role/{roleName}/secret-id</c>, returning the generated
    /// <see cref="SecretIdInfo"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="SecretIdOptions.Metadata"/> is a map in this API and a JSON <b>string</b> on the
    /// wire, which is what AUT-042 specifies and what the server parses. Every other option is
    /// omitted from the body when the caller left it unset (OVR-007).
    /// </remarks>
    /// <param name="roleName">The role's name.</param>
    /// <param name="options">The secret-id parameters, or <see langword="null"/> for the role's defaults.</param>
    /// <param name="mount">The auth mount path segment. Default <c>approle</c>.</param>
    /// <param name="requestOptions">Per-request options (CFG-060).</param>
    /// <param name="cancellationToken">Runtime cancellation.</param>
    /// <exception cref="BastionVaultException"><c>BV-PROTOCOL-002</c> when the response carries no <c>data.secret_id</c>.</exception>
    public async Task<SecretIdInfo> GenerateSecretIdAsync(
        string roleName,
        SecretIdOptions? options = null,
        string mount = "approle",
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mount);
        string path = $"auth/{mount}/role/{roleName}/secret-id";
        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, Serialise(options), requestOptions,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);

        if (ReadString(response, "secret_id") is not { } secretId)
        {
            throw EnvelopeMismatch(path, "data.secret_id");
        }

        return new SecretIdInfo
        {
            SecretId = new SecretString(secretId),
            SecretIdAccessor = ReadString(response, "secret_id_accessor"),
            SecretIdTtl = ReadSeconds(response, "secret_id_ttl"),
            SecretIdNumUses = ReadInt(response, "secret_id_num_uses"),
            Environments = ReadStringArray(response, "environments"),
        };
    }

    /// <summary>
    /// AUT-042's request body. <c>metadata</c> is serialised as a JSON object and then written as a
    /// JSON <i>string</i> value, which is the double encoding the wire format uses.
    /// </summary>
    /// <summary>
    /// The relaxed escaper, so the <c>metadata</c> JSON-in-a-string of AUT-042 lands on the wire as
    /// <c>"{\"k\":\"v\"}"</c> rather than <c>"{"k":"v"}"</c>.
    /// </summary>
    /// <remarks>
    /// Both are valid JSON and both parse to the same object, so this is not a correctness fix —
    /// it is a <b>parity</b> one. Rust's <c>serde_json</c> and Python's <c>json</c> both emit
    /// <c>\"</c>, and a byte-for-byte fixture comparison across the three SDKs would otherwise
    /// diverge on a field no requirement says anything about. The same reasoning as the harness's
    /// own <c>ResponseSerializerOptions</c>.
    /// </remarks>
    private static readonly JsonWriterOptions BodyWriterOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static ReadOnlyMemory<byte>? Serialise(SecretIdOptions? options)
    {
        if (options is null)
        {
            return null;
        }

        ArrayBufferWriter<byte> buffer = new();
        using Utf8JsonWriter writer = new(buffer, BodyWriterOptions);
        writer.WriteStartObject();
        if (options.Metadata is { Count: > 0 } metadata)
        {
            writer.WriteString("metadata", JsonSerializer.Serialize(metadata));
        }

        WriteList(writer, "cidr_list", options.CidrList);
        WriteList(writer, "token_bound_cidrs", options.TokenBoundCidrs);
        if (options.NumUses is { } numUses)
        {
            writer.WriteNumber("num_uses", numUses);
        }

        if (options.Ttl is { } ttl)
        {
            writer.WriteNumber("ttl", (long)ttl.TotalSeconds);
        }

        WriteList(writer, "environments", options.Environments);
        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenMemory;
    }

    private static void WriteList(Utf8JsonWriter writer, string name, IReadOnlyList<string>? values)
    {
        if (values is not { Count: > 0 })
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

    private BastionVaultException EnvelopeMismatch(string path, string field)
    {
        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.ProtocolUnexpectedResponse);
        return BastionVaultException.Request(
            ErrorCodes.ProtocolUnexpectedResponse,
            entry.Category,
            entry.Message,
            entry.Hint,
            retryable: entry.Retryable,
            attempts: 1,
            path: path,
            address: context.Config.Address,
            details: new Dictionary<string, object?>(StringComparer.Ordinal) { ["field"] = field });
    }

    private static string? ReadString(Response? response, string name)
    {
        return response?.Data is { } data && data.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? ReadInt(Response? response, string name)
    {
        return response?.Data is { } data && data.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;
    }

    private static TimeSpan? ReadSeconds(Response? response, string name)
    {
        return response?.Data is { } data && data.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? TimeSpan.FromSeconds(value.GetInt64())
            : null;
    }

    private static string[] ReadStringArray(Response? response, string name)
    {
        return response?.Data is { } data && data.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray()
            : Array.Empty<string>();
    }
}

/// <summary>AUT-042's parameters for <see cref="AppIdOperations.GenerateSecretIdAsync"/>.</summary>
public sealed record SecretIdOptions
{
    /// <summary>Arbitrary metadata; a map here, a JSON string on the wire (AUT-042).</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>CIDR blocks the secret id may be used from.</summary>
    public IReadOnlyList<string>? CidrList { get; init; }

    /// <summary>CIDR blocks the issued token is bound to.</summary>
    public IReadOnlyList<string>? TokenBoundCidrs { get; init; }

    /// <summary>How many times the secret id may be used; unset means the role's default.</summary>
    public int? NumUses { get; init; }

    /// <summary>The secret id's lifetime; seconds on the wire.</summary>
    public TimeSpan? Ttl { get; init; }

    /// <summary>Environment globs the credential is scoped to (AUT-044).</summary>
    public IReadOnlyList<string>? Environments { get; init; }
}

/// <summary>AUT-042's response shape for <see cref="AppIdOperations.GenerateSecretIdAsync"/>.</summary>
public sealed class SecretIdInfo
{
    /// <summary>The generated secret id, in a redacting type (AUT-031, CNF-031, CNF-032).</summary>
    public required SecretString SecretId { get; init; }

    /// <summary>The accessor, which identifies the secret id without being usable as one.</summary>
    public string? SecretIdAccessor { get; init; }

    /// <summary>The secret id's remaining lifetime; seconds on the wire.</summary>
    public TimeSpan? SecretIdTtl { get; init; }

    /// <summary>How many uses the secret id has left.</summary>
    public int? SecretIdNumUses { get; init; }

    /// <summary>The environment globs the secret id is scoped to (AUT-044).</summary>
    public IReadOnlyList<string> Environments { get; init; } = Array.Empty<string>();
}
