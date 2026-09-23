# `PkiOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/PkiOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/PkiOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `PkiAcmeOperations`

#### `ReadConfigAsync(mount, options, cancellationToken)`

Reads the ACME configuration: `GET {mount}/acme/config`.

Wire params: `mount` builds the route; no body. Returns the <see cref="PkiAcmeConfig"/>, or `null` when unconfigured. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.Acme.ReadConfig — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:1067`*

#### `WriteConfigAsync(config, mount, options, cancellationToken)`

Writes the ACME configuration: `POST {mount}/acme/config`.

Wire params: `mount` builds the route; body carries <see cref="PkiAcmeConfig"/>'s `enabled`, `default_role`, `default_issuer_ref`, `external_hostname`, `nonce_ttl_secs`, `dns_resolvers`, `eab_required`, `rate_window_secs`, `rate_orders_per_window`. Returns no value. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.Acme.WriteConfig — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:1080`*

#### `DeleteConfigAsync(mount, options, cancellationToken)`

Deletes the ACME configuration: `DELETE {mount}/acme/config`.

Wire params: `mount` builds the route; no body. Returns no value; deleting an absent configuration is not an error. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.Acme.DeleteConfig — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:1093`*

#### `DirectoryUrl(mount)`

09's client-side-only URL builder: `{endpoint}/{ApiPrefix}/{mount}/acme/directory`. Makes
no request — the RFC 8555 protocol paths are for an ACME client, not this SDK
(`09-pki-engine.md:115-117`) — and uses the client's configured
`ApiPrefix` rather than a per-call override, since this operation
takes no <see cref="RequestOptions"/> (there being no request to apply them to).

HTTP call: none — this is a client-side URL builder, not a request. Wire params: `mount` builds the path segment. Returns the directory URL string, never `null`. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.Acme.DirectoryUrl — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:1112`*

### `PkiCsrOperations`

#### `GenerateAsync(role, commonName, altNames, ipSans, emailSans, keyRef, exported, exportable, mount, options, cancellationToken)`

