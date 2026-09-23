# Security guide (.NET)

**Implements** [`specifications/17-usage-guides.md`](../../specifications/17-usage-guides.md)'s
security-relevant guidance, adapted to .NET. The language-neutral behaviour this page relies on is
specified in [01 — conformance and quality](../../specifications/01-conformance-and-quality.md)'s
security baseline (`CNF-030`…`CNF-035`) and
[02 — client configuration](../../specifications/02-client-configuration.md#token-helper-file).
This page is **risk tier R2** (`CRS-003`): it is instructions an operator will follow, and a wrong
line here causes the exact misconfiguration it exists to prevent, so read every claim as checked
against the SDK's code rather than paraphrased.

## The task

Know what this SDK never writes to a log, exception message, or `ToString`, how redacting types
work, how token files are handled on disk, what TLS defaults apply and how to pin a CA bundle,
why `TlsSkipVerify` is dangerous enough to need a warning of its own, and what a least-privilege
policy for a service account looks like.

Every C# block below is compiled and executed on every build by
`dotnet/BastionVault.IntegrationSdk.DocsSamples`, and checked byte-for-byte against the source
that ran (`DOC-003`). If a block here is wrong, the build is red.

## Prerequisites

| You need | Detail |
|---|---|
| A running server | `https://vault.example.com:8200` throughout this guide |
| A token | The [authentication guide](authentication.md) covers obtaining one |
| The package | `dotnet add package BastionVault.IntegrationSdk` |

### The policy the example needs

<!-- docs:sample security/least-privilege-policy -->
```csharp
// A service account reading one application's secrets needs exactly this, and no more:
// read on the data it uses, and nothing on metadata, undelete, destroy, or any other
// mount. PolicyBuilder emits the HCL deterministically, so the policy you review here is
// the policy Sys.WritePolicy uploads - they cannot drift apart.
string hcl = new PolicyBuilder()
    .AddPath("secret/data/app/*", [Capability.Read])
    .Build();

Console.WriteLine(hcl);
```

which emits:

```hcl
path "secret/data/app/*" {
  capabilities = ["read"]
}
```

Grant exactly the capability the service account uses and no more. A service that only reads
`app/*` gets `read` on `secret/data/app/*` and nothing on `metadata/`, `undelete/`, `destroy/`, or
any other mount — each of those is a separate server route with its own capability, not something
`read` on `data/` implies (see the [secrets guide](secrets-kv.md)).

## Step 1 — What the SDK never logs, and how redaction works

Secret material — tokens, passwords, secret IDs, unseal keys, private keys, KV data — **never**
appears in this SDK's logs, exception messages, or `ToString`/debug output at any log level
(`CNF-031`). Types that hold secret material, such as `SecretString`, redact in their default
string representation (`CNF-032`); the only way to read the value out is `Reveal()`, and it is a
method rather than a property specifically so that reading a secret out is always a visible,
searchable call site in your own code.

<!-- docs:sample security/redaction -->
```csharp
// SecretString never exposes its value through ToString, Debug output, or string
// interpolation - only Reveal() does, and Reveal() is a method precisely so that reading
// a secret out is always a visible, searchable call site (CNF-031, CNF-032).
SecretString token = new("s.FAKEtoken");

Console.WriteLine($"token: {token}");           // prints "token: [REDACTED]"
Console.WriteLine($"token: {token.ToString()}"); // same

string actualValue = token.Reveal()!;
```

This SDK is **stricter than the specification requires**: `CNF-031` permits a token to be shown
redacted as its first four characters followed by `…`, when debug logging is explicitly enabled,
but this implementation does not take that allowance up. There is no code path in
`BastionVault.IntegrationSdk` that truncates or partially reveals a token for a log line — a token
never reaches a log at all, at any level, in any form. If you go looking for a partially-redacted
token in your debug output to confirm logging is configured correctly, you will not find one, and
that is expected: its absence is the guarantee, not a sign of misconfiguration.

## Step 2 — TLS defaults, pinning a CA, and why `TlsSkipVerify` is dangerous

TLS certificate verification is **enabled by default** and there is no configuration surface that
weakens it silently (`CNF-030`). Pin a CA bundle with `CaCertPath` (a file) or `CaCertPem` (inline
PEM, which wins when both are set); set `CaCertReplacesSystemRoots = true` to trust *only* that
bundle instead of adding it to the platform trust store, which is the tighter choice for a service
that talks to exactly one known vault and should not also trust every public CA.

<!-- docs:sample security/pin-a-ca -->
```csharp
// CaCertPath adds this bundle to the trust store used for this client's connections.
// Set CaCertReplacesSystemRoots = true to trust *only* this bundle instead of adding
// to the platform roots - the tighter choice for a service talking to one known vault.
using BastionVaultClient client = new(new BastionVaultClientOptions
{
    Address = "https://vault.example.com:8200",
    Token = "s.FAKEtoken",
    CaCertPath = caCertPath,
    CaCertReplacesSystemRoots = true,
});

Console.WriteLine($"insecure: {client.Config.IsInsecure}");
```

`TlsSkipVerify` disables certificate verification **entirely** — any server on the network path,
including one actively performing a man-in-the-middle attack, is accepted without complaint. It
exists for a local development server with a self-signed certificate you have no other way to pin,
and for nothing else. Setting it is never silent: it requires an explicit configuration flag, it
emits exactly one warning-level log line per `Client` instance (`CNF-030`), and it sets
`Client.Config.IsInsecure = true`, so a health check, a log scraper, or a code reviewer grepping
for `IsInsecure` can catch it even if nobody is watching the console when the process starts.

<!-- docs:sample security/tls-skip-verify-danger -->
```csharp
// TlsSkipVerify disables certificate verification entirely: any server, including one on
// the network path performing a man-in-the-middle, is accepted. It exists for a local dev
// server with a self-signed certificate you cannot otherwise pin, never for production.
// CNF-030 makes the danger observable rather than silent: setting it always emits exactly
// one warning-level log line per Client instance, and Client.Config.IsInsecure becomes
// true, so a health check or a log scraper can catch it even if nobody reads the console.
using BastionVaultClient client = new(new BastionVaultClientOptions
{
    Address = "https://vault.example.com:8200",
    Token = "s.FAKEtoken",
    TlsSkipVerify = true,
    Logger = logger,
});

Console.WriteLine($"insecure: {client.Config.IsInsecure}");
```

## Step 3 — Token file handling

The SDK **never** writes a token to disk on its own (`CNF-033`). It only does so when the
application explicitly opts in with `UseTokenHelper = true` and a `TokenFile` path, mirroring the
Vault CLI's token-helper convention. When it does write, the file is created with **owner-only**
permissions (`0600`, or the closest platform equivalent) at the moment of creation, not `chmod`'d
afterwards — a write-then-`chmod` sequence would leave a window during which the token sits on
disk under the process umask's default, wider, permissions.

<!-- docs:sample security/token-file-handling -->
```csharp
// CNF-033: the SDK never writes a token to disk unless the application explicitly
// opts in with UseTokenHelper. When it does, the file is created with owner-only
// permissions (0600, or the platform's closest equivalent) at creation time, not
// chmod'd afterwards - a write-then-chmod would leave a window where the token sits
// on disk under the process umask's default, wider, mode.
using BastionVaultClient client = new(new BastionVaultClientOptions
{
    Address = "https://vault.example.com:8200",
    Token = "s.FAKEtoken",
    TokenFile = tokenFile,
    UseTokenHelper = true,
});

client.Auth.PersistToken();
```

An encrypted CLI token file carries the `BVTOK1:` marker; the SDK recognises that marker and
refuses to treat the file's contents as a plain token (`BV-CONFIG-010`), rather than reading
ciphertext as if it were a credential.

## Step 4 — Reserved token metadata

`Auth.Token.Create`'s `Meta` dictionary is free-form, with one exception: a fixed set of keys is
reserved for identity, namespace and scope data the server manages itself — `spiffe_id`,
`machine_id`, `username`, `entity_id`, `mount_path`, `role_name`, `role`, `namespace_path`,
`namespace_id`, `child_visible`, `auth_method`, `groups`, `subject`, `name_id`, `name_id_format`,
`ferrogate_kid`, `session_id`, `approle_machine_bypass`, `machine_identity_exempt`, and any key
starting with `approle_env_`. Writing one of them is refused **client-side** before a request is
sent — the server would refuse it too (`meta key(s) … are reserved`), so refusing early costs the
caller nothing and saves a round trip.

<!-- docs:sample security/reserved-token-metadata -->
```csharp
// Auth.Token.Create's meta is free-form, except for the identity/namespace/scope keys the
// server reserves for itself: writing one of them is refused client-side (AUT-081) before
// a request is even sent, with the same code the server would answer anyway.
try
{
    await client.Auth.Token.CreateAsync(new CreateTokenRequest
    {
        Policies = ["default"],
        Meta = new Dictionary<string, string> { ["username"] = "someone-else" },
    });
}
catch (BastionVaultException e) when (e.Code == ErrorCodes.InputReservedTokenMetaKey)
{
    Console.Error.WriteLine($"{e.Code}: {e.Hint}");
}
```

## The whole program

<!-- docs:sample security/tls-skip-verify-danger -->
```csharp
// TlsSkipVerify disables certificate verification entirely: any server, including one on
// the network path performing a man-in-the-middle, is accepted. It exists for a local dev
// server with a self-signed certificate you cannot otherwise pin, never for production.
// CNF-030 makes the danger observable rather than silent: setting it always emits exactly
// one warning-level log line per Client instance, and Client.Config.IsInsecure becomes
// true, so a health check or a log scraper can catch it even if nobody reads the console.
using BastionVaultClient client = new(new BastionVaultClientOptions
{
    Address = "https://vault.example.com:8200",
    Token = "s.FAKEtoken",
    TlsSkipVerify = true,
    Logger = logger,
});

Console.WriteLine($"insecure: {client.Config.IsInsecure}");
```

## What can go wrong

| Code | Meaning | Fix |
|---|---|---|
| `BV-CONFIG-005` | `CaCertPath` (or `ClientCertPath`/`ClientKeyPath`) names a file that cannot be read | Check the path exists and the process can read it — raised at construction, before any request |
| `BV-CONFIG-006` | A certificate or key file is not valid PEM | Ensure it contains `-----BEGIN CERTIFICATE-----`/`PRIVATE KEY` blocks and is not DER or PKCS#12 |
| `BV-TRANSPORT-003` | The TLS handshake or certificate verification failed against the pinned CA | Check `CaCertPath`/`CaCertPem` actually cover the server's certificate; check `TlsServerName` matches a SAN; check the clock. Only as a diagnostic step, and never in production, `TlsSkipVerify` confirms whether trust is the cause |
| `BV-CONFIG-001` | The configured address is malformed | Fix `Address`; see the [configuration reference](configuration.md) |
| `BV-CONFIG-010` | The token file holds an encrypted CLI token (`BVTOK1:`), not a plain one | Use the Vault CLI to obtain a plain token, or point `TokenFile` at a different path |
| `BV-CONFIG-011` | The token file could not be written | Check the directory exists and the process can write to it |
| `BV-INPUT-009` | `Auth.Token.Create`'s `Meta` named a reserved key | Remove the reserved key (see "Reserved token metadata" above); use an application-specific name instead |

## Next steps

- **Authentication guide** — token lifecycle, auto-renew, and machine identity.
- **Configuration reference** — every TLS-related setting, its environment variable, default and
  validation error code.
- **Resilience and operations guide** — the production checklist this guide's TLS and token
  guidance feeds into.
- **Vault compatibility gaps** — why certificate (mTLS) auth answers `BV-SERVER-004` on current
  servers even though the SDK still presents a client certificate at the TLS layer.
