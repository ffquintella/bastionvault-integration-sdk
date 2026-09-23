using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using BastionVault.IntegrationSdk.IntegrationTests.Harness;

namespace BastionVault.IntegrationSdk.IntegrationTests.Scenarios;

/// <summary>
/// ITG-S21: PKI root generation, roles, issuance, CSR signing, certificate reads, paging and CRL
/// (15-testing-requirements.md:259-261). This SDK performs no cryptography (OVR-002, D-M9-1);
/// where the scenario needs a real X.509 chain build or a real CSR, that work is done here with
/// <c>System.Security.Cryptography</c> as an independent verifier, exactly as ITG-S20 verifies TOTP
/// codes with its own RFC 6238 implementation rather than trusting the SDK's own claim.
/// </summary>
public sealed class Scenario21_PkiRootAndIssuance : IntegrationTest
{
    [IntegrationFact]
    public async Task Root_role_issue_sign_read_page_and_crl_all_map_correctly()
    {
        // D-0021-1 / review ruling (slice 5 handback): assert 15-testing-requirements.md:259-261's
        // specified outcome, never a defect's actual return; record any divergence and fail at the
        // bottom, once, so this goes green on its own the day F10 (dates) or R-37 (F2) land.
        List<string> findings = [];

        string mount = await Resources.MountAsync(MountTypes.Pki, "root");
        string roleName = Unique("role");
        string commonName = $"{Unique("leaf")}.example.test";

        string issuerId;
        string rootCertificatePem;
        try
        {
            PkiRootCertificate root = await Client.Pki.GenerateRootAsync(
                PkiKeyGenerationType.Internal, new PkiRootSpec { CommonName = "root-ca", KeyType = "ec" }, mount);
            Assert.False(string.IsNullOrEmpty(root.Certificate));
            Assert.False(string.IsNullOrEmpty(root.IssuerId));
            issuerId = root.IssuerId;
            rootCertificatePem = root.Certificate;
        }
        catch (BastionVaultException ex)
        {
            findings.Add($"GenerateRootAsync should return a parsed root certificate (F10): {ex.Code} {ex.ServerMessage}");

            // Reach past the broken typed path to keep exercising the rest of the surface: the
            // root exists server-side regardless (F10 is a response-parsing defect, not a
            // generation one), readable through two routes whose typed shapes never touch a date.
            PkiIssuersConfig? issuers = await Client.Pki.ReadIssuersConfigAsync(mount);
            issuerId = issuers?.Default ?? throw new InvalidOperationException("expected a default issuer after root generation");
            IReadOnlyDictionary<string, JsonElement>? ca = await Client.Pki.ReadCaAsync(pem: true, mount: mount);
            rootCertificatePem = ca?["certificate"].GetString() ?? throw new InvalidOperationException("expected {mount}/ca/pem to carry a certificate field");
        }

        // F2/R-37: 09 Roles and issuance gives PkiRole a Ttl and a MaxTtl. Measured rejected
        // (BV-INPUT-100) here and on every other PKI duration field below - see the handback table.
        try
        {
            await Client.Pki.WriteRoleAsync(
                roleName, new PkiRole { Ttl = TimeSpan.FromHours(12), MaxTtl = TimeSpan.FromDays(1), KeyType = "ec", IssuerRef = issuerId }, mount);
        }
        catch (BastionVaultException ex)
        {
            findings.Add($"WriteRoleAsync should accept Ttl/MaxTtl (F2/R-37): {ex.Code}");
            await Client.Pki.WriteRoleAsync(roleName, new PkiRole { KeyType = "ec", IssuerRef = issuerId }, mount);
        }

        // F2/R-37: IssueRequest.Ttl. Measured rejected, same as the role's.
        IssuedCertificate issued;
        try
        {
            issued = await Client.Pki.IssueAsync(roleName, new IssueRequest { CommonName = commonName, Ttl = TimeSpan.FromHours(6) }, mount);
        }
        catch (BastionVaultException ex)
        {
            findings.Add($"IssueAsync should accept Ttl (F2/R-37): {ex.Code}");
            issued = await Client.Pki.IssueAsync(roleName, new IssueRequest { CommonName = commonName }, mount);
        }

        Assert.False(string.IsNullOrEmpty(issued.Certificate));
        Assert.False(string.IsNullOrEmpty(issued.SerialNumber));

        using (X509Certificate2 leaf = X509Certificate2.CreateFromPem(issued.Certificate))
        using (X509Certificate2 rootCert = X509Certificate2.CreateFromPem(rootCertificatePem))
        {
            Assert.Contains(commonName, leaf.Subject, StringComparison.Ordinal);

            using X509Chain chain = new();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            _ = chain.ChainPolicy.CustomTrustStore.Add(rootCert);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            bool chainBuilds = chain.Build(leaf);
            Assert.True(
                chainBuilds,
                "expected the issued leaf to chain to the generated root; statuses: " +
                string.Join(", ", chain.ChainStatus.Select(s => $"{s.Status}: {s.StatusInformation}")));
        }

        // Sign a CSR generated independently of the SDK (no crypto in the SDK, OVR-002).
        using ECDsa csrKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string csrCommonName = $"{Unique("csr")}.example.test";
        CertificateRequest csrRequest = new($"CN={csrCommonName}", csrKey, HashAlgorithmName.SHA256);
        string csrPem = ToPem("CERTIFICATE REQUEST", csrRequest.CreateSigningRequest());

        // F2/R-37: SignRequest.Ttl. Measured rejected, same as the others.
        SignedCertificate signed;
        try
        {
            signed = await Client.Pki.SignAsync(roleName, new SignRequest { Csr = csrPem, Ttl = TimeSpan.FromHours(2) }, mount);
        }
        catch (BastionVaultException ex)
        {
            findings.Add($"SignAsync should accept Ttl (F2/R-37): {ex.Code}");
            signed = await Client.Pki.SignAsync(roleName, new SignRequest { Csr = csrPem }, mount);
        }

        using (X509Certificate2 signedCert = X509Certificate2.CreateFromPem(signed.Certificate))
        {
            Assert.Contains(csrCommonName, signedCert.Subject, StringComparison.Ordinal);
        }

        // ITG-S21: "read cert by serial". Same F10 defect class: `issued_at` is a required field
        // ReadCertificateAsync cannot parse today. Reached past via Logical.Raw when it fails.
        try
        {
            CertificateRecord? record = await Client.Pki.ReadCertificateAsync(issued.SerialNumber, mount);
            Assert.NotNull(record);
            Assert.Equal(issued.SerialNumber, record!.SerialNumber);
        }
        catch (BastionVaultException ex)
        {
            findings.Add($"ReadCertificateAsync should parse (F10): {ex.Code}");
            RawResponse rawCertificate = await Client.Logical.RawAsync("GET", $"/v1/{mount}/cert/{issued.SerialNumber}");
            using JsonDocument rawCertificateDoc = JsonDocument.Parse(rawCertificate.Body);
            Assert.Equal(issued.Certificate, rawCertificateDoc.RootElement.GetProperty("data").GetProperty("certificate").GetString());
        }

        // ITG-S21: "ListCertificatesInfo pages it with common_name". Same F10 class (NotAfter is
        // required in CertificateSummary).
        try
        {
            Page<CertificateSummary> page = await Client.Pki.ListCertificatesInfoAsync(mount, limit: 50);
            Assert.Contains(page.Records, r => r.CommonName == commonName);
        }
        catch (BastionVaultException ex)
        {
            findings.Add($"ListCertificatesInfoAsync should parse and page results (F10): {ex.Code}");
            RawResponse rawCertsInfo = await Client.Logical.RawAsync("GET", $"/v1/{mount}/certs-info?limit=50");
            using JsonDocument rawCertsInfoDoc = JsonDocument.Parse(rawCertsInfo.Body);
            bool sawCommonName = rawCertsInfoDoc.RootElement.GetProperty("data").GetProperty("records").EnumerateArray()
                .Any(record => record.TryGetProperty("common_name", out JsonElement cn) && cn.GetString() == commonName);
            Assert.True(sawCommonName, $"expected certs-info to list a record with common_name {commonName}");
        }

        await Client.Pki.RevokeAsync(issued.SerialNumber, mount);

        // ITG-S21: "revoke -> CRL contains serial". Not F10 - `crl_number` is entirely absent from
        // the response (an owner question per the review: a missing field, not a mis-encoded one),
        // so Crl's required CrlNumber never parses.
        string crlPemBeforeRotate;
        try
        {
            Crl crl = await Client.Pki.ReadCrlAsync(mount: mount);
            Assert.True(crl.CrlNumber > 0);
            crlPemBeforeRotate = crl.CrlPem;
        }
        catch (BastionVaultException ex)
        {
            findings.Add($"ReadCrlAsync should return crl_number (missing field): {ex.Code}");
            crlPemBeforeRotate = await ReadCrlPemViaRaw(mount);
        }

        AssertCrlContainsSerial(crlPemBeforeRotate, issued.Certificate);

        await Client.Pki.RotateCrlAsync(mount);

        string crlPemAfterRotate;
        try
        {
            crlPemAfterRotate = (await Client.Pki.ReadCrlAsync(mount: mount)).CrlPem;
        }
        catch (BastionVaultException)
        {
            crlPemAfterRotate = await ReadCrlPemViaRaw(mount);
        }

        AssertCrlContainsSerial(crlPemAfterRotate, issued.Certificate);
        Assert.NotEqual(crlPemBeforeRotate, crlPemAfterRotate);

        await Client.Pki.TidyAsync(mount: mount);

        Assert.True(findings.Count == 0, "ITG-S21 unmet requirements:\n" + string.Join("\n", findings));
    }

