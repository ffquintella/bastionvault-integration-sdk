using System.Text.Json;
using BastionVault.IntegrationSdk.DocsSamples.Infrastructure;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.DocsSamples.Samples;

/// <summary>
/// Every executed sample shown by <c>docs/dotnet/engines/totp.md</c> (D6). Section 17 carries no
/// dedicated usage guide for TOTP, so this page — and these samples — are built directly from
/// <c>11-totp-engine.md</c>. Reuses <see cref="MockVaultFixture"/>'s server: routes are bound per
/// test on <see cref="MockVaultFixture.Server"/> and cleared after, so no test leaks a route to
/// another.
/// </summary>
public sealed class TotpSamples : IClassFixture<MockVaultFixture>
{
    private const string AliceKeyRoute = "/v1/totp/keys/alice";
    private const string AliceCodeRoute = "/v1/totp/code/alice";
    private const string VendorKeyRoute = "/v1/totp/keys/vendor-app";
    private const string VendorCodeRoute = "/v1/totp/code/vendor-app";

    private readonly MockVaultFixture vault;

    public TotpSamples(MockVaultFixture vault)
    {
        this.vault = vault;
    }

    [Fact]
    public void The_policy_this_guide_needs_is_the_policy_PolicyBuilder_builds()
    {
        // docs:begin totp/policy
        string hcl = new PolicyBuilder()
            .AddPath("totp/keys/alice", [Capability.Create, Capability.Read, Capability.Delete])
            .AddPath("totp/code/alice", [Capability.Read, Capability.Update])
            .AddPath("totp/keys/vendor-app", [Capability.Create, Capability.Read])
            .AddPath("totp/code/vendor-app", [Capability.Update])
            .Build();

        Console.WriteLine(hcl);
        // docs:end totp/policy

        Assert.Equal(PolicyShownInTheGuide(), hcl.Trim());
    }

    private static string PolicyShownInTheGuide()
    {
        string path = Path.Combine(DocsRepository.Docs.FullName, "dotnet", "engines", "totp.md");
        string[] lines = File.ReadAllLines(path);
        int opening = Array.FindIndex(lines, line => line.TrimEnd() == "```hcl");
        Assert.True(opening >= 0, $"{path} shows no ```hcl policy block (DOC-011).");
        int closing = Array.FindIndex(lines, opening + 1, line => line.TrimEnd() == "```");
        Assert.True(closing > opening, $"{path}: the ```hcl block is never closed.");
        return string.Join('\n', lines[(opening + 1)..closing]).Trim();
    }

    [Fact]
    public async Task Step_1_create_a_generate_mode_key_and_read_a_code_back()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(AliceKeyRoute, Json(200, GenerateModeCreatedBody()));
        vault.Server.SetRouteResponse(AliceCodeRoute, Json(200, """{"data":{"code":"045678"}}"""));

        // docs:begin totp/generate-mode
        // TOT-001: exactly one of Generate/Key/Url; AccountName is required unless Url carries a label.
        TotpKeyCreated created = await client.Totp.CreateKeyAsync("alice", new TotpKeySpec
        {
            Generate = true,
            AccountName = "alice",
            Issuer = "Corp Vault",
        });
        Console.WriteLine($"seed exported: {created.Key!.HasValue}");
        // created.Key/Url/Barcode are present because CreateKeyAsync's Exported defaulted to true.

        string code = await client.Totp.GenerateCodeAsync("alice");
        Console.WriteLine($"current code: {code}");
        // TOT-004: code is a string, never parsed as a number - a leading zero would otherwise be lost.
        // docs:end totp/generate-mode

        Assert.True(created.Key!.HasValue);
        Assert.Equal("045678", code);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task Step_2_create_a_provider_mode_key_and_validate_a_code()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(VendorKeyRoute, Json(200, """{"data":{"name":"vendor-app","generate":false}}"""));
        vault.Server.SetRouteResponse(VendorCodeRoute, Json(200, """{"data":{"valid":true}}"""));

