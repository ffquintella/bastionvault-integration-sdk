using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// The per-field sub-paths of an AppID role (AUT-043, Appendix A's
/// <c>Auth.AppId.Admin.&lt;Field&gt;</c> row).
/// </summary>
/// <remarks>
/// An enum rather than a free string (D-M6-9): Appendix A enumerates exactly these thirteen, the
/// server has no other, and a typo in a free string would reach the server as a role sub-path that
/// does not exist and come back as a puzzling <c>404</c>. Adding a member when the server adds a
/// field is an additive, non-breaking change.
/// </remarks>
public enum AppIdRoleField
{
    /// <summary><c>policies</c>.</summary>
    Policies,

    /// <summary><c>bound-cidr-list</c>.</summary>
    BoundCidrList,

    /// <summary><c>secret-id-bound-cidrs</c>.</summary>
    SecretIdBoundCidrs,

    /// <summary><c>bound-source-ips</c>.</summary>
    BoundSourceIps,

    /// <summary><c>bypass-machine-binding</c> — the AUT-040 machine-identity gate's per-role escape.</summary>
    BypassMachineBinding,

    /// <summary><c>token-bound-cidrs</c>.</summary>
    TokenBoundCidrs,

    /// <summary><c>bind-secret-id</c>.</summary>
    BindSecretId,

    /// <summary><c>secret-id-num-uses</c>.</summary>
    SecretIdNumUses,

    /// <summary><c>secret-id-ttl</c>.</summary>
    SecretIdTtl,

    /// <summary><c>period</c>.</summary>
    Period,

    /// <summary><c>token-num-uses</c>.</summary>
    TokenNumUses,

    /// <summary><c>token-ttl</c>.</summary>
    TokenTtl,

    /// <summary><c>token-max-ttl</c>.</summary>
    TokenMaxTtl,
}

/// <summary>
/// AUT-043's full AppID role-administration surface, reached from
/// <see cref="AppIdOperations.Admin"/>. Complete-level; every operation requires a token.
/// </summary>
/// <remarks>
/// <para>
/// Role and config documents are exchanged as <see cref="JsonElement"/> and <see cref="Response"/>
/// for the reason recorded on <see cref="FerrogateAdminOperations"/>: AUT-043 enumerates paths and
/// defers their field sets to Appendix A, which lists no schema for them (D-M6-5). The two AppID
/// shapes the specification <i>does</i> pin — AUT-042's <see cref="SecretIdOptions"/> and
/// <see cref="SecretIdInfo"/> — stay typed, on <see cref="AppIdOperations"/>.
/// </para>
/// <para>
/// A secret id is always taken and returned as <see cref="SecretString"/>, never as a plain string,
/// including on the lookup and destroy paths where it travels in a request <i>body</i> (CNF-031).
/// An accessor is a plain string: it identifies a secret id without being usable as one.
/// </para>
/// </remarks>
public sealed class AppIdAdminOperations
{
    /// <summary>
    /// Appendix A's thirteen field path segments, positionally aligned with
    /// <see cref="AppIdRoleField"/>. One table rather than a thirteen-arm switch, so the enum and
    /// the wire names cannot fall out of step silently.
    /// </summary>
    private static readonly string[] FieldSegments =
    [
        "policies",
        "bound-cidr-list",
        "secret-id-bound-cidrs",
        "bound-source-ips",
        "bypass-machine-binding",
        "token-bound-cidrs",
        "bind-secret-id",
        "secret-id-num-uses",
        "secret-id-ttl",
        "period",
        "token-num-uses",
        "token-ttl",
        "token-max-ttl",
    ];

    private readonly AuthEndpoint endpoint;

    internal AppIdAdminOperations(ClientContext context, string activeNamespace)
    {
        endpoint = new AuthEndpoint(context, activeNamespace);
    }

