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
/// M2b adds the login operations (<c>Auth.Userpass.Login</c>, <c>Auth.AppId.Login</c>), driven the
/// same way. AUT-035's FIDO2 pair, AUT-050…AUT-054's FerroGate flows, AUT-060's OIDC/SAML and
/// AUT-070's <c>Auth.Cert.Login</c> are still absent because they are M6's (D-M2-5): their fixtures
/// report <c>pending</c> rather than being driven by a stub (D-M1c-25, D-M2-8).
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
        List<int> renewedLeases = new();
        List<string> failureCodes = new();
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
        operation.Token.Register(client.Dispose);
        invocation.Transport.Exhausted += operation.Cancel;

        JsonElement args = invocation.Arguments;
        FixtureError? error = null;
        try
        {
            await client.Auth.Userpass.LoginAsync(
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
    private static object TokenInfoResult(TokenInfo info) => new Dictionary<string, object?>(StringComparer.Ordinal)
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

    /// <summary>
    /// Projects <see cref="AuthInfo"/> onto the fixture assertion surface. <c>IssuedAt</c> renders
    /// in the same <c>2026-09-13T12:00:00Z</c> spelling the fixtures' <c>clock.start</c> uses
    /// (AUT-013), and AUT-044's derived <c>EnvironmentScope</c> is projected as an object so
    /// <c>auth.appid.env-scope-derived</c> can assert its three members.
    /// </summary>
    private static object AuthInfoResult(AuthInfo auth) => new Dictionary<string, object?>(StringComparer.Ordinal)
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

    private static string? OptionalString(JsonElement args, string name)
        => args.ValueKind == JsonValueKind.Object
            && args.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static SecretString? OptionalSecret(JsonElement args, string name)
        => OptionalString(args, name) is { } value ? new SecretString(value) : null;

    /// <summary>
    /// The client state M2a's fixtures assert. <c>Auth.CurrentToken</c> is what
    /// <c>auth.token.revoke-self-clears-token</c> checks is <c>$absent</c> after AUT-083 clears it.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> ClientState(BastionVaultClient client) => new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["RateGate.Paused"] = client.RateGateState.Paused,
        ["Auth.CurrentToken"] = client.Auth.CurrentToken is { } token ? new RedactedValue(token.Reveal() ?? string.Empty) : null,
        ["Auth.TokenSource.Kind"] = client.Auth.TokenSource.Kind.ToString(),
    };

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
