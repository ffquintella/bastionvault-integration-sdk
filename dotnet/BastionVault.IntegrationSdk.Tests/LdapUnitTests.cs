using System.Text;
using BastionVault.IntegrationSdk.Testing;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>12 §LDAP / Active Directory (M10 slice c, DR-0017): the <c>openldap</c> mount, reached from <c>Client.Ldap</c>.</summary>
public sealed class LdapUnitTests
{
    private const string Address = "https://vault.example.com:8200";

    [Fact]
    public async Task ReadConfig_returns_null_on_404_and_WriteConfig_serialises_every_field()
    {
        FakeTransport readTransport = new();
        readTransport.EnqueueResponse(404);
        Assert.Null(await BuildClient(readTransport).Ldap.ReadConfigAsync());

        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"url":"ldaps://dc1.example.com","binddn":"cn=admin,dc=example,dc=com","userdn":"dc=example,dc=com","directory_type":"active_directory","password_policy":"policy-1","request_timeout":10,"starttls":true,"client_tls_cert":"CERT","tls_min_version":"tls12","insecure_tls":false,"userattr":"cn"}}"""));
        BastionVaultClient client = BuildClient(transport);
        LdapConfig? config = await client.Ldap.ReadConfigAsync();
        Assert.Equal("ldaps://dc1.example.com", config!.Url);
        Assert.Equal("active_directory", config.DirectoryType);
        Assert.Null(config.BindPass);
        Assert.Null(config.ClientTlsKey);
        Assert.Equal(TimeSpan.FromSeconds(10), config.RequestTimeout);

        FakeTransport writeTransport = new();
        writeTransport.EnqueueResponse(204);
        BastionVaultClient writeClient = BuildClient(writeTransport);
        await writeClient.Ldap.WriteConfigAsync(new LdapConfig
        {
            Url = "ldaps://dc1.example.com",
            BindDn = "cn=admin,dc=example,dc=com",
            BindPass = new SecretString("hunter2"),
            UserDn = "dc=example,dc=com",
            DirectoryType = "openldap",
            PasswordPolicy = "policy-1",
            RequestTimeout = TimeSpan.FromSeconds(15),
            StartTls = true,
            ClientTlsCert = "CERT",
            ClientTlsKey = new SecretString("KEY"),
            TlsMinVersion = "tls13",
            InsecureTls = false,
            UserAttr = "uid",
        });

        Assert.Equal("POST", writeTransport.Requests[0].Method);
        Assert.EndsWith("/v1/openldap/config", writeTransport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
        string body = Encoding.UTF8.GetString(writeTransport.Requests[0].Body.Span);
        Assert.Contains("\"bindpass\":\"hunter2\"", body, StringComparison.Ordinal);
        Assert.Contains("\"client_tls_key\":\"KEY\"", body, StringComparison.Ordinal);
        Assert.Contains("\"userattr\":\"uid\"", body, StringComparison.Ordinal);
        Assert.Contains("\"request_timeout\":15", body, StringComparison.Ordinal);

        FakeTransport deleteTransport = new();
        deleteTransport.EnqueueResponse(204);
        await BuildClient(deleteTransport).Ldap.DeleteConfigAsync();
        Assert.Equal("DELETE", deleteTransport.Requests[0].Method);
    }

    [Fact]
    [Requirement("LDP-001")]
    [Trait("Requirement", "LDP-001")]
    public async Task WriteConfig_refuses_insecure_tls_without_acknowledgement_client_side()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Ldap.WriteConfigAsync(new LdapConfig { InsecureTls = true }));

        Assert.Equal(ErrorCodes.InputInvalidArgument, exception.Code);
        Assert.Empty(transport.Requests);

        // Explicit refusal (AcknowledgeInsecureTls = false), not merely its absence, still trips
        // the guard.
        FakeTransport refusedTransport = new();
        BastionVaultException refusedException = await Assert.ThrowsAsync<BastionVaultException>(
            () => BuildClient(refusedTransport).Ldap.WriteConfigAsync(new LdapConfig
            {
                InsecureTls = true,
                AcknowledgeInsecureTls = false,
            }));
        Assert.Equal(ErrorCodes.InputInvalidArgument, refusedException.Code);
        Assert.Empty(refusedTransport.Requests);

        // Acknowledged: the request proceeds.
        FakeTransport acknowledgedTransport = new();
        acknowledgedTransport.EnqueueResponse(204);
        await BuildClient(acknowledgedTransport).Ldap.WriteConfigAsync(new LdapConfig
        {
            InsecureTls = true,
            AcknowledgeInsecureTls = true,
        });
        _ = Assert.Single(acknowledgedTransport.Requests);

        // insecure_tls absent or false never trips the guard, acknowledged or not.
        FakeTransport safeTransport = new();
        safeTransport.EnqueueResponse(204);
        await BuildClient(safeTransport).Ldap.WriteConfigAsync(new LdapConfig { InsecureTls = false });
        _ = Assert.Single(safeTransport.Requests);
    }

    [Fact]
    public async Task RotateRoot_and_CheckConnection_and_RotateRole_send_the_documented_routes()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(200, body: Json("""{"data":{"ok":true,"stage":"bind","url":"ldaps://dc1","bind_dn":"cn=admin","host":"dc1","port":636,"scheme":"ldaps","latency_ms":42}}"""));
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        await client.Ldap.RotateRootAsync();
        Assert.EndsWith("/v1/openldap/rotate-root", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        LdapCheckConnectionResult result = await client.Ldap.CheckConnectionAsync();
        Assert.True(result.Ok);
        Assert.Equal("bind", result.Stage);
        Assert.Equal("cn=admin", result.BindDn);
        Assert.Equal(636, result.Port);
        Assert.Equal(42L, result.LatencyMs);
        Assert.EndsWith("/v1/openldap/check-connection", transport.Requests[1].Uri.AbsoluteUri, StringComparison.Ordinal);

        await client.Ldap.RotateRoleAsync("svc-role");
        Assert.EndsWith("/v1/openldap/rotate-role/svc-role", transport.Requests[2].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckConnection_on_failure_reports_only_as_far_as_the_probe_got()
    {
        // F1: a probe that fails at the `bind` stage never reaches Host/Port/LatencyMs — only
        // Ok, Stage and Error are populated.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"ok":false,"stage":"bind","error":"invalid credentials"}}"""));
        BastionVaultClient client = BuildClient(transport);

        LdapCheckConnectionResult result = await client.Ldap.CheckConnectionAsync();

        Assert.False(result.Ok);
        Assert.Equal("bind", result.Stage);
        Assert.Equal("invalid credentials", result.Error);
        Assert.Null(result.Host);
        Assert.Null(result.Port);
        Assert.Null(result.LatencyMs);
    }

    [Fact]
    public async Task CheckConnection_raises_an_envelope_mismatch_on_a_bodyless_response()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Ldap.CheckConnectionAsync());
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    public async Task StaticCred_returns_a_redacting_password_and_null_on_404()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"username":"svc","dn":"cn=svc,dc=example,dc=com","password":"s3cret","last_rotated":"2024-01-01T00:00:00Z","ttl_secs":3600}}"""));
        BastionVaultClient client = BuildClient(transport);

        LdapStaticCred? cred = await client.Ldap.StaticCredAsync("svc-role");
        Assert.Equal("svc", cred!.Username);
        Assert.Equal("s3cret", cred.Password.Reveal());
        Assert.DoesNotContain("s3cret", cred.Password.ToString(), StringComparison.Ordinal);
        Assert.EndsWith("/v1/openldap/static-cred/svc-role", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport missingTransport = new();
        missingTransport.EnqueueResponse(404);
        Assert.Null(await BuildClient(missingTransport).Ldap.StaticCredAsync("ghost"));

        FakeTransport incompleteTransport = new();
        incompleteTransport.EnqueueResponse(200, body: Json("""{"data":{"dn":"cn=svc,dc=example,dc=com"}}"""));
        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => BuildClient(incompleteTransport).Ldap.StaticCredAsync("svc-role"));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    public async Task StaticRoles_list_read_write_delete_round_trip()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"keys":["svc-role"]}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"dn":"cn=svc,dc=example,dc=com","username":"svc","rotation_period":86400,"password_policy":"policy-1"}}"""));
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        Assert.Equal(["svc-role"], await client.Ldap.StaticRoles.ListAsync());
        Assert.EndsWith("/v1/openldap/static-role/", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        LdapStaticRole? role = await client.Ldap.StaticRoles.ReadAsync("svc-role");
        Assert.Equal("svc", role!.Username);
        Assert.Equal(TimeSpan.FromDays(1), role.RotationPeriod);

        await client.Ldap.StaticRoles.WriteAsync("svc-role", new LdapStaticRole { Dn = "cn=svc,dc=example,dc=com", Username = "svc" });
        Assert.Equal("POST", transport.Requests[2].Method);

        await client.Ldap.StaticRoles.DeleteAsync("svc-role");
        Assert.Equal("DELETE", transport.Requests[3].Method);

        FakeTransport missingTransport = new();
        missingTransport.EnqueueResponse(404);
        Assert.Null(await BuildClient(missingTransport).Ldap.StaticRoles.ReadAsync("ghost"));

        // Every field set (the write test above leaves rotation_period/password_policy absent),
        // and every field absent on read.
        FakeTransport fullWriteTransport = new();
        fullWriteTransport.EnqueueResponse(204);
        await BuildClient(fullWriteTransport).Ldap.StaticRoles.WriteAsync("svc-role", new LdapStaticRole
        {
            Dn = "cn=svc,dc=example,dc=com",
            Username = "svc",
            RotationPeriod = TimeSpan.FromDays(1),
            PasswordPolicy = "policy-1",
        });
        string fullBody = Encoding.UTF8.GetString(fullWriteTransport.Requests[0].Body.Span);
        Assert.Contains("\"rotation_period\":86400", fullBody, StringComparison.Ordinal);
        Assert.Contains("\"password_policy\":\"policy-1\"", fullBody, StringComparison.Ordinal);

        FakeTransport emptyReadTransport = new();
        emptyReadTransport.EnqueueResponse(200, body: Json("""{"data":{}}"""));
        LdapStaticRole? emptyRole = await BuildClient(emptyReadTransport).Ldap.StaticRoles.ReadAsync("svc-role");
        Assert.Null(emptyRole!.Dn);
        Assert.Null(emptyRole.RotationPeriod);
    }

    [Fact]
    public async Task Library_list_read_write_delete_check_out_check_in_and_status_round_trip()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"keys":["set-1"]}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"service_account_names":["svc-1","svc-2"],"ttl":3600,"max_ttl":86400}}"""));
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(200, body: Json("""{"data":{"service_account_name":"svc-1","password":"s3cret","lease_id":"lease-1","ttl_secs":3600}}"""));
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(200, body: Json("""{"data":{"checked_out":{"svc-1":{"lease_id":"lease-1"}},"available":["svc-2"]}}"""));
        BastionVaultClient client = BuildClient(transport);

        Assert.Equal(["set-1"], await client.Ldap.Library.ListAsync());
        Assert.EndsWith("/v1/openldap/library/", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        LdapLibrarySet? set = await client.Ldap.Library.ReadAsync("set-1");
        Assert.Equal(["svc-1", "svc-2"], set!.ServiceAccountNames);

        await client.Ldap.Library.WriteAsync("set-1", new LdapLibrarySet { ServiceAccountNames = ["svc-1"] });
        Assert.Equal("POST", transport.Requests[2].Method);

        await client.Ldap.Library.DeleteAsync("set-1");
        Assert.Equal("DELETE", transport.Requests[3].Method);

        LdapLibraryCheckOut checkOut = await client.Ldap.Library.CheckOutAsync("set-1", TimeSpan.FromMinutes(5));
        Assert.Equal("svc-1", checkOut.ServiceAccountName);
        Assert.Equal("s3cret", checkOut.Password.Reveal());
        string checkOutBody = Encoding.UTF8.GetString(transport.Requests[4].Body.Span);
        Assert.Contains("\"ttl\":300", checkOutBody, StringComparison.Ordinal);
        Assert.EndsWith("/v1/openldap/library/set-1/check-out", transport.Requests[4].Uri.AbsoluteUri, StringComparison.Ordinal);

        await client.Ldap.Library.CheckInAsync("set-1", "svc-1");
        string checkInBody = Encoding.UTF8.GetString(transport.Requests[5].Body.Span);
        Assert.Contains("\"service_account_name\":\"svc-1\"", checkInBody, StringComparison.Ordinal);

        LdapLibraryStatus status = await client.Ldap.Library.StatusAsync("set-1");
        Assert.Equal(["svc-2"], status.Available);
        Assert.True(status.CheckedOut.ContainsKey("svc-1"));

        // CheckIn without a specific account: the request body omits `service_account_name`.
        FakeTransport checkInAnyTransport = new();
        checkInAnyTransport.EnqueueResponse(204);
        await BuildClient(checkInAnyTransport).Ldap.Library.CheckInAsync("set-1");
        Assert.Equal(0, checkInAnyTransport.Requests[0].Body.Length);

        FakeTransport missingTransport = new();
        missingTransport.EnqueueResponse(404);
        Assert.Null(await BuildClient(missingTransport).Ldap.Library.ReadAsync("ghost"));

        FakeTransport checkOutMismatchTransport = new();
        checkOutMismatchTransport.EnqueueResponse(204);
        BastionVaultException checkOutException = await Assert.ThrowsAsync<BastionVaultException>(
            () => BuildClient(checkOutMismatchTransport).Ldap.Library.CheckOutAsync("set-1"));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, checkOutException.Code);

        FakeTransport statusMismatchTransport = new();
        statusMismatchTransport.EnqueueResponse(204);
        BastionVaultException statusException = await Assert.ThrowsAsync<BastionVaultException>(
            () => BuildClient(statusMismatchTransport).Ldap.Library.StatusAsync("set-1"));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, statusException.Code);

        // CheckOut's own field-by-field requiredness: a response missing `lease_id` fails there.
        FakeTransport checkOutIncompleteTransport = new();
        checkOutIncompleteTransport.EnqueueResponse(200, body: Json("""{"data":{"service_account_name":"svc-1","password":"s3cret"}}"""));
        BastionVaultException checkOutIncompleteException = await Assert.ThrowsAsync<BastionVaultException>(
            () => BuildClient(checkOutIncompleteTransport).Ldap.Library.CheckOutAsync("set-1"));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, checkOutIncompleteException.Code);

        // `checked_out` absent falls back to an empty map rather than throwing.
        FakeTransport noCheckedOutTransport = new();
        noCheckedOutTransport.EnqueueResponse(200, body: Json("""{"data":{"available":["svc-1"]}}"""));
        LdapLibraryStatus emptyStatus = await BuildClient(noCheckedOutTransport).Ldap.Library.StatusAsync("set-1");
        Assert.Empty(emptyStatus.CheckedOut);

        // Every LdapLibrarySet member set (the write test above leaves ttl/max_ttl/etc. absent).
        FakeTransport fullSetWriteTransport = new();
        fullSetWriteTransport.EnqueueResponse(204);
        await BuildClient(fullSetWriteTransport).Ldap.Library.WriteAsync("set-1", new LdapLibrarySet
        {
            ServiceAccountNames = ["svc-1"],
            Ttl = TimeSpan.FromHours(1),
            MaxTtl = TimeSpan.FromHours(2),
            DisableCheckInEnforcement = true,
            AffinityTtl = TimeSpan.FromMinutes(30),
        });
        string fullSetBody = Encoding.UTF8.GetString(fullSetWriteTransport.Requests[0].Body.Span);
        Assert.Contains("\"max_ttl\":7200", fullSetBody, StringComparison.Ordinal);
        Assert.Contains("\"disable_check_in_enforcement\":true", fullSetBody, StringComparison.Ordinal);
        Assert.Contains("\"affinity_ttl\":1800", fullSetBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_operation_rejects_empty_arguments_before_any_request_is_sent()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Ldap.ReadConfigAsync(mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => client.Ldap.WriteConfigAsync(null!));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Ldap.StaticCredAsync(string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Ldap.RotateRoleAsync(string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Ldap.StaticRoles.ReadAsync(string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Ldap.Library.ReadAsync(string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => client.Ldap.Library.WriteAsync("set-1", null!));
        Assert.Empty(transport.Requests);
    }

    private static BastionVaultClient BuildClient(ITransport transport)
    {
        BastionVaultClientOptions options = new()
        {
            Address = Address,
            Token = "s.FAKE-token-0000000000000000",
            Transport = transport,
            RateGate = new RateGate { RatePerSecond = 0 },
            RetryPolicy = new RetryPolicy { MaxAttempts = 1 },
        };
        return new BastionVaultClient(options, EnvironmentSource.None);
    }

    private static ReadOnlyMemory<byte> Json(string json)
    {
        return Encoding.UTF8.GetBytes(json);
    }
}
