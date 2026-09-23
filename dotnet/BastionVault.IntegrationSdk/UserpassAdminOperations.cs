using System.Globalization;
using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// One row of <c>Auth.Userpass.Admin.ListUsersInfo</c> (14 — batch and request efficiency,
/// PAG-005's sibling listing). Relocated verbatim from <see cref="UserpassOperations"/> by R-29
/// (D-M10-3), alongside <see cref="UserpassAdminOperations.ListUsersInfoAsync"/>.
/// </summary>
/// <remarks>
/// 14 §Bulk metadata listings names a third wire field here, <c>registered_keys</c>, and this type
/// deliberately does not model it. It is mentioned exactly once in the whole specification, with no
/// shape given and no captured fixture to derive one from — plural and snake_case, sitting beside
/// <c>fido2_enabled</c>, so a list of registered FIDO2 credentials reads at least as naturally as a
/// count, and a count would conventionally be named <c>registered_keys_count</c>. D-M1c-25 exists
/// for exactly this: a deferred member returns the shape the specification names, never a plausible
/// guess, because omitting it is additive to fix later and guessing wrong on a public API shape is
/// a breaking change to fix later. No <c>PAG-*</c> requirement asks for this field. Left for a
/// captured fixture or a specification shape to settle.
/// </remarks>
public sealed class UserSummary
{
    /// <summary>The wire <c>username</c> field; falls back to the listing's own key when the server omits it.</summary>
    public required string Username { get; init; }

    /// <summary>The wire <c>fido2_enabled</c> flag.</summary>
    public bool Fido2Enabled { get; init; }
}

/// <summary>
/// Appendix A's <c>Auth.Userpass.Admin.*</c> surface, reached from
/// <see cref="UserpassOperations.Admin"/>. Raw <see cref="JsonElement"/>/<see cref="Response"/>
/// wherever Appendix A gives no field set (D-M6-5), as on <see cref="AppIdAdminOperations"/>.
/// </summary>
public sealed class UserpassAdminOperations
{
    private readonly AuthEndpoint endpoint;
    private readonly LogicalOperations logical;

    internal UserpassAdminOperations(ClientContext context, string activeNamespace)
    {
        endpoint = new AuthEndpoint(context, activeNamespace);
        logical = new LogicalOperations(context, activeNamespace);
    }

    /// <summary>Appendix A: <c>LIST auth/{mount}/users/</c> — every userpass username on the mount.</summary>
    /// <remarks>Wire params: <c>mount</c> builds the route; no body. Returns an empty list when there are none (TRN-050), never <see langword="null"/>. Conformance: Standard. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.Userpass.Admin.ListUsers — 05-authentication.md</spec>
    public Task<IReadOnlyList<string>> ListUsersAsync(string mount = "userpass", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.ListKeysAsync($"auth/{AuthEndpoint.Mount(mount)}/users/", options, cancellationToken);
    }

