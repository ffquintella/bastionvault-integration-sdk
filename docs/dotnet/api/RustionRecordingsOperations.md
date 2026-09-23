# `RustionRecordingsOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/RustionRecordingsOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/RustionRecordingsOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `RustionRecordingsOperations`

#### `ListAsync(mount, options, cancellationToken)`

`LIST {mount}/recordings/`.

Wire params: none. Returns the recording ids, empty (never `null`) when none exist or the path is absent. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Recordings.List — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionRecordingsOperations.cs:26`*

#### `ReadAsync(rid, mount, options, cancellationToken)`

`GET {mount}/recordings/{rid}`. `rid` is validated against `rec_[A-Za-z0-9_-]+` before any request is sent.

Wire params: none beyond `rid`/`mount`. Returns the raw response, or `null` for a 204, an empty body, or (404 treated as absent) an unknown `rid`. Conformance: Level X (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): `BV-INPUT-001` on a malformed `rid`, refused before any request is sent.

**Spec:** `Rustion.Recordings.Read — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionRecordingsOperations.cs:39`*

#### `BlobAsync(rid, mount, options, cancellationToken)`

`GET {mount}/recordings/{rid}/blob`. RUS-002: node-local. <see cref="DownloadAsync"/>'s
fallback payload when the chunk route is unsupported; carries the same `bytes_b64`
field a chunk does.

Wire params: none beyond `rid`/`mount`. Returns the assembled recording bytes, never `null`. Conformance: Level X (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): `BV-INPUT-001` on a malformed `rid`, refused before any request is sent.

**Spec:** `Rustion.Recordings.Blob — RUS-002`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionRecordingsOperations.cs:55`*

#### `ChunkAsync(rid, index, mount, options, cancellationToken)`

`GET {mount}/recordings/{rid}/chunk/{index}`. RUS-002: node-local. See <see cref="RustionRecordingChunk"/>.

Wire params: none beyond `rid`/`index`/`mount`. Returns <see cref="RustionRecordingChunk"/>, never `null`; the chunk route being unsupported is the ordinary `BV-SERVER-*` common-set case <see cref="DownloadAsync"/> catches to fall back to <see cref="BlobAsync"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): `BV-INPUT-001` on a malformed `rid`; `BV-PROTOCOL-002` on a missing/non-boolean `eof` or missing `bytes_b64`; `BV-INPUT-008` on a `416` (index past end).

**Spec:** `Rustion.Recordings.Chunk — RUS-002`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionRecordingsOperations.cs:69`*

#### `KeystrokesAsync(rid, mount, options, cancellationToken)`

`GET {mount}/recordings/{rid}/keystrokes`. No response shape is documented (D-M1c-25).

Wire params: none beyond `rid`/`mount`. Returns the raw response, or `null` for a 204, an empty body, or (404 treated as absent) an unknown `rid`. May carry keystroke content typed during the recorded session — treat as sensitive. Conformance: Level X (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): `BV-INPUT-001` on a malformed `rid`.

**Spec:** `Rustion.Recordings.Keystrokes — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionRecordingsOperations.cs:89`*

#### `DownloadAsync(rid, mount, options, cancellationToken)`

RUS-001: reads chunk 0, then 1, 2, … until `eof` is `true`, concatenating each `bytes_b64` payload and verifying SHA-256 when a digest was reported. Falls back to <see cref="BlobAsync"/> when the chunk route answers `BV-SERVER-004`.

Wire params: none beyond `rid`/`mount`. Returns the assembled recording bytes, never `null`. Conformance: Level X (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): `BV-INPUT-001` on a malformed `rid`; `BV-PROTOCOL-004` on a SHA-256 mismatch; `BV-INPUT-008` on a `416` (index past end); `BV-CONFLICT-002` on a `409` naming two digests — both reach the caller unchanged through the standard error-mapping pipeline.

**Spec:** `Rustion.Recordings.Download — RUS-001`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionRecordingsOperations.cs:101`*

#### `PullAsync(mount, options, cancellationToken)`

`POST {mount}/recordings/pull`. No request/response shape is documented (D-M1c-25).

Wire params: none. Returns `void`; the response body, if any, is discarded. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Recordings.Pull — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionRecordingsOperations.cs:139`*

#### `ReconcileAsync(mount, options, cancellationToken)`

`POST {mount}/recordings/reconcile`. No request/response shape is documented (D-M1c-25).

Wire params: none. Returns `void`; the response body, if any, is discarded. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Recordings.Reconcile — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionRecordingsOperations.cs:151`*

#### `ReplayLogAsync(mount, options, cancellationToken)`

`POST {mount}/recordings/replay-log`. No request/response shape is documented (D-M1c-25).

Wire params: none. Returns `void`; the response body, if any, is discarded. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Recordings.ReplayLog — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionRecordingsOperations.cs:163`*

#### `IndexKeystrokesAsync(mount, options, cancellationToken)`

`POST {mount}/recordings/keystrokes/index`. No request/response shape is documented (D-M1c-25).

Wire params: none. Returns `void`; the response body, if any, is discarded. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Recordings.IndexKeystrokes — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionRecordingsOperations.cs:175`*

#### `KeystrokeSearchAsync(query, limit, mount, options, cancellationToken)`

`POST {mount}/recordings/keystroke-search`. `query` travels in the POST body, never a query string (12's own note).

Wire params: query (req), limit. Returns the raw response, or `null` for a 204 or an empty body (a 404 is not treated as absent here and reaches the caller as an error). Results may carry keystroke content typed during recorded sessions — treat as sensitive. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Recordings.KeystrokeSearch — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionRecordingsOperations.cs:187`*

