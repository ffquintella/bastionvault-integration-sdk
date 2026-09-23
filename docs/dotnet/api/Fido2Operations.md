# `Fido2Operations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/Fido2Operations.cs`](../../../dotnet/BastionVault.IntegrationSdk/Fido2Operations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `Fido2Operations`

#### `LoginBeginAsync(username, mount, options, cancellationToken)`

AUT-035: `POST auth/{mount}/login/begin`, unauthenticated, returning the server's
WebAuthn assertion options uninterpreted.

Wire params: `username`. Returns a <see cref="WebAuthnAssertionOptions"/>, never `null`. Conformance: Complete (AUT-035). Errors beyond the common set (ERR-061): none.

**Spec:** `Auth.Fido2.LoginBegin — AUT-035`

*Source: `dotnet/BastionVault.IntegrationSdk/Fido2Operations.cs:57`*

#### `LoginCompleteAsync(username, credentialJson, mount, options, cancellationToken)`

AUT-035: `POST auth/{mount}/login/complete`. Completion follows the login response
contract, so every AUT-010…AUT-013 rule applies unchanged.

Wire params: `username`, the assertion response (opaque JSON). Returns <see cref="AuthInfo"/>, never `null`. Conformance: Complete (AUT-035). Errors beyond the common set (ERR-061): `BV-INPUT-001`, `BV-AUTH-003`.

**Spec:** `Auth.Fido2.LoginComplete — AUT-035`

*Source: `dotnet/BastionVault.IntegrationSdk/Fido2Operations.cs:81`*

### `WebAuthnAssertionOptions`

#### `Json`

The server's assertion-options JSON, verbatim and uninterpreted (AUT-035).

*Source: `dotnet/BastionVault.IntegrationSdk/Fido2Operations.cs:26`*

