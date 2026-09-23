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

    /// <summary>
    /// TRN-031: writes a duration field in the Go-style string form ("mount <c>config</c>" fields
    /// quote their default — <c>09-pki-engine.md:48,82,84</c>) rather than integer seconds, omitted
    /// entirely when absent (OVR-007). Shares <see cref="GoDuration"/> with <c>KvV1Operations.cs:130</c>
    /// and <c>KvV2Operations.cs:569</c> rather than a second formatter.
    /// </summary>
    public static void WriteGoDuration(Utf8JsonWriter writer, string name, TimeSpan? value)
    {
        if (value is { } duration)
        {
            writer.WriteString(name, GoDuration.Format(duration));
        }
    }

    /// <summary>The read half of <see cref="WriteGoDuration"/>: a non-string, an unparsable string, and an absent field all yield <see langword="null"/> (never a silently-guessed default).</summary>
    public static TimeSpan? ReadGoDuration(IReadOnlyDictionary<string, JsonElement> wire, string name)
    {
        return GoDuration.TryParse(KvWire.ReadString(wire, name));
    }

    /// <summary>Serialises a <see cref="PkiRole"/> body (OVR-007: absent members are omitted, never sent empty).</summary>
    public static ReadOnlyMemory<byte> SerialiseRole(PkiRole role)
    {
        ArgumentNullException.ThrowIfNull(role);
        return KvWire.Serialise(writer =>
        {
            // TRN-031/09-pki-engine.md:48-68 (measured, DR-0021 F2): role ttl/max_ttl are one of
            // the six duration fields this engine requires as a Go-style string, not a number —
            // WriteGoDuration, not WriteSeconds, matching IssueAsync/SignAsync and the root/sign
            // paths below.
            WriteGoDuration(writer, "ttl", role.Ttl);
            WriteGoDuration(writer, "max_ttl", role.MaxTtl);
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
            // 09-pki-engine.md §Types (measured, DR-0021): bvault 0.44.5's certs-info row omits
            // both is_orphaned and source entirely. An absent field is not false / a protocol
            // violation — it reads as null, matching Crl.CrlNumber's precedent.
            IsOrphaned = ReadBool(wire, "is_orphaned"),
            Source = KvWire.ReadString(wire, "source"),
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
            // 09-pki-engine.md:105-115 (measured, DR-0021 second addendum, supersedes F4):
            // bvault 0.44.5 omits crl_number entirely. An absent field is not the number 0, so it
            // reads as null rather than throwing or being defaulted.
            CrlNumber = KvWire.ReadInt(wire, "crl_number"),
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

    // ================================================================ M9 slice b: CA lifecycle

    /// <summary>The wire's <c>internal</c>/<c>exported</c> path segment for <see cref="PkiKeyGenerationType"/>.</summary>
    public static string KeyGenerationSegment(PkiKeyGenerationType type)
    {
        return type switch
        {
            PkiKeyGenerationType.Internal => "internal",
            PkiKeyGenerationType.Exported => "exported",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "unrecognised PkiKeyGenerationType"),
        };
    }

    /// <summary>Writes a JSON array field, omitted entirely when the list is absent (OVR-007).</summary>
    public static void WriteStringArray(Utf8JsonWriter writer, string name, IReadOnlyList<string>? values)
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

    /// <summary>
    /// Reads a JSON array field back, distinguishing "absent" (<see langword="null"/>, F3's
    /// precedent) from "present but empty" (<c>[]</c>), the same distinction <see cref="SplitCsv"/>
    /// makes for a CSV-typed field — needed here so a patch-shaped read-modify-write (e.g.
    /// <see cref="PkiUrls"/>) cannot clear a field the server never returned.
    /// </summary>
    public static IReadOnlyList<string>? ReadStringArrayOrNull(IReadOnlyDictionary<string, JsonElement> wire, string name)
    {
        if (!wire.TryGetValue(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToArray();
    }

    /// <summary>Serialises a <see cref="PkiRootSpec"/> body (<c>09-pki-engine.md:42</c>).</summary>
    public static ReadOnlyMemory<byte> SerialiseRootSpec(PkiRootSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return KvWire.Serialise(writer =>
        {
            writer.WriteString("common_name", spec.CommonName);
            if (spec.Organization is { } organization)
            {
                writer.WriteString("organization", organization);
            }

            if (spec.KeyType is { } keyType)
            {
                writer.WriteString("key_type", keyType);
            }

            if (spec.KeyBits is { } keyBits)
            {
                writer.WriteNumber("key_bits", keyBits);
            }

            // TRN-031/09-pki-engine.md:48-68 (measured, DR-0021 F2): root/generate's ttl is one of
            // the six duration fields this engine requires as a Go-style string.
            WriteGoDuration(writer, "ttl", spec.Ttl);
            if (spec.IssuerName is { } issuerName)
            {
                writer.WriteString("issuer_name", issuerName);
            }

            if (spec.KeyRef is { } keyRef)
            {
                writer.WriteString("key_ref", keyRef);
            }
        });
    }

    /// <summary>Serialises a <see cref="PkiIntermediateSpec"/> body. See its own doc comment for the D-M9-17 transcription this shape follows.</summary>
    public static ReadOnlyMemory<byte> SerialiseIntermediateSpec(PkiIntermediateSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return KvWire.Serialise(writer =>
        {
            writer.WriteString("common_name", spec.CommonName);
            if (spec.Organization is { } organization)
            {
                writer.WriteString("organization", organization);
            }

            if (spec.KeyType is { } keyType)
            {
                writer.WriteString("key_type", keyType);
            }

            if (spec.KeyBits is { } keyBits)
            {
                writer.WriteNumber("key_bits", keyBits);
            }

            WriteSeconds(writer, "ttl", spec.Ttl);
            if (spec.IssuerName is { } issuerName)
            {
                writer.WriteString("issuer_name", issuerName);
            }

            if (spec.KeyRef is { } keyRef)
            {
                writer.WriteString("key_ref", keyRef);
            }
        });
    }

    /// <summary>PKI-001, PKI-002: <c>Pki.GenerateRoot</c>'s result, transcribed from <c>09-pki-engine.md:42</c>.</summary>
    public static PkiRootCertificate ReadRootCertificate(IReadOnlyDictionary<string, JsonElement> wire, string path)
    {
        return new PkiRootCertificate
        {
            Certificate = KvWire.ReadString(wire, "certificate") ?? throw KvWire.EnvelopeMismatch(path, "certificate"),
            IssuingCa = KvWire.ReadString(wire, "issuing_ca") ?? throw KvWire.EnvelopeMismatch(path, "issuing_ca"),
            IssuerId = KvWire.ReadString(wire, "issuer_id") ?? throw KvWire.EnvelopeMismatch(path, "issuer_id"),
            IssuerName = KvWire.ReadString(wire, "issuer_name") ?? throw KvWire.EnvelopeMismatch(path, "issuer_name"),
            Expiration = KvWire.RequireInstant(wire, "expiration", path),
            PrivateKey = KvWire.ReadString(wire, "private_key") is { } key ? new SecretString(key) : null,
            PrivateKeyType = KvWire.ReadString(wire, "private_key_type"),
            KeyId = KvWire.ReadString(wire, "key_id"),
        };
    }

    /// <summary>Serialises a <see cref="SignIntermediateRequest"/> body (<c>09-pki-engine.md:43</c>).</summary>
    public static ReadOnlyMemory<byte> SerialiseSignIntermediate(SignIntermediateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return KvWire.Serialise(writer =>
        {
            writer.WriteString("csr", request.Csr);
            if (request.CommonName is { } commonName)
            {
                writer.WriteString("common_name", commonName);
            }

            if (request.Organization is { } organization)
            {
                writer.WriteString("organization", organization);
            }

            // TRN-031/09-pki-engine.md:48-68 (measured, DR-0021 F2): root/sign-intermediate's ttl
            // is one of the six duration fields this engine requires as a Go-style string.
            WriteGoDuration(writer, "ttl", request.Ttl);
            if (request.MaxPathLength is { } maxPathLength)
            {
                writer.WriteNumber("max_path_length", maxPathLength);
            }

            if (request.IssuerRef is { } issuerRef)
            {
                writer.WriteString("issuer_ref", issuerRef);
            }
        });
    }

    /// <summary>PKI-001: <c>Pki.SignIntermediate</c>'s result, transcribed from <c>09-pki-engine.md:43</c>.</summary>
    public static SignedIntermediateCertificate ReadSignedIntermediateCertificate(IReadOnlyDictionary<string, JsonElement> wire, string path)
    {
        return new SignedIntermediateCertificate
        {
            Certificate = KvWire.ReadString(wire, "certificate") ?? throw KvWire.EnvelopeMismatch(path, "certificate"),
            IssuingCa = KvWire.ReadString(wire, "issuing_ca") ?? throw KvWire.EnvelopeMismatch(path, "issuing_ca"),
        };
    }

    /// <summary>PKI-001, PKI-002: <c>Pki.GenerateIntermediate</c>'s result, transcribed from <c>09-pki-engine.md:44</c>.</summary>
    public static PkiIntermediateCsr ReadIntermediateCsr(IReadOnlyDictionary<string, JsonElement> wire, string path)
    {
        return new PkiIntermediateCsr
        {
            Csr = KvWire.ReadString(wire, "csr") ?? throw KvWire.EnvelopeMismatch(path, "csr"),
            KeyId = KvWire.ReadString(wire, "key_id"),
            PrivateKey = KvWire.ReadString(wire, "private_key") is { } key ? new SecretString(key) : null,
            PrivateKeyType = KvWire.ReadString(wire, "private_key_type"),
        };
    }

    /// <summary>Serialises a <see cref="PkiUrls"/> body (<c>09-pki-engine.md:47</c>).</summary>
    public static ReadOnlyMemory<byte> SerialiseUrls(PkiUrls urls)
    {
        ArgumentNullException.ThrowIfNull(urls);
        return KvWire.Serialise(writer =>
        {
            WriteStringArray(writer, "issuing_certificates", urls.IssuingCertificates);
            WriteStringArray(writer, "crl_distribution_points", urls.CrlDistributionPoints);
            WriteStringArray(writer, "ocsp_servers", urls.OcspServers);
        });
    }

    /// <summary>Reads a <see cref="PkiUrls"/> body back (<c>09-pki-engine.md:47</c>, patch-shaped like <see cref="PkiRole"/>).</summary>
    public static PkiUrls ReadUrls(IReadOnlyDictionary<string, JsonElement> wire)
    {
        return new PkiUrls
        {
            IssuingCertificates = ReadStringArrayOrNull(wire, "issuing_certificates"),
            CrlDistributionPoints = ReadStringArrayOrNull(wire, "crl_distribution_points"),
            OcspServers = ReadStringArrayOrNull(wire, "ocsp_servers"),
        };
    }

    /// <summary>Serialises a <see cref="PkiCrlConfig"/> body (<c>09-pki-engine.md:48</c>).</summary>
    public static ReadOnlyMemory<byte> SerialiseCrlConfig(PkiCrlConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return KvWire.Serialise(writer =>
        {
            WriteGoDuration(writer, "expiry", config.Expiry);
            WriteBool(writer, "disable", config.Disable);
        });
    }

    /// <summary>Reads a <see cref="PkiCrlConfig"/> body back (<c>09-pki-engine.md:48</c>).</summary>
    public static PkiCrlConfig ReadCrlConfig(IReadOnlyDictionary<string, JsonElement> wire)
    {
        return new PkiCrlConfig
        {
            Expiry = ReadGoDuration(wire, "expiry"),
            Disable = ReadBool(wire, "disable"),
        };
    }

    /// <summary>Serialises a <see cref="PkiIssuersConfig"/> body (<c>09-pki-engine.md:49</c>).</summary>
    public static ReadOnlyMemory<byte> SerialiseIssuersConfig(PkiIssuersConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return KvWire.Serialise(writer =>
        {
            if (config.Default is { } defaultRef)
            {
                writer.WriteString("default", defaultRef);
            }
        });
    }

    /// <summary>Reads a <see cref="PkiIssuersConfig"/> body back (<c>09-pki-engine.md:49</c>).</summary>
    public static PkiIssuersConfig ReadIssuersConfig(IReadOnlyDictionary<string, JsonElement> wire)
    {
        return new PkiIssuersConfig { Default = KvWire.ReadString(wire, "default") };
    }

    /// <summary>Serialises a <see cref="PkiIssuerWrite"/> body (<c>09-pki-engine.md:50</c>).</summary>
    public static ReadOnlyMemory<byte> SerialiseIssuerWrite(PkiIssuerWrite issuer)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        return KvWire.Serialise(writer =>
        {
            if (issuer.IssuerName is { } issuerName)
            {
                writer.WriteString("issuer_name", issuerName);
            }

            WriteStringArray(writer, "usage", issuer.Usage);
        });
    }

    /// <summary>PKI-001: <c>Pki.SetSignedIntermediate</c>'s result, transcribed from <c>09-pki-engine.md:45</c>.</summary>
    public static SetSignedIntermediateResult ReadSetSignedIntermediateResult(IReadOnlyDictionary<string, JsonElement> wire, string path)
    {
        return new SetSignedIntermediateResult
        {
            ImportedIssuers = KvWire.ReadStringList(wire, "imported_issuers"),
            ImportedKeys = KvWire.ReadStringList(wire, "imported_keys"),
            IssuerId = KvWire.ReadString(wire, "issuer_id") ?? throw KvWire.EnvelopeMismatch(path, "issuer_id"),
            IssuerName = KvWire.ReadString(wire, "issuer_name") ?? throw KvWire.EnvelopeMismatch(path, "issuer_name"),
        };
    }

    /// <summary>Serialises a <see cref="PkiTidyOptions"/> body (<c>09-pki-engine.md:82</c>).</summary>
    public static ReadOnlyMemory<byte> SerialiseTidyOptions(PkiTidyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return KvWire.Serialise(writer =>
        {
            WriteBool(writer, "tidy_cert_store", options.TidyCertStore);
            WriteBool(writer, "tidy_revoked_certs", options.TidyRevokedCerts);
            WriteGoDuration(writer, "safety_buffer", options.SafetyBuffer);
        });
    }

    /// <summary>Serialises a <see cref="PkiAutoTidyConfig"/> body (<c>09-pki-engine.md:84</c>).</summary>
    public static ReadOnlyMemory<byte> SerialiseAutoTidyConfig(PkiAutoTidyConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return KvWire.Serialise(writer =>
        {
            WriteBool(writer, "enabled", config.Enabled);
            WriteGoDuration(writer, "interval", config.Interval);
        });
    }

    /// <summary>Reads a <see cref="PkiAutoTidyConfig"/> body back (<c>09-pki-engine.md:84</c>).</summary>
    public static PkiAutoTidyConfig ReadAutoTidyConfig(IReadOnlyDictionary<string, JsonElement> wire)
    {
        return new PkiAutoTidyConfig
        {
            Enabled = ReadBool(wire, "enabled"),
            Interval = ReadGoDuration(wire, "interval"),
        };
    }

    /// <summary>Serialises a <see cref="PkiAcmeConfig"/> body (<c>09-pki-engine.md:112-114</c>).</summary>
    public static ReadOnlyMemory<byte> SerialiseAcmeConfig(PkiAcmeConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return KvWire.Serialise(writer =>
        {
            WriteBool(writer, "enabled", config.Enabled);
            if (config.DefaultRole is { } defaultRole)
            {
                writer.WriteString("default_role", defaultRole);
            }

            if (config.DefaultIssuerRef is { } defaultIssuerRef)
            {
                writer.WriteString("default_issuer_ref", defaultIssuerRef);
            }

            if (config.ExternalHostname is { } externalHostname)
            {
                writer.WriteString("external_hostname", externalHostname);
            }

            if (config.NonceTtlSecs is { } nonceTtlSecs)
            {
                writer.WriteNumber("nonce_ttl_secs", nonceTtlSecs);
            }

            WriteStringArray(writer, "dns_resolvers", config.DnsResolvers);
            WriteBool(writer, "eab_required", config.EabRequired);
            if (config.RateWindowSecs is { } rateWindowSecs)
            {
                writer.WriteNumber("rate_window_secs", rateWindowSecs);
            }
            if (config.RateOrdersPerWindow is { } rateOrdersPerWindow)
            {
                writer.WriteNumber("rate_orders_per_window", rateOrdersPerWindow);
            }
        });
    }

    /// <summary>Reads a <see cref="PkiAcmeConfig"/> body back (<c>09-pki-engine.md:112-114</c>).</summary>
    public static PkiAcmeConfig ReadAcmeConfig(IReadOnlyDictionary<string, JsonElement> wire)
    {
        return new PkiAcmeConfig
        {
            Enabled = ReadBool(wire, "enabled"),
            DefaultRole = KvWire.ReadString(wire, "default_role"),
            DefaultIssuerRef = KvWire.ReadString(wire, "default_issuer_ref"),
            ExternalHostname = KvWire.ReadString(wire, "external_hostname"),
            NonceTtlSecs = SysWire.ReadNullableLong(wire, "nonce_ttl_secs"),
            DnsResolvers = ReadStringArrayOrNull(wire, "dns_resolvers"),
            EabRequired = ReadBool(wire, "eab_required"),
            RateWindowSecs = SysWire.ReadNullableLong(wire, "rate_window_secs"),
            RateOrdersPerWindow = KvWire.ReadInt(wire, "rate_orders_per_window"),
        };
    }

    // ============================================================================ M9 slice c: outbound CSR / inbound sign-request queues

    /// <summary>PKI-030: an empty or whitespace-only <c>reason</c> fails client-side (<c>BV-INPUT-001</c>), no request sent. The second limb (queue-cap → <c>BV-QUOTA-002</c>) stays baselined under D-M9-11 — no document states the server's message, so it is not implemented here.</summary>
    public static void RequireReason(string reason, string path)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw KvWire.InvalidArgument("reason", "must not be empty or whitespace (PKI-030)", path);
        }
    }

    /// <summary>PKI-001, PKI-002: <c>Pki.Csr.Generate</c>'s result, transcribed from the sibling shape 09-pki-engine.md:44 defines for <c>Pki.GenerateIntermediate</c> (D-M9-10's whole-set transcription, D-M9-17). See <see cref="PkiGeneratedCsr"/>.</summary>
    public static PkiGeneratedCsr ReadGeneratedCsr(IReadOnlyDictionary<string, JsonElement> wire, string path)
    {
        return new PkiGeneratedCsr
        {
            Csr = KvWire.ReadString(wire, "csr") ?? throw KvWire.EnvelopeMismatch(path, "csr"),
            KeyId = KvWire.ReadString(wire, "key_id"),
            PrivateKey = KvWire.ReadString(wire, "private_key") is { } key ? new SecretString(key) : null,
            PrivateKeyType = KvWire.ReadString(wire, "private_key_type"),
        };
    }

    /// <summary>
    /// D-M9-10/D-M9-21: reads a <c>*-info</c> page whose records have no defined shape, so each record
    /// surfaces as the raw wire map rather than a guessed type. Shared by <c>Pki.Csr.ListInfo</c>
    /// (<c>csr-info</c>) and <c>Pki.SignRequests.ListInfo</c> (<c>sign-request-info</c>) — the same
    /// keys/records zip <see cref="PkiOperations.ListCertificatesInfoAsync"/> performs against
    /// <see cref="CertificateSummary"/>, but against <see cref="SysWire.AsMap(JsonElement)"/> instead
    /// of a typed reader. See R-31.
    /// </summary>
    public static Page<IReadOnlyDictionary<string, JsonElement>> ReadRawInfoPage(IReadOnlyDictionary<string, JsonElement> data, string path)
    {
        IReadOnlyList<string> keys = SysWire.ReadKeys(data);

        // D-M9-30/PAG-005: same ordering as PkiOperations.ListCertificatesInfoAsync — the
        // structural length check runs before any per-record decode, so a short or long records
        // array is reported as the length mismatch it is rather than whatever the loop trips over
        // first. No fixture drives this path yet, but the latent defect is the same shape.
        bool hasRecordsArray = data.TryGetValue("records", out JsonElement recordsElement) && recordsElement.ValueKind == JsonValueKind.Array;
        int recordCount = hasRecordsArray ? recordsElement.GetArrayLength() : 0;
        if (recordCount != keys.Count)
        {
            throw KvWire.EnvelopeMismatch(path, "records");
        }

        List<IReadOnlyDictionary<string, JsonElement>> records = [];
        if (hasRecordsArray)
        {
            foreach (JsonElement record in recordsElement.EnumerateArray())
            {
                records.Add(record.ValueKind == JsonValueKind.Object
                    ? SysWire.AsMap(record)
                    : throw KvWire.EnvelopeMismatch(path, "records[]"));
            }
        }

        string? next = SysWire.ReadString(data, "next");
        return new Page<IReadOnlyDictionary<string, JsonElement>>
        {
            Keys = keys,
            Records = records,
            Total = SysWire.ReadNullableLong(data, "total") is { } total ? (int)total : keys.Count,
            Next = string.IsNullOrEmpty(next) ? null : next,
            Truncated = data.TryGetValue("truncated", out JsonElement truncated) && truncated.ValueKind == JsonValueKind.True,
        };
    }

    /// <summary>
    /// D-M9-19/D-M9-20's request-body-only rule, applied to <c>Pki.SignRequests.Approve</c>'s
    /// <c>overrides</c> parameter: 09-pki-engine.md:103 names the parameter but no field list for it.
    /// <c>Pki.Sign</c>'s sibling row (<c>09-pki-engine.md:30</c>, "csr (required) + overrides") settles
    /// the <b>placement</b> — <c>SignAsync</c> writes its override fields flat into the same object as
    /// <c>csr</c>, which is the precedent this follows for writing flat rather than nesting under an
    /// invented <c>overrides</c> key — but it does not settle the <b>field set</b>: D-M9-17 could
    /// transcribe <see cref="IssueRequest"/>'s complete set there because 09 signals a superset for
    /// that very operation, and no document does the same here, so no field set is invented
    /// (D-M1c-25, D-M9-10's symmetric request-side reasoning). D-M9-24 accepts this design subject to
    /// <paramref name="reservedKeys"/>'s guard. See R-31.
    /// </summary>
    /// <remarks>
    /// RF-1 (M9 slice c handback): <see cref="Utf8JsonWriter"/> does not reject a duplicate property
    /// name, and a typical server-side JSON parser (<c>serde_json</c>, Go's <c>encoding/json</c>)
    /// takes the <b>last</b> occurrence — so an unchecked <paramref name="map"/> entry named
    /// <c>role</c> would silently outrank the caller's named <paramref name="reservedKeys"/> argument
    /// on the very route that authorises a certificate issuance. Every reserved key is checked against
    /// <paramref name="map"/> before anything is written, client-side, before dispatch, raising
    /// <c>BV-INPUT-001</c> on the first collision found.
    /// </remarks>
    public static void WriteFlatMap(Utf8JsonWriter writer, IReadOnlyDictionary<string, JsonElement>? map, IReadOnlyCollection<string> reservedKeys, string path)
    {
        if (map is null)
        {
            return;
        }

        foreach (string reserved in reservedKeys)
        {
            if (map.ContainsKey(reserved))
            {
                throw KvWire.InvalidArgument("overrides", $"must not override the '{reserved}' field", path);
            }
        }

        foreach ((string key, JsonElement value) in map)
        {
            writer.WritePropertyName(key);
            value.WriteTo(writer);
        }
    }
}
