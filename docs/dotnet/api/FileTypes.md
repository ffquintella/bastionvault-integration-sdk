# `FileTypes` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/FileTypes.cs`](../../../dotnet/BastionVault.IntegrationSdk/FileTypes.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `FileCreateRequest`

#### `Name`

The wire `name` field.

*Source: `dotnet/BastionVault.IntegrationSdk/FileTypes.cs:13`*

#### `Resource`

The wire `resource` field; omitted from the body when unset.

*Source: `dotnet/BastionVault.IntegrationSdk/FileTypes.cs:16`*

#### `MimeType`

The wire `mime_type` field; omitted from the body when unset.

*Source: `dotnet/BastionVault.IntegrationSdk/FileTypes.cs:19`*

#### `Tags`

The wire `tags` array; omitted from the body when unset.

*Source: `dotnet/BastionVault.IntegrationSdk/FileTypes.cs:22`*

#### `Notes`

The wire `notes` field; omitted from the body when unset.

*Source: `dotnet/BastionVault.IntegrationSdk/FileTypes.cs:25`*

#### `Content`

FIL-001: base64-encoded by the SDK into `content_base64`; never a base64 string here.
<see cref="ReadOnlyMemory{T}"/> rather than `byte[]` (a caller-supplied array converts
implicitly), the same property shape `Body` already uses.

*Source: `dotnet/BastionVault.IntegrationSdk/FileTypes.cs:32`*

### `FileUpdateRequest`

#### `Name`

The wire `name` field; omitted from the body when unset.

*Source: `dotnet/BastionVault.IntegrationSdk/FileTypes.cs:42`*

#### `Resource`

The wire `resource` field; omitted from the body when unset.

*Source: `dotnet/BastionVault.IntegrationSdk/FileTypes.cs:45`*

#### `MimeType`

The wire `mime_type` field; omitted from the body when unset.

*Source: `dotnet/BastionVault.IntegrationSdk/FileTypes.cs:48`*

#### `Tags`

The wire `tags` array; omitted from the body when unset.

*Source: `dotnet/BastionVault.IntegrationSdk/FileTypes.cs:51`*

#### `Notes`

The wire `notes` field; omitted from the body when unset.

*Source: `dotnet/BastionVault.IntegrationSdk/FileTypes.cs:54`*

### `SyncTarget`

#### `Kind`

The wire `kind` field: `local-fs` or `smb`.

*Source: `dotnet/BastionVault.IntegrationSdk/FileTypes.cs:66`*

#### `Fields`

Every other field this target needs, exactly as the caller supplies it.

*Source: `dotnet/BastionVault.IntegrationSdk/FileTypes.cs:69`*

