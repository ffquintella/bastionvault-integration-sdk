# Identity engine (.NET)

Section 17 carries no dedicated usage guide for this `identity/`-mount surface; this page is built
directly from [12 — Other engines and identity](../../../specifications/12-other-engines-and-identity.md)'s
Identity table (`IDN-001`, `IDN-002`), following the same task → prerequisites → steps → complete
example → what can go wrong → next steps structure every other engine guide on this site uses
(`DOC-010`).

⚠️ **Do not confuse this with `Client.Identity.Profile`/`DefaultAccount`/`SshSecurityKey`/
`NamespaceAssignment`.** Those four live on the same `Client.Identity` object but are SYS-080's
own `/v2/sys/identity/*` self-service surface, covered by the [authentication guide](../authentication.md).
Everything on this page — `Self`, `Aliases`, `Groups`, `Sharing`, `Owner` — is section 12's
`identity/*` mount instead, and none of it is `/v2`-pinned.

## The task

Read the calling token's own entity record, put callers into a **user group** so a policy can grant
access to the group rather than to each entity, and share one specific resource directly with
another entity through **Identity.Sharing** rather than widening a policy for it. **This SDK
performs no access-control decision of its own**: every capability and expiry here is enforced by
the server; the SDK only builds the request, including the base64url encoding `IDN-001` requires of
`target`.

Every C# block below is compiled and executed on every build by
`dotnet/BastionVault.IntegrationSdk.DocsSamples`, and checked byte-for-byte against the source
that ran (`DOC-003`). If a block here is wrong, the build is red.

## Prerequisites

| You need | Detail |
|---|---|
| A running server | `https://vault.example.com:8200` throughout this guide |
| A token | Carrying the policy below. The [authentication guide](../authentication.md) covers obtaining one |
| The `identity/` mount | Fixed by the server; no `mount` parameter to override on this surface |
| The package | `dotnet add package BastionVault.IntegrationSdk` |

### The policy the example needs

<!-- docs:sample identity/policy -->
```csharp
string hcl = new PolicyBuilder()
    .AddPath("identity/entity/self", [Capability.Read])
    .AddPath("identity/entity/aliases", [Capability.Read])
    .AddPath("identity/group/user/db-admins", [Capability.Create, Capability.Read])
    .AddPath("identity/sharing/by-target/*", [Capability.Read, Capability.Create, Capability.Update, Capability.List])
    .AddPath("identity/sharing/for-me", [Capability.List])
    .Build();

Console.WriteLine(hcl);
```

which emits:

```hcl
path "identity/entity/self" {
  capabilities = ["read"]
}

path "identity/entity/aliases" {
  capabilities = ["read"]
}

path "identity/group/user/db-admins" {
  capabilities = ["create", "read"]
}

path "identity/sharing/by-target/*" {
  capabilities = ["read", "create", "update", "list"]
}

path "identity/sharing/for-me" {
  capabilities = ["list"]
}
```

## Step 1 — Read the calling token's own entity and aliases

<!-- docs:sample identity/self-and-aliases -->
```csharp
// R-35: no fixture captures this route against a real server, so EntitySelf/Aliases ship
// on ordinary unit coverage rather than fixture conformance (see "What can go wrong" below).
EntitySelf self = await client.Identity.SelfAsync();
Console.WriteLine($"{self.Username} via {self.MountPath}, entity {self.EntityId}");

IReadOnlyList<JsonElement> aliases = await client.Identity.AliasesAsync();
Console.WriteLine($"{aliases.Count} alias(es) on this entity");
```

### What goes over the wire

```http
GET /v1/identity/entity/self HTTP/1.1
Host: vault.example.com:8200
X-BastionVault-Token: s.FAKEtoken
```

```json
{
  "data": {
    "entity_id": "entity-1",
    "username": "alice",
    "mount_path": "auth/appid/",
    "role_name": "ops",
    "aliases": [{"mount_path": "auth/appid/", "name": "alice"}]
  }
}
```

`Identity.Self()` **lazily provisions the entity** — the first call for a caller with no entity yet
creates one rather than 404ing. Every field on `EntitySelf` is nullable and `Raw` carries the whole
object, because section 12 gives a field list, not a captured wire body — see this page's gap note
below for why.

## Step 2 — Put callers into a user group

<!-- docs:sample identity/groups -->
```csharp
await client.Identity.Groups.WriteAsync("user", "db-admins", new IdentityGroupSpec
{
    Description = "Database administrators",
    Members = ["alice", "bob"],
    Policies = ["db-admin"],
});

vault.Server.SetRouteResponse(GroupRoute, Json(200, GroupBody()));
IdentityGroup? group = await client.Identity.Groups.ReadAsync("user", "db-admins");
Console.WriteLine($"{group!.Members.Count} member(s), policies: {string.Join(", ", group.Policies)}");
```

