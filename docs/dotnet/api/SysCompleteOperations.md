# `SysCompleteOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/SysCompleteOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/SysCompleteOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `DosOperations`

#### `ReadConfigAsync(options, cancellationToken)`

`GET /v2/sys/dos/config`.

Wire params: none. Returns a <see cref="DosConfig"/>, never `null` (throws instead). Conformance: Complete (no requirement ID is minted for this surface, see the class remarks). Errors beyond the common set (ERR-061): none.

**Spec:** `Sys.Dos.ReadConfig — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SysCompleteOperations.cs:31`*

#### `WriteConfigAsync(patch, options, cancellationToken)`

`POST /v2/sys/dos/config` — a partial update, as the table says in as many
words. Only the fields `patch` sets are written, so a caller changing
`ban_secs` does not reset the other five. This is the opposite of SYS-060's
full-replace namespace write, and the difference is the requirement's, not a choice
(D-M7-19 ⇄ this).

Wire params: `enabled`, `window_secs`, `max_requests`, `auth_max_requests`, `ban_secs`, `refresh_secs`, each sent only when `patch` sets it. Returns the resulting <see cref="DosConfig"/>, never `null` (throws instead). Conformance: Complete (no requirement ID is minted for this surface, see the class remarks — the `SYS-060` mentioned above governs a different, full-replace operation, not this one). Errors beyond the common set (ERR-061): none.

**Spec:** `Sys.Dos.WriteConfig — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SysCompleteOperations.cs:60`*

#### `StatsAsync(options, cancellationToken)`

`GET /v2/sys/dos/stats`. The body's shape is not specified anywhere, so it is returned unparsed rather than modelled from a guess (D-M1c-25).

Wire params: none. Returns the raw response body as a <see cref="JsonElement"/>, never a default value (throws instead). Conformance: Complete (no requirement ID is minted for this surface, see the class remarks). Errors beyond the common set (ERR-061): none.

**Spec:** `Sys.Dos.Stats — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SysCompleteOperations.cs:84`*

#### `BanAsync(ip, ttlSecs, reason, options, cancellationToken)`

`POST /v2/sys/dos/bans/{ip}`. `ttlSecs` and `reason`
are omitted from the body when unset rather than sent as zero and `""`, which would be
two different requests from the one the caller made.

Wire params: `ttl_secs`, `reason`, each omitted when unset. Returns nothing. Conformance: Complete (no requirement ID is minted for this surface, see the class remarks). Errors beyond the common set (ERR-061): none.

**Spec:** `Sys.Dos.Ban — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SysCompleteOperations.cs:99`*

#### `UnbanAsync(ip, options, cancellationToken)`

`DELETE /v2/sys/dos/bans/{ip}`.

Wire params: none. Returns nothing. Conformance: Complete (no requirement ID is minted for this surface, see the class remarks). Errors beyond the common set (ERR-061): none.

**Spec:** `Sys.Dos.Unban — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SysCompleteOperations.cs:110`*

### `ExchangeOperations`

#### `ExportAsync(request, options, cancellationToken)`

`POST sys/exchange/export`.

Wire params: `request`, forwarded verbatim (no specified body, see the class remarks). Returns the server's response body verbatim, or `null` when it sends none. Conformance: Complete (no requirement ID is minted for this surface). Errors beyond the common set (ERR-061): none.

**Spec:** `Sys.Exchange.Export — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SysCompleteOperations.cs:253`*

#### `ImportAsync(request, options, cancellationToken)`

`POST sys/exchange/import`.

Wire params: `request`, forwarded verbatim (no specified body, see the class remarks). Returns the server's response body verbatim, or `null` when it sends none. Conformance: Complete (no requirement ID is minted for this surface). Errors beyond the common set (ERR-061): none.

**Spec:** `Sys.Exchange.Import — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SysCompleteOperations.cs:261`*

#### `ImportPreviewAsync(request, options, cancellationToken)`

`POST sys/exchange/import/preview` — the dry run.

Wire params: `request`, forwarded verbatim (no specified body, see the class remarks). Returns the server's response body verbatim, or `null` when it sends none. Conformance: Complete (no requirement ID is minted for this surface). Errors beyond the common set (ERR-061): none.

**Spec:** `Sys.Exchange.ImportPreview — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SysCompleteOperations.cs:269`*

#### `ImportApplyAsync(request, options, cancellationToken)`

`POST sys/exchange/import/apply`.

Wire params: `request`, forwarded verbatim (no specified body, see the class remarks). Returns the server's response body verbatim, or `null` when it sends none. Conformance: Complete (no requirement ID is minted for this surface). Errors beyond the common set (ERR-061): none.

**Spec:** `Sys.Exchange.ImportApply — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SysCompleteOperations.cs:277`*

### `OwnerTransferOperations`

#### `KvAsync(spec, options, cancellationToken)`

`POST sys/kv-owner/transfer`.

Wire params: `spec`, forwarded verbatim (no specified body, see the class remarks). Returns the server's response body verbatim, or `null` when it sends none. Conformance: Complete (no requirement ID is minted for this surface). Errors beyond the common set (ERR-061): none.

**Spec:** `Sys.OwnerTransfer.Kv — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SysCompleteOperations.cs:194`*

#### `ResourceAsync(spec, options, cancellationToken)`

`POST sys/resource-owner/transfer`.

Wire params: `spec`, forwarded verbatim (no specified body, see the class remarks). Returns the server's response body verbatim, or `null` when it sends none. Conformance: Complete (no requirement ID is minted for this surface). Errors beyond the common set (ERR-061): none.

**Spec:** `Sys.OwnerTransfer.Resource — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SysCompleteOperations.cs:202`*

#### `AssetGroupAsync(spec, options, cancellationToken)`

`POST sys/asset-group-owner/transfer`.

Wire params: `spec`, forwarded verbatim (no specified body, see the class remarks). Returns the server's response body verbatim, or `null` when it sends none. Conformance: Complete (no requirement ID is minted for this surface). Errors beyond the common set (ERR-061): none.

**Spec:** `Sys.OwnerTransfer.AssetGroup — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SysCompleteOperations.cs:210`*

#### `FileAsync(spec, options, cancellationToken)`

`POST sys/file-owner/transfer`.

Wire params: `spec`, forwarded verbatim (no specified body, see the class remarks). Returns the server's response body verbatim, or `null` when it sends none. Conformance: Complete (no requirement ID is minted for this surface). Errors beyond the common set (ERR-061): none.

**Spec:** `Sys.OwnerTransfer.File — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SysCompleteOperations.cs:218`*

### `VaultCompatibilityGaps`

#### `AbsentSurfaces`

SYS-100's list, as path prefixes. Each entry is a surface a HashiCorp Vault client would
expect and this server does not serve; the built-in default policy mentions some of them,
but the routes do not exist.

*Source: `dotnet/BastionVault.IntegrationSdk/SysCompleteOperations.cs:315`*