    /// <summary>
    /// 14 §Bulk metadata listings: <c>GET auth/{mount}/users-info?after=&amp;limit=</c>
    /// (PAG-001…PAG-003, PAG-005), D-M8-7's Userpass half of the two areas M8 wires. Relocated here
    /// verbatim from <see cref="UserpassOperations"/> by R-29 (D-M10-3) — a breaking rename on an
    /// API never published to a package registry, so no released consumer is broken (CRS-004).
    /// </summary>
    /// <remarks>Wire params: pinned to <c>v2</c> (14 §Bulk metadata listings); query carries <paramref name="after"/> (previous page's <see cref="Page{T}.Next"/>, verbatim) and <paramref name="limit"/> (defaulted to 100, validated <c>1-500</c>, PAG-001). Returns a <see cref="Page{T}"/> of <see cref="UserSummary"/>, never <see langword="null"/>. Conformance: Standard. Errors beyond the common set (ERR-061): <c>BV-INPUT-004</c> (limit out of range, PAG-001); <c>BV-PROTOCOL-002</c> (records/keys length mismatch, PAG-005).</remarks>
    /// <spec>Auth.Userpass.Admin.ListUsersInfo — PAG-001</spec>
    public async Task<Page<UserSummary>> ListUsersInfoAsync(
        string mount = "userpass",
        string? after = null,
        int? limit = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        int effectiveLimit = PagingWire.ValidateLimit(limit);
        string wireMount = UrlBuilder.EncodePathFragment(mount);
        string query = after is null
            ? $"limit={effectiveLimit.ToString(CultureInfo.InvariantCulture)}"
            : $"after={UrlBuilder.EncodeQueryValue(after)}&limit={effectiveLimit.ToString(CultureInfo.InvariantCulture)}";

        // 14 §Bulk metadata listings pins this route to /v2, the way BAT-001 and CCH pin theirs.
        RequestOptions pinned = (options ?? new RequestOptions()) with { ApiVersion = "v2" };
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"auth/{wireMount}/users-info?{query}", null, pinned,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        IReadOnlyDictionary<string, JsonElement> data = response?.Data ?? throw EnvelopeMismatch("users-info", "keys");

        IReadOnlyList<string> keys = SysWire.ReadKeys(data);
        List<UserSummary> records = [];
        if (data.TryGetValue("records", out JsonElement recordsElement) && recordsElement.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement record in recordsElement.EnumerateArray())
            {
                string fallback = index < keys.Count ? keys[index] : string.Empty;
                records.Add(record.ValueKind == JsonValueKind.Object
                    ? ToUserSummary(SysWire.AsMap(record), fallback)
                    : throw EnvelopeMismatch("users-info", "records[]"));
                index++;
            }
        }

        if (records.Count != keys.Count)
        {
            throw EnvelopeMismatch("users-info", "records");
        }

