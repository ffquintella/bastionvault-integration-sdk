using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BastionVault.IntegrationSdk.DocsSamples.Infrastructure;

namespace BastionVault.IntegrationSdk.DocsSamples.Samples;

/// <summary>
/// The executed sample shown by <c>docs/dotnet/configuration.md</c> (D3), the .NET configuration
/// reference. See <c>GettingStartedSamples</c> for the marker convention this file follows.
/// </summary>
public sealed class ConfigurationSamples
{
    [Fact]
    public void Explicit_options_construction()
    {
        // D-M11-17: a literal `CaCertPath` cannot be materialised at construction — CFG-013
        // validates the file is readable before the client exists — so this test writes a real,
        // readable PEM bundle to a temp path first, exactly as an application pointing at a CA
        // bundle that does not come from the process environment would have to.
        string caCertPath = Path.Combine(Path.GetTempPath(), $"bastionvault-docs-ca-{Guid.NewGuid():N}.pem");
        File.WriteAllText(caCertPath, GenerateSelfSignedCaPem());
        try
        {
            // docs:begin configuration/explicit-options
            // Every value spelled out in code, overriding whatever the process environment
            // holds (CFG-001: an explicit value always wins). CaCertPath is validated at
            // construction, so it must already point at a real, readable PEM bundle.
            using BastionVaultClient client = new(new BastionVaultClientOptions
            {
                Address = "https://vault.example.com:8200",
                Token = "s.FAKEtoken",
                Namespace = "team-a",
                CaCertPath = caCertPath,
            });

            Console.WriteLine($"address:   {client.Config.Address}");
            Console.WriteLine($"namespace: {client.Config.Namespace}");
            Console.WriteLine($"CA bundle: {client.Config.CaCertPath}");
            // docs:end configuration/explicit-options

            Assert.Equal("https://vault.example.com:8200", client.Config.Address);
            Assert.Equal("team-a", client.Config.Namespace);
            Assert.Equal(caCertPath, client.Config.CaCertPath);
            Assert.NotNull(client.Config.CaCertificates);
        }
        finally
        {
            File.Delete(caCertPath);
        }
    }

    // A fresh, throwaway self-signed CA (DOC-014: never a real one), generated once per test
    // run so this sample exercises a genuine PEM parse (CFG-014) instead of a hand-typed
    // literal that would need to stay valid forever.
    private static string GenerateSelfSignedCaPem()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        CertificateRequest request = new("CN=bastionvault-docs-demo", key, HashAlgorithmName.SHA256);
        using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));
        return certificate.ExportCertificatePem();
    }
}
