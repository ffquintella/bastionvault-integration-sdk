# Appendix A — Endpoint Catalogue

Every endpoint the SDK exposes, grouped by area. **Prefix** `v1` means the operation uses
`ApiPrefix` (both `/v1` and `/v2` serve it); `v2` means the SDK MUST pin `/v2`. **Level**
is the conformance level (C = Core, S = Standard, X = Complete). **Auth** `no` marks
endpoints callable without a token. Paths are logical (relative to the prefix); `{mount}`
defaults are shown in the area heading. Verbs: `R` = GET, `W` = POST/PUT, `D` = DELETE,
`L` = LIST.

## System (`sys/`)

| Canonical operation | Verb | Path | Prefix | Level | Auth | Notes |
|---|---|---|---|---|---|---|
| `Sys.Health` | R | `sys/health` | v1 | C | no | status by code + body |
| `Sys.SealStatus` | R | `sys/seal-status` | v1 | C | no | `t`/`n` swapped |
| `Sys.InitStatus` | R | `sys/init` | v1 | C | no | |
| `Sys.Init` | W | `sys/init` | v1 | C | no | |
| `Sys.Seal` | W | `sys/seal` | v1 | S | sudo | 204 |
| `Sys.Unseal` | W | `sys/unseal` | v1 | C | no | idempotent |
| `Sys.ServerInfo` | R | `sys/info` | v1 | C | tiered | |
| `Sys.ClusterStatus` | R | `sys/cluster-status` | v1 | S | token or cluster-local | |
| `Sys.Cluster.RemoveNode/Leave/Failover` | W | `sys/cluster/{remove-node,leave,failover}` | v1 | X | yes | 204; hiqlite only |
| `Sys.HsmStatus` | R | `sys/hsm/status` | **v2** | S | yes | |
| `Sys.ListMounts` | R | `sys/mounts` | v1 | C | yes | two-field entries |
| `Sys.Mount` / `Sys.Unmount` | W / D | `sys/mounts/{path}` | v1 | C | yes | 204 |
| `Sys.Remount` | W | `sys/remount` | v1 | S | yes | 409 on conflict |
| `Sys.ListMountsDetailed` | R | `sys/internal/ui/mounts[/{path}]` | v1 | S | yes | ACL-filtered |
| `Sys.ListAuthMethods` | R | `sys/auth` | v1 | C | yes | |
| `Sys.EnableAuthMethod` / `DisableAuthMethod` | W / D | `sys/auth/{path}` | v1 | C | yes | |
| `Sys.ListPolicies` | R | `sys/policies/acl` | v1 | C | yes | `{keys}` |
| `Sys.ReadPolicy` / `WritePolicy` / `DeletePolicy` | R / W / D | `sys/policies/acl/{name}` | v1 | C | yes | `{name, policy}` |
| `Sys.PolicyHistory` | R | `sys/policies/acl/{name}/history` | v1 | S | yes | |
| `Sys.Legacy.ListPolicies` / `ReadPolicy` … | R/W/D | `sys/policy[/{name}]` | v1 | X | yes | `rules` field |
| `Sys.TestPolicy` | W | `sys/policies/acl/test` | **v2** | S | yes | dry-run |
| `Sys.ReadPolicyTests` / `WritePolicyTests` | R / W | `sys/policy-tests/{name}` | **v2** | S | yes | |
| `Sys.CapabilitiesSelf` | W | `sys/capabilities-self` | **v2** | C | yes | |
| `Sys.ListNamespaces` | L | `sys/namespaces` | v1 | S | yes | |
| `Sys.ReadNamespace` / `WriteNamespace` / `DeleteNamespace` | R / W / D | `sys/namespaces/{path}` | v1 | S | yes | write = full replace |
| `Sys.NamespacesSelf` | R | `sys/namespaces-self` | v1 | S | yes | |
| `Sys.ListNamespacesInfo` | R | `sys/namespaces-info?after&limit` | v1 | S | yes | paged |
| `Sys.NamespaceLinks.*` | R/L/W/D | `sys/namespace-links[/{id}]` | v1 | X | yes | |
| `Sys.Batch` | W | `sys/batch` | **v2** | C | yes | |
| `Sys.CacheVersion` | R | `sys/cache/version?topics&watch` | **v2** | S | yes | ETag/304 |
| `Sys.CacheFlush` | W | `sys/cache/flush` | v1 | X | yes | |
| `Sys.Dos.ReadConfig` / `WriteConfig` | R / W | `sys/dos/config` | **v2** | X | root | |
| `Sys.Dos.Stats` | R | `sys/dos/stats` | **v2** | X | root | |
| `Sys.Dos.Ban` / `Unban` | W / D | `sys/dos/bans/{ip}` | **v2** | X | root | |
| `Sys.Audit.ListDevices` | R | `sys/audit` | v1 | X | yes | |
| `Sys.Audit.EnableDevice` / `DisableDevice` | W / D | `sys/audit/{path}` | v1 | X | yes | |
| `Sys.Audit.Events` | R | `sys/audit/events?from&to&limit` | v1 | X | yes | |
| `Sys.DashboardSummary` | R | `sys/dashboard/summary` | v1 | X | yes | |
| `Sys.Backup` / `Sys.Restore` | W / W | `sys/backup`, `sys/restore` | v1 | X | yes | octet-stream |
| `Sys.Export` / `Sys.Import` | R / W | `sys/export/{path}`, `sys/import/{mount}` | v1 | X | yes | |
| `Sys.Exchange.Export/Import/ImportPreview/ImportApply` | W | `sys/exchange/{export,import,import/preview,import/apply}` | v1 | X | yes | |
| `Sys.OwnerTransfer.Kv/Resource/AssetGroup/File` | W | `sys/{kv,resource,asset-group,file}-owner/transfer` | v1 | X | yes | |
| `Sys.KvOwnerClaim` / `OwnerBackfill` | W | `sys/kv-owner/claim`, `sys/owner/backfill` | v1 | X | yes | |
| `Sys.SsoSettings` / `SsoProviders` | R | `sys/sso/{settings,providers}` | v1 | X | yes | |
| `Sys.Raw.*` | R/W/D | `sys/raw/{path}` | v1 | X | root | discouraged |
| `Sys.Plugins.*` | R/W/D/L | `sys/plugins/…`, `sys/plugins/active-surfaces[?watch=1]`, `sys/plugins/{plugin}/versions/{version}/asset/{sha256}` | v1 | X | yes | asset SHA-256 verified |
| `Sys.ScheduledExports.*` | R/W/D | `sys/scheduled-exports/…` | v1 | X | yes | |
| `Identity.Profile.Read` | R | `sys/identity/profile/self` | **v2** | X | yes | |
| `Identity.Profile.ChangePassword` / `UpdateContact` | W | `sys/identity/profile/self/{password,contact}` | **v2** | X | yes | |
| `Identity.DefaultAccount.ReadSelf` / `WriteSelf` | R / W | `sys/identity/default-account/self` | **v2** | X | yes | |
| `Identity.DefaultAccount.List/Read/Write` | L/R/W | `sys/identity/default-account[/{mount}/{name}]` | **v2** | X | yes | |
| `Identity.SshSecurityKey.*` | L/R/W/D | `sys/identity/ssh-security-key[/self|/{mount}/{name}]` | **v2** | X | yes | |
| `Identity.NamespaceAssignment.*` | L/R/W/D | `sys/identity/ns-assignment[/{mount}/{name}]` | v1 | X | yes | |

