# `IdentityKernelTypes` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/IdentityKernelTypes.cs`](../../../dotnet/BastionVault.IntegrationSdk/IdentityKernelTypes.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `EntitySelf`

#### `EntityId`

The wire `entity_id` field.

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityKernelTypes.cs:13`*

#### `Username`

The wire `username` field.

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityKernelTypes.cs:16`*

#### `MountPath`

The wire `mount_path` field.

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityKernelTypes.cs:19`*

#### `RoleName`

The wire `role_name` field.

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityKernelTypes.cs:22`*

#### `PrimaryMount`

The wire `primary_mount` field.

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityKernelTypes.cs:25`*

#### `PrimaryName`

The wire `primary_name` field.

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityKernelTypes.cs:28`*

#### `CreatedAt`

The wire `created_at` field, RFC 3339.

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityKernelTypes.cs:31`*

#### `Aliases`

The wire `aliases` array; each entry a raw element, since 12 names no field inside one.

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityKernelTypes.cs:34`*

#### `Raw`

The whole object as sent.

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityKernelTypes.cs:37`*

### `IdentityGroup`

#### `Description`

The wire `description` field.

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityKernelTypes.cs:44`*

#### `Members`

The wire `members` array; empty when the server omits it.

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityKernelTypes.cs:47`*

#### `Policies`

The wire `policies` array; empty when the server omits it.

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityKernelTypes.cs:50`*

#### `Raw`

The whole record as sent.

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityKernelTypes.cs:53`*

### `IdentityGroupSpec`

#### `Description`

The wire `description` field; omitted when unset.

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityKernelTypes.cs:60`*

#### `Members`

The wire `members` array; omitted when unset.

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityKernelTypes.cs:63`*

#### `Policies`

The wire `policies` array; omitted when unset.

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityKernelTypes.cs:66`*

### `IdentitySharingForMe`

#### `EntityId`

The wire `entity_id` field.

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityKernelTypes.cs:91`*

#### `GroupSharedResources`

The wire `group_shared_resources` field.

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityKernelTypes.cs:94`*

#### `Entries`

The wire `entries` array; each entry a raw element, since 12 names no field inside one.

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityKernelTypes.cs:97`*

#### `Raw`

The whole object as sent.

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityKernelTypes.cs:100`*

### `IdentitySharingSpec`

#### `GranteeKind`

The wire `grantee_kind` field (`entity` default | `group_user` | `group_app`); omitted when unset.

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityKernelTypes.cs:73`*

#### `Capabilities`

The wire `capabilities` array; omitted from the body when unset.

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityKernelTypes.cs:76`*

#### `ExpiresAt`

The wire `expires_at` field, RFC 3339; omitted from the body when unset.

*Source: `dotnet/BastionVault.IntegrationSdk/IdentityKernelTypes.cs:79`*