        string? next = SysWire.ReadString(data, "next");
        return new Page<UserSummary>
        {
            Keys = keys,
            Records = records,
            Total = SysWire.ReadNullableLong(data, "total") is { } total ? (int)total : keys.Count,
            Next = string.IsNullOrEmpty(next) ? null : next,
            Truncated = data.TryGetValue("truncated", out JsonElement truncated) && truncated.ValueKind == JsonValueKind.True,
        };
    }

    /// <summary>
    /// PAG-004: <see cref="ListUsersInfoAsync"/>'s iterator, following
    /// <see cref="SysOperations.ListNamespacesInfoAllAsync"/>'s shape exactly — both are the same
    /// shared machinery (<see cref="PagingWire.IteratePagesAsync{T}"/>). Relocated here verbatim
    /// alongside <see cref="ListUsersInfoAsync"/> by R-29 (D-M10-3).
    /// </summary>
    /// <remarks>HTTP call: none directly — repeatedly delegates to <see cref="ListUsersInfoAsync"/>, one call per page. Wire params: as that call's, plus <paramref name="maxRecords"/> (client-side cap, no wire effect). Returns an async sequence, never <see langword="null"/>, terminating when the server reports no further page. Conformance: Standard (PAG-004). Errors beyond the common set (ERR-061): <c>BV-INPUT-005</c> (<paramref name="maxRecords"/> exceeded, client-side).</remarks>
    /// <spec>Auth.Userpass.Admin.ListUsersInfoAll — PAG-004</spec>
    public IAsyncEnumerable<KeyValuePair<string, UserSummary>> ListUsersInfoAllAsync(
        string mount = "userpass",
        int? limit = null,
        int maxRecords = PagingWire.DefaultMaxRecords,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return PagingWire.IteratePagesAsync(
            (after, token) => ListUsersInfoAsync(mount, after, limit, options, token),
            maxRecords,
            cancellationToken);
    }

    private static UserSummary ToUserSummary(Dictionary<string, JsonElement> data, string fallback)
    {
        return new UserSummary
        {
            Username = SysWire.ReadString(data, "username") ?? fallback,
            Fido2Enabled = data.TryGetValue("fido2_enabled", out JsonElement fido2Flag) && fido2Flag.ValueKind == JsonValueKind.True,
        };
    }

    /// <summary>Appendix A: <c>GET auth/{mount}/users/{username}</c> — the user document.</summary>
    /// <remarks>Wire params: <c>mount</c>, <c>username</c> build the route; no body. Returns <see langword="null"/> on a <c>404</c> with an empty body. The response's field set is not pinned by Appendix A (D-M6-5); nothing in the specification suggests the password is echoed back on read. Conformance: Standard. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.Userpass.Admin.ReadUser — 05-authentication.md</spec>
    public Task<Response?> ReadUserAsync(string username, string mount = "userpass", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.ReadAsync(UserPath(mount, username), options, cancellationToken);
    }

    /// <summary>Appendix A: <c>POST auth/{mount}/users/{username}</c> — creates or overwrites the user, including its password and policies.</summary>
    /// <remarks>Wire params: <paramref name="user"/> sent verbatim as the body (Appendix A gives no field set, D-M6-5). Writes credential material — a password field in <paramref name="user"/> travels as plain JSON, not <see cref="SecretString"/>, because the whole document is caller-shaped; a caller holding a password separately should route it through <see cref="SetPasswordAsync"/> instead. Policy attachment is measured to use <c>token_policies</c>, not <c>policies</c> (DR-0021 F5). Returns the write response, or <see langword="null"/> on an empty body. Conformance: Standard. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.Userpass.Admin.WriteUser — 05-authentication.md</spec>
    public Task<Response?> WriteUserAsync(string username, JsonElement user, string mount = "userpass", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.WriteAsync(UserPath(mount, username), AuthEndpoint.Payload(user), options, cancellationToken);
    }

    /// <summary>Appendix A: <c>DELETE auth/{mount}/users/{username}</c> — deletes the user and its stored credential.</summary>
    /// <remarks>Wire params: <c>mount</c>, <c>username</c> build the route; no body. Returns nothing; an already-absent user is not an error. Conformance: Standard. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.Userpass.Admin.DeleteUser — 05-authentication.md</spec>
    public async Task DeleteUserAsync(string username, string mount = "userpass", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        _ = await endpoint.DeleteAsync(UserPath(mount, username), options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Appendix A: <c>POST auth/{mount}/users/{username}/password</c> — rotates the user's
    /// password. Appendix A gives no schema, but the name pins the one field, so this takes a
    /// <see cref="SecretString"/> directly rather than raw JSON —
    /// <see cref="AppIdAdminOperations.CustomSecretIdAsync"/>'s precedent for a named-not-guessed
    /// secret.
    /// </summary>
    /// <remarks>Wire params: body <c>{"password": password}</c>. Writes credential material; the response, if any, does not need to and is not asserted to echo the password back (Appendix A gives no field set, D-M6-5). Returns the write response, or <see langword="null"/> on an empty body. Conformance: Standard. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.Userpass.Admin.SetPassword — 05-authentication.md</spec>
    public Task<Response?> SetPasswordAsync(string username, SecretString password, string mount = "userpass", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);
        return endpoint.WriteAsync(
            $"{UserPath(mount, username)}/password", AuthEndpoint.JsonObject(("password", password.Reveal())), options, cancellationToken);
    }

    /// <summary>
    /// Appendix A: <c>POST auth/{mount}/users/{username}/unlock</c>, no body — restores a login
    /// AUT-032 locked (<c>15-testing-requirements.md:206</c>'s M12 integration scenario).
    /// </summary>
    /// <remarks>Wire params: <c>mount</c>, <c>username</c> build the route; no body. Returns the write response, or <see langword="null"/> on an empty body. This call does not raise <c>BV-AUTH-006</c>; that code is raised on the locked account's own login attempt, per AUT-032. Conformance: Standard (AUT-032). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.Userpass.Admin.Unlock — AUT-032</spec>
    public Task<Response?> UnlockAsync(string username, string mount = "userpass", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.WriteAsync($"{UserPath(mount, username)}/unlock", null, options, cancellationToken);
    }

    /// <summary>Appendix A: <c>GET auth/{mount}/users/{username}/fido2</c> — the user's registered FIDO2 credential metadata.</summary>
    /// <remarks>Wire params: <c>mount</c>, <c>username</c> build the route; no body. Returns <see langword="null"/> on a <c>404</c> with an empty body. FIDO2 registration is public-key-based; the response's field set is not pinned by Appendix A (D-M6-5), but a FIDO2 credential's private key never leaves the authenticator, so this call cannot return one. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.Userpass.Admin.ReadFido2 — 05-authentication.md</spec>
    public Task<Response?> ReadFido2Async(string username, string mount = "userpass", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.ReadAsync($"{UserPath(mount, username)}/fido2", options, cancellationToken);
    }

    /// <summary>Appendix A: <c>DELETE auth/{mount}/users/{username}/fido2</c> — removes the user's registered FIDO2 credential; the user can no longer authenticate via FIDO2 afterwards.</summary>
    /// <remarks>Wire params: <c>mount</c>, <c>username</c> build the route; no body. Returns nothing; an already-absent credential is not an error. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.Userpass.Admin.DeleteFido2 — 05-authentication.md</spec>
    public async Task DeleteFido2Async(string username, string mount = "userpass", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        _ = await endpoint.DeleteAsync($"{UserPath(mount, username)}/fido2", options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Appendix A: <c>GET auth/{mount}/config/lockout</c> — the mount-wide account-lockout policy (AUT-032's threshold and duration).</summary>
    /// <remarks>Wire params: <c>mount</c> builds the route; no body. Returns <see langword="null"/> on a <c>404</c> with an empty body. Conformance: Standard. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.Userpass.Admin.ReadLockout — 05-authentication.md</spec>
    public Task<Response?> ReadLockoutAsync(string mount = "userpass", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.ReadAsync($"auth/{AuthEndpoint.Mount(mount)}/config/lockout", options, cancellationToken);
    }

    /// <summary>Appendix A: <c>POST auth/{mount}/config/lockout</c> — sets the mount-wide account-lockout policy.</summary>
    /// <remarks>Wire params: <paramref name="config"/> sent verbatim as the body (Appendix A gives no field set, D-M6-5). Returns the write response, or <see langword="null"/> on an empty body. Conformance: Standard. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.Userpass.Admin.WriteLockout — 05-authentication.md</spec>
    public Task<Response?> WriteLockoutAsync(JsonElement config, string mount = "userpass", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.WriteAsync($"auth/{AuthEndpoint.Mount(mount)}/config/lockout", AuthEndpoint.Payload(config), options, cancellationToken);
    }

    /// <summary>Appendix A: <c>GET auth/{mount}/config/mfa</c> — the mount-wide MFA (TOTP) policy.</summary>
    /// <remarks>Wire params: <c>mount</c> builds the route; no body. Returns <see langword="null"/> on a <c>404</c> with an empty body. Conformance: Standard. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.Userpass.Admin.ReadMfa — 05-authentication.md</spec>
    public Task<Response?> ReadMfaAsync(string mount = "userpass", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.ReadAsync($"auth/{AuthEndpoint.Mount(mount)}/config/mfa", options, cancellationToken);
    }

    /// <summary>Appendix A: <c>POST auth/{mount}/config/mfa</c> — sets the mount-wide MFA (TOTP) policy.</summary>
    /// <remarks>Wire params: <paramref name="config"/> sent verbatim as the body (Appendix A gives no field set, D-M6-5). Returns the write response, or <see langword="null"/> on an empty body. Conformance: Standard. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.Userpass.Admin.WriteMfa — 05-authentication.md</spec>
    public Task<Response?> WriteMfaAsync(JsonElement config, string mount = "userpass", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.WriteAsync($"auth/{AuthEndpoint.Mount(mount)}/config/mfa", AuthEndpoint.Payload(config), options, cancellationToken);
    }

    private static string UserPath(string mount, string username)
    {
        return $"auth/{AuthEndpoint.Mount(mount)}/users/{AuthEndpoint.Segment(username)}";
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
            attempts: 0,
            path: path,
            details: new Dictionary<string, object?>(StringComparer.Ordinal) { ["expectedField"] = field });
    }
}
