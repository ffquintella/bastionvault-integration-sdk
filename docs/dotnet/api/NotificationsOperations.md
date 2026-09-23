# `NotificationsOperations` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/NotificationsOperations.cs`](../../../dotnet/BastionVault.IntegrationSdk/NotificationsOperations.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `NotificationsChannelsOperations`

#### `ListAsync(mount, options, cancellationToken)`

`GET {mount}/channels`. 12 names no per-entry field set, so each entry stays a raw <see cref="JsonElement"/> (D-M1c-25).

Wire params: none. Returns the configured channels, empty (never `null`) when none exist or the path is absent; an entry can carry the channel's configured destination (email/webhook/etc.) — treat as sensitive, do not log unredacted. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Notifications.Channels.List — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/NotificationsOperations.cs:193`*

#### `TestAsync(channel, to, mount, options, cancellationToken)`

`POST {mount}/channels/{channel}/test` with `{"to": to}`.

Wire params: to (req). Returns `void`. `to` is the destination address this call sends a live test notification to — an egress surface; the SDK never inspects or logs its value. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Notifications.Channels.Test — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/NotificationsOperations.cs:206`*

### `NotificationsInboxOperations`

#### `ListAsync(mount, options, cancellationToken)`

`GET {mount}/inbox`. 12 names no per-entry field set, so each entry stays a raw <see cref="JsonElement"/> (D-M1c-25).

Wire params: none. Returns the inbox entries, empty (never `null`) when none exist or the path is absent. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Notifications.Inbox.List — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/NotificationsOperations.cs:106`*

#### `UnreadCountAsync(mount, options, cancellationToken)`

`GET {mount}/inbox/unread-count`. 12 names no field for the count itself, so the untyped map fallback applies (D-M1c-25).

Wire params: none. Returns the raw wire map, or `null` when the path is absent (404 treated as absent) or the body is empty. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Notifications.Inbox.UnreadCount — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/NotificationsOperations.cs:119`*

#### `MarkReadAsync(id, mount, options, cancellationToken)`

`POST {mount}/inbox/{id}/read`.

Wire params: none beyond `id`/`mount`. Returns `void`. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Notifications.Inbox.MarkRead — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/NotificationsOperations.cs:132`*

#### `ReadAllAsync(mount, options, cancellationToken)`

`POST {mount}/inbox/read-all`.

Wire params: none. Returns `void`. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Notifications.Inbox.ReadAll — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/NotificationsOperations.cs:145`*

#### `DismissAsync(id, mount, options, cancellationToken)`

`DELETE {mount}/inbox/{id}`.

Wire params: none beyond `id`/`mount`. Returns `void`. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Notifications.Inbox.Dismiss — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/NotificationsOperations.cs:157`*

### `NotificationsOperations`

#### `Inbox`

12 §Notifications: `{mount}/inbox/*`.

*Source: `dotnet/BastionVault.IntegrationSdk/NotificationsOperations.cs:26`*

#### `Channels`

12 §Notifications: `{mount}/channels/*`.

*Source: `dotnet/BastionVault.IntegrationSdk/NotificationsOperations.cs:29`*

#### `SendAsync(request, mount, options, cancellationToken)`

`POST {mount}/send`. `title` is required; a plain <see cref="ArgumentException"/> (Notifications carries no requirement ID of its own, DR-0017), not an invented `BV-INPUT-001` recognition.

Wire params: title (req), body, severity, channels[], action_url, target{}, metadata{}. Returns `void`. `Target`/`ActionUrl` can carry a per-send destination the channel plugin dispatches to — an egress surface the SDK never inspects or logs. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Notifications.Send — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/NotificationsOperations.cs:34`*

#### `SentAsync(mount, options, cancellationToken)`

`GET {mount}/sent/`. 12 names no per-entry field set, so each entry stays a raw <see cref="JsonElement"/> (D-M1c-25).

Wire params: none. Returns the sent-notification entries, empty (never `null`) when none exist or the path is absent; entries are untyped and may echo a prior send's target/action_url — treat as sensitive. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Notifications.Sent — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/NotificationsOperations.cs:49`*

#### `ReadConfigAsync(mount, options, cancellationToken)`

`GET {mount}/config`.

Wire params: none. Returns <see cref="NotificationsConfig"/>, or `null` when no config is set (404 treated as absent). Carries no credential or destination material (inbox_cap, plugin_rate_per_min only). Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Notifications.ReadConfig — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/NotificationsOperations.cs:62`*

#### `WriteConfigAsync(config, mount, options, cancellationToken)`

`POST {mount}/config`.

Wire params (patch-shaped, omitted members untouched): inbox_cap, plugin_rate_per_min. Returns `void`. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).

**Spec:** `Notifications.WriteConfig — 12-other-engines-and-identity.md`

*Source: `dotnet/BastionVault.IntegrationSdk/NotificationsOperations.cs:75`*

