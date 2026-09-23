# `OidcOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/OidcOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/OidcOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `OidcOperations`

#### `Admin`

AUT-060's role and config administration.

*Source: `dotnet/BastionVault.IntegrationSdk/OidcOperations.cs:40`*

#### `AuthUrlAsync(redirectUri, role, mount, options, cancellationToken)`

AUT-060: `POST auth/{mount}/auth_url`, unauthenticated, returning the authorisation
URL to send the operator to.

Wire params: `redirect_uri`, `role` (optional). Returns the authorisation URL as a string, never `null` (throws instead). Conformance: Shared (AUT-060). Errors beyond the common set (ERR-061): `BV-PROTOCOL-002`.

**Spec:** `Auth.Oidc.AuthUrl — AUT-060`

*Source: `dotnet/BastionVault.IntegrationSdk/OidcOperations.cs:55`*

#### `CallbackAsync(state, code, mount, options, cancellationToken)`

AUT-060: `POST auth/{mount}/callback`. This is the login, so the whole login response
contract (AUT-010…AUT-013) applies and no token header is sent (TRN-015).

Wire params: `state`, `code`. Returns <see cref="AuthInfo"/>, never `null`. Conformance: Shared (AUT-060). Errors beyond the common set (ERR-061): AUT-010…AUT-013's login-response refinements.

**Spec:** `Auth.Oidc.Callback — AUT-060`

*Source: `dotnet/BastionVault.IntegrationSdk/OidcOperations.cs:84`*

