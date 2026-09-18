using System.Globalization;
using System.Text;
using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;
using BastionVault.IntegrationSdk.Testing;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// M2b's requirements whose assertions are not expressible as a single-threaded wire-shape fixture:
/// the login-response contract's non-fixtured arms (AUT-010…AUT-013), AUT-002's two paths,
/// AUT-003's re-login and replay, AUT-030…AUT-032, AUT-040…AUT-042, AUT-044's derivation table,
/// the section-05 security requirements (AUT-100, AUT-101, CNF-031, CNF-032), and CFG-020/ERR-022's
/// client-side refusal across the whole unauthenticated-path list.
/// </summary>
public sealed class AuthLoginTests
{
    private const string Address = "https://vault.example.com:8200";
    private const string Password = "password-fixture";

    // ---- AUT-030: the Userpass request shape. ----

    [Fact]
    [Requirement("AUT-030")]
    [Trait("Requirement", "AUT-030")]
    public async Task Userpass_login_posts_to_the_encoded_username_path_and_omits_totp_code_when_absent()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(LoginBody(FakeTokens.Child)));
        transport.EnqueueResponse(200, body: Json(LoginBody(FakeTokens.Child)));
        BastionVaultClient client = BuildClient(transport);

        // TRN-020: the username is a single path segment and is percent-encoded there. `/` is in
        // the reserved set precisely so a username cannot forge an extra segment.
        _ = await client.Auth.Userpass.LoginAsync("a d/e?f", new SecretString(Password));
        _ = await client.Auth.Userpass.LoginAsync("alice", new SecretString(Password), "123456", "corp-users");

        Assert.Equal("POST", transport.Requests[0].Method);
        // TRN-020 lists `/` "inside a segment" and `?` in the set, and both are load-bearing here:
        // unencoded, `a d/e?f` would have sent the two-segment path `.../login/a%20d/e` with `f`
        // moved into the query string — a different endpoint, reachable without a token.
        Assert.Equal("/v1/auth/userpass/login/a%20d%2Fe%3Ff", transport.Requests[0].Uri.AbsolutePath);
        Assert.Equal(string.Empty, transport.Requests[0].Uri.Query);
        // AUT-030: `totp_code` is *omitted*, not sent empty.
        Assert.Equal($"{{\"password\":\"{Password}\"}}", BodyOf(transport.Requests[0]));

        Assert.Equal("/v1/auth/corp-users/login/alice", transport.Requests[1].Uri.AbsolutePath);
        Assert.Equal($"{{\"password\":\"{Password}\",\"totp_code\":\"123456\"}}", BodyOf(transport.Requests[1]));
        // A SecretString may hold no value at all. `password` is a required body field, so it is
        // sent as the empty string rather than as JSON `null`, which the server would reject with a
        // shape error instead of the credential error the caller needs to see.
        transport.EnqueueResponse(200, body: Json(LoginBody(FakeTokens.Child)));
        _ = await client.Auth.Userpass.LoginAsync("alice", SecretString.Empty);
        Assert.Equal("""{"password":""}""", BodyOf(transport.Requests[2]));
        // TRN-015 / CFG-020's first MUST: a login never carries the token header, even once the
        // client holds a token from the previous login.
        Assert.DoesNotContain("X-BastionVault-Token", transport.Requests[1].Headers.Keys);
    }

    // ---- AUT-010: a 200 without a token is a failure, and no token is stored. ----

    [Theory]
    [InlineData("""{"renewable":false,"lease_id":"","lease_duration":0,"auth":null,"data":{}}""")]
    [InlineData("""{"renewable":false,"auth":{"policies":["default"]},"data":{}}""")]
    [InlineData("""{"renewable":false,"auth":{"client_token":""},"data":{}}""")]
    [Requirement("AUT-010")]
    [Trait("Requirement", "AUT-010")]
    public async Task A_200_login_response_without_a_usable_client_token_is_BV_AUTH_003_and_stores_nothing(string body)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(body));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.Userpass.LoginAsync("alice", new SecretString(Password)));

        Assert.Equal(ErrorCodes.AuthLoginRejected, exception.Code);
        Assert.Equal(200, exception.StatusCode);
        Assert.False(exception.Retryable);
        Assert.Equal(1, exception.Attempts);
        // "MUST never store an empty token": an empty `client_token` is as much a failure as a
        // missing one, and neither leaves the client holding a credential.
        Assert.Null(client.Auth.CurrentToken);
        Assert.Equal(TokenSourceKind.Static, client.Auth.TokenSource.Kind);
    }

    [Fact]
    [Requirement("AUT-010")]
    [Trait("Requirement", "AUT-010")]
    public async Task The_rejection_reason_is_carried_verbatim_as_ServerMessage_and_is_absent_when_the_body_states_none()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"auth":null,"data":{"error":"Some reason the SDK does not recognise."}}"""));
        transport.EnqueueResponse(200, body: Json("""{"auth":null,"data":{}}"""));
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException stated = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.Userpass.LoginAsync("alice", new SecretString(Password)));
        BastionVaultException silent = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.Userpass.LoginAsync("alice", new SecretString(Password)));
        // A 200 with no body at all: the executor reports it as empty, so there is no `data.error`
        // either. Still a login failure, still BV-AUTH-003 (AUT-010), never a fabricated AuthInfo.
        BastionVaultException empty = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.Userpass.LoginAsync("alice", new SecretString(Password)));

        Assert.Equal("Some reason the SDK does not recognise.", stated.ServerMessage);
        Assert.Equal(ErrorCodes.AuthLoginRejected, stated.Code);
        Assert.Null(silent.ServerMessage);
        Assert.Equal(ErrorCodes.AuthLoginRejected, silent.Code);
        Assert.Null(empty.ServerMessage);
        Assert.Equal(ErrorCodes.AuthLoginRejected, empty.Code);
    }

    // ---- AUT-011: refinement through the generated table, not a hand-written predicate. ----

    public static TheoryData<string, string> GeneratedLoginRejections()
    {
        TheoryData<string, string> data = [];
        foreach ((string code, string message) in LoginRejectionMessages.All)
        {
            data.Add(code, message);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(GeneratedLoginRejections))]
    [Requirement("AUT-011")]
    [Trait("Requirement", "AUT-011")]
    public async Task Every_generated_login_rejection_message_refines_BV_AUTH_003_to_its_own_code(string code, string message)
    {
        // D-M2-4a: AUT-011 is reached by calling the *shared generated table* from the 200 path,
        // so this theory is driven by the table itself. A reworded Appendix B row, a changed code
        // or a deleted row changes the cases this test runs — which is the property D-M2-25 item 3
        // was after, and the reason the mock server reads the same list.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(JsonSerializer.Serialize(new
        {
            renewable = false,
            lease_id = string.Empty,
            lease_duration = 0,
            auth = (object?)null,
            data = new { error = message },
        })));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.Userpass.LoginAsync("alice", new SecretString(Password)));

        Assert.Equal(code, exception.Code);
        Assert.Equal(message, exception.ServerMessage);
        Assert.Equal(200, exception.StatusCode);
        // Retryability is ERR-006's, never the call site's: `rate_limited: …` → BV-RATE-001 is
        // rendered `no` by Appendix B, and a 200 call site does not override it (D-M2-4a).
        Assert.Equal(ErrorCatalog.Require(code).Retryable, exception.Retryable);
        Assert.Null(client.Auth.CurrentToken);
    }

    [Fact]
    [Requirement("AUT-011")]
    [Trait("Requirement", "AUT-011")]
    public async Task The_generated_table_covers_every_code_AUT_011_names_and_a_locked_account_exposes_retry_after_secs()
    {
        // The enumeration AUT-011 itself gives, asserted against the generated table so a row lost
        // in the appendix fails here rather than silently reducing the theory above to nothing.
        string[] expected =
        [
            ErrorCodes.AuthLoginRejected, ErrorCodes.AuthInvalidCredentials, ErrorCodes.AuthAccountDisabled,
            ErrorCodes.AuthAccountLocked, ErrorCodes.AuthTotpRequired, ErrorCodes.AuthInvalidTotp,
            ErrorCodes.AuthPasswordLoginDisabled, ErrorCodes.AuthEnrolmentPending,
            ErrorCodes.AuthEnrolmentRejected, ErrorCodes.AuthMachineRevoked, ErrorCodes.RateLimitedByDosGuard,
        ];
        Assert.All(expected, code => Assert.Contains(code, LoginRejectionMessages.All.Select(row => row.Code)));

        // AUT-011 spells BV-AUTH-006's message `account temporarily locked; try again in N
        // seconds`; Appendix B §2's normalisation rule is written for a trailing `(retry after
        // Ns)`. `Details.retry_after_secs` must be captured from either spelling, because the
        // first is the one the server sends and the fixture carries.
        foreach ((string message, int seconds) in new[]
        {
            ("account temporarily locked; try again in 842 seconds", 842),
            ("Account temporarily locked (retry after 300s).", 300),
        })
        {
            FakeTransport transport = new();
            transport.EnqueueResponse(200, body: Json(JsonSerializer.Serialize(new
            {
                auth = (object?)null,
                data = new { error = message },
            })));
            BastionVaultClient client = BuildClient(transport);

            BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
                () => client.Auth.Userpass.LoginAsync("alice", new SecretString(Password)));

            Assert.Equal(ErrorCodes.AuthAccountLocked, exception.Code);
            Assert.Equal(seconds, Assert.IsType<int>(exception.Details["retry_after_secs"]));
        }
    }

    // ---- AUT-032: a locked account is never retried. ----

    [Fact]
    [Requirement("AUT-032")]
    [Trait("Requirement", "AUT-032")]
    public async Task A_locked_account_is_never_auto_retried_even_when_the_retry_policy_is_told_to()
    {
        // Belt and braces, because AUT-032 is a MUST NOT. The retry policy is configured with five
        // attempts *and* BV-AUTH-006 explicitly in RetryOn — the most retry-happy configuration a
        // caller can build — and the login still costs exactly one request, because a 200 never
        // enters the retry loop's failure path at all.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"auth":null,"data":{"error":"account temporarily locked; try again in 30 seconds"}}"""));
        BastionVaultClient client = BuildClient(transport, options => options.RetryPolicy = new RetryPolicy
        {
            MaxAttempts = 5,
            RetryOn = [ErrorCodes.AuthAccountLocked],
        });

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.Userpass.LoginAsync("alice", new SecretString(Password)));

        Assert.Equal(ErrorCodes.AuthAccountLocked, exception.Code);
        Assert.False(exception.Retryable);
        _ = Assert.Single(transport.Requests);
        Assert.Equal(1, exception.Attempts);
    }

    // ---- AUT-012: the 400 arms, through the executor's ordinary non-2xx path. ----

    [Theory]
    [InlineData("invalid role_id", "BV-AUTH-010")]
    [InlineData("invalid secret_id", "BV-AUTH-010")]
    [InlineData("invalid secret id: not found", "BV-AUTH-010")]
    [InlineData("missing role_id", "BV-AUTH-010")]
    [InlineData("machine_token is required: AppID logins must present a FerroGate machine token", "BV-AUTH-011")]
    // The `machine ` row as *generated*: Appendix B §2 writes it
    // `prefix | machine_token / machine  (is not bound, is not approved)`, and the generator
    // compiles the parenthesised pair into a **conjunctive** `ContainsAll` guard, so a message must
    // carry both phrases. AUT-012 states no such condition — it says only "starts with
    // `machine_token` or `machine `" — so a real single-phrase message
    // (`machine m-17 is not bound to this role`) falls through to `BV-INPUT-100`. Reported as a
    // defect at handback, not patched here: the fix is in `tools/error-catalogue`'s reading of the
    // appendix and it would change the Rust and Python generated tables too, which Stage 1 freezes.
    [InlineData("machine m-17 is not bound and is not approved for this role", "BV-AUTH-011")]
    [Requirement("AUT-012")]
    [Trait("Requirement", "AUT-012")]
    public async Task An_AppId_login_400_maps_through_the_generated_table_to_BV_AUTH_010_or_011(string message, string code)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(400, body: Json(JsonSerializer.Serialize(new { error = message })));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.AppId.LoginAsync("r", new SecretString("s")));

        Assert.Equal(code, exception.Code);
        Assert.Equal(400, exception.StatusCode);
        Assert.False(exception.Retryable);
        Assert.Null(client.Auth.CurrentToken);
    }

    // ---- AUT-013: what a successful login records. ----

    [Fact]
    [Requirement("AUT-013")]
    [Trait("Requirement", "AUT-013")]
    public async Task A_successful_login_records_the_token_the_clock_time_the_lease_the_policies_and_the_metadata()
    {
        DateTimeOffset issued = DateTimeOffset.Parse("2026-09-13T12:00:00Z", CultureInfo.InvariantCulture);
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(JsonSerializer.Serialize(new
        {
            renewable = false,
            lease_id = string.Empty,
            lease_duration = 0,
            auth = new
            {
                client_token = FakeTokens.Child,
                policies = new[] { "default", "app-read" },
                metadata = new Dictionary<string, string> { ["username"] = "alice" },
                lease_duration = 2764800,
                renewable = true,
            },
            data = new { },
        })));
        BastionVaultClient client = BuildClient(transport, options => options.Clock = new MutableClock(issued));

        AuthInfo auth = await client.Auth.Userpass.LoginAsync("alice", new SecretString(Password));

        Assert.Equal(FakeTokens.Child, auth.ClientToken.Reveal());
        // IssuedAt comes from the injected clock (D-M1b-7), which is what makes AUT-090's future
        // schedule and AUT-003's token age drivable deterministically.
        Assert.Equal(issued, auth.IssuedAt);
        Assert.Equal(TimeSpan.FromSeconds(2764800), auth.LeaseDuration);
        Assert.True(auth.Renewable);
        Assert.Equal(["default", "app-read"], auth.Policies);
        Assert.Equal("alice", auth.Metadata!["username"]);
        // And the client now holds it (AUT-001: as a Static source, AUT-004: readable, redacting).
        Assert.Equal(FakeTokens.Child, client.Auth.CurrentToken!.Reveal());
        Assert.Equal("[REDACTED]", client.Auth.CurrentToken!.ToString());
        Assert.Equal(TokenSourceKind.Static, client.Auth.TokenSource.Kind);
    }

    // ---- AUT-002: lazy by default, eager on request. ----

    [Fact]
    [Requirement("AUT-002")]
    [Trait("Requirement", "AUT-002")]
    public async Task A_Login_source_logs_in_lazily_on_the_first_authenticated_request()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(LoginBody(FakeTokens.Child)));
        transport.EnqueueResponse(200, body: Json("""{"data":{"a":1}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"a":1}}"""));
        BastionVaultClient client = BuildClient(transport, options => options.TokenSource = UserpassSource());

        // D-M2-9's documented answer: constructing the client performs no login, and neither does
        // reading AUT-004's observables.
        Assert.Empty(transport.Requests);
        Assert.Null(client.Auth.CurrentToken);
        Assert.Equal(TokenSourceKind.Login, client.Auth.TokenSource.Kind);

        _ = await client.Logical.ReadAsync("secret/data/x");

        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal("/v1/auth/userpass/login/alice", transport.Requests[0].Uri.AbsolutePath);
        Assert.Equal(FakeTokens.Child, transport.Requests[1].Headers["X-BastionVault-Token"]);

        // Cached: a second request re-uses the token rather than logging in again.
        _ = await client.Logical.ReadAsync("secret/data/x");
        Assert.Equal(3, transport.Requests.Count);
    }

    [Fact]
    [Requirement("AUT-002")]
    [Trait("Requirement", "AUT-002")]
    public async Task Authenticate_forces_the_login_eagerly_and_refuses_a_source_that_has_none()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(LoginBody(FakeTokens.Child)));
        BastionVaultClient client = BuildClient(transport, options => options.TokenSource = UserpassSource());

        AuthInfo auth = await client.Auth.AuthenticateAsync();

        _ = Assert.Single(transport.Requests);
        Assert.Equal(FakeTokens.Child, auth.ClientToken.Reveal());
        Assert.Equal(FakeTokens.Child, client.Auth.CurrentToken!.Reveal());
        // The source survives its own login: replacing it with a Static one would discard the
        // credentials AUT-003's re-login needs.
        Assert.Equal(TokenSourceKind.Login, client.Auth.TokenSource.Kind);

        // A Static or Callback source has no login to force. Refused client-side rather than
        // answered with a fabricated AuthInfo (D-M1c-25).
        BastionVaultClient staticClient = BuildClient(new FakeTransport(), options => options.Token = FakeTokens.Client);
        BastionVaultException refused = await Assert.ThrowsAsync<BastionVaultException>(
            () => staticClient.Auth.AuthenticateAsync());
        Assert.Equal(ErrorCodes.InputInvalidArgument, refused.Code);
        Assert.Equal("TokenSource", refused.Details["argument"]);
    }

    [Fact]
    [Requirement("AUT-002")]
    [Trait("Requirement", "AUT-002")]
    public async Task A_Login_source_that_was_never_installed_on_a_client_reports_BV_AUTH_001_rather_than_no_token()
    {
        // `TokenSource.Login` is declarative: the client binds the performer. An instance the
        // application resolves itself has nothing to log in through, and answering "no token"
        // would look like CFG-020's case while actually being a misuse.
        TokenSource unbound = UserpassSource();

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => unbound.ResolveAsync());

        Assert.Equal(ErrorCodes.AuthNoToken, exception.Code);
        Assert.Contains("not installed on a client", (string)exception.Details["reason"]!, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("AUT-002")]
    [Trait("Requirement", "AUT-002")]
    public void The_login_method_and_the_credentials_must_agree()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => TokenSource.Login(
            AuthMethod.AppId,
            LoginCredentials.ForUserpass("alice", new SecretString(Password))));

        Assert.Equal("method", exception.ParamName);
    }

    // ---- AUT-003: re-login and replay. ----

    [Fact]
    [Requirement("AUT-003")]
    [Trait("Requirement", "AUT-003")]
    public async Task A_403_on_an_idempotent_request_re_logs_in_once_and_replays_as_one_logical_operation()
    {
        MutableClock clock = new(DateTimeOffset.UnixEpoch);
        List<RequestEvent> events = [];
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(LoginBody(FakeTokens.Child)));
        transport.EnqueueResponse(403, body: Json("""{"error":"Permission denied."}"""));
        transport.EnqueueResponse(200, body: Json(LoginBody(FakeTokens.Rotated)));
        transport.EnqueueResponse(200, body: Json("""{"data":{"a":1}}"""));
        BastionVaultClient client = BuildClient(transport, options =>
        {
            options.Clock = clock;
            options.Observer = new RecordingObserver(events);
            options.TokenSource = UserpassSource(new LoginOptions { ReloginOnPermissionDenied = true });
        });

        _ = await client.Auth.AuthenticateAsync();
        // The token must be older than MinReloginInterval (default 30s) for AUT-003 to fire.
        clock.Now += TimeSpan.FromMinutes(1);

        Response? response = await client.Logical.ReadAsync("secret/data/x");

        Assert.NotNull(response);
        Assert.Equal(4, transport.Requests.Count);
        Assert.Equal("/v1/auth/userpass/login/alice", transport.Requests[2].Uri.AbsolutePath);
        // The replay carries the *new* token, which is the whole point.
        Assert.Equal(FakeTokens.Rotated, transport.Requests[3].Headers["X-BastionVault-Token"]);

        // D-M2-9's accounting: one caller-visible operation keeps one requestId across both
        // passes, and the reported attempt count accumulates rather than restarting at 1. The
        // interposed login is a different request to a different path and gets its own id.
        RequestEvent[] reads = events.Where(e => e.Path == "secret/data/x").ToArray();
        Assert.Equal(2, reads.Length);
        Assert.Equal(reads[0].RequestId, reads[1].RequestId);
        Assert.Equal([1, 2], reads.Select(e => e.Attempt));
        Assert.DoesNotContain(events.Where(e => e.Path != "secret/data/x"), e => e.RequestId == reads[0].RequestId);
    }

    [Fact]
    [Requirement("AUT-003")]
    [Trait("Requirement", "AUT-003")]
    public async Task Relogin_is_opt_in_and_is_refused_for_a_fresh_token_or_a_non_idempotent_request()
    {
        // Three negative arms, each one of AUT-003's own conditions.
        // 1. The opt-in is off by default.
        await AssertNoReplay(
            new LoginOptions(),
            advance: TimeSpan.FromMinutes(1),
            read: client => client.Logical.ReadAsync("secret/data/x"));

        // 2. The token is younger than MinReloginInterval: a 403 that soon is a policy denial, not
        //    a stale credential, and re-logging-in would turn one clear failure into a loop.
        await AssertNoReplay(
            new LoginOptions { ReloginOnPermissionDenied = true },
            advance: TimeSpan.FromSeconds(5),
            read: client => client.Logical.ReadAsync("secret/data/x"));

        // 3. The request was not idempotent. AUT-003 means GET/LIST/HEAD only (D-M2-9): replaying a
        //    write after a permission change is the exact case the opt-in warns about.
        await AssertNoReplay(
            new LoginOptions { ReloginOnPermissionDenied = true },
            advance: TimeSpan.FromMinutes(1),
            read: client => client.Logical.WriteAsync("secret/data/x", JsonDocument.Parse("{}").RootElement));
    }

    [Fact]
    [Requirement("AUT-003")]
    [Trait("Requirement", "AUT-003")]
    public async Task A_relogin_that_itself_fails_still_counts_against_MinReloginInterval()
    {
        // The interval is measured from the last re-login *started*, not only from the token's
        // issue time. Without that, a re-login whose own login fails leaves the token's issue time
        // as the reference, so every subsequent 403 starts another login — a 403 storm becomes a
        // login storm against an unauthenticated endpoint.
        MutableClock clock = new(DateTimeOffset.UnixEpoch);
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(LoginBody(FakeTokens.Child)));   // the first login
        transport.EnqueueResponse(403, body: Json("""{"error":"Permission denied."}"""));
        transport.EnqueueResponse(500, body: Json("""{"error":"Internal error."}"""));   // the re-login fails
        transport.EnqueueResponse(403, body: Json("""{"error":"Permission denied."}"""));
        BastionVaultClient client = BuildClient(transport, options =>
        {
            options.Clock = clock;
            options.TokenSource = UserpassSource(new LoginOptions { ReloginOnPermissionDenied = true });
        });

        _ = await client.Auth.AuthenticateAsync();
        clock.Now += TimeSpan.FromMinutes(1);

        // The first 403 wins the gate, re-logs in, and that login fails: the failure is the
        // source's own, so it reaches the caller with its own code rather than as BV-AUTH-017.
        BastionVaultException first = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Logical.ReadAsync("secret/data/x"));
        Assert.Equal(ErrorCodes.ServerInternalError, first.Code);
        Assert.Equal(3, transport.Requests.Count);

        // The second 403 arrives 10 seconds later — inside MinReloginInterval measured from the
        // re-login that was started — so it does not start another one.
        clock.Now += TimeSpan.FromSeconds(10);
        BastionVaultException second = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Logical.ReadAsync("secret/data/x"));
        Assert.Equal(ErrorCodes.AuthzPermissionDenied, second.Code);
        Assert.Equal(4, transport.Requests.Count);
    }

    [Fact]
    [Requirement("RES-001")]
    [Requirement("AUT-003")]
    [Trait("Requirement", "RES-001")]
    public async Task The_relogin_replay_runs_inside_what_is_left_of_the_RES_001_cap()
    {
        // R-18, ruled Option A and implemented as D-M6-21: the standing adverse shape for the
        // relogin replay, and the sibling of `FailoverUnitTests`'
        // `Retries_spent_before_the_node_failure_still_leave_the_total_inside_the_cap` (D-M5-28).
        // The first pass does not answer 403 on attempt 1: two 502s are retried under CFG-050
        // first, so the pass has spent its whole budget before the 403 arrives. Unbounded — which
        // is what D-M2-9 originally specified — the replay would get a fresh MaxAttempts and the
        // caller would see six non-login wire attempts against a cap of four. That was measured at
        // 6 before the clamp; the assertion below is the cap, never the observed breach.
        //
        // A literal-mode client on purpose: no discovery means no failover, so nothing but the
        // relogin replay can be producing the extra attempts.
        MutableClock clock = new(DateTimeOffset.UnixEpoch);
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(LoginBody(FakeTokens.Child)));          // AUT-002's login
        transport.EnqueueResponse(502, body: Json("""{"error":"Bad gateway."}"""));       // pass 1, attempt 1
        transport.EnqueueResponse(502, body: Json("""{"error":"Bad gateway."}"""));       // pass 1, attempt 2
        transport.EnqueueResponse(403, body: Json("""{"error":"Permission denied."}""")); // pass 1, attempt 3
        transport.EnqueueResponse(200, body: Json(LoginBody(FakeTokens.Rotated)));        // AUT-003's re-login
        transport.EnqueueResponse(502, body: Json("""{"error":"Bad gateway."}"""));       // the replay's one attempt
        // Scripted but unreachable: a fifth non-login attempt would be the breach, and it must fail
        // as an assertion below rather than as "FakeTransport has no scripted response left".
        transport.EnqueueResponse(502, body: Json("""{"error":"Bad gateway."}"""));
        transport.EnqueueResponse(502, body: Json("""{"error":"Bad gateway."}"""));

        BastionVaultClient client = BuildClient(transport, options =>
        {
            options.Clock = clock;
            options.RetryPolicy = new RetryPolicy { MaxAttempts = 3, InitialBackoff = TimeSpan.Zero };
            options.TokenSource = UserpassSource(new LoginOptions
            {
                ReloginOnPermissionDenied = true,
                MinReloginInterval = TimeSpan.Zero,
            });
        });

        _ = await client.Auth.AuthenticateAsync();
        clock.Now += TimeSpan.FromMinutes(1);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Logical.ReadAsync("secret/data/x"));

        // RES-001 on both surfaces a caller can see: the wire, and Error.Attempts.
        const int cap = 3 + 1;
        int wireAttempts = transport.Requests.Count(request => request.Uri.AbsolutePath == "/v1/secret/data/x");
        Assert.Equal(cap, wireAttempts);
        Assert.Equal(cap, failure.Attempts);
        // The replay still happened, and still carried the new token: the clamp bounds AUT-003's
        // replay, it does not remove it. Math.Max(1, …) is what guarantees the one attempt.
        Assert.Equal(2, transport.Requests.Count(request => request.Uri.AbsolutePath == "/v1/auth/userpass/login/alice"));
        Assert.Equal(FakeTokens.Rotated, transport.Requests[5].Headers["X-BastionVault-Token"]);
        Assert.Equal(ErrorCodes.ServerUnavailable, failure.Code);
    }

    [Fact]
    [Requirement("AUT-003")]
    [Trait("Requirement", "AUT-003")]
    public async Task A_403_from_the_login_itself_does_not_trigger_a_relogin()
    {
        // AUT-041's gated AppID login answers 403 too. The replay keys on the *outer* request's
        // BV-AUTHZ-001; keying on a code alone would make the login's own refusal re-trigger the
        // login. This is what D-M2-25 item 2's origin marker buys.
        MutableClock clock = new(DateTimeOffset.UnixEpoch);
        FakeTransport transport = new();
        transport.EnqueueResponse(403, body: Json("""{"error":"Permission denied."}"""));
        BastionVaultClient client = BuildClient(transport, options =>
        {
            options.Clock = clock;
            options.TokenSource = TokenSource.Login(
                AuthMethod.AppId,
                LoginCredentials.ForAppId("11111111", new SecretString("22222222")),
                new LoginOptions { ReloginOnPermissionDenied = true });
        });

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Logical.ReadAsync("secret/data/x"));

        Assert.Equal(ErrorCodes.AuthzPermissionDenied, exception.Code);
        _ = Assert.Single(transport.Requests);
        Assert.Equal("/v1/auth/approle/login", transport.Requests[0].Uri.AbsolutePath);
    }

    // ---- D-M2-25 item 2: the BV-AUTH-017 guard, both ways. ----

    [Fact]
    [Requirement("AUT-010")]
    [Trait("Requirement", "AUT-010")]
    public async Task A_recognised_login_failure_inside_a_Login_source_reaches_the_caller_with_its_own_code()
    {
        // The companion to AuthReviewFindingTests' wrapping test: the login-response contract's own
        // verdict is marked at its origin and is *not* replaced by BV-AUTH-017.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"auth":null,"data":{"error":"invalid username or password"}}"""));
        BastionVaultClient client = BuildClient(transport, options => options.TokenSource = UserpassSource());

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Logical.ReadAsync("secret/data/x"));

        Assert.Equal(ErrorCodes.AuthInvalidCredentials, exception.Code);
        Assert.NotEqual(ErrorCodes.AuthTokenSourceFailed, exception.Code);
        // The login's own path, because the login is what failed — and only one request was made.
        Assert.Equal("auth/userpass/login/alice", exception.Path);
        _ = Assert.Single(transport.Requests);
    }

    [Fact]
    [Requirement("AUT-010")]
    [Trait("Requirement", "AUT-010")]
    public async Task A_cancelled_resolution_still_maps_to_BV_TRANSPORT_005_and_never_to_BV_AUTH_017()
    {
        // D-M2-18 item 1: the catch order in RunLoopAsync is load-bearing, and narrowing the
        // BV-AUTH-017 filter to the origin marker must not have disturbed it. Inverted, a
        // cancellation maps to BV-AUTH-017 instead of BV-TRANSPORT-005.
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        BastionVaultClient client = BuildClient(new FakeTransport(), options =>
            options.TokenSource = TokenSource.Callback(token => Task.FromCanceled<SecretString>(token)));

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Logical.ReadAsync("secret/data/x", cancellationToken: cancellation.Token));

        Assert.Equal(ErrorCodes.TransportCancelled, exception.Code);
        Assert.Equal(0, exception.Attempts);
    }

    // ---- AUT-040, AUT-041: the AppID request shape. ----

    [Fact]
    [Requirement("AUT-040")]
    [Trait("Requirement", "AUT-040")]
    public async Task AppId_login_sends_role_id_and_secret_id_and_omits_machine_token_when_absent()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(LoginBody(FakeTokens.Child)));
        transport.EnqueueResponse(200, body: Json(LoginBody(FakeTokens.Child)));
        transport.EnqueueResponse(200, body: Json(LoginBody(FakeTokens.Child)));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Auth.AppId.LoginAsync("role-1", new SecretString("secret-1"));
        _ = await client.Auth.AppId.LoginAsync("role-1", new SecretString("secret-1"), new SecretString(FakeTokens.Machine));
        _ = await client.Auth.AppId.LoginAsync("role-1", mount: "machines");

        Assert.Equal("/v1/auth/approle/login", transport.Requests[0].Uri.AbsolutePath);
        Assert.Equal("""{"role_id":"role-1","secret_id":"secret-1"}""", BodyOf(transport.Requests[0]));
        Assert.Equal(
            $$"""{"role_id":"role-1","secret_id":"secret-1","machine_token":"{{FakeTokens.Machine}}"}""",
            BodyOf(transport.Requests[1]));
        // A bind-secret-id=false role logs in with the role id alone; `secret_id` is omitted, not
        // sent empty (OVR-007).
        Assert.Equal("""{"role_id":"role-1"}""", BodyOf(transport.Requests[2]));
        Assert.Equal("/v1/auth/machines/login", transport.Requests[2].Uri.AbsolutePath);
    }

    [Fact]
    [Requirement("AUT-041")]
    [Trait("Requirement", "AUT-041")]
    public async Task A_namespace_scoped_AppId_login_carries_the_namespace_header_and_a_403_hint_names_the_setting()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(LoginBody(FakeTokens.Child)));
        BastionVaultClient scoped = BuildClient(transport, options => options.Namespace = "dti/esi");

        _ = await scoped.Auth.AppId.LoginAsync("role-1", new SecretString("secret-1"));
        Assert.Equal("dti/esi", transport.Requests[0].Headers["X-BastionVault-Namespace"]);

        // A view's namespace reaches the login too, because the login is built by the same header
        // builder every request uses (CFG-071).
        FakeTransport viewTransport = new();
        viewTransport.EnqueueResponse(200, body: Json(LoginBody(FakeTokens.Child)));
        BastionVaultClient root = BuildClient(viewTransport);
        _ = await root.WithNamespace("child").Auth.AppId.LoginAsync("role-1", new SecretString("secret-1"));
        Assert.Equal("child", viewTransport.Requests[0].Headers["X-BastionVault-Namespace"]);

        // And with no namespace set, a 403 on this path is enriched with the ERR-040 note naming
        // the setting — the hint a namespace-scoped role's operator needs.
        FakeTransport gated = new();
        gated.EnqueueResponse(403, body: Json("""{"error":"Permission denied."}"""));
        BastionVaultClient client = BuildClient(gated);
        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.AppId.LoginAsync("role-1", new SecretString("secret-1")));

        Assert.Equal(ErrorCodes.AuthzPermissionDenied, exception.Code);
        Assert.Contains("No namespace is set", exception.Hint, StringComparison.Ordinal);
        Assert.Contains("`Namespace`", exception.Hint, StringComparison.Ordinal);
    }

    // ---- AUT-042: role-id read and secret-id generation. ----

    [Fact]
    [Requirement("AUT-042")]
    [Trait("Requirement", "AUT-042")]
    public async Task ReadRoleId_and_GenerateSecretId_use_the_appendix_A_paths_and_the_AUT_042_field_names()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"role_id":"11111111-1111-1111-1111-111111111111"}}"""));
        transport.EnqueueResponse(200, body: Json(JsonSerializer.Serialize(new
        {
            data = new
            {
                secret_id = "22222222-2222-2222-2222-222222222222",
                secret_id_accessor = "acc-1",
                secret_id_ttl = 600,
                secret_id_num_uses = 3,
                environments = new[] { "prod-*", "staging" },
            },
        })));
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        string roleId = await client.Auth.AppId.ReadRoleIdAsync("svc");
        SecretIdInfo info = await client.Auth.AppId.GenerateSecretIdAsync("svc", new SecretIdOptions
        {
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["team"] = "esi" },
            CidrList = ["10.0.0.0/8"],
            TokenBoundCidrs = ["10.1.0.0/16"],
            NumUses = 3,
            Ttl = TimeSpan.FromMinutes(10),
            Environments = ["prod-*"],
        });

        Assert.Equal("11111111-1111-1111-1111-111111111111", roleId);
        Assert.Equal("GET", transport.Requests[0].Method);
        Assert.Equal("/v1/auth/approle/role/svc/role-id", transport.Requests[0].Uri.AbsolutePath);

        Assert.Equal("POST", transport.Requests[1].Method);
        Assert.Equal("/v1/auth/approle/role/svc/secret-id", transport.Requests[1].Uri.AbsolutePath);
        // AUT-042: `metadata` is a map in the API and a JSON *string* on the wire.
        Assert.Equal(
            """{"metadata":"{\"team\":\"esi\"}","cidr_list":["10.0.0.0/8"],"token_bound_cidrs":["10.1.0.0/16"],"num_uses":3,"ttl":600,"environments":["prod-*"]}""",
            BodyOf(transport.Requests[1]));

        Assert.Equal("22222222-2222-2222-2222-222222222222", info.SecretId.Reveal());
        Assert.Equal("acc-1", info.SecretIdAccessor);
        Assert.Equal(TimeSpan.FromSeconds(600), info.SecretIdTtl);
        Assert.Equal(3, info.SecretIdNumUses);
        Assert.Equal(["prod-*", "staging"], info.Environments);
    }

    [Fact]
    [Requirement("AUT-042")]
    [Trait("Requirement", "AUT-042")]
    public async Task An_omitted_or_partial_SecretIdOptions_sends_only_what_the_caller_set()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"secret_id":"s-1"}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"secret_id":"s-2"}}"""));
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        SecretIdInfo defaults = await client.Auth.AppId.GenerateSecretIdAsync("svc");
        // OVR-007: no options at all means no body at all, so the role's own defaults apply.
        Assert.Equal("s-1", defaults.SecretId.Reveal());
        Assert.Null(defaults.SecretIdAccessor);
        Assert.Null(defaults.SecretIdTtl);
        Assert.Null(defaults.SecretIdNumUses);
        Assert.Empty(defaults.Environments);
        Assert.Equal(0, transport.Requests[0].Body.Length);

        // And every field the caller left unset is omitted individually, including an empty list —
        // which is not the same request as "no list", and would otherwise clear the role's.
        _ = await client.Auth.AppId.GenerateSecretIdAsync("svc", new SecretIdOptions
        {
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["team"] = "esi" },
            CidrList = [],
        });
        Assert.Equal("""{"metadata":"{\"team\":\"esi\"}"}""", BodyOf(transport.Requests[1]));
    }

    [Fact]
    [Requirement("AUT-042")]
    [Trait("Requirement", "AUT-042")]
    public async Task A_response_missing_the_field_the_operation_returns_is_BV_PROTOCOL_002()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{}}"""));
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(200, body: Json("""{"data":{"role_id":42}}"""));
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        // A response that does not match the documented envelope is BV-PROTOCOL-002 — not a null
        // the caller dereferences, and not a fabricated empty result (D-M1c-25). Four shapes: the
        // field absent, the field absent on the other operation, no envelope at all (204), and the
        // field present with the wrong JSON type.
        foreach (Func<Task> call in new Func<Task>[]
        {
            () => client.Auth.AppId.GenerateSecretIdAsync("svc"),
            () => client.Auth.AppId.ReadRoleIdAsync("svc"),
            () => client.Auth.AppId.ReadRoleIdAsync("svc"),
            () => client.Auth.AppId.ReadRoleIdAsync("svc"),
        })
        {
            BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(call);
            Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
        }
    }

    // ---- AUT-044: the derived environment scope. ----

    [Theory]
    [InlineData(null, null, null, false, 0, 0)]
    [InlineData("true", "prod-*,staging", "*", true, 2, 1)]
    [InlineData("false", "prod-*", null, false, 1, 0)]
    [InlineData("1", " a , b ", "", true, 2, 0)]
    [InlineData("yes", null, null, true, 0, 0)]
    [InlineData("on", null, null, true, 0, 0)]
    [InlineData("nonsense", null, null, false, 0, 0)]
    [Requirement("AUT-044")]
    [Trait("Requirement", "AUT-044")]
    public async Task EnvironmentScope_is_derived_from_the_approle_env_metadata_keys(
        string? scoped, string? secret, string? machine, bool expectScoped, int secretGlobs, int machineGlobs)
    {
        Dictionary<string, string> metadata = new(StringComparer.Ordinal) { ["role_name"] = "svc" };
        if (scoped is not null)
        {
            metadata["approle_env_scoped"] = scoped;
        }

        if (secret is not null)
        {
            metadata["approle_env_secret"] = secret;
        }

        if (machine is not null)
        {
            metadata["approle_env_machine"] = machine;
        }

        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(JsonSerializer.Serialize(new
        {
            auth = new
            {
                client_token = FakeTokens.Child,
                policies = new[] { "default" },
                metadata,
                lease_duration = 1200,
                renewable = true,
            },
            data = new { },
        })));
        BastionVaultClient client = BuildClient(transport);

        AuthInfo auth = await client.Auth.AppId.LoginAsync("role-1", new SecretString("secret-1"));

        Assert.Equal(expectScoped, auth.EnvironmentScope.Scoped);
        Assert.Equal(secretGlobs, auth.EnvironmentScope.SecretGlobs.Count);
        Assert.Equal(machineGlobs, auth.EnvironmentScope.MachineGlobs.Count);
        if (secretGlobs == 2 && secret == " a , b ")
        {
            // Whitespace around a glob is not part of it.
            Assert.Equal(["a", "b"], auth.EnvironmentScope.SecretGlobs);
        }
    }

    [Fact]
    [Requirement("AUT-044")]
    [Trait("Requirement", "AUT-044")]
    public async Task A_login_with_no_metadata_at_all_is_unscoped_rather_than_null()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(JsonSerializer.Serialize(new
        {
            auth = new { client_token = FakeTokens.Child },
            data = new { },
        })));
        BastionVaultClient client = BuildClient(transport);

        AuthInfo auth = await client.Auth.AppId.LoginAsync("role-1");

        Assert.Null(auth.Metadata);
        Assert.False(auth.EnvironmentScope.Scoped);
        Assert.Empty(auth.EnvironmentScope.SecretGlobs);
        Assert.Empty(auth.EnvironmentScope.MachineGlobs);
    }

    // ---- AUT-100, AUT-031, CNF-031, CNF-032: credential handling. ----

    [Fact]
    [Requirement("AUT-100")]
    [Requirement("AUT-031")]
    [Trait("Requirement", "AUT-100")]
    public async Task A_one_shot_login_retains_no_credentials_and_a_Login_source_retains_them_in_redacting_types()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(LoginBody(FakeTokens.Child)));
        BastionVaultClient oneShot = BuildClient(transport);

        _ = await oneShot.Auth.Userpass.LoginAsync("alice", new SecretString(Password));

        // AUT-100's default arm: the source that results is Static, which by construction holds a
        // token and no credentials. There is no reachable path from the client back to the
        // password, which is what "MUST NOT be retained" means in a garbage-collected runtime.
        Assert.Equal(TokenSourceKind.Static, oneShot.Auth.TokenSource.Kind);
        Assert.Null(oneShot.Auth.TokenSource.Descriptor);

        // AUT-100's exception: a Login source keeps them, because AUT-003's re-login needs them —
        // and keeps them in redacting types (AUT-031, CNF-031, CNF-032).
        FakeTransport sourceTransport = new();
        sourceTransport.EnqueueResponse(200, body: Json(LoginBody(FakeTokens.Child)));
        BastionVaultClient sourced = BuildClient(sourceTransport, options => options.TokenSource = UserpassSource());
        _ = await sourced.Auth.AuthenticateAsync();

        LoginCredentials retained = sourced.Auth.TokenSource.Descriptor!.Credentials;
        Assert.Equal(Password, retained.Password!.Reveal());
        Assert.Equal("[REDACTED]", retained.Password!.ToString());
        Assert.Equal("[REDACTED]", retained.ToString());
    }

    [Fact]
    [Requirement("CNF-032")]
    [Requirement("CNF-031")]
    [Trait("Requirement", "CNF-032")]
    public async Task Every_type_holding_secret_material_redacts_its_default_string_representation()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"secret_id":"22222222-2222-2222-2222-222222222222"}}"""));
        transport.EnqueueResponse(200, body: Json(LoginBody(FakeTokens.Child)));
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        SecretIdInfo info = await client.Auth.AppId.GenerateSecretIdAsync("svc");
        AuthInfo auth = await client.Auth.Userpass.LoginAsync("alice", new SecretString(Password));

        // D-M2-3's marker, on every M2b type that can hold credential material.
        Assert.Equal("[REDACTED]", info.SecretId.ToString());
        Assert.Equal("[REDACTED]", auth.ClientToken.ToString());
        Assert.Equal("[REDACTED]", LoginCredentials.ForAppId("r", new SecretString("s"), new SecretString("m")).ToString());
        Assert.Equal("[REDACTED]", new SecretString(Password).ToString());
    }

    // ---- AUT-101, TST-051: nothing about the auth object is logged or observed. ----

    [Fact]
    [Requirement("AUT-101")]
    [Requirement("CNF-031")]
    [Trait("Requirement", "AUT-101")]
    public async Task No_login_surfaces_the_auth_object_to_the_logger_or_the_CFG_080_observer()
    {
        // AUT-101 is a *restriction* on the observability hook (D-M2-25 item 1): the SDK must not
        // log the `auth` object, and the hook receives only `policies.length`, `lease_duration` and
        // `renewable`. RequestEvent carries none of the three, so the upper bound holds by
        // construction; what has to be asserted is that nothing else leaks either.
        CapturingClientLogger logger = new();
        CapturingRequestObserver observer = new();
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(JsonSerializer.Serialize(new
        {
            auth = new
            {
                client_token = FakeTokens.Child,
                policies = new[] { "default", "app-read" },
                metadata = new Dictionary<string, string> { ["username"] = "alice", ["approle_env_secret"] = "prod-*" },
                lease_duration = 1200,
                renewable = true,
            },
            data = new { },
        })));
        transport.EnqueueResponse(200, body: Json(LoginBody(FakeTokens.Rotated)));
        BastionVaultClient client = BuildClient(transport, options =>
        {
            options.Logger = logger;
            options.Observer = observer;
        });

        _ = await client.Auth.Userpass.LoginAsync("alice", new SecretString(Password), "123456");
        _ = await client.Auth.AppId.LoginAsync("role-1", new SecretString("22222222-2222-2222-2222-222222222222"), new SecretString(FakeTokens.Machine));

        string[] surfaced = logger.Lines
            .Concat(observer.Events.Select(captured => captured.ToString()))
            .ToArray();
        Assert.NotEmpty(observer.Events);

        foreach (string forbidden in new[]
        {
            FakeTokens.Child, FakeTokens.Rotated, Password, "123456",
            "22222222-2222-2222-2222-222222222222", FakeTokens.Machine,
            // Not secret in the strictest sense, but the `auth` object is what AUT-101 forbids
            // logging, and its policy names and metadata are the parts of it that identify a
            // principal.
            "app-read", "approle_env_secret", "prod-*",
        })
        {
            Assert.All(surfaced, line => Assert.DoesNotContain(forbidden, line, StringComparison.Ordinal));
        }

        // The login path itself is observed (CFG-080 fires on every attempt, RES-002), which is
        // what makes the assertion above non-vacuous.
        Assert.Contains(observer.Events, captured => captured.Path == "auth/userpass/login/alice");
        Assert.Contains(observer.Events, captured => captured.Path == "auth/approle/login");
    }

    [Fact]
    [Requirement("AUT-101")]
    [Trait("Requirement", "AUT-101")]
    public async Task A_rejected_login_surfaces_the_reason_and_never_the_credential()
    {
        CapturingClientLogger logger = new();
        CapturingRequestObserver observer = new();
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"auth":null,"data":{"error":"invalid username or password"}}"""));
        BastionVaultClient client = BuildClient(transport, options =>
        {
            options.Logger = logger;
            options.Observer = observer;
        });

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.Userpass.LoginAsync("alice", new SecretString(Password), "123456"));

        // ERR-002's one-line form is the widest surface an application prints, so it is the one
        // searched: the reason is there, the credential is not.
        string rendered = exception.ToString();
        Assert.Contains("invalid username or password", rendered, StringComparison.Ordinal);
        foreach (string line in logger.Lines.Append(rendered).Concat(observer.Events.Select(e => e.ToString())))
        {
            Assert.DoesNotContain(Password, line, StringComparison.Ordinal);
            Assert.DoesNotContain("123456", line, StringComparison.Ordinal);
        }
    }

    // ---- CFG-020, ERR-022: the client-side refusal and its exemptions. ----

    [Theory]
    [InlineData("sys/health")]
    [InlineData("sys/seal-status")]
    [InlineData("sys/init")]
    [InlineData("sys/unseal")]
    [InlineData("sys/info")]
    [InlineData("auth/ferrogate/requirement")]
    [InlineData("auth/ferrogate/enroll")]
    [InlineData("auth/userpass/login/alice")]
    [InlineData("auth/approle/login")]
    [Requirement("CFG-020")]
    [Trait("Requirement", "CFG-020")]
    public async Task Every_unauthenticated_endpoint_works_without_a_token(string path)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"a":1}}"""));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Logical.ReadAsync(path);

        _ = Assert.Single(transport.Requests);
        Assert.DoesNotContain("X-BastionVault-Token", transport.Requests[0].Headers.Keys);
    }

    [Theory]
    [InlineData("secret/data/x")]
    [InlineData("auth/token/lookup-self")]
    [InlineData("sys/policies/acl/admin")]
    [InlineData("sys/health-check")]
    [InlineData("sys/health/extra")]
    [InlineData("auth/userpass/login/alice/extra")]
    [InlineData("auth/ferrogate/status")]
    [Requirement("CFG-020")]
    [Requirement("ERR-022")]
    [Trait("Requirement", "CFG-020")]
    public async Task An_authenticated_operation_with_no_token_is_refused_before_any_network_call(string path)
    {
        // The last four cases are the ones a substring test would get wrong: `sys/health-check`
        // and `sys/health/extra` are not `sys/health`, `auth/userpass/login/alice/extra` is not a
        // login path, and `auth/ferrogate/status` is not on CFG-020's list even though its two
        // siblings are.
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Logical.ReadAsync(path));

        Assert.Equal(ErrorCodes.AuthNoToken, exception.Code);
        // ERR-022: the server answers a missing token three different ways — 400 on logical paths,
        // 403 on inline sys handlers, 401 inside batch results. None of them is visible here,
        // because no request was sent: no status, no attempts.
        Assert.Null(exception.StatusCode);
        Assert.Equal(0, exception.Attempts);
        Assert.False(exception.Retryable);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    [Requirement("ERR-022")]
    [Trait("Requirement", "ERR-022")]
    public async Task The_preflight_runs_after_resolution_and_a_per_call_token_or_a_login_satisfies_it()
    {
        // ERR-022's ruling: the preflight runs *after* token-source resolution, never before. "No
        // token" is a source that *resolved* to absent or empty — not a Login source that has not
        // resolved yet, which is why the lazy login below is not refused.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(LoginBody(FakeTokens.Child)));
        transport.EnqueueResponse(200, body: Json("""{"data":{"a":1}}"""));
        BastionVaultClient lazily = BuildClient(transport, options => options.TokenSource = UserpassSource());

        Assert.Null(lazily.Auth.CurrentToken);
        _ = await lazily.Logical.ReadAsync("secret/data/x");
        Assert.Equal(FakeTokens.Child, transport.Requests[1].Headers["X-BastionVault-Token"]);

        // A per-call token (CFG-060) is "the current token" for that call and satisfies it too.
        FakeTransport pinned = new();
        pinned.EnqueueResponse(200, body: Json("""{"data":{"a":1}}"""));
        BastionVaultClient client = BuildClient(pinned);
        _ = await client.Logical.ReadAsync("secret/data/x", new RequestOptions { Token = new SecretString(FakeTokens.Explicit) });
        Assert.Equal(FakeTokens.Explicit, pinned.Requests[0].Headers["X-BastionVault-Token"]);

        // And the escape hatch stays an escape hatch: ERR-022 scopes the refusal to typed-operation
        // callers, so `Logical.Raw` sends the request and the caller sees the server's own answer.
        FakeTransport raw = new();
        raw.EnqueueResponse(400, body: Json("""{"error":"missing client token"}"""));
        BastionVaultClient rawClient = BuildClient(raw);
        BastionVaultException fromServer = await Assert.ThrowsAsync<BastionVaultException>(
            () => rawClient.Logical.RawAsync("GET", "/v1/secret/data/x"));
        Assert.Equal(ErrorCodes.AuthNoToken, fromServer.Code);
        Assert.Equal(400, fromServer.StatusCode);
        _ = Assert.Single(raw.Requests);
    }

    // ---- helpers ----

    private static async Task AssertNoReplay(LoginOptions loginOptions, TimeSpan advance, Func<BastionVaultClient, Task> read)
    {
        MutableClock clock = new(DateTimeOffset.UnixEpoch);
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(LoginBody(FakeTokens.Child)));
        transport.EnqueueResponse(403, body: Json("""{"error":"Permission denied."}"""));
        BastionVaultClient client = BuildClient(transport, options =>
        {
            options.Clock = clock;
            options.TokenSource = UserpassSource(loginOptions);
        });

        _ = await client.Auth.AuthenticateAsync();
        clock.Now += advance;

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => read(client));

        Assert.Equal(ErrorCodes.AuthzPermissionDenied, exception.Code);
        // The login plus the one denied request, and nothing else: no second login, no replay.
        Assert.Equal(2, transport.Requests.Count);
    }

    private static TokenSource UserpassSource(LoginOptions? options = null)
    {
        return TokenSource.Login(
                AuthMethod.Userpass,
                LoginCredentials.ForUserpass("alice", new SecretString(Password)),
                options);
    }

    private static BastionVaultClient BuildClient(ITransport transport, Action<BastionVaultClientOptions>? configure = null)
    {
        BastionVaultClientOptions options = new()
        {
            Address = Address,
            Transport = transport,
            Clock = new MutableClock(DateTimeOffset.UnixEpoch),
            JitterSource = new ZeroJitterSource(),
            RateGate = new RateGate { RatePerSecond = 0 },
            RetryPolicy = new RetryPolicy { MaxAttempts = 1 },
        };
        configure?.Invoke(options);
        return new BastionVaultClient(options, EnvironmentSource.None);
    }

    private static ReadOnlyMemory<byte> Json(string json)
    {
        return Encoding.UTF8.GetBytes(json);
    }

    private static string BodyOf(TransportRequest request)
    {
        return Encoding.UTF8.GetString(request.Body.Span);
    }

    private static string LoginBody(string token)
    {
        return JsonSerializer.Serialize(new
        {
            renewable = false,
            lease_id = string.Empty,
            lease_duration = 0,
            auth = new
            {
                client_token = token,
                policies = new[] { "default" },
                metadata = new Dictionary<string, string>(StringComparer.Ordinal),
                lease_duration = 1200,
                renewable = true,
            },
            data = new { },
        });
    }

    /// <summary>
    /// An <see cref="IClock"/> a test can move. AUT-003's "older than <c>MinReloginInterval</c>" is
    /// the first M2 requirement whose subject is elapsed time between two operations rather than a
    /// single reading, so a frozen clock cannot drive it (D-M2-19 predicted this for M2c; AUT-003
    /// needs it one slice earlier).
    /// </summary>
    private sealed class MutableClock : IClock
    {
        public MutableClock(DateTimeOffset now) => Now = now;

        public DateTimeOffset Now { get; set; }

        public DateTimeOffset NowUtc()
        {
            return Now;
        }

        public Task Delay(TimeSpan duration, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class ZeroJitterSource : IJitterSource
    {
        public double NextDouble()
        {
            return 0.5;
        }
    }

    private sealed class RecordingObserver : IRequestObserver
    {
        private readonly List<RequestEvent> events;

        public RecordingObserver(List<RequestEvent> events) => this.events = events;

        public void OnRequestCompleted(RequestEvent requestEvent)
        {
            events.Add(requestEvent);
        }
    }
}
