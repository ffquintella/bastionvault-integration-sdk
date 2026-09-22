using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// 12 §LDAP / Active Directory: <c>Ldap.ReadConfig</c>/<c>WriteConfig</c>'s shared field list
/// (<c>{mount}/config</c>). Patch-shaped (OVR-007), like <see cref="PkiRole"/>: a
/// <see langword="null"/> member is omitted on write and means "the server never returned this
/// field" on read. <see cref="BindPass"/> and <see cref="ClientTlsKey"/> are write-only — the
/// server never returns either on a read, so both stay <see langword="null"/> there.
/// </summary>
public sealed class LdapConfig
{
    /// <summary>The wire <c>url</c> field.</summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "The SDK treats this value as opaque text sent to the server, not parsed. System.Uri has no counterpart in the Rust and Python SDKs, so typing it here would make the .NET signature the odd one out for no behavioural gain (CLA-003).")]
    public string? Url { get; init; }

    /// <summary>The wire <c>binddn</c> field.</summary>
    public string? BindDn { get; init; }

    /// <summary>The wire <c>bindpass</c> field. Write-only; never returned on read.</summary>
    public SecretString? BindPass { get; init; }

    /// <summary>The wire <c>userdn</c> field.</summary>
    public string? UserDn { get; init; }

    /// <summary>The wire <c>directory_type</c> field: <c>openldap</c> or <c>active_directory</c>.</summary>
    public string? DirectoryType { get; init; }

    /// <summary>The wire <c>password_policy</c> field.</summary>
    public string? PasswordPolicy { get; init; }

    /// <summary>The wire <c>request_timeout</c> field, in seconds. Server default 10 when omitted.</summary>
    public TimeSpan? RequestTimeout { get; init; }

    /// <summary>The wire <c>starttls</c> field.</summary>
    public bool? StartTls { get; init; }

    /// <summary>The wire <c>client_tls_cert</c> field.</summary>
    public string? ClientTlsCert { get; init; }

    /// <summary>The wire <c>client_tls_key</c> field. Write-only; never returned on read.</summary>
    public SecretString? ClientTlsKey { get; init; }

    /// <summary>The wire <c>tls_min_version</c> field: <c>tls12</c> or <c>tls13</c>.</summary>
    public string? TlsMinVersion { get; init; }

    /// <summary>
    /// The wire <c>insecure_tls</c> field. LDP-001: when <see langword="true"/>,
    /// <see cref="AcknowledgeInsecureTls"/> must also be <see langword="true"/>, or
    /// <see cref="LdapOperations.WriteConfigAsync"/> refuses the write client-side
    /// (<c>BV-INPUT-001</c>) before any request is sent, mirroring the server's own check.
    /// </summary>
    public bool? InsecureTls { get; init; }

    /// <summary>The wire <c>acknowledge_insecure_tls</c> field. See <see cref="InsecureTls"/> (LDP-001).</summary>
    public bool? AcknowledgeInsecureTls { get; init; }

    /// <summary>The wire <c>userattr</c> field. Server default <c>cn</c> when omitted.</summary>
    public string? UserAttr { get; init; }
}

/// <summary>
/// 12 §LDAP: <c>Ldap.CheckConnection</c>'s result (<c>GET {mount}/check-connection</c>). Only
/// <see cref="Ok"/> is guaranteed; every other member is present only as far as the probe got
/// (e.g. a <c>bind</c>-stage failure reports <see cref="Stage"/>/<see cref="Error"/> but never
/// <see cref="Host"/>/<see cref="Port"/>/<see cref="LatencyMs"/>) — a deliberate relaxation of
/// <see cref="LdapStaticCred"/>'s stricter convention, not an oversight.
/// </summary>
public sealed class LdapCheckConnectionResult
{
    /// <summary>The wire <c>ok</c> field: whether the probe succeeded.</summary>
    public required bool Ok { get; init; }

    /// <summary>The wire <c>stage</c> field: which step the probe reached.</summary>
    public string? Stage { get; init; }

    /// <summary>The wire <c>error</c> field, present only when <see cref="Ok"/> is <see langword="false"/>.</summary>
    public string? Error { get; init; }

    /// <summary>The wire <c>url</c> field: the configured LDAP URL the probe used.</summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "The SDK treats this value as opaque text echoed from the server, not parsed. System.Uri has no counterpart in the Rust and Python SDKs, so typing it here would make the .NET signature the odd one out for no behavioural gain (CLA-003).")]
    public string? Url { get; init; }

    /// <summary>The wire <c>bind_dn</c> field.</summary>
    public string? BindDn { get; init; }

    /// <summary>The wire <c>host</c> field.</summary>
    public string? Host { get; init; }

