using System.Text.Json;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// One entry of the <c>sys/mounts</c> or <c>sys/auth</c> table (SYS-020). ⚠️ The server returns
/// exactly two fields per entry; there is no <c>config</c>, <c>uuid</c>, <c>options</c> or
/// <c>accessor</c> on this shape, and inventing one would be a field the wire never fills.
/// <see cref="MountDetail"/> is the richer shape, and it comes from a different endpoint.
/// </summary>
public sealed class MountInfo
{
    /// <summary>The engine type, e.g. <c>kv-v2</c>. One of <see cref="MountTypes"/> on a current server (SYS-021).</summary>
    public required string Type { get; init; }

    /// <summary>The mount's description. The empty string when the server sent one; <see langword="null"/> when it sent no field at all.</summary>
    public string? Description { get; init; }
}

/// <summary>
/// One entry of the ACL-filtered <c>sys/internal/ui/mounts</c> table, which carries four fields
/// rather than <see cref="MountInfo"/>'s two.
/// </summary>
public sealed class MountDetail
{
    /// <summary>The engine type, e.g. <c>kv-v2</c>.</summary>
    public required string Type { get; init; }

    /// <summary>The mount's description, or <see langword="null"/> when the server omitted it.</summary>
    public string? Description { get; init; }

    /// <summary>The mount's UUID, or <see langword="null"/> when the server omitted it.</summary>
    public string? Uuid { get; init; }

    /// <summary>The mount's <c>options</c> map, or <see langword="null"/> when the server omitted it. Values stay raw: the wire does not fix their types.</summary>
    public IReadOnlyDictionary<string, JsonElement>? Options { get; init; }
}

/// <summary>
/// <c>Sys.ListMountsDetailed</c>'s result: the ACL-filtered mount table, split into its two halves
/// exactly as <c>sys/internal/ui/mounts</c> returns them. Auth paths are relative here too, per
/// SYS-030.
/// </summary>
public sealed class MountTable
{
    /// <summary>The secrets-engine mounts, keyed by path with a trailing <c>/</c> (SYS-022).</summary>
    public required IReadOnlyDictionary<string, MountDetail> Secret { get; init; }

    /// <summary>The auth-method mounts, keyed by path with a trailing <c>/</c> and no <c>auth/</c> prefix (SYS-022, SYS-030).</summary>
    public required IReadOnlyDictionary<string, MountDetail> Auth { get; init; }
}

/// <summary>
/// The body of <c>Sys.Mount</c> and <c>Sys.EnableAuthMethod</c>.
/// </summary>
/// <remarks>
/// ⚠️ SYS-020: there is deliberately <b>no</b> <c>Config</c> member. <c>POST sys/mounts/{path}</c>
/// ignores <c>default_lease_ttl</c> and <c>max_lease_ttl</c> outright, so a <c>Config</c> field
/// would be a setting the SDK accepted and the server discarded — the failure mode is silent, and
/// the caller would have no way to tell. KV v2 tuning goes through the engine's own <c>config</c>
/// path (<c>Kv.V2.WriteConfig</c>, 07 — KV engine).
/// </remarks>
public sealed class MountRequest
{
    /// <summary>The engine or auth-method type. Required by the server; one of <see cref="MountTypes"/> on a current server (SYS-021).</summary>
    public required string Type { get; init; }

    /// <summary>An optional human-readable description.</summary>
    public string? Description { get; init; }

    /// <summary>The engine's own options map, sent verbatim when non-empty and omitted when <see langword="null"/>.</summary>
    public IReadOnlyDictionary<string, string>? Options { get; init; }
}

/// <summary>
/// SYS-021's mount-type strings, as constants rather than an enum: the server's type set grows
/// without a protocol version bump, and an enum would make a type the SDK has not heard of
/// unrepresentable. <see cref="MountRequest.Type"/> therefore takes a <see cref="string"/>, and
/// these are the spellings a current server accepts.
/// </summary>
public static class MountTypes
{
    /// <summary>KV v1: flat, optional lease (07 — KV engine).</summary>
    public const string Kv = "kv";

    /// <summary>KV v2: versioned, soft delete, CAS, environments. New <c>secret/</c> mounts are this.</summary>
    public const string KvV2 = "kv-v2";

    /// <summary>The transit encryption-as-a-service engine.</summary>
    public const string Transit = "transit";

    /// <summary>The PKI certificate engine.</summary>
    public const string Pki = "pki";

    /// <summary>The SSH secrets engine.</summary>
    public const string Ssh = "ssh";

    /// <summary>The SSH broker engine.</summary>
    public const string SshBroker = "ssh-broker";

    /// <summary>The TOTP engine.</summary>
    public const string Totp = "totp";

    /// <summary>The OpenLDAP engine.</summary>
    public const string OpenLdap = "openldap";

    /// <summary>The files engine.</summary>
    public const string Files = "files";

    /// <summary>The resource engine.</summary>
    public const string Resource = "resource";

    /// <summary>The rustion engine.</summary>
    public const string Rustion = "rustion";

    /// <summary>The notifications engine.</summary>
    public const string Notifications = "notifications";

    /// <summary>The certificate-lifecycle engine.</summary>
    public const string CertLifecycle = "cert-lifecycle";

    /// <summary>Every secrets-engine type SYS-021 names, in specification order.</summary>
    public static IReadOnlyList<string> Secret { get; } =
    [
        Kv, KvV2, Transit, Pki, Ssh, SshBroker, Totp, OpenLdap, Files, Resource, Rustion,
        Notifications, CertLifecycle,
    ];

    /// <summary>Every auth-method type SYS-021 names, in specification order, including <see cref="AuthTypes.Cert"/>.</summary>
    public static IReadOnlyList<string> Auth { get; } =
    [
        AuthTypes.Userpass, AuthTypes.AppRole, AuthTypes.Ferrogate, AuthTypes.Fido2,
        AuthTypes.Oidc, AuthTypes.Saml, AuthTypes.Cert,
    ];
}

/// <summary>SYS-021's auth-method type strings, kept separate from <see cref="MountTypes"/> because the two tables are separate on the wire.</summary>
public static class AuthTypes
{
    /// <summary>Username and password (05 — authentication).</summary>
    public const string Userpass = "userpass";

    /// <summary>AppRole / AppId machine authentication.</summary>
    public const string AppRole = "approle";

    /// <summary>The ferrogate method.</summary>
    public const string Ferrogate = "ferrogate";

    /// <summary>WebAuthn / FIDO2.</summary>
    public const string Fido2 = "fido2";

    /// <summary>OIDC.</summary>
    public const string Oidc = "oidc";

    /// <summary>SAML.</summary>
    public const string Saml = "saml";

    /// <summary>⚠️ SYS-021: TLS client certificates. Disabled on current servers — enabling it answers <c>BV-SERVER-004</c>.</summary>
    public const string Cert = "cert";
}
