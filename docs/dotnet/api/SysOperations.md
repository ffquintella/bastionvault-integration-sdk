# `SysOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/SysOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/SysOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `SysOperations`

#### `HealthAsync(options, cancellationToken)`

SYS-001, SYS-002: `GET sys/health`, unauthenticated (CFG-020's exemption list already
carries it). Never raises for a `200`/`429`/`501`/`503` response — those
four are folded into `State` instead — and raises only on a
transport failure or a non-JSON body (`BV-PROTOCOL-002`). No `472`/`473` code
and no `standbyok`-style query parameter exists on this endpoint or is ever sent.

Wire params: none. Returns a <see cref="HealthStatus"/>, never `null`, for
every one of the four statuses this call answers without raising. Conformance: Core
(SYS-001). Errors beyond the common set (ERR-061): `BV-PROTOCOL-002` (non-JSON body).

**Spec:** `Sys.Health — SYS-001`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:66`*

#### `SealStatusAsync(options, cancellationToken)`

SYS-005: `GET sys/seal-status`, unauthenticated, always `200`. The server's wire
`t`/`n` carry `secret_shares`/`secret_threshold` — reversed from the
usual naming — so both the raw fields and the correctly-named derived ones are exposed.

Wire params: none. Returns a <see cref="SealStatus"/>, never `null`.
Conformance: Core (SYS-005). No error codes beyond the common set (ERR-061).

**Spec:** `Sys.SealStatus — SYS-005`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:102`*

#### `ServerInfoAsync(options, cancellationToken)`

SYS-008: `GET sys/info`. The anonymous tier carries `Initialized`
and `Sealed` only; a live token adds the other four, which stay
`null` — never defaulted — when the server omits them.

Wire params: none. Returns a <see cref="ServerInfo"/>, never `null`.
Conformance: Core (SYS-008). No error codes beyond the common set (ERR-061).

**Spec:** `Sys.ServerInfo — SYS-008`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:120`*

#### `ClusterStatusAsync(options, cancellationToken)`

SYS-006: `GET sys/cluster-status`. Requires a live token (CFG-020/ERR-022's ordinary
client-side refusal applies — this path is not on the unauthenticated list); the documented
`403` is not special-cased here and surfaces as the ordinary typed
`BV-AUTHZ-003` exception through the shared status/message mapping, like any other
mapped error. Optional fields stay `null` on a non-clustered backend.

Wire params: none. Returns a <see cref="ClusterStatus"/>, never `null`.
Conformance: Standard (SYS-006). Errors beyond the common set (ERR-061):
`BV-AUTHZ-003` (no live token).

**Spec:** `Sys.ClusterStatus — SYS-006`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:150`*

#### `CapabilitiesSelfAsync(paths, options, cancellationToken)`

SYS-050…SYS-053: `POST /v2/sys/capabilities-self` (TRN-071 — the `/v2` prefix is
pinned and cannot be overridden away by `ApiVersion`). An empty
`paths` is refused client-side with `BV-INPUT-002` before any request
is sent (SYS-052). Reads the `capabilities` map, never the duplicated top-level keys.

Wire params: `paths` is the request body (`{"paths": [...]}`); no
query params. Returns a <see cref="Capabilities"/>, never `null`.
Conformance: Core (SYS-050). Errors beyond the common set (ERR-061): `BV-INPUT-002`
(empty `paths`, client-side).

**Spec:** `Sys.CapabilitiesSelf — SYS-050`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:180`*

#### `CanAsync(path, capability, options, cancellationToken)`

SYS-053: the spec-named `Client.Sys.Can` convenience (":9 — All operations live under
`Client.Sys`"), one round trip. Fetches `path`'s capabilities via
<see cref="CapabilitiesSelfAsync"/> and delegates the boolean check to
`Can` — the no-round-trip form, kept for a caller that already
holds a fetched <see cref="Capabilities"/> result.

HTTP call: none directly — delegates to <see cref="CapabilitiesSelfAsync"/>
(`POST /v2/sys/capabilities-self`). Wire params: as that call's. Returns a
`bool`, never `null`. Conformance: Core (SYS-053). No error
codes beyond the common set (ERR-061) other than those <see cref="CapabilitiesSelfAsync"/>
already documents.

**Spec:** `Sys.Can — SYS-053`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:226`*

#### `HsmStatusAsync(options, cancellationToken)`

06 — system API, "Health and status": `GET /v2/sys/hsm/status`. v2-only, and the
`/v2` prefix is pinned here (TRN-071) exactly as it is for
<see cref="CapabilitiesSelfAsync"/>, so a `v1` client and a
`ApiVersion` override both still reach the v2 handler. Every
named field stays optional and `Raw` carries the whole object,
because the specification's body ends in an ellipsis.

