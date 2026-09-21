using System.Text;
using BastionVault.IntegrationSdk.Testing;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// 10 — SSH engine and SSH broker (M9 slice d, DR-0016). The wire-shape assertions for a real
/// request/response round trip live in the Appendix C <c>ssh.*</c>/<c>sshbroker.*</c> fixtures
/// (<see cref="SshFixturesTests"/>); this file covers what a single-request fixture cannot — every
/// argument-validation branch, every optional field, the <c>0644</c> file write, the <c>/v2</c> pin
/// on every broker route and its absence on every engine route, the <c>PAG-004</c> iterator, and
/// every failure path.
/// </summary>
public sealed class SshUnitTests
{
    private const string Address = "https://vault.example.com:8200";

    // ---------------------------------------------------------------- CA configuration

    [Fact]
    [Requirement("D-M9-1")]
    [Trait("Requirement", "D-M9-1")]
    public async Task ConfigureCa_serialises_generate_signing_key_default_and_returns_public_key_verbatim()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"public_key":"ssh-ed25519 AAAA CA","algorithm":"ed25519"}}"""));
        BastionVaultClient client = BuildClient(transport);

        SshCaKey ca = await client.Ssh.ConfigureCaAsync();

        Assert.Equal("ssh-ed25519 AAAA CA", ca.PublicKey);
        Assert.Equal("ed25519", ca.Algorithm);
        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Equal("""{"generate_signing_key":true}""", body);
        Assert.EndsWith("/v1/ssh/config/ca", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("D-M9-3")]
    [Trait("Requirement", "D-M9-3")]
    public async Task ConfigureCa_serialises_an_uploaded_private_key_and_algorithm_when_set()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"public_key":"pk"}}"""));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Ssh.ConfigureCaAsync(generateSigningKey: false, privateKey: new SecretString("PRIVATEKEYMATERIAL"), algorithm: "mldsa65");

        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains("\"generate_signing_key\":false", body, StringComparison.Ordinal);
        Assert.Contains("\"private_key\":\"PRIVATEKEYMATERIAL\"", body, StringComparison.Ordinal);
        Assert.Contains("\"algorithm\":\"mldsa65\"", body, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("D-M9-1")]
    [Trait("Requirement", "D-M9-1")]
    public async Task ConfigureCa_throws_envelope_mismatch_on_a_missing_public_key()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Ssh.ConfigureCaAsync());
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    public async Task ConfigureCa_throws_envelope_mismatch_when_the_response_is_absent()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Ssh.ConfigureCaAsync());
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    [Requirement("D-M9-1")]
    [Trait("Requirement", "D-M9-1")]
    public async Task ReadCa_returns_null_on_404_and_the_key_when_present()
    {
        FakeTransport notFound = new();
        notFound.EnqueueResponse(404);
        BastionVaultClient absentClient = BuildClient(notFound);
        Assert.Null(await absentClient.Ssh.ReadCaAsync());

        FakeTransport present = new();
        present.EnqueueResponse(200, body: Json("""{"data":{"public_key":"ssh-ed25519 AAAA CA"}}"""));
        BastionVaultClient client = BuildClient(present);
        SshCaKey? ca = await client.Ssh.ReadCaAsync();
        Assert.Equal("ssh-ed25519 AAAA CA", ca!.PublicKey);
        Assert.Null(ca.Algorithm);
    }

    [Fact]
    [Requirement("D-M9-1")]
    [Trait("Requirement", "D-M9-1")]
    public async Task DeleteCa_sends_a_delete_to_the_config_ca_path()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        await client.Ssh.DeleteCaAsync();

        Assert.Equal("DELETE", transport.Requests[0].Method);
        Assert.EndsWith("/v1/ssh/config/ca", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("D-M9-1")]
    [Trait("Requirement", "D-M9-1")]
    public async Task PublicKey_returns_the_wire_field_verbatim_and_throws_on_a_missing_field()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"public_key":"ssh-ed25519 AAAA authorized-keys-form"}}"""));
        BastionVaultClient client = BuildClient(transport);

        string key = await client.Ssh.PublicKeyAsync();

        Assert.Equal("ssh-ed25519 AAAA authorized-keys-form", key);
        Assert.EndsWith("/v1/ssh/public_key", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport missing = new();
        missing.EnqueueResponse(200, body: Json("""{"data":{}}"""));
        BastionVaultClient missingClient = BuildClient(missing);
        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => missingClient.Ssh.PublicKeyAsync());
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);

        FakeTransport empty = new();
        empty.EnqueueResponse(404);
        BastionVaultClient emptyClient = BuildClient(empty);
        _ = await Assert.ThrowsAsync<BastionVaultException>(() => emptyClient.Ssh.PublicKeyAsync());
    }

    [Fact]
    public async Task PublicKey_throws_envelope_mismatch_when_the_response_is_absent()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Ssh.PublicKeyAsync());
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    // ---------------------------------------------------------------- roles

    [Fact]
    [Requirement("D-M9-9")]
    [Trait("Requirement", "D-M9-9")]
    public async Task ListRoles_returns_the_wire_list_and_an_empty_list_on_404()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"keys":["user-role","host-role"]}}"""));
        BastionVaultClient client = BuildClient(transport);

        IReadOnlyList<string> roles = await client.Ssh.ListRolesAsync();

        Assert.Equal(["user-role", "host-role"], roles);
        Assert.Equal("LIST", transport.Requests[0].Method);
        Assert.EndsWith("/v1/ssh/roles/", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport emptyTransport = new();
        emptyTransport.EnqueueResponse(404);
        BastionVaultClient emptyClient = BuildClient(emptyTransport);
        Assert.Empty(await emptyClient.Ssh.ListRolesAsync());
    }

    // Deliberately carries no [Requirement] tag. The SSH role's allow-list CSV binding is an
    // inference, not a requirement: section 10 pins the CSV form only for valid_principals
    // (10-ssh-engine.md:38), and DR-0016 D-M9-29 is what settles the rest. A [Requirement]
    // attribute names a specification requirement ID and traceability.py reads it as one, so
    // citing a decision record there would be the same category error D-M9-30 forbids, in a
    // new dress. The decision is cited here instead.
    [Fact]
    public async Task WriteRole_serialises_every_field_when_set_and_omits_absent_fields()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200);
        BastionVaultClient client = BuildClient(transport);

        SshRole role = new()
        {
            KeyType = "ca",
            AlgorithmSigner = "ssh-ed25519",
            CertType = "user",
            AllowedUsers = ["ubuntu", "admin"],
            DefaultUser = "ubuntu",
            AllowedExtensions = ["permit-pty"],
            DefaultExtensions = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal)
            {
                ["permit-pty"] = System.Text.Json.JsonDocument.Parse("\"\"").RootElement,
            },
            AllowedCriticalOptions = ["force-command"],
            DefaultCriticalOptions = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal)
            {
                ["force-command"] = System.Text.Json.JsonDocument.Parse("\"/bin/true\"").RootElement,
            },
            Ttl = TimeSpan.FromHours(1),
            MaxTtl = TimeSpan.FromHours(2),
            NotBeforeDuration = TimeSpan.FromSeconds(30),
            KeyIdFormat = "{{role_name}}-{{token_display_name}}",
            CidrList = ["10.0.0.0/24"],
            ExcludeCidrList = ["10.0.0.5/32"],
            Port = 2222,
            PqcOnly = true,
        };

        await client.Ssh.WriteRoleAsync("user-role", role);

        Assert.Equal("POST", transport.Requests[0].Method);
        Assert.EndsWith("/v1/ssh/roles/user-role", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains("\"key_type\":\"ca\"", body, StringComparison.Ordinal);
        Assert.Contains("\"algorithm_signer\":\"ssh-ed25519\"", body, StringComparison.Ordinal);
        Assert.Contains("\"cert_type\":\"user\"", body, StringComparison.Ordinal);
        Assert.Contains("\"allowed_users\":\"ubuntu,admin\"", body, StringComparison.Ordinal);
        Assert.Contains("\"default_user\":\"ubuntu\"", body, StringComparison.Ordinal);
        Assert.Contains("\"allowed_extensions\":\"permit-pty\"", body, StringComparison.Ordinal);
        Assert.Contains("\"default_extensions\":{\"permit-pty\":\"\"}", body, StringComparison.Ordinal);
        Assert.Contains("\"allowed_critical_options\":\"force-command\"", body, StringComparison.Ordinal);
        Assert.Contains("\"default_critical_options\":{\"force-command\":\"/bin/true\"}", body, StringComparison.Ordinal);
        Assert.Contains("\"ttl\":3600", body, StringComparison.Ordinal);
        Assert.Contains("\"max_ttl\":7200", body, StringComparison.Ordinal);
        Assert.Contains("\"not_before_duration\":30", body, StringComparison.Ordinal);
        Assert.Contains("\"key_id_format\":\"{{role_name}}-{{token_display_name}}\"", body, StringComparison.Ordinal);
        Assert.Contains("\"cidr_list\":\"10.0.0.0/24\"", body, StringComparison.Ordinal);
        Assert.Contains("\"exclude_cidr_list\":\"10.0.0.5/32\"", body, StringComparison.Ordinal);
        Assert.Contains("\"port\":2222", body, StringComparison.Ordinal);
        Assert.Contains("\"pqc_only\":true", body, StringComparison.Ordinal);

        FakeTransport minimalTransport = new();
        minimalTransport.EnqueueResponse(200);
        BastionVaultClient minimalClient = BuildClient(minimalTransport);
        await minimalClient.Ssh.WriteRoleAsync("empty-role", new SshRole());
        Assert.Equal("{}", Encoding.UTF8.GetString(minimalTransport.Requests[0].Body.Span));
    }

    [Fact]
    [Requirement("D-M9-9")]
    [Trait("Requirement", "D-M9-9")]
    public async Task ReadRole_returns_null_on_404_and_round_trips_every_field()
    {
        FakeTransport notFound = new();
        notFound.EnqueueResponse(404);
        BastionVaultClient absentClient = BuildClient(notFound);
        Assert.Null(await absentClient.Ssh.ReadRoleAsync("ghost"));

        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""
        {
          "data": {
            "key_type": "otp",
            "algorithm_signer": "ssh-ed25519",
            "cert_type": "host",
            "allowed_users": "ubuntu",
            "default_user": "ubuntu",
            "allowed_extensions": "",
            "default_extensions": {"permit-pty": ""},
            "allowed_critical_options": "force-command",
            "default_critical_options": {},
            "ttl": 3600,
            "max_ttl": 7200,
            "not_before_duration": 30,
            "key_id_format": "{{role_name}}",
            "cidr_list": "10.0.0.0/24",
            "exclude_cidr_list": "",
            "port": 22,
            "pqc_only": false
          }
        }
        """));
        BastionVaultClient client = BuildClient(transport);

        SshRole? role = await client.Ssh.ReadRoleAsync("user-role");

        Assert.Equal("otp", role!.KeyType);
        Assert.Equal("ssh-ed25519", role.AlgorithmSigner);
        Assert.Equal("host", role.CertType);
        Assert.Equal(["ubuntu"], role.AllowedUsers);
        Assert.Equal("ubuntu", role.DefaultUser);
        Assert.Equal([], role.AllowedExtensions);
        Assert.Equal(["force-command"], role.AllowedCriticalOptions);
        Assert.Equal(TimeSpan.FromHours(1), role.Ttl);
        Assert.Equal(TimeSpan.FromHours(2), role.MaxTtl);
        Assert.Equal(TimeSpan.FromSeconds(30), role.NotBeforeDuration);
        Assert.Equal("{{role_name}}", role.KeyIdFormat);
        Assert.Equal(["10.0.0.0/24"], role.CidrList);
        Assert.Equal([], role.ExcludeCidrList);
        Assert.Equal(22, role.Port);
        Assert.False(role.PqcOnly);
    }

    // No [Requirement] tag, for the reason given above WriteRole_serialises_every_field_...:
    // DR-0016 D-M9-29 settles this tolerant read, and no specification requirement pins it.
    [Fact]
    public async Task ReadRole_accepts_a_json_array_for_an_allow_list_field_and_treats_an_unexpected_wire_type_as_absent()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""
        {
          "data": {
            "allowed_users": ["ubuntu", "admin"],
            "allowed_extensions": 7,
            "pqc_only": "yes"
          }
        }
        """));
        BastionVaultClient client = BuildClient(transport);

        SshRole? role = await client.Ssh.ReadRoleAsync("user-role");

        Assert.Equal(["ubuntu", "admin"], role!.AllowedUsers);
        Assert.Null(role.AllowedExtensions);
        Assert.Null(role.PqcOnly);
    }

    [Fact]
    [Requirement("D-M9-9")]
    [Trait("Requirement", "D-M9-9")]
    public async Task DeleteRole_sends_a_delete_to_the_role_path()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        await client.Ssh.DeleteRoleAsync("user-role");

        Assert.Equal("DELETE", transport.Requests[0].Method);
        Assert.EndsWith("/v1/ssh/roles/user-role", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("D-M9-7")]
    [Trait("Requirement", "D-M9-7")]
    public async Task ListRolesInfo_is_unpinned_and_validates_limit_and_zips_records()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""
        {"data":{"keys":["a"],"records":[{"key_type":"ca"}],"total":1,"next":null,"truncated":false}}
        """));
        BastionVaultClient client = BuildClient(transport);

        Page<SshRole> page = await client.Ssh.ListRolesInfoAsync();

        Assert.Equal(["a"], page.Keys);
        Assert.Equal("ca", page.Records[0].KeyType);
        Assert.Null(page.Next);
        Assert.False(page.Truncated);
        // D-M9-7: unpinned, so it follows ApiPrefix (v1 here), never a literal v2/.
        Assert.EndsWith("/v1/ssh/roles-info?limit=100", transport.Requests[0].Uri.ToString(), StringComparison.Ordinal);

        BastionVaultException outOfRange = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Ssh.ListRolesInfoAsync(limit: 0));
        Assert.Equal(ErrorCodes.InputOutOfRange, outOfRange.Code);
    }

    [Fact]
    [Requirement("PAG-005")]
    [Trait("Requirement", "PAG-005")]
    public async Task ListRolesInfo_passes_after_verbatim_and_throws_on_a_zip_mismatch()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"keys":["a","b"],"records":[{"key_type":"ca"}],"total":2,"truncated":false}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Ssh.ListRolesInfoAsync(after: "cursor-1"));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
        Assert.Contains("after=cursor-1", transport.Requests[0].Uri.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListRolesInfo_treats_an_absent_records_field_as_an_empty_page()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"keys":[]}}"""));
        BastionVaultClient client = BuildClient(transport);

        Page<SshRole> page = await client.Ssh.ListRolesInfoAsync();

        Assert.Empty(page.Keys);
        Assert.Empty(page.Records);
    }

    [Fact]
    public async Task ListRolesInfo_throws_envelope_mismatch_when_a_record_element_is_not_an_object()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"keys":["a"],"records":["not-an-object"]}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Ssh.ListRolesInfoAsync());

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    public async Task ListRolesInfo_throws_envelope_mismatch_when_the_response_is_absent()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Ssh.ListRolesInfoAsync());

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    [Requirement("D-M9-8")]
    [Trait("Requirement", "D-M9-8")]
    public async Task ListRolesInfoAllAsync_walks_every_page_in_cursor_order()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"keys":["a"],"records":[{"key_type":"ca"}],"total":2,"next":"cursor-1","truncated":true}"""));
        transport.EnqueueResponse(200, body: Json("""{"keys":["b"],"records":[{"key_type":"otp"}],"total":2,"truncated":false}"""));
        BastionVaultClient client = BuildClient(transport);

        List<KeyValuePair<string, SshRole>> entries = [];
        await foreach (KeyValuePair<string, SshRole> entry in client.Ssh.ListRolesInfoAllAsync())
        {
            entries.Add(entry);
        }

        Assert.Equal(["a", "b"], entries.Select(entry => entry.Key));
        Assert.Equal("ca", entries[0].Value.KeyType);
        Assert.Equal("otp", entries[1].Value.KeyType);
        Assert.DoesNotContain("cursor-1", transport.Requests[0].Uri.ToString(), StringComparison.Ordinal);
        Assert.Contains("after=cursor-1", transport.Requests[1].Uri.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("D-M9-8")]
    [Trait("Requirement", "D-M9-8")]
    public async Task ListRolesInfoAllAsync_stops_at_MaxRecords_with_BV_INPUT_005()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"keys":["a","b","c"],"records":[{"key_type":"ca"},{"key_type":"ca"},{"key_type":"ca"}],"total":3,"truncated":false}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(async () =>
        {
            await foreach (KeyValuePair<string, SshRole> _ in client.Ssh.ListRolesInfoAllAsync(maxRecords: 2))
            {
            }
        });

        Assert.Equal(ErrorCodes.InputIterationCapExceeded, exception.Code);
        Assert.Equal(3, exception.Details["total"]);
        Assert.Equal(2, exception.Details["maxRecords"]);
    }

    // ---------------------------------------------------------------- signing (CA mode)

    [Fact]
    [Requirement("SSH-001")]
    [Trait("Requirement", "SSH-001")]
    public async Task Sign_rejects_an_empty_or_whitespace_public_key_client_side_with_no_request_sent()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException emptyException = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Ssh.SignAsync("user-role", new SshSignRequest { PublicKey = string.Empty }));
        Assert.Equal(ErrorCodes.InputInvalidArgument, emptyException.Code);

        BastionVaultException whitespaceException = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Ssh.SignAsync("user-role", new SshSignRequest { PublicKey = "   " }));
        Assert.Equal(ErrorCodes.InputInvalidArgument, whitespaceException.Code);

        Assert.Empty(transport.Requests);
    }

    [Fact]
    [Requirement("TRN-031")]
    [Trait("Requirement", "TRN-031")]
    public async Task Sign_serialises_valid_principals_as_csv_and_returns_the_certificate_line_verbatim()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""
        {"data":{"signed_key":"ssh-ed25519-cert-v01@openssh.com AAAA","serial_number":"7","algorithm":"ssh-ed25519"}}
        """));
        BastionVaultClient client = BuildClient(transport);

        SignedSshCertificate result = await client.Ssh.SignAsync("user-role", new SshSignRequest
        {
            PublicKey = "ssh-ed25519 AAAA user@host",
            ValidPrincipals = ["ubuntu", "admin"],
            Ttl = TimeSpan.FromMinutes(5),
            CertType = "user",
            KeyId = "web-01",
        });

        Assert.Equal("ssh-ed25519-cert-v01@openssh.com AAAA", result.SignedKey);
        Assert.Equal("7", result.SerialNumber);
        Assert.Equal("ssh-ed25519", result.Algorithm);
        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains("\"valid_principals\":\"ubuntu,admin\"", body, StringComparison.Ordinal);
        Assert.Contains("\"ttl\":300", body, StringComparison.Ordinal);
        Assert.EndsWith("/v1/ssh/sign/user-role", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sign_throws_envelope_mismatch_when_signed_key_is_missing()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"serial_number":"7"}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Ssh.SignAsync("user-role", new SshSignRequest { PublicKey = "ssh-ed25519 AAAA" }));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    public async Task Sign_throws_envelope_mismatch_when_serial_number_is_missing()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"signed_key":"ssh-ed25519-cert-v01@openssh.com AAAA"}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Ssh.SignAsync("user-role", new SshSignRequest { PublicKey = "ssh-ed25519 AAAA" }));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    public async Task Sign_throws_envelope_mismatch_when_the_response_is_absent()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Ssh.SignAsync("user-role", new SshSignRequest { PublicKey = "ssh-ed25519 AAAA" }));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    [Requirement("SSH-002")]
    [Requirement("D-M9-12")]
    [Trait("Requirement", "SSH-002")]
    public void WriteCertificateFile_writes_the_cert_pub_suffix_with_0644_and_makes_no_request()
    {
        string basePath = Path.Combine(Path.GetTempPath(), $"bastionvault-ssh-{Guid.NewGuid():N}");
        try
        {
            FakeTransport transport = new();
            BastionVaultClient client = BuildClient(transport);

            client.Ssh.WriteCertificateFile("ssh-ed25519-cert-v01@openssh.com AAAA", basePath);

            string expectedPath = $"{basePath}-cert.pub";
            Assert.True(File.Exists(expectedPath));
            Assert.Equal("ssh-ed25519-cert-v01@openssh.com AAAA", File.ReadAllText(expectedPath));
            Assert.Empty(transport.Requests);

            if (!OperatingSystem.IsWindows())
            {
                UnixFileMode mode = File.GetUnixFileMode(expectedPath);
                Assert.Equal(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
                    mode);
            }
        }
        finally
        {
            string expectedPath = $"{basePath}-cert.pub";
            if (File.Exists(expectedPath))
            {
                File.Delete(expectedPath);
            }
        }
    }

    [Fact]
    public void WriteCertificateFile_rejects_empty_arguments()
    {
        BastionVaultClient client = BuildClient(new FakeTransport());

        _ = Assert.Throws<ArgumentException>(() => client.Ssh.WriteCertificateFile(string.Empty, "/tmp/id_rsa"));
        _ = Assert.Throws<ArgumentException>(() => client.Ssh.WriteCertificateFile("cert-line", string.Empty));
    }

    [Fact]
    [Requirement("D-M9-12")]
    [Trait("Requirement", "D-M9-12")]
    public void WriteCertificateFile_falls_back_to_a_plain_write_when_the_creation_seam_reports_PlatformNotSupported()
    {
        string basePath = Path.Combine(Path.GetTempPath(), $"bastionvault-ssh-{Guid.NewGuid():N}");
        string expectedPath = $"{basePath}-cert.pub";
        Action<string, string> original = BastionVault.IntegrationSdk.Internal.SshFiles.CreateWithMode;
        try
        {
            BastionVault.IntegrationSdk.Internal.SshFiles.CreateWithMode = (_, _) => throw new PlatformNotSupportedException();
            BastionVaultClient client = BuildClient(new FakeTransport());

            client.Ssh.WriteCertificateFile("ssh-ed25519-cert-v01@openssh.com AAAA", basePath);

            Assert.True(File.Exists(expectedPath));
            Assert.Equal("ssh-ed25519-cert-v01@openssh.com AAAA", File.ReadAllText(expectedPath));
        }
        finally
        {
            BastionVault.IntegrationSdk.Internal.SshFiles.CreateWithMode = original;
            if (File.Exists(expectedPath))
            {
                File.Delete(expectedPath);
            }
        }
    }

    [Fact]
    [Requirement("ERR-036")]
    [Trait("Requirement", "ERR-036")]
    public void WriteCertificateFile_surfaces_a_coded_error_from_the_catalogue_when_the_creation_seam_cannot_write()
    {
        string basePath = Path.Combine(Path.GetTempPath(), $"bastionvault-ssh-{Guid.NewGuid():N}");
        Action<string, string> original = BastionVault.IntegrationSdk.Internal.SshFiles.CreateWithMode;
        try
        {
            BastionVault.IntegrationSdk.Internal.SshFiles.CreateWithMode = (_, _) => throw new UnauthorizedAccessException();
            BastionVaultClient client = BuildClient(new FakeTransport());

            BastionVaultException exception = Assert.Throws<BastionVaultException>(
                () => client.Ssh.WriteCertificateFile("ssh-ed25519-cert-v01@openssh.com AAAA", basePath));

            Assert.Equal(ErrorCodes.ConfigTokenFileNotWritable, exception.Code);
            Assert.Equal(ErrorCatalog.Require(ErrorCodes.ConfigTokenFileNotWritable).Message, exception.Message);
            Assert.False(File.Exists($"{basePath}-cert.pub"));
        }
        finally
        {
            BastionVault.IntegrationSdk.Internal.SshFiles.CreateWithMode = original;
        }
    }

    // ---------------------------------------------------------------- one-time passwords (OTP mode)

    [Fact]
    [Requirement("SSH-003")]
    [Trait("Requirement", "SSH-003")]
    public async Task Creds_rejects_a_malformed_ip_client_side_with_no_request_sent()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Ssh.CredsAsync("deploy", "not-an-ip"));

        Assert.Equal(ErrorCodes.InputInvalidArgument, exception.Code);
        Assert.Empty(transport.Requests);
    }

    [Theory]
    [InlineData("203.0.113.5")]
    [InlineData("2001:db8::1")]
    [Requirement("SSH-003")]
    [Trait("Requirement", "SSH-003")]
    public async Task Creds_accepts_ipv4_and_ipv6_literals_and_redacts_the_returned_otp(string ip)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""
        {"data":{"key":"OTPSECRETVALUE","key_type":"otp","username":"ubuntu","ip":"__IP__","port":22,"ttl":300}}
        """.Replace("__IP__", ip)));
        BastionVaultClient client = BuildClient(transport);

        SshCredentials creds = await client.Ssh.CredsAsync("deploy", ip, "ubuntu", TimeSpan.FromMinutes(5));

        Assert.Contains("OTPSECRETVALUE", creds.Key.Reveal(), StringComparison.Ordinal);
        Assert.Equal("[REDACTED]", creds.Key.ToString());
        Assert.DoesNotContain("OTPSECRETVALUE", creds.Key.ToString(), StringComparison.Ordinal);
        Assert.Equal("otp", creds.KeyType);
        Assert.Equal("ubuntu", creds.Username);
        Assert.Equal(ip, creds.Ip);
        Assert.Equal(22, creds.Port);
        Assert.Equal(TimeSpan.FromMinutes(5), creds.Ttl);
        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains($"\"ip\":\"{ip}\"", body, StringComparison.Ordinal);
        Assert.Contains("\"username\":\"ubuntu\"", body, StringComparison.Ordinal);
        Assert.Contains("\"ttl\":300", body, StringComparison.Ordinal);
        Assert.EndsWith("/v1/ssh/creds/deploy", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("D-M9-3")]
    [Trait("Requirement", "D-M9-3")]
    public async Task Creds_throws_envelope_mismatch_when_key_is_missing()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"username":"ubuntu"}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Ssh.CredsAsync("deploy", "203.0.113.5"));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    public async Task Creds_throws_envelope_mismatch_when_key_type_is_missing()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"key":"OTPSECRETVALUE"}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Ssh.CredsAsync("deploy", "203.0.113.5"));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    public async Task Creds_throws_envelope_mismatch_when_username_is_missing()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"key":"OTPSECRETVALUE","key_type":"otp"}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Ssh.CredsAsync("deploy", "203.0.113.5"));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    public async Task Creds_throws_envelope_mismatch_when_ip_is_missing()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"key":"OTPSECRETVALUE","key_type":"otp","username":"ubuntu"}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Ssh.CredsAsync("deploy", "203.0.113.5"));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    public async Task Creds_throws_envelope_mismatch_when_port_is_missing()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"key":"OTPSECRETVALUE","key_type":"otp","username":"ubuntu","ip":"203.0.113.5"}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Ssh.CredsAsync("deploy", "203.0.113.5"));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    public async Task Creds_throws_envelope_mismatch_when_the_response_is_absent()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Ssh.CredsAsync("deploy", "203.0.113.5"));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    [Requirement("D-M9-20")]
    [Trait("Requirement", "D-M9-20")]
    public async Task Verify_sends_the_otp_in_the_request_body_never_the_path_or_query()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"username":"ubuntu","ip":"203.0.113.5","role_name":"deploy","port":22}}"""));
        BastionVaultClient client = BuildClient(transport);

        SshOtpVerification? result = await client.Ssh.VerifyAsync(new SecretString("SUPERSECRETOTP"));

        Assert.Equal("ubuntu", result!.Username);
        Assert.Equal("203.0.113.5", result.Ip);
        Assert.Equal("deploy", result.RoleName);
        Assert.Equal(22, result.Port);
        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains("\"otp\":\"SUPERSECRETOTP\"", body, StringComparison.Ordinal);
        string uri = transport.Requests[0].Uri.ToString();
        Assert.DoesNotContain("SUPERSECRETOTP", uri, StringComparison.Ordinal);
        Assert.Equal("https://vault.example.com:8200/v1/ssh/verify", uri);
    }

    [Fact]
    [Requirement("D-M9-20")]
    [Trait("Requirement", "D-M9-20")]
    public async Task Verify_returns_null_when_the_response_is_absent()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(404);
        BastionVaultClient client = BuildClient(transport);

        Assert.Null(await client.Ssh.VerifyAsync(new SecretString("otp")));
    }

    [Fact]
    public async Task Verify_throws_envelope_mismatch_when_username_is_missing()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"ip":"203.0.113.5","role_name":"deploy","port":22}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Ssh.VerifyAsync(new SecretString("otp")));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    public async Task Verify_throws_envelope_mismatch_when_ip_is_missing()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"username":"ubuntu","role_name":"deploy","port":22}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Ssh.VerifyAsync(new SecretString("otp")));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    public async Task Verify_throws_envelope_mismatch_when_role_name_is_missing()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"username":"ubuntu","ip":"203.0.113.5","port":22}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Ssh.VerifyAsync(new SecretString("otp")));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    public async Task Verify_throws_envelope_mismatch_when_port_is_missing()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"username":"ubuntu","ip":"203.0.113.5","role_name":"deploy"}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Ssh.VerifyAsync(new SecretString("otp")));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    [Requirement("SSH-003")]
    [Trait("Requirement", "SSH-003")]
    public async Task Lookup_rejects_a_malformed_ip_client_side_and_returns_the_wire_roles()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Ssh.LookupAsync("also-not-an-ip"));
        Assert.Equal(ErrorCodes.InputInvalidArgument, exception.Code);
        Assert.Empty(transport.Requests);

        FakeTransport okTransport = new();
        okTransport.EnqueueResponse(200, body: Json("""{"data":{"roles":["deploy","admin"]}}"""));
        BastionVaultClient okClient = BuildClient(okTransport);
        IReadOnlyList<string> roles = await okClient.Ssh.LookupAsync("203.0.113.5", "ubuntu");
        Assert.Equal(["deploy", "admin"], roles);
        string body = Encoding.UTF8.GetString(okTransport.Requests[0].Body.Span);
        Assert.Contains("\"ip\":\"203.0.113.5\"", body, StringComparison.Ordinal);
        Assert.Contains("\"username\":\"ubuntu\"", body, StringComparison.Ordinal);

        FakeTransport emptyTransport = new();
        emptyTransport.EnqueueResponse(404);
        BastionVaultClient emptyClient = BuildClient(emptyTransport);
        Assert.Empty(await emptyClient.Ssh.LookupAsync("203.0.113.6"));
    }

    // ---------------------------------------------------------------- SSH broker

    [Fact]
    [Requirement("SSB-001")]
    [Trait("Requirement", "SSB-001")]
    public async Task Broker_global_read_write_are_pinned_to_v2_and_round_trip_the_shared_shape()
    {
        FakeTransport readTransport = new();
        readTransport.EnqueueResponse(200, body: Json("""{"data":{"login_class_default":"brokered","login_class_lock":true}}"""));
        BastionVaultClient readClient = BuildClient(readTransport);
        SshBrokerGlobalPolicy? read = await readClient.SshBroker.ReadGlobalAsync();
        Assert.Equal("brokered", read!.LoginClassDefault);
        Assert.True(read.LoginClassLock);
        Assert.StartsWith("https://vault.example.com:8200/v2/ssh-broker/policy/global", readTransport.Requests[0].Uri.ToString(), StringComparison.Ordinal);

        FakeTransport absentTransport = new();
        absentTransport.EnqueueResponse(404);
        BastionVaultClient absentClient = BuildClient(absentTransport);
        Assert.Null(await absentClient.SshBroker.ReadGlobalAsync());

        FakeTransport sparseTransport = new();
        sparseTransport.EnqueueResponse(200, body: Json("""{"data":{}}"""));
        BastionVaultClient sparseClient = BuildClient(sparseTransport);
        SshBrokerGlobalPolicy? sparse = await sparseClient.SshBroker.ReadGlobalAsync();
        Assert.Null(sparse!.LoginClassDefault);
        Assert.Null(sparse.LoginClassLock);

        FakeTransport writeTransport = new();
        writeTransport.EnqueueResponse(200);
        BastionVaultClient writeClient = BuildClient(writeTransport);
        await writeClient.SshBroker.WriteGlobalAsync(new SshBrokerGlobalPolicy { LoginClassDefault = "shared-credential", LoginClassLock = false });
        Assert.Equal("PUT", writeTransport.Requests[0].Method);
        Assert.StartsWith("https://vault.example.com:8200/v2/ssh-broker/policy/global", writeTransport.Requests[0].Uri.ToString(), StringComparison.Ordinal);
        string body = Encoding.UTF8.GetString(writeTransport.Requests[0].Body.Span);
        Assert.Contains("\"login_class_default\":\"shared-credential\"", body, StringComparison.Ordinal);
        Assert.Contains("\"login_class_lock\":false", body, StringComparison.Ordinal);

        FakeTransport minimalTransport = new();
        minimalTransport.EnqueueResponse(200);
        BastionVaultClient minimalClient = BuildClient(minimalTransport);
        await minimalClient.SshBroker.WriteGlobalAsync(new SshBrokerGlobalPolicy());
        Assert.Equal("{}", Encoding.UTF8.GetString(minimalTransport.Requests[0].Body.Span));
    }

    [Fact]
    [Requirement("SSB-001")]
    [Trait("Requirement", "SSB-001")]
    public async Task Broker_type_read_write_delete_are_pinned_to_v2()
    {
        FakeTransport readTransport = new();
        readTransport.EnqueueResponse(200, body: Json("""{"data":{"login_class":"brokered","lock":false}}"""));
        BastionVaultClient readClient = BuildClient(readTransport);
        SshBrokerTypePolicy? read = await readClient.SshBroker.ReadTypeAsync("linux");
        Assert.Equal("brokered", read!.LoginClass);
        Assert.False(read.Lock);
        Assert.StartsWith("https://vault.example.com:8200/v2/ssh-broker/policy/type/linux", readTransport.Requests[0].Uri.ToString(), StringComparison.Ordinal);

        FakeTransport writeTransport = new();
        writeTransport.EnqueueResponse(200);
        BastionVaultClient writeClient = BuildClient(writeTransport);
        await writeClient.SshBroker.WriteTypeAsync("linux", new SshBrokerTypePolicy { LoginClass = "shared-credential", Lock = true });
        Assert.Equal("PUT", writeTransport.Requests[0].Method);
        Assert.StartsWith("https://vault.example.com:8200/v2/ssh-broker/policy/type/linux", writeTransport.Requests[0].Uri.ToString(), StringComparison.Ordinal);
        Assert.Contains("\"lock\":true", Encoding.UTF8.GetString(writeTransport.Requests[0].Body.Span), StringComparison.Ordinal);

        FakeTransport deleteTransport = new();
        deleteTransport.EnqueueResponse(204);
        BastionVaultClient deleteClient = BuildClient(deleteTransport);
        await deleteClient.SshBroker.DeleteTypeAsync("linux");
        Assert.Equal("DELETE", deleteTransport.Requests[0].Method);
        Assert.StartsWith("https://vault.example.com:8200/v2/ssh-broker/policy/type/linux", deleteTransport.Requests[0].Uri.ToString(), StringComparison.Ordinal);

        FakeTransport absentTransport = new();
        absentTransport.EnqueueResponse(404);
        BastionVaultClient absentClient = BuildClient(absentTransport);
        Assert.Null(await absentClient.SshBroker.ReadTypeAsync("ghost"));

        FakeTransport sparseTransport = new();
        sparseTransport.EnqueueResponse(200, body: Json("""{"data":{}}"""));
        BastionVaultClient sparseClient = BuildClient(sparseTransport);
        SshBrokerTypePolicy? sparse = await sparseClient.SshBroker.ReadTypeAsync("linux");
        Assert.Null(sparse!.LoginClass);
        Assert.Null(sparse.Lock);

        FakeTransport minimalTransport = new();
        minimalTransport.EnqueueResponse(200);
        BastionVaultClient minimalClient = BuildClient(minimalTransport);
        await minimalClient.SshBroker.WriteTypeAsync("windows", new SshBrokerTypePolicy());
        Assert.Equal("{}", Encoding.UTF8.GetString(minimalTransport.Requests[0].Body.Span));
    }

    [Fact]
    [Requirement("SSB-001")]
    [Trait("Requirement", "SSB-001")]
    public async Task Broker_asset_group_read_write_delete_are_pinned_to_v2()
    {
        FakeTransport readTransport = new();
        readTransport.EnqueueResponse(200, body: Json("""{"data":{"login_class":"brokered","priority":10,"lock":true}}"""));
        BastionVaultClient readClient = BuildClient(readTransport);
        SshBrokerAssetGroupPolicy? read = await readClient.SshBroker.ReadAssetGroupAsync("g1");
        Assert.Equal("brokered", read!.LoginClass);
        Assert.Equal(10, read.Priority);
        Assert.True(read.Lock);
        Assert.StartsWith("https://vault.example.com:8200/v2/ssh-broker/policy/asset-group/g1", readTransport.Requests[0].Uri.ToString(), StringComparison.Ordinal);

        FakeTransport writeTransport = new();
        writeTransport.EnqueueResponse(200);
        BastionVaultClient writeClient = BuildClient(writeTransport);
        await writeClient.SshBroker.WriteAssetGroupAsync("g1", new SshBrokerAssetGroupPolicy { LoginClass = "brokered", Priority = 5, Lock = true });
        Assert.Equal("PUT", writeTransport.Requests[0].Method);
        Assert.StartsWith("https://vault.example.com:8200/v2/ssh-broker/policy/asset-group/g1", writeTransport.Requests[0].Uri.ToString(), StringComparison.Ordinal);
        string body = Encoding.UTF8.GetString(writeTransport.Requests[0].Body.Span);
        Assert.Contains("\"priority\":5", body, StringComparison.Ordinal);
        Assert.Contains("\"lock\":true", body, StringComparison.Ordinal);

        FakeTransport minimalTransport = new();
        minimalTransport.EnqueueResponse(200);
        BastionVaultClient minimalClient = BuildClient(minimalTransport);
        await minimalClient.SshBroker.WriteAssetGroupAsync("g2", new SshBrokerAssetGroupPolicy());
        Assert.Equal("{}", Encoding.UTF8.GetString(minimalTransport.Requests[0].Body.Span));

        FakeTransport deleteTransport = new();
        deleteTransport.EnqueueResponse(204);
        BastionVaultClient deleteClient = BuildClient(deleteTransport);
        await deleteClient.SshBroker.DeleteAssetGroupAsync("g1");
        Assert.StartsWith("https://vault.example.com:8200/v2/ssh-broker/policy/asset-group/g1", deleteTransport.Requests[0].Uri.ToString(), StringComparison.Ordinal);

        FakeTransport absentTransport = new();
        absentTransport.EnqueueResponse(404);
        BastionVaultClient absentClient = BuildClient(absentTransport);
        Assert.Null(await absentClient.SshBroker.ReadAssetGroupAsync("ghost"));
    }

    [Fact]
    [Requirement("SSB-001")]
    [Trait("Requirement", "SSB-001")]
    public async Task Broker_resource_read_write_delete_are_pinned_to_v2()
    {
        FakeTransport readTransport = new();
        readTransport.EnqueueResponse(200, body: Json("""{"data":{"login_class":"shared-credential"}}"""));
        BastionVaultClient readClient = BuildClient(readTransport);
        SshBrokerResourcePolicy? read = await readClient.SshBroker.ReadResourceAsync("srv-1");
        Assert.Equal("shared-credential", read!.LoginClass);
        Assert.StartsWith("https://vault.example.com:8200/v2/ssh-broker/policy/resource/srv-1", readTransport.Requests[0].Uri.ToString(), StringComparison.Ordinal);

        FakeTransport writeTransport = new();
        writeTransport.EnqueueResponse(200);
        BastionVaultClient writeClient = BuildClient(writeTransport);
        await writeClient.SshBroker.WriteResourceAsync("srv-1", new SshBrokerResourcePolicy { LoginClass = "brokered" });
        Assert.Equal("PUT", writeTransport.Requests[0].Method);
        Assert.StartsWith("https://vault.example.com:8200/v2/ssh-broker/policy/resource/srv-1", writeTransport.Requests[0].Uri.ToString(), StringComparison.Ordinal);
        string body = Encoding.UTF8.GetString(writeTransport.Requests[0].Body.Span);
        Assert.Equal("""{"login_class":"brokered"}""", body);

        FakeTransport deleteTransport = new();
        deleteTransport.EnqueueResponse(204);
        BastionVaultClient deleteClient = BuildClient(deleteTransport);
        await deleteClient.SshBroker.DeleteResourceAsync("srv-1");
        Assert.StartsWith("https://vault.example.com:8200/v2/ssh-broker/policy/resource/srv-1", deleteTransport.Requests[0].Uri.ToString(), StringComparison.Ordinal);

        FakeTransport absentTransport = new();
        absentTransport.EnqueueResponse(404);
        BastionVaultClient absentClient = BuildClient(absentTransport);
        Assert.Null(await absentClient.SshBroker.ReadResourceAsync("ghost"));

        FakeTransport minimalTransport = new();
        minimalTransport.EnqueueResponse(200);
        BastionVaultClient minimalClient = BuildClient(minimalTransport);
        await minimalClient.SshBroker.WriteResourceAsync("srv-2", new SshBrokerResourcePolicy());
        Assert.Equal("{}", Encoding.UTF8.GetString(minimalTransport.Requests[0].Body.Span));
    }

    [Fact]
    [Requirement("SSB-001")]
    [Trait("Requirement", "SSB-001")]
    public async Task Effective_is_pinned_to_v2_csv_joins_asset_group_ids_and_returns_the_resolution()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"login_class":"brokered","source":"type"}}"""));
        BastionVaultClient client = BuildClient(transport);

        SshBrokerEffectivePolicy result = await client.SshBroker.EffectiveAsync("srv-1", "linux", ["g1", "g2"]);

        Assert.Equal("brokered", result.LoginClass);
        Assert.Equal("type", result.Source);
        Assert.StartsWith("https://vault.example.com:8200/v2/ssh-broker/policy/effective", transport.Requests[0].Uri.ToString(), StringComparison.Ordinal);
        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains("\"resource_id\":\"srv-1\"", body, StringComparison.Ordinal);
        Assert.Contains("\"resource_type\":\"linux\"", body, StringComparison.Ordinal);
        Assert.Contains("\"asset_group_ids\":\"g1,g2\"", body, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("SSB-001")]
    [Trait("Requirement", "SSB-001")]
    public async Task Effective_omits_asset_group_ids_when_absent_and_throws_envelope_mismatch_on_a_missing_login_class()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"source":"global"}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.SshBroker.EffectiveAsync("srv-1", "linux"));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.DoesNotContain("asset_group_ids", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Effective_throws_envelope_mismatch_when_source_is_missing()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"login_class":"brokered"}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.SshBroker.EffectiveAsync("srv-1", "linux"));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    public async Task Effective_throws_envelope_mismatch_when_the_response_is_absent()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.SshBroker.EffectiveAsync("srv-1", "linux"));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    [Requirement("SSB-002")]
    [Trait("Requirement", "SSB-002")]
    public async Task WriteResource_maps_login_class_locked_to_BV_AUTHZ_005()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(403, body: Json("""{"error":"login_class_locked"}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.SshBroker.WriteResourceAsync("srv-1", new SshBrokerResourcePolicy { LoginClass = "brokered" }));

        Assert.Equal(ErrorCodes.AuthzLoginClassLocked, exception.Code);
        Assert.StartsWith("https://vault.example.com:8200/v2/ssh-broker/policy/resource/srv-1", transport.Requests[0].Uri.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("SSB-002")]
    [Trait("Requirement", "SSB-002")]
    public async Task WriteResource_maps_brokered_resource_no_static_credential_to_BV_CONFLICT_003()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(409, body: Json("""{"error":"brokered_resource_no_static_credential"}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.SshBroker.WriteResourceAsync("srv-1", new SshBrokerResourcePolicy { LoginClass = "brokered" }));

        Assert.Equal(ErrorCodes.ConflictBrokeredResourceStaticCredential, exception.Code);
        Assert.StartsWith("https://vault.example.com:8200/v2/ssh-broker/policy/resource/srv-1", transport.Requests[0].Uri.ToString(), StringComparison.Ordinal);
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
