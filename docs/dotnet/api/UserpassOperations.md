# `UserpassOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/UserpassOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/UserpassOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `UserpassOperations`

#### `Admin`

Appendix A's `Auth.Userpass.Admin.*` surface.

*Source: `dotnet/BastionVault.IntegrationSdk/UserpassOperations.cs:30`*

#### `LoginAsync(username, password, totpCode, mount, options, cancellationToken)`

AUT-030: `POST auth/{mount}/login/{username}` with body
`{"password": "…", "totp_code": "…"?}`. On success the client holds the new token; on
any rejection it holds none.

The username is URL-path-encoded by the one encoder every path goes through (TRN-020), and
`totp_code` is omitted from the body when `totpCode` is absent
rather than sent as an empty string.






AUT-032: a locked account is not fixed by retrying, and this SDK does not retry it.
`BV-AUTH-006 AccountLocked` arrives as an HTTP `200` (the login-response contract),
so it never reaches the CFG-051…055 retry loop's failure path at all, and the code is
`Retryable = false` in the generated catalogue because ERR-006's retryable set does not
contain it. Wait `Details.retry_after_secs` seconds, or ask an administrator to unlock
the account; a further attempt before then extends the lockout rather than shortening it.






AUT-100: the credentials are not retained. A successful login installs a
`Static` source holding the issued token, and
`password` is referenced only for the duration of the call. An application
that wants the SDK to be able to log in again — AUT-002's lazy login, AUT-003's re-login —
installs a `Login` source instead, which is the one place AUT-100
allows credentials to be kept.

**Spec:** `Auth.Userpass.Login — AUT-030`

*Source: `dotnet/BastionVault.IntegrationSdk/UserpassOperations.cs:72`*

#### `Fido2LoginBeginAsync(username, mount, options, cancellationToken)`

AUT-035: `POST auth/{mount}/fido2/login/begin`, unauthenticated, returning the
server's WebAuthn assertion options uninterpreted.

An account whose password login has been disabled in favour of a security key answers a
<see cref="LoginAsync"/> attempt with `BV-AUTH-009` (AUT-011); this pair is what that
code points the caller at.

**Spec:** `Auth.Userpass.Fido2LoginBegin — AUT-035`

*Source: `dotnet/BastionVault.IntegrationSdk/UserpassOperations.cs:102`*

#### `Fido2LoginCompleteAsync(username, credentialJson, mount, options, cancellationToken)`

AUT-035: `POST auth/{mount}/fido2/login/complete`. Completion follows the login
response contract, so every AUT-010…AUT-013 rule applies unchanged.

Wire params: `username`, the assertion response (opaque JSON). Returns <see cref="AuthInfo"/>, never `null`. Conformance: Complete (AUT-035). Errors beyond the common set (ERR-061): `BV-INPUT-001`, `BV-AUTH-003`.

**Spec:** `Auth.Userpass.Fido2LoginComplete — AUT-035`

*Source: `dotnet/BastionVault.IntegrationSdk/UserpassOperations.cs:126`*

