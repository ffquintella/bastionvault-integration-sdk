using System.Text.Json;
using BastionVault.IntegrationSdk.DocsSamples.Infrastructure;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.DocsSamples.Samples;

/// <summary>
/// Every executed sample shown by <c>docs/dotnet/engines/ssh.md</c> (D6), built from
/// <c>specifications/17-usage-guides.md</c> guide 12 (the SSH half) and
/// <c>10-ssh-engine.md</c>. Reuses <see cref="MockVaultFixture"/>'s server: routes are bound per
/// test on <see cref="MockVaultFixture.Server"/> and cleared after, so no test leaks a route to
/// another.
/// </summary>
public sealed class SshSamples : IClassFixture<MockVaultFixture>
{
    private const string ConfigureCaRoute = "/v1/ssh/config/ca";
    private const string OpsRoleRoute = "/v1/ssh/roles/ops";
    private const string SignRoute = "/v1/ssh/sign/ops";
    private const string BastionRoleRoute = "/v1/ssh/roles/bastion-hosts";
    private const string CredsRoute = "/v1/ssh/creds/bastion-hosts";
    private const string VerifyRoute = "/v1/ssh/verify";

    private readonly MockVaultFixture vault;

    public SshSamples(MockVaultFixture vault)
    {
        this.vault = vault;
    }

    [Fact]
    public void The_policy_this_guide_needs_is_the_policy_PolicyBuilder_builds()
    {
        // docs:begin ssh/policy
        string hcl = new PolicyBuilder()
            .AddPath("ssh/config/ca", [Capability.Create, Capability.Read])
            .AddPath("ssh/roles/ops", [Capability.Create, Capability.Read])
            .AddPath("ssh/sign/ops", [Capability.Update])
            .AddPath("ssh/roles/bastion-hosts", [Capability.Create, Capability.Read])
            .AddPath("ssh/creds/bastion-hosts", [Capability.Update])
            .AddPath("ssh/verify", [Capability.Update])
            .Build();

        Console.WriteLine(hcl);
        // docs:end ssh/policy

        Assert.Equal(PolicyShownInTheGuide(), hcl.Trim());
    }

    private static string PolicyShownInTheGuide()
    {
        string path = Path.Combine(DocsRepository.Docs.FullName, "dotnet", "engines", "ssh.md");
        string[] lines = File.ReadAllLines(path);
        int opening = Array.FindIndex(lines, line => line.TrimEnd() == "```hcl");
        Assert.True(opening >= 0, $"{path} shows no ```hcl policy block (DOC-011).");
        int closing = Array.FindIndex(lines, opening + 1, line => line.TrimEnd() == "```");
        Assert.True(closing > opening, $"{path}: the ```hcl block is never closed.");
        return string.Join('\n', lines[(opening + 1)..closing]).Trim();
    }

    [Fact]
    public async Task Step_1_configure_the_ca_and_sign_a_user_certificate()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(ConfigureCaRoute, Json(200, CaBody()));
        vault.Server.SetRouteResponse(OpsRoleRoute, Json(200, "{}"));
        vault.Server.SetRouteResponse(SignRoute, Json(200, SignedCertBody()));
        string certPath = "id_ed25519-cert.pub";

        try
        {
            // docs:begin ssh/configure-ca-and-sign
            SshCaKey ca = await client.Ssh.ConfigureCaAsync(generateSigningKey: true);
            Console.WriteLine($"CA public key: {ca.PublicKey}");

            await client.Ssh.WriteRoleAsync("ops", new SshRole
            {
                KeyType = "ca",
                AllowedUsers = ["ubuntu", "ops"],
                DefaultUser = "ubuntu",
                Ttl = TimeSpan.FromMinutes(30),
            });

            // SSH-001: an empty or whitespace-only PublicKey is refused client-side, before any request is sent.
            SignedSshCertificate signed = await client.Ssh.SignAsync("ops", new SshSignRequest
            {
                PublicKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIFAKEUSERKEY user@example.com",
                ValidPrincipals = ["ubuntu"],
            });
            client.Ssh.WriteCertificateFile(signed.SignedKey, "id_ed25519");
            // SSH-002: writes id_ed25519-cert.pub with 0644, the file OpenSSH expects alongside the key pair.
            // docs:end ssh/configure-ca-and-sign

            Assert.Equal("42", signed.SerialNumber);
            Assert.True(File.Exists(certPath));
        }
        finally
        {
            File.Delete(certPath);
        }

        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task Step_2_issue_and_verify_a_one_time_password()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(BastionRoleRoute, Json(200, "{}"));
        vault.Server.SetRouteResponse(CredsRoute, Json(200, CredsBody()));
        vault.Server.SetRouteResponse(VerifyRoute, Json(200, VerifyBody()));

