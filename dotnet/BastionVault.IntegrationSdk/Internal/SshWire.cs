using System.Net;
using System.Text.Json;

namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// The wire reading, CSV-field handling and client-side rejection <see cref="SshOperations"/> and
/// <see cref="SshBrokerOperations"/> share (10 — SSH engine and SSH broker). One copy, mirroring the
/// <see cref="PkiWire"/> precedent (D-M9-3). CSV joining/splitting and the seconds-field helpers are
/// not duplicated here: <c>10-ssh-engine.md:38</c> states the PKI-010 pattern explicitly for
/// <c>valid_principals</c>, so this reuses <see cref="PkiWire.WriteCsv"/>/<see cref="PkiWire.SplitCsv"/>
/// and <see cref="PkiWire.WriteSeconds"/>/<see cref="PkiWire.ReadSeconds"/> directly rather than a
/// second copy of the same null-vs-empty convention.
/// </summary>
internal static class SshWire
{
    /// <summary>SSH-001: <c>public_key</c> empty or whitespace-only → <c>BV-INPUT-001</c> client-side, no request sent.</summary>
    public static void RequirePublicKey(string publicKey, string path)
    {
        if (string.IsNullOrWhiteSpace(publicKey))
        {
            throw KvWire.InvalidArgument("publicKey", "must not be empty or whitespace (SSH-001)", path);
        }
    }

    /// <summary>SSH-003: <c>ip</c> MUST be a valid IP literal, checked client-side before any request is sent.</summary>
    public static void RequireIp(string ip, string path)
    {
        if (!IPAddress.TryParse(ip, out _))
        {
            throw KvWire.InvalidArgument("ip", $"must be a valid IP literal (SSH-003), got '{ip}'", path);
        }
    }

    private static void WriteBool(Utf8JsonWriter writer, string name, bool? value)
    {
        if (value is { } flag)
        {
            writer.WriteBoolean(name, flag);
        }
    }

