using System.Text.Json;
using BastionVault.IntegrationSdk.DocsSamples.Infrastructure;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.DocsSamples.Samples;

/// <summary>
/// Every executed sample shown by <c>docs/dotnet/engines/files.md</c> (D6). Section 17 carries no
/// dedicated usage guide for Files, so this page — and these samples — are built directly from
/// <c>12-other-engines-and-identity.md</c>'s Files table (FIL-001). Reuses
/// <see cref="MockVaultFixture"/>'s server: routes are bound per test on
/// <see cref="MockVaultFixture.Server"/> and cleared after, so no test leaks a route to another.
/// </summary>
public sealed class FilesSamples : IClassFixture<MockVaultFixture>
{
    private const string FilesRoute = "/v1/files/files/";
    private const string FileRoute = "/v1/files/files/f-1";
    private const string ContentRoute = "/v1/files/files/f-1/content";
    private const string VersionsRoute = "/v1/files/files/f-1/versions";
    private const string SyncRoute = "/v1/files/files/f-1/sync/nightly";
    private const string PushRoute = "/v1/files/files/f-1/sync/nightly/push";
    private const string RepointRoute = "/v1/files/files/repoint-resource";

    private readonly MockVaultFixture vault;

    public FilesSamples(MockVaultFixture vault)
    {
        this.vault = vault;
    }

    [Fact]
    public void The_policy_this_guide_needs_is_the_policy_PolicyBuilder_builds()
    {
        // docs:begin files/policy
        string hcl = new PolicyBuilder()
            .AddPath("files/files/", [Capability.Create, Capability.List])
            .AddPath("files/files/f-1", [Capability.Read, Capability.Update, Capability.Delete])
            .AddPath("files/files/f-1/content", [Capability.Read])
            .AddPath("files/files/f-1/versions", [Capability.Read])
            .AddPath("files/files/f-1/sync/nightly", [Capability.Create, Capability.Update])
            .AddPath("files/files/repoint-resource", [Capability.Update])
            .Build();

        Console.WriteLine(hcl);
        // docs:end files/policy

        Assert.Equal(PolicyShownInTheGuide(), hcl.Trim());
    }

    private static string PolicyShownInTheGuide()
    {
        string path = Path.Combine(DocsRepository.Docs.FullName, "dotnet", "engines", "files.md");
        string[] lines = File.ReadAllLines(path);
        int opening = Array.FindIndex(lines, line => line.TrimEnd() == "```hcl");
        Assert.True(opening >= 0, $"{path} shows no ```hcl policy block (DOC-011).");
        int closing = Array.FindIndex(lines, opening + 1, line => line.TrimEnd() == "```");
        Assert.True(closing > opening, $"{path}: the ```hcl block is never closed.");
        return string.Join('\n', lines[(opening + 1)..closing]).Trim();
    }

    [Fact]
    public async Task Step_1_create_a_file_and_list_the_mount()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(FilesRoute, Json(200, CreatedBody()));

        // docs:begin files/create-and-list
        // FIL-001: Content is raw bytes; the SDK base64-encodes it into content_base64 itself.
        byte[] payload = "id,name\n1,widget\n"u8.ToArray();
        string id = await client.Files.CreateAsync(new FileCreateRequest
        {
            Name = "catalog.csv",
            MimeType = "text/csv",
            Tags = ["catalog", "nightly"],
            Content = payload,
        });
        Console.WriteLine($"created file {id}");
        // docs:end files/create-and-list

        Assert.Equal("f-1", id);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task Step_2_read_content_back_update_metadata_and_list_versions()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(ContentRoute, Json(200, ContentBody()));
        vault.Server.SetRouteResponse(FileRoute, Json(200, "{}"));
        vault.Server.SetRouteResponse(VersionsRoute, Json(200, VersionsBody()));

        // docs:begin files/content-and-versions
        byte[] roundTripped = await client.Files.ContentAsync("f-1");
        Console.WriteLine($"content is {roundTripped.Length} bytes");

        await client.Files.UpdateAsync("f-1", new FileUpdateRequest { Notes = "regenerated nightly" });

