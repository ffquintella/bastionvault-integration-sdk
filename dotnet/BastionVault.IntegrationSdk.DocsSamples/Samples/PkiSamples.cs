using System.Text.Json;
using BastionVault.IntegrationSdk.DocsSamples.Infrastructure;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.DocsSamples.Samples;

/// <summary>
/// Every executed sample shown by <c>docs/dotnet/engines/pki.md</c> (D6), built from
/// <c>specifications/17-usage-guides.md</c> guide 12 (the certificate half) and
/// <c>09-pki-engine.md</c>. Reuses <see cref="MockVaultFixture"/>'s server: routes are bound per
/// test on <see cref="MockVaultFixture.Server"/> and cleared after, so no test leaks a route to
/// another.
/// </summary>
public sealed class PkiSamples : IClassFixture<MockVaultFixture>
{
    private const string RootRoute = "/v1/pki/root/generate/internal";
    private const string RoleRoute = "/v1/pki/roles/web";
    private const string IssueRoute = "/v1/pki/issue/web";
    private const string CertsInfoRoute = "/v2/pki/certs-info";
    private const string RevokeRoute = "/v1/pki/revoke";
    private const string CsrGenerateRoute = "/v1/pki/csr/generate";
    private const string SignRequestImportRoute = "/v1/pki/sign-request/import";
    private const string SignRequestApproveRoute = "/v1/pki/sign-request/sr-1/approve";

    private readonly MockVaultFixture vault;

    public PkiSamples(MockVaultFixture vault)
    {
        this.vault = vault;
    }

    [Fact]
    public void The_policy_this_guide_needs_is_the_policy_PolicyBuilder_builds()
    {
        // docs:begin pki/policy
        string hcl = new PolicyBuilder()
            .AddPath("pki/root/generate/internal", [Capability.Create, Capability.Update])
            .AddPath("pki/roles/web", [Capability.Create, Capability.Read, Capability.Update])
            .AddPath("pki/issue/web", [Capability.Update])
            .AddPath("pki/certs-info", [Capability.Read])
            .AddPath("pki/revoke", [Capability.Update])
            .AddPath("pki/sign-request/*", [Capability.Read, Capability.Update, Capability.List])
            .Build();

        Console.WriteLine(hcl);
        // docs:end pki/policy

        Assert.Equal(PolicyShownInTheGuide(), hcl.Trim());
    }

    private static string PolicyShownInTheGuide()
    {
        string path = Path.Combine(DocsRepository.Docs.FullName, "dotnet", "engines", "pki.md");
        string[] lines = File.ReadAllLines(path);
        int opening = Array.FindIndex(lines, line => line.TrimEnd() == "```hcl");
        Assert.True(opening >= 0, $"{path} shows no ```hcl policy block (DOC-011).");
        int closing = Array.FindIndex(lines, opening + 1, line => line.TrimEnd() == "```");
        Assert.True(closing > opening, $"{path}: the ```hcl block is never closed.");
        return string.Join('\n', lines[(opening + 1)..closing]).Trim();
    }

    [Fact]
    public async Task Step_1_generate_a_root_ca_and_define_a_role()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(RootRoute, Json(200, RootBody()));
        vault.Server.SetRouteResponse(RoleRoute, Json(200, "{}"));

        // docs:begin pki/root-and-role
        PkiRootCertificate root = await client.Pki.GenerateRootAsync(
            PkiKeyGenerationType.Internal,
            new PkiRootSpec { CommonName = "Corp Root", KeyType = "ec", Ttl = TimeSpan.FromHours(87600) });
        Console.WriteLine($"root issuer {root.IssuerId}, expires {root.Expiration:O}");
        // PrivateKey is null here: Internal never returns key material (PKI-002).

        await client.Pki.WriteRoleAsync("web", new PkiRole
        {
            AllowedDomains = ["example.com"],
            AllowSubdomains = true,
            MaxTtl = TimeSpan.FromHours(720),
        });
        // docs:end pki/root-and-role

        Assert.Equal("issuer-1", root.IssuerId);
        Assert.Null(root.PrivateKey);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task Step_2_issue_a_certificate_then_list_certificates_in_bulk()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(IssueRoute, Json(200, IssueBody()));
        vault.Server.SetRouteResponse(CertsInfoRoute, Json(200, CertsInfoPageBody()));

