using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BastionVault.IntegrationSdk.DocsSamples.Infrastructure;

namespace BastionVault.IntegrationSdk.DocsSamples.Samples;

/// <summary>
/// Every executed sample shown by <c>docs/dotnet/security.md</c> (D10). Risk tier R2 (CRS-003):
/// this page is operator-followed instruction, so every sample here is checked against the exact
/// SDK types and messages it describes rather than paraphrased.
/// </summary>
public sealed class SecuritySamples
{
    [Fact]
    public void Secret_material_redacts_by_default()
    {
        // docs:begin security/redaction
        // SecretString never exposes its value through ToString, Debug output, or string
        // interpolation - only Reveal() does, and Reveal() is a method precisely so that reading
        // a secret out is always a visible, searchable call site (CNF-031, CNF-032).
        SecretString token = new("s.FAKEtoken");

        Console.WriteLine($"token: {token}");           // prints "token: [REDACTED]"
        Console.WriteLine($"token: {token.ToString()}"); // same

        string actualValue = token.Reveal()!;
        // docs:end security/redaction

        Assert.Equal("[REDACTED]", token.ToString());
        Assert.Equal("s.FAKEtoken", actualValue);
    }

    [Fact]
    public void Pinning_a_CA_bundle()
    {
        string caCertPath = Path.Combine(Path.GetTempPath(), $"bastionvault-docs-security-ca-{Guid.NewGuid():N}.pem");
        File.WriteAllText(caCertPath, GenerateSelfSignedCaPem());
        try
        {
            // docs:begin security/pin-a-ca
            // CaCertPath adds this bundle to the trust store used for this client's connections.
            // Set CaCertReplacesSystemRoots = true to trust *only* this bundle instead of adding
            // to the platform roots - the tighter choice for a service talking to one known vault.
            using BastionVaultClient client = new(new BastionVaultClientOptions
            {
                Address = "https://vault.example.com:8200",
                Token = "s.FAKEtoken",
                CaCertPath = caCertPath,
                CaCertReplacesSystemRoots = true,
            });

            Console.WriteLine($"insecure: {client.Config.IsInsecure}");
            // docs:end security/pin-a-ca

            Assert.False(client.Config.IsInsecure);
        }
        finally
        {
            File.Delete(caCertPath);
        }
    }

    [Fact]
    public void TlsSkipVerify_logs_a_warning_and_marks_the_client_insecure()
    {
        CapturingLogger logger = new();

        // docs:begin security/tls-skip-verify-danger
        // TlsSkipVerify disables certificate verification entirely: any server, including one on
        // the network path performing a man-in-the-middle, is accepted. It exists for a local dev
        // server with a self-signed certificate you cannot otherwise pin, never for production.
        // CNF-030 makes the danger observable rather than silent: setting it always emits exactly
        // one warning-level log line per Client instance, and Client.Config.IsInsecure becomes
        // true, so a health check or a log scraper can catch it even if nobody reads the console.
        using BastionVaultClient client = new(new BastionVaultClientOptions
        {
            Address = "https://vault.example.com:8200",
            Token = "s.FAKEtoken",
            TlsSkipVerify = true,
            Logger = logger,
        });

        Console.WriteLine($"insecure: {client.Config.IsInsecure}");
        // docs:end security/tls-skip-verify-danger

        Assert.True(client.Config.IsInsecure);
        Assert.Single(logger.Warnings);
        Assert.Contains("TlsSkipVerify=true", logger.Warnings[0]);
    }

