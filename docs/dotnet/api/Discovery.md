# `Discovery` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/Discovery.cs`](../../../dotnet/BastionVault.IntegrationSdk/Discovery.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `Candidate`

#### `ClusterHealthy`

The body's `cluster_healthy` field. DSC-022: it MUST NOT by itself change
<see cref="State"/>; it is surfaced here so ranking can prefer healthy nodes.

*Source: `dotnet/BastionVault.IntegrationSdk/Discovery.cs:163`*

#### `ClusterId`

The body's `cluster_id`, absent on current servers and kept for forward-compat (DSC-021, DSC-032).

*Source: `dotnet/BastionVault.IntegrationSdk/Discovery.cs:166`*

#### `Version`

The body's `version`, absent on current servers and kept for forward-compat (DSC-021).

*Source: `dotnet/BastionVault.IntegrationSdk/Discovery.cs:169`*

### `DiscoveryConfig`

#### `SrvService`

The SRV service prefix prepended to a bare cluster name (DSC-010).

*Source: `dotnet/BastionVault.IntegrationSdk/Discovery.cs:63`*

#### `DefaultScheme`

The scheme used for a synthesised candidate URL (DSC-012, DSC-013).

*Source: `dotnet/BastionVault.IntegrationSdk/Discovery.cs:66`*

#### `DefaultPort`

The port used when an address carries none (DSC-012, DSC-013).

*Source: `dotnet/BastionVault.IntegrationSdk/Discovery.cs:69`*

#### `ResolveTimeout`

How long the SRV lookup may take before it counts as "no records" (DSC-011).

*Source: `dotnet/BastionVault.IntegrationSdk/Discovery.cs:72`*

#### `StrictDiscovery`

DSC-017: when `true` (the default, D-R16-5), a non-SRV-shaped cluster name
that yields no SRV records raises `BV-DISCOVERY-004` instead of synthesising a DSC-012
literal candidate. An SRV-shaped name (`_…`) with no records already raises via
DSC-012's other arm (`BV-DISCOVERY-001`) regardless of this setting.

*Source: `dotnet/BastionVault.IntegrationSdk/Discovery.cs:80`*

#### `Nameservers`

DSC-050: the nameservers the built-in default <see cref="ISrvResolver"/> queries.
`null` (the default) means query the platform's configured nameservers; an
explicit list, when set, overrides platform discovery entirely and is used instead — the
operator remedy for a scoped resolver platform discovery cannot see (D-R16-6, D-R16-7), and
what makes the default resolver testable against an in-process fake DNS server without
touching the host's own resolver configuration. Has no effect when an application supplies
its own `SrvResolver` — an injected resolver always
wins and never sees this setting.

*Source: `dotnet/BastionVault.IntegrationSdk/Discovery.cs:92`*

### `HealthConfig`

#### `ProbeTimeout`

Per-candidate probe timeout.

*Source: `dotnet/BastionVault.IntegrationSdk/Discovery.cs:107`*

#### `Parallelism`

The maximum number of probes in flight at once (DSC-020).

*Source: `dotnet/BastionVault.IntegrationSdk/Discovery.cs:110`*

### `NodeSelection`

#### `ClusterId`

The pinned node's `cluster_id`, when it reported one.

*Source: `dotnet/BastionVault.IntegrationSdk/Discovery.cs:191`*

#### `Version`

The pinned node's `version`, when it reported one.

*Source: `dotnet/BastionVault.IntegrationSdk/Discovery.cs:194`*

### `SrvRecord`

#### `ResolveAsync(ownerName, cancellationToken)`

Resolves SRV records for `ownerName`. An empty result and a thrown failure
are treated identically by the SDK (DSC-011), so an implementation may choose either.

*Source: `dotnet/BastionVault.IntegrationSdk/Discovery.cs:129`*

