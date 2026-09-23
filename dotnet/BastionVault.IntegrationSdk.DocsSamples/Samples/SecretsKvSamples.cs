using System.Text.Json;
using BastionVault.IntegrationSdk.DocsSamples.Infrastructure;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.DocsSamples.Samples;

/// <summary>
/// Every executed sample shown by <c>docs/dotnet/secrets-kv.md</c> (D5), the .NET adaptation of
/// guides 5-7 in <c>specifications/17-usage-guides.md</c>. Reuses <see cref="MockVaultFixture"/>'s
/// server: routes are bound per test on <see cref="MockVaultFixture.Server"/> and cleared after,
/// so no test leaks a route to another.
/// </summary>
public sealed class SecretsKvSamples : IClassFixture<MockVaultFixture>
{
    private const string DataRoute = "/v1/secret/data/app/db";
    private const string MetadataRoute = "/v1/secret/metadata/app/db";
    private const string UndeleteRoute = "/v1/secret/undelete/app/db";
    private const string DestroyRoute = "/v1/secret/destroy/app/db";

    private readonly MockVaultFixture vault;

    public SecretsKvSamples(MockVaultFixture vault)
    {
        this.vault = vault;
    }

    [Fact]
    public void The_policy_this_guide_needs_is_the_policy_PolicyBuilder_builds()
    {
        // docs:begin secrets-kv/policy
        // "data/" is part of the logical path a policy names; the SDK inserts it for you when
        // you call Kv.V2 with just the mount and the path inside it.
        string hcl = new PolicyBuilder()
            .AddPath("secret/data/app/*", [Capability.Create, Capability.Read, Capability.Update, Capability.Delete])
            .AddPath("secret/metadata/app/*", [Capability.Read, Capability.List, Capability.Delete])
            .AddPath("secret/undelete/app/*", [Capability.Update])
            .AddPath("secret/destroy/app/*", [Capability.Update])
            .Build();

        Console.WriteLine(hcl);
        // docs:end secrets-kv/policy

        Assert.Equal(PolicyShownInTheGuide(), hcl.Trim());
    }

    private static string PolicyShownInTheGuide()
    {
        string path = Path.Combine(DocsRepository.Docs.FullName, "dotnet", "secrets-kv.md");
        string[] lines = File.ReadAllLines(path);
        int opening = Array.FindIndex(lines, line => line.TrimEnd() == "```hcl");
        Assert.True(opening >= 0, $"{path} shows no ```hcl policy block (DOC-011).");
        int closing = Array.FindIndex(lines, opening + 1, line => line.TrimEnd() == "```");
        Assert.True(closing > opening, $"{path}: the ```hcl block is never closed.");
        return string.Join('\n', lines[(opening + 1)..closing]).Trim();
    }

    [Fact]
    public async Task Step_1_write_version_and_check_and_set()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(DataRoute, Json(200, WriteBody(version: 1)));

        // docs:begin secrets-kv/write-and-cas
        KvV2VersionMetadata v1 = await client.Kv.V2.WriteSecretAsync("app/db", Data(("username", "admin"), ("password", "p1")));

        vault.Server.SetRouteResponse(DataRoute, Json(200, WriteBody(version: 2)));
        // Cas: v1.Version means "only write if the current version is still v1" - optimistic
        // concurrency, no server-side lock held between the read and this write.
        KvV2VersionMetadata v2 = await client.Kv.V2.WriteSecretAsync(
            "app/db", Data(("username", "admin"), ("password", "p2")), options: new KvWriteOptions { Cas = v1.Version });

        vault.Server.SetRouteResponse(DataRoute, Json(400, """{"error":"Check-and-set parameter did not match the current version."}"""));
        try
        {
            await client.Kv.V2.WriteSecretAsync("app/db", Data(("username", "admin")), options: new KvWriteOptions { Cas = 1 });
        }
        catch (BastionVaultException e) when (e.Code == ErrorCodes.KvCasMismatch)
        {
            Console.WriteLine("someone else wrote first; re-read and retry");
        }

        vault.Server.SetRouteResponse(DataRoute, Json(200, ReadVersionBody(version: 1, username: "admin", password: "p1")));
        KvV2Secret? old = await client.Kv.V2.ReadSecretAsync("app/db", version: 1);
        Console.WriteLine($"v1 password was {old!.Data!["password"].GetString()}");
        // docs:end secrets-kv/write-and-cas

        Assert.Equal(1, v1.Version);
        Assert.Equal(2, v2.Version);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task Step_2_soft_delete_undelete_and_destroy()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        // docs:begin secrets-kv/soft-delete-and-destroy
        vault.Server.SetRouteResponse(DataRoute, new MockResponse(204, BodyIsJson: false));
        await client.Kv.V2.SoftDeleteAsync("app/db"); // DELETE data/app/db, latest version only

        vault.Server.SetRouteResponse(DataRoute, Json(200, SoftDeletedBody(version: 2)));
        KvV2Secret? deleted = await client.Kv.V2.ReadSecretAsync("app/db");
        // Soft-deleted is a successful read with no data, not null and not an error (KV2-004).
        Console.WriteLine($"version {deleted!.Metadata.Version} deleted at {deleted.Metadata.DeletionTime:O}");

