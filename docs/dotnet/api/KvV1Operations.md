# `KvV1Operations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/KvV1Operations.cs`](../../../dotnet/BastionVault.IntegrationSdk/KvV1Operations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `KvV1Operations`

#### `ReadAsync(path, mount, options, cancellationToken)`

KV1-002, KV1-003: `GET {mount}/{path}`. Returns `null` for a
`404` with an empty body; use <see cref="GetAsync"/> to raise `BV-KV-001` instead.

`path`/`mount` are wire path segments. Conformance: Core (KV1-002, KV1-003). No error codes beyond the common set (ERR-061).

**Spec:** `Kv.V1.Read — KV1-002`

*Source: `dotnet/BastionVault.IntegrationSdk/KvV1Operations.cs:41`*

#### `GetAsync(path, mount, options, cancellationToken)`

KV1-002: <see cref="ReadAsync"/>, raising `BV-KV-001 SecretNotFound` instead of returning `null`.

Never returns `null`. Conformance: Core (KV1-002). Errors beyond the common set (ERR-061): `BV-KV-001 SecretNotFound`.

**Spec:** `Kv.V1.Read — KV1-002`

*Source: `dotnet/BastionVault.IntegrationSdk/KvV1Operations.cs:87`*

#### `WriteAsync(path, data, mount, ttl, options, cancellationToken)`

KV1-001: `POST {mount}/{path}` with the body stored verbatim, plus
`{"ttl": "&lt;duration&gt;"}` when `ttl` is given. An empty
`data` is refused client-side with `BV-INPUT-001`, and so is a
negative `ttl` (D-M4-13): section 07 is silent on one, <see cref="GoDuration"/>
would happily emit `"-1h"`, and a negative lease has no meaning the server defines, so
passing it through would be the plausible guess D-M1c-25 forbids.

Returns nothing (server answers `204`/`200` with no data used). Conformance: Core (KV1-001). Errors beyond the common set (ERR-061): `BV-INPUT-001` for empty `data` or a negative `ttl`.

**Spec:** `Kv.V1.Write — KV1-001`

*Source: `dotnet/BastionVault.IntegrationSdk/KvV1Operations.cs:107`*

#### `DeleteAsync(path, mount, options, cancellationToken)`

KV-002: `DELETE {mount}/{path}` → `204`.

Returns nothing; a missing secret is not an error (idempotent delete). Conformance: Core (KV1-004). No error codes beyond the common set (ERR-061).

**Spec:** `Kv.V1.Delete — KV1-004`

*Source: `dotnet/BastionVault.IntegrationSdk/KvV1Operations.cs:154`*

#### `ListAsync(prefix, mount, options, cancellationToken)`

KV-002: `LIST {mount}/{prefix}/` (TRN-011's literal verb). A `404` with an empty
body is an empty list, never an error.

Never returns `null`; an absent prefix is an empty list. Conformance: Core (KV1-003). No error codes beyond the common set (ERR-061).

**Spec:** `Kv.V1.List — KV1-003`

*Source: `dotnet/BastionVault.IntegrationSdk/KvV1Operations.cs:180`*

