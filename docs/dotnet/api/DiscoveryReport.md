# `DiscoveryReport` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/DiscoveryReport.cs`](../../../dotnet/BastionVault.IntegrationSdk/DiscoveryReport.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `DiscoveryReport`

#### `Degraded`

DSC-016: the cause of a DSC-012 degradation, or `None`.
A property, not a <see cref="Render"/> column (D-R16-12) — <see cref="Render"/>'s output is a
byte-compared shared fixture artefact and must not gain a column for this.

*Source: `dotnet/BastionVault.IntegrationSdk/DiscoveryReport.cs:28`*

#### `Render()`

RES-020: renders the table the CLI prints, column-for-column as
`specifications/13-cluster-discovery-and-resilience.md` lines 143-152 print it.

Lines are joined with `\n` and not the platform separator: the rendering is a specified
output shape shared by three language SDKs, so it must not differ between Windows and
everything else.

*Source: `dotnet/BastionVault.IntegrationSdk/DiscoveryReport.cs:48`*

