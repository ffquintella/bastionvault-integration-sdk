using System.Text.Json;

namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// The wire reading and serialising <see cref="LdapOperations"/> and its nested operations share
/// (12 §LDAP), mirroring <see cref="PkiWire"/> (D-M9-3). <see cref="ReadCheckConnectionResult"/>
/// reads <c>12-other-engines-and-identity.md:76</c>'s PascalCase names as their snake_case wire
/// spelling, per <c>00-overview.md:119-120</c>'s canonical-name/wire-field convention.
/// </summary>
internal static class LdapWire
{
    /// <summary>LDP-001: <c>insecure_tls = true</c> requires <c>acknowledge_insecure_tls = true</c>, refused client-side (<c>BV-INPUT-001</c>) before any request is sent, mirroring the server's own check.</summary>
    public static void RequireAcknowledgedInsecureTls(LdapConfig config, string path)
    {
        if (config.InsecureTls == true && config.AcknowledgeInsecureTls != true)
        {
            throw KvWire.InvalidArgument(
                "insecureTls",
                "requires acknowledgeInsecureTls to also be true before this config is sent (LDP-001)",
                path);
        }
    }

    /// <summary>Serialises an <see cref="LdapConfig"/> body (OVR-007: absent members are omitted, never sent empty).</summary>
    public static ReadOnlyMemory<byte> SerialiseConfig(LdapConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return KvWire.Serialise(writer =>
        {
            if (config.Url is { } url)
            {
                writer.WriteString("url", url);
            }

            if (config.BindDn is { } bindDn)
            {
                writer.WriteString("binddn", bindDn);
            }

            if (config.BindPass is { HasValue: true } bindPass)
            {
                writer.WriteString("bindpass", bindPass.Reveal());
            }

            if (config.UserDn is { } userDn)
            {
                writer.WriteString("userdn", userDn);
            }

            if (config.DirectoryType is { } directoryType)
            {
                writer.WriteString("directory_type", directoryType);
            }

            if (config.PasswordPolicy is { } passwordPolicy)
            {
                writer.WriteString("password_policy", passwordPolicy);
            }

            WriteSeconds(writer, "request_timeout", config.RequestTimeout);
            WriteBool(writer, "starttls", config.StartTls);
            if (config.ClientTlsCert is { } clientTlsCert)
            {
                writer.WriteString("client_tls_cert", clientTlsCert);
            }

            if (config.ClientTlsKey is { HasValue: true } clientTlsKey)
            {
                writer.WriteString("client_tls_key", clientTlsKey.Reveal());
            }

            if (config.TlsMinVersion is { } tlsMinVersion)
            {
                writer.WriteString("tls_min_version", tlsMinVersion);
            }

            WriteBool(writer, "insecure_tls", config.InsecureTls);
            WriteBool(writer, "acknowledge_insecure_tls", config.AcknowledgeInsecureTls);
            if (config.UserAttr is { } userAttr)
            {
                writer.WriteString("userattr", userAttr);
            }
        });
    }

    /// <summary>Reads an <see cref="LdapConfig"/> body back. <c>bindpass</c>/<c>client_tls_key</c> are never returned, so both stay <see langword="null"/>.</summary>
    public static LdapConfig ReadConfig(IReadOnlyDictionary<string, JsonElement> wire)
    {
        return new LdapConfig
        {
            Url = KvWire.ReadString(wire, "url"),
            BindDn = KvWire.ReadString(wire, "binddn"),
            UserDn = KvWire.ReadString(wire, "userdn"),
            DirectoryType = KvWire.ReadString(wire, "directory_type"),
            PasswordPolicy = KvWire.ReadString(wire, "password_policy"),
            RequestTimeout = ReadSeconds(wire, "request_timeout"),
            StartTls = ReadBool(wire, "starttls"),
            ClientTlsCert = KvWire.ReadString(wire, "client_tls_cert"),
            TlsMinVersion = KvWire.ReadString(wire, "tls_min_version"),
            InsecureTls = ReadBool(wire, "insecure_tls"),
            AcknowledgeInsecureTls = ReadBool(wire, "acknowledge_insecure_tls"),
            UserAttr = KvWire.ReadString(wire, "userattr"),
        };
    }

    /// <summary>12 §LDAP: <c>Ldap.CheckConnection</c>'s result. See this file's remarks for the PascalCase-to-snake_case transcription this reads against.</summary>
    public static LdapCheckConnectionResult ReadCheckConnectionResult(IReadOnlyDictionary<string, JsonElement> wire)
    {
        return new LdapCheckConnectionResult
        {
            Ok = KvWire.ReadBool(wire, "ok"),
            Stage = KvWire.ReadString(wire, "stage"),
            Error = KvWire.ReadString(wire, "error"),
            Url = KvWire.ReadString(wire, "url"),
            BindDn = KvWire.ReadString(wire, "bind_dn"),
            Host = KvWire.ReadString(wire, "host"),
            Port = KvWire.ReadInt(wire, "port"),
            Scheme = KvWire.ReadString(wire, "scheme"),
            LatencyMs = SysWire.ReadNullableLong(wire, "latency_ms"),
        };
    }