`POST {mount}/csr/generate`. 09-pki-engine.md:90 names this operation's fields as an
object literal (`{role, common_name, alt_names, ip_sans, email_sans, key_ref, exported,
exportable}`), the same notation `GenerateKeyAsync` and
`ImportKeyAsync` bind as separate optional parameters rather than a
request type, so this follows the same shape. None of the fields carries a "(required)"
marker on this row (unlike `IssueAsync`'s `common_name`), so no
client-side requiredness check is added beyond what 09 states.

D-M9-10: 09 defines no response shape for this route, but its own `exported`
parameter establishes the response may carry key material (PKI-002), so the result is the
typed, redacting <see cref="PkiGeneratedCsr"/> — transcribed whole-set (D-M9-17) from the
sibling shape `GenerateIntermediateAsync` defines at
`09-pki-engine.md:44` — rather than an untyped map (D-M9-16). Wire params: `mount`
builds the route; body carries `role`, `common_name`, `alt_names`, `ip_sans`,
`email_sans`, `key_ref`, `exported`, `exportable` (all optional). Returns the
<see cref="PkiGeneratedCsr"/>, never `null`; key material redacted (PKI-002).
Conformance: Complete (PKI-002). No error codes beyond the common set (ERR-061).

**Spec:** `Pki.Csr.Generate — PKI-002`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:1160`*

#### `ListAsync(mount, options, cancellationToken)`

Lists the pending CSR ids: `LIST {mount}/csr/`.

Wire params: `mount` builds the route; no query or body params. Returns an empty list when the backend has none, never `null`. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.Csr.List — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:1207`*

#### `ListInfoAsync(mount, after, limit, options, cancellationToken)`

14 §Bulk metadata listings: `GET {mount}/csr-info?after=&amp;limit=`.

D-M9-31: pinned to `/v2`, the same reasoning as
`ListCertificatesInfoAsync`: the PKI table carries no Prefix
column for `csr-info`, so Appendix A is silent rather than implying `v1`, and
`14-batch-and-request-efficiency.md:98` governs. This reverses D-M9-7. D-M9-10/D-M9-21
still hold: 09 defines no response shape for this listing's records, so each record
surfaces as the raw wire map (`ReadRawInfoPage`) rather than a guessed
type. See R-31. Wire params: `mount` builds the route; query carries `after`
(previous page's `Next`, verbatim) and `limit` (PAG-001: 1–500,
default 100). Returns a <see cref="Page{T}"/> of raw response maps, never
`null`. Conformance: Complete. Errors beyond the common set (ERR-061):
`BV-INPUT-004` for `limit` outside `1…500`.

**Spec:** `Pki.Csr.ListInfo — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:1232`*

#### `ListInfoAllAsync(mount, limit, maxRecords, options, cancellationToken)`

D-M9-8: PAG-004's iterator for <see cref="ListInfoAsync"/>, following
`ListCertificatesInfoAllAsync`'s exact shape.

HTTP call: none directly — delegates each page to <see cref="ListInfoAsync"/>. Returns an async stream of id/raw-map pairs, never `null`. Conformance: Complete (PAG-004). Errors beyond the common set (ERR-061): `BV-INPUT-004` (via the delegated page fetch), `BV-INPUT-005` when `maxRecords` is exceeded.

**Spec:** `Pki.Csr.ListInfoAll — PAG-004`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:1256`*

#### `ReadAsync(id, mount, options, cancellationToken)`

`GET {mount}/csr/{id}`. 09 names no response shape, and no parameter here can cause key
material to return, so the untyped map fallback applies (D-M9-21). See R-31.

Known soft edge of D-M9-16 (DR-0016 open question 5). A CSR generated with
`exportable: true` (`GenerateAsync`'s `exportable`
parameter) may itself carry key material when later read back, the same unresolved case as
`ReadKeyAsync`. D-M9-16's rule keys on this call's own request
parameters, which is the only signal 09 gives it — a route that can return a secret without a
parameter of its own announcing it is invisible to the rule. Not a defect in this slice; booked
for M10. Wire params: `id`/`mount` build the route; no body. Returns the raw response
map, or `null` when the CSR does not exist. Conformance: Complete. No error
codes beyond the common set (ERR-061).

**Spec:** `Pki.Csr.Read — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:1285`*

#### `DeleteAsync(id, mount, options, cancellationToken)`

Deletes a pending CSR: `DELETE {mount}/csr/{id}`.

Wire params: `id`/`mount` build the route; no body. Returns no value; deleting an absent CSR is not an error. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.Csr.Delete — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:1299`*

#### `SetSignedAsync(id, certificate, mount, options, cancellationToken)`

`POST {mount}/csr/{id}/set-signed`. 09 names no response shape, and no parameter here can cause key material to return, so the untyped map fallback applies (D-M9-21). See R-31.

Wire params: `id`/`mount` build the route; body carries `certificate` (required). Returns the raw response map, or `null` on an empty body. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.Csr.SetSigned — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:1312`*

### `PkiOperations`

#### `Acme`

09 §ACME: the `{mount}/acme/config` surface plus `DirectoryUrl`
(D-M9-3's `Transit.Byok` nested-operations idiom, `TransitOperations.cs:36`). The
RFC 8555 protocol paths themselves (`acme/directory`, `new-nonce`, `new-account`,
…) are for ACME clients, not this SDK, and are deliberately not wrapped beyond that one helper
(09 §ACME, D-M9-14's negative check).

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:46`*

#### `Csr`

09 §Outbound CSR queue (external signing): the `{mount}/csr/*` surface (D-M9-3's nested-
operations idiom).

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:52`*

#### `SignRequests`

09 §Inbound sign-request queue (approval workflow): the `{mount}/sign-request/*` surface
(D-M9-3's nested-operations idiom).

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:58`*

#### `ListRolesAsync(mount, options, cancellationToken)`

Lists the role names under `mount`: `LIST {mount}/roles/`.

Wire params: `mount` builds the route; no query or body params. Returns an empty list when the backend has none, never `null`. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.ListRoles — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:65`*

#### `WriteRoleAsync(name, role, mount, options, cancellationToken)`

Creates or replaces a role: `POST {mount}/roles/{name}`.

Wire params: `name`/`mount` build the route; body carries <see cref="PkiRole"/>'s fields (PKI-010: CSV-typed fields such as `alt_names`, `allowed_domains`, `key_usage` are comma-joined). Returns no value. Conformance: Complete (PKI-010). No error codes beyond the common set (ERR-061).

**Spec:** `Pki.WriteRole — PKI-010`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:78`*

#### `ReadRoleAsync(name, mount, options, cancellationToken)`

Reads a role: `GET {mount}/roles/{name}`.

Wire params: `name`/`mount` build the route; no body. Returns the <see cref="PkiRole"/>, or `null` when the role does not exist (PKI-010: CSV-typed fields are split back into lists on read). Conformance: Complete (PKI-010). No error codes beyond the common set (ERR-061).

**Spec:** `Pki.ReadRole — PKI-010`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:92`*

#### `DeleteRoleAsync(name, mount, options, cancellationToken)`

Deletes a role: `DELETE {mount}/roles/{name}`.

Wire params: `name`/`mount` build the route; no body. Returns no value; deleting an absent role is not an error. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.DeleteRole — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:106`*

#### `IssueAsync(role, request, mount, options, cancellationToken)`

Issues a leaf certificate: `POST {mount}/issue/{role}`.

Wire params: `role`/`mount` build the route; body carries `common_name` (required), `alt_names`, `ip_sans`, `ttl`, `issuer_ref`, `key_ref`, `upn_sans`, `email_sans`, `ad_sid` (PKI-010 CSV joining). Returns the <see cref="IssuedCertificate"/>, never `null`; key material redacted (PKI-002), PEM verbatim (PKI-001). Conformance: Complete (PKI-011). Errors beyond the common set (ERR-061): `BV-INPUT-001` for an empty `CommonName` (client-side).

**Spec:** `Pki.Issue — PKI-011`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:119`*

#### `SignAsync(role, request, mount, options, cancellationToken)`

Signs a caller-supplied CSR against a role: `POST {mount}/sign/{role}`.

Wire params: `role`/`mount` build the route; body carries `csr` (required), `common_name`, `alt_names`, `ip_sans`, `ttl`, `issuer_ref`, `key_ref`, `upn_sans`, `email_sans`, `ad_sid` (PKI-010 CSV joining). Returns the <see cref="SignedCertificate"/>, never `null`; it carries no private key field (the caller already holds the key). Conformance: Complete (PKI-010). No error codes beyond the common set (ERR-061).

**Spec:** `Pki.Sign — PKI-010`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:160`*

#### `SignVerbatimAsync(csr, ttl, issuerRef, mount, options, cancellationToken)`

Signs a CSR verbatim, without a role's constraints: `POST {mount}/sign-verbatim`.

Wire params: `mount` builds the route; body carries `csr` (required), `ttl`, `issuer_ref`. Returns the <see cref="SignedCertificate"/>, never `null`. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.SignVerbatim — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:206`*

#### `ListCertificatesAsync(mount, options, cancellationToken)`

Lists certificate serials under `mount`: `LIST {mount}/certs/`.

Wire params: `mount` builds the route; no query or body params. Returns an empty list when the backend has none, never `null`. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.ListCertificates — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:234`*

#### `ListCertificatesInfoAsync(mount, after, limit, options, cancellationToken)`

14 §Bulk metadata listings: `GET {mount}/certs-info?after=&amp;limit=`, the
cursor-paginated bulk listing.

D-M9-31: pinned to `/v2`. Appendix A's PKI table carries no Prefix column at all for
`certs-info`, so the legend at `appendix-a-endpoint-catalogue.md:3-5` — which
defines what the column means when present — says nothing about this route; the catalogue
is silent, not `v1`. With the catalogue silent, `14-batch-and-request-efficiency.md:98`
governs, and the `efficiency.pagination.zip-mismatch-protocol-error` fixture — captured
from the server's own behaviour at M8 — agrees: `/v2`. This reverses D-M9-7, which
mistook the catalogue's silence for an implicit `v1`. Wire params: `mount` builds the route; query carries `after` (previous page's `Next`, verbatim) and `limit` (PAG-001: 1–500, default 100). Returns a <see cref="Page{T}"/> of <see cref="CertificateSummary"/>, never `null`. Conformance: Complete. Errors beyond the common set (ERR-061): `BV-INPUT-004` for `limit` outside `1…500`.

**Spec:** `Pki.ListCertificatesInfo — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:258`*

#### `ListCertificatesInfoAllAsync(mount, limit, maxRecords, options, cancellationToken)`

D-M9-8: PAG-004's iterator for this area. Walks every page in cursor order via
<see cref="PagingWire.IteratePagesAsync{T}"/>, following
`ListNamespacesInfoAllAsync` and
`ListUsersInfoAllAsync`'s exact shape: each page fetch is
ordinary rate-gated traffic, and `BV-INPUT-005` is raised at
`maxRecords` (default 5000) rather than paging without bound.

HTTP call: none directly — delegates each page to <see cref="ListCertificatesInfoAsync"/>. Returns an async stream of serial/<see cref="CertificateSummary"/> pairs, never `null`. Conformance: Complete (PAG-004). Errors beyond the common set (ERR-061): `BV-INPUT-004` (via the delegated page fetch), `BV-INPUT-005` when `maxRecords` is exceeded.

**Spec:** `Pki.ListCertificatesInfoAll — PAG-004`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:324`*

#### `ReadCertificateAsync(serial, mount, options, cancellationToken)`

Reads a certificate record: `GET {mount}/cert/{serial}`. PKI-020: `serial` is sent exactly as given.

Wire params: `serial`/`mount` build the route; no body. Returns the <see cref="CertificateRecord"/>, or `null` when the serial does not exist; PEM returned verbatim (PKI-001). Conformance: Complete (PKI-020). No error codes beyond the common set (ERR-061).

**Spec:** `Pki.ReadCertificate — PKI-020`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:340`*

#### `DeleteCertificateAsync(serial, force, mount, options, cancellationToken)`

Deletes a certificate record: `DELETE {mount}/cert/{serial}`. PKI-020: `serial` is sent exactly as given.

Wire params: `serial`/`mount` build the route; body carries `force` when `true`, omitted otherwise. Returns no value; deleting an absent serial is not an error. Conformance: Complete (PKI-020). No error codes beyond the common set (ERR-061).

**Spec:** `Pki.DeleteCertificate — PKI-020`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:355`*

#### `AttachKeyAsync(serial, keyRef, mount, options, cancellationToken)`

Attaches a managed key to a certificate: `POST {mount}/cert/{serial}/key`.

Wire params: `serial`/`mount` build the route; body carries `key_ref` (required). Returns no value. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.AttachKey — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:371`*

#### `DetachKeyAsync(serial, mount, options, cancellationToken)`

Detaches the managed key from a certificate: `DELETE {mount}/cert/{serial}/key`.

Wire params: `serial`/`mount` build the route; no body. Returns no value. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.DetachKey — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:386`*

#### `ExportCertificateAsync(serial, format, includePrivateKey, mode, password, mount, options, cancellationToken)`

`POST {mount}/cert/{serial}/export`. D-M9-16 (M9 slice a handback): 09 defines no
response shape for this route, and its own `includePrivateKey`/`mode` parameters
establish that the response may carry key material (PKI-002) — unlike
<see cref="TransitByokOperations"/>'s unshaped routes, which carry none — so the whole body
is wrapped verbatim (PKI-001) in <see cref="PkiCertificateExport"/> rather than surfaced
through an untyped, unredacted map.

D-M9-19 (M9 slice a handback, second round): 09 and Appendix A specify this route as
`GET/POST`, and an earlier revision of this binding added a `useGet` form that
carried `password` in the query string — a worse leak than the one D-M9-16
closed, because `Internal/ErrorPaths.cs` redacts path segments only and never
inspects a query string, so the secret reached the request observer, the exception's
`Path`/`Details["path"]`, and the hint enrichment unredacted. **Binds POST only.**
D-M9-20: secret material never travels in a URL path segment or query string, only in a
request body — the GET form is not offered here, and is not to be re-added without first
closing `ErrorPaths.Redact`'s query-string gap (R-32). Wire params: `serial`/`mount` build the route; body carries `format`, `include_private_key`, `mode`, `password` (omitted when unset). Returns the <see cref="PkiCertificateExport"/>, never `null`; the payload is the raw response body, redacted (PKI-002). Conformance: Complete (PKI-001, PKI-002). No error codes beyond the common set (ERR-061).

**Spec:** `Pki.ExportCertificate — PKI-002`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:416`*

#### `ImportCertificateAsync(certificate, source, mount, options, cancellationToken)`

Imports an externally-issued certificate: `POST {mount}/certs/import`. 09 names only the request fields, not a response shape.

Wire params: `mount` builds the route; body carries `certificate` (required), `source` (omitted when unset). Returns the raw response map, or `null` on an empty body; no typed shape exists to decode into (09 §Certificates and CRL). Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.ImportCertificate — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:455`*

#### `RevokeAsync(serialNumber, mount, options, cancellationToken)`

Revokes a certificate: `POST {mount}/revoke`. PKI-020: `serialNumber` is sent exactly as given.

Wire params: `mount` builds the route; body carries `serial_number` (required). Returns no value. Conformance: Complete (PKI-020). No error codes beyond the common set (ERR-061).

**Spec:** `Pki.Revoke — PKI-020`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:479`*

#### `ReadCrlAsync(pem, mount, options, cancellationToken)`

Reads the mount's CRL: `GET {mount}/crl[/pem]`.

Wire params: `mount` builds the route; `pem` selects the `/pem` segment; no body. Returns the <see cref="Crl"/>, never `null`; PEM returned verbatim (PKI-001). Conformance: Complete (PKI-001). No error codes beyond the common set (ERR-061).

**Spec:** `Pki.ReadCrl — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:493`*

#### `RotateCrlAsync(mount, options, cancellationToken)`

Forces a CRL rotation: `POST {mount}/crl/rotate`.

Wire params: `mount` builds the route; no body. Returns no value. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.RotateCrl — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:507`*

#### `ReadIssuerCrlAsync(issuerRef, pem, mount, options, cancellationToken)`

Reads a specific issuer's CRL: `GET {mount}/issuer/{ref}/crl[/pem]`.

Wire params: `issuerRef`/`mount` build the route; `pem` selects the `/pem` segment; no body. Returns the <see cref="Crl"/>, never `null`; PEM returned verbatim (PKI-001). Conformance: Complete (PKI-001). No error codes beyond the common set (ERR-061).

**Spec:** `Pki.ReadIssuerCrl — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:519`*

#### `GenerateRootAsync(type, spec, mount, options, cancellationToken)`

Generates a root CA: `POST {mount}/root/generate/{internal|exported}`.

Wire params: `mount` and `type` (`internal` or `exported`) build the route; body carries `common_name` (required), `organization`, `key_type`, `key_bits`, `ttl`, `issuer_name`, `key_ref`. Returns the <see cref="PkiRootCertificate"/>, never `null`; `PrivateKey` is populated only for `Exported` and is redacted (PKI-002); PEM returned verbatim (PKI-001). Conformance: Complete (PKI-001, PKI-002). No error codes beyond the common set (ERR-061).

**Spec:** `Pki.GenerateRoot — PKI-002`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:537`*

#### `SignIntermediateAsync(request, mount, options, cancellationToken)`

Signs an intermediate CSR against the root: `POST {mount}/root/sign-intermediate`.

Wire params: `mount` builds the route; body carries `csr` (required), `common_name`, `organization`, `ttl`, `max_path_length` (default -1), `issuer_ref`. Returns the <see cref="SignedIntermediateCertificate"/>, never `null`; PEM returned verbatim (PKI-001). Conformance: Complete (PKI-001). No error codes beyond the common set (ERR-061).

**Spec:** `Pki.SignIntermediate — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:553`*

#### `GenerateIntermediateAsync(type, spec, mount, options, cancellationToken)`

Generates an intermediate CA CSR: `POST {mount}/intermediate/generate/{internal|exported}`. See <see cref="PkiIntermediateSpec"/> for the D-M9-17 transcription behind its request shape.

Wire params: `mount` and `type` (`internal` or `exported`) build the route; body carries `common_name` (required) plus <see cref="PkiIntermediateSpec"/>'s remaining fields. Returns the <see cref="PkiIntermediateCsr"/>, never `null`; `PrivateKey` is populated only for `Exported` and is redacted (PKI-002); PEM returned verbatim (PKI-001). Conformance: Complete (PKI-001, PKI-002). No error codes beyond the common set (ERR-061).

**Spec:** `Pki.GenerateIntermediate — PKI-002`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:569`*

#### `SetSignedIntermediateAsync(certificate, issuerName, mount, options, cancellationToken)`

Imports a signed intermediate certificate as a new issuer: `POST {mount}/intermediate/set-signed`.

Wire params: `mount` builds the route; body carries `certificate` (required), `issuer_name` (omitted when unset). Returns the <see cref="SetSignedIntermediateResult"/>, never `null`. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.SetSignedIntermediate — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:585`*

#### `ConfigureCaAsync(pemBundle, issuerName, mount, options, cancellationToken)`

Configures the CA from an externally-supplied PEM bundle: `POST {mount}/config/ca`. 09 names only the request fields, not a response shape, and no parameter here can cause key material to return (D-M9-16's boundary).

Wire params: `mount` builds the route; body carries `pem_bundle` (required), `issuer_name` (omitted when unset). Returns the raw response map, or `null` on an empty body. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.ConfigureCa — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:609`*

#### `ReadUrlsAsync(mount, options, cancellationToken)`

Reads the issuing/CRL/OCSP URLs config: `GET {mount}/config/urls`.

Wire params: `mount` builds the route; no body. Returns the <see cref="PkiUrls"/>, or `null` when unset. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.ReadUrls — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:632`*

#### `WriteUrlsAsync(urls, mount, options, cancellationToken)`

Writes the issuing/CRL/OCSP URLs config: `POST {mount}/config/urls`.

Wire params: `mount` builds the route; body carries <see cref="PkiUrls"/>'s `issuing_certificates`, `crl_distribution_points`, `ocsp_servers` arrays. Returns no value. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.WriteUrls — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:645`*

#### `ReadCrlConfigAsync(mount, options, cancellationToken)`

Reads the CRL config: `GET {mount}/config/crl`.

Wire params: `mount` builds the route; no body. Returns the <see cref="PkiCrlConfig"/>, or `null` when unset. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.ReadCrlConfig — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:658`*

#### `WriteCrlConfigAsync(config, mount, options, cancellationToken)`

Writes the CRL config: `POST {mount}/config/crl`.

Wire params: `mount` builds the route; body carries <see cref="PkiCrlConfig"/>'s `expiry` (default `72h`) and `disable`. Returns no value. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.WriteCrlConfig — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:671`*

#### `ReadIssuersConfigAsync(mount, options, cancellationToken)`

Reads the default-issuer config: `GET {mount}/config/issuers`.

Wire params: `mount` builds the route; no body. Returns the <see cref="PkiIssuersConfig"/>, or `null` when unset. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.ReadIssuersConfig — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:684`*

#### `WriteIssuersConfigAsync(config, mount, options, cancellationToken)`

Writes the default-issuer config: `POST {mount}/config/issuers`.

Wire params: `mount` builds the route; body carries <see cref="PkiIssuersConfig"/>'s `default` field. Returns no value. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.WriteIssuersConfig — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:697`*

#### `ListIssuersAsync(mount, options, cancellationToken)`

Lists issuer refs under `mount`: `LIST {mount}/issuers/`.

Wire params: `mount` builds the route; no query or body params. Returns an empty list when the backend has none, never `null`. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.ListIssuers — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:710`*

#### `ReadIssuerAsync(issuerRef, mount, options, cancellationToken)`

Reads an issuer's metadata: `GET {mount}/issuer/{ref}`. 09 names no response shape for an issuer object, and it carries no private key (line 52), so it is not a PKI-002 route.

Wire params: `issuerRef`/`mount` build the route; no body. Returns the raw response map, or `null` when the issuer does not exist. Conformance: Complete (PKI-002). No error codes beyond the common set (ERR-061).

**Spec:** `Pki.ReadIssuer — PKI-002`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:723`*

#### `WriteIssuerAsync(issuerRef, issuer, mount, options, cancellationToken)`

Updates an issuer's name and usage: `POST {mount}/issuer/{ref}`.

Wire params: `issuerRef`/`mount` build the route; body carries <see cref="PkiIssuerWrite"/>'s `issuer_name` (omitted when unset) and `usage` as a JSON array. Returns the raw response map, or `null` on an empty body. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.WriteIssuer — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:737`*

#### `DeleteIssuerAsync(issuerRef, mount, options, cancellationToken)`

Deletes an issuer: `DELETE {mount}/issuer/{ref}`.

Wire params: `issuerRef`/`mount` build the route; no body. Returns no value; deleting an absent issuer is not an error. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.DeleteIssuer — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:752`*

#### `IssuerChainAsync(issuerRef, mount, options, cancellationToken)`

Reads an issuer's certificate chain: `GET {mount}/issuer/{ref}/chain`. 09 names no response shape.

Wire params: `issuerRef`/`mount` build the route; no body. Returns the raw response map, or `null` when the issuer does not exist. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.IssuerChain — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:765`*

#### `ExportIssuerAsync(issuerRef, format, includeChain, password, mount, options, cancellationToken)`

`{mount}/issuer/{ref}/export`. Appendix A marks this route `R,W` (both GET and
POST), but this binds POST only, for the same reason D-M9-19 restricted
<see cref="ExportCertificateAsync"/> to POST: `password` is
<see cref="SecretString"/>-typed and D-M9-20 requires secret material to travel in a request
body, never a path segment or query string — a GET form here has nowhere but the query
string to carry it. 09 §CA lifecycle states private keys are never exported here
(line 52) — the opposite case from <see cref="ExportCertificateAsync"/> — so this is not a
PKI-002 route and no parameter implying it can export a key is added here (D-M9-1).

Wire params: `issuerRef`/`mount` build the route; body carries `format` (default `"pem"`), `include_chain`, and `password` (<see cref="SecretString"/>, sent only when set). Returns the raw response map, or `null` when the issuer does not exist. Conformance: Complete (PKI-002). No error codes beyond the common set (ERR-061).

**Spec:** `Pki.ExportIssuer — PKI-002`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:788`*

#### `ReadCaAsync(pem, mount, options, cancellationToken)`

Reads the mount's CA certificate: `GET {mount}/ca[/pem]`. 09 names no response shape.

Wire params: `mount` builds the route; `pem` selects the `/pem` segment; no body. Returns the raw response map, or `null` when no CA is configured. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.ReadCa — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:814`*

#### `ReadCaChainAsync(mount, options, cancellationToken)`

Reads the mount's CA chain: `GET {mount}/ca_chain`. 09 names no response shape.

Wire params: `mount` builds the route; no body. Returns the raw response map, or `null` when no CA is configured. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.ReadCaChain — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:828`*

#### `ListKeysAsync(mount, options, cancellationToken)`

Lists the managed-key refs under `mount`: `LIST {mount}/keys/`.

Wire params: `mount` builds the route; no query or body params. Returns an empty list when the backend has none, never `null`. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.ListKeys — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:843`*

#### `GenerateKeyAsync(type, keyType, keyBits, name, exportable, mount, options, cancellationToken)`

`POST {mount}/keys/generate/{internal|exported}`. D-M9-16's generalised rule: 09 §Managed
keys defines no response shape, and the `Exported` path form
establishes the response may carry key material (PKI-002), so the whole body is wrapped in a
redacting type rather than surfaced through an untyped, unredacted map. See
<see cref="PkiGeneratedKey"/>.

Wire params: `mount` and `type` (`internal`/`exported`) build the route `{mount}/keys/generate/{internal,exported}`; body carries `key_type`, `key_bits`, `name`, `exportable` (all optional). Returns the <see cref="PkiGeneratedKey"/>, never `null`; key material redacted (PKI-002). Conformance: Complete (PKI-002). No error codes beyond the common set (ERR-061).

**Spec:** `Pki.GenerateKey — PKI-002`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:862`*

#### `ImportKeyAsync(privateKey, name, exportable, mount, options, cancellationToken)`

Imports an externally generated private key: `POST {mount}/keys/import`. D-M9-20: the private key travels in the request body, never a path segment or query string.

Wire params: `mount` builds the route; body carries `private_key` (required, <see cref="SecretString"/>), `name`, `exportable` (both optional). Returns the raw response map, or `null` on an empty body. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.ImportKey — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:909`*

#### `ReadKeyAsync(keyRef, mount, options, cancellationToken)`

Reads a managed key's metadata: `GET {mount}/key/{ref}`. 09 names no response shape, and no parameter here establishes a private key could return.

Wire params: `keyRef`/`mount` build the route; no body. Returns the raw response map, or `null` when the key does not exist. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.ReadKey — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:938`*

#### `DeleteKeyAsync(keyRef, force, mount, options, cancellationToken)`

Deletes a managed key: `DELETE {mount}/key/{ref}`.

Wire params: `keyRef`/`mount` build the route; body carries `force` when `true`, omitted otherwise. Returns no value; deleting an absent key is not an error. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.DeleteKey — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:952`*

#### `TidyAsync(tidyOptions, mount, options, cancellationToken)`

Starts a tidy operation: `POST {mount}/tidy`.

Wire params: `mount` builds the route; body carries <see cref="PkiTidyOptions"/>'s `tidy_cert_store`, `tidy_revoked_certs` (server default `true` when omitted), and `safety_buffer` (TRN-031 duration string, server default `"72h"` when omitted). Returns no value. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.Tidy — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:970`*

#### `TidyStatusAsync(mount, options, cancellationToken)`

Reads tidy's current/last-run status: `GET {mount}/tidy-status`. 09 names no response shape.

Wire params: `mount` builds the route; no body. Returns the raw response map, or `null` when no tidy has run. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.TidyStatus — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:983`*

#### `ReadAutoTidyAsync(mount, options, cancellationToken)`

Reads the auto-tidy configuration: `GET {mount}/config/auto-tidy`.

Wire params: `mount` builds the route; no body. Returns the <see cref="PkiAutoTidyConfig"/>, or `null` when unconfigured. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.ReadAutoTidy — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:996`*

#### `WriteAutoTidyAsync(config, mount, options, cancellationToken)`

Writes the auto-tidy configuration: `POST {mount}/config/auto-tidy`.

Wire params: `mount` builds the route; body carries <see cref="PkiAutoTidyConfig"/>'s `enabled` and `interval` (TRN-031 duration string, server default `"12h"` when omitted). Returns no value. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.WriteAutoTidy — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:1009`*

### `PkiSignRequestOperations`

#### `ImportAsync(csr, requester, notes, suggestedRole, allowDuplicate, mount, options, cancellationToken)`

`POST {mount}/sign-request/import`. 09-pki-engine.md:99 names this operation's fields as
an object literal (`{csr, requester, notes, suggested_role, allow_duplicate}`), bound as
separate optional parameters following `ImportKeyAsync`'s idiom for
the same notation. 09 names only the request fields, not a response shape, and no parameter
here can cause key material to return, so the untyped map fallback applies (D-M9-21). See
R-31.

Wire params: `mount` builds the route; body carries `csr` (required), `requester`, `notes`, `suggested_role`, `allow_duplicate` (all optional). Returns the raw response map, or `null` on an empty body. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.SignRequests.Import — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:1361`*

#### `ListAsync(mount, options, cancellationToken)`

Lists the pending sign-request ids: `LIST {mount}/sign-request/`.

Wire params: `mount` builds the route; no query or body params. Returns an empty list when the backend has none, never `null`. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.SignRequests.List — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:1400`*

#### `ListInfoAsync(mount, after, limit, options, cancellationToken)`

14 §Bulk metadata listings: `GET {mount}/sign-request-info?after=&amp;limit=`.

D-M9-31: pinned to `/v2`, the same reasoning as `ListInfoAsync`:
no Prefix column names anything for this row, so Appendix A is silent rather than implying
`v1`, and `14-batch-and-request-efficiency.md:98` governs. This reverses D-M9-7.
D-M9-10/D-M9-21 still hold: 09 defines no response shape for this listing's records, so each
record surfaces as the raw wire map (`ReadRawInfoPage`). See R-31. Wire
params: `mount` builds the route; query carries `after` (previous page's
`Next`, verbatim) and `limit` (PAG-001: 1–500, default 100). Returns a
<see cref="Page{T}"/> of raw response maps, never `null`. Conformance:
Complete. Errors beyond the common set (ERR-061): `BV-INPUT-004` for `limit` outside
`1…500`.

**Spec:** `Pki.SignRequests.ListInfo — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:1424`*

#### `ListInfoAllAsync(mount, limit, maxRecords, options, cancellationToken)`

D-M9-8: PAG-004's iterator for <see cref="ListInfoAsync"/>, following
`ListCertificatesInfoAllAsync`'s exact shape.

HTTP call: none directly — delegates each page to <see cref="ListInfoAsync"/>. Returns an async stream of id/raw-map pairs, never `null`. Conformance: Complete (PAG-004). Errors beyond the common set (ERR-061): `BV-INPUT-004` (via the delegated page fetch), `BV-INPUT-005` when `maxRecords` is exceeded.

**Spec:** `Pki.SignRequests.ListInfoAll — PAG-004`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:1448`*

#### `ReadAsync(id, mount, options, cancellationToken)`

`GET {mount}/sign-request/{id}`. 09 names no response shape, so the untyped map fallback applies (D-M9-21). See R-31.

Wire params: `id`/`mount` build the route; no body. Returns the raw response map, or `null` when the sign-request does not exist. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.SignRequests.Read — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:1464`*

#### `DeleteAsync(id, mount, options, cancellationToken)`

Deletes a pending sign-request: `DELETE {mount}/sign-request/{id}`.

Wire params: `id`/`mount` build the route; no body. Returns no value; deleting an absent sign-request is not an error. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.SignRequests.Delete — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:1478`*

#### `PreflightAsync(id, mount, options, cancellationToken)`

`POST {mount}/sign-request/{id}/preflight`. 09 names no request or response fields, so the untyped map fallback applies (D-M9-21). See R-31.

Wire params: `id`/`mount` build the route; no body. Returns the raw response map, or `null` on an empty body. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Pki.SignRequests.Preflight — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:1491`*

#### `ApproveAsync(id, role, overrides, mount, options, cancellationToken)`

`POST {mount}/sign-request/{id}/approve`. 09-pki-engine.md:103 names `role`
and an `overrides?` parameter but no field list for the latter. `Pki.Sign`'s sibling
row (`09-pki-engine.md:30`, "+ overrides") settles the placement — <see
cref="PkiOperations.SignAsync"/> writes its override fields flat into the same object as
`csr` — but not the field set: D-M9-17 could transcribe <see cref="SignRequest"/>'s
complete set there because 09 signals a superset for that very operation, and no document does
so here, so D-M1c-25 forbids guessing one. `overrides` is therefore a raw
<see cref="IReadOnlyDictionary{TKey,TValue}"/> written flat into the request body next to
`role` (`WriteFlatMap`), rather than typed fields or an invented
nested key — D-M9-24 accepts this design subject to RF-1's collision guard: `role` is
reserved, so an `overrides` entry named `role` fails client-side with
`BV-INPUT-001` rather than silently outranking the named parameter on the wire (a JSON
object with a duplicate key resolves to whichever the parser reads last). 09 names no response
shape either, and no parameter here establishes exported key material, so the result is the
untyped map fallback (D-M9-21). See R-31.

Wire params: `id`/`mount` build the route; body carries `role` (required) plus `overrides`'s entries written flat (`role` reserved; a colliding key fails client-side with `BV-INPUT-001`, no request sent). Returns the raw response map, or `null` on an empty body. Conformance: Complete. Errors beyond the common set (ERR-061): `BV-INPUT-001` when `overrides` contains a key named `role` (client-side).

**Spec:** `Pki.SignRequests.Approve — 09-pki-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:1521`*

#### `ApproveVerbatimAsync(id, ttl, issuerRef, mount, options, cancellationToken)`

`POST {mount}/sign-request/{id}/approve-verbatim` (server-enforced `ttl` ≤ 30 days;
TRN-031: 09-pki-engine.md:104 does not quote `ttl`, so it is integer seconds like
`SignVerbatimAsync`'s `ttl`, not a Go-style string). No
requirement ID mandates a client-side cap, so none is added (D-M1c-25) — the 30-day limit is
left to the server. 09 names no response shape either, and no parameter here establishes
exported key material, so the untyped map fallback applies (D-M9-21). See R-31.

Wire params: `id`/`mount` build the route; body carries `ttl` (TRN-031 integer seconds) and `issuer_ref` (both optional). Returns the raw response map, or `null` on an empty body. Conformance: Complete (TRN-031). No error codes beyond the common set (ERR-061).

**Spec:** `Pki.SignRequests.ApproveVerbatim — TRN-031`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:1551`*

#### `RejectAsync(id, reason, mount, options, cancellationToken)`

`POST {mount}/sign-request/{id}/reject`. PKI-030's first limb: an empty or
whitespace-only `reason` fails client-side with `BV-INPUT-001`, no
request sent. The second limb — a 500-pending queue-cap breach recognised as
`BV-QUOTA-002 QueueFull` "by message" — stays on the traceability baseline (D-M9-11): no
document states the server's message, so nothing is guessed (D-M1c-25).

Wire params: `id`/`mount` build the route; body carries `reason` (required). Returns no value. Conformance: Complete (PKI-030). Errors beyond the common set (ERR-061): `BV-INPUT-001` for an empty or whitespace-only `reason` (client-side).

**Spec:** `Pki.SignRequests.Reject — PKI-030`

*Source: `dotnet/BastionVault.IntegrationSdk/PkiOperations.cs:1581`*

