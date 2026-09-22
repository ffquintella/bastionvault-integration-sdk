using System.Text.Json;
using BastionVault.IntegrationSdk.IntegrationTests.Harness;

namespace BastionVault.IntegrationSdk.IntegrationTests.Scenarios;

/// <summary>ITG-S07: userpass (15-testing-requirements.md:203-206).</summary>
public sealed class Scenario07_Userpass : IntegrationTest
{
    [IntegrationFact]
    public async Task Login_wrong_password_disabled_and_lockout_all_map_correctly()
    {
        string mount = await Resources.EnableAuthAsync("userpass", "up");
        string policyName = await Resources.WritePolicyAsync("up-policy", """
            path "secret/data/scratch/*" {
              capabilities = ["read"]
            }
            """);

        // A small lockout window so the test does not wait on the server's 900s default.
        _ = await Client.Auth.Userpass.Admin.WriteLockoutAsync(
            JsonDocument.Parse("""{"enabled":true,"max_failed_attempts":2,"lockout_duration_secs":2}""").RootElement,
            mount);

        string username = Resources.TrackUser(mount, "alice");
        SecretString password = new("Sc3n07-passw0rd!");
        _ = await Client.Auth.Userpass.Admin.WriteUserAsync(
            username,
            JsonDocument.Parse($$"""{"password":"{{password.Reveal()}}","token_policies":["{{policyName}}"]}""").RootElement,
            mount);

        using BastionVaultClient anon = Server.CreateClient(o => o.Token = null!);
        AuthInfo login = await anon.Auth.Userpass.LoginAsync(username, password, mount: mount);
        Resources.TrackToken("alice-session", login.ClientToken);
        Assert.Contains(policyName, login.Policies);
        Assert.True(login.ClientToken.HasValue);

        // A fresh client with the issued token proves the token actually works.
        using BastionVaultClient asAlice = Server.CreateClient(o => o.Token = login.ClientToken.Reveal());
        TokenInfo self = await asAlice.Auth.Token.LookupSelfAsync();
        Assert.Contains(policyName, self.Policies);

        BastionVaultException wrongPassword = await Assert.ThrowsAsync<BastionVaultException>(
            () => anon.Auth.Userpass.LoginAsync(username, new SecretString("nope"), mount: mount));
        Assert.Equal(ErrorCodes.AuthInvalidCredentials, wrongPassword.Code);

        string disabledUser = Resources.TrackUser(mount, "bob");
        _ = await Client.Auth.Userpass.Admin.WriteUserAsync(
            disabledUser,
            JsonDocument.Parse($$"""{"password":"{{password.Reveal()}}","token_policies":["{{policyName}}"],"disabled":true}""").RootElement,
            mount);
        BastionVaultException disabled = await Assert.ThrowsAsync<BastionVaultException>(
            () => anon.Auth.Userpass.LoginAsync(disabledUser, password, mount: mount));
        Assert.Equal(ErrorCodes.AuthAccountDisabled, disabled.Code);

        string lockUser = Resources.TrackUser(mount, "carol");
        _ = await Client.Auth.Userpass.Admin.WriteUserAsync(
            lockUser,
            JsonDocument.Parse($$"""{"password":"{{password.Reveal()}}","token_policies":["{{policyName}}"]}""").RootElement,
            mount);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            _ = await Assert.ThrowsAsync<BastionVaultException>(
                () => anon.Auth.Userpass.LoginAsync(lockUser, new SecretString("nope"), mount: mount));
        }

        BastionVaultException locked = await Assert.ThrowsAsync<BastionVaultException>(
            () => anon.Auth.Userpass.LoginAsync(lockUser, password, mount: mount));
        Assert.Equal(ErrorCodes.AuthAccountLocked, locked.Code);
        Assert.True(locked.Details.ContainsKey("retry_after_secs"));

        _ = await Client.Auth.Userpass.Admin.UnlockAsync(lockUser, mount);
        AuthInfo unlockedLogin = await anon.Auth.Userpass.LoginAsync(lockUser, password, mount: mount);
        Resources.TrackToken("carol-session", unlockedLogin.ClientToken);
    }
}