        // docs:begin totp/provider-mode
        TotpKeyCreated vendor = await client.Totp.CreateKeyAsync("vendor-app", new TotpKeySpec
        {
            Key = new SecretString("JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP"),
            AccountName = "vendor-app",
            Digits = 6,
        });
        Console.WriteLine($"provider-mode key created: generate={vendor.Generate}");

        bool valid = await client.Totp.ValidateCodeAsync("vendor-app", "123456");
        // TOT-003: false covers both a wrong code and a replayed one when replay_check is on - the
        // two are indistinguishable at this API, by server design, not an SDK gap.
        Console.WriteLine($"code accepted: {valid}");
        // docs:end totp/provider-mode

        Assert.False(vendor.Generate);
        Assert.True(valid);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task The_whole_program_creates_a_key_and_reads_a_code()
    {
        vault.Server.ClearRouteResponses();

        // docs:begin totp/complete
        using BastionVaultClient client = new();

        try
        {
            vault.Server.SetRouteResponse(AliceKeyRoute, Json(200, GenerateModeCreatedBody()));
            TotpKeyCreated created = await client.Totp.CreateKeyAsync("alice", new TotpKeySpec { Generate = true, AccountName = "alice" });
            Console.WriteLine($"created key, generate-mode={created.Generate}");

            vault.Server.SetRouteResponse(AliceCodeRoute, Json(200, """{"data":{"code":"045678"}}"""));
            string code = await client.Totp.GenerateCodeAsync("alice");
            Console.WriteLine($"code: {code}");

            vault.Server.SetRouteResponse(AliceKeyRoute, Json(200, ReadKeyBody()));
            TotpKey? readBack = await client.Totp.ReadKeyAsync("alice");
            Console.WriteLine($"digits configured: {readBack!.Digits}");
        }
        catch (BastionVaultException e)
        {
            Console.Error.WriteLine($"{e.Code}: {e.Message} ({e.Hint}); retryable: {e.Retryable}");
            throw;
        }
        // docs:end totp/complete

        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task What_can_go_wrong_maps_every_totp_code_to_a_remedy()
    {
        vault.Server.ClearRouteResponses();
        // Reused verbatim from specifications/fixtures/totp/totp.wrong-mode.json (TOT-003).
        vault.Server.SetRouteResponse(
            VendorCodeRoute, Json(500, """{"error":"this key is provider-mode; POST a `code` to validate, not GET"}"""));
        using BastionVaultClient client = vault.CreateClient();

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(async () =>
        {
            // docs:begin totp/handling-errors
            try
            {
                await client.Totp.GenerateCodeAsync("vendor-app");
            }
            catch (BastionVaultException e)
            {
                string remedy = e.Code switch
                {
                    ErrorCodes.TotpKeyNotFound => "check the name, or create the key first",
                    ErrorCodes.TotpWrongModeForOperation => "use GenerateCode for generate-mode, ValidateCode for provider-mode",
                    ErrorCodes.InputInvalidArgument => "check TOT-001's exclusivity and required-field rules",
                    _ => "look the code up in the error reference",
                };
                Console.Error.WriteLine($"{e.Code}: {e.Message} - {remedy}");
                throw;
            }
            // docs:end totp/handling-errors
        });

        Assert.Equal(ErrorCodes.TotpWrongModeForOperation, failure.Code);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    private static MockResponse Json(int status, string body) => new(status, Body: Compact(body));

    private static string Compact(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    // Reused verbatim from specifications/fixtures/totp/totp.generate-mode-create.json (TOT-001/TOT-002).
    private static string GenerateModeCreatedBody() => Compact("""
        {
          "data": {
            "name": "alice",
            "generate": true,
            "key": "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP",
            "url": "otpauth://totp/issuer:alice?secret=JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP&issuer=issuer",
            "barcode": "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="
          }
        }
        """);

    private static string ReadKeyBody() => Compact("""
        {"data":{"generate":true,"account_name":"alice","algorithm":"SHA1","digits":6,"period":30,"skew":1,"replay_check":true}}
        """);
}