## Token store (`auth/token/`)

| Canonical operation | Verb | Path | Prefix | Level | Notes |
|---|---|---|---|---|---|
| `Auth.Token.Create` | W | `auth/token/create` | v1 | C | |
| `Auth.Token.Lookup` | R | `auth/token/lookup/{token}` | v1 | C | redact path |
| `Auth.Token.LookupSelf` | R | `auth/token/lookup-self` | v1 | C | |
| `Auth.Token.Renew` / `RenewSelf` | W | `auth/token/renew/{token}` | v1 | C | `{increment}` required |
| `Auth.Token.Revoke` | W | `auth/token/revoke/{token}` | v1 | C | 204 |
| `Auth.Token.RevokeOrphan` | W | `auth/token/revoke-orphan/{token}` | v1 | S | sudo |
| `Auth.Token.RevokeSelf` | W | `auth/token/revoke-self` | v1 | C | 204 |
| `Auth.Token.AuditLogin` | W | `auth/token/audit-login` | v1 | S | 204 |

## Userpass (`auth/{mount}` = `auth/userpass`)

| Canonical operation | Verb | Path | Level | Auth | Notes |
|---|---|---|---|---|---|
| `Auth.Userpass.Login` | W | `auth/{mount}/login/{username}` | C | no | 200+`data.error` on failure |
| `Auth.Userpass.Admin.ListUsers` | L | `auth/{mount}/users/` | S | yes | |
| `Auth.Userpass.Admin.ListUsersInfo` | R | `auth/{mount}/users-info?after&limit` | S | yes | v2 recommended |
| `Auth.Userpass.Admin.ReadUser/WriteUser/DeleteUser` | R/W/D | `auth/{mount}/users/{username}` | S | yes | |
| `Auth.Userpass.Admin.SetPassword` | W | `auth/{mount}/users/{username}/password` | S | yes | |
| `Auth.Userpass.Admin.Unlock` | W | `auth/{mount}/users/{username}/unlock` | S | yes | |
| `Auth.Userpass.Admin.ReadFido2/DeleteFido2` | R/D | `auth/{mount}/users/{username}/fido2` | X | yes | |
| `Auth.Userpass.Admin.ReadLockout/WriteLockout` | R/W | `auth/{mount}/config/lockout` | S | yes | |
| `Auth.Userpass.Admin.ReadMfa/WriteMfa` | R/W | `auth/{mount}/config/mfa` | S | yes | |
| `Auth.Userpass.Fido2Config` (R/W) | R/W | `auth/{mount}/fido2/config` | X | no (read) | |
| `Auth.Userpass.Fido2RegisterBegin/Complete` | W | `auth/{mount}/fido2/register/{begin,complete}` | X | yes | |
| `Auth.Userpass.Fido2LoginBegin/Complete` | W | `auth/{mount}/fido2/login/{begin,complete}` | X | no | |
| `Auth.Userpass.StepUpBegin/Verify` | W | `auth/{mount}/v2/step-up/{begin,verify}` | X | yes | |