/// <summary>ITG-S08: AppID (serial - the mount config step mutates server-observable auth state per this slice's brief).</summary>
public sealed class Scenario08_AppId : SerialIntegrationTest
{
    [IntegrationFact]
    public async Task Role_id_and_single_use_secret_id_login_map_correctly()
    {
        string mount = await Resources.EnableAuthAsync("approle", "ai");

        // AUT-040: require_machine defaults on; this mount-scoped step disables the gate so the
        // test can log in without a FerroGate machine token.
        _ = await Client.Auth.AppId.Admin.WriteConfigAsync(
            JsonDocument.Parse("""{"require_machine":false}""").RootElement, mount);

        string roleName = Resources.Name("role");
        _ = await Client.Auth.AppId.Admin.WriteRoleAsync(
            roleName,
            JsonDocument.Parse("""{"policies":["default"],"bind_secret_id":true}""").RootElement,
            mount);

        string roleId = await Client.Auth.AppId.ReadRoleIdAsync(roleName, mount);
        Assert.False(string.IsNullOrWhiteSpace(roleId));

        SecretIdInfo secretId = await Client.Auth.AppId.GenerateSecretIdAsync(
            roleName, new SecretIdOptions { NumUses = 1 }, mount);

        using BastionVaultClient anon = Server.CreateClient(o => o.Token = null!);
        AuthInfo login = await anon.Auth.AppId.LoginAsync(roleId, secretId.SecretId, mount: mount);
        Resources.TrackToken("appid-session", login.ClientToken);
        Assert.True(login.ClientToken.HasValue);

        BastionVaultException reused = await Assert.ThrowsAsync<BastionVaultException>(
            () => anon.Auth.AppId.LoginAsync(roleId, secretId.SecretId, mount: mount));
        Assert.Equal(ErrorCodes.AuthInvalidAppIdCredentials, reused.Code);

        BastionVaultException wrongRole = await Assert.ThrowsAsync<BastionVaultException>(
            () => anon.Auth.AppId.LoginAsync("not-a-role-id", secretId.SecretId, mount: mount));
        Assert.Equal(ErrorCodes.AuthInvalidAppIdCredentials, wrongRole.Code);
    }
}

/// <summary>ITG-S09: AppID bound_source_ips gate, Standard (15-testing-requirements.md:211).</summary>
public sealed class Scenario09_AppIdBoundSourceIps : IntegrationTest
{
    [IntegrationFact]
    public async Task Login_from_an_excluded_source_ip_is_denied()
    {
        string mount = await Resources.EnableAuthAsync("approle", "gated");
        _ = await Client.Auth.AppId.Admin.WriteConfigAsync(
            JsonDocument.Parse("""{"require_machine":false}""").RootElement, mount);

        string roleName = Resources.Name("role");
        _ = await Client.Auth.AppId.Admin.WriteRoleAsync(
            roleName,
            JsonDocument.Parse("""{"policies":["default"],"bind_secret_id":true,"bound_source_ips":["10.0.0.0/8"]}""").RootElement,
            mount);

        string roleId = await Client.Auth.AppId.ReadRoleIdAsync(roleName, mount);
        SecretIdInfo secretId = await Client.Auth.AppId.GenerateSecretIdAsync(roleName, mount: mount);

        using BastionVaultClient anon = Server.CreateClient(o => o.Token = null!);
        BastionVaultException denied = await Assert.ThrowsAsync<BastionVaultException>(
            () => anon.Auth.AppId.LoginAsync(roleId, secretId.SecretId, mount: mount));
        Assert.Equal(ErrorCodes.AuthzPermissionDenied, denied.Code);
    }
}

