# `NotificationsTypes` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/NotificationsTypes.cs`](../../../dotnet/BastionVault.IntegrationSdk/NotificationsTypes.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `NotificationSendRequest`

#### `Title`

The wire `title` field. Required; an empty or whitespace-only value is refused client-side with an <see cref="ArgumentException"/> before any request is sent (Notifications carries no requirement ID, DR-0017).

*Source: `dotnet/BastionVault.IntegrationSdk/NotificationsTypes.cs:14`*

#### `Body`

The wire `body` field.

*Source: `dotnet/BastionVault.IntegrationSdk/NotificationsTypes.cs:17`*

#### `Severity`

The wire `severity` field: `info`, `success`, `warning`, or `critical`.

*Source: `dotnet/BastionVault.IntegrationSdk/NotificationsTypes.cs:20`*

#### `Channels`

The wire `channels` array.

*Source: `dotnet/BastionVault.IntegrationSdk/NotificationsTypes.cs:23`*

#### `ActionUrl`

The wire `action_url` field.

*Source: `dotnet/BastionVault.IntegrationSdk/NotificationsTypes.cs:27`*

#### `Target`

The wire `target` object.

*Source: `dotnet/BastionVault.IntegrationSdk/NotificationsTypes.cs:30`*

#### `Metadata`

The wire `metadata` object.

*Source: `dotnet/BastionVault.IntegrationSdk/NotificationsTypes.cs:33`*

### `NotificationsConfig`

#### `InboxCap`

The wire `inbox_cap` field.

*Source: `dotnet/BastionVault.IntegrationSdk/NotificationsTypes.cs:43`*

#### `PluginRatePerMin`

The wire `plugin_rate_per_min` field.

*Source: `dotnet/BastionVault.IntegrationSdk/NotificationsTypes.cs:46`*