    private static bool? ReadBool(IReadOnlyDictionary<string, JsonElement> wire, string name)
    {
        return wire.TryGetValue(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
    }

    private static void WriteMapIfPresent(Utf8JsonWriter writer, string name, IReadOnlyDictionary<string, JsonElement>? map)
    {
        if (map is not null)
        {
            KvWire.WriteMap(writer, name, map);
        }
    }

    /// <summary>
    /// D-M9-29: the read half of an <see cref="SshRole"/> allow-list field. This SDK always
    /// <b>writes</b> these fields CSV-joined (<c>PkiWire.WriteCsv</c>, the PKI-010 pattern
    /// <c>10-ssh-engine.md:38</c> states explicitly for <c>valid_principals</c>), but 10 never
    /// states that the server's own responses echo the same encoding for a role's allow-lists —
    /// unlike <c>valid_principals</c>, which is a request-only field on <c>Ssh.Sign</c>. Reading
    /// with <see cref="PkiWire.SplitCsv"/> alone would silently report <see langword="null"/> for
    /// a JSON array response on an authorisation-relevant field (<c>PkiWire.cs:30</c> returns
    /// <see langword="null"/> for any non-string value) — a wrong answer that fails silently
    /// rather than loudly. This accepts <b>either</b> wire form without guessing which the server
    /// actually sends, and does not touch <see cref="PkiWire.SplitCsv"/> itself, which three other
    /// slices own (<b>CLA-008</b>). An absent field still yields <see langword="null"/>, matching
    /// <see cref="PkiWire.SplitCsv"/>'s own absent/present-empty distinction. A JSON array with no
    /// string elements (e.g. <c>[1, 2]</c>) yields an empty list rather than <see langword="null"/>,
    /// and a mixed array silently drops its non-string entries; both are a tolerant reader failing
    /// safe rather than open for an allow-list field, and are not treated as a wire-shape error.
    /// </summary>
    private static IReadOnlyList<string>? ReadCsvOrArray(IReadOnlyDictionary<string, JsonElement> wire, string name)
    {
        if (!wire.TryGetValue(name, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => PkiWire.SplitCsv(wire, name),
            JsonValueKind.Array => value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!)
                .ToArray(),
            _ => null,
        };
    }

    // ------------------------------------------------------------ CA configuration

    /// <summary>10 §CA configuration: <c>Ssh.ConfigureCa</c>/<c>Ssh.ReadCa</c>'s shared result shape.</summary>
    public static SshCaKey ReadCaKey(IReadOnlyDictionary<string, JsonElement> wire, string path)
    {
        return new SshCaKey
        {
            PublicKey = KvWire.ReadString(wire, "public_key") ?? throw KvWire.EnvelopeMismatch(path, "public_key"),
            Algorithm = KvWire.ReadString(wire, "algorithm"),
        };
    }

    // ------------------------------------------------------------ roles

    /// <summary>Serialises an <see cref="SshRole"/> body (OVR-007: absent members are omitted, never sent empty).</summary>
    public static ReadOnlyMemory<byte> SerialiseRole(SshRole role)
    {
        ArgumentNullException.ThrowIfNull(role);
        return KvWire.Serialise(writer =>
        {
            if (role.KeyType is { } keyType)
            {
                writer.WriteString("key_type", keyType);
            }

            if (role.AlgorithmSigner is { } algorithmSigner)
            {
                writer.WriteString("algorithm_signer", algorithmSigner);
            }

            if (role.CertType is { } certType)
            {
                writer.WriteString("cert_type", certType);
            }

            PkiWire.WriteCsv(writer, "allowed_users", role.AllowedUsers);
            if (role.DefaultUser is { } defaultUser)
            {
                writer.WriteString("default_user", defaultUser);
            }

            PkiWire.WriteCsv(writer, "allowed_extensions", role.AllowedExtensions);
            WriteMapIfPresent(writer, "default_extensions", role.DefaultExtensions);
            PkiWire.WriteCsv(writer, "allowed_critical_options", role.AllowedCriticalOptions);
            WriteMapIfPresent(writer, "default_critical_options", role.DefaultCriticalOptions);
            PkiWire.WriteSeconds(writer, "ttl", role.Ttl);
            PkiWire.WriteSeconds(writer, "max_ttl", role.MaxTtl);
            PkiWire.WriteSeconds(writer, "not_before_duration", role.NotBeforeDuration);
            if (role.KeyIdFormat is { } keyIdFormat)
            {
                writer.WriteString("key_id_format", keyIdFormat);
            }

            PkiWire.WriteCsv(writer, "cidr_list", role.CidrList);
            PkiWire.WriteCsv(writer, "exclude_cidr_list", role.ExcludeCidrList);
            if (role.Port is { } port)
            {
                writer.WriteNumber("port", port);
            }

            WriteBool(writer, "pqc_only", role.PqcOnly);
        });
    }

    /// <summary>Reads an <see cref="SshRole"/> back from the wire (10 §Roles' shared field list).</summary>
    public static SshRole ReadRole(IReadOnlyDictionary<string, JsonElement> wire)
    {
        return new SshRole
        {
            KeyType = KvWire.ReadString(wire, "key_type"),
            AlgorithmSigner = KvWire.ReadString(wire, "algorithm_signer"),
            CertType = KvWire.ReadString(wire, "cert_type"),
            AllowedUsers = ReadCsvOrArray(wire, "allowed_users"),
            DefaultUser = KvWire.ReadString(wire, "default_user"),
            AllowedExtensions = ReadCsvOrArray(wire, "allowed_extensions"),
            DefaultExtensions = KvWire.ReadDataMap(wire, "default_extensions"),
            AllowedCriticalOptions = ReadCsvOrArray(wire, "allowed_critical_options"),
            DefaultCriticalOptions = KvWire.ReadDataMap(wire, "default_critical_options"),
            Ttl = PkiWire.ReadSeconds(wire, "ttl"),
            MaxTtl = PkiWire.ReadSeconds(wire, "max_ttl"),
            NotBeforeDuration = PkiWire.ReadSeconds(wire, "not_before_duration"),
            KeyIdFormat = KvWire.ReadString(wire, "key_id_format"),
            CidrList = ReadCsvOrArray(wire, "cidr_list"),
            ExcludeCidrList = ReadCsvOrArray(wire, "exclude_cidr_list"),
            Port = KvWire.ReadInt(wire, "port"),
            PqcOnly = ReadBool(wire, "pqc_only"),
        };
    }

    /// <summary>
    /// 14 §Bulk metadata listings: <c>Ssh.ListRolesInfo</c>'s page, transcribed as
    /// <see cref="SshRole"/> per D-M9-9 (the owning section names the element type, not
    /// <c>14-batch-and-request-efficiency.md</c>'s <c>SshRoleSummary</c>, which has no field list
    /// anywhere). Follows <see cref="PkiOperations.ListCertificatesInfoAsync"/>'s zip exactly.
    /// </summary>
    public static Page<SshRole> ReadRolePage(IReadOnlyDictionary<string, JsonElement> data, string path)
    {
        IReadOnlyList<string> keys = SysWire.ReadKeys(data);
        List<SshRole> records = [];
        if (data.TryGetValue("records", out JsonElement recordsElement) && recordsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement record in recordsElement.EnumerateArray())
            {
                records.Add(record.ValueKind == JsonValueKind.Object
                    ? ReadRole(SysWire.AsMap(record))
                    : throw KvWire.EnvelopeMismatch(path, "records[]"));
            }
        }

        if (records.Count != keys.Count)
        {
            throw KvWire.EnvelopeMismatch(path, "records");
        }

        string? next = SysWire.ReadString(data, "next");
        return new Page<SshRole>
        {
            Keys = keys,
            Records = records,
            Total = SysWire.ReadNullableLong(data, "total") is { } total ? (int)total : keys.Count,
            Next = string.IsNullOrEmpty(next) ? null : next,
            Truncated = data.TryGetValue("truncated", out JsonElement truncated) && truncated.ValueKind == JsonValueKind.True,
        };
    }