    private async Task<string> ReadCrlPemViaRaw(string mount)
    {
        RawResponse raw = await Client.Logical.RawAsync("GET", $"/v1/{mount}/crl");
        using JsonDocument doc = JsonDocument.Parse(raw.Body);
        return doc.RootElement.GetProperty("data").GetProperty("crl").GetString()
            ?? throw new InvalidOperationException("expected a crl field");
    }

    /// <summary>
    /// Verifies the CRL's DER body contains the leaf's own serial-number bytes: a real, if narrow,
    /// check that the revoked entry is present, without the SDK (or this test) parsing a full X.509
    /// CRL structure (OVR-002 stays a test-code boundary, not a licence to weaken the assertion).
    /// </summary>
    private static void AssertCrlContainsSerial(string crlPem, string certificatePem)
    {
        byte[] crlDer = FromPem(crlPem);
        using X509Certificate2 cert = X509Certificate2.CreateFromPem(certificatePem);
        byte[] serial = [.. cert.SerialNumberBytes.Span];
        Assert.True(
            ContainsSubsequence(crlDer, serial),
            $"expected the CRL DER body to contain serial {Convert.ToHexString(serial)}");
    }

    private static bool ContainsSubsequence(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || needle.Length > haystack.Length)
        {
            return false;
        }

        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return true;
            }
        }

        return false;
    }

    internal static string ToPem(string label, byte[] der)
    {
        string base64 = Convert.ToBase64String(der, Base64FormattingOptions.InsertLineBreaks);
        return $"-----BEGIN {label}-----\n{base64}\n-----END {label}-----\n";
    }

    internal static byte[] FromPem(string pem)
    {
        ReadOnlySpan<char> span = pem.AsSpan();
        int begin = span.IndexOf("-----BEGIN");
        int headerEnd = span[begin..].IndexOf('\n') + begin;
        int footerStart = span.LastIndexOf("-----END");
        string body = pem[(headerEnd + 1)..footerStart];
        return Convert.FromBase64String(body.Replace("\n", string.Empty, StringComparison.Ordinal).Replace("\r", string.Empty, StringComparison.Ordinal));
    }
}

