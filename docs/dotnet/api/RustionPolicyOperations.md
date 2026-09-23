# `RustionPolicyOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/RustionPolicyOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/RustionPolicyOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `RustionBastionGroupsOperations`

#### `ListAsync(mount, options, cancellationToken)`

Lists bastion group names: `LIST {mount}/bastion-groups/`.

Wire params: `mount` builds the route; no body. Returns an empty list when the backend has none, never `null`. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.BastionGroups.List — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionPolicyOperations.cs:218`*

#### `ReadAsync(name, mount, options, cancellationToken)`

Reads a bastion group: `GET {mount}/bastion-groups/{name}`.

Wire params: `name`/`mount` build the route; no body. Returns the raw <see cref="Response"/> (no field-level schema documented), or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.BastionGroups.Read — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionPolicyOperations.cs:231`*

#### `WriteAsync(name, group, mount, options, cancellationToken)`

Writes a bastion group: `PUT {mount}/bastion-groups/{name}`. `group` is sent verbatim (D-M1c-25).

Wire params: `name`/`mount` build the route; body carries `group` verbatim. Returns the raw <see cref="Response"/> (no field-level schema documented), or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.BastionGroups.Write — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionPolicyOperations.cs:244`*

#### `DeleteAsync(name, mount, options, cancellationToken)`

Deletes a bastion group: `DELETE {mount}/bastion-groups/{name}`.

Wire params: `name`/`mount` build the route; no body. Returns `void` on success. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.BastionGroups.Delete — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionPolicyOperations.cs:257`*

### `RustionDispatcherOperations`

#### `PreviewAsync(request, mount, options, cancellationToken)`

Previews dispatcher routing: `POST {mount}/dispatcher/preview`. `request` is sent verbatim (D-M1c-25).

Wire params: `mount` builds the route; body carries `request` verbatim. Returns the raw <see cref="Response"/> (no field-level schema documented), or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Dispatcher.Preview — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionPolicyOperations.cs:288`*

### `RustionPolicyOperations`

#### `ReadGlobalAsync(mount, options, cancellationToken)`

Reads the global policy: `GET {mount}/policy/global`.

Wire params: `mount` builds the route; no body. Returns the raw response map verbatim, or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Policy.ReadGlobal — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionPolicyOperations.cs:24`*

#### `WriteGlobalAsync(policy, mount, options, cancellationToken)`

Writes the global policy: `PUT {mount}/policy/global`. `policy` is sent verbatim (D-M1c-25).

Wire params: `mount` builds the route; body carries `policy` verbatim. Returns the raw <see cref="Response"/> (no field-level schema documented), or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Policy.WriteGlobal — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionPolicyOperations.cs:37`*

#### `ReadTypeAsync(type, mount, options, cancellationToken)`

Reads a target-type policy: `GET {mount}/policy/type/{type}`.

Wire params: `type`/`mount` build the route; no body. Returns the raw <see cref="Response"/> (no field-level schema documented), or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Policy.ReadType — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionPolicyOperations.cs:49`*

#### `WriteTypeAsync(type, policy, mount, options, cancellationToken)`

Writes a target-type policy: `PUT {mount}/policy/type/{type}`. `policy` is sent verbatim (D-M1c-25).

Wire params: `type`/`mount` build the route; body carries `policy` verbatim. Returns the raw <see cref="Response"/> (no field-level schema documented), or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Policy.WriteType — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionPolicyOperations.cs:62`*

#### `DeleteTypeAsync(type, mount, options, cancellationToken)`

Deletes a target-type policy: `DELETE {mount}/policy/type/{type}`.

Wire params: `type`/`mount` build the route; no body. Returns `void` on success. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Policy.DeleteType — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionPolicyOperations.cs:75`*

#### `ReadAssetGroupAsync(id, mount, options, cancellationToken)`

Reads an asset-group policy: `GET {mount}/policy/asset-group/{id}`.

Wire params: `id`/`mount` build the route; no body. Returns the raw <see cref="Response"/> (no field-level schema documented), or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Policy.ReadAssetGroup — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionPolicyOperations.cs:88`*

#### `WriteAssetGroupAsync(id, policy, mount, options, cancellationToken)`

Writes an asset-group policy: `PUT {mount}/policy/asset-group/{id}`. `policy` is sent verbatim (D-M1c-25).

Wire params: `id`/`mount` build the route; body carries `policy` verbatim. Returns the raw <see cref="Response"/> (no field-level schema documented), or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Policy.WriteAssetGroup — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionPolicyOperations.cs:101`*

#### `DeleteAssetGroupAsync(id, mount, options, cancellationToken)`

Deletes an asset-group policy: `DELETE {mount}/policy/asset-group/{id}`.

Wire params: `id`/`mount` build the route; no body. Returns `void` on success. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Policy.DeleteAssetGroup — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionPolicyOperations.cs:114`*

#### `ReadResourceAsync(id, mount, options, cancellationToken)`

Reads a resource policy: `GET {mount}/policy/resource/{id}`.

Wire params: `id`/`mount` build the route; no body. Returns the raw <see cref="Response"/> (no field-level schema documented), or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Policy.ReadResource — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionPolicyOperations.cs:127`*

#### `WriteResourceAsync(id, policy, mount, options, cancellationToken)`

Writes a resource policy: `PUT {mount}/policy/resource/{id}`. `policy` is sent verbatim (D-M1c-25).

Wire params: `id`/`mount` build the route; body carries `policy` verbatim. Returns the raw <see cref="Response"/> (no field-level schema documented), or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Policy.WriteResource — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionPolicyOperations.cs:140`*

#### `DeleteResourceAsync(id, mount, options, cancellationToken)`

Deletes a resource policy: `DELETE {mount}/policy/resource/{id}`.

Wire params: `id`/`mount` build the route; no body. Returns `void` on success. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Policy.DeleteResource — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionPolicyOperations.cs:153`*

#### `ForceRustionAsync(request, mount, options, cancellationToken)`

Forces a policy re-evaluation: `POST {mount}/policy/force-rustion`. `request` is sent verbatim (D-M1c-25).

Wire params: `mount` builds the route; body carries `request` verbatim. Returns the raw <see cref="Response"/> (no field-level schema documented), or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Policy.ForceRustion — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionPolicyOperations.cs:166`*

#### `EffectiveAsync(request, mount, options, cancellationToken)`

Evaluates effective policy: `POST {mount}/policy/effective`. `request` is sent verbatim (D-M1c-25).

Wire params: `mount` builds the route; body carries `request` verbatim. Returns the raw <see cref="Response"/> (no field-level schema documented), or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Policy.Effective — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionPolicyOperations.cs:178`*

### `RustionTelemetryOperations`

#### `ReadAsync(mount, options, cancellationToken)`

Reads current telemetry: `GET {mount}/telemetry`.

Wire params: `mount` builds the route; no body. Returns the raw response map verbatim, or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Telemetry.Read — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionPolicyOperations.cs:313`*

#### `PollAsync(mount, options, cancellationToken)`

Polls telemetry: `GET {mount}/telemetry/poll`. Read-only inference (D-M10-e): "poll" is modelled as a side-effect-free fetch, not an acknowledging drain.

Wire params: `mount` builds the route; no body. Returns the raw response map verbatim, or `null` per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).

**Spec:** `Rustion.Telemetry.Poll — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/RustionPolicyOperations.cs:326`*

