# `SdkInfo` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/SdkInfo.cs`](../../../dotnet/BastionVault.IntegrationSdk/SdkInfo.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `SdkInfo`

#### `SpecificationVersion`

The version of the BastionVault Integration SDK specification (see
`specifications/00-overview.md#specification-version`) that this library implements.

*Source: `dotnet/BastionVault.IntegrationSdk/SdkInfo.cs:13`*

#### `SdkVersion`

The version of this SDK package, matching the `&lt;Version&gt;` element in
`BastionVault.IntegrationSdk.csproj`.

*Source: `dotnet/BastionVault.IntegrationSdk/SdkInfo.cs:19`*

#### `SpecificationSourceRelease`

The upstream BastionVault server release that <see cref="SpecificationVersion"/> was
derived from (CNF-047), pinned in `specifications/provenance.json` as
`specificationVersion`'s companion `upstream` release.

*Source: `dotnet/BastionVault.IntegrationSdk/SdkInfo.cs:26`*

#### `SpecificationSourceRef`

The upstream git ref (tag) corresponding to <see cref="SpecificationSourceRelease"/>,
as pinned in `specifications/provenance.json` (CNF-047).

*Source: `dotnet/BastionVault.IntegrationSdk/SdkInfo.cs:32`*

