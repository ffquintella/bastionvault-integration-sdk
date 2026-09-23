# `SshOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/SshOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/SshOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `SshOperations`

#### `ConfigureCaAsync(generateSigningKey, privateKey, algorithm, mount, options, cancellationToken)`

Configures (or generates) the SSH CA's signing key: `POST {mount}/config/ca`.

Wire params: `mount` builds the route; body carries
`generateSigningKey` (default `true`),
`privateKey` (optional, imported when set), `algorithm`
(optional; `ed25519` or `mldsa65`, PQC feature-gated). Returns
<see cref="SshCaKey"/>, never `null`. Conformance: Complete. No error codes
beyond the common set (ERR-061).

**Spec:** `Ssh.ConfigureCa — 10-ssh-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SshOperations.cs:37`*

#### `ReadCaAsync(mount, options, cancellationToken)`

Reads the SSH CA's public key and algorithm: `GET {mount}/config/ca`.

Wire params: `mount` builds the route; no body. Returns
<see cref="SshCaKey"/>, or `null` when the CA is not yet configured.
Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Ssh.ReadCa — 10-ssh-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SshOperations.cs:70`*

#### `DeleteCaAsync(mount, options, cancellationToken)`

Deletes the SSH CA's configuration: `DELETE {mount}/config/ca`. 204.

Wire params: `mount` builds the route; no body. Returns
`void` on the server's `204`. Conformance: Complete. No error codes
beyond the common set (ERR-061).

**Spec:** `Ssh.DeleteCa — 10-ssh-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SshOperations.cs:88`*

#### `PublicKeyAsync(mount, options, cancellationToken)`

`GET {mount}/public_key`. Returns the authorized-keys-form bytes verbatim (D-M9-1).

A mount path is Shape A (`03-transport-and-protocol.md:114`) — an object envelope
carrying `data` — and `TRN-040` makes shape detection safe either way, so this
reads `data.public_key` through the same envelope every other route in this class
uses, rather than treating the response as a raw, unwrapped body. The wire field name
`"public_key"` itself is unpinned by any document or fixture. Wire params:
`mount` builds the route; no body. Returns the public key string, never
`null`. Conformance: Complete (TRN-040). No error codes beyond the common
set (ERR-061).

**Spec:** `Ssh.PublicKey — TRN-040`

*Source: `dotnet/BastionVault.IntegrationSdk/SshOperations.cs:109`*

#### `ListRolesAsync(mount, options, cancellationToken)`

Lists the role names under `mount`: `LIST {mount}/roles/`.

Wire params: `mount` builds the route; no query or body params. Returns an
empty list when the backend has none, never `null`. Conformance: Complete.
No error codes beyond the common set (ERR-061).

**Spec:** `Ssh.ListRoles — 10-ssh-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SshOperations.cs:130`*

#### `ListRolesInfoAsync(mount, after, limit, options, cancellationToken)`

14 §Bulk metadata listings: `GET {mount}/roles-info?after=&amp;limit=`, the
cursor-paginated bulk listing.

Deliberately unpinned (D-M9-7): `appendix-a-endpoint-catalogue.md:221` marks this
route `v1`, and the legend at `:3-5` defines `v1` as "uses `ApiPrefix`,
only an explicit `v2` pins" — so this passes `options` through
unmodified rather than pinning a literal `v2/`. Do not "fix" this back to a pin; see
`ListUsersInfoAsync` for the different case where Appendix A
does name `v2` for its route. Wire params: `mount` builds the route;
query carries `after` (cursor, PAG-002) and `limit`
(defaulted to 100 and validated to `1-500`, PAG-001). Returns <see cref="Page{T}"/> of
<see cref="SshRole"/>, never `null`. Conformance: Complete (PAG-001).
Errors beyond the common set (ERR-061): `BV-INPUT-004` (limit out of range, PAG-001);
`BV-PROTOCOL-002` (records/keys length mismatch, PAG-005).

**Spec:** `Ssh.ListRolesInfo — PAG-001`

*Source: `dotnet/BastionVault.IntegrationSdk/SshOperations.cs:158`*

#### `ListRolesInfoAllAsync(mount, limit, maxRecords, options, cancellationToken)`

D-M9-8: PAG-004's iterator for this area, following
`ListCertificatesInfoAllAsync`'s exact shape.

HTTP call: none directly — walks <see cref="ListRolesInfoAsync"/> pages via
<see cref="PagingWire.IteratePagesAsync{T}"/>. Wire params: as <see cref="ListRolesInfoAsync"/>,
plus `maxRecords` (client-side cap, no wire effect). Returns each record
keyed by its name, never `null`. Conformance: Complete (PAG-004). Errors
beyond the common set (ERR-061): `BV-INPUT-005` when the walk would exceed
`maxRecords`.

**Spec:** `Ssh.ListRolesInfoAll — PAG-004`

*Source: `dotnet/BastionVault.IntegrationSdk/SshOperations.cs:189`*

#### `WriteRoleAsync(name, role, mount, options, cancellationToken)`

Creates or replaces an SSH role: `POST {mount}/roles/{name}`.

Wire params: `name`/`mount` build the route; body carries
`role`'s fields (`key_type`, `algorithm_signer`, `cert_type`,
`allowed_users`, `default_user`, `allowed_extensions`,
`default_extensions`, `allowed_critical_options`, `default_critical_options`,
`ttl`, `max_ttl`, `not_before_duration`, `key_id_format`,
`cidr_list`, `exclude_cidr_list`, `port`, `pqc_only`). Returns
`void` on success. Conformance: Complete. No error codes beyond the common
set (ERR-061).

**Spec:** `Ssh.WriteRole — 10-ssh-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SshOperations.cs:214`*

#### `ReadRoleAsync(name, mount, options, cancellationToken)`

Reads an SSH role's configuration: `GET {mount}/roles/{name}`.

Wire params: `name`/`mount` build the route; no body. A
missing role is `null`, never an exception. Conformance: Complete. No
error codes beyond the common set (ERR-061).

**Spec:** `Ssh.ReadRole — 10-ssh-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SshOperations.cs:232`*

#### `DeleteRoleAsync(name, mount, options, cancellationToken)`

Deletes an SSH role: `DELETE {mount}/roles/{name}`.

Wire params: `name`/`mount` build the route; no body.
Returns `void` on success. Conformance: Complete. No error codes beyond
the common set (ERR-061).

**Spec:** `Ssh.DeleteRole — 10-ssh-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SshOperations.cs:250`*

#### `SignAsync(role, request, mount, options, cancellationToken)`

Signs a public key as a CA-mode SSH certificate: `POST {mount}/sign/{role}`. SSH-001:
`request`'s `PublicKey` empty or whitespace
raises `BV-INPUT-001` before any request is sent.

Wire params: `role`/`mount` build the route; body carries
`request`'s `PublicKey` (required), `ValidPrincipals` (CSV on the
wire), `Ttl`, `CertType`, `KeyId`, `Extensions`, `CriticalOptions`.
Returns <see cref="SignedSshCertificate"/>, never `null`. Conformance:
Complete (SSH-001). Errors beyond the common set (ERR-061): `BV-INPUT-001` (empty
public key, client- or server-side), `BV-SSH-001 CaNotConfigured`,
`BV-SSH-002 RoleNotFound`, `BV-SSH-005 WrongRoleMode`,
`BV-SSH-006 PqcOnlyClassicalCa`, `BV-AUTHZ-004 PrincipalNotAllowed`,
`BV-SERVER-005` (CA key load or cert sign failure).

**Spec:** `Ssh.Sign — SSH-001`

*Source: `dotnet/BastionVault.IntegrationSdk/SshOperations.cs:279`*

#### `WriteCertificateFile(signedKey, path)`

SSH-002: writes `signedKey` verbatim to `{path}-cert.pub` with
`0644`, applied at file creation (D-M9-12). Not a network operation: makes no request.

HTTP call: none — a client-side file writer. Wire params: none; `signedKey`
and `path` are used locally. Returns `void` on success, or
throws on a file-system failure (not a <see cref="BastionVaultException"/>). Conformance:
Complete (SSH-002). No error codes beyond the common set (ERR-061); this member raises no
<see cref="BastionVaultException"/> at all, since it makes no request.

**Spec:** `Ssh.WriteCertificateFile — SSH-002`

*Source: `dotnet/BastionVault.IntegrationSdk/SshOperations.cs:306`*

#### `CredsAsync(role, ip, username, ttl, mount, options, cancellationToken)`

Issues a one-time-password SSH credential: `POST {mount}/creds/{role}`. SSH-003:
`ip` is validated as an IP literal client-side, raising
`BV-INPUT-001` before any request is sent.

Wire params: `role`/`mount` build the route; body carries
`ip` (required), `username` (optional),
`ttl` (optional, seconds on the wire). Returns <see cref="SshCredentials"/>,
never `null`; `Key` is the OTP (redacting). Conformance: Complete
(SSH-003). Errors beyond the common set (ERR-061): `BV-INPUT-001` (invalid IP,
client- or server-side), `BV-SSH-002 RoleNotFound`, `BV-SSH-003 IpNotAllowed`,
`BV-SSH-005 WrongRoleMode`.

**Spec:** `Ssh.Creds — SSH-003`

*Source: `dotnet/BastionVault.IntegrationSdk/SshOperations.cs:330`*

#### `VerifyAsync(otp, mount, options, cancellationToken)`

Verifies (and consumes) a one-time password: `POST {mount}/verify`. D-M9-20:
`otp` is secret material and travels in the request body, never a path
segment or query string. An invalid or expired otp is a server-recognised failure
(`BV-SSH-004`), not a null result; a genuinely absent response shapes to
`null`, the same convention as <see cref="ReadRoleAsync"/>.

Wire params: `mount` builds the route; body carries `otp`
(required, revealed only on the wire). Returns <see cref="SshOtpVerification"/>, or
`null` for a genuinely absent response. Conformance: Complete. Errors
beyond the common set (ERR-061): `BV-SSH-004 InvalidOtp` for an invalid or expired
otp.

**Spec:** `Ssh.Verify — 10-ssh-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SshOperations.cs:371`*

#### `LookupAsync(ip, username, mount, options, cancellationToken)`

Looks up the roles an IP/username pair is permitted to request OTP credentials for:
`POST {mount}/lookup`. SSH-003 applies to this route's own `ip` field
exactly as it does to <see cref="CredsAsync"/>'s: validated as an IP literal client-side,
raising `BV-INPUT-001` before any request is sent.

Wire params: `mount` builds the route; body carries `ip`
(required), `username` (optional). Returns the matching role names, an
empty list when there are none, never `null`. Conformance: Complete
(SSH-003). Errors beyond the common set (ERR-061): `BV-INPUT-001` (invalid IP,
client- or server-side).

**Spec:** `Ssh.Lookup — SSH-003`

*Source: `dotnet/BastionVault.IntegrationSdk/SshOperations.cs:398`*

