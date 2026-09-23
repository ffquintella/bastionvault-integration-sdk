# `Audit` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/Audit.cs`](../../../dotnet/BastionVault.IntegrationSdk/Audit.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `AuditDevice`

#### `Path`

The wire `path` field, in SYS-022's table form (a single trailing `/`).

*Source: `dotnet/BastionVault.IntegrationSdk/Audit.cs:9`*

#### `Type`

The wire `type` field: the audit backend (`file`, `syslog`, …).

*Source: `dotnet/BastionVault.IntegrationSdk/Audit.cs:12`*

#### `Description`

The wire `description` field; `null` when the server omits it.

*Source: `dotnet/BastionVault.IntegrationSdk/Audit.cs:15`*

#### `Namespace`

The wire `namespace` field: the namespace the device is registered in. `""` denotes root.

*Source: `dotnet/BastionVault.IntegrationSdk/Audit.cs:18`*

#### `Mirror`

The wire `mirror` field: whether the device mirrors another namespace's stream.

*Source: `dotnet/BastionVault.IntegrationSdk/Audit.cs:21`*

### `AuditDeviceSpec`

#### `Type`

The wire `type` field: the audit backend to enable. Required.

*Source: `dotnet/BastionVault.IntegrationSdk/Audit.cs:28`*

#### `Description`

The wire `description` field; omitted from the body when unset.

*Source: `dotnet/BastionVault.IntegrationSdk/Audit.cs:31`*

#### `Options`

The wire `options` map (backend-specific, e.g. `file_path`); omitted when unset or empty.

*Source: `dotnet/BastionVault.IntegrationSdk/Audit.cs:34`*

#### `Mirror`

The wire `mirror` field. Tri-state on purpose: `null` omits the key and
lets the server default it, which is a different request from sending
`false` explicitly.

*Source: `dotnet/BastionVault.IntegrationSdk/Audit.cs:41`*

### `AuditEvent`

#### `Timestamp`

The wire `ts` field, parsed as RFC 3339; `null` when absent or unparsable.

*Source: `dotnet/BastionVault.IntegrationSdk/Audit.cs:48`*

#### `User`

The wire `user` field.

*Source: `dotnet/BastionVault.IntegrationSdk/Audit.cs:51`*

#### `Machine`

The wire `machine` field. Optional on the wire and optional here (SYS-070's table marks it `machine?`).

*Source: `dotnet/BastionVault.IntegrationSdk/Audit.cs:54`*

#### `Op`

The wire `op` field.

*Source: `dotnet/BastionVault.IntegrationSdk/Audit.cs:57`*

#### `Category`

The wire `category` field.

*Source: `dotnet/BastionVault.IntegrationSdk/Audit.cs:60`*

#### `Target`

The wire `target` field.

*Source: `dotnet/BastionVault.IntegrationSdk/Audit.cs:63`*

#### `ChangedFields`

The wire `changed_fields` array; empty when the server omits it.

*Source: `dotnet/BastionVault.IntegrationSdk/Audit.cs:66`*

#### `Summary`

The wire `summary` field.

*Source: `dotnet/BastionVault.IntegrationSdk/Audit.cs:69`*

#### `Raw`

The whole event object as sent, so a field this type does not name is still reachable.

*Source: `dotnet/BastionVault.IntegrationSdk/Audit.cs:72`*

