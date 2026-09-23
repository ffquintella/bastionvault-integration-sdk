# `AssetGroupOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/AssetGroupOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/AssetGroupOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `AssetGroupOperations`

#### `ListAsync(options, cancellationToken)`

Lists asset group names: `LIST resource-group/groups`.

Wire params: none. Returns an empty list when there are none, never `null`. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `AssetGroups.List — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/AssetGroupOperations.cs:25`*

#### `ReadAsync(name, options, cancellationToken)`

Reads an asset group by name: `GET resource-group/groups/{name}`.

Wire params: `name` builds the route. Returns the raw <see cref="Response"/>, or `null` when the group does not exist. Conformance: Complete. Errors beyond the common set (ERR-061): `BV-INPUT-001` for a `..` segment in `name`.

**Spec:** `AssetGroups.Read — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/AssetGroupOperations.cs:36`*

#### `WriteAsync(name, spec, options, cancellationToken)`

Creates or replaces an asset group: `PUT resource-group/groups/{name}`.

Wire params: `name` builds the route; body is the caller-supplied `spec` verbatim. Returns the raw <see cref="Response"/>, which may be `null` for an empty body. Conformance: Complete. Errors beyond the common set (ERR-061): `BV-INPUT-001` for a `..` segment in `name` or an undefined `spec`.

**Spec:** `AssetGroups.Write — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/AssetGroupOperations.cs:46`*

#### `DeleteAsync(name, options, cancellationToken)`

Deletes an asset group: `DELETE resource-group/groups/{name}`.

Wire params: `name` builds the route; no body. Returns no value; deleting an absent group is not an error. Conformance: Complete. Errors beyond the common set (ERR-061): `BV-INPUT-001` for a `..` segment in `name`.

**Spec:** `AssetGroups.Delete — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/AssetGroupOperations.cs:56`*

#### `HistoryAsync(name, options, cancellationToken)`

Reads a group's change history: `GET resource-group/groups/{name}/history`. No documented shape beyond the array itself.

Wire params: `name` builds the route. Returns an empty list when there is no history, never `null`; each entry is a raw <see cref="JsonElement"/> (D-M1c-25). Conformance: Complete. Errors beyond the common set (ERR-061): `BV-INPUT-001` for a `..` segment in `name`.

**Spec:** `AssetGroups.History — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/AssetGroupOperations.cs:66`*

#### `ByResourceAsync(name, options, cancellationToken)`

Finds the asset group(s) that contain a resource by name: `GET resource-group/by-resource/{name}`.

Wire params: `name` builds the route. Returns the raw <see cref="Response"/>, or `null` when no group contains the resource. Conformance: Complete. Errors beyond the common set (ERR-061): `BV-INPUT-001` for an empty `name` or a `..` segment in it.

**Spec:** `AssetGroups.ByResource — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/AssetGroupOperations.cs:77`*

#### `BySecretAsync(path, options, cancellationToken)`

Finds the asset group(s) that contain a secret by path: `GET resource-group/by-secret/{b64url path}`. The SDK base64url-encodes `path` itself, an analogous but not IDN-001-governed treatment (that requirement is `identity/sharing/*`'s own).

Wire params: `path` is base64url-encoded client-side to build the route. Returns the raw <see cref="Response"/>, or `null` when no group contains the secret. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `AssetGroups.BySecret — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/AssetGroupOperations.cs:88`*

#### `ReindexAsync(options, cancellationToken)`

Triggers a full reindex of the asset-group engine: `PUT resource-group/reindex`.

Wire params: none. Returns no value. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `AssetGroups.Reindex — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/AssetGroupOperations.cs:99`*