        try
        {
            // docs:begin pki/issue-and-list
            // PKI-011: an empty CommonName is refused client-side, before any request is sent.
            IssuedCertificate cert = await client.Pki.IssueAsync("web", new IssueRequest
            {
                CommonName = "api.example.com",
                AltNames = ["www.example.com"],
                Ttl = TimeSpan.FromHours(72),
            });
            File.WriteAllText("tls.crt", cert.Certificate);
            File.WriteAllText("tls.key", cert.PrivateKey!.Reveal()!);

            // One request, however many certificates exist - never `for serial in list: await ReadCertificate(serial)`.
            Page<CertificateSummary> page = await client.Pki.ListCertificatesInfoAsync(limit: 100);
            foreach (CertificateSummary summary in page.Records)
            {
                Console.WriteLine($"{summary.SerialNumber}: {summary.CommonName}, orphaned={summary.IsOrphaned}");
            }
            // docs:end pki/issue-and-list

            Assert.Equal("aa:bb:cc:dd:ee:ff", cert.SerialNumber);
            Assert.Equal(2, page.Records.Count);
            Assert.True(page.Truncated);
        }
        finally
        {
            File.Delete("tls.crt");
            File.Delete("tls.key");
        }

        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task Step_3_revoke_a_certificate_and_route_a_csr_through_approval()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(RevokeRoute, Json(200, "{}"));
        vault.Server.SetRouteResponse(CsrGenerateRoute, Json(200, CsrBody()));
        vault.Server.SetRouteResponse(SignRequestImportRoute, Json(200, """{"data":{"id":"sr-1","status":"pending"}}"""));
        vault.Server.SetRouteResponse(SignRequestApproveRoute, Json(200, """{"data":{"status":"approved"}}"""));

        // docs:begin pki/revoke-and-approve
        await client.Pki.RevokeAsync("aa:bb:cc:dd:ee:ff");

        // The outbound queue: an externally-held key signs a CSR this SDK only generates and tracks.
        PkiGeneratedCsr queued = await client.Pki.Csr.GenerateAsync(role: "web", commonName: "batch.example.com");
        Console.WriteLine($"csr queued: {queued.Csr.Length} bytes");

        // The inbound queue: a CSR someone else generated, awaiting approval against a role.
        IReadOnlyDictionary<string, JsonElement>? imported =
            await client.Pki.SignRequests.ImportAsync(csr: queued.Csr, requester: "batch-job");
        string requestId = imported!["id"].GetString()!;
        await client.Pki.SignRequests.ApproveAsync(requestId, "web");

        // PKI-030: an empty reason is refused client-side; the alternative path if you reject instead.
        try
        {
            await client.Pki.SignRequests.RejectAsync(requestId, string.Empty);
        }
        catch (BastionVaultException e) when (e.Code == ErrorCodes.InputInvalidArgument)
        {
            Console.WriteLine("a rejection always needs a reason");
        }
        // docs:end pki/revoke-and-approve

