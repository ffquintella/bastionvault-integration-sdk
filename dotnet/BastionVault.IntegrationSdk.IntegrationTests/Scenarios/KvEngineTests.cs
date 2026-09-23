using System.Text.Json;
using BastionVault.IntegrationSdk.IntegrationTests.Harness;

namespace BastionVault.IntegrationSdk.IntegrationTests.Scenarios;

/// <summary>ITG-S13: KV v1 (15-testing-requirements.md:225-226).</summary>
public sealed class Scenario13_KvV1 : IntegrationTest
{
    [IntegrationFact]
    public async Task Write_read_list_delete_and_ttl_lease_all_map_correctly()
    {
        string mount = await Resources.MountAsync("kv", "v1");
        string path = Unique("secret");

        await Client.Kv.V1.WriteAsync(path, Data("value", "one"), mount);
        KvV1Secret? read = await Client.Kv.V1.ReadAsync(path, mount);
        Assert.NotNull(read);
        Assert.Equal("one", read!.Data["value"].GetString());

        IReadOnlyList<string> listed = await Client.Kv.V1.ListAsync(mount: mount);
        Assert.Contains(path, listed);

        await Client.Kv.V1.DeleteAsync(path, mount);
        KvV1Secret? afterDelete = await Client.Kv.V1.ReadAsync(path, mount);
        Assert.Null(afterDelete);

        // F2 relevance (DR-0021): the SDK already spells this `ttl` as a Go duration string
        // (KvV1Operations.WriteAsync's own doc comment, GoDuration.Format), not as the numeric
        // form F2 found rejected on auth/token/create. This is the positive control.
        string leasedPath = Unique("leased");
        await Client.Kv.V1.WriteAsync(leasedPath, Data("value", "two"), mount, ttl: TimeSpan.FromMinutes(5));
        KvV1Secret leased = await Client.Kv.V1.GetAsync(leasedPath, mount);
        Assert.True(leased.Renewable);
        Assert.True(leased.LeaseDuration > TimeSpan.Zero);
    }

    private static IReadOnlyDictionary<string, JsonElement> Data(string key, string value)
    {
        return new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [key] = JsonDocument.Parse($"\"{value}\"").RootElement.Clone(),
        };
    }
}

/// <summary>ITG-S14: KV v2 lifecycle (15-testing-requirements.md:227-232).</summary>
public sealed class Scenario14_KvV2Lifecycle : IntegrationTest
{
    [IntegrationFact]
    public async Task Versions_cas_soft_delete_destroy_and_metadata_all_map_correctly()
    {
        string mount = await Resources.MountAsync("kv-v2", "lifecycle");
        string path = Unique("secret");

        KvV2VersionMetadata v1 = await Client.Kv.V2.WriteSecretAsync(path, Data("value", "one"), mount);
        Assert.Equal(1, v1.Version);

        KvV2VersionMetadata v2 = await Client.Kv.V2.WriteSecretAsync(path, Data("value", "two"), mount);
        Assert.Equal(2, v2.Version);

        KvV2Secret? latest = await Client.Kv.V2.ReadSecretAsync(path, mount);
        Assert.NotNull(latest);
        Assert.Equal("two", latest!.Data!["value"].GetString());

        KvV2Secret? readV1 = await Client.Kv.V2.ReadSecretAsync(path, mount, version: 1);
        Assert.NotNull(readV1);
        Assert.Equal("one", readV1!.Data!["value"].GetString());

        KvV2VersionMetadata v3 = await Client.Kv.V2.WriteSecretAsync(
            path, Data("value", "three"), mount, new KvWriteOptions { Cas = 2 });
        Assert.Equal(3, v3.Version);

        BastionVaultException casMismatch = await Assert.ThrowsAsync<BastionVaultException>(
            () => Client.Kv.V2.WriteSecretAsync(path, Data("value", "four"), mount, new KvWriteOptions { Cas = 1 }));
        Assert.Equal(ErrorCodes.KvCasMismatch, casMismatch.Code);

        KvV2Config? config = await Client.Kv.V2.ReadConfigAsync(mount);
        Assert.NotNull(config);
        await Client.Kv.V2.WriteConfigAsync(new KvV2Config
        {
            MaxVersions = config!.MaxVersions,
            CasRequired = true,
            DeleteVersionAfter = config.DeleteVersionAfter,
            Environments = config.Environments,
        }, mount);

        BastionVaultException casRequired = await Assert.ThrowsAsync<BastionVaultException>(
            () => Client.Kv.V2.WriteSecretAsync(path, Data("value", "five"), mount));
        Assert.Equal(ErrorCodes.KvCasRequired, casRequired.Code);

        await Client.Kv.V2.SoftDeleteAsync(path, mount);
        KvV2Secret? softDeleted = await Client.Kv.V2.ReadSecretAsync(path, mount);
        Assert.NotNull(softDeleted);
        Assert.Equal(KvV2SecretState.SoftDeleted, softDeleted!.State);
        _ = Assert.NotNull(softDeleted.Metadata.DeletionTime);

        await Client.Kv.V2.UndeleteAsync(path, [3], mount);
        KvV2Secret? undeleted = await Client.Kv.V2.ReadSecretAsync(path, mount);
        Assert.NotNull(undeleted);
        Assert.Equal(KvV2SecretState.Live, undeleted!.State);

        await Client.Kv.V2.DestroyAsync(path, [1], mount);
        BastionVaultException destroyed = await Assert.ThrowsAsync<BastionVaultException>(
            () => Client.Kv.V2.GetSecretAsync(path, mount, version: 1));
        Assert.Equal(ErrorCodes.KvVersionDestroyed, destroyed.Code);

        KvV2Metadata? metadata = await Client.Kv.V2.ReadMetadataAsync(path, mount);
        Assert.NotNull(metadata);
        Assert.True(metadata!.Versions.TryGetValue(1, out KvV2VersionMetadata? v1Metadata));
        Assert.True(v1Metadata!.Destroyed);
        Assert.True(metadata.Versions.TryGetValue(3, out KvV2VersionMetadata? v3Metadata));
        Assert.False(v3Metadata!.Destroyed);

        IReadOnlyList<string> listed = await Client.Kv.V2.ListAsync(mount: mount);
        Assert.Contains(path, listed);

        await Client.Kv.V2.DeleteMetadataAsync(path, mount);
        KvV2Metadata? afterDeleteMetadata = await Client.Kv.V2.ReadMetadataAsync(path, mount);
        Assert.Null(afterDeleteMetadata);
    }

