# `CertOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/CertOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/CertOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `CertOperations`

#### `LoginAsync(mount, options, cancellationToken)`

AUT-070: `POST auth/{mount}/login`, with the client certificate presented at the TLS
layer by CFG-044 rather than in the body.

Wire params: `mount` builds the route; the request body carries no fields
(the client certificate is presented at the TLS layer, not in the body). Returns
<see cref="AuthInfo"/>, never `null`; installs the resulting token
(CFG-060). Conformance: Complete (AUT-070). Errors beyond the common set (ERR-061):
`BV-SERVER-004 UnsupportedByServer` — raised on every current server, since the
`cert` auth backend registers no paths.

**Spec:** `Auth.Cert.Login — AUT-070`

*Source: `dotnet/BastionVault.IntegrationSdk/CertOperations.cs:61`*

