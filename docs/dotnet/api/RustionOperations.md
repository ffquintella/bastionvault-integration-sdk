# `RustionOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/RustionOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/RustionOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `RustionAuthorityOperations`

#### `AttestAsync(request, mount, options, cancellationToken)`

Submits an authority attestation envelope: `POST {mount}/authority/attest`. `request` is sent verbatim (D-M1c-25).

Wire params: `mount` builds the route; body carries `request` verbatim. Returns the raw <see cref="Response"/> (no field-level schema documented), or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Authority.Attest — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionOperations.cs:297`*

### `RustionMasterOperations`

#### `ReadConfigAsync(mount, options, cancellationToken)`

Reads the master engine's configuration: `GET {mount}/master/config`.

Wire params: `mount` builds the route; no body. Returns the raw response map verbatim, or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Master.ReadConfig — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionOperations.cs:221`*

#### `WriteConfigAsync(config, mount, options, cancellationToken)`

Writes the master engine's configuration: `POST {mount}/master/config`. `config` is sent verbatim (D-M1c-25).

Wire params: `mount` builds the route; body carries `config` verbatim. Returns the raw <see cref="Response"/> (no field-level schema documented), or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Master.WriteConfig — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionOperations.cs:234`*

#### `PubKeyAsync(mount, options, cancellationToken)`

Reads the master public key: `GET {mount}/master/pubkey`. Public key material, not secret — no <see cref="SecretString"/> wrapping.

Wire params: `mount` builds the route; no body. Returns the raw response map verbatim, or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Master.PubKey — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionOperations.cs:246`*

#### `IssueAsync(request, mount, options, cancellationToken)`

Issues a master credential: `POST {mount}/master/issue`.

Wire params: `mount` builds the route; body carries `request` verbatim when supplied, otherwise none. Returns the raw <see cref="Response"/> (no field-level schema documented), or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Master.Issue — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionOperations.cs:259`*

#### `RotateAsync(mount, options, cancellationToken)`

Rotates the master key: `POST {mount}/master/rotate`.

Wire params: `mount` builds the route; no body. Returns the raw <see cref="Response"/> (no field-level schema documented), or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Master.Rotate — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionOperations.cs:272`*

### `RustionOperations`

#### `Targets`

`{mount}/targets[/health|/probe|/{id}[/probe|/listeners/refresh]]`.

*Source: `dotnet/BastionVault.IntegrationSdk/RustionOperations.cs:34`*

#### `Master`

`{mount}/master/{config|pubkey|issue|rotate}`.

*Source: `dotnet/BastionVault.IntegrationSdk/RustionOperations.cs:37`*

#### `Authority`

`{mount}/authority/attest` — typed despite section 12's table omitting it (Appendix B names it by this canonical operation twice).

*Source: `dotnet/BastionVault.IntegrationSdk/RustionOperations.cs:40`*

#### `Session`

`{mount}/session/{open|renew|kill}` plus the `/v2`-pinned `OpenConnectOnly`.

*Source: `dotnet/BastionVault.IntegrationSdk/RustionOperations.cs:43`*

#### `Recordings`

`{mount}/recordings/*`, including RUS-001's `Download` and RUS-002's node-local chunk/blob reads.

*Source: `dotnet/BastionVault.IntegrationSdk/RustionOperations.cs:46`*

#### `Policy`

`{mount}/policy/{global,type/{t},asset-group/{id},resource/{id},force-rustion,effective}`.

*Source: `dotnet/BastionVault.IntegrationSdk/RustionOperations.cs:49`*

#### `BastionGroups`

`{mount}/bastion-groups[/{name}]`.

*Source: `dotnet/BastionVault.IntegrationSdk/RustionOperations.cs:52`*

#### `Dispatcher`

`{mount}/dispatcher/preview`.

*Source: `dotnet/BastionVault.IntegrationSdk/RustionOperations.cs:55`*

#### `Telemetry`

`{mount}/telemetry[/poll]`.

*Source: `dotnet/BastionVault.IntegrationSdk/RustionOperations.cs:58`*

#### `DeploymentIdAsync(mount, options, cancellationToken)`

Reads the deployment identifier: `GET {mount}/deployment-id`. 12 names no response shape, so this returns the raw map rather than assuming one (D-M1c-25).

