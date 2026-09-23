# Rustion engine (.NET)

Section 17 carries no dedicated usage guide for Rustion; this page is built directly from
[12 — Other engines and identity](../../../specifications/12-other-engines-and-identity.md)'s
Rustion table (`RUS-001`…`RUS-003`), following the same task → prerequisites → steps → complete
example → what can go wrong → next steps structure every other engine guide on this site uses
(`DOC-010`).

`Rustion.Recordings`, `Rustion.Policy`, `Rustion.BastionGroups`, `Rustion.Dispatcher` and
`Rustion.Telemetry` are all nested facades on this one engine (DR-0018 D-M11-10) and are covered
here rather than on pages of their own. Rustion is a **large surface**; this SDK types the
operator-facing subset shown below, and exposes everything else through `Client.Logical` at the
paths [Appendix A](../../../specifications/appendix-a-endpoint-catalogue.md) names.

## The task

Register a bastion **target** and probe it for reachability, open a **connect-only** session against
a resource without the caller ever handling the underlying credential, and list and read session
**recordings**. **This SDK performs no bastion protocol of its own** (00 §Non-goals, OVR-002):
every field here is request building, response parsing and error mapping; the actual proxying,
recording and credential brokering happen on the server and the bastion nodes themselves.

Every C# block below is compiled and executed on every build by
`dotnet/BastionVault.IntegrationSdk.DocsSamples`, and checked byte-for-byte against the source
that ran (`DOC-003`). If a block here is wrong, the build is red.

## Prerequisites

| You need | Detail |
|---|---|
| A running server | `https://vault.example.com:8200` throughout this guide |
| A token | Carrying the policy below. The [authentication guide](../authentication.md) covers obtaining one |
| A Rustion mount | `rustion/` — the default `mount` on every `Client.Rustion` member |
| A secret the session can resolve | `OpenConnectOnly`'s `SecretId` names a secret the server, not this SDK, resolves into credential material |
| The package | `dotnet add package BastionVault.IntegrationSdk` |

### The policy the example needs

<!-- docs:sample rustion/policy -->
```csharp
string hcl = new PolicyBuilder()
    .AddPath("rustion/targets/", [Capability.Create, Capability.List])
    .AddPath("rustion/targets/bastion-1", [Capability.Read])
    .AddPath("rustion/targets/bastion-1/probe", [Capability.Update])
    .AddPath("rustion/session/open", [Capability.Update])
    .AddPath("rustion/recordings/", [Capability.List])
    .AddPath("rustion/recordings/rec_abc123", [Capability.Read])
    .Build();

Console.WriteLine(hcl);
```

which emits:

```hcl
path "rustion/targets/" {
  capabilities = ["create", "list"]
}

path "rustion/targets/bastion-1" {
  capabilities = ["read"]
}

path "rustion/targets/bastion-1/probe" {
  capabilities = ["update"]
}

path "rustion/session/open" {
  capabilities = ["update"]
}

path "rustion/recordings/" {
  capabilities = ["list"]
}

path "rustion/recordings/rec_abc123" {
  capabilities = ["read"]
}
```

## Step 1 — Register a bastion target, then probe it

<!-- docs:sample rustion/targets -->
```csharp
// 12 names no field-level schema for a target, so it travels as an opaque bag (D-M1c-25).
using JsonDocument target = JsonDocument.Parse("""{"name":"bastion-1","host":"bastion1.internal","port":22}""");
await client.Rustion.Targets.CreateAsync(target.RootElement.Clone());

IReadOnlyList<string> targets = await client.Rustion.Targets.ListAsync();
Console.WriteLine($"{targets.Count} target(s) registered");

BastionVault.IntegrationSdk.Response? probe = await client.Rustion.Targets.ProbeAsync("bastion-1");
Console.WriteLine($"probe result: {probe!.Raw}");
```

### What goes over the wire

```http
POST /v1/rustion/targets/ HTTP/1.1
Host: vault.example.com:8200
X-BastionVault-Token: s.FAKEtoken
Content-Type: application/json

{"name":"bastion-1","host":"bastion1.internal","port":22}
```

```json
{"data":{"reachable":true,"latency_ms":12}}
```

Section 12 documents no field-level schema for a Rustion target beyond its existence, so
`Targets.Create`/`Read`/`Write` all take and return a raw `JsonElement`/`Response` rather than a
guessed shape — the same idiom `Rustion.Master`/`Authority` use for the same reason.

## Step 2 — Open a connect-only session against a shared secret

<!-- docs:sample rustion/session -->
```csharp
// v2-pinned (Appendix A): the server resolves the credential from secret_id itself - the
// caller never sees or forwards the raw credential material.
BastionVault.IntegrationSdk.Response? session = await client.Rustion.Session.OpenConnectOnlyAsync(new RustionSessionOpenConnectOnlyRequest
{
    ResourceName = "db-primary",
    SecretId = "secret-abc123",
    TargetHost = "db1.internal",
    TargetPort = 5432,
    TargetProtocol = "postgres",
});
Console.WriteLine($"session opened: {session!.Raw}");
```

`OpenConnectOnlyAsync` is the **only** Rustion route this page pins to `/v2`, and it is pinned
regardless of `mount` ([Appendix A](../../../specifications/appendix-a-endpoint-catalogue.md) bolds
and hardcodes this one path unlike every other Rustion row). `SecretId` and `ConnectTicket` both
travel in the POST body only, never a query string.

## Step 3 — List and read session recordings