## AppID (`auth/{mount}` = `auth/approle`)

| Canonical operation | Verb | Path | Level | Auth |
|---|---|---|---|---|
| `Auth.AppId.Login` | W | `auth/{mount}/login` | C | no |
| `Auth.AppId.ReadRoleId` / `WriteRoleId` | R/W | `auth/{mount}/role/{name}/role-id` | C/S | yes |
| `Auth.AppId.GenerateSecretId` | W | `auth/{mount}/role/{name}/secret-id` | C | yes |
| `Auth.AppId.Admin.ListRoles` | L | `auth/{mount}/role/` | S | yes |
| `Auth.AppId.Admin.ReadRole/WriteRole/DeleteRole` | R/W/D | `auth/{mount}/role/{name}` | S | yes |
| `Auth.AppId.Admin.<Field>` (R/W/D) | R/W/D | `auth/{mount}/role/{name}/{policies,bound-cidr-list,secret-id-bound-cidrs,bound-source-ips,bypass-machine-binding,token-bound-cidrs,bind-secret-id,secret-id-num-uses,secret-id-ttl,period,token-num-uses,token-ttl,token-max-ttl}` | X | yes |
| `Auth.AppId.Admin.ReadLocalSecretIds` | R | `auth/{mount}/role/{name}/local-secret-ids` | X | yes |
| `Auth.AppId.Admin.ListSecretIdAccessors` | L | `auth/{mount}/role/{name}/secret-id/` | S | yes |
| `Auth.AppId.Admin.LookupSecretId/DestroySecretId` | W / W,D | `auth/{mount}/role/{name}/secret-id/{lookup,destroy}` | S | yes |
| `Auth.AppId.Admin.LookupSecretIdAccessor/DestroySecretIdAccessor` | W / W,D | `auth/{mount}/role/{name}/secret-id-accessor/{lookup,destroy}` | S | yes |
| `Auth.AppId.Admin.CustomSecretId` | W | `auth/{mount}/role/{name}/custom-secret-id` | X | yes |
| `Auth.AppId.Admin.ListMachines/BindMachine` | L/W | `auth/{mount}/role/{name}/machine/` | X | yes |
| `Auth.AppId.Admin.ReadMachine/UnbindMachine` | R/D | `auth/{mount}/role/{name}/machine/{machine_id}` | X | yes |
| `Auth.AppId.Admin.ReadConfig/WriteConfig` | R/W | `auth/{mount}/config` (`require_machine`) | S | yes |
| `Auth.AppId.Admin.TidySecretIds` | W | `auth/{mount}/tidy/secret-id` | X | yes |

