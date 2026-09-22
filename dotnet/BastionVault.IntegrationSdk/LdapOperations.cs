using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// 12 §LDAP / Active Directory (OVR-008), reached from <see cref="BastionVaultClient.Ldap"/>.
/// <c>mount</c> defaults to <c>"openldap"</c>. LDP-001's insecure-TLS acknowledgement is enforced
/// client-side in <see cref="WriteConfigAsync"/>, mirroring the server's own check.
/// </summary>
public sealed class LdapOperations
{
    private const string DefaultMount = "openldap";

    private readonly LogicalOperations logical;

    internal LdapOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
        StaticRoles = new LdapStaticRoleOperations(logical);
        Library = new LdapLibraryOperations(logical);
    }

    /// <summary>12 §LDAP: <c>{mount}/static-role/*</c>.</summary>
    public LdapStaticRoleOperations StaticRoles { get; }

    /// <summary>12 §LDAP: <c>{mount}/library/*</c>.</summary>
    public LdapLibraryOperations Library { get; }

    /// <summary><c>GET {mount}/config</c>.</summary>
    public async Task<LdapConfig?> ReadConfigAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{Encode(mount)}/config", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data is { } data ? LdapWire.ReadConfig(data) : null;
    }

    /// <summary><c>POST {mount}/config</c>. LDP-001: refused client-side (<c>BV-INPUT-001</c>) when <c>InsecureTls</c> is set without <c>AcknowledgeInsecureTls</c>.</summary>
    public async Task WriteConfigAsync(
        LdapConfig config, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{Encode(mount)}/config";
        LdapWire.RequireAcknowledgedInsecureTls(config, path);
        _ = await logical.ExecuteShapedAsync(
            "POST", path, LdapWire.SerialiseConfig(config), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>DELETE {mount}/config</c>.</summary>
    public async Task DeleteConfigAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "DELETE", $"{Encode(mount)}/config", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>POST {mount}/rotate-root</c>.</summary>
    public async Task RotateRootAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{Encode(mount)}/rotate-root", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>GET {mount}/check-connection</c>.</summary>
    public async Task<LdapCheckConnectionResult> CheckConnectionAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{Encode(mount)}/check-connection";
        Response? response = await logical.ExecuteShapedAsync(
            "GET", path, null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return LdapWire.ReadCheckConnectionResult(response?.Data ?? throw KvWire.EnvelopeMismatch(path, "ok"));
    }

    /// <summary><c>GET {mount}/static-cred/{name}</c>.</summary>
    public async Task<LdapStaticCred?> StaticCredAsync(
        string name, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{Encode(mount)}/static-cred/{UrlBuilder.EncodePathSegment(name)}";
        Response? response = await logical.ExecuteShapedAsync(
            "GET", path, null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data is { } data ? LdapWire.ReadStaticCred(data, path) : null;
    }

    /// <summary><c>POST {mount}/rotate-role/{name}</c>.</summary>
    public async Task RotateRoleAsync(
        string name, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{Encode(mount)}/rotate-role/{UrlBuilder.EncodePathSegment(name)}", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    private static string Encode(string mount)
    {
        return UrlBuilder.EncodePathFragment(mount.Trim('/'));
    }
}

/// <summary>12 §LDAP: <c>{mount}/static-role/*</c>, reached from <see cref="LdapOperations.StaticRoles"/>.</summary>
public sealed class LdapStaticRoleOperations
{
    private const string DefaultMount = "openldap";

    private readonly LogicalOperations logical;

    internal LdapStaticRoleOperations(LogicalOperations logical)
    {
        this.logical = logical;
    }

    /// <summary><c>LIST {mount}/static-role/</c>.</summary>
    public async Task<IReadOnlyList<string>> ListAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "LIST", $"{Encode(mount)}/static-role/", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return KvWire.ReadKeys(response);
    }

    /// <summary><c>GET {mount}/static-role/{name}</c>.</summary>
    public async Task<LdapStaticRole?> ReadAsync(
        string name, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", RolePath(mount, name), null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data is { } data ? LdapWire.ReadStaticRole(data) : null;
    }

    /// <summary><c>POST {mount}/static-role/{name}</c>.</summary>
    public async Task WriteAsync(
        string name, LdapStaticRole role, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(role);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", RolePath(mount, name), LdapWire.SerialiseStaticRole(role), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>DELETE {mount}/static-role/{name}</c>.</summary>
    public async Task DeleteAsync(
        string name, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "DELETE", RolePath(mount, name), null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    private static string RolePath(string mount, string name)
    {
        return $"{Encode(mount)}/static-role/{UrlBuilder.EncodePathSegment(name)}";
    }

    private static string Encode(string mount)
    {
        return UrlBuilder.EncodePathFragment(mount.Trim('/'));
    }
}

/// <summary>12 §LDAP: <c>{mount}/library/*</c>, reached from <see cref="LdapOperations.Library"/>.</summary>
public sealed class LdapLibraryOperations
{
    private const string DefaultMount = "openldap";

    private readonly LogicalOperations logical;

    internal LdapLibraryOperations(LogicalOperations logical)
    {
        this.logical = logical;
    }

    /// <summary><c>LIST {mount}/library/</c>.</summary>
    public async Task<IReadOnlyList<string>> ListAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "LIST", $"{Encode(mount)}/library/", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return KvWire.ReadKeys(response);
    }

    /// <summary><c>GET {mount}/library/{set}</c>.</summary>
    public async Task<LdapLibrarySet?> ReadAsync(
        string set, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(set);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", SetPath(mount, set), null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data is { } data ? LdapWire.ReadLibrarySet(data) : null;
    }

    /// <summary><c>POST {mount}/library/{set}</c>.</summary>
    public async Task WriteAsync(
        string set, LdapLibrarySet spec, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(set);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", SetPath(mount, set), LdapWire.SerialiseLibrarySet(spec), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>DELETE {mount}/library/{set}</c>.</summary>
    public async Task DeleteAsync(
        string set, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(set);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "DELETE", SetPath(mount, set), null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>POST {mount}/library/{set}/check-out</c>.</summary>
    public async Task<LdapLibraryCheckOut> CheckOutAsync(
        string set, TimeSpan? ttl = null, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(set);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{SetPath(mount, set)}/check-out";
        ReadOnlyMemory<byte>? body = ttl is { } duration
            ? KvWire.Serialise(writer => writer.WriteNumber("ttl", (long)duration.TotalSeconds))
            : null;
        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return LdapWire.ReadLibraryCheckOut(response?.Data ?? throw KvWire.EnvelopeMismatch(path, "service_account_name"), path);
    }

    /// <summary><c>POST {mount}/library/{set}/check-in</c>.</summary>
    public async Task CheckInAsync(
        string set, string? account = null, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(set);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ReadOnlyMemory<byte>? body = account is null
            ? null
            : KvWire.Serialise(writer => writer.WriteString("service_account_name", account));
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{SetPath(mount, set)}/check-in", body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>GET {mount}/library/{set}/status</c>.</summary>
    public async Task<LdapLibraryStatus> StatusAsync(
        string set, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(set);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{SetPath(mount, set)}/status";
        Response? response = await logical.ExecuteShapedAsync(
            "GET", path, null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return LdapWire.ReadLibraryStatus(response?.Data ?? throw KvWire.EnvelopeMismatch(path, "available"));
    }

    private static string SetPath(string mount, string set)
    {
        return $"{Encode(mount)}/library/{UrlBuilder.EncodePathSegment(set)}";
    }

    private static string Encode(string mount)
    {
        return UrlBuilder.EncodePathFragment(mount.Trim('/'));
    }
}
