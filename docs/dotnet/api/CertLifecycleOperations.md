# `CertLifecycleOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/CertLifecycleOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/CertLifecycleOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `CertLifecycleOperations`

#### `ListTargetsAsync(mount, options, cancellationToken)`

Lists the renewal-target names under `mount`: `LIST {mount}/targets/`.

Wire params: `mount` builds the route; no query or body params. Returns an
empty list when the backend has none, never `null`. Conformance: Complete.
No error codes beyond the common set (ERR-061).

**Spec:** `CertLifecycle.ListTargets — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleOperations.cs:31`*

#### `ListTargetsInfoAsync(mount, after, limit, options, cancellationToken)`

14 §Bulk metadata listings: `GET /v2/{mount}/targets-info?after=&amp;limit=`. Pinned to
`/v2` per `14-batch-and-request-efficiency.md:102`, which names this exact route.

Wire params: `mount` builds the route; query carries `after`
(cursor, PAG-002) and `limit` (defaulted to 100 and validated to
`1-500`, PAG-001). Returns <see cref="Page{T}"/> of <see cref="Target"/>, never
`null`. Conformance: Complete (PAG-001). Errors beyond the common set
(ERR-061): `BV-INPUT-004` (limit out of range, PAG-001); `BV-PROTOCOL-002`
(records/keys length mismatch, PAG-005).

**Spec:** `CertLifecycle.ListTargetsInfo — PAG-001`

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleOperations.cs:54`*

#### `ListTargetsInfoAllAsync(mount, limit, maxRecords, options, cancellationToken)`

D-M9-8: PAG-004's iterator for <see cref="ListTargetsInfoAsync"/>, following `ListCertificatesInfoAllAsync`'s exact shape.

HTTP call: none directly — walks <see cref="ListTargetsInfoAsync"/> pages via
<see cref="PagingWire.IteratePagesAsync{T}"/>. Wire params: as <see cref="ListTargetsInfoAsync"/>,
plus `maxRecords` (client-side cap, no wire effect). Returns each record
keyed by its name, never `null`. Conformance: Complete (PAG-004). Errors
beyond the common set (ERR-061): `BV-INPUT-005` when the walk would exceed
`maxRecords`.

**Spec:** `CertLifecycle.ListTargetsInfoAll — PAG-004`

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleOperations.cs:113`*

#### `ReadTargetAsync(name, mount, options, cancellationToken)`

Reads a renewal target's configuration: `GET {mount}/targets/{name}`.

Wire params: `name`/`mount` build the route; no body.
A missing target is `null`, never an exception. Conformance: Complete. No
error codes beyond the common set (ERR-061).

**Spec:** `CertLifecycle.ReadTarget — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleOperations.cs:133`*

#### `WriteTargetAsync(name, target, mount, options, cancellationToken)`

Creates or replaces a renewal target's configuration: `POST {mount}/targets/{name}`.

Wire params: `name`/`mount` build the route; body carries
`target`'s fields (`kind`, `address`, `pki_mount`,
`role_ref`, `common_name`, `alt_names`, `ip_sans`, `ttl`,
`key_policy`, `key_ref`, `renew_before`). Returns `void` on
success. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `CertLifecycle.WriteTarget — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleOperations.cs:153`*

#### `DeleteTargetAsync(name, mount, options, cancellationToken)`

Deletes a renewal target: `DELETE {mount}/targets/{name}`.

Wire params: `name`/`mount` build the route; no body.
Returns `void` on success. Conformance: Complete. No error codes beyond
the common set (ERR-061).

**Spec:** `CertLifecycle.DeleteTarget — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleOperations.cs:171`*

#### `StateAsync(name, mount, options, cancellationToken)`

Reads a renewal target's renewer state: `GET {mount}/state/{name}`.

Wire params: `name`/`mount` build the route; no body.
Returns <see cref="TargetState"/>, or `null` when the target (or its
state) is absent. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `CertLifecycle.State — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleOperations.cs:188`*

#### `RenewAsync(name, mount, options, cancellationToken)`

Triggers an out-of-cycle renewal for a target: `POST {mount}/renew/{name}`.

An unknown `name` reaches the caller as `BV-NOTFOUND-008` through the
shared message-recognition pipeline (already catalogued; no operation-local remap is added
here). Wire params: `name`/`mount` build the route; no
body. Returns `void` on success. Conformance: Complete. Errors beyond the
common set (ERR-061): `BV-NOTFOUND-008 ResourceNotFound` for an unknown target.

**Spec:** `CertLifecycle.Renew — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleOperations.cs:208`*

#### `ReadSchedulerConfigAsync(mount, options, cancellationToken)`

Reads the renewal scheduler's configuration: `GET {mount}/scheduler/config`.

Wire params: `mount` builds the route; no body. Returns
<see cref="SchedulerConfig"/> (`client_token_set` replaces the write-only
`client_token` on read), or `null` when unset. Conformance:
Complete. No error codes beyond the common set (ERR-061).

**Spec:** `CertLifecycle.ReadSchedulerConfig — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleOperations.cs:226`*

#### `WriteSchedulerConfigAsync(config, mount, options, cancellationToken)`

Writes the renewal scheduler's configuration: `POST {mount}/scheduler/config`.

Wire params: `mount` builds the route; body carries `config`'s
`enabled`, `tick_interval_seconds` (server-enforced &#8805; 30),
`client_token` (write-only), `base_backoff_seconds`, `max_backoff_seconds`.
Returns `void` on success. Conformance: Complete. No error codes beyond
the common set (ERR-061).

**Spec:** `CertLifecycle.WriteSchedulerConfig — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleOperations.cs:245`*

#### `DeliverersAsync(mount, options, cancellationToken)`

`GET {mount}/sys/deliverers`. 12 names no response shape, so the untyped map fallback applies (D-M1c-25).

Wire params: `mount` builds the route; no body. Returns the server's
response map verbatim, or `null` per the shared envelope rules — no typed
contract is invented (D-M1c-25). Conformance: Complete. No error codes beyond the common
set (ERR-061).

**Spec:** `CertLifecycle.Deliverers — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/CertLifecycleOperations.cs:263`*