Wire params: none. Returns a <see cref="HsmStatus"/>, never `null`.
Conformance: Standard (Appendix A groups this mount, no per-operation row). No error
codes beyond the common set (ERR-061).

**Spec:** `Sys.HsmStatus — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:247`*

#### `InitStatusAsync(options, cancellationToken)`

SYS-010's status probe: `GET sys/init` → `{"initialized": bool}`. Unauthenticated (CFG-020 lists the path).

Wire params: none. Returns a `bool`, never `null`.
Conformance: Core (SYS-010). No error codes beyond the common set (ERR-061).

**Spec:** `Sys.InitStatus — SYS-010`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:270`*

#### `InitAsync(shares, threshold, options, cancellationToken)`

SYS-010, SYS-011: `PUT sys/init`, which a vault answers exactly once. Both
`shares` and `threshold` or neither (omitted is the HSM
auto-unseal case), and `1 ≤ threshold ≤ shares ≤ 255`; either rule broken is
`BV-INPUT-001` refused client-side, before a request is sent.

The returned <see cref="InitResult"/> is <see cref="IDisposable"/> and holds the only copy
of the unseal keys and the root token that will ever exist: the server keeps none. Dispose
it once the material has been stored (SYS-011).



Wire params: body carries `secret_shares`/`secret_threshold`, both or neither.
Conformance: Core (SYS-010). Errors beyond the common set (ERR-061): `BV-INPUT-001`
(the shares/threshold rule, client-side).

**Spec:** `Sys.Init — SYS-010`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:295`*

#### `SealAsync(options, cancellationToken)`

SYS-013: `PUT sys/seal` → `204`, sudo-gated. Flagged non-retryable and
excluded from failover: sealing is the one operation whose success removes the node's
ability to answer, so a replay against a second node would seal a second node, and a replay
against the same one would report a failure the first attempt had already completed.

Wire params: none. Returns `void` on the server's `204`.
Conformance: Standard (SYS-013). No error codes beyond the common set (ERR-061).

**Spec:** `Sys.Seal — SYS-013`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:329`*

#### `UnsealAsync(key, options, cancellationToken)`

SYS-012, SYS-013: `PUT sys/unseal` → <see cref="SealStatus"/>, idempotent on the server
when the vault is already unsealed. An invalid key answers `BV-INPUT-101` and an
uninitialised vault `BV-SERVER-007`, both through the shared Appendix B recognition.
Flagged non-retryable and excluded from failover (SYS-013): unseal progress is
per node, so a replay elsewhere would spend a share against a different node's
counter.

Wire params: `key` is the request body (`{"key": ...}`); no query
params. Returns a <see cref="SealStatus"/>, never `null`. Conformance:
Core (SYS-012). Errors beyond the common set (ERR-061): `BV-INPUT-101` (invalid key),
`BV-SERVER-007` (uninitialised vault).

**Spec:** `Sys.Unseal — SYS-012`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:351`*

#### `ListMountsAsync(options, cancellationToken)`

SYS-020, SYS-022: `GET sys/mounts` → the mount table, keyed by path with a trailing
`/`. ⚠️ Two fields per entry and no more; see <see cref="MountInfo"/>.