/// <summary>ITG-S10: token store (15-testing-requirements.md:212-216).</summary>
public sealed class Scenario10_TokenStore : IntegrationTest
{
    [IntegrationFact]
    public async Task Create_lookup_renew_revoke_and_the_reserved_meta_guard_all_map_correctly()
    {
        string policyName = await Resources.WritePolicyAsync("renewable", """
            path "auth/token/renew/*" {
              capabilities = ["update"]
            }
            """);

        AuthInfo child = await Client.Auth.Token.CreateAsync(
            new CreateTokenRequest { Policies = [policyName], Renewable = true });
        Resources.TrackToken("child", child.ClientToken);

        TokenInfo looked = await Client.Auth.Token.LookupAsync(child.ClientToken.Reveal()!);
        Assert.Contains(policyName, looked.Policies);

        using BastionVaultClient asChild = Server.CreateClient(o => o.Token = child.ClientToken.Reveal());
        TokenInfo beforeRenew = await asChild.Auth.Token.LookupSelfAsync();
        AuthInfo renewed = await asChild.Auth.Token.RenewSelfAsync(increment: 0);
        Assert.True(renewed.ClientToken.HasValue);
        TokenInfo afterRenew = await asChild.Auth.Token.LookupSelfAsync();
        Assert.True(
            afterRenew.RemainingTtl is null || beforeRenew.RemainingTtl is null
                || afterRenew.RemainingTtl >= beforeRenew.RemainingTtl - TimeSpan.FromSeconds(2),
            "RenewSelf should not shorten the remaining TTL");

        await Client.Auth.Token.RevokeAsync(child.ClientToken.Reveal()!);
        BastionVaultException notFound = await Assert.ThrowsAsync<BastionVaultException>(
            () => Client.Auth.Token.LookupAsync(child.ClientToken.Reveal()!));
        Assert.Equal(ErrorCodes.NotFoundTokenNotFound, notFound.Code);
        Resources.Track("token-already-revoked", "child", _ => Task.CompletedTask);

        AuthInfo nonRoot = await Client.Auth.Token.CreateAsync(new CreateTokenRequest { Policies = ["default"] });
        using BastionVaultClient asNonRoot = Server.CreateClient(o => o.Token = nonRoot.ClientToken.Reveal());
        await asNonRoot.Auth.Token.RevokeSelfAsync();

        // RevokeSelfAsync's own doc comment: the client clears its local token unconditionally, so
        // the very next call has none to send and fails client-side (BV-AUTH-001), before the
        // (also-revoked) token would have reached the server as BV-AUTHZ-001.
        BastionVaultException afterSelfRevoke = await Assert.ThrowsAsync<BastionVaultException>(
            () => asNonRoot.Auth.Token.LookupSelfAsync());
        Assert.Equal(ErrorCodes.AuthNoToken, afterSelfRevoke.Code);

        CreateTokenRequest reservedMetaRequest = new()
        {
            Policies = ["default"],
            Meta = new Dictionary<string, string> { ["username"] = "x" },
        };
        BastionVaultException clientSideRefusal = await Assert.ThrowsAsync<BastionVaultException>(
            () => Client.Auth.Token.CreateAsync(reservedMetaRequest));
        Assert.Equal(ErrorCodes.InputReservedTokenMetaKey, clientSideRefusal.Code);

        BastionVaultException serverSideRefusal = await Assert.ThrowsAsync<BastionVaultException>(
            () => Client.Logical.WriteAsync(
                "auth/token/create",
                JsonDocument.Parse("""{"policies":["default"],"meta":{"username":"x"}}""").RootElement));
        Assert.Equal(400, serverSideRefusal.StatusCode);
    }
}

