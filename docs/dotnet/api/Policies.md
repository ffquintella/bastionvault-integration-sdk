# `Policies` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/Policies.cs`](../../../dotnet/BastionVault.IntegrationSdk/Policies.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `Policy`

#### `Name`

The policy's name, as the server spelled it.

*Source: `dotnet/BastionVault.IntegrationSdk/Policies.cs:20`*

#### `Hcl`

The policy document. Never `null`: a server that sends neither key sends an empty document.

*Source: `dotnet/BastionVault.IntegrationSdk/Policies.cs:23`*

### `PolicyBuilder`

#### `AddPath(path, capabilities, requiredParameters, allowedParameters, scopes, groups)`

Adds one `path` block. `capabilities` reuses SYS-051's
<see cref="Capability"/>, so an unrecognised verb a caller needs is still expressible
through `Other` and is emitted verbatim.

*Source: `dotnet/BastionVault.IntegrationSdk/Policies.cs:197`*

#### `WithMetadata(key, value)`

Sets one `metadata` entry. Setting the same key twice replaces the value and keeps the original position.

*Source: `dotnet/BastionVault.IntegrationSdk/Policies.cs:221`*

#### `Build()`

Emits the document. An empty builder emits the empty string, never a stray blank block.

*Source: `dotnet/BastionVault.IntegrationSdk/Policies.cs:240`*

### `PolicyHistoryEntry`

#### `Timestamp`

The wire `ts` field, parsed as RFC 3339 UTC; `null` when the server omitted or malformed it.

*Source: `dotnet/BastionVault.IntegrationSdk/Policies.cs:30`*

#### `User`

The wire `user` field.

*Source: `dotnet/BastionVault.IntegrationSdk/Policies.cs:33`*

#### `Op`

The wire `op` field.

*Source: `dotnet/BastionVault.IntegrationSdk/Policies.cs:36`*

#### `BeforeRaw`

The wire `before_raw` field: the document as it was, or `null` on a create.

*Source: `dotnet/BastionVault.IntegrationSdk/Policies.cs:39`*

#### `AfterRaw`

The wire `after_raw` field: the document as it became, or `null` on a delete.

*Source: `dotnet/BastionVault.IntegrationSdk/Policies.cs:42`*

### `PolicyTestCase`

#### `Path`

The path to evaluate.

*Source: `dotnet/BastionVault.IntegrationSdk/Policies.cs:59`*

#### `Capability`

The capability to evaluate on <see cref="Path"/>.

*Source: `dotnet/BastionVault.IntegrationSdk/Policies.cs:62`*

#### `Policies`

SYS-045's tri-state. See the remarks on <see cref="PolicyTestCase"/>.

*Source: `dotnet/BastionVault.IntegrationSdk/Policies.cs:65`*

#### `Env`

The optional `env` selector, omitted from the wire when `null`.

*Source: `dotnet/BastionVault.IntegrationSdk/Policies.cs:68`*

### `PolicyTestCaseResult`

#### `Path`

The evaluated path, echoed back.

*Source: `dotnet/BastionVault.IntegrationSdk/Policies.cs:126`*

#### `Capability`

The evaluated capability, echoed back.

*Source: `dotnet/BastionVault.IntegrationSdk/Policies.cs:129`*

#### `Allowed`

Whether the capability is granted.

*Source: `dotnet/BastionVault.IntegrationSdk/Policies.cs:132`*

#### `MatchedPath`

The policy path that matched, or `null` when none did.

*Source: `dotnet/BastionVault.IntegrationSdk/Policies.cs:135`*

#### `MatchKind`

How <see cref="MatchedPath"/> matched.

*Source: `dotnet/BastionVault.IntegrationSdk/Policies.cs:138`*

#### `DeniedByDeny`

Whether an explicit `deny` produced the refusal.

*Source: `dotnet/BastionVault.IntegrationSdk/Policies.cs:141`*

#### `GrantingPolicies`

The policies that granted the capability.

*Source: `dotnet/BastionVault.IntegrationSdk/Policies.cs:144`*

#### `EvaluatedPolicies`

Every policy the server evaluated for this case.

*Source: `dotnet/BastionVault.IntegrationSdk/Policies.cs:147`*

#### `MissingPolicies`

The named policies the server could not find.

*Source: `dotnet/BastionVault.IntegrationSdk/Policies.cs:150`*

#### `DraftOnlyAllowed`

Whether the draft alone, without the named policies, would have allowed the case.

*Source: `dotnet/BastionVault.IntegrationSdk/Policies.cs:153`*

### `PolicyTestResult`

#### `ParseOk`

Whether the draft document parsed.

*Source: `dotnet/BastionVault.IntegrationSdk/Policies.cs:160`*

#### `Errors`

The parse errors, empty when <see cref="ParseOk"/>.

*Source: `dotnet/BastionVault.IntegrationSdk/Policies.cs:163`*

#### `Results`

One result per requested case, in request order.

*Source: `dotnet/BastionVault.IntegrationSdk/Policies.cs:166`*

