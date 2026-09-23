# `LegacyPolicyOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/LegacyPolicyOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/LegacyPolicyOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `LegacyPolicyOperations`

#### `ListPoliciesAsync(options, cancellationToken)`

SYS-040: `GET sys/policy` → `{"keys": [...]}`, the same listing `ListPoliciesAsync` returns.

Wire params: none. Returns a list of policy names, never `null` (empty when there are none). Conformance: Complete (SYS-040). Errors beyond the common set (ERR-061): none.

**Spec:** `Sys.Legacy.ListPolicies — SYS-040`

*Source: `dotnet/BastionVault.IntegrationSdk/LegacyPolicyOperations.cs:39`*

#### `ReadPolicyAsync(name, options, cancellationToken)`

SYS-040: `GET sys/policy/{name}` → a <see cref="Policy"/> whose `Hcl`
is filled from the legacy `rules` key, or `null` when there is no such
policy. This is the difference SYS-040 exists to hide, and it is hidden by sharing
`SysWire.ToPolicy` with the `policies/acl` surface rather than by a second parser.

Wire params: none. Returns a <see cref="Policy"/>, or `null` when no policy has this name. Conformance: Complete (SYS-040). Errors beyond the common set (ERR-061): none.

**Spec:** `Sys.Legacy.ReadPolicy — SYS-040`

*Source: `dotnet/BastionVault.IntegrationSdk/LegacyPolicyOperations.cs:54`*

