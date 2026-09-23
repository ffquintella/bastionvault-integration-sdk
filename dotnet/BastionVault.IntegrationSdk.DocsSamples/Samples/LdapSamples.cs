using System.Text.Json;
using BastionVault.IntegrationSdk.DocsSamples.Infrastructure;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.DocsSamples.Samples;

/// <summary>
/// Every executed sample shown by <c>docs/dotnet/engines/ldap.md</c> (D6). Section 17 carries no
/// dedicated usage guide for LDAP, so this page — and these samples — are built directly from
/// <c>12-other-engines-and-identity.md</c>'s LDAP / Active Directory table. Reuses
/// <see cref="MockVaultFixture"/>'s server: routes are bound per test on
/// <see cref="MockVaultFixture.Server"/> and cleared after, so no test leaks a route to another.
/// </summary>
public sealed class LdapSamples : IClassFixture<MockVaultFixture>
{
    private const string ConfigRoute = "/v1/openldap/config";
    private const string RotateRootRoute = "/v1/openldap/rotate-root";
    private const string CheckConnectionRoute = "/v1/openldap/check-connection";
    private const string StaticRoleRoute = "/v1/openldap/static-role/dbadmin";
    private const string StaticCredRoute = "/v1/openldap/static-cred/dbadmin";
    private const string LibrarySetRoute = "/v1/openldap/library/svc-accounts";
    private const string CheckOutRoute = "/v1/openldap/library/svc-accounts/check-out";
    private const string CheckInRoute = "/v1/openldap/library/svc-accounts/check-in";

    private readonly MockVaultFixture vault;

    public LdapSamples(MockVaultFixture vault)
    {
        this.vault = vault;
    }

    [Fact]
    public void The_policy_this_guide_needs_is_the_policy_PolicyBuilder_builds()
    {
        // docs:begin ldap/policy
        string hcl = new PolicyBuilder()
            .AddPath("openldap/config", [Capability.Create, Capability.Read, Capability.Update])
            .AddPath("openldap/rotate-root", [Capability.Update])
            .AddPath("openldap/static-role/dbadmin", [Capability.Create, Capability.Read])
            .AddPath("openldap/static-cred/dbadmin", [Capability.Read])
            .AddPath("openldap/library/svc-accounts", [Capability.Create, Capability.Read])
            .AddPath("openldap/library/svc-accounts/check-out", [Capability.Update])
            .AddPath("openldap/library/svc-accounts/check-in", [Capability.Update])
            .Build();

        Console.WriteLine(hcl);
        // docs:end ldap/policy

        Assert.Equal(PolicyShownInTheGuide(), hcl.Trim());
    }

    private static string PolicyShownInTheGuide()
    {
        string path = Path.Combine(DocsRepository.Docs.FullName, "dotnet", "engines", "ldap.md");
        string[] lines = File.ReadAllLines(path);
        int opening = Array.FindIndex(lines, line => line.TrimEnd() == "```hcl");
        Assert.True(opening >= 0, $"{path} shows no ```hcl policy block (DOC-011).");
        int closing = Array.FindIndex(lines, opening + 1, line => line.TrimEnd() == "```");
        Assert.True(closing > opening, $"{path}: the ```hcl block is never closed.");
        return string.Join('\n', lines[(opening + 1)..closing]).Trim();
    }

    [Fact]
    public async Task Step_1_configure_the_directory_rotate_the_root_password_and_probe_it()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(ConfigRoute, Json(200, "{}"));
        vault.Server.SetRouteResponse(RotateRootRoute, Json(200, "{}"));
        vault.Server.SetRouteResponse(CheckConnectionRoute, Json(200, CheckConnectionBody()));

        // docs:begin ldap/configure-and-rotate
        await client.Ldap.WriteConfigAsync(new LdapConfig
        {
            Url = "ldaps://ldap.example.com:636",
            BindDn = "cn=vault,ou=svc,dc=example,dc=com",
            BindPass = new SecretString("bindpassword123"),
            UserDn = "ou=people,dc=example,dc=com",
            DirectoryType = "active_directory",
            StartTls = false,
            TlsMinVersion = "tls13",
        });
        // LDP-001: InsecureTls stays unset here; setting it true without AcknowledgeInsecureTls
        // also true is refused client-side (BV-INPUT-001), before any request is sent.

        await client.Ldap.RotateRootAsync();

        LdapCheckConnectionResult probe = await client.Ldap.CheckConnectionAsync();
        Console.WriteLine($"connection ok: {probe.Ok} (stage {probe.Stage}, {probe.LatencyMs}ms)");
        // docs:end ldap/configure-and-rotate

        Assert.True(probe.Ok);
        Assert.Equal("bind", probe.Stage);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task Step_2_define_a_static_role_and_read_its_rotated_credential()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(StaticRoleRoute, Json(200, "{}"));
        vault.Server.SetRouteResponse(StaticCredRoute, Json(200, StaticCredBody()));

        // docs:begin ldap/static-role-and-cred
        await client.Ldap.StaticRoles.WriteAsync("dbadmin", new LdapStaticRole
        {
            Dn = "cn=dbadmin,ou=svc,dc=example,dc=com",
            Username = "dbadmin",
            RotationPeriod = TimeSpan.FromHours(24),
        });