Wire params: none. Returns the mount table, never `null`. Conformance:
Core (the two-field shape is table prose in 06-system-api.md, not a numbered
requirement; SYS-020's own MUST governs <see cref="MountAsync"/>, not this read). No
error codes beyond the common set (ERR-061).

**Spec:** `Sys.ListMounts — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:373`*

#### `MountAsync(path, request, options, cancellationToken)`

SYS-020, SYS-022, SYS-024: `POST sys/mounts/{path}` → `204`. The path is accepted
with or without a trailing `/`; an empty one is `BV-INPUT-001` client-side.
A mount-quota breach is `507` → `BV-QUOTA-001` through the shared mapping.
Invalidates this client's SYS-026 mount-type cache for the active namespace.

Wire params: `path` builds the route; `request` is the
body. Returns `void` on the server's `204`. Conformance: Core
(SYS-020). Errors beyond the common set (ERR-061): `BV-INPUT-001` (empty path,
client-side), `BV-QUOTA-001` (mount-quota breach).

**Spec:** `Sys.Mount — SYS-020`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:391`*

#### `UnmountAsync(path, options, cancellationToken)`

SYS-022, SYS-026: `DELETE sys/mounts/{path}` → `204`, and invalidates the mount-type cache.

Wire params: `path` builds the route; no body. Returns
`void` on the server's `204`. Conformance: Core (SYS-022). No error
codes beyond the common set (ERR-061).

**Spec:** `Sys.Unmount — SYS-022`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:408`*

#### `RemountAsync(from, to, options, cancellationToken)`

SYS-022, SYS-023, SYS-026: `POST sys/remount` with `{"from", "to"}` → `204`.
Both paths are sent in the table form the server's own error text uses (`kv/`), and
both are accepted from the caller with or without the slash. Invalidates the mount-type
cache.

Wire params: body carries `from`/`to`, table-form. Returns `void`
on the server's `204`. Conformance: Standard (SYS-023). Errors beyond the common set
(ERR-061): `BV-INPUT-001` (unknown mount table type, remapped from the server's
`409`).

**Spec:** `Sys.Remount — SYS-023`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:430`*

#### `ListMountsDetailedAsync(options, cancellationToken)`

06 — system API, "Mounts": `GET sys/internal/ui/mounts`, the ACL-filtered table, split
into its `secret` and `auth` halves. Auth keys are relative (SYS-030) and every
key carries its trailing `/` (SYS-022).

Wire params: none. Returns a <see cref="MountTable"/>, never `null`.
Conformance: Standard (SYS-030). No error codes beyond the common set (ERR-061).

**Spec:** `Sys.ListMountsDetailed — SYS-030`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:459`*

#### `ReadMountAsync(path, options, cancellationToken)`

SYS-025: there is no per-mount read on the server — `GET sys/mounts/{path}` returns the
whole table — so this filters <see cref="ListMountsAsync"/> client-side and returns
`null` when the mount is absent. Deliberately not served from the
SYS-026 cache: SYS-026 scopes the cache to <see cref="MountTypeOfAsync"/>, and a
`ReadMount` that could be up to 60 seconds stale is a different contract from the one
the specification writes.

HTTP call: none directly — delegates to <see cref="ListMountsAsync"/> (`GET sys/mounts`)
and filters client-side. Wire params: as that call's. Returns a nullable
<see cref="MountInfo"/>: `null` when the mount is absent. Conformance:
Standard (SYS-025). No error codes beyond the common set (ERR-061).

**Spec:** `Sys.ReadMount — SYS-025`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:487`*

#### `MountTypeOfAsync(path, options, cancellationToken)`

SYS-026: the mount's type, or `null` when there is no such mount, from a
per-client cache with a 60-second TTL that <see cref="MountAsync"/>,
<see cref="UnmountAsync"/> and <see cref="RemountAsync"/> invalidate. This is the lookup
`Kv.DetectVersion` is built on (KV-001).

HTTP call: none on a cache hit; otherwise delegates to <see cref="ListMountsAsync"/>
(`GET sys/mounts`) to refill the cache. Wire params: as that call's. Returns a
nullable `string`: `null` when there is no such mount.
Conformance: Standard (SYS-026). No error codes beyond the common set (ERR-061).

**Spec:** `Sys.MountTypeOf — SYS-026`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:507`*

#### `ListAuthMethodsAsync(options, cancellationToken)`

SYS-030: `GET sys/auth`, the same two-field shape as <see cref="ListMountsAsync"/>, with relative keys (`userpass/`, never `auth/userpass/`).

Wire params: none. Returns the auth-method table, never `null`.
Conformance: Core (SYS-030). No error codes beyond the common set (ERR-061).

**Spec:** `Sys.ListAuthMethods — SYS-030`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:527`*

#### `EnableAuthMethodAsync(path, request, options, cancellationToken)`

SYS-030: `POST sys/auth/{path}` → `204`. `auth/userpass/` and `userpass` are both accepted and normalise to the same mount.

Wire params: `path` builds the route; `request` is the
body. Returns `void` on the server's `204`. Conformance: Core
(SYS-030). No error codes beyond the common set (ERR-061).

**Spec:** `Sys.EnableAuthMethod — SYS-030`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:539`*

#### `DisableAuthMethodAsync(path, options, cancellationToken)`

SYS-030: `DELETE sys/auth/{path}` → `204`. ⚠️ This revokes every token the method issued.

Wire params: `path` builds the route; no body. Returns
`void` on the server's `204`. Conformance: Core (SYS-030). No error
codes beyond the common set (ERR-061).

**Spec:** `Sys.DisableAuthMethod — SYS-030`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:555`*

#### `Legacy`

SYS-040: the legacy `sys/policy` surface, which the specification exposes as a
MAY. Reads only — see DR-0012 D-M7-14 for why the legacy write and delete are not
here.

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:568`*

#### `ListPoliciesAsync(options, cancellationToken)`

SYS-040: `GET sys/policies/acl` → `{"keys": [...]}`. In the root namespace the server appends `root` to the list; the SDK passes the list through as sent.

Wire params: none. Returns the policy-name list, never `null`.
Conformance: Core (SYS-040). No error codes beyond the common set (ERR-061).

**Spec:** `Sys.ListPolicies — SYS-040`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:576`*

#### `ReadPolicyAsync(name, options, cancellationToken)`

SYS-040: `GET sys/policies/acl/{name}` → <see cref="Policy"/>, or `null`
when there is no such policy. The server's `404 No policy named: X` is Appendix B's
`BV-NOTFOUND-005` rule, and is turned into absence here rather than raised, because the
specification's response column writes the two together ("→ null / `BV-NOTFOUND-005`")
and a reader is the one operation where "not there" is an answer rather than a failure.

Wire params: `name` builds the route; no query or body. Returns a
nullable <see cref="Policy"/>: `null` when there is no such policy.
Conformance: Core (SYS-040). Errors beyond the common set (ERR-061): none — the
`BV-NOTFOUND-005` the server would otherwise raise is turned into a `null`
return here.

**Spec:** `Sys.ReadPolicy — SYS-040`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:598`*

#### `WritePolicyAsync(name, hcl, options, cancellationToken)`

SYS-040, SYS-041, SYS-042: `POST sys/policies/acl/{name}` with `{"policy": hcl}`
→ `204`. `root` and `test` are refused client-side with
`BV-INPUT-010` (SYS-041; `test` is the dry-run route's own segment, so writing it
would collide with <see cref="TestPolicyAsync"/>). The two SYS-042 server strings —
sentinel policies inside a namespace, and the cross-namespace path refusal — are Appendix B
§2 recognition rules and reach the caller as `BV-INPUT-100` and
`BV-INPUT-102 CrossNamespacePolicyPath` through the shared mapping, with no
operation-local remap.

Wire params: `name` builds the route; `hcl` is the body's
`policy` field. Returns `void` on the server's `204`.
Conformance: Core (SYS-040). Errors beyond the common set (ERR-061): `BV-INPUT-010`
(reserved name, client-side), `BV-INPUT-100`, `BV-INPUT-102`
(`CrossNamespacePolicyPath`).

**Spec:** `Sys.WritePolicy — SYS-040`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:634`*

#### `DeletePolicyAsync(name, options, cancellationToken)`

SYS-041: `DELETE sys/policies/acl/{name}` → `204`. `default` and `root`
are refused client-side with `BV-INPUT-010`. ⚠️ The reserved set differs from
<see cref="WritePolicyAsync"/>'s and that is not an oversight: SYS-041 reserves `test`
against writes (the dry-run route owns the segment) and `default` against
deletes (it is the policy every token carries).

Wire params: `name` builds the route; no body. Returns
`void` on the server's `204`. Conformance: Core (SYS-041). Errors
beyond the common set (ERR-061): `BV-INPUT-010` (reserved name, client-side).

**Spec:** `Sys.DeletePolicy — SYS-041`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:661`*

#### `PolicyHistoryAsync(name, options, cancellationToken)`

SYS-040: `GET sys/policies/acl/{name}/history` → the `entries` array, newest-first as the server orders it.

Wire params: `name` builds the route; no query or body. Returns an empty
list rather than `null` when the server's `entries` is absent or not
an array. Conformance: Standard (SYS-040). No error codes beyond the common set (ERR-061).

**Spec:** `Sys.PolicyHistory — SYS-040`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:681`*

#### `TestPolicyAsync(draft, cases, name, options, cancellationToken)`

SYS-045: the policy dry-run, `POST /v2/sys/policies/acl/test`. The `/v2` prefix is
pinned (TRN-071, Appendix A), so neither `ApiPrefix` nor
`ApiVersion` can route it at a handler that does not exist.

⚠️ `Policies` is tri-state and the three states are preserved
onto the wire: `null` omits the key (server default `["default"]`),
`[]` is sent as an empty array (the draft alone), and a list is sent as itself (the
draft plus those).






`name` follows `cases` rather than sitting between
`draft` and it, as `SYS-045`'s signature writes it: C# has no
optional parameter before a required one. The wire order is unaffected.






Naming `root` is sent, and the server's `400` is remapped to
`BV-INPUT-010`, the code SYS-045 names. D-M7-26 overturned slice b's client-side
refusal: SYS-045 writes the refusal as an HTTP status code and pairs it with an
unreadable-policy `403` that cannot be known client-side, where SYS-041 says
"client-side" in as many words. `Attempts` and
`StatusCode` are therefore both observable. The remap is
scoped to a call that actually named `root`, so every other `400` this
route answers — a malformed draft first among them — keeps the shared mapping's answer.
Naming a policy the token cannot read is the server's call and reaches the caller as the
`403` → `BV-AUTHZ-001` the shared mapping already produces.






Wire params: body carries `draft`, `cases` and, when named, `name`.
Returns a <see cref="PolicyTestResult"/>, never `null`. Conformance:
Standard (SYS-045). Errors beyond the common set (ERR-061): `BV-INPUT-010` (naming
`root`, remapped from the server's `400`).

**Spec:** `Sys.TestPolicy — SYS-045`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:745`*

#### `ReadPolicyTestsAsync(name, options, cancellationToken)`

SYS-045: `GET /v2/sys/policy-tests/{name}` — the saved effectivity cases, `/v2`-pinned (TRN-071, Appendix A).

Wire params: `name` builds the route; no query or body. Returns an empty
list rather than `null` when the server's `cases` is absent or not an
array. Conformance: Standard (SYS-045). No error codes beyond the common set (ERR-061).

**Spec:** `Sys.ReadPolicyTests — SYS-045`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:789`*

#### `WritePolicyTestsAsync(name, cases, options, cancellationToken)`

SYS-045: `POST /v2/sys/policy-tests/{name}` with `{"cases": [...]}`, `/v2`-pinned. The tri-state is preserved here too — the cases are serialised by the same writer the dry-run uses.

Wire params: `name` builds the route; `cases` is the
body. Returns `void` on the server's `204`. Conformance: Standard
(SYS-045). No error codes beyond the common set (ERR-061).

**Spec:** `Sys.WritePolicyTests — SYS-045`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:817`*

#### `ListNamespacesAsync(options, cancellationToken)`

SYS-060: `LIST sys/namespaces` → the children of the active namespace.

Wire params: none. Returns the child-namespace list, never `null`.
Conformance: Standard (Appendix A groups this mount, no per-operation row; SYS-060's
MUST governs WriteNamespace's reset behaviour, not this read). No error codes beyond
the common set (ERR-061).

**Spec:** `Sys.ListNamespaces — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:835`*

#### `ReadNamespaceAsync(path, options, cancellationToken)`

SYS-060, SYS-061: `GET sys/namespaces/{path}` → the record, or `null`
when there is no such namespace (the server's `404 no such namespace: "x"` is
Appendix B's `BV-NOTFOUND-007` rule). An empty `path` is
`BV-INPUT-001` client-side: it would address the root record, which SYS-061 says is not
reachable over HTTP at all.

Wire params: `path` builds the route; no query or body. Returns a
nullable <see cref="Namespace"/>: `null` when there is no such namespace.
Conformance: Standard (Appendix A groups this mount, no per-operation row; SYS-060's
MUST governs WriteNamespace's reset behaviour, not this read). Errors beyond the
common set (ERR-061): `BV-INPUT-001` (empty path, client-side).

**Spec:** `Sys.ReadNamespace — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:857`*

#### `WriteNamespaceAsync(path, spec, options, cancellationToken)`

SYS-060, SYS-061, SYS-062: `POST sys/namespaces/{path}` → `200` with the record.

⚠️ This is an upsert and a full replace. Every quota
`spec` omits is written as `0` and an omitted
`ChildVisibleDefault` is written as `false`, so
writing a freshly constructed spec at an existing namespace clears its quotas rather
than leaving them alone. <see cref="UpdateNamespaceAsync"/> is the read-merge-write form and
is the one to use to change a single field.



SYS-061: `WriteNamespace("")` is refused client-side with `BV-INPUT-001`. The root
namespace record cannot be written over HTTP and no operation for it exists on this class.






Wire params: `path` builds the route; `spec` is the body.
Returns the written <see cref="Namespace"/>, never `null`. Conformance:
Standard (SYS-060). Errors beyond the common set (ERR-061): `BV-INPUT-001` (empty
path, client-side).

**Spec:** `Sys.WriteNamespace — SYS-060`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:897`*

#### `UpdateNamespaceAsync(path, patch, options, cancellationToken)`

SYS-060: the read-merge-write form of <see cref="WriteNamespaceAsync"/>. Reads the current
record, overlays only the members `patch` sets, and writes the result back
— two round trips, and deliberately so, because the server has no partial update on this
route. A namespace that does not exist raises `BV-NOTFOUND-007` rather than creating
one: a patch has nothing to merge into, and an upsert here would silently write the patch's
unset members as zeroes.

HTTP call: none directly — composed of <see cref="ReadNamespaceAsync"/>'s
`GET sys/namespaces/{path}` followed by <see cref="WriteNamespaceAsync"/>'s
`POST sys/namespaces/{path}`. Wire params: as those two calls'. Returns the merged
<see cref="Namespace"/>, never `null`. Conformance: Standard (SYS-060).
Errors beyond the common set (ERR-061): `BV-NOTFOUND-007` (namespace does not exist).

**Spec:** `Sys.UpdateNamespace — SYS-060`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:924`*

#### `DeleteNamespaceAsync(path, options, cancellationToken)`

SYS-062: `DELETE sys/namespaces/{path}` → `204`.

⚠️ This destroys tenant data. The delete cascades: every mount inside the
namespace is unmounted and its stored secrets go with it. The server refuses while child
namespaces exist, so a tenant tree is removed leaves-first; there is no recursive form and
the SDK does not synthesise one. The operation is named `DeleteNamespace` and never
`Remove…`, because SYS-062 requires the destructive word.



Wire params: `path` builds the route; no body. Returns
`void` on the server's `204`. Conformance: Standard (SYS-062). No
error codes beyond the common set (ERR-061).

**Spec:** `Sys.DeleteNamespace — SYS-062`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:964`*

#### `NamespacesSelfAsync(options, cancellationToken)`

SYS-060: `GET sys/namespaces-self`. `""` denotes root in both `Namespaces` and `TokenNamespace`.

Wire params: none. Returns a <see cref="NamespacesSelf"/>, never `null`.
Conformance: Standard (Appendix A groups this mount, no per-operation row; SYS-060's
MUST governs WriteNamespace's reset behaviour, not this read). No error codes beyond
the common set (ERR-061).

**Spec:** `Sys.NamespacesSelf — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:980`*

#### `ListNamespacesInfoAsync(after, limit, options, cancellationToken)`

SYS-060: `GET sys/namespaces-info?after=&amp;limit=`, the cursor-paginated bulk listing
(14 — batch and request efficiency). `limit` defaults to 100 and is
validated to `1 ≤ limit ≤ 500` client-side (`BV-INPUT-004`);
`after` is the previous page's `Next`, passed verbatim
and never computed. An empty `next` is exposed as `null`, and a page
whose `keys` and `records` differ in length is `BV-PROTOCOL-002`.

Wire params: `after`/`limit` query params, `after` omitted when
`null`. Returns a <see cref="Page{T}"/>, never `null`.
Conformance: Standard (Appendix A groups this mount, no per-operation row; SYS-060's
MUST governs WriteNamespace's reset behaviour, not this listing). Errors beyond the
common set (ERR-061): `BV-INPUT-004` (`limit` out of range, client-side),
`BV-PROTOCOL-002` (mismatched `keys`/`records` length).

**Spec:** `Sys.ListNamespacesInfo — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:1011`*

#### `ListNamespacesInfoAllAsync(limit, maxRecords, options, cancellationToken)`

PAG-004: <see cref="ListNamespacesInfoAsync"/>'s iterator. Walks every page in cursor order
via <see cref="PagingWire.IteratePagesAsync{T}"/>, so it honours the client rate gate
exactly as a caller looping <see cref="ListNamespacesInfoAsync"/> by hand would — each page
fetch is ordinary `Request` traffic, and no exemption is
invented for it (D-M8-7). Raises `BV-INPUT-005` at `maxRecords`
(default 5000) rather than paging without bound.

HTTP call: none directly — repeatedly delegates to <see cref="ListNamespacesInfoAsync"/>
(`GET sys/namespaces-info?after=&amp;limit=`), one call per page. Wire params: as that
call's. Returns an async sequence, never `null`, terminating when the
server reports no further page. Conformance: Standard (PAG-004). Errors beyond the common
set (ERR-061): `BV-INPUT-005` (`maxRecords` exceeded, client-side).

**Spec:** `Sys.ListNamespacesInfoAll — PAG-004`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:1075`*

#### `CacheVersionAsync(topics, watch, ifNoneMatch, options, cancellationToken)`

CCH-001…CCH-005: `GET sys/cache/version?topics=…`. At most 64 topics
(`BV-INPUT-004` otherwise), comma-joined into one `topics` parameter
(CCH-001). `ifNoneMatch`, when given, is sent as `If-None-Match`, and
a `304` answers as `NotModified` rather than an error
(CCH-002) — the transport-level distinction already exists at D-M1b-10, this method only
names it. `watch` raises the per-call timeout to at least 40 s regardless
of any caller-supplied `Timeout` (CCH-003). See
<see cref="CacheVersion"/>'s remarks for CCH-004 and CCH-005: both are about how the caller
reads `Topics`, not about anything this method parses differently.

Wire params: `topics` query param (comma-joined); `watch=1` when
`watch`; `If-None-Match` header when `ifNoneMatch`
is given. Returns a <see cref="CacheVersion"/>, never `null`. Conformance:
Standard (CCH-001). Errors beyond the common set (ERR-061): `BV-INPUT-004` (more than
64 topics, client-side).

**Spec:** `Sys.CacheVersion — CCH-001`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:1106`*

#### `Audit`

SYS-070: the audit device registry and the audit event query.

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:1182`*

#### `Dos`

The DoS-guard admin surface (06 — "Batch, cache version, DoS"). Root-only, `/v2`-pinned, and carrying no `SYS-*` id.

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:1185`*

#### `OwnerTransfer`

The four admin owner-transfer routes. No `SYS-*` id and no specified body — see <see cref="OwnerTransferOperations"/>.

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:1188`*

#### `Exchange`

The four `sys/exchange/*` routes. No `SYS-*` id and no specified body — see <see cref="ExchangeOperations"/>.

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:1191`*

#### `DashboardSummaryAsync(options, cancellationToken)`

06 — "Dashboard…": `GET sys/dashboard/summary`. ⚠️ `audit_24h` and
`attention` are omitted for a caller without audit read and stay optional here; the
rest of the body is on `Raw`, because the specification names
only those two keys.

Wire params: none. Returns a <see cref="DashboardSummary"/>, never `null`.
Conformance: Complete. No per-operation requirement ID (06-system-api.md, "Dashboard,
identity self-service, owner transfers"). No error codes beyond the common set (ERR-061).

**Spec:** `Sys.DashboardSummary — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:1205`*

#### `SsoSettingsAsync(options, cancellationToken)`

06 — "Dashboard…": `GET sys/sso/settings`. Returned unparsed: the specification names the route and no field of the body (D-M1c-25).

Wire params: none. Returns the raw <see cref="JsonElement"/> body, never
`null` (throws `BV-PROTOCOL-002`-family envelope mismatch instead).
Conformance: Complete. No per-operation requirement ID (06-system-api.md, "Dashboard,
identity self-service, owner transfers"). No error codes beyond the common set (ERR-061).

**Spec:** `Sys.SsoSettings — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:1228`*

#### `SsoProvidersAsync(options, cancellationToken)`

06 — "Dashboard…": `GET sys/sso/providers`. Returned unparsed, for the same reason as <see cref="SsoSettingsAsync"/>.

Wire params: none. Returns the raw <see cref="JsonElement"/> body, never
`null` (throws `BV-PROTOCOL-002`-family envelope mismatch instead).
Conformance: Complete. No per-operation requirement ID (06-system-api.md, "Dashboard,
identity self-service, owner transfers"). No error codes beyond the common set (ERR-061).

**Spec:** `Sys.SsoProviders — 06-system-api.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:1244`*

#### `BackupAsync(options, cancellationToken)`

SYS-090: `POST sys/backup` → the raw `.bvbk` bytes.

Excluded from retry and from failover, through the two flags SYS-013 and DSC-045
already own — `nonRetryable` short-circuits the retry predicate ahead of every policy
term, and `nodeLocal` removes the call from `WillFailover`. No third mechanism
was added (D-M7-35).






The response is not buffered above `MaxResponseBytes`: the transport bounds the
read and aborts past the limit (TRN-033, D-M1b-20), so a backup larger than the configured
bound raises `BV-TRANSPORT-004` rather than landing in memory. Raise
`MaxResponseBytes` to take a larger one. See DR-0012 D-M7-34 for why this satisfies
SYS-090's "MUST stream" and what a `Stream`-returning overload would have cost.






The two bounds are not symmetric, and this one is not configurable.
<see cref="RestoreAsync"/> is capped at `MaxRequestBodyBytes` (32 MiB, TRN-032), which
no option raises. A vault whose backup exceeds 32 MiB therefore backs up successfully — the
response bound above is both larger by default and adjustable — and cannot be restored
through this SDK at all. Spec-correct, but the asymmetry is real, so do not read the advice
above as implying the restore path will accept whatever the backup path produced (D-M7-46).






Wire params: none. Returns the raw backup bytes, never `null`.
Conformance: Complete (SYS-090). No error codes beyond the common set (ERR-061), other
than `BV-TRANSPORT-004` (response past `MaxResponseBytes`) already named above.

**Spec:** `Sys.Backup — SYS-090`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:1284`*

#### `RestoreAsync(backup, options, cancellationToken)`

SYS-090, SYS-091: `POST sys/restore` with the raw `.bvbk` bytes as the request
body (`Content-Type: application/octet-stream`).

Excluded from retry and failover exactly as <see cref="BackupAsync"/> is, and for a stronger
reason: a replayed restore would re-apply a whole vault image, and a failover would apply it
to a node the caller did not choose.



SYS-091: an HMAC, magic-number, version or corruption failure is a `500` whose message
Appendix B §2 already recognises (`backup hmac verification failed`,
`hmac verification failed`, and the `backup` + `invalid magic` /
`unsupported version` / `corrupted` prefix rule), so it reaches the caller as
`BV-INPUT-103 BackupFileInvalid`, non-retryable, and no code was minted.
No operation-local remap is installed. D-M7-36 installed one because the generated
rule for the `+ a/b/c` form could never fire — the generator compiled the appendix's
alternation as a `ContainsAll` conjunction (R-23). D-M8-2 fixed the generator, the rule
now carries a `ContainsAny` qualifier group, and all four of SYS-091's named failures
reach the caller through the shared table. The remap deleted with the defect, as D-M7-36
designed it to.






Wire params: `backup` is the raw request body
(`Content-Type: application/octet-stream`); no query params. Returns a
<see cref="RestoreResult"/>, never `null`. Conformance: Complete
(SYS-090). Errors beyond the common set (ERR-061): `BV-INPUT-103`
(`BackupFileInvalid`).

**Spec:** `Sys.Restore — SYS-090`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:1321`*

#### `SealClusterWideAsync(options, cancellationToken)`

RES-030: `Sys.Seal` against every discovered candidate, including the sealed and
the unreachable ones, returning one result per node keyed by its URL.

Deferred from M5 by D-M5-3 only because `SYS-012`/`SYS-013` did not exist; slice a
landed them, so the deferral is discharged here.






⚠️ Not subject to failover or retry (RES-030 says so explicitly, and SYS-013 already
does for the base operations). A node that refuses the connection becomes a
<see cref="ClusterNodeResult"/> with `Succeeded` false and its
error attached; it does not abort the fan-out, because the point of the variant is to reach
every node.






Candidates are not probed and not filtered: RES-030 says "all discovered candidates
(including sealed/unreachable)", so the DSC-030…033 eligibility rules that pick one
node deliberately do not apply. On a literal-address client the candidate set is the single
configured address, which is what "all discovered candidates" means when discovery did not
run (DSC-001).






Nodes are visited sequentially, in candidate order. A fan-out in parallel would be
faster and is what a first draft reaches for, but unsealing is a per-node share counter and
a caller reading a partial result while the operation is still running has no way to tell a
slow node from a failed one.






HTTP call: `Sys.Seal`'s `PUT sys/seal`, once per discovered candidate. Wire
params: none, per call. Returns one <see cref="ClusterNodeResult"/> per endpoint, never
`null`. Conformance: Complete (RES-030). No error codes beyond the common
set (ERR-061) other than what each per-node call already documents — a failing node's
error is attached to its own result rather than raised.

**Spec:** `Sys.SealClusterWide — RES-030`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:1385`*

#### `UnsealClusterWideAsync(key, options, cancellationToken)`

RES-030: `Sys.Unseal` against every discovered candidate, returning each node's
<see cref="SealStatus"/> — which is what makes the variant necessary, since unseal progress
is counted per node. Same exclusions, same ordering and same candidate rule as
<see cref="SealClusterWideAsync"/>.

HTTP call: `Sys.Unseal`'s `PUT sys/unseal`, once per discovered candidate. Wire
params: `key` is each call's body. Returns one
<see cref="ClusterNodeResult"/> per endpoint, never `null`. Conformance:
Complete (RES-030). No error codes beyond the common set (ERR-061) other than what each
per-node call already documents.

**Spec:** `Sys.UnsealClusterWide — RES-030`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:1414`*

#### `BatchAsync(operations, options, cancellationToken)`

BAT-001…BAT-006: `POST /v2/sys/batch` — one HTTP request carrying every operation,
under the client's token and namespace.

A batch is not a transaction (BAT-008). The server runs the operations in order and
does not roll back, so a batch whose fourth operation fails leaves the first three applied.
There is no `Transaction` anywhere in this API and none is planned; a caller needing
atomicity does not have it here.






The call succeeds when the operations fail (BAT-005). Every returned
<see cref="BatchResult"/> whose `Status` is 400 or above carries a
mapped `Error`, and the caller inspects them. This method raises
only when the HTTP request itself failed (BAT-006): `BV-INPUT-003` for a batch
the server also judged oversized, `BV-AUTHZ-001` for a `403` on `sys/batch`
itself, and `BV-SERVER-004` for a `404` "path not supported", which means the
server predates batching. All three arrive through the ordinary ERR-020 mapping — no
status branch is written here, because Appendix B §2 already recognises
`logical backend path not supported` as `BV-SERVER-004`.






Three refusals happen client-side, before anything is sent: an empty list
(`BV-INPUT-002`, BAT-002), more than
`BatchMaxOperations` operations (`BV-INPUT-003` with the cap
in `Details.max`, BAT-002), and a `Data` that is present on
a non-write or absent on a write (`BV-INPUT-001`, BAT-004).






Wire params: `operations` is the body's `operations` array. Returns
one <see cref="BatchResult"/> per operation, never `null`. Conformance:
Core (BAT-001). Errors beyond the common set (ERR-061): `BV-INPUT-002` (empty list),
`BV-INPUT-003` (batch too large), `BV-INPUT-001` (data present/absent
mismatch), `BV-SERVER-004` (server predates batching).

**Spec:** `Sys.Batch — BAT-001`

*Source: `dotnet/BastionVault.IntegrationSdk/SysOperations.cs:1848`*

