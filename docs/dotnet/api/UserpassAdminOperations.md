# `UserpassAdminOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/UserpassAdminOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/UserpassAdminOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `UserSummary`

#### `Username`

The wire `username` field; falls back to the listing's own key when the server omits it.

*Source: `dotnet/BastionVault.IntegrationSdk/UserpassAdminOperations.cs:26`*

#### `Fido2Enabled`

The wire `fido2_enabled` flag.

*Source: `dotnet/BastionVault.IntegrationSdk/UserpassAdminOperations.cs:29`*

### `UserpassAdminOperations`

#### `ListUsersAsync(mount, options, cancellationToken)`

Appendix A: `LIST auth/{mount}/users/` — every userpass username on the mount.

Wire params: `mount` builds the route; no body. Returns an empty list when there are none (TRN-050), never `null`. Conformance: Standard. No error codes beyond the common set (ERR-061).

**Spec:** `Auth.Userpass.Admin.ListUsers — 05-authentication.md`

*Source: `dotnet/BastionVault.IntegrationSdk/UserpassAdminOperations.cs:51`*

#### `ListUsersInfoAsync(mount, after, limit, options, cancellationToken)`

14 §Bulk metadata listings: `GET auth/{mount}/users-info?after=&amp;limit=`
(PAG-001…PAG-003, PAG-005), D-M8-7's Userpass half of the two areas M8 wires. Relocated here
verbatim from <see cref="UserpassOperations"/> by R-29 (D-M10-3) — a breaking rename on an
API never published to a package registry, so no released consumer is broken (CRS-004).