/// <summary>ITG-S22: PKI intermediate flow (15-testing-requirements.md:262-263).</summary>
public sealed class Scenario22_PkiIntermediateFlow : IntegrationTest
{
    [IntegrationFact]
    public async Task Intermediate_generate_sign_set_signed_and_issue_all_map_correctly()
    {
        // D-0021-1 / review ruling: assert the specified outcome, not the defect. See Scenario21.
        List<string> findings = [];

        string rootMount = await Resources.MountAsync(MountTypes.Pki, "int-root");
        string intMount = await Resources.MountAsync(MountTypes.Pki, "int-child");

        try
        {
            _ = await Client.Pki.GenerateRootAsync(
                PkiKeyGenerationType.Internal, new PkiRootSpec { CommonName = "root-ca", KeyType = "ec" }, rootMount);
        }
        catch (BastionVaultException ex)
        {
            // F10 (same defect Scenario21 documents in full): the root exists server-side
            // regardless, and this flow only needs the mount to have a default issuer.
            findings.Add($"GenerateRootAsync should return a parsed root certificate (F10): {ex.Code}");
        }

        PkiIntermediateCsr intermediateCsr = await Client.Pki.GenerateIntermediateAsync(
            PkiKeyGenerationType.Internal,
            new PkiIntermediateSpec { CommonName = $"{Unique("intermediate")}-ca", KeyType = "ec" },
            intMount);
        Assert.False(string.IsNullOrEmpty(intermediateCsr.Csr));

        // F2/R-37: SignIntermediateRequest.Ttl - measured rejected, same as every other PKI
        // duration field Scenario21 exercises.
        SignedIntermediateCertificate signedIntermediate;
        try
        {
            signedIntermediate = await Client.Pki.SignIntermediateAsync(
                new SignIntermediateRequest { Csr = intermediateCsr.Csr, Ttl = TimeSpan.FromDays(15) }, rootMount);
        }
        catch (BastionVaultException ex)
        {
            findings.Add($"SignIntermediateAsync should accept Ttl (F2/R-37): {ex.Code}");
            signedIntermediate = await Client.Pki.SignIntermediateAsync(
                new SignIntermediateRequest { Csr = intermediateCsr.Csr }, rootMount);
        }

        Assert.False(string.IsNullOrEmpty(signedIntermediate.Certificate));

        SetSignedIntermediateResult setSigned = await Client.Pki.SetSignedIntermediateAsync(
            signedIntermediate.Certificate, mount: intMount);
        Assert.NotEmpty(setSigned.ImportedIssuers);

        string roleName = Unique("role");
        await Client.Pki.WriteRoleAsync(
            roleName, new PkiRole { KeyType = "ec", IssuerRef = setSigned.IssuerId }, intMount);

        string commonName = $"{Unique("leaf")}.example.test";
        IssuedCertificate issued = await Client.Pki.IssueAsync(
            roleName, new IssueRequest { CommonName = commonName, IssuerRef = setSigned.IssuerId }, intMount);
        Assert.False(string.IsNullOrEmpty(issued.Certificate));

        using X509Certificate2 leaf = X509Certificate2.CreateFromPem(issued.Certificate);
        Assert.Contains(commonName, leaf.Subject, StringComparison.Ordinal);

        Assert.True(findings.Count == 0, "ITG-S22 unmet requirements:\n" + string.Join("\n", findings));
    }
}

