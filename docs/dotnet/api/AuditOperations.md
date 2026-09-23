# `AuditOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/AuditOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/AuditOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `AuditOperations`

#### `ListDevicesAsync(options, cancellationToken)`

06 — system API, "Audit": `GET sys/audit` → the `devices` array. An absent or non-array `devices` is an empty registry, not a protocol failure.

Wire params: none. Returns a list of <see cref="AuditDevice"/>, never `null` (empty when no devices are registered). Conformance: Complete (Appendix A groups this mount, no per-operation row; SYS-070's MUST governs <see cref="EventsAsync"/>'s from/to/limit handling, not this listing). Errors beyond the common set (ERR-061): none.

**Spec:** `Sys.Audit.ListDevices — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/AuditOperations.cs:34`*

#### `EnableDeviceAsync(path, spec, options, cancellationToken)`

SYS-070: `POST sys/audit/{path}` → `204`. The path is accepted with or without a
trailing `/` and an empty one is `BV-INPUT-001` client-side, exactly as SYS-022's
mount paths are (`MountPaths.ToWire`, D-M7-7 — no second normaliser).

Wire params: `type`, `description`, `options`, `mirror`, from `spec`. Returns nothing. Conformance: Complete (Appendix A groups this mount, no per-operation row; SYS-070's MUST governs <see cref="EventsAsync"/>'s from/to/limit handling, not this write). Errors beyond the common set (ERR-061): `BV-INPUT-001` for an empty `path`.

**Spec:** `Sys.Audit.EnableDevice — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/AuditOperations.cs:70`*

#### `DisableDeviceAsync(path, options, cancellationToken)`

06 — system API, "Audit": `DELETE sys/audit/{path}` → `204`.

Wire params: none. Returns nothing. Conformance: Complete (Appendix A groups this mount, no per-operation row; SYS-070's MUST governs <see cref="EventsAsync"/>'s from/to/limit handling, not this delete). Errors beyond the common set (ERR-061): `BV-INPUT-001` for an empty `path`.

**Spec:** `Sys.Audit.DisableDevice — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/AuditOperations.cs:82`*

#### `EventsAsync(from, to, limit, options, cancellationToken)`

SYS-070: `GET sys/audit/events?from=&amp;to=&amp;limit=` → the `events` array,
newest first as the server orders it.

`from` and `to` are serialised as RFC 3339 UTC —
converted to UTC first, then formatted with a literal `Z` — and percent-encoded onto
the query string. Converting rather than rejecting a non-UTC
<see cref="DateTimeOffset"/> is deliberate: the type carries its offset, so the conversion
is lossless and information-preserving, where a refusal would make a perfectly
unambiguous instant an error.






`limit` is validated `≥ 1` client-side with `BV-INPUT-004`
(SYS-070). There is no upper bound here: PAG-001's `1…500` range governs the
`*-info` cursor listings and SYS-070 names only the lower bound, so capping this one
at 500 would be a client-side refusal of a request the requirement does not refuse
(D-M1c-25).





Wire params: `from`, `to` (RFC 3339 UTC query values, when supplied), `limit`. Returns a list of <see cref="AuditEvent"/>, never `null` (empty when there are no events). Conformance: Complete (SYS-070, PAG-001). Errors beyond the common set (ERR-061): `BV-INPUT-004` for `limit` &lt; 1.

**Spec:** `Sys.Audit.Events — SYS-070`

*Source: `dotnet/BastionVault.IntegrationSdk/AuditOperations.cs:113`*

