# 12 — Other Engines and Identity

**Complete** conformance level unless noted. Each area is a sub-client on `Client`. Where
this section lists fields only, request/response shapes follow the generic Shape A
envelope and standard error mapping; the exact paths are in
[Appendix A](appendix-a-endpoint-catalogue.md).

## Identity (`identity/` mount, kernel)

| Operation | HTTP | Notes |
|-----------|------|-------|
| `Identity.Self()` → `EntitySelf` | `GET identity/entity/self` | `{entity_id, username, mount_path, role_name, primary_mount?, primary_name?, created_at?, aliases[]?}`; lazily provisions the entity |
| `Identity.Aliases()` | `GET identity/entity/aliases` | |
| `Identity.Groups.List(kind)` / `Read(kind, name)` / `Write(kind, name, {description, members[], policies[]})` / `Delete` / `History` | `LIST/GET/PUT/DELETE identity/group/{user|app}/{name}[/history]` | `kind ∈ user | app` |
| `Identity.Sharing.Get/Put/Delete(kind, target, grantee, spec)` | `GET/PUT/DELETE identity/sharing/by-target/{kind}/{target}/{grantee}` | `target` = **base64url (no padding)** of the canonical target path |
| `Identity.Sharing.ListByTarget(kind, target)` / `ListByGrantee(grantee)` / `ForMe()` | `LIST identity/sharing/by-target/{kind}/{target}`, `LIST …/by-grantee/{g}`, `LIST …/for-me` | |
| `Identity.Owner.*` | `identity/owner/{kv|file|resource}/…` | ownership records |

- **IDN-001** The SDK MUST perform the base64url encoding of `target` itself; callers
  pass the plain path (`secret/app/db`). `target_kind ∈ kv-secret | resource |
  asset-group | file`; `grantee_kind ∈ entity (default) | group_user | group_app`;
  `capabilities[]`; `expires_at` RFC 3339.
- **IDN-002** `ForMe` returns `{entity_id, group_shared_resources, entries[]}`; group
  shares appear only when a policy carries `metadata.group_shared_resources = "true"` —
  the SDK MUST document that.

## Asset groups (`resource-group/` mount)

| Operation | HTTP |
|-----------|------|
| `AssetGroups.List()` / `Read(name)` / `Write(name, spec)` / `Delete(name)` / `History(name)` | `LIST/GET/PUT/DELETE resource-group/groups/{name}[/history]` |
| `AssetGroups.ByResource(name)` / `BySecret(path)` | `GET resource-group/by-resource/{name}`, `GET resource-group/by-secret/{base64url(path)}` |
| `AssetGroups.Reindex()` | `PUT resource-group/reindex` |

## Resources (`resource`, default mount `resources/`)

| Operation | HTTP | Notes |
|-----------|------|-------|
| `Resources.ReadTypes()` / `WriteTypes(schema)` | `GET/POST {mount}/config/types` | |
| `Resources.List()` | `LIST {mount}/resources/` | |
| `Resources.Search({q?, type?, offset?, limit?})` | `POST {mount}/resources/search` | body, not query |
| `Resources.Read(name)` / `Write(name, record)` / `Delete(name)` / `History(name)` | `{mount}/resources/{name}[/history]` | |
| `Resources.Rename(name, newName)` | `POST {mount}/resources/{name}/rename` `{new_name}` | migrates secrets, shares, groups, ownership |
| `Resources.Secrets.List(resource)` / `Read(resource, key)` / `Write` / `Delete` / `History` / `ReadVersion(resource, key, n)` | `{mount}/secrets/{resource}/{key}[/history|/version/{n}]` | |
| `Resources.Connect.MfaBegin({resource, profile_id})` → factors | `POST {mount}/v2/connect/mfa/begin` | |
| `Resources.Connect.MfaVerify({resource, profile_id, method = totp|fido2, totp_code?, credential?})` → `{connect_ticket}` | `POST {mount}/v2/connect/mfa/verify` | ticket is single-use, redacting |
| `Resources.Connect.Authorize({resource, profile_id, connect_ticket?})` | `POST {mount}/v2/connect/authorize` | |

- **RSC-001** Connect-MFA errors: `connect MFA requires an authenticated caller` /
  `no authenticated caller` (401) → `BV-AUTH-002`; `second-factor verification failed`
  (403) → `BV-AUTH-016 SecondFactorFailed`; `` `resource` is required `` (400) →
  `BV-INPUT-001`.
