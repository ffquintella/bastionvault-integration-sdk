# Appendix B — Error Catalogue

Normative table of every stable error code. Each implementation's error catalog
(ERR-036) MUST contain exactly these codes with these default messages and hints (hints
may be extended with context notes per [04 — Hint enrichment](04-error-model.md#hint-enrichment-from-context)).
Column **R** is `Retryable`.

## 1. Codes

### Configuration (`BV-CONFIG-*`) — raised before any request; R = no

| Code | Name | Message | Hint |
|------|------|---------|------|
| BV-CONFIG-001 | InvalidAddress | The server address is missing or not a valid URL or cluster name. | Set `Address` (or `BASTIONVAULT_ADDR`) to `https://host:8200`, or to a bare DNS name for cluster discovery. IPv6 literals must be bracketed. |
| BV-CONFIG-002 | InsecureHttpNotAllowed | Plain `http://` to a non-loopback host is not allowed. | Use `https://`, or set `AllowInsecureHttp = true` only for isolated test networks. |
| BV-CONFIG-003 | InvalidSettingValue | A configuration value has the wrong type or range. | Check `Details.setting`; booleans accept 1/0/true/false/yes/no/on/off, durations accept `30s`, `1m30s` or integer seconds; timeouts must be > 0. |
| BV-CONFIG-004 | ClientCertIncomplete | Only one of `ClientCertPath` / `ClientKeyPath` is set. | Provide both the client certificate and its private key (PEM), or neither. |
| BV-CONFIG-005 | FileNotReadable | A configured file cannot be read. | Check `Details.path` exists and the process user can read it. |
| BV-CONFIG-006 | InvalidPem | A certificate or key is not valid PEM. | Ensure the file contains `-----BEGIN CERTIFICATE-----`/`PRIVATE KEY` blocks and is not DER or PKCS#12. |
| BV-CONFIG-007 | InvalidNamespace | The namespace path is malformed. | Use `parent/child` without a leading slash, whitespace or control characters. |
| BV-CONFIG-008 | ReservedHeader | A custom header would override a header the SDK manages. | Remove `X-BastionVault-Token`, `X-Vault-Token`, `Authorization`, `Cookie`, `X-BastionVault-Namespace`, `Host`, `Content-Length` from `Headers`; use `Token`/`Namespace` instead. |
| BV-CONFIG-009 | ListVerbUnsupported | The HTTP stack cannot send the custom `LIST` method. | Use the SDK's default transport or an HTTP client that allows non-standard methods; the server does not support `?list=true`. |
| BV-CONFIG-010 | EncryptedTokenFile | The token file is in the CLI's encrypted `BVTOK1:` format. | Set `BASTIONVAULT_TOKEN`, or export a token with `bvault ferrogate token --field client_token`; the SDK reads plaintext token files only. |

### Input (`BV-INPUT-*`) — R = no

| Code | Name | Message | Hint |
|------|------|---------|------|
| BV-INPUT-001 | InvalidArgument | An argument is missing or invalid. | See `Details.argument` and `Details.reason`; required strings must be non-empty, `env` cannot contain `/`, `env` and `envs` are mutually exclusive. |
| BV-INPUT-002 | EmptyCollection | A list argument must not be empty. | Provide at least one item (`paths`, `versions`, `operations`). |
| BV-INPUT-003 | BatchTooLarge | The batch exceeds the maximum number of operations. | Split into batches of at most `Details.max` (default 128) operations. |
| BV-INPUT-004 | OutOfRange | A numeric argument is out of range. | Respect the documented bounds: `limit` 1–500, `topics` ≤ 64, `Random.bytes` ≤ 4096. |
| BV-INPUT-005 | IterationCapExceeded | A paging iterator stopped at its safety cap. | Raise `MaxRecords` or filter server-side; `Details.total` reports the full count. |
| BV-INPUT-006 | UnsupportedOption | The option is not supported by BastionVault. | Response wrapping (`WrapTtl`) is not implemented by the server; remove the option. |
| BV-INPUT-007 | BodyTooLarge | The request body exceeds the server limit. | Keep bodies under 32 MiB; for files, upload smaller versions or use sync targets. |
| BV-INPUT-008 | ChunkIndexOutOfRange | The recording chunk index is past the end. | Read chunk 0 first and stop at `eof`; `Details.chunk_count` is the real count. |
| BV-INPUT-009 | ReservedTokenMetaKey | A `meta` key is reserved by the server. | Remove `Details.keys` (identity/namespace/scope keys such as `username`, `spiffe_id`, `approle_env_*`); use application-specific key names. |
| BV-INPUT-010 | ReservedPolicyName | The policy name is reserved. | `root`, `default` (delete) and `test` cannot be written or deleted; choose another name. |
| BV-INPUT-011 | InvalidCiphertextFormat | The value is not a BastionVault ciphertext/signature. | Expected `bvault:v<N>:<base64>` (or `bvault:v<N>:pqc:<algo>:<base64>`); pass the exact string returned by Transit. |
| BV-INPUT-012 | NotBase64 | A binary field was not valid base64. | Pass raw bytes and let the SDK encode, or use the `*Base64` parameter with valid standard base64. |
| BV-INPUT-100 | ServerRejectedRequest | The server rejected the request as invalid. | Read `ServerMessage`; typical causes are a missing required field, a wrong field type, or a mutually exclusive combination. |
| BV-INPUT-101 | InvalidUnsealKey | The unseal key was rejected. | Provide a valid hex key share from `Sys.Init`; check for copy/paste truncation. |
| BV-INPUT-102 | CrossNamespacePolicyPath | The policy references a path outside its namespace. | Policies are namespace-scoped; remove `sys/*`/`auth/*` or other-tenant paths, or write the policy in the namespace that owns them. |
| BV-INPUT-103 | BackupFileInvalid | The backup file failed integrity checks. | The `.bvbk` file is corrupted, tampered, or from an unsupported version; restore from a fresh `Sys.Backup`. |

### Transport (`BV-TRANSPORT-*`)

| Code | Name | R | Message | Hint |
|------|------|---|---------|------|
| BV-TRANSPORT-001 | ConnectionFailed | yes | Could not connect to the server. | Check `Address`, DNS, firewall and that the server is listening (default `https://127.0.0.1:8200`). |
| BV-TRANSPORT-002 | Timeout | yes | The request timed out. | Increase `Timeout`/`ConnectTimeout`, check server load; long-poll calls need ≥ 40 s. |
| BV-TRANSPORT-003 | TlsError | yes | TLS handshake or certificate verification failed. | Provide the server CA via `CaCertPath`; check `TlsServerName` matches a SAN; verify the clock. Only as a diagnostic step, and never in production, `TlsSkipVerify` confirms whether trust is the cause. |
| BV-TRANSPORT-004 | ResponseTooLarge | no | The response exceeded `MaxResponseBytes`. | Use the chunked route (`Rustion.Recordings.Download`) or paging (`*-info`), or raise `MaxResponseBytes`. |
| BV-TRANSPORT-005 | Cancelled | no | The operation was cancelled. | The caller cancelled; no request state is known. Retry is the caller's decision. |

### Protocol (`BV-PROTOCOL-*`) — R = no

| Code | Name | Message | Hint |
|------|------|---------|------|
| BV-PROTOCOL-001 | MethodNotAllowed | The server does not accept this HTTP method on this path. | Only GET, POST/PUT, DELETE and LIST are routed; use the matching logical operation. |
| BV-PROTOCOL-002 | UnexpectedResponse | The server response could not be interpreted. | The body was not JSON or not a known shape (`Details.snippet`); confirm `Address` points at a BastionVault API listener, not a proxy or GUI. |
| BV-PROTOCOL-003 | UnexpectedRedirect | The server answered with a redirect. | BastionVault never redirects; a proxy or load balancer in front of it does. Point `Address` at the vault or fix the proxy. |
| BV-PROTOCOL-004 | DigestMismatch | Downloaded bytes do not match the expected SHA-256. | The artifact was corrupted in transit or on the server; do not use it. Retry once, then inspect the server/bastion. |

### Authentication (`BV-AUTH-*`) — R = no

| Code | Name | Message | Hint |
|------|------|---------|------|
| BV-AUTH-001 | NoToken | No token is configured for an authenticated request. | Set `Token`/`BASTIONVAULT_TOKEN`, or configure a `Login` token source (AppID, Userpass, FerroGate). |
| BV-AUTH-002 | Unauthenticated | The server requires authentication for this call. | Provide a valid token; for connect-MFA calls the caller must be a userpass principal. |
| BV-AUTH-003 | LoginRejected | The login was rejected. | The server answered 200 without a token: `ServerMessage` gives the reason; check credentials and account state. |
| BV-AUTH-004 | InvalidCredentials | Invalid username or password. | Verify the username, the password, and that the user exists on this mount (`Details.mount`). |
| BV-AUTH-005 | AccountDisabled | The account is disabled. | An administrator must set `disabled = false` on `auth/<mount>/users/<name>`. |
| BV-AUTH-006 | AccountLocked | The account is temporarily locked after failed attempts. | Wait `Details.retry_after_secs` seconds or ask an administrator to call `Unlock`; do not retry automatically. |
| BV-AUTH-007 | TotpRequired | A TOTP code is required for this account. | Pass `totpCode` to `Userpass.Login`. |
| BV-AUTH-008 | InvalidTotp | The TOTP code is invalid. | Check clock skew and the code's 30 s window; failed codes count toward lockout. |
| BV-AUTH-009 | PasswordLoginDisabled | Password login is disabled for this account. | Use the FIDO2 login flow (`Userpass.Fido2LoginBegin/Complete`). |
| BV-AUTH-010 | InvalidAppIdCredentials | The AppID role_id or secret_id is invalid or exhausted. | Check `role_id`, generate a fresh `secret_id` (it may be single-use or expired), and confirm the mount path. |
| BV-AUTH-011 | AppIdMachineBinding | The AppID login failed the machine-identity check. | Pass a FerroGate `machine_token` for a machine bound to the role, or have an admin set `bypass_machine_binding = true` on the role (paired with `bound_source_ips`). |
| BV-AUTH-012 | EnrolmentPending | The machine is awaiting administrator approval. | An admin must approve the machine (`Auth.Ferrogate.Admin.Approve`); poll `Auth.Ferrogate.Status`. |
| BV-AUTH-013 | EnrolmentRejected | The machine enrolment was rejected. | Contact the vault administrator; the machine must be re-registered. |
| BV-AUTH-014 | MachineRevoked | The machine's access was revoked. | Contact the vault administrator. |
| BV-AUTH-015 | TokenNotRenewable | The token cannot be renewed. | The token is expired, revoked, non-renewable (batch) or past `explicit_max_ttl`; log in again. |
| BV-AUTH-016 | SecondFactorFailed | Second-factor verification failed. | Check the TOTP code or FIDO2 assertion and retry the MFA flow. |

### Authorization (`BV-AUTHZ-*`) — R = no

| Code | Name | Message | Hint |
|------|------|---------|------|
| BV-AUTHZ-001 | PermissionDenied | The token does not have permission for this path (or the token is invalid, expired or revoked). | Check the token's policies grant the capability on `Details.path` (`Sys.CapabilitiesSelf`); verify the token with `Auth.Token.LookupSelf`; if the credential is namespace-scoped set `Namespace`; a `token_bound_cidrs` or `bound_source_ips` rule may exclude this client. |
| BV-AUTHZ-002 | NamespaceNotOperable | The token cannot operate in the active namespace. | The token is bound to `Details.token_namespace` and is not child-visible for `Details.active_namespace`; log in with the namespace header or use a child-visible token. |
| BV-AUTHZ-003 | ClusterStatusGated | Cluster status requires a token or a cluster-local caller. | Provide any valid token, or run from a cluster node. |
| BV-AUTHZ-004 | PrincipalNotAllowed | A requested SSH principal is not allowed by the role. | Use principals listed in the role's `allowed_users`, or update the role. |
| BV-AUTHZ-005 | LoginClassLocked | The SSH login class is locked by an upstream policy tier. | Change it at the tier that holds the lock (global/type/asset-group) or remove the lock. |

### Not found (`BV-NOTFOUND-*`) — R = no

| Code | Name | Message | Hint |
|------|------|---------|------|
| BV-NOTFOUND-001 | PathNotFound | Nothing exists at this path. | Check the mount and the engine's path layout (`Details.path`); KV v2 data lives under `<mount>/data/<name>`; unregistered `sys/*` routes also answer 404. |
| BV-NOTFOUND-002 | MountNotFound | No secrets engine or auth method is mounted at this path. | List mounts with `Sys.ListMounts`/`Sys.ListAuthMethods`; check the namespace. |
| BV-NOTFOUND-005 | PolicyNotFound | No policy with this name exists. | `Sys.ListPolicies` shows available names; names are namespace-scoped. |
| BV-NOTFOUND-006 | TokenNotFound | The token does not exist or was revoked. | Log in again; a revoked or expired token cannot be looked up or renewed. |
| BV-NOTFOUND-007 | NamespaceNotFound | No such namespace. | `Sys.ListNamespaces` from the parent namespace; paths are relative to the active namespace. |
| BV-NOTFOUND-008 | ResourceNotFound | The named object does not exist in this engine. | Check the name and mount; `Details.kind` names the object type (target, role, key…). |

### Conflict (`BV-CONFLICT-*`) — R = no

| Code | Name | Message | Hint |
|------|------|---------|------|
| BV-CONFLICT-001 | Conflict | The request conflicts with current server state. | Read `ServerMessage`; re-read the object and retry with the current state. |
| BV-CONFLICT-002 | RecordingDigestMismatch | The recording bytes do not match the recorded digest. | Deterministic failure: do not retry; inspect the bastion and the sidecar digest. |
| BV-CONFLICT-003 | BrokeredResourceStaticCredential | A static SSH credential cannot be attached to a brokered resource. | Remove `private_key`/`password` or change the resource's `login_class`. |
| BV-CONFLICT-004 | MountPathInUse | The destination mount path is already in use. | Choose another `to` path or unmount the existing engine first. |
| BV-CONFLICT-005 | AlreadyInitialized | The vault is already initialized. | Use `Sys.Unseal` with existing key shares; `Init` only runs once. |
| BV-CONFLICT-006 | SecretAlreadyExists | The secret already exists (CAS 0 failed). | Use `WriteSecret` with the current version as `cas`, or `UpdateWithRetry`. |
| BV-CONFLICT-007 | KeyNameExists | A key with this name already exists. | Choose a different `name` or reference the existing key by `key_ref`. |

### Rate limit and quota (`BV-RATE-*`, `BV-QUOTA-*`)

| Code | Name | R | Message | Hint |
|------|------|---|---------|------|
| BV-RATE-001 | RateLimitedByDosGuard | no | The server's abuse guard temporarily blocked this client IP. | The rate gate is paused for `RetryAfter` seconds. Reduce request fan-out — use `Sys.Batch`, `Kv.ReadMany`, `*-info` pages and a read cache — and do not add retries. |
| BV-RATE-002 | NamespaceRateQuotaExceeded | yes | The namespace request-rate quota was exceeded. | Slow down or ask an admin to raise `request_rate` on the namespace; back off before retrying. |
| BV-QUOTA-001 | NamespaceQuotaExceeded | no | A namespace capacity quota was reached. | `ServerMessage` names the quota (mounts, leases, entities, storage); free capacity or raise the quota via `Sys.UpdateNamespace`. |
| BV-QUOTA-002 | QueueFull | no | The server queue is full. | Approve, reject or delete pending items before submitting more (limit in `Details.max`). |

### Server state (`BV-SERVER-*`)

| Code | Name | R | Message | Hint |
|------|------|---|---------|------|
| BV-SERVER-001 | Sealed | no | The vault is sealed. | An operator must unseal it (`bvault operator unseal` or HSM auto-unseal); the SDK does not retry. Use `Sys.Health` to watch for readiness. |
| BV-SERVER-002 | Unavailable | yes | The server is temporarily unavailable. | Cluster has no leader/quorum, node unhealthy, or HSM unreachable; the SDK retries idempotent calls. Check `Sys.ClusterStatus` and node health. |
| BV-SERVER-003 | Standby | yes | The node is a standby and cannot serve this request. | Discovery should have picked the leader; call `Client.Reconnect()` or point `Address` at the leader. |
| BV-SERVER-004 | UnsupportedByServer | no | This server version does not support the endpoint. | Check `Client.ServerVersion()`; upgrade the server or use the documented fallback (per-object reads instead of `*-info`, `/blob` instead of chunks). Also returned for disabled backends such as `cert` auth. |
| BV-SERVER-005 | InternalError | no | The server reported an internal error. | Read `ServerMessage`; many engine validation errors are reported as 500 — the message names the field or object. Check server logs if it is generic. |
| BV-SERVER-006 | ApiVersionMismatch | no | The engine is not available on the requested API version. | Pin the call to `/v2` (`RequestOptions.ApiVersion = 2`) or use the typed operation, which pins automatically. |
| BV-SERVER-007 | NotInitialized | no | The vault is not initialized. | Run `Sys.Init` once and store the keys and root token securely. |

### Discovery (`BV-DISCOVERY-*`)

| Code | Name | R | Message | Hint |
|------|------|---|---------|------|
| BV-DISCOVERY-001 | NoCandidates | no | Cluster discovery found no nodes. | Check the SRV record `_bvault._tcp.<name>` exists, or use a literal `https://host:port` address. |
| BV-DISCOVERY-002 | NoHealthyNode | no | No healthy node was found in the cluster. | `Details.candidates` lists each node's state (sealed/uninitialized/unreachable); unseal or start the nodes, check TLS SANs cover SRV targets. |
| BV-DISCOVERY-003 | NodeUnavailable | yes | The pinned node became unavailable. | Read/list calls fail over once automatically; for writes or node-local sessions call `Client.Reconnect()` and retry. |

### KV (`BV-KV-*`) — R = no

| Code | Name | Message | Hint |
|------|------|---------|------|
| BV-KV-001 | SecretNotFound | No secret exists at this path. | Check mount and path (`<mount>/data/<name>` for v2); list with `Kv.V2.List`. |
| BV-KV-002 | DataFieldMissing | The write has no data. | Provide a non-empty `data` map (v2 wraps it as `{"data": {...}}`). |
| BV-KV-003 | CasMismatch | The check-and-set version did not match the current version. | Re-read the secret to get the current version and retry with `cas` set to it (or use `UpdateWithRetry`); `cas = 0` means the secret must not exist yet. |
| BV-KV-004 | CasRequired | This secret or mount requires check-and-set. | Pass `WriteOptions.Cas` with the current version (0 for a new secret). |
| BV-KV-005 | VersionDestroyed | The requested version was permanently destroyed. | Destroyed versions cannot be recovered; read another version or the latest. |
| BV-KV-006 | EnvironmentNotDeclared | The secret has no overrides for this environment. | `Metadata.AvailableEnvs` lists declared environments; write overrides with `PatchEnvironment` or read the base. |
| BV-KV-007 | VersionSoftDeleted | The requested version is soft-deleted. | Call `Kv.V2.Undelete` to restore it, or read another version. |
| BV-KV-008 | VersionNotFound | The requested version does not exist. | Check `Kv.V2.ReadMetadata` for available versions; pruning by `max_versions` removes old ones. |
| BV-KV-009 | EnvironmentRequired | This token is environment-scoped and must pass `env`. | Pass `env` matching the token's scope globs (`Details.secret_globs`, `Details.machine_globs`). |
| BV-KV-010 | NotAKvMount | The mount is not a KV engine. | `Sys.MountTypeOf` reports `Details.type`; use the matching engine client. |
| BV-KV-011 | FieldNotFound | The secret has no such field. | Available fields are `Details.fields`. |

### Transit (`BV-TRANSIT-*`) — R = no

| Code | Name | Message | Hint |
|------|------|---------|------|
| BV-TRANSIT-001 | KeyNotFound | The transit key does not exist. | Create it with `Transit.CreateKey` or check the mount. |
| BV-TRANSIT-002 | KeyTypeConflict | A key with this name exists with a different type. | Use another name, or delete the existing key (requires `deletion_allowed`). |
| BV-TRANSIT-003 | DeletionNotAllowed | The key is protected from deletion. | Set `deletion_allowed = true` via `Transit.ConfigureKey` first. |
| BV-TRANSIT-004 | VersionNotDecryptable | The ciphertext version is below `min_decryption_version` or missing. | Rewrap ciphertexts before raising `min_decryption_version`, or lower it via `Transit.ConfigureKey`. |
| BV-TRANSIT-005 | OperationNotSupportedByKeyType | The key type does not support this operation. | Symmetric AEAD keys encrypt/decrypt; signature keys sign/verify; KEM keys use datakey. |
| BV-TRANSIT-006 | AlgorithmMismatch | The ciphertext/signature algorithm does not match the key. | Use the key that produced the value; check the `pqc:<algo>` tag. |

### PKI (`BV-PKI-*`) — R = no

| Code | Name | Message | Hint |
|------|------|---------|------|
| BV-PKI-001 | RoleNotFound | The PKI role does not exist. | Create it with `Pki.WriteRole`. |
| BV-PKI-002 | CertificateNotFound | No certificate with this serial. | Check the serial format (`aa:bb:…` or hex) and the mount. |
| BV-PKI-003 | CaNotConfigured | The mount has no CA. | Run `Pki.GenerateRoot` or `Pki.ConfigureCa` first. |
| BV-PKI-004 | InvalidCaMaterial | The CA bundle or chain is invalid. | Ensure the PEM contains a CA certificate matching its private key and a correct chain order. |

### SSH (`BV-SSH-*`) — R = no

| Code | Name | Message | Hint |
|------|------|---------|------|
| BV-SSH-001 | CaNotConfigured | The SSH CA is not configured. | Call `Ssh.ConfigureCa` first. |
| BV-SSH-002 | RoleNotFound | The SSH role does not exist. | Create it with `Ssh.WriteRole`. |
| BV-SSH-003 | IpNotAllowed | The IP is outside the role's CIDR list. | Use an IP within `cidr_list` (not in `exclude_cidr_list`) or update the role. |
| BV-SSH-004 | InvalidOtp | The OTP is invalid or expired. | OTPs are single-use; request a new one with `Ssh.Creds`. |
| BV-SSH-005 | WrongRoleMode | The role's key_type does not match the operation. | Use `Ssh.Sign` for `ca` roles and `Ssh.Creds` for `otp` roles. |
| BV-SSH-006 | PqcOnlyClassicalCa | The role requires PQC but the CA is classical. | Reconfigure the CA with `algorithm = mldsa65` or clear `pqc_only`. |

### TOTP (`BV-TOTP-*`) — R = no

| Code | Name | Message | Hint |
|------|------|---------|------|
| BV-TOTP-001 | KeyNotFound | The TOTP key does not exist. | Create it with `Totp.CreateKey`. |
| BV-TOTP-002 | WrongModeForOperation | The operation does not match the key mode. | Generate-mode keys: `GenerateCode`; provider-mode keys: `ValidateCode`. |

### Rustion (`BV-RUSTION-*`) — R = no

| Code | Name | Message | Hint |
|------|------|---------|------|
| BV-RUSTION-001 | AuthorityPendingApproval | The bastion has not approved this authority yet. | Approve the authority on the bastion side. |
| BV-RUSTION-002 | AuthorityTombstoned | The authority was tombstoned. | Rotate the master key and re-enrol. |
| BV-RUSTION-003 | AttestationMismatch | The authority attestation does not match. | Re-run `Rustion.Authority.Attest`. |
| BV-RUSTION-004 | UnknownAuthority | The bastion does not know this authority. | Export `Rustion.Master.PubKey` and enrol it on the bastion. |
| BV-RUSTION-005 | SignatureInvalid | The envelope signature failed verification. | Check the master key material; rotate if compromised. |
| BV-RUSTION-006 | EnvelopeReplay | The envelope was replayed. | Do not resend the same envelope; generate a new request. |
| BV-RUSTION-007 | PolicyDenied | The bastion policy denied the session. | Check `Rustion.Policy.Effective` for the resource. |

## 2. Server-message recognition

The mapping function (ERR step 4) MUST apply these rules to the trimmed, lower-cased
server message (trailing `.` and a `(retry after Ns)` suffix ignored). `prefix` matches
the start of the message; `exact` the whole message; `contains` a substring.

| Match | Server text | Code |
|-------|-------------|------|
| exact | `permission denied` | BV-AUTHZ-001 |
| exact | `missing client token` / `request client token is missing` | BV-AUTH-001 |
| exact | `bastionvault is sealed` / contains `is sealed` (5xx) | BV-SERVER-001 |
| exact | `bastionvault is not initialized` | BV-SERVER-007 |
| exact | `bastionvault is already initialized` | BV-CONFLICT-005 |
| exact | `bastionvault unseal key is invalid` | BV-INPUT-101 |
| exact | `logical backend path not supported` | BV-SERVER-004 |
| exact | `logical backend operation not supported` | BV-PROTOCOL-001 |
| exact | `router mount not found` | BV-NOTFOUND-002 |
| exact | `router mount conflict` / `mount path already exists` | BV-CONFLICT-004 |
| exact | `mount path is protected, cannot mount` | BV-INPUT-100 |
| prefix | `no matching mount at` | BV-NOTFOUND-002 |
| prefix | `path already in use at` | BV-CONFLICT-004 |
| exact | `request is invalid` | BV-INPUT-100 |
| exact | `request field is not found` / `request field is invalid` / `no data field is available for the request` | BV-INPUT-100 |
| prefix | `api version mismatch` | BV-SERVER-006 |
| exact | `cluster has no leader` / `cluster lost quorum` / `cluster node is unhealthy` | BV-SERVER-002 |
| contains (5xx) | `standby` | BV-SERVER-003 |
| contains (5xx) | `uninitialized` | BV-SERVER-007 |
| prefix | `request temporarily blocked by dos protection` | BV-RATE-001 |
| prefix | `namespace request-rate quota exceeded` | BV-RATE-002 |
| prefix | `namespace quota exceeded` | BV-QUOTA-001 |
| prefix | `cluster status requires a valid token` | BV-AUTHZ-003 |
| prefix | `no policy named` | BV-NOTFOUND-005 |
| prefix | `no such namespace` | BV-NOTFOUND-007 |
| prefix | `batch must contain at least one operation` | BV-INPUT-002 |
| prefix | `batch has` (contains `exceeds max`) | BV-INPUT-003 |
| prefix | `meta key(s)` (contains `are reserved`) | BV-INPUT-009 |
| prefix | `cannot assign policy` | BV-AUTHZ-001 (Details.policy) |
| prefix | `root tokens may not be created` / `expiring root tokens cannot create` | BV-AUTHZ-001 |
| prefix | `cannot create a token in namespace` / `cannot set child_visible` | BV-AUTHZ-002 |
| prefix | `sentinel (rgp/egp) policies cannot be created` | BV-INPUT-100 |
| contains | `namespace` + `refuse`/`cross-namespace` (policy write) | BV-INPUT-102 |
| prefix | `backup hmac verification failed` / `backup` + `invalid magic`/`unsupported version`/`corrupted` | BV-INPUT-103 |
| exact | `module kv data field is missing` / `data field is missing from request` | BV-KV-002 |
| exact | `check-and-set parameter did not match the current version` | BV-KV-003 |
| exact | `check-and-set parameter required for this call` | BV-KV-004 |
| exact | `version has been permanently destroyed` | BV-KV-005 |
| exact | `version does not exist` | BV-KV-008 |
| exact | `invalid username or password` | BV-AUTH-004 |
| exact | `account is disabled` | BV-AUTH-005 |
| prefix | `account temporarily locked` | BV-AUTH-006 (Details.retry_after_secs) |
| exact | `a totp code is required for this account` | BV-AUTH-007 |
| exact | `invalid totp code` | BV-AUTH-008 |
| prefix | `password login is disabled for this account` | BV-AUTH-009 |
| prefix | `missing role_id` / `invalid role_id` / `invalid secret_id` / `invalid secret id` | BV-AUTH-010 |
| prefix | `machine_token` / `machine ` (`is not bound`, `is not approved`) | BV-AUTH-011 |
| prefix | `source address` (contains `unauthorized`) | BV-AUTHZ-001 (Details.source_ip) |
| prefix | `enrolment_pending` | BV-AUTH-012 |
| exact | `enrolment_rejected` | BV-AUTH-013 |
| exact | `machine_revoked` | BV-AUTH-014 |
| prefix | `rate_limited` | BV-RATE-001 |
| exact | `direct svid login is disabled (accept_svid is off)` | BV-AUTH-003 |
| exact | `unknown machine` | BV-NOTFOUND-008 |
| exact | `second-factor verification failed` | BV-AUTH-016 |
| exact | `connect mfa requires an authenticated caller` / `no authenticated caller` / `step-up requires an authenticated caller` | BV-AUTH-002 |
| prefix | `unknown key` | BV-TRANSIT-001 |
| prefix | `key ` + contains `already exists with type` | BV-TRANSIT-002 |
| prefix | `deletion_allowed is false` | BV-TRANSIT-003 |
| prefix | `version ` + contains `is below min_decryption_version` / `not found on key` | BV-TRANSIT-004 |
| contains | `does not support /` / `is symmetric-aead only` / `is symmetric only` / `do not support /` | BV-TRANSIT-005 |
| contains | `algorithm mismatch` / `hmac framing must not carry a pqc tag` | BV-TRANSIT-006 |
| prefix | `not a bvault ciphertext` / `malformed ciphertext` / `malformed pqc ciphertext` / `ciphertext version must be` | BV-INPUT-011 |
| contains | `: not base64 (` | BV-INPUT-012 |
| prefix | `bytes capped at 4096` | BV-INPUT-004 |
| exact | `pki role is not found` | BV-PKI-001 |
| exact | `pki certificate is not found` | BV-PKI-002 |
| exact | `pki ca is not config` / `pki ca private key is not found` | BV-PKI-003 |
| prefix | `pki pem bundle` / `pki cert chain` / `pki cert is not ca` / `pki ca public key` / `pki ca extension` | BV-PKI-004 |
| exact | `pki key_name already exists` | BV-CONFLICT-007 |
| prefix | `pki key type` / `pki key bits` / `pki key operation` / `pki data is invalid` | BV-INPUT-100 |
| exact | `pki internal error` | BV-SERVER-005 |
| prefix | `ssh ca not configured` | BV-SSH-001 |
| prefix | `unknown role` (ssh mount) | BV-SSH-002 |
| prefix | `role ` + contains `/sign is for ca-mode roles` / `use /sign for ca-mode roles` | BV-SSH-005 |
| prefix | `ip ` + contains `is not in role's cidr_list` | BV-SSH-003 |
| exact | `invalid or expired otp` | BV-SSH-004 |
| prefix | `role has pqc_only=true but the ca is classical` | BV-SSH-006 |
| contains | `valid_principals` + `not allowed by role` | BV-AUTHZ-004 |
| exact | `login_class_locked` | BV-AUTHZ-005 |
| exact | `brokered_resource_no_static_credential` | BV-CONFLICT-003 |
| prefix | `unknown totp key` | BV-TOTP-001 |
| prefix | `this key is provider-mode` / `this key is generate-mode` | BV-TOTP-002 |
| prefix | `cert-lifecycle: target` + contains `not found` | BV-NOTFOUND-008 |
| exact | `authority_pending_approval` / `authority_tombstoned` / `attestation_mismatch` / `unknown_authority` / `signature_invalid` / `envelope_replay` / `policy_denied` | BV-RUSTION-001…007 respectively |
| contains (409, recordings) | `sha256` / `digest` | BV-CONFLICT-002 |
| contains (500) | `hmac verification failed` | BV-INPUT-103 |

Unmatched messages fall to the status table in [04](04-error-model.md#mapping-algorithm-server-response--code).

## 3. Invariants tested by every SDK

- Every code above exists in the catalog with the message and hint given (whitespace-
  normalised comparison).
- No code outside this appendix exists in the catalog (new codes require a spec change).
- `Retryable` matches column **R** (absent column ⇒ `no`).
- Every recognition row produces its code for at least one fixture in Appendix C.
