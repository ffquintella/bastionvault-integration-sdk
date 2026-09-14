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

    /// <summary>
    /// The rest of CFG-020's unauthenticated-path list, beside the <c>auth/*/login</c> arm
    /// <see cref="LoginPathPattern"/> already matched for token omission (D-M1c-24).
    /// </summary>
    /// <remarks>
    /// One list, consulted twice, which is what D-M2-9's review answer asked for: the login arm
    /// skips resolution entirely (CFG-020's first MUST — a login carries no token header), and the
    /// whole list exempts a path from CFG-020's second MUST, the client-side refusal. A second copy
    /// of the list would be a second thing to keep in step with the specification.
    /// <para>
    /// Only the refusal is exempted, not the header: <c>sys/health</c> and friends still send a
    /// token when the client has one, which is the pre-M2b behaviour and which the server accepts.
    /// The requirement is that they <i>work</i> without one.
    /// </para>
    /// </remarks>
    private static readonly string[] UnauthenticatedPaths =
    [
        "sys/health", "sys/seal-status", "sys/init", "sys/unseal", "sys/info",
        "auth/ferrogate/requirement", "auth/ferrogate/enroll",
    ];

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

    /// <summary>
    /// The identity and the accumulated attempt count of one <b>caller-visible</b> operation,
    /// passed <i>into</i> the retry loop rather than minted inside it (D-M2-9).
    /// </summary>
    /// <remarks>
    /// <para>
    /// D-M2-9 amends D-M1b-8: "stable across every attempt of this logical operation" now reads
    /// "…including across an AUT-003 re-login replay". The replay is a second pass of one caller
    /// call, so it must not mint a second <see cref="RequestId"/> and must not restart the
    /// reported attempt count — an application that opted into
    /// <c>ReloginOnPermissionDenied</c> would otherwise get worse observability than it had
    /// before opting in.
    /// </para>
    /// <para>
    /// The two counters are genuinely different numbers and D-M2-9 ruling 2 is explicit about
    /// why. Per-pass <c>attempt</c> drives retry <i>eligibility</i> and the backoff exponent and
    /// restarts at 1 on the replay; accumulated <see cref="AttemptsBefore"/> <c>+ attempt</c> is
    /// what the thrown error's <c>Attempts</c> and the observer report. Feeding the accumulated
    /// value into the eligibility check instead would mean that at <c>MaxAttempts = 3</c> a first
    /// pass which burned all three attempts leaves the replay with none, so CFG-051…055 would
    /// silently not apply to the replayed request at all.
    /// </para>
    /// <para>
    /// The AUT-003 replay itself is M2b's. This is the accounting it will use, landed and tested
    /// here because it is a cross-language observability contract, not because M2a re-logs in.
    /// </para>
    /// </remarks>
    internal readonly record struct RequestExecution(string RequestId, int AttemptsBefore)
    {
        /// <summary>A fresh identity for a caller's first pass.</summary>
        public static RequestExecution New() => new(Guid.NewGuid().ToString("n"), 0);
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
        CancellationToken cancellationToken,
        RequestExecution? execution = null,
        bool isLogin = false)
    {
        options ??= new RequestOptions();
        GuardInputPreflight(options, jsonBody);

        ClientConfig config = context.Config;
        string apiVersion = options.ApiVersion ?? config.ApiPrefix;
        // A login's path is already encoded by the login runner (AUT-030 / TRN-020), because a
        // username may contain `/` or `?` and those are indistinguishable from structure once
        // interpolated. Encoding it twice would send `%252F` instead of `%2F`.
        Uri uri = BuildUri(config, apiVersion, rawPath, isRaw: false, pathIsEncoded: isLogin);
        string displayPath = BuildDisplayPath(EffectiveNamespace(options), rawPath);
        bool isIdempotent = options.Idempotent ?? defaultIdempotent;

        Verdict<Outcome> Classify(TransportResponse response, int attemptsTotal)
        {
            Outcome? outcome = TryHandleResponse(response, method, displayPath, config.Address, attemptsTotal, treatNotFoundEmptyAsAbsent, out BastionVaultException? failure);
            return outcome is { } value
                ? new Verdict<Outcome> { IsSuccess = true, Value = value }
                : new Verdict<Outcome> { IsSuccess = false, Failure = failure };
        }

        return await RunWithReloginAsync(
            // ERR-022 applies to a *typed-operation* caller, which is every operation built on this
            // entry point. `ExecuteRawAsync` opts out below.
            pending => RunLoopAsync(method, uri, jsonBody, options, isIdempotent, rawPath, displayPath, Classify, pending, refuseWithoutToken: true, isLogin, cancellationToken),
            execution ?? RequestExecution.New(),
            method).ConfigureAwait(false);
    }

    /// <summary>Sends <see cref="RawResponse"/> without envelope parsing (D-M1b-12): errors still map through <see cref="StatusCodeMapper"/>.</summary>
    public async Task<RawResponse> ExecuteRawAsync(
        string method,
        string absolutePath,
        ReadOnlyMemory<byte>? body,
        RequestOptions? options,
        CancellationToken cancellationToken,
        RequestExecution? execution = null)
    {
        options ??= new RequestOptions();
        ClientConfig config = context.Config;
        Uri uri = BuildUri(config, options.ApiVersion ?? config.ApiPrefix, absolutePath, isRaw: true);
        bool isIdempotent = method is "GET" or "HEAD" or "OPTIONS" or "LIST";
        string displayPath = BuildDisplayPath(EffectiveNamespace(options), absolutePath);

        Verdict<RawResponse> Classify(TransportResponse response, int attemptsTotal)
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
                    response.StatusCode, null, Array.Empty<string>(), null, method, displayPath, config.Address, attemptsTotal, IsWhitespaceOrEmpty(response.Body)));
            }
            else
            {
                (string? serverMessage, IReadOnlyList<string> serverErrors, bool bodyEmpty) parsed = ParseErrorBody(response.Body);
                TimeSpan? retryAfter = ParseRetryAfter(response.Headers);
                failure = StatusCodeMapper.Map(new StatusCodeMapper.Context(
                    response.StatusCode, parsed.serverMessage, parsed.serverErrors, retryAfter, method, displayPath, config.Address, attemptsTotal, parsed.bodyEmpty));
            }

            return new Verdict<RawResponse> { IsSuccess = false, Failure = failure };
        }

        return await RunWithReloginAsync(
            // CFG-020's refusal does **not** apply here. ERR-022 scopes it to typed-operation
            // callers, and `Logical.Raw` is the documented escape hatch (D-M1b-12): its path is
            // absolute and already carries the API prefix, so CFG-020's list — written in logical
            // paths — cannot be matched against it without inventing a prefix-stripping rule the
            // specification does not state. A raw caller sees the server's own answer, which is
            // exactly what an escape hatch is for.
            pending => RunLoopAsync(method, uri, body, options, isIdempotent, absolutePath, displayPath, Classify, pending, refuseWithoutToken: false, isLogin: false, cancellationToken),
            execution ?? RequestExecution.New(),
            method).ConfigureAwait(false);
    }

    /// <summary>
    /// AUT-003's re-login-and-replay: a <b>one-shot outer step</b> wrapping one complete execution
    /// pass, never an arm inside the CFG-051…055 retry loop (D-M2-9).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Outside the loop because <c>BV-AUTHZ-001</c> is not retryable, so the loop has already
    /// exited by the time this fires; re-entering it on a non-retryable code would be a second
    /// meaning for the same construct. And the replay is <b>one caller-visible operation</b>, so
    /// the <c>requestId</c> is threaded through unchanged and the second pass starts its reported
    /// count where the first left off (D-M2-9 ruling on D-M1b-8). The interposed login is a
    /// different request to a different path and gets its own id, which is correct.
    /// </para>
    /// <para>
    /// The filter deliberately excludes an error the login itself produced
    /// (<c>RecognizedAtSource</c>, D-M2-25 item 2). AUT-041's gated AppID login answers <c>403</c>
    /// too, and keying on that would re-login in response to the login's own refusal.
    /// </para>
    /// </remarks>
    private async Task<TResult> RunWithReloginAsync<TResult>(
        Func<RequestExecution, Task<TResult>> pass,
        RequestExecution execution,
        string method)
    {
        try
        {
            return await pass(execution).ConfigureAwait(false);
        }
        catch (BastionVaultException denied) when (IsReloginCandidate(denied, method))
        {
            // The filter above decides from the *error and the method*; the source is decided here.
            // Two reasons. An exception filter runs before the stack unwinds and must not have side
            // effects, and `TryBeginRelogin` invalidates the source's cached token. And the source
            // is mutable state a concurrent `SetToken` can replace, so reading it once, where it is
            // acted on, avoids a filter and a body that disagree — which is also what keeps both
            // arms below reachable: a Static-sourced client's 403 on a GET arrives here and
            // rethrows, and so does an opted-out Login source's (D-M1c-25 — no unreachable arm).
            if (context.TokenSource.Descriptor is not { Options.ReloginOnPermissionDenied: true } descriptor
                || !context.TryBeginRelogin(descriptor.Options.MinReloginInterval))
            {
                throw;
            }

            return await pass(new RequestExecution(execution.RequestId, denied.Attempts)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The conditions AUT-003 states about the <i>failure</i>: the code is <c>BV-AUTHZ-001</c> and
    /// it came from the outer request rather than from the login itself, and the request was
    /// idempotent — which for this purpose means <c>GET</c>, <c>LIST</c> and <c>HEAD</c> only. The
    /// remaining two conditions are about the token source and are checked where the re-login is
    /// started, in <see cref="RunWithReloginAsync"/>.
    /// </summary>
    /// <remarks>
    /// Not HTTP idempotency, which would include <c>PUT</c> and <c>DELETE</c>: AUT-003's own
    /// justification is that a 403 also means "policy does not allow", so replaying a write after a
    /// permission change is the exact case the opt-in warns about, and a replayed <c>DELETE</c>
    /// against a vault is not a cost this SDK gets to choose for the application (D-M2-9).
    /// <see cref="RequestOptions.Idempotent"/> is not consulted for the same reason: it is a
    /// <i>retry</i> switch (D-M1b-6), and letting it authorise a replayed write would smuggle that
    /// choice in through a setting whose documentation says nothing about logins.
    /// </remarks>
    private static bool IsReloginCandidate(BastionVaultException error, string method)
        => string.Equals(error.Code, ErrorCodes.AuthzPermissionDenied, StringComparison.Ordinal)
            && error is not IRecognizedAtSource { RecognizedAtSource: true }
            && method is "GET" or "LIST" or "HEAD";

    private static void GuardInputPreflight(RequestOptions options, ReadOnlyMemory<byte>? jsonBody)
    {
        if (options.WrapTtl is not null)
        {
            ErrorCatalogEntry wrapEntry = ErrorCatalog.Require(ErrorCodes.InputUnsupportedOption);
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
            ErrorCatalogEntry bodyEntry = ErrorCatalog.Require(ErrorCodes.InputBodyTooLarge);
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
        string rawPath,
        string displayPath,
        Func<TransportResponse, int, Verdict<TResult>> classify,
        RequestExecution execution,
        bool refuseWithoutToken,
        bool isLogin,
        CancellationToken cancellationToken)
    {
        ClientConfig config = context.Config;
        RetryPolicy retryPolicy = config.RetryPolicy;
        int attempt = 0;
        int maxAttempts = Math.Max(1, retryPolicy.MaxAttempts);
        // D-M2-9's seam: the token comes from TokenSource.ResolveAsync(), not from a field read.
        // The *placement* is unchanged and deliberately so — D-M1b-9 put the snapshot here, above
        // the retry loop, and that is CFG-070's "in-flight requests keep the token they started
        // with". Only the source of the value changed.
        // The raw path, never the display path: CFG-020's login-path pattern is anchored and
        // would not match through the `[ns=…] ` prefix, which would send a token on a login call.
        string tokenValue;
        try
        {
            tokenValue = await ResolveTokenAsync(options, rawPath, isLogin, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // D-M2-9 made resolution I/O-performing and put it *above* the loop, so a cancelled
            // Callback or Login resolution no longer passes through the in-loop cancellation
            // mapping. Left unhandled it would surface as a bare OperationCanceledException,
            // which ERR-020/TRN-054 forbid: a caller never catches a runtime exception type.
            // Reported with the attempts made so far — none, on this pass — because the request
            // was never sent.
            throw TransportFailureMapper.MapCancelled(method, displayPath, config.Address, execution.AttemptsBefore);
        }
        catch (Exception exception) when (exception is not IRecognizedAtSource { RecognizedAtSource: true })
        {
            // BV-AUTH-017 TokenSourceFailed (D-M2-16). The source is configured and its resolution
            // failed, which BV-AUTH-001 NoToken does not describe — that code's message is "No
            // token is *configured*". Retryable is false because ERR-006's retryable set is an
            // exhaustive enumeration and this code is not in it; attempts matches the cancellation
            // arm above, because no request was sent either way; and the source's own exception is
            // preserved as the cause rather than being flattened into a message.
            //
            // D-M2-25 item 2 narrowed this filter from `is not BastionVaultException`. A coded
            // error the *login-response contract* produced (BV-AUTH-003…BV-AUTH-014,
            // BV-AUTH-010/011, BV-RATE-001, and AUT-041's gated BV-AUTHZ-001) is marked at its
            // origin and passes through with its own code, because replacing a specific code with
            // a generic one would throw AUT-010…013 away. Any *other* BastionVaultException a
            // source delegate leaks — one raised by code inside the delegate rather than by the
            // login — is now wrapped, so the source's internal Path and Method can no longer
            // masquerade as the outer request's.
            //
            // The filter order above is load-bearing (D-M2-18 item 1): `catch
            // (OperationCanceledException)` must stay first, or a cancelled resolution maps to
            // BV-AUTH-017 instead of BV-TRANSPORT-005.
            ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.AuthTokenSourceFailed);
            throw BastionVaultException.Request(
                ErrorCodes.AuthTokenSourceFailed,
                entry.Category,
                entry.Message,
                entry.Hint,
                retryable: entry.Retryable,
                attempts: execution.AttemptsBefore,
                method: method,
                path: displayPath,
                address: config.Address,
                cause: exception);
        }

        // CFG-020's second MUST and ERR-022: an authenticated operation with no token fails
        // client-side, before any network call. Raised **outside** the guard above on purpose —
        // inside it, this BV-AUTH-001 would be caught by the BV-AUTH-017 filter and reported as a
        // token-source failure, which is the opposite of what CFG-020 says happened.
        //
        // ERR-022's ruling: the preflight runs *after* resolution, never before. "No token" is a
        // source that resolved to absent or empty — not a Login source that has not resolved yet.
        // The whole point of ERR-022 is that the server's missing-token behaviour is inconsistent
        // (400 on logical paths, 403 on inline sys handlers, 401 inside batch results), so no
        // typed-operation caller should ever see any of the three.
        if (refuseWithoutToken && tokenValue.Length == 0 && !isLogin && !IsUnauthenticated(rawPath))
        {
            throw NoToken(method, displayPath, config.Address, execution.AttemptsBefore);
        }

        // D-M1b-8, as amended by D-M2-9: minted by the caller, above any replay, so one
        // caller-visible operation keeps one id across every pass.
        string requestId = execution.RequestId;
        // CFG-080 / TST-051: the observer sees the *redacted* display path. AUT-080's
        // `auth/token/renew/{token}` and Auth.Token.Lookup's `auth/token/lookup/{token}` put a
        // live token in the path, and RequestEvent.Path is the second consumer of that string
        // after the error (ERR-003 already covers the first). Redacted once here so no call site
        // can pass the unredacted form.
        // Redact never returns null for a non-null path, so there is no fallback arm to write —
        // and writing one would be an unreachable branch the CNF-010 floor allows no pragma to
        // excuse.
        string observedPath = ErrorPaths.Redact(displayPath)!;
        // RES-004: TotalTimeout bounds attempts *and* backoff together; Timeout bounds each attempt.
        DateTimeOffset? deadline = options.TotalTimeout is { } totalTimeout ? context.Clock.NowUtc() + totalTimeout : null;

        while (true)
        {
            attempt++;
            int attemptsTotal = execution.AttemptsBefore + attempt;
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
                BastionVaultException cancelled = TransportFailureMapper.MapCancelled(method, displayPath, config.Address, attemptsTotal);
                Notify(method, observedPath, requestId, attemptsTotal, null, cancelled.Code, stopwatch.Elapsed);
                throw cancelled;
            }

            Notify(method, observedPath, requestId, attemptsTotal, response?.StatusCode, failure?.Code, stopwatch.Elapsed);

            if (response is not null)
            {
                // The transport is the enforcement point for TRN-033 (D-M1b-20); this is a cheap
                // backstop for transports (e.g. FakeTransport) that hand back an in-memory body
                // without themselves bounding the read.
                if (response.Body.Length > config.MaxResponseBytes)
                {
                    throw TransportFailureMapper.MapResponseTooLarge(method, displayPath, config.Address, attemptsTotal);
                }

                // D-M1b-22: the rate gate pauses on status 429 itself, not on the mapped code —
                // EFF-003/EFF-004 are written in terms of the status, and BV-RATE-002 is a 429 too.
                if (response.StatusCode == 429)
                {
                    PauseRateGate(response.Headers);
                }

                Verdict<TResult> verdict = classify(response, attemptsTotal);
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
                && (deadline is null || context.Clock.NowUtc() < deadline);

            if (!eligible)
            {
                throw Present(error, attemptsTotal, method, displayPath, EffectiveNamespace(options), config);
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
                TimeSpan remaining = d - context.Clock.NowUtc();
                backoff = backoff < remaining ? backoff : (remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
            }

            await context.Clock.Delay(backoff, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The single place a request-scoped error becomes the exception the caller sees: it fixes the
    /// attempt count and the request-scoped fields, records the path in <c>Details</c>, interpolates
    /// it into any hint that points at <c>Details.path</c> (ERR-034), and appends the ERR-040
    /// context notes (D-M1c-5). Enrichment lives here rather than in <see cref="StatusCodeMapper"/>
    /// because two of the seven rows fire on transport failures, which never reach that function.
    /// </summary>
    private static BastionVaultException Present(
        BastionVaultException error,
        int attemptsTotal,
        string method,
        string displayPath,
        string activeNamespace,
        ClientConfig config)
    {
        // The redacted *display* path — the `[ns=…] ` form, not the raw one — is what
        // Details["path"], the ERR-034 interpolation and the ERR-040 context all see, matching
        // rust/.../logical.rs finish_error and python/.../logical.py. Redaction runs after
        // prefixing and still finds the token segment, because `[ns=a] auth/token/lookup/<tok>`
        // splits on '/' with `lookup` intact.
        // One null arm, matching RunLoopAsync's observedPath: Redact never returns null for a
        // non-null path, so the `?? displayPath` fallback here was unreachable (review, non-blocking).
        string redactedPath = ErrorPaths.Redact(displayPath)!;

        Dictionary<string, object?> details = new(error.Details, StringComparer.Ordinal)
        {
            ["path"] = redactedPath,
        };

        string hint = HintEnrichment.InterpolatePath(error.Hint, redactedPath);
        hint = HintEnrichment.Enrich(
            error.Code,
            hint,
            new HintEnrichment.Context(
                error.StatusCode,
                error.RetryAfter,
                redactedPath,
                activeNamespace,
                HasCaCertificate: config.CaCertPath is not null || config.CaCertPem is not null,
                config.Address));

        return BastionVaultException.Request(
            error.Code,
            error.Category,
            error.Message,
            hint,
            error.Retryable,
            attempts: attemptsTotal,
            serverMessage: error.ServerMessage,
            serverErrors: error.ServerErrors,
            statusCode: error.StatusCode,
            retryAfter: error.RetryAfter,
            method: method,
            path: displayPath,
            address: config.Address,
            details: details,
            cause: error.Cause);
    }

    private void PauseRateGate(IReadOnlyDictionary<string, string> headers)
    {
        TimeSpan? retryAfter = ParseRetryAfter(headers);
        TimeSpan pause = retryAfter is { } wait
            ? (wait < TimeSpan.FromSeconds(30) ? wait : TimeSpan.FromSeconds(30))
            : TimeSpan.FromSeconds(1);
        context.RateGate.Pause(context.Clock.NowUtc() + pause);
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
                response.StatusCode, null, Array.Empty<string>(), null, method, logicalPath, address, attempt, bodyEmptyRaw));
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
                response.StatusCode, null, Array.Empty<string>(), ParseRetryAfter(response.Headers), method, logicalPath, address, attempt, BodyEmpty: true));
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
            response.StatusCode, extracted.serverMessage, extracted.serverErrors, retryAfter, method, logicalPath, address, attempt, BodyEmpty: false));
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

    /// <summary>
    /// D-M2-9: token resolution for one pass. Asynchronous because two of AUT-001's three source
    /// variants perform I/O; called exactly once per pass, above the retry loop (D-M1b-9).
    /// </summary>
    /// <param name="options">The per-request options; <see cref="RequestOptions.Token"/> wins.</param>
    /// <param name="rawPath">The unencoded logical path.</param>
    /// <param name="isLogin">
    /// <see langword="true"/> when the caller <i>is</i> the login runner, which knows it is
    /// performing a login and does not need the path sniffed.
    /// </param>
    /// <param name="cancellationToken">Runtime cancellation.</param>
    /// <remarks>
    /// Both a flag and a pattern, and each covers what the other cannot. The flag is authoritative
    /// for the SDK's own login operations, because <see cref="LoginPathPattern"/> is matched against
    /// the <b>unencoded</b> path and AUT-030's username may legitimately contain <c>/</c> or
    /// <c>?</c> (TRN-020 is why it is encoded at all) — <c>auth/userpass/login/a/b</c> is one login
    /// with an awkward username, and an anchored single-segment pattern cannot tell it from a
    /// two-segment path. The pattern still covers an application that reaches a login through the
    /// generic logical layer (<c>Logical.Write("auth/userpass/login/bob")</c>), where a single
    /// segment is the only form the SDK can safely assume.
    /// </remarks>
    private async Task<string> ResolveTokenAsync(RequestOptions options, string rawPath, bool isLogin, CancellationToken cancellationToken)
    {
        if (options.Token is not null)
        {
            return options.Token.Reveal() ?? string.Empty;
        }

        if (isLogin || LoginPathPattern.IsMatch(LogicalPath(rawPath)))
        {
            // CFG-020's first MUST (D-M1c-24): a login carries no token header. Resolution is
            // skipped entirely rather than resolved-and-discarded, so a Login source does not
            // recurse into a login in order to send one.
            return string.Empty;
        }

        SecretString? resolved = await context.ResolveTokenAsync(cancellationToken).ConfigureAwait(false);
        return resolved?.Reveal() ?? string.Empty;
    }

    /// <summary>The path without its leading <c>/</c> (TRN-002) and without any <c>?query</c>.</summary>
    private static string LogicalPath(string rawPath)
    {
        int queryIndex = rawPath.IndexOf('?', StringComparison.Ordinal);
        string pathOnly = queryIndex >= 0 ? rawPath[..queryIndex] : rawPath;
        return pathOnly.TrimStart('/');
    }

    /// <summary>
    /// Whether CFG-020 exempts <paramref name="rawPath"/> from the client-side refusal: the
    /// <c>auth/*/login</c> arm plus the seven named endpoints. One list, the same one
    /// <see cref="ResolveTokenAsync"/> consults for token omission.
    /// </summary>
    private static bool IsUnauthenticated(string rawPath)
    {
        string logical = LogicalPath(rawPath);
        return LoginPathPattern.IsMatch(logical)
            || UnauthenticatedPaths.Contains(logical, StringComparer.Ordinal);
    }

    /// <summary>
    /// CFG-020 / ERR-022's client-side refusal. <c>Attempts</c> is the count already accumulated
    /// (zero on a first pass) because no request was sent, and <c>StatusCode</c> is absent for the
    /// same reason — which is exactly how a caller tells this apart from the server's own
    /// missing-token answers.
    /// </summary>
    private static BastionVaultException NoToken(string method, string displayPath, string address, int attempts)
    {
        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.AuthNoToken);
        string redactedPath = ErrorPaths.Redact(displayPath)!;
        return BastionVaultException.Request(
            ErrorCodes.AuthNoToken,
            entry.Category,
            entry.Message,
            HintEnrichment.InterpolatePath(entry.Hint, redactedPath),
            retryable: entry.Retryable,
            attempts: attempts,
            method: method,
            path: displayPath,
            address: address,
            details: new Dictionary<string, object?>(StringComparer.Ordinal) { ["path"] = redactedPath });
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

    private static Uri BuildUri(ClientConfig config, string apiVersion, string rawPath, bool isRaw, bool pathIsEncoded = false)
    {
        (string encodedPath, string? encodedQuery) = UrlBuilder.SplitAndEncode(rawPath, pathIsEncoded);
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

    /// <summary>
    /// ERR-001's <c>Path</c>: the logical path with the namespace prefix for display
    /// (<c>[ns=dti/esi] secret/data/x</c>), as <c>04-error-model.md</c>'s field table defines it.
    /// </summary>
    /// <remarks>
    /// Byte-identical to <c>rust/.../logical.rs</c>'s <c>display_path</c> and
    /// <c>python/.../logical.py</c>'s <c>_display_path</c>, down to the single space after
    /// <c>]</c> and the absence of any prefix when the namespace is empty. Built once per
    /// operation and threaded through the mapper, the observer event and the error, so no call
    /// site can produce a second form. .NET carried the raw path with no prefix (and a leading
    /// <c>/</c> the other two do not add) through M1a and M1b; no fixture caught it, which is
    /// why this lives in one function with a parity test on it rather than at each call site.
    /// </remarks>
    private static string BuildDisplayPath(string activeNamespace, string rawPath)
        => activeNamespace.Length == 0 ? rawPath : $"[ns={activeNamespace}] {rawPath}";

    /// <summary>The namespace this request actually carries: the per-request override, else the client/view's.</summary>
    private string EffectiveNamespace(RequestOptions options) => (options.Namespace ?? activeNamespace).TrimEnd('/');
}
