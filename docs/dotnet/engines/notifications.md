# Notifications engine (.NET)

Section 17 carries no dedicated usage guide for Notifications; this page is built directly from
[12 — Other engines and identity](../../../specifications/12-other-engines-and-identity.md)'s
Notifications table, following the same task → prerequisites → steps → complete example → what can
go wrong → next steps structure every other engine guide on this site uses (`DOC-010`). This area
carries no requirement ID of its own — every MUST governing it is the generic Shape A envelope and
standard error mapping every route in this SDK already shares (DR-0017).

## The task

Send an operator-facing notification across one or more channels, read it back from a recipient's
inbox, and register or test a delivery channel. This is the SDK's own operational-alerting surface,
not a customer-facing message queue: a typical caller is `cert-lifecycle`'s scheduler telling an
on-call channel that a renewal failed, or a script confirming a channel is wired up correctly before
relying on it.

Every C# block below is compiled and executed on every build by
`dotnet/BastionVault.IntegrationSdk.DocsSamples`, and checked byte-for-byte against the source
that ran (`DOC-003`). If a block here is wrong, the build is red.

## Prerequisites

| You need | Detail |
|---|---|
| A running server | `https://vault.example.com:8200` throughout this guide |
| A token | Carrying the policy below. The [authentication guide](../authentication.md) covers obtaining one |
| A Notifications mount | `notifications/` — the default `mount` on every `Client.Notifications` member |
| The package | `dotnet add package BastionVault.IntegrationSdk` |

### The policy the example needs

<!-- docs:sample notifications/policy -->
```csharp
string hcl = new PolicyBuilder()
    .AddPath("notifications/send", [Capability.Update])
    .AddPath("notifications/sent/", [Capability.List])
    .AddPath("notifications/inbox", [Capability.Read])
    .AddPath("notifications/inbox/unread-count", [Capability.Read])
    .AddPath("notifications/inbox/note-1/read", [Capability.Update])
    .AddPath("notifications/channels", [Capability.Read])
    .AddPath("notifications/channels/email/test", [Capability.Update])
    .AddPath("notifications/config", [Capability.Create, Capability.Read, Capability.Update])
    .Build();

Console.WriteLine(hcl);
```

which emits:

```hcl
path "notifications/send" {
  capabilities = ["update"]
}

path "notifications/sent/" {
  capabilities = ["list"]
}

path "notifications/inbox" {
  capabilities = ["read"]
}

path "notifications/inbox/unread-count" {
  capabilities = ["read"]
}

path "notifications/inbox/note-1/read" {
  capabilities = ["update"]
}

path "notifications/channels" {
  capabilities = ["read"]
}

path "notifications/channels/email/test" {
  capabilities = ["update"]
}

path "notifications/config" {
  capabilities = ["create", "read", "update"]
}
```

## Step 1 — Send a notification, then list what has been sent

<!-- docs:sample notifications/send-and-sent -->
```csharp
await client.Notifications.SendAsync(new NotificationSendRequest
{
    Title = "Certificate renewal failed",
    Body = "cert-lifecycle could not renew api.example.com; see the target's state.",
    Severity = "critical",
    Channels = ["email", "slack-ops"],
});

// 12 names no per-entry field set, so each entry stays a raw JsonElement (D-M1c-25).
IReadOnlyList<JsonElement> sent = await client.Notifications.SentAsync();
Console.WriteLine($"{sent.Count} notification(s) sent so far");
```

### What goes over the wire

```http
POST /v1/notifications/send HTTP/1.1
Host: vault.example.com:8200
X-BastionVault-Token: s.FAKEtoken
Content-Type: application/json

{"title":"Certificate renewal failed","body":"cert-lifecycle could not renew api.example.com; see the target's state.","severity":"critical","channels":["email","slack-ops"]}
```

```json
{"data":{}}
```

