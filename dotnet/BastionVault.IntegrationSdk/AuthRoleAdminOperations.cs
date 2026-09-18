using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// AUT-060's role and config administration, reached from <see cref="OidcOperations.Admin"/> and
/// <see cref="SamlOperations.Admin"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>One type, two mounts</b> (D-M6-6). Appendix A gives OIDC and SAML the <i>same</i> admin
/// surface — <c>auth/{mount}/config</c> and <c>auth/{mount}/role[/{name}]</c> — so two identical
/// classes would be two places for the same path to drift. Each instance carries its own default
/// mount, which is the only thing that differs between them, and every method still takes an
/// explicit <c>mount</c> for a non-default one.
/// </para>
/// <para>
/// Config and role documents are exchanged as <see cref="JsonElement"/> and <see cref="Response"/>
/// for the reason recorded on <see cref="FerrogateAdminOperations"/>: AUT-060 names paths, not
/// field sets, and inventing a typed model would be the SDK writing a contract the specification
/// has not (D-M1c-25).
/// </para>
/// </remarks>
public sealed class AuthRoleAdminOperations
{
    private readonly AuthEndpoint endpoint;
    private readonly string defaultMount;

    internal AuthRoleAdminOperations(ClientContext context, string activeNamespace, string defaultMount)
    {
        endpoint = new AuthEndpoint(context, activeNamespace);
        this.defaultMount = defaultMount;
    }

    /// <summary>AUT-060: <c>GET auth/{mount}/config</c>.</summary>
    public Task<Response?> ReadConfigAsync(string? mount = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.ReadAsync($"auth/{Mount(mount)}/config", options, cancellationToken);
    }

    /// <summary>AUT-060: <c>POST auth/{mount}/config</c>.</summary>
    public Task<Response?> WriteConfigAsync(JsonElement config, string? mount = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.WriteAsync($"auth/{Mount(mount)}/config", AuthEndpoint.Payload(config), options, cancellationToken);
    }

    /// <summary>AUT-060: <c>LIST auth/{mount}/role</c>. An empty list when there are none (TRN-050).</summary>
    public Task<IReadOnlyList<string>> ListRolesAsync(string? mount = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.ListKeysAsync($"auth/{Mount(mount)}/role", options, cancellationToken);
    }

    /// <summary>AUT-060: <c>GET auth/{mount}/role/{name}</c>.</summary>
    public Task<Response?> ReadRoleAsync(string name, string? mount = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.ReadAsync(RolePath(mount, name), options, cancellationToken);
    }

    /// <summary>AUT-060: <c>POST auth/{mount}/role/{name}</c>.</summary>
    public Task<Response?> WriteRoleAsync(string name, JsonElement role, string? mount = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.WriteAsync(RolePath(mount, name), AuthEndpoint.Payload(role), options, cancellationToken);
    }

    /// <summary>AUT-060: <c>DELETE auth/{mount}/role/{name}</c>.</summary>
    public async Task DeleteRoleAsync(string name, string? mount = null, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        _ = await endpoint.DeleteAsync(RolePath(mount, name), options, cancellationToken).ConfigureAwait(false);
    }

    private string Mount(string? mount)
    {
        return AuthEndpoint.Mount(mount ?? defaultMount);
    }

    private string RolePath(string? mount, string name)
    {
        return $"auth/{Mount(mount)}/role/{AuthEndpoint.Segment(name)}";
    }
}
