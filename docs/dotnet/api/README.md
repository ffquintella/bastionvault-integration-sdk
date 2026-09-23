# API reference (.NET)

**Implements** D11
([`specifications/16-documentation-requirements.md`](../../../specifications/16-documentation-requirements.md#L24)).
Generated from doc comments for every public symbol by
`tools/api-reference/generate.py`, which reuses
`tools/doc-worksheet/cs_model.py` (the same C# scanner DOC-005/DOC-006's worksheet
depends on) rather than a second, unverified parser. Regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

90 pages, one per source file that declares at least one public member.
A file that is entirely internal-facing (`Internal/`, most of `Testing/`) emits no
page, so this count is smaller than
`ls dotnet/BastionVault.IntegrationSdk/*.cs dotnet/BastionVault.IntegrationSdk/**/*.cs`.

| Page | Public members |
|---|---:|
| [`AppIdAdminOperations`](AppIdAdminOperations.md) | 22 |
| [`AppIdOperations`](AppIdOperations.md) | 15 |
| [`AssetGroupOperations`](AssetGroupOperations.md) | 8 |
| [`Audit`](Audit.md) | 18 |
| [`AuditOperations`](AuditOperations.md) | 4 |
| [`AuthOperations`](AuthOperations.md) | 14 |
| [`AuthRoleAdminOperations`](AuthRoleAdminOperations.md) | 6 |
| [`AutoRenewPolicy`](AutoRenewPolicy.md) | 12 |
| [`BastionVaultClient`](BastionVaultClient.md) | 31 |
| [`BastionVaultClientOptions`](BastionVaultClientOptions.md) | 35 |
| [`BastionVaultException`](BastionVaultException.md) | 16 |
| [`BatchTypes`](BatchTypes.md) | 9 |
| [`CacheVersion`](CacheVersion.md) | 6 |
| [`CacheWatcher`](CacheWatcher.md) | 8 |
| [`Capabilities`](Capabilities.md) | 5 |
| [`CertLifecycleOperations`](CertLifecycleOperations.md) | 11 |
| [`CertLifecycleTypes`](CertLifecycleTypes.md) | 24 |
| [`CertOperations`](CertOperations.md) | 1 |
| [`ClientConfig`](ClientConfig.md) | 34 |
| [`Clock`](Clock.md) | 3 |
| [`ClusterStatus`](ClusterStatus.md) | 6 |
| [`Discovery`](Discovery.md) | 14 |
| [`DiscoveryReport`](DiscoveryReport.md) | 2 |
| [`EnvironmentScope`](EnvironmentScope.md) | 3 |
| [`EnvironmentSource`](EnvironmentSource.md) | 6 |
| [`ErrorCatalog`](ErrorCatalog.md) | 2 |
| [`FerrogateAdminOperations`](FerrogateAdminOperations.md) | 9 |
| [`FerrogateOperations`](FerrogateOperations.md) | 14 |
| [`Fido2Operations`](Fido2Operations.md) | 3 |
| [`FileTypes`](FileTypes.md) | 13 |
| [`FilesOperations`](FilesOperations.md) | 18 |
| [`HealthStatus`](HealthStatus.md) | 7 |
| [`HsmStatus`](HsmStatus.md) | 5 |
| [`HttpClientTransport`](HttpClientTransport.md) | 3 |
| [`IClientLogger`](IClientLogger.md) | 2 |
| [`ITransport`](ITransport.md) | 3 |
| [`Identity`](Identity.md) | 22 |
| [`IdentityKernelTypes`](IdentityKernelTypes.md) | 23 |
| [`IdentityOperations`](IdentityOperations.md) | 40 |
| [`InitResult`](InitResult.md) | 6 |
| [`JitterSource`](JitterSource.md) | 2 |
| [`KvOperations`](KvOperations.md) | 9 |
| [`KvTypes`](KvTypes.md) | 35 |
| [`KvV1Operations`](KvV1Operations.md) | 5 |
| [`KvV2Operations`](KvV2Operations.md) | 22 |
| [`LdapOperations`](LdapOperations.md) | 20 |
| [`LdapTypes`](LdapTypes.md) | 43 |
| [`LegacyPolicyOperations`](LegacyPolicyOperations.md) | 2 |
| [`LogicalOperations`](LogicalOperations.md) | 5 |
| [`LoginCredentials`](LoginCredentials.md) | 11 |
| [`LoginOptions`](LoginOptions.md) | 2 |
| [`Mounts`](Mounts.md) | 13 |
| [`Namespaces`](Namespaces.md) | 30 |
| [`NotificationsOperations`](NotificationsOperations.md) | 13 |
| [`NotificationsTypes`](NotificationsTypes.md) | 9 |
| [`OidcOperations`](OidcOperations.md) | 3 |
| [`PkiOperations`](PkiOperations.md) | 72 |
| [`PkiTypes`](PkiTypes.md) | 159 |
| [`Policies`](Policies.md) | 27 |
| [`RateGate`](RateGate.md) | 3 |
| [`RequestOptions`](RequestOptions.md) | 9 |
| [`ResourceTypes`](ResourceTypes.md) | 16 |
| [`ResourcesOperations`](ResourcesOperations.md) | 20 |
| [`Response`](Response.md) | 19 |
| [`RetryPolicy`](RetryPolicy.md) | 8 |
| [`RustionOperations`](RustionOperations.md) | 29 |
| [`RustionPolicyOperations`](RustionPolicyOperations.md) | 20 |
| [`RustionRecordingsOperations`](RustionRecordingsOperations.md) | 11 |
| [`RustionTypes`](RustionTypes.md) | 20 |
| [`SamlOperations`](SamlOperations.md) | 6 |
| [`SdkInfo`](SdkInfo.md) | 4 |
| [`SealStatus`](SealStatus.md) | 6 |
| [`SecretBytes`](SecretBytes.md) | 7 |
| [`SecretString`](SecretString.md) | 7 |
| [`ServerInfo`](ServerInfo.md) | 6 |
| [`SshBrokerOperations`](SshBrokerOperations.md) | 12 |
| [`SshOperations`](SshOperations.md) | 15 |
| [`SshTypes`](SshTypes.md) | 49 |
| [`SysComplete`](SysComplete.md) | 16 |
| [`SysCompleteOperations`](SysCompleteOperations.md) | 14 |
| [`SysOperations`](SysOperations.md) | 51 |
| [`TokenInfo`](TokenInfo.md) | 25 |
| [`TokenOperations`](TokenOperations.md) | 11 |
| [`TokenSource`](TokenSource.md) | 5 |
| [`TotpOperations`](TotpOperations.md) | 6 |
| [`TotpTypes`](TotpTypes.md) | 26 |
| [`TransitOperations`](TransitOperations.md) | 23 |
| [`TransitTypes`](TransitTypes.md) | 34 |
| [`UserpassAdminOperations`](UserpassAdminOperations.md) | 16 |
| [`UserpassOperations`](UserpassOperations.md) | 4 |