/// <summary>ITG-S23: SSH CA and OTP modes (15-testing-requirements.md:265-269).</summary>
public sealed class Scenario23_Ssh : IntegrationTest
{
    [IntegrationFact]
    public async Task Ca_signing_and_otp_creds_all_map_correctly()
    {
        string mount = await Resources.MountAsync(MountTypes.Ssh, "ca");

        SshCaKey ca = await Client.Ssh.ConfigureCaAsync(mount: mount);
        Assert.False(string.IsNullOrEmpty(ca.PublicKey));

        string caRoleName = Unique("carole");
        await Client.Ssh.WriteRoleAsync(
            caRoleName,
            new SshRole
            {
                KeyType = "ca",
                AllowedUsers = ["testuser"],
                DefaultUser = "testuser",
                CertType = "user",
                Ttl = TimeSpan.FromHours(2),
                MaxTtl = TimeSpan.FromHours(4),
            },
            mount);

        (string publicKeyLine, string tempDir) = GenerateLocalEd25519PublicKey();
        try
        {
            SignedSshCertificate signed = await Client.Ssh.SignAsync(
                caRoleName,
                new SshSignRequest { PublicKey = publicKeyLine, ValidPrincipals = ["testuser"], CertType = "user" },
                mount);
            Assert.False(string.IsNullOrEmpty(signed.SignedKey));

            string certPathPrefix = Path.Combine(tempDir, "issued");
            Client.Ssh.WriteCertificateFile(signed.SignedKey, certPathPrefix);
            string certPath = $"{certPathPrefix}-cert.pub";
            Assert.True(File.Exists(certPath));

            string parsed = RunSshKeygen($"-L -f \"{certPath}\"");
            Assert.Contains("testuser", parsed, StringComparison.Ordinal);
            Assert.Contains("user certificate", parsed, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }

        string otpRoleName = Unique("otprole");
        await Client.Ssh.WriteRoleAsync(
            otpRoleName,
            new SshRole { KeyType = "otp", DefaultUser = "otpuser", CidrList = ["203.0.113.0/24"] },
            mount);

        SshCredentials creds = await Client.Ssh.CredsAsync(otpRoleName, "203.0.113.7", mount: mount);
        Assert.Equal("otp", creds.KeyType);

        SshOtpVerification? verified = await Client.Ssh.VerifyAsync(creds.Key, mount);
        Assert.NotNull(verified);
        Assert.Equal(otpRoleName, verified!.RoleName);

        BastionVaultException ipOutOfRange = await Assert.ThrowsAsync<BastionVaultException>(
            () => Client.Ssh.CredsAsync(otpRoleName, "198.51.100.7", mount: mount));
        Assert.Equal(ErrorCodes.SshIpNotAllowed, ipOutOfRange.Code);

        BastionVaultException bogusOtp = await Assert.ThrowsAsync<BastionVaultException>(
            () => Client.Ssh.VerifyAsync(new SecretString("not-a-real-otp"), mount));
        Assert.Equal(ErrorCodes.SshInvalidOtp, bogusOtp.Code);
    }

    /// <summary>
    /// Shells out to the OS <c>ssh-keygen</c> (D-M9-1: this SDK does no cryptography, and neither
    /// does this test) to produce a real OpenSSH ed25519 key pair, mirroring ITG-S20's independent
    /// RFC 6238 verifier.
    /// </summary>
    private static (string PublicKeyLine, string TempDir) GenerateLocalEd25519PublicKey()
    {
        string tempDir = Directory.CreateTempSubdirectory("bvault-itg-s23-").FullName;
        string keyPath = Path.Combine(tempDir, "id_ed25519");
        _ = RunSshKeygen($"-t ed25519 -N \"\" -C \"itg-s23\" -f \"{keyPath}\"");
        string publicKeyLine = File.ReadAllText($"{keyPath}.pub").Trim();
        return (publicKeyLine, tempDir);
    }

    private static string RunSshKeygen(string arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "ssh-keygen",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("ssh-keygen did not start");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        _ = process.WaitForExit(30_000);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ssh-keygen {arguments} failed ({process.ExitCode}): {error}");
        }

        return output;
    }
}

