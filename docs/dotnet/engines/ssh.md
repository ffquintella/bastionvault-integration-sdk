# SSH engine (.NET)

**Implements** [`specifications/17-usage-guides.md` guide 12](../../../specifications/17-usage-guides.md)
(the SSH half), adapted to .NET. The language-neutral behaviour this page relies on is specified
in [10 — SSH engine and SSH broker](../../../specifications/10-ssh-engine.md) (`SSH-001`…
`SSH-003`, `SSB-001`…`SSB-002`). Read that document for what every SDK must do; read this page for
the .NET spelling of it.

## The task

Configure an SSH certificate authority, define a role, sign a user's own public key into a
short-lived SSH certificate (CA mode), and separately issue and verify a one-time password for a
role that grants shared-credential access instead (OTP mode). A role is one or the other, never
both (`key_type`: `"ca"` or `"otp"`), and this SDK performs no cryptography of its own (00
§Non-goals, OVR-002): every OpenSSH key and certificate line here is the server's bytes,
unmodified.

Every C# block below is compiled and executed on every build by
`dotnet/BastionVault.IntegrationSdk.DocsSamples`, and checked byte-for-byte against the source
that ran (`DOC-003`). If a block here is wrong, the build is red.

## Prerequisites

| You need | Detail |
|---|---|
| A running server | `https://vault.example.com:8200` throughout this guide |
| A token | Carrying the policy below. The [authentication guide](../authentication.md) covers obtaining one |
| An SSH mount | `ssh/` — the default `mount` on every `Client.Ssh` member |
| The package | `dotnet add package BastionVault.IntegrationSdk` |

### The policy the example needs

<!-- docs:sample ssh/policy -->
```csharp
string hcl = new PolicyBuilder()
    .AddPath("ssh/config/ca", [Capability.Create, Capability.Read])
    .AddPath("ssh/roles/ops", [Capability.Create, Capability.Read])
    .AddPath("ssh/sign/ops", [Capability.Update])
    .AddPath("ssh/roles/bastion-hosts", [Capability.Create, Capability.Read])
    .AddPath("ssh/creds/bastion-hosts", [Capability.Update])
    .AddPath("ssh/verify", [Capability.Update])
    .Build();

Console.WriteLine(hcl);
```

which emits:

```hcl
path "ssh/config/ca" {
  capabilities = ["create", "read"]
}

path "ssh/roles/ops" {
  capabilities = ["create", "read"]
}

path "ssh/sign/ops" {
  capabilities = ["update"]
}

path "ssh/roles/bastion-hosts" {
  capabilities = ["create", "read"]
}

path "ssh/creds/bastion-hosts" {
  capabilities = ["update"]
}

path "ssh/verify" {
  capabilities = ["update"]
}
```

## Step 1 — Configure the CA and sign a user certificate

<!-- docs:sample ssh/configure-ca-and-sign -->
```csharp
SshCaKey ca = await client.Ssh.ConfigureCaAsync(generateSigningKey: true);
Console.WriteLine($"CA public key: {ca.PublicKey}");

await client.Ssh.WriteRoleAsync("ops", new SshRole
{
    KeyType = "ca",
    AllowedUsers = ["ubuntu", "ops"],
    DefaultUser = "ubuntu",
    Ttl = TimeSpan.FromMinutes(30),
});

// SSH-001: an empty or whitespace-only PublicKey is refused client-side, before any request is sent.
SignedSshCertificate signed = await client.Ssh.SignAsync("ops", new SshSignRequest
{
    PublicKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIFAKEUSERKEY user@example.com",
    ValidPrincipals = ["ubuntu"],
});
client.Ssh.WriteCertificateFile(signed.SignedKey, "id_ed25519");
// SSH-002: writes id_ed25519-cert.pub with 0644, the file OpenSSH expects alongside the key pair.
```

### What goes over the wire

```http
POST /v1/ssh/sign/ops HTTP/1.1
Host: vault.example.com:8200
X-BastionVault-Token: s.FAKEtoken
Content-Type: application/json

{"public_key":"ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIFAKEUSERKEY user@example.com","valid_principals":"ubuntu"}
```

```json
{
  "data": {
    "signed_key": "ssh-ed25519-cert-v01@openssh.com AAAAHHNzaC1lZDI1NTE5LWNlcnQtdjAxQG9wZW5zc2guY29tFAKESIGNEDCERT user@example.com",
    "serial_number": "42",
    "algorithm": "ssh-ed25519"
  }
}
```

`ValidPrincipals` is a list in C# and a comma-separated string on the wire (SSH-002's sibling
convention to PKI-010); the server rejects a principal the role does not allow with
`BV-AUTHZ-004`, not silently dropping it.

