using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// AUT-054's FerroGate administration surface, reached from
/// <see cref="FerrogateOperations.Admin"/>. Every operation here is <b>root</b>-authenticated per
/// Appendix A.
/// </summary>
/// <remarks>
/// <para>
/// Reads and writes exchange <see cref="Response"/> and <see cref="JsonElement"/> rather than a
/// typed record per endpoint (D-M6-5). AUT-054 enumerates <i>paths</i>, not field sets, and no
/// requirement pins the shape of the <c>config</c> or <c>register</c> documents; a typed model
/// here would be the SDK inventing a contract the specification has not written, which D-M1c-25
/// forbids. The four typed FerroGate answers the specification <i>does</i> pin — AUT-051's
/// requirement, AUT-052's status and enrolment — are typed, on
/// <see cref="FerrogateOperations"/>.
/// </para>
/// <para>
/// The approve/reject/revoke trio is three methods rather than one taking a verb, so the set of
/// legal transitions is the public API rather than a runtime string check.
/// </para>
/// </remarks>
public sealed class FerrogateAdminOperations
{
    private readonly AuthEndpoint endpoint;

    internal FerrogateAdminOperations(ClientContext context, string activeNamespace)
    {
        endpoint = new AuthEndpoint(context, activeNamespace);
    }

    /// <summary>AUT-054: <c>GET auth/{mount}/config</c>.</summary>
    public Task<Response?> ReadConfigAsync(string mount = "ferrogate", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.ReadAsync($"auth/{AuthEndpoint.Mount(mount)}/config", options, cancellationToken);
    }

    /// <summary>AUT-054: <c>POST auth/{mount}/config</c>.</summary>
    public Task<Response?> WriteConfigAsync(JsonElement config, string mount = "ferrogate", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.WriteAsync($"auth/{AuthEndpoint.Mount(mount)}/config", AuthEndpoint.Payload(config), options, cancellationToken);
    }

    /// <summary>AUT-054: <c>POST auth/{mount}/register</c> — an administrator registering a machine directly.</summary>
    public Task<Response?> RegisterAsync(JsonElement machine, string mount = "ferrogate", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.WriteAsync($"auth/{AuthEndpoint.Mount(mount)}/register", AuthEndpoint.Payload(machine), options, cancellationToken);
    }

    /// <summary>AUT-054: <c>LIST auth/{mount}/machines/</c>. An empty list when there are none (TRN-050).</summary>
    public Task<IReadOnlyList<string>> ListMachinesAsync(string mount = "ferrogate", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.ListKeysAsync($"auth/{AuthEndpoint.Mount(mount)}/machines/", options, cancellationToken);
    }

    /// <summary>AUT-054: <c>GET auth/{mount}/machines/{machineId}</c>.</summary>
    public Task<Response?> ReadMachineAsync(string machineId, string mount = "ferrogate", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.ReadAsync(MachinePath(mount, machineId), options, cancellationToken);
    }

    /// <summary>AUT-054: <c>DELETE auth/{mount}/machines/{machineId}</c>.</summary>
    public async Task DeleteMachineAsync(string machineId, string mount = "ferrogate", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        _ = await endpoint.DeleteAsync(MachinePath(mount, machineId), options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>AUT-054: <c>POST auth/{mount}/machines/{machineId}/approve</c>. The machine may log in afterwards.</summary>
    public Task<Response?> ApproveAsync(string machineId, string mount = "ferrogate", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.WriteAsync($"{MachinePath(mount, machineId)}/approve", null, options, cancellationToken);
    }

    /// <summary>AUT-054: <c>POST auth/{mount}/machines/{machineId}/reject</c>. Its logins then fail <c>BV-AUTH-013</c>.</summary>
    public Task<Response?> RejectAsync(string machineId, string mount = "ferrogate", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.WriteAsync($"{MachinePath(mount, machineId)}/reject", null, options, cancellationToken);
    }

    /// <summary>AUT-054: <c>POST auth/{mount}/machines/{machineId}/revoke</c>. Its logins then fail <c>BV-AUTH-014</c>.</summary>
    public Task<Response?> RevokeAsync(string machineId, string mount = "ferrogate", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.WriteAsync($"{MachinePath(mount, machineId)}/revoke", null, options, cancellationToken);
    }

    private static string MachinePath(string mount, string machineId)
    {
        return $"auth/{AuthEndpoint.Mount(mount)}/machines/{AuthEndpoint.Segment(machineId)}";
    }
}