/// <summary>ITG-S24: the files engine (15-testing-requirements.md:272-273).</summary>
public sealed class Scenario24_Files : IntegrationTest
{
    [IntegrationFact]
    public async Task Create_read_version_restore_and_delete_all_map_correctly()
    {
        // D-0021-1 / review ruling: assert the specified outcome, not the defect. See Scenario21.
        List<string> findings = [];

        string mount = await Resources.MountAsync(MountTypes.Files, "docs");
        string name = Unique("file");
        byte[] firstContent = "scenario24 v1"u8.ToArray();

        string id = await Client.Files.CreateAsync(new FileCreateRequest { Name = name, Content = firstContent }, mount);
        Resources.Track("file", id, ct => Client.Files.DeleteAsync(id, mount, cancellationToken: ct));

        byte[] readBack = await Client.Files.ContentAsync(id, mount);
        Assert.Equal(firstContent, readBack);

        // FileUpdateRequest (12 §Files) carries no content field - there is no typed way to write a
        // new version's bytes; Logical.Write reaches the same wire route with a raw body instead of
        // widening a frozen type (FAM-002: a missing Content member on FileUpdateRequest is an open
        // question for the Architect, not something this scenario works around by inventing API).
        byte[] secondContent = "scenario24 v2, longer than v1"u8.ToArray();
        JsonElement updateBody = JsonDocument.Parse(
            $$"""{"content_base64":"{{Convert.ToBase64String(secondContent)}}"}""").RootElement;
        _ = await Client.Logical.WriteAsync($"{mount}/files/{id}", updateBody);

        byte[] readAfterUpdate = await Client.Files.ContentAsync(id, mount);
        Assert.Equal(secondContent, readAfterUpdate);

        // ITG-S24: "second write creates a version". Measured: `{mount}/files/{id}/versions` wraps
        // its array as `data.versions` (Shape A, a named nested key), not `data` itself as an array
        // (Shape B); IdentityKernelWire.ReadArrayEnvelope only unwraps the latter, so VersionsAsync
        // returns empty here even though a prior version exists (confirmed via Logical.Raw).
        IReadOnlyList<JsonElement> versions = await Client.Files.VersionsAsync(id, mount);
        if (versions.Count == 0)
        {
            findings.Add("VersionsAsync should list the prior version (data.versions shape mismatch in IdentityKernelWire.ReadArrayEnvelope)");
            RawResponse rawVersions = await Client.Logical.RawAsync("GET", $"/v1/{mount}/files/{id}/versions");
            using JsonDocument rawVersionsDoc = JsonDocument.Parse(rawVersions.Body);
            JsonElement versionsArray = rawVersionsDoc.RootElement.GetProperty("data").GetProperty("versions");
            Assert.Equal(1, versionsArray.GetArrayLength());
        }
        else
        {
            _ = Assert.Single(versions);
        }

        await Client.Files.RestoreVersionAsync(id, 1, mount);
        byte[] readAfterRestore = await Client.Files.ContentAsync(id, mount);
        Assert.Equal(firstContent, readAfterRestore);

        await Client.Files.DeleteAsync(id, mount);
        Resources.Track("file-already-deleted", id, _ => Task.CompletedTask);

        Assert.True(findings.Count == 0, "ITG-S24 unmet requirements:\n" + string.Join("\n", findings));
    }
}