        // No shape beyond the array itself is documented (D-M1c-25), so each entry is a raw element.
        IReadOnlyList<JsonElement> versions = await client.Files.VersionsAsync("f-1");
        Console.WriteLine($"{versions.Count} version(s) on record");
        // docs:end files/content-and-versions

        Assert.Equal("id,name\n1,widget\n"u8.ToArray(), roundTripped);
        Assert.Equal(2, versions.Count);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task Step_3_point_the_file_at_a_sync_target_and_push_it()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(SyncRoute, Json(200, "{}"));
        vault.Server.SetRouteResponse(PushRoute, Json(200, "{}"));
        vault.Server.SetRouteResponse(RepointRoute, Json(200, "{}"));

        // docs:begin files/sync-and-repoint
        // R-36: section 12 names `kind` but not the credential fields a `local-fs`/`smb` target
        // needs, so everything past `kind` travels as an opaque bag through `Fields`, never a
        // typed property this SDK would otherwise have to guess the wire name of.
        using JsonDocument fields = JsonDocument.Parse("""{"share":"\\\\fileserver\\exports","username":"svc-sync"}""");
        await client.Files.Sync.WriteAsync("f-1", "nightly", new SyncTarget
        {
            Kind = "smb",
            Fields = fields.RootElement.Clone(),
        });

        await client.Files.Sync.PushAsync("f-1", "nightly");
        Console.WriteLine("pushed to the nightly sync target");

        await client.Files.RepointResourceAsync(oldResource: "app/legacy-db", newResource: "app/db");
        // docs:end files/sync-and-repoint

        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task The_whole_program_creates_a_file_and_reads_it_back()
    {
        vault.Server.ClearRouteResponses();

        // docs:begin files/complete
        using BastionVaultClient client = new();

        try
        {
            vault.Server.SetRouteResponse(FilesRoute, Json(200, CreatedBody()));
            string id = await client.Files.CreateAsync(new FileCreateRequest
            {
                Name = "catalog.csv",
                Content = "id,name\n1,widget\n"u8.ToArray(),
            });
            Console.WriteLine($"created file {id}");

            vault.Server.SetRouteResponse(ContentRoute, Json(200, ContentBody()));
            byte[] content = await client.Files.ContentAsync(id);
            Console.WriteLine($"read back {content.Length} bytes");
        }
        catch (BastionVaultException e)
        {
            Console.Error.WriteLine($"{e.Code}: {e.Message} ({e.Hint}); retryable: {e.Retryable}");
            throw;
        }
        // docs:end files/complete

        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task What_can_go_wrong_maps_every_files_code_to_a_remedy()
    {
        vault.Server.ClearRouteResponses();
        vault.Server.SetRouteResponse("/v1/files/files/missing/content", new MockResponse(404, Body: string.Empty, BodyIsJson: false));
        using BastionVaultClient client = vault.CreateClient();

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(async () =>
        {
            // docs:begin files/handling-errors
            try
            {
                await client.Files.ContentAsync("missing");
            }
            catch (BastionVaultException e)
            {
                string remedy = e.Code switch
                {
                    ErrorCodes.NotFoundPathNotFound => "check the id, or Files.List() for what exists",
                    ErrorCodes.InputBodyTooLarge => "the encoded content exceeds the 32 MiB body limit (FIL-001); shrink or chunk it",
                    ErrorCodes.AuthzPermissionDenied => "extend the calling token's policy to cover this path",
                    _ => "look the code up in the error reference",
                };
                Console.Error.WriteLine($"{e.Code}: {e.Message} - {remedy}");
                throw;
            }
            // docs:end files/handling-errors
        });

        Assert.Equal(ErrorCodes.NotFoundPathNotFound, failure.Code);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    private static MockResponse Json(int status, string body) => new(status, Body: Compact(body));

    private static string Compact(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    private static string CreatedBody() => Compact("""{"data":{"id":"f-1"}}""");

    private static string ContentBody() => Compact("""
        {"data":{"content_base64":"aWQsbmFtZQoxLHdpZGdldAo="}}
        """);

    private static string VersionsBody() => Compact("""
        {"data":[{"version":1,"created_time":"2026-09-01T00:00:00Z"},{"version":2,"created_time":"2026-09-20T00:00:00Z"}]}
        """);
}