## FerroGate (`auth/{mount}` = `auth/ferrogate`)

| Canonical operation | Verb | Path | Level | Auth |
|---|---|---|---|---|
| `Auth.Ferrogate.Requirement` | R | `auth/{mount}/requirement` | S | no |
| `Auth.Ferrogate.Login` | W | `auth/{mount}/login` | S | no |
| `Auth.Ferrogate.Status` | W | `auth/{mount}/status` | S | no |
| `Auth.Ferrogate.Enroll` | W | `auth/{mount}/enroll` | S | no |
| `Auth.Ferrogate.Admin.ReadConfig/WriteConfig` | R/W | `auth/{mount}/config` | X | root |
| `Auth.Ferrogate.Admin.Register` | W | `auth/{mount}/register` | X | root |
| `Auth.Ferrogate.Admin.ListMachines` | L | `auth/{mount}/machines/` | X | root |
| `Auth.Ferrogate.Admin.ReadMachine/DeleteMachine` | R/D | `auth/{mount}/machines/{id}` | X | root |
| `Auth.Ferrogate.Admin.Approve/Reject/Revoke` | W | `auth/{mount}/machines/{id}/{approve,reject,revoke}` | X | root |

## FIDO2 standalone (`auth/fido2`), OIDC (`auth/oidc`), SAML (`auth/saml`), Cert (`auth/cert`)

| Canonical operation | Verb | Path | Level | Auth |
|---|---|---|---|---|
| `Auth.Fido2.Config` (R/W) | R/W | `auth/{mount}/config` | X | yes |
| `Auth.Fido2.Credentials.List/Read/Write/Delete` | L/R/W/D | `auth/{mount}/credentials[/{username}]` | X | yes |
| `Auth.Fido2.RegisterBegin/Complete` | W | `auth/{mount}/register/{begin,complete}` | X | yes |
| `Auth.Fido2.LoginBegin/Complete` | W | `auth/{mount}/login/{begin,complete}` | X | no |
| `Auth.Oidc.AuthUrl` / `Callback` | W | `auth/{mount}/auth_url`, `auth/{mount}/callback` | S | no |
| `Auth.Oidc.Admin.Config` (R/W), `Roles.List/Read/Write/Delete` | R/W/L/D | `auth/{mount}/config`, `auth/{mount}/role[/{name}]` | X | yes |
| `Auth.Saml.Login` / `Callback` | W | `auth/{mount}/login`, `auth/{mount}/callback` | S | no |
| `Auth.Saml.Admin.Config` (R/W), `Roles.*` | R/W/L/D | `auth/{mount}/config`, `auth/{mount}/role[/{name}]` | X | yes |
| `Auth.Cert.Login` | W | `auth/{mount}/login` | X | no | **disabled on server** → `BV-SERVER-004` |

