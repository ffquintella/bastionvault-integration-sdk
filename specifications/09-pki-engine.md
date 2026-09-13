# 09 — PKI Engine (`pki`)

X.509 certificate authority with classical (`rsa`, `ec`, `ed25519`) and post-quantum
(`ml-dsa-44/65/87`) keys, multiple issuers, managed keys, CRL, ACME, an outbound CSR queue
and an inbound sign-request approval queue. Operations live under `Client.Pki` with
`mount` default `"pki"`. **Complete** conformance level.

## Types (wire → canonical)

```
IssuedCertificate { Certificate, IssuingCa, CaChain[], PrivateKey?, PrivateKeyType?, SerialNumber, IssuerId, KeyId? }
SignedCertificate { Certificate, IssuingCa, CaChain[], SerialNumber, IssuerId, KeyId? }
CertificateRecord { Certificate, SerialNumber, IssuedAt, NotAfter?, IssuerId?, IsOrphaned?, Source?, RevokedAt?, KeyId?, KeyName? }
CertificateSummary { SerialNumber, IssuedAt, RevokedAt?, NotAfter, IssuerId, IsOrphaned, Source, KeyId, CommonName, IssuerDn }   // certs-info
Crl { Crl (PEM), CrlNumber, IssuerId }
```

- **PKI-001** PEM fields MUST be returned verbatim (no re-encoding). The SDK MUST NOT
  parse certificates itself except in optional helpers clearly named `Parse*` that use
  the runtime's X.509 library.
- **PKI-002** `PrivateKey` MUST be a redacting type.

## Roles and issuance

| Operation | HTTP | Key fields |
|-----------|------|------------|
| `Pki.ListRoles(mount)` | `LIST {mount}/roles/` | |
| `Pki.WriteRole(mount, name, PkiRole)` / `ReadRole` / `DeleteRole` | `POST/GET/DELETE {mount}/roles/{name}` | `ttl`, `max_ttl`, `key_type` (`ec`), `key_bits`, `signature_bits`, `allow_localhost` (true), `allow_any_name` (true), `allow_ip_sans` (true), `allow_subdomains`, `allow_bare_domains`, `allowed_domains`, `allow_glob_domains`, `server_flag` (true), `client_flag` (true), `use_csr_sans` (true), `use_csr_common_name` (true), `key_usage` (`DigitalSignature,KeyEncipherment`), `ext_key_usage`, `ext_key_usage_oids`, `country`, `province`, `locality`, `organization`, `ou`, `no_store`, `generate_lease`, `not_before_duration` (30), `issuer_ref`, `allow_key_reuse`, `allowed_key_refs`, `acme_enabled` (true), `allow_upn_sans`, `allowed_upn_domains`, `allow_email_sans`, `allowed_email_domains`, `allow_ad_sid`, `ad_sid` |
| `Pki.Issue(mount, role, IssueRequest)` → `IssuedCertificate` | `POST {mount}/issue/{role}` | `common_name` (required), `alt_names`, `ip_sans`, `ttl`, `issuer_ref`, `key_ref`, `upn_sans`, `email_sans`, `ad_sid` |
| `Pki.Sign(mount, role, SignRequest)` → `SignedCertificate` | `POST {mount}/sign/{role}` | `csr` (required) + overrides |
| `Pki.SignVerbatim(mount, csr, ttl?, issuerRef?)` | `POST {mount}/sign-verbatim` | |

- **PKI-010** CSV-typed fields (`alt_names`, `ip_sans`, `allowed_domains`, `key_usage`,
  …) MUST be accepted as lists and joined with `,` on the wire; on read they MUST be
  split back into lists.
- **PKI-011** `common_name` empty → `BV-INPUT-001` client-side.

## CA lifecycle