/// <summary>ITG-S25: the resources engine (15-testing-requirements.md:274-275).</summary>
public sealed class Scenario25_Resources : IntegrationTest
{
    [IntegrationFact]
    public async Task Create_secret_list_history_rename_and_delete_all_map_correctly()
    {
        // D-0021-1 / review ruling: assert the specified outcome, not the defect. See Scenario21.
        List<string> findings = [];

        string mount = await Resources.MountAsync(MountTypes.Resource, "assets");
        string name = Unique("box");

        JsonElement record = JsonDocument.Parse("""{"type":"generic"}""").RootElement;
        _ = await Client.Resources.WriteAsync(name, record, mount);
        Resources.Track("resource", name, ct => Client.Resources.DeleteAsync(name, mount, cancellationToken: ct));

        Response? read = await Client.Resources.ReadAsync(name, mount);
        Assert.NotNull(read);

        string secretKey = Unique("secretkey");
        JsonElement secretValue = JsonDocument.Parse("""{"password":"s3cr3t"}""").RootElement;
        _ = await Client.Resources.Secrets.WriteAsync(name, secretKey, secretValue, mount);

        IReadOnlyList<string> secrets = await Client.Resources.Secrets.ListAsync(name, mount);
        Assert.Contains(secretKey, secrets);

        // ITG-S25: "secret history has one version". Same shape defect Scenario24 documents for
        // Files: this route wraps its array as `data.versions`, not `data` itself, so HistoryAsync's
        // IdentityKernelWire.ReadArrayEnvelope never unwraps it. Confirmed via Logical.Raw.
        IReadOnlyList<JsonElement> history = await Client.Resources.Secrets.HistoryAsync(name, secretKey, mount);
        if (history.Count == 0)
        {
            findings.Add("Secrets.History should list one version (data.versions shape mismatch in IdentityKernelWire.ReadArrayEnvelope)");
            RawResponse rawHistory = await Client.Logical.RawAsync("GET", $"/v1/{mount}/secrets/{name}/{secretKey}/history");
            using JsonDocument rawHistoryDoc = JsonDocument.Parse(rawHistory.Body);
            JsonElement historyVersions = rawHistoryDoc.RootElement.GetProperty("data").GetProperty("versions");
            Assert.Equal(1, historyVersions.GetArrayLength());
        }
        else
        {
            _ = Assert.Single(history);
        }

        string newName = Unique("box-renamed");
        await Client.Resources.RenameAsync(name, newName, mount);
        Resources.Track("resource-renamed", newName, ct => Client.Resources.DeleteAsync(newName, mount, cancellationToken: ct));

        IReadOnlyList<string> secretsAfterRename = await Client.Resources.Secrets.ListAsync(newName, mount);
        Assert.Contains(secretKey, secretsAfterRename);

        await Client.Resources.DeleteAsync(newName, mount);
        Resources.Track("resource-already-deleted", newName, _ => Task.CompletedTask);

        Assert.True(findings.Count == 0, "ITG-S25 unmet requirements:\n" + string.Join("\n", findings));
    }
}

