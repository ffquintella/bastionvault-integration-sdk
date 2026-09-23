using System.Globalization;
using System.Text;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// AUT-090…AUT-094's renewal loop, driven against the real <see cref="BastionVaultClient"/> on a
/// virtual clock. The two <c>auth.autorenew.*</c> conformance fixtures pin the schedule itself;
/// these cover what a fixture cannot reach without a second wave of exchanges — AUT-092's backoff
/// and its three stop-immediately codes, AUT-093's single re-login, and AUT-094's cancellation.
/// </summary>
/// <remarks>
/// The clock here is a unit-test double, not <c>FixtureClock</c>: D-M2-27's virtual mode is a
/// <i>fixture</i> instrument, and a unit test that borrowed it would be asserting against the
/// harness rather than against the library.
/// </remarks>
public sealed class AutoRenewTests
{
    private const string Address = "https://vault.example.com:8200";

    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-09-13T12:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    [Requirement("AUT-090")]
    [Requirement("AUT-091")]
    [Trait("Requirement", "AUT-090")]
    public async Task Renewal_is_scheduled_at_the_fraction_of_the_lease_and_recomputed_from_each_answer()
    {
        VirtualClock clock = new(Start);
        ScriptedTransportDouble transport = new(
            Login(lease: 3600),
            Renew(lease: 1800),
            // A renewal that comes back non-renewable ends the loop without any cancellation, which
            // is what keeps this test free of a second concurrency mechanism.
            Renew(lease: 1800, renewable: false));

        Renewals renewals = await RunAsync(clock, transport, new AutoRenewPolicy { Enabled = true });

        // 3600 × 0.66 = 2376 s, then 1800 × 0.66 = 1188 s from the renewed credential's own issue
        // time — the recomputation AUT-091 requires, not a repeat of the first interval.
        Assert.Equal([TimeSpan.FromSeconds(2376), TimeSpan.FromSeconds(1188)], clock.Waits);
        Assert.Equal(2, renewals.Renewed.Count);
        Assert.Equal(RenewalStoppedReason.NotRenewable, renewals.Stopped);
    }

    [Fact]
    [Requirement("AUT-090")]
    [Trait("Requirement", "AUT-090")]
    public async Task Renewal_is_never_scheduled_sooner_than_MinInterval_after_the_previous_one()
    {
        VirtualClock clock = new(Start);
        ScriptedTransportDouble transport = new(
            Login(lease: 100),
            // 4 × 0.66 = 2.64 s, which is inside the 10 s floor.
            Renew(lease: 4),
            Renew(lease: 4, renewable: false));

        Renewals renewals = await RunAsync(clock, transport, new AutoRenewPolicy
        {
            Enabled = true,
            MinInterval = TimeSpan.FromSeconds(10),
        });

        Assert.Equal([TimeSpan.FromSeconds(66), TimeSpan.FromSeconds(10)], clock.Waits);
        Assert.Equal(2, renewals.Renewed.Count);
    }