    private static IReadOnlyDictionary<string, JsonElement> Data(string key, string value)
    {
        return new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [key] = JsonDocument.Parse($"\"{value}\"").RootElement.Clone(),
        };
    }
}

/// <summary>ITG-S15: KV v2 `max_versions` pruning (15-testing-requirements.md:233-234).</summary>
public sealed class Scenario15_KvV2MaxVersions : IntegrationTest
{
    [IntegrationFact]
    public async Task Max_versions_prunes_the_oldest_version_from_metadata()
    {
        string mount = await Resources.MountAsync("kv-v2", "maxver");
        string path = Unique("secret");

        KvV2Config? config = await Client.Kv.V2.ReadConfigAsync(mount);
        Assert.NotNull(config);
        await Client.Kv.V2.WriteConfigAsync(new KvV2Config
        {
            MaxVersions = 2,
            CasRequired = config!.CasRequired,
            DeleteVersionAfter = config.DeleteVersionAfter,
            Environments = config.Environments,
        }, mount);

        _ = await Client.Kv.V2.WriteSecretAsync(path, Data("value", "one"), mount);
        _ = await Client.Kv.V2.WriteSecretAsync(path, Data("value", "two"), mount);
        _ = await Client.Kv.V2.WriteSecretAsync(path, Data("value", "three"), mount);

        KvV2Metadata? metadata = await Client.Kv.V2.ReadMetadataAsync(path, mount);
        Assert.NotNull(metadata);
        Assert.DoesNotContain(1, metadata!.Versions.Keys);
        Assert.Contains(3, metadata.Versions.Keys);
    }

    private static IReadOnlyDictionary<string, JsonElement> Data(string key, string value)
    {
        return new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [key] = JsonDocument.Parse($"\"{value}\"").RootElement.Clone(),
        };
    }
}

