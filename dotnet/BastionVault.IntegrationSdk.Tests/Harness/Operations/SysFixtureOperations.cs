using System.Text.Json;
using BastionVault.IntegrationSdk;

namespace BastionVault.IntegrationSdk.Tests.Harness.Operations;

/// <summary>
/// Registers the landed <c>Sys.*</c> fixture operations against the real SDK (DR-0007, DR-0012):
/// health and status (SYS-001, SYS-002, SYS-005, SYS-006, SYS-008, <c>HsmStatus</c>), self
/// capability introspection (SYS-050…SYS-053), initialisation/seal/unseal (SYS-010…SYS-013),
/// mounts (SYS-020…SYS-026) and auth methods (SYS-030), and M7c's audit (SYS-070), identity
/// (SYS-080), backup/restore (SYS-090, SYS-091), RES-030 cluster-wide variants and the
/// Complete-tier admin surfaces, driven through the real <see cref="BastionVaultClient"/> and the fixture's
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

        // ---- M7c (DR-0012): audit, identity, backup/restore, the Complete-tier admin surfaces
        // and RES-030. Registered here so a future fixture naming any of them is *run* rather
        // than reported pending for want of a registration (D-M2-10).

        registry.Register("Sys.Audit.ListDevices", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => (object?)(await client.Sys.Audit.ListDevicesAsync(options).ConfigureAwait(false))
                .Select(device => (object?)AuditDeviceResult(device)).ToList()).ConfigureAwait(false);
        });

        registry.Register("Sys.Audit.EnableDevice", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            string path = RequiredString(args, "path");
            AuditDeviceSpec spec = ReadAuditDeviceSpec(args);
            return await RunAsync(client, async () =>
            {
                await client.Sys.Audit.EnableDeviceAsync(path, spec, options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Sys.Audit.DisableDevice", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            string path = RequiredString(invocation.Arguments, "path");
            return await RunAsync(client, async () =>
            {
                await client.Sys.Audit.DisableDeviceAsync(path, options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Sys.Audit.Events", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => (object?)(await client.Sys.Audit
                .EventsAsync(OptionalTimestamp(args, "from"), OptionalTimestamp(args, "to"), OptionalInt(args, "limit") ?? 500, options)
                .ConfigureAwait(false)).Select(item => (object?)AuditEventResult(item)).ToList()).ConfigureAwait(false);
        });

        registry.Register("Sys.DashboardSummary", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () =>
            {
                DashboardSummary summary = await client.Sys.DashboardSummaryAsync(options).ConfigureAwait(false);
                return new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["HasAudit24h"] = summary.Audit24h is not null,
                    ["HasAttention"] = summary.Attention is not null,
                };
            }).ConfigureAwait(false);
        });

        registry.Register("Sys.SsoSettings", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => (object?)(await client.Sys.SsoSettingsAsync(options).ConfigureAwait(false)).GetRawText()).ConfigureAwait(false);
        });

        registry.Register("Sys.SsoProviders", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => (object?)(await client.Sys.SsoProvidersAsync(options).ConfigureAwait(false)).GetRawText()).ConfigureAwait(false);
        });

        registry.Register("Sys.Dos.ReadConfig", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => DosConfigResult(await client.Sys.Dos.ReadConfigAsync(options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Sys.Dos.WriteConfig", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            DosConfig patch = ReadDosConfig(invocation.Arguments);
            return await RunAsync(client, async () => DosConfigResult(await client.Sys.Dos.WriteConfigAsync(patch, options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Sys.Dos.Stats", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => (object?)(await client.Sys.Dos.StatsAsync(options).ConfigureAwait(false)).GetRawText()).ConfigureAwait(false);
        });

        registry.Register("Sys.Dos.Ban", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            string ip = RequiredString(args, "ip");
            return await RunAsync(client, async () =>
            {
                await client.Sys.Dos.BanAsync(ip, OptionalInt(args, "ttlSecs"), OptionalString(args, "reason"), options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Sys.Dos.Unban", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            string ip = RequiredString(invocation.Arguments, "ip");
            return await RunAsync(client, async () =>
            {
                await client.Sys.Dos.UnbanAsync(ip, options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Sys.Backup", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => (object?)Convert.ToBase64String(
                await client.Sys.BackupAsync(options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Sys.Restore", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            byte[] bytes = Convert.FromBase64String(RequiredString(invocation.Arguments, "backup"));
            return await RunAsync(client, async () =>
            {
                RestoreResult result = await client.Sys.RestoreAsync(bytes, options).ConfigureAwait(false);
                return new Dictionary<string, object?>(StringComparer.Ordinal) { ["EntriesRestored"] = result.EntriesRestored };
            }).ConfigureAwait(false);
        });

        registry.Register("Sys.SealClusterWide", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => ClusterWideResult(
                await client.Sys.SealClusterWideAsync(options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Sys.UnsealClusterWide", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            string key = RequiredString(invocation.Arguments, "key");
            return await RunAsync(client, async () => ClusterWideResult(
                await client.Sys.UnsealClusterWideAsync(key, options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Sys.OwnerTransfer.Kv", async invocation => await OwnerTransferAsync(invocation, "kv").ConfigureAwait(false));
        registry.Register("Sys.OwnerTransfer.Resource", async invocation => await OwnerTransferAsync(invocation, "resource").ConfigureAwait(false));
        registry.Register("Sys.OwnerTransfer.AssetGroup", async invocation => await OwnerTransferAsync(invocation, "asset-group").ConfigureAwait(false));
        registry.Register("Sys.OwnerTransfer.File", async invocation => await OwnerTransferAsync(invocation, "file").ConfigureAwait(false));

        registry.Register("Sys.Exchange.Export", async invocation => await ExchangeAsync(invocation, "export").ConfigureAwait(false));
        registry.Register("Sys.Exchange.Import", async invocation => await ExchangeAsync(invocation, "import").ConfigureAwait(false));
        registry.Register("Sys.Exchange.ImportPreview", async invocation => await ExchangeAsync(invocation, "import/preview").ConfigureAwait(false));
        registry.Register("Sys.Exchange.ImportApply", async invocation => await ExchangeAsync(invocation, "import/apply").ConfigureAwait(false));

        registry.Register("Identity.Profile.Read", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () =>
            {
                IdentityProfile profile = await client.Identity.Profile.ReadAsync(options).ConfigureAwait(false);
                return new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["Username"] = profile.Username,
                    ["DisplayName"] = profile.DisplayName,
                    ["Email"] = profile.Email,
                    ["Phone"] = profile.Phone,
                    ["Mount"] = profile.Mount,
                };
            }).ConfigureAwait(false);
        });

        registry.Register("Identity.Profile.ChangePassword", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () =>
            {
                await client.Identity.Profile.ChangePasswordAsync(
                    new SecretString(RequiredString(args, "current")),
                    new SecretString(RequiredString(args, "new")),
                    options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Identity.Profile.UpdateContact", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () =>
            {
                await client.Identity.Profile.UpdateContactAsync(OptionalString(args, "email"), OptionalString(args, "phone"), options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Identity.DefaultAccount.ReadSelf", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => DefaultAccountResult(
                await client.Identity.DefaultAccount.ReadSelfAsync(options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Identity.DefaultAccount.WriteSelf", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            DefaultAccountSpec spec = ReadDefaultAccountSpec(invocation.Arguments);
            return await RunAsync(client, async () =>
            {
                await client.Identity.DefaultAccount.WriteSelfAsync(spec, options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Identity.DefaultAccount.Read", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => DefaultAccountResult(await client.Identity.DefaultAccount
                .ReadAsync(RequiredString(args, "mount"), RequiredString(args, "name"), options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Identity.DefaultAccount.Write", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            DefaultAccountSpec spec = ReadDefaultAccountSpec(args);
            return await RunAsync(client, async () =>
            {
                await client.Identity.DefaultAccount
                    .WriteAsync(RequiredString(args, "mount"), RequiredString(args, "name"), spec, options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Identity.SshSecurityKey.List", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => (object?)(await client.Identity.SshSecurityKey.ListAsync(options).ConfigureAwait(false)).ToList()).ConfigureAwait(false);
        });

        registry.Register("Identity.SshSecurityKey.ReadSelf", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => SshKeyResult(
                await client.Identity.SshSecurityKey.ReadSelfAsync(options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Identity.SshSecurityKey.WriteSelf", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            SshSecurityKeySpec spec = ReadSshKeySpec(invocation.Arguments);
            return await RunAsync(client, async () =>
            {
                await client.Identity.SshSecurityKey.WriteSelfAsync(spec, options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Identity.SshSecurityKey.DeleteSelf", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () =>
            {
                await client.Identity.SshSecurityKey.DeleteSelfAsync(options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Identity.SshSecurityKey.Read", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () => SshKeyResult(await client.Identity.SshSecurityKey
                .ReadAsync(RequiredString(args, "mount"), RequiredString(args, "name"), options).ConfigureAwait(false))).ConfigureAwait(false);
        });

        registry.Register("Identity.SshSecurityKey.Write", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            SshSecurityKeySpec spec = ReadSshKeySpec(args);
            return await RunAsync(client, async () =>
            {
                await client.Identity.SshSecurityKey
                    .WriteAsync(RequiredString(args, "mount"), RequiredString(args, "name"), spec, options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Identity.SshSecurityKey.Delete", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () =>
            {
                await client.Identity.SshSecurityKey
                    .DeleteAsync(RequiredString(args, "mount"), RequiredString(args, "name"), options).ConfigureAwait(false);
                return null;
            }).ConfigureAwait(false);
        });

        registry.Register("Identity.NamespaceAssignment.List", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            return await RunAsync(client, async () => (object?)(await client.Identity.NamespaceAssignment.ListAsync(options).ConfigureAwait(false)).ToList()).ConfigureAwait(false);
        });

        registry.Register("Identity.NamespaceAssignment.Read", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            return await RunAsync(client, async () =>
            {
                NamespaceAssignment? assignment = await client.Identity.NamespaceAssignment
                    .ReadAsync(RequiredString(args, "mount"), RequiredString(args, "name"), options).ConfigureAwait(false);
                return assignment is null
                    ? null
                    : new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["Namespaces"] = assignment.Namespaces.Select(item => (object?)item).ToList(),
                        ["DefaultNamespace"] = assignment.DefaultNamespace,
                    };
            }).ConfigureAwait(false);
        });

        registry.Register("Identity.NamespaceAssignment.Write", async invocation =>
        {
            (BastionVaultClient client, RequestOptions options) = Build(invocation);
            JsonElement args = invocation.Arguments;
            string[] namespaces = args.TryGetProperty("namespaces", out JsonElement element) && element.ValueKind == JsonValueKind.Array
                ? element.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray()
                : [];
            return await RunAsync(client, async () =>
            {
                await client.Identity.NamespaceAssignment.WriteAsync(
                    RequiredString(args, "mount"), RequiredString(args, "name"), namespaces, OptionalString(args, "defaultNamespace"), options).ConfigureAwait(false);
                return null;
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

    private static async ValueTask<FixtureOperationResult> OwnerTransferAsync(FixtureInvocation invocation, string kind)
    {
        (BastionVaultClient client, RequestOptions options) = Build(invocation);
        JsonElement spec = invocation.Arguments.TryGetProperty("spec", out JsonElement element) ? element.Clone() : default;
        return await RunAsync(client, async () =>
        {
            JsonElement? result = kind switch
            {
                "kv" => await client.Sys.OwnerTransfer.KvAsync(spec, options).ConfigureAwait(false),
                "resource" => await client.Sys.OwnerTransfer.ResourceAsync(spec, options).ConfigureAwait(false),
                "asset-group" => await client.Sys.OwnerTransfer.AssetGroupAsync(spec, options).ConfigureAwait(false),
                _ => await client.Sys.OwnerTransfer.FileAsync(spec, options).ConfigureAwait(false),
            };
            return (object?)result?.GetRawText();
        }).ConfigureAwait(false);
    }

    private static async ValueTask<FixtureOperationResult> ExchangeAsync(FixtureInvocation invocation, string kind)
    {
        (BastionVaultClient client, RequestOptions options) = Build(invocation);
        JsonElement request = invocation.Arguments.TryGetProperty("request", out JsonElement element) ? element.Clone() : default;
        return await RunAsync(client, async () =>
        {
            JsonElement? result = kind switch
            {
                "export" => await client.Sys.Exchange.ExportAsync(request, options).ConfigureAwait(false),
                "import" => await client.Sys.Exchange.ImportAsync(request, options).ConfigureAwait(false),
                "import/preview" => await client.Sys.Exchange.ImportPreviewAsync(request, options).ConfigureAwait(false),
                _ => await client.Sys.Exchange.ImportApplyAsync(request, options).ConfigureAwait(false),
            };
            return (object?)result?.GetRawText();
        }).ConfigureAwait(false);
    }

    private static object AuditDeviceResult(AuditDevice device)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Path"] = device.Path,
            ["Type"] = device.Type,
            ["Description"] = device.Description,
            ["Namespace"] = device.Namespace,
            ["Mirror"] = device.Mirror,
        };
    }

    private static object AuditEventResult(AuditEvent item)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Timestamp"] = item.Timestamp?.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["User"] = item.User,
            ["Machine"] = item.Machine,
            ["Op"] = item.Op,
            ["Category"] = item.Category,
            ["Target"] = item.Target,
            ["ChangedFields"] = item.ChangedFields.Select(field => (object?)field).ToList(),
            ["Summary"] = item.Summary,
        };
    }

    private static object DosConfigResult(DosConfig config)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Enabled"] = config.Enabled,
            ["WindowSecs"] = config.WindowSecs,
            ["MaxRequests"] = config.MaxRequests,
            ["AuthMaxRequests"] = config.AuthMaxRequests,
            ["BanSecs"] = config.BanSecs,
            ["RefreshSecs"] = config.RefreshSecs,
        };
    }

    private static object ClusterWideResult(IReadOnlyDictionary<string, ClusterNodeResult> results)
    {
        return results.ToDictionary(
            entry => entry.Key,
            entry => (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Url"] = entry.Value.Url,
                ["Succeeded"] = entry.Value.Succeeded,
                ["Sealed"] = entry.Value.SealStatus?.Sealed,
                ["ErrorCode"] = entry.Value.Error?.Code,
            },
            StringComparer.Ordinal);
    }

    private static object? DefaultAccountResult(DefaultAccount? account)
    {
        return account is null
            ? null
            : new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Username"] = account.Username,
                ["Domain"] = account.Domain,
                // CNF-031: the value is never put in a fixture result. Whether the server sent it
                // is the observable SYS-080 actually specifies.
                ["HasWindowsPassword"] = account.WindowsPassword is not null,
            };
    }

    private static object? SshKeyResult(SshSecurityKey? key)
    {
        return key is null
            ? null
            : new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Name"] = key.Name,
                ["PublicKey"] = key.PublicKey,
                ["Fingerprint"] = key.Fingerprint,
            };
    }

    private static AuditDeviceSpec ReadAuditDeviceSpec(JsonElement args)
    {
        JsonElement spec = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("spec", out JsonElement element)
            ? element
            : throw new FixtureAssertionException("operation argument 'spec' is missing.");

        return new AuditDeviceSpec
        {
            Type = RequiredString(spec, "type"),
            Description = OptionalString(spec, "description"),
            Options = spec.TryGetProperty("options", out JsonElement options) && options.ValueKind == JsonValueKind.Object
                ? options.EnumerateObject().ToDictionary(option => option.Name, option => option.Value.GetString() ?? string.Empty, StringComparer.Ordinal)
                : null,
            Mirror = spec.TryGetProperty("mirror", out JsonElement mirror) && mirror.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? mirror.GetBoolean()
                : null,
        };
    }

    private static DosConfig ReadDosConfig(JsonElement args)
    {
        JsonElement patch = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("patch", out JsonElement element) ? element : args;
        return new DosConfig
        {
            Enabled = patch.ValueKind == JsonValueKind.Object && patch.TryGetProperty("enabled", out JsonElement enabled) && enabled.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? enabled.GetBoolean()
                : null,
            WindowSecs = OptionalLong(patch, "window_secs"),
            MaxRequests = OptionalLong(patch, "max_requests"),
            AuthMaxRequests = OptionalLong(patch, "auth_max_requests"),
            BanSecs = OptionalLong(patch, "ban_secs"),
            RefreshSecs = OptionalLong(patch, "refresh_secs"),
        };
    }

    private static DefaultAccountSpec ReadDefaultAccountSpec(JsonElement args)
    {
        JsonElement spec = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("spec", out JsonElement element) ? element : args;
        string? password = OptionalString(spec, "windows_password");
        return new DefaultAccountSpec
        {
            Username = OptionalString(spec, "username"),
            Domain = OptionalString(spec, "domain"),
            WindowsPassword = password is null ? null : new SecretString(password),
        };
    }

    private static SshSecurityKeySpec ReadSshKeySpec(JsonElement args)
    {
        JsonElement spec = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("spec", out JsonElement element) ? element : args;
        return new SshSecurityKeySpec
        {
            Name = OptionalString(spec, "name"),
            PublicKey = OptionalString(spec, "public_key"),
        };
    }

    private static string? OptionalString(JsonElement args, string name)
    {
        return args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static DateTimeOffset? OptionalTimestamp(JsonElement args, string name)
    {
        return OptionalString(args, name) is { } text
            && DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)
            ? parsed
            : null;
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
