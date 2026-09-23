# `SshBrokerOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/SshBrokerOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/SshBrokerOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `SshBrokerOperations`

#### `ReadGlobalAsync(options, cancellationToken)`

Reads the root-gated global login-class policy: `GET /v2/ssh-broker/policy/global`.

Wire params: none; the route is fixed and `/v2`-pinned (SSB-001). Returns
<see cref="SshBrokerGlobalPolicy"/>, or `null` when unset. Conformance:
Complete (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `SshBroker.ReadGlobal — 10-ssh-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SshBrokerOperations.cs:29`*

#### `WriteGlobalAsync(policy, options, cancellationToken)`

Writes the root-gated global login-class policy: `PUT /v2/ssh-broker/policy/global`.
D-M9-27: `10-ssh-engine.md:77` states the verb explicitly.

Wire params: none in the route; body carries `login_class_default`/`login_class_lock`.
Returns `void` on success. Conformance: Complete (Appendix A groups this mount, no per-operation row). Errors beyond
the common set (ERR-061): `BV-AUTHZ-005 LoginClassLocked` (SSB-002) when this tier is
already locked.

**Spec:** `SshBroker.WriteGlobal — 10-ssh-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SshBrokerOperations.cs:50`*

#### `ReadTypeAsync(type, options, cancellationToken)`

Reads a role-type login-class policy: `GET /v2/ssh-broker/policy/type/{type}`.

Wire params: `type` builds the route (SSB-001, `/v2`-pinned). Returns
<see cref="SshBrokerTypePolicy"/>, or `null` when unset. Conformance:
Complete (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `SshBroker.ReadType — 10-ssh-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SshBrokerOperations.cs:67`*

#### `WriteTypeAsync(type, policy, options, cancellationToken)`

Writes a role-type login-class policy: `PUT /v2/ssh-broker/policy/type/{type}`.
D-M9-27: `10-ssh-engine.md:78` states no verb for this row; `PUT` is inferred as
this policy family's one sibling write, and is booked to M12 for server verification if
wrong.

Wire params: `type` builds the route; body carries `policy`'s
`login_class`/`lock` (SSB-001, `/v2`-pinned). Returns `void`
on success. Conformance: Complete (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061):
`BV-AUTHZ-005 LoginClassLocked` (SSB-002) when this tier is already locked.

**Spec:** `SshBroker.WriteType — 10-ssh-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SshBrokerOperations.cs:91`*

#### `DeleteTypeAsync(type, options, cancellationToken)`

Deletes a role-type login-class policy: `DELETE /v2/ssh-broker/policy/type/{type}`.

Wire params: `type` builds the route (SSB-001, `/v2`-pinned). Returns
`void` on success. Conformance: Complete (Appendix A groups this mount, no per-operation row). No error codes beyond
the common set (ERR-061).

**Spec:** `SshBroker.DeleteType — 10-ssh-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SshBrokerOperations.cs:108`*

#### `ReadAssetGroupAsync(id, options, cancellationToken)`

Reads an asset-group login-class policy: `GET /v2/ssh-broker/policy/asset-group/{id}`.

Wire params: `id` builds the route (SSB-001, `/v2`-pinned). Returns
<see cref="SshBrokerAssetGroupPolicy"/>, or `null` when unset. Conformance:
Complete (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `SshBroker.ReadAssetGroup — 10-ssh-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SshBrokerOperations.cs:124`*

#### `WriteAssetGroupAsync(id, policy, options, cancellationToken)`

Writes an asset-group login-class policy: `PUT /v2/ssh-broker/policy/asset-group/{id}`.
D-M9-27: `10-ssh-engine.md:79` states no verb for this row; `PUT` is inferred as
this policy family's one sibling write, and is booked to M12 for server verification if
wrong.

Wire params: `id` builds the route; body carries `policy`'s
`login_class`/`priority`/`lock` (SSB-001, `/v2`-pinned). Returns
`void` on success. Conformance: Complete (Appendix A groups this mount, no per-operation row). Errors beyond the
common set (ERR-061): `BV-AUTHZ-005 LoginClassLocked` (SSB-002) when this tier is
already locked.

**Spec:** `SshBroker.WriteAssetGroup — 10-ssh-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SshBrokerOperations.cs:149`*

#### `DeleteAssetGroupAsync(id, options, cancellationToken)`

Deletes an asset-group login-class policy: `DELETE /v2/ssh-broker/policy/asset-group/{id}`.

Wire params: `id` builds the route (SSB-001, `/v2`-pinned). Returns
`void` on success. Conformance: Complete (Appendix A groups this mount, no per-operation row). No error codes beyond
the common set (ERR-061).

**Spec:** `SshBroker.DeleteAssetGroup — 10-ssh-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SshBrokerOperations.cs:166`*

#### `ReadResourceAsync(id, options, cancellationToken)`

Reads a resource login-class policy: `GET /v2/ssh-broker/policy/resource/{id}`.

Wire params: `id` builds the route (SSB-001, `/v2`-pinned). Returns
<see cref="SshBrokerResourcePolicy"/>, or `null` when unset. Conformance:
Complete (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `SshBroker.ReadResource — 10-ssh-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SshBrokerOperations.cs:182`*

#### `WriteResourceAsync(id, policy, options, cancellationToken)`

Writes a resource login-class policy: `PUT /v2/ssh-broker/policy/resource/{id}`.
D-M9-27: `10-ssh-engine.md:80` states no verb for this row; `PUT` is inferred as
this policy family's one sibling write, and is booked to M12 for server verification if
wrong.

Wire params: `id` builds the route; body carries `policy`'s
`login_class` (SSB-001, `/v2`-pinned). Returns `void` on success.
Conformance: Complete (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061):
`BV-AUTHZ-005 LoginClassLocked` (SSB-002) when this tier is already locked;
`BV-CONFLICT-003 BrokeredResourceStaticCredential` (SSB-002) when a static credential
is attached to a brokered resource.

**Spec:** `SshBroker.WriteResource — 10-ssh-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SshBrokerOperations.cs:208`*

#### `DeleteResourceAsync(id, options, cancellationToken)`

Deletes a resource login-class policy: `DELETE /v2/ssh-broker/policy/resource/{id}`.

Wire params: `id` builds the route (SSB-001, `/v2`-pinned). Returns
`void` on success. Conformance: Complete (Appendix A groups this mount, no per-operation row). No error codes beyond
the common set (ERR-061).

**Spec:** `SshBroker.DeleteResource — 10-ssh-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SshBrokerOperations.cs:225`*

#### `EffectiveAsync(resourceId, resourceType, assetGroupIds, options, cancellationToken)`

Resolves the effective login-class for a resource across all four policy tiers:
`POST /v2/ssh-broker/policy/effective`. `assetGroupIds` is
CSV-joined on the wire (`SerialiseEffectiveRequest`), matching the
accepted `sshbroker.effective-v2-pinned` fixture.

Wire params: body carries `resourceId`/`resourceType`
(required) and `assetGroupIds` (optional, CSV-joined) (SSB-001,
`/v2`-pinned). Returns <see cref="SshBrokerEffectivePolicy"/>, never
`null`. Conformance: Complete (Appendix A groups this mount, no per-operation row). No error codes beyond the common
set (ERR-061).

**Spec:** `SshBroker.Effective — 10-ssh-engine.md`

*Source: `dotnet/BastionVault.IntegrationSdk/SshBrokerOperations.cs:248`*