Wire params: `mount` builds the route; no body. Returns the raw response map verbatim, or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.DeploymentId — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionOperations.cs:63`*

### `RustionSessionOperations`

#### `OpenAsync(request, mount, options, cancellationToken)`

Opens a v1 bastion session: `POST {mount}/session/open`. `credential_material` is <see cref="SecretString"/>-typed; every other field is an opaque bag.

Wire params: `mount` builds the route; body carries `request`'s `credential_material` and remaining fields verbatim. Returns the raw <see cref="Response"/> (no field-level schema documented), or `null` per the shared envelope rules. Conformance: Complete. Errors beyond the common set (ERR-061): `BV-INPUT-001` when `CredentialMaterial` is empty (client-side, no request sent).

**Spec:** `Rustion.Session.Open — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionOperations.cs:329`*

#### `OpenConnectOnlyAsync(request, options, cancellationToken)`

Opens a v2 connect-only session: `POST /v2/rustion/session/open`. Every field 12
§Rustion documents is typed. R-33: `secret_id` and `connect_ticket` travel in
this POST body only.

Wire params: none — the path is literal, not `{mount}`-templated; body carries `request`'s `resource_name`, `credential_source`, `target_host`, `target_port`, `target_protocol`, `profile_id`, `connect_ticket`. Returns the raw <see cref="Response"/> (no field-level schema documented), or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Session.OpenConnectOnly — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionOperations.cs:352`*

#### `RenewAsync(request, mount, options, cancellationToken)`

Renews a bastion session: `POST {mount}/session/renew`.

Wire params: `mount` builds the route; body carries `request`'s `bastion_id`, `session_id`, `correlation_id` (validated non-empty client-side), `extend_secs`. Returns the raw <see cref="Response"/> (no field-level schema documented), or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Session.Renew — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionOperations.cs:368`*

#### `KillAsync(request, mount, options, cancellationToken)`

Kills a bastion session: `POST {mount}/session/kill`. See <see cref="RustionSessionKillRequest"/>'s remarks for the inferred shape.

Wire params: `mount` builds the route; body carries `request`'s `bastion_id`, `session_id`, `correlation_id` (validated non-empty client-side). Returns the raw <see cref="Response"/> (no field-level schema documented), or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Session.Kill — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionOperations.cs:384`*

### `RustionTargetsOperations`

#### `ListAsync(mount, options, cancellationToken)`

Lists the target names: `LIST {mount}/targets/`.

Wire params: `mount` builds the route; no body. Returns an empty list when the backend has none, never `null`. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Targets.List — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionOperations.cs:89`*

#### `CreateAsync(target, mount, options, cancellationToken)`

Creates a target: `POST {mount}/targets/`. `target` is sent verbatim (D-M1c-25).

Wire params: `mount` builds the route; body carries `target` verbatim. Returns the raw <see cref="Response"/> (no field-level schema documented), or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Targets.Create — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionOperations.cs:102`*

#### `ReadAsync(id, mount, options, cancellationToken)`

Reads a target: `GET {mount}/targets/{id}`.

Wire params: `id`/`mount` build the route; no body. Returns the raw <see cref="Response"/> (no field-level schema documented), or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Targets.Read — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionOperations.cs:114`*

#### `WriteAsync(id, target, mount, options, cancellationToken)`

Writes a target: `PUT {mount}/targets/{id}`. `target` is sent verbatim (D-M1c-25).

Wire params: `id`/`mount` build the route; body carries `target` verbatim. Returns the raw <see cref="Response"/> (no field-level schema documented), or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Targets.Write — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionOperations.cs:127`*

#### `DeleteAsync(id, mount, options, cancellationToken)`

Deletes a target: `DELETE {mount}/targets/{id}`.

Wire params: `id`/`mount` build the route; no body. Returns `void` on success. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Targets.Delete — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionOperations.cs:140`*

#### `ProbeAsync(id, mount, options, cancellationToken)`

Probes one target: `POST {mount}/targets/{id}/probe`.

Wire params: `id`/`mount` build the route; no body. Returns the raw <see cref="Response"/> (no field-level schema documented), or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Targets.Probe — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionOperations.cs:153`*

#### `ProbeAllAsync(mount, options, cancellationToken)`

Probes every target: `POST {mount}/targets/probe` — every target, not one.

Wire params: `mount` builds the route; no body. Returns the raw <see cref="Response"/> (no field-level schema documented), or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Targets.ProbeAll — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionOperations.cs:166`*

#### `HealthAsync(mount, options, cancellationToken)`

Reads aggregate target health: `GET {mount}/targets/health`.

Wire params: `mount` builds the route; no body. Returns the raw <see cref="Response"/> (no field-level schema documented), or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Targets.Health — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionOperations.cs:178`*

#### `RefreshListenersAsync(id, mount, options, cancellationToken)`

Refreshes a target's listeners: `POST {mount}/targets/{id}/listeners/refresh`.

Wire params: `id`/`mount` build the route; no body. Returns `void` on success. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Targets.RefreshListeners — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionOperations.cs:190`*

