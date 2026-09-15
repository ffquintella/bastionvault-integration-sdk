using System.Buffers;
using System.Text.Json;

namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// The one path every M6 <c>Auth.*</c> operation reaches the shared executor through (D-M6-1).
/// It builds no URL of its own beyond encoding the caller's parts, performs no status mapping and
/// parses no envelope: it forwards to <see cref="LogicalOperations.ExecuteShapedAsync"/>, which is
/// D-M2-4's single seam, and exists only so the twelve new operation groups do not each repeat the
/// same five arguments.
/// </summary>
/// <remarks>
/// <para>
/// Every path built here is already encoded, so <c>pathIsEncoded</c> is set: a FerroGate machine id
/// or an OIDC role name is caller-supplied and TRN-020 requires it encoded as a single segment,
/// which the whole-path encoder cannot do once it has been interpolated into a string.
/// </para>
/// <para>
/// This is <b>not</b> a second transport. It holds a <see cref="LogicalOperations"/> and nothing
/// else, which is why an M6 operation still gets CFG-051…055's retry loop, DSC-040's failover,
/// ERR's status mapping and TST-051's observer for free.
/// </para>
/// </remarks>
internal sealed class AuthEndpoint
{
    private readonly LogicalOperations logical;

    public AuthEndpoint(ClientContext context, string activeNamespace)
    {
        Context = context;
        ActiveNamespace = activeNamespace;
        logical = new LogicalOperations(context, activeNamespace);
    }

    public ClientContext Context { get; }

    /// <summary>The namespace of the view this endpoint belongs to (<c>WithNamespace</c>, or CFG-010's configured one).</summary>
    public string ActiveNamespace { get; }

    /// <summary>
    /// The namespace this call will actually be scoped to: <see cref="RequestOptions.Namespace"/>
    /// when the caller overrode it per call (CFG-060), otherwise the view's own.
    /// </summary>
    /// <remarks>
    /// Trimmed exactly as <c>RequestExecutor.EffectiveNamespace</c> trims it, and for the same
    /// reason: the two must agree, or a value that reaches the wire as one namespace would be
    /// remembered here as two. This is the one place outside the executor that needs the answer —
    /// AUT-051's cache key (D-M6-16) — so it is derived, never re-derived differently.
    /// </remarks>
    public string EffectiveNamespace(RequestOptions? options)
    {
        return (options?.Namespace ?? ActiveNamespace).TrimEnd('/');
    }