/// <summary>ITG-S26: identity groups, sharing and entity self (15-testing-requirements.md:276-278).</summary>
public sealed class Scenario26_Identity : IntegrationTest
{
    [IntegrationFact]
    public async Task Group_policy_inheritance_entity_self_and_sharing_all_map_correctly()
    {
        // D-0021-1 / review ruling: assert the specified outcome, not the defect. See Scenario21.
        List<string> findings = [];

        string authMount = await Resources.EnableAuthAsync(AuthTypes.Userpass, "up");
        string kvMount = await Resources.MountAsync(MountTypes.KvV2, "shared");
        string secretPath = Unique("secret");
        _ = await Client.Kv.V2.WriteSecretAsync(
            secretPath,
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["value"] = JsonDocument.Parse("\"shared-value\"").RootElement.Clone(),
            },
            kvMount);

        string policyName = await Resources.WritePolicyAsync("group-policy", $$"""
            path "{{kvMount}}/data/{{secretPath}}" {
              capabilities = ["read"]
            }
            """);

        string username = Resources.TrackUser(authMount, "dave");
        SecretString password = new("Sc3n26-passw0rd!");
        _ = await Client.Auth.Userpass.Admin.WriteUserAsync(
            username,
            JsonDocument.Parse($$"""{"password":"{{password.Reveal()}}"}""").RootElement,
            authMount);

        using BastionVaultClient anon = Server.CreateClient(o => o.Token = null!);
        AuthInfo firstLogin = await anon.Auth.Userpass.LoginAsync(username, password, mount: authMount);
        Resources.TrackToken("dave-first-session", firstLogin.ClientToken);

        using BastionVaultClient asDave = Server.CreateClient(o => o.Token = firstLogin.ClientToken.Reveal());
        EntitySelf self = await asDave.Identity.SelfAsync();
        Assert.Equal(username, self.Username);
        string entityId = self.EntityId ?? throw new InvalidOperationException("expected an entity_id");

