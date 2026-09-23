# Files engine (.NET)

Section 17 carries no dedicated usage guide for Files; this page is built directly from
[12 — Other engines and identity](../../../specifications/12-other-engines-and-identity.md)'s Files
table (`FIL-001`), following the same task → prerequisites → steps → complete example → what can go
wrong → next steps structure every other engine guide on this site uses (`DOC-010`).

## The task

Store a binary blob against a name, read it back, keep a version history, and point the file at a
sync target so a later `Push` mirrors it somewhere else. **This SDK does no encoding work you would
have to duplicate**: `Content`/`ContentAsync` are always raw `byte[]` — `Files.Create` base64-encodes
into the wire's `content_base64` field, and `Files.Content` decodes it back, so no caller ever
handles the wire's base64 form directly (FIL-001).

Every C# block below is compiled and executed on every build by
`dotnet/BastionVault.IntegrationSdk.DocsSamples`, and checked byte-for-byte against the source
that ran (`DOC-003`). If a block here is wrong, the build is red.

## Prerequisites

| You need | Detail |
|---|---|
| A running server | `https://vault.example.com:8200` throughout this guide |
| A token | Carrying the policy below. The [authentication guide](../authentication.md) covers obtaining one |
| A Files mount | `files/` — the default `mount` on every `Client.Files` member |
| The package | `dotnet add package BastionVault.IntegrationSdk` |

### The policy the example needs

<!-- docs:sample files/policy -->
```csharp
string hcl = new PolicyBuilder()
    .AddPath("files/files/", [Capability.Create, Capability.List])
    .AddPath("files/files/f-1", [Capability.Read, Capability.Update, Capability.Delete])
    .AddPath("files/files/f-1/content", [Capability.Read])
    .AddPath("files/files/f-1/versions", [Capability.Read])
    .AddPath("files/files/f-1/sync/nightly", [Capability.Create, Capability.Update])
    .AddPath("files/files/repoint-resource", [Capability.Update])
    .Build();

Console.WriteLine(hcl);
```

which emits:

```hcl
path "files/files/" {
  capabilities = ["create", "list"]
}

path "files/files/f-1" {
  capabilities = ["read", "update", "delete"]
}

path "files/files/f-1/content" {
  capabilities = ["read"]
}

path "files/files/f-1/versions" {
  capabilities = ["read"]
}

path "files/files/f-1/sync/nightly" {
  capabilities = ["create", "update"]
}

path "files/files/repoint-resource" {
  capabilities = ["update"]
}
```

## Step 1 — Create a file

<!-- docs:sample files/create-and-list -->
```csharp
// FIL-001: Content is raw bytes; the SDK base64-encodes it into content_base64 itself.
byte[] payload = "id,name\n1,widget\n"u8.ToArray();
string id = await client.Files.CreateAsync(new FileCreateRequest
{
    Name = "catalog.csv",
    MimeType = "text/csv",
    Tags = ["catalog", "nightly"],
    Content = payload,
});
Console.WriteLine($"created file {id}");
```

### What goes over the wire

```http
POST /v1/files/files/ HTTP/1.1
Host: vault.example.com:8200
X-BastionVault-Token: s.FAKEtoken
Content-Type: application/json

{"name":"catalog.csv","mime_type":"text/csv","tags":["catalog","nightly"],"content_base64":"aWQsbmFtZQoxLHdpZGdldAo="}
```

```json
{"data":{"id":"f-1"}}
```

FIL-001's 32 MiB body-limit check runs on this encoded body, after the base64 growth — not on
`Content.Length` — so a caller sizing their own payload against the limit should budget for
roughly a third more bytes once encoded.

## Step 2 — Read the content back, update metadata, and list versions

<!-- docs:sample files/content-and-versions -->
```csharp
byte[] roundTripped = await client.Files.ContentAsync("f-1");
Console.WriteLine($"content is {roundTripped.Length} bytes");

await client.Files.UpdateAsync("f-1", new FileUpdateRequest { Notes = "regenerated nightly" });

// No shape beyond the array itself is documented (D-M1c-25), so each entry is a raw element.
IReadOnlyList<JsonElement> versions = await client.Files.VersionsAsync("f-1");
Console.WriteLine($"{versions.Count} version(s) on record");
```