        LdapStaticCred? cred = await client.Ldap.StaticCredAsync("dbadmin");
        Console.WriteLine($"{cred!.Username}: last rotated {cred.LastRotated:O}");
        // The password never touches this line unredacted: cred.Password.Reveal() is where it would.
        // docs:end ldap/static-role-and-cred

        Assert.Equal("dbadmin", cred!.Username);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task Step_3_pool_shared_service_accounts_through_the_check_out_library()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        vault.Server.SetRouteResponse(LibrarySetRoute, Json(200, "{}"));
        vault.Server.SetRouteResponse(CheckOutRoute, Json(200, CheckOutBody()));
        vault.Server.SetRouteResponse(CheckInRoute, Json(200, "{}"));

        // docs:begin ldap/library-checkout
        await client.Ldap.Library.WriteAsync("svc-accounts", new LdapLibrarySet
        {
            ServiceAccountNames = ["svc-1", "svc-2"],
            Ttl = TimeSpan.FromHours(1),
            MaxTtl = TimeSpan.FromHours(24),
        });

        LdapLibraryCheckOut checkedOut = await client.Ldap.Library.CheckOutAsync("svc-accounts", ttl: TimeSpan.FromMinutes(30));
        Console.WriteLine($"checked out {checkedOut.ServiceAccountName}, lease {checkedOut.LeaseId}");

        // Always check an account back in when the caller is done with it; the affinity_ttl and
        // disable_check_in_enforcement fields on the set control what happens if you do not.
        await client.Ldap.Library.CheckInAsync("svc-accounts", checkedOut.ServiceAccountName);
        // docs:end ldap/library-checkout

        Assert.Equal("svc-1", checkedOut.ServiceAccountName);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task The_whole_program_configures_ldap_and_reads_a_static_credential()
    {
        vault.Server.ClearRouteResponses();

        // docs:begin ldap/complete
        using BastionVaultClient client = new();

        try
        {
            vault.Server.SetRouteResponse(ConfigRoute, Json(200, "{}"));
            await client.Ldap.WriteConfigAsync(new LdapConfig
            {
                Url = "ldaps://ldap.example.com:636",
                BindDn = "cn=vault,ou=svc,dc=example,dc=com",
                BindPass = new SecretString("bindpassword123"),
            });
            Console.WriteLine("directory configured");

            vault.Server.SetRouteResponse(StaticRoleRoute, Json(200, "{}"));
            await client.Ldap.StaticRoles.WriteAsync("dbadmin", new LdapStaticRole { Username = "dbadmin", RotationPeriod = TimeSpan.FromHours(24) });

            vault.Server.SetRouteResponse(StaticCredRoute, Json(200, StaticCredBody()));
            LdapStaticCred? cred = await client.Ldap.StaticCredAsync("dbadmin");
            Console.WriteLine($"static credential ready for {cred!.Username}");
        }
        catch (BastionVaultException e)
        {
            Console.Error.WriteLine($"{e.Code}: {e.Message} ({e.Hint}); retryable: {e.Retryable}");
            throw;
        }
        // docs:end ldap/complete

        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    [Fact]
    public async Task What_can_go_wrong_maps_every_ldap_code_to_a_remedy()
    {
        vault.Server.ClearRouteResponses();
        using BastionVaultClient client = vault.CreateClient();

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(async () =>
        {
            // docs:begin ldap/handling-errors
            try
            {
                // LDP-001: refused client-side, no request sent - the mirror of the server's own check.
                await client.Ldap.WriteConfigAsync(new LdapConfig { InsecureTls = true });
            }
            catch (BastionVaultException e)
            {
                string remedy = e.Code switch
                {
                    ErrorCodes.InputInvalidArgument => "set AcknowledgeInsecureTls too (LDP-001), or fix the argument named in the message",
                    ErrorCodes.NotFoundPathNotFound => "check the mount and path; nothing is configured there yet",
                    ErrorCodes.AuthzPermissionDenied => "extend the calling token's policy to cover this path",
                    _ => "look the code up in the error reference",
                };
                Console.Error.WriteLine($"{e.Code}: {e.Message} - {remedy}");
                throw;
            }
            // docs:end ldap/handling-errors
        });

        Assert.Equal(ErrorCodes.InputInvalidArgument, failure.Code);
        vault.Server.ClearRouteResponses();
        vault.ServeHealthyVault();
    }

    private static MockResponse Json(int status, string body) => new(status, Body: Compact(body));

    private static string Compact(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    private static string CheckConnectionBody() => Compact("""
        {
          "data": {
            "ok": true,
            "stage": "bind",
            "url": "ldaps://ldap.example.com:636",
            "bind_dn": "cn=vault,ou=svc,dc=example,dc=com",
            "host": "ldap.example.com",
            "port": 636,
            "scheme": "ldaps",
            "latency_ms": 42
          }
        }
        """);

    private static string StaticCredBody() => Compact("""
        {
          "data": {
            "username": "dbadmin",
            "dn": "cn=dbadmin,ou=svc,dc=example,dc=com",
            "password": "rotated-Sup3rSecret!",
            "last_rotated": "2026-09-20T00:00:00Z",
            "ttl_secs": 86400
          }
        }
        """);

    private static string CheckOutBody() => Compact("""
        {
          "data": {
            "service_account_name": "svc-1",
            "password": "checkedout-Sup3rSecret!",
            "lease_id": "openldap/library/svc-accounts/check-out/lease-1",
            "ttl_secs": 1800
          }
        }
        """);
}
