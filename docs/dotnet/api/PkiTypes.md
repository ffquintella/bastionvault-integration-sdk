# `PkiTypes` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/PkiTypes.cs`](../../../dotnet/BastionVault.IntegrationSdk/PkiTypes.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `CertificateRecord`

#### `Certificate`

The stored certificate, PEM-encoded verbatim (PKI-001).

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:251`*

#### `SerialNumber`

PKI-020: the certificate's serial number, exactly as the server returned it.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:254`*

#### `IssuedAt`

When the certificate was issued.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:257`*

#### `NotAfter`

The certificate's expiry, when the server reported one.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:260`*

#### `IssuerId`

The issuer that signed this certificate, when the server reported one.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:263`*

#### `IsOrphaned`

Whether this certificate has no matching issuer on record.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:266`*

#### `Source`

How this certificate entered the store (e.g. `issued`, `imported`).

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:269`*

#### `RevokedAt`

When the certificate was revoked, when it has been.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:272`*

#### `KeyId`

The managed key attached to this certificate, when one is.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:275`*

#### `KeyName`

The managed key's name, alongside <see cref="KeyId"/>.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:278`*

### `CertificateSummary`

#### `SerialNumber`

PKI-020: the certificate's serial number, exactly as the server returned it.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:285`*

#### `IssuedAt`

When the certificate was issued.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:288`*

#### `RevokedAt`

When the certificate was revoked, when it has been.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:291`*

#### `NotAfter`

The certificate's expiry.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:294`*

#### `IssuerId`

The issuer that signed this certificate.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:297`*

#### `IsOrphaned`

Whether this certificate has no matching issuer on record.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:300`*

#### `Source`

How this certificate entered the store (e.g. `issued`, `imported`).

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:303`*

#### `KeyId`

The managed key attached to this certificate, when one is.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:306`*

#### `CommonName`

The certificate's subject common name.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:309`*

#### `IssuerDn`

The issuer's distinguished name.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:312`*

### `Crl`

#### `CrlPem`

The CRL, PEM-encoded verbatim (PKI-001). 09 §Types names the wire field `Crl`, which
C# refuses as a member name identical to its containing type (CS0542); `CrlPem` is the
smallest deviation that still reads as the same field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:323`*

#### `CrlNumber`

The CRL's sequence number.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:326`*

#### `IssuerId`

The issuer this CRL belongs to.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:329`*

### `IssueRequest`

#### `CommonName`

The wire `common_name` field. PKI-011: must not be empty; refused client-side (`BV-INPUT-001`) before any request is sent.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:128`*

#### `AltNames`

PKI-010: the wire's CSV-typed `alt_names`.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:131`*

#### `IpSans`

PKI-010: the wire's CSV-typed `ip_sans`.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:134`*

#### `Ttl`

The wire `ttl` field, in seconds.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:137`*

#### `IssuerRef`

The wire `issuer_ref` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:140`*

#### `KeyRef`

The wire `key_ref` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:143`*

#### `UpnSans`

PKI-010: the wire's CSV-typed `upn_sans`.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:146`*

#### `EmailSans`

PKI-010: the wire's CSV-typed `email_sans`.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:149`*

#### `AdSid`

The wire `ad_sid` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:152`*

### `IssuedCertificate`

#### `Certificate`

The issued certificate, PEM-encoded verbatim (PKI-001).

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:201`*

#### `IssuingCa`

The issuing CA certificate, PEM-encoded verbatim (PKI-001).

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:204`*

#### `CaChain`

The full CA chain, PEM-encoded verbatim (PKI-001).

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:207`*

#### `PrivateKey`

PKI-002: the generated private key, present only when the server generated one, held in a redacting type.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:210`*

#### `PrivateKeyType`

The private key's type, present alongside <see cref="PrivateKey"/>.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:213`*

#### `SerialNumber`

PKI-020: the issued certificate's serial number, exactly as the server returned it.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:216`*

