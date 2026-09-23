using System.Globalization;
using System.Security.Cryptography;
using BastionVault.IntegrationSdk.IntegrationTests.Harness;

namespace BastionVault.IntegrationSdk.IntegrationTests.Scenarios;

/// <summary>ITG-S18: Transit key lifecycle (Standard, 15-testing-requirements.md:244-247).</summary>
public sealed class Scenario18_TransitLifecycle : IntegrationTest
{
    [IntegrationFact]
    public async Task Create_encrypt_rotate_rewrap_min_decryption_and_delete_all_map_correctly()
    {
        string mount = await Resources.MountAsync("transit", "lifecycle");
        string keyName = Unique("key");

        TransitKey created = await Client.Transit.CreateKeyAsync(
            keyName, new TransitKeyOptions { KeyType = TransitKeyTypes.ChaCha20Poly1305 }, mount);
        Assert.Equal(TransitKeyTypes.ChaCha20Poly1305, created.Type);

        byte[] plaintext = "scenario18 plaintext"u8.ToArray();
        TransitEncryptResult encrypted = await Client.Transit.EncryptAsync(keyName, plaintext, mount: mount);
        Assert.Equal(1, encrypted.KeyVersion);

        SecretBytes decrypted = await Client.Transit.DecryptAsync(keyName, encrypted.Ciphertext, mount: mount);
        Assert.Equal(plaintext, decrypted.Reveal());

        TransitKey rotated = await Client.Transit.RotateKeyAsync(keyName, mount);
        Assert.Equal(2, rotated.LatestVersion);

        TransitEncryptResult rewrapped = await Client.Transit.RewrapAsync(keyName, encrypted.Ciphertext, mount: mount);
        Assert.Equal(2, rewrapped.KeyVersion);

        SecretBytes decryptedOld = await Client.Transit.DecryptAsync(keyName, encrypted.Ciphertext, mount: mount);
        Assert.Equal(plaintext, decryptedOld.Reveal());

        _ = await Client.Transit.ConfigureKeyAsync(keyName, new TransitKeyConfig { MinDecryptionVersion = 2 }, mount);
        BastionVaultException belowMin = await Assert.ThrowsAsync<BastionVaultException>(
            () => Client.Transit.DecryptAsync(keyName, encrypted.Ciphertext, mount: mount));
        Assert.Equal(ErrorCodes.TransitVersionNotDecryptable, belowMin.Code);

        BastionVaultException deletionDenied = await Assert.ThrowsAsync<BastionVaultException>(
            () => Client.Transit.DeleteKeyAsync(keyName, mount));
        Assert.Equal(ErrorCodes.TransitDeletionNotAllowed, deletionDenied.Code);

        _ = await Client.Transit.ConfigureKeyAsync(keyName, new TransitKeyConfig { DeletionAllowed = true }, mount);
        await Client.Transit.DeleteKeyAsync(keyName, mount);
        Resources.Track("transit-key-already-deleted", keyName, _ => Task.CompletedTask);
    }
}

/// <summary>ITG-S19: Transit sign/verify/hmac/datakey/random/hash (Standard, 15-testing-requirements.md:248-250).</summary>
public sealed class Scenario19_TransitCryptoOperations : IntegrationTest
{
    [IntegrationFact]
    public async Task Sign_verify_hmac_datakey_random_and_hash_all_map_correctly()
    {
        string mount = await Resources.MountAsync("transit", "crypto");
        byte[] message = "scenario19 message"u8.ToArray();

        string edKey = Unique("ed25519");
        _ = await Client.Transit.CreateKeyAsync(edKey, new TransitKeyOptions { KeyType = TransitKeyTypes.Ed25519 }, mount);
        TransitSignResult edSignature = await Client.Transit.SignAsync(edKey, message, mount: mount);
        Assert.True(await Client.Transit.VerifyAsync(edKey, edSignature.Signature, message, mount: mount));
        Assert.False(await Client.Transit.VerifyAsync(edKey, edSignature.Signature, "tampered input"u8.ToArray(), mount: mount));

        string mldsaKey = Unique("mldsa65");
        _ = await Client.Transit.CreateKeyAsync(mldsaKey, new TransitKeyOptions { KeyType = TransitKeyTypes.MlDsa65 }, mount);
        TransitSignResult mldsaSignature = await Client.Transit.SignAsync(mldsaKey, message, mount: mount);
        Assert.True(await Client.Transit.VerifyAsync(mldsaKey, mldsaSignature.Signature, message, mount: mount));

        string hmacKey = Unique("hmac");
        _ = await Client.Transit.CreateKeyAsync(hmacKey, new TransitKeyOptions { KeyType = TransitKeyTypes.Hmac }, mount);
        TransitHmacResult hmac = await Client.Transit.HmacAsync(hmacKey, message, mount: mount);
        Assert.True(await Client.Transit.VerifyHmacAsync(hmacKey, hmac.Hmac, message, mount: mount));

        string kemKey = Unique("mlkem768");
        _ = await Client.Transit.CreateKeyAsync(kemKey, new TransitKeyOptions { KeyType = TransitKeyTypes.MlKem768 }, mount);
        TransitDataKeyResult dataKey = await Client.Transit.GenerateDataKeyAsync(kemKey, TransitDataKeyMode.Plaintext, mount: mount);
        Assert.NotNull(dataKey.Plaintext);
        SecretBytes unwrapped = await Client.Transit.UnwrapDataKeyAsync(kemKey, dataKey.Ciphertext, mount);
        Assert.Equal(dataKey.Plaintext!.Reveal(), unwrapped.Reveal());

        byte[] random = await Client.Transit.RandomAsync(32, mount: mount);
        Assert.Equal(32, random.Length);

        byte[] hash = await Client.Transit.HashAsync(message, algorithm: TransitHashAlgorithms.Sha2512, mount: mount);
        Assert.Equal(64, hash.Length);
    }
}

