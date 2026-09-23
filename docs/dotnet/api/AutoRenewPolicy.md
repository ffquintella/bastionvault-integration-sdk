# `AutoRenewPolicy` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/AutoRenewPolicy.cs`](../../../dotnet/BastionVault.IntegrationSdk/AutoRenewPolicy.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `AutoRenewPolicy`

#### `Enabled`

Default `false`. No environment variable resolves this setting.

*Source: `dotnet/BastionVault.IntegrationSdk/AutoRenewPolicy.cs:18`*

#### `RenewAtFraction`

AUT-090: the fraction of the lease at which renewal is scheduled —
`IssuedAt + LeaseDuration × RenewAtFraction`.

*Source: `dotnet/BastionVault.IntegrationSdk/AutoRenewPolicy.cs:24`*

#### `MinInterval`

AUT-090: renewal is never scheduled sooner than this after the previous renewal.

*Source: `dotnet/BastionVault.IntegrationSdk/AutoRenewPolicy.cs:27`*

#### `Increment`

The `increment` AUT-080's renew request asks for, or `null` for the
server's own default.

*Source: `dotnet/BastionVault.IntegrationSdk/AutoRenewPolicy.cs:33`*

#### `MaxConsecutiveFailures`

AUT-092: how many consecutive renewal failures are absorbed before the loop stops.

*Source: `dotnet/BastionVault.IntegrationSdk/AutoRenewPolicy.cs:36`*

#### `OnRenewed`

AUT-091: raised after each successful renewal, carrying the renewed credential.

*Source: `dotnet/BastionVault.IntegrationSdk/AutoRenewPolicy.cs:39`*

#### `OnFailed`

AUT-092: raised on each renewal failure, including the ones the loop then absorbs.

*Source: `dotnet/BastionVault.IntegrationSdk/AutoRenewPolicy.cs:42`*

#### `OnStopped`

Raised exactly once, when the loop stops for good (AUT-092, AUT-093, AUT-094). A loop that
stops emits one reason and never runs again.

*Source: `dotnet/BastionVault.IntegrationSdk/AutoRenewPolicy.cs:48`*

### `RenewalEvent`

#### `At`

When the attempt finished, read from the injected clock (never the wall clock).

*Source: `dotnet/BastionVault.IntegrationSdk/AutoRenewPolicy.cs:95`*

#### `Auth`

The renewed credential on success (AUT-091's new `lease_duration` is
`LeaseDuration`), and `null` on failure.

*Source: `dotnet/BastionVault.IntegrationSdk/AutoRenewPolicy.cs:101`*

#### `Error`

The coded failure on failure, and `null` on success.

*Source: `dotnet/BastionVault.IntegrationSdk/AutoRenewPolicy.cs:104`*

#### `ConsecutiveFailures`

AUT-092's counter as it stands after this attempt: `0` after a success, and the number
of consecutive failures so far after a failure.

*Source: `dotnet/BastionVault.IntegrationSdk/AutoRenewPolicy.cs:110`*

