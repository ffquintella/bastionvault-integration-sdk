using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// Builds and sends one logical request, running the single CFG-051..055 retry loop
/// (D-M1b-24: written once, shared by <see cref="ExecuteAsync"/> and <see cref="ExecuteRawAsync"/>,
/// which differ only in how a successful <see cref="TransportResponse"/> is classified — D-M1b-12)
/// around <see cref="ITransport.SendAsync"/>, and mapping the response through
/// <see cref="StatusCodeMapper"/> (ERR-020, D-M1b-4).
/// </summary>
internal sealed class RequestExecutor
{
    private const int MaxRequestBodyBytes = 32 * 1024 * 1024; // TRN-032, a fixed server limit — not configurable.
    private static readonly System.Text.RegularExpressions.Regex LoginPathPattern =
        new(@"^auth/[^/]+/login(/[^/]+)?$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private readonly ClientContext context;
    private readonly string activeNamespace;

    public RequestExecutor(ClientContext context, string activeNamespace)
    {
        this.context = context;
        this.activeNamespace = activeNamespace;
    }

    /// <summary>The outcome of one logical request, before <see cref="LogicalOperations"/> shapes it for its specific operation.</summary>
    public readonly struct Outcome
    {
        public required bool IsEmpty { get; init; }
        public required bool IsNotFoundEmpty { get; init; }
        public JsonElement? Body { get; init; }
        public required int StatusCode { get; init; }
        public required IReadOnlyDictionary<string, string> Headers { get; init; }
    }

    /// <summary>What one attempt's <see cref="TransportResponse"/> resolves to: a terminal result, or a failure to feed the retry decision.</summary>
    private readonly struct Verdict<TResult>
    {
        public required bool IsSuccess { get; init; }
        public TResult? Value { get; init; }
        public BastionVaultException? Failure { get; init; }
    }

    public async Task<Outcome> ExecuteAsync(
        string method,
        string rawPath,
        ReadOnlyMemory<byte>? jsonBody,
        RequestOptions? options,
        bool defaultIdempotent,
        bool treatNotFoundEmptyAsAbsent,
        CancellationToken cancellationToken)
    {
        options ??= new RequestOptions();
        GuardInputPreflight(options, jsonBody);

        ClientConfig config = context.Config;
        string apiVersion = options.ApiVersion ?? config.ApiPrefix;
        Uri uri = BuildUri(config, apiVersion, rawPath, isRaw: false);
        string displayPath = BuildDisplayPath(rawPath);
        bool isIdempotent = options.Idempotent ?? defaultIdempotent;

        return await RunLoopAsync(
            method,
            uri,
            jsonBody,
            options,
            isIdempotent,
            displayPath,
            (response, attempt) =>
            {
                Outcome? outcome = TryHandleResponse(response, method, displayPath, config.Address, attempt, treatNotFoundEmptyAsAbsent, out BastionVaultException? failure);
                return outcome is { } value
                    ? new Verdict<Outcome> { IsSuccess = true, Value = value }
                    : new Verdict<Outcome> { IsSuccess = false, Failure = failure };
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends <see cref="RawResponse"/> without envelope parsing (D-M1b-12): errors still map through <see cref="StatusCodeMapper"/>.</summary>
    public async Task<RawResponse> ExecuteRawAsync(string method, string absolutePath, ReadOnlyMemory<byte>? body, RequestOptions? options, CancellationToken cancellationToken)
    {
        options ??= new RequestOptions();
        ClientConfig config = context.Config;
        Uri uri = BuildUri(config, options.ApiVersion ?? config.ApiPrefix, absolutePath, isRaw: true);
        bool isIdempotent = method is "GET" or "HEAD" or "OPTIONS" or "LIST";

        return await RunLoopAsync(
            method,
            uri,
            body,
            options,
            isIdempotent,
            absolutePath,
            (response, attempt) =>
            {
                if (response.StatusCode is 204 or 304 || response.StatusCode is >= 200 and < 300)
                {
                    return new Verdict<RawResponse>
                    {
                        IsSuccess = true,
                        Value = new RawResponse { StatusCode = response.StatusCode, Headers = response.Headers, Body = response.Body },
                    };
                }

                BastionVaultException failure;
                if (response.StatusCode is >= 300 and <= 399)
                {
                    failure = StatusCodeMapper.Map(new StatusCodeMapper.Context(
                        response.StatusCode, null, Array.Empty<string>(), null, method, absolutePath, config.Address, attempt));
                }
                else
                {
                    (string? serverMessage, IReadOnlyList<string> serverErrors, bool bodyEmpty) parsed = ParseErrorBody(response.Body);
                    TimeSpan? retryAfter = ParseRetryAfter(response.Headers);
                    failure = StatusCodeMapper.Map(new StatusCodeMapper.Context(
                        response.StatusCode, parsed.serverMessage, parsed.serverErrors, retryAfter, method, absolutePath, config.Address, attempt));
                }

                return new Verdict<RawResponse> { IsSuccess = false, Failure = failure };
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static void GuardInputPreflight(RequestOptions options, ReadOnlyMemory<byte>? jsonBody)
    {
        if (options.WrapTtl is not null)
        {
            ErrorCatalogue.Entry wrapEntry = ErrorCatalogue.Get(ErrorCodes.InputUnsupportedOption);
            throw BastionVaultException.Request(
                ErrorCodes.InputUnsupportedOption,
                wrapEntry.Category,
                wrapEntry.Message,
                wrapEntry.Hint,
                retryable: false,
                attempts: 0);
        }

        if (jsonBody is { Length: > MaxRequestBodyBytes })
        {
            ErrorCatalogue.Entry bodyEntry = ErrorCatalogue.Get(ErrorCodes.InputBodyTooLarge);
            throw BastionVaultException.Request(
                ErrorCodes.InputBodyTooLarge,
                bodyEntry.Category,
                bodyEntry.Message,
                bodyEntry.Hint,
                retryable: false,
                attempts: 0);
        }
    }

    /// <summary>
    /// The one CFG-051..055 retry loop (D-M1b-24): builds the request, sends it, fires the
    /// observability hook once per attempt (RES-002), pauses the rate gate on any <c>429</c>
    /// (D-M1b-22), and applies the retry-eligibility/backoff/<c>TotalTimeout</c> (RES-004) rules —
    /// identically whether <paramref name="classify"/> is shaping a logical <see cref="Outcome"/> or
    /// a <see cref="RawResponse"/>.
    /// </summary>
    private async Task<TResult> RunLoopAsync<TResult>(
        string method,
        Uri uri,
        ReadOnlyMemory<byte>? body,
        RequestOptions options,
        bool isIdempotent,
        string displayPath,
        Func<TransportResponse, int, Verdict<TResult>> classify,
        CancellationToken cancellationToken)
    {
        ClientConfig config = context.Config;
        RetryPolicy retryPolicy = config.RetryPolicy;
        int attempt = 0;
        int maxAttempts = Math.Max(1, retryPolicy.MaxAttempts);
        string tokenValue = ResolveToken(options, displayPath);
        string requestId = Guid.NewGuid().ToString("n"); // D-M1b-8: stable across every attempt of this logical operation.
        // RES-004: TotalTimeout bounds attempts *and* backoff together; Timeout bounds each attempt.
        DateTimeOffset? deadline = options.TotalTimeout is { } totalTimeout ? context.Clock.Now() + totalTimeout : null;

        while (true)
        {
            attempt++;
            IReadOnlyDictionary<string, string> requestHeaders = BuildHeaders(config, options, tokenValue, body is not null);
            TransportRequest request = new(method, uri, requestHeaders, body ?? ReadOnlyMemory<byte>.Empty)
            {
                Timeout = options.Timeout ?? config.Timeout,
                ConnectTimeout = config.ConnectTimeout,
                MaxResponseBytes = config.MaxResponseBytes, // D-M1b-20: bound is enforced by the transport while reading.
            };

            BastionVaultException? failure = null;
            TransportResponse? response = null;
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (context.Transport is null)
                {
                    throw new InvalidOperationException("No transport is configured on this client (OVR-001).");
                }

                response = await context.Transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (BastionVaultException exception)
            {
                failure = exception;
            }
            catch (OperationCanceledException)
            {
                BastionVaultException cancelled = TransportFailureMapper.MapCancelled(method, displayPath, config.Address, attempt);
                Notify(method, displayPath, requestId, attempt, null, cancelled.Code, stopwatch.Elapsed);
                throw cancelled;
            }

            Notify(method, displayPath, requestId, attempt, response?.StatusCode, failure?.Code, stopwatch.Elapsed);

            if (response is not null)
            {
                // The transport is the enforcement point for TRN-033 (D-M1b-20); this is a cheap
                // backstop for transports (e.g. FakeTransport) that hand back an in-memory body
                // without themselves bounding the read.
                if (response.Body.Length > config.MaxResponseBytes)
                {
                    throw TransportFailureMapper.MapResponseTooLarge(method, displayPath, config.Address, attempt);
                }

                // D-M1b-22: the rate gate pauses on status 429 itself, not on the mapped code —
                // EFF-003/EFF-004 are written in terms of the status, and BV-RATE-002 is a 429 too.
                if (response.StatusCode == 429)
                {
                    PauseRateGate(response.Headers);
                }

                Verdict<TResult> verdict = classify(response, attempt);
                if (verdict.IsSuccess)
                {
                    return verdict.Value!;
                }

                failure = verdict.Failure;
            }

            BastionVaultException error = failure!;

            bool eligible = attempt < maxAttempts
                && retryPolicy.RetryOn.Contains(error.Code, StringComparer.Ordinal)
                && !IsHardExcluded(error.Code)
                && (isIdempotent || !retryPolicy.RetryIdempotentOnly)
                && (deadline is null || context.Clock.Now() < deadline);

            if (!eligible)
            {
                throw BastionVaultException.Request(
                    error.Code,
                    error.Category,
                    error.Message,
                    error.Hint,
                    error.Retryable,
                    attempts: attempt,
                    serverMessage: error.ServerMessage,
                    serverErrors: error.ServerErrors,
                    statusCode: error.StatusCode,
                    retryAfter: error.RetryAfter,
                    method: method,
                    path: displayPath,
                    address: config.Address,
                    details: error.Details,
                    cause: error.Cause);
            }

            TimeSpan backoff = ComputeBackoff(retryPolicy, attempt, context.JitterSource);
            if (retryPolicy.RespectRetryAfter && error.RetryAfter is { } wait)
            {
                TimeSpan floor = wait > backoff ? wait : backoff;
                TimeSpan cap = TimeSpan.FromTicks(retryPolicy.MaxBackoff.Ticks * 6);
                backoff = floor > cap ? cap : floor;
            }

            if (deadline is { } d)
            {
                TimeSpan remaining = d - context.Clock.Now();
                backoff = backoff < remaining ? backoff : (remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
            }

            await context.Clock.Delay(backoff, cancellationToken).ConfigureAwait(false);
        }
    }

    private void PauseRateGate(IReadOnlyDictionary<string, string> headers)
    {
        TimeSpan? retryAfter = ParseRetryAfter(headers);
        TimeSpan pause = retryAfter is { } wait
            ? (wait < TimeSpan.FromSeconds(30) ? wait : TimeSpan.FromSeconds(30))
            : TimeSpan.FromSeconds(1);
        context.RateGate.Pause(context.Clock.Now() + pause);
    }

    private static Outcome? TryHandleResponse(
        TransportResponse response,
        string method,
        string logicalPath,
        string address,
        int attempt,
        bool treatNotFoundEmptyAsAbsent,
        out BastionVaultException? failure)
    {
        failure = null;
        bool bodyEmptyRaw = IsWhitespaceOrEmpty(response.Body);

        if (response.StatusCode == 204 || (response.StatusCode == 200 && bodyEmptyRaw))
        {
            return new Outcome { IsEmpty = true, IsNotFoundEmpty = false, StatusCode = response.StatusCode, Headers = response.Headers };
        }

        if (response.StatusCode == 404 && bodyEmptyRaw && treatNotFoundEmptyAsAbsent)
        {
            return new Outcome { IsEmpty = false, IsNotFoundEmpty = true, StatusCode = response.StatusCode, Headers = response.Headers };
        }

        if (response.StatusCode is >= 300 and <= 399 && response.StatusCode != 304)
        {
            failure = StatusCodeMapper.Map(new StatusCodeMapper.Context(
                response.StatusCode, null, Array.Empty<string>(), null, method, logicalPath, address, attempt));
            return null;
        }

        if (response.StatusCode == 304)
        {
            return new Outcome { IsEmpty = false, IsNotFoundEmpty = false, Body = null, StatusCode = response.StatusCode, Headers = response.Headers };
        }

        bool success = response.StatusCode is >= 200 and < 300;
        if (bodyEmptyRaw)
        {
            if (success)
            {
                return new Outcome { IsEmpty = true, IsNotFoundEmpty = false, StatusCode = response.StatusCode, Headers = response.Headers };
            }

            failure = StatusCodeMapper.Map(new StatusCodeMapper.Context(
                response.StatusCode, null, Array.Empty<string>(), ParseRetryAfter(response.Headers), method, logicalPath, address, attempt));
            return null;
        }

        if (!TryParseJson(response.Body, out JsonElement parsed, out string snippet))
        {
            failure = StatusCodeMapper.MapNonJson(response.StatusCode, snippet, method, logicalPath, address, attempt);
            return null;
        }

        if (success)
        {
            return new Outcome { IsEmpty = false, IsNotFoundEmpty = false, Body = parsed, StatusCode = response.StatusCode, Headers = response.Headers };
        }

        (string? serverMessage, IReadOnlyList<string> serverErrors) extracted = ExtractServerMessage(parsed);
        TimeSpan? retryAfter = ParseRetryAfter(response.Headers);
        failure = StatusCodeMapper.Map(new StatusCodeMapper.Context(
            response.StatusCode, extracted.serverMessage, extracted.serverErrors, retryAfter, method, logicalPath, address, attempt));
        return null;
    }

    private void Notify(string method, string path, string requestId, int attempt, int? statusCode, string? errorCode, TimeSpan duration)
    {
        if (context.Observer is null)
        {
            return;
        }

        context.Observer.OnRequestCompleted(new RequestEvent(
            Method: method,
            Path: path,
            Namespace: activeNamespace,
            StatusCode: statusCode,
            Duration: duration,
            RequestId: requestId,
            Attempt: attempt,
            ErrorCode: errorCode));
    }

    private static bool IsHardExcluded(string code)
        => code is ErrorCodes.ServerSealed or ErrorCodes.RateLimitedByDosGuard;

    private static TimeSpan ComputeBackoff(RetryPolicy policy, int attempt, IJitterSource jitter)
    {
        double raw = policy.InitialBackoff.TotalMilliseconds * Math.Pow(policy.BackoffMultiplier, attempt - 1);
        double capped = Math.Min(raw, policy.MaxBackoff.TotalMilliseconds);
        double jitterFactor = 1.0 + ((jitter.NextDouble() * 2 - 1) * policy.Jitter);
        double final = Math.Max(0, capped * jitterFactor);
        return TimeSpan.FromMilliseconds(final);
    }

    private static bool IsWhitespaceOrEmpty(ReadOnlyMemory<byte> body)
    {
        if (body.IsEmpty)
        {
            return true;
        }

        ReadOnlySpan<byte> span = body.Span;
        foreach (byte b in span)
        {
            if (b != (byte)' ' && b != (byte)'\t' && b != (byte)'\r' && b != (byte)'\n')
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryParseJson(ReadOnlyMemory<byte> body, out JsonElement element, out string snippet)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            element = document.RootElement.Clone();
            snippet = string.Empty;
            return true;
        }
        catch (JsonException)
        {
            snippet = Sanitize(body);
            element = default;
            return false;
        }
    }

    private static string Sanitize(ReadOnlyMemory<byte> body)
    {
        int length = Math.Min(256, body.Length);
        string raw = Encoding.UTF8.GetString(body.Span[..length]);
        StringBuilder builder = new(raw.Length);
        foreach (char c in raw)
        {
            builder.Append(char.IsControl(c) ? ' ' : c);
        }

        return builder.ToString();
    }

    private static (string? ServerMessage, IReadOnlyList<string> ServerErrors) ExtractServerMessage(JsonElement body)
    {
        if (body.ValueKind == JsonValueKind.Object)
        {
            if (body.TryGetProperty("errors", out JsonElement errorsElement) && errorsElement.ValueKind == JsonValueKind.Array)
            {
                List<string> errors = errorsElement.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToList();
                return (errors.Count > 0 ? string.Join("; ", errors) : null, errors);
            }

            if (body.TryGetProperty("error", out JsonElement errorElement) && errorElement.ValueKind == JsonValueKind.String)
            {
                return (errorElement.GetString(), Array.Empty<string>());
            }
        }

        return (null, Array.Empty<string>());
    }

    private static (string? ServerMessage, IReadOnlyList<string> ServerErrors, bool BodyEmpty) ParseErrorBody(ReadOnlyMemory<byte> body)
    {
        if (IsWhitespaceOrEmpty(body))
        {
            return (null, Array.Empty<string>(), true);
        }

        if (!TryParseJson(body, out JsonElement parsed, out _))
        {
            return (null, Array.Empty<string>(), false);
        }

        (string? serverMessage, IReadOnlyList<string> serverErrors) extracted = ExtractServerMessage(parsed);
        return (extracted.serverMessage, extracted.serverErrors, false);
    }

    private static TimeSpan? ParseRetryAfter(IReadOnlyDictionary<string, string> headers)
    {
        foreach ((string name, string value) in headers)
        {
            if (string.Equals(name, "Retry-After", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out int seconds))
                {
                    return TimeSpan.FromSeconds(seconds);
                }

                if (DateTimeOffset.TryParse(value, out DateTimeOffset when))
                {
                    TimeSpan delta = when - DateTimeOffset.UtcNow;
                    return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
                }
            }
        }

        return null;
    }

    private string ResolveToken(RequestOptions options, string rawPath)
    {
        if (options.Token is not null)
        {
            return options.Token.Reveal() ?? string.Empty;
        }

        string pathOnly = rawPath;
        int queryIndex = pathOnly.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex >= 0)
        {
            pathOnly = pathOnly[..queryIndex];
        }

        string trimmed = pathOnly.TrimStart('/');
        if (LoginPathPattern.IsMatch(trimmed))
        {
            return string.Empty;
        }

        return context.GetToken().Reveal() ?? string.Empty;
    }

    private Dictionary<string, string> BuildHeaders(ClientConfig config, RequestOptions options, string token, bool hasBody)
    {
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string value) in config.Headers)
        {
            headers[name] = value;
        }

        if (options.Headers is not null)
        {
            foreach ((string name, string value) in options.Headers)
            {
                headers[name] = value;
            }
        }

        headers["Accept"] = "application/json";
        if (hasBody)
        {
            headers["Content-Type"] = "application/json";
        }

        if (!string.IsNullOrEmpty(token))
        {
            headers["X-BastionVault-Token"] = token;
        }

        string ns = options.Namespace ?? activeNamespace;
        ns = ns.TrimEnd('/');
        if (ns.Length > 0)
        {
            headers["X-BastionVault-Namespace"] = ns;
        }

        headers["User-Agent"] = $"{config.UserAgent} ({RuntimeInformation.FrameworkDescription})";
        return headers;
    }

    private static Uri BuildUri(ClientConfig config, string apiVersion, string rawPath, bool isRaw)
    {
        (string encodedPath, string? encodedQuery) = UrlBuilder.SplitAndEncode(rawPath);
        string baseAddress = config.Address.TrimEnd('/');
        StringBuilder builder = new(baseAddress);
        if (!isRaw)
        {
            builder.Append('/').Append(apiVersion);
        }

        builder.Append('/').Append(encodedPath);
        if (!string.IsNullOrEmpty(encodedQuery))
        {
            builder.Append('?').Append(encodedQuery);
        }

        return new Uri(builder.ToString(), UriKind.Absolute);
    }

    private static string BuildDisplayPath(string rawPath) => rawPath.StartsWith('/') ? rawPath : "/" + rawPath;
}
