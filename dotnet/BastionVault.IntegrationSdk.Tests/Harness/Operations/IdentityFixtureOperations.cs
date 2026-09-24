using System.Globalization;
using System.Text.Json;
using BastionVault.IntegrationSdk;

namespace BastionVault.IntegrationSdk.Tests.Harness.Operations;

/// <summary>Registers the <c>identity.*</c> fixture operations against the real SDK (DR-0017,
/// D-M12-5 follow-up), following <see cref="PkiFixtureOperations"/>'s minimal-registration
/// precedent: only the operations the fixtures in this file actually exercise.</summary>
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

        registry.Register("Identity.Self", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => SelfResult(
                await client.Identity.SelfAsync(options).ConfigureAwait(false))).ConfigureAwait(false);
        });
    }

    private static object SelfResult(EntitySelf self)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["EntityId"] = self.EntityId,
            ["Username"] = self.Username,
            ["MountPath"] = self.MountPath,
            ["RoleName"] = self.RoleName,
            ["PrimaryMount"] = self.PrimaryMount,
            ["PrimaryName"] = self.PrimaryName,
            ["CreatedAt"] = self.CreatedAt is { } createdAt ? Instant(createdAt) : null,
            ["Aliases"] = self.Aliases?.Select(item => (object?)item).ToList(),
        };
    }

    /// <summary>The full-precision RFC 3339 spelling <c>identity.self</c>'s captured
    /// <c>created_at</c> round-trips through <see cref="System.DateTimeOffset"/> as.</summary>
    private static string Instant(DateTimeOffset value)
    {
        return value.ToString("yyyy-MM-ddTHH:mm:ss.ffffffK", CultureInfo.InvariantCulture);
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