    // ------------------------------------------------------------ signing (CA mode)

    /// <summary>Serialises an <see cref="SshSignRequest"/> body (<c>10-ssh-engine.md:34</c>).</summary>
    public static ReadOnlyMemory<byte> SerialiseSignRequest(SshSignRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return KvWire.Serialise(writer =>
        {
            writer.WriteString("public_key", request.PublicKey);
            PkiWire.WriteCsv(writer, "valid_principals", request.ValidPrincipals);
            PkiWire.WriteSeconds(writer, "ttl", request.Ttl);
            if (request.CertType is { } certType)
            {
                writer.WriteString("cert_type", certType);
            }

            if (request.KeyId is { } keyId)
            {
                writer.WriteString("key_id", keyId);
            }

            WriteMapIfPresent(writer, "extensions", request.Extensions);
            WriteMapIfPresent(writer, "critical_options", request.CriticalOptions);
        });
    }

    /// <summary>SSH-001, SSH-002: <c>Ssh.Sign</c>'s result, transcribed from <c>10-ssh-engine.md:35</c>.</summary>
    public static SignedSshCertificate ReadSignedSshCertificate(IReadOnlyDictionary<string, JsonElement> wire, string path)
    {
        return new SignedSshCertificate
        {
            SignedKey = KvWire.ReadString(wire, "signed_key") ?? throw KvWire.EnvelopeMismatch(path, "signed_key"),
            SerialNumber = KvWire.ReadString(wire, "serial_number") ?? throw KvWire.EnvelopeMismatch(path, "serial_number"),
            Algorithm = KvWire.ReadString(wire, "algorithm"),
        };
    }

    // ------------------------------------------------------------ one-time passwords (OTP mode)

    /// <summary>D-M9-3: <c>Ssh.Creds</c>'s result, transcribed from <c>10-ssh-engine.md:49</c>. <c>key</c> is the OTP, redacted.</summary>
    public static SshCredentials ReadCredentials(IReadOnlyDictionary<string, JsonElement> wire, string path)
    {
        return new SshCredentials
        {
            Key = new SecretString(KvWire.ReadString(wire, "key") ?? throw KvWire.EnvelopeMismatch(path, "key")),
            KeyType = KvWire.ReadString(wire, "key_type") ?? throw KvWire.EnvelopeMismatch(path, "key_type"),
            Username = KvWire.ReadString(wire, "username") ?? throw KvWire.EnvelopeMismatch(path, "username"),
            Ip = KvWire.ReadString(wire, "ip") ?? throw KvWire.EnvelopeMismatch(path, "ip"),
            Port = KvWire.ReadInt(wire, "port") ?? throw KvWire.EnvelopeMismatch(path, "port"),
            Ttl = PkiWire.ReadSeconds(wire, "ttl"),
        };
    }

    /// <summary>10-ssh-engine.md:50: <c>Ssh.Verify</c>'s result on a recognised otp.</summary>
    public static SshOtpVerification ReadOtpVerification(IReadOnlyDictionary<string, JsonElement> wire, string path)
    {
        return new SshOtpVerification
        {
            Username = KvWire.ReadString(wire, "username") ?? throw KvWire.EnvelopeMismatch(path, "username"),
            Ip = KvWire.ReadString(wire, "ip") ?? throw KvWire.EnvelopeMismatch(path, "ip"),
            RoleName = KvWire.ReadString(wire, "role_name") ?? throw KvWire.EnvelopeMismatch(path, "role_name"),
            Port = KvWire.ReadInt(wire, "port") ?? throw KvWire.EnvelopeMismatch(path, "port"),
        };
    }

    // ------------------------------------------------------------ SSH broker

    /// <summary>Serialises an <see cref="SshBrokerGlobalPolicy"/> body (<c>10-ssh-engine.md:77</c>).</summary>
    public static ReadOnlyMemory<byte> SerialiseGlobalPolicy(SshBrokerGlobalPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return KvWire.Serialise(writer =>
        {
            if (policy.LoginClassDefault is { } loginClassDefault)
            {
                writer.WriteString("login_class_default", loginClassDefault);
            }

            WriteBool(writer, "login_class_lock", policy.LoginClassLock);
        });
    }

