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
    /// <remarks>Wire params: none. Returns <see cref="LdapConfig"/>, or <see langword="null"/> when no config is set (404 treated as absent). <see cref="LdapConfig.BindPass"/>/<see cref="LdapConfig.ClientTlsKey"/> are write-only and always <see langword="null"/> here — the bind password is never echoed back. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Ldap.ReadConfig — 12-other-engines-and-identity.md</spec>
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
    /// <remarks>Wire params (patch-shaped, omitted members untouched): url, binddn, bindpass, userdn, directory_type, password_policy, request_timeout, starttls, client_tls_cert, client_tls_key, tls_min_version, insecure_tls, acknowledge_insecure_tls, userattr. Returns <see langword="void"/>. <c>bindpass</c>/<c>client_tls_key</c> are write-only bind credential material the server never echoes back on read; callers must not log <see cref="LdapConfig"/> unredacted. Conformance: Level X (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): <c>BV-INPUT-001</c> (LDP-001's insecure_tls/acknowledge_insecure_tls guard, refused before any request is sent).</remarks>
    /// <spec>Ldap.WriteConfig — LDP-001</spec>
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
    /// <remarks>Wire params: none. Returns <see langword="void"/> — removes the stored bind configuration, including whatever bind password the server holds for it. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Ldap.DeleteConfig — 12-other-engines-and-identity.md</spec>
    public async Task DeleteConfigAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "DELETE", $"{Encode(mount)}/config", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>POST {mount}/rotate-root</c>.</summary>
    /// <remarks>Wire params: none. Returns <see langword="void"/> — rotates the root bind credential server-side; the new value is never returned to the caller. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Ldap.RotateRoot — 12-other-engines-and-identity.md</spec>
    public async Task RotateRootAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{Encode(mount)}/rotate-root", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>GET {mount}/check-connection</c>.</summary>
    /// <remarks>Wire params: none. Returns <see cref="LdapCheckConnectionResult"/>, never <see langword="null"/>; only <see cref="LdapCheckConnectionResult.Ok"/> is guaranteed, other members reflect how far the probe got, and the bind password is never echoed. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061) — a probe failure is reported in the result, not raised.</remarks>
    /// <spec>Ldap.CheckConnection — 12-other-engines-and-identity.md</spec>
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
    /// <remarks>Wire params: none beyond <paramref name="name"/>/<paramref name="mount"/>. Returns <see cref="LdapStaticCred"/>, or <see langword="null"/> when <paramref name="name"/> is not found (404 treated as absent). <see cref="LdapStaticCred.Password"/> is a <see cref="SecretString"/>-typed credential the server issues for this static role — treat it as secret material. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Ldap.StaticCred — 12-other-engines-and-identity.md</spec>
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
    /// <remarks>Wire params: none beyond <paramref name="name"/>/<paramref name="mount"/>. Returns <see langword="void"/> — rotates the named static role's credential server-side without returning it; call <see cref="StaticCredAsync"/> to fetch the new value. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Ldap.RotateRole — 12-other-engines-and-identity.md</spec>
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
    /// <remarks>Wire params: none. Returns the static role names, empty (never <see langword="null"/>) when none exist or the path is absent. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Ldap.StaticRoles.List — 12-other-engines-and-identity.md</spec>
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
    /// <remarks>Wire params: none beyond <paramref name="name"/>/<paramref name="mount"/>. Returns <see cref="LdapStaticRole"/>, or <see langword="null"/> when <paramref name="name"/> is not found (404 treated as absent). Carries no password (<c>dn</c>, <c>username</c>, <c>rotation_period</c>, <c>password_policy</c> only). Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Ldap.StaticRoles.Read — 12-other-engines-and-identity.md</spec>
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
    /// <remarks>Wire params (patch-shaped, omitted members untouched): dn, username, rotation_period, password_policy. Returns <see langword="void"/> — no credential travels in either direction; the server manages the rotated password. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Ldap.StaticRoles.Write — 12-other-engines-and-identity.md</spec>
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
    /// <remarks>Wire params: none. Returns <see langword="void"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Ldap.StaticRoles.Delete — 12-other-engines-and-identity.md</spec>
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
    /// <remarks>Wire params: none. Returns the library set names, empty (never <see langword="null"/>) when none exist or the path is absent. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Ldap.Library.List — 12-other-engines-and-identity.md</spec>
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
    /// <remarks>Wire params: none beyond <paramref name="set"/>/<paramref name="mount"/>. Returns <see cref="LdapLibrarySet"/>, or <see langword="null"/> when <paramref name="set"/> is not found (404 treated as absent). Carries service account names and TTLs, no passwords. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Ldap.Library.Read — 12-other-engines-and-identity.md</spec>
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
    /// <remarks>Wire params (patch-shaped, omitted members untouched): service_account_names[], ttl, max_ttl, disable_check_in_enforcement, affinity_ttl. Returns <see langword="void"/> — no credential travels in either direction. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Ldap.Library.Write — 12-other-engines-and-identity.md</spec>
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
    /// <remarks>Wire params: none. Returns <see langword="void"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Ldap.Library.Delete — 12-other-engines-and-identity.md</spec>
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
    /// <remarks>Wire params: ttl (optional). Returns <see cref="LdapLibraryCheckOut"/>, never <see langword="null"/>; <see cref="LdapLibraryCheckOut.Password"/> is a <see cref="SecretString"/>-typed credential checked out to the caller — treat it as secret material and check it back in via <see cref="CheckInAsync"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Ldap.Library.CheckOut — 12-other-engines-and-identity.md</spec>
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
    /// <remarks>Wire params: service_account_name (optional). Returns <see langword="void"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Ldap.Library.CheckIn — 12-other-engines-and-identity.md</spec>
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
    /// <remarks>Wire params: none. Returns <see cref="LdapLibraryStatus"/>, never <see langword="null"/>; <see cref="LdapLibraryStatus.CheckedOut"/> is a raw wire map keyed by account name (12/Appendix A name no field set for its entries) and <see cref="LdapLibraryStatus.Available"/> lists account names not checked out — neither carries a password. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Ldap.Library.Status — 12-other-engines-and-identity.md</spec>
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
