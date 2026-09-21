using System.Text.Json;

namespace BastionVault.IntegrationSdk;

// ============================================================================ M9 slice d: SSH engine

/// <summary>
/// 10 §CA configuration: <c>Ssh.ConfigureCa</c>/<c>Ssh.ReadCa</c>'s shared result shape
/// (<c>10-ssh-engine.md:12-13</c>). D-M9-1: this SDK performs no cryptography, so
/// <see cref="PublicKey"/> is the server's authorized-keys-form bytes, unmodified.
/// </summary>
public sealed class SshCaKey
{
    /// <summary>The wire <c>public_key</c> field, authorized-keys form, verbatim.</summary>
    public required string PublicKey { get; init; }

    /// <summary>The wire <c>algorithm</c> field. PQC-gated: <c>"" | "ed25519" | "mldsa65"</c>.</summary>
    public string? Algorithm { get; init; }
}

/// <summary>
/// 10 §Roles: <c>Ssh.WriteRole</c>'s request body and <c>Ssh.ReadRole</c>'s result — one shared
/// field list, exactly as <c>10-ssh-engine.md:25-29</c> names it, following <see cref="PkiRole"/>'s
/// patch-shaped convention (OVR-007): every member is nullable and a <see langword="null"/> member
/// is omitted from the request body rather than sent empty.
/// </summary>
/// <remarks>
/// D-M9-29: <see cref="AllowedUsers"/>, <see cref="CidrList"/>, <see cref="ExcludeCidrList"/>,
/// <see cref="AllowedExtensions"/> and <see cref="AllowedCriticalOptions"/> are always
/// <b>written</b> comma-separated, the PKI-010 pattern <c>10-ssh-engine.md:38</c> states explicitly
/// for <c>Ssh.Sign</c>'s <c>valid_principals</c>. But that note exists precisely because CSV is not
/// this section's default, and nothing states that a role's own responses echo the request
/// encoding back — so on <b>read</b>, each of these five accepts either a CSV string or a JSON
/// array (<c>Internal.SshWire.ReadCsvOrArray</c>), never assuming the wire always matches what
/// this SDK writes: a role that allows three users must not silently read back as
/// <see langword="null"/> because the server chose the other wire form. <see cref="DefaultExtensions"/>
/// and <see cref="DefaultCriticalOptions"/> are explicitly marked <c>(map)</c> in the specification
/// and are bound as raw wire maps (<c>KvWire.WriteMap</c>/<c>SysWire.AsMap</c>'s convention) rather
/// than a guessed value type, since 10 states no value shape for either.
/// </remarks>
public sealed class SshRole
{
    /// <summary>The wire <c>key_type</c> field: <c>"ca"</c> or <c>"otp"</c>.</summary>
    public string? KeyType { get; init; }

    /// <summary>The wire <c>algorithm_signer</c> field, e.g. <c>"ssh-ed25519"</c>.</summary>
    public string? AlgorithmSigner { get; init; }

    /// <summary>The wire <c>cert_type</c> field: <c>"user"</c> or <c>"host"</c>.</summary>
    public string? CertType { get; init; }

    /// <summary>The wire's CSV-typed <c>allowed_users</c>.</summary>
    public IReadOnlyList<string>? AllowedUsers { get; init; }

    /// <summary>The wire <c>default_user</c> field.</summary>
    public string? DefaultUser { get; init; }

    /// <summary>The wire's CSV-typed <c>allowed_extensions</c>.</summary>
    public IReadOnlyList<string>? AllowedExtensions { get; init; }

    /// <summary>The wire's map-typed <c>default_extensions</c>.</summary>
    public IReadOnlyDictionary<string, JsonElement>? DefaultExtensions { get; init; }

    /// <summary>The wire's CSV-typed <c>allowed_critical_options</c>.</summary>
    public IReadOnlyList<string>? AllowedCriticalOptions { get; init; }

    /// <summary>The wire's map-typed <c>default_critical_options</c>.</summary>
    public IReadOnlyDictionary<string, JsonElement>? DefaultCriticalOptions { get; init; }

