namespace BastionVault.IntegrationSdk;

/// <summary>
/// 09 §Roles and issuance: the field list <c>Pki.WriteRole</c> sends and <c>Pki.ReadRole</c>
/// returns. Every member is nullable and <see langword="null"/> is omitted from the request body
/// (OVR-007) — the same patch-shaped convention <see cref="TransitKeyOptions"/> and
/// <see cref="TransitKeyConfig"/> already establish — because 09 names no separate read shape for
/// a role, only one field list shared by both directions.
/// </summary>
public sealed class PkiRole
{
    /// <summary>The wire <c>ttl</c> field, in seconds.</summary>
    public TimeSpan? Ttl { get; init; }

    /// <summary>The wire <c>max_ttl</c> field, in seconds.</summary>
    public TimeSpan? MaxTtl { get; init; }

    /// <summary>The wire <c>key_type</c> field. Server default <c>ec</c> when omitted.</summary>
    public string? KeyType { get; init; }

    /// <summary>The wire <c>key_bits</c> field.</summary>
    public int? KeyBits { get; init; }

    /// <summary>The wire <c>signature_bits</c> field.</summary>
    public int? SignatureBits { get; init; }

    /// <summary>The wire <c>allow_localhost</c> field. Server default <see langword="true"/> when omitted.</summary>
    public bool? AllowLocalhost { get; init; }

    /// <summary>The wire <c>allow_any_name</c> field. Server default <see langword="true"/> when omitted.</summary>
    public bool? AllowAnyName { get; init; }

    /// <summary>The wire <c>allow_ip_sans</c> field. Server default <see langword="true"/> when omitted.</summary>
    public bool? AllowIpSans { get; init; }

    /// <summary>The wire <c>allow_subdomains</c> field.</summary>
    public bool? AllowSubdomains { get; init; }

    /// <summary>The wire <c>allow_bare_domains</c> field.</summary>
    public bool? AllowBareDomains { get; init; }

    /// <summary>PKI-010: the wire's CSV-typed <c>allowed_domains</c>, accepted and returned as a list.</summary>
    public IReadOnlyList<string>? AllowedDomains { get; init; }

    /// <summary>The wire <c>allow_glob_domains</c> field.</summary>
    public bool? AllowGlobDomains { get; init; }

    /// <summary>The wire <c>server_flag</c> field. Server default <see langword="true"/> when omitted.</summary>
    public bool? ServerFlag { get; init; }

    /// <summary>The wire <c>client_flag</c> field. Server default <see langword="true"/> when omitted.</summary>
    public bool? ClientFlag { get; init; }

    /// <summary>The wire <c>use_csr_sans</c> field. Server default <see langword="true"/> when omitted.</summary>
    public bool? UseCsrSans { get; init; }

    /// <summary>The wire <c>use_csr_common_name</c> field. Server default <see langword="true"/> when omitted.</summary>
    public bool? UseCsrCommonName { get; init; }

    /// <summary>PKI-010: the wire's CSV-typed <c>key_usage</c>. Server default <c>DigitalSignature,KeyEncipherment</c> when omitted.</summary>
    public IReadOnlyList<string>? KeyUsage { get; init; }

    /// <summary>PKI-010: the wire's CSV-typed <c>ext_key_usage</c>.</summary>
    public IReadOnlyList<string>? ExtKeyUsage { get; init; }

    /// <summary>PKI-010: the wire's CSV-typed <c>ext_key_usage_oids</c>.</summary>
    public IReadOnlyList<string>? ExtKeyUsageOids { get; init; }

    /// <summary>The wire <c>country</c> field.</summary>
    public string? Country { get; init; }

    /// <summary>The wire <c>province</c> field.</summary>
    public string? Province { get; init; }

    /// <summary>The wire <c>locality</c> field.</summary>
    public string? Locality { get; init; }

    /// <summary>The wire <c>organization</c> field.</summary>
    public string? Organization { get; init; }

    /// <summary>The wire <c>ou</c> field.</summary>
    public string? Ou { get; init; }

    /// <summary>The wire <c>no_store</c> field.</summary>
    public bool? NoStore { get; init; }

    /// <summary>The wire <c>generate_lease</c> field.</summary>
    public bool? GenerateLease { get; init; }

    /// <summary>The wire <c>not_before_duration</c> field, in seconds. Server default 30 when omitted.</summary>
    public TimeSpan? NotBeforeDuration { get; init; }