Wire params: pinned to `v2` (14 §Bulk metadata listings); query carries `after` (previous page's `Next`, verbatim) and `limit` (defaulted to 100, validated `1-500`, PAG-001). Returns a <see cref="Page{T}"/> of <see cref="UserSummary"/>, never `null`. Conformance: Standard. Errors beyond the common set (ERR-061): `BV-INPUT-004` (limit out of range, PAG-001); `BV-PROTOCOL-002` (records/keys length mismatch, PAG-005).

**Spec:** `Auth.Userpass.Admin.ListUsersInfo — PAG-001`

*Source: `dotnet/BastionVault.IntegrationSdk/UserpassAdminOperations.cs:64`*

#### `ListUsersInfoAllAsync(mount, limit, maxRecords, options, cancellationToken)`

PAG-004: <see cref="ListUsersInfoAsync"/>'s iterator, following
`ListNamespacesInfoAllAsync`'s shape exactly — both are the same
shared machinery (<see cref="PagingWire.IteratePagesAsync{T}"/>). Relocated here verbatim
alongside <see cref="ListUsersInfoAsync"/> by R-29 (D-M10-3).

HTTP call: none directly — repeatedly delegates to <see cref="ListUsersInfoAsync"/>, one call per page. Wire params: as that call's, plus `maxRecords` (client-side cap, no wire effect). Returns an async sequence, never `null`, terminating when the server reports no further page. Conformance: Standard (PAG-004). Errors beyond the common set (ERR-061): `BV-INPUT-005` (`maxRecords` exceeded, client-side).

**Spec:** `Auth.Userpass.Admin.ListUsersInfoAll — PAG-004`

*Source: `dotnet/BastionVault.IntegrationSdk/UserpassAdminOperations.cs:123`*

#### `ReadUserAsync(username, mount, options, cancellationToken)`

Appendix A: `GET auth/{mount}/users/{username}` — the user document.

Wire params: `mount`, `username` build the route; no body. Returns `null` on a `404` with an empty body. The response's field set is not pinned by Appendix A (D-M6-5); nothing in the specification suggests the password is echoed back on read. Conformance: Standard. No error codes beyond the common set (ERR-061).

**Spec:** `Auth.Userpass.Admin.ReadUser — 05-authentication.md`

*Source: `dotnet/BastionVault.IntegrationSdk/UserpassAdminOperations.cs:148`*

#### `WriteUserAsync(username, user, mount, options, cancellationToken)`

Appendix A: `POST auth/{mount}/users/{username}` — creates or overwrites the user, including its password and policies.

Wire params: `user` sent verbatim as the body (Appendix A gives no field set, D-M6-5). Writes credential material — a password field in `user` travels as plain JSON, not <see cref="SecretString"/>, because the whole document is caller-shaped; a caller holding a password separately should route it through <see cref="SetPasswordAsync"/> instead. Policy attachment is measured to use `token_policies`, not `policies` (DR-0021 F5). Returns the write response, or `null` on an empty body. Conformance: Standard. No error codes beyond the common set (ERR-061).

**Spec:** `Auth.Userpass.Admin.WriteUser — 05-authentication.md`

*Source: `dotnet/BastionVault.IntegrationSdk/UserpassAdminOperations.cs:156`*

#### `DeleteUserAsync(username, mount, options, cancellationToken)`

Appendix A: `DELETE auth/{mount}/users/{username}` — deletes the user and its stored credential.

Wire params: `mount`, `username` build the route; no body. Returns nothing; an already-absent user is not an error. Conformance: Standard. No error codes beyond the common set (ERR-061).

**Spec:** `Auth.Userpass.Admin.DeleteUser — 05-authentication.md`

*Source: `dotnet/BastionVault.IntegrationSdk/UserpassAdminOperations.cs:164`*

#### `SetPasswordAsync(username, password, mount, options, cancellationToken)`

Appendix A: `POST auth/{mount}/users/{username}/password` — rotates the user's
password. Appendix A gives no schema, but the name pins the one field, so this takes a
<see cref="SecretString"/> directly rather than raw JSON —
`CustomSecretIdAsync`'s precedent for a named-not-guessed
secret.

Wire params: body `{"password": password}`. Writes credential material; the response, if any, does not need to and is not asserted to echo the password back (Appendix A gives no field set, D-M6-5). Returns the write response, or `null` on an empty body. Conformance: Standard. No error codes beyond the common set (ERR-061).

**Spec:** `Auth.Userpass.Admin.SetPassword — 05-authentication.md`

*Source: `dotnet/BastionVault.IntegrationSdk/UserpassAdminOperations.cs:178`*

#### `UnlockAsync(username, mount, options, cancellationToken)`

Appendix A: `POST auth/{mount}/users/{username}/unlock`, no body — restores a login
AUT-032 locked (`15-testing-requirements.md:206`'s M12 integration scenario).

Wire params: `mount`, `username` build the route; no body. Returns the write response, or `null` on an empty body. This call does not raise `BV-AUTH-006`; that code is raised on the locked account's own login attempt, per AUT-032. Conformance: Standard (AUT-032). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.Userpass.Admin.Unlock — AUT-032`

*Source: `dotnet/BastionVault.IntegrationSdk/UserpassAdminOperations.cs:191`*

#### `ReadFido2Async(username, mount, options, cancellationToken)`

Appendix A: `GET auth/{mount}/users/{username}/fido2` — the user's registered FIDO2 credential metadata.

Wire params: `mount`, `username` build the route; no body. Returns `null` on a `404` with an empty body. FIDO2 registration is public-key-based; the response's field set is not pinned by Appendix A (D-M6-5), but a FIDO2 credential's private key never leaves the authenticator, so this call cannot return one. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Auth.Userpass.Admin.ReadFido2 — 05-authentication.md`

*Source: `dotnet/BastionVault.IntegrationSdk/UserpassAdminOperations.cs:199`*

#### `DeleteFido2Async(username, mount, options, cancellationToken)`

Appendix A: `DELETE auth/{mount}/users/{username}/fido2` — removes the user's registered FIDO2 credential; the user can no longer authenticate via FIDO2 afterwards.

Wire params: `mount`, `username` build the route; no body. Returns nothing; an already-absent credential is not an error. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Auth.Userpass.Admin.DeleteFido2 — 05-authentication.md`

*Source: `dotnet/BastionVault.IntegrationSdk/UserpassAdminOperations.cs:207`*

#### `ReadLockoutAsync(mount, options, cancellationToken)`

Appendix A: `GET auth/{mount}/config/lockout` — the mount-wide account-lockout policy (AUT-032's threshold and duration).

Wire params: `mount` builds the route; no body. Returns `null` on a `404` with an empty body. Conformance: Standard. No error codes beyond the common set (ERR-061).

**Spec:** `Auth.Userpass.Admin.ReadLockout — 05-authentication.md`

*Source: `dotnet/BastionVault.IntegrationSdk/UserpassAdminOperations.cs:215`*

#### `WriteLockoutAsync(config, mount, options, cancellationToken)`

Appendix A: `POST auth/{mount}/config/lockout` — sets the mount-wide account-lockout policy.

Wire params: `config` sent verbatim as the body (Appendix A gives no field set, D-M6-5). Returns the write response, or `null` on an empty body. Conformance: Standard. No error codes beyond the common set (ERR-061).

**Spec:** `Auth.Userpass.Admin.WriteLockout — 05-authentication.md`

*Source: `dotnet/BastionVault.IntegrationSdk/UserpassAdminOperations.cs:223`*

#### `ReadMfaAsync(mount, options, cancellationToken)`

Appendix A: `GET auth/{mount}/config/mfa` — the mount-wide MFA (TOTP) policy.

Wire params: `mount` builds the route; no body. Returns `null` on a `404` with an empty body. Conformance: Standard. No error codes beyond the common set (ERR-061).

**Spec:** `Auth.Userpass.Admin.ReadMfa — 05-authentication.md`

*Source: `dotnet/BastionVault.IntegrationSdk/UserpassAdminOperations.cs:231`*

#### `WriteMfaAsync(config, mount, options, cancellationToken)`

Appendix A: `POST auth/{mount}/config/mfa` — sets the mount-wide MFA (TOTP) policy.

Wire params: `config` sent verbatim as the body (Appendix A gives no field set, D-M6-5). Returns the write response, or `null` on an empty body. Conformance: Standard. No error codes beyond the common set (ERR-061).

**Spec:** `Auth.Userpass.Admin.WriteMfa — 05-authentication.md`

*Source: `dotnet/BastionVault.IntegrationSdk/UserpassAdminOperations.cs:239`*

