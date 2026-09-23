using BastionVault.IntegrationSdk.DocsSamples.Infrastructure;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.DocsSamples.Samples;

/// <summary>
/// The executed sample shown by <c>docs/dotnet/compatibility-gaps.md</c> (D9). This is a
/// reference document (DR-0018 D-M11-9 names D3, D7, D9, D11 as such), so it does not follow
/// DOC-010's guide order; it still owes DOC-003 one compiled, executed sample, because it is a
/// D1-D10 document.
/// </summary>
public sealed class CompatibilityGapsSamples : IClassFixture<MockVaultFixture>
{
    private const string CertLoginRoute = "/v1/auth/cert/login";

    private readonly MockVaultFixture vault;

    public CompatibilityGapsSamples(MockVaultFixture vault)
    {
        this.vault = vault;
    }

    [Fact]
    public void The_absent_surfaces_are_a_list_the_SDK_exposes_to_code_too()
    {
        // docs:begin compatibility-gaps/absent-surfaces
        // The same list this page prints in prose, reachable from code so a migration script can
        // check it too (SYS-100, SYS-101).
        foreach (string surface in VaultCompatibilityGaps.AbsentSurfaces)
        {
            Console.WriteLine($"not served by this server: {surface}");
        }
        // docs:end compatibility-gaps/absent-surfaces

        Assert.Contains("sys/leases/", VaultCompatibilityGaps.AbsentSurfaces);
        Assert.Contains("cubbyhole/", VaultCompatibilityGaps.AbsentSurfaces);
    }

    [Fact]
    public async Task Cert_auth_is_registered_but_answers_unsupported()
    {
        vault.Server.SetRouteResponse(
            CertLoginRoute,
            new MockResponse(500, Body: """{"error":"Logical backend path not supported."}"""));
        using BastionVaultClient client = vault.CreateClient();

        // docs:begin compatibility-gaps/cert-auth-disabled
        // AUT-070: the cert auth backend registers no paths on current servers. The SDK still
        // exposes Auth.Cert.Login and still presents the client certificate at the TLS layer
        // (CFG-044); the server's "path not supported" answer is mapped to BV-SERVER-004 rather
        // than surfacing as a generic 500, so a caller can tell "this backend is not enabled"
        // apart from "this backend is broken".
        try
        {
            await client.Auth.Cert.LoginAsync();
        }
        catch (BastionVaultException e) when (e.Code == ErrorCodes.ServerUnsupportedByServer)
        {
            Console.Error.WriteLine($"{e.Code}: {e.Hint}");
        }
        // docs:end compatibility-gaps/cert-auth-disabled
    }
}