    /// <summary>The wire <c>issuer_ref</c> field.</summary>
    public string? IssuerRef { get; init; }

    /// <summary>The wire <c>allow_key_reuse</c> field.</summary>
    public bool? AllowKeyReuse { get; init; }

    /// <summary>PKI-010: the wire's CSV-typed <c>allowed_key_refs</c>.</summary>
    public IReadOnlyList<string>? AllowedKeyRefs { get; init; }

    /// <summary>The wire <c>acme_enabled</c> field. Server default <see langword="true"/> when omitted.</summary>
    public bool? AcmeEnabled { get; init; }

    /// <summary>The wire <c>allow_upn_sans</c> field.</summary>
    public bool? AllowUpnSans { get; init; }

    /// <summary>PKI-010: the wire's CSV-typed <c>allowed_upn_domains</c>.</summary>
    public IReadOnlyList<string>? AllowedUpnDomains { get; init; }

    /// <summary>The wire <c>allow_email_sans</c> field.</summary>
    public bool? AllowEmailSans { get; init; }

    /// <summary>PKI-010: the wire's CSV-typed <c>allowed_email_domains</c>.</summary>
    public IReadOnlyList<string>? AllowedEmailDomains { get; init; }

    /// <summary>The wire <c>allow_ad_sid</c> field.</summary>
    public bool? AllowAdSid { get; init; }

    /// <summary>The wire <c>ad_sid</c> field.</summary>
    public string? AdSid { get; init; }
}

/// <summary>09 §Roles and issuance: <c>Pki.Issue</c>'s request body.</summary>
public sealed class IssueRequest
{
    /// <summary>The wire <c>common_name</c> field. PKI-011: must not be empty; refused client-side (<c>BV-INPUT-001</c>) before any request is sent.</summary>
    public required string CommonName { get; init; }

    /// <summary>PKI-010: the wire's CSV-typed <c>alt_names</c>.</summary>
    public IReadOnlyList<string>? AltNames { get; init; }

    /// <summary>PKI-010: the wire's CSV-typed <c>ip_sans</c>.</summary>
    public IReadOnlyList<string>? IpSans { get; init; }

    /// <summary>The wire <c>ttl</c> field, in seconds.</summary>
    public TimeSpan? Ttl { get; init; }

    /// <summary>The wire <c>issuer_ref</c> field.</summary>
    public string? IssuerRef { get; init; }

    /// <summary>The wire <c>key_ref</c> field.</summary>
    public string? KeyRef { get; init; }

    /// <summary>PKI-010: the wire's CSV-typed <c>upn_sans</c>.</summary>
    public IReadOnlyList<string>? UpnSans { get; init; }

    /// <summary>PKI-010: the wire's CSV-typed <c>email_sans</c>.</summary>
    public IReadOnlyList<string>? EmailSans { get; init; }

    /// <summary>The wire <c>ad_sid</c> field.</summary>
    public string? AdSid { get; init; }
}

/// <summary>
/// 09 §Roles and issuance: <c>Pki.Sign</c>'s request body — a CSR plus overrides. D-M9-17 (M9
/// slice a handback): 09 names the overrides generically ("<c>csr</c> (required) + overrides"),
/// with no list of its own, so this carries <see cref="IssueRequest"/>'s <b>complete</b> named
/// set — reusing a sibling's named fields is transcription only when the whole set is taken; a
/// subset is an unrecorded selection of public API fields. Without <see cref="KeyRef"/> and the
/// UPN/email/AD-SID overrides, a caller signing against a role with <c>allow_email_sans</c> or
/// <c>allow_upn_sans</c> could not express those SANs at all.
/// </summary>
public sealed class SignRequest
{
    /// <summary>The wire <c>csr</c> field. Required; refused client-side when empty.</summary>
    public required string Csr { get; init; }

    /// <summary>The wire <c>common_name</c> override.</summary>
    public string? CommonName { get; init; }

    /// <summary>PKI-010: the wire's CSV-typed <c>alt_names</c> override.</summary>
    public IReadOnlyList<string>? AltNames { get; init; }

    /// <summary>PKI-010: the wire's CSV-typed <c>ip_sans</c> override.</summary>
    public IReadOnlyList<string>? IpSans { get; init; }

