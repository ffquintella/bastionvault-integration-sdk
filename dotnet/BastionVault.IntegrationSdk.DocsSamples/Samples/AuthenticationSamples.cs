using System.Text.Json;
using BastionVault.IntegrationSdk.DocsSamples.Infrastructure;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.DocsSamples.Samples;

/// <summary>
/// Every executed sample shown by <c>docs/dotnet/authentication.md</c> (D4), the .NET adaptation
/// of guides 2-4 in <c>specifications/17-usage-guides.md</c>. Reuses
/// <see cref="MockVaultFixture"/>'s server: routes are bound per test on
/// <see cref="MockVaultFixture.Server"/> and cleared after, so no test leaks a route to another.
/// </summary>
public sealed class AuthenticationSamples : IClassFixture<MockVaultFixture>
{
    private readonly MockVaultFixture vault;

    public AuthenticationSamples(MockVaultFixture vault)
    {
        this.vault = vault;
    }

    [Fact]
    public void The_policy_this_guide_needs_is_the_policy_PolicyBuilder_builds()
    {
        // docs:begin authentication/policy
        // A login is unauthenticated on the wire (TRN-015), so the only path a token in this
        // guide's flows needs is the one the token-hygiene step uses to create a child token.
        string hcl = new PolicyBuilder()
            .AddPath("auth/token/create", [Capability.Create, Capability.Update])
            .Build();

        Console.WriteLine(hcl);
        // docs:end authentication/policy

        Assert.Equal(PolicyShownInTheGuide(), hcl.Trim());
    }

    private static string PolicyShownInTheGuide()
    {
        string path = Path.Combine(DocsRepository.Docs.FullName, "dotnet", "authentication.md");
        string[] lines = File.ReadAllLines(path);
        int opening = Array.FindIndex(lines, line => line.TrimEnd() == "```hcl");
        Assert.True(opening >= 0, $"{path} shows no ```hcl policy block (DOC-011).");
        int closing = Array.FindIndex(lines, opening + 1, line => line.TrimEnd() == "```");
        Assert.True(closing > opening, $"{path}: the ```hcl block is never closed.");
        return string.Join('\n', lines[(opening + 1)..closing]).Trim();
    }