| Operation | HTTP |
|-----------|------|
| `Pki.GenerateRoot(mount, Internal|Exported, RootSpec)` → `{Certificate, IssuingCa, IssuerId, IssuerName, Expiration, PrivateKey?, PrivateKeyType?, KeyId?}` | `POST {mount}/root/generate/{internal|exported}` — `common_name` (req), `organization`, `key_type`, `key_bits`, `ttl`, `issuer_name`, `key_ref` |
| `Pki.SignIntermediate(mount, csr, spec)` → `{Certificate, IssuingCa}` | `POST {mount}/root/sign-intermediate` — `csr` (req), `common_name`, `organization`, `ttl`, `max_path_length` (−1), `issuer_ref` |
| `Pki.GenerateIntermediate(mount, Internal|Exported, spec)` → `{Csr, KeyId?, PrivateKey?, PrivateKeyType?}` | `POST {mount}/intermediate/generate/{internal|exported}` |
| `Pki.SetSignedIntermediate(mount, certificate, issuerName?)` → `{ImportedIssuers[], ImportedKeys[], IssuerId, IssuerName}` | `POST {mount}/intermediate/set-signed` |
| `Pki.ConfigureCa(mount, pemBundle, issuerName?)` | `POST {mount}/config/ca` |
| `Pki.ReadUrls/WriteUrls(mount, {issuing_certificates[], crl_distribution_points[], ocsp_servers[]})` | `GET/POST {mount}/config/urls` |
| `Pki.ReadCrlConfig/WriteCrlConfig(mount, {expiry = "72h", disable})` | `GET/POST {mount}/config/crl` |
| `Pki.ReadIssuersConfig/WriteIssuersConfig(mount, {default})` | `GET/POST {mount}/config/issuers` |
| `Pki.ListIssuers` / `ReadIssuer(ref)` / `WriteIssuer(ref, {issuer_name, usage[]})` / `DeleteIssuer(ref)` | `{mount}/issuers/`, `{mount}/issuer/{ref}` |
| `Pki.IssuerChain(ref)` | `GET {mount}/issuer/{ref}/chain` |
| `Pki.ExportIssuer(ref, format = pem|pkcs7|pkcs12, includeChain = true, password?)` | `GET/POST {mount}/issuer/{ref}/export` — private keys never exported |
| `Pki.ReadCa(mount, pem = false)` / `ReadCaChain` | `GET {mount}/ca[/pem]`, `GET {mount}/ca_chain` |

## Certificates and CRL

| Operation | HTTP |
|-----------|------|
| `Pki.ListCertificates(mount)` → `string[]` (serials) | `LIST {mount}/certs/` |
| `Pki.ListCertificatesInfo(mount, after?, limit?)` → `Page<CertificateSummary>` | `GET {mount}/certs-info?after=&limit=` ([14](14-batch-and-request-efficiency.md)) |
| `Pki.ReadCertificate(mount, serial)` → `CertificateRecord?` | `GET {mount}/cert/{serial}` |
| `Pki.DeleteCertificate(mount, serial, force = false)` | `DELETE {mount}/cert/{serial}` |
| `Pki.AttachKey(mount, serial, keyRef)` / `DetachKey` | `POST/DELETE {mount}/cert/{serial}/key` |
| `Pki.ExportCertificate(mount, serial, format, includePrivateKey, mode = normal|backup, password?)` | `GET/POST {mount}/cert/{serial}/export` |
| `Pki.ImportCertificate(mount, certificate, source?)` | `POST {mount}/certs/import` |
| `Pki.Revoke(mount, serialNumber)` | `POST {mount}/revoke` |
| `Pki.ReadCrl(mount, pem = false)` → `Crl` | `GET {mount}/crl[/pem]` |
| `Pki.RotateCrl(mount)` | `POST {mount}/crl/rotate` |
| `Pki.ReadIssuerCrl(mount, ref, pem = false)` | `GET {mount}/issuer/{ref}/crl[/pem]` |

- **PKI-020** Serials MUST be accepted in both `aa:bb:…` and `aabb…` forms and sent as
  given (the route accepts `[0-9a-fA-F:-]`).

## Managed keys

`Pki.ListKeys`, `GenerateKey(Internal|Exported, {key_type, key_bits, name, exportable})`,
`ImportKey({private_key, name, exportable})`, `ReadKey(ref)`, `DeleteKey(ref, force)` on
`{mount}/keys/`, `{mount}/keys/generate/{…}`, `{mount}/keys/import`, `{mount}/key/{ref}`.

