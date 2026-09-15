using System.Text.Json;
using BastionVault.IntegrationSdk;

namespace BastionVault.IntegrationSdk.Tests.Harness.Operations;

/// <summary>
/// Registers the landed <c>Sys.*</c> fixture operations against the real SDK (DR-0007, DR-0012):
/// health and status (SYS-001, SYS-002, SYS-005, SYS-006, SYS-008, <c>HsmStatus</c>), self
/// capability introspection (SYS-050…SYS-053), initialisation/seal/unseal (SYS-010…SYS-013),
/// mounts (SYS-020…SYS-026) and auth methods (SYS-030), driven through the real <see cref="BastionVaultClient"/> and the fixture's
/// <see cref="ScriptedTransport"/>, never a test-only shim (D-M0-2, D-M1a-6).
/// </summary>
public static class SysFixtureOperations
{
    public static void Register(OperationRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Register("Sys.Health", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => HealthResult(await client.Sys.HealthAsync(options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Sys.SealStatus", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => SealStatusResult(await client.Sys.SealStatusAsync(options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Sys.ServerInfo", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => ServerInfoResult(await client.Sys.ServerInfoAsync(options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Sys.ClusterStatus", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => ClusterStatusResult(await client.Sys.ClusterStatusAsync(options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Sys.CapabilitiesSelf", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            string[] paths = args.TryGetProperty("paths", out JsonElement pathsElement) && pathsElement.ValueKind == JsonValueKind.Array
                ? pathsElement.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray()
                : [];
            return await RunAsync(client, async () => CapabilitiesResult(await client.Sys.CapabilitiesSelfAsync(paths, options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Sys.HsmStatus", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => HsmStatusResult(await client.Sys.HsmStatusAsync(options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Sys.InitStatus", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => (object?)await client.Sys.InitStatusAsync(options).ConfigureAwait(false)).ConfigureAwait(false);
        });

        registry.Register("Sys.Init", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () =>
            {
                using InitResult result = await client.Sys
                    .InitAsync(OptionalInt(args, "shares"), OptionalInt(args, "threshold"), options)
                    .ConfigureAwait(false);
                return InitResultView(result);
            }).ConfigureAwait(false);
        });

        registry.Register("Sys.Seal", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () =>
            {
                await client.Sys.SealAsync(options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Sys.Unseal", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            string key = RequiredString(invocation.Arguments, "key");
            return await RunAsync(client, async () => SealStatusResult(await client.Sys.UnsealAsync(key, options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Sys.ListMounts", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => MountTableResult(await client.Sys.ListMountsAsync(options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Sys.ListAuthMethods", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => MountTableResult(await client.Sys.ListAuthMethodsAsync(options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Sys.ListMountsDetailed", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => DetailedResult(await client.Sys.ListMountsDetailedAsync(options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Sys.ReadMount", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            string path = RequiredString(invocation.Arguments, "path");
            return await RunAsync(client, async () =>
            {
                MountInfo? info = await client.Sys.ReadMountAsync(path, options).ConfigureAwait(false);
                return info is null ? null : MountInfoResult(info);
            }).ConfigureAwait(false);
        });

        registry.Register("Sys.MountTypeOf", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            string path = RequiredString(invocation.Arguments, "path");
            return await RunAsync(client, async () => (object?)await client.Sys.MountTypeOfAsync(path, options).ConfigureAwait(false)).ConfigureAwait(false);
        });

        registry.Register("Sys.Mount", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            string path = RequiredString(args, "path");
            MountRequest request = ReadMountRequest(args);
            return await RunAsync(client, async () =>
            {
                await client.Sys.MountAsync(path, request, options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Sys.EnableAuthMethod", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            string path = RequiredString(args, "path");
            MountRequest request = ReadMountRequest(args);
            return await RunAsync(client, async () =>
            {
                await client.Sys.EnableAuthMethodAsync(path, request, options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Sys.Unmount", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            string path = RequiredString(invocation.Arguments, "path");
            return await RunAsync(client, async () =>
            {
                await client.Sys.UnmountAsync(path, options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Sys.DisableAuthMethod", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            string path = RequiredString(invocation.Arguments, "path");
            return await RunAsync(client, async () =>
            {
                await client.Sys.DisableAuthMethodAsync(path, options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Sys.ListPolicies", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => (object?)(await client.Sys.ListPoliciesAsync(options).ConfigureAwait(false)).ToList()).ConfigureAwait(false);
        });

        registry.Register("Sys.ReadPolicy", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            string name = RequiredString(invocation.Arguments, "name");
            return await RunAsync(client, async () => PolicyResult(await client.Sys.ReadPolicyAsync(name, options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Sys.Legacy.ListPolicies", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => (object?)(await client.Sys.Legacy.ListPoliciesAsync(options).ConfigureAwait(false)).ToList()).ConfigureAwait(false);
        });

        registry.Register("Sys.Legacy.ReadPolicy", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            string name = RequiredString(invocation.Arguments, "name");
            return await RunAsync(client, async () => PolicyResult(await client.Sys.Legacy.ReadPolicyAsync(name, options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Sys.WritePolicy", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            string name = RequiredString(args, "name");
            string hcl = RequiredString(args, "hcl");
            return await RunAsync(client, async () =>
            {
                await client.Sys.WritePolicyAsync(name, hcl, options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Sys.DeletePolicy", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            string name = RequiredString(invocation.Arguments, "name");
            return await RunAsync(client, async () =>
            {
                await client.Sys.DeletePolicyAsync(name, options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Sys.PolicyHistory", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            string name = RequiredString(invocation.Arguments, "name");
            return await RunAsync(client, async () => (object?)(await client.Sys.PolicyHistoryAsync(name, options).ConfigureAwait(false))
                .Select(entry => (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["User"] = entry.User,
                    ["Op"] = entry.Op,
                    ["BeforeRaw"] = entry.BeforeRaw,
                    ["AfterRaw"] = entry.AfterRaw,
                })
                .ToList()).ConfigureAwait(false);
        });

        registry.Register("Sys.TestPolicy", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            string draft = RequiredString(args, "policy");
            string? name = args.TryGetProperty("name", out JsonElement nameElement) && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString()
                : null;
            PolicyTestCase[] cases = ReadPolicyTestCases(args);
            return await RunAsync(client, async () => PolicyTestResultView(await client.Sys.TestPolicyAsync(draft, cases, name, options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Sys.ReadPolicyTests", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            string name = RequiredString(invocation.Arguments, "name");
            return await RunAsync(client, async () => (object?)(await client.Sys.ReadPolicyTestsAsync(name, options).ConfigureAwait(false))
                .Select(PolicyTestCaseView)
                .ToList()).ConfigureAwait(false);
        });

        registry.Register("Sys.WritePolicyTests", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            string name = RequiredString(args, "name");
            PolicyTestCase[] cases = ReadPolicyTestCases(args);
            return await RunAsync(client, async () =>
            {
                await client.Sys.WritePolicyTestsAsync(name, cases, options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Sys.ListNamespaces", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => (object?)(await client.Sys.ListNamespacesAsync(options).ConfigureAwait(false)).ToList()).ConfigureAwait(false);
        });

        registry.Register("Sys.ReadNamespace", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            string path = RequiredString(invocation.Arguments, "path");
            return await RunAsync(client, async () => NamespaceResult(await client.Sys.ReadNamespaceAsync(path, options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Sys.WriteNamespace", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            string path = RequiredString(args, "path");
            NamespaceSpec spec = ReadNamespaceSpec(args);
            return await RunAsync(client, async () => NamespaceResult(await client.Sys.WriteNamespaceAsync(path, spec, options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Sys.UpdateNamespace", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            string path = RequiredString(args, "path");
            NamespacePatch patch = ReadNamespacePatch(args);
            return await RunAsync(client, async () => NamespaceResult(await client.Sys.UpdateNamespaceAsync(path, patch, options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Sys.DeleteNamespace", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            string path = RequiredString(invocation.Arguments, "path");
            return await RunAsync(client, async () =>
            {
                await client.Sys.DeleteNamespaceAsync(path, options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Sys.NamespacesSelf", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () =>
            {
                NamespacesSelf self = await client.Sys.NamespacesSelfAsync(options).ConfigureAwait(false);
                return new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["Namespaces"] = self.Namespaces.Select(item => (object?)item).ToList(),
                    ["TokenNamespace"] = self.TokenNamespace,
                    ["Root"] = self.Root,
                };
            }).ConfigureAwait(false);
        });

        registry.Register("Sys.ListNamespacesInfo", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            string? after = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("after", out JsonElement afterElement) && afterElement.ValueKind == JsonValueKind.String
                ? afterElement.GetString()
                : null;
            return await RunAsync(client, async () =>
            {
                Page<Namespace> page = await client.Sys.ListNamespacesInfoAsync(after, OptionalInt(args, "limit"), options).ConfigureAwait(false);
                return new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["Keys"] = page.Keys.Select(key => (object?)key).ToList(),
                    ["Records"] = page.Records.Select(record => (object?)NamespaceResult(record)).ToList(),
                    ["Total"] = page.Total,
                    ["Next"] = page.Next,
                    ["Truncated"] = page.Truncated,
                };
            }).ConfigureAwait(false);
        });

        registry.Register("Sys.Remount", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            string from = RequiredString(args, "from");
            string to = RequiredString(args, "to");
            return await RunAsync(client, async () =>
            {
                await client.Sys.RemountAsync(from, to, options).ConfigureAwait(false);
                return null;
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

    private static object HealthResult(HealthStatus health)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["State"] = health.State.ToString(),
            ["Initialized"] = health.Initialized,
            ["Sealed"] = health.Sealed,
            ["Standby"] = health.Standby,
            ["ClusterHealthy"] = health.ClusterHealthy,
            ["StatusCode"] = health.StatusCode,
        };
    }

    private static object SealStatusResult(SealStatus status)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Sealed"] = status.Sealed,
            ["T"] = status.T,
            ["N"] = status.N,
            ["KeyShares"] = status.KeyShares,
            ["KeyThreshold"] = status.KeyThreshold,
            ["Progress"] = status.Progress,
        };
    }

    private static object ServerInfoResult(ServerInfo info)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Initialized"] = info.Initialized,
            ["Sealed"] = info.Sealed,
            ["Version"] = info.Version,
            ["StartedAt"] = info.StartedAt?.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
            ["UptimeSeconds"] = info.UptimeSeconds,
            ["StorageType"] = info.StorageType,
        };
    }

    private static object ClusterStatusResult(ClusterStatus status)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["StorageType"] = status.StorageType,
            ["Cluster"] = status.Cluster,
            ["NodeId"] = status.NodeId,
            ["IsLeader"] = status.IsLeader,
            ["ClusterHealthy"] = status.ClusterHealthy,
            ["RaftMetrics"] = status.RaftMetrics?.ToDictionary(pair => pair.Key, pair => ToPlainValue(pair.Value), StringComparer.Ordinal),
        };
    }

    private static object CapabilitiesResult(Capabilities capabilities)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["ByPath"] = capabilities.ByPath.ToDictionary(
                pair => pair.Key,
                pair => (object?)pair.Value.Select(capability => (object?)capability.WireValue).ToList(),
                StringComparer.Ordinal),
            ["NamespaceOperable"] = capabilities.NamespaceOperable,
            ["TokenNamespace"] = capabilities.TokenNamespace,
            ["ActiveNamespace"] = capabilities.ActiveNamespace,
        };
    }

private static int? OptionalInt(JsonElement args, string name)
    {
        return args.ValueKind == JsonValueKind.Object
            && args.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;
    }

    private static string RequiredString(JsonElement args, string name)
    {
        return args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new FixtureAssertionException($"operation argument '{name}' is missing or is not a string.");
    }

    private static MountRequest ReadMountRequest(JsonElement args)
    {
        JsonElement request = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("request", out JsonElement element)
            ? element
            : throw new FixtureAssertionException("operation argument 'request' is missing.");

        return new MountRequest
        {
            Type = RequiredString(request, "type"),
            Description = request.TryGetProperty("description", out JsonElement description) && description.ValueKind == JsonValueKind.String
                ? description.GetString()
                : null,
            Options = request.TryGetProperty("options", out JsonElement options) && options.ValueKind == JsonValueKind.Object
                ? options.EnumerateObject().ToDictionary(option => option.Name, option => option.Value.GetString() ?? string.Empty, StringComparer.Ordinal)
                : null,
        };
    }

    private static object? PolicyResult(Policy? policy)
    {
        return policy is null
            ? null
            : new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Name"] = policy.Name,
                ["Hcl"] = policy.Hcl,
            };
    }

    private static object? NamespaceResult(Namespace? value)
    {
        return value is null
            ? null
            : new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Uuid"] = value.Uuid,
                ["Path"] = value.Path,
                ["ParentUuid"] = value.ParentUuid,
                ["ChildVisibleDefault"] = value.ChildVisibleDefault,
                ["Quotas"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["MaxStorageBytes"] = value.Quotas.MaxStorageBytes,
                    ["MaxLeases"] = value.Quotas.MaxLeases,
                    ["RequestRate"] = value.Quotas.RequestRate,
                    ["MaxMounts"] = value.Quotas.MaxMounts,
                    ["MaxEntities"] = value.Quotas.MaxEntities,
                    ["MaxChildNamespaces"] = value.Quotas.MaxChildNamespaces,
                },
            };
    }

    private static object PolicyTestCaseView(PolicyTestCase testCase)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Path"] = testCase.Path,
            ["Capability"] = testCase.Capability.WireValue,
            // SYS-045: null and [] are different answers, and the projection keeps them apart.
            ["Policies"] = testCase.Policies?.Select(policy => (object?)policy).ToList(),
            ["Env"] = testCase.Env,
        };
    }

    private static object PolicyTestResultView(PolicyTestResult result)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["ParseOk"] = result.ParseOk,
            ["Errors"] = result.Errors.Select(error => (object?)error).ToList(),
            ["Results"] = result.Results.Select(item => (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Path"] = item.Path,
                ["Capability"] = item.Capability.WireValue,
                ["Allowed"] = item.Allowed,
                ["MatchedPath"] = item.MatchedPath,
                ["MatchKind"] = item.MatchKind.WireValue,
                ["DeniedByDeny"] = item.DeniedByDeny,
                ["GrantingPolicies"] = item.GrantingPolicies.Select(policy => (object?)policy).ToList(),
                ["EvaluatedPolicies"] = item.EvaluatedPolicies.Select(policy => (object?)policy).ToList(),
                ["MissingPolicies"] = item.MissingPolicies.Select(policy => (object?)policy).ToList(),
                ["DraftOnlyAllowed"] = item.DraftOnlyAllowed,
            }).ToList(),
        };
    }

    /// <summary>
    /// SYS-045's tri-state, read from a fixture: a <c>cases[i]</c> with no <c>policies</c> key at
    /// all yields <see langword="null"/>, and one with <c>"policies": []</c> yields an empty list.
    /// A driver that could not express the difference could not drive the requirement.
    /// </summary>
    private static PolicyTestCase[] ReadPolicyTestCases(JsonElement args)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("cases", out JsonElement cases) || cases.ValueKind != JsonValueKind.Array)
        {
            throw new FixtureAssertionException("operation argument 'cases' is missing or is not an array.");
        }

        return cases.EnumerateArray().Select(item => new PolicyTestCase
        {
            Path = RequiredString(item, "path"),
            Capability = Capability.Other(RequiredString(item, "capability")),
            Policies = item.TryGetProperty("policies", out JsonElement policies) && policies.ValueKind == JsonValueKind.Array
                ? policies.EnumerateArray().Select(policy => policy.GetString() ?? string.Empty).ToList()
                : null,
            Env = item.TryGetProperty("env", out JsonElement env) && env.ValueKind == JsonValueKind.String ? env.GetString() : null,
        }).ToArray();
    }

    private static NamespaceSpec ReadNamespaceSpec(JsonElement args)
    {
        JsonElement spec = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("spec", out JsonElement element)
            ? element
            : throw new FixtureAssertionException("operation argument 'spec' is missing.");

        return new NamespaceSpec
        {
            ChildVisibleDefault = spec.TryGetProperty("childVisibleDefault", out JsonElement visible) && visible.ValueKind == JsonValueKind.True,
            Quotas = spec.TryGetProperty("quotas", out JsonElement quotas) && quotas.ValueKind == JsonValueKind.Object
                ? new NamespaceQuotas
                {
                    MaxStorageBytes = OptionalLong(quotas, "maxStorageBytes"),
                    MaxLeases = OptionalLong(quotas, "maxLeases"),
                    RequestRate = OptionalLong(quotas, "requestRate"),
                    MaxMounts = OptionalLong(quotas, "maxMounts"),
                    MaxEntities = OptionalLong(quotas, "maxEntities"),
                    MaxChildNamespaces = OptionalLong(quotas, "maxChildNamespaces"),
                }
                : null,
        };
    }

    private static NamespacePatch ReadNamespacePatch(JsonElement args)
    {
        JsonElement patch = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("patch", out JsonElement element)
            ? element
            : throw new FixtureAssertionException("operation argument 'patch' is missing.");

        return new NamespacePatch
        {
            ChildVisibleDefault = patch.TryGetProperty("childVisibleDefault", out JsonElement visible) && visible.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? visible.ValueKind == JsonValueKind.True
                : null,
            MaxStorageBytes = NullableLong(patch, "maxStorageBytes"),
            MaxLeases = NullableLong(patch, "maxLeases"),
            RequestRate = NullableLong(patch, "requestRate"),
            MaxMounts = NullableLong(patch, "maxMounts"),
            MaxEntities = NullableLong(patch, "maxEntities"),
            MaxChildNamespaces = NullableLong(patch, "maxChildNamespaces"),
        };
    }

    private static long OptionalLong(JsonElement element, string name)
    {
        return NullableLong(element, name) ?? 0L;
    }

    private static long? NullableLong(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : null;
    }

    private static object HsmStatusResult(HsmStatus status)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Type"] = status.Type,
            ["AutoUnseal"] = status.AutoUnseal,
            ["Sealed"] = status.Sealed,
            ["Initialized"] = status.Initialized,
        };
    }

    /// <summary>
    /// SYS-011: the harness never surfaces the key material itself. Each secret is projected as a
    /// <see cref="RedactedValue"/>, which a fixture asserts with the <c>$redacted</c> sentinel and
    /// which fails that assertion if its <c>ToString</c> ever reveals the value it wraps.
    /// </summary>
    private static object InitResultView(InitResult result)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["KeyCount"] = result.KeyCount,
            ["Keys"] = result.Keys.Select(key => (object?)new RedactedValue(key.Reveal() ?? string.Empty)).ToList(),
            ["RootToken"] = new RedactedValue(result.RootToken.Reveal() ?? string.Empty),
        };
    }

    private static object MountInfoResult(MountInfo info)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Type"] = info.Type,
            ["Description"] = info.Description,
        };
    }

    private static object MountTableResult(IReadOnlyDictionary<string, MountInfo> table)
    {
        return table.ToDictionary(pair => pair.Key, pair => (object?)MountInfoResult(pair.Value), StringComparer.Ordinal);
    }

    private static object DetailedResult(MountTable table)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Secret"] = table.Secret.ToDictionary(pair => pair.Key, pair => (object?)MountDetailResult(pair.Value), StringComparer.Ordinal),
            ["Auth"] = table.Auth.ToDictionary(pair => pair.Key, pair => (object?)MountDetailResult(pair.Value), StringComparer.Ordinal),
        };
    }

    private static object MountDetailResult(MountDetail detail)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Type"] = detail.Type,
            ["Description"] = detail.Description,
            ["Uuid"] = detail.Uuid,
            ["Options"] = detail.Options?.ToDictionary(pair => pair.Key, pair => ToPlainValue(pair.Value), StringComparer.Ordinal),
        };
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