## Identity, sharing, asset groups

| Canonical operation | Verb | Path | Level |
|---|---|---|---|
| `Identity.Self` | R | `identity/entity/self` | S |
| `Identity.Aliases` | R | `identity/entity/aliases` | X |
| `Identity.Groups.List/Read/Write/Delete/History` | L/R/W/D/R | `identity/group/{user,app}/{name}[/history]` | X |
| `Identity.Sharing.Get/Put/Delete` | R/W/D | `identity/sharing/by-target/{kind}/{b64url target}/{grantee}` | X |
| `Identity.Sharing.ListByTarget/ListByGrantee/ForMe` | L | `identity/sharing/by-target/{kind}/{target}`, `…/by-grantee/{g}`, `…/for-me` | X |
| `Identity.Owner.*` | R/W/D | `identity/owner/{kv,file,resource}/…` | X |
| `AssetGroups.List/Read/Write/Delete/History` | L/R/W/D/R | `resource-group/groups/{name}[/history]` | X |
| `AssetGroups.ByResource/BySecret` | R | `resource-group/by-resource/{name}`, `resource-group/by-secret/{b64url}` | X |
| `AssetGroups.Reindex` | W | `resource-group/reindex` | X |

## KV (`{mount}` = `secret`)

| Canonical operation | Verb | Path | Level |
|---|---|---|---|
| `Kv.V1.Read/Write/Delete` | R/W/D | `{mount}/{path}` | C |
| `Kv.V1.List` | L | `{mount}/{prefix}/` | C |
| `Kv.V2.ReadConfig/WriteConfig` | R/W | `{mount}/config` | C |
| `Kv.V2.ReadSecret/WriteSecret/SoftDelete` | R/W/D | `{mount}/data/{path}[?version&env]` | C |
| `Kv.V2.List` | L | `{mount}/metadata/[{prefix}/]` | C |
| `Kv.V2.ReadMetadata/DeleteMetadata` | R/D | `{mount}/metadata/{path}` | C |
| `Kv.V2.Destroy` | W | `{mount}/destroy/{path}` | C |
| `Kv.V2.Undelete` | W | `{mount}/undelete/{path}` | C |

## Transit (`{mount}` = `transit`) — Level S

| Canonical operation | Verb | Path |
|---|---|---|
| `Transit.ListKeys` | L | `{mount}/keys/` |
| `Transit.CreateKey/ReadKey/DeleteKey` | W/R/D | `{mount}/keys/{name}` |
| `Transit.RotateKey` / `ConfigureKey` / `TrimKey` | W | `{mount}/keys/{name}/{rotate,config,trim}` |
| `Transit.Encrypt/Decrypt/Rewrap` | W | `{mount}/{encrypt,decrypt,rewrap}/{name}` |
| `Transit.Sign/Verify` | W | `{mount}/{sign,verify}/{name}` |
| `Transit.Hmac/VerifyHmac` | W | `{mount}/hmac/{name}`, `{mount}/verify/{name}/hmac` |
| `Transit.GenerateDataKey/UnwrapDataKey` | W | `{mount}/datakey/{plaintext,wrapped}/{name}`, `{mount}/datakey/unwrap/{name}` |
| `Transit.Random` / `Hash` | W | `{mount}/random`, `{mount}/hash` |
| `Transit.Byok.WrappingKey/Import/ImportVersion` | R/W/W | `{mount}/wrapping_key`, `{mount}/keys/{name}/{import,import_version}` (feature-gated) |

## PKI (`{mount}` = `pki`) — Level X

