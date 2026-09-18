using System.Globalization;
using System.Text.Json;
using System.Xml;
using BastionVault.IntegrationSdk;

namespace BastionVault.IntegrationSdk.Tests.Harness.Operations;

/// <summary>
/// Registers the M2a <c>Auth.*</c> fixture operations against the real SDK: the token-store
/// operations of <c>05-authentication.md</c> (AUT-020, AUT-080…AUT-085) driven through the real
/// <see cref="BastionVaultClient"/> and the fixture's <see cref="ScriptedTransport"/>, never a
/// test-only shim (D-M0-2, D-M1a-6).
/// </summary>
/// <remarks>
/// M2b added the login operations (<c>Auth.Userpass.Login</c>, <c>Auth.AppId.Login</c>), driven the
/// same way. M6 adds the section-05 remainder: AUT-035's FIDO2 pair on both mounts,
/// AUT-050…AUT-054's FerroGate flows and administration, AUT-060's OIDC and SAML, AUT-070's
/// <c>Auth.Cert.Login</c> and AUT-043's AppID role administration. Every one of them is registered,
/// including the twenty-odd administration operations no fixture drives yet, because the registry
/// is also the cross-language operation vocabulary a later Rust or Python fixture names
/// (D-M6-11).
/// </remarks>
public static class AuthFixtureOperations
{
    /// <summary>The operation names the section-05 fixtures drive, plus those with no fixture of their own.</summary>
    public static void Register(OperationRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Register("Auth.Userpass.Login", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => AuthInfoResult(await client.Auth.Userpass.LoginAsync(
                args.GetProperty("username").GetString()!,
                new SecretString(args.GetProperty("password").GetString()),
                OptionalString(args, "totpCode"),
                OptionalString(args, "mount") ?? "userpass",
                options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Auth.AppId.Login", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => AuthInfoResult(await client.Auth.AppId.LoginAsync(
                args.GetProperty("roleId").GetString()!,
                OptionalSecret(args, "secretId"),
                OptionalSecret(args, "machineToken"),
                OptionalString(args, "mount") ?? "approle",
                options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Auth.AppId.ReadRoleId", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => await client.Auth.AppId.ReadRoleIdAsync(
                args.GetProperty("roleName").GetString()!,
                OptionalString(args, "mount") ?? "approle",
                options).ConfigureAwait(false)).ConfigureAwait(false);
        });

        registry.Register("Auth.AutoRenew.Run", RunAutoRenewAsync);

