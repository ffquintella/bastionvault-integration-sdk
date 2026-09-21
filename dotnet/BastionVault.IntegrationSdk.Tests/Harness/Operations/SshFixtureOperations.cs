using System.Text.Json;
using BastionVault.IntegrationSdk;

namespace BastionVault.IntegrationSdk.Tests.Harness.Operations;

/// <summary>
/// Registers the M9 slice d <c>Ssh.*</c> and <c>SshBroker.*</c> fixture operations against the real
/// SDK (10 — SSH engine and SSH broker), following <see cref="PkiFixtureOperations"/> exactly: one
/// <c>Build</c> resolving the fixture's <c>client</c> block through the real constructor, one
/// <c>RunAsync</c> projecting a <see cref="BastionVaultException"/> through
/// <see cref="FixtureErrors"/>, and one projection per returned type. Only the four operations the
/// four <c>ssh.*</c>/<c>sshbroker.*</c> fixtures actually exercise are registered here
/// (<c>Ssh.Sign</c>, <c>Ssh.Creds</c>, <c>Ssh.Verify</c>, <c>SshBroker.Effective</c>).
/// </summary>
public static class SshFixtureOperations
{
    public static void Register(OperationRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Register("Ssh.Sign", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => SignedResult(
                await client.Ssh.SignAsync(Role(args), SignRequest(args), Mount(args), options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Ssh.Creds", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => CredentialsResult(
                await client.Ssh.CredsAsync(Role(args), Ip(args), OptionalString(args, "username"), null, Mount(args), options).ConfigureAwait(false)))
                .ConfigureAwait(false);
        });

        registry.Register("Ssh.Verify", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => VerificationResult(
                await client.Ssh.VerifyAsync(new SecretString(RequiredString(args, "otp")), Mount(args), options).ConfigureAwait(false)))
                .ConfigureAwait(false);
        });

        registry.Register("SshBroker.Effective", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => EffectiveResult(
                await client.SshBroker.EffectiveAsync(RequiredString(args, "resourceId"), RequiredString(args, "resourceType"), OptionalStringList(args, "assetGroupIds"), options)
                    .ConfigureAwait(false)))
                .ConfigureAwait(false);
        });
    }

    private static (BastionVaultClient Client, RequestOptions Options) Build(FixtureInvocation invocation)
    {
        ScriptedTransportAdapter adapter = new(invocation.Transport);
        BastionVaultClientOptions clientOptions = FixtureClientBuilder.BuildOptions(invocation.Configuration, adapter, invocation.Instruments);
        EnvironmentSource environment = invocation.Configuration.Environment.Count > 0
            ? EnvironmentSource.FromMap(invocation.Configuration.Environment)
            : EnvironmentSource.None;
        BastionVaultClient client = new(clientOptions, environment);
        FixtureClientBuilder.ApplyAuthInfoInstrument(client, invocation.Configuration.Settings);
        return (client, new RequestOptions());
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

    private static string Role(JsonElement args)
    {
        return RequiredString(args, "role");
    }

    private static string Ip(JsonElement args)
    {
        return RequiredString(args, "ip");
    }

    private static string Mount(JsonElement args)
    {
        return args.TryGetProperty("mount", out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : "ssh";
    }

    private static string RequiredString(JsonElement args, string name)
    {
        return args.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : string.Empty;
    }

    private static string? OptionalString(JsonElement args, string name)
    {
        return args.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static IReadOnlyList<string>? OptionalStringList(JsonElement args, string name)
    {
        return args.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToArray()
            : null;
    }

    private static TimeSpan? OptionalSeconds(JsonElement args, string name)
    {
        return args.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? TimeSpan.FromSeconds(value.GetInt64())
            : null;
    }

    private static SshSignRequest SignRequest(JsonElement args)
    {
        return new SshSignRequest
        {
            PublicKey = RequiredString(args, "public_key"),
            ValidPrincipals = OptionalStringList(args, "valid_principals"),
            Ttl = OptionalSeconds(args, "ttl"),
            CertType = OptionalString(args, "cert_type"),
            KeyId = OptionalString(args, "key_id"),
        };
    }

    private static object SignedResult(SignedSshCertificate result)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["SignedKey"] = result.SignedKey,
            ["SerialNumber"] = result.SerialNumber,
            ["Algorithm"] = result.Algorithm,
        };
    }

    private static object? CredentialsResult(SshCredentials result)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            // D-M9-3's SecretString pin for the OTP, applying D-M9-16's redacting-wrapper rule at
            // the fixture boundary: the fixture-visible shape carries a RedactedValue, mirroring
            // PkiFixtureOperations' treatment of PrivateKey, so a fixture can assert `$redacted`
            // without the raw OTP ever appearing in an `expect` block.
            ["Key"] = new RedactedValue(result.Key.Reveal() ?? string.Empty),
            ["KeyType"] = result.KeyType,
            ["Username"] = result.Username,
            ["Ip"] = result.Ip,
            ["Port"] = result.Port,
            ["Ttl"] = result.Ttl?.TotalSeconds,
        };
    }

    private static object? VerificationResult(SshOtpVerification? result)
    {
        return result is null
            ? null
            : new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Username"] = result.Username,
                ["Ip"] = result.Ip,
                ["RoleName"] = result.RoleName,
                ["Port"] = result.Port,
            };
    }

    private static object EffectiveResult(SshBrokerEffectivePolicy result)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["LoginClass"] = result.LoginClass,
            ["Source"] = result.Source,
        };
    }

    private static IReadOnlyDictionary<string, object?> ClientState(BastionVaultClient client)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["RateGate.Paused"] = client.RateGateState.Paused,
        };
    }
}
