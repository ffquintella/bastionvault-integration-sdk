# 10 — SSH Engine (`ssh`) and SSH Broker (`ssh-broker`)

Operations live under `Client.Ssh` (mount default `"ssh"`) and `Client.SshBroker`
(logical mount `ssh-broker`, **v2-only** routes). **Complete** conformance level.

## SSH engine

### CA configuration

| Operation | HTTP | Body / Response |
|-----------|------|-----------------|
| `Ssh.ConfigureCa(mount, {generate_signing_key = true, private_key?, algorithm?})` → `{PublicKey, Algorithm}` | `POST {mount}/config/ca` | `algorithm ∈ "" | ed25519 | mldsa65` (PQC feature-gated) |
| `Ssh.ReadCa(mount)` → `{PublicKey, Algorithm}` | `GET {mount}/config/ca` | |
| `Ssh.DeleteCa(mount)` | `DELETE {mount}/config/ca` | 204 |
| `Ssh.PublicKey(mount)` → `string` | `GET {mount}/public_key` | authorized-keys form |

### Roles

| Operation | HTTP |
|-----------|------|
| `Ssh.ListRoles(mount)` | `LIST {mount}/roles/` |
| `Ssh.ListRolesInfo(mount, after?, limit?)` → `Page<SshRole>` | `GET {mount}/roles-info` |
| `Ssh.WriteRole(mount, name, SshRole)` / `ReadRole` / `DeleteRole` | `{mount}/roles/{name}` |

`SshRole` fields: `key_type` (`ca` \| `otp`), `algorithm_signer` (`ssh-ed25519`),
`cert_type` (`user` \| `host`), `allowed_users`, `default_user`, `allowed_extensions`,
`default_extensions` (map), `allowed_critical_options`, `default_critical_options` (map),
`ttl`, `max_ttl`, `not_before_duration`, `key_id_format`, `cidr_list`, `exclude_cidr_list`,
`port` (22), `pqc_only` (false).

> **Measured — `bvault` 0.44.5, 2026-09-23** ([DR-0021](../decisions/0021-live-server-findings.md)
> F2): `ttl` and `max_ttl` on `{mount}/roles/{name}` are **accepted as JSON numbers**
> (integer seconds). They are recorded here because F2 rejected the same shape on
> `auth/token/create` and on every `pki/*` duration: this endpoint is the counter-example
> that makes a blanket duration rule wrong (TRN-031). `not_before_duration` on the same
> document, and `ttl` on `{mount}/sign/{role}` and `{mount}/creds/{role}`, were **not**
> exercised and are unchanged.

### Signing (CA mode)

```
Ssh.Sign(mount, role, SignRequest { PublicKey (required), ValidPrincipals?, Ttl?, CertType?, KeyId?, Extensions?, CriticalOptions? })
  -> { SignedKey, SerialNumber, Algorithm? }
```

`POST {mount}/sign/{role}`; `valid_principals` is CSV on the wire.

- **SSH-001** `PublicKey` MUST be an OpenSSH public key line; the SDK MUST reject
  empty/whitespace with `BV-INPUT-001`.
- **SSH-002** `SignedKey` is an OpenSSH certificate line; the SDK MUST offer
  `Ssh.WriteCertificateFile(signedKey, path)` writing `<key>-cert.pub` with `0644`.

### One-time passwords (OTP mode)

| Operation | HTTP | Response |
|-----------|------|----------|
| `Ssh.Creds(mount, role, ip, username?, ttl?)` → `{Key, KeyType = "otp", Username, Ip, Port, Ttl}` | `POST {mount}/creds/{role}` | `key` is the OTP (redacting) |
| `Ssh.Verify(mount, otp)` → `{Username, Ip, RoleName, Port}?` | `POST {mount}/verify` | invalid → `BV-SSH-004` |
| `Ssh.Lookup(mount, ip, username?)` → `string[]` roles | `POST {mount}/lookup` | `{roles}` |

- **SSH-003** `ip` MUST be validated as an IP literal client-side (`BV-INPUT-001`).

### Server error strings (recognition)

| Server message | Code |
|----------------|------|
| `ssh CA not configured; POST /config/ca first` | `BV-SSH-001 CaNotConfigured` |
| ``unknown role `<name>` `` | `BV-SSH-002 RoleNotFound` |
| ``role `<r>` has key_type `<k>`; /sign is for CA-mode roles (use /creds for OTP)``, ``role `<r>` is `<k>`, not `otp`; use /sign for CA-mode roles`` | `BV-SSH-005 WrongRoleMode` |
| ``ip `<ip>` is not in role's cidr_list (or is excluded)`` | `BV-SSH-003 IpNotAllowed` |
| ``ip `<ip>` is not a valid IP address: …`` | `BV-INPUT-001` |
| `invalid or expired otp` | `BV-SSH-004 InvalidOtp` |
| `public_key is required`, `otp is required` | `BV-INPUT-001` |
| `role has pqc_only=true but the CA is classical` | `BV-SSH-006 PqcOnlyClassicalCa` |
| `valid_principals … not allowed by role` | `BV-AUTHZ-004 PrincipalNotAllowed` |
| `CA key load failed: …`, `cert sign failed: …` | `BV-SERVER-005` |

## SSH broker (login brokering policy) — **v2-only**

Four tiers resolve a `login_class` (`shared-credential` \| `brokered`) most-restrictive-
wins; a locked upper tier blocks lower writes.

| Operation | HTTP | Fields |
|-----------|------|--------|
| `SshBroker.ReadGlobal()` / `WriteGlobal({login_class_default, login_class_lock})` | `GET/PUT /v2/ssh-broker/policy/global` | root-gated |
| `SshBroker.ReadType(type)` / `WriteType(type, {login_class, lock})` / `DeleteType` | `/v2/ssh-broker/policy/type/{type}` | |
| `SshBroker.ReadAssetGroup(id)` / `WriteAssetGroup(id, {login_class, priority, lock})` / `Delete` | `/v2/ssh-broker/policy/asset-group/{id}` | |
| `SshBroker.ReadResource(id)` / `WriteResource(id, {login_class})` / `Delete` | `/v2/ssh-broker/policy/resource/{id}` | |
| `SshBroker.Effective({resource_id, resource_type, asset_group_ids[]})` → `{LoginClass, Source}` | `POST /v2/ssh-broker/policy/effective` | |

- **SSB-001** All routes MUST be pinned to `/v2`.
- **SSB-002** `403 login_class_locked` → `BV-AUTHZ-005 LoginClassLocked`;
  `409 brokered_resource_no_static_credential` → `BV-CONFLICT-003
  BrokeredResourceStaticCredential`.
