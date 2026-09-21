using System.Text.Json;
using BastionVault.IntegrationSdk;

namespace BastionVault.IntegrationSdk.Tests.Harness.Operations;

/// <summary>
/// Registers the M9 slice a <c>Pki.*</c> fixture operations against the real SDK (09 — PKI
/// engine), following <see cref="TransitFixtureOperations"/> exactly: one <c>Build</c> resolving
/// the fixture's <c>client</c> block through the real constructor, one <c>RunAsync</c> projecting
/// a <see cref="BastionVaultException"/> through <see cref="FixtureErrors"/>, and one projection
/// per returned type. Only the three operations the three <c>pki.*</c> fixtures this slice
/// authors actually exercise are registered here (<c>Pki.Issue</c>, <c>Pki.ListCertificatesInfo</c>,
/// <c>Pki.ReadRole</c>) — the same minimal-registration precedent <c>TransitFixtureOperations</c>
/// sets rather than wiring the whole engine ahead of a fixture that needs it.
/// </summary>
public static class PkiFixtureOperations
{
    public static void Register(OperationRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Register("Pki.Issue", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => IssuedResult(
                await client.Pki.IssueAsync(Role(args), IssueRequest(args), Mount(args), options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Pki.ListCertificatesInfo", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () =>
            {
                Page<CertificateSummary> page = await client.Pki
                    .ListCertificatesInfoAsync(Mount(args), OptionalString(args, "after"), OptionalInt(args, "limit"), options)
                    .ConfigureAwait(false);
                return (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["Keys"] = page.Keys.Select(key => (object?)key).ToList(),
                    ["Records"] = page.Records.Select(record => (object?)SummaryResult(record)).ToList(),
                    ["Total"] = page.Total,
                    ["Next"] = page.Next,
                    ["Truncated"] = page.Truncated,
                };
            }).ConfigureAwait(false);
        });

        registry.Register("Pki.ReadRole", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => (object?)await client.Pki
                .ReadRoleAsync(Name(args), Mount(args), options).ConfigureAwait(false)).ConfigureAwait(false);
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
        return args.TryGetProperty("role", out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : string.Empty;
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
            : "pki";
    }

    private static string? OptionalString(JsonElement args, string name)
    {
        return args.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static int? OptionalInt(JsonElement args, string name)
    {
        return args.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
    }

    private static IReadOnlyList<string>? OptionalStringList(JsonElement args, string name)
    {
        return args.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToArray()
            : null;
    }

    private static IssueRequest IssueRequest(JsonElement args)
    {
        return new IssueRequest
        {
            CommonName = args.TryGetProperty("common_name", out JsonElement commonName) && commonName.ValueKind == JsonValueKind.String
                ? commonName.GetString()!
                : string.Empty,
            AltNames = OptionalStringList(args, "alt_names"),
            IpSans = OptionalStringList(args, "ip_sans"),
            IssuerRef = OptionalString(args, "issuer_ref"),
        };
    }

    private static object IssuedResult(IssuedCertificate result)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Certificate"] = result.Certificate,
            ["IssuingCa"] = result.IssuingCa,
            ["CaChain"] = result.CaChain.Select(item => (object?)item).ToList(),
            // PKI-002: the fixture-visible shape carries a RedactedValue, mirroring
            // TransitFixtureOperations' treatment of a decrypted plaintext, so a fixture can assert
            // `$redacted` without the raw private key ever appearing in an `expect` block on disk.
            ["PrivateKey"] = result.PrivateKey is { } key ? new RedactedValue(key.Reveal() ?? string.Empty) : null,
            ["PrivateKeyType"] = result.PrivateKeyType,
            ["SerialNumber"] = result.SerialNumber,
            ["IssuerId"] = result.IssuerId,
            ["KeyId"] = result.KeyId,
        };
    }

    private static object SummaryResult(CertificateSummary summary)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["SerialNumber"] = summary.SerialNumber,
            ["IssuerId"] = summary.IssuerId,
            ["IsOrphaned"] = summary.IsOrphaned,
            ["Source"] = summary.Source,
            ["KeyId"] = summary.KeyId,
            ["CommonName"] = summary.CommonName,
            ["IssuerDn"] = summary.IssuerDn,
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
