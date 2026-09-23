# `AuthOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/AuthOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/AuthOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `AuthOperations`

#### `TokenSource`

AUT-001: the one `TokenSource` this client holds.
`SetToken` and `Use` replace it
with a `Static` one.

*Source: `dotnet/BastionVault.IntegrationSdk/AuthOperations.cs:39`*

#### `CurrentToken`

AUT-004: the current token as a redacting secret type, or `null` when the
client holds none. Reading this never performs a resolution, so it can neither trigger a
login nor call an application callback.

*Source: `dotnet/BastionVault.IntegrationSdk/AuthOperations.cs:46`*

#### `TokenInfo`

AUT-004: the last `LookupSelfAsync` result, if any.

*Source: `dotnet/BastionVault.IntegrationSdk/AuthOperations.cs:49`*

#### `Token`

The token-store operations (AUT-020, AUT-080…AUT-085).

*Source: `dotnet/BastionVault.IntegrationSdk/AuthOperations.cs:52`*

#### `Userpass`

The Userpass auth method (AUT-030…AUT-032).

*Source: `dotnet/BastionVault.IntegrationSdk/AuthOperations.cs:55`*

#### `AppId`

The AppID auth method, wire type `approle` (AUT-040…AUT-044).

*Source: `dotnet/BastionVault.IntegrationSdk/AuthOperations.cs:58`*

#### `Fido2`

The standalone FIDO2 auth mount (AUT-035).

*Source: `dotnet/BastionVault.IntegrationSdk/AuthOperations.cs:61`*

#### `Ferrogate`

The FerroGate machine-identity auth method (AUT-050…AUT-054).

*Source: `dotnet/BastionVault.IntegrationSdk/AuthOperations.cs:64`*

#### `Oidc`

The OIDC auth method, browser-mediated (AUT-060).

*Source: `dotnet/BastionVault.IntegrationSdk/AuthOperations.cs:67`*

#### `Saml`

The SAML auth method, browser-mediated (AUT-060).

*Source: `dotnet/BastionVault.IntegrationSdk/AuthOperations.cs:70`*

#### `Cert`

The certificate auth method — disabled on current servers (AUT-070).

*Source: `dotnet/BastionVault.IntegrationSdk/AuthOperations.cs:73`*

#### `AuthenticateAsync(cancellationToken)`

AUT-002's eager login: forces a `Login` source to log in
now rather than on the first authenticated request, so a bad credential surfaces at startup
instead of inside the first operation that needs a token.

The documented answer to AUT-002's "the SDK MUST document which" is lazy (D-M2-9):
a `Login` source performs its login on the first authenticated
request. This method exists only to force it, and it is single-flighted with that lazy
path, so calling it concurrently with a request performs one login (D-M2-11(a)).






Calling it repeatedly does not log in repeatedly: the source caches its token, and only
`Invalidate` (AUT-003's re-login, AUT-093's renewal recovery) discards it.

**Spec:** `Auth.Authenticate — AUT-002`

*Source: `dotnet/BastionVault.IntegrationSdk/AuthOperations.cs:101`*

#### `PersistToken()`

CFG-031: writes the current token to `TokenFile` with owner-only permissions. This is
the only way the SDK ever writes that file — a login never does, which is why the
requirement makes it an explicit call rather than a side effect.

HTTP call: none — local file write. Conformance: Core (CFG-031). Errors beyond the common set (ERR-061): `BV-INPUT-001`, `BV-CONFIG-005`.

**Spec:** `Auth.PersistToken — CFG-031`

*Source: `dotnet/BastionVault.IntegrationSdk/AuthOperations.cs:142`*

#### `ForgetPersistedToken()`

CFG-032: deletes `TokenFile` if it is present, and does not fail if it is absent.

HTTP call: none — local file delete. Conformance: Core (CFG-032). No error codes beyond the common set (ERR-061).

**Spec:** `Auth.ForgetPersistedToken — CFG-032`

*Source: `dotnet/BastionVault.IntegrationSdk/AuthOperations.cs:171`*

