# `Namespaces` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/Namespaces.cs`](../../../dotnet/BastionVault.IntegrationSdk/Namespaces.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `Namespace`

#### `Uuid`

The wire `uuid` field.

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:16`*

#### `Path`

The wire `path` field. The empty string denotes root (SYS-061).

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:19`*

#### `ParentUuid`

The wire `parent_uuid` field; `null` for a child of root.

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:22`*

#### `CreatedAt`

The wire `created_at` field, parsed as RFC 3339 UTC; `null` when absent or unparseable.

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:25`*

#### `ChildVisibleDefault`

The wire `child_visible_default` field. ⚠️ SYS-060: `Sys.WriteNamespace` resets this to `false` when the spec omits it.

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:28`*

#### `Quotas`

The namespace's quotas. Never `null`: a server that omits the object sends all-unlimited.

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:31`*

### `NamespacePatch`

#### `ChildVisibleDefault`

Set `child_visible_default`, or leave it as it is.

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:86`*

#### `MaxStorageBytes`

Set `max_storage_bytes`, or leave it as it is.

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:89`*

#### `MaxLeases`

Set `max_leases`, or leave it as it is.

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:92`*

#### `RequestRate`

Set `request_rate`, or leave it as it is.

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:95`*

#### `MaxMounts`

Set `max_mounts`, or leave it as it is.

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:98`*

#### `MaxEntities`

Set `max_entities`, or leave it as it is.

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:101`*

#### `MaxChildNamespaces`

Set `max_child_namespaces`, or leave it as it is.

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:104`*

### `NamespaceQuotas`

#### `MaxStorageBytes`

The wire `max_storage_bytes` field.

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:41`*

#### `MaxLeases`

The wire `max_leases` field.

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:44`*

#### `RequestRate`

The wire `request_rate` field.

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:47`*

#### `MaxMounts`

The wire `max_mounts` field.

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:50`*

#### `MaxEntities`

The wire `max_entities` field.

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:53`*

#### `MaxChildNamespaces`

The wire `max_child_namespaces` field.

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:56`*

### `NamespaceSpec`

#### `ChildVisibleDefault`

The wire `child_visible_default` field. Defaults to `false`, which is also what an omitted field writes.

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:73`*

#### `Quotas`

The quotas to write. `null` writes all six as `0`.

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:76`*

### `NamespacesSelf`

#### `Namespaces`

The wire `namespaces` array. `""` denotes root.

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:111`*

#### `TokenNamespace`

The wire `token_namespace` field. `""` denotes root.

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:114`*

#### `Root`

The wire `root` field: whether the token is a root-namespace token.

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:117`*

### `Page`

#### `Keys`

The page's keys, in server order.

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:129`*

#### `Records`

The page's records; `Records[i]` belongs to `Keys[i]` (PAG-005).

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:132`*

#### `Total`

The total number of records behind the cursor, not the page size.

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:135`*

#### `Next`

The cursor to pass as the next call's `after`; `null` on the last page (PAG-003).

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:138`*

#### `Truncated`

Whether a further page exists.

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:141`*

#### `Entries`

PAG-005's zipped view: each record beside the key it belongs to.

*Source: `dotnet/BastionVault.IntegrationSdk/Namespaces.cs:144`*