## Step 2 — Issue and verify a one-time password (OTP mode)

<!-- docs:sample ssh/otp-creds-and-verify -->
```csharp
await client.Ssh.WriteRoleAsync("bastion-hosts", new SshRole { KeyType = "otp", DefaultUser = "ubuntu" });

// SSH-003: ip is validated as an IP literal client-side, before any request is sent.
SshCredentials creds = await client.Ssh.CredsAsync("bastion-hosts", ip: "10.0.0.12", username: "ubuntu");
Console.WriteLine($"one-time password issued for {creds.Username}@{creds.Ip}:{creds.Port}");

// Verify is what the bastion host itself calls once the caller presents the OTP.
SshOtpVerification? verification = await client.Ssh.VerifyAsync(creds.Key);
Console.WriteLine(verification is null ? "otp rejected" : $"otp valid for {verification.Username}@{verification.Ip}");
```

`creds.Key` is a `SecretString` (the OTP itself); it is never logged unredacted, and `VerifyAsync`
consumes it directly rather than asking the caller to `Reveal()` it first. A verify against an
unknown or already-consumed OTP answers `BV-SSH-004`, not a `null` result — a `null` return from
`VerifyAsync` here is reserved for a route whose response genuinely carries no body, mirroring
`ReadRoleAsync`'s convention elsewhere in this class.

## The whole program

<!-- docs:sample ssh/complete -->
```csharp
using BastionVaultClient client = new();

try
{
    vault.Server.SetRouteResponse(ConfigureCaRoute, Json(200, CaBody()));
    await client.Ssh.ConfigureCaAsync(generateSigningKey: true);

    vault.Server.SetRouteResponse(OpsRoleRoute, Json(200, "{}"));
    await client.Ssh.WriteRoleAsync("ops", new SshRole { KeyType = "ca", AllowedUsers = ["ubuntu"], DefaultUser = "ubuntu" });

    vault.Server.SetRouteResponse(SignRoute, Json(200, SignedCertBody()));
    SignedSshCertificate signed = await client.Ssh.SignAsync("ops", new SshSignRequest
    {
        PublicKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIFAKEUSERKEY user@example.com",
        ValidPrincipals = ["ubuntu"],
    });
    Console.WriteLine($"signed certificate serial {signed.SerialNumber}");
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
| `BV-SSH-001` | This mount has no CA configured yet | `ConfigureCaAsync` first |
| `BV-SSH-002` | The named role does not exist | Check the name, or `WriteRoleAsync` it first |
| `BV-SSH-003` | The `ip` is outside the role's `cidr_list`, or excluded by it | Check the role's CIDR configuration |
| `BV-SSH-004` | The OTP is invalid, expired, or already consumed | Issue a fresh one with `CredsAsync` |
| `BV-SSH-005` | The role's mode does not match the call (`/sign` on an OTP role, or `/creds` on a CA role) | Use `SignAsync` for `ca` roles and `CredsAsync` for `otp` roles |
| `BV-SSH-006` | The role requires PQC but the CA is classical | Reconfigure the CA with a PQC `algorithm`, or relax the role |
| `BV-AUTHZ-004` | A requested principal is not in the role's `allowed_users` | Request only allowed principals, or widen the role |
| `BV-INPUT-001` | `PublicKey` was empty/whitespace, or `ip` was not a valid literal | Both are checked client-side; fix the argument |

Handled completely, that is:

<!-- docs:sample ssh/handling-errors -->
```csharp
try
{
    await client.Ssh.SignAsync("missing-role", new SshSignRequest { PublicKey = "ssh-ed25519 AAAA... user@example.com" });
}
catch (BastionVaultException e)
{
    string remedy = e.Code switch
    {
        ErrorCodes.SshCaNotConfigured => "configure the CA first",
        ErrorCodes.SshRoleNotFound => "check the role name, or write it first",
        ErrorCodes.SshWrongRoleMode => "use Sign for ca roles, Creds for otp roles",
        ErrorCodes.SshIpNotAllowed => "check the role's cidr_list",
        ErrorCodes.SshInvalidOtp => "issue a fresh otp; this one is gone",
        ErrorCodes.AuthzPrincipalNotAllowed => "request only principals the role allows",
        _ => "look the code up in the error reference",
    };
    Console.Error.WriteLine($"{e.Code}: {e.Message} - {remedy}");
    throw;
}
```

## Next steps

- **Authentication guide** — obtaining the token this guide assumes you already hold.
- **PKI engine guide** — the other certificate-issuing engine, for TLS instead of host access.
- **Error reference** — every code, its category, hint and retryability.
