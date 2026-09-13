# 17 — Usage Guides (language-neutral)

These guides are the **source** every SDK adapts into its own documentation
([16 — D2, D4, D5, D8](16-documentation-requirements.md#mandatory-documents)). They are
written in pseudo-code using canonical names; each SDK MUST publish an idiomatic,
runnable version of every guide applicable to its conformance level.

Conventions in the pseudo-code: `await` marks an asynchronous call; `try/catch Error e`
catches the SDK error type; `e.Code` is the stable code.

---

## Guide 1 — Getting started: read your first secret (Core)

**Prerequisites**: a running BastionVault (dev: `bvault server --config config/dev.hcl`),
a token with `read` on `secret/data/app/*`.

```
client = Client.FromEnvironment()            // BASTIONVAULT_ADDR, BASTIONVAULT_TOKEN, BASTIONVAULT_CACERT
// or explicitly:
client = Client(ClientConfig {
  Address:    "https://vault.example.com:8200",
  Token:      Secret("s.FAKE…"),
  CaCertPath: "/etc/ssl/bastionvault-ca.pem",
  Namespace:  ""                              // root
})

health = await client.Sys.Health()
if health.State != Active: fail("vault is " + health.State)

secret = await client.Kv.V2.ReadSecret("secret", "app/db")
if secret == null:            print("no such secret");          exit
if secret.State == SoftDeleted: print("deleted at", secret.Metadata.DeletionTime); exit

username = secret.Data["username"]
password = secret.Data["password"]            // redacting type; call .Reveal() to use
print("version", secret.Metadata.Version, "written by", secret.Metadata.Username)
```

**Wire**: `GET /v1/secret/data/app/db` with `X-BastionVault-Token`, response Shape A with
`data.data` and `data.metadata`.

**What can go wrong**

| Code | Meaning | Fix |
|------|---------|-----|
| `BV-CONFIG-001` | Address invalid or missing | set `Address`/`BASTIONVAULT_ADDR` |
| `BV-TRANSPORT-003` | TLS verification failed | provide the CA via `CaCertPath` |
| `BV-AUTH-001` | no token configured | set `Token` or log in (Guide 2) |
| `BV-AUTHZ-001` | policy denies `read` on `secret/data/app/db` (or token invalid/expired) | check `Sys.CapabilitiesSelf(["secret/data/app/db"])` |
| `BV-SERVER-001` | vault sealed | operator must unseal |
| `BV-NOTFOUND-002` | mount `secret/` missing | `Sys.ListMounts()` |

---

## Guide 2 — Authenticate a service with AppID (Core)

**Prerequisites**: an operator created the role and delivered `role_id` (config) and a
`secret_id` (pipeline); machine binding either satisfied by a FerroGate machine token or
bypassed on the role.

```
client = Client(ClientConfig { Address: addr, CaCertPath: ca, Namespace: "dti/esi" })

machineToken = readEnv("MACHINE_TOKEN")      // optional: output of `bvault ferrogate token --field client_token`

try:
  auth = await client.Auth.AppId.Login(roleId, secretId, machineToken)
catch Error e when e.Code == "BV-AUTH-010":  fail("bad role_id/secret_id (or secret_id used up)")
catch Error e when e.Code == "BV-AUTH-011":  fail("machine binding: " + e.Hint)
catch Error e when e.Code == "BV-AUTHZ-001": fail("credentials OK but gated: namespace/source IP/CIDR — " + e.Hint)

print("token TTL", auth.LeaseDuration, "policies", auth.Policies)
if auth.EnvironmentScope.Scoped: print("this token must pass env=", auth.EnvironmentScope.SecretGlobs)

// long-running service: renew automatically
client.Auth.AutoRenew.Enable({ OnStopped: reason => alert(reason) })
```

**Wire**: `POST /v1/auth/approle/login` **without** a token header, with
`X-BastionVault-Namespace: dti/esi`, body `{"role_id":…,"secret_id":…,"machine_token":…}`.
Success has `auth.client_token`; a credential rejection is **HTTP 400** on this path;
gating failures are **403 Permission denied.**

---

## Guide 3 — Human login with username and password (Core)

```
try:
  auth = await client.Auth.Userpass.Login("alice", Secret(password), totpCode)
catch Error e:
  switch e.Code:
    "BV-AUTH-004": say("wrong username or password")
    "BV-AUTH-005": say("account disabled — contact an admin")
    "BV-AUTH-006": say("locked; retry in " + e.Details["retry_after_secs"] + "s")
    "BV-AUTH-007": promptForTotp(); retry
    "BV-AUTH-008": say("invalid TOTP code")
    "BV-AUTH-009": say("use your security key (FIDO2)")
    default: rethrow
```

**Wire**: `POST /v1/auth/userpass/login/alice` body `{"password":"…","totp_code":"…"}`.
⚠️ Rejections come back as **HTTP 200** with `data.error`; the SDK converts them to the
codes above.

Optional: `client.Auth.PersistToken()` writes `~/.vault-token` (opt-in) so the `bvault` CLI
can reuse it only if the file is plaintext (the CLI's own encrypted format is not
readable by the SDK — `BV-CONFIG-010`).

---

## Guide 4 — Token hygiene: lookup, renew, revoke (Core)

```
info = await client.Auth.Token.LookupSelf()
print(info.Policies, info.DisplayName, info.RemainingTtl)    // RemainingTtl computed by the SDK

if info.RemainingTtl < 5m and auth.Renewable:
  await client.Auth.Token.RenewSelf(increment = 1h)         // POST auth/token/renew/<token> {"increment":3600}

// create a narrow child token for a subprocess
child = await client.Auth.Token.Create({ Policies: ["app-readonly"], Ttl: 15m, NumUses: 20,
                                          Meta: { "purpose": "batch-job" } })  // reserved keys rejected client-side
subprocess.env["BASTIONVAULT_TOKEN"] = child.ClientToken.Reveal()

// on shutdown
await client.Auth.Token.RevokeSelf()                         // clears the local token too
```

Notes the guide MUST include: no `renew-self`/accessor endpoints exist on the server; a
root token's `RevokeSelf` only records a logout; `Lookup` of a revoked token is
`BV-NOTFOUND-006`.

---

## Guide 5 — Write, version, and roll back a KV v2 secret (Core)

```
v1 = await client.Kv.V2.WriteSecret("secret", "app/db", { "username": "admin", "password": "p1" })
v2 = await client.Kv.V2.WriteSecret("secret", "app/db", { "username": "admin", "password": "p2" },
                                    WriteOptions { Cas: v1.Version })         // optimistic concurrency

try:
  await client.Kv.V2.WriteSecret("secret", "app/db", {...}, WriteOptions { Cas: 1 })
catch Error e when e.Code == "BV-KV-003": print("someone else wrote first; re-read")

old  = await client.Kv.V2.GetSecret("secret", "app/db", version = 1)
meta = await client.Kv.V2.ReadMetadata("secret", "app/db")     // meta.Versions[1].CreatedTime …

await client.Kv.V2.SoftDelete("secret", "app/db")              // DELETE data/app/db (latest)
s = await client.Kv.V2.ReadSecret("secret", "app/db")          // s.State == SoftDeleted, s.Data == null
await client.Kv.V2.Undelete("secret", "app/db", [2])
await client.Kv.V2.Destroy("secret", "app/db", [1])            // irreversible → later reads of v1: BV-KV-005

// safe read-modify-write
await client.Kv.V2.UpdateWithRetry("secret", "app/db", current => { ...current, "rotated_at": now() })
```

**Policy needed**:

```hcl
path "secret/data/app/*"     { capabilities = ["create", "read", "update", "delete"] }
path "secret/metadata/app/*" { capabilities = ["read", "list", "delete"] }
path "secret/undelete/app/*" { capabilities = ["update"] }
path "secret/destroy/app/*"  { capabilities = ["update"] }
```

---

## Guide 6 — Per-environment secrets (Core)

```
await client.Kv.V2.WriteAllEnvironments("secret", "app/db",
        base = { "host": "db.internal", "port": "5432", "pool": "10" },
        envs = { "prod":    { "host": "db.prod.internal", "pool": "50" },
                 "staging": { "host": "db.staging.internal" } })

prod = await client.Kv.V2.GetSecret("secret", "app/db", env = "prod")
// prod.Data == { host: db.prod.internal, port: 5432, pool: 50 }; prod.Metadata.ResolvedEnv == "prod"
// prod.Metadata.AvailableEnvs == ["prod", "staging"]

await client.Kv.V2.PatchEnvironment("secret", "app/db", "staging", { "pool": "5" })   // prod untouched

try:   await client.Kv.V2.GetSecret("secret", "app/db", env = "dev")
catch Error e when e.Code == "BV-KV-006": print("dev is not declared on this secret")
```

Notes: `env` travels as `?env=` (query), is visible to policies
(`required_parameters = ["env"]`, `allowed_parameters = {"env" = [...]}`), and is
mandatory for env-scoped AppID tokens (`BV-KV-009` client-side if forgotten).

---

## Guide 7 — Read many secrets efficiently (Core)

```
results = await client.Kv.ReadMany("secret", ["app/db", "app/cache", "app/api-key"])   // one POST /v2/sys/batch
for path, r in results:
  if r is Error: log(path, r.Code)          // e.g. BV-AUTHZ-001 for one path does not fail the others
  else use(r.Data)

// arbitrary mixed batch
batch = await client.Sys.Batch([
  BatchOperation.Read ("secret/data/app/db"),
  BatchOperation.Write("secret/data/app/ttl", { "data": { "ttl": "300" } }),
  BatchOperation.List ("secret/metadata/app/")
])
```

Never `for path in list: await Read(path)` — the server bans an IP above 200 requests /
10 s. The SDK's rate gate (8 req/s, burst 16) protects you, but batching is faster.

---

## Guide 8 — Encrypt application data with Transit (Standard)

```
await client.Transit.CreateKey("transit", "orders", KeyOptions { KeyType: "chacha20-poly1305" })

ct = await client.Transit.Encrypt("transit", "orders", plaintextBytes)         // ct.Ciphertext = "bvault:v1:…"
pt = await client.Transit.Decrypt("transit", "orders", ct.Ciphertext)

await client.Transit.RotateKey("transit", "orders")
ct2 = await client.Transit.Rewrap("transit", "orders", ct.Ciphertext)          // now v2, no plaintext exposure

sig = await client.Transit.Sign("transit", "release-signing", digestBytes)     // ml-dsa-65 key
ok  = await client.Transit.Verify("transit", "release-signing", digestBytes, sig.Signature)

dk  = await client.Transit.GenerateDataKey("transit", "kem", mode = Plaintext) // ml-kem-768: local envelope encryption
```

Errors to show: `BV-TRANSIT-001` (unknown key), `BV-TRANSIT-004` (below
`min_decryption_version`), `BV-TRANSIT-005` (operation not supported by key type),
`BV-INPUT-011` (bad ciphertext framing).

---

## Guide 9 — Connect to an HA cluster (Standard)

```
client = Client(ClientConfig { Address: "vault.corp.example", CaCertPath: ca })   // bare name → SRV discovery
print(client.SelectedNode)                       // { Url, State: ActiveLeader, RttMs }

table = await client.Discover()                  // diagnostics; does not re-pin
render(table)

try:   await client.Kv.V2.ReadSecret(...)
catch Error e when e.Code == "BV-DISCOVERY-003":
  await client.Reconnect()                       // explicit recovery: SRV + probe + re-pin
  retry once
```

Explain: reads/lists fail over **once** automatically when ≥ 2 candidates exist; writes
never do; node-local features (connect sessions, watchers) need `Reconnect`. DNS SRV
record example and SAN requirements from [13](13-cluster-discovery-and-resilience.md).

---

## Guide 10 — Production checklist: timeouts, retries, observability (Standard)

```
client = Client(ClientConfig {
  Address: addr, CaCertPath: ca,
  Timeout: 10s, ConnectTimeout: 3s,
  RetryPolicy: { MaxAttempts: 3, InitialBackoff: 200ms, MaxBackoff: 2s },
  RateGate: { RatePerSecond: 8, Burst: 16 },
  Logger: appLogger,
})
client.OnRequestCompleted(ev => metrics.Observe("bastionvault.client.request.duration", ev.Duration,
                                                 tags = { method: ev.Method, status: ev.StatusCode, code: ev.ErrorCode }))
```

Checklist the guide MUST include: pin a CA; never `TlsSkipVerify` in production; use a
`Login` token source with `AutoRenew`; least-privilege policy per service; cache reads
with TTL and `Sys.CacheVersion`; treat `BV-RATE-001` as a client bug; run the
integration suite against your server version.

---

## Guide 11 — Operate the vault: mounts, policies, namespaces (Complete)

```
await client.Sys.Mount("team-a/", MountRequest { Type: "kv-v2", Description: "team A secrets" })
await client.Kv.V2.WriteConfig("team-a", KvV2Config { MaxVersions: 10, CasRequired: true })

hcl = PolicyBuilder()
        .Path("team-a/data/*", ["create","read","update","delete"]).RequireParameter("env").AllowParameter("env", ["prod","staging"])
        .Path("team-a/metadata/*", ["read","list"])
        .Build()
await client.Sys.WritePolicy("team-a-rw", hcl)
result = await client.Sys.TestPolicy(hcl, "team-a-rw", [{ Path: "team-a/data/x", Capability: "read" }])

ns = await client.Sys.WriteNamespace("engineering", NamespaceSpec { Quotas: { MaxMounts: 20 } })   // full replace!
tenant = client.WithNamespace("engineering")
await tenant.Sys.Mount("secret/", { Type: "kv-v2" })
```

Include: `DeleteNamespace` cascades; `WriteNamespace` replaces (use `UpdateNamespace`);
`Sys.ListMounts` returns only `type`+`description`; remount conflicts are `409`.

---

## Guide 12 — Issue certificates and SSH credentials (Complete)

```
root = await client.Pki.GenerateRoot("pki", Internal, RootSpec { CommonName: "Corp Root", KeyType: "ec", Ttl: "87600h" })
await client.Pki.WriteRole("pki", "web", PkiRole { AllowedDomains: ["example.com"], AllowSubdomains: true, MaxTtl: "720h" })
cert = await client.Pki.Issue("pki", "web", IssueRequest { CommonName: "api.example.com", AltNames: ["www.example.com"], Ttl: "72h" })
writeFile("tls.crt", cert.Certificate); writeFile("tls.key", cert.PrivateKey.Reveal())

page = await client.Pki.ListCertificatesInfo("pki", limit = 100)                 // never 1+N reads
await client.Pki.Revoke("pki", cert.SerialNumber)

ca = await client.Ssh.ConfigureCa("ssh", { GenerateSigningKey: true })
await client.Ssh.WriteRole("ssh", "ops", SshRole { KeyType: "ca", AllowedUsers: "ubuntu,ops", DefaultUser: "ubuntu", Ttl: "30m" })
signed = await client.Ssh.Sign("ssh", "ops", SignRequest { PublicKey: readFile("id_ed25519.pub"), ValidPrincipals: ["ubuntu"] })
client.Ssh.WriteCertificateFile(signed.SignedKey, "id_ed25519-cert.pub")
```

---

## Guide 13 — Diagnosing errors (all levels)

Every error prints as one line:

```
BV-AUTHZ-001: The token does not have permission for this path. — Check the token's policies grant `read` on `secret/data/app/db` (Sys.CapabilitiesSelf). If the token came from another namespace, set Namespace or use a child-visible token. No namespace is set; if the credential is scoped to a namespace, set `Namespace`. [HTTP 403 GET secret/data/app/db] (server: "Permission denied.")
```

Read it left to right: **code** (search the Error reference), **message** (what), **hint**
(what to check, including context notes the SDK added), **request** (method + path as
sent), **server text**. Then:

1. `e.Retryable` false → fix configuration/policy; true → the SDK already retried
   `e.Attempts` times, check the server.
2. `e.Details` holds structured extras (`cas_expected`, `retry_after_secs`,
   `namespace_operable`, `candidates`).
3. `e.Cause` has the underlying runtime exception for transport/TLS problems.
4. Enable debug logging (tokens are redacted) and compare with the server audit log by
   path and timestamp (no request id is emitted by the server).