/// <summary>ITG-S20: TOTP (Standard, 15-testing-requirements.md:252-256).</summary>
public sealed class Scenario20_Totp : IntegrationTest
{
    [IntegrationFact]
    public async Task Generate_mode_provider_mode_validate_replay_and_wrong_mode_all_map_correctly()
    {
        string mount = await Resources.MountAsync("totp", "keys");

        string generateName = Unique("generate");
        TotpKeyCreated generated = await Client.Totp.CreateKeyAsync(
            generateName,
            new TotpKeySpec { Generate = true, Issuer = "bastionvault-sdk", AccountName = "scenario20" },
            mount);
        Assert.True(generated.Generate);
        Assert.NotNull(generated.Key);
        Assert.NotNull(generated.Url);
        _ = Assert.NotNull(generated.Barcode);

        string code = await Client.Totp.GenerateCodeAsync(generateName, mount);
        Assert.Equal(6, code.Length);
        Assert.All(code, c => Assert.True(char.IsAsciiDigit(c)));

        // F2 relevance (DR-0021): `period` is a duration-shaped field (a count of seconds, exactly
        // the shape F2's `ttl` was) sent by TotpWire.Serialise as a raw JSON *number*, never a Go
        // duration string. If the server rejected numeric durations uniformly, this create would
        // fail the same way `Auth.Token.Create` did. It does not: measured below.
        string providerName = Unique("provider");
        const string secretBase32 = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";
        string totpUrl = $"otpauth://totp/{providerName}?secret={secretBase32}&issuer=bastionvault-sdk&algorithm=SHA1&digits=6&period=30";
        TotpKeyCreated provider = await Client.Totp.CreateKeyAsync(
            providerName,
            new TotpKeySpec
            {
                Url = new SecretString(totpUrl),
                Algorithm = TotpAlgorithm.Sha1,
                Digits = 6,
                Period = 30,
            },
            mount);
        Assert.False(provider.Generate);

        string validCode = ComputeTotp(secretBase32, DateTimeOffset.UtcNow);
        Assert.True(await Client.Totp.ValidateCodeAsync(providerName, validCode, mount));
        Assert.False(await Client.Totp.ValidateCodeAsync(providerName, validCode, mount));

        BastionVaultException wrongMode = await Assert.ThrowsAsync<BastionVaultException>(
            () => Client.Totp.GenerateCodeAsync(providerName, mount));
        Assert.Equal(ErrorCodes.TotpWrongModeForOperation, wrongMode.Code);
    }

    /// <summary>
    /// A local RFC 6238 implementation (SHA-1, 6 digits, 30s step), independent of the SDK.
    /// HMAC-SHA1 is RFC 6238's own default algorithm for a TOTP key — not a security control this
    /// scenario is choosing, but the wire format it is verifying interoperability against.
    /// </summary>
    private static string ComputeTotp(string base32Secret, DateTimeOffset instant, int digits = 6, int periodSeconds = 30)
    {
        byte[] key = Base32Decode(base32Secret);
        long counter = instant.ToUnixTimeSeconds() / periodSeconds;
        byte[] counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counterBytes);
        }

#pragma warning disable CA5350 // RFC 6238 mandates HMAC-SHA1 as the TOTP default; this verifies interop with it, not a security boundary.
        using HMACSHA1 hmac = new(key);
#pragma warning restore CA5350
        byte[] hash = hmac.ComputeHash(counterBytes);
        int offset = hash[^1] & 0x0F;
        int binary = ((hash[offset] & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            | (hash[offset + 3] & 0xFF);
        int otp = binary % (int)Math.Pow(10, digits);
        return otp.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        string trimmed = input.TrimEnd('=').ToUpperInvariant();
        List<byte> bytes = [];
        int buffer = 0;
        int bitsLeft = 0;
        foreach (char c in trimmed)
        {
            int value = alphabet.IndexOf(c);
            if (value < 0)
            {
                continue;
            }

            buffer = (buffer << 5) | value;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bytes.Add((byte)((buffer >> (bitsLeft - 8)) & 0xFF));
                bitsLeft -= 8;
            }
        }

        return [.. bytes];
    }
}