/// <summary>ITG-S11: auto-renew, real clock (15-testing-requirements.md:217-218).</summary>
public sealed class Scenario11_AutoRenew : IntegrationTest
{
    [IntegrationFact]
    public async Task At_least_one_renewal_fires_before_the_short_lived_token_would_expire()
    {
        // A Login source is required: CredentialIssued (which the renewal loop awaits) is only
        // published by ClientContext.RecordLogin, which Auth.Token.CreateAsync never calls.
        string mount = await Resources.EnableAuthAsync("approle", "autorenew");
        _ = await Client.Auth.AppId.Admin.WriteConfigAsync(
            JsonDocument.Parse("""{"require_machine":false}""").RootElement, mount);

        string policyName = await Resources.WritePolicyAsync("renewable", """
            path "auth/token/renew/*" {
              capabilities = ["update"]
            }
            """);

        string roleName = Resources.Name("role");
        _ = await Client.Auth.AppId.Admin.WriteRoleAsync(
            roleName,
            JsonDocument.Parse($$"""{"policies":["default","{{policyName}}"],"bind_secret_id":true,"token_ttl":4}""").RootElement,
            mount);

        string roleId = await Client.Auth.AppId.ReadRoleIdAsync(roleName, mount);
        SecretIdInfo secretId = await Client.Auth.AppId.GenerateSecretIdAsync(roleName, mount: mount);

        List<RenewalEvent> renewed = [];
        List<RenewalEvent> failed = [];
        using ManualResetEventSlim renewedSignal = new(false);

        using BastionVaultClient renewingClient = Server.CreateClient(o =>
        {
            o.Token = null!;
            o.AutoRenew = new AutoRenewPolicy
            {
                Enabled = true,
                RenewAtFraction = 0.5,
                MinInterval = TimeSpan.FromMilliseconds(200),
                OnRenewed = e => { lock (renewed) { renewed.Add(e); } renewedSignal.Set(); },
                OnFailed = e => { lock (failed) { failed.Add(e); } },
            };
        });

        AuthInfo login = await renewingClient.Auth.AppId.LoginAsync(roleId, secretId.SecretId, mount: mount);
        Resources.TrackToken("autorenew-session", login.ClientToken);

        bool sawRenewal = renewedSignal.Wait(TimeSpan.FromSeconds(6));

        List<RenewalEvent> failedSnapshot;
        lock (failed)
        {
            failedSnapshot = [.. failed];
        }

        Assert.True(
            sawRenewal,
            "expected at least one OnRenewed event within 6s; " +
            $"{failedSnapshot.Count} OnFailed event(s) instead: " +
            string.Join("; ", failedSnapshot.Select(f => $"{f.Error?.Code}: {f.Error?.ServerMessage}")));

        await Task.Delay(TimeSpan.FromSeconds(1));
        using BastionVaultClient stillValid = Server.CreateClient(o => o.Token = login.ClientToken.Reveal());
        TokenInfo info = await stillValid.Auth.Token.LookupSelfAsync();
        Assert.True(info.RemainingTtl is null || info.RemainingTtl > TimeSpan.Zero);
    }
}

/// <summary>ITG-S12: FerroGate, Complete (15-testing-requirements.md:219-222).</summary>
public sealed class Scenario12_FerroGate : IntegrationTest
{
    [IntegrationFact]
    public async Task Requirement_enroll_and_status_all_map_correctly()
    {
        string mount = await Resources.EnableAuthAsync("ferrogate", "fg");

        using BastionVaultClient anon = Server.CreateClient(o => o.Token = null!);
        FerrogateRequirement requirement = await anon.Auth.Ferrogate.RequirementAsync(mount);
        Assert.False(requirement.RequireMachineIdentity);

        _ = await Client.Auth.Ferrogate.Admin.WriteConfigAsync(
            JsonDocument.Parse("""{"self_enroll_enabled":true}""").RootElement, mount);

        string spiffeId = $"spiffe://example.org/{Unique("machine")}";
        EnrollResult enrolled = await anon.Auth.Ferrogate.EnrollAsync(spiffeId, "scenario12", mount);
        Assert.Equal(MachineIdentityStatus.Pending, enrolled.Status);

        IReadOnlyList<string> machines = await Client.Auth.Ferrogate.Admin.ListMachinesAsync(mount);
        Assert.NotEmpty(machines);

        MachineStatus status = await anon.Auth.Ferrogate.StatusAsync(new SecretString("not-a-real-token"), mount: mount);
        Assert.Equal(MachineIdentityStatus.Unknown, status.Status);
    }
}
