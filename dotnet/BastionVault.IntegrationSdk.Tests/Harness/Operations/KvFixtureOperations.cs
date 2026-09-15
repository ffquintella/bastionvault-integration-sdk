using System.Globalization;
using System.Text.Json;
using BastionVault.IntegrationSdk;

namespace BastionVault.IntegrationSdk.Tests.Harness.Operations;

/// <summary>
/// Registers the M4a <c>Kv.*</c> fixture operations against the real SDK (DR-0009): KV v1 in full
/// (KV1-001…KV1-004) and KV v2's data, metadata and config paths (KV2-001…KV2-030), driven through
/// the real <see cref="BastionVaultClient"/> and the fixture's <see cref="ScriptedTransport"/>,
/// never a test-only shim (D-M0-2, D-M1a-6).
/// </summary>
/// <remarks>
/// Follows <see cref="SysFixtureOperations"/> exactly: one <c>Build</c> that resolves the fixture's
/// <c>client</c> block through the real constructor, one <c>RunAsync</c> that projects a
/// <see cref="BastionVaultException"/> through <see cref="FixtureErrors"/>, and one projection
/// function per returned type.
/// </remarks>
public static class KvFixtureOperations
{
    public static void Register(OperationRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Register("Kv.V1.Read", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => V1Result(
                await client.Kv.V1.ReadAsync(Path(args), Mount(args), options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Kv.V1.Get", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => V1Result(
                await client.Kv.V1.GetAsync(Path(args), Mount(args), options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Kv.V1.Write", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () =>
            {
                await client.Kv.V1.WriteAsync(Path(args), Map(args, "data"), Mount(args), Ttl(args), options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Kv.V1.Delete", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () =>
            {
                await client.Kv.V1.DeleteAsync(Path(args), Mount(args), options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Kv.V1.List", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => (object?)await client.Kv.V1
                .ListAsync(Prefix(args), Mount(args), options).ConfigureAwait(false)).ConfigureAwait(false);
        });

        registry.Register("Kv.V2.ReadSecret", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => V2SecretResult(
                await client.Kv.V2.ReadSecretAsync(Path(args), Mount(args), Version(args), Env(args), options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Kv.V2.GetSecret", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => V2SecretResult(
                await client.Kv.V2.GetSecretAsync(Path(args), Mount(args), Version(args), Env(args), options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Kv.V2.WriteSecret", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => VersionMetadataResult(
                await client.Kv.V2.WriteSecretAsync(Path(args), Map(args, "data"), Mount(args), WriteOptions(args), options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Kv.V2.PatchEnvironment", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => VersionMetadataResult(
                await client.Kv.V2.PatchEnvironmentAsync(
                    Path(args), Env(args)!, Map(args, "overrides"), Mount(args), Cas(args), options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Kv.V2.WriteAllEnvironments", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => VersionMetadataResult(
                await client.Kv.V2.WriteAllEnvironmentsAsync(
                    Path(args), Map(args, "base"), EnvMap(args, "envs"), Mount(args), Cas(args), options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Kv.V2.SoftDelete", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () =>
            {
                await client.Kv.V2.SoftDeleteAsync(Path(args), Mount(args), Versions(args), options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Kv.V2.Undelete", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () =>
            {
                await client.Kv.V2.UndeleteAsync(Path(args), Versions(args) ?? [], Mount(args), options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Kv.V2.Destroy", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () =>
            {
                await client.Kv.V2.DestroyAsync(Path(args), Versions(args) ?? [], Mount(args), options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Kv.V2.ReadMetadata", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => MetadataResult(
                await client.Kv.V2.ReadMetadataAsync(Path(args), Mount(args), options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Kv.V2.DeleteMetadata", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () =>
            {
                await client.Kv.V2.DeleteMetadataAsync(Path(args), Mount(args), options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Kv.V2.List", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => (object?)await client.Kv.V2
                .ListAsync(Prefix(args), Mount(args), options).ConfigureAwait(false)).ConfigureAwait(false);
        });

        registry.Register("Kv.V2.ReadConfig", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => ConfigResult(
                await client.Kv.V2.ReadConfigAsync(Mount(args), options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Kv.V2.WriteConfig", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () =>
            {
                await client.Kv.V2.WriteConfigAsync(Config(args, "config"), Mount(args), options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Kv.V2.UpdateConfig", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => ConfigResult(
                await client.Kv.V2.UpdateConfigAsync(ConfigPatch(args, "patch"), Mount(args), options).ConfigureAwait(false))).ConfigureAwait(false);
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

    private static string Path(JsonElement args)
    {
        return args.TryGetProperty("path", out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : string.Empty;
    }

    private static string Prefix(JsonElement args)
    {
        return args.TryGetProperty("prefix", out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : string.Empty;
    }

    /// <summary>The fixture may omit <c>mount</c>, in which case the SDK's own <c>"secret"</c> default applies (D-M4-4).</summary>
    private static string Mount(JsonElement args)
    {
        return args.TryGetProperty("mount", out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : "secret";
    }

    private static int? Version(JsonElement args)
    {
        return args.TryGetProperty("version", out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;
    }

    private static int? Cas(JsonElement args)
    {
        return args.TryGetProperty("cas", out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;
    }

    private static string? Env(JsonElement args)
    {
        return args.TryGetProperty("env", out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static TimeSpan? Ttl(JsonElement args)
    {
        return args.TryGetProperty("ttl", out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? System.Xml.XmlConvert.ToTimeSpan(value.GetString()!)
            : null;
    }

    private static IReadOnlyList<int>? Versions(JsonElement args)
    {
        return args.TryGetProperty("versions", out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(item => item.GetInt32()).ToArray()
            : null;
    }

    private static IReadOnlyDictionary<string, JsonElement> Map(JsonElement args, string name)
    {
        return args.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Object
            ? value.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>> EnvMap(JsonElement args, string name)
    {
        Dictionary<string, IReadOnlyDictionary<string, JsonElement>> result = new(StringComparer.Ordinal);
        if (args.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in value.EnumerateObject())
            {
                result[property.Name] = property.Value.EnumerateObject()
                    .ToDictionary(inner => inner.Name, inner => inner.Value.Clone(), StringComparer.Ordinal);
            }
        }

        return result;
    }

    /// <summary>Binds a fixture's <c>options</c> argument to <see cref="KvWriteOptions"/> by its PascalCase member names.</summary>
    private static KvWriteOptions? WriteOptions(JsonElement args)
    {
        if (!args.TryGetProperty("options", out JsonElement value) || value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new KvWriteOptions
        {
            Cas = value.TryGetProperty("Cas", out JsonElement cas) && cas.ValueKind == JsonValueKind.Number ? cas.GetInt32() : null,
            Env = value.TryGetProperty("Env", out JsonElement env) && env.ValueKind == JsonValueKind.String ? env.GetString() : null,
            Envs = value.TryGetProperty("Envs", out JsonElement envs) && envs.ValueKind == JsonValueKind.Object
                ? envs.EnumerateObject().ToDictionary(
                    property => property.Name,
                    property => (IReadOnlyDictionary<string, JsonElement>)property.Value.EnumerateObject()
                        .ToDictionary(inner => inner.Name, inner => inner.Value.Clone(), StringComparer.Ordinal),
                    StringComparer.Ordinal)
                : null,
        };
    }

    private static KvV2Config Config(JsonElement args, string name)
    {
        JsonElement value = args.GetProperty(name);
        return new KvV2Config
        {
            MaxVersions = value.TryGetProperty("MaxVersions", out JsonElement max) ? max.GetInt32() : 0,
            CasRequired = value.TryGetProperty("CasRequired", out JsonElement cas) && cas.GetBoolean(),
            DeleteVersionAfter = value.TryGetProperty("DeleteVersionAfter", out JsonElement after) ? after.GetString()! : "0s",
            Environments = value.TryGetProperty("Environments", out JsonElement environments) && environments.ValueKind == JsonValueKind.Array
                ? environments.EnumerateArray().Select(item => item.GetString()!).ToArray()
                : Array.Empty<string>(),
        };
    }

    private static KvV2ConfigPatch ConfigPatch(JsonElement args, string name)
    {
        JsonElement value = args.GetProperty(name);
        return new KvV2ConfigPatch
        {
            MaxVersions = value.TryGetProperty("MaxVersions", out JsonElement max) && max.ValueKind == JsonValueKind.Number ? max.GetInt32() : null,
            CasRequired = value.TryGetProperty("CasRequired", out JsonElement cas) && cas.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? cas.GetBoolean()
                : null,
            DeleteVersionAfter = value.TryGetProperty("DeleteVersionAfter", out JsonElement after) && after.ValueKind == JsonValueKind.String
                ? after.GetString()
                : null,
            Environments = value.TryGetProperty("Environments", out JsonElement environments) && environments.ValueKind == JsonValueKind.Array
                ? environments.EnumerateArray().Select(item => item.GetString()!).ToArray()
                : null,
        };
    }

    private static object? V1Result(KvV1Secret? secret)
    {
        return secret is null
            ? null
            : new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Data"] = PlainMap(secret.Data),
                ["LeaseDuration"] = (int)secret.LeaseDuration.TotalSeconds,
                ["Renewable"] = secret.Renewable,
            };
    }

    private static object? V2SecretResult(KvV2Secret? secret)
    {
        return secret is null
            ? null
            : new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["State"] = secret.State.ToString(),
                ["Data"] = secret.Data is null ? null : PlainMap(secret.Data),
                ["Metadata"] = VersionMetadataResult(secret.Metadata),
            };
    }

    private static object VersionMetadataResult(KvV2VersionMetadata metadata)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Version"] = metadata.Version,
            ["CreatedTime"] = Instant(metadata.CreatedTime),
            ["DeletionTime"] = metadata.DeletionTime is { } deleted ? Instant(deleted) : null,
            ["Destroyed"] = metadata.Destroyed,
            ["Username"] = metadata.Username,
            ["Operation"] = metadata.Operation,
            ["ResolvedEnv"] = metadata.ResolvedEnv,
            ["AvailableEnvs"] = metadata.AvailableEnvs.Select(value => (object?)value).ToList(),
        };
    }

    private static object? MetadataResult(KvV2Metadata? metadata)
    {
        return metadata is null
            ? null
            : new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["CurrentVersion"] = metadata.CurrentVersion,
                ["OldestVersion"] = metadata.OldestVersion,
                ["MaxVersions"] = metadata.MaxVersions,
                ["CasRequired"] = metadata.CasRequired,
                ["DeleteVersionAfter"] = metadata.DeleteVersionAfter,
                ["CreatedTime"] = Instant(metadata.CreatedTime),
                ["UpdatedTime"] = Instant(metadata.UpdatedTime),
                // Keyed by the version number as a string, matching the wire and the fixture.
                ["Versions"] = metadata.Versions.ToDictionary(
                    pair => pair.Key.ToString(CultureInfo.InvariantCulture),
                    pair => (object?)VersionMetadataResult(pair.Value),
                    StringComparer.Ordinal),
            };
    }

    private static object? ConfigResult(KvV2Config? config)
    {
        return config is null
            ? null
            : new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["MaxVersions"] = config.MaxVersions,
                ["CasRequired"] = config.CasRequired,
                ["DeleteVersionAfter"] = config.DeleteVersionAfter,
                ["Environments"] = config.Environments.Select(value => (object?)value).ToList(),
            };
    }

    /// <summary>The RFC 3339 spelling the fixtures use, matching <see cref="SysFixtureOperations"/>'s <c>started_at</c>.</summary>
    private static string Instant(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    private static Dictionary<string, object?> PlainMap(IReadOnlyDictionary<string, JsonElement> map)
    {
        return map.ToDictionary(pair => pair.Key, pair => ToPlainValue(pair.Value), StringComparer.Ordinal);
    }

    private static object? ToPlainValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(property => property.Name, property => ToPlainValue(property.Value), StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(ToPlainValue).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out long integer) ? integer : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
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