    /// <summary>Serialises an <see cref="LdapStaticRole"/> body (<c>{mount}/static-role/{name}</c>).</summary>
    public static ReadOnlyMemory<byte> SerialiseStaticRole(LdapStaticRole role)
    {
        ArgumentNullException.ThrowIfNull(role);
        return KvWire.Serialise(writer =>
        {
            if (role.Dn is { } dn)
            {
                writer.WriteString("dn", dn);
            }

            if (role.Username is { } username)
            {
                writer.WriteString("username", username);
            }

            WriteSeconds(writer, "rotation_period", role.RotationPeriod);
            if (role.PasswordPolicy is { } passwordPolicy)
            {
                writer.WriteString("password_policy", passwordPolicy);
            }
        });
    }

    /// <summary>Reads an <see cref="LdapStaticRole"/> body back.</summary>
    public static LdapStaticRole ReadStaticRole(IReadOnlyDictionary<string, JsonElement> wire)
    {
        return new LdapStaticRole
        {
            Dn = KvWire.ReadString(wire, "dn"),
            Username = KvWire.ReadString(wire, "username"),
            RotationPeriod = ReadSeconds(wire, "rotation_period"),
            PasswordPolicy = KvWire.ReadString(wire, "password_policy"),
        };
    }

    /// <summary>12 §LDAP: <c>Ldap.StaticCred</c>'s result. PKI-002-style redaction: <c>password</c> is a credential, held in a <see cref="SecretString"/>.</summary>
    public static LdapStaticCred ReadStaticCred(IReadOnlyDictionary<string, JsonElement> wire, string path)
    {
        return new LdapStaticCred
        {
            Username = KvWire.ReadString(wire, "username") ?? throw KvWire.EnvelopeMismatch(path, "username"),
            Dn = KvWire.ReadString(wire, "dn") ?? throw KvWire.EnvelopeMismatch(path, "dn"),
            Password = new SecretString(KvWire.ReadString(wire, "password") ?? throw KvWire.EnvelopeMismatch(path, "password")),
            LastRotated = KvWire.ReadOptionalInstant(wire, "last_rotated"),
            TtlSecs = SysWire.ReadNullableLong(wire, "ttl_secs"),
        };
    }

    /// <summary>Serialises an <see cref="LdapLibrarySet"/> body (<c>{mount}/library/{set}</c>).</summary>
    public static ReadOnlyMemory<byte> SerialiseLibrarySet(LdapLibrarySet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        return KvWire.Serialise(writer =>
        {
            WriteStringArray(writer, "service_account_names", set.ServiceAccountNames);
            WriteSeconds(writer, "ttl", set.Ttl);
            WriteSeconds(writer, "max_ttl", set.MaxTtl);
            WriteBool(writer, "disable_check_in_enforcement", set.DisableCheckInEnforcement);
            WriteSeconds(writer, "affinity_ttl", set.AffinityTtl);
        });
    }

    /// <summary>Reads an <see cref="LdapLibrarySet"/> body back.</summary>
    public static LdapLibrarySet ReadLibrarySet(IReadOnlyDictionary<string, JsonElement> wire)
    {
        return new LdapLibrarySet
        {
            ServiceAccountNames = KvWire.ReadStringList(wire, "service_account_names"),
            Ttl = ReadSeconds(wire, "ttl"),
            MaxTtl = ReadSeconds(wire, "max_ttl"),
            DisableCheckInEnforcement = ReadBool(wire, "disable_check_in_enforcement"),
            AffinityTtl = ReadSeconds(wire, "affinity_ttl"),
        };
    }

    /// <summary>12 §LDAP: <c>Ldap.Library.CheckOut</c>'s result. <c>password</c> is a credential, held in a <see cref="SecretString"/>.</summary>
    public static LdapLibraryCheckOut ReadLibraryCheckOut(IReadOnlyDictionary<string, JsonElement> wire, string path)
    {
        return new LdapLibraryCheckOut
        {
            ServiceAccountName = KvWire.ReadString(wire, "service_account_name") ?? throw KvWire.EnvelopeMismatch(path, "service_account_name"),
            Password = new SecretString(KvWire.ReadString(wire, "password") ?? throw KvWire.EnvelopeMismatch(path, "password")),
            LeaseId = KvWire.ReadString(wire, "lease_id") ?? throw KvWire.EnvelopeMismatch(path, "lease_id"),
            TtlSecs = SysWire.ReadNullableLong(wire, "ttl_secs") ?? throw KvWire.EnvelopeMismatch(path, "ttl_secs"),
        };
    }

    /// <summary>12 §LDAP: <c>Ldap.Library.Status</c>'s result. <c>checked_out</c>'s entries carry no documented field set, so they stay a raw wire map (D-M1c-25).</summary>
    public static LdapLibraryStatus ReadLibraryStatus(IReadOnlyDictionary<string, JsonElement> wire)
    {
        return new LdapLibraryStatus
        {
            CheckedOut = KvWire.ReadDataMap(wire, "checked_out") ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal),
            Available = KvWire.ReadStringList(wire, "available"),
        };
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

    private static void WriteSeconds(Utf8JsonWriter writer, string name, TimeSpan? value)
    {
        if (value is { } duration)
        {
            writer.WriteNumber(name, (long)duration.TotalSeconds);
        }
    }

    private static TimeSpan? ReadSeconds(IReadOnlyDictionary<string, JsonElement> wire, string name)
    {
        return wire.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? TimeSpan.FromSeconds(value.GetInt64())
            : null;
    }

    private static void WriteStringArray(Utf8JsonWriter writer, string name, IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return;
        }

        writer.WriteStartArray(name);
        foreach (string value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }
}
