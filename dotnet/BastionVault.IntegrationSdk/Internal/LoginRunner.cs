using System.Buffers;
using System.Text.Json;

namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// The one implementation of the login-response contract (<c>05-authentication.md</c> §Login
/// response contract): builds the request, sends it through the shared
/// <see cref="RequestExecutor"/>, and turns the answer into an <see cref="AuthInfo"/> (AUT-013) or
/// into the recognised failure AUT-010 and AUT-011 require.
/// </summary>
/// <remarks>
/// <para>
/// <b>D-M2-4a's second call site, and nothing more.</b> A <c>200</c> carrying no
/// <c>auth.client_token</c> is a <i>failure</i>, and all three executors hand a 2xx body back
/// before recognition runs — so the <c>data.error</c> literals of Appendix B §2 are never
/// presented to <see cref="MessageRecognition.Recognise"/> by the executor. This calls the
/// <b>shared generated table</b> itself. It builds no table, no status mapping and no URL of its
/// own, which is D-M2-4's prohibition still standing: AUT-012's <c>400</c> travels the executor's
/// ordinary non-2xx path and is not special-cased here at all.
/// </para>
/// <para>
/// <b>Every error this type produces is marked <c>RecognizedAtSource</c></b> (D-M2-25 item 2).
/// Marking by origin rather than by a code whitelist is what lets the executor tell "the login the
/// SDK performed for a <see cref="TokenSourceKind.Login"/> source failed, with its own code" from
/// "a source delegate leaked an unrelated coded exception", and it is also what stops AUT-003's
/// replay keying on the <c>BV-AUTHZ-001</c> of an AUT-041 gated login instead of the outer
/// request's.
/// </para>
/// </remarks>
internal sealed class LoginRunner
{
    private readonly ClientContext context;
    private readonly string activeNamespace;

    public LoginRunner(ClientContext context, string activeNamespace)
    {
        this.context = context;
        this.activeNamespace = activeNamespace;
    }

    /// <summary>
    /// Performs the login <paramref name="credentials"/> describes.
    /// </summary>
    /// <param name="credentials">Which flow, and with what.</param>
    /// <param name="install">
    /// <see langword="true"/> for a one-shot <c>Auth.Userpass.Login</c>/<c>Auth.AppId.Login</c>
    /// call, which replaces the client's source with a <see cref="TokenSourceKind.Static"/> one and
    /// therefore retains no credentials (AUT-100's default arm). <see langword="false"/> when the
    /// caller <i>is</i> a <see cref="TokenSourceKind.Login"/> source resolving itself: replacing
    /// the source there would destroy the very source performing the login.
    /// </param>
    /// <param name="options">Per-request options (CFG-060).</param>
    /// <param name="cancellationToken">Runtime cancellation.</param>
    public async Task<AuthInfo> LoginAsync(
        LoginCredentials credentials,
        bool install,
        RequestOptions? options,
        CancellationToken cancellationToken)
    {
        string path = LoginPath(credentials);
        ReadOnlyMemory<byte> body = LoginBody(credentials);
        LogicalOperations logical = new(context, activeNamespace);

        Response? response;
        try
        {
            response = await logical.ExecuteShapedAsync(
                "POST", path, body, options,
                defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken,
                isLogin: true).ConfigureAwait(false);
        }
        catch (BastionVaultException failure)
        {
            // AUT-012's 400, AUT-041's 403, the login path's 429 and every transport failure: the
            // executor already mapped them through the shared table. Only the marker is added.
            throw failure.MarkRecognizedAtSource();
        }

        // AUT-010: a 200 without `auth.client_token` is a login failure, and an empty token is one
        // too — `HasValue` covers both `"client_token": ""` and the field being absent, which is
        // the "MUST never store an empty token" half of the same requirement.
        if (response?.Auth is not { ClientToken.HasValue: true } auth)
        {
            throw Rejected(response, path, options);
        }

        // AUT-013: the credential is recorded before it is returned, so `Auth.CurrentToken`,
        // AUT-003's token age and AUT-090's future schedule all read the same issue time.
        context.RecordLogin(auth, install);
        return auth;
    }

    /// <summary>AUT-030 / AUT-040: the login path, with the mount as a path segment.</summary>
    /// <remarks>
    /// <para>
    /// The username is encoded <b>here</b>, while it is still a value, and the request is marked
    /// <c>isLogin</c> so the executor does not encode it a second time. AUT-030 requires it
    /// URL-path-encoded per TRN-020, and TRN-020's set includes <c>/</c> "inside a segment" and
    /// <c>?</c> — neither of which the whole-path encoder can see once the username has been
    /// interpolated into a string: there, a <c>/</c> is a separator to preserve and a <c>?</c>
    /// begins a query. A username of <c>a/b</c> would otherwise be sent as the two-segment path
    /// <c>auth/userpass/login/a/b</c>, which is a different endpoint, and one containing <c>?</c>
    /// would have its tail silently moved into the query string.
    /// </para>
    /// <para>
    /// The mount goes through the <i>fragment</i> encoder instead, because a mount path may
    /// legitimately contain a <c>/</c> as a separator and no requirement treats it as one segment.
    /// Two encoders, two kinds of part, and the assembled path is then final — which is what
    /// <c>isLogin</c> tells the executor.
    /// </para>
    /// </remarks>
    private static string LoginPath(LoginCredentials credentials)
    {
        string mount = UrlBuilder.EncodePathFragment(credentials.Mount);
        return credentials.Method == AuthMethod.Userpass
            ? $"auth/{mount}/login/{UrlBuilder.EncodePathSegment(credentials.Username!)}"
            : $"auth/{mount}/login";
    }

