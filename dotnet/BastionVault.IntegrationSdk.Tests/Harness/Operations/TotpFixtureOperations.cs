using System.Text.Json;
using BastionVault.IntegrationSdk;

namespace BastionVault.IntegrationSdk.Tests.Harness.Operations;

/// <summary>
/// Registers the M8c <c>Totp.*</c> fixture operations against the real SDK (11 — TOTP engine),
/// driven through the real <see cref="BastionVaultClient"/> and the fixture's
/// <see cref="ScriptedTransport"/>, following <see cref="KvFixtureOperations"/> exactly.
/// </summary>
public static class TotpFixtureOperations
{
    public static void Register(OperationRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Register("Totp.ListKeys", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => (object?)await client.Totp
                .ListKeysAsync(Mount(args), options).ConfigureAwait(false)).ConfigureAwait(false);
        });

        registry.Register("Totp.CreateKey", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => CreatedResult(
                await client.Totp.CreateKeyAsync(Name(args), Spec(args), Mount(args), options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Totp.ReadKey", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => KeyResult(
                await client.Totp.ReadKeyAsync(Name(args), Mount(args), options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Totp.DeleteKey", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () =>
            {
                await client.Totp.DeleteKeyAsync(Name(args), Mount(args), options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Totp.GenerateCode", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => (object?)await client.Totp
                .GenerateCodeAsync(Name(args), Mount(args), options).ConfigureAwait(false)).ConfigureAwait(false);
        });

        registry.Register("Totp.ValidateCode", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => (object?)await client.Totp
                .ValidateCodeAsync(Name(args), Code(args), Mount(args), options).ConfigureAwait(false)).ConfigureAwait(false);
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
        FixtureClientBuilder.ApplyDiscoveryInstruments(client, invocation.Configuration.Settings);
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

    private static string Name(JsonElement args)
    {
        return args.TryGetProperty("name", out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : string.Empty;
    }

    private static string Code(JsonElement args)
    {
        return args.TryGetProperty("code", out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : string.Empty;
    }

    /// <summary>The fixture may omit <c>mount</c>, in which case the SDK's own <c>"totp"</c> default applies.</summary>
    private static string Mount(JsonElement args)
    {
        return args.TryGetProperty("mount", out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : "totp";
    }

    /// <summary>Binds a fixture's <c>spec</c> argument to <see cref="TotpKeySpec"/> by its PascalCase member names.</summary>
    private static TotpKeySpec Spec(JsonElement args)
    {
        if (!args.TryGetProperty("spec", out JsonElement value) || value.ValueKind != JsonValueKind.Object)
        {
            return new TotpKeySpec();
        }

        return new TotpKeySpec
        {
            Generate = value.TryGetProperty("Generate", out JsonElement generate) && generate.ValueKind == JsonValueKind.True,
            Key = OptionalSecret(value, "Key"),
            Url = OptionalSecret(value, "Url"),
            KeySize = OptionalInt(value, "KeySize"),
            Issuer = OptionalString(value, "Issuer"),
            AccountName = OptionalString(value, "AccountName"),
            Algorithm = OptionalAlgorithm(value, "Algorithm"),
            Digits = OptionalInt(value, "Digits"),
            Period = OptionalInt(value, "Period"),
            Skew = OptionalInt(value, "Skew"),
            QrSize = OptionalInt(value, "QrSize"),
            Exported = OptionalBool(value, "Exported"),
            ReplayCheck = OptionalBool(value, "ReplayCheck"),
        };
    }

    private static string? OptionalString(JsonElement value, string name)
    {
        return value.TryGetProperty(name, out JsonElement field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
    }

    private static SecretString? OptionalSecret(JsonElement value, string name)
    {
        return OptionalString(value, name) is { } text ? new SecretString(text) : null;
    }

    private static int? OptionalInt(JsonElement value, string name)
    {
        return value.TryGetProperty(name, out JsonElement field) && field.ValueKind == JsonValueKind.Number ? field.GetInt32() : null;
    }

    private static bool? OptionalBool(JsonElement value, string name)
    {
        return value.TryGetProperty(name, out JsonElement field) && field.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? field.GetBoolean()
            : null;
    }

    private static TotpAlgorithm? OptionalAlgorithm(JsonElement value, string name)
    {
        return OptionalString(value, name) switch
        {
            "Sha1" => TotpAlgorithm.Sha1,
            "Sha256" => TotpAlgorithm.Sha256,
            "Sha512" => TotpAlgorithm.Sha512,
            _ => null,
        };
    }

    /// <summary>
    /// Projects <see cref="TotpKeyCreated"/>. <c>Key</c> and <c>Url</c> render through
    /// <see cref="RedactedValue"/> (TOT-002), matching the pattern <c>AuthFixtureOperations</c>
    /// uses for <see cref="AuthInfo.ClientToken"/>, so a fixture asserts <c>$redacted</c> rather
    /// than the seed itself.
    /// </summary>
    private static object CreatedResult(TotpKeyCreated created)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Name"] = created.Name,
            ["Generate"] = created.Generate,
            ["Key"] = created.Key is { } key ? new RedactedValue(key.Reveal() ?? string.Empty) : null,
            ["Url"] = created.Url is { } url ? new RedactedValue(url.Reveal() ?? string.Empty) : null,
            ["Barcode"] = created.Barcode is { } barcode ? Convert.ToBase64String(barcode.Span) : null,
        };
    }

    private static object? KeyResult(TotpKey? key)
    {
        return key is null
            ? null
            : new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Generate"] = key.Generate,
                ["Issuer"] = key.Issuer,
                ["AccountName"] = key.AccountName,
                ["Algorithm"] = key.Algorithm,
                ["Digits"] = key.Digits,
                ["Period"] = key.Period,
                ["Skew"] = key.Skew,
                ["ReplayCheck"] = key.ReplayCheck,
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
