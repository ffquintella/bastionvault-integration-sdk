using System.Text.Json;
using BastionVault.IntegrationSdk;

namespace BastionVault.IntegrationSdk.Tests.Harness.Operations;

/// <summary>
/// Registers the M10 slice a <c>Identity.Sharing.Put</c> fixture operation against the real SDK
/// (DR-0017), following <see cref="PkiFixtureOperations"/> exactly. Only the one operation the
/// one <c>identity.*</c> fixture this slice drives actually exercises is registered here — the
/// same minimal-registration precedent that file sets.
/// </summary>
public static class IdentityFixtureOperations
{
    public static void Register(OperationRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Register("Identity.Sharing.Put", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () =>
            {
                await client.Identity.Sharing.PutAsync(Kind(args), Target(args), Grantee(args), Spec(args), options).ConfigureAwait(false);
                return (object?)null;
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

    private static string Kind(JsonElement args)
    {
        return args.GetProperty("kind").GetString()!;
    }

    private static string Target(JsonElement args)
    {
        return args.GetProperty("target").GetString()!;
    }

    private static string Grantee(JsonElement args)
    {
        return args.GetProperty("grantee").GetString()!;
    }

    private static IdentitySharingSpec Spec(JsonElement args)
    {
        JsonElement spec = args.GetProperty("spec");
        return new IdentitySharingSpec
        {
            GranteeKind = spec.TryGetProperty("GranteeKind", out JsonElement granteeKind) ? granteeKind.GetString() : null,
            Capabilities = spec.TryGetProperty("Capabilities", out JsonElement capabilities) && capabilities.ValueKind == JsonValueKind.Array
                ? capabilities.EnumerateArray().Select(item => item.GetString()!).ToArray()
                : null,
        };
    }

    private static IReadOnlyDictionary<string, object?>? ClientState(BastionVaultClient client)
    {
        return null;
    }
}