    /// <summary>
    /// The request body of <c>05-authentication.md</c> §Method: Userpass / §Method: AppID. Every
    /// optional field is <b>omitted</b> when absent rather than sent empty — AUT-030 says so for
    /// <c>totp_code</c> and AUT-040 for <c>machine_token</c>, and OVR-007 says so generally.
    /// </summary>
    private static ReadOnlyMemory<byte> LoginBody(LoginCredentials credentials)
    {
        ArrayBufferWriter<byte> buffer = new();
        using Utf8JsonWriter writer = new(buffer);
        writer.WriteStartObject();
        if (credentials.Method == AuthMethod.Userpass)
        {
            writer.WriteString("password", credentials.Password?.Reveal() ?? string.Empty);
            WriteIfPresent(writer, "totp_code", credentials.TotpCode);
        }
        else
        {
            writer.WriteString("role_id", credentials.RoleId);
            WriteIfPresent(writer, "secret_id", credentials.SecretId?.Reveal());
            WriteIfPresent(writer, "machine_token", credentials.MachineToken?.Reveal());
        }

        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenMemory;
    }

    private static void WriteIfPresent(Utf8JsonWriter writer, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            writer.WriteString(name, value);
        }
    }

    /// <summary>
    /// AUT-010 and AUT-011: the <c>200</c> rejection. <c>data.error</c> goes to the shared
    /// recogniser at status <c>200</c>; an unrecognised message degrades to
    /// <c>BV-AUTH-003 LoginRejected</c>, which is coarse but correct, so a new server rejection
    /// string is a one-row Appendix B edit and a regeneration rather than an SDK change (D-M2-4a).
    /// </summary>
    /// <remarks>
    /// Retryability is <c>ERR-006</c>'s, read from the generated catalogue entry and never
    /// overridden here — including for <c>rate_limited: …</c> → <c>BV-RATE-001</c>, which
    /// Appendix B renders <c>no</c>. AUT-032's "MUST NOT auto-retry a locked account" needs no code
    /// of its own for the same reason it needs no exception: a <c>200</c> never enters the retry
    /// loop's failure path at all, and <c>BV-AUTH-006</c> is not in <c>ERR-006</c>'s retryable set.
    /// </remarks>
    private BastionVaultException Rejected(Response? response, string path, RequestOptions? options)
    {
        string? serverMessage = ReadDataError(response);
        string displayPath = DisplayPath(options, path);
        string redactedPath = ErrorPaths.Redact(displayPath)!;
        MessageRecognition.Recognised? recognised = MessageRecognition.Recognise(serverMessage, 200, redactedPath);
        string code = recognised?.Code ?? ErrorCodes.AuthLoginRejected;
        ErrorCatalogEntry entry = ErrorCatalog.Require(code);

        string hint = HintEnrichment.InterpolatePath(entry.Hint, redactedPath);
        hint = HintEnrichment.Enrich(
            code,
            hint,
            new HintEnrichment.Context(
                200,
                null,
                redactedPath,
                EffectiveNamespace(options),
                HasCaCertificate: context.Config.CaCertPath is not null || context.Config.CaCertPem is not null,
                context.Config.Address));

        Dictionary<string, object?> details = recognised is { } value
            ? new Dictionary<string, object?>(value.Details, StringComparer.Ordinal)
            : new Dictionary<string, object?>(StringComparer.Ordinal);
        details["path"] = redactedPath;

        return BastionVaultException.Request(
            code,
            entry.Category,
            entry.Message,
            hint,
            retryable: entry.Retryable,
            // One request was sent and answered. The rejection is not a retry of anything, so the
            // count is the executor's own: exactly one attempt.
            attempts: 1,
            serverMessage: serverMessage,
            statusCode: 200,
            method: "POST",
            path: displayPath,
            address: context.Config.Address,
            details: details).MarkRecognizedAtSource();
    }

    /// <summary>
    /// The rejection reason: <c>data.error</c>, when the body carries one. A <c>200</c> with no
    /// body, or with a <c>data</c> object that has no <c>error</c>, yields
    /// <see langword="null"/> — and therefore <c>BV-AUTH-003</c> with no <c>ServerMessage</c>,
    /// which is what AUT-010 describes for a response that is neither a success nor a stated
    /// reason.
    /// </summary>
    private static string? ReadDataError(Response? response)
    {
        return response?.Data is { } data
                && data.TryGetValue("error", out JsonElement error)
                && error.ValueKind == JsonValueKind.String
                ? error.GetString()
                : null;
    }

    private string EffectiveNamespace(RequestOptions? options)
    {
        return (options?.Namespace ?? activeNamespace).TrimEnd('/');
    }

    /// <summary>ERR-001's <c>Path</c>, in the same <c>[ns=…] </c> form the executor builds.</summary>
    private string DisplayPath(RequestOptions? options, string path)
    {
        string ns = EffectiveNamespace(options);
        return ns.Length == 0 ? path : $"[ns={ns}] {path}";
    }
}
