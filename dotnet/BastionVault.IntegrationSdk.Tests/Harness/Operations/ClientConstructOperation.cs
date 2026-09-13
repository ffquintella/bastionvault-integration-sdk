using System.Text.Json;
using BastionVault.IntegrationSdk;

namespace BastionVault.IntegrationSdk.Tests.Harness.Operations;

/// <summary>
/// Registers the <c>Client.Construct</c> fixture operation against the real SDK
/// (<c>decisions/0003-m1a-configuration.md</c>, D-M1a-6): a fixture's <c>client</c> block is mapped
/// onto <see cref="BastionVaultClientOptions"/> and resolved through the real
/// <see cref="BastionVaultClient"/> constructor, never a test-only shim.
/// </summary>
public static class ClientConstructOperation
{
    public const string Name = "Client.Construct";

    public static void Register(OperationRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Register(Name, invocation =>
        {
            FixtureConfiguration configuration = invocation.Configuration;
            BastionVaultClientOptions options = new()
            {
                Address = configuration.Address,
                Token = configuration.Token,
                Namespace = configuration.Namespace,
                ApiPrefix = configuration.ApiPrefix,
            };

            ApplySettings(options, configuration.Settings);

            EnvironmentSource environment = configuration.Environment.Count > 0
                ? EnvironmentSource.FromMap(configuration.Environment)
                : EnvironmentSource.None;

            try
            {
                BastionVaultClient client = new(options, environment);
                Dictionary<string, object?> result = new(StringComparer.Ordinal)
                {
                    ["isInsecure"] = client.IsInsecure,
                };
                return ValueTask.FromResult(new FixtureOperationResult(Result: result));
            }
            catch (BastionVaultException exception)
            {
                FixtureError error = new(
                    Code: exception.Code,
                    StatusCode: exception.StatusCode,
                    Retryable: exception.Retryable,
                    Attempts: exception.Attempts,
                    Hint: exception.Hint);
                return ValueTask.FromResult(new FixtureOperationResult(Error: error));
            }
        });
    }

    private static void ApplySettings(BastionVaultClientOptions options, JsonElement settings)
    {
        if (settings.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (settings.TryGetProperty("Headers", out JsonElement headers) && headers.ValueKind == JsonValueKind.Object)
        {
            options.Headers = headers.EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
        }

        if (settings.TryGetProperty("AllowInsecureHttp", out JsonElement allowInsecureHttp)
            && allowInsecureHttp.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            options.AllowInsecureHttp = allowInsecureHttp.GetBoolean();
        }

        if (settings.TryGetProperty("TlsSkipVerify", out JsonElement tlsSkipVerify)
            && tlsSkipVerify.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            options.TlsSkipVerify = tlsSkipVerify.GetBoolean();
        }
    }
}