    /// <summary>AUT-043: <c>LIST auth/{mount}/role</c> — every AppID role name on the mount.</summary>
    /// <remarks>Wire params: <c>mount</c> builds the route; no body. Returns an empty list when there are none (TRN-050), never <see langword="null"/>. Conformance: Standard (AUT-043). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.AppId.Admin.ListRoles — AUT-043</spec>
    public Task<IReadOnlyList<string>> ListRolesAsync(string mount = "approle", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.ListKeysAsync($"auth/{AuthEndpoint.Mount(mount)}/role", options, cancellationToken);
    }

    /// <summary>AUT-043: <c>GET auth/{mount}/role/{roleName}</c> — the role document.</summary>
    /// <remarks>Wire params: <c>mount</c>, <c>roleName</c> build the route; no body. Returns <see langword="null"/> on a <c>404</c> with an empty body. Conformance: Standard (AUT-043). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.AppId.Admin.ReadRole — AUT-043</spec>
    public Task<Response?> ReadRoleAsync(string roleName, string mount = "approle", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.ReadAsync(RolePath(mount, roleName), options, cancellationToken);
    }

    /// <summary>AUT-043: <c>POST auth/{mount}/role/{roleName}</c> — creates or overwrites the role.</summary>
    /// <remarks>Wire params: <paramref name="role"/> sent verbatim as the body (Appendix A gives no field set, D-M6-5). Returns the server's write response, or <see langword="null"/> on an empty body. Conformance: Standard (AUT-043). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.AppId.Admin.WriteRole — AUT-043</spec>
    public Task<Response?> WriteRoleAsync(string roleName, JsonElement role, string mount = "approle", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.WriteAsync(RolePath(mount, roleName), AuthEndpoint.Payload(role), options, cancellationToken);
    }

