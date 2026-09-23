using System.Buffers;
using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// The token-store operations (<c>auth/token/*</c>, AUT-020, AUT-080…AUT-085), reached from
/// <see cref="AuthOperations.Token"/>.
/// </summary>
/// <remarks>
/// <para>
/// Only the nine paths the server actually has are exposed. <c>lookup-accessor</c>,
/// <c>renew-self</c>, <c>renew-accessor</c>, <c>revoke-accessor</c>, <c>create-orphan</c> as a
/// distinct path, <c>roles</c> and <c>tidy</c> do not exist on the server and the specification
/// forbids exposing operations for them (05 §Token store operations).
/// </para>
/// <para>
/// Every operation issues its request through the same <see cref="RequestExecutor"/> the logical
/// layer uses, via <see cref="LogicalOperations.ExecuteShapedAsync"/> (D-M2-4). None of them builds
/// an HTTP path, a status mapping or a recognition table of its own; the two refinements the
/// section-05 requirements add (AUT-084, AUT-085) live in the shared
/// <see cref="StatusCodeMapper"/>, not here.
/// </para>
/// </remarks>
public sealed class TokenOperations
{
    /// <summary>
    /// AUT-081's reserved <c>meta</c> keys, verbatim from <c>05-authentication.md</c>, plus the
    /// <c>approle_env_</c> prefix rule below. The server refuses them too
    /// (<c>meta key(s) … are reserved</c> → <c>BV-INPUT-009</c>); refusing client-side means the
    /// caller is told before a request is spent, and it is the same code either way.
    /// </summary>
    private static readonly string[] ReservedMetaKeys =
    [
        "spiffe_id", "machine_id", "username", "entity_id", "mount_path", "role_name", "role",
        "namespace_path", "namespace_id", "child_visible", "auth_method", "groups", "subject",
        "name_id", "name_id_format", "ferrogate_kid", "session_id", "approle_machine_bypass",
        "machine_identity_exempt",
    ];

    private const string ReservedMetaKeyPrefix = "approle_env_";

    private readonly ClientContext context;
    private readonly LogicalOperations logical;

    internal TokenOperations(ClientContext context, LogicalOperations logical)
    {
        this.context = context;
        this.logical = logical;
    }