    /// <summary>Reads an <see cref="SshBrokerGlobalPolicy"/> body back.</summary>
    public static SshBrokerGlobalPolicy ReadGlobalPolicy(IReadOnlyDictionary<string, JsonElement> wire)
    {
        return new SshBrokerGlobalPolicy
        {
            LoginClassDefault = KvWire.ReadString(wire, "login_class_default"),
            LoginClassLock = ReadBool(wire, "login_class_lock"),
        };
    }

    /// <summary>Serialises an <see cref="SshBrokerTypePolicy"/> body (<c>10-ssh-engine.md:78</c>).</summary>
    public static ReadOnlyMemory<byte> SerialiseTypePolicy(SshBrokerTypePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return KvWire.Serialise(writer =>
        {
            if (policy.LoginClass is { } loginClass)
            {
                writer.WriteString("login_class", loginClass);
            }

            WriteBool(writer, "lock", policy.Lock);
        });
    }

    /// <summary>Reads an <see cref="SshBrokerTypePolicy"/> body back.</summary>
    public static SshBrokerTypePolicy ReadTypePolicy(IReadOnlyDictionary<string, JsonElement> wire)
    {
        return new SshBrokerTypePolicy
        {
            LoginClass = KvWire.ReadString(wire, "login_class"),
            Lock = ReadBool(wire, "lock"),
        };
    }

    /// <summary>Serialises an <see cref="SshBrokerAssetGroupPolicy"/> body (<c>10-ssh-engine.md:79</c>).</summary>
    public static ReadOnlyMemory<byte> SerialiseAssetGroupPolicy(SshBrokerAssetGroupPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return KvWire.Serialise(writer =>
        {
            if (policy.LoginClass is { } loginClass)
            {
                writer.WriteString("login_class", loginClass);
            }

            if (policy.Priority is { } priority)
            {
                writer.WriteNumber("priority", priority);
            }

            WriteBool(writer, "lock", policy.Lock);
        });
    }

    /// <summary>Reads an <see cref="SshBrokerAssetGroupPolicy"/> body back.</summary>
    public static SshBrokerAssetGroupPolicy ReadAssetGroupPolicy(IReadOnlyDictionary<string, JsonElement> wire)
    {
        return new SshBrokerAssetGroupPolicy
        {
            LoginClass = KvWire.ReadString(wire, "login_class"),
            Priority = KvWire.ReadInt(wire, "priority"),
            Lock = ReadBool(wire, "lock"),
        };
    }

    /// <summary>Serialises an <see cref="SshBrokerResourcePolicy"/> body (<c>10-ssh-engine.md:80</c>).</summary>
    public static ReadOnlyMemory<byte> SerialiseResourcePolicy(SshBrokerResourcePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return KvWire.Serialise(writer =>
        {
            if (policy.LoginClass is { } loginClass)
            {
                writer.WriteString("login_class", loginClass);
            }
        });
    }

    /// <summary>Reads an <see cref="SshBrokerResourcePolicy"/> body back.</summary>
    public static SshBrokerResourcePolicy ReadResourcePolicy(IReadOnlyDictionary<string, JsonElement> wire)
    {
        return new SshBrokerResourcePolicy { LoginClass = KvWire.ReadString(wire, "login_class") };
    }

    /// <summary>
    /// Serialises <c>SshBroker.Effective</c>'s request body (<c>10-ssh-engine.md:81</c>).
    /// <c>asset_group_ids</c> is CSV-joined on the wire, per the accepted
    /// <c>sshbroker.effective-v2-pinned</c> fixture (Appendix C), the same PKI-010 pattern as
    /// <c>valid_principals</c> rather than a JSON array.
    /// </summary>
    public static ReadOnlyMemory<byte> SerialiseEffectiveRequest(string resourceId, string resourceType, IReadOnlyList<string>? assetGroupIds)
    {
        return KvWire.Serialise(writer =>
        {
            writer.WriteString("resource_id", resourceId);
            writer.WriteString("resource_type", resourceType);
            PkiWire.WriteCsv(writer, "asset_group_ids", assetGroupIds);
        });
    }

    /// <summary>Reads <c>SshBroker.Effective</c>'s result back, transcribed from <c>10-ssh-engine.md:81</c>.</summary>
    public static SshBrokerEffectivePolicy ReadEffectivePolicy(IReadOnlyDictionary<string, JsonElement> wire, string path)
    {
        return new SshBrokerEffectivePolicy
        {
            LoginClass = KvWire.ReadString(wire, "login_class") ?? throw KvWire.EnvelopeMismatch(path, "login_class"),
            Source = KvWire.ReadString(wire, "source") ?? throw KvWire.EnvelopeMismatch(path, "source"),
        };
    }
}