`kind` is `user` or `app`; the same shape governs both, and a role covering both kinds of group
needs both `identity/group/user/*` and `identity/group/app/*` in its policy.

## Step 3 — Share a resource directly, then check what is shared with me

<!-- docs:sample identity/sharing -->
```csharp
// IDN-001: the SDK base64url-encodes `target` itself; pass the plain path, never a
// pre-encoded string.
await client.Identity.Sharing.PutAsync("kv-secret", "secret/app/db", "alice", new IdentitySharingSpec
{
    GranteeKind = "entity",
    Capabilities = ["read"],
    ExpiresAt = DateTimeOffset.Parse("2027-01-01T00:00:00Z"),
});

IReadOnlyList<string> grantees = await client.Identity.Sharing.ListByTargetAsync("kv-secret", "secret/app/db");
Console.WriteLine($"shared with: {string.Join(", ", grantees)}");

// IDN-002: a group share appears here only when its policy carries
// metadata.group_shared_resources = "true" server-side - nothing client-side enforces that.
IdentitySharingForMe forMe = await client.Identity.Sharing.ForMeAsync();
Console.WriteLine($"{forMe.Entries.Count} share(s) visible to entity {forMe.EntityId}");
```

`Identity.Sharing.ForMe()`'s group-share filter (`IDN-002`) is worth restating precisely: a group
share appears in `Entries` only when the policy behind it carries
`metadata.group_shared_resources = "true"` **on the server**. This SDK documents that filter but
enforces nothing about it client-side — there is nothing to enforce, since the filtering already
happened before the response reached this SDK.

## The whole program

<!-- docs:sample identity/complete -->
```csharp
using BastionVaultClient client = new();

try
{
    vault.Server.SetRouteResponse(SelfRoute, Json(200, SelfBody()));
    EntitySelf self = await client.Identity.SelfAsync();
    Console.WriteLine($"logged in as {self.Username}, entity {self.EntityId}");

    vault.Server.SetRouteResponse(ForMeRoute, Json(200, ForMeBody()));
    IdentitySharingForMe forMe = await client.Identity.Sharing.ForMeAsync();
    Console.WriteLine($"{forMe.Entries.Count} resource(s) shared with me");
}
catch (BastionVaultException e)
{
    Console.Error.WriteLine($"{e.Code}: {e.Message} ({e.Hint}); retryable: {e.Retryable}");
    throw;
}
```

## What can go wrong

**A gap you will not see coded here (R-35):** `EntitySelf` and `Identity.Aliases()`'s result carry
no fixture captured against a real server — `12-other-engines-and-identity.md` gives the field
list, but no document ships a wire-accurate example response for `identity/entity/self` or
`identity/entity/aliases` the way, say, `09-pki-engine.md`'s fixtures back the PKI page. This SDK
ships `Self`/`Aliases` on **ordinary unit coverage against a hand-built response** (the JSON above
is this page's own, not a captured fixture) rather than the fixture-conformance testing every other
route on this page enjoys. If the server's real shape ever drifts from the field list section 12
gives, nothing here would catch it before a caller does. Identity carries two requirements of its
own beyond that — `IDN-001` and `IDN-002` — and no other engine-specific error codes: every other
failure reaches the caller through the same generic mapping (Appendix B) that every other route in
this SDK shares.

| Code | Meaning | Fix |
|---|---|---|
| `BV-INPUT-001` | `kind`/`name`/`target`/`grantee` was empty or contained a `..` path segment | Fix the argument the exception names; checked client-side before any request is sent |
| `BV-AUTHZ-001` | The calling token's policy does not grant this path | Extend the policy shown above |
| `BV-NOTFOUND-001` | The named group, sharing grant, or owner record does not exist | Check the name/target/grantee, or write it first |

Handled completely, that is:

<!-- docs:sample identity/handling-errors -->
```csharp
try
{
    // A `..` path segment is refused client-side in `kind`/`name`/`target`/`grantee`,
    // no request sent - the same guard `Logical.*` uses everywhere else in this SDK.
    await client.Identity.Groups.ReadAsync("..", "db-admins");
}
catch (BastionVaultException e)
{
    string remedy = e.Code switch
    {
        ErrorCodes.InputInvalidArgument => "`kind`/`name`/`target`/`grantee` was empty or contained a `..` segment; fix the argument named in the message",
        ErrorCodes.AuthzPermissionDenied => "extend the calling token's policy to cover this path",
        ErrorCodes.NotFoundPathNotFound => "check the group, sharing target, or owner id; write it first",
        _ => "look the code up in the error reference",
    };
    Console.Error.WriteLine($"{e.Code}: {e.Message} - {remedy}");
    throw;
}
```

## Next steps

- **Authentication guide** — `Client.Identity.Profile`/`DefaultAccount`/`SshSecurityKey`, the
  SYS-080 surface this page is not about.
- **Resources engine guide** — the most common `Identity.Sharing` target.
- **Error reference** — every code, its category, hint and retryability.
