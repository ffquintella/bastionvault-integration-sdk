# `ResourcesOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/ResourcesOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/ResourcesOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `ResourcesConnectOperations`

#### `MfaBeginAsync(request, mount, options, cancellationToken)`

RSC-001: `POST {mount}/v2/connect/mfa/begin`. No shape given beyond "→ factors" (D-M1c-25).

Wire params: resource, profile_id (<see cref="ConnectMfaBeginRequest"/>). Returns the raw <see cref="Response"/>, which may be `null` for an empty body. Conformance: Level X (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): `BV-INPUT-001` when `Resource` is empty (RSC-001, refused client-side before any request is sent); `BV-AUTH-002` for an unauthenticated caller (RSC-001).

**Spec:** `Resources.Connect.MfaBegin — RSC-001`

*Source: `dotnet/BastionVault.IntegrationSdk/ResourcesOperations.cs:284`*

#### `MfaVerifyAsync(request, mount, options, cancellationToken)`

RSC-001: `POST {mount}/v2/connect/mfa/verify` → `{connect_ticket}`, single-use and redacting.

Wire params: resource, profile_id, method, totp_code?, credential? (<see cref="ConnectMfaVerifyRequest"/>). Returns <see cref="ConnectMfaVerifyResult"/>, never `null`; `ConnectTicket` is a single-use, redacting <see cref="SecretString"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): `BV-INPUT-001` when `Resource` is empty (RSC-001, refused client-side); `BV-AUTH-002` for an unauthenticated caller; `BV-AUTH-016` when second-factor verification fails (RSC-001).

**Spec:** `Resources.Connect.MfaVerify — RSC-001`

*Source: `dotnet/BastionVault.IntegrationSdk/ResourcesOperations.cs:299`*

#### `AuthorizeAsync(request, mount, options, cancellationToken)`

RSC-001: `POST {mount}/v2/connect/authorize`. R-33 guard: the ticket travels in this
POST body only, never a query string (`SerialiseAuthorize`).

Wire params: resource, profile_id, connect_ticket? (<see cref="ConnectAuthorizeRequest"/>). Returns the raw <see cref="Response"/>, which may be `null` for an empty body. Conformance: Level X (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): `BV-INPUT-001` when `Resource` is empty (RSC-001, refused client-side); `BV-AUTH-002` for an unauthenticated caller (RSC-001).

**Spec:** `Resources.Connect.Authorize — RSC-001`

*Source: `dotnet/BastionVault.IntegrationSdk/ResourcesOperations.cs:319`*

### `ResourcesOperations`

#### `Secrets`

12 §Resources: `{mount}/secrets/{resource}/*`. RSC-002: values are redacting.

*Source: `dotnet/BastionVault.IntegrationSdk/ResourcesOperations.cs:27`*

#### `Connect`

12 §Resources: `{mount}/v2/connect/*`, the connect-MFA flow (RSC-001).

*Source: `dotnet/BastionVault.IntegrationSdk/ResourcesOperations.cs:30`*

#### `ReadTypesAsync(mount, options, cancellationToken)`

Reads the resource-type schema: `GET {mount}/config/types`. 12 names no field set for the schema.

Wire params: none beyond `mount`. Returns the schema as a raw field map, or `null` when none is set (404 treated as absent). Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Resources.ReadTypes — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/ResourcesOperations.cs:35`*

#### `WriteTypesAsync(schema, mount, options, cancellationToken)`

Writes the resource-type schema: `POST {mount}/config/types`. `schema` is sent verbatim (D-M1c-25).

Wire params: schema is sent verbatim as the body. Returns `void`. Conformance: Level X (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): `BV-INPUT-001` when `schema` is undefined.

**Spec:** `Resources.WriteTypes — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/ResourcesOperations.cs:48`*

#### `ListAsync(mount, options, cancellationToken)`

Lists resource names: `LIST {mount}/resources/`.

Wire params: none beyond `mount`. Returns the resource names, empty (never `null`) when none exist or the path is absent. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Resources.List — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/ResourcesOperations.cs:60`*

#### `SearchAsync(query, mount, options, cancellationToken)`

