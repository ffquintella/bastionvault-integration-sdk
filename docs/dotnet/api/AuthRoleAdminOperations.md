# `AuthRoleAdminOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/AuthRoleAdminOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/AuthRoleAdminOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `AuthRoleAdminOperations`

#### `ReadConfigAsync(mount, options, cancellationToken)`

AUT-060: `GET auth/{mount}/config`.

Shared implementation for `Auth.Oidc.Admin.Config` and `Auth.Saml.Admin.Config` (D-M6-6); `mount` selects which. Returns `null` on a `404` with an empty body. Conformance: Complete (AUT-060). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.Oidc.Admin.Config — AUT-060`

*Source: `dotnet/BastionVault.IntegrationSdk/AuthRoleAdminOperations.cs:39`*

#### `WriteConfigAsync(config, mount, options, cancellationToken)`

AUT-060: `POST auth/{mount}/config`.

Shared for `Auth.Oidc.Admin.Config`/`Auth.Saml.Admin.Config` (D-M6-6). Wire body: `config` sent verbatim. Conformance: Complete (AUT-060). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.Oidc.Admin.Config — AUT-060`

*Source: `dotnet/BastionVault.IntegrationSdk/AuthRoleAdminOperations.cs:47`*

#### `ListRolesAsync(mount, options, cancellationToken)`

AUT-060: `LIST auth/{mount}/role`. An empty list when there are none (TRN-050).

Shared for `Auth.Oidc.Admin.Roles.List`/`Auth.Saml.Admin.Roles.List` (D-M6-6). Never returns `null`. Conformance: Complete (AUT-060). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.Oidc.Admin.Roles.List — AUT-060`

*Source: `dotnet/BastionVault.IntegrationSdk/AuthRoleAdminOperations.cs:55`*

#### `ReadRoleAsync(name, mount, options, cancellationToken)`

AUT-060: `GET auth/{mount}/role/{name}`.

Shared for `Auth.Oidc.Admin.Roles.Read`/`Auth.Saml.Admin.Roles.Read` (D-M6-6). Returns `null` on a `404` with an empty body. Conformance: Complete (AUT-060). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.Oidc.Admin.Roles.Read — AUT-060`

*Source: `dotnet/BastionVault.IntegrationSdk/AuthRoleAdminOperations.cs:63`*

#### `WriteRoleAsync(name, role, mount, options, cancellationToken)`

AUT-060: `POST auth/{mount}/role/{name}`.

Shared for `Auth.Oidc.Admin.Roles.Write`/`Auth.Saml.Admin.Roles.Write` (D-M6-6). Wire body: `role` sent verbatim. Conformance: Complete (AUT-060). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.Oidc.Admin.Roles.Write — AUT-060`

*Source: `dotnet/BastionVault.IntegrationSdk/AuthRoleAdminOperations.cs:71`*

#### `DeleteRoleAsync(name, mount, options, cancellationToken)`

AUT-060: `DELETE auth/{mount}/role/{name}`.

Shared for `Auth.Oidc.Admin.Roles.Delete`/`Auth.Saml.Admin.Roles.Delete` (D-M6-6). Returns nothing; an absent role is not an error. Conformance: Complete (AUT-060). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.Oidc.Admin.Roles.Delete — AUT-060`

*Source: `dotnet/BastionVault.IntegrationSdk/AuthRoleAdminOperations.cs:79`*

