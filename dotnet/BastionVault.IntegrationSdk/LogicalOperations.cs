using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// The four logical primitives (TRN-001) plus the <see cref="RawAsync"/> escape hatch, reached from
/// <see cref="BastionVaultClient.Logical"/>. Every typed operation in later milestones is built on
/// these.
/// </summary>
/// <remarks>ERR-061's common error set (<c>BV-CONFIG-*</c>, <c>BV-TRANSPORT-*</c>, <c>BV-AUTH-001</c>, <c>BV-AUTHZ-001</c>, <c>BV-SERVER-*</c>, <c>BV-RATE-*</c>) applies to every operation below and is not repeated per member.</remarks>
public sealed class LogicalOperations
{
    private readonly ClientContext context;
    private readonly string activeNamespace;

    internal LogicalOperations(ClientContext context, string activeNamespace)
    {
        this.context = context;
        this.activeNamespace = activeNamespace;
    }

    /// <summary><c>GET path</c>. Returns <see langword="null"/> on a <c>404</c> with an empty body (TRN-050).</summary>
    /// <param name="path">The wire-relative request path (caller-supplied, e.g. <c>secret/data/app</c>); no prefix or mount is added.</param>
    /// <param name="options">Per-call overrides (namespace, timeout, API version). <see langword="null"/> uses the client defaults.</param>
    /// <param name="cancellationToken">Runtime cancellation.</param>
    /// <returns>The parsed <see cref="Response"/>, or <see langword="null"/> when the server answers a <c>404</c> with an empty body (TRN-050).</returns>
    /// <remarks>Conformance: Core — this primitive underlies every typed operation (TRN-001). No error codes beyond the common set (ERR-061); the common set is documented once in <see cref="LogicalOperations"/>'s type-level remarks.</remarks>
    /// <spec>Logical.Read — TRN-001</spec>
    public async Task<Response?> ReadAsync(string path, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return await ExecuteShapedAsync(
            "GET", path, null, options, defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The one path from a logical or typed operation to <see cref="RequestExecutor"/> and back
    /// through <see cref="Shape"/>. <c>Auth.*</c> reaches the executor through here (D-M2-4), so
    /// no auth operation builds its own URL, its own status mapping or its own envelope parsing;
    /// it differs from <see cref="ReadAsync"/> only in the flags TRN-050 and AUT-084 disagree
    /// about — a <c>404</c> with an empty body is absence for <c>Logical.Read</c> and
    /// <c>BV-NOTFOUND-006</c> for a token lookup — and in <paramref name="isLogin"/>, which the
    /// login runner sets because it knows it is performing a login and CFG-020's anchored path
    /// pattern cannot be matched against an unencoded AUT-030 username (TRN-020).
    /// <para>
    /// <paramref name="pathIsEncoded"/> is the encoding half of <paramref name="isLogin"/> on its
    /// own, for a caller that pre-encodes its path without being a login: every KV operation, whose
    /// <c>path</c> is caller-supplied and multi-segment (KV2-030). Reusing <paramref name="isLogin"/>
    /// would also suppress the token header and the ERR-022 refusal, which KV must keep.
    /// </para>
    /// <para>
    /// <paramref name="nodeLocal"/> is DSC-045's failover exclusion and
    /// <paramref name="nonRetryable"/> is SYS-013's retry exclusion. Both default to
    /// <see langword="false"/>, so every operation landed before M7 is byte-for-byte unchanged;
    /// <c>Sys.Seal</c> and <c>Sys.Unseal</c> are the only callers that set either.
    /// </para>
    /// </summary>
    internal async Task<Response?> ExecuteShapedAsync(
        string method,
        string path,
        ReadOnlyMemory<byte>? body,
        RequestOptions? options,
        bool defaultIdempotent,
        bool treatNotFoundEmptyAsAbsent,
        CancellationToken cancellationToken,
        bool isLogin = false,
        bool pathIsEncoded = false,
        bool nodeLocal = false,
        bool nonRetryable = false,
        string? endpointOverride = null)
    {
        RequestExecutor executor = new(context, activeNamespace);
        RequestExecutor.Outcome outcome = await executor.ExecuteAsync(
            method, path, body, options, defaultIdempotent, treatNotFoundEmptyAsAbsent, cancellationToken,
            isLogin: isLogin, pathIsEncoded: pathIsEncoded, nodeLocal: nodeLocal, nonRetryable: nonRetryable,
            endpointOverride: endpointOverride).ConfigureAwait(false);
        return Shape(outcome);
    }

    /// <summary>
    /// SYS-090's seam: the same executor and the same retry loop, with one half of the exchange in
    /// <c>application/octet-stream</c> instead of JSON. Internal — the specification names no
    /// public binary primitive, and <see cref="RawAsync"/> remains the documented escape hatch.
    /// </summary>
    internal async Task<RawResponse> ExecuteBinaryAsync(
        string method,
        string path,
        ReadOnlyMemory<byte>? body,
        RequestExecutor.BinaryShape binary,
        RequestOptions? options,
        CancellationToken cancellationToken)
    {
        RequestExecutor executor = new(context, activeNamespace);
        return await executor.ExecuteBinaryAsync(
            method, path, body, binary, options,
            // SYS-090: excluded from failover (the DSC-045 seam M5 landed) and from retry (the
            // SYS-013 flag slice a landed). No third mechanism.
            nodeLocal: true,
            nonRetryable: true,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary><c>POST path</c> (server also accepts <c>PUT</c>).</summary>
    /// <remarks><c>path</c> is the wire-relative path; <c>body</c> is sent verbatim, <see langword="null"/> for no body. Returns the parsed <see cref="Response"/>, or <see langword="null"/> for a 204 or an empty body. Conformance: Core (TRN-001). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Logical.Write — TRN-001</spec>
    public async Task<Response?> WriteAsync(string path, JsonElement? body = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ReadOnlyMemory<byte>? bytes = body is { } value ? JsonSerializer.SerializeToUtf8Bytes(value) : null;
        return await ExecuteShapedAsync(
            "POST", path, bytes, options, defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary><c>DELETE path</c> with an optional JSON body (KV v2 <c>versions</c>).</summary>
    /// <remarks><c>path</c> is the wire-relative path; <c>body</c> is sent verbatim, <see langword="null"/> for no body. Returns the parsed <see cref="Response"/>, or <see langword="null"/> for a 204 or an empty body. Conformance: Core (TRN-001). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Logical.Delete — TRN-001</spec>
    public async Task<Response?> DeleteAsync(string path, JsonElement? body = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ReadOnlyMemory<byte>? bytes = body is { } value ? JsonSerializer.SerializeToUtf8Bytes(value) : null;
        return await ExecuteShapedAsync(
            "DELETE", path, bytes, options, defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The literal <c>LIST</c> verb (TRN-010). Returns <see langword="null"/> on a <c>404</c> with an empty body.</summary>
    /// <param name="path">The wire-relative request path; the trailing slash convention, if any, is the caller's responsibility.</param>
    /// <param name="options">Per-call overrides. <see langword="null"/> uses the client defaults.</param>
    /// <param name="cancellationToken">Runtime cancellation.</param>
    /// <returns>The parsed <see cref="Response"/>, or <see langword="null"/> on a <c>404</c> with an empty body.</returns>
    /// <remarks>Conformance: Core (TRN-010). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Logical.List — TRN-010</spec>
    public async Task<Response?> ListAsync(string path, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return await ExecuteShapedAsync(
            "LIST", path, null, options, defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The escape hatch (D-M1b-12): <paramref name="absolutePath"/> starts with <c>/</c> and no
    /// prefix is added; the body is returned unparsed. Errors still map through the same status→code
    /// function as every other operation.
    /// </summary>
    /// <remarks><c>method</c> is the literal HTTP verb; <c>absolutePath</c> starts with <c>/</c>, sent as-is; <c>body</c> is sent verbatim. Returns the unparsed <see cref="RawResponse"/>, never <see langword="null"/>. Conformance: Core (D-M1b-12). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Logical.Raw — TRN-001</spec>
    public async Task<RawResponse> RawAsync(string method, string absolutePath, JsonElement? body = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(method);
        ArgumentException.ThrowIfNullOrEmpty(absolutePath);
        RequestExecutor executor = new(context, activeNamespace);
        ReadOnlyMemory<byte>? bytes = body is { } value ? JsonSerializer.SerializeToUtf8Bytes(value) : null;
        return await executor.ExecuteRawAsync(method, absolutePath, bytes, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// ERR-050: current servers never emit <c>warnings</c>, but when one does the SDK surfaces the
    /// list on <see cref="Response.Warnings"/> and logs each entry at <i>warning</i> level through
    /// the CNF-030 logger seam. A warning is never turned into an error (D-M1c-11).
    /// </summary>
    private IReadOnlyList<string> ExtractWarnings(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object
            || !body.TryGetProperty("warnings", out JsonElement element)
            || element.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        List<string> warnings = [];
        foreach (JsonElement item in element.EnumerateArray())
        {
            string? text = item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText();
            if (!string.IsNullOrEmpty(text))
            {
                warnings.Add(text);
            }
        }

        foreach (string warning in warnings)
        {
            context.Logger.Warn($"BastionVault server warning: {warning}");
        }

        return warnings;
    }

    private Response? Shape(RequestExecutor.Outcome outcome)
    {
        if (outcome.IsEmpty || outcome.IsNotFoundEmpty)
        {
            return null;
        }

        if (outcome.Body is not { } body)
        {
            // 304: a Response with no Data, per D-M1b-10.
            return new Response { StatusCode = outcome.StatusCode, Headers = outcome.Headers };
        }

        bool shapeA = body.ValueKind == JsonValueKind.Object
            && (body.TryGetProperty("data", out _)
                || (body.TryGetProperty("auth", out JsonElement authCandidate)
                    && authCandidate.ValueKind == JsonValueKind.Object
                    && authCandidate.TryGetProperty("client_token", out _)));

        IReadOnlyList<string> warnings = ExtractWarnings(body);

        IReadOnlyDictionary<string, JsonElement>? data;
        AuthInfo? auth = null;
        string? leaseId = null;
        bool? renewable = null;
        TimeSpan? leaseDuration = null;

        if (shapeA)
        {
            data = body.TryGetProperty("data", out JsonElement dataElement) && dataElement.ValueKind == JsonValueKind.Object
                ? dataElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal)
                : null;

            if (body.TryGetProperty("auth", out JsonElement authElement) && authElement.ValueKind == JsonValueKind.Object
                && authElement.TryGetProperty("client_token", out JsonElement tokenElement))
            {
                auth = new AuthInfo
                {
                    ClientToken = new SecretString(tokenElement.GetString()),
                    Policies = authElement.TryGetProperty("policies", out JsonElement policiesElement) && policiesElement.ValueKind == JsonValueKind.Array
                        ? policiesElement.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToList()
                        : Array.Empty<string>(),
                    Metadata = authElement.TryGetProperty("metadata", out JsonElement metadataElement) && metadataElement.ValueKind == JsonValueKind.Object
                        ? metadataElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty, StringComparer.Ordinal)
                        : null,
                    LeaseDuration = authElement.TryGetProperty("lease_duration", out JsonElement authLeaseElement) && authLeaseElement.ValueKind == JsonValueKind.Number
                        ? TimeSpan.FromSeconds(authLeaseElement.GetDouble())
                        : null,
                    Renewable = authElement.TryGetProperty("renewable", out JsonElement renewableElement) && renewableElement.ValueKind == JsonValueKind.True,
                    // AUT-013: recorded here, in the one place an `auth` object becomes an
                    // AuthInfo, so a login, an AUT-082 token create and an AUT-080 renew all carry
                    // the same notion of "when this credential was received" and no call site can
                    // forget to stamp it.
                    IssuedAt = context.Clock.NowUtc(),
                };
            }

            if (body.TryGetProperty("lease_id", out JsonElement leaseIdElement) && leaseIdElement.ValueKind == JsonValueKind.String)
            {
                string value = leaseIdElement.GetString() ?? string.Empty;
                leaseId = value.Length > 0 ? value : null; // TRN-041
            }

            if (body.TryGetProperty("renewable", out JsonElement topRenewable) && topRenewable.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                renewable = topRenewable.GetBoolean();
            }

            if (body.TryGetProperty("lease_duration", out JsonElement topLeaseDuration) && topLeaseDuration.ValueKind == JsonValueKind.Number)
            {
                leaseDuration = TimeSpan.FromSeconds(topLeaseDuration.GetDouble());
            }
        }
        else
        {
            data = body.ValueKind == JsonValueKind.Object
                ? body.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal)
                : null;
        }

        return new Response
        {
            Data = data,
            Auth = auth,
            LeaseId = leaseId,
            Renewable = renewable,
            LeaseDuration = leaseDuration,
            Warnings = warnings,
            StatusCode = outcome.StatusCode,
            Headers = outcome.Headers,
            Raw = body,
        };
    }
}