Searches resources by query: `POST {mount}/resources/search`. Body, not a query string (the spec's own note).

Wire params: q?, type?, offset?, limit?, all omitted when unset (<see cref="ResourceSearchQuery"/>). Returns the raw <see cref="Response"/>, which may be `null` for an empty body. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Resources.Search — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/ResourcesOperations.cs:73`*

#### `ReadAsync(name, mount, options, cancellationToken)`

Reads a resource record: `GET {mount}/resources/{name}`. RSC-002: not redacted.

Wire params: none beyond `name`/`mount`. Returns the raw <see cref="Response"/>, or `null` when `name` is not found (404 treated as absent). RSC-002: this record is plain and unredacted, unlike <see cref="ResourcesSecretsOperations"/>'s values. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Resources.Read — RSC-002`

*Source: `dotnet/BastionVault.IntegrationSdk/ResourcesOperations.cs:86`*

#### `WriteAsync(name, record, mount, options, cancellationToken)`

Creates or replaces a resource record: `PUT {mount}/resources/{name}`. `record` is sent verbatim.

Wire params: record is sent verbatim as the body. Returns the raw <see cref="Response"/>, which may be `null` for an empty body. Conformance: Level X (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): `BV-INPUT-001` when `record` is undefined.

**Spec:** `Resources.Write — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/ResourcesOperations.cs:99`*

#### `DeleteAsync(name, mount, options, cancellationToken)`

Deletes a resource record: `DELETE {mount}/resources/{name}`.

Wire params: none beyond `name`/`mount`. Returns `void`. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Resources.Delete — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/ResourcesOperations.cs:112`*

#### `HistoryAsync(name, mount, options, cancellationToken)`

Reads a resource's change history: `GET {mount}/resources/{name}/history`. No shape beyond the array itself.

Wire params: none beyond `name`/`mount`. Returns an empty list when there is no history, never `null`; each entry is a raw <see cref="JsonElement"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Resources.History — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/ResourcesOperations.cs:125`*

#### `RenameAsync(name, newName, mount, options, cancellationToken)`

Renames a resource, migrating its secrets, shares, groups and ownership: `POST {mount}/resources/{name}/rename` with `{"new_name": newName}`.

Wire params: new_name from `newName`. Returns `void`. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Resources.Rename — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/ResourcesOperations.cs:139`*

### `ResourcesSecretsOperations`

#### `ListAsync(resource, mount, options, cancellationToken)`

Lists a resource's secret keys: `LIST {mount}/secrets/{resource}/`.

Wire params: none beyond `resource`/`mount`. Returns the secret keys, empty (never `null`) when none exist or the path is absent. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Resources.Secrets.List — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/ResourcesOperations.cs:167`*

#### `ReadAsync(resource, key, mount, options, cancellationToken)`

RSC-002: `GET {mount}/secrets/{resource}/{key}`. Each field's value comes back redacting.

Wire params: none beyond `resource`/`key`/`mount`. Returns <see cref="ResourceSecret"/>, or `null` when not found (404 treated as absent); RSC-002: each field's value comes back redacting via <see cref="SecretString"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Resources.Secrets.Read — RSC-002`

*Source: `dotnet/BastionVault.IntegrationSdk/ResourcesOperations.cs:182`*

#### `WriteAsync(resource, key, value, mount, options, cancellationToken)`

Writes a secret value: `PUT {mount}/secrets/{resource}/{key}`. `value` is sent verbatim (D-M1c-25).

Wire params: value is sent verbatim as the body. Returns the raw <see cref="Response"/>, which may be `null` for an empty body. Conformance: Level X (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): `BV-INPUT-001` when `value` is undefined.

**Spec:** `Resources.Secrets.Write — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/ResourcesOperations.cs:203`*

#### `DeleteAsync(resource, key, mount, options, cancellationToken)`

Deletes a secret value: `DELETE {mount}/secrets/{resource}/{key}`.

Wire params: none beyond `resource`/`key`/`mount`. Returns `void`. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Resources.Secrets.Delete — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/ResourcesOperations.cs:217`*

#### `HistoryAsync(resource, key, mount, options, cancellationToken)`

Reads a secret's change history: `GET {mount}/secrets/{resource}/{key}/history`. No shape beyond the array itself.

Wire params: none beyond `resource`/`key`/`mount`. Returns an empty list when there is no history, never `null`; each entry is a raw <see cref="JsonElement"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Resources.Secrets.History — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/ResourcesOperations.cs:231`*

#### `ReadVersionAsync(resource, key, version, mount, options, cancellationToken)`

RSC-002: `GET {mount}/secrets/{resource}/{key}/version/{n}`. Each field's value comes back redacting.

Wire params: version builds the route, no body. Returns <see cref="ResourceSecret"/>, or `null` when not found (404 treated as absent); RSC-002: each field's value comes back redacting via <see cref="SecretString"/>, the same as this type's Read. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Resources.Secrets.ReadVersion — RSC-002`

*Source: `dotnet/BastionVault.IntegrationSdk/ResourcesOperations.cs:246`*