- **RSC-002** `Secrets.Read` values MUST be redacting; `Resources.Read` records are not.

## Files (`files`, default mount `files/`)

| Operation | HTTP | Notes |
|-----------|------|-------|
| `Files.List()` | `LIST {mount}/files/` | |
| `Files.Create({name, resource?, mime_type?, tags[], notes?, content})` → `{Id}` | `POST {mount}/files/` | `content` → `content_base64` |
| `Files.Read(id)` / `Update(id, spec)` / `Delete(id)` | `{mount}/files/{id}` | |
| `Files.Content(id)` → `bytes` | `GET {mount}/files/{id}/content` | base64 decoded |
| `Files.History(id)` / `Versions(id)` / `ReadVersion(id, n)` / `VersionContent(id, n)` / `RestoreVersion(id, n)` | `{mount}/files/{id}/history`, `/versions[/{n}[/content|/restore]]` | |
| `Files.RepointResource(old, new)` | `POST {mount}/files/repoint-resource` | |
| `Files.Sync.List(id)` / `Write(id, name, SyncTarget)` / `Delete` / `Push(id, name)` / `Tick()` | `{mount}/files/{id}/sync[/{name}[/push]]`, `POST {mount}/sync-tick` | `SyncTarget.kind ∈ local-fs | smb`; credential fields write-only |

- **FIL-001** Content MUST be handled as bytes with base64 done by the SDK; the SDK MUST
  enforce the 32 MiB body limit after encoding (TRN-032).

## LDAP / Active Directory (`openldap`)

| Operation | HTTP |
|-----------|------|
| `Ldap.ReadConfig()` / `WriteConfig(LdapConfig)` / `DeleteConfig()` | `{mount}/config` — `url, binddn, bindpass (write-only), userdn, directory_type (openldap|active_directory), password_policy, request_timeout (10), starttls, client_tls_cert, client_tls_key (write-only), tls_min_version (tls12|tls13), insecure_tls, acknowledge_insecure_tls, userattr (cn)` |
| `Ldap.RotateRoot()` | `POST {mount}/rotate-root` |
| `Ldap.CheckConnection()` → `{Ok, Stage, Error?, Url, BindDn, Host, Port, Scheme, LatencyMs}` | `GET {mount}/check-connection` |
| `Ldap.StaticRoles.List/Read/Write/Delete(name, {dn, username, rotation_period, password_policy})` | `{mount}/static-role/{name}` |
| `Ldap.StaticCred(name)` → `{Username, Dn, Password, LastRotated?, TtlSecs?}` | `GET {mount}/static-cred/{name}` |
| `Ldap.RotateRole(name)` | `POST {mount}/rotate-role/{name}` |
| `Ldap.Library.List/Read/Write/Delete(set, {service_account_names[], ttl (3600), max_ttl (86400), disable_check_in_enforcement, affinity_ttl})` | `{mount}/library/{set}` |
| `Ldap.Library.CheckOut(set, ttl?)` → `{ServiceAccountName, Password, LeaseId, TtlSecs}` | `POST {mount}/library/{set}/check-out` |
| `Ldap.Library.CheckIn(set, account?)` | `POST {mount}/library/{set}/check-in` |
| `Ldap.Library.Status(set)` → `{CheckedOut{}, Available[]}` | `GET {mount}/library/{set}/status` |

- **LDP-001** `insecure_tls = true` MUST require `acknowledge_insecure_tls = true`
  client-side (`BV-INPUT-001`) mirroring the server.

## Cert lifecycle (`cert-lifecycle`)

| Operation | HTTP |
|-----------|------|
| `CertLifecycle.ListTargets()` / `ListTargetsInfo(after, limit)` → `Page<Target>` | `LIST {mount}/targets/`, `GET {mount}/targets-info` |
| `CertLifecycle.ReadTarget/WriteTarget/DeleteTarget(name, {kind = file, address, pki_mount = pki, role_ref, common_name, alt_names[], ip_sans[], ttl, key_policy = rotate|reuse|agent-generates, key_ref, renew_before = 168h})` | `{mount}/targets/{name}` |
| `CertLifecycle.State(name)` → `{CurrentSerial, CurrentNotAfter, LastRenewal, LastAttempt, LastError, NextAttempt, FailureCount}` | `GET {mount}/state/{name}` |
| `CertLifecycle.Renew(name)` | `POST {mount}/renew/{name}` — `cert-lifecycle: target `x` not found` → `BV-NOTFOUND-008` |
| `CertLifecycle.ReadSchedulerConfig/WriteSchedulerConfig({enabled, tick_interval_seconds ≥ 30, client_token (write-only), base_backoff_seconds, max_backoff_seconds})` | `{mount}/scheduler/config` — read returns `client_token_set` |
| `CertLifecycle.Deliverers()` | `GET {mount}/sys/deliverers` |

