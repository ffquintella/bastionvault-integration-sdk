# `FilesOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/FilesOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/FilesOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `FilesOperations`

#### `Sync`

12 §Files: `{mount}/files/{id}/sync[/{name}[/push]]`, `POST {mount}/sync-tick`.

*Source: `dotnet/BastionVault.IntegrationSdk/FilesOperations.cs:25`*

#### `ListAsync(mount, options, cancellationToken)`

Lists file ids: `LIST {mount}/files/`.

Wire params: none beyond `mount`. Returns the file ids, empty (never `null`) when none exist or the path is absent. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Files.List — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/FilesOperations.cs:30`*

#### `CreateAsync(request, mount, options, cancellationToken)`

FIL-001: `POST {mount}/files/`. `Content` is base64-encoded here, into `content_base64`.

Wire params: name, resource?, mime_type?, tags[]?, notes?, content_base64 from `Content` (<see cref="FileCreateRequest"/>). Returns the new file's id, never `null`. Conformance: Level X (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): `BV-PROTOCOL-002` when the response carries no usable id.

**Spec:** `Files.Create — FIL-001`

*Source: `dotnet/BastionVault.IntegrationSdk/FilesOperations.cs:43`*

#### `ReadAsync(id, mount, options, cancellationToken)`

Reads a file record: `GET {mount}/files/{id}`. No field set is named beyond `Files.Create`'s own request fields.

Wire params: none beyond `id`/`mount`. Returns the raw <see cref="Response"/>, or `null` when `id` is not found (404 treated as absent). Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Files.Read — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/FilesOperations.cs:60`*

#### `UpdateAsync(id, spec, mount, options, cancellationToken)`

Updates a file record: `PUT {mount}/files/{id}`, a patch-shaped update.

Wire params (patch-shaped, omitted members untouched): name, resource, mime_type, tags[], notes (<see cref="FileUpdateRequest"/>); wire field names are never renamed on the wire (OVR-007), only the language-facing accessor is. Returns the raw <see cref="Response"/>, which may be `null` for an empty body. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Files.Update — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/FilesOperations.cs:73`*

#### `DeleteAsync(id, mount, options, cancellationToken)`

Deletes a file: `DELETE {mount}/files/{id}`.

Wire params: none beyond `id`/`mount`. Returns `void`. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Files.Delete — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/FilesOperations.cs:87`*

#### `ContentAsync(id, mount, options, cancellationToken)`

FIL-001: `GET {mount}/files/{id}/content` → `bytes`. The SDK decodes `content_base64` itself.

Wire params: none beyond `id`/`mount`. Returns the decoded file content as bytes, never `null`. Conformance: Level X (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): `BV-PROTOCOL-002` when `content_base64` is missing or not valid base64.

**Spec:** `Files.Content — FIL-001`

*Source: `dotnet/BastionVault.IntegrationSdk/FilesOperations.cs:100`*

#### `HistoryAsync(id, mount, options, cancellationToken)`

Reads a file's change history: `GET {mount}/files/{id}/history`. No shape beyond the array itself.

Wire params: none beyond `id`/`mount`. Returns an empty list when there is no history, never `null`; each entry is a raw <see cref="JsonElement"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Files.History — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/FilesOperations.cs:115`*

#### `VersionsAsync(id, mount, options, cancellationToken)`

Lists a file's version records: `GET {mount}/files/{id}/versions`. No shape beyond the array itself.

Wire params: none beyond `id`/`mount`. Returns an empty list when there are no versions, never `null`; each entry is a raw <see cref="JsonElement"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Files.Versions — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/FilesOperations.cs:129`*

#### `ReadVersionAsync(id, version, mount, options, cancellationToken)`

Reads one version record: `GET {mount}/files/{id}/versions/{n}`. No field set is named for a version record.

Wire params: `version` builds the route, no body. Returns the raw <see cref="Response"/>, or `null` when the version is not found (404 treated as absent). Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Files.ReadVersion — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/FilesOperations.cs:143`*

#### `VersionContentAsync(id, version, mount, options, cancellationToken)`

FIL-001: `GET {mount}/files/{id}/versions/{n}/content` → `bytes`.

Wire params: `version` builds the route, no body. Returns the decoded content of that version as bytes, never `null`. Conformance: Level X (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): `BV-PROTOCOL-002` when `content_base64` is missing or not valid base64.

**Spec:** `Files.VersionContent — FIL-001`

*Source: `dotnet/BastionVault.IntegrationSdk/FilesOperations.cs:156`*

#### `RestoreVersionAsync(id, version, mount, options, cancellationToken)`

Restores a version as the current content: `POST {mount}/files/{id}/versions/{n}/restore`.

Wire params: `version` builds the route, no body. Returns `void`. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Files.RestoreVersion — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/FilesOperations.cs:171`*

#### `RepointResourceAsync(oldResource, newResource, mount, options, cancellationToken)`

Repoints a file to a different resource: `POST {mount}/files/repoint-resource`. No wire
field name is given (Level X); `old_resource`/`new_resource` follow the
operation's own name.

Wire params: old_resource from `oldResource`, new_resource from `newResource`. Returns `void`. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Files.RepointResource — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/FilesOperations.cs:188`*

### `FilesSyncOperations`

#### `ListAsync(id, mount, options, cancellationToken)`

Lists a file's sync target names: `LIST {mount}/files/{id}/sync/`.

Wire params: none beyond `id`/`mount`. Returns the sync target names, empty (never `null`) when none exist or the path is absent. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Files.Sync.List — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/FilesOperations.cs:220`*

#### `WriteAsync(id, name, target, mount, options, cancellationToken)`

Creates or replaces a sync target: `PUT {mount}/files/{id}/sync/{name}`. See <see cref="SyncTarget"/>'s remarks for the credential-field gap.

Wire params: kind from `Kind` plus whatever `Fields` carries verbatim (<see cref="SyncTarget"/>). R-36 (open risk): credential fields for the sync target ship through `Fields` as an opaque JSON bag — section 12 names no wire field for them, so this SDK documents no field name here and never places an example credential value in this comment. Returns the raw <see cref="Response"/>, which may be `null` for an empty body. Conformance: Level X (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): `BV-INPUT-001` when `Fields` is not a JSON object or contains a `kind` key.

**Spec:** `Files.Sync.Write — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/FilesOperations.cs:234`*

#### `DeleteAsync(id, name, mount, options, cancellationToken)`

Removes a sync target: `DELETE {mount}/files/{id}/sync/{name}`.

Wire params: none beyond `id`/`name`/`mount`. Returns `void`. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Files.Sync.Delete — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/FilesOperations.cs:250`*

#### `PushAsync(id, name, mount, options, cancellationToken)`

Pushes a file to one sync target: `POST {mount}/files/{id}/sync/{name}/push`.

Wire params: none beyond `id`/`name`/`mount`. Returns `void`. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Files.Sync.Push — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/FilesOperations.cs:264`*

#### `TickAsync(mount, options, cancellationToken)`

Ticks the sync scheduler: `POST {mount}/sync-tick`. Mount-wide, not scoped to one file.

Wire params: none beyond `mount`. Returns `void`. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Files.Sync.Tick — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/FilesOperations.cs:278`*

