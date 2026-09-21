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