    [Fact]
    [Requirement("AUT-092")]
    [Trait("Requirement", "AUT-092")]
    public async Task A_failed_renewal_backs_off_exponentially_from_one_second_with_no_jitter()
    {
        VirtualClock clock = new(Start);
        ScriptedTransportDouble transport = new(
            Login(lease: 3600),
            ServerError(),
            ServerError(),
            Renew(lease: 3600, renewable: false));

        Renewals renewals = await RunAsync(clock, transport, new AutoRenewPolicy { Enabled = true });

        // The schedule wait, then 1 s and 2 s. Doubling, and exactly those values: D-M2-27 item 6
        // rules that AUT-092's own backoff applies none of the transport path's jitter, so a jitter
        // source leaking in here would move both numbers.
        Assert.Equal(
            [TimeSpan.FromSeconds(2376), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)],
            clock.Waits);
        Assert.Equal(2, renewals.Failed.Count);
        _ = Assert.Single(renewals.Renewed);
    }

    [Fact]
    [Requirement("AUT-092")]
    [Trait("Requirement", "AUT-092")]
    public async Task Backoff_is_capped_at_a_quarter_of_the_remaining_ttl()
    {
        VirtualClock clock = new(Start);
        ScriptedTransportDouble transport = new(Login(lease: 10), ServerError(), Renew(lease: 10, renewable: false));

        Renewals renewals = await RunAsync(clock, transport, new AutoRenewPolicy
        {
            Enabled = true,
            RenewAtFraction = 0.9,
            MinInterval = TimeSpan.Zero,
        });

        // 10 s × 0.9 = 9 s, leaving 1 s of TTL. A quarter of that is 250 ms, which is below the 1 s
        // the backoff starts at — so the cap is what decides the wait, and an uncapped backoff
        // would show up here as a full second.
        Assert.Equal(TimeSpan.FromSeconds(9), clock.Waits[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(250), clock.Waits[1]);
        _ = Assert.Single(renewals.Failed);
    }

    [Theory]
    [InlineData("BV-AUTHZ-001", 403, "Permission denied.")]
    // AUT-085's shape: a 400 `Request is invalid.` on the renew path, which StatusCodeMapper
    // refines to BV-AUTH-015 rather than the generic BV-INPUT-100.
    [InlineData("BV-AUTH-015", 400, "Request is invalid.")]
    [InlineData("BV-SERVER-001", 503, "BastionVault is sealed")]
    [Requirement("AUT-092")]
    [Trait("Requirement", "AUT-092")]
    public async Task The_three_stop_immediately_codes_stop_the_loop_without_a_backoff(string code, int status, string message)
    {
        VirtualClock clock = new(Start);
        ScriptedTransportDouble transport = new(Login(lease: 3600), Failure(status, message));

        Renewals renewals = await RunAsync(clock, transport, new AutoRenewPolicy { Enabled = true });

        // One wait — the schedule — and no backoff wait after it. An extra wait here is exactly the
        // shape of "the loop treated this code as retryable".
        Assert.Equal([TimeSpan.FromSeconds(2376)], clock.Waits);
        Assert.Equal([code], renewals.Failed);
        Assert.Equal(RenewalStoppedReason.RenewalFailed, renewals.Stopped);
    }

    [Fact]
    [Requirement("AUT-092")]
    [Trait("Requirement", "AUT-092")]
    public async Task The_loop_stops_after_MaxConsecutiveFailures_and_reports_RenewalFailed()
    {
        VirtualClock clock = new(Start);
        ScriptedTransportDouble transport = new(Login(lease: 3600), ServerError(), ServerError(), ServerError());

        Renewals renewals = await RunAsync(clock, transport, new AutoRenewPolicy
        {
            Enabled = true,
            MaxConsecutiveFailures = 3,
        });

        Assert.Equal(3, renewals.Failed.Count);
        Assert.Equal(RenewalStoppedReason.RenewalFailed, renewals.Stopped);
        // Two backoffs, not three: the failure that reaches the limit stops rather than waiting.
        Assert.Equal([TimeSpan.FromSeconds(2376), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], clock.Waits);
    }

    [Fact]
    [Requirement("AUT-092")]
    [Trait("Requirement", "AUT-092")]
    public async Task Clearing_the_token_stops_the_loop_and_reports_TokenRevoked()
    {
        VirtualClock clock = new(Start);
        ScriptedTransportDouble transport = new(Login(lease: 3600));
        Renewals renewals = new();

        using BastionVaultClient client = Build(clock, transport, new AutoRenewPolicy { Enabled = true }, renewals);
        // Cleared *while the loop is inside its scheduled wait*, which is the case the
        // ClientContext.TokenCleared signal exists for: without it the loop would sleep out the
        // rest of a lease before noticing, and AUT-092 says "stop immediately".
        clock.BeforeWait = () => client.ClearToken();
        _ = await client.Auth.Userpass.LoginAsync("alice", new SecretString("password-fixture"));
        await client.RenewalCompletion!.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Empty(renewals.Renewed);
        // One request only: the login. The renewal the schedule was waiting for never went out.
        _ = Assert.Single(transport.Requests);
        Assert.Equal(RenewalStoppedReason.TokenRevoked, renewals.Stopped);
    }

    [Fact]
    [Requirement("AUT-092")]
    [Trait("Requirement", "AUT-092")]
    public async Task A_token_cleared_between_waits_stops_the_loop_before_the_next_renewal()
    {
        VirtualClock clock = new(Start);
        ScriptedTransportDouble transport = new(Login(lease: 3600), Renew(lease: 3600));
        Renewals renewals = new();
        BastionVaultClient? client = null;

        client = Build(
            clock,
            transport,
            new AutoRenewPolicy
            {
                Enabled = true,
                // AUT-083's RevokeSelf lands here too: it clears the token after a successful call,
                // so the loop finds no token at the next wake rather than in a wait.
                OnRenewed = _ => client!.ClearToken(),
            },
            renewals);
        using (client)
        {
            _ = await client.Auth.Userpass.LoginAsync("alice", new SecretString("password-fixture"));
            await client.RenewalCompletion!.WaitAsync(TimeSpan.FromSeconds(30));
        }

        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal(RenewalStoppedReason.TokenRevoked, renewals.Stopped);
    }

    [Fact]
    [Requirement("AUT-090")]
    [Requirement("AUT-095")]
    [Trait("Requirement", "AUT-090")]
    public async Task A_non_renewable_credential_is_never_renewed()
    {
        VirtualClock clock = new(Start);
        ScriptedTransportDouble transport = new(Login(lease: 3600, renewable: false));
        Renewals renewals = new();
        CapturingClientLogger logger = new();

        using BastionVaultClient client = Build(
            clock,
            transport,
            new AutoRenewPolicy { Enabled = true },
            renewals,
            options => options.Logger = logger);
        _ = await client.Auth.Userpass.LoginAsync("alice", new SecretString("password-fixture"));
        await client.RenewalCompletion!.WaitAsync(TimeSpan.FromSeconds(30));

        // AUT-090's precondition, which is also AUT-095's batch and non-renewable tokens: no
        // request, no wait, and a reason rather than silence.
        Assert.Empty(clock.Waits);
        _ = Assert.Single(transport.Requests);
        Assert.Equal(RenewalStoppedReason.NotRenewable, renewals.Stopped);
        // AUT-095: exactly one log line, at info level (never Warn — Warn is reserved for
        // ERR-050's server warnings, which this response carries none of), and nothing further
        // happens once it is emitted.
        string line = Assert.Single(logger.InfoLines);
        Assert.Contains("not renewable", line, StringComparison.Ordinal);
        Assert.Empty(logger.WarnLines);
    }

    [Fact]
    [Requirement("AUT-090")]
    [Trait("Requirement", "AUT-090")]
    public async Task A_credential_with_no_lease_is_never_renewed()
    {
        VirtualClock clock = new(Start);
        ScriptedTransportDouble transport = new(Login(lease: 0));

        Renewals renewals = await RunAsync(clock, transport, new AutoRenewPolicy { Enabled = true });

        Assert.Empty(clock.Waits);
        Assert.Equal(RenewalStoppedReason.NotRenewable, renewals.Stopped);
    }

    [Fact]
    [Requirement("AUT-090")]
    [Trait("Requirement", "AUT-090")]
    public async Task A_schedule_that_is_already_due_waits_zero_rather_than_a_negative_duration()
    {
        VirtualClock clock = new(Start);
        ScriptedTransportDouble transport = new(Login(lease: 3600), Renew(lease: 3600, renewable: false));

        // RenewAtFraction 0 schedules the renewal at the credential's own issue time, which is
        // already in the past by the time the loop computes it. A clock cannot be asked to wait a
        // negative duration, and asking would be the arithmetic bug this arm exists to avoid.
        Renewals renewals = await RunAsync(clock, transport, new AutoRenewPolicy
        {
            Enabled = true,
            RenewAtFraction = 0,
        });

        Assert.Equal([TimeSpan.Zero], clock.Waits);
        _ = Assert.Single(renewals.Renewed);
    }

    [Fact]
    [Requirement("AUT-094")]
    [Trait("Requirement", "AUT-094")]
    public async Task A_renewal_cancelled_in_flight_reports_Disposed_and_not_a_renewal_failure()
    {
        VirtualClock clock = new(Start);
        List<string> failures = [];
        RenewalStoppedReason? stopped = null;
        BastionVaultClient? client = null;
        ScriptedTransportDouble transport = new(
            Login(lease: 3600),
            () =>
            {
                // Dispose lands while the renewal request is in flight. The executor maps that to
                // the coded BV-TRANSPORT-005, which the loop must read as cancellation and not as
                // an AUT-092 renewal failure.
                client!.Dispose();
                throw new OperationCanceledException();
            });

        client = Build(
            clock,
            transport,
            new AutoRenewPolicy
            {
                Enabled = true,
                OnFailed = renewal => failures.Add(renewal.Error?.Code ?? string.Empty),
                OnStopped = reason => stopped = reason,
            },
            new Renewals());
        using (client)
        {
            _ = await client.Auth.Userpass.LoginAsync("alice", new SecretString("password-fixture"));
            await client.RenewalCompletion!.WaitAsync(TimeSpan.FromSeconds(30));
        }

        Assert.Empty(failures);
        Assert.Equal(RenewalStoppedReason.Disposed, stopped);
    }

    [Fact]
    [Requirement("AUT-090")]
    [Trait("Requirement", "AUT-090")]
    public async Task A_policy_with_no_callbacks_runs_the_same_loop_and_reports_nothing()
    {
        VirtualClock clock = new(Start);
        ScriptedTransportDouble transport = new(
            Login(lease: 3600),
            ServerError(),
            Renew(lease: 3600, renewable: false));

        using BastionVaultClient client = Build(
            clock,
            transport,
            autoRenew: null,
            new Renewals(),
            // Set directly, bypassing the test's own capturing callbacks: AUT-090's three callbacks
            // are optional, and a loop that assumed one was attached would throw on a policy that
            // is exactly D-M2-6's default plus `Enabled`.
            options => options.AutoRenew = new AutoRenewPolicy { Enabled = true });
        _ = await client.Auth.Userpass.LoginAsync("alice", new SecretString("password-fixture"));
        await client.RenewalCompletion!.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(3, transport.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(2376), TimeSpan.FromSeconds(1)], clock.Waits);
    }

    [Fact]
    [Requirement("AUT-092")]
    [Trait("Requirement", "AUT-092")]
    public async Task Backoff_is_zero_once_the_lease_has_already_run_out()
    {
        VirtualClock clock = new(Start);
        ScriptedTransportDouble transport = new(Login(lease: 10), ServerError(), Renew(lease: 10, renewable: false));

        // RenewAtFraction 1.0 schedules the renewal at the instant the lease expires, so there is
        // no remaining TTL left to take a quarter of. The backoff is then zero rather than negative
        // — a negative wait is not a wait a clock can be asked for.
        Renewals renewals = await RunAsync(clock, transport, new AutoRenewPolicy
        {
            Enabled = true,
            RenewAtFraction = 1.0,
            MinInterval = TimeSpan.Zero,
        });

        Assert.Equal([TimeSpan.FromSeconds(10), TimeSpan.Zero], clock.Waits);
        _ = Assert.Single(renewals.Failed);
    }

    [Fact]
    [Requirement("AUT-090")]
    [Trait("Requirement", "AUT-090")]
    public async Task A_credential_whose_envelope_carries_no_lease_duration_is_never_renewed()
    {
        VirtualClock clock = new(Start);
        ScriptedTransportDouble transport = new(() => new TransportResponse(
            200,
            Headers(),
            Encoding.UTF8.GetBytes(
                "{\"renewable\":false,\"lease_id\":\"\",\"lease_duration\":0,\"auth\":{\"client_token\":\""
                    + FakeTokens.Client
                    + "\",\"policies\":[\"default\"],\"renewable\":true},\"data\":{}}")));

        Renewals renewals = await RunAsync(clock, transport, new AutoRenewPolicy { Enabled = true });

        // `renewable: true` with no lease at all. AUT-090 needs both operands, and inventing one
        // would schedule a renewal on a TTL the server never stated (D-M1c-25).
        Assert.Empty(clock.Waits);
        Assert.Equal(RenewalStoppedReason.NotRenewable, renewals.Stopped);
    }

    [Fact]
    [Requirement("AUT-092")]
    [Trait("Requirement", "AUT-092")]
    public async Task Clearing_the_token_from_the_OnStopped_callback_is_absorbed()
    {
        VirtualClock clock = new(Start);
        ScriptedTransportDouble transport = new(Login(lease: 3600, renewable: false));
        RenewalStoppedReason? stopped = null;
        BastionVaultClient? client = null;

        client = Build(
            clock,
            transport,
            new AutoRenewPolicy
            {
                Enabled = true,
                // A plausible application reaction to "renewal has stopped", and the one ordering
                // where the loop's own cancellation source is already gone when the token-cleared
                // signal arrives.
                OnStopped = reason =>
                {
                    stopped = reason;
                    client!.ClearToken();
                },
            },
            new Renewals());
        using (client)
        {
            _ = await client.Auth.Userpass.LoginAsync("alice", new SecretString("password-fixture"));
            await client.RenewalCompletion!.WaitAsync(TimeSpan.FromSeconds(30));
        }

        Assert.Equal(RenewalStoppedReason.NotRenewable, stopped);
        Assert.Null(client.Auth.CurrentToken);
    }

    [Fact]
    [Requirement("AUT-094")]
    [Trait("Requirement", "AUT-094")]
    public void Clearing_the_token_on_a_client_with_no_renewal_loop_is_a_no_op()
    {
        VirtualClock clock = new(Start);
        ScriptedTransportDouble transport = new();

        using BastionVaultClient client = Build(clock, transport, autoRenew: null, new Renewals());
        client.SetToken(new SecretString(FakeTokens.Client));
        client.ClearToken();

        Assert.Null(client.Auth.CurrentToken);
    }

    [Fact]
    [Requirement("AUT-093")]
    [Trait("Requirement", "AUT-093")]
    public async Task A_Login_source_logs_in_once_after_renewal_stops_and_resumes_the_schedule()
    {
        VirtualClock clock = new(Start);
        ScriptedTransportDouble transport = new(
            Login(lease: 3600),
            Failure(403, "Permission denied."),
            Login(lease: 600),
            Renew(lease: 600, renewable: false));
        Renewals renewals = new();

        using BastionVaultClient client = Build(
            clock,
            transport,
            new AutoRenewPolicy { Enabled = true },
            renewals,
            options => options.TokenSource = TokenSource.Login(
                AuthMethod.Userpass,
                LoginCredentials.ForUserpass("alice", new SecretString("password-fixture"))));

        _ = await client.Auth.AuthenticateAsync();
        await client.RenewalCompletion!.WaitAsync(TimeSpan.FromSeconds(30));

        // The schedule of the first credential, then the schedule of the one the fresh login
        // granted: 600 × 0.66 = 396 s. No OnStopped(RenewalFailed) in between — AUT-093 replaces
        // that stop with the re-login, and emits ReloginFailed only when the login itself fails.
        Assert.Equal([TimeSpan.FromSeconds(2376), TimeSpan.FromSeconds(396)], clock.Waits);
        _ = Assert.Single(renewals.Renewed);
        Assert.Equal(RenewalStoppedReason.NotRenewable, renewals.Stopped);
    }

    [Fact]
    [Requirement("AUT-093")]
    [Trait("Requirement", "AUT-093")]
    public async Task The_single_relogin_allowance_is_re_armed_by_the_next_successful_renewal()
    {
        VirtualClock clock = new(Start);
        ScriptedTransportDouble transport = new(
            Login(lease: 3600),
            Failure(403, "Permission denied."),
            Login(lease: 600),
            Renew(lease: 600),
            Failure(403, "Permission denied."),
            Login(lease: 300),
            Renew(lease: 300, renewable: false));
        Renewals renewals = new();

        using BastionVaultClient client = Build(
            clock,
            transport,
            new AutoRenewPolicy { Enabled = true },
            renewals,
            options => options.TokenSource = TokenSource.Login(
                AuthMethod.Userpass,
                LoginCredentials.ForUserpass("alice", new SecretString("password-fixture"))));

        _ = await client.Auth.AuthenticateAsync();
        await client.RenewalCompletion!.WaitAsync(TimeSpan.FromSeconds(30));

        // "Once" is once per stop, not once per client. A client that recovered — the renewal
        // between the two 403s succeeded — gets its allowance back, or a long-lived process would
        // be left with one re-login for the rest of its life after a single transient outage.
        Assert.Equal(7, transport.Requests.Count);
        Assert.Equal(
            [TimeSpan.FromSeconds(2376), TimeSpan.FromSeconds(396), TimeSpan.FromSeconds(396), TimeSpan.FromSeconds(198)],
            clock.Waits);
        Assert.Equal(2, renewals.Renewed.Count);
        Assert.Equal(["BV-AUTHZ-001", "BV-AUTHZ-001"], renewals.Failed);
        Assert.Equal(RenewalStoppedReason.NotRenewable, renewals.Stopped);
    }

    [Fact]
    [Requirement("AUT-093")]
    [Trait("Requirement", "AUT-093")]
    public async Task A_failed_fresh_login_reports_ReloginFailed()
    {
        VirtualClock clock = new(Start);
        ScriptedTransportDouble transport = new(
            Login(lease: 3600),
            Failure(403, "Permission denied."),
            Failure(400, "invalid username or password"));
        Renewals renewals = new();

        using BastionVaultClient client = Build(
            clock,
            transport,
            new AutoRenewPolicy { Enabled = true },
            renewals,
            options => options.TokenSource = TokenSource.Login(
                AuthMethod.Userpass,
                LoginCredentials.ForUserpass("alice", new SecretString("password-fixture"))));

        _ = await client.Auth.AuthenticateAsync();
        await client.RenewalCompletion!.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(RenewalStoppedReason.ReloginFailed, renewals.Stopped);
    }

    [Fact]
    [Requirement("AUT-094")]
    [Trait("Requirement", "AUT-094")]
    public async Task Disposing_the_client_stops_the_loop_and_reports_Disposed()
    {
        VirtualClock clock = new(Start);
        ScriptedTransportDouble transport = new(Login(lease: 3600), Renew(lease: 3600));
        Renewals renewals = new();

        BastionVaultClient client = Build(clock, transport, new AutoRenewPolicy { Enabled = true }, renewals);
        transport.OnExhausted = client.Dispose;
        _ = await client.Auth.Userpass.LoginAsync("alice", new SecretString("password-fixture"));
        await client.RenewalCompletion!.WaitAsync(TimeSpan.FromSeconds(30));

        _ = Assert.Single(renewals.Renewed);
        Assert.Equal(RenewalStoppedReason.Disposed, renewals.Stopped);
        // Idempotent: shutting down twice is not an error, and neither is disposing a namespace
        // view, which owns no loop of its own.
        client.Dispose();
        client.WithNamespace("team-a").Dispose();
        Assert.Equal(RenewalStoppedReason.Disposed, renewals.Stopped);
    }

    [Fact]
    [Requirement("AUT-094")]
    [Trait("Requirement", "AUT-094")]
    public void A_client_with_AutoRenew_disabled_starts_no_loop_and_disposes_cleanly()
    {
        VirtualClock clock = new(Start);
        ScriptedTransportDouble transport = new();

        using BastionVaultClient client = Build(clock, transport, autoRenew: null, new Renewals());

        Assert.Null(client.RenewalCompletion);
        Assert.False(client.Config.AutoRenew.Enabled);
    }

    [Fact]
    [Requirement("AUT-090")]
    [Trait("Requirement", "AUT-090")]
    public async Task The_configured_increment_is_sent_and_a_null_increment_asks_for_the_server_default()
    {
        VirtualClock configured = new(Start);
        ScriptedTransportDouble withIncrement = new(Login(lease: 3600), Renew(lease: 3600, renewable: false));
        _ = await RunAsync(configured, withIncrement, new AutoRenewPolicy
        {
            Enabled = true,
            Increment = TimeSpan.FromHours(2),
        });

        VirtualClock defaulted = new(Start);
        ScriptedTransportDouble withoutIncrement = new(Login(lease: 3600), Renew(lease: 3600, renewable: false));
        _ = await RunAsync(defaulted, withoutIncrement, new AutoRenewPolicy { Enabled = true });

        Assert.Contains("\"increment\":7200", Body(withIncrement.Requests[1]), StringComparison.Ordinal);
        Assert.Contains("\"increment\":0", Body(withoutIncrement.Requests[1]), StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("AUT-090")]
    [Trait("Requirement", "AUT-090")]
    public async Task A_204_renewal_is_a_success_that_keeps_the_loop_scheduling()
    {
        // DR-0021 F1: a Login-sourced token renews with `204 No Content`. The loop must not die on
        // it — it keeps the lease it already knew (600 s, from the login) and schedules the next
        // wake from it, exactly as it would from a 200 envelope repeating the same lease.
        VirtualClock clock = new(Start);
        ScriptedTransportDouble transport = new(
            Login(lease: 600),
            RenewNoContent(),
            Renew(lease: 600, renewable: false));

        Renewals renewals = await RunAsync(clock, transport, new AutoRenewPolicy { Enabled = true });

        // 600 × 0.66 = 396 s, then the same 396 s again: the 204 carried no new lease, so the
        // schedule restarts from the same 600 s duration it already had.
        Assert.Equal([TimeSpan.FromSeconds(396), TimeSpan.FromSeconds(396)], clock.Waits);
        Assert.Equal(2, renewals.Renewed.Count);
        Assert.Empty(renewals.Failed);
        Assert.Equal(RenewalStoppedReason.NotRenewable, renewals.Stopped);
    }

    [Fact]
    [Requirement("AUT-091")]
    [Requirement("AUT-092")]
    [Trait("Requirement", "AUT-091")]
    public async Task A_renewal_event_carries_the_credential_on_success_and_the_coded_failure_on_failure()
    {
        VirtualClock clock = new(Start);
        ScriptedTransportDouble transport = new(
            Login(lease: 3600),
            ServerError(),
            Renew(lease: 900, renewable: false));
        List<RenewalEvent> renewed = [];
        List<RenewalEvent> failed = [];
        Renewals renewals = new();

        using BastionVaultClient client = Build(
            clock,
            transport,
            new AutoRenewPolicy
            {
                Enabled = true,
                OnRenewed = renewed.Add,
                OnFailed = failed.Add,
                OnStopped = reason => renewals.Stopped = reason,
            },
            renewals);
        _ = await client.Auth.Userpass.LoginAsync("alice", new SecretString("password-fixture"));
        await client.RenewalCompletion!.WaitAsync(TimeSpan.FromSeconds(30));

        RenewalEvent failure = Assert.Single(failed);
        Assert.Equal(1, failure.ConsecutiveFailures);
        Assert.Null(failure.Auth);
        Assert.NotNull(failure.Error);

        RenewalEvent success = Assert.Single(renewed);
        Assert.Equal(0, success.ConsecutiveFailures);
        Assert.Null(success.Error);
        Assert.Equal(TimeSpan.FromSeconds(900), success.Auth?.LeaseDuration);
        // Read from the injected clock, never the wall clock (AUT-013, D-M1b-7).
        Assert.Equal(clock.NowUtc(), success.At);
    }

    private static async Task<Renewals> RunAsync(VirtualClock clock, ScriptedTransportDouble transport, AutoRenewPolicy policy)
    {
        Renewals renewals = new();
        using BastionVaultClient client = Build(clock, transport, policy, renewals);
        _ = await client.Auth.Userpass.LoginAsync("alice", new SecretString("password-fixture"));
        await client.RenewalCompletion!.WaitAsync(TimeSpan.FromSeconds(30));
        return renewals;
    }

    private static BastionVaultClient Build(
        VirtualClock clock,
        ScriptedTransportDouble transport,
        AutoRenewPolicy? autoRenew,
        Renewals renewals,
        Action<BastionVaultClientOptions>? configure = null)
    {
        BastionVaultClientOptions options = new()
        {
            Address = Address,
            Transport = transport,
            Clock = clock,
            RateGate = new RateGate { RatePerSecond = 0 },
            RetryPolicy = new RetryPolicy { MaxAttempts = 1 },
            AutoRenew = autoRenew is null ? null : renewals.Attach(autoRenew),
        };
        configure?.Invoke(options);
        return new BastionVaultClient(options, EnvironmentSource.None);
    }

    private static string Body(TransportRequest request)
    {
        return Encoding.UTF8.GetString(request.Body.Span);
    }

    private static Func<TransportResponse> Login(int lease, bool renewable = true)
    {
        return Envelope(200, FakeTokens.Client, lease, renewable);
    }

    private static Func<TransportResponse> Renew(int lease, bool renewable = true)
    {
        return Envelope(200, FakeTokens.Client, lease, renewable);
    }

    private static Func<TransportResponse> RenewNoContent()
    {
        return () => new TransportResponse(204, Headers(), ReadOnlyMemory<byte>.Empty);
    }

    private static Func<TransportResponse> ServerError()
    {
        return () => new TransportResponse(500, Headers(), Encoding.UTF8.GetBytes("{\"error\":\"internal error\"}"));
    }

    private static Func<TransportResponse> Failure(int status, string message)
    {
        return () => new TransportResponse(
                status,
                Headers(),
                Encoding.UTF8.GetBytes($"{{\"error\":\"{message}\"}}"));
    }

    private static Func<TransportResponse> Envelope(int status, string token, int lease, bool renewable)
    {
        return () => new TransportResponse(
                status,
                Headers(),
                Encoding.UTF8.GetBytes(
                    "{\"renewable\":false,\"lease_id\":\"\",\"lease_duration\":0,\"auth\":{\"client_token\":\""
                        + token
                        + "\",\"policies\":[\"default\"],\"metadata\":{},\"lease_duration\":"
                        + lease.ToString(CultureInfo.InvariantCulture)
                        + ",\"renewable\":"
                        + (renewable ? "true" : "false")
                        + "},\"data\":{}}"));
    }

    private static Dictionary<string, string> Headers()
    {
        return new(StringComparer.OrdinalIgnoreCase) { ["Content-Type"] = "application/json" };
    }

    /// <summary>What the loop reported, collected through the three AUT-090 callbacks.</summary>
    private sealed class Renewals
    {
        public List<TimeSpan> Renewed { get; } = [];

        public List<string> Failed { get; } = [];

        public RenewalStoppedReason? Stopped { get; set; }

        public AutoRenewPolicy Attach(AutoRenewPolicy policy)
        {
            return policy with
            {
                OnRenewed = policy.OnRenewed ?? (renewal => Renewed.Add(renewal.Auth?.LeaseDuration ?? TimeSpan.Zero)),
                OnFailed = policy.OnFailed ?? (renewal => Failed.Add(renewal.Error?.Code ?? string.Empty)),
                OnStopped = policy.OnStopped ?? (reason => Stopped = reason),
            };
        }
    }

    /// <summary>
    /// A clock that makes time pass by being asked to wait, and records what it was asked for. The
    /// unit-test counterpart of D-M2-27's fixture virtual mode, and separate from it on purpose.
    /// </summary>
    private sealed class VirtualClock : IClock
    {
        private readonly object gate = new();
        private readonly List<TimeSpan> waits = [];
        private DateTimeOffset now;

        public VirtualClock(DateTimeOffset start) => now = start;

        /// <summary>
        /// Runs at the top of every <see cref="Delay"/>, before cancellation is observed: the only
        /// way a test can make something happen <i>during</i> a wait that takes no real time.
        /// </summary>
        public Action? BeforeWait { get; set; }

        public IReadOnlyList<TimeSpan> Waits
        {
            get
            {
                lock (gate)
                {
                    return waits.ToArray();
                }
            }
        }

        public DateTimeOffset NowUtc()
        {
            lock (gate)
            {
                return now;
            }
        }

        public Task Delay(TimeSpan duration, CancellationToken cancellationToken)
        {
            BeforeWait?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                now += duration;
                waits.Add(duration);
                if (waits.Count > 64)
                {
                    // The same bound D-M2-27 fixes for the fixture clock, for the same reason:
                    // virtual time makes a spin fast, so only a count can catch one.
                    throw new InvalidOperationException("The renewal loop asked for more than 64 waits; it is spinning.");
                }
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>A scripted <see cref="ITransport"/> with a hook for the last scripted answer.</summary>
    private sealed class ScriptedTransportDouble : ITransport
    {
        private readonly object gate = new();
        private readonly Queue<Func<TransportResponse>> script;
        private readonly List<TransportRequest> requests = [];

        public ScriptedTransportDouble(params Func<TransportResponse>[] script)
            => this.script = new Queue<Func<TransportResponse>>(script);

        /// <summary>Invoked once the last scripted answer has been produced.</summary>
        public Action? OnExhausted { get; set; }

        public IReadOnlyList<TransportRequest> Requests
        {
            get
            {
                lock (gate)
                {
                    return requests.ToArray();
                }
            }
        }

        public bool SupportsCustomVerbs => true;

        public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Func<TransportResponse> next;
            bool exhausted;
            lock (gate)
            {
                requests.Add(request);
                if (script.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"No scripted response remains for {request.Method} {request.Uri}; the loop made one request too many.");
                }

                next = script.Dequeue();
                exhausted = script.Count == 0;
            }

            TransportResponse response = next();
            if (exhausted)
            {
                OnExhausted?.Invoke();
            }

            return Task.FromResult(response);
        }
    }
}