/// <summary>ITG-S16: KV v2 environments (15-testing-requirements.md:235-239).</summary>
public sealed class Scenario16_KvV2Environments : IntegrationTest
{
    [IntegrationFact]
    public async Task Environment_writes_reads_and_the_env_envs_guard_all_map_correctly()
    {
        string mount = await Resources.MountAsync("kv-v2", "envs");
        string path = Unique("secret");

        _ = await Client.Kv.V2.WriteAllEnvironmentsAsync(
            path,
            Data("value", "base"),
            new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>(StringComparer.Ordinal)
            {
                ["prod"] = Data("value", "prod-value"),
                ["staging"] = Data("value", "staging-value"),
            },
            mount);

        KvV2Secret? baseRead = await Client.Kv.V2.ReadSecretAsync(path, mount);
        Assert.NotNull(baseRead);
        Assert.Equal("base", baseRead!.Data!["value"].GetString());

        KvV2Secret? prodRead = await Client.Kv.V2.ReadSecretAsync(path, mount, env: "prod");
        Assert.NotNull(prodRead);
        Assert.Equal("prod-value", prodRead!.Data!["value"].GetString());
        Assert.Equal("prod", prodRead.Metadata.ResolvedEnv);
        Assert.Contains("prod", prodRead.Metadata.AvailableEnvs);
        Assert.Contains("staging", prodRead.Metadata.AvailableEnvs);

        _ = await Client.Kv.V2.PatchEnvironmentAsync(path, "staging", Data("value", "staging-patched"), mount);

        KvV2Secret? prodStillThere = await Client.Kv.V2.ReadSecretAsync(path, mount, env: "prod");
        Assert.NotNull(prodStillThere);
        Assert.Equal("prod-value", prodStillThere!.Data!["value"].GetString());

        KvV2Secret? stagingPatched = await Client.Kv.V2.ReadSecretAsync(path, mount, env: "staging");
        Assert.NotNull(stagingPatched);
        Assert.Equal("staging-patched", stagingPatched!.Data!["value"].GetString());

        BastionVaultException undeclared = await Assert.ThrowsAsync<BastionVaultException>(
            () => Client.Kv.V2.GetSecretAsync(path, mount, env: "dev"));
        Assert.Equal(ErrorCodes.KvEnvironmentNotDeclared, undeclared.Code);

        BastionVaultException clientSide = await Assert.ThrowsAsync<BastionVaultException>(
            () => Client.Kv.V2.WriteSecretAsync(
                path,
                Data("value", "x"),
                mount,
                new KvWriteOptions
                {
                    Env = "prod",
                    Envs = new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>(StringComparer.Ordinal)
                    {
                        ["prod"] = Data("value", "y"),
                    },
                }));
        Assert.Equal(ErrorCodes.InputInvalidArgument, clientSide.Code);

        string dataPath = KvV2Operations.DataPath(path, mount);
        BastionVaultException serverSide = await Assert.ThrowsAsync<BastionVaultException>(
            () => Client.Logical.WriteAsync(
                dataPath,
                JsonDocument.Parse("""{"data":{"value":"z"},"env":"prod","envs":{"prod":{"value":"z"}}}""").RootElement));
        Assert.Equal(400, serverSide.StatusCode);

        _ = await Client.Kv.V2.UpdateConfigAsync(new KvV2ConfigPatch { Environments = ["prod", "staging", "dev"] }, mount);
        KvV2Config? roundTripped = await Client.Kv.V2.ReadConfigAsync(mount);
        Assert.NotNull(roundTripped);
        Assert.Contains("prod", roundTripped!.Environments);
        Assert.Contains("dev", roundTripped.Environments);
    }

    private static IReadOnlyDictionary<string, JsonElement> Data(string key, string value)
    {
        return new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [key] = JsonDocument.Parse($"\"{value}\"").RootElement.Clone(),
        };
    }
}

/// <summary>ITG-S17: `Kv.ReadMany` over `Sys.Batch` (15-testing-requirements.md:240-241).</summary>
public sealed class Scenario17_KvReadMany : IntegrationTest
{
    [IntegrationFact]
    public async Task ReadMany_over_five_secrets_returns_all_five_with_one_authz_failure_in_its_slot()
    {
        string mount = await Resources.MountAsync("kv-v2", "readmany");
        List<string> paths = [];
        for (int i = 0; i < 5; i++)
        {
            string secretPath = Unique($"secret{i}");
            _ = await Client.Kv.V2.WriteSecretAsync(secretPath, Data("value", $"v{i}"), mount);
            paths.Add(secretPath);
        }

        string deniedPath = paths[2];
        string policyHcl = string.Join('\n', paths.Select(p => $$"""
            path "{{mount}}/data/{{p}}" {
              capabilities = ["{{(p == deniedPath ? "deny" : "read")}}"]
            }
            """));
        string policyName = await Resources.WritePolicyAsync("readmany", policyHcl);

        AuthInfo restricted = await Client.Auth.Token.CreateAsync(new CreateTokenRequest { Policies = [policyName] });
        Resources.TrackToken("readmany-scoped", restricted.ClientToken);

        using BastionVaultClient scopedClient = Server.CreateClient(o => o.Token = restricted.ClientToken.Reveal());
        IReadOnlyDictionary<string, KvReadManyEntry> results = await scopedClient.Kv.ReadManyAsync(mount, paths);

        Assert.Equal(5, results.Count);
        foreach (string p in paths)
        {
            Assert.True(results.TryGetValue(p, out KvReadManyEntry? entry));
            if (p == deniedPath)
            {
                Assert.False(entry!.IsSuccess);
                Assert.Equal(ErrorCodes.AuthzPermissionDenied, entry.Error!.Code);
            }
            else
            {
                Assert.True(entry!.IsSuccess);
                Assert.NotNull(entry.Data);
            }
        }
    }

    private static IReadOnlyDictionary<string, JsonElement> Data(string key, string value)
    {
        return new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [key] = JsonDocument.Parse($"\"{value}\"").RootElement.Clone(),
        };
    }
}
