# `ClusterStatus` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/ClusterStatus.cs`](../../../dotnet/BastionVault.IntegrationSdk/ClusterStatus.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `ClusterStatus`

#### `StorageType`

The wire `storage_type` field. Always present.

*Source: `dotnet/BastionVault.IntegrationSdk/ClusterStatus.cs:13`*

#### `Cluster`

The wire `cluster` field. Always present.

*Source: `dotnet/BastionVault.IntegrationSdk/ClusterStatus.cs:16`*

#### `NodeId`

The wire `node_id` field. Absent on a non-clustered backend.

*Source: `dotnet/BastionVault.IntegrationSdk/ClusterStatus.cs:19`*

#### `IsLeader`

The wire `is_leader` field. Absent on a non-clustered backend.

*Source: `dotnet/BastionVault.IntegrationSdk/ClusterStatus.cs:22`*

#### `ClusterHealthy`

The wire `cluster_healthy` field. Absent on a non-clustered backend.

*Source: `dotnet/BastionVault.IntegrationSdk/ClusterStatus.cs:25`*

#### `RaftMetrics`

D-M3-4: an opaque map, because the wire fixes no key set for it. Absent, not defaulted,
when the backend omits it — the same optionality rule as every other field in this shape.

*Source: `dotnet/BastionVault.IntegrationSdk/ClusterStatus.cs:31`*