#### `IssuerId`

The issuer that signed this certificate.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:219`*

#### `KeyId`

The managed key used, when one was.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:222`*

### `PkiAcmeConfig`

#### `Enabled`

The wire `enabled` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:621`*

#### `DefaultRole`

The wire `default_role` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:624`*

#### `DefaultIssuerRef`

The wire `default_issuer_ref` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:627`*

#### `ExternalHostname`

The wire `external_hostname` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:630`*

#### `NonceTtlSecs`

The wire `nonce_ttl_secs` field. R4: the `_secs` suffix is the endpoint declaring
the integer-seconds form (TRN-031), so this keeps the suffix and the `long?` type
`SysCompleteOperations.cs`'s `DosConfig.WindowSecs`/`BanSecs`/`RefreshSecs`
already establish for this exact spelling, rather than a <see cref="TimeSpan"/> the wire form
does not ask for.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:639`*

#### `DnsResolvers`

The wire `dns_resolvers` array.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:642`*

#### `EabRequired`

The wire `eab_required` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:645`*

#### `RateWindowSecs`

The wire `rate_window_secs` field. See <see cref="NonceTtlSecs"/>'s remark — same `_secs`/`long?` precedent.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:648`*

#### `RateOrdersPerWindow`

The wire `rate_orders_per_window` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:651`*

### `PkiAutoTidyConfig`

#### `Enabled`

The wire `enabled` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:605`*

#### `Interval`

The wire `interval` field. TRN-031: `09-pki-engine.md:84` quotes its default
(`"12h"`), the Go-style string form. Server default 12h when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:611`*

### `PkiCertificateExport`

#### `Payload`

The response body verbatim (PKI-001), redacted (PKI-002) so it never reaches a log by accident.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:345`*

### `PkiCrlConfig`

#### `Expiry`

The wire `expiry` field. TRN-031: `09-pki-engine.md:48` quotes its default
(`"72h"`), which is the specification's own discriminator for the Go-style string form
rather than integer seconds — unlike `ttl`/`not_before_duration`, which TRN-031
and 09 leave unquoted. Server default 72h when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:544`*

#### `Disable`

The wire `disable` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:547`*

### `PkiGeneratedCsr`

#### `Csr`

The generated CSR, PEM-encoded verbatim (PKI-001).

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:670`*

#### `KeyId`

The managed key used, when one was.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:673`*

#### `PrivateKey`

PKI-002: the generated private key, present only when `exported` was requested, held in a redacting type.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:676`*

#### `PrivateKeyType`

The private key's type, present alongside <see cref="PrivateKey"/>.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:679`*

### `PkiGeneratedKey`

#### `Payload`

The response body verbatim (PKI-001), redacted (PKI-002) so it never reaches a log by accident.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:578`*

### `PkiIntermediateCsr`

#### `Csr`

The generated CSR, PEM-encoded verbatim (PKI-001).

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:489`*

#### `KeyId`

The managed key used, when one was.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:492`*

#### `PrivateKey`

PKI-002: the generated private key, present only for the `exported` path form, held in a redacting type.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:495`*

#### `PrivateKeyType`

The private key's type, present alongside <see cref="PrivateKey"/>.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:498`*

### `PkiIntermediateSpec`

#### `CommonName`

The wire `common_name` field. Required.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:464`*

#### `Organization`

The wire `organization` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:467`*

#### `KeyType`

The wire `key_type` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:470`*

#### `KeyBits`

The wire `key_bits` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:473`*

#### `Ttl`

The wire `ttl` field, in seconds.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:476`*

#### `IssuerName`

The wire `issuer_name` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:479`*

#### `KeyRef`

The wire `key_ref` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:482`*

### `PkiIssuerWrite`

#### `IssuerName`

The wire `issuer_name` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:561`*

#### `Usage`

The wire `usage` array.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:564`*

### `PkiIssuersConfig`

#### `Default`

The wire `default` field: the default issuer's ref.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:554`*

