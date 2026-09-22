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
    /// <param name="RequestId">D-M1b-8's stable identity for one caller-visible operation.</param>
    /// <param name="AttemptsBefore">The attempts already spent by earlier passes of this operation.</param>
    /// <param name="IsBoundedReplay">
    /// Whether this pass is a <b>replay</b> of an earlier pass of the same caller call, and must
    /// therefore run inside what is left of RES-001's total budget rather than on a fresh
    /// <c>MaxAttempts</c>. Both replay mechanisms set it: DSC-042's failover replay (D-M5-28) and
    /// AUT-003's relogin replay (D-M6-21). One flag rather than two, because RES-001's cap is one
    /// cap and a reader who found two would have to work out whether they differ.
    /// </param>
    internal readonly record struct RequestExecution(
        string RequestId,
        int AttemptsBefore,
        bool IsBoundedReplay = false)
    {
        /// <summary>A fresh identity for a caller's first pass.</summary>
        public static RequestExecution New()
        {
            return new(Guid.NewGuid().ToString("n"), 0);
        }
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
        bool isLogin = false,
        bool pathIsEncoded = false,
        bool nodeLocal = false,
        bool nonRetryable = false,
        string? endpointOverride = null)
    {
        options ??= new RequestOptions();
        GuardInputPreflight(options, jsonBody);

        ClientConfig config = context.Config;
        string apiVersion = options.ApiVersion ?? config.ApiPrefix;
        // A login's path is already encoded by the login runner (AUT-030 / TRN-020), because a
        // username may contain `/` or `?` and those are indistinguishable from structure once
        // interpolated. Encoding it twice would send `%252F` instead of `%2F`. KV sets the same
        // flag for the same reason and without being a login (KV2-030): its `path` is
        // caller-supplied and multi-segment, so `isLogin` cannot be reused as the carrier — it
        // also suppresses the token header (CFG-020) and the ERR-022 refusal.
        string displayPath = BuildDisplayPath(EffectiveNamespace(options), rawPath);
        bool isIdempotent = options.Idempotent ?? defaultIdempotent;

        Verdict<Outcome> Classify(TransportResponse response, int attemptsTotal)
        {
            Outcome? outcome = TryHandleResponse(response, method, displayPath, config.Address, attemptsTotal, treatNotFoundEmptyAsAbsent, out BastionVaultException? failure);
            return outcome is { } value
                ? new Verdict<Outcome> { IsSuccess = true, Value = value }
                : new Verdict<Outcome> { IsSuccess = false, Failure = failure };
        }

        CallBudget budget = new();
        return await RunWithFailoverAsync(
            outer => RunWithReloginAsync(
                // ERR-022 applies to a *typed-operation* caller, which is every operation built on
                // this entry point. `ExecuteRawAsync` opts out below.
                pending => RunLoopAsync(method, new Target(apiVersion, rawPath, IsRaw: false, PathIsEncoded: isLogin || pathIsEncoded), jsonBody, options, isIdempotent, rawPath, displayPath, Classify, pending, refuseWithoutToken: true, isLogin, budget, nodeLocal, cancellationToken, nonRetryable: nonRetryable, endpointOverride: endpointOverride),
                outer,
                method,
                budget),
            execution ?? RequestExecution.New(),
            isIdempotent,
            nodeLocal,
            budget,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// SYS-090's entry point: a <b>logical</b> path (so <c>ApiPrefix</c> applies, unlike
    /// <see cref="ExecuteRawAsync"/>) whose request or response body is
    /// <c>application/octet-stream</c> rather than JSON. Errors still map through
    /// <see cref="StatusCodeMapper"/>, so SYS-091's <c>500</c> reaches the caller as the
    /// <c>BV-INPUT-103</c> Appendix B §2 already recognises.
    /// </summary>
    /// <remarks>
    /// A third entry point rather than a flag on <see cref="ExecuteAsync"/>: every caller of
    /// <see cref="ExecuteAsync"/> gets an envelope-parsed <see cref="Outcome"/>, and a backup file
    /// is not JSON, so a flag would let a future caller ask for a parse that cannot succeed. The
    /// <b>loop</b> is still the single D-M1b-24 one — only <c>classify</c> differs — so the
    /// <paramref name="nonRetryable"/> and <paramref name="nodeLocal"/> exclusions SYS-090 requires
    /// are the same ones SYS-013 already proved, not a second implementation of them.
    /// <para>
    /// TRN-033's response bound is enforced by the transport <i>while reading</i> (D-M1b-20), which
    /// is what SYS-090's "no full buffering above <c>MaxResponseBytes</c>" asks for: a backup larger
    /// than the bound is aborted mid-read and never lands in memory. See DR-0012 D-M7-34.
    /// </para>
    /// </remarks>
    public async Task<RawResponse> ExecuteBinaryAsync(
        string method,
        string rawPath,
        ReadOnlyMemory<byte>? body,
        BinaryShape binary,
        RequestOptions? options,
        bool nodeLocal,
        bool nonRetryable,
        CancellationToken cancellationToken)
    {
        options ??= new RequestOptions();
        GuardInputPreflight(options, body);

        ClientConfig config = context.Config;
        string apiVersion = options.ApiVersion ?? config.ApiPrefix;
        string displayPath = BuildDisplayPath(EffectiveNamespace(options), rawPath);
        // SYS-090's two operations are both POST and both change state; neither is a failover or a
        // retry candidate under any policy, which the two flags say explicitly rather than relying
        // on the verb.
        const bool IsIdempotent = false;

        Verdict<RawResponse> Classify(TransportResponse response, int attemptsTotal)
        {
            if (response.StatusCode is >= 200 and < 300)
            {
                return new Verdict<RawResponse>
                {
                    IsSuccess = true,
                    Value = new RawResponse { StatusCode = response.StatusCode, Headers = response.Headers, Body = response.Body },
                };
            }

            (string? serverMessage, IReadOnlyList<string> serverErrors, bool bodyEmpty) parsed = ParseErrorBody(response.Body);
            return new Verdict<RawResponse>
            {
                IsSuccess = false,
                Failure = StatusCodeMapper.Map(new StatusCodeMapper.Context(
                    response.StatusCode, parsed.serverMessage, parsed.serverErrors, ParseRetryAfter(response.Headers),
                    method, displayPath, config.Address, attemptsTotal, parsed.bodyEmpty)),
            };
        }

        CallBudget budget = new();
        return await RunWithFailoverAsync(
            outer => RunWithReloginAsync(
                pending => RunLoopAsync(
                    method,
                    new Target(apiVersion, rawPath, IsRaw: false, PathIsEncoded: false),
                    body,
                    options,
                    IsIdempotent,
                    rawPath,
                    displayPath,
                    Classify,
                    pending,
                    refuseWithoutToken: true,
                    isLogin: false,
                    budget,
                    nodeLocal,
                    cancellationToken,
                    nonRetryable: nonRetryable,
                    binary: binary),
                outer,
                method,
                budget),
            RequestExecution.New(),
            IsIdempotent,
            nodeLocal,
            budget,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends <see cref="RawResponse"/> without envelope parsing (D-M1b-12): errors still map through <see cref="StatusCodeMapper"/>.</summary>
    public async Task<RawResponse> ExecuteRawAsync(
        string method,
        string absolutePath,
        ReadOnlyMemory<byte>? body,
        RequestOptions? options,
        CancellationToken cancellationToken,
        RequestExecution? execution = null,
        bool nodeLocal = false)
    {
        options ??= new RequestOptions();
        ClientConfig config = context.Config;
        Target target = new(options.ApiVersion ?? config.ApiPrefix, absolutePath, IsRaw: true, PathIsEncoded: false);
        bool isIdempotent = method is "GET" or "HEAD" or "OPTIONS" or "LIST";
        string displayPath = BuildDisplayPath(EffectiveNamespace(options), absolutePath);

        Verdict<RawResponse> Classify(TransportResponse response, int attemptsTotal)
        {
            if (response.StatusCode is 204 or 304 or >= 200 and < 300)
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

        CallBudget budget = new();
        return await RunWithFailoverAsync(
            outer => RunWithReloginAsync(
            // CFG-020's refusal does **not** apply here. ERR-022 scopes it to typed-operation
            // callers, and `Logical.Raw` is the documented escape hatch (D-M1b-12): its path is
            // absolute and already carries the API prefix, so CFG-020's list — written in logical
            // paths — cannot be matched against it without inventing a prefix-stripping rule the
            // specification does not state. A raw caller sees the server's own answer, which is
            // exactly what an escape hatch is for.
                pending => RunLoopAsync(method, target, body, options, isIdempotent, absolutePath, displayPath, Classify, pending, refuseWithoutToken: false, isLogin: false, budget, nodeLocal, cancellationToken),
                outer,
                method,
                budget),
            execution ?? RequestExecution.New(),
            isIdempotent,
            nodeLocal,
            budget,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// SYS-001/SYS-002: <c>Sys.Health</c>'s status→state mapping treats <c>200</c>, <c>429</c>,
    /// <c>501</c> and <c>503</c> as <b>successful</b> outcomes carrying a parsed body — the one
    /// operation in this SDK whose contract is "never raise for this closed status set", and the
    /// only reason a caller ever sees a body alongside a non-2xx status. A dedicated entry point
    /// rather than a flag on <see cref="ExecuteAsync"/>: every other caller's contract is "any
    /// non-2xx is an error", and a flag would let a future caller opt out of that by accident. Any
    /// status outside the closed set (which the specification says the server never sends) falls
    /// through to the ordinary <see cref="TryHandleResponse"/> mapping, so an unanticipated status
    /// still raises rather than being silently swallowed.
    /// </summary>
    public async Task<Outcome> ExecuteHealthAsync(
        string method,
        string rawPath,
        RequestOptions? options,
        CancellationToken cancellationToken)
    {
        options ??= new RequestOptions();
        GuardInputPreflight(options, null);

        ClientConfig config = context.Config;
        string apiVersion = options.ApiVersion ?? config.ApiPrefix;
        string displayPath = BuildDisplayPath(EffectiveNamespace(options), rawPath);

        Verdict<Outcome> Classify(TransportResponse response, int attemptsTotal)
        {
            if (response.StatusCode is 200 or 429 or 501 or 503)
            {
                if (!TryParseJson(response.Body, out JsonElement parsed, out string snippet))
                {
                    return new Verdict<Outcome>
                    {
                        IsSuccess = false,
                        Failure = StatusCodeMapper.MapNonJson(response.StatusCode, snippet, method, displayPath, config.Address, attemptsTotal),
                    };
                }

                return new Verdict<Outcome>
                {
                    IsSuccess = true,
                    Value = new Outcome { IsEmpty = false, IsNotFoundEmpty = false, Body = parsed, StatusCode = response.StatusCode, Headers = response.Headers },
                };
            }

            Outcome? outcome = TryHandleResponse(response, method, displayPath, config.Address, attemptsTotal, treatNotFoundEmptyAsAbsent: false, out BastionVaultException? failure);
            return outcome is { } value
                ? new Verdict<Outcome> { IsSuccess = true, Value = value }
                : new Verdict<Outcome> { IsSuccess = false, Failure = failure };
        }

        // D-M5-27: `Sys.Health` takes part in failover like any other idempotent read, and that is
        // correct rather than an oversight — so do not "fix" it. SYS-001 makes 200/429/501/503
        // *successful* outcomes carrying a parsed body, so a sealed node returns
        // `HealthStatus { Sealed = true }` and never an error; DSC-041 limb (ii) can therefore
        // never fire for this operation, and the only thing that fails it over is a genuine
        // transport failure, which is exactly what failover is for. Failing over cannot hide a
        // sealed node from the caller. `Sys.Health` reports the health of the session's node;
        // `Client.Discover()` (DSC-036) is the per-node view, and it never moves the pin.
        CallBudget budget = new();
        return await RunWithFailoverAsync(
            outer => RunWithReloginAsync(
                pending => RunLoopAsync(method, new Target(apiVersion, rawPath, IsRaw: false, PathIsEncoded: false), null, options, isIdempotent: true, rawPath, displayPath, Classify, pending, refuseWithoutToken: false, isLogin: false, budget, nodeLocal: false, cancellationToken, pauseRateGateOn429: false),
                outer,
                method,
                budget),
            RequestExecution.New(),
            isIdempotent: true,
            nodeLocal: false,
            budget,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The one-shot budgets of <b>one caller-visible operation</b>: DSC-042's single failover
    /// replay and AUT-003's single re-login.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A mutable cell rather than fields on <see cref="RequestExecution"/>, and the reason is the
    /// direction each fact has to travel. <see cref="RequestExecution.IsBoundedReplay"/> describes
    /// <i>this pass</i> and flows downward, so the struct carries it. A spent one-shot has to flow
    /// <i>upward</i>: <see cref="RunWithReloginAsync"/> is nested inside
    /// <see cref="RunWithFailoverAsync"/>, so a re-login spent in the inner wrapper must still be
    /// spent when the outer wrapper builds the failover replay's execution — and a copy of a
    /// readonly struct made in an inner frame cannot tell an outer frame anything (D-M5-29).
    /// </para>
    /// <para>
    /// <see cref="FailoverAvailable"/> is also read in two places that must agree: the retry loop,
    /// which suppresses its own retry while a failover is still pending, and
    /// <see cref="RunWithFailoverAsync"/>, which spends it.
    /// </para>
    /// </remarks>
    private sealed class CallBudget
    {
        /// <summary>Whether the single DSC-042 failover replay is still unspent.</summary>
        public bool FailoverAvailable { get; set; } = true;

        /// <summary>
        /// Whether AUT-003's single re-login is still unspent. D-M2-9 allows one per caller call,
        /// and a failover replay must inherit it spent rather than mint a second one (D-M5-29).
        /// </summary>
        public bool ReloginAvailable { get; set; } = true;

        /// <summary>
        /// The endpoint the failing attempt used (D-M5-12's comparison value). Not nullable: the
        /// retry loop sets it whenever it decides a failover is pending, which is the same
        /// predicate <see cref="RunWithFailoverAsync"/>'s filter uses, so a fallback for "unset"
        /// would be an unreachable branch. The empty default is still safe — it matches no
        /// endpoint, so the failover step would take DSC-043's late-arrival path.
        /// </summary>
        public string FailedEndpoint { get; set; } = string.Empty;
    }

    /// <summary>
    /// DSC-042's <b>exactly one</b> failover replay, as a one-shot outer step around a complete
    /// execution pass — the same shape AUT-003's re-login uses, and for the same reason: the retry
    /// loop has already exited by the time this fires, because a node failure is not in CFG-050's
    /// <c>RetryOn</c> (D-M5-6).
    /// </summary>
    /// <remarks>
    /// <para>
    /// D-M5-7's accounting: the replay is a second pass of <i>one</i> caller call, so it keeps the
    /// same <c>requestId</c>, restarts its per-pass attempt counter at 1, and carries the
    /// accumulated count forward in <see cref="RequestExecution.AttemptsBefore"/>. The replay
    /// therefore does <b>not</b> consume a retry attempt, which is what makes
    /// <c>resilience.failover.read-once</c> satisfiable at <c>MaxAttempts: 1</c>.
    /// </para>
    /// <para>
    /// RES-001's cap is held by the <b>bound</b> on the replay pass's own budget (D-M5-28), not by
    /// the shape of the failure that triggered it: a node failure can arrive on any attempt, after
    /// however many CFG-050 retries the pass has already spent. <see cref="RunLoopAsync"/> reads
    /// <see cref="RequestExecution.IsBoundedReplay"/> to apply it.
    /// </para>
    /// <para>
    /// DSC-044 has two arms and both land here. Nowhere to move — unarmed, or every remaining
    /// candidate unhealthy — rethrows the original untouched. A replay that <i>also</i> fails as a
    /// node failure surfaces the <b>original</b> error with the accumulated attempt count, not the
    /// replay's: for limb (i) that keeps <c>Details.host</c> pointing at the node that actually
    /// died first, and for limb (ii) it keeps the Appendix B code the caller is owed (D-M5-5). A
    /// replay that fails any other way propagates on its own terms, because "the replayed request's
    /// own outcome classifies normally" (D-M5-6).
    /// </para>
    /// </remarks>
    private async Task<TResult> RunWithFailoverAsync<TResult>(
        Func<RequestExecution, Task<TResult>> pass,
        RequestExecution execution,
        bool isIdempotent,
        bool nodeLocal,
        CallBudget budget,
        CancellationToken cancellationToken)
    {
        try
        {
            return await pass(execution).ConfigureAwait(false);
        }
        catch (BastionVaultException failure) when (budget.FailoverAvailable && WillFailover(failure, isIdempotent, nodeLocal))
        {
            // Spent before the replay, so the replayed pass retries under CFG-050 exactly as an
            // ordinary pass would (D-M5-6) and cannot fail over a second time (DSC-042).
            budget.FailoverAvailable = false;
            NodeSelection? moved = await context.Discovery
                .TryFailoverAsync(budget.FailedEndpoint, cancellationToken)
                .ConfigureAwait(false);
            if (moved is null)
            {
                throw;
            }

            try
            {
                // `with`, not a new instance: the replay inherits the request id, the
                // accumulated count, and — per D-M5-29 — whether a re-login has already been
                // spent by this caller call.
                return await pass(execution with
                {
                    AttemptsBefore = failure.Attempts,
                    IsBoundedReplay = true,
                }).ConfigureAwait(false);
            }
            catch (BastionVaultException replayed) when (IsNodeFailure(replayed))
            {
                throw Reattribute(failure, replayed.Attempts);
            }
        }
    }

    /// <summary>
    /// Whether this failure will be answered by the DSC-042 replay: an armed discovery client, an
    /// idempotent operation, an operation DSC-045 does not exclude, and one of DSC-041's two limbs.
    /// </summary>
    /// <remarks>
    /// Writes and deletes are never replayed — an ambiguous commit is worse than a failure — which
    /// is why this reads <c>isIdempotent</c> (the operation's own default, or the caller's
    /// <see cref="RequestOptions.Idempotent"/> override) and not the HTTP method.
    /// </remarks>
    private bool WillFailover(BastionVaultException error, bool isIdempotent, bool nodeLocal)
    {
        return !nodeLocal
            && isIdempotent
            && context.Discovery.IsFailoverArmed
            && IsNodeFailure(error);
    }

    /// <summary>
    /// DSC-041's two limbs, as the <i>trigger</i> test. Limb (i) has already been reclassified to
    /// <c>BV-DISCOVERY-003</c> by <see cref="ReclassifyNodeFailure"/>; limb (ii) is recognised here
    /// and <b>keeps its own code</b>, contributing the trigger and nothing else (D-M5-5).
    /// </summary>
    private static bool IsNodeFailure(BastionVaultException error)
    {
        return string.Equals(error.Code, ErrorCodes.DiscoveryNodeUnavailable, StringComparison.Ordinal)
            || IsStandbyOrSealedServerMessage(error);
    }

    /// <summary>
    /// DSC-041 limb (ii): a <c>5xx</c> whose <b>server</b> message contains <c>sealed</c>,
    /// <c>uninitialized</c> or <c>standby</c>, case-insensitively.
    /// </summary>
    /// <remarks>
    /// The server's message, never the catalogue's own: <c>BV-SERVER-001</c>'s message is "The
    /// server is sealed", so matching against that would make every sealed error a limb (ii)
    /// trigger by tautology, including one the server never described that way.
    /// </remarks>
    private static bool IsStandbyOrSealedServerMessage(BastionVaultException error)
    {
        if (error.StatusCode is not (>= 500 and <= 599) || error.ServerMessage is not { } message)
        {
            return false;
        }

        return message.Contains("sealed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("uninitialized", StringComparison.OrdinalIgnoreCase)
            || message.Contains("standby", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// DSC-041 limb (i): reclassifies a transport-level failure on a discovery-chosen node to
    /// <c>BV-DISCOVERY-003 NodeUnavailable</c>, carrying <c>Details.host</c> and
    /// <c>Details.reason</c>.
    /// </summary>
    /// <remarks>
    /// Two scopes, both from D-M5-5 and both load-bearing. <b>Discovery mode only</b> — in literal
    /// mode the M1b mapping stands, which is what keeps <c>transport.retry.write-not-retried</c>,
    /// <c>errors.enrichment.connection-refused-default-address</c> and
    /// <c>errors.enrichment.tls-no-ca</c> green unamended. And <b>exactly the three kinds DSC-041
    /// names</b>: <c>Dns</c>, <c>TlsVerify</c> and <c>TlsHandshake</c> are not node failures in
    /// either mode, because the requirement's parenthetical does not name them and D-M1c-25 forbids
    /// widening a list the specification closed — a DNS failure is also the one kind that says
    /// nothing about whether the node is alive.
    /// </remarks>
    private BastionVaultException ReclassifyNodeFailure(BastionVaultException failure, Uri uri)
    {
        if (!context.Discovery.IsDiscoveryMode
            || failure.TransportKind is not (TransportFailureKind.ConnectionRefused
                or TransportFailureKind.Reset
                or TransportFailureKind.Timeout))
        {
            return failure;
        }

        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.DiscoveryNodeUnavailable);
        return BastionVaultException.Request(
            ErrorCodes.DiscoveryNodeUnavailable,
            entry.Category,
            entry.Message,
            entry.Hint,
            retryable: entry.Retryable,
            attempts: failure.Attempts,
            details: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["host"] = uri.Host,
                ["reason"] = Reason(failure.TransportKind.Value),
            },
            cause: failure.Cause ?? failure);
    }

    /// <summary>
    /// <c>Details.reason</c>'s vocabulary: the fixture <c>fail</c> names, so an operator reading an
    /// error and an author reading a fixture see the same word in all three languages.
    /// </summary>
    private static string Reason(TransportFailureKind kind)
    {
        return kind switch
        {
            TransportFailureKind.ConnectionRefused => "connection_refused",
            TransportFailureKind.Reset => "reset",
            // The only remaining kind ReclassifyNodeFailure admits.
            _ => "timeout",
        };
    }

    /// <summary>
    /// DSC-044: the original error, with <c>Attempts</c> incremented to the total actually made.
    /// Every other field is carried over, so the caller sees the failure that started the failover
    /// and not a second description of it.
    /// </summary>
    private static BastionVaultException Reattribute(BastionVaultException original, int attempts)
    {
        return BastionVaultException.Request(
            original.Code,
            original.Category,
            original.Message,
            original.Hint,
            original.Retryable,
            attempts: attempts,
            serverMessage: original.ServerMessage,
            serverErrors: original.ServerErrors,
            statusCode: original.StatusCode,
            retryAfter: original.RetryAfter,
            method: original.Method,
            path: original.Path,
            address: original.Address,
            details: original.Details,
            cause: original.Cause);
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
        string method,
        CallBudget budget)
    {
        try
        {
            return await pass(execution).ConfigureAwait(false);
        }
        // D-M5-29: the one-shot is carried on the execution rather than being implicit in this
        // method's own control flow. Failover wraps re-login, so a failover replay re-enters here
        // with a fresh invocation, and without this flag one caller-visible operation could
        // re-login twice — against D-M2-9's "one caller call, one replay". The 30-second
        // MinReloginInterval default hides it; an application setting it to zero does not.
        catch (BastionVaultException denied) when (budget.ReloginAvailable && IsReloginCandidate(denied, method))
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

            budget.ReloginAvailable = false;
            // D-M6-21 (R-18): the replay is bounded by what is left of RES-001's total, exactly as
            // D-M5-28 bounded the failover replay. D-M2-9 gave it a fresh MaxAttempts, which let a
            // single caller call reach 2 x MaxAttempts wire attempts against a cap of
            // MaxAttempts + 1 — measured at 6 against 4. RES-001 says "total attempts" with no
            // qualifier, so the Strategic ruling was to make the two replay mechanisms obey the one
            // cap. The cost is accepted and recorded: where pass 1 burnt the whole budget, AUT-003's
            // replay keeps one attempt, which "MAY re-login once and replay" still permits.
            return await pass(execution with { AttemptsBefore = denied.Attempts, IsBoundedReplay = true }).ConfigureAwait(false);
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
    {
        return string.Equals(error.Code, ErrorCodes.AuthzPermissionDenied, StringComparison.Ordinal)
                && error is not IRecognizedAtSource { RecognizedAtSource: true }
                && method is "GET" or "LIST" or "HEAD";
    }

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
    /// (D-M1b-22) — unless <paramref name="pauseRateGateOn429"/> is <see langword="false"/>, which
    /// <c>sys/health</c> passes because that path is DoS-guard-exempt (SYS-001/06-system-api.md:15)
    /// and its own 429 is a normal, non-error outcome rather than a signal that the caller was
    /// actually throttled — and applies the retry-eligibility/backoff/<c>TotalTimeout</c> (RES-004)
    /// rules — identically whether <paramref name="classify"/> is shaping a logical
    /// <see cref="Outcome"/> or a <see cref="RawResponse"/>.
    /// </summary>
    private async Task<TResult> RunLoopAsync<TResult>(
        string method,
        Target target,
        ReadOnlyMemory<byte>? body,
        RequestOptions options,
        bool isIdempotent,
        string rawPath,
        string displayPath,
        Func<TransportResponse, int, Verdict<TResult>> classify,
        RequestExecution execution,
        bool refuseWithoutToken,
        bool isLogin,
        CallBudget budget,
        bool nodeLocal,
        CancellationToken cancellationToken,
        bool pauseRateGateOn429 = true,
        bool nonRetryable = false,
        BinaryShape binary = BinaryShape.None,
        string? endpointOverride = null)
    {
        ClientConfig config = context.Config;
        // D-M5-9: the first operation on a discovery-mode client runs discovery before its first
        // attempt, as if ConnectAsync had been called. A no-op for a literal client, which is every
        // client with a `://`, an explicit port, or an IP-literal address.
        await context.Discovery.EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        RetryPolicy retryPolicy = config.RetryPolicy;
        int attempt = 0;
        int configuredAttempts = Math.Max(1, retryPolicy.MaxAttempts);
        // D-M5-28 and D-M6-21: a *replay* gets only what is left of RES-001's global budget, not a
        // fresh MaxAttempts. Without the clamp, a node failure on a later attempt — two 502s
        // retried under CFG-050, then a refused connection — hands the replay a whole new budget
        // and the caller sees MaxAttempts + MaxAttempts wire attempts against a cap of
        // MaxAttempts + 1. `Math.Max` contains the exhausted case, so a replay always keeps at
        // least one attempt, which is what keeps DSC-042's MUST satisfiable (and
        // `resilience.failover.read-once` green at MaxAttempts: 1). Stated precisely on purpose:
        // the RES-001 breach this clamp fixes was itself a confidently-worded false premise in a
        // comment two lines from here.
        //
        // Both replay mechanisms are bounded, and R-18 is what closed the gap between them.
        // D-M5-28 clamped DSC-042's failover replay and left AUT-003's relogin replay on D-M2-9's
        // fresh budget, on the reading that RES-001 governs only the former; that reading was
        // ruled against (RES-001 says "total attempts", unqualified), so the flag now means "this
        // pass is a replay" rather than "this pass is a failover replay". It is still a flag
        // rather than an inference from AttemptsBefore, which is non-zero for an ordinary retried
        // pass too.
        int maxAttempts = execution.IsBoundedReplay
            ? Math.Max(1, configuredAttempts + 1 - execution.AttemptsBefore)
            : configuredAttempts;
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
            // D-M5-11: built per attempt, from the endpoint cell, because failover requires the
            // authority to change between attempts. In literal mode the cell holds
            // `config.Address` and never changes, so this is byte-identical to building it once.
            // RES-030: a *ClusterWide variant addresses one named node per call rather than the
            // pinned one, so the endpoint is an argument there. It is null for every other
            // operation, which leaves this read byte-identical to D-M5-11's.
            string attemptEndpoint = endpointOverride ?? context.Endpoint;
            Uri uri = BuildUri(attemptEndpoint, target);
            IReadOnlyDictionary<string, string> requestHeaders = BuildHeaders(config, options, tokenValue, body is not null, binary);
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
                // EFF-001: the one choke point every caller-visible request passes through, so
                // "every outgoing request" is a property of this line rather than of a habit. The
                // two egress paths that do not pass through here — the DSC-020 health probe and
                // the DSC-014 SRV lookup — call the gate too, claiming their exemption by name
                // (`EgressKind`), so an un-gated path cannot be mistaken for a forgotten one.
                //
                // Inside this `try` deliberately: a cancellation while queued is then mapped by
                // the `OperationCanceledException` arm below into BV-TRANSPORT-005 like any other
                // cancelled call, rather than escaping as a bare runtime exception (ERR-020).
                await context.RateGate.AcquireAsync(EgressKind.Request, cancellationToken).ConfigureAwait(false);

                // RES-002's Duration is the request's, not the queue's: a caller reading a 3 s
                // duration must be able to conclude the server took 3 s. The gate wait is visible
                // through RateGateState (EFF-006) instead.
                stopwatch.Restart();

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
                // sys/health's own 429 is exempt (pauseRateGateOn429 is false there): SYS-001 makes
                // it a normal Standby outcome, not a sign the caller was actually rate-limited.
                if (response.StatusCode == 429 && pauseRateGateOn429)
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

            // DSC-041 limb (i), scoped to discovery mode by D-M5-5: a connection refused, a reset or
            // a timeout on a node *discovery chose* is a node failure. In literal mode the M1b
            // mapping is untouched, which is what keeps the three landed BV-TRANSPORT-* fixtures
            // green without amendment.
            BastionVaultException error = ReclassifyNodeFailure(failure!, uri);

            // D-M5-6 and §13's retry-policy step 1: failover runs *before* any backoff retry, so a
            // pending failover suppresses this pass's retry rather than competing with it. A node
            // failure is not in CFG-050's default RetryOn anyway — this is what also stops a
            // limb (ii) BV-SERVER-003, which *is* in it, from being retried against the dead node
            // before the replay has had its turn (§13:125, "retried only via failover").
            bool failoverPending = budget.FailoverAvailable && WillFailover(error, isIdempotent, nodeLocal);
            if (failoverPending)
            {
                // The endpoint this attempt actually used, for D-M5-12's late-arrival comparison.
                // Captured here because by the time the failover lock is free another task may
                // already have moved the pin.
                budget.FailedEndpoint = attemptEndpoint;
            }

            // SYS-013: `Seal` and `Unseal` are flagged non-retryable at the operation, so no
            // RetryPolicy a caller configures can replay them — not `RetryOn` carrying their code,
            // and not `RetryIdempotentOnly: false` turning the write arm back on. The flag sits
            // here, beside the policy it overrides, rather than at `isIdempotent`: `isIdempotent`
            // is also the *failover* predicate (`WillFailover`), and DSC-045's exclusion is the
            // separate `nodeLocal` flag, so folding the two together would make one requirement's
            // change silently move the other.
            bool eligible = !nonRetryable
                && attempt < maxAttempts
                && retryPolicy.RetryOn.Contains(error.Code, StringComparer.Ordinal)
                && !IsHardExcluded(error.Code)
                && (isIdempotent || !retryPolicy.RetryIdempotentOnly)
                && (deadline is null || context.Clock.NowUtc() < deadline)
                && !failoverPending;

            if (!eligible)
            {
                throw Present(error, attemptsTotal, method, displayPath, EffectiveNamespace(options), config);
            }

            TimeSpan backoff = BackoffCalculator.Compute(retryPolicy, attempt, context.JitterSource);
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
                config.Address,
                Method: method));

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

        if (response.StatusCode is >= 300 and <= 399 and not 304)
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
            response.StatusCode, extracted.serverMessage, extracted.serverErrors, retryAfter, method, logicalPath, address, attempt, BodyEmpty: false, Body: parsed));
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
    {
        return code is ErrorCodes.ServerSealed or ErrorCodes.RateLimitedByDosGuard;
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
            if (b is not ((byte)' ') and not ((byte)'\t') and not ((byte)'\r') and not ((byte)'\n'))
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
            _ = builder.Append(char.IsControl(c) ? ' ' : c);
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

    /// <summary>
    /// SYS-090: which half of a request/response pair carries <c>application/octet-stream</c>
    /// rather than JSON. <c>Sys.Backup</c> sends no body and reads bytes; <c>Sys.Restore</c> sends
    /// bytes and reads JSON. Modelled as an enum rather than two booleans because the two are
    /// never both set by any operation the specification names, and a pair of booleans would
    /// permit a combination with no meaning.
    /// </summary>
    internal enum BinaryShape
    {
        /// <summary>JSON in, JSON out: every operation but SYS-090's two.</summary>
        None,

        /// <summary>JSON (or no) request body, <c>application/octet-stream</c> response: <c>Sys.Backup</c>.</summary>
        Response,

        /// <summary><c>application/octet-stream</c> request body, JSON response: <c>Sys.Restore</c>.</summary>
        Request,
    }

    private Dictionary<string, string> BuildHeaders(ClientConfig config, RequestOptions options, string token, bool hasBody, BinaryShape binary = BinaryShape.None)
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

        // Set after the caller's CFG-017 headers, as they always have been, so a per-call header
        // cannot repoint the content negotiation of a typed operation. SYS-090's two operations
        // are the only ones that are not JSON on both halves, and they say so through `binary`
        // rather than through an overridable header.
        headers["Accept"] = binary == BinaryShape.Response ? "application/octet-stream" : "application/json";
        if (hasBody)
        {
            headers["Content-Type"] = binary == BinaryShape.Request ? "application/octet-stream" : "application/json";
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

    /// <summary>
    /// Everything about a request URI except its authority, which D-M5-11 moved to a per-attempt
    /// read off <see cref="ClientContext.Endpoint"/>.
    /// </summary>
    private readonly record struct Target(string ApiVersion, string RawPath, bool IsRaw, bool PathIsEncoded);

    private static Uri BuildUri(string endpoint, Target target)
    {
        (string encodedPath, string? encodedQuery) = UrlBuilder.SplitAndEncode(target.RawPath, target.PathIsEncoded);
        string baseAddress = endpoint.TrimEnd('/');
        StringBuilder builder = new(baseAddress);
        if (!target.IsRaw)
        {
            _ = builder.Append('/').Append(target.ApiVersion);
        }

        _ = builder.Append('/').Append(encodedPath);
        if (!string.IsNullOrEmpty(encodedQuery))
        {
            _ = builder.Append('?').Append(encodedQuery);
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
    {
        return activeNamespace.Length == 0 ? rawPath : $"[ns={activeNamespace}] {rawPath}";
    }

    /// <summary>The namespace this request actually carries: the per-request override, else the client/view's.</summary>
    private string EffectiveNamespace(RequestOptions options)
    {
        return (options.Namespace ?? activeNamespace).TrimEnd('/');
    }
}