    [Fact]
    public async Task Step_1_authenticate_a_service_with_appid()
    {
        vault.Server.ClearRouteResponses();
        vault.Server.SetRouteResponse("/v1/auth/approle/login", Json(200, LoginBody("s.FAKEchildtoken")));
        using BastionVaultClient client = new(new BastionVaultClientOptions { Namespace = "dti/esi" });

        // docs:begin authentication/appid-login
        // No token header goes out on this call: the role_id/secret_id pair is the credential.
        // A namespace-scoped role needs Namespace configured on the client too (AUT-041); the
        // SDK adds X-BastionVault-Namespace for you.
        AuthInfo auth;
        try
        {
            auth = await client.Auth.AppId.LoginAsync("app-role", new SecretString("s.FAKEsecretid"));
        }
        catch (BastionVaultException e) when (e.Code == ErrorCodes.AuthInvalidAppIdCredentials)
        {
            Console.Error.WriteLine("bad role_id/secret_id, or the secret_id was already used up");
            throw;
        }
        catch (BastionVaultException e) when (e.Code == ErrorCodes.AuthAppIdMachineBinding)
        {
            Console.Error.WriteLine($"machine binding: {e.Hint}");
            throw;
        }
        catch (BastionVaultException e) when (e.Code == ErrorCodes.AuthzPermissionDenied)
        {
            // A 403 on this path is gating, never a bad credential (AUT-041): namespace,
            // source IP/CIDR, or machine binding. The hint names which.
            Console.Error.WriteLine($"credentials OK but gated: {e.Hint}");
            throw;
        }

        Console.WriteLine($"token TTL {auth.LeaseDuration}, policies [{string.Join(", ", auth.Policies)}]");
        if (auth.EnvironmentScope.Scoped)
        {
            // AUT-044: this token must pass `env` on every KV v2 call - the secrets guide's
            // BV-KV-009 - and this is where a caller learns it must.
            Console.WriteLine($"this token must pass env= one of [{string.Join(", ", auth.EnvironmentScope.SecretGlobs)}]");
        }
        // docs:end authentication/appid-login

        Assert.Equal(TimeSpan.FromSeconds(1200), auth.LeaseDuration);
        Assert.Contains("default", auth.Policies);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task An_appid_login_with_a_bad_role_id_is_BV_AUTH_010_not_a_generic_failure()
    {
        // AUT-012: a 400 whose message starts with "invalid role_id" is refined to
        // BV-AUTH-010, distinct from every other 400 on this path.
        vault.Server.ClearRouteResponses();
        vault.Server.SetRouteResponse("/v1/auth/approle/login", Json(400, """{"error":"invalid role_id"}"""));
        using BastionVaultClient client = new();

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.AppId.LoginAsync("wrong-role", new SecretString("s.FAKEsecretid")));

        Assert.Equal(ErrorCodes.AuthInvalidAppIdCredentials, failure.Code);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task Step_2_human_login_with_username_and_password()
    {
        vault.Server.ClearRouteResponses();
        vault.Server.SetRouteResponse("/v1/auth/userpass/login/alice", Json(200, LoginBody("s.FAKEhumantoken")));
        using BastionVaultClient client = new();

        // docs:begin authentication/userpass-login
        // AUT-011: a rejected credential comes back as HTTP 200 with data.error, never
        // 401/403. Switch on Code below - never parse ServerMessage yourself.
        AuthInfo auth;
        try
        {
            auth = await client.Auth.Userpass.LoginAsync("alice", new SecretString("s.FAKEpassword"));
        }
        catch (BastionVaultException e)
        {
            string reason = e.Code switch
            {
                ErrorCodes.AuthInvalidCredentials => "wrong username or password",
                ErrorCodes.AuthAccountDisabled => "account disabled - contact an admin",
                ErrorCodes.AuthAccountLocked => $"locked; retry in {e.Details["retry_after_secs"]}s",
                ErrorCodes.AuthTotpRequired => "a TOTP code is required; prompt for it and retry",
                ErrorCodes.AuthInvalidTotp => "invalid TOTP code",
                ErrorCodes.AuthPasswordLoginDisabled => "use your FIDO2 security key instead",
                _ => throw e,
            };
            Console.Error.WriteLine(reason);
            throw;
        }

        Console.WriteLine($"logged in as alice, policies [{string.Join(", ", auth.Policies)}]");
        // docs:end authentication/userpass-login

        Assert.Contains("default", auth.Policies);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Theory]
    [InlineData("""{"auth":null,"data":{"error":"account is disabled"}}""", ErrorCodes.AuthAccountDisabled)]
    [InlineData("""{"auth":null,"data":{"error":"account temporarily locked; try again in 30 seconds"}}""", ErrorCodes.AuthAccountLocked)]
    [InlineData("""{"auth":null,"data":{"error":"a TOTP code is required for this account"}}""", ErrorCodes.AuthTotpRequired)]
    [InlineData("""{"auth":null,"data":{"error":"invalid TOTP code"}}""", ErrorCodes.AuthInvalidTotp)]
    [InlineData("""{"auth":null,"data":{"error":"Password login is disabled for this account. Use your FIDO2 security key instead."}}""", ErrorCodes.AuthPasswordLoginDisabled)]
    public async Task Every_recognised_userpass_rejection_maps_to_its_own_code(string body, string expectedCode)
    {
        vault.Server.ClearRouteResponses();
        vault.Server.SetRouteResponse("/v1/auth/userpass/login/alice", Json(200, body));
        using BastionVaultClient client = new();

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.Userpass.LoginAsync("alice", new SecretString("s.FAKEpassword")));

        Assert.Equal(expectedCode, failure.Code);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task Step_3_keep_the_token_healthy_lookup_and_renew()
    {
        vault.Server.ClearRouteResponses();
        vault.Server.SetRouteResponse("/v1/auth/token/lookup-self", Json(200, LookupSelfBody()));
        vault.Server.SetRouteResponse("/v1/auth/token/renew/s.FAKEtoken", Json(200, RenewBody()));
        using BastionVaultClient client = vault.CreateClient();

        // docs:begin authentication/token-hygiene
        // RemainingTtl is computed by the SDK; the wire's own `ttl` field is always 0 and is
        // never exposed (AUT-014).
        TokenInfo info = await client.Auth.Token.LookupSelfAsync();
        Console.WriteLine($"{info.DisplayName}: policies [{string.Join(", ", info.Policies)}], remaining {info.RemainingTtl}");

        if (info.RemainingTtl is { } remaining && remaining < TimeSpan.FromMinutes(5))
        {
            // AUT-080: there is no `renew-self` path; RenewSelf posts to renew/{currentToken}.
            AuthInfo renewed = await client.Auth.Token.RenewSelfAsync(increment: 3600);
            Console.WriteLine($"renewed: new lease {renewed.LeaseDuration}");
        }
        // docs:end authentication/token-hygiene

        Assert.Equal("alice", info.DisplayName);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task Step_4_issue_a_narrow_child_token_then_revoke_on_shutdown()
    {
        vault.Server.ClearRouteResponses();
        vault.Server.SetRouteResponse("/v1/auth/token/create", Json(200, LoginBody("s.FAKEchildtoken")));
        vault.Server.SetRouteResponse("/v1/auth/token/revoke-self", new MockResponse(204, BodyIsJson: false));
        using BastionVaultClient client = vault.CreateClient();

        // docs:begin authentication/token-hygiene-create-and-revoke
        // AUT-081: a reserved meta key (spiffe_id, machine_id, entity_id, ...) is refused
        // client-side before anything is sent; "purpose" below is not reserved.
        AuthInfo child = await client.Auth.Token.CreateAsync(new CreateTokenRequest
        {
            Policies = ["app-readonly"],
            Ttl = TimeSpan.FromMinutes(15),
            NumUses = 20,
            Meta = new Dictionary<string, string> { ["purpose"] = "batch-job" },
        });

        Console.WriteLine($"child token TTL {child.LeaseDuration}, num_uses limited");

        // ... hand child.ClientToken to the subprocess. Then, on this process's own shutdown:
        await client.Auth.Token.RevokeSelfAsync();
        // docs:end authentication/token-hygiene-create-and-revoke

        Assert.Equal(TimeSpan.FromSeconds(1200), child.LeaseDuration);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public void Configuring_AutoRenew_for_a_long_running_service()
    {
        // docs:begin authentication/auto-renew
        // AUT-090..AUT-095: configured once, at construction. The SDK schedules RenewSelf at
        // IssuedAt + LeaseDuration x RenewAtFraction, retries a failure with backoff up to
        // MaxConsecutiveFailures, and gives a Login-sourced token one fresh login before
        // giving up.

        // Nothing runs until a login issues a credential, so this sample shows configuration
        // only; the schedule itself runs on real wall-clock time and is exercised by the SDK's
        // own test suite, which drives it with a clock this public API does not expose.
        using BastionVaultClient client = new(new BastionVaultClientOptions
        {
            AutoRenew = new AutoRenewPolicy
            {
                Enabled = true,
                RenewAtFraction = 0.66,
                MaxConsecutiveFailures = 5,
                OnRenewed = renewal => Console.WriteLine($"renewed, new TTL {renewal.Auth?.LeaseDuration}"),
                OnFailed = failure => Console.Error.WriteLine($"renewal attempt failed: {failure.Error?.Code}"),
                OnStopped = reason => Console.Error.WriteLine($"AutoRenew stopped: {reason}"),
            },
        });

        Console.WriteLine(
            $"AutoRenew enabled: {client.Config.AutoRenew.Enabled}, "
            + $"renews at {client.Config.AutoRenew.RenewAtFraction:P0} of the lease");
        // docs:end authentication/auto-renew

        Assert.True(client.Config.AutoRenew.Enabled);
        Assert.Equal(0.66, client.Config.AutoRenew.RenewAtFraction);
        Assert.Equal(5, client.Config.AutoRenew.MaxConsecutiveFailures);
    }

    [Fact]
    public async Task The_whole_program_authenticates_keeps_the_token_healthy_and_revokes_it()
    {
        vault.Server.ClearRouteResponses();
        vault.Server.SetRouteResponse("/v1/auth/approle/login", Json(200, LoginBody("s.FAKEchildtoken")));
        vault.Server.SetRouteResponse("/v1/auth/token/lookup-self", Json(200, LookupSelfBody()));
        vault.Server.SetRouteResponse("/v1/auth/token/renew/s.FAKEchildtoken", Json(200, RenewBody()));
        vault.Server.SetRouteResponse("/v1/auth/token/revoke-self", new MockResponse(204, BodyIsJson: false));
        int before = vault.Server.Requests.Count;

        // docs:begin authentication/complete
        using BastionVaultClient client = new(new BastionVaultClientOptions { Namespace = "dti/esi" });

        try
        {
            AuthInfo auth = await client.Auth.AppId.LoginAsync("app-role", new SecretString("s.FAKEsecretid"));
            Console.WriteLine($"authenticated, TTL {auth.LeaseDuration}");

            TokenInfo info = await client.Auth.Token.LookupSelfAsync();
            if (info.RemainingTtl is { } remaining && remaining < TimeSpan.FromMinutes(5))
            {
                _ = await client.Auth.Token.RenewSelfAsync(increment: 3600);
                Console.WriteLine("renewed");
            }

            // ... do the service's actual work here, using `client` for every call ...

            await client.Auth.Token.RevokeSelfAsync();
            Console.WriteLine("revoked on shutdown");
        }
        catch (BastionVaultException e)
        {
            Console.Error.WriteLine($"{e.Code}: {e.Message} ({e.Hint}); retryable: {e.Retryable}");
            throw;
        }
        // docs:end authentication/complete

        Assert.Equal(4, vault.Server.Requests.Count - before);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task What_can_go_wrong_maps_every_auth_code_to_a_remedy()
    {
        vault.Server.ClearRouteResponses();
        vault.Server.SetRouteResponse("/v1/auth/approle/login", Json(400, """{"error":"invalid secret_id"}"""));
        using BastionVaultClient client = new();

        BastionVaultException bad = await Assert.ThrowsAsync<BastionVaultException>(async () =>
        {
            // docs:begin authentication/handling-errors
            try
            {
                _ = await client.Auth.AppId.LoginAsync("app-role", new SecretString("s.FAKEsecretid"));
            }
            catch (BastionVaultException e)
            {
                string remedy = e.Code switch
                {
                    ErrorCodes.AuthInvalidAppIdCredentials => "check role_id/secret_id; a secret_id may be single-use and already spent",
                    ErrorCodes.AuthAppIdMachineBinding => "supply a FerroGate machine token, or set bypass_machine_binding on the role",
                    ErrorCodes.AuthzPermissionDenied => "gated: check namespace, source IP/CIDR, and machine binding",
                    ErrorCodes.AuthInvalidCredentials => "wrong username or password",
                    ErrorCodes.AuthAccountLocked => "locked; do not retry automatically",
                    ErrorCodes.AuthTokenNotRenewable => "the token is not renewable, or has already expired",
                    ErrorCodes.NotFoundTokenNotFound => "the token being looked up no longer exists",
                    _ => "look the code up in the error reference",
                };
                Console.Error.WriteLine($"{e.Code}: {e.Message} - {remedy}");
                throw;
            }
            // docs:end authentication/handling-errors
        });

        Assert.Equal(ErrorCodes.AuthInvalidAppIdCredentials, bad.Code);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    private static MockResponse Json(int status, string body) => new(status, Body: Compact(body));

    private static string Compact(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    // The login-response contract's success arm (05-authentication.md), the shape every
    // AppID/Userpass/Token.Create success carries.
    private static string LoginBody(string token) => Compact($$"""
        {
          "renewable": true,
          "lease_id": "",
          "lease_duration": 1200,
          "auth": {
            "client_token": "{{token}}",
            "policies": ["default"],
            "metadata": {},
            "lease_duration": 1200,
            "renewable": true
          },
          "data": null
        }
        """);

    // creation_time is fixed in the past and creation_ttl is short, so RemainingTtl is already
    // negative regardless of when this test runs - deterministic without an injectable clock.
    private static string LookupSelfBody() => Compact("""
        {
          "renewable": false,
          "lease_id": "",
          "lease_duration": 0,
          "auth": null,
          "data": {
            "id": "s.FAKEtoken",
            "policies": ["default"],
            "path": "auth/approle/login",
            "meta": {},
            "display_name": "alice",
            "num_uses": 0,
            "ttl": 0,
            "creation_time": 1700000000,
            "creation_ttl": 300,
            "explicit_max_ttl": 0
          }
        }
        """);

    private static string RenewBody() => LoginBody("s.FAKEtoken");
}