### `PkiRole`

#### `Ttl`

The wire `ttl` field, in seconds.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:13`*

#### `MaxTtl`

The wire `max_ttl` field, in seconds.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:16`*

#### `KeyType`

The wire `key_type` field. Server default `ec` when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:19`*

#### `KeyBits`

The wire `key_bits` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:22`*

#### `SignatureBits`

The wire `signature_bits` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:25`*

#### `AllowLocalhost`

The wire `allow_localhost` field. Server default `true` when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:28`*

#### `AllowAnyName`

The wire `allow_any_name` field. Server default `true` when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:31`*

#### `AllowIpSans`

The wire `allow_ip_sans` field. Server default `true` when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:34`*

#### `AllowSubdomains`

The wire `allow_subdomains` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:37`*

#### `AllowBareDomains`

The wire `allow_bare_domains` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:40`*

#### `AllowedDomains`

PKI-010: the wire's CSV-typed `allowed_domains`, accepted and returned as a list.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:43`*

#### `AllowGlobDomains`

The wire `allow_glob_domains` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:46`*

#### `ServerFlag`

The wire `server_flag` field. Server default `true` when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:49`*

#### `ClientFlag`

The wire `client_flag` field. Server default `true` when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:52`*

#### `UseCsrSans`

The wire `use_csr_sans` field. Server default `true` when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:55`*

#### `UseCsrCommonName`

The wire `use_csr_common_name` field. Server default `true` when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:58`*

#### `KeyUsage`

PKI-010: the wire's CSV-typed `key_usage`. Server default `DigitalSignature,KeyEncipherment` when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:61`*

#### `ExtKeyUsage`

PKI-010: the wire's CSV-typed `ext_key_usage`.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:64`*

#### `ExtKeyUsageOids`

PKI-010: the wire's CSV-typed `ext_key_usage_oids`.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:67`*

#### `Country`

The wire `country` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:70`*

#### `Province`

The wire `province` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:73`*

#### `Locality`

The wire `locality` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:76`*

#### `Organization`

The wire `organization` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:79`*

#### `Ou`

The wire `ou` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:82`*

#### `NoStore`

The wire `no_store` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:85`*

#### `GenerateLease`

The wire `generate_lease` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:88`*

#### `NotBeforeDuration`

The wire `not_before_duration` field, in seconds. Server default 30 when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:91`*

#### `IssuerRef`

The wire `issuer_ref` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:94`*

#### `AllowKeyReuse`

The wire `allow_key_reuse` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:97`*

#### `AllowedKeyRefs`

PKI-010: the wire's CSV-typed `allowed_key_refs`.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:100`*

#### `AcmeEnabled`

The wire `acme_enabled` field. Server default `true` when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:103`*

#### `AllowUpnSans`

The wire `allow_upn_sans` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:106`*

#### `AllowedUpnDomains`

PKI-010: the wire's CSV-typed `allowed_upn_domains`.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:109`*

#### `AllowEmailSans`

The wire `allow_email_sans` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:112`*

#### `AllowedEmailDomains`

PKI-010: the wire's CSV-typed `allowed_email_domains`.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:115`*

#### `AllowAdSid`

The wire `allow_ad_sid` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:118`*

#### `AdSid`

The wire `ad_sid` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:121`*

### `PkiRootCertificate`

#### `Certificate`

The generated root certificate, PEM-encoded verbatim (PKI-001).

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:395`*

#### `IssuingCa`

The issuing CA certificate, PEM-encoded verbatim (PKI-001).

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:398`*

#### `IssuerId`

The new issuer's id.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:401`*

#### `IssuerName`

The new issuer's name.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:404`*

#### `Expiration`

When the new root expires.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:407`*

#### `PrivateKey`

PKI-002: the generated private key, present only for the `exported` path form, held in a redacting type.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:410`*

#### `PrivateKeyType`

