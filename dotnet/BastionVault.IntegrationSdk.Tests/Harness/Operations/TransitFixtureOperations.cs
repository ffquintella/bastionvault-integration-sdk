using System.Text.Json;
using BastionVault.IntegrationSdk;

namespace BastionVault.IntegrationSdk.Tests.Harness.Operations;

/// <summary>
/// Registers the 08 <c>Transit.*</c> fixture operations against the real SDK (DR-0013 slice b),
/// following <see cref="KvFixtureOperations"/> exactly: one <c>Build</c> resolving the fixture's
/// <c>client</c> block through the real constructor, one <c>RunAsync</c> projecting a
/// <see cref="BastionVaultException"/> through <see cref="FixtureErrors"/>, and one projection per
/// returned type. <c>Transit.EncryptThenDecrypt</c> is the one harness-only composite (FIX-011's
/// exception for a flow that is inherently multi-request, the same shape
/// <c>Auth.AutoRenew.Run</c> already establishes): a round trip cannot be certified from either
/// leg alone.
/// </summary>
public static class TransitFixtureOperations
{
    public static void Register(OperationRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Register("Transit.Encrypt", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => EncryptResult(
                await client.Transit.EncryptAsync(
                    Name(args), plaintextBase64: PlaintextBase64(args), contextBase64: ContextBase64(args),
                    mount: Mount(args), options: options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Transit.Decrypt", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => DecryptResult(
                await client.Transit.DecryptAsync(
                    Name(args), Ciphertext(args), contextBase64: ContextBase64(args),
                    mount: Mount(args), options: options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Transit.Random", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => (object?)Convert.ToBase64String(
                await client.Transit.RandomAsync(BytesCount(args), Mount(args), options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Transit.EncryptThenDecrypt", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () =>
            {
                TransitEncryptResult encrypted = await client.Transit.EncryptAsync(
                    Name(args), plaintextBase64: PlaintextBase64(args), mount: Mount(args), options: options).ConfigureAwait(false);
                SecretBytes decrypted = await client.Transit.DecryptAsync(
                    Name(args), encrypted.Ciphertext, mount: Mount(args), options: options).ConfigureAwait(false);
                byte[] originalPlaintext = Convert.FromBase64String(PlaintextBase64(args)!);
                bool roundTrip = originalPlaintext.AsSpan().SequenceEqual(decrypted.Reveal());
                return (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["Ciphertext"] = encrypted.Ciphertext,
                    ["KeyVersion"] = encrypted.KeyVersion,
                    ["Plaintext"] = new RedactedValue(Convert.ToBase64String(decrypted.Reveal() ?? [])),
                    ["RoundTrip"] = roundTrip,
                };
            }).ConfigureAwait(false);
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

    private static string Name(JsonElement args)
    {
        return args.TryGetProperty("name", out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : string.Empty;
    }

    private static string Mount(JsonElement args)
    {
        return args.TryGetProperty("mount", out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : "transit";
    }

    private static string? PlaintextBase64(JsonElement args)
    {
        return args.TryGetProperty("plaintext", out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string? ContextBase64(JsonElement args)
    {
        return args.TryGetProperty("context", out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string Ciphertext(JsonElement args)
    {
        return args.TryGetProperty("ciphertext", out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : string.Empty;
    }

    private static int BytesCount(JsonElement args)
    {
        return args.TryGetProperty("bytes", out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 32;
    }

    private static object EncryptResult(TransitEncryptResult result)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Ciphertext"] = result.Ciphertext,
            ["KeyVersion"] = result.KeyVersion,
        };
    }

    private static object DecryptResult(SecretBytes plaintext)
    {
        // TRS-013: the fixture-visible shape carries a RedactedValue, exactly as
        // LogicalFixtureOperations wraps AuthInfo.ClientToken (D-M2-7's TST-051 pattern), so a
        // fixture can assert "$redacted" without the raw plaintext ever appearing in an `expect`
        // block on disk.
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Plaintext"] = new RedactedValue(Convert.ToBase64String(plaintext.Reveal() ?? [])),
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
