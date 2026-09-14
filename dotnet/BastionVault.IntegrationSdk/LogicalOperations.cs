using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// The four logical primitives (TRN-001) plus the <see cref="RawAsync"/> escape hatch, reached from
/// <see cref="BastionVaultClient.Logical"/>. Every typed operation in later milestones is built on
/// these.
/// </summary>
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
    /// </summary>
    internal async Task<Response?> ExecuteShapedAsync(
        string method,
        string path,
        ReadOnlyMemory<byte>? body,
        RequestOptions? options,
        bool defaultIdempotent,
        bool treatNotFoundEmptyAsAbsent,
        CancellationToken cancellationToken,
        bool isLogin = false)
    {
        RequestExecutor executor = new(context, activeNamespace);
        RequestExecutor.Outcome outcome = await executor.ExecuteAsync(
            method, path, body, options, defaultIdempotent, treatNotFoundEmptyAsAbsent, cancellationToken, isLogin: isLogin).ConfigureAwait(false);
        return Shape(outcome);
    }

    /// <summary><c>POST path</c> (server also accepts <c>PUT</c>).</summary>
    public async Task<Response?> WriteAsync(string path, JsonElement? body = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ReadOnlyMemory<byte>? bytes = body is { } value ? JsonSerializer.SerializeToUtf8Bytes(value) : null;
        return await ExecuteShapedAsync(
            "POST", path, bytes, options, defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary><c>DELETE path</c> with an optional JSON body (KV v2 <c>versions</c>).</summary>
    public async Task<Response?> DeleteAsync(string path, JsonElement? body = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ReadOnlyMemory<byte>? bytes = body is { } value ? JsonSerializer.SerializeToUtf8Bytes(value) : null;
        return await ExecuteShapedAsync(
            "DELETE", path, bytes, options, defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The literal <c>LIST</c> verb (TRN-010). Returns <see langword="null"/> on a <c>404</c> with an empty body.</summary>
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

        List<string> warnings = new();
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