        // docs:begin ssh/otp-creds-and-verify
        await client.Ssh.WriteRoleAsync("bastion-hosts", new SshRole { KeyType = "otp", DefaultUser = "ubuntu" });

        // SSH-003: ip is validated as an IP literal client-side, before any request is sent.
        SshCredentials creds = await client.Ssh.CredsAsync("bastion-hosts", ip: "10.0.0.12", username: "ubuntu");
        Console.WriteLine($"one-time password issued for {creds.Username}@{creds.Ip}:{creds.Port}");

        // Verify is what the bastion host itself calls once the caller presents the OTP.
        SshOtpVerification? verification = await client.Ssh.VerifyAsync(creds.Key);
        Console.WriteLine(verification is null ? "otp rejected" : $"otp valid for {verification.Username}@{verification.Ip}");
        // docs:end ssh/otp-creds-and-verify

        Assert.NotNull(verification);
        Assert.Equal("ubuntu", verification!.Username);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task The_whole_program_configures_a_ca_and_signs_a_certificate()
    {
        vault.Server.ClearRouteResponses();

        // docs:begin ssh/complete
        using BastionVaultClient client = new();

        try
        {
            vault.Server.SetRouteResponse(ConfigureCaRoute, Json(200, CaBody()));
            await client.Ssh.ConfigureCaAsync(generateSigningKey: true);

            vault.Server.SetRouteResponse(OpsRoleRoute, Json(200, "{}"));
            await client.Ssh.WriteRoleAsync("ops", new SshRole { KeyType = "ca", AllowedUsers = ["ubuntu"], DefaultUser = "ubuntu" });

            vault.Server.SetRouteResponse(SignRoute, Json(200, SignedCertBody()));
            SignedSshCertificate signed = await client.Ssh.SignAsync("ops", new SshSignRequest
            {
                PublicKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIFAKEUSERKEY user@example.com",
                ValidPrincipals = ["ubuntu"],
            });
            Console.WriteLine($"signed certificate serial {signed.SerialNumber}");
        }
        catch (BastionVaultException e)
        {
            Console.Error.WriteLine($"{e.Code}: {e.Message} ({e.Hint}); retryable: {e.Retryable}");
            throw;
        }
        // docs:end ssh/complete

        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task What_can_go_wrong_maps_every_ssh_code_to_a_remedy()
    {
        vault.Server.ClearRouteResponses();
        vault.Server.SetRouteResponse(
            "/v1/ssh/sign/missing-role", Json(500, """{"error":"unknown role `missing-role`"}"""));
        using BastionVaultClient client = vault.CreateClient();

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(async () =>
        {
            // docs:begin ssh/handling-errors
            try
            {
                await client.Ssh.SignAsync("missing-role", new SshSignRequest { PublicKey = "ssh-ed25519 AAAA... user@example.com" });
            }
            catch (BastionVaultException e)
            {
                string remedy = e.Code switch
                {
                    ErrorCodes.SshCaNotConfigured => "configure the CA first",
                    ErrorCodes.SshRoleNotFound => "check the role name, or write it first",
                    ErrorCodes.SshWrongRoleMode => "use Sign for ca roles, Creds for otp roles",
                    ErrorCodes.SshIpNotAllowed => "check the role's cidr_list",
                    ErrorCodes.SshInvalidOtp => "issue a fresh otp; this one is gone",
                    ErrorCodes.AuthzPrincipalNotAllowed => "request only principals the role allows",
                    _ => "look the code up in the error reference",
                };
                Console.Error.WriteLine($"{e.Code}: {e.Message} - {remedy}");
                throw;
            }
            // docs:end ssh/handling-errors
        });

        Assert.Equal(ErrorCodes.SshRoleNotFound, failure.Code);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    private static MockResponse Json(int status, string body) => new(status, Body: Compact(body));

    private static string Compact(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    private static string CaBody() => Compact(
        """{"data":{"public_key":"ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIFAKECAPUBLICKEY","algorithm":""}}""");

    // Reused verbatim from specifications/fixtures/ssh/ssh.sign.json (TRN-031).
    private static string SignedCertBody() => Compact("""
        {
          "data": {
            "signed_key": "ssh-ed25519-cert-v01@openssh.com AAAAHHNzaC1lZDI1NTE5LWNlcnQtdjAxQG9wZW5zc2guY29tFAKESIGNEDCERT user@example.com",
            "serial_number": "42",
            "algorithm": "ssh-ed25519"
          }
        }
        """);

    private static string CredsBody() => Compact("""
        {"data":{"key":"a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4","key_type":"otp","username":"ubuntu","ip":"10.0.0.12","port":22}}
        """);

    private static string VerifyBody() => Compact(
        """{"data":{"username":"ubuntu","ip":"10.0.0.12","role_name":"bastion-hosts","port":22}}""");
}