    /// <summary>The wire <c>ttl</c> override, in seconds.</summary>
    public TimeSpan? Ttl { get; init; }

    /// <summary>The wire <c>issuer_ref</c> override.</summary>
    public string? IssuerRef { get; init; }

    /// <summary>The wire <c>key_ref</c> override.</summary>
    public string? KeyRef { get; init; }

    /// <summary>PKI-010: the wire's CSV-typed <c>upn_sans</c> override.</summary>
    public IReadOnlyList<string>? UpnSans { get; init; }

    /// <summary>PKI-010: the wire's CSV-typed <c>email_sans</c> override.</summary>
    public IReadOnlyList<string>? EmailSans { get; init; }

    /// <summary>The wire <c>ad_sid</c> override.</summary>
    public string? AdSid { get; init; }
}

/// <summary>09 §Types: <c>Pki.Issue</c>'s result.</summary>
public sealed class IssuedCertificate
{
    /// <summary>The issued certificate, PEM-encoded verbatim (PKI-001).</summary>
    public required string Certificate { get; init; }

    /// <summary>The issuing CA certificate, PEM-encoded verbatim (PKI-001).</summary>
    public required string IssuingCa { get; init; }

    /// <summary>The full CA chain, PEM-encoded verbatim (PKI-001).</summary>
    public required IReadOnlyList<string> CaChain { get; init; }

    /// <summary>PKI-002: the generated private key, present only when the server generated one, held in a redacting type.</summary>
    public SecretString? PrivateKey { get; init; }

    /// <summary>The private key's type, present alongside <see cref="PrivateKey"/>.</summary>
    public string? PrivateKeyType { get; init; }

    /// <summary>PKI-020: the issued certificate's serial number, exactly as the server returned it.</summary>
    public required string SerialNumber { get; init; }

    /// <summary>The issuer that signed this certificate.</summary>
    public required string IssuerId { get; init; }

    /// <summary>The managed key used, when one was.</summary>
    public string? KeyId { get; init; }
}

/// <summary>09 §Types: <c>Pki.Sign</c>'s result.</summary>
public sealed class SignedCertificate
{
    /// <summary>The signed certificate, PEM-encoded verbatim (PKI-001).</summary>
    public required string Certificate { get; init; }

    /// <summary>The issuing CA certificate, PEM-encoded verbatim (PKI-001).</summary>
    public required string IssuingCa { get; init; }

    /// <summary>The full CA chain, PEM-encoded verbatim (PKI-001).</summary>
    public required IReadOnlyList<string> CaChain { get; init; }

    /// <summary>PKI-020: the signed certificate's serial number, exactly as the server returned it.</summary>
    public required string SerialNumber { get; init; }

    /// <summary>The issuer that signed this certificate.</summary>
    public required string IssuerId { get; init; }

    /// <summary>The managed key used, when one was.</summary>
    public string? KeyId { get; init; }
}

/// <summary>09 §Types: <c>Pki.ReadCertificate</c>'s result.</summary>
public sealed class CertificateRecord
{
    /// <summary>The stored certificate, PEM-encoded verbatim (PKI-001).</summary>
    public required string Certificate { get; init; }

    /// <summary>PKI-020: the certificate's serial number, exactly as the server returned it.</summary>
    public required string SerialNumber { get; init; }

    /// <summary>When the certificate was issued.</summary>
    public required DateTimeOffset IssuedAt { get; init; }

    /// <summary>The certificate's expiry, when the server reported one.</summary>
    public DateTimeOffset? NotAfter { get; init; }

    /// <summary>The issuer that signed this certificate, when the server reported one.</summary>
    public string? IssuerId { get; init; }

    /// <summary>Whether this certificate has no matching issuer on record.</summary>
    public bool? IsOrphaned { get; init; }

    /// <summary>How this certificate entered the store (e.g. <c>issued</c>, <c>imported</c>).</summary>
    public string? Source { get; init; }

    /// <summary>When the certificate was revoked, when it has been.</summary>
    public DateTimeOffset? RevokedAt { get; init; }

    /// <summary>The managed key attached to this certificate, when one is.</summary>
    public string? KeyId { get; init; }

    /// <summary>The managed key's name, alongside <see cref="KeyId"/>.</summary>
    public string? KeyName { get; init; }
}

