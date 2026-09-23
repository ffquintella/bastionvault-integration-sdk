# `AppIdOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/AppIdOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/AppIdOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `AppIdOperations`

#### `Admin`

AUT-043's Complete-level role administration surface.

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdOperations.cs:33`*

#### `LoginAsync(roleId, secretId, machineToken, mount, options, cancellationToken)`

AUT-040: `POST auth/{mount}/login` with body
`{"role_id": "…", "secret_id": "…", "machine_token": "…"?}`.

The machine-identity gate is on by default. The server's
`auth/approle/config.require_machine` defaults to on, so a login without
`machineToken` is refused — `BV-AUTH-011 AppIdMachineBinding` — unless
the role has `bypass_machine_binding = true` (which administrators normally pair with
`bound_source_ips`). Obtain the FerroGate machine token from the local Machine Identity
Agent; `bvault ferrogate token --format json` is the documented bridge (AUT-053).
`machine_token` is sent only when supplied, never as an empty string.






AUT-041: namespace-scoped roles. When the client or view has a `Namespace`, the
login carries `X-BastionVault-Namespace` — it is built by the same header builder every
request uses, so a login cannot silently omit it. A `403` on this path with no
namespace set is enriched with a hint naming the `Namespace` setting (ERR-040).






AUT-044's <see cref="EnvironmentScope"/> is derived from the returned
`Metadata`; a role that is environment-scoped reports
`Scoped` `true`, which tells a KV caller an
`env` is mandatory.





Wire params: `role_id`, `secret_id`, `machine_token` (sent only when supplied). Returns <see cref="AuthInfo"/>, never `null`. Conformance: Core (AUT-040). Errors beyond the common set (ERR-061): `BV-AUTH-010`, `BV-AUTH-011`, `BV-AUTHZ-001`.

**Spec:** `Auth.AppId.Login — AUT-040`

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdOperations.cs:75`*

#### `ReadRoleIdAsync(roleName, mount, options, cancellationToken)`

AUT-042: `GET auth/{mount}/role/{roleName}/role-id`, returning `data.role_id`.

Wire params: none. Returns the role id as a string, never `null` (throws instead). Conformance: Core/Shared (AUT-042). Errors beyond the common set (ERR-061): `BV-PROTOCOL-002`.

**Spec:** `Auth.AppId.ReadRoleId — AUT-042`

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdOperations.cs:100`*

#### `GenerateSecretIdAsync(roleName, options, mount, requestOptions, cancellationToken)`

AUT-042: `POST auth/{mount}/role/{roleName}/secret-id`, returning the generated
<see cref="SecretIdInfo"/>.

`Metadata` is a map in this API and a JSON string on the
wire, which is what AUT-042 specifies and what the server parses. Every other option is
omitted from the body when the caller left it unset (OVR-007).

**Spec:** `Auth.AppId.GenerateSecretId — AUT-042`

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdOperations.cs:133`*

### `SecretIdInfo`

#### `SecretId`

The generated secret id, in a redacting type (AUT-031, CNF-031, CNF-032).

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdOperations.cs:301`*

#### `SecretIdAccessor`

The accessor, which identifies the secret id without being usable as one.

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdOperations.cs:304`*

#### `SecretIdTtl`

The secret id's remaining lifetime; seconds on the wire.

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdOperations.cs:307`*

#### `SecretIdNumUses`

How many uses the secret id has left.

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdOperations.cs:310`*

#### `Environments`

The environment globs the secret id is scoped to (AUT-044).

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdOperations.cs:313`*

### `SecretIdOptions`

#### `Metadata`

Arbitrary metadata; a map here, a JSON string on the wire (AUT-042).

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdOperations.cs:279`*

#### `CidrList`

CIDR blocks the secret id may be used from.

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdOperations.cs:282`*

#### `TokenBoundCidrs`

CIDR blocks the issued token is bound to.

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdOperations.cs:285`*

#### `NumUses`

How many times the secret id may be used; unset means the role's default.

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdOperations.cs:288`*

#### `Ttl`

The secret id's lifetime; seconds on the wire.

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdOperations.cs:291`*

#### `Environments`

Environment globs the credential is scoped to (AUT-044).

*Source: `dotnet/BastionVault.IntegrationSdk/AppIdOperations.cs:294`*