<!-- docs:sample rustion/recordings -->
```csharp
IReadOnlyList<string> recordings = await client.Rustion.Recordings.ListAsync();
Console.WriteLine($"{recordings.Count} recording(s) on record");

// `rid` must match `rec_[A-Za-z0-9_-]+`, checked client-side before any request is sent.
BastionVault.IntegrationSdk.Response? recording = await client.Rustion.Recordings.ReadAsync("rec_abc123");
Console.WriteLine($"recording: {recording!.Raw}");

// Recordings.Download (not shown here) reads chunk 0, 1, 2, ... until eof, verifying the
// recording's SHA-256 when the server reports one (RUS-001) - both are node-local (RUS-002).
```

`rid` is validated against `rec_[A-Za-z0-9_-]+` client-side, before any request is sent. This page's
examples stop at reading a recording's own record; a full download is `Recordings.Download`
(RUS-001): it reads chunk `0`, then `1`, `2`, … until `eof`, concatenates each `bytes_b64` payload,
and verifies the recording's SHA-256 when the server reports one — raising `BV-PROTOCOL-004
DigestMismatch` on a mismatch. Both the chunk route and its `Blob` fallback are **node-local**
(RUS-002, DSC-045): a retry after a discovery failover can land on a different node and see a
different chunk set, so this SDK does not retry a chunk download across nodes on your behalf.

## The whole program

<!-- docs:sample rustion/complete -->
```csharp
using BastionVaultClient client = new();

try
{
    vault.Server.SetRouteResponse(TargetsRoute, Json(200, "{}"));
    using JsonDocument target = JsonDocument.Parse("""{"name":"bastion-1","host":"bastion1.internal","port":22}""");
    await client.Rustion.Targets.CreateAsync(target.RootElement.Clone());
    Console.WriteLine("target registered");

    vault.Server.SetRouteResponse(SessionOpenRoute, Json(200, SessionOpenBody()));
    BastionVault.IntegrationSdk.Response? session = await client.Rustion.Session.OpenConnectOnlyAsync(new RustionSessionOpenConnectOnlyRequest
    {
        ResourceName = "db-primary",
        SecretId = "secret-abc123",
        TargetHost = "db1.internal",
        TargetPort = 5432,
        TargetProtocol = "postgres",
    });
    Console.WriteLine("session opened");
}
catch (BastionVaultException e)
{
    Console.Error.WriteLine($"{e.Code}: {e.Message} ({e.Hint}); retryable: {e.Retryable}");
    throw;
}
```

## What can go wrong

Rustion is the one engine on this page with its own dedicated error codes (`RUS-003`): the
authority-handshake tokens the server can return all map to a fixed `BV-RUSTION-00N`, never a
generic code.

| Code | Meaning | Fix |
|---|---|---|
| `BV-RUSTION-001` | `authority_pending_approval`: the bastion's authority is registered but not yet approved | Wait for approval, or approve it |
| `BV-RUSTION-002` | `authority_tombstoned`: the authority was revoked and cannot be reused | Re-attest with a new authority |
| `BV-RUSTION-003` | `attestation_mismatch`: the attestation does not match the expected authority | Re-run attestation with the correct material |
| `BV-RUSTION-004` | `unknown_authority`: no authority is registered for this bastion | Attest the bastion first |
| `BV-RUSTION-005` | `signature_invalid`: the envelope's signature does not verify | Check the signing key configured for this authority |
| `BV-RUSTION-006` | `envelope_replay`: this envelope was already consumed | Issue a fresh envelope; this one cannot be reused |
| `BV-RUSTION-007` | `policy_denied`: the Rustion policy itself denies this action | Check `Policy.Effective` for what is actually allowed |
| `BV-INPUT-001` | `rid` did not match `rec_[A-Za-z0-9_-]+` | Fix the recording id; checked client-side before any request is sent |
| `BV-PROTOCOL-004` | `Download`'s assembled bytes did not match the reported SHA-256 (RUS-001) | Retry the download; treat a repeated mismatch as a server-side data problem |
| `BV-TRANSPORT-004` | The `/blob` fallback exceeded `MaxResponseBytes` (RUS-001) | Fetch the recording in chunks instead of via the whole-blob fallback |

Handled completely, that is:

<!-- docs:sample rustion/handling-errors -->
```csharp
try
{
    // A malformed rid never reaches the server - refused client-side (BV-INPUT-001).
    await client.Rustion.Recordings.ReadAsync("not-a-recording-id");
}
catch (BastionVaultException e)
{
    string remedy = e.Code switch
    {
        ErrorCodes.InputInvalidArgument => "`rid` must match `rec_[A-Za-z0-9_-]+`",
        ErrorCodes.RustionAuthorityPendingApproval => "wait for the pending authority to be approved, or approve it",
        ErrorCodes.RustionUnknownAuthority => "the bastion's authority is not registered; attest it first",
        ErrorCodes.RustionPolicyDenied => "the Rustion policy itself denies this action; check policy.effective",
        ErrorCodes.ProtocolDigestMismatch => "Download's assembled bytes did not match the reported SHA-256 (RUS-001); retry the download",
        ErrorCodes.TransportResponseTooLarge => "the blob fallback exceeded MaxResponseBytes; fetch chunks individually instead",
        _ => "look the code up in the error reference",
    };
    Console.Error.WriteLine($"{e.Code}: {e.Message} - {remedy}");
    throw;
}
```

## Next steps

- **Authentication guide** — obtaining the token this guide assumes you already hold.
- **Resources engine guide** — `Resources.Connect`'s MFA flow, the usual precursor to a connect ticket.
- **Error reference** — every code, its category, hint and retryability.
