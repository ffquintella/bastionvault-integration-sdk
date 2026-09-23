# `RateGate` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/RateGate.cs`](../../../dotnet/BastionVault.IntegrationSdk/RateGate.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `RateGate`

#### `RatePerSecond`

Default 8. `0` disables the rate gate.

*Source: `dotnet/BastionVault.IntegrationSdk/RateGate.cs:12`*

#### `Burst`

Default 16. `0` disables the rate gate.

*Source: `dotnet/BastionVault.IntegrationSdk/RateGate.cs:15`*

#### `IsDisabled`

EFF-001: `true` when either <see cref="RatePerSecond"/> or
<see cref="Burst"/> is `0` — "setting either to `0` disables the gate", read as
written.

The <see cref="Burst"/> limb is M8d's correction. Before the token bucket existed this
property had one reader (none on the request path), so the missing limb was invisible;
with the bucket in place a `Burst` of `0` and a non-zero `RatePerSecond`
would have meant "no request may ever proceed without waiting" rather than "off", which is
the opposite of what EFF-001 says. `rust/.../rate.rs` has read both limbs since M1a
(`either_field_at_zero_disables_the_gate`), so this closes a parity gap rather than
opening one.

*Source: `dotnet/BastionVault.IntegrationSdk/RateGate.cs:31`*