/// <summary>09 §Types: one row of <c>Pki.ListCertificatesInfo</c> (14 — batch and request efficiency).</summary>
public sealed class CertificateSummary
{
    /// <summary>PKI-020: the certificate's serial number, exactly as the server returned it.</summary>
    public required string SerialNumber { get; init; }

    /// <summary>When the certificate was issued.</summary>
    public required DateTimeOffset IssuedAt { get; init; }

    /// <summary>When the certificate was revoked, when it has been.</summary>
    public DateTimeOffset? RevokedAt { get; init; }

    /// <summary>The certificate's expiry.</summary>
    public required DateTimeOffset NotAfter { get; init; }

    /// <summary>The issuer that signed this certificate.</summary>
    public required string IssuerId { get; init; }

    /// <summary>Whether this certificate has no matching issuer on record.</summary>
    public required bool IsOrphaned { get; init; }

    /// <summary>How this certificate entered the store (e.g. <c>issued</c>, <c>imported</c>).</summary>
    public required string Source { get; init; }

    /// <summary>The managed key attached to this certificate, when one is.</summary>
    public string? KeyId { get; init; }

    /// <summary>The certificate's subject common name.</summary>
    public required string CommonName { get; init; }

    /// <summary>The issuer's distinguished name.</summary>
    public required string IssuerDn { get; init; }
}

/// <summary>09 §Types: <c>Pki.ReadCrl</c> and <c>Pki.ReadIssuerCrl</c>'s result.</summary>
public sealed class Crl
{
    /// <summary>
    /// The CRL, PEM-encoded verbatim (PKI-001). 09 §Types names the wire field <c>Crl</c>, which
    /// C# refuses as a member name identical to its containing type (CS0542); <c>CrlPem</c> is the
    /// smallest deviation that still reads as the same field.
    /// </summary>
    public required string CrlPem { get; init; }

    /// <summary>The CRL's sequence number.</summary>
    public required int CrlNumber { get; init; }

    /// <summary>The issuer this CRL belongs to.</summary>
    public required string IssuerId { get; init; }
}

/// <summary>
/// D-M9-16: <c>Pki.ExportCertificate</c>'s result. 09 defines no response shape for
/// <c>{mount}/cert/{serial}/export</c>, and the route's own <c>includePrivateKey</c>/<c>mode</c>
/// parameters establish that its response may carry key material (PKI-002) — unlike
/// <c>Pki.ExportIssuer</c>, which never exports a private key (09 §CA lifecycle), there is no
/// sibling shape to transcribe here. This wraps the whole response body, verbatim (PKI-001), in a
/// redacting type, rather than surfacing it through an untyped, unredacted map. It asserts nothing
/// about the payload's internal structure — only that it may be sensitive, which the route's own
/// parameters already establish. See R-31 for the typed shape this is a placeholder for.
/// </summary>
public sealed class PkiCertificateExport
{
    /// <summary>The response body verbatim (PKI-001), redacted (PKI-002) so it never reaches a log by accident.</summary>
    public required SecretString Payload { get; init; }
}

// ============================================================================ M9 slice b: CA lifecycle

/// <summary>
/// 09 §CA lifecycle: the closed <c>internal</c>/<c>exported</c> choice on the wire's
/// <c>{mount}/root/generate/{internal|exported}</c>, <c>{mount}/intermediate/generate/{internal|exported}</c>
/// and <c>{mount}/keys/generate/{internal|exported}</c> path segment. A C# enum rather than a free
/// string, following <see cref="TotpAlgorithm"/>'s precedent (<c>TotpTypes.cs:9</c>): the
/// specification names exactly these two path forms and no others.
/// </summary>
public enum PkiKeyGenerationType
{
    /// <summary>The wire's <c>internal</c> path segment: the private key never leaves the server.</summary>
    Internal,

    /// <summary>The wire's <c>exported</c> path segment: the response may carry the generated private key (PKI-002).</summary>
    Exported,
}

/// <summary>09 §CA lifecycle: <c>Pki.GenerateRoot</c>'s request body (<c>{mount}/root/generate/{internal|exported}</c>).</summary>
public sealed class PkiRootSpec
{
    /// <summary>The wire <c>common_name</c> field. Required.</summary>
    public required string CommonName { get; init; }

    /// <summary>The wire <c>organization</c> field.</summary>
    public string? Organization { get; init; }

    /// <summary>The wire <c>key_type</c> field.</summary>
    public string? KeyType { get; init; }