    /// <summary>The wire <c>ttl</c> field, in seconds (TRN-031: unquoted, integer seconds).</summary>
    public TimeSpan? Ttl { get; init; }

    /// <summary>The wire <c>max_ttl</c> field, in seconds.</summary>
    public TimeSpan? MaxTtl { get; init; }

    /// <summary>The wire <c>not_before_duration</c> field, in seconds.</summary>
    public TimeSpan? NotBeforeDuration { get; init; }

    /// <summary>The wire <c>key_id_format</c> template string.</summary>
    public string? KeyIdFormat { get; init; }

    /// <summary>The wire's CSV-typed <c>cidr_list</c>.</summary>
    public IReadOnlyList<string>? CidrList { get; init; }

    /// <summary>The wire's CSV-typed <c>exclude_cidr_list</c>.</summary>
    public IReadOnlyList<string>? ExcludeCidrList { get; init; }

    /// <summary>The wire <c>port</c> field. Server default 22 when omitted.</summary>
    public int? Port { get; init; }

    /// <summary>The wire <c>pqc_only</c> field. Server default <see langword="false"/> when omitted.</summary>
    public bool? PqcOnly { get; init; }
}

/// <summary>
/// 10 §Signing (CA mode): <c>Ssh.Sign</c>'s request body (<c>10-ssh-engine.md:34</c>).
/// </summary>
public sealed class SshSignRequest
{
    /// <summary>SSH-001: the wire <c>public_key</c> field. Required; refused client-side when empty or whitespace-only.</summary>
    public required string PublicKey { get; init; }

    /// <summary>The wire's CSV-typed <c>valid_principals</c> (10-ssh-engine.md:38).</summary>
    public IReadOnlyList<string>? ValidPrincipals { get; init; }

    /// <summary>The wire <c>ttl</c> field, in seconds.</summary>
    public TimeSpan? Ttl { get; init; }

    /// <summary>The wire <c>cert_type</c> field: <c>"user"</c> or <c>"host"</c>.</summary>
    public string? CertType { get; init; }

    /// <summary>The wire <c>key_id</c> field.</summary>
    public string? KeyId { get; init; }

    /// <summary>The wire's map-typed <c>extensions</c> override.</summary>
    public IReadOnlyDictionary<string, JsonElement>? Extensions { get; init; }

    /// <summary>The wire's map-typed <c>critical_options</c> override.</summary>
    public IReadOnlyDictionary<string, JsonElement>? CriticalOptions { get; init; }
}

/// <summary>
/// 10 §Signing (CA mode): <c>Ssh.Sign</c>'s result, transcribed from <c>10-ssh-engine.md:35</c>.
/// SSH-002: <see cref="SignedKey"/> is an OpenSSH certificate line, verbatim; use
/// <see cref="SshOperations.WriteCertificateFile"/> to persist it with the required <c>0644</c> mode.
/// </summary>
public sealed class SignedSshCertificate
{
    /// <summary>The signed OpenSSH certificate line, verbatim (SSH-002).</summary>
    public required string SignedKey { get; init; }

    /// <summary>
    /// The certificate's serial number, exactly as the server returned it. Bound as a string
    /// rather than a numeric type: 10 states no type for this field, and the PKI serial-number
    /// precedent (<see cref="IssuedCertificate.SerialNumber"/>) is to forward the server's bytes
    /// rather than assume a numeric range that later overflows or loses precision (D-M1c-25).
    /// </summary>
    public required string SerialNumber { get; init; }

    /// <summary>The wire <c>algorithm</c> field, when the server returned one.</summary>
    public string? Algorithm { get; init; }
}

/// <summary>
/// 10 §One-time passwords (OTP mode): <c>Ssh.Creds</c>'s result, transcribed from
/// <c>10-ssh-engine.md:49</c>. <see cref="Key"/> is the OTP (D-M9-3: <see cref="SecretString"/>).
/// </summary>
public sealed class SshCredentials
{
    /// <summary>The generated one-time password, redacted (D-M9-3).</summary>
    public required SecretString Key { get; init; }

