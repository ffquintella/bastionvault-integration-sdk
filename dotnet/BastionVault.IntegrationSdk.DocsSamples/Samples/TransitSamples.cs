using System.Text.Json;
using BastionVault.IntegrationSdk.DocsSamples.Infrastructure;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.DocsSamples.Samples;

/// <summary>
/// Every executed sample shown by <c>docs/dotnet/engines/transit.md</c> (D6), built from
/// <c>specifications/17-usage-guides.md</c> guide 8 and <c>08-transit-engine.md</c>. Reuses
/// <see cref="MockVaultFixture"/>'s server: routes are bound per test on
/// <see cref="MockVaultFixture.Server"/> and cleared after, so no test leaks a route to another.
/// </summary>
public sealed class TransitSamples : IClassFixture<MockVaultFixture>
{
    private const string KeyRoute = "/v1/transit/keys/orders";
    private const string RotateRoute = "/v1/transit/keys/orders/rotate";
    private const string ConfigRoute = "/v1/transit/keys/orders/config";
    private const string EncryptRoute = "/v1/transit/encrypt/orders";
    private const string DecryptRoute = "/v1/transit/decrypt/orders";
    private const string RewrapRoute = "/v1/transit/rewrap/orders";
    private const string SigningKeyRoute = "/v1/transit/keys/release-signing";
    private const string SignRoute = "/v1/transit/sign/release-signing";
    private const string VerifyRoute = "/v1/transit/verify/release-signing";

    private readonly MockVaultFixture vault;

    public TransitSamples(MockVaultFixture vault)
    {
        this.vault = vault;
    }

    [Fact]
    public void The_policy_this_guide_needs_is_the_policy_PolicyBuilder_builds()
    {
        // docs:begin transit/policy
        string hcl = new PolicyBuilder()
            .AddPath("transit/keys/orders", [Capability.Create, Capability.Read, Capability.Update])
            .AddPath("transit/keys/orders/rotate", [Capability.Update])
            .AddPath("transit/encrypt/orders", [Capability.Update])
            .AddPath("transit/decrypt/orders", [Capability.Update])
            .AddPath("transit/rewrap/orders", [Capability.Update])
            .AddPath("transit/sign/release-signing", [Capability.Update])
            .AddPath("transit/verify/release-signing", [Capability.Update])
            .Build();

        Console.WriteLine(hcl);
        // docs:end transit/policy

        Assert.Equal(PolicyShownInTheGuide(), hcl.Trim());
    }

    private static string PolicyShownInTheGuide()
    {
        string path = Path.Combine(DocsRepository.Docs.FullName, "dotnet", "engines", "transit.md");
        string[] lines = File.ReadAllLines(path);
        int opening = Array.FindIndex(lines, line => line.TrimEnd() == "```hcl");
        Assert.True(opening >= 0, $"{path} shows no ```hcl policy block (DOC-011).");
        int closing = Array.FindIndex(lines, opening + 1, line => line.TrimEnd() == "```");
        Assert.True(closing > opening, $"{path}: the ```hcl block is never closed.");
        return string.Join('\n', lines[(opening + 1)..closing]).Trim();
    }

    [Fact]
    public async Task Step_1_create_a_key_then_encrypt_and_decrypt()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(KeyRoute, Json(200, KeyBody(latestVersion: 1)));

        // docs:begin transit/encrypt-decrypt
        TransitKey key = await client.Transit.CreateKeyAsync("orders");
        // Key.Type defaults server-side to chacha20-poly1305 when KeyOptions is omitted (08 §Key types).

        byte[] plaintext = "order #42: 3x widget"u8.ToArray();
        vault.Server.SetRouteResponse(EncryptRoute, Json(200, EncryptBody(version: 1)));
        TransitEncryptResult encrypted = await client.Transit.EncryptAsync("orders", plaintext);
        Console.WriteLine($"ciphertext: {encrypted.Ciphertext} (key version {encrypted.KeyVersion})");

        // TRS-002: a malformed value never reaches the server - validated client-side first.
        TransitParsedCiphertext parsed = TransitOperations.ParseCiphertext(encrypted.Ciphertext);
        Console.WriteLine($"framing version {parsed.Version}");

