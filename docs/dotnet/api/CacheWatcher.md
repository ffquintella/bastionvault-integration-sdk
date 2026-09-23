# `CacheWatcher` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/CacheWatcher.cs`](../../../dotnet/BastionVault.IntegrationSdk/CacheWatcher.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `CacheTopicChanged`

#### `Topic`

The topic name, as it appears in `Topics`.

*Source: `dotnet/BastionVault.IntegrationSdk/CacheWatcher.cs:191`*

#### `PreviousEpoch`

The epoch last observed for this topic, before this change.

*Source: `dotnet/BastionVault.IntegrationSdk/CacheWatcher.cs:194`*

#### `CurrentEpoch`

The epoch this observation reported. Always greater than <see cref="PreviousEpoch"/>.

*Source: `dotnet/BastionVault.IntegrationSdk/CacheWatcher.cs:197`*

### `CacheWatcher`

#### `RunAsync(cancellationToken)`

Runs until the loop stops, emitting exactly one `OnStopped`
as it does. The caller owns the returned task and the `cancellationToken`
that stops it.

*Source: `dotnet/BastionVault.IntegrationSdk/CacheWatcher.cs:60`*

### `CacheWatcherPolicy`

#### `MaxConsecutiveFailures`

How many consecutive watch failures are absorbed before the loop stops. Default 5.

*Source: `dotnet/BastionVault.IntegrationSdk/CacheWatcher.cs:157`*

#### `OnChanged`

CCH-004: raised once per topic whose epoch increased since the last observation.

*Source: `dotnet/BastionVault.IntegrationSdk/CacheWatcher.cs:160`*

#### `OnFailed`

Raised on each watch failure, including the ones the loop then absorbs with backoff.

*Source: `dotnet/BastionVault.IntegrationSdk/CacheWatcher.cs:163`*

#### `OnStopped`

Raised exactly once, when the loop stops for good. A loop that stops emits one reason and
never runs again.

*Source: `dotnet/BastionVault.IntegrationSdk/CacheWatcher.cs:169`*

