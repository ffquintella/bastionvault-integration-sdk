# `AppIdAdminOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/AppIdAdminOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/AppIdAdminOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `AppIdAdminOperations`

#### `ListRolesAsync(mount, options, cancellationToken)`

AUT-043: `LIST auth/{mount}/role` — every AppID role name on the mount.

Wire params: `mount` builds the route; no body. Returns an empty list when there are none (TRN-050), never `null`. Conformance: Standard (AUT-043). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.AppId.Admin.ListRoles — AUT-043`

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdAdminOperations.cs:110`*

#### `ReadRoleAsync(roleName, mount, options, cancellationToken)`

AUT-043: `GET auth/{mount}/role/{roleName}` — the role document.

Wire params: `mount`, `roleName` build the route; no body. Returns `null` on a `404` with an empty body. Conformance: Standard (AUT-043). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.AppId.Admin.ReadRole — AUT-043`

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdAdminOperations.cs:118`*

#### `WriteRoleAsync(roleName, role, mount, options, cancellationToken)`

AUT-043: `POST auth/{mount}/role/{roleName}` — creates or overwrites the role.

Wire params: `role` sent verbatim as the body (Appendix A gives no field set, D-M6-5). Returns the server's write response, or `null` on an empty body. Conformance: Standard (AUT-043). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.AppId.Admin.WriteRole — AUT-043`

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdAdminOperations.cs:126`*

#### `DeleteRoleAsync(roleName, mount, options, cancellationToken)`

AUT-043: `DELETE auth/{mount}/role/{roleName}` — deletes the role, including its bound machines and outstanding secret ids.

Wire params: `mount`, `roleName` build the route; no body. Returns nothing; an already-absent role is not an error. Conformance: Standard (AUT-043). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.AppId.Admin.DeleteRole — AUT-043`

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdAdminOperations.cs:134`*

#### `WriteRoleIdAsync(roleName, roleId, mount, options, cancellationToken)`

AUT-043: `POST auth/{mount}/role/{roleName}/role-id` — the write half of `ReadRoleIdAsync`, setting a caller-chosen role id.

Wire params: body `{"role_id": roleId}`. A role id is a public identifier paired with a secret id at login, not a secret, so it is a plain string. Returns the write response, or `null` on an empty body. Conformance: Complete (AUT-043). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.AppId.Admin.WriteRoleId — AUT-043`

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdAdminOperations.cs:142`*

#### `ReadFieldAsync(roleName, field, mount, options, cancellationToken)`

AUT-043: `GET auth/{mount}/role/{roleName}/{field}` — reads one role field in isolation.

Wire params: `field` selects the path segment via <see cref="AppIdRoleField"/>; no body. Returns `null` on a `404` with an empty body. Conformance: Complete (AUT-043). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.AppId.Admin.ReadField — AUT-043`

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdAdminOperations.cs:152`*

#### `WriteFieldAsync(roleName, field, value, mount, options, cancellationToken)`

AUT-043: `POST auth/{mount}/role/{roleName}/{field}` — overwrites one role field in isolation.

Wire params: `value` sent verbatim as the body; `field` selects the path segment. Returns the write response, or `null` on an empty body. Conformance: Complete (AUT-043). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.AppId.Admin.WriteField — AUT-043`

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdAdminOperations.cs:160`*

#### `DeleteFieldAsync(roleName, field, mount, options, cancellationToken)`

AUT-043: `DELETE auth/{mount}/role/{roleName}/{field}`, resetting it to the role's default.

Wire params: `field` selects the path segment; no body. Returns nothing. Conformance: Complete (AUT-043). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.AppId.Admin.DeleteField — AUT-043`

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdAdminOperations.cs:168`*

#### `ReadLocalSecretIdsAsync(roleName, mount, options, cancellationToken)`

AUT-043: `GET auth/{mount}/role/{roleName}/local-secret-ids`.

Wire params: `mount`, `roleName` build the route; no body. Returns `null` on a `404` with an empty body. Conformance: Complete (AUT-043). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.AppId.Admin.ReadLocalSecretIds — AUT-043`

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdAdminOperations.cs:176`*

#### `ListSecretIdAccessorsAsync(roleName, mount, options, cancellationToken)`

AUT-043: `LIST auth/{mount}/role/{roleName}/secret-id/` — the accessors, never the secret ids.

Wire params: `mount`, `roleName` build the route; no body. Returns an empty list when there are none (TRN-050), never `null`. The list carries only accessors — a secret id itself is issued once and cannot be re-listed or re-read. Conformance: Standard (AUT-043). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.AppId.Admin.ListSecretIdAccessors — AUT-043`

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdAdminOperations.cs:184`*

#### `LookupSecretIdAsync(roleName, secretId, mount, options, cancellationToken)`

AUT-043: `POST auth/{mount}/role/{roleName}/secret-id/lookup` — presents a secret id to read back its metadata.

Wire params: body `{"secret_id": secretId}`, taken and held as <see cref="SecretString"/> (CNF-031), even though it travels in a request body here rather than a header. Appendix A gives no response field set (D-M6-5); this SDK does not assert whether the secret id itself is echoed back in the response. Returns `null` on a `404` with an empty body. Conformance: Standard (AUT-043). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.AppId.Admin.LookupSecretId — AUT-043`

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdAdminOperations.cs:192`*

#### `DestroySecretIdAsync(roleName, secretId, mount, options, cancellationToken)`

AUT-043: `POST auth/{mount}/role/{roleName}/secret-id/destroy` — revokes the secret id, permanently.

Wire params: body `{"secret_id": secretId}`, taken as <see cref="SecretString"/> (CNF-031). Returns nothing; the destroyed secret id can no longer authenticate. Conformance: Standard (AUT-043). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.AppId.Admin.DestroySecretId — AUT-043`

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdAdminOperations.cs:202`*

#### `LookupSecretIdAccessorAsync(roleName, accessor, mount, options, cancellationToken)`

AUT-043: `POST auth/{mount}/role/{roleName}/secret-id-accessor/lookup` — the accessor-keyed sibling of <see cref="LookupSecretIdAsync"/>, so a caller who never held the secret id can still read its metadata.

Wire params: body `{"secret_id_accessor": accessor}`. `accessor` identifies a secret id without being usable as one, so it is a plain string, not <see cref="SecretString"/>. Appendix A gives no response field set (D-M6-5); the accessor cannot be used to recover the secret id itself. Returns `null` on a `404` with an empty body. Conformance: Standard (AUT-043). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.AppId.Admin.LookupSecretIdAccessor — AUT-043`

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdAdminOperations.cs:215`*

#### `DestroySecretIdAccessorAsync(roleName, accessor, mount, options, cancellationToken)`

AUT-043: `POST auth/{mount}/role/{roleName}/secret-id-accessor/destroy` — the accessor-keyed sibling of <see cref="DestroySecretIdAsync"/>.

Wire params: body `{"secret_id_accessor": accessor}`, plain string. Returns nothing; the destroyed secret id can no longer authenticate. Conformance: Standard (AUT-043). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.AppId.Admin.DestroySecretIdAccessor — AUT-043`

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdAdminOperations.cs:228`*

#### `CustomSecretIdAsync(roleName, secretId, mount, options, cancellationToken)`

AUT-043: `POST auth/{mount}/role/{roleName}/custom-secret-id` — registers a caller-chosen secret id instead of generating one.

Wire params: body `{"secret_id": secretId}`, taken as <see cref="SecretString"/> (CNF-031). Writes credential material the caller already holds; the response, if any, does not need to and is not asserted to echo it back (Appendix A gives no field set, D-M6-5). Returns the write response, or `null` on an empty body. Conformance: Complete (AUT-043). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.AppId.Admin.CustomSecretId — AUT-043`

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdAdminOperations.cs:241`*

#### `ListMachinesAsync(roleName, mount, options, cancellationToken)`

AUT-043: `LIST auth/{mount}/role/{roleName}/machine/` — the machines bound to this role (AUT-040's gate).

Wire params: `mount`, `roleName` build the route; no body. Returns an empty list when there are none (TRN-050), never `null`. Conformance: Complete (AUT-043). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.AppId.Admin.ListMachines — AUT-043`

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdAdminOperations.cs:254`*

#### `BindMachineAsync(roleName, machine, mount, options, cancellationToken)`

AUT-043: `POST auth/{mount}/role/{roleName}/machine/` — binds a machine to the role (AUT-040's gate).

Wire params: `machine` sent verbatim as the body (Appendix A gives no field set, D-M6-5). Returns the write response, or `null` on an empty body. Conformance: Complete (AUT-043). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.AppId.Admin.BindMachine — AUT-043`

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdAdminOperations.cs:262`*

#### `ReadMachineAsync(roleName, machineId, mount, options, cancellationToken)`

AUT-043: `GET auth/{mount}/role/{roleName}/machine/{machineId}`.

Wire params: `mount`, `roleName`, `machineId` build the route; no body. Returns `null` on a `404` with an empty body. Conformance: Complete (AUT-043). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.AppId.Admin.ReadMachine — AUT-043`

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdAdminOperations.cs:270`*

#### `UnbindMachineAsync(roleName, machineId, mount, options, cancellationToken)`

AUT-043: `DELETE auth/{mount}/role/{roleName}/machine/{machineId}` — unbinds the machine; it can no longer log in under AUT-040's gate unless another bound machine matches.

Wire params: `mount`, `roleName`, `machineId` build the route; no body. Returns nothing; an already-unbound machine is not an error. Conformance: Complete (AUT-043). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.AppId.Admin.UnbindMachine — AUT-043`

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdAdminOperations.cs:278`*

#### `ReadConfigAsync(mount, options, cancellationToken)`

AUT-043: `GET auth/{mount}/config`, which carries `require_machine` — the mount-wide half of AUT-040's machine-identity gate, default on.

Wire params: `mount` builds the route; no body. Returns `null` on a `404` with an empty body. Conformance: Standard (AUT-043). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.AppId.Admin.ReadConfig — AUT-043`

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdAdminOperations.cs:286`*

#### `WriteConfigAsync(config, mount, options, cancellationToken)`

AUT-043: `POST auth/{mount}/config` — sets `require_machine` and any other mount-wide config field.

Wire params: `config` sent verbatim as the body (Appendix A gives no field set, D-M6-5). Returns the write response, or `null` on an empty body. Conformance: Standard (AUT-043). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.AppId.Admin.WriteConfig — AUT-043`

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdAdminOperations.cs:294`*

#### `TidySecretIdsAsync(mount, options, cancellationToken)`

AUT-043: `POST auth/{mount}/tidy/secret-id` — removes expired secret ids.

Wire params: `mount` builds the route; no body. Returns the write response, or `null` on an empty body. Conformance: Complete (AUT-043). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.AppId.Admin.TidySecretIds — AUT-043`

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdAdminOperations.cs:302`*