`NotificationSendRequest.Target` and `.Metadata` carry no documented field set (section 12 names
only `Send`'s top-level fields), so both stay raw wire maps here rather than a guessed shape —
supply whatever keys your channel configuration expects.

## Step 2 — Read the inbox and mark a notification read

<!-- docs:sample notifications/inbox -->
```csharp
IReadOnlyList<JsonElement> inbox = await client.Notifications.Inbox.ListAsync();
Console.WriteLine($"{inbox.Count} notification(s) in the inbox");

IReadOnlyDictionary<string, JsonElement>? unread = await client.Notifications.Inbox.UnreadCountAsync();
Console.WriteLine($"unread: {unread!["count"].GetInt32()}");

await client.Notifications.Inbox.MarkReadAsync("note-1");
```

`Inbox.List`, like `Sent` above, names no per-entry field set beyond "an array" (12), so each entry
is a raw `JsonElement`; read the field your channel or UI needs directly off it.

## Step 3 — List channels, send a test, and set the mount's own config

<!-- docs:sample notifications/channels-and-config -->
```csharp
IReadOnlyList<JsonElement> channels = await client.Notifications.Channels.ListAsync();
Console.WriteLine($"{channels.Count} channel(s) configured");

await client.Notifications.Channels.TestAsync("email", to: "oncall@example.com");

await client.Notifications.WriteConfigAsync(new NotificationsConfig
{
    InboxCap = 500,
    PluginRatePerMin = 60,
});
```

`Channels.Test` is the fastest way to confirm a channel is reachable before depending on it for a
real alert: it sends one message to the address you supply, through the channel you name, and
raises the same errors a real `Send` to that channel would.

## The whole program

<!-- docs:sample notifications/complete -->
```csharp
using BastionVaultClient client = new();

try
{
    vault.Server.SetRouteResponse(SendRoute, Json(200, "{}"));
    await client.Notifications.SendAsync(new NotificationSendRequest { Title = "Certificate renewal failed", Severity = "critical" });
    Console.WriteLine("notification sent");

    vault.Server.SetRouteResponse(UnreadCountRoute, Json(200, """{"data":{"count":1}}"""));
    IReadOnlyDictionary<string, JsonElement>? unread = await client.Notifications.Inbox.UnreadCountAsync();
    Console.WriteLine($"unread: {unread!["count"].GetInt32()}");
}
catch (BastionVaultException e)
{
    Console.Error.WriteLine($"{e.Code}: {e.Message} ({e.Hint}); retryable: {e.Retryable}");
    throw;
}
```

## What can go wrong

Notifications carries no requirement ID of its own (DR-0017) and no engine-specific error codes:
every failure on this surface reaches the caller through the same generic mapping (Appendix B) that
every other route in this SDK shares — with one exception, called out below.

| Code | Meaning | Fix |
|---|---|---|
| *(`ArgumentException`, not a `BastionVaultException`)* | `Send`'s `title` was empty or whitespace-only | Supply a non-empty title; checked client-side before any request is sent, following the same no-requirement-ID precedent as `Pki.SignAsync` |
| `BV-NOTFOUND-001` | The notification `id` or channel name does not exist | Check the id/name, or `Inbox.List()`/`Channels.List()` for what exists |
| `BV-AUTHZ-001` | The calling token's policy does not grant this path | Extend the policy shown above |

Handled completely, that is:

<!-- docs:sample notifications/handling-errors -->
```csharp
try
{
    // Notifications carries no requirement ID of its own (DR-0017), so an empty
    // required `title` is a plain ArgumentException, not an invented BV-INPUT-001.
    await client.Notifications.SendAsync(new NotificationSendRequest { Title = string.Empty });
}
catch (ArgumentException)
{
    Console.Error.WriteLine("title is required and cannot be empty or whitespace");
    throw;
}
catch (BastionVaultException e)
{
    string remedy = e.Code switch
    {
        ErrorCodes.NotFoundPathNotFound => "check the notification id, or Inbox.List() for what exists",
        ErrorCodes.AuthzPermissionDenied => "extend the calling token's policy to cover this path",
        _ => "look the code up in the error reference",
    };
    Console.Error.WriteLine($"{e.Code}: {e.Message} - {remedy}");
    throw;
}
```

## Next steps

- **Authentication guide** — obtaining the token this guide assumes you already hold.
- **Cert lifecycle engine guide** — a typical source of the alerts this page sends.
- **Error reference** — every code, its category, hint and retryability.