        vault.Server.SetRouteResponse(DecryptRoute, Json(200, DecryptBody(plaintext)));
        SecretBytes decrypted = await client.Transit.DecryptAsync("orders", encrypted.Ciphertext);
        Console.WriteLine($"round-tripped: {System.Text.Encoding.UTF8.GetString(decrypted.Reveal()!) == "order #42: 3x widget"}");
        // docs:end transit/encrypt-decrypt

        Assert.Equal(TransitKeyTypes.ChaCha20Poly1305, key.Type);
        Assert.Equal(1, parsed.Version);
        Assert.Equal("order #42: 3x widget", System.Text.Encoding.UTF8.GetString(decrypted.Reveal()!));
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task Step_2_rotate_the_key_and_rewrap_existing_ciphertext()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        // docs:begin transit/rotate-and-rewrap
        vault.Server.SetRouteResponse(RotateRoute, Json(200, KeyBody(latestVersion: 2)));
        TransitKey rotated = await client.Transit.RotateKeyAsync("orders");
        Console.WriteLine($"latest version is now {rotated.LatestVersion}");

        // Rewrap re-encrypts under the new version server-side; the plaintext never leaves the
        // server and this SDK never sees it, unlike a decrypt-then-encrypt round trip you might
        // write instead.
        vault.Server.SetRouteResponse(RewrapRoute, Json(200, EncryptBody(version: 2)));
        TransitEncryptResult rewrapped = await client.Transit.RewrapAsync("orders", "bvault:v1:c2VhbGVkLWJ5dGVz");
        Console.WriteLine($"now under key version {rewrapped.KeyVersion}");

        // A version below MinDecryptionVersion answers BV-TRANSIT-004; ConfigureKey moves that floor.
        vault.Server.SetRouteResponse(ConfigRoute, Json(200, KeyBody(latestVersion: 2, minDecryptionVersion: 2)));
        TransitKey trimmed = await client.Transit.ConfigureKeyAsync("orders", new TransitKeyConfig { MinDecryptionVersion = 2 });
        Console.WriteLine($"versions below {trimmed.MinDecryptionVersion} can no longer decrypt");
        // docs:end transit/rotate-and-rewrap

        Assert.Equal(2, rotated.LatestVersion);
        Assert.Equal(2, rewrapped.KeyVersion);
        Assert.Equal(2, trimmed.MinDecryptionVersion);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task Step_3_sign_and_verify_with_an_asymmetric_key()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(SigningKeyRoute, Json(200, KeyBody(latestVersion: 1, keyType: TransitKeyTypes.Ed25519)));

        // docs:begin transit/sign-and-verify
        await client.Transit.CreateKeyAsync("release-signing", new TransitKeyOptions { KeyType = TransitKeyTypes.Ed25519 });

        byte[] digest = System.Security.Cryptography.SHA256.HashData("release-1.4.0.tar.gz"u8.ToArray());
        vault.Server.SetRouteResponse(SignRoute, Json(200, SignBody()));
        TransitSignResult signature = await client.Transit.SignAsync("release-signing", digest);

        vault.Server.SetRouteResponse(VerifyRoute, Json(200, """{"valid":true}"""));
        bool valid = await client.Transit.VerifyAsync("release-signing", signature.Signature, digest);
        Console.WriteLine($"signature valid: {valid}");
        // docs:end transit/sign-and-verify

        Assert.True(valid);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task The_whole_program_creates_encrypts_decrypts_rotates_and_rewraps()
    {
        vault.Server.ClearRouteResponses();

        // docs:begin transit/complete
        using BastionVaultClient client = new();

        try
        {
            vault.Server.SetRouteResponse(KeyRoute, Json(200, KeyBody(latestVersion: 1)));
            await client.Transit.CreateKeyAsync("orders");

            byte[] plaintext = "order #42"u8.ToArray();
            vault.Server.SetRouteResponse(EncryptRoute, Json(200, EncryptBody(version: 1)));
            TransitEncryptResult encrypted = await client.Transit.EncryptAsync("orders", plaintext);
            Console.WriteLine($"encrypted under key version {encrypted.KeyVersion}");

            vault.Server.SetRouteResponse(DecryptRoute, Json(200, DecryptBody(plaintext)));
            SecretBytes decrypted = await client.Transit.DecryptAsync("orders", encrypted.Ciphertext);
            Console.WriteLine($"decrypted length: {decrypted.Reveal()!.Length}");

            vault.Server.SetRouteResponse(RotateRoute, Json(200, KeyBody(latestVersion: 2)));
            await client.Transit.RotateKeyAsync("orders");

            vault.Server.SetRouteResponse(RewrapRoute, Json(200, EncryptBody(version: 2)));
            TransitEncryptResult rewrapped = await client.Transit.RewrapAsync("orders", encrypted.Ciphertext);
            Console.WriteLine($"rewrapped to key version {rewrapped.KeyVersion}");
        }
        catch (BastionVaultException e)
        {
            Console.Error.WriteLine($"{e.Code}: {e.Message} ({e.Hint}); retryable: {e.Retryable}");
            throw;
        }
        // docs:end transit/complete

        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task What_can_go_wrong_maps_every_transit_code_to_a_remedy()
    {
        vault.Server.ClearRouteResponses();
        vault.Server.SetRouteResponse(DecryptRoute, Json(500, """{"error":"not a bvault ciphertext: malformed"}"""));
        using BastionVaultClient client = vault.CreateClient();

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(async () =>
        {
            // docs:begin transit/handling-errors
            try
            {
                await client.Transit.DecryptAsync("orders", "bvault:v1:not-really-base64");
            }
            catch (BastionVaultException e)
            {
                string remedy = e.Code switch
                {
                    ErrorCodes.TransitKeyNotFound => "check the name, or create the key first",
                    ErrorCodes.TransitDeletionNotAllowed => "flip deletion_allowed via ConfigureKey first",
                    ErrorCodes.TransitVersionNotDecryptable => "use a version still above min_decryption_version",
                    ErrorCodes.TransitOperationNotSupportedByKeyType => "check 08 for what this key type supports",
                    ErrorCodes.TransitAlgorithmMismatch => "match the algorithm the key was created with",
                    ErrorCodes.InputInvalidCiphertextFormat => "pass the exact ciphertext Encrypt/Sign returned",
                    ErrorCodes.InputNotBase64 => "pass raw bytes and let the SDK encode them",
                    _ => "look the code up in the error reference",
                };
                Console.Error.WriteLine($"{e.Code}: {e.Message} - {remedy}");
                throw;
            }
            // docs:end transit/handling-errors
        });

        Assert.Equal(ErrorCodes.InputInvalidCiphertextFormat, failure.Code);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    private static MockResponse Json(int status, string body) => new(status, Body: Compact(body));

    private static string Compact(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    private static string KeyBody(int latestVersion, int minDecryptionVersion = 1, string keyType = TransitKeyTypes.ChaCha20Poly1305) => Compact(
        "{\"data\":{\"name\":\"orders\",\"type\":\"" + keyType + "\",\"latest_version\":" + latestVersion
        + ",\"min_decryption_version\":" + minDecryptionVersion
        + ",\"min_available_version\":1,\"deletion_allowed\":false,\"exportable\":false,\"derived\":false,"
        + "\"convergent_encryption\":false,\"keys\":{\"1\":\"2026-09-13T10:00:00Z\"}}}");

    private static string EncryptBody(int version) => Compact(
        "{\"data\":{\"ciphertext\":\"bvault:v" + version + ":c2VhbGVkLWJ5dGVz\",\"key_version\":" + version + "}}");

    private static string DecryptBody(byte[] plaintext) => Compact(
        "{\"data\":{\"plaintext\":\"" + Convert.ToBase64String(plaintext) + "\"}}");

    private static string SignBody() => Compact(
        """{"data":{"signature":"bvault:v1:c2lnbmF0dXJlLWJ5dGVz","key_version":1}}""");
}
