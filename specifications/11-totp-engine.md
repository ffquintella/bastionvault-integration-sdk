# 11 — TOTP Engine (`totp`)

Operations live under `Client.Totp` with `mount` default `"totp"`.

A key is either **generate-mode** (the vault owns the seed and produces codes) or
**provider-mode** (the seed came from `key`/`url` and the vault validates codes).

## Operations

| Operation | HTTP | Body | Response |
|-----------|------|------|----------|
| `Totp.ListKeys(mount)` | `LIST {mount}/keys/` | — | `{keys}` |
| `Totp.CreateKey(mount, name, TotpKeySpec)` → `TotpKeyCreated` | `POST {mount}/keys/{name}` | `generate` (false), `key` (base32), `url` (`otpauth://`), `key_size` (20), `issuer`, `account_name`, `algorithm` (`SHA1`/`SHA256`/`SHA512`), `digits` (6 or 8), `period` (30), `skew` (1), `qr_size` (200; 0 disables), `exported` (true), `replay_check` (true) | `{name, generate}` + (`generate && exported`) `{key, url, barcode}` |
| `Totp.ReadKey(mount, name)` → `TotpKey?` | `GET {mount}/keys/{name}` | — | `{generate, issuer, account_name, algorithm, digits, period, skew, replay_check}` — seed never returned |
| `Totp.DeleteKey(mount, name)` | `DELETE {mount}/keys/{name}` | — | 204 |
| `Totp.GenerateCode(mount, name)` → `string` | `GET {mount}/code/{name}` | — | `{code}` (generate-mode only) |
| `Totp.ValidateCode(mount, name, code)` → `bool` | `POST {mount}/code/{name}` | `{"code": "..."}` | `{valid}` (provider-mode only) |

- **TOT-001** `CreateKey` MUST validate client-side: exactly one of `generate`, `key`,
  `url` (`BV-INPUT-001`); `digits ∈ {6, 8}`; `period ≥ 1`; `algorithm` in the allowed
  set; `account_name` required unless `url` carries a label.
- **TOT-002** `barcode` is a base64 PNG; the SDK MUST expose it as bytes and MUST hold
  `key`/`url` in redacting types.
- **TOT-003** `ValidateCode` returns `false` for a wrong code and for a replayed code
  when `replay_check` is on; both are `{valid: false}`, not errors. The SDK MUST document
  that a replay is indistinguishable from a wrong code at the API level.
- **TOT-004** Codes MUST be sent as strings (leading zeros matter).

## Server error strings (recognition)

| Server message | Code |
|----------------|------|
| ``unknown totp key `<name>` `` | `BV-TOTP-001 KeyNotFound` |
| `this key is provider-mode; POST a `code` to validate, not GET` | `BV-TOTP-002 WrongModeForOperation` |
| `this key is generate-mode; GET the code, do not POST` | `BV-TOTP-002` |
| `` `code` is required `` | `BV-INPUT-001` |
| `provider mode requires `key` or `url`` | `BV-INPUT-001` |
| `account_name is required (or pass it via the otpauth url path label)` | `BV-INPUT-001` |
