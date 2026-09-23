# `FerrogateOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/FerrogateOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/FerrogateOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `EnrollResult`

#### `Status`

The state the enrolment landed in, normally `Pending`.

*Source: `dotnet/BastionVault.IntegrationSdk/FerrogateOperations.cs:68`*

#### `Raw`

The whole `data` object; see `Raw`.

*Source: `dotnet/BastionVault.IntegrationSdk/FerrogateOperations.cs:71`*

### `FerrogateOperations`

#### `Admin`

AUT-054's root-level administration surface.

*Source: `dotnet/BastionVault.IntegrationSdk/FerrogateOperations.cs:101`*

#### `RequirementAsync(mount, options, cancellationToken)`

AUT-051: `GET auth/{mount}/requirement`, callable without a token (CFG-020).

Wire params: none. Returns a <see cref="FerrogateRequirement"/>, never `null` (throws instead). Conformance: Shared (AUT-051). Errors beyond the common set (ERR-061): `BV-PROTOCOL-002`.

**Spec:** `Auth.Ferrogate.Requirement — AUT-051`

*Source: `dotnet/BastionVault.IntegrationSdk/FerrogateOperations.cs:112`*

#### `IsMachineIdentityRequiredAsync(mount, options, cancellationToken)`

AUT-051's cached convenience: <see cref="RequirementAsync"/>'s
`RequireMachineIdentity`, fetched once per
(namespace, mount) and answered from memory thereafter.

The cache lives on the client, not on this short-lived view, so `Client.Auth` read
twice still answers from one fetch. It has no expiry: the flag is a deployment-level
property, AUT-051 names no lifetime, and a caller who needs the live answer calls
<see cref="RequirementAsync"/>, which refreshes the cache as a side effect (D-M6-14).






⚠️ The namespace is half the key (D-M6-16). The client's state is shared by every
`WithNamespace` view and `Namespace` overrides it per call,
so a mount-only key would answer one tenant's auth posture out of another's entry — and in
the fail-open direction, telling a namespace that does require a machine identity
that it does not.

**Spec:** `Auth.Ferrogate.IsMachineIdentityRequired — AUT-051`

*Source: `dotnet/BastionVault.IntegrationSdk/FerrogateOperations.cs:162`*

#### `LoginAsync(childToken, dpopProof, userToken, mount, options, cancellationToken)`

AUT-050: `POST auth/{mount}/login` with body
`{"token": childToken, "dpop": proof?, "user_token": userToken?}`.

The proof travels twice. AUT-050 requires a supplied `dpopProof` to
be sent both as the `DPoP` header and as the `dpop` body field; this is not a
redundancy the SDK may optimise away, because the server reads the two independently. The
header is merged into `Headers` rather than replacing it, and
`DPoP` is not on TRN-012's reserved list, so a caller's own headers survive.

**Spec:** `Auth.Ferrogate.Login — AUT-050`

*Source: `dotnet/BastionVault.IntegrationSdk/FerrogateOperations.cs:201`*

#### `StatusAsync(childToken, dpopProof, mount, options, cancellationToken)`

AUT-052: `POST auth/{mount}/status`, callable without a token, mapping the wire
`status` onto <see cref="MachineIdentityStatus"/>.

Wire params: `token`, `dpop` (optional). Returns a <see cref="MachineStatus"/>, never `null`. Conformance: Shared (AUT-052). Errors beyond the common set (ERR-061): none beyond the status mapping in <see cref="MachineIdentityStatus"/>.

**Spec:** `Auth.Ferrogate.Status — AUT-052`

*Source: `dotnet/BastionVault.IntegrationSdk/FerrogateOperations.cs:232`*

#### `EnrollAsync(spiffeId, comment, mount, options, cancellationToken)`

AUT-052: `POST auth/{mount}/enroll`, callable without a token (CFG-020).
Never returns a token — enrolment requests a machine identity, it does not grant one.
The machine becomes usable only once an administrator approves it
(`ApproveAsync`).

Wire params: `spiffe_id`, `comment` (optional). Returns an <see cref="EnrollResult"/>, never `null`, and never a token. Conformance: Shared (AUT-052). Errors beyond the common set (ERR-061): none.

**Spec:** `Auth.Ferrogate.Enroll — AUT-052`

*Source: `dotnet/BastionVault.IntegrationSdk/FerrogateOperations.cs:266`*

### `FerrogateRequirement`

#### `RequireMachineIdentity`

Whether this server requires a machine identity for AppID logins (AUT-051).

*Source: `dotnet/BastionVault.IntegrationSdk/FerrogateOperations.cs:35`*

#### `ExpectedAudience`

The audience a child token must carry.

*Source: `dotnet/BastionVault.IntegrationSdk/FerrogateOperations.cs:38`*

#### `TrustDomain`

The SPIFFE trust domain the Machine Identity Agent issues under.

*Source: `dotnet/BastionVault.IntegrationSdk/FerrogateOperations.cs:41`*

#### `MiaEnvironment`

The Machine Identity Agent environment name.

*Source: `dotnet/BastionVault.IntegrationSdk/FerrogateOperations.cs:44`*

### `MachineStatus`

#### `Status`

The enrolment state (AUT-052).

*Source: `dotnet/BastionVault.IntegrationSdk/FerrogateOperations.cs:51`*

#### `Raw`

The whole `data` object, so a field the requirement does not name is still reachable
without the SDK guessing a name for it (D-M1c-25).

*Source: `dotnet/BastionVault.IntegrationSdk/FerrogateOperations.cs:57`*

