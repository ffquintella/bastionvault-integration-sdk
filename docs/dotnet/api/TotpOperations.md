# `TotpOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/TotpOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/TotpOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `TotpOperations`

#### `ListKeysAsync(mount, options, cancellationToken)`

11: `LIST {mount}/keys/`. An empty list when there are none (TRN-050).

Wire params: `mount` builds the route; no body. Conformance: Standard (TRN-050). No error codes beyond the common set (ERR-061).

**Spec:** `Totp.ListKeys — TRN-050`

*Source: `dotnet/BastionVault.IntegrationSdk/TotpOperations.cs:39`*

#### `CreateKeyAsync(name, spec, mount, options, cancellationToken)`

11: `POST {mount}/keys/{name}`. TOT-001's client-side validation runs before any
request is sent.

Wire params: `name`/`mount` build the route; body carries `generate`, `key`, `url`, `key_size`, `issuer`, `account_name`, `algorithm`, `digits`, `period`, `skew`, `qr_size`, `exported`, `replay_check` per <see cref="TotpKeySpec"/>. Returns <see cref="TotpKeyCreated"/>, never `null`. Conformance: Standard (TOT-001). Errors beyond the common set (ERR-061): `BV-INPUT-001` (raised server-side too, though TOT-001 validates client-side first).

**Spec:** `Totp.CreateKey — TOT-001`

*Source: `dotnet/BastionVault.IntegrationSdk/TotpOperations.cs:56`*

#### `ReadKeyAsync(name, mount, options, cancellationToken)`

11: `GET {mount}/keys/{name}`. The seed is never returned.

Wire params: `name`/`mount` build the route; no body. A missing key is `null` (TRN-050), never an exception. Conformance: Standard (TRN-050). No error codes beyond the common set (ERR-061).

**Spec:** `Totp.ReadKey — TRN-050`

*Source: `dotnet/BastionVault.IntegrationSdk/TotpOperations.cs:76`*

#### `DeleteKeyAsync(name, mount, options, cancellationToken)`

11: `DELETE {mount}/keys/{name}` → `204`.

Wire params: `name`/`mount` build the route; no body. Returns `void` on the server's `204`. Conformance: Standard (every typed operation is built on `Logical.Delete` per TRN-001, but that primitive-exposure MUST is TRN-001's own, not this operation's; 11 states no delete-specific behaviour beyond the route). Errors beyond the common set (ERR-061): `BV-TOTP-001 KeyNotFound`.

**Spec:** `Totp.DeleteKey — 11-totp-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/TotpOperations.cs:92`*

#### `GenerateCodeAsync(name, mount, options, cancellationToken)`

11: `GET {mount}/code/{name}` — generate-mode only. Returns the wire's `code`
string verbatim (TOT-004): a leading zero is significant and the value is never parsed as a
number. A provider-mode key answers `BV-TOTP-002 WrongModeForOperation`, generated from
Appendix B §2 with no remap here.

Wire params: `name`/`mount` build the route; no body. Never returns `null`; a missing key raises rather than yielding an empty code. Conformance: Standard (TOT-004). Errors beyond the common set (ERR-061): `BV-TOTP-001 KeyNotFound`, `BV-TOTP-002 WrongModeForOperation`.

**Spec:** `Totp.GenerateCode — TOT-004`

*Source: `dotnet/BastionVault.IntegrationSdk/TotpOperations.cs:112`*

#### `ValidateCodeAsync(name, code, mount, options, cancellationToken)`

11: `POST {mount}/code/{name}` with `{"code": "&lt;code&gt;"}` — provider-mode
only. `code` is sent as a string (TOT-004), preserving a leading zero.

TOT-003: a wrong code and a replayed code (when `replay_check` is on) both answer
`{valid: false}`, not an error, and the two are indistinguishable at the API level.
The server gives the SDK no way to tell them apart, and this method does not invent one.
A generate-mode key answers `BV-TOTP-002 WrongModeForOperation`, generated from
Appendix B §2 with no remap here.

**Spec:** `Totp.ValidateCode — TOT-003`

*Source: `dotnet/BastionVault.IntegrationSdk/TotpOperations.cs:139`*