    [Fact]
    public void Token_files_are_written_owner_only_and_never_by_default()
    {
        string tokenFile = Path.Combine(Path.GetTempPath(), $"bastionvault-docs-security-token-{Guid.NewGuid():N}");
        try
        {
            // docs:begin security/token-file-handling
            // CNF-033: the SDK never writes a token to disk unless the application explicitly
            // opts in with UseTokenHelper. When it does, the file is created with owner-only
            // permissions (0600, or the platform's closest equivalent) at creation time, not
            // chmod'd afterwards - a write-then-chmod would leave a window where the token sits
            // on disk under the process umask's default, wider, mode.
            using BastionVaultClient client = new(new BastionVaultClientOptions
            {
                Address = "https://vault.example.com:8200",
                Token = "s.FAKEtoken",
                TokenFile = tokenFile,
                UseTokenHelper = true,
            });

            client.Auth.PersistToken();
            // docs:end security/token-file-handling

            Assert.True(File.Exists(tokenFile));
            Assert.Equal("s.FAKEtoken", File.ReadAllText(tokenFile));
        }
        finally
        {
            File.Delete(tokenFile);
        }
    }

    [Fact]
    public async Task Reserved_token_metadata_keys_are_refused_client_side()
    {
        using BastionVaultClient client = new(new BastionVaultClientOptions
        {
            Address = "https://vault.example.com:8200",
            Token = "s.FAKEtoken",
        });

        // docs:begin security/reserved-token-metadata
        // Auth.Token.Create's meta is free-form, except for the identity/namespace/scope keys the
        // server reserves for itself: writing one of them is refused client-side (AUT-081) before
        // a request is even sent, with the same code the server would answer anyway.
        try
        {
            await client.Auth.Token.CreateAsync(new CreateTokenRequest
            {
                Policies = ["default"],
                Meta = new Dictionary<string, string> { ["username"] = "someone-else" },
            });
        }
        catch (BastionVaultException e) when (e.Code == ErrorCodes.InputReservedTokenMetaKey)
        {
            Console.Error.WriteLine($"{e.Code}: {e.Hint}");
        }
        // docs:end security/reserved-token-metadata

        BastionVaultException caught = await Assert.ThrowsAsync<BastionVaultException>(() => client.Auth.Token.CreateAsync(new CreateTokenRequest
        {
            Meta = new Dictionary<string, string> { ["spiffe_id"] = "x" },
        }));
        Assert.Equal(ErrorCodes.InputReservedTokenMetaKey, caught.Code);
    }

    [Fact]
    public void The_least_privilege_policy_a_service_account_needs()
    {
        // docs:begin security/least-privilege-policy
        // A service account reading one application's secrets needs exactly this, and no more:
        // read on the data it uses, and nothing on metadata, undelete, destroy, or any other
        // mount. PolicyBuilder emits the HCL deterministically, so the policy you review here is
        // the policy Sys.WritePolicy uploads - they cannot drift apart.
        string hcl = new PolicyBuilder()
            .AddPath("secret/data/app/*", [Capability.Read])
            .Build();

        Console.WriteLine(hcl);
        // docs:end security/least-privilege-policy

        Assert.Equal(PolicyShownInTheGuide(), hcl.Trim());
    }

    private static string PolicyShownInTheGuide()
    {
        string path = Path.Combine(DocsRepository.Docs.FullName, "dotnet", "security.md");
        string[] lines = File.ReadAllLines(path);
        int opening = Array.FindIndex(lines, line => line.TrimEnd() == "```hcl");
        Assert.True(opening >= 0, $"{path} shows no ```hcl policy block (DOC-011).");
        int closing = Array.FindIndex(lines, opening + 1, line => line.TrimEnd() == "```");
        Assert.True(closing > opening, $"{path}: the ```hcl block is never closed.");
        return string.Join('\n', lines[(opening + 1)..closing]).Trim();
    }

    private static string GenerateSelfSignedCaPem()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        CertificateRequest request = new("CN=bastionvault-docs-security-demo", key, HashAlgorithmName.SHA256);
        using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));
        return certificate.ExportCertificatePem();
    }

    private sealed class CapturingLogger : IClientLogger
    {
        private readonly List<string> warnings = [];

        public IReadOnlyList<string> Warnings => warnings;

        public void Warn(string message)
        {
            warnings.Add(message);
        }
    }
}