`Files.History`, `Files.Versions`, `Files.ReadVersion` and `Files.VersionContent` all share one
limitation worth stating plainly: section 12 names the route and says "an array" or "a version
record", but no field set beyond that, so every entry here is a raw `JsonElement` rather than a
guessed type. Read a field you need by name off the element itself.

## Step 3 — Point the file at a sync target, then push it

<!-- docs:sample files/sync-and-repoint -->
```csharp
// R-36: section 12 names `kind` but not the credential fields a `local-fs`/`smb` target
// needs, so everything past `kind` travels as an opaque bag through `Fields`, never a
// typed property this SDK would otherwise have to guess the wire name of.
using JsonDocument fields = JsonDocument.Parse("""{"share":"\\\\fileserver\\exports","username":"svc-sync"}""");
await client.Files.Sync.WriteAsync("f-1", "nightly", new SyncTarget
{
    Kind = "smb",
    Fields = fields.RootElement.Clone(),
});

await client.Files.Sync.PushAsync("f-1", "nightly");
Console.WriteLine("pushed to the nightly sync target");

await client.Files.RepointResourceAsync(oldResource: "app/legacy-db", newResource: "app/db");
```

**A gap you will not see coded here:** `12-other-engines-and-identity.md` names `SyncTarget.Kind`
(`local-fs` or `smb`) but documents no wire field name for the credential fields a real `smb`
target needs — a share path, a username, a password, or however the server actually spells them
(R-36). Rather than guess a field name this SDK cannot cite, every field past `Kind` travels through
`SyncTarget.Fields` as an opaque JSON bag, exactly as the caller supplies it. This SDK enforces only
that `Fields` is a JSON object and does not itself carry a `kind` key (which would silently shadow
`SyncTarget.Kind` on the wire); it does not validate, name, or redact anything inside that bag. If
your sync target's credentials are secret material, treat `Fields` as if it were as sensitive as a
`SecretString` yourself — this SDK cannot do that redaction for a shape it does not know.

## The whole program

<!-- docs:sample files/complete -->
```csharp
using BastionVaultClient client = new();

try
{
    vault.Server.SetRouteResponse(FilesRoute, Json(200, CreatedBody()));
    string id = await client.Files.CreateAsync(new FileCreateRequest
    {
        Name = "catalog.csv",
        Content = "id,name\n1,widget\n"u8.ToArray(),
    });
    Console.WriteLine($"created file {id}");

    vault.Server.SetRouteResponse(ContentRoute, Json(200, ContentBody()));
    byte[] content = await client.Files.ContentAsync(id);
    Console.WriteLine($"read back {content.Length} bytes");
}
catch (BastionVaultException e)
{
    Console.Error.WriteLine($"{e.Code}: {e.Message} ({e.Hint}); retryable: {e.Retryable}");
    throw;
}
```

## What can go wrong

Files carries one requirement of its own — `FIL-001` — and no engine-specific error codes: every
other failure on this surface reaches the caller through the same generic mapping (Appendix B)
that every other route in this SDK shares.

| Code | Meaning | Fix |
|---|---|---|
| `BV-NOTFOUND-001` | The file `id`, version, or sync target `name` does not exist | Check the id/version/name, or `Files.List()` for what exists |
| `BV-INPUT-007` | The base64-encoded body exceeds the 32 MiB limit (FIL-001) | Shrink the content, or split it across multiple files |
| `BV-AUTHZ-001` | The calling token's policy does not grant this path | Extend the policy shown above |

Handled completely, that is:

<!-- docs:sample files/handling-errors -->
```csharp
try
{
    await client.Files.ContentAsync("missing");
}
catch (BastionVaultException e)
{
    string remedy = e.Code switch
    {
        ErrorCodes.NotFoundPathNotFound => "check the id, or Files.List() for what exists",
        ErrorCodes.InputBodyTooLarge => "the encoded content exceeds the 32 MiB body limit (FIL-001); shrink or chunk it",
        ErrorCodes.AuthzPermissionDenied => "extend the calling token's policy to cover this path",
        _ => "look the code up in the error reference",
    };
    Console.Error.WriteLine($"{e.Code}: {e.Message} - {remedy}");
    throw;
}
```

## Next steps

- **Authentication guide** — obtaining the token this guide assumes you already hold.
- **Resources engine guide** — attaching a file to a resource record via `resource`/`RepointResource`.
- **Error reference** — every code, its category, hint and retryability.