| Canonical operation | Verb | Path |
|---|---|---|
| `Pki.ListRoles` / `ReadRole/WriteRole/DeleteRole` | L / R/W/D | `{mount}/roles/`, `{mount}/roles/{name}` |
| `Pki.Issue` / `Sign` / `SignVerbatim` | W | `{mount}/issue/{role}`, `{mount}/sign/{role}`, `{mount}/sign-verbatim` |
| `Pki.GenerateRoot` | W | `{mount}/root/generate/{internal,exported}` |
| `Pki.SignIntermediate` | W | `{mount}/root/sign-intermediate` |
| `Pki.GenerateIntermediate` / `SetSignedIntermediate` | W | `{mount}/intermediate/generate/{internal,exported}`, `{mount}/intermediate/set-signed` |
| `Pki.ConfigureCa` | W | `{mount}/config/ca` |
| `Pki.ReadUrls/WriteUrls`, `ReadCrlConfig/WriteCrlConfig`, `ReadIssuersConfig/WriteIssuersConfig` | R/W | `{mount}/config/{urls,crl,issuers}` |
| `Pki.ListIssuers` / `ReadIssuer/WriteIssuer/DeleteIssuer` | L / R/W/D | `{mount}/issuers/`, `{mount}/issuer/{ref}` |
| `Pki.IssuerChain` / `ExportIssuer` / `ReadIssuerCrl` | R / R,W / R | `{mount}/issuer/{ref}/{chain,export,crl[/pem]}` |
| `Pki.ReadCa` / `ReadCaChain` | R | `{mount}/ca[/pem]`, `{mount}/ca_chain` |
| `Pki.ListCertificates` / `ListCertificatesInfo` | L / R | `{mount}/certs/`, `{mount}/certs-info?after&limit` |
| `Pki.ImportCertificate` | W | `{mount}/certs/import` |
| `Pki.ReadCertificate/DeleteCertificate` | R/D | `{mount}/cert/{serial}` |
| `Pki.AttachKey/DetachKey` | W/D | `{mount}/cert/{serial}/key` |
| `Pki.ExportCertificate` | R,W | `{mount}/cert/{serial}/export` |
| `Pki.Revoke` | W | `{mount}/revoke` |
| `Pki.ReadCrl` / `RotateCrl` | R / W | `{mount}/crl[/pem]`, `{mount}/crl/rotate` |
| `Pki.ListKeys` / `GenerateKey` / `ImportKey` / `ReadKey/DeleteKey` | L / W / W / R,D | `{mount}/keys/`, `{mount}/keys/generate/{internal,exported}`, `{mount}/keys/import`, `{mount}/key/{ref}` |
| `Pki.Tidy` / `TidyStatus` / `ReadAutoTidy/WriteAutoTidy` | W / R / R,W | `{mount}/tidy`, `{mount}/tidy-status`, `{mount}/config/auto-tidy` |
| `Pki.Csr.Generate/List/ListInfo/Read/Delete/SetSigned` | W/L/R/R/D/W | `{mount}/csr/generate`, `{mount}/csr/`, `{mount}/csr-info`, `{mount}/csr/{id}[/set-signed]` |
| `Pki.SignRequests.Import/List/ListInfo/Read/Delete/Preflight/Approve/ApproveVerbatim/Reject` | W/L/R/R/D/W/W/W/W | `{mount}/sign-request/import`, `{mount}/sign-request/`, `{mount}/sign-request-info`, `{mount}/sign-request/{id}[/preflight|/approve|/approve-verbatim|/reject]` |
| `Pki.Acme.ReadConfig/WriteConfig/DeleteConfig` / `DirectoryUrl` | R/W/D | `{mount}/acme/config` (protocol paths `{mount}/acme/*` not wrapped) |

## SSH (`{mount}` = `ssh`) and SSH broker — Level X