## Tidy

`Pki.Tidy(mount, {tidy_cert_store = true, tidy_revoked_certs = true, safety_buffer = "72h"})`
→ `POST {mount}/tidy`; `Pki.TidyStatus` → `GET {mount}/tidy-status`;
`Pki.ReadAutoTidy/WriteAutoTidy({enabled, interval = "12h", …})` → `{mount}/config/auto-tidy`.

## Outbound CSR queue (external signing)

| Operation | HTTP |
|-----------|------|
| `Pki.Csr.Generate(mount, {role, common_name, alt_names, ip_sans, email_sans, key_ref, exported, exportable})` | `POST {mount}/csr/generate` |
| `Pki.Csr.List` / `ListInfo(after, limit)` | `LIST {mount}/csr/`, `GET {mount}/csr-info` |
| `Pki.Csr.Read(id)` / `Delete(id)` | `GET/DELETE {mount}/csr/{id}` |
| `Pki.Csr.SetSigned(id, certificate)` | `POST {mount}/csr/{id}/set-signed` |

## Inbound sign-request queue (approval workflow)

| Operation | HTTP |
|-----------|------|
| `Pki.SignRequests.Import({csr, requester, notes, suggested_role, allow_duplicate})` | `POST {mount}/sign-request/import` |
| `Pki.SignRequests.List` / `ListInfo(after, limit)` | `LIST {mount}/sign-request/`, `GET {mount}/sign-request-info` |
| `Pki.SignRequests.Read(id)` / `Delete(id)` | `GET/DELETE {mount}/sign-request/{id}` |
| `Pki.SignRequests.Preflight(id)` | `POST {mount}/sign-request/{id}/preflight` |
| `Pki.SignRequests.Approve(id, role, overrides?)` | `POST {mount}/sign-request/{id}/approve` |
| `Pki.SignRequests.ApproveVerbatim(id, ttl?, issuerRef?)` | `POST {mount}/sign-request/{id}/approve-verbatim` (ttl ≤ 30 d) |
| `Pki.SignRequests.Reject(id, reason)` | `POST {mount}/sign-request/{id}/reject` — `reason` required |

- **PKI-030** `Reject` with empty reason → `BV-INPUT-001`. Queue cap (500 pending) breach
  → `BV-QUOTA-002 QueueFull` by message.

## ACME (informative)

`{mount}/acme/config` (read/write/delete: `enabled`, `default_role`, `default_issuer_ref`,
`external_hostname`, `nonce_ttl_secs`, `dns_resolvers`, `eab_required`,
`rate_window_secs`, `rate_orders_per_window`) is a normal authenticated config path the
SDK MUST type as `Pki.Acme.ReadConfig/WriteConfig/DeleteConfig`. The RFC 8555 protocol
paths (`acme/directory`, `new-nonce`, `new-account`, …) are for ACME clients, not this
SDK; the SDK MUST NOT wrap them beyond `Pki.Acme.DirectoryUrl(mount) -> string`.

## Server error strings (recognition)

| Server message | HTTP | Code |
|----------------|------|------|
| `PKI role is not found.` | 500 | `BV-PKI-001 RoleNotFound` |
| `PKI certificate is not found.` | 500 | `BV-PKI-002 CertificateNotFound` |
| `PKI ca is not config.`, `PKI ca private key is not found.` | 500 | `BV-PKI-003 CaNotConfigured` |
| `PKI pem bundle is invalid.`, `PKI cert chain is incorrect.`, `PKI cert is not ca.`, `PKI ca public key of certificate does not match private key.`, `PKI ca extension is incorrect.` | 400/500 | `BV-PKI-004 InvalidCaMaterial` |
| `PKI key type is invalid.`, `PKI key bits is invalid.` | 400 | `BV-INPUT-100` |
| `PKI key_name already exists.` | 500 | `BV-CONFLICT-007 KeyNameExists` |
| `PKI key operation is invalid.`, `PKI data is invalid.` | 500 | `BV-INPUT-100` |
| `PKI internal error.` | 500 | `BV-SERVER-005` |