    /// <summary>The wire <c>key_bits</c> field.</summary>
    public int? KeyBits { get; init; }

    /// <summary>The wire <c>ttl</c> field, in seconds.</summary>
    public TimeSpan? Ttl { get; init; }

    /// <summary>The wire <c>issuer_name</c> field.</summary>
    public string? IssuerName { get; init; }

    /// <summary>The wire <c>key_ref</c> field.</summary>
    public string? KeyRef { get; init; }
}

/// <summary>09 §CA lifecycle: <c>Pki.GenerateRoot</c>'s result, transcribed verbatim from <c>09-pki-engine.md:42</c>.</summary>
public sealed class PkiRootCertificate
{
    /// <summary>The generated root certificate, PEM-encoded verbatim (PKI-001).</summary>
    public required string Certificate { get; init; }

    /// <summary>The issuing CA certificate, PEM-encoded verbatim (PKI-001).</summary>
    public required string IssuingCa { get; init; }

    /// <summary>The new issuer's id.</summary>
    public required string IssuerId { get; init; }

    /// <summary>The new issuer's name.</summary>
    public required string IssuerName { get; init; }

    /// <summary>When the new root expires.</summary>
    public required DateTimeOffset Expiration { get; init; }

    /// <summary>PKI-002: the generated private key, present only for the <c>exported</c> path form, held in a redacting type.</summary>
    public SecretString? PrivateKey { get; init; }

    /// <summary>The private key's type, present alongside <see cref="PrivateKey"/>.</summary>
    public string? PrivateKeyType { get; init; }

    /// <summary>The managed key used, when one was.</summary>
    public string? KeyId { get; init; }
}

/// <summary>09 §CA lifecycle: <c>Pki.SignIntermediate</c>'s request body (<c>{mount}/root/sign-intermediate</c>).</summary>
public sealed class SignIntermediateRequest
{
    /// <summary>The wire <c>csr</c> field. Required.</summary>
    public required string Csr { get; init; }

    /// <summary>The wire <c>common_name</c> field.</summary>
    public string? CommonName { get; init; }

    /// <summary>The wire <c>organization</c> field.</summary>
    public string? Organization { get; init; }

    /// <summary>The wire <c>ttl</c> field, in seconds.</summary>
    public TimeSpan? Ttl { get; init; }

    /// <summary>The wire <c>max_path_length</c> field. Server default <c>-1</c> when omitted.</summary>
    public int? MaxPathLength { get; init; }

    /// <summary>The wire <c>issuer_ref</c> field.</summary>
    public string? IssuerRef { get; init; }
}

/// <summary>09 §CA lifecycle: <c>Pki.SignIntermediate</c>'s result, transcribed verbatim from <c>09-pki-engine.md:43</c>.</summary>
public sealed class SignedIntermediateCertificate
{
    /// <summary>The signed intermediate certificate, PEM-encoded verbatim (PKI-001).</summary>
    public required string Certificate { get; init; }

    /// <summary>The issuing CA certificate, PEM-encoded verbatim (PKI-001).</summary>
    public required string IssuingCa { get; init; }
}

/// <summary>
/// 09 §CA lifecycle: <c>Pki.GenerateIntermediate</c>'s request body
/// (<c>{mount}/intermediate/generate/{internal|exported}</c>). 09's own row for this operation names
/// no request fields at all — unlike <see cref="PkiRootSpec"/>'s row, which lists seven — but the
/// route sits at the immediately adjacent path, generating the same kind of key material for the
/// same kind of CA object one step earlier in its lifecycle. D-M9-17's rule (reuse a sibling's
/// <b>complete</b> named set, never a judged subset) is applied here on the request side: this is
/// <see cref="PkiRootSpec"/>'s full field list, transcribed rather than guessed at a smaller or a
/// different set.
/// </summary>
public sealed class PkiIntermediateSpec
{
    /// <summary>The wire <c>common_name</c> field. Required.</summary>
    public required string CommonName { get; init; }

    /// <summary>The wire <c>organization</c> field.</summary>
    public string? Organization { get; init; }

    /// <summary>The wire <c>key_type</c> field.</summary>
    public string? KeyType { get; init; }

    /// <summary>The wire <c>key_bits</c> field.</summary>
    public int? KeyBits { get; init; }

    /// <summary>The wire <c>ttl</c> field, in seconds.</summary>
    public TimeSpan? Ttl { get; init; }

