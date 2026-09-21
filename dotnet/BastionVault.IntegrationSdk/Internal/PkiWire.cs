using System.Text.Json;

namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// The wire reading, CSV-field handling and client-side rejection <see cref="PkiOperations"/>
/// shares (09 — PKI engine). One copy, so every binding reads a PEM field verbatim (PKI-001),
/// redacts a returned private key the same way (PKI-002), and joins/splits a CSV-typed field the
/// same way (PKI-010) — mirroring the <see cref="TransitWire"/>/<see cref="TotpWire"/> precedent
/// (D-M9-3).
/// </summary>
internal static class PkiWire
{
    /// <summary>PKI-010: joins a caller-supplied list into the wire's comma-separated string, or omits the field when the list is null.</summary>
    public static string? JoinCsv(IReadOnlyList<string>? values)
    {
        return values is null ? null : string.Join(',', values);
    }

    /// <summary>
    /// PKI-010: splits the wire's comma-separated string back into a list. F3 (M9 slice a
    /// handback): an <b>absent</b> field yields <see langword="null"/> — never <c>[]</c> — because
    /// <see cref="PkiRole"/> is patch-shaped (OVR-007) and a read-modify-write must be able to tell
    /// "the server never returned this field" from "the server returned it empty"; sending the
    /// former back as <c>[]</c> would clear a field the caller never touched. A field present but
    /// empty yields <c>[]</c>, and a non-empty field yields its split values.
    /// </summary>
    public static IReadOnlyList<string>? SplitCsv(IReadOnlyDictionary<string, JsonElement> wire, string name)
    {
        if (!wire.TryGetValue(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string text = value.GetString() ?? string.Empty;
        return text.Length == 0 ? [] : text.Split(',', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>PKI-011: <c>common_name</c> empty → <c>BV-INPUT-001</c> client-side, no request sent.</summary>
    public static void RequireCommonName(string commonName, string path)
    {
        if (string.IsNullOrEmpty(commonName))
        {
            throw KvWire.InvalidArgument("commonName", "must not be empty (PKI-011)", path);
        }
    }

    /// <summary>Writes a duration field in seconds, omitted entirely when absent (OVR-007).</summary>
    public static void WriteSeconds(Utf8JsonWriter writer, string name, TimeSpan? value)
    {
        if (value is { } duration)
        {
            writer.WriteNumber(name, (long)duration.TotalSeconds);
        }
    }

    /// <summary>Writes a CSV-typed field, omitted entirely when the list is absent (OVR-007, PKI-010).</summary>
    public static void WriteCsv(Utf8JsonWriter writer, string name, IReadOnlyList<string>? values)
    {
        if (JoinCsv(values) is { } joined)
        {
            writer.WriteString(name, joined);
        }
    }

    public static TimeSpan? ReadSeconds(IReadOnlyDictionary<string, JsonElement> wire, string name)
    {
        return wire.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? TimeSpan.FromSeconds(value.GetInt64())
            : null;
    }

    /// <summary>Serialises a <see cref="PkiRole"/> body (OVR-007: absent members are omitted, never sent empty).</summary>
    public static ReadOnlyMemory<byte> SerialiseRole(PkiRole role)
    {
        ArgumentNullException.ThrowIfNull(role);
        return KvWire.Serialise(writer =>
        {
            WriteSeconds(writer, "ttl", role.Ttl);
            WriteSeconds(writer, "max_ttl", role.MaxTtl);
            if (role.KeyType is { } keyType)
            {
                writer.WriteString("key_type", keyType);
            }

            if (role.KeyBits is { } keyBits)
            {
                writer.WriteNumber("key_bits", keyBits);
            }

            if (role.SignatureBits is { } signatureBits)
            {
                writer.WriteNumber("signature_bits", signatureBits);
            }

            WriteBool(writer, "allow_localhost", role.AllowLocalhost);
            WriteBool(writer, "allow_any_name", role.AllowAnyName);
            WriteBool(writer, "allow_ip_sans", role.AllowIpSans);
            WriteBool(writer, "allow_subdomains", role.AllowSubdomains);
            WriteBool(writer, "allow_bare_domains", role.AllowBareDomains);
            WriteCsv(writer, "allowed_domains", role.AllowedDomains);
            WriteBool(writer, "allow_glob_domains", role.AllowGlobDomains);
            WriteBool(writer, "server_flag", role.ServerFlag);
            WriteBool(writer, "client_flag", role.ClientFlag);
            WriteBool(writer, "use_csr_sans", role.UseCsrSans);
            WriteBool(writer, "use_csr_common_name", role.UseCsrCommonName);
            WriteCsv(writer, "key_usage", role.KeyUsage);
            WriteCsv(writer, "ext_key_usage", role.ExtKeyUsage);
            WriteCsv(writer, "ext_key_usage_oids", role.ExtKeyUsageOids);
            if (role.Country is { } country)
            {
                writer.WriteString("country", country);
            }

            if (role.Province is { } province)
            {
                writer.WriteString("province", province);
            }

            if (role.Locality is { } locality)
            {
                writer.WriteString("locality", locality);
            }

            if (role.Organization is { } organization)
            {
                writer.WriteString("organization", organization);
            }

            if (role.Ou is { } ou)
            {
                writer.WriteString("ou", ou);
            }

            WriteBool(writer, "no_store", role.NoStore);
            WriteBool(writer, "generate_lease", role.GenerateLease);
            WriteSeconds(writer, "not_before_duration", role.NotBeforeDuration);
            if (role.IssuerRef is { } issuerRef)
            {
                writer.WriteString("issuer_ref", issuerRef);
            }

            WriteBool(writer, "allow_key_reuse", role.AllowKeyReuse);
            WriteCsv(writer, "allowed_key_refs", role.AllowedKeyRefs);
            WriteBool(writer, "acme_enabled", role.AcmeEnabled);
            WriteBool(writer, "allow_upn_sans", role.AllowUpnSans);
            WriteCsv(writer, "allowed_upn_domains", role.AllowedUpnDomains);
            WriteBool(writer, "allow_email_sans", role.AllowEmailSans);
            WriteCsv(writer, "allowed_email_domains", role.AllowedEmailDomains);
            WriteBool(writer, "allow_ad_sid", role.AllowAdSid);
            if (role.AdSid is { } adSid)
            {
                writer.WriteString("ad_sid", adSid);
            }
        });
    }

    /// <summary>Reads a <see cref="PkiRole"/> back from the wire (09 §Roles and issuance's shared field list).</summary>
    public static PkiRole ReadRole(IReadOnlyDictionary<string, JsonElement> wire)
    {
        return new PkiRole
        {
            Ttl = ReadSeconds(wire, "ttl"),
            MaxTtl = ReadSeconds(wire, "max_ttl"),
            KeyType = KvWire.ReadString(wire, "key_type"),
            KeyBits = KvWire.ReadInt(wire, "key_bits"),
            SignatureBits = KvWire.ReadInt(wire, "signature_bits"),
            AllowLocalhost = ReadBool(wire, "allow_localhost"),
            AllowAnyName = ReadBool(wire, "allow_any_name"),
            AllowIpSans = ReadBool(wire, "allow_ip_sans"),
            AllowSubdomains = ReadBool(wire, "allow_subdomains"),
            AllowBareDomains = ReadBool(wire, "allow_bare_domains"),
            AllowedDomains = SplitCsv(wire, "allowed_domains"),
            AllowGlobDomains = ReadBool(wire, "allow_glob_domains"),
            ServerFlag = ReadBool(wire, "server_flag"),
            ClientFlag = ReadBool(wire, "client_flag"),
            UseCsrSans = ReadBool(wire, "use_csr_sans"),
            UseCsrCommonName = ReadBool(wire, "use_csr_common_name"),
            KeyUsage = SplitCsv(wire, "key_usage"),
            ExtKeyUsage = SplitCsv(wire, "ext_key_usage"),
            ExtKeyUsageOids = SplitCsv(wire, "ext_key_usage_oids"),
            Country = KvWire.ReadString(wire, "country"),
            Province = KvWire.ReadString(wire, "province"),
            Locality = KvWire.ReadString(wire, "locality"),
            Organization = KvWire.ReadString(wire, "organization"),
            Ou = KvWire.ReadString(wire, "ou"),
            NoStore = ReadBool(wire, "no_store"),
            GenerateLease = ReadBool(wire, "generate_lease"),
            NotBeforeDuration = ReadSeconds(wire, "not_before_duration"),
            IssuerRef = KvWire.ReadString(wire, "issuer_ref"),
            AllowKeyReuse = ReadBool(wire, "allow_key_reuse"),
            AllowedKeyRefs = SplitCsv(wire, "allowed_key_refs"),
            AcmeEnabled = ReadBool(wire, "acme_enabled"),
            AllowUpnSans = ReadBool(wire, "allow_upn_sans"),
            AllowedUpnDomains = SplitCsv(wire, "allowed_upn_domains"),
            AllowEmailSans = ReadBool(wire, "allow_email_sans"),
            AllowedEmailDomains = SplitCsv(wire, "allowed_email_domains"),
            AllowAdSid = ReadBool(wire, "allow_ad_sid"),
            AdSid = KvWire.ReadString(wire, "ad_sid"),
        };
    }

    /// <summary>PKI-001, PKI-002: <c>Pki.Issue</c>'s result, the PEM fields verbatim and the private key redacted.</summary>
    public static IssuedCertificate ReadIssuedCertificate(IReadOnlyDictionary<string, JsonElement> wire, string path)
    {
        return new IssuedCertificate
        {
            Certificate = KvWire.ReadString(wire, "certificate") ?? throw KvWire.EnvelopeMismatch(path, "certificate"),
            IssuingCa = KvWire.ReadString(wire, "issuing_ca") ?? throw KvWire.EnvelopeMismatch(path, "issuing_ca"),
            CaChain = KvWire.ReadStringList(wire, "ca_chain"),
            PrivateKey = KvWire.ReadString(wire, "private_key") is { } key ? new SecretString(key) : null,
            PrivateKeyType = KvWire.ReadString(wire, "private_key_type"),
            SerialNumber = KvWire.ReadString(wire, "serial_number") ?? throw KvWire.EnvelopeMismatch(path, "serial_number"),
            IssuerId = KvWire.ReadString(wire, "issuer_id") ?? throw KvWire.EnvelopeMismatch(path, "issuer_id"),
            KeyId = KvWire.ReadString(wire, "key_id"),
        };
    }

    /// <summary>PKI-001: <c>Pki.Sign</c>/<c>Pki.SignVerbatim</c>'s result, the PEM fields verbatim.</summary>
    public static SignedCertificate ReadSignedCertificate(IReadOnlyDictionary<string, JsonElement> wire, string path)
    {
        return new SignedCertificate
        {
            Certificate = KvWire.ReadString(wire, "certificate") ?? throw KvWire.EnvelopeMismatch(path, "certificate"),
            IssuingCa = KvWire.ReadString(wire, "issuing_ca") ?? throw KvWire.EnvelopeMismatch(path, "issuing_ca"),
            CaChain = KvWire.ReadStringList(wire, "ca_chain"),
            SerialNumber = KvWire.ReadString(wire, "serial_number") ?? throw KvWire.EnvelopeMismatch(path, "serial_number"),
            IssuerId = KvWire.ReadString(wire, "issuer_id") ?? throw KvWire.EnvelopeMismatch(path, "issuer_id"),
            KeyId = KvWire.ReadString(wire, "key_id"),
        };
    }

    /// <summary>PKI-001: <c>Pki.ReadCertificate</c>'s result, the PEM field verbatim.</summary>
    public static CertificateRecord ReadCertificateRecord(IReadOnlyDictionary<string, JsonElement> wire, string path)
    {
        return new CertificateRecord
        {
            Certificate = KvWire.ReadString(wire, "certificate") ?? throw KvWire.EnvelopeMismatch(path, "certificate"),
            SerialNumber = KvWire.ReadString(wire, "serial_number") ?? throw KvWire.EnvelopeMismatch(path, "serial_number"),
            IssuedAt = KvWire.RequireInstant(wire, "issued_at", path),
            NotAfter = KvWire.ReadOptionalInstant(wire, "not_after"),
            IssuerId = KvWire.ReadString(wire, "issuer_id"),
            IsOrphaned = wire.TryGetValue("is_orphaned", out JsonElement orphaned) && orphaned.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? orphaned.GetBoolean()
                : null,
            Source = KvWire.ReadString(wire, "source"),
            RevokedAt = KvWire.ReadOptionalInstant(wire, "revoked_at"),
            KeyId = KvWire.ReadString(wire, "key_id"),
            KeyName = KvWire.ReadString(wire, "key_name"),
        };
    }

    /// <summary>09 §Types: one row of <c>Pki.ListCertificatesInfo</c> (PAG-005).</summary>
    public static CertificateSummary ReadCertificateSummary(IReadOnlyDictionary<string, JsonElement> wire, string fallbackSerial, string path)
    {
        return new CertificateSummary
        {
            SerialNumber = KvWire.ReadString(wire, "serial_number") ?? fallbackSerial,
            IssuedAt = KvWire.RequireInstant(wire, "issued_at", path),
            RevokedAt = KvWire.ReadOptionalInstant(wire, "revoked_at"),
            NotAfter = KvWire.RequireInstant(wire, "not_after", path),
            IssuerId = KvWire.ReadString(wire, "issuer_id") ?? throw KvWire.EnvelopeMismatch(path, "issuer_id"),
            IsOrphaned = ReadBool(wire, "is_orphaned") ?? false,
            Source = KvWire.ReadString(wire, "source") ?? throw KvWire.EnvelopeMismatch(path, "source"),
            KeyId = KvWire.ReadString(wire, "key_id"),
            CommonName = KvWire.ReadString(wire, "common_name") ?? throw KvWire.EnvelopeMismatch(path, "common_name"),
            IssuerDn = KvWire.ReadString(wire, "issuer_dn") ?? throw KvWire.EnvelopeMismatch(path, "issuer_dn"),
        };
    }

    /// <summary>PKI-001: <c>Pki.ReadCrl</c>/<c>Pki.ReadIssuerCrl</c>'s result, the PEM field verbatim.</summary>
    public static Crl ReadCrl(IReadOnlyDictionary<string, JsonElement> wire, string path)
    {
        return new Crl
        {
            CrlPem = KvWire.ReadString(wire, "crl") ?? throw KvWire.EnvelopeMismatch(path, "crl"),
            // F4 (M9 slice a handback): 09 §Types marks no member of Crl optional, so an absent
            // crl_number is a protocol violation, not the number 0 — consistent with its neighbours.
            CrlNumber = KvWire.ReadInt(wire, "crl_number") ?? throw KvWire.EnvelopeMismatch(path, "crl_number"),
            IssuerId = KvWire.ReadString(wire, "issuer_id") ?? throw KvWire.EnvelopeMismatch(path, "issuer_id"),
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
}