    /// <summary>AUT-043: <c>DELETE auth/{mount}/role/{roleName}</c> — deletes the role, including its bound machines and outstanding secret ids.</summary>
    /// <remarks>Wire params: <c>mount</c>, <c>roleName</c> build the route; no body. Returns nothing; an already-absent role is not an error. Conformance: Standard (AUT-043). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.AppId.Admin.DeleteRole — AUT-043</spec>
    public async Task DeleteRoleAsync(string roleName, string mount = "approle", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        _ = await endpoint.DeleteAsync(RolePath(mount, roleName), options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>AUT-043: <c>POST auth/{mount}/role/{roleName}/role-id</c> — the write half of <see cref="AppIdOperations.ReadRoleIdAsync"/>, setting a caller-chosen role id.</summary>
    /// <remarks>Wire params: body <c>{"role_id": roleId}</c>. A role id is a public identifier paired with a secret id at login, not a secret, so it is a plain string. Returns the write response, or <see langword="null"/> on an empty body. Conformance: Complete (AUT-043). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.AppId.Admin.WriteRoleId — AUT-043</spec>
    public Task<Response?> WriteRoleIdAsync(string roleName, string roleId, string mount = "approle", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleId);
        return endpoint.WriteAsync(
            $"{RolePath(mount, roleName)}/role-id", AuthEndpoint.JsonObject(("role_id", roleId)), options, cancellationToken);
    }

    /// <summary>AUT-043: <c>GET auth/{mount}/role/{roleName}/{field}</c> — reads one role field in isolation.</summary>
    /// <remarks>Wire params: <paramref name="field"/> selects the path segment via <see cref="AppIdRoleField"/>; no body. Returns <see langword="null"/> on a <c>404</c> with an empty body. Conformance: Complete (AUT-043). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.AppId.Admin.ReadField — AUT-043</spec>
    public Task<Response?> ReadFieldAsync(string roleName, AppIdRoleField field, string mount = "approle", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.ReadAsync(FieldPath(mount, roleName, field), options, cancellationToken);
    }

    /// <summary>AUT-043: <c>POST auth/{mount}/role/{roleName}/{field}</c> — overwrites one role field in isolation.</summary>
    /// <remarks>Wire params: <paramref name="value"/> sent verbatim as the body; <paramref name="field"/> selects the path segment. Returns the write response, or <see langword="null"/> on an empty body. Conformance: Complete (AUT-043). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.AppId.Admin.WriteField — AUT-043</spec>
    public Task<Response?> WriteFieldAsync(string roleName, AppIdRoleField field, JsonElement value, string mount = "approle", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.WriteAsync(FieldPath(mount, roleName, field), AuthEndpoint.Payload(value), options, cancellationToken);
    }

    /// <summary>AUT-043: <c>DELETE auth/{mount}/role/{roleName}/{field}</c>, resetting it to the role's default.</summary>
    /// <remarks>Wire params: <paramref name="field"/> selects the path segment; no body. Returns nothing. Conformance: Complete (AUT-043). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.AppId.Admin.DeleteField — AUT-043</spec>
    public async Task DeleteFieldAsync(string roleName, AppIdRoleField field, string mount = "approle", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        _ = await endpoint.DeleteAsync(FieldPath(mount, roleName, field), options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>AUT-043: <c>GET auth/{mount}/role/{roleName}/local-secret-ids</c>.</summary>
    /// <remarks>Wire params: <c>mount</c>, <c>roleName</c> build the route; no body. Returns <see langword="null"/> on a <c>404</c> with an empty body. Conformance: Complete (AUT-043). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.AppId.Admin.ReadLocalSecretIds — AUT-043</spec>
    public Task<Response?> ReadLocalSecretIdsAsync(string roleName, string mount = "approle", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.ReadAsync($"{RolePath(mount, roleName)}/local-secret-ids", options, cancellationToken);
    }

    /// <summary>AUT-043: <c>LIST auth/{mount}/role/{roleName}/secret-id/</c> — the accessors, never the secret ids.</summary>
    /// <remarks>Wire params: <c>mount</c>, <c>roleName</c> build the route; no body. Returns an empty list when there are none (TRN-050), never <see langword="null"/>. The list carries only accessors — a secret id itself is issued once and cannot be re-listed or re-read. Conformance: Standard (AUT-043). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.AppId.Admin.ListSecretIdAccessors — AUT-043</spec>
    public Task<IReadOnlyList<string>> ListSecretIdAccessorsAsync(string roleName, string mount = "approle", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.ListKeysAsync($"{RolePath(mount, roleName)}/secret-id/", options, cancellationToken);
    }

    /// <summary>AUT-043: <c>POST auth/{mount}/role/{roleName}/secret-id/lookup</c> — presents a secret id to read back its metadata.</summary>
    /// <remarks>Wire params: body <c>{"secret_id": secretId}</c>, taken and held as <see cref="SecretString"/> (CNF-031), even though it travels in a request body here rather than a header. Appendix A gives no response field set (D-M6-5); this SDK does not assert whether the secret id itself is echoed back in the response. Returns <see langword="null"/> on a <c>404</c> with an empty body. Conformance: Standard (AUT-043). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.AppId.Admin.LookupSecretId — AUT-043</spec>
    public Task<Response?> LookupSecretIdAsync(string roleName, SecretString secretId, string mount = "approle", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secretId);
        return endpoint.WriteAsync(
            $"{RolePath(mount, roleName)}/secret-id/lookup", AuthEndpoint.JsonObject(("secret_id", secretId.Reveal())), options, cancellationToken);
    }

    /// <summary>AUT-043: <c>POST auth/{mount}/role/{roleName}/secret-id/destroy</c> — revokes the secret id, permanently.</summary>
    /// <remarks>Wire params: body <c>{"secret_id": secretId}</c>, taken as <see cref="SecretString"/> (CNF-031). Returns nothing; the destroyed secret id can no longer authenticate. Conformance: Standard (AUT-043). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.AppId.Admin.DestroySecretId — AUT-043</spec>
    public async Task DestroySecretIdAsync(string roleName, SecretString secretId, string mount = "approle", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secretId);
        _ = await endpoint.WriteAsync(
            $"{RolePath(mount, roleName)}/secret-id/destroy",
            AuthEndpoint.JsonObject(("secret_id", secretId.Reveal())),
            options,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>AUT-043: <c>POST auth/{mount}/role/{roleName}/secret-id-accessor/lookup</c> — the accessor-keyed sibling of <see cref="LookupSecretIdAsync"/>, so a caller who never held the secret id can still read its metadata.</summary>
    /// <remarks>Wire params: body <c>{"secret_id_accessor": accessor}</c>. <paramref name="accessor"/> identifies a secret id without being usable as one, so it is a plain string, not <see cref="SecretString"/>. Appendix A gives no response field set (D-M6-5); the accessor cannot be used to recover the secret id itself. Returns <see langword="null"/> on a <c>404</c> with an empty body. Conformance: Standard (AUT-043). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.AppId.Admin.LookupSecretIdAccessor — AUT-043</spec>
    public Task<Response?> LookupSecretIdAccessorAsync(string roleName, string accessor, string mount = "approle", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessor);
        return endpoint.WriteAsync(
            $"{RolePath(mount, roleName)}/secret-id-accessor/lookup",
            AuthEndpoint.JsonObject(("secret_id_accessor", accessor)),
            options,
            cancellationToken);
    }

    /// <summary>AUT-043: <c>POST auth/{mount}/role/{roleName}/secret-id-accessor/destroy</c> — the accessor-keyed sibling of <see cref="DestroySecretIdAsync"/>.</summary>
    /// <remarks>Wire params: body <c>{"secret_id_accessor": accessor}</c>, plain string. Returns nothing; the destroyed secret id can no longer authenticate. Conformance: Standard (AUT-043). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.AppId.Admin.DestroySecretIdAccessor — AUT-043</spec>
    public async Task DestroySecretIdAccessorAsync(string roleName, string accessor, string mount = "approle", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessor);
        _ = await endpoint.WriteAsync(
            $"{RolePath(mount, roleName)}/secret-id-accessor/destroy",
            AuthEndpoint.JsonObject(("secret_id_accessor", accessor)),
            options,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>AUT-043: <c>POST auth/{mount}/role/{roleName}/custom-secret-id</c> — registers a caller-chosen secret id instead of generating one.</summary>
    /// <remarks>Wire params: body <c>{"secret_id": secretId}</c>, taken as <see cref="SecretString"/> (CNF-031). Writes credential material the caller already holds; the response, if any, does not need to and is not asserted to echo it back (Appendix A gives no field set, D-M6-5). Returns the write response, or <see langword="null"/> on an empty body. Conformance: Complete (AUT-043). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.AppId.Admin.CustomSecretId — AUT-043</spec>
    public Task<Response?> CustomSecretIdAsync(string roleName, SecretString secretId, string mount = "approle", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secretId);
        return endpoint.WriteAsync(
            $"{RolePath(mount, roleName)}/custom-secret-id",
            AuthEndpoint.JsonObject(("secret_id", secretId.Reveal())),
            options,
            cancellationToken);
    }

    /// <summary>AUT-043: <c>LIST auth/{mount}/role/{roleName}/machine/</c> — the machines bound to this role (AUT-040's gate).</summary>
    /// <remarks>Wire params: <c>mount</c>, <c>roleName</c> build the route; no body. Returns an empty list when there are none (TRN-050), never <see langword="null"/>. Conformance: Complete (AUT-043). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.AppId.Admin.ListMachines — AUT-043</spec>
    public Task<IReadOnlyList<string>> ListMachinesAsync(string roleName, string mount = "approle", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.ListKeysAsync($"{RolePath(mount, roleName)}/machine/", options, cancellationToken);
    }

    /// <summary>AUT-043: <c>POST auth/{mount}/role/{roleName}/machine/</c> — binds a machine to the role (AUT-040's gate).</summary>
    /// <remarks>Wire params: <paramref name="machine"/> sent verbatim as the body (Appendix A gives no field set, D-M6-5). Returns the write response, or <see langword="null"/> on an empty body. Conformance: Complete (AUT-043). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.AppId.Admin.BindMachine — AUT-043</spec>
    public Task<Response?> BindMachineAsync(string roleName, JsonElement machine, string mount = "approle", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.WriteAsync($"{RolePath(mount, roleName)}/machine/", AuthEndpoint.Payload(machine), options, cancellationToken);
    }

    /// <summary>AUT-043: <c>GET auth/{mount}/role/{roleName}/machine/{machineId}</c>.</summary>
    /// <remarks>Wire params: <c>mount</c>, <c>roleName</c>, <c>machineId</c> build the route; no body. Returns <see langword="null"/> on a <c>404</c> with an empty body. Conformance: Complete (AUT-043). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.AppId.Admin.ReadMachine — AUT-043</spec>
    public Task<Response?> ReadMachineAsync(string roleName, string machineId, string mount = "approle", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.ReadAsync(MachinePath(mount, roleName, machineId), options, cancellationToken);
    }

    /// <summary>AUT-043: <c>DELETE auth/{mount}/role/{roleName}/machine/{machineId}</c> — unbinds the machine; it can no longer log in under AUT-040's gate unless another bound machine matches.</summary>
    /// <remarks>Wire params: <c>mount</c>, <c>roleName</c>, <c>machineId</c> build the route; no body. Returns nothing; an already-unbound machine is not an error. Conformance: Complete (AUT-043). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.AppId.Admin.UnbindMachine — AUT-043</spec>
    public async Task UnbindMachineAsync(string roleName, string machineId, string mount = "approle", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        _ = await endpoint.DeleteAsync(MachinePath(mount, roleName, machineId), options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>AUT-043: <c>GET auth/{mount}/config</c>, which carries <c>require_machine</c> — the mount-wide half of AUT-040's machine-identity gate, default <b>on</b>.</summary>
    /// <remarks>Wire params: <c>mount</c> builds the route; no body. Returns <see langword="null"/> on a <c>404</c> with an empty body. Conformance: Standard (AUT-043). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.AppId.Admin.ReadConfig — AUT-043</spec>
    public Task<Response?> ReadConfigAsync(string mount = "approle", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.ReadAsync($"auth/{AuthEndpoint.Mount(mount)}/config", options, cancellationToken);
    }

    /// <summary>AUT-043: <c>POST auth/{mount}/config</c> — sets <c>require_machine</c> and any other mount-wide config field.</summary>
    /// <remarks>Wire params: <paramref name="config"/> sent verbatim as the body (Appendix A gives no field set, D-M6-5). Returns the write response, or <see langword="null"/> on an empty body. Conformance: Standard (AUT-043). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.AppId.Admin.WriteConfig — AUT-043</spec>
    public Task<Response?> WriteConfigAsync(JsonElement config, string mount = "approle", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.WriteAsync($"auth/{AuthEndpoint.Mount(mount)}/config", AuthEndpoint.Payload(config), options, cancellationToken);
    }

    /// <summary>AUT-043: <c>POST auth/{mount}/tidy/secret-id</c> — removes expired secret ids.</summary>
    /// <remarks>Wire params: <c>mount</c> builds the route; no body. Returns the write response, or <see langword="null"/> on an empty body. Conformance: Complete (AUT-043). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Auth.AppId.Admin.TidySecretIds — AUT-043</spec>
    public Task<Response?> TidySecretIdsAsync(string mount = "approle", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return endpoint.WriteAsync($"auth/{AuthEndpoint.Mount(mount)}/tidy/secret-id", null, options, cancellationToken);
    }

    private static string RolePath(string mount, string roleName)
    {
        return $"auth/{AuthEndpoint.Mount(mount)}/role/{AuthEndpoint.Segment(roleName)}";
    }

    private static string MachinePath(string mount, string roleName, string machineId)
    {
        return $"{RolePath(mount, roleName)}/machine/{AuthEndpoint.Segment(machineId)}";
    }

    /// <summary>
    /// The field sub-path. An enum value outside the declared thirteen — reachable only by casting
    /// an integer — is rejected client-side rather than sent as a nonsense path.
    /// </summary>
    private static string FieldPath(string mount, string roleName, AppIdRoleField field)
    {
        int index = (int)field;
        if (index < 0 || index >= FieldSegments.Length)
        {
            throw AuthEndpoint.InvalidArgument(
                "field", $"{index} is not a declared AppIdRoleField; Appendix A enumerates exactly {FieldSegments.Length}.");
        }

        return $"{RolePath(mount, roleName)}/{FieldSegments[index]}";
    }
}
