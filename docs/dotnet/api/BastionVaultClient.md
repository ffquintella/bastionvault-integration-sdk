# `BastionVaultClient` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs`](../../../dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `BastionVaultClient`

#### `Config`

The fully resolved, immutable configuration this client was constructed with.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:125`*

#### `Transport`

The transport this client sends requests through (OVR-001), or `null` when none was supplied.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:128`*

#### `IsInsecure`

True when certificate verification is disabled (CFG-018); a CNF-030 warning was already logged.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:131`*

#### `Namespace`

The active namespace for this client or view (differs from <see cref="Config"/>'s only after <see cref="WithNamespace"/>).

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:134`*

#### `Logical`

The four logical primitives and the `Raw` escape hatch (TRN-001).

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:137`*

#### `Auth`

The `Auth` area (OVR-008): the token source (AUT-001), the current credential
(AUT-004), the token store (AUT-020, AUT-080…AUT-085) and the token-helper write path
(CFG-031, CFG-032).

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:144`*

#### `Sys`

The Core `sys` surface (OVR-008, D-M3-1): health and status (SYS-001, SYS-002, SYS-005,
SYS-006, SYS-008) and self capability introspection (SYS-050…SYS-053).

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:150`*

#### `Kv`

The `Kv` area (OVR-008, 07 — KV engine): the version-explicit `Kv.V1` and
`Kv.V2` sub-clients (KV-002).

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:156`*

#### `Identity`

SYS-080's identity self-service surface: the calling token's profile, default account, SSH
security keys and namespace assignment. Every route is `/v2`-pinned (TRN-071).

Named `Identity` rather than `Sys.Identity` because that is the operation name
`06-system-api.md` and Appendix A both write, and it is the cross-language contract
Rust and Python transcribe (DR-0012 D-M7-27). `12-other-engines-and-identity.md`'s
wider `Identity.*` surface is a later milestone's, and is additive to this class.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:168`*

#### `Transit`

The Transit engine surface (08 — Transit engine): encryption, signing, HMAC and datakeys as
a service. `mount` defaults to `"transit"`. The SDK performs no cryptography
itself (00 §Purpose, §Non-goals) — every member base64-encodes, sends one request, and
parses the response.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:176`*

#### `Totp`

11 — TOTP engine (OVR-008): key CRUD plus code generation and validation. The SDK
generates and validates no codes itself (OVR-002); see <see cref="TotpOperations"/>.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:182`*

#### `Pki`

09 — PKI engine (OVR-008): roles, issuance, certificates and the CRL. `mount` defaults
to `"pki"`. The SDK performs no cryptography and parses no certificate itself (00
§Purpose, §Non-goals); every PEM field is the server's bytes, unmodified (PKI-001). CA
lifecycle, managed keys, tidy, ACME and the two queues are later slices (D-M9-5).

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:190`*

#### `Ssh`

10 — SSH engine (OVR-008): CA configuration, roles, CA-mode signing and OTP-mode
credentials. `mount` defaults to `"ssh"`. The SDK performs no cryptography (00
§Purpose, §Non-goals, D-M9-1); every OpenSSH key and certificate line is the server's bytes,
unmodified.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:198`*

#### `SshBroker`

10 — SSH broker (OVR-008): login-brokering policy across the global, type, asset-group and
resource tiers, and the effective-policy resolution. Every route is `/v2`-pinned
(SSB-001) against the fixed `ssh-broker` logical mount.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:205`*

#### `AssetGroups`

12 — Asset groups (OVR-008): the `resource-group/` mount's group CRUD, history,
resource/secret lookups (IDN-001's base64url treatment for the latter) and reindex.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:211`*

#### `Resources`

12 — Resources (OVR-008): the `resource` engine's records, attached secrets
(`Secrets`, RSC-002) and connect-MFA flow
(`Connect`, RSC-001). `mount` defaults to `"resources"`.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:218`*

#### `Files`

12 — Files (OVR-008): the `files` engine's metadata, content and sync targets
(`Sync`). `mount` defaults to `"files"`. FIL-001: every
content parameter is `byte[]`; the SDK performs the base64 encoding/decoding.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:225`*

#### `Ldap`