        registry.Register("Auth.Token.LookupSelf", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => TokenInfoResult(await client.Auth.Token.LookupSelfAsync(options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Auth.Token.Lookup", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            string token = invocation.Arguments.GetProperty("token").GetString()!;
            return await RunAsync(client, async () => TokenInfoResult(await client.Auth.Token.LookupAsync(token, options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Auth.Token.Verify", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => TokenInfoResult(await client.Auth.Token.VerifyAsync(options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Auth.Token.RenewSelf", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            int increment = invocation.Arguments.GetProperty("increment").GetInt32();
            return await RunAsync(client, async () => AuthInfoResult(await client.Auth.Token.RenewSelfAsync(increment, options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Auth.Token.Renew", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            string token = invocation.Arguments.GetProperty("token").GetString()!;
            int increment = invocation.Arguments.GetProperty("increment").GetInt32();
            return await RunAsync(client, async () => AuthInfoResult(await client.Auth.Token.RenewAsync(token, increment, options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Auth.Token.Create", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            CreateTokenRequest request = ParseCreateRequest(invocation.Arguments.GetProperty("request"));
            return await RunAsync(client, async () => AuthInfoResult(await client.Auth.Token.CreateAsync(request, options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Auth.Token.RevokeSelf", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () =>
            {
                await client.Auth.Token.RevokeSelfAsync(options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Auth.Token.Revoke", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            string token = invocation.Arguments.GetProperty("token").GetString()!;
            return await RunAsync(client, async () =>
            {
                await client.Auth.Token.RevokeAsync(token, options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Auth.Token.RevokeOrphan", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            string token = invocation.Arguments.GetProperty("token").GetString()!;
            return await RunAsync(client, async () =>
            {
                await client.Auth.Token.RevokeOrphanAsync(token, options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Auth.Token.AuditLogin", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () =>
            {
                await client.Auth.Token.AuditLoginAsync(options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        RegisterM6(registry);
    }

    /// <summary>
    /// M6's section-05 remainder (AUT-035, AUT-043, AUT-050…AUT-054, AUT-060, AUT-070), registered
    /// through one adapter so every handler resolves its client, its options and its error the
    /// same way the M2 handlers above do.
    /// </summary>
    private static void RegisterM6(OperationRegistry registry)
    {
        // ---- AUT-035: FIDO2, on the userpass mount and on the standalone one. ----
        Register(registry, "Auth.Userpass.Fido2LoginBegin", async (client, args, options) => AssertionResult(
            await client.Auth.Userpass.Fido2LoginBeginAsync(
                Required(args, "username"), OptionalString(args, "mount") ?? "userpass", options).ConfigureAwait(false)));

        Register(registry, "Auth.Userpass.Fido2LoginComplete", async (client, args, options) => AuthInfoResult(
            await client.Auth.Userpass.Fido2LoginCompleteAsync(
                Required(args, "username"), RawJson(args, "credential"), OptionalString(args, "mount") ?? "userpass", options).ConfigureAwait(false)));

        Register(registry, "Auth.Fido2.LoginBegin", async (client, args, options) => AssertionResult(
            await client.Auth.Fido2.LoginBeginAsync(
                Required(args, "username"), OptionalString(args, "mount") ?? "fido2", options).ConfigureAwait(false)));

        Register(registry, "Auth.Fido2.LoginComplete", async (client, args, options) => AuthInfoResult(
            await client.Auth.Fido2.LoginCompleteAsync(
                Required(args, "username"), RawJson(args, "credential"), OptionalString(args, "mount") ?? "fido2", options).ConfigureAwait(false)));

        // ---- AUT-050…AUT-054: FerroGate. ----
        Register(registry, "Auth.Ferrogate.Requirement", async (client, args, options) => RequirementResult(
            await client.Auth.Ferrogate.RequirementAsync(Mount(args, "ferrogate"), options).ConfigureAwait(false)));

        Register(registry, "Auth.Ferrogate.IsMachineIdentityRequired", async (client, args, options) =>
            await client.Auth.Ferrogate.IsMachineIdentityRequiredAsync(Mount(args, "ferrogate"), options).ConfigureAwait(false));

        Register(registry, "Auth.Ferrogate.Login", async (client, args, options) => AuthInfoResult(
            await client.Auth.Ferrogate.LoginAsync(
                new SecretString(Required(args, "childToken")),
                OptionalString(args, "dpopProof"),
                OptionalSecret(args, "userToken"),
                Mount(args, "ferrogate"),
                options).ConfigureAwait(false)));

        Register(registry, "Auth.Ferrogate.Status", async (client, args, options) => StatusResult(
            await client.Auth.Ferrogate.StatusAsync(
                new SecretString(Required(args, "childToken")),
                OptionalString(args, "dpopProof"),
                Mount(args, "ferrogate"),
                options).ConfigureAwait(false)));

        Register(registry, "Auth.Ferrogate.Enroll", async (client, args, options) => EnrollResultOf(
            await client.Auth.Ferrogate.EnrollAsync(
                Required(args, "spiffeId"), OptionalString(args, "comment"), Mount(args, "ferrogate"), options).ConfigureAwait(false)));

        Register(registry, "Auth.Ferrogate.Admin.ReadConfig", async (client, args, options) => DataResult(
            await client.Auth.Ferrogate.Admin.ReadConfigAsync(Mount(args, "ferrogate"), options).ConfigureAwait(false)));

        Register(registry, "Auth.Ferrogate.Admin.WriteConfig", async (client, args, options) => DataResult(
            await client.Auth.Ferrogate.Admin.WriteConfigAsync(Document(args, "config"), Mount(args, "ferrogate"), options).ConfigureAwait(false)));

        Register(registry, "Auth.Ferrogate.Admin.Register", async (client, args, options) => DataResult(
            await client.Auth.Ferrogate.Admin.RegisterAsync(Document(args, "machine"), Mount(args, "ferrogate"), options).ConfigureAwait(false)));

        Register(registry, "Auth.Ferrogate.Admin.ListMachines", async (client, args, options) => Keys(
            await client.Auth.Ferrogate.Admin.ListMachinesAsync(Mount(args, "ferrogate"), options).ConfigureAwait(false)));

        Register(registry, "Auth.Ferrogate.Admin.ReadMachine", async (client, args, options) => DataResult(
            await client.Auth.Ferrogate.Admin.ReadMachineAsync(Required(args, "machineId"), Mount(args, "ferrogate"), options).ConfigureAwait(false)));

        Register(registry, "Auth.Ferrogate.Admin.DeleteMachine", async (client, args, options) =>
        {
            await client.Auth.Ferrogate.Admin.DeleteMachineAsync(Required(args, "machineId"), Mount(args, "ferrogate"), options).ConfigureAwait(false);
            return null;
        });

        Register(registry, "Auth.Ferrogate.Admin.Approve", async (client, args, options) => DataResult(
            await client.Auth.Ferrogate.Admin.ApproveAsync(Required(args, "machineId"), Mount(args, "ferrogate"), options).ConfigureAwait(false)));

        Register(registry, "Auth.Ferrogate.Admin.Reject", async (client, args, options) => DataResult(
            await client.Auth.Ferrogate.Admin.RejectAsync(Required(args, "machineId"), Mount(args, "ferrogate"), options).ConfigureAwait(false)));

        Register(registry, "Auth.Ferrogate.Admin.Revoke", async (client, args, options) => DataResult(
            await client.Auth.Ferrogate.Admin.RevokeAsync(Required(args, "machineId"), Mount(args, "ferrogate"), options).ConfigureAwait(false)));

        // ---- AUT-060: OIDC and SAML. ----
        Register(registry, "Auth.Oidc.AuthUrl", async (client, args, options) =>
            await client.Auth.Oidc.AuthUrlAsync(
                Required(args, "redirectUri"), OptionalString(args, "role"), Mount(args, "oidc"), options).ConfigureAwait(false));

        Register(registry, "Auth.Oidc.Callback", async (client, args, options) => AuthInfoResult(
            await client.Auth.Oidc.CallbackAsync(
                Required(args, "state"), Required(args, "code"), Mount(args, "oidc"), options).ConfigureAwait(false)));

        Register(registry, "Auth.Saml.Login", async (client, args, options) => SamlResult(
            await client.Auth.Saml.LoginAsync(
                OptionalString(args, "redirectUri"), OptionalString(args, "role"), Mount(args, "saml"), options).ConfigureAwait(false)));

        Register(registry, "Auth.Saml.Callback", async (client, args, options) => AuthInfoResult(
            await client.Auth.Saml.CallbackAsync(
                Required(args, "samlResponse"), Required(args, "relayState"), Mount(args, "saml"), options).ConfigureAwait(false)));

        RegisterRoleAdmin(registry, "Auth.Oidc.Admin", client => client.Auth.Oidc.Admin);
        RegisterRoleAdmin(registry, "Auth.Saml.Admin", client => client.Auth.Saml.Admin);

        // ---- AUT-070: the disabled certificate backend. ----
        Register(registry, "Auth.Cert.Login", async (client, args, options) => AuthInfoResult(
            await client.Auth.Cert.LoginAsync(Mount(args, "cert"), options).ConfigureAwait(false)));

        RegisterAppIdAdmin(registry);
    }

    /// <summary>AUT-060's shared OIDC/SAML admin surface, registered once per mount prefix (D-M6-6).</summary>
    private static void RegisterRoleAdmin(OperationRegistry registry, string prefix, Func<BastionVaultClient, AuthRoleAdminOperations> select)
    {
        Register(registry, $"{prefix}.ReadConfig", async (client, args, options) => DataResult(
            await select(client).ReadConfigAsync(OptionalString(args, "mount"), options).ConfigureAwait(false)));

        Register(registry, $"{prefix}.WriteConfig", async (client, args, options) => DataResult(
            await select(client).WriteConfigAsync(Document(args, "config"), OptionalString(args, "mount"), options).ConfigureAwait(false)));

        Register(registry, $"{prefix}.ListRoles", async (client, args, options) => Keys(
            await select(client).ListRolesAsync(OptionalString(args, "mount"), options).ConfigureAwait(false)));

        Register(registry, $"{prefix}.ReadRole", async (client, args, options) => DataResult(
            await select(client).ReadRoleAsync(Required(args, "name"), OptionalString(args, "mount"), options).ConfigureAwait(false)));

        Register(registry, $"{prefix}.WriteRole", async (client, args, options) => DataResult(
            await select(client).WriteRoleAsync(Required(args, "name"), Document(args, "role"), OptionalString(args, "mount"), options).ConfigureAwait(false)));

        Register(registry, $"{prefix}.DeleteRole", async (client, args, options) =>
        {
            await select(client).DeleteRoleAsync(Required(args, "name"), OptionalString(args, "mount"), options).ConfigureAwait(false);
            return null;
        });
    }

    /// <summary>AUT-043's AppID role administration.</summary>
    private static void RegisterAppIdAdmin(OperationRegistry registry)
    {
        Register(registry, "Auth.AppId.Admin.ListRoles", async (client, args, options) => Keys(
            await client.Auth.AppId.Admin.ListRolesAsync(Mount(args, "approle"), options).ConfigureAwait(false)));

        Register(registry, "Auth.AppId.Admin.ReadRole", async (client, args, options) => DataResult(
            await client.Auth.AppId.Admin.ReadRoleAsync(Role(args), Mount(args, "approle"), options).ConfigureAwait(false)));

        Register(registry, "Auth.AppId.Admin.WriteRole", async (client, args, options) => DataResult(
            await client.Auth.AppId.Admin.WriteRoleAsync(Role(args), Document(args, "role"), Mount(args, "approle"), options).ConfigureAwait(false)));

        Register(registry, "Auth.AppId.Admin.DeleteRole", async (client, args, options) =>
        {
            await client.Auth.AppId.Admin.DeleteRoleAsync(Role(args), Mount(args, "approle"), options).ConfigureAwait(false);
            return null;
        });

        Register(registry, "Auth.AppId.Admin.WriteRoleId", async (client, args, options) => DataResult(
            await client.Auth.AppId.Admin.WriteRoleIdAsync(Role(args), Required(args, "roleId"), Mount(args, "approle"), options).ConfigureAwait(false)));

        Register(registry, "Auth.AppId.Admin.ReadField", async (client, args, options) => DataResult(
            await client.Auth.AppId.Admin.ReadFieldAsync(Role(args), Field(args), Mount(args, "approle"), options).ConfigureAwait(false)));

        Register(registry, "Auth.AppId.Admin.WriteField", async (client, args, options) => DataResult(
            await client.Auth.AppId.Admin.WriteFieldAsync(Role(args), Field(args), Document(args, "value"), Mount(args, "approle"), options).ConfigureAwait(false)));

        Register(registry, "Auth.AppId.Admin.DeleteField", async (client, args, options) =>
        {
            await client.Auth.AppId.Admin.DeleteFieldAsync(Role(args), Field(args), Mount(args, "approle"), options).ConfigureAwait(false);
            return null;
        });

        Register(registry, "Auth.AppId.Admin.ReadLocalSecretIds", async (client, args, options) => DataResult(
            await client.Auth.AppId.Admin.ReadLocalSecretIdsAsync(Role(args), Mount(args, "approle"), options).ConfigureAwait(false)));

        Register(registry, "Auth.AppId.Admin.ListSecretIdAccessors", async (client, args, options) => Keys(
            await client.Auth.AppId.Admin.ListSecretIdAccessorsAsync(Role(args), Mount(args, "approle"), options).ConfigureAwait(false)));

        Register(registry, "Auth.AppId.Admin.LookupSecretId", async (client, args, options) => DataResult(
            await client.Auth.AppId.Admin.LookupSecretIdAsync(Role(args), new SecretString(Required(args, "secretId")), Mount(args, "approle"), options).ConfigureAwait(false)));

        Register(registry, "Auth.AppId.Admin.DestroySecretId", async (client, args, options) =>
        {
            await client.Auth.AppId.Admin.DestroySecretIdAsync(Role(args), new SecretString(Required(args, "secretId")), Mount(args, "approle"), options).ConfigureAwait(false);
            return null;
        });

        Register(registry, "Auth.AppId.Admin.LookupSecretIdAccessor", async (client, args, options) => DataResult(
            await client.Auth.AppId.Admin.LookupSecretIdAccessorAsync(Role(args), Required(args, "accessor"), Mount(args, "approle"), options).ConfigureAwait(false)));

        Register(registry, "Auth.AppId.Admin.DestroySecretIdAccessor", async (client, args, options) =>
        {
            await client.Auth.AppId.Admin.DestroySecretIdAccessorAsync(Role(args), Required(args, "accessor"), Mount(args, "approle"), options).ConfigureAwait(false);
            return null;
        });

        Register(registry, "Auth.AppId.Admin.CustomSecretId", async (client, args, options) => DataResult(
            await client.Auth.AppId.Admin.CustomSecretIdAsync(Role(args), new SecretString(Required(args, "secretId")), Mount(args, "approle"), options).ConfigureAwait(false)));

        Register(registry, "Auth.AppId.Admin.ListMachines", async (client, args, options) => Keys(
            await client.Auth.AppId.Admin.ListMachinesAsync(Role(args), Mount(args, "approle"), options).ConfigureAwait(false)));

        Register(registry, "Auth.AppId.Admin.BindMachine", async (client, args, options) => DataResult(
            await client.Auth.AppId.Admin.BindMachineAsync(Role(args), Document(args, "machine"), Mount(args, "approle"), options).ConfigureAwait(false)));

        Register(registry, "Auth.AppId.Admin.ReadMachine", async (client, args, options) => DataResult(
            await client.Auth.AppId.Admin.ReadMachineAsync(Role(args), Required(args, "machineId"), Mount(args, "approle"), options).ConfigureAwait(false)));

        Register(registry, "Auth.AppId.Admin.UnbindMachine", async (client, args, options) =>
        {
            await client.Auth.AppId.Admin.UnbindMachineAsync(Role(args), Required(args, "machineId"), Mount(args, "approle"), options).ConfigureAwait(false);
            return null;
        });

        Register(registry, "Auth.AppId.Admin.ReadConfig", async (client, args, options) => DataResult(
            await client.Auth.AppId.Admin.ReadConfigAsync(Mount(args, "approle"), options).ConfigureAwait(false)));

        Register(registry, "Auth.AppId.Admin.WriteConfig", async (client, args, options) => DataResult(
            await client.Auth.AppId.Admin.WriteConfigAsync(Document(args, "config"), Mount(args, "approle"), options).ConfigureAwait(false)));

        Register(registry, "Auth.AppId.Admin.TidySecretIds", async (client, args, options) => DataResult(
            await client.Auth.AppId.Admin.TidySecretIdsAsync(Mount(args, "approle"), options).ConfigureAwait(false)));
    }

    /// <summary>
    /// The one adapter every M6 handler goes through: builds the real client from the fixture's
    /// <c>client</c> block, runs the call, and turns a <see cref="BastionVaultException"/> into the
    /// fixture's <c>expect.error</c> shape.
    /// </summary>
    private static void Register(
        OperationRegistry registry,
        string name,
        Func<BastionVaultClient, JsonElement, RequestOptions, Task<object?>> call)
    {
        registry.Register(name, async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, () => call(client, args, options)).ConfigureAwait(false);
        });
    }

    private static string Required(JsonElement args, string name)
    {
        return OptionalString(args, name)
            ?? throw new FixtureAssertionException($"the operation requires a string argument '{name}'.");
    }

    private static string Mount(JsonElement args, string fallback)
    {
        return OptionalString(args, "mount") ?? fallback;
    }

    private static string Role(JsonElement args)
    {
        return Required(args, "roleName");
    }

    private static AppIdRoleField Field(JsonElement args)
    {
        return Enum.Parse<AppIdRoleField>(Required(args, "field"), ignoreCase: true);
    }

    /// <summary>A JSON document argument, passed to the SDK verbatim.</summary>
    private static JsonElement Document(JsonElement args, string name)
    {
        return args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out JsonElement value)
            ? value
            : throw new FixtureAssertionException($"the operation requires a JSON argument '{name}'.");
    }

    /// <summary>AUT-035's opaque credential: the fixture's JSON, re-serialised to the text the SDK takes.</summary>
    private static string RawJson(JsonElement args, string name)
    {
        return Document(args, name).GetRawText();
    }

    private static object AssertionResult(WebAuthnAssertionOptions assertion)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal) { ["Json"] = assertion.Json };
    }

    private static object RequirementResult(FerrogateRequirement requirement)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["RequireMachineIdentity"] = requirement.RequireMachineIdentity,
            ["ExpectedAudience"] = requirement.ExpectedAudience,
            ["TrustDomain"] = requirement.TrustDomain,
            ["MiaEnvironment"] = requirement.MiaEnvironment,
        };
    }

    private static object StatusResult(MachineStatus status)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal) { ["Status"] = status.Status.ToString() };
    }

    private static object EnrollResultOf(EnrollResult result)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal) { ["Status"] = result.Status.ToString() };
    }

    private static object SamlResult(SamlLoginRequest request)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["SsoUrl"] = request.SsoUrl,
            ["RelayState"] = request.RelayState,
            ["RequestId"] = request.RequestId,
        };
    }

    private static object Keys(IReadOnlyList<string> keys)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Keys"] = keys.Select(key => (object?)key).ToArray(),
        };
    }

    /// <summary>
    /// The <c>data</c> object of an administration response, which is the whole assertable surface
    /// of an operation the specification gives no typed shape (D-M6-5).
    /// </summary>
    private static object? DataResult(Response? response)
    {
        return response?.Data is { } data
            ? data.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal)
            : null;
    }

    /// <summary>
    /// AUT-090…AUT-094's loop, driven end to end: one login to give the loop the credential
    /// AUT-090 schedules from, then the loop itself, until the scripted exchanges run out and
    /// D-M2-27 item 7's cut-off disposes the client.
    /// </summary>
    /// <remarks>
    /// The renewal callbacks are captured here rather than declared in the fixture because a JSON
    /// document cannot express a delegate. What the fixture asserts is their <i>record</i>: how
    /// many renewals succeeded, how many failed, the lease each renewal returned (AUT-091), and the
    /// single <c>OnStopped</c> reason (AUT-092, AUT-094).
    /// </remarks>
    private static async ValueTask<FixtureOperationResult> RunAutoRenewAsync(FixtureInvocation invocation)
    {
        List<int> renewedLeases = [];
        List<string> failureCodes = [];
        RenewalStoppedReason? stopped = null;
        AutoRenewPolicy declared = FixtureClientBuilder.ReadAutoRenew(invocation.Configuration.Settings)
            ?? new AutoRenewPolicy { Enabled = true };
        AutoRenewPolicy policy = declared with
        {
            OnRenewed = renewal => renewedLeases.Add((int)(renewal.Auth?.LeaseDuration ?? TimeSpan.Zero).TotalSeconds),
            OnFailed = renewal => failureCodes.Add(renewal.Error?.Code ?? string.Empty),
            // AUT-092/AUT-094: exactly one reason per loop. Recorded with a first-write-wins
            // assignment so a second emission would be visible as a harness failure rather than
            // silently overwriting the first.
            OnStopped = reason => stopped = stopped is null
                ? reason
                : throw new FixtureAssertionException($"the renewal loop stopped twice ({stopped} then {reason})."),
        };

        ScriptedTransportAdapter adapter = new(invocation.Transport);
        BastionVaultClientOptions clientOptions = FixtureClientBuilder.BuildOptions(
            invocation.Configuration, adapter, invocation.Instruments, policy);
        EnvironmentSource environment = invocation.Configuration.Environment.Count > 0
            ? EnvironmentSource.FromMap(invocation.Configuration.Environment)
            : EnvironmentSource.None;

        using CancellationTokenSource operation = new();
        using BastionVaultClient client = new(clientOptions, environment);
        // D-M2-27 item 7: the driver's cut-off, wired to the *real* AUT-094 path — the operation's
        // token source cancels, and its registration disposes the client, which is what stops the
        // loop. Not a test-only escape hatch.
        _ = operation.Token.Register(client.Dispose);
        invocation.Transport.Exhausted += operation.Cancel;

        JsonElement args = invocation.Arguments;
        FixtureError? error = null;
        try
        {
            _ = await client.Auth.Userpass.LoginAsync(
                args.GetProperty("username").GetString()!,
                new SecretString(args.GetProperty("password").GetString()),
                OptionalString(args, "totpCode"),
                OptionalString(args, "mount") ?? "userpass").ConfigureAwait(false);

            // A wall-clock backstop against a loop that never stops at all. It is emphatically not
            // the anti-spin guard — D-M2-27 rejected wall-clock time for that, because virtual time
            // makes a spin *fast* — it is only what turns a wedged suite into a failing test.
            await client.RenewalCompletion!.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        }
        catch (BastionVaultException failure)
        {
            error = FixtureErrors.From(failure);
        }
        catch (TimeoutException)
        {
            throw new FixtureAssertionException(
                "the renewal loop did not stop within 30 s of wall-clock time after the scripted exchanges ran out.");
        }

        return new FixtureOperationResult(
            Result: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Renewed"] = renewedLeases.Count,
                ["RenewedLeaseDurations"] = renewedLeases.Select(lease => (object?)lease).ToArray(),
                ["Failed"] = failureCodes.Count,
                ["FailureCodes"] = failureCodes.Select(code => (object?)code).ToArray(),
                ["Stopped"] = stopped?.ToString(),
            },
            Error: error,
            ClientState: ClientState(client));
    }

    private static (BastionVaultClient Client, RequestOptions Options) Build(FixtureInvocation invocation)
    {
        ScriptedTransportAdapter adapter = new(invocation.Transport);
        BastionVaultClientOptions clientOptions = FixtureClientBuilder.BuildOptions(invocation.Configuration, adapter, invocation.Instruments);
        EnvironmentSource environment = invocation.Configuration.Environment.Count > 0
            ? EnvironmentSource.FromMap(invocation.Configuration.Environment)
            : EnvironmentSource.None;
        return (new BastionVaultClient(clientOptions, environment), new RequestOptions());
    }

    private static async ValueTask<FixtureOperationResult> RunAsync(BastionVaultClient client, Func<Task<object?>> call)
    {
        try
        {
            object? result = await call().ConfigureAwait(false);
            return new FixtureOperationResult(Result: result, ClientState: ClientState(client));
        }
        catch (BastionVaultException exception)
        {
            return new FixtureOperationResult(Error: FixtureErrors.From(exception), ClientState: ClientState(client));
        }
    }

    /// <summary>
    /// Projects <see cref="TokenInfo"/> onto the fixture's assertion surface. Durations render as
    /// ISO 8601 because that is how the fixtures spell them (<c>"RemainingTtl": "PT1H"</c>), and
    /// <see cref="TokenInfo.Id"/> renders through <see cref="RedactedValue"/> so a fixture can
    /// assert <c>$redacted</c> on it rather than its value.
    /// </summary>
    private static object TokenInfoResult(TokenInfo info)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Id"] = info.Id is { } id ? new RedactedValue(id.Reveal() ?? string.Empty) : null,
            ["Policies"] = info.Policies,
            ["Path"] = info.Path,
            ["Meta"] = info.Meta?.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal),
            ["DisplayName"] = info.DisplayName,
            ["NumUses"] = info.NumUses,
            ["CreationTime"] = info.CreationTime?.ToUnixTimeSeconds(),
            ["CreationTtl"] = XmlConvert.ToString(info.CreationTtl),
            ["ExplicitMaxTtl"] = XmlConvert.ToString(info.ExplicitMaxTtl),
            ["Period"] = info.Period is { } period ? XmlConvert.ToString(period) : null,
            ["RemainingTtl"] = info.RemainingTtl is { } remaining ? XmlConvert.ToString(remaining) : null,
        };
    }

    /// <summary>
    /// Projects <see cref="AuthInfo"/> onto the fixture assertion surface. <c>IssuedAt</c> renders
    /// in the same <c>2026-09-13T12:00:00Z</c> spelling the fixtures' <c>clock.start</c> uses
    /// (AUT-013), and AUT-044's derived <c>EnvironmentScope</c> is projected as an object so
    /// <c>auth.appid.env-scope-derived</c> can assert its three members.
    /// </summary>
    private static object AuthInfoResult(AuthInfo auth)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["ClientToken"] = new RedactedValue(auth.ClientToken.Reveal() ?? string.Empty),
            ["Policies"] = auth.Policies,
            ["Metadata"] = auth.Metadata?.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal),
            ["LeaseDuration"] = auth.LeaseDuration is { } lease ? (int)lease.TotalSeconds : null,
            ["Renewable"] = auth.Renewable,
            ["IssuedAt"] = auth.IssuedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["EnvironmentScope"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Scoped"] = auth.EnvironmentScope.Scoped,
                ["SecretGlobs"] = auth.EnvironmentScope.SecretGlobs,
                ["MachineGlobs"] = auth.EnvironmentScope.MachineGlobs,
            },
        };
    }

    private static string? OptionalString(JsonElement args, string name)
    {
        return args.ValueKind == JsonValueKind.Object
                && args.TryGetProperty(name, out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static SecretString? OptionalSecret(JsonElement args, string name)
    {
        return OptionalString(args, name) is { } value ? new SecretString(value) : null;
    }

    /// <summary>
    /// The client state M2a's fixtures assert. <c>Auth.CurrentToken</c> is what
    /// <c>auth.token.revoke-self-clears-token</c> checks is <c>$absent</c> after AUT-083 clears it.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> ClientState(BastionVaultClient client)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["RateGate.Paused"] = client.RateGateState.Paused,
            ["Auth.CurrentToken"] = client.Auth.CurrentToken is { } token ? new RedactedValue(token.Reveal() ?? string.Empty) : null,
            ["Auth.TokenSource.Kind"] = client.Auth.TokenSource.Kind.ToString(),
        };
    }

    private static CreateTokenRequest ParseCreateRequest(JsonElement element)
    {
        CreateTokenRequest request = new();
        if (element.TryGetProperty("Policies", out JsonElement policies) && policies.ValueKind == JsonValueKind.Array)
        {
            request = request with { Policies = policies.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray() };
        }

        if (element.TryGetProperty("Meta", out JsonElement meta) && meta.ValueKind == JsonValueKind.Object)
        {
            request = request with
            {
                Meta = meta.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty, StringComparer.Ordinal),
            };
        }

        if (element.TryGetProperty("Ttl", out JsonElement ttl) && ttl.ValueKind == JsonValueKind.String)
        {
            request = request with { Ttl = XmlConvert.ToTimeSpan(ttl.GetString()!) };
        }

        if (element.TryGetProperty("DisplayName", out JsonElement displayName) && displayName.ValueKind == JsonValueKind.String)
        {
            request = request with { DisplayName = displayName.GetString() };
        }

        if (element.TryGetProperty("UseResult", out JsonElement useResult) && useResult.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            request = request with { UseResult = useResult.GetBoolean() };
        }

        return request;
    }
}