## Notifications (`notifications`)

| Operation | HTTP |
|-----------|------|
| `Notifications.Send({title (req), body, severity = info|success|warning|critical, channels[], action_url, target{}, metadata{}})` | `POST {mount}/send` |
| `Notifications.Inbox.List()` / `UnreadCount()` / `MarkRead(id)` / `ReadAll()` / `Dismiss(id)` | `{mount}/inbox[/unread-count|/read-all|/{id}/read|/{id}]` |
| `Notifications.Channels.List()` / `Test(channel, to)` | `{mount}/channels[/{channel}/test]` |
| `Notifications.Sent()` | `{mount}/sent/` |
| `Notifications.ReadConfig/WriteConfig({inbox_cap, plugin_rate_per_min})` | `{mount}/config` |

## Rustion (bastion integration, `rustion`)

Large surface; the SDK MUST type the operator-facing subset below and expose the rest via
`Logical.*` (paths in Appendix A).

| Operation | HTTP | Notes |
|-----------|------|-------|
| `Rustion.Targets.List/Create/Read/Write/Delete/Probe/ProbeAll/Health/RefreshListeners` | `{mount}/targets[/{id}[/probe|/listeners/refresh]]`, `targets/health`, `targets/probe` | |
| `Rustion.Master.ReadConfig/WriteConfig/PubKey/Issue/Rotate` | `{mount}/master/{config|pubkey|issue|rotate}` | |
| `Rustion.DeploymentId()` | `GET {mount}/deployment-id` | |
| `Rustion.Session.Open(SessionRequest)` | `POST {mount}/session/open` | raw `credential_material` (v1) |
| `Rustion.Session.OpenConnectOnly({resource_name, credential_source{kind = secret, secret_id}, target_host, target_port, target_protocol, profile_id?, connect_ticket?})` | `POST /v2/rustion/session/open` | **v2 pinned**; server resolves the credential |
| `Rustion.Session.Renew({bastion_id, session_id, correlation_id, extend_secs = 1800})` / `Kill(...)` | `{mount}/session/{renew|kill}` | |
| `Rustion.Recordings.List()` / `Read(rid)` / `Blob(rid)` / `Chunk(rid, n)` / `Keystrokes(rid)` | `{mount}/recordings[/{rid}[/blob[/chunk/{n}]|/keystrokes]]` | `rid` matches `rec_[A-Za-z0-9_-]+` |
| `Rustion.Recordings.Download(rid)` → `bytes` | chunk loop | see RUS-001 |
| `Rustion.Recordings.Pull/Reconcile/ReplayLog/IndexKeystrokes/KeystrokeSearch({query, limit})` | `POST {mount}/recordings/…` | search query goes in the **body** |
| `Rustion.Policy.*`, `Rustion.BastionGroups.*`, `Rustion.Dispatcher.Preview`, `Rustion.Telemetry.*` | Appendix A | |

- **RUS-001** `Download` MUST read chunk 0, then chunks until `eof == true`, concatenate
  `bytes_b64` payloads, and verify SHA-256 of the assembled bytes against `sha256` when
  `digest_verified` is true or a digest is present; mismatch → `BV-PROTOCOL-004
  DigestMismatch`. `416` (index past end) → `BV-INPUT-008` with `Details.chunk_count`.
  A `409` naming two digests → `BV-CONFLICT-002 RecordingDigestMismatch` (non-retryable).
  When the chunk route is missing (`BV-SERVER-004`) the SDK MUST fall back to `/blob` and
  MUST raise `BV-TRANSPORT-004` if it exceeds `MaxResponseBytes`.
- **RUS-002** Chunk/blob downloads are node-local (DSC-045): no failover.
- **RUS-003** Rustion error tokens `authority_pending_approval`, `authority_tombstoned`,
  `attestation_mismatch` (403), `unknown_authority`, `signature_invalid` (401),
  `envelope_replay` (409), `policy_denied` MUST map to `BV-RUSTION-001…007` (Appendix B).

## Cubbyhole — not available

⚠️ No `cubbyhole/` mount exists on the server (SYS-100). The SDK MUST NOT expose one.