        // Before the group grants it, dave's own token has no path to this secret.
        BastionVaultException deniedBeforeGroup = await Assert.ThrowsAsync<BastionVaultException>(
            () => asDave.Kv.V2.ReadSecretAsync(secretPath, kvMount));
        Assert.Equal(ErrorCodes.AuthzPermissionDenied, deniedBeforeGroup.Code);

        string groupName = Unique("group");
        await Client.Identity.Groups.WriteAsync(
            "user", groupName, new IdentityGroupSpec { Members = [entityId], Policies = [policyName] });
        Resources.Track("identity-group", groupName, ct => Client.Identity.Groups.DeleteAsync("user", groupName, cancellationToken: ct));

        AuthInfo secondLogin = await anon.Auth.Userpass.LoginAsync(username, password, mount: authMount);
        Resources.TrackToken("dave-second-session", secondLogin.ClientToken);

        // ITG-S26: "user login policies include it". Measured: they do not, on this server - a
        // server-side identity-group resolution gap, not a client mistake (the group record itself
        // round-trips correctly, confirmed via Logical.Raw during triage).
        if (!secondLogin.Policies.Contains(policyName))
        {
            findings.Add("the second login's policies should include the group's policy (identity-group resolution gap)");
        }

        using BastionVaultClient asDaveSecond = Server.CreateClient(o => o.Token = secondLogin.ClientToken.Reveal());
        try
        {
            KvV2Secret? afterGroup = await asDaveSecond.Kv.V2.ReadSecretAsync(secretPath, kvMount);
            Assert.NotNull(afterGroup);
        }
        catch (BastionVaultException ex)
        {
            findings.Add($"reading the shared secret after group membership should succeed (identity-group resolution gap): {ex.Code}");
        }

        string target = $"{kvMount}/{secretPath}";
        await Client.Identity.Sharing.PutAsync(
            "kv-secret",
            target,
            groupName,
            new IdentitySharingSpec { GranteeKind = "group_user", Capabilities = ["read"] });
        Resources.Track("identity-share", $"{target}->{groupName}", ct => Client.Identity.Sharing.DeleteAsync("kv-secret", target, groupName, cancellationToken: ct));

        // The direct grant round-trips: reading it back by its own key confirms the share was
        // created exactly as requested, independent of the list forms checked below.
        JsonElement? directGrant = await Client.Identity.Sharing.GetAsync("kv-secret", target, groupName);
        _ = Assert.NotNull(directGrant);
        JsonElement directGrantData = directGrant!.Value.GetProperty("data");
        Assert.Equal(target, directGrantData.GetProperty("target_path").GetString());
        Assert.Equal("group_user", directGrantData.GetProperty("grantee_kind").GetString());

        // ITG-S26: "Sharing.ForMe for the user lists it". Measured: none of the three list forms
        // this scenario's spec line names return the grant confirmed above - a server-side
        // indexing gap, not a client mistake.
        IReadOnlyList<string> byGrantee = await Client.Identity.Sharing.ListByGranteeAsync(groupName);
        if (byGrantee.Count == 0)
        {
            findings.Add("ListByGrantee should list the group's share (server-side indexing gap)");
        }

        IReadOnlyList<string> byTarget = await Client.Identity.Sharing.ListByTargetAsync("kv-secret", target);
        if (byTarget.Count == 0)
        {
            findings.Add("ListByTarget should list the group's share (server-side indexing gap)");
        }

        IdentitySharingForMe forMe = await asDaveSecond.Identity.Sharing.ForMeAsync();
        if (forMe.Entries.Count == 0)
        {
            findings.Add("Sharing.ForMe should list the group's share for the member (server-side indexing gap)");
        }

        Assert.True(findings.Count == 0, "ITG-S26 unmet requirements:\n" + string.Join("\n", findings));
    }
}