| Canonical operation | Verb | Path | Prefix |
|---|---|---|---|
| `Ssh.ConfigureCa/ReadCa/DeleteCa` | W/R/D | `{mount}/config/ca` | v1 |
| `Ssh.PublicKey` | R | `{mount}/public_key` | v1 |
| `Ssh.ListRoles` / `ListRolesInfo` | L / R | `{mount}/roles/`, `{mount}/roles-info` | v1 |
| `Ssh.ReadRole/WriteRole/DeleteRole` | R/W/D | `{mount}/roles/{name}` | v1 |
| `Ssh.Sign` | W | `{mount}/sign/{role}` | v1 |
| `Ssh.Creds` / `Verify` / `Lookup` | W | `{mount}/creds/{role}`, `{mount}/verify`, `{mount}/lookup` | v1 |
| `SshBroker.ReadGlobal/WriteGlobal` | R/W | `ssh-broker/policy/global` | **v2** |
| `SshBroker.*Type/AssetGroup/Resource` | R/W/D | `ssh-broker/policy/{type,asset-group,resource}/{id}` | **v2** |
| `SshBroker.Effective` | W | `ssh-broker/policy/effective` | **v2** |

## TOTP (`{mount}` = `totp`) — Level S

| Canonical operation | Verb | Path |
|---|---|---|
| `Totp.ListKeys` | L | `{mount}/keys/` |
| `Totp.CreateKey/ReadKey/DeleteKey` | W/R/D | `{mount}/keys/{name}` |
| `Totp.GenerateCode` / `ValidateCode` | R / W | `{mount}/code/{name}` |

## LDAP (`openldap`), Files (`files`), Resources (`resources`), Cert lifecycle, Notifications, Rustion — Level X

| Area | Paths (see [12](12-other-engines-and-identity.md)) |
|---|---|
| LDAP | `{mount}/config`, `rotate-root`, `check-connection`, `static-role/[{name}]`, `static-cred/{name}`, `rotate-role/{name}`, `library/[{set}[/check-out|/check-in|/status]]` |
| Files | `{mount}/files/`, `files/{id}`, `files/{id}/content`, `files/{id}/history`, `files/{id}/versions[/{n}[/content|/restore]]`, `files/{id}/sync[/{name}[/push]]`, `files/repoint-resource`, `sync-tick` |
| Resources | `{mount}/config/types`, `resources/`, `resources/search`, `resources/{name}[/history|/rename]`, `secrets/{resource}/[{key}[/history|/version/{n}]]`, `v2/connect/mfa/{begin,verify}`, `v2/connect/authorize` |
| Cert lifecycle | `{mount}/targets/`, `targets-info`, `targets/{name}`, `state/{name}`, `renew/{name}`, `scheduler/config`, `sys/deliverers` |
| Notifications | `{mount}/send`, `inbox[/unread-count|/read-all|/{id}/read|/{id}]`, `channels[/{channel}/test]`, `sent/`, `config` |
| Rustion | `{mount}/targets[/health|/probe|/{id}[/probe|/listeners/refresh]]`, `master/{config,pubkey,issue,rotate}`, `deployment-id`, `session/{open,renew,kill}`, **`/v2/rustion/session/open`**, `authority/attest`, `target/deenrol`, `recordings[/{rid}[/blob[/chunk/{n}]|/keystrokes]]`, `recordings/{replay-log,pull,reconcile,keystrokes/index,keystroke-search}`, `telemetry[/poll]`, `policy/{global,type/{t},asset-group/{id},resource/{id},force-rustion,effective}`, `dispatcher/preview`, `bastion-groups[/{name}]` |

## Endpoints that do **not** exist (do not implement)

`sys/leases/*`, `sys/renew`, `sys/revoke`, `sys/revoke-prefix` (stubs unreachable over
HTTP), `sys/wrapping/*`, `sys/leader`, `sys/version`, `sys/capabilities`,
`sys/capabilities-accessor`, `cubbyhole/*`, `auth/token/lookup-accessor`,
`auth/token/renew-self`, `auth/token/renew-accessor`, `auth/token/revoke-accessor`,
`auth/token/roles`, `auth/token/tidy`, `{kv}/delete/{path}`, `{kv}/subkeys/{path}`,
`{kv}/metadata/{path}` (write), `GET …?list=true`.
