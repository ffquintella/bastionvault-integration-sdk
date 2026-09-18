using System.Globalization;
using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// One row of <c>Auth.Userpass.ListUsersInfo</c> (14 — batch and request efficiency, PAG-005's
/// sibling listing).
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
/// The Userpass auth method (<c>05-authentication.md</c> §Method: Userpass, AUT-030…AUT-032),
/// reached from <see cref="AuthOperations.Userpass"/>.
/// </summary>
/// <remarks>
/// <para>
/// AUT-035's FIDO2 pair (<see cref="Fido2LoginBeginAsync"/>, <see cref="Fido2LoginCompleteAsync"/>)
/// lands here in M6, on the userpass mount's own <c>auth/{mount}/fido2/login/{begin,complete}</c>
/// paths (Appendix A). The standalone <c>fido2</c> mount's identical flow is
/// <see cref="AuthOperations.Fido2"/>; one implementation drives both.
/// </para>
/// <para>
/// <see cref="ListUsersInfoAsync"/> lands in M8 (D-M8-7), named exactly as 14 §Bulk metadata
/// listings names it — <c>Auth.Userpass.ListUsersInfo</c>, with no <c>.Admin</c> segment.
/// Appendix A's endpoint catalogue nests every other Userpass administration operation, including
/// this one, under <c>Auth.Userpass.Admin.*</c>; none of that surface (<c>ListUsers</c>,
/// <c>ReadUser</c>/<c>WriteUser</c>/<c>DeleteUser</c>, lockout, MFA, FIDO2 admin) is built by this
/// milestone, and the naming discrepancy between the two sections is unresolved — flagged here,
/// not decided, the same way DR-0012 D-M7-11 leaves <c>Namespace</c> vs. <c>NamespaceSummary</c>
/// open rather than guessing.
/// </para>
/// </remarks>
public sealed class UserpassOperations
{
    private readonly LoginRunner runner;
    private readonly Fido2LoginFlow fido2;
    private readonly LogicalOperations logical;

    internal UserpassOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
        runner = new LoginRunner(context, activeNamespace);
        fido2 = new Fido2LoginFlow(context, activeNamespace, standalone: false);
    }

    /// <summary>
    /// AUT-030: <c>POST auth/{mount}/login/{username}</c> with body
    /// <c>{"password": "…", "totp_code": "…"?}</c>. On success the client holds the new token; on
    /// any rejection it holds none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The username is URL-path-encoded by the one encoder every path goes through (TRN-020), and
    /// <c>totp_code</c> is <b>omitted</b> from the body when <paramref name="totpCode"/> is absent
    /// rather than sent as an empty string.
    /// </para>
    /// <para>
    /// <b>AUT-032: a locked account is not fixed by retrying, and this SDK does not retry it.</b>
    /// <c>BV-AUTH-006 AccountLocked</c> arrives as an HTTP <c>200</c> (the login-response contract),
    /// so it never reaches the CFG-051…055 retry loop's failure path at all, and the code is
    /// <c>Retryable = false</c> in the generated catalogue because ERR-006's retryable set does not
    /// contain it. Wait <c>Details.retry_after_secs</c> seconds, or ask an administrator to unlock
    /// the account; a further attempt before then extends the lockout rather than shortening it.
    /// </para>
    /// <para>
    /// <b>AUT-100: the credentials are not retained.</b> A successful login installs a
    /// <see cref="TokenSourceKind.Static"/> source holding the issued token, and
    /// <paramref name="password"/> is referenced only for the duration of the call. An application
    /// that wants the SDK to be able to log in again — AUT-002's lazy login, AUT-003's re-login —
    /// installs a <see cref="TokenSource.Login"/> source instead, which is the one place AUT-100
    /// allows credentials to be kept.
    /// </para>
    /// </remarks>
    /// <param name="username">The account name; URL-path-encoded when sent (AUT-030).</param>
    /// <param name="password">The password, in a redacting type (AUT-031).</param>
    /// <param name="totpCode">The TOTP code, when the account requires one (<c>BV-AUTH-007</c>).</param>
    /// <param name="mount">The auth mount path segment. Default <c>userpass</c>.</param>
    /// <param name="options">Per-request options (CFG-060).</param>
    /// <param name="cancellationToken">Runtime cancellation.</param>
    /// <exception cref="BastionVaultException">
    /// <c>BV-AUTH-003</c> and its AUT-011 refinements (<c>BV-AUTH-004</c>…<c>BV-AUTH-009</c>,
    /// <c>BV-AUTH-012</c>…<c>BV-AUTH-014</c>, <c>BV-RATE-001</c>) for a rejected login.
    /// </exception>
    public Task<AuthInfo> LoginAsync(
        string username,
        SecretString password,
        string? totpCode = null,
        string mount = "userpass",
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return runner.LoginAsync(
                LoginCredentials.ForUserpass(username, password, totpCode, mount),
                install: true,
                options,
                cancellationToken);
    }

    /// <summary>
    /// AUT-035: <c>POST auth/{mount}/fido2/login/begin</c>, unauthenticated, returning the
    /// server's WebAuthn assertion options uninterpreted.
    /// </summary>
    /// <remarks>
    /// An account whose password login has been disabled in favour of a security key answers a
    /// <see cref="LoginAsync"/> attempt with <c>BV-AUTH-009</c> (AUT-011); this pair is what that
    /// code points the caller at.
    /// </remarks>
    /// <param name="username">The account the assertion is being requested for.</param>
    /// <param name="mount">The auth mount path segment. Default <c>userpass</c>.</param>
    /// <param name="options">Per-request options (CFG-060).</param>
    /// <param name="cancellationToken">Runtime cancellation.</param>
    public Task<WebAuthnAssertionOptions> Fido2LoginBeginAsync(
        string username,
        string mount = "userpass",
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return fido2.BeginAsync(username, mount, options, cancellationToken);
    }

    /// <summary>
    /// AUT-035: <c>POST auth/{mount}/fido2/login/complete</c>. Completion follows the login
    /// response contract, so every AUT-010…AUT-013 rule applies unchanged.
    /// </summary>
    /// <param name="username">The account the assertion belongs to.</param>
    /// <param name="credentialJson">The authenticator's response, as opaque JSON (AUT-035).</param>
    /// <param name="mount">The auth mount path segment. Default <c>userpass</c>.</param>
    /// <param name="options">Per-request options (CFG-060).</param>
    /// <param name="cancellationToken">Runtime cancellation.</param>
    /// <exception cref="BastionVaultException">
    /// <c>BV-INPUT-001</c> when <paramref name="credentialJson"/> is not well-formed JSON;
    /// <c>BV-AUTH-003</c> and its AUT-011 refinements for a rejected assertion.
    /// </exception>
    public Task<AuthInfo> Fido2LoginCompleteAsync(
        string username,
        string credentialJson,
        string mount = "userpass",
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return fido2.CompleteAsync(username, credentialJson, mount, options, cancellationToken);
    }

    /// <summary>
    /// 14 §Bulk metadata listings: <c>GET auth/{mount}/users-info?after=&amp;limit=</c>
    /// (PAG-001…PAG-003, PAG-005), D-M8-7's Userpass half of the two areas M8 wires. See this
    /// class's remarks for the <c>Auth.Userpass.Admin</c> naming discrepancy this method does not
    /// resolve.
    /// </summary>
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
    /// shared machinery (<see cref="PagingWire.IteratePagesAsync{T}"/>).
    /// </summary>
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
