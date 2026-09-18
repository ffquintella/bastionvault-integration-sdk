using System.Reflection;
using System.Text;
using BastionVault.IntegrationSdk.Internal;
using BastionVault.IntegrationSdk.Testing;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// M7c's unit coverage for the last of <c>06-system-api.md</c> (DR-0012, slice c): SYS-070's audit
/// surface and its RFC 3339 query serialisation, SYS-080's <c>/v2</c>-pinned identity self-service
/// and its write-preserve contact update, SYS-090/SYS-091's backup and restore, SYS-100's
/// asserted <i>absences</i>, SYS-101's gap list, and RES-030's cluster-wide seal and unseal.
/// </summary>
public sealed class SysCompleteUnitTests
{
    private const string Address = "https://vault.example.com:8200";
    private const string Token = "s.FAKE-token-0000000000000000";

    private const string One = "https://bv-1.corp.example:8200";
    private const string Two = "https://bv-2.corp.example:8200";
    private const string Three = "https://bv-3.corp.example:8200";

    // ---- SYS-070: audit -------------------------------------------------------------------

    [Fact]
    [Requirement("SYS-070")]
    [Trait("Requirement", "SYS-070")]
    public async Task ListDevices_reads_all_five_fields_normalises_the_path_and_treats_an_absent_array_as_empty()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """
            {"devices":[
              {"path":"file","type":"file","description":"local","namespace":"","mirror":true},
              {"path":"syslog/","type":"syslog","mirror":false},
              "not-an-object"
            ]}
            """));
        transport.EnqueueResponse(200, body: Json("{}"));
        transport.EnqueueResponse(404);
        using BastionVaultClient client = BuildClient(transport);

        IReadOnlyList<AuditDevice> devices = await client.Sys.Audit.ListDevicesAsync();

        Assert.Equal(2, devices.Count);
        // SYS-022's table form on both, whichever form the server used.
        Assert.Equal(["file/", "syslog/"], devices.Select(device => device.Path));
        Assert.Equal("file", devices[0].Type);
        Assert.Equal("local", devices[0].Description);
        Assert.Equal(string.Empty, devices[0].Namespace);
        Assert.True(devices[0].Mirror);
        Assert.Null(devices[1].Description);
        Assert.False(devices[1].Mirror);
        Assert.Equal($"{Address}/v1/sys/audit", transport.Requests[0].Uri.ToString());

        Assert.Empty(await client.Sys.Audit.ListDevicesAsync());
        Assert.Empty(await client.Sys.Audit.ListDevicesAsync());
    }

    [Fact]
    [Requirement("SYS-070")]
    [Trait("Requirement", "SYS-070")]
    public async Task EnableDevice_writes_only_the_fields_that_were_set_and_DisableDevice_deletes()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        using BastionVaultClient client = BuildClient(transport);

        await client.Sys.Audit.EnableDeviceAsync("file/", new AuditDeviceSpec { Type = "file" });
        await client.Sys.Audit.EnableDeviceAsync(
            "  syslog  ",
            new AuditDeviceSpec
            {
                Type = "syslog",
                Description = "central",
                Options = new Dictionary<string, string>(StringComparer.Ordinal) { ["facility"] = "AUTH" },
                Mirror = false,
            });
        await client.Sys.Audit.DisableDeviceAsync("/file/");

        // An unset Mirror omits the key; `false` is sent. The two are different requests.
        Assert.Equal("""{"type":"file"}""", BodyOf(transport.Requests[0]));
        Assert.Equal("""{"type":"syslog","description":"central","options":{"facility":"AUTH"},"mirror":false}""", BodyOf(transport.Requests[1]));
        Assert.Equal($"{Address}/v1/sys/audit/file", transport.Requests[0].Uri.ToString());
        Assert.Equal($"{Address}/v1/sys/audit/syslog", transport.Requests[1].Uri.ToString());
        Assert.Equal($"{Address}/v1/sys/audit/file", transport.Requests[2].Uri.ToString());
        Assert.Equal(["POST", "POST", "DELETE"], transport.Requests.Select(request => request.Method));
    }

    [Fact]
    [Requirement("SYS-070")]
    [Trait("Requirement", "SYS-070")]
    public async Task Events_serialises_from_and_to_as_RFC_3339_UTC_and_percent_encodes_them()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"events":[]}"""));
        transport.EnqueueResponse(200, body: Json("""{"events":[]}"""));
        transport.EnqueueResponse(200, body: Json("""{"events":[]}"""));
        using BastionVaultClient client = BuildClient(transport);

        // A non-UTC offset is *converted*, not refused: a DateTimeOffset names an unambiguous
        // instant, so 09:30+02:00 and 07:30Z are the same moment and the wire format carries one.
        _ = await client.Sys.Audit.EventsAsync(
            new DateTimeOffset(2026, 9, 18, 9, 30, 0, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero),
            limit: 25);
        _ = await client.Sys.Audit.EventsAsync(to: new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        _ = await client.Sys.Audit.EventsAsync();

        // SYS-070's "(percent-encoded)": the values go through UrlBuilder.EncodeQueryValue, the
        // one query encoder this SDK has (D-M7-20 — no second one). RFC 3339 UTC happens to
        // contain no character a query component must escape (`:` and `-` are both legal there per
        // RFC 3986 §3.4), so the encoded form equals the literal form. The requirement is that the
        // value is *encoded*, not that it is mangled; a value that does need escaping is covered by
        // the `after=` cursor on Sys.ListNamespacesInfo, which uses the same call.
        Assert.Equal(
            $"{Address}/v1/sys/audit/events?from=2026-09-18T07:30:00Z&to=2026-09-18T12:00:00Z&limit=25",
            transport.Requests[0].Uri.ToString());
        // The conversion is the load-bearing half: 09:30+02:00 went out as 07:30Z.
        Assert.Contains("from=2026-09-18T07:30:00Z", transport.Requests[0].Uri.ToString(), StringComparison.Ordinal);
        // An omitted bound writes no key at all rather than an empty one.
        Assert.Equal($"{Address}/v1/sys/audit/events?to=2026-01-02T03:04:05Z&limit=500", transport.Requests[1].Uri.ToString());
        Assert.Equal($"{Address}/v1/sys/audit/events?limit=500", transport.Requests[2].Uri.ToString());
    }

    [Theory]
    [Requirement("SYS-070")]
    [Trait("Requirement", "SYS-070")]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Events_refuses_a_limit_below_one_client_side_with_BV_INPUT_004(int limit)
    {
        FakeTransport transport = new();
        using BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.Audit.EventsAsync(limit: limit));

        Assert.Equal(ErrorCodes.InputOutOfRange, failure.Code);
        Assert.Equal(0, failure.Attempts);
        Assert.Equal("limit", failure.Details["argument"]);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    [Requirement("SYS-070")]
    [Trait("Requirement", "SYS-070")]
    public async Task Events_reads_every_field_and_keeps_the_servers_newest_first_order()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """
            {"events":[
              {"ts":"2026-09-18T12:00:00Z","user":"ana","machine":"ws-1","op":"write","category":"kv",
               "target":"secret/a","changed_fields":["value"],"summary":"wrote secret/a"},
              {"ts":"2026-09-17T12:00:00Z","user":"bo","op":"read"},
              "not-an-object"
            ]}
            """));
        using BastionVaultClient client = BuildClient(transport);

        IReadOnlyList<AuditEvent> events = await client.Sys.Audit.EventsAsync();

        Assert.Equal(2, events.Count);
        // SYS-070 says the server orders newest first; the SDK passes that order through and does
        // not re-sort, so an implementation that sorted ascending would fail here.
        Assert.Equal("ana", events[0].User);
        Assert.Equal("bo", events[1].User);
        Assert.True(events[0].Timestamp > events[1].Timestamp);
        Assert.Equal("ws-1", events[0].Machine);
        Assert.Equal("kv", events[0].Category);
        Assert.Equal("secret/a", events[0].Target);
        Assert.Equal(["value"], events[0].ChangedFields);
        Assert.Equal("wrote secret/a", events[0].Summary);
        // `machine?` is optional on the wire and stays optional here.
        Assert.Null(events[1].Machine);
        Assert.Empty(events[1].ChangedFields);
        Assert.Equal("read", events[1].Raw.GetProperty("op").GetString());
    }

    [Fact]
    [Requirement("SYS-070")]
    [Trait("Requirement", "SYS-070")]
    public async Task An_absent_or_non_array_events_key_is_an_empty_result_rather_than_a_protocol_failure()
    {
        // The same treatment ListDevices gives an absent `devices`: the server answering "no
        // events in that window" with an object that omits the key is not a malformed response.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("{}"));
        transport.EnqueueResponse(200, body: Json("""{"events":"none"}"""));
        transport.EnqueueResponse(404);
        using BastionVaultClient client = BuildClient(transport);

        Assert.Empty(await client.Sys.Audit.EventsAsync());
        Assert.Empty(await client.Sys.Audit.EventsAsync());
        Assert.Empty(await client.Sys.Audit.EventsAsync());
    }

    // ---- SYS-080: identity self-service ----------------------------------------------------

    [Fact]
    [Requirement("SYS-080")]
    [Requirement("TRN-071")]
    [Trait("Requirement", "SYS-080")]
    public async Task Every_identity_route_is_pinned_to_v2_against_both_the_client_prefix_and_a_per_call_override()
    {
        // SYS-080's whole content: "All /v2/sys/identity/* paths MUST be pinned to /v2 (TRN-071)".
        // The client is a v1 client *and* every call passes ApiVersion = "v1", so an implementation
        // that used `?? "v2"` as a default rather than a pin fails every assertion below.
        FakeTransport transport = new();
        for (int index = 0; index < 13; index++)
        {
            transport.EnqueueResponse(200, body: Json("""{"keys":[],"namespaces":[]}"""));
        }

        using BastionVaultClient client = BuildClient(transport, apiPrefix: "v1");
        RequestOptions v1 = new() { ApiVersion = "v1" };

        _ = await client.Identity.Profile.ReadAsync(v1);
        await client.Identity.Profile.ChangePasswordAsync(new SecretString("old"), new SecretString("new"), v1);
        await client.Identity.Profile.UpdateContactAsync("a@example.com", null, v1);
        _ = await client.Identity.DefaultAccount.ReadSelfAsync(v1);
        await client.Identity.DefaultAccount.WriteSelfAsync(new DefaultAccountSpec { Username = "ana" }, v1);
        _ = await client.Identity.DefaultAccount.ReadAsync("userpass", "ana", v1);
        _ = await client.Identity.SshSecurityKey.ListAsync(v1);
        _ = await client.Identity.SshSecurityKey.ReadSelfAsync(v1);
        await client.Identity.SshSecurityKey.WriteSelfAsync(new SshSecurityKeySpec { Name = "yubi" }, v1);
        await client.Identity.SshSecurityKey.DeleteSelfAsync(v1);
        _ = await client.Identity.SshSecurityKey.ReadAsync("userpass", "ana", v1);
        _ = await client.Identity.NamespaceAssignment.ListAsync(v1);
        _ = await client.Identity.NamespaceAssignment.ReadAsync("userpass", "ana", v1);

        Assert.Equal(
            [
                $"{Address}/v2/sys/identity/profile/self",
                $"{Address}/v2/sys/identity/profile/self/password",
                $"{Address}/v2/sys/identity/profile/self/contact",
                $"{Address}/v2/sys/identity/default-account/self",
                $"{Address}/v2/sys/identity/default-account/self",
                $"{Address}/v2/sys/identity/default-account/userpass/ana",
                $"{Address}/v2/sys/identity/ssh-security-key",
                $"{Address}/v2/sys/identity/ssh-security-key/self",
                $"{Address}/v2/sys/identity/ssh-security-key/self",
                $"{Address}/v2/sys/identity/ssh-security-key/self",
                $"{Address}/v2/sys/identity/ssh-security-key/userpass/ana",
                $"{Address}/v2/sys/identity/ns-assignment",
                $"{Address}/v2/sys/identity/ns-assignment/userpass/ana",
            ],
            transport.Requests.Select(request => request.Uri.ToString()));
        Assert.DoesNotContain(transport.Requests, request => request.Uri.AbsolutePath.StartsWith("/v1/", StringComparison.Ordinal));
    }

    [Fact]
    [Requirement("SYS-080")]
    [Trait("Requirement", "SYS-080")]
    public async Task UpdateContact_is_write_preserve_so_omit_keeps_and_empty_string_clears()
    {
        // The requirement in one method: three calls, three different bodies. A signature that
        // took two non-nullable strings could not express the first row at all.
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        using BastionVaultClient client = BuildClient(transport);

        await client.Identity.Profile.UpdateContactAsync();
        await client.Identity.Profile.UpdateContactAsync(email: "ana@example.com");
        await client.Identity.Profile.UpdateContactAsync(email: string.Empty);
        await client.Identity.Profile.UpdateContactAsync(email: string.Empty, phone: "21 0000");

        // Omitted → no key at all → the server keeps what it has.
        Assert.Equal("{}", BodyOf(transport.Requests[0]));
        Assert.Equal("""{"email":"ana@example.com"}""", BodyOf(transport.Requests[1]));
        // "" → the key is present and empty → the server clears it. Distinct from the line above.
        Assert.Equal("""{"email":""}""", BodyOf(transport.Requests[2]));
        Assert.Equal("""{"email":"","phone":"21 0000"}""", BodyOf(transport.Requests[3]));
    }

    [Fact]
    [Requirement("SYS-080")]
    [Trait("Requirement", "SYS-080")]
    public async Task The_windows_password_is_read_when_the_server_sends_it_wrapped_and_is_null_when_it_does_not()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"username":"ana","domain":"CORP","windows_password":"hunter2"}"""));
        transport.EnqueueResponse(200, body: Json("""{"username":"ana","domain":"CORP"}"""));
        using BastionVaultClient client = BuildClient(transport);

        DefaultAccount? self = await client.Identity.DefaultAccount.ReadSelfAsync();
        DefaultAccount? admin = await client.Identity.DefaultAccount.ReadAsync("userpass", "ana");

        Assert.NotNull(self!.WindowsPassword);
        Assert.Equal("hunter2", self.WindowsPassword!.Reveal());
        // CNF-031: the value never reaches a log through ToString().
        Assert.Equal("[REDACTED]", self.WindowsPassword.ToString());
        // SYS-080: withheld outside a GET by the owner. Null, not an empty SecretString — the
        // caller must be able to tell "the server did not send it" from "there is no password".
        Assert.Null(admin!.WindowsPassword);
    }

    [Fact]
    [Requirement("SYS-080")]
    [Trait("Requirement", "SYS-080")]
    public async Task An_identity_admin_path_refuses_an_empty_segment_client_side_and_encodes_the_rest()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("{}"));
        using BastionVaultClient client = BuildClient(transport);

        foreach (Func<Task> call in new Func<Task>[]
                 {
                     () => client.Identity.DefaultAccount.ReadAsync(" ", "ana"),
                     () => client.Identity.DefaultAccount.ReadAsync("userpass", "/"),
                     () => client.Identity.SshSecurityKey.ReadAsync(string.Empty, "ana"),
                     () => client.Identity.NamespaceAssignment.ReadAsync("userpass", string.Empty),
                 })
        {
            BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(call);
            Assert.Equal(ErrorCodes.InputInvalidArgument, failure.Code);
            Assert.Equal(0, failure.Attempts);
        }

        Assert.Empty(transport.Requests);

        // A `/` inside a name cannot invent a path level: it is encoded as one segment (AUT-030's
        // seam), so `a/b` addresses one record rather than two levels of route.
        _ = await client.Identity.NamespaceAssignment.ReadAsync("userpass", "a/c");
        Assert.Equal($"{Address}/v2/sys/identity/ns-assignment/userpass/a%2Fc", transport.Requests[0].Uri.ToString());
    }

    [Fact]
    [Requirement("SYS-080")]
    [Trait("Requirement", "SYS-080")]
    public async Task The_identity_surface_reads_its_records_and_writes_only_the_fields_that_were_set()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"username":"ana","display_name":"Ana","email":"a@x","phone":"","mount":"userpass"}"""));
        transport.EnqueueResponse(200, body: Json("""{"name":"yubi","public_key":"ssh-ed25519 AAAA","fingerprint":"SHA256:x"}"""));
        transport.EnqueueResponse(200, body: Json("""{"namespaces":["a","b"],"default_namespace":"a"}"""));
        transport.EnqueueResponse(200, body: Json("""{"keys":["userpass/ana"]}"""));
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        using BastionVaultClient client = BuildClient(transport);

        IdentityProfile profile = await client.Identity.Profile.ReadAsync();
        Assert.Equal("ana", profile.Username);
        Assert.Equal("Ana", profile.DisplayName);
        Assert.Equal("a@x", profile.Email);
        // "" is a cleared contact, not an absent one, and survives as "".
        Assert.Equal(string.Empty, profile.Phone);
        Assert.Equal("userpass", profile.Mount);

        SshSecurityKey? key = await client.Identity.SshSecurityKey.ReadSelfAsync();
        Assert.Equal("yubi", key!.Name);
        Assert.Equal("SHA256:x", key.Fingerprint);

        NamespaceAssignment? assignment = await client.Identity.NamespaceAssignment.ReadAsync("userpass", "ana");
        Assert.Equal(["a", "b"], assignment!.Namespaces);
        Assert.Equal("a", assignment.DefaultNamespace);

        Assert.Equal(["userpass/ana"], await client.Identity.NamespaceAssignment.ListAsync());
        Assert.Equal("LIST", transport.Requests[3].Method);

        await client.Identity.SshSecurityKey.WriteSelfAsync(new SshSecurityKeySpec { PublicKey = "ssh-ed25519 BBBB" });
        Assert.Equal("""{"public_key":"ssh-ed25519 BBBB"}""", BodyOf(transport.Requests[4]));

        // An empty namespace list is always written: it is a login *restriction*, so "no
        // namespaces" and "do not touch the restriction" are not the same request.
        await client.Identity.NamespaceAssignment.WriteAsync("userpass", "ana", []);
        Assert.Equal("""{"namespaces":[]}""", BodyOf(transport.Requests[5]));
    }

    [Fact]
    [Requirement("SYS-080")]
    [Trait("Requirement", "SYS-080")]
    public async Task The_admin_forms_write_and_delete_the_named_record_and_an_absent_one_reads_as_null()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);   // DefaultAccount.Write (admin)
        transport.EnqueueResponse(204);   // SshSecurityKey.Write (admin)
        transport.EnqueueResponse(204);   // SshSecurityKey.Delete (admin)
        transport.EnqueueResponse(204);   // NamespaceAssignment.Write, with a default namespace
        transport.EnqueueResponse(404);   // DefaultAccount.Read  → absent
        transport.EnqueueResponse(404);   // SshSecurityKey.Read  → absent
        transport.EnqueueResponse(404);   // NamespaceAssignment.Read → absent
        using BastionVaultClient client = BuildClient(transport);

        await client.Identity.DefaultAccount.WriteAsync(
            "userpass",
            "ana",
            new DefaultAccountSpec { Username = "ana", Domain = "CORP", WindowsPassword = new SecretString("hunter2") });
        await client.Identity.SshSecurityKey.WriteAsync("userpass", "ana", new SshSecurityKeySpec { Name = "yubi", PublicKey = "ssh-ed25519 AAAA" });
        await client.Identity.SshSecurityKey.DeleteAsync("userpass", "ana");
        await client.Identity.NamespaceAssignment.WriteAsync("userpass", "ana", ["team-a", "team-b"], "team-a");

        Assert.Equal("""{"username":"ana","domain":"CORP","windows_password":"hunter2"}""", BodyOf(transport.Requests[0]));
        Assert.Equal("""{"name":"yubi","public_key":"ssh-ed25519 AAAA"}""", BodyOf(transport.Requests[1]));
        Assert.Equal("""{"namespaces":["team-a","team-b"],"default_namespace":"team-a"}""", BodyOf(transport.Requests[3]));
        Assert.Equal(
            [
                $"{Address}/v2/sys/identity/default-account/userpass/ana",
                $"{Address}/v2/sys/identity/ssh-security-key/userpass/ana",
                $"{Address}/v2/sys/identity/ssh-security-key/userpass/ana",
                $"{Address}/v2/sys/identity/ns-assignment/userpass/ana",
            ],
            transport.Requests.Take(4).Select(request => request.Uri.ToString()));
        Assert.Equal("DELETE", transport.Requests[2].Method);

        // A 404 with an empty body is absence on all three readers, not an exception.
        Assert.Null(await client.Identity.DefaultAccount.ReadAsync("userpass", "ana"));
        Assert.Null(await client.Identity.SshSecurityKey.ReadAsync("userpass", "ana"));
        Assert.Null(await client.Identity.NamespaceAssignment.ReadAsync("userpass", "ana"));
    }

    [Fact]
    [Requirement("SYS-080")]
    [Trait("Requirement", "SYS-080")]
    public async Task A_bodyless_profile_read_is_BV_PROTOCOL_002_because_SYS_080_says_the_route_never_404s()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        using BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(() => client.Identity.Profile.ReadAsync());

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, failure.Code);
        Assert.Equal("username", failure.Details["expectedField"]);
    }

    [Fact]
    [Requirement("SYS-080")]
    [Trait("Requirement", "SYS-080")]
    public async Task ChangePassword_sends_both_secrets_and_maps_the_two_documented_statuses()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(400, body: Json("""{"error":"password does not meet policy"}"""));
        transport.EnqueueResponse(403, body: Json("""{"error":"permission denied"}"""));
        using BastionVaultClient client = BuildClient(transport);

        await client.Identity.Profile.ChangePasswordAsync(new SecretString("old"), new SecretString("new"));
        Assert.Equal("""{"current_password":"old","new_password":"new"}""", BodyOf(transport.Requests[0]));

        BastionVaultException bad = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Identity.Profile.ChangePasswordAsync(new SecretString("old"), new SecretString("weak")));
        Assert.Equal(ErrorCodes.InputServerRejectedRequest, bad.Code);

        BastionVaultException denied = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Identity.Profile.ChangePasswordAsync(new SecretString("old"), new SecretString("new")));
        Assert.Equal(ErrorCodes.AuthzPermissionDenied, denied.Code);
    }

    // ---- SYS-090 / SYS-091: backup and restore ---------------------------------------------

    [Fact]
    [Requirement("SYS-090")]
    [Trait("Requirement", "SYS-090")]
    public async Task Backup_asks_for_octet_stream_and_returns_the_bytes_verbatim()
    {
        byte[] file = [0x42, 0x56, 0x42, 0x4B, 0x00, 0xFF, 0x10];
        FakeTransport transport = new();
        transport.EnqueueResponse(
            200,
            headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Disposition"] = "attachment; filename=\"backup.bvbk\"",
            },
            body: file);
        using BastionVaultClient client = BuildClient(transport);

        byte[] bytes = await client.Sys.BackupAsync();

        Assert.Equal(file, bytes);
        // The response half is binary; the request has no body, so no Content-Type is sent.
        Assert.Equal("application/octet-stream", transport.Requests[0].Headers["Accept"]);
        Assert.False(transport.Requests[0].Headers.ContainsKey("Content-Type"));
        Assert.Equal($"{Address}/v1/sys/backup", transport.Requests[0].Uri.ToString());
        Assert.Equal("POST", transport.Requests[0].Method);
    }

    [Fact]
    [Requirement("SYS-090")]
    [Trait("Requirement", "SYS-090")]
    public async Task Restore_sends_the_bytes_as_octet_stream_and_reads_entries_restored()
    {
        byte[] file = [0x42, 0x56, 0x42, 0x4B, 0x01];
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"entries_restored":1907}"""));
        transport.EnqueueResponse(204);
        using BastionVaultClient client = BuildClient(transport);

        RestoreResult result = await client.Sys.RestoreAsync(file);

        Assert.Equal(1907L, result.EntriesRestored);
        Assert.Equal(file, transport.Requests[0].Body.ToArray());
        Assert.Equal("application/octet-stream", transport.Requests[0].Headers["Content-Type"]);
        // The *response* half is JSON, so Accept is unchanged.
        Assert.Equal("application/json", transport.Requests[0].Headers["Accept"]);
        Assert.Equal($"{Address}/v1/sys/restore", transport.Requests[0].Uri.ToString());

        // A body-less 2xx is BV-PROTOCOL-002: the requirement names `EntriesRestored`, so an
        // invented zero would be the plausible guess D-M1c-25 forbids. So is a non-object body,
        // and so is an object with no `entries_restored`.
        foreach (int _ in Enumerable.Range(0, 1))
        {
            BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.RestoreAsync(file));
            Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, failure.Code);
            Assert.Equal("entries_restored", failure.Details["expectedField"]);
        }

        FakeTransport malformed = new();
        malformed.EnqueueResponse(200, body: Json("[1,2,3]"));
        malformed.EnqueueResponse(200, body: Json("{}"));
        using BastionVaultClient other = BuildClient(malformed);

        BastionVaultException notAnObject = await Assert.ThrowsAsync<BastionVaultException>(() => other.Sys.RestoreAsync(file));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, notAnObject.Code);
        BastionVaultException noField = await Assert.ThrowsAsync<BastionVaultException>(() => other.Sys.RestoreAsync(file));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, noField.Code);
    }

    [Fact]
    [Requirement("SYS-090")]
    [Trait("Requirement", "SYS-090")]
    public async Task A_backup_larger_than_MaxResponseBytes_is_refused_rather_than_buffered()
    {
        // SYS-090's "no full buffering above MaxResponseBytes", through TRN-033's existing bound
        // (D-M1b-20): the transport aborts while reading, and the executor's backstop catches an
        // in-memory transport that does not. Either way the caller gets BV-TRANSPORT-004 and the
        // oversized body never becomes a byte[].
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: new byte[64]);
        using BastionVaultClient client = BuildClient(transport, maxResponseBytes: 32);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.BackupAsync());

        Assert.Equal(ErrorCodes.TransportResponseTooLarge, failure.Code);
    }

    [Theory]
    [Requirement("SYS-091")]
    [Trait("Requirement", "SYS-091")]
    [InlineData("Backup HMAC verification failed: file may be tampered.")]
    [InlineData("backup file has an invalid magic number")]
    [InlineData("backup uses an unsupported version")]
    [InlineData("backup archive is corrupted")]
    public async Task A_restore_integrity_failure_is_BV_INPUT_103_and_is_never_retried(string message)
    {
        // SYS-091's four failure modes. No operation-local remap and no code minted: every one of
        // these messages is already an Appendix B §2 recognition rule, which is D-M7-18's
        // situation rather than D-M7-6's — and the claim is asserted here rather than stated.
        FakeTransport transport = new();
        transport.EnqueueResponse(500, body: Json($$"""{"error":{{System.Text.Json.JsonSerializer.Serialize(message)}}}"""));
        using BastionVaultClient client = BuildClient(transport, maxAttempts: 5);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.RestoreAsync(new byte[] { 1 }));

        Assert.Equal(ErrorCodes.InputBackupFileInvalid, failure.Code);
        Assert.Equal("BackupFileInvalid", ErrorCatalog.Require(ErrorCodes.InputBackupFileInvalid).Name);
        Assert.False(failure.Retryable);
        Assert.Equal(500, failure.StatusCode);
        // SYS-090's retry exclusion, proved against a policy that would otherwise replay: five
        // attempts configured, one attempt made.
        Assert.Equal(1, failure.Attempts);
        _ = Assert.Single(transport.Requests);
    }

    [Fact]
    [Requirement("SYS-090")]
    [Trait("Requirement", "SYS-090")]
    public async Task Backup_and_Restore_are_excluded_from_retry_by_any_policy_a_caller_can_write()
    {
        // The adversarial configuration D-M7-3 used for Seal/Unseal, reused: the failure's own
        // code is in RetryOn, RetryIdempotentOnly is off, and MaxAttempts is 5. `nonRetryable`
        // short-circuits ahead of every one of those terms, so exactly one attempt is made.
        RetryPolicy replayEverything = new()
        {
            MaxAttempts = 5,
            InitialBackoff = TimeSpan.Zero,
            RetryIdempotentOnly = false,
            RetryOn = [ErrorCodes.ServerInternalError, ErrorCodes.ServerUnavailable],
        };

        FakeTransport backupTransport = new();
        backupTransport.EnqueueResponse(503, body: Json("""{"error":"node is busy"}"""));
        using BastionVaultClient backupClient = BuildClient(backupTransport, retryPolicy: replayEverything);
        BastionVaultException backupFailure = await Assert.ThrowsAsync<BastionVaultException>(() => backupClient.Sys.BackupAsync());
        Assert.Equal(1, backupFailure.Attempts);
        _ = Assert.Single(backupTransport.Requests);

        FakeTransport restoreTransport = new();
        restoreTransport.EnqueueResponse(503, body: Json("""{"error":"node is busy"}"""));
        using BastionVaultClient restoreClient = BuildClient(restoreTransport, retryPolicy: replayEverything);
        BastionVaultException restoreFailure = await Assert.ThrowsAsync<BastionVaultException>(() => restoreClient.Sys.RestoreAsync(new byte[] { 1 }));
        Assert.Equal(1, restoreFailure.Attempts);
        _ = Assert.Single(restoreTransport.Requests);
    }

    [Fact]
    [Requirement("SYS-090")]
    [Requirement("DSC-045")]
    [Trait("Requirement", "SYS-090")]
    public async Task Backup_and_Restore_never_fail_over_to_a_second_node()
    {
        // DSC-045's `nodeLocal` seam, which SYS-090 needs for a stronger reason than SYS-013 did:
        // a backup replayed elsewhere is a *different vault's* image, and a restore replayed
        // elsewhere writes one. A node failure on a failover-armed client is terminal here, where
        // a Logical.Read on the same client would have replayed against bv-2.
        RoutingTransport transport = new((_, _) => throw TransportFailureMapper.Map(TransportFailureKind.ConnectionRefused));
        using BastionVaultClient client = Pinned(transport);

        BastionVaultException backup = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.BackupAsync());
        BastionVaultException restore = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.RestoreAsync(new byte[] { 1 }));

        // One attempt each, both against the pinned node: no health probe, no second node.
        Assert.Equal(
            [$"{One}/v1/sys/backup", $"{One}/v1/sys/restore"],
            transport.Requests.Select(request => request.Uri.AbsoluteUri));
        Assert.Equal(One, client.SelectedNode!.Url);
        Assert.Equal(1, backup.Attempts);
        Assert.Equal(1, restore.Attempts);
    }

    // ---- RES-030: the cluster-wide variants -------------------------------------------------

    [Fact]
    [Requirement("RES-030")]
    [Trait("Requirement", "RES-030")]
    public async Task UnsealClusterWide_reaches_every_candidate_including_the_unreachable_one()
    {
        // RES-030's whole point: unsealing must reach every node, so an unreachable one is a
        // *result*, not the end of the fan-out. bv-2 refuses the connection and bv-3 still runs.
        RoutingTransport transport = new((request, _) => request.Uri.Host switch
        {
            "bv-2.corp.example" => throw TransportFailureMapper.Map(TransportFailureKind.ConnectionRefused),
            "bv-3.corp.example" => Json(200, """{"sealed":false,"t":5,"n":3,"progress":0}"""),
            _ => Json(200, """{"sealed":true,"t":5,"n":3,"progress":1}"""),
        });
        using BastionVaultClient client = Pinned(transport, candidates: [One, Two, Three]);

        IReadOnlyDictionary<string, ClusterNodeResult> results = await client.Sys.UnsealClusterWideAsync("share-1");

        Assert.Equal([One, Two, Three], results.Keys.Order(StringComparer.Ordinal));
        Assert.True(results[One].Succeeded);
        Assert.True(results[One].SealStatus!.Sealed);
        Assert.Equal(1, results[One].SealStatus!.Progress);
        Assert.False(results[Two].Succeeded);
        Assert.Equal(ErrorCodes.DiscoveryNodeUnavailable, results[Two].Error!.Code);
        Assert.Null(results[Two].SealStatus);
        Assert.True(results[Three].Succeeded);
        Assert.False(results[Three].SealStatus!.Sealed);

        // Exactly one attempt per node, in candidate order, and the *same* share on each: no
        // retry, no failover, no probe. A failover would have added a /sys/health here.
        Assert.Equal(
            [$"{One}/v1/sys/unseal", $"{Two}/v1/sys/unseal", $"{Three}/v1/sys/unseal"],
            transport.Requests.Select(request => request.Uri.AbsoluteUri));
        Assert.All(transport.Requests, request => Assert.Equal("""{"key":"share-1"}""", Encoding.UTF8.GetString(request.Body.Span)));
        // The pin did not move: a node failure inside a cluster-wide fan-out is not a reason to
        // re-pin the session (DSC-040).
        Assert.Equal(One, client.SelectedNode!.Url);
    }

    [Fact]
    [Requirement("RES-030")]
    [Trait("Requirement", "RES-030")]
    public async Task SealClusterWide_is_excluded_from_retry_by_any_policy_a_caller_can_write()
    {
        RoutingTransport transport = new((_, _) => Json(503, """{"error":"node is busy"}"""));
        using BastionVaultClient client = Pinned(transport, maxAttempts: 5, candidates: [One, Two]);

        IReadOnlyDictionary<string, ClusterNodeResult> results = await client.Sys.SealClusterWideAsync();

        // Two candidates, two wire attempts: `nonRetryable` held against MaxAttempts = 5, and
        // `nodeLocal` kept the 503 from arming a failover between them.
        Assert.Equal([$"{One}/v1/sys/seal", $"{Two}/v1/sys/seal"], transport.Requests.Select(request => request.Uri.AbsoluteUri));
        Assert.All(results.Values, result => Assert.False(result.Succeeded));
        Assert.All(results.Values, result => Assert.Equal(1, result.Error!.Attempts));
        // Seal has no response body, so there is no per-node status to report.
        Assert.All(results.Values, result => Assert.Null(result.SealStatus));
        Assert.Equal(["PUT", "PUT"], transport.Requests.Select(request => request.Method));
    }

    [Fact]
    [Requirement("RES-030")]
    [Trait("Requirement", "RES-030")]
    public async Task On_a_literal_address_client_the_candidate_set_is_the_one_configured_node()
    {
        // DSC-001 makes literal mode "no DNS, no probing", so "all discovered candidates" is a set
        // of one. The variant still works rather than refusing, because RES-030 describes a
        // fan-out over whatever was discovered, not a requirement that discovery ran.
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        using BastionVaultClient client = BuildClient(transport);

        IReadOnlyDictionary<string, ClusterNodeResult> results = await client.Sys.SealClusterWideAsync();

        string only = Assert.Single(results.Keys);
        Assert.Equal(Address, only);
        Assert.True(results[only].Succeeded);
        Assert.Equal($"{Address}/v1/sys/seal", transport.Requests[0].Uri.ToString());
    }

    [Fact]
    [Requirement("RES-030")]
    [Trait("Requirement", "RES-030")]
    public async Task A_node_that_answers_unseal_without_a_body_is_that_nodes_failure_and_not_the_fan_outs()
    {
        // The per-node result map is what makes this expressible: bv-1's envelope mismatch is
        // bv-1's, and bv-2 still gets its share.
        RoutingTransport transport = new((request, _) => request.Uri.Host == "bv-1.corp.example"
            ? new TransportResponse(204, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), default)
            : Json(200, """{"sealed":false,"t":5,"n":3,"progress":0}"""));
        using BastionVaultClient client = Pinned(transport);

        IReadOnlyDictionary<string, ClusterNodeResult> results = await client.Sys.UnsealClusterWideAsync("share-1");

        Assert.False(results[One].Succeeded);
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, results[One].Error!.Code);
        Assert.True(results[Two].Succeeded);
        Assert.False(results[Two].SealStatus!.Sealed);
    }

    [Fact]
    [Requirement("RES-030")]
    [Trait("Requirement", "RES-030")]
    public async Task UnsealClusterWide_refuses_an_empty_key_before_touching_any_node()
    {
        FakeTransport transport = new();
        using BastionVaultClient client = BuildClient(transport);

        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Sys.UnsealClusterWideAsync(string.Empty));
        Assert.Empty(transport.Requests);
    }

    // ---- SYS-100 / SYS-101: the absences ----------------------------------------------------

    [Fact]
    [Requirement("SYS-100")]
    [Trait("Requirement", "SYS-100")]
    public void No_lease_renew_revoke_wrapping_or_cubbyhole_operation_exists_on_any_public_surface()
    {
        // SYS-100 is satisfied by an absence, and an absence is only enforceable if something
        // asserts it. This is that assertion: a future pass that adds `Sys.Leases.List` or
        // `Sys.Wrapping.Unwrap` fails here rather than shipping a surface the server does not
        // serve. Reflection over the whole public API, not a hand-kept list of types.
        string[] forbidden =
        [
            "Lease", "Leases", "Renew", "Revoke", "Wrap", "Wrapping", "Unwrap", "Cubbyhole",
        ];

        // Scoped to the *operation-bearing* types — the ones a caller reaches an endpoint through —
        // because SYS-100 forbids an operation, not a word. `NamespaceQuotas.MaxLeases` is a
        // quota field on a namespace record, not a lease API, and `Response.LeaseId` is
        // TRN-041's informational passthrough that SYS-100's own last sentence preserves.
        Type[] surfaces =
        [
            .. typeof(BastionVaultClient).Assembly.GetExportedTypes()
                .Where(type => type == typeof(BastionVaultClient) || type.Name.EndsWith("Operations", StringComparison.Ordinal)),
        ];

        (string Type, string Member)[] offenders =
        [
            .. surfaces
                .SelectMany(type => type
                    .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Where(member => member is MethodInfo or PropertyInfo)
                    .Select(member => (Type: type.Name, Member: member.Name)))
                .Where(entry => forbidden.Any(word => entry.Member.StartsWith(word, StringComparison.Ordinal)))
                // AUT-080's token renewal is `auth/token/renew*`, which exists and is a different
                // surface from the absent `sys/renew`; SYS-100 names the `sys` routes only.
                .Where(entry => entry.Type is not "TokenOperations")
                // AUT-054's `auth/ferrogate/machines/{id}/revoke` revokes a *machine identity*,
                // not a lease or a token, and is again not a `sys` route. Exempted as the exact
                // member rather than the whole type, so a future `FerrogateAdminOperations.Unwrap*`
                // would still fail here. Found at the M6/M7 merge: neither milestone's suite could
                // see it, because the operation and the test that scans for it landed on different
                // branches — the scan is assembly-wide, so its result is too.
                .Where(entry => entry is not { Type: "FerrogateAdminOperations", Member: "RevokeAsync" })
                // 08's `Transit.UnwrapDataKey` and `Transit.Byok.WrappingKey` are real, specified
                // section-08 operations (TRS-013, 08 §Operations) that happen to share a word
                // prefix with SYS-100's absent `sys/wrapping/*`. Neither builds a `sys/` route —
                // both are `{mount}/datakey/unwrap/{name}` and `{mount}/wrapping_key` on the
                // Transit engine — so they are exempt as the exact members, the same shape as the
                // two exemptions above.
                .Where(entry => entry is not { Type: "TransitOperations", Member: "UnwrapDataKeyAsync" })
                .Where(entry => entry is not { Type: "TransitByokOperations", Member: "WrappingKeyAsync" }),
        ];

        Assert.Empty(offenders);
        // The scan is only meaningful if it actually saw the surfaces, so assert it did.
        Assert.Contains(surfaces, type => type == typeof(SysOperations));
        Assert.Contains(surfaces, type => type == typeof(IdentityOperations));
        Assert.True(surfaces.Length >= 10);

        // And the positive half: nothing anywhere builds one of the absent routes.
        Assert.Equal(
            ["sys/leases/", "sys/renew", "sys/revoke", "sys/wrapping/", "cubbyhole/"],
            VaultCompatibilityGaps.AbsentSurfaces);
    }

    [Fact]
    [Requirement("SYS-101")]
    [Trait("Requirement", "SYS-101")]
    public void The_compatibility_gap_list_is_reachable_from_code_and_named_in_the_readme()
    {
        // SYS-101 requires the documentation to *list* the SYS-100 absences. The in-SDK half is
        // this list; the prose half is dotnet/README.md, which is asserted rather than assumed so
        // the two cannot drift.
        Assert.Equal(5, VaultCompatibilityGaps.AbsentSurfaces.Count);

        string readme = File.ReadAllText(ReadmePath());
        Assert.Contains("Vault compatibility gaps", readme, StringComparison.Ordinal);
        Assert.All(
            VaultCompatibilityGaps.AbsentSurfaces,
            surface => Assert.Contains(surface, readme, StringComparison.Ordinal));
    }

    // ---- The Complete-tier admin surfaces (no SYS-* id; deliberately untagged) ---------------

    [Fact]
    public async Task The_DoS_config_write_is_a_partial_update_and_every_DoS_route_is_v2_pinned()
    {
        // No [Requirement] attribute: `Sys.Dos.*` is named in 06's Complete-tier table and in
        // Appendix A, and neither states a MUST, so there is no id to cite and none is minted
        // (D-M7-10's rule). The test exists because the behaviour is real; the tag does not,
        // because the requirement is not.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"enabled":true,"window_secs":60,"max_requests":100,"auth_max_requests":10,"ban_secs":300,"refresh_secs":5}"""));
        transport.EnqueueResponse(200, body: Json("""{"enabled":true,"window_secs":60,"max_requests":100,"auth_max_requests":10,"ban_secs":900,"refresh_secs":5}"""));
        transport.EnqueueResponse(200, body: Json("""{"bans":2}"""));
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        using BastionVaultClient client = BuildClient(transport, apiPrefix: "v1");

        DosConfig current = await client.Sys.Dos.ReadConfigAsync(new RequestOptions { ApiVersion = "v1" });
        Assert.True(current.Enabled);
        Assert.Equal(60L, current.WindowSecs);
        Assert.Equal(300L, current.BanSecs);

        DosConfig updated = await client.Sys.Dos.WriteConfigAsync(new DosConfig { Enabled = false, BanSecs = 900 });
        Assert.Equal(900L, updated.BanSecs);
        // Partial: only the field the caller set is on the wire, so the other five survive. A
        // full-replace body here would silently reset the guard.
        Assert.Equal("""{"enabled":false,"ban_secs":900}""", BodyOf(transport.Requests[1]));

        Assert.Equal(2, (await client.Sys.Dos.StatsAsync()).GetProperty("bans").GetInt32());
        await client.Sys.Dos.BanAsync("203.0.113.7", ttlSecs: 60, reason: "scan");
        await client.Sys.Dos.UnbanAsync("203.0.113.7");

        Assert.Equal("""{"ttl_secs":60,"reason":"scan"}""", BodyOf(transport.Requests[3]));
        Assert.All(transport.Requests, request => Assert.StartsWith($"{Address}/v2/sys/dos/", request.Uri.ToString(), StringComparison.Ordinal));
        Assert.Equal($"{Address}/v2/sys/dos/bans/203.0.113.7", transport.Requests[4].Uri.ToString());
    }

    [Fact]
    public async Task The_dashboard_summary_keeps_its_two_audit_gated_keys_optional()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"mounts":4,"audit_24h":{"events":12},"attention":["seal"]}"""));
        transport.EnqueueResponse(200, body: Json("""{"mounts":4}"""));
        using BastionVaultClient client = BuildClient(transport);

        DashboardSummary withAudit = await client.Sys.DashboardSummaryAsync();
        Assert.Equal(12, withAudit.Audit24h!.Value.GetProperty("events").GetInt32());
        _ = Assert.Single(withAudit.Attention!.Value.EnumerateArray());

        // Omitted for a caller without audit read: null, never an empty object.
        DashboardSummary withoutAudit = await client.Sys.DashboardSummaryAsync();
        Assert.Null(withoutAudit.Audit24h);
        Assert.Null(withoutAudit.Attention);
        Assert.Equal(4, withoutAudit.Raw.GetProperty("mounts").GetInt32());
    }

    [Fact]
    public async Task The_sso_owner_transfer_and_exchange_routes_forward_their_bodies_verbatim()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"enabled":true}"""));
        transport.EnqueueResponse(200, body: Json("""{"providers":["okta"]}"""));
        transport.EnqueueResponse(200, body: Json("""{"transferred":3}"""));
        transport.EnqueueResponse(200, body: Json("""{"transferred":1}"""));
        transport.EnqueueResponse(200, body: Json("""{"transferred":1}"""));
        transport.EnqueueResponse(200, body: Json("""{"transferred":1}"""));
        transport.EnqueueResponse(200, body: Json("""{"entries":[]}"""));
        transport.EnqueueResponse(200, body: Json("""{"imported":0}"""));
        transport.EnqueueResponse(200, body: Json("""{"would_import":2}"""));
        transport.EnqueueResponse(200, body: Json("""{"imported":2}"""));
        using BastionVaultClient client = BuildClient(transport);

        Assert.True((await client.Sys.SsoSettingsAsync()).GetProperty("enabled").GetBoolean());
        Assert.Equal("okta", (await client.Sys.SsoProvidersAsync()).GetProperty("providers")[0].GetString());

        System.Text.Json.JsonElement spec = System.Text.Json.JsonDocument.Parse("""{"from":"ana","to":"bo"}""").RootElement.Clone();
        Assert.Equal(3, (await client.Sys.OwnerTransfer.KvAsync(spec))!.Value.GetProperty("transferred").GetInt32());
        _ = await client.Sys.OwnerTransfer.ResourceAsync(spec);
        _ = await client.Sys.OwnerTransfer.AssetGroupAsync(spec);
        _ = await client.Sys.OwnerTransfer.FileAsync(spec);
        _ = await client.Sys.Exchange.ExportAsync(spec);
        _ = await client.Sys.Exchange.ImportAsync(spec);
        _ = await client.Sys.Exchange.ImportPreviewAsync(spec);
        _ = await client.Sys.Exchange.ImportApplyAsync(spec);

        Assert.Equal(
            [
                $"{Address}/v1/sys/sso/settings",
                $"{Address}/v1/sys/sso/providers",
                $"{Address}/v1/sys/kv-owner/transfer",
                $"{Address}/v1/sys/resource-owner/transfer",
                $"{Address}/v1/sys/asset-group-owner/transfer",
                $"{Address}/v1/sys/file-owner/transfer",
                $"{Address}/v1/sys/exchange/export",
                $"{Address}/v1/sys/exchange/import",
                $"{Address}/v1/sys/exchange/import/preview",
                $"{Address}/v1/sys/exchange/import/apply",
            ],
            transport.Requests.Select(request => request.Uri.ToString()));
        // Forwarded verbatim: nothing here models a body the specification does not write.
        Assert.Equal("""{"from":"ana","to":"bo"}""", BodyOf(transport.Requests[2]));
    }

    [Fact]
    public async Task Every_Complete_tier_reader_raises_BV_PROTOCOL_002_rather_than_inventing_an_empty_result()
    {
        // D-M1c-25 applied uniformly across the surfaces this slice adds: an operation whose
        // specification names a response object answers a body-less 2xx with BV-PROTOCOL-002, not
        // with a default-constructed result. Untagged for the same reason the other Complete-tier
        // tests are: these rows carry no SYS-* id.
        FakeTransport transport = new();
        for (int index = 0; index < 10; index++)
        {
            transport.EnqueueResponse(204);
        }

        using BastionVaultClient client = BuildClient(transport);

        Func<Task>[] readers =
        [
            () => client.Sys.Dos.ReadConfigAsync(),
            () => client.Sys.Dos.WriteConfigAsync(new DosConfig()),
            () => client.Sys.Dos.StatsAsync(),
            () => client.Sys.DashboardSummaryAsync(),
            () => client.Sys.SsoSettingsAsync(),
            () => client.Sys.SsoProvidersAsync(),
        ];

        foreach (Func<Task> reader in readers)
        {
            BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(reader);
            Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, failure.Code);
        }

        // The forwarding surfaces are the deliberate exception: their response shape is not
        // specified either, so a body-less answer is `null` rather than a protocol failure.
        System.Text.Json.JsonElement empty = System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone();
        Assert.Null(await client.Sys.OwnerTransfer.KvAsync(empty));
        Assert.Null(await client.Sys.Exchange.ExportAsync(empty));
        // Same for the two identity listings: TRN-050 makes an empty LIST an empty list.
        Assert.Empty(await client.Identity.SshSecurityKey.ListAsync());
        Assert.Empty(await client.Identity.NamespaceAssignment.ListAsync());
    }

    [Fact]
    public async Task A_default_JsonElement_body_is_refused_client_side_rather_than_throwing_a_runtime_exception()
    {
        // `default(JsonElement)` is Undefined, and System.Text.Json answers it with a bare
        // InvalidOperationException. ERR-020 and TRN-054 say a caller never catches a runtime
        // exception type, so the two forwarding surfaces refuse it with BV-INPUT-001 instead.
        FakeTransport transport = new();
        using BastionVaultClient client = BuildClient(transport);

        foreach (Func<Task> call in new Func<Task>[]
                 {
                     () => client.Sys.OwnerTransfer.KvAsync(default),
                     () => client.Sys.OwnerTransfer.ResourceAsync(default),
                     () => client.Sys.OwnerTransfer.AssetGroupAsync(default),
                     () => client.Sys.OwnerTransfer.FileAsync(default),
                     () => client.Sys.Exchange.ExportAsync(default),
                     () => client.Sys.Exchange.ImportAsync(default),
                     () => client.Sys.Exchange.ImportPreviewAsync(default),
                     () => client.Sys.Exchange.ImportApplyAsync(default),
                 })
        {
            BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(call);
            Assert.Equal(ErrorCodes.InputInvalidArgument, failure.Code);
            Assert.Equal(0, failure.Attempts);
        }

        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task A_DoS_config_read_distinguishes_an_absent_number_from_a_zero_and_a_false_from_a_missing_flag()
    {
        // `0` is a real quota value on this surface, so every field is nullable and an absent one
        // reads as null rather than as 0 — the same rule NamespaceQuotas deliberately does *not*
        // follow, because SYS-060 defines `0` as "unlimited" there and nothing is ever absent.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"enabled":false,"max_requests":0}"""));
        transport.EnqueueResponse(200, body: Json("""{"enabled":true}"""));
        transport.EnqueueResponse(200, body: Json("""{"window_secs":30}"""));
        using BastionVaultClient client = BuildClient(transport);

        DosConfig sparse = await client.Sys.Dos.ReadConfigAsync();
        Assert.False(sparse.Enabled);
        Assert.Equal(0L, sparse.MaxRequests);
        Assert.Null(sparse.WindowSecs);
        Assert.Null(sparse.BanSecs);

        // And a patch that sets nothing writes an empty object rather than six zeroes.
        DosConfig echoed = await client.Sys.Dos.WriteConfigAsync(new DosConfig());
        Assert.True(echoed.Enabled);
        Assert.Equal("{}", BodyOf(transport.Requests[1]));

        // An absent `enabled` is null, not false: the guard's state is unknown, not "off".
        DosConfig unknown = await client.Sys.Dos.ReadConfigAsync();
        Assert.Null(unknown.Enabled);
        Assert.Equal(30L, unknown.WindowSecs);
    }

    [Theory]
    [Requirement("SYS-091")]
    [Trait("Requirement", "SYS-091")]
    [InlineData(500, null, "BV-SERVER-005")]
    [InlineData(500, "disk is full", "BV-SERVER-005")]
    [InlineData(400, "backup archive is corrupted", "BV-INPUT-103")]
    public async Task The_SYS_091_mapping_is_the_shared_tables_and_stays_narrow(int status, string? message, string expected)
    {
        // The counterpart to the test above, and the assertion that keeps it narrow: a 500 that is
        // not an integrity failure — no body at all, or a message Appendix B §2 does not name —
        // still falls through to the status table.
        //
        // The third row changed with R-23's fix (D-M8-2). It used to assert BV-INPUT-100 at a 400,
        // which was the *remap's* answer: D-M7-36's operation-local remap was scoped to a 500, and
        // the generated rule could not fire at any status because the generator compiled the
        // appendix's `+ a/b/c` alternation as a conjunction. With the generator fixed and the remap
        // deleted, the shared table answers, and Appendix B §2's row carries no status qualifier —
        // unlike the `contains (500) hmac verification failed` row three lines below it, which
        // shows the appendix qualifies by status when it means to. Recognition runs ahead of the
        // status table (D-M1c-3), so BV-INPUT-103 at a 400 is the appendix's answer, not a
        // widening: the old expectation recorded the defect.
        FakeTransport transport = new();
        transport.EnqueueResponse(
            status,
            body: message is null ? default : Json($$"""{"error":{{System.Text.Json.JsonSerializer.Serialize(message)}}}"""));
        using BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.RestoreAsync(new byte[] { 1 }));

        Assert.Equal(expected, failure.Code);
    }

    // ---- ERR-040's KV-v2 row, re-booked to M7 by D-M4-14 ------------------------------------

    [Fact]
    [Requirement("ERR-040")]
    [Trait("Requirement", "ERR-040")]
    public async Task A_v1_shaped_read_on_a_v2_mount_gets_the_data_route_note_and_nothing_else_does()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(404, body: Json("""{"error":"Nothing exists at this path."}"""));
        transport.EnqueueResponse(200, body: Json("""{"secret/":{"type":"kv-v2"}}"""));
        transport.EnqueueResponse(404, body: Json("""{"error":"Nothing exists at this path."}"""));
        using BastionVaultClient client = BuildClient(transport);

        BastionVaultException onV2 = await Assert.ThrowsAsync<BastionVaultException>(() => client.Kv.V1.ReadAsync("app/db", "secret"));

        Assert.Equal(ErrorCodes.NotFoundPathNotFound, onV2.Code);
        Assert.Contains("This mount is KV v2; use `secret/data/app/db` or `Kv.ReadSecret`.", onV2.Hint, StringComparison.Ordinal);

        // The mount table is now warm (SYS-026), and `secret` is still kv-v2, so a second 404 on
        // the same mount costs no second lookup — three enqueued responses, three requests.
        BastionVaultException again = await Assert.ThrowsAsync<BastionVaultException>(() => client.Kv.V1.ReadAsync("app/other", "secret"));
        Assert.Contains("This mount is KV v2; use `secret/data/app/other`", again.Hint, StringComparison.Ordinal);
        Assert.Equal(3, transport.Requests.Count);
    }

    [Fact]
    [Requirement("ERR-040")]
    [Trait("Requirement", "ERR-040")]
    public async Task The_note_is_absent_on_a_v1_mount_and_a_failed_mount_lookup_never_replaces_the_callers_error()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(404, body: Json("""{"error":"Nothing exists at this path."}"""));
        transport.EnqueueResponse(200, body: Json("""{"secret/":{"type":"kv"}}"""));
        using BastionVaultClient client = BuildClient(transport);

        BastionVaultException onV1 = await Assert.ThrowsAsync<BastionVaultException>(() => client.Kv.V1.ReadAsync("app/db", "secret"));
        Assert.DoesNotContain("This mount is KV v2", onV1.Hint, StringComparison.Ordinal);

        // A token with no read on sys/mounts is the ordinary case, and an enrichment must never
        // replace the failure it was trying to explain: the caller still sees its own 404.
        FakeTransport denied = new();
        denied.EnqueueResponse(404, body: Json("""{"error":"Nothing exists at this path."}"""));
        denied.EnqueueResponse(403, body: Json("""{"error":"permission denied"}"""));
        using BastionVaultClient deniedClient = BuildClient(denied);

        BastionVaultException original = await Assert.ThrowsAsync<BastionVaultException>(() => deniedClient.Kv.V1.ReadAsync("app/db", "secret"));
        Assert.Equal(ErrorCodes.NotFoundPathNotFound, original.Code);
        Assert.Equal(404, original.StatusCode);
        Assert.DoesNotContain("This mount is KV v2", original.Hint, StringComparison.Ordinal);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static string ReadmePath()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BastionVault.IntegrationSdk.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "README.md");
    }

    private static BastionVaultClient BuildClient(
        ITransport transport,
        int maxAttempts = 1,
        string apiPrefix = "v1",
        long? maxResponseBytes = null,
        RetryPolicy? retryPolicy = null)
    {
        return new BastionVaultClient(
            new BastionVaultClientOptions
            {
                Address = Address,
                Token = Token,
                ApiPrefix = apiPrefix,
                Transport = transport,
                MaxResponseBytes = maxResponseBytes,
                RateGate = new RateGate { RatePerSecond = 0 },
                RetryPolicy = retryPolicy ?? new RetryPolicy { MaxAttempts = maxAttempts, InitialBackoff = TimeSpan.Zero },
            },
            EnvironmentSource.None);
    }

    /// <summary>A discovery-mode client seeded with a pinned node and a candidate set (D-M5-15), as <c>FailoverUnitTests</c> builds one.</summary>
    private static BastionVaultClient Pinned(ITransport transport, int maxAttempts = 3, IReadOnlyList<string>? candidates = null)
    {
        BastionVaultClient client = new(
            new BastionVaultClientOptions
            {
                Address = "vault.corp.example",
                Token = Token,
                Transport = transport,
                RateGate = new RateGate { RatePerSecond = 0 },
                RetryPolicy = new RetryPolicy { MaxAttempts = maxAttempts, InitialBackoff = TimeSpan.Zero },
            },
            EnvironmentSource.None);

        client.Context.Discovery.Seed(
            new NodeSelection(One, NodeState.ActiveLeader, null),
            (candidates ?? [One, Two])
                .Select(url => new Uri(url, UriKind.Absolute))
                .Select(uri => new Candidate(uri.AbsoluteUri.TrimEnd('/'), uri.Host, uri.Port, null, null))
                .ToArray());
        return client;
    }

    private static TransportResponse Json(int status, string body)
    {
        return new TransportResponse(status, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), Encoding.UTF8.GetBytes(body));
    }

    private static string BodyOf(TransportRequest request)
    {
        return Encoding.UTF8.GetString(request.Body.Span);
    }

    private static ReadOnlyMemory<byte> Json(string json)
    {
        return Encoding.UTF8.GetBytes(json);
    }

    private sealed class RoutingTransport : ITransport
    {
        private readonly Func<TransportRequest, int, TransportResponse> answer;
        private readonly List<TransportRequest> requests = [];

        public RoutingTransport(Func<TransportRequest, int, TransportResponse> answer)
        {
            this.answer = answer;
        }

        public IReadOnlyList<TransportRequest> Requests => requests;

        public bool SupportsCustomVerbs => true;

        public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken = default)
        {
            requests.Add(request);
            return Task.FromResult(answer(request, requests.Count - 1));
        }
    }
}