    /// <summary>
    /// AUT-020: replaces the client's source with a <see cref="TokenSourceKind.Static"/> one
    /// holding <paramref name="token"/>. No network call. An empty or whitespace-only token is
    /// refused with <c>BV-INPUT-001</c>.
    /// </summary>
    /// <remarks>HTTP call: none — client-side assignment only. Wire params: none. Returns nothing. Conformance: Core (AUT-020). Errors beyond the common set (ERR-061): <c>BV-INPUT-001</c>. <paramref name="token"/> is a redacting <see cref="SecretString"/> and is never logged.</remarks>
    /// <spec>Auth.Token.Use — AUT-020</spec>
    public void Use(SecretString token)
    {
        ArgumentNullException.ThrowIfNull(token);
        string? value = token.Reveal();
        if (string.IsNullOrWhiteSpace(value))
        {
            ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.InputInvalidArgument);
            throw BastionVaultException.Request(
                ErrorCodes.InputInvalidArgument,
                entry.Category,
                entry.Message,
                entry.Hint,
                retryable: false,
                attempts: 0,
                details: new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["argument"] = "token",
                    ["reason"] = "A token must be a non-empty, non-whitespace string.",
                });
        }

        context.SetToken(token);
    }

    /// <summary>
    /// <c>Auth.Token.Verify</c>: a <see cref="LookupSelfAsync"/> whose purpose is to fail with
    /// <c>BV-AUTHZ-001</c> when the token is invalid (05 §Method: Token).
    /// </summary>
    /// <remarks>HTTP call: <c>GET auth/token/lookup-self</c>, via <see cref="LookupSelfAsync"/>. Wire params: none. Returns a <see cref="TokenInfo"/>, never <see langword="null"/> (throws instead). Conformance: Core (05 §Method: Token). Errors beyond the common set (ERR-061): <c>BV-AUTHZ-001</c> for an invalid token. <see cref="TokenInfo.Id"/>, when present, carries the token's own id in a redacting <see cref="SecretString"/> and is never logged in cleartext.</remarks>
    /// <spec>Auth.Token.Verify — 05-authentication.md</spec>
    public Task<TokenInfo> VerifyAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return LookupSelfAsync(options, cancellationToken);
    }

    /// <summary>
    /// AUT-082: <c>POST auth/token/create</c>. Returns the created token's <see cref="AuthInfo"/>
    /// and does <b>not</b> switch the client's token unless
    /// <see cref="CreateTokenRequest.UseResult"/> is set. Reserved <c>meta</c> keys are refused
    /// before any request (AUT-081).
    /// </summary>
    /// <remarks>Wire params: <c>policies</c>, <c>ttl</c>, <c>period</c>, <c>num_uses</c>, <c>renewable</c>, <c>meta</c>, <c>display_name</c>, <c>explicit_max_ttl</c>, <c>no_default_policy</c>, <c>no_parent</c>, <c>id</c>, <c>type</c>, <c>child_visible</c>, each omitted when unset. Returns <see cref="AuthInfo"/>, never <see langword="null"/> (throws instead). Conformance: Core (AUT-082, AUT-081). Errors beyond the common set (ERR-061): <c>BV-INPUT-009</c> for a reserved <c>meta</c> key. The created token's value is carried only in <see cref="AuthInfo.ClientToken"/>, a redacting <see cref="SecretString"/>, and is never logged.</remarks>
    /// <spec>Auth.Token.Create — AUT-082</spec>
    public async Task<AuthInfo> CreateAsync(CreateTokenRequest request, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        GuardReservedMeta(request.Meta);

        Response? response = await logical.ExecuteShapedAsync(
            "POST", "auth/token/create", Serialise(request), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
        AuthInfo auth = RequireAuth(response, "auth/token/create");
        if (request.UseResult)
        {
            context.SetToken(auth.ClientToken);
        }

        return auth;
    }

    /// <summary>
    /// <c>GET auth/token/lookup/{token}</c>. A <c>404</c> with an empty body is
    /// <c>BV-NOTFOUND-006 TokenNotFound</c>, not absence (AUT-084), and the token segment of the
    /// path is redacted in the error and in the observer event (ERR-003, CFG-080).
    /// </summary>
    /// <remarks>Wire params: <paramref name="token"/>, in the path. Returns a <see cref="TokenInfo"/>, never <see langword="null"/> (throws <c>BV-NOTFOUND-006</c> instead). Conformance: Core (AUT-084). Errors beyond the common set (ERR-061): <c>BV-NOTFOUND-006</c>. <paramref name="token"/> is redacted wherever the path is surfaced (ERR-003, CFG-080), and <see cref="TokenInfo.Id"/> is a redacting <see cref="SecretString"/>.</remarks>
    /// <spec>Auth.Token.Lookup — AUT-084</spec>
    public async Task<TokenInfo> LookupAsync(string token, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"auth/token/lookup/{token}", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
        return ReadTokenInfo(response, "auth/token/lookup");
    }

    /// <summary>
    /// <c>GET auth/token/lookup-self</c>. The result is also recorded as
    /// <see cref="AuthOperations.TokenInfo"/> (AUT-004).
    /// </summary>
    /// <remarks>Wire params: none. Returns a <see cref="TokenInfo"/>, never <see langword="null"/> (throws instead). Conformance: Core (AUT-004). Errors beyond the common set (ERR-061): none. <see cref="TokenInfo.Id"/>, when present, is a redacting <see cref="SecretString"/> and is never logged in cleartext.</remarks>
    /// <spec>Auth.Token.LookupSelf — AUT-004</spec>
    public async Task<TokenInfo> LookupSelfAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "GET", "auth/token/lookup-self", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
        TokenInfo info = ReadTokenInfo(response, "auth/token/lookup-self");
        context.SetTokenInfo(info);
        return info;
    }

    /// <summary>
    /// <c>POST auth/token/renew/{token}</c> with the <b>required</b> <c>increment</c> body. An
    /// unknown or expired token yields <c>BV-AUTH-015 TokenNotRenewable</c> (AUT-085).
    /// </summary>
    /// <remarks>Wire params: <paramref name="token"/> in the path, <c>increment</c> in the body. Returns <see cref="AuthInfo"/>, never <see langword="null"/>. Conformance: Core (AUT-085). Errors beyond the common set (ERR-061): <c>BV-AUTH-015</c>. The renewed token's value is carried only in <see cref="AuthInfo.ClientToken"/>, a redacting <see cref="SecretString"/>.</remarks>
    /// <spec>Auth.Token.Renew — AUT-085</spec>
    public async Task<AuthInfo> RenewAsync(string token, int increment, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return await RenewPathAsync(token, increment, options, previous: null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// AUT-080: <c>RenewSelf</c> goes through <c>renew/{currentToken}</c> — there is no
    /// <c>renew-self</c> path on the server — so the live token appears in the request path, and
    /// the path is therefore redacted wherever it is surfaced (ERR-003 in the error, CFG-080 in
    /// the observer event).
    /// </summary>
    /// <remarks>Wire params: the current token in the path, <c>increment</c> in the body. Returns <see cref="AuthInfo"/>, never <see langword="null"/>. Conformance: Core (AUT-080). Errors beyond the common set (ERR-061): <c>BV-AUTH-015</c>. The renewed value is carried only in <see cref="AuthInfo.ClientToken"/>, a redacting <see cref="SecretString"/>.</remarks>
    /// <spec>Auth.Token.RenewSelf — AUT-080</spec>
    public async Task<AuthInfo> RenewSelfAsync(int increment, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Resolved exactly **once**, and then pinned onto the request so the executor's own
        // resolution is skipped: RequestOptions.Token is the first thing ResolveTokenAsync honours.
        //
        // Resolving twice — once here for the path, once there for the header — was M2a's F2
        // defect. AUT-080 says "the current token in the path", and with a Callback source, whose
        // contract is to be called on every resolution, two resolutions can return two different
        // tokens: the request would then renew token A while authenticating as token B. A per-call
        // RequestOptions.Token is itself "the current token" for this call (CFG-060), so it is
        // used as-is and the client's source is not resolved at all.
        SecretString current = options?.Token
            ?? await context.ResolveTokenAsync(cancellationToken).ConfigureAwait(false)
            ?? SecretString.Empty;
        RequestOptions pinned = (options ?? new RequestOptions()) with { Token = current };
        // F1: on a content-free renewal (204/empty 200), ResolveRenewedAuth falls back to the
        // client's own last-known login credential rather than fabricating one — the only lease
        // information this SDK has ever been given for a Login-sourced token.
        return await RenewPathAsync(current.Reveal() ?? string.Empty, increment, pinned, context.LastLogin, cancellationToken).ConfigureAwait(false);
    }

    /// <summary><c>POST auth/token/revoke/{token}</c>.</summary>
    /// <remarks>Wire params: <paramref name="token"/>, in the path. Returns nothing. Conformance: Core (05 §Method: Token). Errors beyond the common set (ERR-061): none. <paramref name="token"/> is redacted wherever the path is surfaced (ERR-003, CFG-080).</remarks>
    /// <spec>Auth.Token.Revoke — 05-authentication.md</spec>
    public Task RevokeAsync(string token, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return logical.ExecuteShapedAsync(
            "POST", $"auth/token/revoke/{token}", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken);
    }

    /// <summary><c>POST auth/token/revoke-orphan/{token}</c> (sudo).</summary>
    /// <remarks>Wire params: <paramref name="token"/>, in the path. Returns nothing. Conformance: Shared (05 §Method: Token). Errors beyond the common set (ERR-061): none. <paramref name="token"/> is redacted wherever the path is surfaced (ERR-003, CFG-080).</remarks>
    /// <spec>Auth.Token.RevokeOrphan — 05-authentication.md</spec>
    public Task RevokeOrphanAsync(string token, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return logical.ExecuteShapedAsync(
            "POST", $"auth/token/revoke-orphan/{token}", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken);
    }

    /// <summary>
    /// AUT-083: <c>POST auth/token/revoke-self</c>, then clears the local token. A root-policy
    /// token is accepted by the server but not actually revoked (the logout is only recorded);
    /// the SDK clears its token either way, because the server's response is identical and the
    /// client cannot tell the two apart.
    /// </summary>
    /// <remarks>Wire params: none. Returns nothing. Conformance: Core (AUT-083). Errors beyond the common set (ERR-061): none.</remarks>
    /// <spec>Auth.Token.RevokeSelf — AUT-083</spec>
    public async Task RevokeSelfAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        _ = await logical.ExecuteShapedAsync(
            "POST", "auth/token/revoke-self", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
        context.SetToken(SecretString.Empty);
    }

    /// <summary><c>POST auth/token/audit-login</c>: records a login event for a token sign-in.</summary>
    /// <remarks>Wire params: none. Returns nothing. Conformance: Shared (05 §Method: Token). Errors beyond the common set (ERR-061): none.</remarks>
    /// <spec>Auth.Token.AuditLogin — 05-authentication.md</spec>
    public Task AuditLoginAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return logical.ExecuteShapedAsync(
                "POST", "auth/token/audit-login", null, options,
                defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken);
    }

    private async Task<AuthInfo> RenewPathAsync(
        string token, int increment, RequestOptions? options, AuthInfo? previous, CancellationToken cancellationToken)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "POST", $"auth/token/renew/{token}", IncrementBody(increment), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
        return ResolveRenewedAuth(response, token, previous);
    }

    /// <summary>
    /// AUT-081's client-side refusal. The catalogue hint for <c>BV-INPUT-009</c> points at
    /// <c>Details.keys</c>, so the offending keys are interpolated into it on the same ERR-034
    /// principle the path uses: a hint that names a details key must name the value the SDK
    /// actually saw.
    /// </summary>
    private static void GuardReservedMeta(IReadOnlyDictionary<string, string>? meta)
    {
        if (meta is null)
        {
            return;
        }

        List<string> offending = meta.Keys.Where(IsReservedMetaKey).OrderBy(key => key, StringComparer.Ordinal).ToList();
        if (offending.Count == 0)
        {
            return;
        }

        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.InputReservedTokenMetaKey);
        throw BastionVaultException.Request(
            ErrorCodes.InputReservedTokenMetaKey,
            entry.Category,
            entry.Message,
            HintEnrichment.InterpolateKeys(entry.Hint, offending),
            retryable: false,
            attempts: 0,
            details: new Dictionary<string, object?>(StringComparer.Ordinal) { ["keys"] = offending.ToArray() });
    }

    private static bool IsReservedMetaKey(string key)
    {
        return key.StartsWith(ReservedMetaKeyPrefix, StringComparison.Ordinal)
                || ReservedMetaKeys.Contains(key, StringComparer.Ordinal);
    }

    private static ReadOnlyMemory<byte> IncrementBody(int increment)
    {
        ArrayBufferWriter<byte> buffer = new();
        using Utf8JsonWriter writer = new(buffer);
        writer.WriteStartObject();
        writer.WriteNumber("increment", increment);
        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenMemory;
    }

    /// <summary>
    /// Serialises <see cref="CreateTokenRequest"/> to the wire field names of
    /// <c>05-authentication.md</c>'s table, omitting every property the caller left unset
    /// (OVR-007). <see cref="CreateTokenRequest.UseResult"/> is a client-side switch and is never
    /// sent.
    /// </summary>
    private static ReadOnlyMemory<byte> Serialise(CreateTokenRequest request)
    {
        ArrayBufferWriter<byte> buffer = new();
        using Utf8JsonWriter writer = new(buffer);
        writer.WriteStartObject();
        if (request.Policies is { } policies)
        {
            writer.WriteStartArray("policies");
            foreach (string policy in policies)
            {
                writer.WriteStringValue(policy);
            }

            writer.WriteEndArray();
        }

        // 05-authentication.md:196-228 (measured, DR-0021): auth/token/create's ttl is sent as a
        // Go-style duration string, not a number — unlike period and explicit_max_ttl below, which
        // were not measured and stay integer seconds (D-M1c-25).
        PkiWire.WriteGoDuration(writer, "ttl", request.Ttl);
        WriteSeconds(writer, "period", request.Period);
        if (request.NumUses is { } numUses)
        {
            writer.WriteNumber("num_uses", numUses);
        }

        writer.WriteBoolean("renewable", request.Renewable);
        if (request.Meta is { } meta)
        {
            writer.WriteStartObject("meta");
            foreach ((string key, string value) in meta)
            {
                writer.WriteString(key, value);
            }

            writer.WriteEndObject();
        }

        if (request.DisplayName is { } displayName)
        {
            writer.WriteString("display_name", displayName);
        }

        WriteSeconds(writer, "explicit_max_ttl", request.ExplicitMaxTtl);
        if (request.NoDefaultPolicy is { } noDefaultPolicy)
        {
            writer.WriteBoolean("no_default_policy", noDefaultPolicy);
        }

        if (request.NoParent is { } noParent)
        {
            writer.WriteBoolean("no_parent", noParent);
        }

        if (request.Id is { } id)
        {
            writer.WriteString("id", id);
        }

        if (request.Type is { } type)
        {
            writer.WriteString("type", type);
        }

        if (request.ChildVisible is { } childVisible)
        {
            writer.WriteBoolean("child_visible", childVisible);
        }

        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenMemory;
    }

    private static void WriteSeconds(Utf8JsonWriter writer, string name, TimeSpan? value)
    {
        if (value is { } duration)
        {
            writer.WriteNumber(name, (long)duration.TotalSeconds);
        }
    }

    /// <summary>
    /// An operation whose response contract is an envelope <c>auth</c> object got something else.
    /// Reported as <c>BV-PROTOCOL-001</c>, which is what <c>04-error-model.md</c> names for a
    /// response that does not match the documented envelope — not as a null the caller would
    /// dereference, and not as a fabricated empty <see cref="AuthInfo"/> (D-M1c-25).
    /// </summary>
    private static AuthInfo RequireAuth(Response? response, string path)
    {
        return response?.Auth ?? throw EnvelopeMismatch(path, "auth");
    }

    // DR-0021 F1: a null response for auth/token/renew/{token} is a content-free success (204, or
    // a 200 with an empty body — treatNotFoundEmptyAsAbsent: false keeps a 404 from reaching here
    // as null), not an envelope mismatch. `previous` (RenewSelfAsync's ClientContext.LastLogin;
    // null for RenewAsync's arbitrary token) is carried over as-is except IssuedAt, which becomes
    // now, since the server told the SDK nothing new.
    private AuthInfo ResolveRenewedAuth(Response? response, string token, AuthInfo? previous)
    {
        if (response is not null)
        {
            return response.Auth ?? throw EnvelopeMismatch("auth/token/renew", "auth");
        }

        return new AuthInfo
        {
            ClientToken = new SecretString(token),
            IssuedAt = context.Clock.NowUtc(),
            LeaseDuration = previous?.LeaseDuration,
            Renewable = previous?.Renewable ?? false,
            Policies = previous?.Policies ?? Array.Empty<string>(),
            Metadata = previous?.Metadata,
        };
    }

    /// <summary>
    /// Maps a lookup's <c>data</c> object, computing AUT-014's <see cref="TokenInfo.RemainingTtl"/>
    /// from the injected clock. The wire <c>ttl</c> field is read by nothing: it is always
    /// <c>0</c> and the specification forbids exposing it.
    /// </summary>
    private TokenInfo ReadTokenInfo(Response? response, string path)
    {
        if (response?.Data is not { } data)
        {
            throw EnvelopeMismatch(path, "data");
        }

        DateTimeOffset? creationTime = ReadUnixTime(data, "creation_time");
        TimeSpan creationTtl = ReadSeconds(data, "creation_ttl") ?? TimeSpan.Zero;
        // AUT-014: creation_time + creation_ttl − NowUtc, and null when creation_ttl is 0. Both
        // operands come from the response and the clock, never from the wire `ttl`.
        TimeSpan? remainingTtl = creationTtl == TimeSpan.Zero || creationTime is null
            ? null
            : creationTime.Value + creationTtl - context.Clock.NowUtc();

        return new TokenInfo
        {
            Id = data.TryGetValue("id", out JsonElement id) && id.ValueKind == JsonValueKind.String
                ? new SecretString(id.GetString())
                : null,
            Policies = ReadStringArray(data, "policies"),
            Path = ReadString(data, "path"),
            Meta = ReadStringMap(data, "meta"),
            DisplayName = ReadString(data, "display_name"),
            NumUses = ReadInt(data, "num_uses") ?? 0,
            CreationTime = creationTime,
            CreationTtl = creationTtl,
            ExplicitMaxTtl = ReadSeconds(data, "explicit_max_ttl") ?? TimeSpan.Zero,
            Period = ReadSeconds(data, "period"),
            RemainingTtl = remainingTtl,
        };
    }

    private static BastionVaultException EnvelopeMismatch(string path, string field)
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
            details: new Dictionary<string, object?>(StringComparer.Ordinal) { ["field"] = field });
    }

    private static string? ReadString(IReadOnlyDictionary<string, JsonElement> data, string name)
    {
        return data.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static int? ReadInt(IReadOnlyDictionary<string, JsonElement> data, string name)
    {
        return data.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
    }

    private static TimeSpan? ReadSeconds(IReadOnlyDictionary<string, JsonElement> data, string name)
    {
        return data.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
                ? TimeSpan.FromSeconds(value.GetInt64())
                : null;
    }

    private static DateTimeOffset? ReadUnixTime(IReadOnlyDictionary<string, JsonElement> data, string name)
    {
        return data.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeSeconds(value.GetInt64())
                : null;
    }

    private static string[] ReadStringArray(IReadOnlyDictionary<string, JsonElement> data, string name)
    {
        return data.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray()
                : Array.Empty<string>();
    }

    private static Dictionary<string, string>? ReadStringMap(IReadOnlyDictionary<string, JsonElement> data, string name)
    {
        return data.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.Object
                ? value.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty, StringComparer.Ordinal)
                : null;
    }
}
