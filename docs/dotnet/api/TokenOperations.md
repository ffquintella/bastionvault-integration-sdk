# `TokenOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/TokenOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/TokenOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `TokenOperations`

#### `Use(token)`

AUT-020: replaces the client's source with a `Static` one
holding `token`. No network call. An empty or whitespace-only token is
refused with `BV-INPUT-001`.

HTTP call: none — client-side assignment only. Wire params: none. Returns nothing. Conformance: Core (AUT-020). Errors beyond the common set (ERR-061): `BV-INPUT-001`. `token` is a redacting <see cref="SecretString"/> and is never logged.

**Spec:** `Auth.Token.Use — AUT-020`

*Source: `dotnet/BastionVault.IntegrationSdk/TokenOperations.cs:60`*

#### `VerifyAsync(options, cancellationToken)`

`Auth.Token.Verify`: a <see cref="LookupSelfAsync"/> whose purpose is to fail with
`BV-AUTHZ-001` when the token is invalid (05 §Method: Token).

HTTP call: `GET auth/token/lookup-self`, via <see cref="LookupSelfAsync"/>. Wire params: none. Returns a <see cref="TokenInfo"/>, never `null` (throws instead). Conformance: Core (05 §Method: Token). Errors beyond the common set (ERR-061): `BV-AUTHZ-001` for an invalid token. `Id`, when present, carries the token's own id in a redacting <see cref="SecretString"/> and is never logged in cleartext.

**Spec:** `Auth.Token.Verify — 05-authentication.md`

*Source: `dotnet/BastionVault.IntegrationSdk/TokenOperations.cs:90`*

#### `CreateAsync(request, options, cancellationToken)`

AUT-082: `POST auth/token/create`. Returns the created token's <see cref="AuthInfo"/>
and does not switch the client's token unless
`UseResult` is set. Reserved `meta` keys are refused
before any request (AUT-081).

Wire params: `policies`, `ttl`, `period`, `num_uses`, `renewable`, `meta`, `display_name`, `explicit_max_ttl`, `no_default_policy`, `no_parent`, `id`, `type`, `child_visible`, each omitted when unset. Returns <see cref="AuthInfo"/>, never `null` (throws instead). Conformance: Core (AUT-082, AUT-081). Errors beyond the common set (ERR-061): `BV-INPUT-009` for a reserved `meta` key. The created token's value is carried only in `ClientToken`, a redacting <see cref="SecretString"/>, and is never logged.

**Spec:** `Auth.Token.Create — AUT-082`

*Source: `dotnet/BastionVault.IntegrationSdk/TokenOperations.cs:103`*

#### `LookupAsync(token, options, cancellationToken)`

`GET auth/token/lookup/{token}`. A `404` with an empty body is
`BV-NOTFOUND-006 TokenNotFound`, not absence (AUT-084), and the token segment of the
path is redacted in the error and in the observer event (ERR-003, CFG-080).

Wire params: `token`, in the path. Returns a <see cref="TokenInfo"/>, never `null` (throws `BV-NOTFOUND-006` instead). Conformance: Core (AUT-084). Errors beyond the common set (ERR-061): `BV-NOTFOUND-006`. `token` is redacted wherever the path is surfaced (ERR-003, CFG-080), and `Id` is a redacting <see cref="SecretString"/>.

**Spec:** `Auth.Token.Lookup — AUT-084`

*Source: `dotnet/BastionVault.IntegrationSdk/TokenOperations.cs:127`*

#### `LookupSelfAsync(options, cancellationToken)`

`GET auth/token/lookup-self`. The result is also recorded as
`TokenInfo` (AUT-004).

Wire params: none. Returns a <see cref="TokenInfo"/>, never `null` (throws instead). Conformance: Core (AUT-004). Errors beyond the common set (ERR-061): none. `Id`, when present, is a redacting <see cref="SecretString"/> and is never logged in cleartext.

**Spec:** `Auth.Token.LookupSelf — AUT-004`

*Source: `dotnet/BastionVault.IntegrationSdk/TokenOperations.cs:142`*

#### `RenewAsync(token, increment, options, cancellationToken)`

`POST auth/token/renew/{token}` with the required `increment` body. An
unknown or expired token yields `BV-AUTH-015 TokenNotRenewable` (AUT-085).

Wire params: `token` in the path, `increment` in the body. Returns <see cref="AuthInfo"/>, never `null`. Conformance: Core (AUT-085). Errors beyond the common set (ERR-061): `BV-AUTH-015`. The renewed token's value is carried only in `ClientToken`, a redacting <see cref="SecretString"/>.

**Spec:** `Auth.Token.Renew — AUT-085`

*Source: `dotnet/BastionVault.IntegrationSdk/TokenOperations.cs:158`*

#### `RenewSelfAsync(increment, options, cancellationToken)`

AUT-080: `RenewSelf` goes through `renew/{currentToken}` — there is no
`renew-self` path on the server — so the live token appears in the request path, and
the path is therefore redacted wherever it is surfaced (ERR-003 in the error, CFG-080 in
the observer event).

Wire params: the current token in the path, `increment` in the body. Returns <see cref="AuthInfo"/>, never `null`. Conformance: Core (AUT-080). Errors beyond the common set (ERR-061): `BV-AUTH-015`. The renewed value is carried only in `ClientToken`, a redacting <see cref="SecretString"/>.

**Spec:** `Auth.Token.RenewSelf — AUT-080`

*Source: `dotnet/BastionVault.IntegrationSdk/TokenOperations.cs:172`*

#### `RevokeAsync(token, options, cancellationToken)`

`POST auth/token/revoke/{token}`.

Wire params: `token`, in the path. Returns nothing. Conformance: Core (05 §Method: Token). Errors beyond the common set (ERR-061): none. `token` is redacted wherever the path is surfaced (ERR-003, CFG-080).

**Spec:** `Auth.Token.Revoke — 05-authentication.md`

*Source: `dotnet/BastionVault.IntegrationSdk/TokenOperations.cs:196`*

#### `RevokeOrphanAsync(token, options, cancellationToken)`

`POST auth/token/revoke-orphan/{token}` (sudo).

Wire params: `token`, in the path. Returns nothing. Conformance: Shared (05 §Method: Token). Errors beyond the common set (ERR-061): none. `token` is redacted wherever the path is surfaced (ERR-003, CFG-080).

**Spec:** `Auth.Token.RevokeOrphan — 05-authentication.md`

*Source: `dotnet/BastionVault.IntegrationSdk/TokenOperations.cs:207`*

#### `RevokeSelfAsync(options, cancellationToken)`

AUT-083: `POST auth/token/revoke-self`, then clears the local token. A root-policy
token is accepted by the server but not actually revoked (the logout is only recorded);
the SDK clears its token either way, because the server's response is identical and the
client cannot tell the two apart.

Wire params: none. Returns nothing. Conformance: Core (AUT-083). Errors beyond the common set (ERR-061): none.

**Spec:** `Auth.Token.RevokeSelf — AUT-083`

*Source: `dotnet/BastionVault.IntegrationSdk/TokenOperations.cs:223`*

#### `AuditLoginAsync(options, cancellationToken)`

`POST auth/token/audit-login`: records a login event for a token sign-in.

Wire params: none. Returns nothing. Conformance: Shared (05 §Method: Token). Errors beyond the common set (ERR-061): none.

**Spec:** `Auth.Token.AuditLogin — 05-authentication.md`

*Source: `dotnet/BastionVault.IntegrationSdk/TokenOperations.cs:234`*

