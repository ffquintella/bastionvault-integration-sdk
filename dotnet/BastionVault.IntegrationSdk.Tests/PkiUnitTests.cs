using System.Text;
using BastionVault.IntegrationSdk.Testing;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// 09 — PKI engine (M9 slice a, DR-0016): roles, issuance, certificates and the CRL. The wire-shape
/// assertions for a real request/response round trip live in the Appendix C <c>pki.*</c> fixtures
/// (<see cref="PkiFixturesTests"/>); this file covers what a single-request fixture cannot — every
/// argument-validation branch, every optional field, and every failure path.
/// </summary>
public sealed class PkiUnitTests
{
    private const string Address = "https://vault.example.com:8200";

    // ---------------------------------------------------------------- roles

    [Fact]
    [Requirement("PKI-020")]
    [Trait("Requirement", "PKI-020")]
    public async Task ListRoles_returns_the_wire_list_and_an_empty_list_on_404()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"keys":["web-server","client-cert"]}}"""));
        BastionVaultClient client = BuildClient(transport);

        IReadOnlyList<string> roles = await client.Pki.ListRolesAsync();

        Assert.Equal(["web-server", "client-cert"], roles);
        Assert.Equal("LIST", transport.Requests[0].Method);
        Assert.EndsWith("/v1/pki/roles/", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport emptyTransport = new();
        emptyTransport.EnqueueResponse(404);
        BastionVaultClient emptyClient = BuildClient(emptyTransport);
        Assert.Empty(await emptyClient.Pki.ListRolesAsync());
    }

    [Fact]
    [Requirement("PKI-010")]
    [Trait("Requirement", "PKI-010")]
    public async Task WriteRole_serialises_every_field_when_set()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200);
        BastionVaultClient client = BuildClient(transport);

        PkiRole role = new()
        {
            Ttl = TimeSpan.FromHours(1),
            MaxTtl = TimeSpan.FromHours(2),
            KeyType = "ec",
            KeyBits = 256,
            SignatureBits = 384,
            AllowLocalhost = true,
            AllowAnyName = false,
            AllowIpSans = true,
            AllowSubdomains = true,
            AllowBareDomains = false,
            AllowedDomains = ["example.com", "example.org"],
            AllowGlobDomains = true,
            ServerFlag = true,
            ClientFlag = false,
            UseCsrSans = true,
            UseCsrCommonName = false,
            KeyUsage = ["DigitalSignature", "KeyEncipherment"],
            ExtKeyUsage = ["ServerAuth"],
            ExtKeyUsageOids = ["1.2.3.4"],
            Country = "US",
            Province = "CA",
            Locality = "SF",
            Organization = "Example Inc",
            Ou = "Eng",
            NoStore = false,
            GenerateLease = true,
            NotBeforeDuration = TimeSpan.FromSeconds(30),
            IssuerRef = "issuer-1",
            AllowKeyReuse = true,
            AllowedKeyRefs = ["key-1", "key-2"],
            AcmeEnabled = true,
            AllowUpnSans = true,
            AllowedUpnDomains = ["example.com"],
            AllowEmailSans = true,
            AllowedEmailDomains = ["example.com"],
            AllowAdSid = true,
            AdSid = "S-1-5-21",
        };

        await client.Pki.WriteRoleAsync("web-server", role);

        Assert.Equal("POST", transport.Requests[0].Method);
        Assert.EndsWith("/v1/pki/roles/web-server", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains("\"ttl\":3600", body, StringComparison.Ordinal);
        Assert.Contains("\"max_ttl\":7200", body, StringComparison.Ordinal);
        Assert.Contains("\"key_type\":\"ec\"", body, StringComparison.Ordinal);
        Assert.Contains("\"key_bits\":256", body, StringComparison.Ordinal);
        Assert.Contains("\"signature_bits\":384", body, StringComparison.Ordinal);
        Assert.Contains("\"allow_localhost\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"allow_any_name\":false", body, StringComparison.Ordinal);
        Assert.Contains("\"allowed_domains\":\"example.com,example.org\"", body, StringComparison.Ordinal);
        Assert.Contains("\"key_usage\":\"DigitalSignature,KeyEncipherment\"", body, StringComparison.Ordinal);
        Assert.Contains("\"ext_key_usage\":\"ServerAuth\"", body, StringComparison.Ordinal);
        Assert.Contains("\"ext_key_usage_oids\":\"1.2.3.4\"", body, StringComparison.Ordinal);
        Assert.Contains("\"country\":\"US\"", body, StringComparison.Ordinal);
        Assert.Contains("\"province\":\"CA\"", body, StringComparison.Ordinal);
        Assert.Contains("\"locality\":\"SF\"", body, StringComparison.Ordinal);
        Assert.Contains("\"organization\":\"Example Inc\"", body, StringComparison.Ordinal);
        Assert.Contains("\"ou\":\"Eng\"", body, StringComparison.Ordinal);
        Assert.Contains("\"not_before_duration\":30", body, StringComparison.Ordinal);
        Assert.Contains("\"issuer_ref\":\"issuer-1\"", body, StringComparison.Ordinal);
        Assert.Contains("\"allowed_key_refs\":\"key-1,key-2\"", body, StringComparison.Ordinal);
        Assert.Contains("\"acme_enabled\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"allowed_upn_domains\":\"example.com\"", body, StringComparison.Ordinal);
        Assert.Contains("\"allowed_email_domains\":\"example.com\"", body, StringComparison.Ordinal);
        Assert.Contains("\"ad_sid\":\"S-1-5-21\"", body, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("PKI-010")]
    [Trait("Requirement", "PKI-010")]
    public async Task WriteRole_omits_every_unset_field()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200);
        BastionVaultClient client = BuildClient(transport);

        await client.Pki.WriteRoleAsync("web-server", new PkiRole());

        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Equal("{}", body);
    }

    [Fact]
    [Requirement("PKI-010")]
    [Trait("Requirement", "PKI-010")]
    public async Task ReadRole_reads_every_field_and_splits_csv_fields_back_into_lists()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""
        {
          "data": {
            "ttl": 3600,
            "max_ttl": 7200,
            "key_type": "ec",
            "key_bits": 256,
            "signature_bits": 384,
            "allow_localhost": true,
            "allow_any_name": false,
            "allow_ip_sans": true,
            "allow_subdomains": true,
            "allow_bare_domains": false,
            "allowed_domains": "example.com,example.org",
            "allow_glob_domains": true,
            "server_flag": true,
            "client_flag": false,
            "use_csr_sans": true,
            "use_csr_common_name": false,
            "key_usage": "DigitalSignature,KeyEncipherment",
            "ext_key_usage": "ServerAuth",
            "ext_key_usage_oids": "1.2.3.4",
            "country": "US",
            "province": "CA",
            "locality": "SF",
            "organization": "Example Inc",
            "ou": "Eng",
            "no_store": false,
            "generate_lease": true,
            "not_before_duration": 30,
            "issuer_ref": "issuer-1",
            "allow_key_reuse": true,
            "allowed_key_refs": "key-1,key-2",
            "acme_enabled": true,
            "allow_upn_sans": true,
            "allowed_upn_domains": "example.com",
            "allow_email_sans": true,
            "allowed_email_domains": "example.com",
            "allow_ad_sid": true,
            "ad_sid": "S-1-5-21"
          }
        }
        """));
        BastionVaultClient client = BuildClient(transport);

        PkiRole? role = await client.Pki.ReadRoleAsync("web-server");

        Assert.NotNull(role);
        Assert.Equal(TimeSpan.FromSeconds(3600), role!.Ttl);
        Assert.Equal(TimeSpan.FromSeconds(7200), role.MaxTtl);
        Assert.Equal("ec", role.KeyType);
        Assert.Equal(256, role.KeyBits);
        Assert.Equal(384, role.SignatureBits);
        Assert.True(role.AllowLocalhost);
        Assert.False(role.AllowAnyName);
        Assert.Equal(["example.com", "example.org"], role.AllowedDomains);
        Assert.Equal(["DigitalSignature", "KeyEncipherment"], role.KeyUsage);
        Assert.Equal(["ServerAuth"], role.ExtKeyUsage);
        Assert.Equal(["1.2.3.4"], role.ExtKeyUsageOids);
        Assert.Equal("US", role.Country);
        Assert.Equal("CA", role.Province);
        Assert.Equal("SF", role.Locality);
        Assert.Equal("Example Inc", role.Organization);
        Assert.Equal("Eng", role.Ou);
        Assert.Equal(TimeSpan.FromSeconds(30), role.NotBeforeDuration);
        Assert.Equal("issuer-1", role.IssuerRef);
        Assert.Equal(["key-1", "key-2"], role.AllowedKeyRefs);
        Assert.True(role.AcmeEnabled);
        Assert.Equal(["example.com"], role.AllowedUpnDomains);
        Assert.Equal(["example.com"], role.AllowedEmailDomains);
        Assert.True(role.AllowAdSid);
        Assert.Equal("S-1-5-21", role.AdSid);

        FakeTransport missingTransport = new();
        missingTransport.EnqueueResponse(404);
        BastionVaultClient missingClient = BuildClient(missingTransport);
        Assert.Null(await missingClient.Pki.ReadRoleAsync("ghost"));
    }

    [Fact]
    [Requirement("PKI-010")]
    [Trait("Requirement", "PKI-010")]
    public async Task ReadRole_leaves_absent_csv_fields_null_so_a_read_modify_write_does_not_clear_them()
    {
        // F3 (M9 slice a handback): PkiRole is patch-shaped (OVR-007) — an absent field must
        // round-trip as null, not [], or a read-modify-write sends `"allowed_domains":""` for a
        // field the server never returned, clearing it instead of leaving it alone.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{}}"""));
        BastionVaultClient client = BuildClient(transport);

        PkiRole? role = await client.Pki.ReadRoleAsync("web-server");

        Assert.NotNull(role);
        Assert.Null(role!.AllowedDomains);
        Assert.Null(role.KeyUsage);
        Assert.Null(role.ExtKeyUsage);
        Assert.Null(role.ExtKeyUsageOids);
        Assert.Null(role.AllowedKeyRefs);
        Assert.Null(role.AllowedUpnDomains);
        Assert.Null(role.AllowedEmailDomains);
        Assert.Null(role.Ttl);
        Assert.Null(role.KeyType);
    }

    [Fact]
    [Requirement("PKI-010")]
    [Trait("Requirement", "PKI-010")]
    public async Task ReadRole_reads_a_present_but_empty_csv_field_as_an_empty_list_not_null()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"allowed_domains":""}}"""));
        BastionVaultClient client = BuildClient(transport);

        PkiRole? role = await client.Pki.ReadRoleAsync("web-server");

        Assert.NotNull(role);
        Assert.NotNull(role!.AllowedDomains);
        Assert.Empty(role.AllowedDomains!);
    }

    [Fact]
    [Requirement("PKI-010")]
    [Trait("Requirement", "PKI-010")]
    public async Task ReadRole_then_WriteRole_does_not_clear_a_field_the_server_never_returned()
    {
        // F3's actual failure scenario (M9 slice a handback, second round): the defect lived in
        // read-modify-write, not in either half alone. A role read back from a server that never
        // set allowed_domains, then written straight back unmodified, must not emit
        // "allowed_domains":"" — that would clear a field the caller never touched.
        FakeTransport readTransport = new();
        readTransport.EnqueueResponse(200, body: Json("""{"data":{}}"""));
        BastionVaultClient readClient = BuildClient(readTransport);
        PkiRole? role = await readClient.Pki.ReadRoleAsync("web-server");
        Assert.NotNull(role);

        FakeTransport writeTransport = new();
        writeTransport.EnqueueResponse(200);
        BastionVaultClient writeClient = BuildClient(writeTransport);
        await writeClient.Pki.WriteRoleAsync("web-server", role!);

        string body = Encoding.UTF8.GetString(writeTransport.Requests[0].Body.Span);
        Assert.DoesNotContain("allowed_domains", body, StringComparison.Ordinal);
        Assert.DoesNotContain("key_usage", body, StringComparison.Ordinal);
        Assert.Equal("{}", body);
    }

    [Fact]
    [Requirement("PKI-001")]
    [Trait("Requirement", "PKI-001")]
    public async Task DeleteRole_sends_a_delete_to_the_role_path()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        await client.Pki.DeleteRoleAsync("web-server");

        Assert.Equal("DELETE", transport.Requests[0].Method);
        Assert.EndsWith("/v1/pki/roles/web-server", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- issuance

    [Fact]
    [Requirement("PKI-011")]
    [Trait("Requirement", "PKI-011")]
    public async Task Issue_rejects_an_empty_common_name_client_side_with_no_request_sent()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Pki.IssueAsync("web-server", new IssueRequest { CommonName = string.Empty }));

        Assert.Equal(ErrorCodes.InputInvalidArgument, exception.Code);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    [Requirement("PKI-001")]
    [Requirement("PKI-002")]
    [Requirement("PKI-010")]
    [Requirement("PKI-020")]
    [Trait("Requirement", "PKI-001")]
    public async Task Issue_returns_PEM_fields_verbatim_and_redacts_the_generated_private_key()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""
        {
          "data": {
            "certificate": "-----BEGIN CERTIFICATE-----\nAAA\n-----END CERTIFICATE-----\n",
            "issuing_ca": "-----BEGIN CERTIFICATE-----\nBBB\n-----END CERTIFICATE-----\n",
            "ca_chain": ["-----BEGIN CERTIFICATE-----\nBBB\n-----END CERTIFICATE-----\n"],
            "private_key": "-----BEGIN PRIVATE KEY-----\nCCC\n-----END PRIVATE KEY-----\n",
            "private_key_type": "ec",
            "serial_number": "aa:bb:cc",
            "issuer_id": "issuer-1",
            "key_id": "key-1"
          }
        }
        """));
        BastionVaultClient client = BuildClient(transport);

        IssuedCertificate issued = await client.Pki.IssueAsync(
            "web-server",
            new IssueRequest
            {
                CommonName = "example.com",
                AltNames = ["www.example.com"],
                IpSans = ["10.0.0.1"],
                Ttl = TimeSpan.FromHours(1),
                IssuerRef = "issuer-1",
                KeyRef = "key-1",
                UpnSans = ["alice@example.com"],
                EmailSans = ["alice@example.com"],
                AdSid = "S-1-5-21",
            });

        Assert.Equal("-----BEGIN CERTIFICATE-----\nAAA\n-----END CERTIFICATE-----\n", issued.Certificate);
        Assert.Equal("-----BEGIN CERTIFICATE-----\nBBB\n-----END CERTIFICATE-----\n", issued.IssuingCa);
        _ = Assert.Single(issued.CaChain);
        Assert.NotNull(issued.PrivateKey);
        Assert.Equal("[REDACTED]", issued.PrivateKey!.ToString());
        Assert.Equal("-----BEGIN PRIVATE KEY-----\nCCC\n-----END PRIVATE KEY-----\n", issued.PrivateKey.Reveal());
        Assert.Equal("ec", issued.PrivateKeyType);
        Assert.Equal("aa:bb:cc", issued.SerialNumber);
        Assert.Equal("issuer-1", issued.IssuerId);
        Assert.Equal("key-1", issued.KeyId);

        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains("\"common_name\":\"example.com\"", body, StringComparison.Ordinal);
        Assert.Contains("\"alt_names\":\"www.example.com\"", body, StringComparison.Ordinal);
        Assert.Contains("\"ip_sans\":\"10.0.0.1\"", body, StringComparison.Ordinal);
        Assert.Contains("\"ttl\":3600", body, StringComparison.Ordinal);
        Assert.Contains("\"issuer_ref\":\"issuer-1\"", body, StringComparison.Ordinal);
        Assert.Contains("\"key_ref\":\"key-1\"", body, StringComparison.Ordinal);
        Assert.Contains("\"upn_sans\":\"alice@example.com\"", body, StringComparison.Ordinal);
        Assert.Contains("\"email_sans\":\"alice@example.com\"", body, StringComparison.Ordinal);
        Assert.Contains("\"ad_sid\":\"S-1-5-21\"", body, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("PKI-002")]
    [Trait("Requirement", "PKI-002")]
    public async Task Issue_leaves_private_key_absent_when_the_server_did_not_generate_one()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""
        {
          "data": {
            "certificate": "-----BEGIN CERTIFICATE-----\nAAA\n-----END CERTIFICATE-----\n",
            "issuing_ca": "-----BEGIN CERTIFICATE-----\nBBB\n-----END CERTIFICATE-----\n",
            "serial_number": "aa:bb:cc",
            "issuer_id": "issuer-1"
          }
        }
        """));
        BastionVaultClient client = BuildClient(transport);

        IssuedCertificate issued = await client.Pki.IssueAsync("web-server", new IssueRequest { CommonName = "example.com" });

        Assert.Null(issued.PrivateKey);
        Assert.Null(issued.PrivateKeyType);
        Assert.Null(issued.KeyId);
        Assert.Empty(issued.CaChain);
    }

    [Theory]
    [Requirement("PKI-001")]
    [Trait("Requirement", "PKI-001")]
    [InlineData("""{"data":{"issuing_ca":"x","serial_number":"aa","issuer_id":"i"}}""")]
    [InlineData("""{"data":{"certificate":"x","serial_number":"aa","issuer_id":"i"}}""")]
    [InlineData("""{"data":{"certificate":"x","issuing_ca":"x","issuer_id":"i"}}""")]
    [InlineData("""{"data":{"certificate":"x","issuing_ca":"x","serial_number":"aa"}}""")]
    public async Task Issue_raises_a_protocol_error_when_a_required_field_is_missing(string body)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(body));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Pki.IssueAsync("web-server", new IssueRequest { CommonName = "example.com" }));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    [Requirement("PKI-001")]
    [Trait("Requirement", "PKI-001")]
    public async Task Sign_sends_the_csr_and_every_override_and_returns_PEM_fields_verbatim()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""
        {
          "data": {
            "certificate": "-----BEGIN CERTIFICATE-----\nAAA\n-----END CERTIFICATE-----\n",
            "issuing_ca": "-----BEGIN CERTIFICATE-----\nBBB\n-----END CERTIFICATE-----\n",
            "ca_chain": ["-----BEGIN CERTIFICATE-----\nBBB\n-----END CERTIFICATE-----\n"],
            "serial_number": "aa:bb:cc",
            "issuer_id": "issuer-1",
            "key_id": "key-1"
          }
        }
        """));
        BastionVaultClient client = BuildClient(transport);

        SignedCertificate signed = await client.Pki.SignAsync(
            "web-server",
            new SignRequest
            {
                Csr = "-----BEGIN CERTIFICATE REQUEST-----\nZZZ\n-----END CERTIFICATE REQUEST-----\n",
                CommonName = "example.com",
                AltNames = ["www.example.com"],
                IpSans = ["10.0.0.1"],
                Ttl = TimeSpan.FromHours(1),
                IssuerRef = "issuer-1",
                // D-M9-17: SignRequest carries IssueRequest's complete named set, not a subset.
                KeyRef = "key-1",
                UpnSans = ["alice@example.com"],
                EmailSans = ["alice@example.com"],
                AdSid = "S-1-5-21",
            });

        Assert.Equal("-----BEGIN CERTIFICATE-----\nAAA\n-----END CERTIFICATE-----\n", signed.Certificate);
        Assert.Equal("key-1", signed.KeyId);
        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains("\"csr\":\"-----BEGIN CERTIFICATE REQUEST-----", body, StringComparison.Ordinal);
        Assert.Contains("\"common_name\":\"example.com\"", body, StringComparison.Ordinal);
        Assert.Contains("\"alt_names\":\"www.example.com\"", body, StringComparison.Ordinal);
        Assert.Contains("\"ip_sans\":\"10.0.0.1\"", body, StringComparison.Ordinal);
        Assert.Contains("\"ttl\":3600", body, StringComparison.Ordinal);
        Assert.Contains("\"issuer_ref\":\"issuer-1\"", body, StringComparison.Ordinal);
        Assert.Contains("\"key_ref\":\"key-1\"", body, StringComparison.Ordinal);
        Assert.Contains("\"upn_sans\":\"alice@example.com\"", body, StringComparison.Ordinal);
        Assert.Contains("\"email_sans\":\"alice@example.com\"", body, StringComparison.Ordinal);
        Assert.Contains("\"ad_sid\":\"S-1-5-21\"", body, StringComparison.Ordinal);
        Assert.EndsWith("/v1/pki/sign/web-server", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("PKI-001")]
    [Trait("Requirement", "PKI-001")]
    public async Task Sign_rejects_an_empty_csr_client_side()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        _ = await Assert.ThrowsAsync<ArgumentException>(
            () => client.Pki.SignAsync("web-server", new SignRequest { Csr = string.Empty }));

        Assert.Empty(transport.Requests);
    }

    [Fact]
    [Requirement("PKI-001")]
    [Trait("Requirement", "PKI-001")]
    public async Task Sign_omits_every_unset_override()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""
        {"data":{"certificate":"x","issuing_ca":"x","serial_number":"aa","issuer_id":"i"}}
        """));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Pki.SignAsync("web-server", new SignRequest { Csr = "csr-body" });

        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Equal("{\"csr\":\"csr-body\"}", body);
    }

    [Fact]
    [Requirement("PKI-001")]
    [Trait("Requirement", "PKI-001")]
    public async Task SignVerbatim_sends_ttl_and_issuer_ref_when_given_and_omits_them_when_not()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""
        {"data":{"certificate":"x","issuing_ca":"x","serial_number":"aa","issuer_id":"i"}}
        """));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Pki.SignVerbatimAsync("csr-body", TimeSpan.FromMinutes(30), "issuer-1");

        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains("\"csr\":\"csr-body\"", body, StringComparison.Ordinal);
        Assert.Contains("\"ttl\":1800", body, StringComparison.Ordinal);
        Assert.Contains("\"issuer_ref\":\"issuer-1\"", body, StringComparison.Ordinal);
        Assert.EndsWith("/v1/pki/sign-verbatim", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport bareTransport = new();
        bareTransport.EnqueueResponse(200, body: Json("""
        {"data":{"certificate":"x","issuing_ca":"x","serial_number":"aa","issuer_id":"i"}}
        """));
        BastionVaultClient bareClient = BuildClient(bareTransport);
        _ = await bareClient.Pki.SignVerbatimAsync("csr-body");
        Assert.Equal("{\"csr\":\"csr-body\"}", Encoding.UTF8.GetString(bareTransport.Requests[0].Body.Span));
    }

    // ---------------------------------------------------------------- certificates

    [Fact]
    [Requirement("PKI-020")]
    [Trait("Requirement", "PKI-020")]
    public async Task ListCertificates_returns_the_wire_list_and_an_empty_list_on_404()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"keys":["aa:bb:cc"]}}"""));
        BastionVaultClient client = BuildClient(transport);

        Assert.Equal(["aa:bb:cc"], await client.Pki.ListCertificatesAsync());

        FakeTransport emptyTransport = new();
        emptyTransport.EnqueueResponse(404);
        BastionVaultClient emptyClient = BuildClient(emptyTransport);
        Assert.Empty(await emptyClient.Pki.ListCertificatesAsync());
    }

    [Fact]
    [Requirement("PKI-020")]
    [Trait("Requirement", "PKI-020")]
    public async Task ReadCertificate_reads_every_field_sends_the_serial_verbatim_and_returns_null_on_404()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""
        {
          "data": {
            "certificate": "-----BEGIN CERTIFICATE-----\nAAA\n-----END CERTIFICATE-----\n",
            "serial_number": "aa:bb:cc",
            "issued_at": "2026-01-01T00:00:00Z",
            "not_after": "2027-01-01T00:00:00Z",
            "issuer_id": "issuer-1",
            "is_orphaned": false,
            "source": "issued",
            "revoked_at": "2026-06-01T00:00:00Z",
            "key_id": "key-1",
            "key_name": "my-key"
          }
        }
        """));
        BastionVaultClient client = BuildClient(transport);

        CertificateRecord? record = await client.Pki.ReadCertificateAsync("aa:bb:cc");

        Assert.NotNull(record);
        Assert.Equal("aa:bb:cc", record!.SerialNumber);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), record.IssuedAt);
        _ = Assert.NotNull(record.NotAfter);
        Assert.Equal("issuer-1", record.IssuerId);
        Assert.False(record.IsOrphaned);
        Assert.Equal("issued", record.Source);
        _ = Assert.NotNull(record.RevokedAt);
        Assert.Equal("key-1", record.KeyId);
        Assert.Equal("my-key", record.KeyName);
        Assert.EndsWith("/v1/pki/cert/aa:bb:cc", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport missingTransport = new();
        missingTransport.EnqueueResponse(404);
        BastionVaultClient missingClient = BuildClient(missingTransport);
        Assert.Null(await missingClient.Pki.ReadCertificateAsync("aabbcc"));
    }

    [Fact]
    [Requirement("PKI-020")]
    [Trait("Requirement", "PKI-020")]
    public async Task ReadCertificate_accepts_issued_at_not_after_and_revoked_at_as_bare_epoch_numbers()
    {
        // DR-0021 F10: PKI reads every timestamp through KvWire.RequireInstant /
        // ReadOptionalInstant, the same helpers Transit's F8a fix widened. This is the regression
        // guard that proves the tolerance reached PKI, not only KV.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"certificate":"cert","serial_number":"aa:bb:cc","issued_at":1767225600,"not_after":1798761600,"revoked_at":1782950400}}"""));
        BastionVaultClient client = BuildClient(transport);

        CertificateRecord? record = await client.Pki.ReadCertificateAsync("aa:bb:cc");

        Assert.NotNull(record);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1767225600), record!.IssuedAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1798761600), record.NotAfter);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1782950400), record.RevokedAt);
    }

    [Fact]
    [Requirement("PKI-001")]
    [Trait("Requirement", "PKI-001")]
    public async Task ReadCertificate_leaves_optional_fields_null_when_absent()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""
        {"data":{"certificate":"x","serial_number":"aa","issued_at":"2026-01-01T00:00:00Z"}}
        """));
        BastionVaultClient client = BuildClient(transport);

        CertificateRecord? record = await client.Pki.ReadCertificateAsync("aa");

        Assert.NotNull(record);
        Assert.Null(record!.NotAfter);
        Assert.Null(record.IssuerId);
        Assert.Null(record.IsOrphaned);
        Assert.Null(record.Source);
        Assert.Null(record.RevokedAt);
        Assert.Null(record.KeyId);
        Assert.Null(record.KeyName);
    }

    [Fact]
    [Requirement("PKI-001")]
    [Trait("Requirement", "PKI-001")]
    public async Task ReadCertificate_raises_a_protocol_error_when_issued_at_is_missing()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"certificate":"x","serial_number":"aa"}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Pki.ReadCertificateAsync("aa"));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    [Requirement("PKI-020")]
    [Trait("Requirement", "PKI-020")]
    public async Task DeleteCertificate_sends_force_only_when_true()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        await client.Pki.DeleteCertificateAsync("aa:bb:cc", force: true);

        Assert.Equal("DELETE", transport.Requests[0].Method);
        Assert.EndsWith("/v1/pki/cert/aa:bb:cc", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("{\"force\":true}", Encoding.UTF8.GetString(transport.Requests[0].Body.Span));

        FakeTransport noForceTransport = new();
        noForceTransport.EnqueueResponse(204);
        BastionVaultClient noForceClient = BuildClient(noForceTransport);
        await noForceClient.Pki.DeleteCertificateAsync("aa:bb:cc");
        Assert.Equal(0, noForceTransport.Requests[0].Body.Length);
    }

    [Fact]
    [Requirement("PKI-020")]
    [Trait("Requirement", "PKI-020")]
    public async Task AttachKey_and_DetachKey_send_the_expected_verbs_and_body()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200);
        BastionVaultClient client = BuildClient(transport);

        await client.Pki.AttachKeyAsync("aa:bb:cc", "key-1");

        Assert.Equal("POST", transport.Requests[0].Method);
        Assert.EndsWith("/v1/pki/cert/aa:bb:cc/key", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("{\"key_ref\":\"key-1\"}", Encoding.UTF8.GetString(transport.Requests[0].Body.Span));

        FakeTransport detachTransport = new();
        detachTransport.EnqueueResponse(204);
        BastionVaultClient detachClient = BuildClient(detachTransport);
        await detachClient.Pki.DetachKeyAsync("aa:bb:cc");
        Assert.Equal("DELETE", detachTransport.Requests[0].Method);
        Assert.EndsWith("/v1/pki/cert/aa:bb:cc/key", detachTransport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("PKI-002")]
    [Trait("Requirement", "PKI-002")]
    public async Task ExportCertificate_sends_password_only_when_given_and_redacts_the_response()
    {
        // B1/D-M9-16 (M9 slice a handback): the response can carry key material
        // (`includePrivateKey`/`mode` establish it), so the whole body is wrapped in a redacting
        // type, and the test asserts the redaction — not merely that a result came back.
        const string keyMaterial = "-----BEGIN PRIVATE KEY-----\nSECRETKEYMATERIAL\n-----END PRIVATE KEY-----\n";
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            "{\"data\":{\"certificate\":\"x\",\"private_key\":\"" + keyMaterial.Replace("\n", "\\n", StringComparison.Ordinal) + "\"}}"));
        BastionVaultClient client = BuildClient(transport);

        PkiCertificateExport export = await client.Pki.ExportCertificateAsync(
            "aa:bb:cc", format: "pem", includePrivateKey: true, mode: "backup", password: new SecretString("hunter2"));

        // PKI-001: the payload carries the response verbatim — the key material is present when revealed.
        Assert.Contains("SECRETKEYMATERIAL", export.Payload.Reveal(), StringComparison.Ordinal);
        // PKI-002: but never through ToString(), which is what a log call site would use.
        Assert.Equal("[REDACTED]", export.Payload.ToString());
        Assert.DoesNotContain("SECRETKEYMATERIAL", export.Payload.ToString(), StringComparison.Ordinal);

        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains("\"format\":\"pem\"", body, StringComparison.Ordinal);
        Assert.Contains("\"include_private_key\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"mode\":\"backup\"", body, StringComparison.Ordinal);
        Assert.Contains("\"password\":\"hunter2\"", body, StringComparison.Ordinal);
        Assert.Equal("POST", transport.Requests[0].Method);
        Assert.EndsWith("/v1/pki/cert/aa:bb:cc/export", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport noPasswordTransport = new();
        noPasswordTransport.EnqueueResponse(200, body: Json("""{"data":{"certificate":"x"}}"""));
        BastionVaultClient noPasswordClient = BuildClient(noPasswordTransport);
        _ = await noPasswordClient.Pki.ExportCertificateAsync("aa:bb:cc", "pem", false);
        Assert.DoesNotContain("password", Encoding.UTF8.GetString(noPasswordTransport.Requests[0].Body.Span), StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("PKI-001")]
    [Trait("Requirement", "PKI-001")]
    public async Task ExportCertificate_raises_a_protocol_error_on_a_204_with_no_body()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Pki.ExportCertificateAsync("aa:bb:cc", "pem", false));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    [Requirement("PKI-001")]
    [Trait("Requirement", "PKI-001")]
    public async Task ImportCertificate_sends_source_only_when_given()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{}}"""));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Pki.ImportCertificateAsync("-----BEGIN CERTIFICATE-----\nX\n-----END CERTIFICATE-----\n", source: "external");

        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains("\"source\":\"external\"", body, StringComparison.Ordinal);
        Assert.EndsWith("/v1/pki/certs/import", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport bareTransport = new();
        bareTransport.EnqueueResponse(200, body: Json("""{"data":{}}"""));
        BastionVaultClient bareClient = BuildClient(bareTransport);
        _ = await bareClient.Pki.ImportCertificateAsync("cert-body");
        Assert.DoesNotContain("source", Encoding.UTF8.GetString(bareTransport.Requests[0].Body.Span), StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("PKI-020")]
    [Trait("Requirement", "PKI-020")]
    public async Task Revoke_sends_the_serial_number_verbatim_in_the_body()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200);
        BastionVaultClient client = BuildClient(transport);

        await client.Pki.RevokeAsync("aa:bb:cc");

        Assert.Equal("{\"serial_number\":\"aa:bb:cc\"}", Encoding.UTF8.GetString(transport.Requests[0].Body.Span));
        Assert.EndsWith("/v1/pki/revoke", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- CRL

    [Fact]
    [Requirement("PKI-001")]
    [Trait("Requirement", "PKI-001")]
    public async Task ReadCrl_reads_the_pem_field_verbatim_and_honours_the_pem_flag_in_the_path()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"crl":"-----BEGIN X509 CRL-----\nX\n-----END X509 CRL-----\n","crl_number":7,"issuer_id":"issuer-1"}}"""));
        BastionVaultClient client = BuildClient(transport);

        Crl crl = await client.Pki.ReadCrlAsync();

        Assert.Equal("-----BEGIN X509 CRL-----\nX\n-----END X509 CRL-----\n", crl.CrlPem);
        Assert.Equal(7, crl.CrlNumber);
        Assert.Equal("issuer-1", crl.IssuerId);
        Assert.EndsWith("/v1/pki/crl", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport pemTransport = new();
        pemTransport.EnqueueResponse(200, body: Json("""{"data":{"crl":"x","crl_number":1,"issuer_id":"i"}}"""));
        BastionVaultClient pemClient = BuildClient(pemTransport);
        _ = await pemClient.Pki.ReadCrlAsync(pem: true);
        Assert.EndsWith("/v1/pki/crl/pem", pemTransport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("PKI-001")]
    [Trait("Requirement", "PKI-001")]
    public async Task ReadCrl_raises_a_protocol_error_when_the_wire_omits_crl()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"crl_number":1,"issuer_id":"i"}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Pki.ReadCrlAsync());

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    [Requirement("PKI-001")]
    [Trait("Requirement", "PKI-001")]
    public async Task ReadCrl_raises_a_protocol_error_when_the_wire_omits_crl_number()
    {
        // F4 (M9 slice a handback): 09 §Types marks no member of Crl optional, so an absent
        // crl_number is BV-PROTOCOL-002, never a defaulted 0.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"crl":"x","issuer_id":"i"}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Pki.ReadCrlAsync());

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    [Requirement("PKI-001")]
    [Trait("Requirement", "PKI-001")]
    public async Task RotateCrl_sends_a_bare_post()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200);
        BastionVaultClient client = BuildClient(transport);

        await client.Pki.RotateCrlAsync();

        Assert.Equal("POST", transport.Requests[0].Method);
        Assert.EndsWith("/v1/pki/crl/rotate", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("PKI-001")]
    [Trait("Requirement", "PKI-001")]
    public async Task ReadIssuerCrl_honours_the_pem_flag_in_the_path()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"crl":"x","crl_number":1,"issuer_id":"issuer-1"}}"""));
        BastionVaultClient client = BuildClient(transport);

        Crl crl = await client.Pki.ReadIssuerCrlAsync("issuer-1");

        Assert.Equal("issuer-1", crl.IssuerId);
        Assert.EndsWith("/v1/pki/issuer/issuer-1/crl", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport pemTransport = new();
        pemTransport.EnqueueResponse(200, body: Json("""{"data":{"crl":"x","crl_number":1,"issuer_id":"issuer-1"}}"""));
        BastionVaultClient pemClient = BuildClient(pemTransport);
        _ = await pemClient.Pki.ReadIssuerCrlAsync("issuer-1", pem: true);
        Assert.EndsWith("/v1/pki/issuer/issuer-1/crl/pem", pemTransport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- certs-info paging (D-M9-7, D-M9-8)

    [Fact]
    [Requirement("PAG-001")]
    [Requirement("PAG-005")]
    [Trait("Requirement", "PAG-001")]
    public async Task ListCertificatesInfo_pins_v2_and_defaults_the_limit()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""
        {"keys":["aa"],"records":[{"serial_number":"aa","issued_at":"2026-01-01T00:00:00Z","not_after":"2027-01-01T00:00:00Z","issuer_id":"i","is_orphaned":false,"source":"issued","common_name":"a.example.com","issuer_dn":"CN=Root"}],"total":1,"truncated":false}
        """));
        BastionVaultClient client = BuildClient(transport);

        Page<CertificateSummary> page = await client.Pki.ListCertificatesInfoAsync();

        Assert.Equal(["aa"], page.Keys);
        _ = Assert.Single(page.Records);
        Assert.Equal("a.example.com", page.Records[0].CommonName);
        Assert.Null(page.Next);
        Assert.False(page.Truncated);
        // D-M9-31: pinned to /v2 regardless of ApiPrefix (v1 here); reverses D-M9-7.
        Assert.EndsWith("/v2/pki/certs-info?limit=100", transport.Requests[0].Uri.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("PAG-001")]
    [Trait("Requirement", "PAG-001")]
    public async Task ListCertificatesInfo_passes_after_verbatim_and_validates_limit()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"keys":[],"records":[],"total":0,"truncated":false}"""));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Pki.ListCertificatesInfoAsync(after: "cursor-1", limit: 10);

        Assert.EndsWith("/v2/pki/certs-info?after=cursor-1&limit=10", transport.Requests[0].Uri.ToString(), StringComparison.Ordinal);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Pki.ListCertificatesInfoAsync(limit: 0));
        Assert.Equal(ErrorCodes.InputOutOfRange, exception.Code);
    }

    [Fact]
    [Requirement("PAG-005")]
    [Trait("Requirement", "PAG-005")]
    public async Task ListCertificatesInfo_raises_a_protocol_error_when_keys_and_records_lengths_differ()
    {
        FakeTransport transport = new();
        // D-M9-30: the sole present record is also incomplete (no issued_at/issuer_id/...), which
        // is deliberate — this asserts the length guard fires *before* any per-record decode would
        // ever notice that, not merely that some BV-PROTOCOL-002 eventually surfaces.
        transport.EnqueueResponse(200, body: Json("""{"keys":["aa","bb"],"records":[{"path":"aa"}],"total":2,"truncated":false}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Pki.ListCertificatesInfoAsync());

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
        Assert.Equal(200, exception.StatusCode);
    }

    [Fact]
    public async Task ListCertificatesInfo_raises_a_protocol_error_when_a_present_record_is_missing_a_required_field()
    {
        // D-M9-30's other guard: keys.Count and the records array length agree (both 1), so
        // PAG-005's length check passes; the failure is a present record missing a field 09
        // §Types declares required (issued_at). Not tagged PAG-005 — that requirement names the
        // length check, not this one.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"keys":["aa"],"records":[{"serial_number":"aa"}],"total":1,"truncated":false}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Pki.ListCertificatesInfoAsync());

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    [Requirement("PAG-005")]
    [Trait("Requirement", "PAG-005")]
    public async Task ListCertificatesInfo_raises_a_protocol_error_when_the_response_has_no_data()
    {
        // A 204 shapes to a null Response (TRN-050's other half) rather than the 404-is-absence
        // reading treatNotFoundEmptyAsAbsent gives elsewhere: this route passes false for it
        // (there is no requirement-given meaning for "this listing is absent"), so a 404 instead
        // raises BV-NOTFOUND-001 through the ordinary status mapping, not BV-PROTOCOL-002.
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Pki.ListCertificatesInfoAsync());

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    [Requirement("PAG-004")]
    [Trait("Requirement", "PAG-004")]
    public async Task ListCertificatesInfoAllAsync_walks_every_page_in_cursor_order()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"keys":["aa"],"records":[{"serial_number":"aa","issued_at":"2026-01-01T00:00:00Z","not_after":"2027-01-01T00:00:00Z","issuer_id":"i","is_orphaned":false,"source":"issued","common_name":"a","issuer_dn":"CN=Root"}],"total":2,"next":"aa","truncated":true}"""));
        transport.EnqueueResponse(200, body: Json(
            """{"keys":["bb"],"records":[{"serial_number":"bb","issued_at":"2026-01-01T00:00:00Z","not_after":"2027-01-01T00:00:00Z","issuer_id":"i","is_orphaned":false,"source":"issued","common_name":"b","issuer_dn":"CN=Root"}],"total":2,"next":"","truncated":false}"""));
        BastionVaultClient client = BuildClient(transport);

        List<string> keys = [];
        await foreach (KeyValuePair<string, CertificateSummary> entry in client.Pki.ListCertificatesInfoAllAsync())
        {
            keys.Add(entry.Key);
        }

        Assert.Equal(["aa", "bb"], keys);
        Assert.EndsWith("after=aa&limit=100", transport.Requests[1].Uri.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("PAG-004")]
    [Trait("Requirement", "PAG-004")]
    public async Task ListCertificatesInfoAllAsync_stops_at_MaxRecords_with_BV_INPUT_005()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"keys":["aa","bb","cc"],"records":[{"serial_number":"aa","issued_at":"2026-01-01T00:00:00Z","not_after":"2027-01-01T00:00:00Z","issuer_id":"i","is_orphaned":false,"source":"issued","common_name":"a","issuer_dn":"CN=Root"},{"serial_number":"bb","issued_at":"2026-01-01T00:00:00Z","not_after":"2027-01-01T00:00:00Z","issuer_id":"i","is_orphaned":false,"source":"issued","common_name":"b","issuer_dn":"CN=Root"},{"serial_number":"cc","issued_at":"2026-01-01T00:00:00Z","not_after":"2027-01-01T00:00:00Z","issuer_id":"i","is_orphaned":false,"source":"issued","common_name":"c","issuer_dn":"CN=Root"}],"total":100,"next":"x","truncated":true}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(async () =>
        {
            await foreach (KeyValuePair<string, CertificateSummary> _ in client.Pki.ListCertificatesInfoAllAsync(maxRecords: 2))
            {
            }
        }).ConfigureAwait(false);

        Assert.Equal(ErrorCodes.InputIterationCapExceeded, failure.Code);
        Assert.Equal(100, failure.Details["total"]);
        Assert.Equal(2, failure.Details["maxRecords"]);
    }

    // ---------------------------------------------------------------- CA lifecycle (M9 slice b)

    [Fact]
    [Requirement("PKI-002")]
    [Trait("Requirement", "PKI-002")]
    public async Task GenerateRoot_reads_every_field_and_redacts_the_private_key()
    {
        const string keyMaterial = "-----BEGIN PRIVATE KEY-----\nROOTKEYMATERIAL\n-----END PRIVATE KEY-----\n";
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            "{\"data\":{\"certificate\":\"cert\",\"issuing_ca\":\"ca\",\"issuer_id\":\"iss-1\",\"issuer_name\":\"root-2024\"," +
            "\"expiration\":\"2030-01-01T00:00:00Z\",\"private_key\":\"" + keyMaterial.Replace("\n", "\\n", StringComparison.Ordinal) + "\",\"private_key_type\":\"ec\",\"key_id\":\"key-1\"}}"));
        BastionVaultClient client = BuildClient(transport);

        PkiRootCertificate root = await client.Pki.GenerateRootAsync(
            PkiKeyGenerationType.Exported,
            new PkiRootSpec { CommonName = "root.example.com", Organization = "Example Inc", KeyType = "ec", KeyBits = 256, Ttl = TimeSpan.FromDays(3650), IssuerName = "root-2024", KeyRef = "key-1" });

        Assert.Equal("cert", root.Certificate);
        Assert.Equal("ca", root.IssuingCa);
        Assert.Equal("iss-1", root.IssuerId);
        Assert.Equal("root-2024", root.IssuerName);
        Assert.Equal("ec", root.PrivateKeyType);
        Assert.Equal("key-1", root.KeyId);
        // PKI-002: the exported private key is present when revealed, but never through ToString().
        Assert.Contains("ROOTKEYMATERIAL", root.PrivateKey!.Reveal(), StringComparison.Ordinal);
        Assert.Equal("[REDACTED]", root.PrivateKey.ToString());

        Assert.Equal("POST", transport.Requests[0].Method);
        Assert.EndsWith("/v1/pki/root/generate/exported", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains("\"common_name\":\"root.example.com\"", body, StringComparison.Ordinal);
        Assert.Contains("\"organization\":\"Example Inc\"", body, StringComparison.Ordinal);
        Assert.Contains("\"key_ref\":\"key-1\"", body, StringComparison.Ordinal);

        FakeTransport internalTransport = new();
        internalTransport.EnqueueResponse(200, body: Json(
            "{\"data\":{\"certificate\":\"cert\",\"issuing_ca\":\"ca\",\"issuer_id\":\"iss-1\",\"issuer_name\":\"root-2024\",\"expiration\":\"2030-01-01T00:00:00Z\"}}"));
        BastionVaultClient internalClient = BuildClient(internalTransport);
        PkiRootCertificate internalRoot = await internalClient.Pki.GenerateRootAsync(PkiKeyGenerationType.Internal, new PkiRootSpec { CommonName = "root.example.com" });
        Assert.Null(internalRoot.PrivateKey);
        Assert.EndsWith("/v1/pki/root/generate/internal", internalTransport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateRoot_rejects_an_empty_common_name_before_any_request_is_sent()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        _ = await Assert.ThrowsAsync<ArgumentException>(
            () => client.Pki.GenerateRootAsync(PkiKeyGenerationType.Internal, new PkiRootSpec { CommonName = string.Empty }));
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task SignIntermediate_serialises_overrides_and_reads_the_certificate_pair()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"certificate":"signed-cert","issuing_ca":"ca-cert"}}"""));
        BastionVaultClient client = BuildClient(transport);

        SignedIntermediateCertificate result = await client.Pki.SignIntermediateAsync(
            new SignIntermediateRequest { Csr = "csr-body", CommonName = "intermediate", Organization = "Example Inc", Ttl = TimeSpan.FromDays(30), MaxPathLength = 0, IssuerRef = "root-issuer" });

        Assert.Equal("signed-cert", result.Certificate);
        Assert.Equal("ca-cert", result.IssuingCa);
        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains("\"csr\":\"csr-body\"", body, StringComparison.Ordinal);
        Assert.Contains("\"max_path_length\":0", body, StringComparison.Ordinal);
        Assert.Contains("\"issuer_ref\":\"root-issuer\"", body, StringComparison.Ordinal);
        Assert.EndsWith("/v1/pki/root/sign-intermediate", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignIntermediate_rejects_an_empty_csr()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.SignIntermediateAsync(new SignIntermediateRequest { Csr = string.Empty }));
        Assert.Empty(transport.Requests);
    }

    [Fact]
    [Requirement("PKI-002")]
    [Trait("Requirement", "PKI-002")]
    public async Task GenerateIntermediate_redacts_the_private_key_when_exported()
    {
        const string keyMaterial = "INTERMEDIATEKEYMATERIAL";
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            "{\"data\":{\"csr\":\"csr-out\",\"key_id\":\"key-2\",\"private_key\":\"" + keyMaterial + "\",\"private_key_type\":\"ec\"}}"));
        BastionVaultClient client = BuildClient(transport);

        PkiIntermediateCsr result = await client.Pki.GenerateIntermediateAsync(
            PkiKeyGenerationType.Exported, new PkiIntermediateSpec { CommonName = "intermediate.example.com" });

        Assert.Equal("csr-out", result.Csr);
        Assert.Equal("key-2", result.KeyId);
        Assert.Contains(keyMaterial, result.PrivateKey!.Reveal(), StringComparison.Ordinal);
        Assert.Equal("[REDACTED]", result.PrivateKey.ToString());
        Assert.DoesNotContain(keyMaterial, result.PrivateKey.ToString(), StringComparison.Ordinal);
        Assert.EndsWith("/v1/pki/intermediate/generate/exported", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetSignedIntermediate_reads_the_imported_lists()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"imported_issuers":["iss-2"],"imported_keys":["key-3"],"issuer_id":"iss-2","issuer_name":"intermediate-2024"}}"""));
        BastionVaultClient client = BuildClient(transport);

        SetSignedIntermediateResult result = await client.Pki.SetSignedIntermediateAsync("signed-cert", "intermediate-2024");

        Assert.Equal(["iss-2"], result.ImportedIssuers);
        Assert.Equal(["key-3"], result.ImportedKeys);
        Assert.Equal("iss-2", result.IssuerId);
        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains("\"certificate\":\"signed-cert\"", body, StringComparison.Ordinal);
        Assert.Contains("\"issuer_name\":\"intermediate-2024\"", body, StringComparison.Ordinal);
        Assert.EndsWith("/v1/pki/intermediate/set-signed", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfigureCa_sends_the_bundle_and_returns_the_untyped_response()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"imported_issuers":["iss-1"]}}"""));
        BastionVaultClient client = BuildClient(transport);

        IReadOnlyDictionary<string, System.Text.Json.JsonElement>? result = await client.Pki.ConfigureCaAsync("pem-bundle", "root-2024");

        Assert.NotNull(result);
        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains("\"pem_bundle\":\"pem-bundle\"", body, StringComparison.Ordinal);
        Assert.Contains("\"issuer_name\":\"root-2024\"", body, StringComparison.Ordinal);
        Assert.EndsWith("/v1/pki/config/ca", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Urls_round_trip_distinguishes_absent_from_empty()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"issuing_certificates":["http://x/ca"],"ocsp_servers":[]}}"""));
        BastionVaultClient client = BuildClient(transport);

        PkiUrls? urls = await client.Pki.ReadUrlsAsync();

        Assert.NotNull(urls);
        Assert.Equal(["http://x/ca"], urls!.IssuingCertificates);
        Assert.Null(urls.CrlDistributionPoints);
        Assert.Empty(urls.OcspServers!);

        FakeTransport writeTransport = new();
        writeTransport.EnqueueResponse(200);
        BastionVaultClient writeClient = BuildClient(writeTransport);
        await writeClient.Pki.WriteUrlsAsync(new PkiUrls { IssuingCertificates = ["http://y/ca"] });
        string body = Encoding.UTF8.GetString(writeTransport.Requests[0].Body.Span);
        Assert.Contains("\"issuing_certificates\":[\"http://y/ca\"]", body, StringComparison.Ordinal);
        Assert.DoesNotContain("crl_distribution_points", body, StringComparison.Ordinal);
        Assert.EndsWith("/v1/pki/config/urls", writeTransport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CrlConfig_round_trips_expiry_and_disable()
    {
        // TRN-031 (gate B1): 09-pki-engine.md:48 quotes expiry's default ("72h"), so the wire form
        // is a Go-style string, not integer seconds — the discriminator the gate applied uniformly
        // to `config` fields.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"expiry":"72h","disable":false}}"""));
        BastionVaultClient client = BuildClient(transport);

        PkiCrlConfig? config = await client.Pki.ReadCrlConfigAsync();
        Assert.Equal(TimeSpan.FromHours(72), config!.Expiry);
        Assert.False(config.Disable);

        FakeTransport writeTransport = new();
        writeTransport.EnqueueResponse(200);
        BastionVaultClient writeClient = BuildClient(writeTransport);
        await writeClient.Pki.WriteCrlConfigAsync(new PkiCrlConfig { Expiry = TimeSpan.FromHours(24), Disable = true });
        string body = Encoding.UTF8.GetString(writeTransport.Requests[0].Body.Span);
        Assert.Contains("\"expiry\":\"24h\"", body, StringComparison.Ordinal);
        Assert.Contains("\"disable\":true", body, StringComparison.Ordinal);
        Assert.EndsWith("/v1/pki/config/crl", writeTransport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CrlConfig_read_yields_null_expiry_on_an_unparsable_or_absent_value()
    {
        FakeTransport numberTransport = new();
        numberTransport.EnqueueResponse(200, body: Json("""{"data":{"expiry":259200}}"""));
        BastionVaultClient numberClient = BuildClient(numberTransport);
        Assert.Null((await numberClient.Pki.ReadCrlConfigAsync())!.Expiry);

        FakeTransport absentTransport = new();
        absentTransport.EnqueueResponse(200, body: Json("""{"data":{}}"""));
        BastionVaultClient absentClient = BuildClient(absentTransport);
        Assert.Null((await absentClient.Pki.ReadCrlConfigAsync())!.Expiry);
    }

    [Fact]
    public async Task IssuersConfig_round_trips_the_default_ref()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"default":"iss-1"}}"""));
        BastionVaultClient client = BuildClient(transport);

        PkiIssuersConfig? config = await client.Pki.ReadIssuersConfigAsync();
        Assert.Equal("iss-1", config!.Default);

        FakeTransport writeTransport = new();
        writeTransport.EnqueueResponse(200);
        BastionVaultClient writeClient = BuildClient(writeTransport);
        await writeClient.Pki.WriteIssuersConfigAsync(new PkiIssuersConfig { Default = "iss-2" });
        Assert.Contains("\"default\":\"iss-2\"", Encoding.UTF8.GetString(writeTransport.Requests[0].Body.Span), StringComparison.Ordinal);
        Assert.EndsWith("/v1/pki/config/issuers", writeTransport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Issuers_list_read_write_and_delete()
    {
        FakeTransport listTransport = new();
        listTransport.EnqueueResponse(200, body: Json("""{"data":{"keys":["iss-1","iss-2"]}}"""));
        BastionVaultClient listClient = BuildClient(listTransport);
        Assert.Equal(["iss-1", "iss-2"], await listClient.Pki.ListIssuersAsync());
        Assert.Equal("LIST", listTransport.Requests[0].Method);
        Assert.EndsWith("/v1/pki/issuers/", listTransport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport readTransport = new();
        readTransport.EnqueueResponse(200, body: Json("""{"data":{"issuer_name":"root-2024"}}"""));
        BastionVaultClient readClient = BuildClient(readTransport);
        IReadOnlyDictionary<string, System.Text.Json.JsonElement>? read = await readClient.Pki.ReadIssuerAsync("iss-1");
        Assert.NotNull(read);
        Assert.EndsWith("/v1/pki/issuer/iss-1", readTransport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport writeTransport = new();
        writeTransport.EnqueueResponse(200, body: Json("""{"data":{"issuer_name":"root-2024b"}}"""));
        BastionVaultClient writeClient = BuildClient(writeTransport);
        _ = await writeClient.Pki.WriteIssuerAsync("iss-1", new PkiIssuerWrite { IssuerName = "root-2024b", Usage = ["issuing-certificates", "crl-signing"] });
        string writeBody = Encoding.UTF8.GetString(writeTransport.Requests[0].Body.Span);
        Assert.Contains("\"issuer_name\":\"root-2024b\"", writeBody, StringComparison.Ordinal);
        Assert.Contains("\"usage\":[\"issuing-certificates\",\"crl-signing\"]", writeBody, StringComparison.Ordinal);
        Assert.Equal("POST", writeTransport.Requests[0].Method);

        FakeTransport deleteTransport = new();
        deleteTransport.EnqueueResponse(204);
        BastionVaultClient deleteClient = BuildClient(deleteTransport);
        await deleteClient.Pki.DeleteIssuerAsync("iss-1");
        Assert.Equal("DELETE", deleteTransport.Requests[0].Method);
        Assert.EndsWith("/v1/pki/issuer/iss-1", deleteTransport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IssuerChain_reads_the_untyped_response()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"ca_chain":["cert-a","cert-b"]}}"""));
        BastionVaultClient client = BuildClient(transport);
        IReadOnlyDictionary<string, System.Text.Json.JsonElement>? chain = await client.Pki.IssuerChainAsync("iss-1");
        Assert.NotNull(chain);
        Assert.EndsWith("/v1/pki/issuer/iss-1/chain", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("PKI-001")]
    [Trait("Requirement", "PKI-001")]
    public async Task ExportIssuer_never_exposes_a_private_key_parameter()
    {
        // D-M9-1: 09-pki-engine.md:52 states private keys are never exported here, so no parameter
        // implying otherwise exists — verified structurally, not just by the happy path below.
        System.Reflection.MethodInfo method = typeof(PkiOperations).GetMethod(nameof(PkiOperations.ExportIssuerAsync))!;
        Assert.DoesNotContain(method.GetParameters(), p => p.Name is "includePrivateKey" or "exportPrivateKey");

        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"certificate":"cert"}}"""));
        BastionVaultClient client = BuildClient(transport);
        IReadOnlyDictionary<string, System.Text.Json.JsonElement>? export = await client.Pki.ExportIssuerAsync(
            "iss-1", format: "pkcs12", includeChain: true, password: new SecretString("hunter2"));
        Assert.NotNull(export);
        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains("\"password\":\"hunter2\"", body, StringComparison.Ordinal);
        // R6/D-M9-20: the body assertion above is only half of it — the other half is that the
        // secret never reached the URI, which is what a query-string leak (D-M9-19's defect) would
        // have shown up as.
        Assert.DoesNotContain("hunter2", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("POST", transport.Requests[0].Method);
        Assert.EndsWith("/v1/pki/issuer/iss-1/export", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadCa_and_ReadCaChain_read_the_untyped_response()
    {
        FakeTransport caTransport = new();
        caTransport.EnqueueResponse(200, body: Json("""{"data":{"certificate":"root-cert"}}"""));
        BastionVaultClient caClient = BuildClient(caTransport);
        IReadOnlyDictionary<string, System.Text.Json.JsonElement>? ca = await caClient.Pki.ReadCaAsync(pem: true);
        Assert.NotNull(ca);
        Assert.EndsWith("/v1/pki/ca/pem", caTransport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport chainTransport = new();
        chainTransport.EnqueueResponse(200, body: Json("""{"data":{"certificates":["a","b"]}}"""));
        BastionVaultClient chainClient = BuildClient(chainTransport);
        IReadOnlyDictionary<string, System.Text.Json.JsonElement>? chain = await chainClient.Pki.ReadCaChainAsync();
        Assert.NotNull(chain);
        Assert.EndsWith("/v1/pki/ca_chain", chainTransport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- managed keys (M9 slice b)

    [Fact]
    public async Task ListKeys_returns_the_wire_list()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"keys":["key-1","key-2"]}}"""));
        BastionVaultClient client = BuildClient(transport);
        Assert.Equal(["key-1", "key-2"], await client.Pki.ListKeysAsync());
        Assert.EndsWith("/v1/pki/keys/", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("PKI-002")]
    [Trait("Requirement", "PKI-002")]
    public async Task GenerateKey_redacts_the_response_when_exported()
    {
        // D-M9-16's generalised rule: 09 §Managed keys defines no response shape at all, and the
        // Exported path form establishes the response may carry key material, so the whole body is
        // wrapped rather than surfaced through an untyped map — the test asserts the redaction, not
        // merely that a result came back.
        const string keyMaterial = "MANAGEDKEYMATERIAL";
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("{\"data\":{\"key_id\":\"key-9\",\"private_key\":\"" + keyMaterial + "\"}}"));
        BastionVaultClient client = BuildClient(transport);

        PkiGeneratedKey generated = await client.Pki.GenerateKeyAsync(
            PkiKeyGenerationType.Exported, keyType: "ec", keyBits: 256, name: "my-key", exportable: true);

        Assert.Contains(keyMaterial, generated.Payload.Reveal(), StringComparison.Ordinal);
        Assert.Equal("[REDACTED]", generated.Payload.ToString());
        Assert.DoesNotContain(keyMaterial, generated.Payload.ToString(), StringComparison.Ordinal);
        Assert.EndsWith("/v1/pki/keys/generate/exported", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains("\"key_type\":\"ec\"", body, StringComparison.Ordinal);
        Assert.Contains("\"exportable\":true", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateKey_raises_a_protocol_error_on_a_204_with_no_body()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);
        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Pki.GenerateKeyAsync(PkiKeyGenerationType.Internal));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    [Requirement("PKI-002")]
    [Trait("Requirement", "PKI-002")]
    public async Task ImportKey_sends_the_private_key_only_in_the_request_body()
    {
        // D-M9-20: secret material never travels in a path segment or query string, only a body.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"key_id":"key-1"}}"""));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Pki.ImportKeyAsync(new SecretString("SECRETIMPORTEDKEY"), name: "my-key", exportable: true);

        TransportRequest request = transport.Requests[0];
        Assert.Equal("POST", request.Method);
        Assert.DoesNotContain("SECRETIMPORTEDKEY", request.Uri.AbsoluteUri, StringComparison.Ordinal);
        string body = Encoding.UTF8.GetString(request.Body.Span);
        Assert.Contains("\"private_key\":\"SECRETIMPORTEDKEY\"", body, StringComparison.Ordinal);
        Assert.EndsWith("/v1/pki/keys/import", request.Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadKey_and_DeleteKey()
    {
        FakeTransport readTransport = new();
        readTransport.EnqueueResponse(200, body: Json("""{"data":{"name":"my-key"}}"""));
        BastionVaultClient readClient = BuildClient(readTransport);
        IReadOnlyDictionary<string, System.Text.Json.JsonElement>? key = await readClient.Pki.ReadKeyAsync("key-1");
        Assert.NotNull(key);
        Assert.EndsWith("/v1/pki/key/key-1", readTransport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport deleteTransport = new();
        deleteTransport.EnqueueResponse(204);
        BastionVaultClient deleteClient = BuildClient(deleteTransport);
        await deleteClient.Pki.DeleteKeyAsync("key-1", force: true);
        string deleteBody = Encoding.UTF8.GetString(deleteTransport.Requests[0].Body.Span);
        Assert.Contains("\"force\":true", deleteBody, StringComparison.Ordinal);
        Assert.Equal("DELETE", deleteTransport.Requests[0].Method);
        Assert.EndsWith("/v1/pki/key/key-1", deleteTransport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        // R7 (gate): force's default (false) sends no body at all, distinct from the true arm above.
        FakeTransport defaultDeleteTransport = new();
        defaultDeleteTransport.EnqueueResponse(204);
        BastionVaultClient defaultDeleteClient = BuildClient(defaultDeleteTransport);
        await defaultDeleteClient.Pki.DeleteKeyAsync("key-1");
        Assert.Equal(0, defaultDeleteTransport.Requests[0].Body.Length);
    }

    // ---------------------------------------------------------------- tidy (M9 slice b)

    [Fact]
    public async Task Tidy_sends_the_default_options_when_none_are_given()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200);
        BastionVaultClient client = BuildClient(transport);
        await client.Pki.TidyAsync();
        Assert.Equal("POST", transport.Requests[0].Method);
        Assert.EndsWith("/v1/pki/tidy", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("{}", Encoding.UTF8.GetString(transport.Requests[0].Body.Span));

        FakeTransport withOptionsTransport = new();
        withOptionsTransport.EnqueueResponse(200);
        BastionVaultClient withOptionsClient = BuildClient(withOptionsTransport);
        await withOptionsClient.Pki.TidyAsync(new PkiTidyOptions { TidyCertStore = false, TidyRevokedCerts = true, SafetyBuffer = TimeSpan.FromHours(48) });
        string body = Encoding.UTF8.GetString(withOptionsTransport.Requests[0].Body.Span);
        Assert.Contains("\"tidy_cert_store\":false", body, StringComparison.Ordinal);
        // TRN-031 (gate B1): safety_buffer quotes its default ("72h") in 09-pki-engine.md:82, so the
        // wire form is Go-style, not integer seconds.
        Assert.Contains("\"safety_buffer\":\"48h\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TidyStatus_reads_the_untyped_response()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"state":"Finished"}}"""));
        BastionVaultClient client = BuildClient(transport);
        IReadOnlyDictionary<string, System.Text.Json.JsonElement>? status = await client.Pki.TidyStatusAsync();
        Assert.NotNull(status);
        Assert.EndsWith("/v1/pki/tidy-status", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AutoTidy_round_trips_only_its_two_named_fields()
    {
        // TRN-031 (gate B1): interval quotes its default ("12h") in 09-pki-engine.md:84, so the wire
        // form is Go-style, not integer seconds.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"enabled":true,"interval":"12h"}}"""));
        BastionVaultClient client = BuildClient(transport);
        PkiAutoTidyConfig? config = await client.Pki.ReadAutoTidyAsync();
        Assert.True(config!.Enabled);
        Assert.Equal(TimeSpan.FromHours(12), config.Interval);

        FakeTransport writeTransport = new();
        writeTransport.EnqueueResponse(200);
        BastionVaultClient writeClient = BuildClient(writeTransport);
        await writeClient.Pki.WriteAutoTidyAsync(new PkiAutoTidyConfig { Enabled = false });
        string body = Encoding.UTF8.GetString(writeTransport.Requests[0].Body.Span);
        Assert.Contains("\"enabled\":false", body, StringComparison.Ordinal);
        Assert.DoesNotContain("interval", body, StringComparison.Ordinal);
        Assert.EndsWith("/v1/pki/config/auto-tidy", writeTransport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- ACME (M9 slice b)

    [Fact]
    public async Task Acme_config_round_trips_and_deletes()
    {
        FakeTransport readTransport = new();
        readTransport.EnqueueResponse(200, body: Json(
            """{"data":{"enabled":true,"default_role":"acme-role","dns_resolvers":["1.1.1.1"],"rate_orders_per_window":100}}"""));
        BastionVaultClient readClient = BuildClient(readTransport);
        PkiAcmeConfig? config = await readClient.Pki.Acme.ReadConfigAsync();
        Assert.True(config!.Enabled);
        Assert.Equal("acme-role", config.DefaultRole);
        Assert.Equal(["1.1.1.1"], config.DnsResolvers);
        Assert.Equal(100, config.RateOrdersPerWindow);
        Assert.EndsWith("/v1/pki/acme/config", readTransport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport writeTransport = new();
        writeTransport.EnqueueResponse(200);
        BastionVaultClient writeClient = BuildClient(writeTransport);
        await writeClient.Pki.Acme.WriteConfigAsync(new PkiAcmeConfig { Enabled = true, ExternalHostname = "acme.example.com", NonceTtlSecs = 60 });
        string writeBody = Encoding.UTF8.GetString(writeTransport.Requests[0].Body.Span);
        Assert.Contains("\"external_hostname\":\"acme.example.com\"", writeBody, StringComparison.Ordinal);
        Assert.Contains("\"nonce_ttl_secs\":60", writeBody, StringComparison.Ordinal);
        Assert.Equal("POST", writeTransport.Requests[0].Method);

        FakeTransport deleteTransport = new();
        deleteTransport.EnqueueResponse(204);
        BastionVaultClient deleteClient = BuildClient(deleteTransport);
        await deleteClient.Pki.Acme.DeleteConfigAsync();
        Assert.Equal("DELETE", deleteTransport.Requests[0].Method);
        Assert.EndsWith("/v1/pki/acme/config", deleteTransport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectoryUrl_builds_the_url_without_sending_a_request()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        string url = client.Pki.Acme.DirectoryUrl();

        Assert.Equal($"{Address}/v1/pki/acme/directory", url);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public void DirectoryUrl_honours_a_non_default_mount_and_rejects_an_empty_one()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        Assert.Equal($"{Address}/v1/pki-other/acme/directory", client.Pki.Acme.DirectoryUrl("pki-other"));
        _ = Assert.Throws<ArgumentException>(() => client.Pki.Acme.DirectoryUrl(string.Empty));
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public void PkiAcmeOperations_wraps_no_route_beyond_the_config_three_and_DirectoryUrl()
    {
        // D-M9-14's negative check: 09-pki-engine.md:115-117 forbids wrapping the RFC 8555 protocol
        // paths (acme/directory, new-nonce, new-account, ...) beyond Pki.Acme.DirectoryUrl.
        string[] publicMemberNames = [.. typeof(PkiAcmeOperations)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .OrderBy(name => name, StringComparer.Ordinal)];

        Assert.Equal(
            new[] { "DeleteConfigAsync", "DirectoryUrl", "ReadConfigAsync", "WriteConfigAsync" },
            publicMemberNames);
    }

    [Fact]
    public async Task GenerateRoot_GenerateIntermediate_and_GenerateKey_reject_a_null_or_empty_mount()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        _ = await Assert.ThrowsAsync<ArgumentException>(
            () => client.Pki.GenerateRootAsync(PkiKeyGenerationType.Internal, new PkiRootSpec { CommonName = "c" }, mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(
            () => client.Pki.SignIntermediateAsync(new SignIntermediateRequest { Csr = "c" }, mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(
            () => client.Pki.GenerateIntermediateAsync(PkiKeyGenerationType.Internal, new PkiIntermediateSpec { CommonName = "c" }, mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.SetSignedIntermediateAsync("c", mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.ConfigureCaAsync("c", mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.ReadUrlsAsync(mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.WriteUrlsAsync(new PkiUrls(), mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.ReadCrlConfigAsync(mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.WriteCrlConfigAsync(new PkiCrlConfig(), mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.ReadIssuersConfigAsync(mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.WriteIssuersConfigAsync(new PkiIssuersConfig(), mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.ListIssuersAsync(mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.ReadIssuerAsync("i", mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.WriteIssuerAsync("i", new PkiIssuerWrite(), mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.DeleteIssuerAsync("i", mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.IssuerChainAsync("i", mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.ExportIssuerAsync("i", mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.ReadCaAsync(mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.ReadCaChainAsync(mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.ListKeysAsync(mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.GenerateKeyAsync(PkiKeyGenerationType.Internal, mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.ImportKeyAsync(new SecretString("k"), mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.ReadKeyAsync("k", mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.DeleteKeyAsync("k", mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.TidyAsync(mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.TidyStatusAsync(mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.ReadAutoTidyAsync(mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.WriteAutoTidyAsync(new PkiAutoTidyConfig(), mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.Acme.ReadConfigAsync(mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.Acme.WriteConfigAsync(new PkiAcmeConfig(), mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.Acme.DeleteConfigAsync(mount: string.Empty));

        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task GenerateIntermediate_serialises_every_optional_field_and_omits_the_private_key_when_internal()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"csr":"csr-out"}}"""));
        BastionVaultClient client = BuildClient(transport);

        PkiIntermediateCsr result = await client.Pki.GenerateIntermediateAsync(
            PkiKeyGenerationType.Internal,
            new PkiIntermediateSpec { CommonName = "intermediate.example.com", Organization = "Example Inc", KeyType = "ec", KeyBits = 256, Ttl = TimeSpan.FromDays(1), IssuerName = "intermediate-2024", KeyRef = "key-2" });

        Assert.Equal("csr-out", result.Csr);
        Assert.Null(result.KeyId);
        Assert.Null(result.PrivateKey);
        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains("\"organization\":\"Example Inc\"", body, StringComparison.Ordinal);
        Assert.Contains("\"key_type\":\"ec\"", body, StringComparison.Ordinal);
        Assert.Contains("\"key_bits\":256", body, StringComparison.Ordinal);
        Assert.Contains("\"issuer_name\":\"intermediate-2024\"", body, StringComparison.Ordinal);
        Assert.Contains("\"key_ref\":\"key-2\"", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"data":{"issuing_ca":"ca","issuer_id":"i","issuer_name":"n","expiration":"2030-01-01T00:00:00Z"}}""")]
    [InlineData("""{"data":{"certificate":"c","issuer_id":"i","issuer_name":"n","expiration":"2030-01-01T00:00:00Z"}}""")]
    [InlineData("""{"data":{"certificate":"c","issuing_ca":"ca","issuer_name":"n","expiration":"2030-01-01T00:00:00Z"}}""")]
    [InlineData("""{"data":{"certificate":"c","issuing_ca":"ca","issuer_id":"i","expiration":"2030-01-01T00:00:00Z"}}""")]
    public async Task GenerateRoot_raises_an_envelope_mismatch_when_a_required_field_is_missing(string body)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(body));
        BastionVaultClient client = BuildClient(transport);
        _ = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Pki.GenerateRootAsync(PkiKeyGenerationType.Internal, new PkiRootSpec { CommonName = "c" }));
    }

    [Fact]
    public async Task GenerateIntermediate_raises_an_envelope_mismatch_when_csr_is_missing()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"key_id":"k"}}"""));
        BastionVaultClient client = BuildClient(transport);
        _ = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Pki.GenerateIntermediateAsync(PkiKeyGenerationType.Internal, new PkiIntermediateSpec { CommonName = "c" }));
    }

    [Theory]
    [InlineData("""{"data":{"issuing_ca":"ca"}}""")]
    [InlineData("""{"data":{"certificate":"c"}}""")]
    public async Task SignIntermediate_raises_an_envelope_mismatch_when_a_required_field_is_missing(string body)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(body));
        BastionVaultClient client = BuildClient(transport);
        _ = await Assert.ThrowsAsync<BastionVaultException>(() => client.Pki.SignIntermediateAsync(new SignIntermediateRequest { Csr = "c" }));
    }

    [Theory]
    [InlineData("""{"data":{"imported_issuers":[],"imported_keys":[],"issuer_name":"n"}}""")]
    [InlineData("""{"data":{"imported_issuers":[],"imported_keys":[],"issuer_id":"i"}}""")]
    public async Task SetSignedIntermediate_raises_an_envelope_mismatch_when_a_required_field_is_missing(string body)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(body));
        BastionVaultClient client = BuildClient(transport);
        _ = await Assert.ThrowsAsync<BastionVaultException>(() => client.Pki.SetSignedIntermediateAsync("c"));
    }

    [Fact]
    public async Task ConfigureCa_and_SetSignedIntermediate_omit_the_issuer_name_when_not_given()
    {
        FakeTransport configureTransport = new();
        configureTransport.EnqueueResponse(200, body: Json("""{"data":{}}"""));
        BastionVaultClient configureClient = BuildClient(configureTransport);
        _ = await configureClient.Pki.ConfigureCaAsync("pem-bundle");
        Assert.DoesNotContain("issuer_name", Encoding.UTF8.GetString(configureTransport.Requests[0].Body.Span), StringComparison.Ordinal);

        FakeTransport setSignedTransport = new();
        setSignedTransport.EnqueueResponse(200, body: Json(
            """{"data":{"imported_issuers":[],"imported_keys":[],"issuer_id":"i","issuer_name":"n"}}"""));
        BastionVaultClient setSignedClient = BuildClient(setSignedTransport);
        _ = await setSignedClient.Pki.SetSignedIntermediateAsync("cert");
        Assert.DoesNotContain("issuer_name", Encoding.UTF8.GetString(setSignedTransport.Requests[0].Body.Span), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteIssuer_omits_both_fields_when_neither_is_given()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{}}"""));
        BastionVaultClient client = BuildClient(transport);
        _ = await client.Pki.WriteIssuerAsync("iss-1", new PkiIssuerWrite());
        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Equal("{}", body);
    }

    [Fact]
    public async Task WriteUrls_omits_every_field_when_none_is_given()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200);
        BastionVaultClient client = BuildClient(transport);
        await client.Pki.WriteUrlsAsync(new PkiUrls());
        Assert.Equal("{}", Encoding.UTF8.GetString(transport.Requests[0].Body.Span));
    }

    [Fact]
    public async Task WriteAutoTidy_serialises_both_fields_when_both_are_given()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200);
        BastionVaultClient client = BuildClient(transport);
        await client.Pki.WriteAutoTidyAsync(new PkiAutoTidyConfig { Enabled = true, Interval = TimeSpan.FromHours(6) });
        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains("\"enabled\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"interval\":\"6h\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Acme_WriteConfig_serialises_every_remaining_optional_field()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200);
        BastionVaultClient client = BuildClient(transport);
        await client.Pki.Acme.WriteConfigAsync(new PkiAcmeConfig
        {
            DefaultRole = "acme-role",
            DefaultIssuerRef = "iss-1",
            EabRequired = true,
            RateWindowSecs = 60,
            RateOrdersPerWindow = 50,
            DnsResolvers = ["1.1.1.1", "8.8.8.8"],
        });
        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains("\"default_role\":\"acme-role\"", body, StringComparison.Ordinal);
        Assert.Contains("\"default_issuer_ref\":\"iss-1\"", body, StringComparison.Ordinal);
        Assert.Contains("\"eab_required\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"rate_window_secs\":60", body, StringComparison.Ordinal);
        Assert.Contains("\"rate_orders_per_window\":50", body, StringComparison.Ordinal);
        Assert.Contains("\"dns_resolvers\":[\"1.1.1.1\",\"8.8.8.8\"]", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Untyped_and_typed_config_reads_return_null_on_a_404()
    {
        async Task AssertNullOn404<T>(Func<BastionVaultClient, Task<T?>> call)
            where T : class
        {
            FakeTransport transport = new();
            transport.EnqueueResponse(404);
            BastionVaultClient client = BuildClient(transport);
            Assert.Null(await call(client));
        }

        await AssertNullOn404(c => c.Pki.ReadUrlsAsync());
        await AssertNullOn404(c => c.Pki.ReadCrlConfigAsync());
        await AssertNullOn404(c => c.Pki.ReadIssuersConfigAsync());
        await AssertNullOn404(c => c.Pki.ReadIssuerAsync("iss-1"));
        await AssertNullOn404(c => c.Pki.IssuerChainAsync("iss-1"));
        await AssertNullOn404(c => c.Pki.ReadCaAsync());
        await AssertNullOn404(c => c.Pki.ReadCaChainAsync());
        await AssertNullOn404(c => c.Pki.ReadKeyAsync("key-1"));
        await AssertNullOn404(c => c.Pki.TidyStatusAsync());
        await AssertNullOn404(c => c.Pki.ReadAutoTidyAsync());
        await AssertNullOn404(c => c.Pki.Acme.ReadConfigAsync());
        // RF-3 (M9 slice c handback): slice c's own map-returning reads shared this branch untested.
        await AssertNullOn404(c => c.Pki.Csr.ReadAsync("csr-1"));
        await AssertNullOn404(c => c.Pki.SignRequests.ReadAsync("sr-1"));
    }

    [Fact]
    public async Task Untyped_writes_return_the_bodyless_response_on_a_204()
    {
        async Task AssertNotThrowingOn204(Func<BastionVaultClient, Task> call)
        {
            FakeTransport transport = new();
            transport.EnqueueResponse(204);
            BastionVaultClient client = BuildClient(transport);
            await call(client);
        }

        await AssertNotThrowingOn204(c => c.Pki.ConfigureCaAsync("pem-bundle"));
        await AssertNotThrowingOn204(c => c.Pki.WriteIssuerAsync("iss-1", new PkiIssuerWrite()));
        await AssertNotThrowingOn204(c => c.Pki.ExportIssuerAsync("iss-1"));
        await AssertNotThrowingOn204(c => c.Pki.ImportKeyAsync(new SecretString("k")));
        // RF-3 (M9 slice c handback): slice c's own map-returning writes shared this branch untested.
        await AssertNotThrowingOn204(c => c.Pki.Csr.SetSignedAsync("csr-1", "cert"));
        await AssertNotThrowingOn204(c => c.Pki.SignRequests.ImportAsync("csr-body"));
        await AssertNotThrowingOn204(c => c.Pki.SignRequests.PreflightAsync("sr-1"));
        await AssertNotThrowingOn204(c => c.Pki.SignRequests.ApproveAsync("sr-1", "web-server"));
        await AssertNotThrowingOn204(c => c.Pki.SignRequests.ApproveVerbatimAsync("sr-1"));
    }

    // ---------------------------------------------------------------- outbound CSR queue (M9 slice c)

    [Fact]
    [Requirement("PKI-002")]
    [Trait("Requirement", "PKI-002")]
    public async Task CsrGenerate_serialises_every_field_and_redacts_an_exported_private_key()
    {
        // D-M9-10/D-M9-17: transcribed whole-set from Pki.GenerateIntermediate's response shape.
        const string keyMaterial = "-----BEGIN PRIVATE KEY-----\nSECRETCSRKEYMATERIAL\n-----END PRIVATE KEY-----\n";
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            "{\"data\":{\"csr\":\"-----BEGIN CERTIFICATE REQUEST-----\\nX\\n-----END CERTIFICATE REQUEST-----\\n\",\"key_id\":\"key-1\",\"private_key\":\""
            + keyMaterial.Replace("\n", "\\n", StringComparison.Ordinal) + "\",\"private_key_type\":\"ec\"}}"));
        BastionVaultClient client = BuildClient(transport);

        PkiGeneratedCsr result = await client.Pki.Csr.GenerateAsync(
            role: "web-server",
            commonName: "example.com",
            altNames: ["a.example.com", "b.example.com"],
            ipSans: ["10.0.0.1"],
            emailSans: ["ops@example.com"],
            keyRef: "key-ref-1",
            exported: true,
            exportable: true);

        Assert.Equal("key-1", result.KeyId);
        Assert.Equal("ec", result.PrivateKeyType);
        // PKI-001: verbatim when revealed.
        Assert.Contains("SECRETCSRKEYMATERIAL", result.PrivateKey!.Reveal(), StringComparison.Ordinal);
        // PKI-002: never through ToString().
        Assert.Equal("[REDACTED]", result.PrivateKey.ToString());
        Assert.DoesNotContain("SECRETCSRKEYMATERIAL", result.PrivateKey.ToString(), StringComparison.Ordinal);

        TransportRequest request = transport.Requests[0];
        Assert.Equal("POST", request.Method);
        Assert.EndsWith("/v1/pki/csr/generate", request.Uri.AbsoluteUri, StringComparison.Ordinal);
        string body = Encoding.UTF8.GetString(request.Body.Span);
        Assert.Contains("\"role\":\"web-server\"", body, StringComparison.Ordinal);
        Assert.Contains("\"common_name\":\"example.com\"", body, StringComparison.Ordinal);
        Assert.Contains("\"alt_names\":\"a.example.com,b.example.com\"", body, StringComparison.Ordinal);
        Assert.Contains("\"ip_sans\":\"10.0.0.1\"", body, StringComparison.Ordinal);
        Assert.Contains("\"email_sans\":\"ops@example.com\"", body, StringComparison.Ordinal);
        Assert.Contains("\"key_ref\":\"key-ref-1\"", body, StringComparison.Ordinal);
        Assert.Contains("\"exported\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"exportable\":true", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CsrGenerate_omits_absent_fields_and_returns_no_private_key_when_not_exported()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"csr":"-----BEGIN CERTIFICATE REQUEST-----\nX\n-----END CERTIFICATE REQUEST-----\n"}}"""));
        BastionVaultClient client = BuildClient(transport);

        PkiGeneratedCsr result = await client.Pki.Csr.GenerateAsync();

        Assert.Null(result.KeyId);
        Assert.Null(result.PrivateKey);
        Assert.Null(result.PrivateKeyType);
        Assert.Equal("{}", Encoding.UTF8.GetString(transport.Requests[0].Body.Span));
    }

    [Fact]
    [Requirement("PKI-001")]
    [Trait("Requirement", "PKI-001")]
    public async Task CsrGenerate_raises_a_protocol_error_on_a_204_with_no_body()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Pki.Csr.GenerateAsync());

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    public async Task CsrGenerate_raises_a_protocol_error_when_the_response_has_no_csr_field()
    {
        // PkiWire.ReadGeneratedCsr's missing-csr throw.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Pki.Csr.GenerateAsync());

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    public async Task CsrListInfo_raises_a_protocol_error_when_the_response_has_no_data()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Pki.Csr.ListInfoAsync());

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    public async Task CsrListInfo_raises_a_protocol_error_when_keys_and_records_lengths_differ()
    {
        // PAG-005's mismatch guard (PkiWire.ReadRawInfoPage, shared with sign-request-info).
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"keys":["a","b"],"records":[{}]}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Pki.Csr.ListInfoAsync());

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    public async Task CsrList_and_CsrRead_and_CsrDelete_and_CsrSetSigned()
    {
        FakeTransport listTransport = new();
        listTransport.EnqueueResponse(200, body: Json("""{"data":{"keys":["csr-1","csr-2"]}}"""));
        BastionVaultClient listClient = BuildClient(listTransport);
        Assert.Equal(["csr-1", "csr-2"], await listClient.Pki.Csr.ListAsync());
        Assert.Equal("LIST", listTransport.Requests[0].Method);
        Assert.EndsWith("/v1/pki/csr/", listTransport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport readTransport = new();
        readTransport.EnqueueResponse(200, body: Json("""{"data":{"csr":"x"}}"""));
        BastionVaultClient readClient = BuildClient(readTransport);
        Assert.NotNull(await readClient.Pki.Csr.ReadAsync("csr-1"));
        Assert.EndsWith("/v1/pki/csr/csr-1", readTransport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport deleteTransport = new();
        deleteTransport.EnqueueResponse(204);
        BastionVaultClient deleteClient = BuildClient(deleteTransport);
        await deleteClient.Pki.Csr.DeleteAsync("csr-1");
        Assert.Equal("DELETE", deleteTransport.Requests[0].Method);
        Assert.EndsWith("/v1/pki/csr/csr-1", deleteTransport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport setSignedTransport = new();
        setSignedTransport.EnqueueResponse(200, body: Json("""{"data":{}}"""));
        BastionVaultClient setSignedClient = BuildClient(setSignedTransport);
        _ = await setSignedClient.Pki.Csr.SetSignedAsync("csr-1", "-----BEGIN CERTIFICATE-----\nX\n-----END CERTIFICATE-----\n");
        string setSignedBody = Encoding.UTF8.GetString(setSignedTransport.Requests[0].Body.Span);
        Assert.Contains("\"certificate\":\"-----BEGIN CERTIFICATE-----", setSignedBody, StringComparison.Ordinal);
        Assert.Equal("POST", setSignedTransport.Requests[0].Method);
        Assert.EndsWith("/v1/pki/csr/csr-1/set-signed", setSignedTransport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("PAG-001")]
    [Trait("Requirement", "PAG-001")]
    public async Task CsrListInfo_pins_v2_with_raw_records()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"keys":["csr-1"],"records":[{"role":"web-server","common_name":"example.com"}],"total":1,"truncated":false}"""));
        BastionVaultClient client = BuildClient(transport);

        Page<IReadOnlyDictionary<string, System.Text.Json.JsonElement>> page = await client.Pki.Csr.ListInfoAsync();

        Assert.Equal(["csr-1"], page.Keys);
        Assert.Equal("example.com", page.Records[0]["common_name"].GetString());
        // D-M9-31: pinned to /v2 regardless of ApiPrefix (v1 here); reverses D-M9-7.
        Assert.EndsWith("/v2/pki/csr-info?limit=100", transport.Requests[0].Uri.ToString(), StringComparison.Ordinal);

        // RF-2 (M9 slice c handback): PAG-001's reject arm was untested on this route.
        BastionVaultException tooLow = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Pki.Csr.ListInfoAsync(limit: 0));
        Assert.Equal(ErrorCodes.InputOutOfRange, tooLow.Code);

        BastionVaultException tooHigh = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Pki.Csr.ListInfoAsync(limit: 501));
        Assert.Equal(ErrorCodes.InputOutOfRange, tooHigh.Code);
    }

    [Fact]
    [Requirement("PAG-004")]
    [Trait("Requirement", "PAG-004")]
    public async Task CsrListInfoAllAsync_walks_every_page_in_cursor_order()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"keys":["csr-1"],"records":[{"role":"a"}],"total":2,"next":"csr-1","truncated":true}"""));
        transport.EnqueueResponse(200, body: Json(
            """{"keys":["csr-2"],"records":[{"role":"b"}],"total":2,"next":"","truncated":false}"""));
        BastionVaultClient client = BuildClient(transport);

        List<string> keys = [];
        await foreach (KeyValuePair<string, IReadOnlyDictionary<string, System.Text.Json.JsonElement>> entry in client.Pki.Csr.ListInfoAllAsync())
        {
            keys.Add(entry.Key);
        }

        Assert.Equal(["csr-1", "csr-2"], keys);
        Assert.EndsWith("after=csr-1&limit=100", transport.Requests[1].Uri.ToString(), StringComparison.Ordinal);

        // RF-2 (M9 slice c handback): PAG-004's 5000-cap arm was untested on this route.
        FakeTransport cappedTransport = new();
        cappedTransport.EnqueueResponse(200, body: Json(
            """{"keys":["csr-1","csr-2"],"records":[{"role":"a"},{"role":"b"}],"total":100,"next":"csr-2","truncated":true}"""));
        BastionVaultClient cappedClient = BuildClient(cappedTransport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(async () =>
        {
            await foreach (KeyValuePair<string, IReadOnlyDictionary<string, System.Text.Json.JsonElement>> _ in cappedClient.Pki.Csr.ListInfoAllAsync(maxRecords: 1))
            {
            }
        }).ConfigureAwait(false);

        Assert.Equal(ErrorCodes.InputIterationCapExceeded, failure.Code);
        Assert.Equal(100, failure.Details["total"]);
        Assert.Equal(1, failure.Details["maxRecords"]);
    }

    // ---------------------------------------------------------------- inbound sign-request queue (M9 slice c)

    [Fact]
    public async Task SignRequestsImport_sends_the_optional_fields_only_when_given()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"id":"sr-1"}}"""));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Pki.SignRequests.ImportAsync(
            "-----BEGIN CERTIFICATE REQUEST-----\nX\n-----END CERTIFICATE REQUEST-----\n",
            requester: "alice", notes: "urgent", suggestedRole: "web-server", allowDuplicate: true);

        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains("\"csr\":\"-----BEGIN CERTIFICATE REQUEST", body, StringComparison.Ordinal);
        Assert.Contains("\"requester\":\"alice\"", body, StringComparison.Ordinal);
        Assert.Contains("\"notes\":\"urgent\"", body, StringComparison.Ordinal);
        Assert.Contains("\"suggested_role\":\"web-server\"", body, StringComparison.Ordinal);
        Assert.Contains("\"allow_duplicate\":true", body, StringComparison.Ordinal);
        Assert.EndsWith("/v1/pki/sign-request/import", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport minimalTransport = new();
        minimalTransport.EnqueueResponse(200, body: Json("""{"data":{}}"""));
        BastionVaultClient minimalClient = BuildClient(minimalTransport);
        _ = await minimalClient.Pki.SignRequests.ImportAsync("csr-body");
        Assert.Equal("{\"csr\":\"csr-body\"}", Encoding.UTF8.GetString(minimalTransport.Requests[0].Body.Span));
    }

    [Fact]
    public async Task SignRequestsListInfo_raises_a_protocol_error_when_the_response_has_no_data()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Pki.SignRequests.ListInfoAsync());

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    public async Task SignRequestsList_and_Read_and_Delete_and_Preflight()
    {
        FakeTransport listTransport = new();
        listTransport.EnqueueResponse(200, body: Json("""{"data":{"keys":["sr-1"]}}"""));
        BastionVaultClient listClient = BuildClient(listTransport);
        Assert.Equal(["sr-1"], await listClient.Pki.SignRequests.ListAsync());
        Assert.Equal("LIST", listTransport.Requests[0].Method);
        Assert.EndsWith("/v1/pki/sign-request/", listTransport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport readTransport = new();
        readTransport.EnqueueResponse(200, body: Json("""{"data":{"csr":"x"}}"""));
        BastionVaultClient readClient = BuildClient(readTransport);
        Assert.NotNull(await readClient.Pki.SignRequests.ReadAsync("sr-1"));
        Assert.EndsWith("/v1/pki/sign-request/sr-1", readTransport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport deleteTransport = new();
        deleteTransport.EnqueueResponse(204);
        BastionVaultClient deleteClient = BuildClient(deleteTransport);
        await deleteClient.Pki.SignRequests.DeleteAsync("sr-1");
        Assert.Equal("DELETE", deleteTransport.Requests[0].Method);
        Assert.EndsWith("/v1/pki/sign-request/sr-1", deleteTransport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport preflightTransport = new();
        preflightTransport.EnqueueResponse(200, body: Json("""{"data":{"ok":true}}"""));
        BastionVaultClient preflightClient = BuildClient(preflightTransport);
        Assert.NotNull(await preflightClient.Pki.SignRequests.PreflightAsync("sr-1"));
        Assert.Equal("POST", preflightTransport.Requests[0].Method);
        Assert.EndsWith("/v1/pki/sign-request/sr-1/preflight", preflightTransport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal(0, preflightTransport.Requests[0].Body.Length);
    }

    [Fact]
    [Requirement("PAG-001")]
    [Trait("Requirement", "PAG-001")]
    public async Task SignRequestsListInfo_pins_v2_with_raw_records()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"keys":["sr-1"],"records":[{"requester":"alice"}],"total":1,"truncated":false}"""));
        BastionVaultClient client = BuildClient(transport);

        Page<IReadOnlyDictionary<string, System.Text.Json.JsonElement>> page = await client.Pki.SignRequests.ListInfoAsync();

        Assert.Equal(["sr-1"], page.Keys);
        Assert.Equal("alice", page.Records[0]["requester"].GetString());
        // D-M9-31: pinned to /v2 regardless of ApiPrefix (v1 here); reverses D-M9-7.
        Assert.EndsWith("/v2/pki/sign-request-info?limit=100", transport.Requests[0].Uri.ToString(), StringComparison.Ordinal);

        // RF-2 (M9 slice c handback): PAG-001's reject arm was untested on this route.
        BastionVaultException tooLow = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Pki.SignRequests.ListInfoAsync(limit: 0));
        Assert.Equal(ErrorCodes.InputOutOfRange, tooLow.Code);

        BastionVaultException tooHigh = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Pki.SignRequests.ListInfoAsync(limit: 501));
        Assert.Equal(ErrorCodes.InputOutOfRange, tooHigh.Code);
    }

    [Fact]
    [Requirement("PAG-004")]
    [Trait("Requirement", "PAG-004")]
    public async Task SignRequestsListInfoAllAsync_walks_every_page_in_cursor_order()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"keys":["sr-1"],"records":[{"requester":"a"}],"total":2,"next":"sr-1","truncated":true}"""));
        transport.EnqueueResponse(200, body: Json(
            """{"keys":["sr-2"],"records":[{"requester":"b"}],"total":2,"next":"","truncated":false}"""));
        BastionVaultClient client = BuildClient(transport);

        List<string> keys = [];
        await foreach (KeyValuePair<string, IReadOnlyDictionary<string, System.Text.Json.JsonElement>> entry in client.Pki.SignRequests.ListInfoAllAsync())
        {
            keys.Add(entry.Key);
        }

        Assert.Equal(["sr-1", "sr-2"], keys);
        Assert.EndsWith("after=sr-1&limit=100", transport.Requests[1].Uri.ToString(), StringComparison.Ordinal);

        // RF-2 (M9 slice c handback): PAG-004's 5000-cap arm was untested on this route.
        FakeTransport cappedTransport = new();
        cappedTransport.EnqueueResponse(200, body: Json(
            """{"keys":["sr-1","sr-2"],"records":[{"requester":"a"},{"requester":"b"}],"total":100,"next":"sr-2","truncated":true}"""));
        BastionVaultClient cappedClient = BuildClient(cappedTransport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(async () =>
        {
            await foreach (KeyValuePair<string, IReadOnlyDictionary<string, System.Text.Json.JsonElement>> _ in cappedClient.Pki.SignRequests.ListInfoAllAsync(maxRecords: 1))
            {
            }
        }).ConfigureAwait(false);

        Assert.Equal(ErrorCodes.InputIterationCapExceeded, failure.Code);
        Assert.Equal(100, failure.Details["total"]);
        Assert.Equal(1, failure.Details["maxRecords"]);
    }

    [Fact]
    public async Task SignRequestsApprove_writes_role_and_flattens_overrides_next_to_it()
    {
        // 09-pki-engine.md:103 names `overrides?` with no field list — D-M1c-25 forbids guessing
        // one, so the caller-supplied map is written flat rather than typed or nested.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"certificate":"x"}}"""));
        BastionVaultClient client = BuildClient(transport);

        Dictionary<string, System.Text.Json.JsonElement> overrides = new(StringComparer.Ordinal)
        {
            ["ttl"] = System.Text.Json.JsonDocument.Parse("3600").RootElement,
            ["common_name"] = System.Text.Json.JsonDocument.Parse("\"example.com\"").RootElement,
        };

        _ = await client.Pki.SignRequests.ApproveAsync("sr-1", "web-server", overrides);

        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains("\"role\":\"web-server\"", body, StringComparison.Ordinal);
        Assert.Contains("\"ttl\":3600", body, StringComparison.Ordinal);
        Assert.Contains("\"common_name\":\"example.com\"", body, StringComparison.Ordinal);
        Assert.Equal("POST", transport.Requests[0].Method);
        Assert.EndsWith("/v1/pki/sign-request/sr-1/approve", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport noOverridesTransport = new();
        noOverridesTransport.EnqueueResponse(200, body: Json("""{"data":{}}"""));
        BastionVaultClient noOverridesClient = BuildClient(noOverridesTransport);
        _ = await noOverridesClient.Pki.SignRequests.ApproveAsync("sr-1", "web-server");
        Assert.Equal("{\"role\":\"web-server\"}", Encoding.UTF8.GetString(noOverridesTransport.Requests[0].Body.Span));
    }

    [Fact]
    public async Task SignRequestsApprove_rejects_an_overrides_entry_that_collides_with_role_client_side()
    {
        // RF-1 (M9 slice c handback): Utf8JsonWriter does not reject a duplicate property name, and
        // serde_json/encoding/json both take the last occurrence, so an unchecked overrides["role"]
        // would silently outrank the named role parameter on the wire. Rejected client-side before
        // any request is sent.
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        Dictionary<string, System.Text.Json.JsonElement> collidingOverrides = new(StringComparer.Ordinal)
        {
            ["role"] = System.Text.Json.JsonDocument.Parse("\"admin\"").RootElement,
        };

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Pki.SignRequests.ApproveAsync("sr-1", "web-server", collidingOverrides));

        Assert.Equal(ErrorCodes.InputInvalidArgument, exception.Code);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task SignRequestsApproveVerbatim_sends_ttl_as_integer_seconds_not_a_Go_style_string()
    {
        // TRN-031: 09-pki-engine.md:104 does not quote its ttl, so it is integer seconds, matching
        // Pki.SignVerbatimAsync's ttl rather than the quoted config-style fields (expiry, interval,
        // safety_buffer).
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"certificate":"x"}}"""));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Pki.SignRequests.ApproveVerbatimAsync("sr-1", ttl: TimeSpan.FromDays(1), issuerRef: "issuer-1");

        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains("\"ttl\":86400", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ttl\":\"", body, StringComparison.Ordinal);
        Assert.Contains("\"issuer_ref\":\"issuer-1\"", body, StringComparison.Ordinal);
        Assert.EndsWith("/v1/pki/sign-request/sr-1/approve-verbatim", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport minimalTransport = new();
        minimalTransport.EnqueueResponse(200, body: Json("""{"data":{}}"""));
        BastionVaultClient minimalClient = BuildClient(minimalTransport);
        _ = await minimalClient.Pki.SignRequests.ApproveVerbatimAsync("sr-1");
        Assert.Equal("{}", Encoding.UTF8.GetString(minimalTransport.Requests[0].Body.Span));
    }

    [Fact]
    public async Task SignRequestsReject_rejects_an_empty_or_whitespace_reason_client_side_with_no_request_sent()
    {
        // PKI-030's first limb (not tagged [Requirement]: the ID stays baselined under D-M9-11
        // because its second limb — the BV-QUOTA-002 queue-cap message — is not implementable from
        // any document, and tagging this test would flip the ID to "covered" against that baseline).
        FakeTransport emptyTransport = new();
        BastionVaultClient emptyClient = BuildClient(emptyTransport);
        BastionVaultException emptyException = await Assert.ThrowsAsync<BastionVaultException>(
            () => emptyClient.Pki.SignRequests.RejectAsync("sr-1", string.Empty));
        Assert.Equal(ErrorCodes.InputInvalidArgument, emptyException.Code);
        Assert.Empty(emptyTransport.Requests);

        FakeTransport whitespaceTransport = new();
        BastionVaultClient whitespaceClient = BuildClient(whitespaceTransport);
        BastionVaultException whitespaceException = await Assert.ThrowsAsync<BastionVaultException>(
            () => whitespaceClient.Pki.SignRequests.RejectAsync("sr-1", "   "));
        Assert.Equal(ErrorCodes.InputInvalidArgument, whitespaceException.Code);
        Assert.Empty(whitespaceTransport.Requests);

        FakeTransport validTransport = new();
        validTransport.EnqueueResponse(204);
        BastionVaultClient validClient = BuildClient(validTransport);
        await validClient.Pki.SignRequests.RejectAsync("sr-1", "duplicate request");
        string body = Encoding.UTF8.GetString(validTransport.Requests[0].Body.Span);
        Assert.Contains("\"reason\":\"duplicate request\"", body, StringComparison.Ordinal);
        Assert.Equal("POST", validTransport.Requests[0].Method);
        Assert.EndsWith("/v1/pki/sign-request/sr-1/reject", validTransport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- argument guards

    [Fact]
    [Requirement("PKI-001")]
    [Trait("Requirement", "PKI-001")]
    public async Task Every_operation_rejects_a_null_or_empty_mount()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.ListRolesAsync(mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.WriteRoleAsync("r", new PkiRole(), mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.ReadRoleAsync("r", mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.DeleteRoleAsync("r", mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(
            () => client.Pki.IssueAsync("r", new IssueRequest { CommonName = "a" }, mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(
            () => client.Pki.SignAsync("r", new SignRequest { Csr = "c" }, mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.SignVerbatimAsync("c", mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.ListCertificatesAsync(mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.ListCertificatesInfoAsync(mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.ReadCertificateAsync("s", mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.DeleteCertificateAsync("s", mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.AttachKeyAsync("s", "k", mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.DetachKeyAsync("s", mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.ExportCertificateAsync("s", "pem", false, mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.ImportCertificateAsync("c", mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.RevokeAsync("s", mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.ReadCrlAsync(mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.RotateCrlAsync(mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Pki.ReadIssuerCrlAsync("i", mount: string.Empty));

        Assert.Empty(transport.Requests);
    }

    private static BastionVaultClient BuildClient(ITransport transport, Action<BastionVaultClientOptions>? configure = null)
    {
        BastionVaultClientOptions options = new()
        {
            Address = Address,
            Token = "s.FAKE-token-0000000000000000",
            Transport = transport,
            RateGate = new RateGate { RatePerSecond = 0 },
            RetryPolicy = new RetryPolicy { MaxAttempts = 1 },
        };
        configure?.Invoke(options);
        return new BastionVaultClient(options, EnvironmentSource.None);
    }

    private static ReadOnlyMemory<byte> Json(string json)
    {
        return Encoding.UTF8.GetBytes(json);
    }
}
