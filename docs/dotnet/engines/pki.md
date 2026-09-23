# PKI engine (.NET)

**Implements** [`specifications/17-usage-guides.md` guide 12](../../../specifications/17-usage-guides.md)
(the certificate half), adapted to .NET. The language-neutral behaviour this page relies on is
specified in [09 — PKI engine](../../../specifications/09-pki-engine.md) (`PKI-001`…`PKI-030`).
Read that document for what every SDK must do; read this page for the .NET spelling of it.

`Client.Pki.Csr` (the outbound CSR queue for external signing) and `Client.Pki.SignRequests`
(the inbound sign-request approval queue) are nested facades on the same engine (DR-0018
D-M11-10) and are covered here rather than on a page of their own.

## The task

Stand up a root certificate authority, define an issuance role, issue and revoke certificates
against it, list issued certificates in bulk without a 1+N read pattern, and route an externally
generated CSR through the approval queue. **This SDK performs no cryptography and parses no
certificate** (00 §Non-goals, OVR-002, PKI-001): every PEM field this page's examples print is the
server's bytes, unmodified.

Every C# block below is compiled and executed on every build by
`dotnet/BastionVault.IntegrationSdk.DocsSamples`, and checked byte-for-byte against the source
that ran (`DOC-003`). If a block here is wrong, the build is red.

## Prerequisites

| You need | Detail |
|---|---|
| A running server | `https://vault.example.com:8200` throughout this guide |
| A token | Carrying the policy below. The [authentication guide](../authentication.md) covers obtaining one |
| A PKI mount | `pki/` — the default `mount` on every `Client.Pki` member |
| The package | `dotnet add package BastionVault.IntegrationSdk` |

### The policy the example needs

<!-- docs:sample pki/policy -->
```csharp
string hcl = new PolicyBuilder()
    .AddPath("pki/root/generate/internal", [Capability.Create, Capability.Update])
    .AddPath("pki/roles/web", [Capability.Create, Capability.Read, Capability.Update])
    .AddPath("pki/issue/web", [Capability.Update])
    .AddPath("pki/certs-info", [Capability.Read])
    .AddPath("pki/revoke", [Capability.Update])
    .AddPath("pki/sign-request/*", [Capability.Read, Capability.Update, Capability.List])
    .Build();

Console.WriteLine(hcl);
```

which emits:

```hcl
path "pki/root/generate/internal" {
  capabilities = ["create", "update"]
}

path "pki/roles/web" {
  capabilities = ["create", "read", "update"]
}

path "pki/issue/web" {
  capabilities = ["update"]
}

path "pki/certs-info" {
  capabilities = ["read"]
}

path "pki/revoke" {
  capabilities = ["update"]
}

path "pki/sign-request/*" {
  capabilities = ["read", "update", "list"]
}
```

## Step 1 — Generate a root CA and define a role

<!-- docs:sample pki/root-and-role -->
```csharp
PkiRootCertificate root = await client.Pki.GenerateRootAsync(
    PkiKeyGenerationType.Internal,
    new PkiRootSpec { CommonName = "Corp Root", KeyType = "ec", Ttl = TimeSpan.FromHours(87600) });
Console.WriteLine($"root issuer {root.IssuerId}, expires {root.Expiration:O}");
// PrivateKey is null here: Internal never returns key material (PKI-002).

await client.Pki.WriteRoleAsync("web", new PkiRole
{
    AllowedDomains = ["example.com"],
    AllowSubdomains = true,
    MaxTtl = TimeSpan.FromHours(720),
});
```

`GenerateRootAsync`'s `Internal` form keeps the private key on the server permanently; the
`Exported` form returns it once, in `PrivateKey`, held in a redacting `SecretString` (PKI-002) so
it never reaches a log by accident.

## Step 2 — Issue a certificate, then list certificates in bulk

<!-- docs:sample pki/issue-and-list -->
```csharp
// PKI-011: an empty CommonName is refused client-side, before any request is sent.
IssuedCertificate cert = await client.Pki.IssueAsync("web", new IssueRequest
{
    CommonName = "api.example.com",
    AltNames = ["www.example.com"],
    Ttl = TimeSpan.FromHours(72),
});
File.WriteAllText("tls.crt", cert.Certificate);
File.WriteAllText("tls.key", cert.PrivateKey!.Reveal()!);

// One request, however many certificates exist - never `for serial in list: await ReadCertificate(serial)`.
Page<CertificateSummary> page = await client.Pki.ListCertificatesInfoAsync(limit: 100);
foreach (CertificateSummary summary in page.Records)
{
    Console.WriteLine($"{summary.SerialNumber}: {summary.CommonName}, orphaned={summary.IsOrphaned}");
}
```

### What goes over the wire

```http
GET /v2/pki/certs-info?limit=2 HTTP/1.1
Host: vault.example.com:8200
X-BastionVault-Token: s.FAKEtoken
```

```json
{
  "data": {
    "keys": ["aabbcc", "ddeeff"],
    "records": [
      {"serial_number": "aa:bb:cc", "issued_at": "2026-01-01T00:00:00Z", "not_after": "2027-01-01T00:00:00Z", "issuer_id": "issuer-1", "is_orphaned": false, "source": "issued", "key_id": "key-1", "common_name": "one.example.com", "issuer_dn": "CN=Example Root"},
      {"serial_number": "dd:ee:ff", "issued_at": "2026-01-02T00:00:00Z", "not_after": "2027-01-02T00:00:00Z", "issuer_id": "issuer-1", "is_orphaned": false, "source": "issued", "key_id": "key-2", "common_name": "two.example.com", "issuer_dn": "CN=Example Root"}
    ],
    "total": 5,
    "next": "ddeeff",
    "truncated": true
  }
}
```

