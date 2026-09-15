using System.Text;
using BastionVault.IntegrationSdk.Internal;
using BastionVault.IntegrationSdk.Testing;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// The four blocking findings of M2a's R3 handback review (F1…F4), each reproduced before it was
/// fixed. All four were in the request path and none was visible to the fixture suite, which is
/// why the gate reads the path rather than only the results.
/// </summary>
public sealed class AuthReviewFindingTests
{
    private const string Address = "https://vault.example.com:8200";

    // ---- F1: RefineForTokenStore dropped both of AUT-085's conditions and both of AUT-084's. ----

    [Fact]
    [Requirement("AUT-085")]
    [Trait("Requirement", "AUT-085")]
    public async Task F1_AUT_085_fires_only_on_a_400_whose_message_is_exactly_request_is_invalid()
    {
        // AUT-085 (05-authentication.md:214) is conditioned on 400 *and* `Request is invalid.`.
        // Appendix B §2 has three further rows yielding BV-INPUT-100 — `request field is not
        // found`, `request field is invalid`, `no data field is available for the request` — and
        // ResolveCode routes every unmapped 4xx there too. Refining on the post-fallthrough code
        // therefore turned a malformed request body into TokenNotRenewable, and a caller
        // branching on TokenNotRenewable to re-login would re-login in response to its own bug.
        // `increment` is required by the spec's table, so the missing-`increment` shape below is
        // not hypothetical.
        (string Body, int Status, string Expected)[] cases =
        [
            // The one AUT-085 actually names.
            ("""{"error":"Request is invalid."}""", 400, ErrorCodes.AuthTokenNotRenewable),
            // Same recognised code, different row: the caller's body was wrong, not the token.
            ("""{"error":"request field is not found"}""", 400, ErrorCodes.InputServerRejectedRequest),
            ("""{"error":"request field is invalid"}""", 400, ErrorCodes.InputServerRejectedRequest),
            ("""{"error":"no data field is available for the request"}""", 400, ErrorCodes.InputServerRejectedRequest),
            // Right message, wrong status: AUT-085 names 400 and nothing else.
            ("""{"error":"Request is invalid."}""", 422, ErrorCodes.InputServerRejectedRequest),
            // No message at all reaches the status table, not AUT-085.
            ("", 400, ErrorCodes.InputServerRejectedRequest),
        ];

        foreach ((string body, int status, string expected) in cases)
        {
            FakeTransport transport = new();
            transport.EnqueueResponse(status, body: Encoding.UTF8.GetBytes(body));
            BastionVaultClient client = BuildClient(transport);

            BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
                () => client.Auth.Token.RenewAsync(FakeTokens.Explicit, 3600));

            Assert.Equal(expected, exception.Code);
        }
    }

    [Fact]
    [Requirement("AUT-085")]
    [Trait("Requirement", "AUT-085")]
    public async Task F1_AUT_085_does_not_fire_off_the_renew_path()
    {
        // The refinement is scoped to `auth/token/renew/{token}`. A user path that merely contains
        // that text is not the renew endpoint.
        FakeTransport transport = new();
        transport.EnqueueResponse(400, body: Encoding.UTF8.GetBytes("""{"error":"Request is invalid."}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Logical.WriteAsync("secret/data/auth/token/renew/notes"));

        Assert.Equal(ErrorCodes.InputServerRejectedRequest, exception.Code);
    }

    [Fact]
    [Requirement("AUT-084")]
    [Trait("Requirement", "AUT-084")]
    public async Task F1_AUT_084_fires_only_on_a_404_with_an_empty_body_under_the_lookup_token_path()
    {
        // AUT-084 names three conditions — 404, *empty body*, and a `Lookup` of a token — and the
        // predicate tested only the first. `Contains("auth/token/lookup")` additionally matched
        // `lookup-self` (a different endpoint, for which the specification names no refinement)
        // and any caller path containing that substring.
        FakeTransport unknownToken = new();
        unknownToken.EnqueueResponse(404);
        BastionVaultException notFound = await Assert.ThrowsAsync<BastionVaultException>(
            () => BuildClient(unknownToken).Auth.Token.LookupAsync(FakeTokens.Explicit));
        Assert.Equal(ErrorCodes.NotFoundTokenNotFound, notFound.Code);

        // A 404 with a body is not AUT-084's shape: the body is the server telling the caller
        // something else, and D-M1c-25 says a deferred branch returns what the specification
        // names rather than a plausible guess.
        FakeTransport withBody = new();
        withBody.EnqueueResponse(404, body: Encoding.UTF8.GetBytes("""{"error":"unrecognised by appendix b"}"""));
        BastionVaultException bodied = await Assert.ThrowsAsync<BastionVaultException>(
            () => BuildClient(withBody).Auth.Token.LookupAsync(FakeTokens.Explicit));
        Assert.Equal(ErrorCodes.NotFoundPathNotFound, bodied.Code);

        // `lookup-self` is a distinct path and AUT-084 names `Lookup`.
        FakeTransport self = new();
        self.EnqueueResponse(404);
        BastionVaultException lookupSelf = await Assert.ThrowsAsync<BastionVaultException>(
            () => BuildClient(self).Auth.Token.LookupSelfAsync());
        Assert.Equal(ErrorCodes.NotFoundPathNotFound, lookupSelf.Code);

        // A caller path that merely contains the text is not the token-lookup endpoint.
        FakeTransport lookalike = new();
        lookalike.EnqueueResponse(404);
        BastionVaultException notAuth = await Assert.ThrowsAsync<BastionVaultException>(
            () => BuildClient(lookalike).Logical.WriteAsync("secret/data/auth/token/lookup/notes"));
        Assert.Equal(ErrorCodes.NotFoundPathNotFound, notAuth.Code);
    }

    // ---- F2: RenewSelfAsync resolved the token twice. ----

    [Fact]
    [Requirement("AUT-080")]
    [Trait("Requirement", "AUT-080")]
    public async Task F2_RenewSelf_resolves_the_current_token_exactly_once()
    {
        // AUT-080 says "the current token in the path". Two resolutions mean there is no single
        // current token: with a Callback source — whose contract is to be called on every
        // resolution — the path renewed token A while the header authenticated as token B.
        int resolutions = 0;
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Encoding.UTF8.GetBytes(RenewBody()));
        BastionVaultClient client = BuildClient(transport, options => options.TokenSource = TokenSource.Callback(_ =>
        {
            int ordinal = Interlocked.Increment(ref resolutions);
            return Task.FromResult(new SecretString(FakeTokens.Rotating(ordinal)));
        }));

        _ = await client.Auth.Token.RenewSelfAsync(3600);

        Assert.Equal(1, resolutions);
        string pathToken = transport.Requests[0].Uri.AbsolutePath["/v1/auth/token/renew/".Length..];
        Assert.Equal(pathToken, transport.Requests[0].Headers["X-BastionVault-Token"]);
    }

    [Fact]
    [Requirement("AUT-080")]
    [Requirement("CFG-060")]
    [Trait("Requirement", "AUT-080")]
    public async Task F2_RenewSelf_honours_a_per_call_token_override_as_the_current_token()
    {
        // With RequestOptions.Token set, that per-call token *is* the token this call authenticates
        // as (CFG-060), so it is also the one AUT-080 puts in the path — and no resolution of the
        // client's own source happens at all.
        int resolutions = 0;
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Encoding.UTF8.GetBytes(RenewBody()));
        BastionVaultClient client = BuildClient(transport, options => options.TokenSource = TokenSource.Callback(cancellationToken =>
        {
            _ = Interlocked.Increment(ref resolutions);
            return Task.FromResult(new SecretString(FakeTokens.Child));
        }));

        _ = await client.Auth.Token.RenewSelfAsync(3600, new RequestOptions { Token = new SecretString(FakeTokens.Explicit) });

        Assert.Equal(0, resolutions);
        Assert.Equal($"/v1/auth/token/renew/{FakeTokens.Explicit}", transport.Requests[0].Uri.AbsolutePath);
        Assert.Equal(FakeTokens.Explicit, transport.Requests[0].Headers["X-BastionVault-Token"]);
    }

    // ---- F3 (D-M2-17): a faulted single-flight must not poison the client. ----

    [Fact]
    [Requirement("CFG-070")]
    [Trait("Requirement", "CFG-070")]
    public async Task F3_the_awaiters_of_one_failed_flight_share_its_failure_and_none_of_them_retries()
    {
        // D-M2-11(a) said concurrent callers share one login *attempt*. It never said a failure is
        // cached for the client's lifetime — which is what Lazy<Task<T>> with
        // ExecutionAndPublication does by itself.
        FailingLogin login = new();
        TokenSource source = TokenSource.LoginWith(login.PerformAsync);

        // Invoked directly rather than through Task.Run, which makes the attachment deterministic
        // instead of merely likely. ResolveAsync is an async method, so its body runs synchronously
        // up to its first await — and that await is on `flight.Value`, which means every call has
        // already read the single-flight cell and joined *this* flight by the time it hands back a
        // task. Through Task.Run a straggler could read the cell after the failure re-armed it,
        // legitimately start a second login, and make the assertion below flake — which it did,
        // under full-suite load and not in isolation.
        Task<SecretString?>[] first = Enumerable.Range(0, 8).Select(_ => source.ResolveAsync()).ToArray();
        Assert.True(login.WaitUntilEntered().IsCompleted);
        login.FailTheFlight();

        foreach (Task<SecretString?> awaiter in first)
        {
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => awaiter).ConfigureAwait(false);
        }

        // Eight awaiters, one attempt: no retry storm.
        Assert.Equal(1, login.Invocations);
    }

    [Fact]
    [Requirement("CFG-070")]
    [Trait("Requirement", "CFG-070")]
    public async Task F3_a_subsequent_resolution_after_a_failed_flight_attempts_again()
    {
        // One transient login failure at startup must not brick the client permanently.
        FailingLogin login = new();
        TokenSource source = TokenSource.LoginWith(login.PerformAsync);

        Task<SecretString?> failing = Task.Run(() => source.ResolveAsync());
        await login.WaitUntilEntered().ConfigureAwait(false);
        login.FailTheFlight();
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => failing).ConfigureAwait(false);

        login.Rearm(succeed: true);
        SecretString? recovered = await source.ResolveAsync().ConfigureAwait(false);

        Assert.Equal(FakeTokens.Child, recovered!.Reveal());
        Assert.Equal(2, login.Invocations);
    }

    [Fact]
    [Requirement("CFG-070")]
    [Trait("Requirement", "CFG-070")]
    public async Task F3_a_login_that_throws_synchronously_or_cancels_itself_also_recovers()
    {
        // A synchronous throw from the performer faults the Lazy *factory*, whose cached exception
        // Lazy rethrows forever — a strictly worse poisoning than the faulted-task case, and one
        // the async wrapper below removes. A performer that raises OperationCanceledException
        // itself faults the flight as Canceled rather than Faulted, which is the third state the
        // re-arm has to recognise.
        int calls = 0;
        TokenSource synchronous = TokenSource.LoginWith(_ =>
        {
            calls++;
            return calls == 1
                ? throw new InvalidOperationException("synchronous login failure")
                : Task.FromResult(new SecretString(FakeTokens.Child));
        });

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => synchronous.ResolveAsync()).ConfigureAwait(false);
        Assert.Equal(FakeTokens.Child, (await synchronous.ResolveAsync().ConfigureAwait(false))!.Reveal());
        Assert.Equal(2, calls);

        int cancelling = 0;
        TokenSource selfCancelled = TokenSource.LoginWith(async _ =>
        {
            cancelling++;
            await Task.Yield();
            return cancelling == 1
                ? throw new OperationCanceledException("the source cancelled itself")
                : new SecretString(FakeTokens.Child);
        });

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => selfCancelled.ResolveAsync()).ConfigureAwait(false);
        Assert.Equal(FakeTokens.Child, (await selfCancelled.ResolveAsync().ConfigureAwait(false))!.Reveal());
        Assert.Equal(2, cancelling);
    }

    // ---- F4 (D-M2-16): the two codes the Strategic tree minted. ----

    [Fact]
    [Requirement("AUT-001")]
    [Trait("Requirement", "AUT-001")]
    public async Task F4_a_token_source_that_fails_maps_to_BV_AUTH_017_with_its_exception_as_the_cause()
    {
        // BV-AUTH-001 NoToken says "No token is *configured*"; here one is configured and its
        // resolution failed, so no existing row fits. BV-AUTH-017 TokenSourceFailed is the row
        // DR-0006 D-M2-16 added for it.
        InvalidOperationException cause = new("the KMS is unreachable");
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport, options =>
            options.TokenSource = TokenSource.Callback(_ => Task.FromException<SecretString>(cause)));

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Logical.ReadAsync("secret/data/x"));

        Assert.Equal(ErrorCodes.AuthTokenSourceFailed, exception.Code);
        Assert.Equal(ErrorCategory.Authentication, exception.Category);
        // ERR-006's retryable set is closed and does not contain this code.
        Assert.False(exception.Retryable);
        Assert.Equal(0, exception.Attempts);
        Assert.Same(cause, exception.Cause);
        Assert.Empty(transport.Requests);
        // The source's own message is not the SDK's message, and the catalogue's is used verbatim.
        Assert.Equal("The configured token source did not produce a token.", exception.Message);
    }

    [Fact]
    [Requirement("AUT-001")]
    [Trait("Requirement", "AUT-001")]
    public async Task F4_a_coded_error_a_source_delegate_merely_leaks_is_now_wrapped_as_BV_AUTH_017()
    {
        // D-M2-25 item 2 narrowed M2a's guard. M2a passed *every* BastionVaultException from a
        // source through unwrapped, on the reasoning that M2b's Login source raises BV-AUTH-004
        // and friends and those must reach the caller with their own code. That reasoning is still
        // right — see the companion test below — but it was implemented with too wide a filter:
        // a Callback delegate that leaks an unrelated coded exception (raised by code *inside* the
        // delegate rather than by the login) also surfaced verbatim, carrying the source's own
        // Path and Method as if they were the outer request's.
        BastionVaultException inner = BastionVaultException.Config(
            ErrorCodes.ConfigEncryptedTokenFile, "The token file is in the CLI's encrypted `BVTOK1:` format.", "hint");
        BastionVaultClient client = BuildClient(new FakeTransport(), options =>
            options.TokenSource = TokenSource.Callback(_ => Task.FromException<SecretString>(inner)));

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Logical.ReadAsync("secret/data/x"));

        Assert.NotSame(inner, exception);
        Assert.Equal(ErrorCodes.AuthTokenSourceFailed, exception.Code);
        // The original is preserved rather than flattened into a message, and the surfaced request
        // identity is now the outer read's, not the source's.
        Assert.Same(inner, exception.Cause);
        Assert.Equal("GET", exception.Method);
        Assert.Equal("secret/data/x", exception.Path);
    }

    [Fact]
    [Requirement("CFG-031")]
    [Trait("Requirement", "CFG-031")]
    public void F4_an_unwritable_token_file_is_BV_CONFIG_011_not_the_not_readable_code()
    {
        // BV-CONFIG-005's message says the file cannot be *read*, which is the wrong sentence for
        // a failed PersistToken. D-M2-16 amends D-M2-13 and lands BV-CONFIG-011 now.
        string unwritable = Path.Combine(Path.GetTempPath(), $"bastionvault-absent-{Guid.NewGuid():n}", "nested", "token");
        BastionVaultClient client = BuildClient(new FakeTransport(), options =>
        {
            options.Token = FakeTokens.Client;
            options.TokenFile = unwritable;
        });

        BastionVaultException exception = Assert.Throws<BastionVaultException>(client.Auth.PersistToken);

        Assert.Equal(ErrorCodes.ConfigTokenFileNotWritable, exception.Code);
        Assert.Equal("The token file cannot be written.", exception.Message);
        Assert.Contains("Auth.PersistToken", exception.Hint, StringComparison.Ordinal);
        Assert.Equal(unwritable, exception.Details["path"]);
        Assert.NotNull(exception.Cause);

        // ForgetPersistedToken reports the same code when the delete itself cannot be done — and
        // still treats a merely absent file as success (CFG-032).
        string directory = Directory.CreateTempSubdirectory("bastionvault-forget-code-").FullName;
        try
        {
            BastionVaultClient onDirectory = BuildClient(new FakeTransport(), options =>
            {
                options.Token = FakeTokens.Client;
                options.TokenFile = directory;
            });

            BastionVaultException failure = Assert.Throws<BastionVaultException>(onDirectory.Auth.ForgetPersistedToken);
            Assert.Equal(ErrorCodes.ConfigTokenFileNotWritable, failure.Code);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // ---- helpers ----

    private static BastionVaultClient BuildClient(ITransport transport, Action<BastionVaultClientOptions>? configure = null)
    {
        BastionVaultClientOptions options = new()
        {
            Address = Address,
            Token = FakeTokens.Client,
            Transport = transport,
            Clock = SystemClock.Instance,
            RateGate = new RateGate { RatePerSecond = 0 },
            RetryPolicy = new RetryPolicy { MaxAttempts = 1 },
        };
        configure?.Invoke(options);
        return new BastionVaultClient(options, EnvironmentSource.None);
    }

    /// <summary>
    /// A renew response. The token is assembled from <see cref="FakeTokens"/> rather than written
    /// as a literal: CNF-025's secret scan rejects `s.&lt;20+ alnum&gt;` outside the fixtures
    /// directory, and D-M1c-15 fixed that by building the values at runtime rather than by
    /// widening the whitelist.
    /// </summary>
    private static string RenewBody()
    {
        return $$$"""
        {"renewable":false,"lease_id":"","lease_duration":0,
         "auth":{"client_token":"{{{FakeTokens.Renewed}}}","policies":["default"],
                 "metadata":{},"lease_duration":3600,"renewable":true},"data":{}}
        """;
    }

    /// <summary>
    /// A login performer whose first flight fails on command, so "the awaiters of one flight share
    /// its failure" and "a subsequent resolution attempts again" can be asserted separately.
    /// </summary>
    private sealed class FailingLogin
    {
        private TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<SecretString> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int invocations;

        public int Invocations => Volatile.Read(ref invocations);

        public async Task<SecretString> PerformAsync(CancellationToken cancellationToken)
        {
            _ = Interlocked.Increment(ref invocations);
            _ = entered.TrySetResult();
            return await gate.Task.ConfigureAwait(false);
        }

        public Task WaitUntilEntered()
        {
            return entered.Task;
        }

        public void FailTheFlight()
        {
            _ = gate.TrySetException(new InvalidOperationException("login failed"));
        }

        public void Rearm(bool succeed)
        {
            entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            gate = new TaskCompletionSource<SecretString>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (succeed)
            {
                _ = gate.TrySetResult(new SecretString(FakeTokens.Child));
            }
        }
    }
}