    /// <summary>The wire <c>key_type</c> field, always <c>"otp"</c> for this route.</summary>
    public required string KeyType { get; init; }

    /// <summary>The wire <c>username</c> field.</summary>
    public required string Username { get; init; }

    /// <summary>The wire <c>ip</c> field.</summary>
    public required string Ip { get; init; }

    /// <summary>The wire <c>port</c> field.</summary>
    public required int Port { get; init; }

    /// <summary>The wire <c>ttl</c> field, in seconds.</summary>
    public TimeSpan? Ttl { get; init; }
}

/// <summary>
/// 10 §One-time passwords (OTP mode): <c>Ssh.Verify</c>'s result, transcribed from
/// <c>10-ssh-engine.md:50</c>.
/// </summary>
public sealed class SshOtpVerification
{
    /// <summary>The wire <c>username</c> field.</summary>
    public required string Username { get; init; }

    /// <summary>The wire <c>ip</c> field.</summary>
    public required string Ip { get; init; }

    /// <summary>The wire <c>role_name</c> field.</summary>
    public required string RoleName { get; init; }

    /// <summary>The wire <c>port</c> field.</summary>
    public required int Port { get; init; }
}

// ============================================================================ M9 slice d: SSH broker

/// <summary>
/// 10 §SSH broker: <c>SshBroker.ReadGlobal</c>/<c>WriteGlobal</c>'s shared field list
/// (<c>10-ssh-engine.md:77</c>), root-gated. Patch-shaped (OVR-007) like <see cref="PkiRole"/>.
/// </summary>
public sealed class SshBrokerGlobalPolicy
{
    /// <summary>The wire <c>login_class_default</c> field: <c>"shared-credential"</c> or <c>"brokered"</c>.</summary>
    public string? LoginClassDefault { get; init; }

    /// <summary>The wire <c>login_class_lock</c> field.</summary>
    public bool? LoginClassLock { get; init; }
}

/// <summary>
/// 10 §SSH broker: <c>SshBroker.ReadType</c>/<c>WriteType</c>'s shared field list
/// (<c>10-ssh-engine.md:78</c>). Patch-shaped (OVR-007).
/// </summary>
public sealed class SshBrokerTypePolicy
{
    /// <summary>The wire <c>login_class</c> field.</summary>
    public string? LoginClass { get; init; }

    /// <summary>The wire <c>lock</c> field.</summary>
    public bool? Lock { get; init; }
}

/// <summary>
/// 10 §SSH broker: <c>SshBroker.ReadAssetGroup</c>/<c>WriteAssetGroup</c>'s shared field list
/// (<c>10-ssh-engine.md:79</c>). Patch-shaped (OVR-007).
/// </summary>
public sealed class SshBrokerAssetGroupPolicy
{
    /// <summary>The wire <c>login_class</c> field.</summary>
    public string? LoginClass { get; init; }

    /// <summary>The wire <c>priority</c> field, resolving most-restrictive-wins across tiers.</summary>
    public int? Priority { get; init; }

    /// <summary>The wire <c>lock</c> field.</summary>
    public bool? Lock { get; init; }
}

/// <summary>
/// 10 §SSH broker: <c>SshBroker.ReadResource</c>/<c>WriteResource</c>'s shared field list
/// (<c>10-ssh-engine.md:80</c>). Patch-shaped (OVR-007).
/// </summary>
public sealed class SshBrokerResourcePolicy
{
    /// <summary>The wire <c>login_class</c> field.</summary>
    public string? LoginClass { get; init; }
}

/// <summary>
/// 10 §SSH broker: <c>SshBroker.Effective</c>'s result, transcribed from
/// <c>10-ssh-engine.md:81</c>.
/// </summary>
public sealed class SshBrokerEffectivePolicy
{
    /// <summary>The wire <c>login_class</c> field: the resolved <c>shared-credential</c> or <c>brokered</c> class.</summary>
    public required string LoginClass { get; init; }

    /// <summary>The wire <c>source</c> field: which tier the resolution came from.</summary>
    public required string Source { get; init; }
}