    /// <summary>The wire <c>port</c> field.</summary>
    public int? Port { get; init; }

    /// <summary>The wire <c>scheme</c> field.</summary>
    public string? Scheme { get; init; }

    /// <summary>The wire <c>latency_ms</c> field.</summary>
    public long? LatencyMs { get; init; }
}

/// <summary>
/// 12 §LDAP: <c>Ldap.StaticRoles.Read</c>/<c>Write</c>'s shared field list
/// (<c>{mount}/static-role/{name}</c>). Patch-shaped (OVR-007), like <see cref="LdapConfig"/>.
/// </summary>
public sealed class LdapStaticRole
{
    /// <summary>The wire <c>dn</c> field.</summary>
    public string? Dn { get; init; }

    /// <summary>The wire <c>username</c> field.</summary>
    public string? Username { get; init; }

    /// <summary>The wire <c>rotation_period</c> field, in seconds.</summary>
    public TimeSpan? RotationPeriod { get; init; }

    /// <summary>The wire <c>password_policy</c> field.</summary>
    public string? PasswordPolicy { get; init; }
}

/// <summary>
/// 12 §LDAP: <c>Ldap.StaticCred</c>'s result (<c>GET {mount}/static-cred/{name}</c>). This is a
/// credential-issuance response, so <see cref="Password"/> is <see cref="SecretString"/>-typed
/// (the same treatment every other returned password or key gets in this SDK).
/// </summary>
public sealed class LdapStaticCred
{
    /// <summary>The wire <c>username</c> field.</summary>
    public required string Username { get; init; }

    /// <summary>The wire <c>dn</c> field.</summary>
    public required string Dn { get; init; }

    /// <summary>The wire <c>password</c> field, redacted.</summary>
    public required SecretString Password { get; init; }

    /// <summary>The wire <c>last_rotated</c> field, when the role has rotated at least once.</summary>
    public DateTimeOffset? LastRotated { get; init; }

    /// <summary>The wire <c>ttl_secs</c> field.</summary>
    public long? TtlSecs { get; init; }
}

/// <summary>
/// 12 §LDAP: <c>Ldap.Library.Read</c>/<c>Write</c>'s shared field list
/// (<c>{mount}/library/{set}</c>). Patch-shaped (OVR-007), like <see cref="LdapConfig"/>.
/// </summary>
public sealed class LdapLibrarySet
{
    /// <summary>The wire <c>service_account_names</c> array.</summary>
    public IReadOnlyList<string>? ServiceAccountNames { get; init; }

    /// <summary>The wire <c>ttl</c> field, in seconds. Server default 3600 when omitted.</summary>
    public TimeSpan? Ttl { get; init; }

    /// <summary>The wire <c>max_ttl</c> field, in seconds. Server default 86400 when omitted.</summary>
    public TimeSpan? MaxTtl { get; init; }

    /// <summary>The wire <c>disable_check_in_enforcement</c> field.</summary>
    public bool? DisableCheckInEnforcement { get; init; }

    /// <summary>The wire <c>affinity_ttl</c> field, in seconds.</summary>
    public TimeSpan? AffinityTtl { get; init; }
}

/// <summary>
/// 12 §LDAP: <c>Ldap.Library.CheckOut</c>'s result (<c>POST {mount}/library/{set}/check-out</c>).
/// A credential-issuance response, so <see cref="Password"/> is <see cref="SecretString"/>-typed.
/// </summary>
public sealed class LdapLibraryCheckOut
{
    /// <summary>The wire <c>service_account_name</c> field: the account checked out.</summary>
    public required string ServiceAccountName { get; init; }

    /// <summary>The wire <c>password</c> field, redacted.</summary>
    public required SecretString Password { get; init; }

    /// <summary>The wire <c>lease_id</c> field.</summary>
    public required string LeaseId { get; init; }

    /// <summary>The wire <c>ttl_secs</c> field.</summary>
    public required long TtlSecs { get; init; }
}

/// <summary>
/// 12 §LDAP: <c>Ldap.Library.Status</c>'s result (<c>GET {mount}/library/{set}/status</c>).
/// Neither section 12 nor Appendix A names a field set for <see cref="CheckedOut"/>'s entries,
/// so it stays a raw wire map rather than a guessed type (D-M1c-25).
/// </summary>
public sealed class LdapLibraryStatus
{
    /// <summary>The wire <c>checked_out</c> object, keyed by service account name.</summary>
    public required IReadOnlyDictionary<string, JsonElement> CheckedOut { get; init; }

    /// <summary>The wire <c>available</c> array: service account names not currently checked out.</summary>
    public required IReadOnlyList<string> Available { get; init; }
}