The private key's type, present alongside <see cref="PrivateKey"/>.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:413`*

#### `KeyId`

The managed key used, when one was.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:416`*

### `PkiRootSpec`

#### `CommonName`

The wire `common_name` field. Required.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:370`*

#### `Organization`

The wire `organization` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:373`*

#### `KeyType`

The wire `key_type` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:376`*

#### `KeyBits`

The wire `key_bits` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:379`*

#### `Ttl`

The wire `ttl` field, in seconds.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:382`*

#### `IssuerName`

The wire `issuer_name` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:385`*

#### `KeyRef`

The wire `key_ref` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:388`*

### `PkiTidyOptions`

#### `TidyCertStore`

The wire `tidy_cert_store` field. Server default `true` when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:585`*

#### `TidyRevokedCerts`

The wire `tidy_revoked_certs` field. Server default `true` when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:588`*

#### `SafetyBuffer`

The wire `safety_buffer` field. TRN-031: `09-pki-engine.md:82` quotes its default
(`"72h"`), the Go-style string form. Server default 72h when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:594`*

### `PkiUrls`

#### `IssuingCertificates`

The wire `issuing_certificates` array.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:526`*

#### `CrlDistributionPoints`

The wire `crl_distribution_points` array.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:529`*

#### `OcspServers`

The wire `ocsp_servers` array.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:532`*

### `SetSignedIntermediateResult`

#### `ImportedIssuers`

The issuer ids this call imported.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:505`*

#### `ImportedKeys`

The managed key ids this call imported.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:508`*

#### `IssuerId`

The issuer this certificate is now filed under.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:511`*

#### `IssuerName`

The issuer's name.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:514`*

### `SignIntermediateRequest`

#### `Csr`

The wire `csr` field. Required.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:423`*

#### `CommonName`

The wire `common_name` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:426`*

#### `Organization`

The wire `organization` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:429`*

#### `Ttl`

The wire `ttl` field, in seconds.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:432`*

#### `MaxPathLength`

The wire `max_path_length` field. Server default `-1` when omitted.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:435`*

#### `IssuerRef`

The wire `issuer_ref` field.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:438`*

### `SignRequest`

#### `Csr`

The wire `csr` field. Required; refused client-side when empty.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:167`*

#### `CommonName`

The wire `common_name` override.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:170`*

#### `AltNames`

PKI-010: the wire's CSV-typed `alt_names` override.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:173`*

#### `IpSans`

PKI-010: the wire's CSV-typed `ip_sans` override.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:176`*

#### `Ttl`

The wire `ttl` override, in seconds.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:179`*

#### `IssuerRef`

The wire `issuer_ref` override.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:182`*

#### `KeyRef`

The wire `key_ref` override.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:185`*

#### `UpnSans`

PKI-010: the wire's CSV-typed `upn_sans` override.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:188`*

#### `EmailSans`

PKI-010: the wire's CSV-typed `email_sans` override.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:191`*

#### `AdSid`

The wire `ad_sid` override.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:194`*

### `SignedCertificate`

#### `Certificate`

The signed certificate, PEM-encoded verbatim (PKI-001).

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:229`*

#### `IssuingCa`

The issuing CA certificate, PEM-encoded verbatim (PKI-001).

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:232`*

#### `CaChain`

The full CA chain, PEM-encoded verbatim (PKI-001).

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:235`*

#### `SerialNumber`

PKI-020: the signed certificate's serial number, exactly as the server returned it.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:238`*

#### `IssuerId`

The issuer that signed this certificate.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:241`*

#### `KeyId`

The managed key used, when one was.

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:244`*

### `SignedIntermediateCertificate`

#### `Certificate`

The signed intermediate certificate, PEM-encoded verbatim (PKI-001).

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:445`*

#### `IssuingCa`

The issuing CA certificate, PEM-encoded verbatim (PKI-001).

*Source: `dotnet/BastionVault.IntegrationSdk/PkiTypes.cs:448`*