    /// <summary>The wire <c>issuer_name</c> field.</summary>
    public string? IssuerName { get; init; }

    /// <summary>The wire <c>key_ref</c> field.</summary>
    public string? KeyRef { get; init; }
}

/// <summary>09 §CA lifecycle: <c>Pki.GenerateIntermediate</c>'s result, transcribed verbatim from <c>09-pki-engine.md:44</c>.</summary>
public sealed class PkiIntermediateCsr
{
    /// <summary>The generated CSR, PEM-encoded verbatim (PKI-001).</summary>
    public required string Csr { get; init; }

    /// <summary>The managed key used, when one was.</summary>
    public string? KeyId { get; init; }

    /// <summary>PKI-002: the generated private key, present only for the <c>exported</c> path form, held in a redacting type.</summary>
    public SecretString? PrivateKey { get; init; }

    /// <summary>The private key's type, present alongside <see cref="PrivateKey"/>.</summary>
    public string? PrivateKeyType { get; init; }
}

/// <summary>09 §CA lifecycle: <c>Pki.SetSignedIntermediate</c>'s result, transcribed verbatim from <c>09-pki-engine.md:45</c>.</summary>
public sealed class SetSignedIntermediateResult
{
    /// <summary>The issuer ids this call imported.</summary>
    public required IReadOnlyList<string> ImportedIssuers { get; init; }

    /// <summary>The managed key ids this call imported.</summary>
    public required IReadOnlyList<string> ImportedKeys { get; init; }

    /// <summary>The issuer this certificate is now filed under.</summary>
    public required string IssuerId { get; init; }

    /// <summary>The issuer's name.</summary>
    public required string IssuerName { get; init; }
}

/// <summary>
/// 09 §CA lifecycle: <c>Pki.ReadUrls</c>/<c>WriteUrls</c>'s shared field list
/// (<c>{mount}/config/urls</c>). Patch-shaped (OVR-007), like <see cref="PkiRole"/>: a
/// <see langword="null"/> member is omitted on write and means "the server never returned this
/// field" on read.
/// </summary>
public sealed class PkiUrls
{
    /// <summary>The wire <c>issuing_certificates</c> array.</summary>
    public IReadOnlyList<string>? IssuingCertificates { get; init; }

    /// <summary>The wire <c>crl_distribution_points</c> array.</summary>
    public IReadOnlyList<string>? CrlDistributionPoints { get; init; }

    /// <summary>The wire <c>ocsp_servers</c> array.</summary>
    public IReadOnlyList<string>? OcspServers { get; init; }
}

/// <summary>09 §CA lifecycle: <c>Pki.ReadCrlConfig</c>/<c>WriteCrlConfig</c>'s shared field list (<c>{mount}/config/crl</c>).</summary>
public sealed class PkiCrlConfig
{
    /// <summary>
    /// The wire <c>expiry</c> field. TRN-031: <c>09-pki-engine.md:48</c> quotes its default
    /// (<c>"72h"</c>), which is the specification's own discriminator for the Go-style string form
    /// rather than integer seconds — unlike <c>ttl</c>/<c>not_before_duration</c>, which TRN-031
    /// and 09 leave unquoted. Server default 72h when omitted.
    /// </summary>
    public TimeSpan? Expiry { get; init; }

    /// <summary>The wire <c>disable</c> field.</summary>
    public bool? Disable { get; init; }
}

/// <summary>09 §CA lifecycle: <c>Pki.ReadIssuersConfig</c>/<c>WriteIssuersConfig</c>'s shared field list (<c>{mount}/config/issuers</c>).</summary>
public sealed class PkiIssuersConfig
{
    /// <summary>The wire <c>default</c> field: the default issuer's ref.</summary>
    public string? Default { get; init; }
}

/// <summary>09 §CA lifecycle: <c>Pki.WriteIssuer</c>'s request body (<c>{mount}/issuer/{ref}</c>).</summary>
public sealed class PkiIssuerWrite
{
    /// <summary>The wire <c>issuer_name</c> field.</summary>
    public string? IssuerName { get; init; }

    /// <summary>The wire <c>usage</c> array.</summary>
    public IReadOnlyList<string>? Usage { get; init; }
}