        Assert.Equal("sr-1", requestId);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task The_whole_program_generates_issues_and_revokes()
    {
        vault.Server.ClearRouteResponses();

        // docs:begin pki/complete
        using BastionVaultClient client = new();

        try
        {
            vault.Server.SetRouteResponse(RootRoute, Json(200, RootBody()));
            PkiRootCertificate root = await client.Pki.GenerateRootAsync(
                PkiKeyGenerationType.Internal, new PkiRootSpec { CommonName = "Corp Root", KeyType = "ec" });
            Console.WriteLine($"root issuer {root.IssuerId}");

            vault.Server.SetRouteResponse(RoleRoute, Json(200, "{}"));
            await client.Pki.WriteRoleAsync("web", new PkiRole { AllowedDomains = ["example.com"], AllowSubdomains = true });

            vault.Server.SetRouteResponse(IssueRoute, Json(200, IssueBody()));
            IssuedCertificate cert = await client.Pki.IssueAsync("web", new IssueRequest { CommonName = "api.example.com" });
            Console.WriteLine($"issued {cert.SerialNumber}");

            vault.Server.SetRouteResponse(RevokeRoute, Json(200, "{}"));
            await client.Pki.RevokeAsync(cert.SerialNumber);
            Console.WriteLine("revoked");
        }
        catch (BastionVaultException e)
        {
            Console.Error.WriteLine($"{e.Code}: {e.Message} ({e.Hint}); retryable: {e.Retryable}");
            throw;
        }
        // docs:end pki/complete

        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task What_can_go_wrong_maps_every_pki_code_to_a_remedy()
    {
        vault.Server.ClearRouteResponses();
        vault.Server.SetRouteResponse(
            "/v1/pki/issue/missing-role", Json(500, """{"error":"PKI role is not found."}"""));
        using BastionVaultClient client = vault.CreateClient();

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(async () =>
        {
            // docs:begin pki/handling-errors
            try
            {
                await client.Pki.IssueAsync("missing-role", new IssueRequest { CommonName = "x.example.com" });
            }
            catch (BastionVaultException e)
            {
                string remedy = e.Code switch
                {
                    ErrorCodes.PkiRoleNotFound => "check the role name, or write it first",
                    ErrorCodes.PkiCertificateNotFound => "check the serial number",
                    ErrorCodes.PkiCaNotConfigured => "generate or configure a CA on this mount first",
                    ErrorCodes.PkiInvalidCaMaterial => "check the PEM bundle or chain you supplied",
                    ErrorCodes.ConflictKeyNameExists => "pick a different managed-key name",
                    ErrorCodes.InputInvalidArgument => "a required field was empty; see the exception message",
                    _ => "look the code up in the error reference",
                };
                Console.Error.WriteLine($"{e.Code}: {e.Message} - {remedy}");
                throw;
            }
            // docs:end pki/handling-errors
        });

        Assert.Equal(ErrorCodes.PkiRoleNotFound, failure.Code);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    private static MockResponse Json(int status, string body) => new(status, Body: Compact(body));

    private static string Compact(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    private static string RootBody() => Compact("""
        {
          "data": {
            "certificate": "-----BEGIN CERTIFICATE-----\nMIIBFAKEROOTCA\n-----END CERTIFICATE-----\n",
            "issuing_ca": "-----BEGIN CERTIFICATE-----\nMIIBFAKEROOTCA\n-----END CERTIFICATE-----\n",
            "issuer_id": "issuer-1",
            "issuer_name": "Corp Root",
            "expiration": "2036-09-13T10:00:00Z"
          }
        }
        """);

    private static string IssueBody() => Compact("""
        {
          "data": {
            "certificate": "-----BEGIN CERTIFICATE-----\nMIIBFAKECERTDATA\n-----END CERTIFICATE-----\n",
            "issuing_ca": "-----BEGIN CERTIFICATE-----\nMIIBFAKEISSUINGCA\n-----END CERTIFICATE-----\n",
            "ca_chain": [
              "-----BEGIN CERTIFICATE-----\nMIIBFAKEISSUINGCA\n-----END CERTIFICATE-----\n",
              "-----BEGIN CERTIFICATE-----\nMIIBFAKEROOTCA\n-----END CERTIFICATE-----\n"
            ],
            "private_key": "-----BEGIN PRIVATE KEY-----\nMIIBFAKEPRIVATEKEY\n-----END PRIVATE KEY-----\n",
            "private_key_type": "ec",
            "serial_number": "aa:bb:cc:dd:ee:ff",
            "issuer_id": "issuer-1",
            "key_id": "key-1"
          }
        }
        """);

    // Reused verbatim from specifications/fixtures/pki/pki.certs-info-page.json (PAG-003/PAG-005/PKI-020).
    private static string CertsInfoPageBody() => Compact("""
        {
          "data": {
            "keys": ["aabbcc", "ddeeff"],
            "records": [
              {"serial_number": "aa:bb:cc", "issued_at": "2026-01-01T00:00:00Z", "not_after": "2027-01-01T00:00:00Z", "issuer_id": "issuer-1", "is_orphaned": false, "source": "issued", "key_id": "key-1", "common_name": "one.example.com", "issuer_dn": "CN=Example Root"},
              {"serial_number": "dd:ee:ff", "issued_at": "2026-01-02T00:00:00Z", "not_after": "2027-01-02T00:00:00Z", "issuer_id": "issuer-1", "is_orphaned": false, "source": "issued", "key_id": "key-2", "common_name": "two.example.com", "issuer_dn": "CN=Example Root"}
            ],
            "total": 5,
            "next": "ddeeff",
            "truncated": true
          }
        }
        """);

    private static string CsrBody() => Compact("""
        {"data":{"csr":"-----BEGIN CERTIFICATE REQUEST-----\nMIIBFAKECSRDATA\n-----END CERTIFICATE REQUEST-----\n"}}
        """);
}
