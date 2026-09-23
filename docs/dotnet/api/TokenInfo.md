# `TokenInfo` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/TokenInfo.cs`](../../../dotnet/BastionVault.IntegrationSdk/TokenInfo.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `CreateTokenRequest`

#### `Policies`

Wire `policies`.

*Source: `dotnet/BastionVault.IntegrationSdk/TokenInfo.cs:70`*

#### `Ttl`

Wire `ttl`, sent in seconds.

*Source: `dotnet/BastionVault.IntegrationSdk/TokenInfo.cs:73`*

#### `Period`

Wire `period`, sent in seconds.

*Source: `dotnet/BastionVault.IntegrationSdk/TokenInfo.cs:76`*

#### `NumUses`

Wire `num_uses`.

*Source: `dotnet/BastionVault.IntegrationSdk/TokenInfo.cs:79`*

#### `Renewable`

Wire `renewable`. Default `true`, as the server's is.

*Source: `dotnet/BastionVault.IntegrationSdk/TokenInfo.cs:82`*

#### `Meta`

Wire `meta`. Reserved keys are refused client-side (AUT-081).

*Source: `dotnet/BastionVault.IntegrationSdk/TokenInfo.cs:85`*

#### `DisplayName`

Wire `display_name`.

*Source: `dotnet/BastionVault.IntegrationSdk/TokenInfo.cs:88`*

#### `ExplicitMaxTtl`

Wire `explicit_max_ttl`, sent in seconds.

*Source: `dotnet/BastionVault.IntegrationSdk/TokenInfo.cs:91`*

#### `NoDefaultPolicy`

Wire `no_default_policy`.

*Source: `dotnet/BastionVault.IntegrationSdk/TokenInfo.cs:94`*

#### `NoParent`

Wire `no_parent` (root only).

*Source: `dotnet/BastionVault.IntegrationSdk/TokenInfo.cs:97`*

#### `Id`

Wire `id` (root only).

*Source: `dotnet/BastionVault.IntegrationSdk/TokenInfo.cs:100`*

#### `Type`

Wire `type`.

*Source: `dotnet/BastionVault.IntegrationSdk/TokenInfo.cs:103`*

#### `ChildVisible`

Wire `child_visible`.

*Source: `dotnet/BastionVault.IntegrationSdk/TokenInfo.cs:106`*

#### `UseResult`

AUT-082's opt-in: when `true` the created token replaces the client's own
(which AUT-001 makes a `Static` source). Default
`false` — `Create` never switches the client's token unless asked.

*Source: `dotnet/BastionVault.IntegrationSdk/TokenInfo.cs:113`*

### `TokenInfo`

#### `Id`

The token itself, held redacting (CNF-031); see the type remarks.

*Source: `dotnet/BastionVault.IntegrationSdk/TokenInfo.cs:25`*

#### `Policies`

Policies attached to the token.

*Source: `dotnet/BastionVault.IntegrationSdk/TokenInfo.cs:28`*

#### `Path`

The auth path the token was issued from, e.g. `auth/userpass/login/alice`.

*Source: `dotnet/BastionVault.IntegrationSdk/TokenInfo.cs:31`*

#### `Meta`

The token's metadata map.

*Source: `dotnet/BastionVault.IntegrationSdk/TokenInfo.cs:34`*

#### `DisplayName`

The token's display name.

*Source: `dotnet/BastionVault.IntegrationSdk/TokenInfo.cs:37`*

#### `NumUses`

Remaining uses, `0` for unlimited.

*Source: `dotnet/BastionVault.IntegrationSdk/TokenInfo.cs:40`*

#### `CreationTime`

Wire `creation_time`, a unix timestamp, as an instant.

*Source: `dotnet/BastionVault.IntegrationSdk/TokenInfo.cs:43`*

#### `CreationTtl`

Wire `creation_ttl` in seconds; `Zero` means "no TTL".

*Source: `dotnet/BastionVault.IntegrationSdk/TokenInfo.cs:46`*

#### `ExplicitMaxTtl`

Wire `explicit_max_ttl` in seconds.

*Source: `dotnet/BastionVault.IntegrationSdk/TokenInfo.cs:49`*

#### `Period`

Wire `period` in seconds, when the token is periodic.

*Source: `dotnet/BastionVault.IntegrationSdk/TokenInfo.cs:52`*

#### `RemainingTtl`

AUT-014: `CreationTime + CreationTtl − Clock.NowUtc()`, and `null`
when <see cref="CreationTtl"/> is zero. Computed from the injected clock at parse time, so
a fixture that declares `clock.start` pins it exactly (D-M2-7).

*Source: `dotnet/BastionVault.IntegrationSdk/TokenInfo.cs:59`*