/// <summary>
/// D-M9-16's generalised rule, applied to <c>Pki.GenerateKey</c>: 09 §Managed keys defines no
/// response shape for any of its rows, and this route's own <c>Internal</c>/<c>Exported</c> path
/// choice establishes that its response may carry key material (PKI-002) — the same situation as
/// <see cref="PkiCertificateExport"/>, with no sibling shape to transcribe. The whole response body
/// is wrapped verbatim (PKI-001) in a redacting type rather than surfaced through an untyped,
/// unredacted map.
/// </summary>
public sealed class PkiGeneratedKey
{
    /// <summary>The response body verbatim (PKI-001), redacted (PKI-002) so it never reaches a log by accident.</summary>
    public required SecretString Payload { get; init; }
}

/// <summary>09 §Tidy: <c>Pki.Tidy</c>'s request body (<c>{mount}/tidy</c>).</summary>
public sealed class PkiTidyOptions
{
    /// <summary>The wire <c>tidy_cert_store</c> field. Server default <see langword="true"/> when omitted.</summary>
    public bool? TidyCertStore { get; init; }

    /// <summary>The wire <c>tidy_revoked_certs</c> field. Server default <see langword="true"/> when omitted.</summary>
    public bool? TidyRevokedCerts { get; init; }

    /// <summary>
    /// The wire <c>safety_buffer</c> field. TRN-031: <c>09-pki-engine.md:82</c> quotes its default
    /// (<c>"72h"</c>), the Go-style string form. Server default 72h when omitted.
    /// </summary>
    public TimeSpan? SafetyBuffer { get; init; }
}

/// <summary>
/// 09 §Tidy: <c>Pki.ReadAutoTidy</c>/<c>WriteAutoTidy</c>'s field list, named as
/// <c>{enabled, interval = "12h", …}</c> — the trailing ellipsis names further fields 09 does not
/// enumerate, and D-M1c-25 forbids guessing them, so only the two named fields are bound here.
/// </summary>
public sealed class PkiAutoTidyConfig
{
    /// <summary>The wire <c>enabled</c> field.</summary>
    public bool? Enabled { get; init; }

    /// <summary>
    /// The wire <c>interval</c> field. TRN-031: <c>09-pki-engine.md:84</c> quotes its default
    /// (<c>"12h"</c>), the Go-style string form. Server default 12h when omitted.
    /// </summary>
    public TimeSpan? Interval { get; init; }
}

/// <summary>
/// 09 §ACME: <c>Pki.Acme.ReadConfig</c>/<c>WriteConfig</c>'s field list (<c>{mount}/acme/config</c>),
/// transcribed verbatim from <c>09-pki-engine.md:112-114</c>.
/// </summary>
public sealed class PkiAcmeConfig
{
    /// <summary>The wire <c>enabled</c> field.</summary>
    public bool? Enabled { get; init; }

    /// <summary>The wire <c>default_role</c> field.</summary>
    public string? DefaultRole { get; init; }

    /// <summary>The wire <c>default_issuer_ref</c> field.</summary>
    public string? DefaultIssuerRef { get; init; }

    /// <summary>The wire <c>external_hostname</c> field.</summary>
    public string? ExternalHostname { get; init; }

    /// <summary>
    /// The wire <c>nonce_ttl_secs</c> field. R4: the <c>_secs</c> suffix is the endpoint declaring
    /// the integer-seconds form (TRN-031), so this keeps the suffix and the <c>long?</c> type
    /// <c>SysCompleteOperations.cs</c>'s <c>DosConfig.WindowSecs</c>/<c>BanSecs</c>/<c>RefreshSecs</c>
    /// already establish for this exact spelling, rather than a <see cref="TimeSpan"/> the wire form
    /// does not ask for.
    /// </summary>
    public long? NonceTtlSecs { get; init; }

    /// <summary>The wire <c>dns_resolvers</c> array.</summary>
    public IReadOnlyList<string>? DnsResolvers { get; init; }

    /// <summary>The wire <c>eab_required</c> field.</summary>
    public bool? EabRequired { get; init; }

    /// <summary>The wire <c>rate_window_secs</c> field. See <see cref="NonceTtlSecs"/>'s remark — same <c>_secs</c>/<c>long?</c> precedent.</summary>
    public long? RateWindowSecs { get; init; }

    /// <summary>The wire <c>rate_orders_per_window</c> field.</summary>
    public int? RateOrdersPerWindow { get; init; }
}