`ListCertificatesInfoAsync` is pinned to `/v2` regardless of `mount` (D-M9-31); `page.Next` is the
cursor for the following page, and `page.Truncated` tells you there is one before you fetch it.

## Step 3 — Revoke a certificate, and route an external CSR through approval

<!-- docs:sample pki/revoke-and-approve -->
```csharp
await client.Pki.RevokeAsync("aa:bb:cc:dd:ee:ff");

// The outbound queue: an externally-held key signs a CSR this SDK only generates and tracks.
PkiGeneratedCsr queued = await client.Pki.Csr.GenerateAsync(role: "web", commonName: "batch.example.com");
Console.WriteLine($"csr queued: {queued.Csr.Length} bytes");

// The inbound queue: a CSR someone else generated, awaiting approval against a role.
IReadOnlyDictionary<string, JsonElement>? imported =
    await client.Pki.SignRequests.ImportAsync(csr: queued.Csr, requester: "batch-job");
string requestId = imported!["id"].GetString()!;
await client.Pki.SignRequests.ApproveAsync(requestId, "web");

// PKI-030: an empty reason is refused client-side; the alternative path if you reject instead.
try
{
    await client.Pki.SignRequests.RejectAsync(requestId, string.Empty);
}
catch (BastionVaultException e) when (e.Code == ErrorCodes.InputInvalidArgument)
{
    Console.WriteLine("a rejection always needs a reason");
}
```

## The whole program

<!-- docs:sample pki/complete -->
```csharp
using BastionVaultClient client = new();

try
{
    vault.Server.SetRouteResponse(RootRoute, Json(200, RootBody()));
    PkiRootCertificate root = await client.Pki.GenerateRootAsync(
        PkiKeyGenerationType.Internal, new PkiRootSpec { CommonName = "Corp Root", KeyType = "ec" });
    Console.WriteLine($"root issuer {root.IssuerId}");

    vault.Server.SetRouteResponse(RoleRoute, Json(200, "{}"));
    await client.Pki.WriteRoleAsync("web", new PkiRole { AllowedDomains = ["example.com"], AllowSubdomains = true });

    vault.Server.SetRouteResponse(IssueRoute, Json(200, IssueBody()));
    IssuedCertificate cert = await client.Pki.IssueAsync("web", new IssueRequest { CommonName = "api.example.com" });
    Console.WriteLine($"issued {cert.SerialNumber}");

    vault.Server.SetRouteResponse(RevokeRoute, Json(200, "{}"));
    await client.Pki.RevokeAsync(cert.SerialNumber);
    Console.WriteLine("revoked");
}
catch (BastionVaultException e)
{
    Console.Error.WriteLine($"{e.Code}: {e.Message} ({e.Hint}); retryable: {e.Retryable}");
    throw;
}
```

## What can go wrong

| Code | Meaning | Fix |
|---|---|---|
| `BV-PKI-001` | The named role does not exist | Check the name, or `WriteRoleAsync` it first |
| `BV-PKI-002` | The certificate serial is not on record | Check the serial, or `ListCertificatesInfoAsync` for what exists |
| `BV-PKI-003` | This mount has no CA configured yet | `GenerateRootAsync` or `ConfigureCaAsync` first |
| `BV-PKI-004` | The supplied CA material (PEM bundle, chain, cert) is invalid | Check the bundle against 09 §CA lifecycle's requirements |
| `BV-CONFLICT-007` | A managed key name is already taken | Pick a different name, or read the existing key |
| `BV-INPUT-001` | `CommonName` was empty, or `Reject`'s `reason` was empty | Supply the required field; both are checked client-side |

**A gap you will not see coded here:** `09-pki-engine.md`'s `PKI-030` also names a queue-cap limb
— a `BV-QUOTA-002 QueueFull` when the inbound sign-request queue hits its 500-pending cap. No
document states the server's message for that condition (R-31), and this SDK's rule is never to
guess a message-recognition string it cannot cite, so that limb is **not implemented**: a queue-cap
breach today surfaces as whatever generic error the server's `500` maps to, not as
`BV-QUOTA-002`. Only `Reject`'s empty-`reason` check ships.

Handled completely, that is:

<!-- docs:sample pki/handling-errors -->
```csharp
try
{
    await client.Pki.IssueAsync("missing-role", new IssueRequest { CommonName = "x.example.com" });
}
catch (BastionVaultException e)
{
    string remedy = e.Code switch
    {
        ErrorCodes.PkiRoleNotFound => "check the role name, or write it first",
        ErrorCodes.PkiCertificateNotFound => "check the serial number",
        ErrorCodes.PkiCaNotConfigured => "generate or configure a CA on this mount first",
        ErrorCodes.PkiInvalidCaMaterial => "check the PEM bundle or chain you supplied",
        ErrorCodes.ConflictKeyNameExists => "pick a different managed-key name",
        ErrorCodes.InputInvalidArgument => "a required field was empty; see the exception message",
        _ => "look the code up in the error reference",
    };
    Console.Error.WriteLine($"{e.Code}: {e.Message} - {remedy}");
    throw;
}
```

## Next steps

- **Authentication guide** — obtaining the token this guide assumes you already hold.
- **SSH engine guide** — the other certificate-issuing engine, for host access instead of TLS.
- **Error reference** — every code, its category, hint and retryability.
