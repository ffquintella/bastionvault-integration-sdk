using System.Text.Json;

namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// The wire reading and serialising <see cref="CertLifecycleOperations"/> shares (12 §Cert
/// lifecycle), mirroring <see cref="PkiWire"/>'s precedent for its own area (D-M9-3). Every
/// duration field here is unquoted in <c>12-other-engines-and-identity.md</c> (TRN-031), so all
/// of them — including <c>renew_before</c> — are integer seconds, never a Go-style string.
/// </summary>
internal static class CertLifecycleWire
{
    /// <summary>Serialises a <see cref="Target"/> body (OVR-007: absent members are omitted, never sent empty).</summary>
    public static ReadOnlyMemory<byte> SerialiseTarget(Target target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return KvWire.Serialise(writer =>
        {
            if (target.Kind is { } kind)
            {
                writer.WriteString("kind", kind);
            }

            if (target.Address is { } address)
            {
                writer.WriteString("address", address);
            }

            if (target.PkiMount is { } pkiMount)
            {
                writer.WriteString("pki_mount", pkiMount);
            }

            if (target.RoleRef is { } roleRef)
            {
                writer.WriteString("role_ref", roleRef);
            }

            if (target.CommonName is { } commonName)
            {
                writer.WriteString("common_name", commonName);
            }

            WriteStringArray(writer, "alt_names", target.AltNames);
            WriteStringArray(writer, "ip_sans", target.IpSans);
            WriteSeconds(writer, "ttl", target.Ttl);
            if (target.KeyPolicy is { } keyPolicy)
            {
                writer.WriteString("key_policy", keyPolicy);
            }

            if (target.KeyRef is { } keyRef)
            {
                writer.WriteString("key_ref", keyRef);
            }

            WriteSeconds(writer, "renew_before", target.RenewBefore);
        });
    }

    /// <summary>Reads a <see cref="Target"/> body back.</summary>
    public static Target ReadTarget(IReadOnlyDictionary<string, JsonElement> wire)
    {
        return new Target
        {
            Kind = KvWire.ReadString(wire, "kind"),
            Address = KvWire.ReadString(wire, "address"),
            PkiMount = KvWire.ReadString(wire, "pki_mount"),
            RoleRef = KvWire.ReadString(wire, "role_ref"),
            CommonName = KvWire.ReadString(wire, "common_name"),
            AltNames = ReadStringArrayOrNull(wire, "alt_names"),
            IpSans = ReadStringArrayOrNull(wire, "ip_sans"),
            Ttl = ReadSeconds(wire, "ttl"),
            KeyPolicy = KvWire.ReadString(wire, "key_policy"),
            KeyRef = KvWire.ReadString(wire, "key_ref"),
            RenewBefore = ReadSeconds(wire, "renew_before"),
        };
    }

    /// <summary>12 §Cert lifecycle: <c>CertLifecycle.State</c>'s result.</summary>
    public static TargetState ReadTargetState(IReadOnlyDictionary<string, JsonElement> wire)
    {
        return new TargetState
        {
            CurrentSerial = KvWire.ReadString(wire, "current_serial"),
            CurrentNotAfter = KvWire.ReadOptionalInstant(wire, "current_not_after"),
            LastRenewal = KvWire.ReadOptionalInstant(wire, "last_renewal"),
            LastAttempt = KvWire.ReadOptionalInstant(wire, "last_attempt"),
            LastError = KvWire.ReadString(wire, "last_error"),
            NextAttempt = KvWire.ReadOptionalInstant(wire, "next_attempt"),
            FailureCount = KvWire.ReadInt(wire, "failure_count"),
        };
    }

    /// <summary>Serialises a <see cref="SchedulerConfig"/> body. <c>client_token_set</c> is read-only and never written.</summary>
    public static ReadOnlyMemory<byte> SerialiseSchedulerConfig(SchedulerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return KvWire.Serialise(writer =>
        {
            WriteBool(writer, "enabled", config.Enabled);
            if (config.TickIntervalSeconds is { } tickIntervalSeconds)
            {
                writer.WriteNumber("tick_interval_seconds", tickIntervalSeconds);
            }

            if (config.ClientToken is { HasValue: true } clientToken)
            {
                writer.WriteString("client_token", clientToken.Reveal());
            }

            if (config.BaseBackoffSeconds is { } baseBackoffSeconds)
            {
                writer.WriteNumber("base_backoff_seconds", baseBackoffSeconds);
            }

            if (config.MaxBackoffSeconds is { } maxBackoffSeconds)
            {
                writer.WriteNumber("max_backoff_seconds", maxBackoffSeconds);
            }
        });
    }

    /// <summary>Reads a <see cref="SchedulerConfig"/> body back. <c>client_token</c> is never returned; <c>client_token_set</c> is read here instead.</summary>
    public static SchedulerConfig ReadSchedulerConfig(IReadOnlyDictionary<string, JsonElement> wire)
    {
        return new SchedulerConfig
        {
            Enabled = ReadBool(wire, "enabled"),
            TickIntervalSeconds = SysWire.ReadNullableLong(wire, "tick_interval_seconds"),
            ClientTokenSet = ReadBool(wire, "client_token_set"),
            BaseBackoffSeconds = SysWire.ReadNullableLong(wire, "base_backoff_seconds"),
            MaxBackoffSeconds = SysWire.ReadNullableLong(wire, "max_backoff_seconds"),
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

    private static string[]? ReadStringArrayOrNull(IReadOnlyDictionary<string, JsonElement> wire, string name)
    {
        if (!wire.TryGetValue(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToArray();
    }
}