        vault.Server.SetRouteResponse(UndeleteRoute, new MockResponse(204, BodyIsJson: false));
        await client.Kv.V2.UndeleteAsync("app/db", [2]);

        vault.Server.SetRouteResponse(DestroyRoute, new MockResponse(204, BodyIsJson: false));
        await client.Kv.V2.DestroyAsync("app/db", [1]); // irreversible

        vault.Server.SetRouteResponse(DataRoute, Json(404, """{"error":"Version has been permanently destroyed."}"""));
        try
        {
            await client.Kv.V2.ReadSecretAsync("app/db", version: 1);
        }
        catch (BastionVaultException e) when (e.Code == ErrorCodes.KvVersionDestroyed)
        {
            Console.WriteLine("v1 is gone for good");
        }
        // docs:end secrets-kv/soft-delete-and-destroy

        Assert.Equal(KvV2SecretState.SoftDeleted, deleted.State);
        Assert.Null(deleted.Data);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task Step_3_per_environment_secrets()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(DataRoute, Json(200, WriteBody(version: 1)));

        // docs:begin secrets-kv/environments
        await client.Kv.V2.WriteAllEnvironmentsAsync(
            "app/db",
            baseData: Data(("host", "db.internal"), ("port", "5432"), ("pool", "10")),
            envs: new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>(StringComparer.Ordinal)
            {
                ["prod"] = Data(("host", "db.prod.internal"), ("pool", "50")),
                ["staging"] = Data(("host", "db.staging.internal")),
            });

        vault.Server.SetRouteResponse(DataRoute, Json(200, ProdEnvironmentBody()));
        KvV2Secret prod = await client.Kv.V2.GetSecretAsync("app/db", env: "prod");
        Console.WriteLine($"prod host {prod.Data!["host"].GetString()}, resolved from [{string.Join(", ", prod.Metadata.AvailableEnvs)}]");

        vault.Server.SetRouteResponse(DataRoute, Json(200, WriteBody(version: 2)));
        await client.Kv.V2.PatchEnvironmentAsync("app/db", "staging", Data(("pool", "5"))); // prod untouched

        vault.Server.SetRouteResponse(DataRoute, new MockResponse(404, Body: string.Empty, BodyIsJson: false));
        vault.Server.SetRouteResponse(MetadataRoute, Json(200, MetadataBody()));
        try
        {
            await client.Kv.V2.GetSecretAsync("app/db", env: "dev");
        }
        catch (BastionVaultException e) when (e.Code == ErrorCodes.KvEnvironmentNotDeclared)
        {
            Console.WriteLine("dev is not declared on this secret");
        }
        // docs:end secrets-kv/environments

