# `SamlOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/SamlOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/SamlOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `SamlLoginRequest`

#### `SsoUrl`

The identity provider's single sign-on URL to open in a browser.

*Source: `dotnet/BastionVault.IntegrationSdk/SamlOperations.cs:11`*

#### `RelayState`

The relay state to hand back to `CallbackAsync`.

*Source: `dotnet/BastionVault.IntegrationSdk/SamlOperations.cs:14`*

#### `RequestId`

The SAML request id, for correlating the assertion with this request.

*Source: `dotnet/BastionVault.IntegrationSdk/SamlOperations.cs:17`*

### `SamlOperations`

#### `Admin`

AUT-060's role and config administration.

*Source: `dotnet/BastionVault.IntegrationSdk/SamlOperations.cs:43`*

#### `LoginAsync(redirectUri, role, mount, options, cancellationToken)`

AUT-060: `POST auth/{mount}/login`, unauthenticated. Despite the path, this is
not the login — it starts the browser round trip and returns no token.
<see cref="CallbackAsync"/> is the login.

Wire params: `redirect_uri` (optional), `role` (optional). Returns a <see cref="SamlLoginRequest"/>, never `null` (throws instead). Conformance: Shared (AUT-060). Errors beyond the common set (ERR-061): `BV-PROTOCOL-002`.

**Spec:** `Auth.Saml.Login — AUT-060`

*Source: `dotnet/BastionVault.IntegrationSdk/SamlOperations.cs:59`*

#### `CallbackAsync(samlResponse, relayState, mount, options, cancellationToken)`

AUT-060: `POST auth/{mount}/callback`. This is the login, so the whole login response
contract (AUT-010…AUT-013) applies and no token header is sent (TRN-015).

Wire params: `saml_response`, `relay_state`. Returns <see cref="AuthInfo"/>, never `null`. Conformance: Shared (AUT-060). Errors beyond the common set (ERR-061): AUT-010…AUT-013's login-response refinements.

**Spec:** `Auth.Saml.Callback — AUT-060`

*Source: `dotnet/BastionVault.IntegrationSdk/SamlOperations.cs:92`*

