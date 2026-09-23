# `LdapOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/LdapOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/LdapOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `LdapLibraryOperations`

#### `ListAsync(mount, options, cancellationToken)`

`LIST {mount}/library/`.

Wire params: none. Returns the library set names, empty (never `null`) when none exist or the path is absent. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Ldap.Library.List — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/LdapOperations.cs:221`*

#### `ReadAsync(set, mount, options, cancellationToken)`

`GET {mount}/library/{set}`.

Wire params: none beyond `set`/`mount`. Returns <see cref="LdapLibrarySet"/>, or `null` when `set` is not found (404 treated as absent). Carries service account names and TTLs, no passwords. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Ldap.Library.Read — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/LdapOperations.cs:234`*

#### `WriteAsync(set, spec, mount, options, cancellationToken)`

`POST {mount}/library/{set}`.

Wire params (patch-shaped, omitted members untouched): service_account_names[], ttl, max_ttl, disable_check_in_enforcement, affinity_ttl. Returns `void` — no credential travels in either direction. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Ldap.Library.Write — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/LdapOperations.cs:248`*

#### `DeleteAsync(set, mount, options, cancellationToken)`

`DELETE {mount}/library/{set}`.

Wire params: none. Returns `void`. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Ldap.Library.Delete — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/LdapOperations.cs:262`*

#### `CheckOutAsync(set, ttl, mount, options, cancellationToken)`

`POST {mount}/library/{set}/check-out`.

Wire params: ttl (optional). Returns <see cref="LdapLibraryCheckOut"/>, never `null`; `Password` is a <see cref="SecretString"/>-typed credential checked out to the caller — treat it as secret material and check it back in via <see cref="CheckInAsync"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Ldap.Library.CheckOut — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/LdapOperations.cs:275`*

#### `CheckInAsync(set, account, mount, options, cancellationToken)`

`POST {mount}/library/{set}/check-in`.

Wire params: service_account_name (optional). Returns `void`. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Ldap.Library.CheckIn — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/LdapOperations.cs:293`*

#### `StatusAsync(set, mount, options, cancellationToken)`

`GET {mount}/library/{set}/status`.

Wire params: none. Returns <see cref="LdapLibraryStatus"/>, never `null`; `CheckedOut` is a raw wire map keyed by account name (12/Appendix A name no field set for its entries) and `Available` lists account names not checked out — neither carries a password. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Ldap.Library.Status — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/LdapOperations.cs:309`*

### `LdapOperations`

#### `StaticRoles`

12 §LDAP: `{mount}/static-role/*`.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapOperations.cs:24`*

#### `Library`

12 §LDAP: `{mount}/library/*`.

*Source: `dotnet/BastionVault.IntegrationSdk/LdapOperations.cs:27`*

#### `ReadConfigAsync(mount, options, cancellationToken)`

`GET {mount}/config`.

Wire params: none. Returns <see cref="LdapConfig"/>, or `null` when no config is set (404 treated as absent). `BindPass`/`ClientTlsKey` are write-only and always `null` here — the bind password is never echoed back. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Ldap.ReadConfig — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/LdapOperations.cs:32`*

#### `WriteConfigAsync(config, mount, options, cancellationToken)`

`POST {mount}/config`. LDP-001: refused client-side (`BV-INPUT-001`) when `InsecureTls` is set without `AcknowledgeInsecureTls`.

Wire params (patch-shaped, omitted members untouched): url, binddn, bindpass, userdn, directory_type, password_policy, request_timeout, starttls, client_tls_cert, client_tls_key, tls_min_version, insecure_tls, acknowledge_insecure_tls, userattr. Returns `void`. `bindpass`/`client_tls_key` are write-only bind credential material the server never echoes back on read; callers must not log <see cref="LdapConfig"/> unredacted. Conformance: Level X (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): `BV-INPUT-001` (LDP-001's insecure_tls/acknowledge_insecure_tls guard, refused before any request is sent).

**Spec:** `Ldap.WriteConfig — LDP-001`

*Source: `dotnet/BastionVault.IntegrationSdk/LdapOperations.cs:45`*

#### `DeleteConfigAsync(mount, options, cancellationToken)`

`DELETE {mount}/config`.

Wire params: none. Returns `void` — removes the stored bind configuration, including whatever bind password the server holds for it. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Ldap.DeleteConfig — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/LdapOperations.cs:60`*

#### `RotateRootAsync(mount, options, cancellationToken)`

`POST {mount}/rotate-root`.

Wire params: none. Returns `void` — rotates the root bind credential server-side; the new value is never returned to the caller. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Ldap.RotateRoot — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/LdapOperations.cs:72`*

#### `CheckConnectionAsync(mount, options, cancellationToken)`

`GET {mount}/check-connection`.

Wire params: none. Returns <see cref="LdapCheckConnectionResult"/>, never `null`; only `Ok` is guaranteed, other members reflect how far the probe got, and the bind password is never echoed. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061) — a probe failure is reported in the result, not raised.

**Spec:** `Ldap.CheckConnection — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/LdapOperations.cs:84`*

#### `StaticCredAsync(name, mount, options, cancellationToken)`

`GET {mount}/static-cred/{name}`.

Wire params: none beyond `name`/`mount`. Returns <see cref="LdapStaticCred"/>, or `null` when `name` is not found (404 treated as absent). `Password` is a <see cref="SecretString"/>-typed credential the server issues for this static role — treat it as secret material. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Ldap.StaticCred — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/LdapOperations.cs:98`*

#### `RotateRoleAsync(name, mount, options, cancellationToken)`

`POST {mount}/rotate-role/{name}`.

Wire params: none beyond `name`/`mount`. Returns `void` — rotates the named static role's credential server-side without returning it; call <see cref="StaticCredAsync"/> to fetch the new value. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Ldap.RotateRole — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/LdapOperations.cs:113`*

### `LdapStaticRoleOperations`

#### `ListAsync(mount, options, cancellationToken)`

`LIST {mount}/static-role/`.

Wire params: none. Returns the static role names, empty (never `null`) when none exist or the path is absent. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Ldap.StaticRoles.List — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/LdapOperations.cs:144`*

#### `ReadAsync(name, mount, options, cancellationToken)`

`GET {mount}/static-role/{name}`.

Wire params: none beyond `name`/`mount`. Returns <see cref="LdapStaticRole"/>, or `null` when `name` is not found (404 treated as absent). Carries no password (`dn`, `username`, `rotation_period`, `password_policy` only). Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Ldap.StaticRoles.Read — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/LdapOperations.cs:157`*

#### `WriteAsync(name, role, mount, options, cancellationToken)`

`POST {mount}/static-role/{name}`.

Wire params (patch-shaped, omitted members untouched): dn, username, rotation_period, password_policy. Returns `void` — no credential travels in either direction; the server manages the rotated password. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Ldap.StaticRoles.Write — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/LdapOperations.cs:171`*

#### `DeleteAsync(name, mount, options, cancellationToken)`

`DELETE {mount}/static-role/{name}`.

Wire params: none. Returns `void`. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Ldap.StaticRoles.Delete — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/LdapOperations.cs:185`*