        Assert.Equal("db.prod.internal", prod.Data["host"].GetString());
        Assert.Equal("prod", prod.Metadata.ResolvedEnv);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task Step_4_read_many_secrets_efficiently()
    {
        vault.Server.ClearRouteResponses();
        vault.Server.SetRouteResponse("/v2/sys/batch", Json(200, BatchReadManyBody()));
        using BastionVaultClient client = vault.CreateClient();

        // docs:begin secrets-kv/read-many
        // One POST /v2/sys/batch, not three requests (KV-010, BAT-007). A failing path does not
        // fail the others (BAT-005/BAT-008): never `for path in list: await Read(path)`.
        IReadOnlyDictionary<string, KvReadManyEntry> results =
            await client.Kv.ReadManyAsync("secret", ["app/db", "app/cache", "app/missing"]);

        foreach ((string path, KvReadManyEntry entry) in results)
        {
            if (!entry.IsSuccess)
            {
                Console.Error.WriteLine($"{path}: {entry.Error!.Code}");
            }
            else
            {
                Console.WriteLine($"{path}: {entry.Data!.Count} field(s)");
            }
        }
        // docs:end secrets-kv/read-many

        Assert.True(results["app/db"].IsSuccess);
        Assert.False(results["app/missing"].IsSuccess);
        Assert.Equal(ErrorCodes.AuthzPermissionDenied, results["app/missing"].Error!.Code);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task The_whole_program_writes_reads_soft_deletes_and_undeletes()
    {
        vault.Server.ClearRouteResponses();

        // docs:begin secrets-kv/complete
        using BastionVaultClient client = new();

        try
        {
            vault.Server.SetRouteResponse(DataRoute, Json(200, WriteBody(version: 1)));
            KvV2VersionMetadata written = await client.Kv.V2.WriteSecretAsync("app/db", Data(("username", "admin"), ("password", "p1")));
            Console.WriteLine($"wrote version {written.Version}");

            vault.Server.SetRouteResponse(DataRoute, Json(200, ReadVersionBody(version: 1, username: "admin", password: "p1")));
            KvV2Secret? secret = await client.Kv.V2.ReadSecretAsync("app/db");
            Console.WriteLine($"read back: username {secret!.Data!["username"].GetString()}");

            vault.Server.SetRouteResponse(DataRoute, new MockResponse(204, BodyIsJson: false));
            await client.Kv.V2.SoftDeleteAsync("app/db");

            vault.Server.SetRouteResponse(UndeleteRoute, new MockResponse(204, BodyIsJson: false));
            await client.Kv.V2.UndeleteAsync("app/db", [written.Version]);
            Console.WriteLine("undeleted");
        }
        catch (BastionVaultException e)
        {
            Console.Error.WriteLine($"{e.Code}: {e.Message} ({e.Hint}); retryable: {e.Retryable}");
            throw;
        }
        // docs:end secrets-kv/complete

        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task What_can_go_wrong_maps_every_kv_code_to_a_remedy()
    {
        vault.Server.ClearRouteResponses();
        vault.Server.SetRouteResponse(DataRoute, Json(400, """{"error":"Check-and-set parameter did not match the current version."}"""));
        using BastionVaultClient client = vault.CreateClient();

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(async () =>
        {
            // docs:begin secrets-kv/handling-errors
            try
            {
                await client.Kv.V2.WriteSecretAsync("app/db", Data(("k", "v")), options: new KvWriteOptions { Cas = 1 });
            }
            catch (BastionVaultException e)
            {
                string remedy = e.Code switch
                {
                    ErrorCodes.KvCasMismatch => "re-read the secret and retry with the current version",
                    ErrorCodes.KvCasRequired => "this mount requires cas_required; always send Cas",
                    ErrorCodes.KvVersionDestroyed => "that version is gone for good; pick another",
                    ErrorCodes.KvVersionNotFound => "that version never existed",
                    ErrorCodes.KvEnvironmentNotDeclared => "the secret exists, but not for this env",
                    ErrorCodes.KvEnvironmentRequired => "this token is env-scoped; pass env= on every call",
                    ErrorCodes.KvFieldNotFound => "that field is not present in this version",
                    _ => "look the code up in the error reference",
                };
                Console.Error.WriteLine($"{e.Code}: {e.Message} - {remedy}");
                throw;
            }
            // docs:end secrets-kv/handling-errors
        });

        Assert.Equal(ErrorCodes.KvCasMismatch, failure.Code);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    private static MockResponse Json(int status, string body) => new(status, Body: Compact(body));

    private static string Compact(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    private static IReadOnlyDictionary<string, JsonElement> Data(params (string Key, string Value)[] pairs)
    {
        Dictionary<string, JsonElement> data = new(StringComparer.Ordinal);
        foreach ((string key, string value) in pairs)
        {
            data[key] = JsonDocument.Parse($"\"{value}\"").RootElement.Clone();
        }

        return data;
    }

    private static string WriteBody(int version) => Compact(
        "{\"data\":{\"version\":" + version
        + ",\"created_time\":\"2026-09-13T12:00:00Z\",\"deletion_time\":\"\",\"destroyed\":false}}");

    private static string ReadVersionBody(int version, string username, string password) => Compact(
        "{\"data\":{\"data\":{\"username\":\"" + username + "\",\"password\":\"" + password + "\"},"
        + "\"metadata\":{\"version\":" + version
        + ",\"created_time\":\"2026-09-13T10:00:00Z\",\"deletion_time\":\"\",\"destroyed\":false}}}");

    private static string SoftDeletedBody(int version) => Compact(
        "{\"data\":{\"data\":null,\"metadata\":{\"version\":" + version
        + ",\"created_time\":\"2026-09-13T09:00:00Z\",\"deletion_time\":\"2026-09-13T11:00:00Z\",\"destroyed\":false}}}");

    // A merged read: base + envs["prod"], with resolved_env and available_envs (07 §Environments).
    private static string ProdEnvironmentBody() => Compact("""
        {
          "data": {
            "data": {"host":"db.prod.internal","port":"5432","pool":"50"},
            "metadata": {"version":1,"created_time":"2026-09-13T10:00:00Z","deletion_time":"","destroyed":false,"resolved_env":"prod","available_envs":["prod","staging"]}
          }
        }
        """);

    private static string MetadataBody() => Compact("""
        {"data":{"current_version":2,"created_time":"2026-09-13T09:00:00Z","updated_time":"2026-09-13T12:00:00Z","versions":{"1":{"version":1,"created_time":"2026-09-13T09:00:00Z","destroyed":false},"2":{"version":2,"created_time":"2026-09-13T12:00:00Z","destroyed":false}}}}
        """);

    // /v2/sys/batch's own response shape (14 §Batch endpoint): one path denied, the others fine.
    private static string BatchReadManyBody() => Compact("""
        {
          "results": [
            {"status":200,"path":"secret/data/app/db","data":{"data":{"username":"admin","password":"p2"}}},
            {"status":200,"path":"secret/data/app/cache","data":{"data":{"ttl":"300"}}},
            {"status":403,"path":"secret/data/app/missing","errors":["permission denied"]}
          ]
        }
        """);
}