    /// <summary>
    /// The auth mount as a path <i>fragment</i>: a mount may legitimately contain a <c>/</c> as a
    /// separator, exactly as <see cref="LoginRunner"/> already treats it.
    /// </summary>
    public static string Mount(string mount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mount);
        return UrlBuilder.EncodePathFragment(mount);
    }

    /// <summary>
    /// A caller-supplied value that is exactly one path segment — a role name, a machine id, a
    /// username — encoded per TRN-020 so a <c>/</c> in it cannot silently become a second segment.
    /// </summary>
    public static string Segment(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return UrlBuilder.EncodePathSegment(value);
    }

    public Task<Response?> ReadAsync(string path, RequestOptions? options, CancellationToken cancellationToken)
    {
        return logical.ExecuteShapedAsync(
            "GET", path, null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>
    /// A <c>LIST</c>, with TRN-050's <c>404</c>-empty folded to absence so a role with no keys reads
    /// as an empty list rather than an error — the same contract <c>Logical.List</c> has.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListKeysAsync(string path, RequestOptions? options, CancellationToken cancellationToken)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "LIST", path, null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return KvWire.ReadKeys(response);
    }

    public Task<Response?> WriteAsync(string path, ReadOnlyMemory<byte>? body, RequestOptions? options, CancellationToken cancellationToken)
    {
        return logical.ExecuteShapedAsync(
            "POST", path, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>
    /// A request that carries <b>no</b> token header and is exempt from CFG-020's client-side
    /// refusal, for the auth endpoints Appendix A marks <c>Auth: no</c> which are not themselves
    /// logins: AUT-035's <c>login/begin</c>, AUT-051's <c>requirement</c>, AUT-052's
    /// <c>status</c> and <c>enroll</c>, AUT-060's <c>auth_url</c> and SAML <c>login</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The exemption is a property of the operation, not of the path</b> (D-M6-10).
    /// <c>Logical.Read("auth/ferrogate/status")</c> is still refused without a token, because
    /// CFG-020's list is literal and an M2b test pins that exact case; what M6 adds is that
    /// <c>Auth.Ferrogate.Status</c> — which AUT-052 says is callable unauthenticated, at any mount
    /// — does not consult it. Widening the shared list instead would have changed the behaviour of
    /// a path CFG-020 enumerates, which is a specification question and not this tree's to answer.
    /// </para>
    /// <para>
    /// It reaches token omission through the executor's <c>isLogin</c> flag, which is the one seam
    /// CFG-020's first MUST is implemented behind (D-M2-9). No second mechanism is introduced: a
    /// second way to omit the token would be a second thing to audit.
    /// </para>
    /// <para>
    /// <c>pathIsEncoded</c> is passed explicitly here even though the executor computes
    /// <c>isLogin || pathIsEncoded</c> and would reach the same answer without it. Relying on that
    /// coincidence would leave this class's own contract — "every path built here is already
    /// encoded" — true only by accident, and would mislead a Rust or Python transcriber whose
    /// executor need not fold the two flags the same way (D-M6-19).
    /// </para>
    /// </remarks>
    public Task<Response?> WriteTokenlessAsync(string path, ReadOnlyMemory<byte>? body, RequestOptions? options, CancellationToken cancellationToken)
    {
        return logical.ExecuteShapedAsync(
            "POST", path, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken,
            isLogin: true, pathIsEncoded: true);
    }

    /// <summary>The <c>GET</c> counterpart of <see cref="WriteTokenlessAsync"/>, for AUT-051's <c>requirement</c>.</summary>
    public Task<Response?> ReadTokenlessAsync(string path, RequestOptions? options, CancellationToken cancellationToken)
    {
        return logical.ExecuteShapedAsync(
            "GET", path, null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken,
            isLogin: true, pathIsEncoded: true);
    }

    public Task<Response?> DeleteAsync(string path, RequestOptions? options, CancellationToken cancellationToken)
    {
        return logical.ExecuteShapedAsync(
            "DELETE", path, null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>
    /// The relaxed escaper, for the same <b>parity</b> reason <see cref="AppIdOperations"/> uses it
    /// (D-M6-12).
    /// </summary>
    /// <remarks>
    /// .NET's default encoder escapes <c>+</c> as <c>\u002B</c>; Rust's <c>serde_json</c> and
    /// Python's <c>json</c> do not. Both spellings are valid JSON and both parse to the same
    /// string, so this is not a correctness fix — but AUT-060's <c>saml_response</c> is base64 and
    /// therefore <i>routinely</i> contains <c>+</c> and <c>/</c>, which would make a byte-for-byte
    /// fixture comparison across the three SDKs diverge on every SAML callback.
    /// </remarks>
    private static readonly JsonWriterOptions BodyWriterOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// A flat JSON object whose absent members are <b>omitted</b> rather than sent empty (OVR-007),
    /// which is the same rule <see cref="LoginRunner"/> applies to <c>totp_code</c> (AUT-030) and
    /// <c>machine_token</c> (AUT-040).
    /// </summary>
    public static ReadOnlyMemory<byte> JsonObject(params (string Name, string? Value)[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArrayBufferWriter<byte> buffer = new();
        using Utf8JsonWriter writer = new(buffer, BodyWriterOptions);
        writer.WriteStartObject();
        foreach ((string name, string? value) in fields)
        {
            if (!string.IsNullOrEmpty(value))
            {
                writer.WriteString(name, value);
            }
        }

        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenMemory;
    }

    /// <summary>
    /// A caller-supplied JSON document as a request body, serialised verbatim. Not nullable: an
    /// operation that sends no body passes <see langword="null"/> straight to
    /// <see cref="WriteAsync"/>, so a null arm here would be a branch no call site can reach.
    /// </summary>
    public static ReadOnlyMemory<byte> Payload(JsonElement body)
    {
        return JsonSerializer.SerializeToUtf8Bytes(body, PayloadSerializerOptions);
    }

    /// <summary>The <see cref="BodyWriterOptions"/> escaping rule, for a whole caller document.</summary>
    private static readonly JsonSerializerOptions PayloadSerializerOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary><c>data.{name}</c> as a string, or <see langword="null"/> when it is absent or not one.</summary>
    public static string? ReadString(Response? response, string name)
    {
        return response?.Data is { } data && data.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    /// <summary>
    /// <c>data.{name}</c> as a boolean; <see langword="false"/> when absent, because the server
    /// omits a false flag. Takes the <c>data</c> object rather than the <see cref="Response"/> so
    /// the "no envelope at all" arm — which its one caller has already rejected — is not a branch
    /// nothing can reach.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Its one caller is a security gate</b> — AUT-051's <c>require_machine_identity</c> — so
    /// the failure direction matters (D-M6-17). Reading only literal JSON <c>true</c> means a
    /// server that spells the flag <c>"true"</c> or <c>1</c> is understood as <i>not</i> requiring
    /// a machine identity, which is the unsafe answer: the application skips a FerroGate login it
    /// is subject to. The three spellings a JSON encoder can plausibly produce for a true flag are
    /// therefore all read as true.
    /// </para>
    /// <para>
    /// It stops there rather than treating everything non-false as true. Absence is
    /// <see langword="false"/> by AUT-051's own shape (the server omits a false flag), and an
    /// object, an array or a word outside the set is a response the requirement does not describe
    /// at all; inventing a truth value for it would be the guess D-M1c-25 forbids, and it is
    /// <see cref="EnvelopeMismatch"/>'s territory rather than this reader's.
    /// </para>
    /// </remarks>
    public static bool ReadBool(IReadOnlyDictionary<string, JsonElement> data, string name)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!data.TryGetValue(name, out JsonElement value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            // `"true"`, in any casing and with surrounding space, is the same claim as `true`.
            JsonValueKind.String => bool.TryParse(value.GetString(), out bool parsed) && parsed,
            // A numeric flag is true when it is anything but zero, which is every language's rule
            // for one. A non-integral or out-of-range number is not a flag and reads as false.
            JsonValueKind.Number => value.TryGetInt64(out long number) && number != 0,
            _ => false,
        };
    }

    /// <summary>The whole <c>data</c> object, so a caller loses nothing the SDK does not model.</summary>
    public static IReadOnlyDictionary<string, JsonElement> ReadData(Response? response)
    {
        return response?.Data ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    }

    /// <summary>
    /// <c>BV-PROTOCOL-002</c> for a response that is shaped unlike anything the requirement
    /// describes — the same failure <see cref="AppIdOperations"/> raises for a missing
    /// <c>data.role_id</c>, raised the same way so the two cannot drift.
    /// </summary>
    public BastionVaultException EnvelopeMismatch(string path, string field)
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
            address: Context.Config.Address,
            details: new Dictionary<string, object?>(StringComparer.Ordinal) { ["field"] = field });
    }

    /// <summary>
    /// <c>BV-INPUT-001</c> for an argument the SDK rejects before any network call.
    /// </summary>
    public static BastionVaultException InvalidArgument(string argument, string reason)
    {
        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.InputInvalidArgument);
        return BastionVaultException.Request(
            ErrorCodes.InputInvalidArgument,
            entry.Category,
            entry.Message,
            entry.Hint,
            retryable: false,
            attempts: 0,
            details: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["argument"] = argument,
                ["reason"] = reason,
            });
    }
}