12 — LDAP / Active Directory (OVR-008): config, root rotation, connection check, static
roles and the service-account library. `mount` defaults to `"openldap"`.
LDP-001's insecure-TLS acknowledgement is enforced client-side before any request is sent.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:232`*

#### `CertLifecycle`

12 — Cert lifecycle (OVR-008): renewal targets, their renewer state, the scheduler config
and the deliverer registry. `mount` defaults to `"cert-lifecycle"`. Carries no
requirement ID of its own (DR-0017); every MUST is the generic Shape A envelope and
standard error mapping (03/04).

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:240`*

#### `Notifications`

12 — Notifications (OVR-008): sending, the inbox, channels and configuration. `mount`
defaults to `"notifications"`. Carries no requirement ID of its own (DR-0017); every
MUST is the generic Shape A envelope and standard error mapping (03/04).

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:247`*

#### `Rustion`

12 — Rustion (OVR-008): the bastion-integration mount's operator-facing surface (targets,
master key, authority attestation, sessions, recordings, policy, bastion groups, dispatcher
and telemetry). `mount` defaults to `"rustion"`. RUS-001..003.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:254`*

#### `RateGateState`

The observable client-side rate-gate pause state (D-M1b-16).

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:257`*

#### `InputLabel`

DSC-035's `Client.InputLabel`: the address as configured, verbatim, in both literal and discovery mode.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:260`*

#### `SelectedNode`

DSC-035's `Client.SelectedNode`: the node discovery pinned, or `null`
before discovery has run and in literal mode, where nothing was probed and nothing was
chosen (D-M5-10).

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:267`*

#### `ConnectAsync(cancellationToken)`

Runs cluster discovery and pins a node, returning the pick. Idempotent: a second call on a
pinned client returns the existing pick without re-probing (D-M5-9).

Discovery cannot run in the constructor — it is asynchronous and it can fail, and CFG-005's
construction must stay synchronous and non-networking — so it is either this method or the
first operation, which runs it lazily (D-M5-8 ruling 3, D-M5-9).

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:284`*

#### `DiscoverAsync(cancellationToken)`

DSC-036's diagnostics: the full ranked candidate table, without changing the pinned node.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:292`*

#### `ReconnectAsync(cancellationToken)`

Re-runs full discovery (SRV plus probe) and re-pins. Safe to call concurrently.
`null` on a literal-mode client, for the reason
<see cref="ConnectAsync"/> gives.

The member is pinned by D-M5-8 and lands with the rest of the surface so the shape is
reviewed once. DSC-046 — which is the requirement this member exists for, including its
concurrency clause under an in-flight failover — stays baselined for M5b, the slice that owns
the failover lock it has to interact with.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:308`*

#### `SetToken(token)`

Replaces the token used by this client and every view sharing its token cell (CFG-070),
which per AUT-001 means replacing its `TokenSource` with a
`Static` one. Thread-safe; in-flight requests keep the token
they started with, because each pass resolved its own snapshot before entering the retry
loop (D-M1b-9).

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:320`*

#### `ClearToken()`

Clears the token used by this client and every view sharing its token cell (CFG-070).

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:327`*

#### `WithNamespace(ns)`

Returns a lightweight view sharing the transport, the configuration and the same token cell —
a <see cref="SetToken"/> on this client is visible to the view (CFG-071) — differing only in
namespace.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:337`*

#### `Dispose()`

AUT-094: stops this client's automatic renewal loop. Idempotent, and safe to call from
inside a renewal callback.

Deliberately does not wait for the loop to unwind. Dispose is reachable from a
renewal callback, and a Dispose that awaited the loop would then be waiting on the thread
it is running on. The loop observes the cancellation and emits
`OnStopped` with
`Disposed`, which is how an application learns it has
finished.






It disposes the transport only when this client created it (DR-0020 D-2,
<see cref="ownsTransport"/>). An injected transport is the application's (OVR-001), may be
shared between clients, and CFG-072's "construct a new client" would otherwise tear down a
connection pool the caller still owns. A <see cref="WithNamespace"/> view owns no loop and
never owns a transport, so disposing one is a no-op and leaves its parent's renewal and
transport running.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClient.cs:365`*

