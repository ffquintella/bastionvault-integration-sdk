# `IdentityOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `DefaultAccountOperations`

#### `ReadSelfAsync(options, cancellationToken)`

Reads the calling identity's own default account: `GET /v2/sys/identity/default-account/self`.
⚠️ This is the only operation on this surface the server ever fills
`WindowsPassword` on, and only for the record's own owner.

Wire params: none. Returns the <see cref="DefaultAccount"/>, or `null` when none is set. Conformance: Complete (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Identity.DefaultAccount.ReadSelf — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:253`*

#### `WriteSelfAsync(spec, options, cancellationToken)`

Writes the calling identity's own default account: `POST /v2/sys/identity/default-account/self`.

Wire params: body carries `username`/`domain`/`windows_password`, each omitted when unset on `spec`. Returns no value. Conformance: Complete (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Identity.DefaultAccount.WriteSelf — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:261`*

#### `ReadAsync(mount, name, options, cancellationToken)`

Reads another identity's default account (admin): `GET /v2/sys/identity/default-account/{mount}/{name}`. `WindowsPassword` is never filled here.

Wire params: `mount`/`name` build the route. Returns the <see cref="DefaultAccount"/>, or `null` when none is set. Conformance: Complete (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): `BV-INPUT-001` for an empty or all-slash `mount`/`name`.

**Spec:** `Identity.DefaultAccount.Read — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:269`*

#### `WriteAsync(mount, name, spec, options, cancellationToken)`

Writes another identity's default account (admin): `POST /v2/sys/identity/default-account/{mount}/{name}`.

Wire params: `mount`/`name` build the route; body carries `username`/`domain`/`windows_password`, each omitted when unset on `spec`. Returns no value. Conformance: Complete (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): `BV-INPUT-001` for an empty or all-slash `mount`/`name`.

**Spec:** `Identity.DefaultAccount.Write — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:277`*

### `IdentityGroupOperations`

#### `ListAsync(kind, options, cancellationToken)`

Lists group names of a kind: `LIST identity/group/{kind}`.

Wire params: `kind` builds the route. Returns an empty list when there are none, never `null`. Conformance: Complete. Errors beyond the common set (ERR-061): `BV-INPUT-001` for a `..` segment in `kind`.

**Spec:** `Identity.Groups.List — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:580`*

#### `ReadAsync(kind, name, options, cancellationToken)`

Reads a group: `GET identity/group/{kind}/{name}`.

Wire params: `kind`/`name` build the route. Returns the <see cref="IdentityGroup"/>, or `null` when it does not exist. Conformance: Complete. Errors beyond the common set (ERR-061): `BV-INPUT-001` for a `..` segment in `kind`/`name`.

**Spec:** `Identity.Groups.Read — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:591`*

#### `WriteAsync(kind, name, spec, options, cancellationToken)`

Creates or replaces a group: `PUT identity/group/{kind}/{name}` with `{description, members[], policies[]}`.

Wire params: `kind`/`name` build the route; body carries `description`/`members`/`policies`, each omitted when unset on `spec`. Returns no value. Conformance: Complete. Errors beyond the common set (ERR-061): `BV-INPUT-001` for a `..` segment in `kind`/`name`.

**Spec:** `Identity.Groups.Write — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:602`*

#### `DeleteAsync(kind, name, options, cancellationToken)`

Deletes a group: `DELETE identity/group/{kind}/{name}`.

Wire params: `kind`/`name` build the route. Returns no value; deleting an absent group is not an error. Conformance: Complete. Errors beyond the common set (ERR-061): `BV-INPUT-001` for a `..` segment in `kind`/`name`.

**Spec:** `Identity.Groups.Delete — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:613`*

#### `HistoryAsync(kind, name, options, cancellationToken)`

Reads a group's change history: `GET identity/group/{kind}/{name}/history`. No documented shape beyond the array itself.

Wire params: `kind`/`name` build the route. Returns an empty list when there is no history, never `null`. Conformance: Complete. Errors beyond the common set (ERR-061): `BV-INPUT-001` for a `..` segment in `kind`/`name`.

**Spec:** `Identity.Groups.History — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:623`*

### `IdentityOperations`

#### `Profile`

SYS-080: `/v2/sys/identity/profile/self[…]` — read, change password, update contact.

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:49`*

#### `DefaultAccount`

SYS-080: `/v2/sys/identity/default-account[…]` — the self and admin forms.

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:52`*

#### `SshSecurityKey`

SYS-080: `/v2/sys/identity/ssh-security-key[…]`.

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:55`*

#### `NamespaceAssignment`

SYS-080: `/v2/sys/identity/ns-assignment[…]` — the login restriction.

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:58`*

#### `Groups`

12: `identity/group/{user|app}/*` — the user- and app-group surface.

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:61`*

#### `Sharing`

12: `identity/sharing/*` — direct grants (IDN-001) and the three list forms (IDN-002).

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:64`*

#### `Owner`

12: `identity/owner/{kv|file|resource}/*` — ownership records.

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:67`*

#### `SelfAsync(options, cancellationToken)`

Reads the calling token's own entity, lazily provisioning it if needed: `GET identity/entity/self`.
Unlike every other member on this class, this route is not `/v2`-pinned — it is
section 12's own `identity/` mount, not SYS-080's `sys/identity/*`.

Wire params: none. Returns the <see cref="EntitySelf"/>, never `null`. Conformance: Standard. No error codes beyond the common set (ERR-061).

**Spec:** `Identity.Self — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:76`*

#### `AliasesAsync(options, cancellationToken)`

Lists the calling entity's aliases: `GET identity/entity/aliases`. No documented shape
beyond the array itself (D-M1c-25), so each entry is a raw <see cref="JsonElement"/> rather
than a guessed type.

Wire params: none. Returns an empty list when there are none, never `null`. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Identity.Aliases — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:107`*

### `IdentityOwnerOperations`

#### `ReadAsync(kind, id, options, cancellationToken)`

Reads an ownership record: `GET identity/owner/{kind}/{id}`, `kind ∈ kv | file | resource`.

Wire params: `kind`/`id` build the route. Returns the raw <see cref="Response"/>, or `null` when it does not exist. Conformance: Complete. Errors beyond the common set (ERR-061): `BV-INPUT-001` for a `..` segment in `kind`/`id`.

**Spec:** `Identity.Owner.Read — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:750`*

#### `WriteAsync(kind, id, spec, options, cancellationToken)`

Writes an ownership record: `PUT identity/owner/{kind}/{id}`.

Wire params: `kind`/`id` build the route; body is the caller-supplied `spec` verbatim. Returns the raw <see cref="Response"/>, which may be `null` for an empty body. Conformance: Complete. Errors beyond the common set (ERR-061): `BV-INPUT-001` for a `..` segment in `kind`/`id` or an undefined `spec`.

**Spec:** `Identity.Owner.Write — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:760`*

#### `DeleteAsync(kind, id, options, cancellationToken)`

Deletes an ownership record: `DELETE identity/owner/{kind}/{id}`.

Wire params: `kind`/`id` build the route. Returns no value; deleting an absent record is not an error. Conformance: Complete. Errors beyond the common set (ERR-061): `BV-INPUT-001` for a `..` segment in `kind`/`id`.

**Spec:** `Identity.Owner.Delete — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:770`*

### `IdentityProfileOperations`

#### `ReadAsync(options, cancellationToken)`

Reads the calling token's own profile: `GET /v2/sys/identity/profile/self`. The table
states this route never 404s, so the return is non-nullable; a body-less response is
`BV-PROTOCOL-002` rather than an invented empty profile (D-M1c-25).

Wire params: none. Returns the <see cref="IdentityProfile"/>, never `null`. Conformance: Complete (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): `BV-PROTOCOL-002` for a body-less response.

**Spec:** `Identity.Profile.Read — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:133`*

#### `ChangePasswordAsync(currentPassword, newPassword, options, cancellationToken)`

Changes the calling token's own password: `POST /v2/sys/identity/profile/self/password`
with `{"current_password", "new_password"}`. A `400` reaches the caller as
`BV-INPUT-100` and a `403` as `BV-AUTHZ-001`, both through the shared status
mapping with no operation-local remap (the same situation as D-M7-18, not D-M7-6's).

Wire params: body carries `current_password`/`new_password`. Returns no value. Conformance: Complete (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): `BV-INPUT-100` for a rejected password.

**Spec:** `Identity.Profile.ChangePassword — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:160`*

#### `UpdateContactAsync(email, phone, options, cancellationToken)`

Updates the calling token's own contact fields: `POST /v2/sys/identity/profile/self/contact`, write-preserve.

⚠️ The tri-state is the whole requirement and it is preserved onto the wire:
`null` omits the key and keeps the stored value, `""` is sent as
an empty string and clears it, and any other value replaces it. Modelling either
parameter as a non-nullable `string` would collapse "keep" and "clear" into one
request, which is the defect the requirement exists to prevent — the same shape as
SYS-045's tri-state (D-M7-17) and decided in the same way. Wire params: `email`/`phone`,
each omitted when `null`. Returns no value. Conformance: Complete
(SYS-045 tri-state precedent; Appendix A groups this mount, no per-operation row). No
error codes beyond the common set (ERR-061).

**Spec:** `Identity.Profile.UpdateContact — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:189`*

### `IdentitySharingOperations`

#### `GetAsync(kind, target, grantee, options, cancellationToken)`

Reads a direct sharing grant: `GET identity/sharing/by-target/{kind}/{b64url target}/{grantee}` (IDN-001).

Wire params: `kind`/`grantee` build the route; `target` is base64url-encoded client-side (IDN-001). Returns the raw <see cref="JsonElement"/>, or `null` when there is no grant. Conformance: Complete (IDN-001). No error codes beyond the common set (ERR-061).

**Spec:** `Identity.Sharing.Get — IDN-001`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:648`*

#### `PutAsync(kind, target, grantee, spec, options, cancellationToken)`

Puts a direct sharing grant: `PUT identity/sharing/by-target/{kind}/{b64url target}/{grantee}` (IDN-001).
The SDK performs the base64url encoding of `target` itself; pass the plain
path (e.g. `secret/app/db`), never a pre-encoded string.

Wire params: `kind`/`grantee` build the route, `target` is base64url-encoded client-side; body carries `target_kind`/`target_path` (always sent) and `grantee_kind`/`capabilities`/`expires_at` (omitted when unset) (IDN-001). Returns no value. Conformance: Complete (IDN-001). No error codes beyond the common set (ERR-061).

**Spec:** `Identity.Sharing.Put — IDN-001`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:663`*

#### `DeleteAsync(kind, target, grantee, options, cancellationToken)`

Deletes a direct sharing grant: `DELETE identity/sharing/by-target/{kind}/{b64url target}/{grantee}` (IDN-001).

Wire params: `kind`/`grantee` build the route; `target` is base64url-encoded client-side (IDN-001). Returns no value; deleting an absent grant is not an error. Conformance: Complete (IDN-001). No error codes beyond the common set (ERR-061).

**Spec:** `Identity.Sharing.Delete — IDN-001`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:682`*

#### `ListByTargetAsync(kind, target, options, cancellationToken)`

Lists grantees for a target: `LIST identity/sharing/by-target/{kind}/{target}`. Applies
the same client-side base64url encoding as <see cref="GetAsync"/>/<see cref="PutAsync"/>/
<see cref="DeleteAsync"/> (IDN-001): the route has the identical `by-target/{kind}/{target}`
shape, and an unencoded multi-segment `target` would otherwise change the route.

Wire params: `kind` builds the route; `target` is base64url-encoded client-side (IDN-001). Returns an empty list when there are none, never `null`. Conformance: Complete (IDN-001). No error codes beyond the common set (ERR-061).

**Spec:** `Identity.Sharing.ListByTarget — IDN-001`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:697`*

#### `ListByGranteeAsync(grantee, options, cancellationToken)`

Lists targets shared to a grantee: `LIST identity/sharing/by-grantee/{grantee}`.

Wire params: `grantee` builds the route. Returns an empty list when there are none, never `null`. Conformance: Complete. Errors beyond the common set (ERR-061): `BV-INPUT-001` for a `..` segment in `grantee`.

**Spec:** `Identity.Sharing.ListByGrantee — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:708`*

#### `ForMeAsync(options, cancellationToken)`

Lists resources shared to the caller: `LIST identity/sharing/for-me` → `{entity_id,
group_shared_resources, entries[]}` (IDN-002). See <see cref="IdentitySharingForMe"/>'s
remarks for the group-share filter this SDK documents but does not itself enforce.

Wire params: none. Returns the <see cref="IdentitySharingForMe"/>, never `null`. Conformance: Complete (IDN-002). No error codes beyond the common set (ERR-061).

**Spec:** `Identity.Sharing.ForMe — IDN-002`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:723`*

### `NamespaceAssignmentOperations`

#### `ListAsync(options, cancellationToken)`

Lists namespace-assignment record names: `LIST /v2/sys/identity/ns-assignment` → `{"keys": [...]}`.

Wire params: none. Returns an empty list when there are none, never `null`. Conformance: Complete (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Identity.NamespaceAssignment.List — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:488`*

#### `ReadAsync(mount, name, options, cancellationToken)`

Reads a namespace assignment: `GET /v2/sys/identity/ns-assignment/{mount}/{name}`, or `null` when there is no assignment.

Wire params: `mount`/`name` build the route. Returns the <see cref="NamespaceAssignment"/>, or `null` when none is set. Conformance: Complete (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): `BV-INPUT-001` for an empty or all-slash `mount`/`name`.

**Spec:** `Identity.NamespaceAssignment.Read — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:499`*

#### `WriteAsync(mount, name, namespaces, defaultNamespace, options, cancellationToken)`

Writes a namespace assignment (the login restriction): `POST /v2/sys/identity/ns-assignment/{mount}/{name}` with
`{"namespaces": [...], "default_namespace"?}`.

`namespaces` is always written, including when empty: this is a login
restriction, so an empty list and an omitted key would differ in effect and only the
list the caller passed is knowable here. `defaultNamespace` is omitted when
`null`. Wire params: `mount`/`name` build the route. Returns no
value. Conformance: Complete (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061):
`BV-INPUT-001` for an empty or all-slash `mount`/`name`.

**Spec:** `Identity.NamespaceAssignment.Write — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:530`*

### `SshSecurityKeyOperations`

#### `ListAsync(options, cancellationToken)`

Lists SSH security-key record names: `LIST /v2/sys/identity/ssh-security-key` → `{"keys": [...]}`.

Wire params: none. Returns an empty list when there are none, never `null`. Conformance: Complete (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Identity.SshSecurityKey.List — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:355`*

#### `ReadSelfAsync(options, cancellationToken)`

Reads the calling identity's own SSH security key: `GET /v2/sys/identity/ssh-security-key/self`.

Wire params: none. Returns the <see cref="SshSecurityKey"/>, or `null` when none is set. Conformance: Complete (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Identity.SshSecurityKey.ReadSelf — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:366`*

#### `WriteSelfAsync(spec, options, cancellationToken)`

Writes the calling identity's own SSH security key: `POST /v2/sys/identity/ssh-security-key/self`.

Wire params: body carries `name`/`public_key`, each omitted when unset on `spec`. Returns no value. Conformance: Complete (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Identity.SshSecurityKey.WriteSelf — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:374`*

#### `DeleteSelfAsync(options, cancellationToken)`

Deletes the calling identity's own SSH security key: `DELETE /v2/sys/identity/ssh-security-key/self`.

Wire params: none. Returns no value; deleting an absent key is not an error. Conformance: Complete (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Identity.SshSecurityKey.DeleteSelf — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:382`*

#### `ReadAsync(mount, name, options, cancellationToken)`

Reads another identity's SSH security key (admin): `GET /v2/sys/identity/ssh-security-key/{mount}/{name}`.

Wire params: `mount`/`name` build the route. Returns the <see cref="SshSecurityKey"/>, or `null` when none is set. Conformance: Complete (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): `BV-INPUT-001` for an empty or all-slash `mount`/`name`.

**Spec:** `Identity.SshSecurityKey.Read — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:390`*

#### `WriteAsync(mount, name, spec, options, cancellationToken)`

Writes another identity's SSH security key (admin): `POST /v2/sys/identity/ssh-security-key/{mount}/{name}`.

Wire params: `mount`/`name` build the route; body carries `name`/`public_key`, each omitted when unset on `spec`. Returns no value. Conformance: Complete (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): `BV-INPUT-001` for an empty or all-slash `mount`/`name`.

**Spec:** `Identity.SshSecurityKey.Write — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:398`*

#### `DeleteAsync(mount, name, options, cancellationToken)`

Deletes another identity's SSH security key (admin): `DELETE /v2/sys/identity/ssh-security-key/{mount}/{name}`.

Wire params: `mount`/`name` build the route. Returns no value; deleting an absent key is not an error. Conformance: Complete (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): `BV-INPUT-001` for an empty or all-slash `mount`/`name`.

**Spec:** `Identity.SshSecurityKey.Delete — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs:406`*

