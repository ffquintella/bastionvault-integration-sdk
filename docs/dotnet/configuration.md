# Configuration reference (.NET)

**Implements** [`specifications/02-client-configuration.md`](../../specifications/02-client-configuration.md)
(`CFG-001`…`CFG-081`), adapted to the .NET spellings in
[`BastionVaultClientOptions`](../../dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs)
and [`ClientConfig`](../../dotnet/BastionVault.IntegrationSdk/ClientConfig.cs). Read the
specification for the language-neutral rule; read this page for the .NET setting name, its
environment variable, its default, and the `BV-CONFIG-*` code a bad value raises.

This is a reference document, not a guide, so it does not follow the `DOC-010` task order — there
is no single task here, only a lookup. `ConfigurationDocsDriftTests`
(`dotnet/BastionVault.IntegrationSdk.Tests/ConfigurationDocsDriftTests.cs`) reflects over
`ClientConfig` and `Internal/ConfigurationResolver.cs` on every test run and fails the build the
day this table stops matching the code, in either direction.

## Settings

Every setting `BastionVaultClientOptions` accepts, its `ClientConfig` name after resolution, its
environment variable(s), its default, and what it is for. `Discovery`, `Health` and
`BatchMaxOperations` are constructor-settable options with no row in the specification's settings
table (`ClientConfig.cs`'s own remarks explain why) and are deliberately absent from this list.

| Setting | Type | Default | Environment variable(s) | Notes |
|---|---|---|---|---|
| `Address` | URL or cluster name | `https://127.0.0.1:8200` | `BASTIONVAULT_ADDR`, `VAULT_ADDR` | A value containing `://` is a literal node URL; a bare DNS name triggers cluster discovery. |
| `Token` | secret string | none | `BASTIONVAULT_TOKEN`, `VAULT_TOKEN` | `CFG-020`: absence is not an error. `ClientConfig.Token` is a `SecretString`; `HasValue` tells you whether one is configured, `Reveal()` reads it. |
| `TokenFile` | path | `~/.vault-token` | `BASTIONVAULT_TOKEN_FILE` | Only read when `Token` is unset and `UseTokenHelper` is `true`. |
| `UseTokenHelper` | bool | `false` | `BASTIONVAULT_USE_TOKEN_HELPER` | Opt-in to reading the token file (`CFG-030`). |
| `Namespace` | string | `""` (root) | `BASTIONVAULT_NAMESPACE`, `VAULT_NAMESPACE` | Sent as `X-BastionVault-Namespace`. A leading `/`, `//`, whitespace or control character raises `BV-CONFIG-007`; a trailing `/` is stripped silently. |
| `CaCertPath` | path | none | `BASTIONVAULT_CACERT`, `VAULT_CACERT` | PEM bundle trusted in addition to (or, with `CaCertReplacesSystemRoots`, instead of) the system roots. |
| `CaCertPem` | string | none | — | Inline PEM alternative to `CaCertPath`; wins when both are set. |
| `CaCertReplacesSystemRoots` | bool | `false` | — | `CFG-040`: `true` replaces the platform trust store rather than adding to it. |
| `ClientCertPath` | path | none | `BASTIONVAULT_CLIENT_CERT`, `VAULT_CLIENT_CERT` | mTLS client certificate (PEM). Must be set together with `ClientKeyPath`, or construction raises `BV-CONFIG-004`. |
| `ClientKeyPath` | path | none | `BASTIONVAULT_CLIENT_KEY`, `VAULT_CLIENT_KEY` | mTLS client private key (PEM). |
| `TlsSkipVerify` | bool | `false` | `BASTIONVAULT_SKIP_VERIFY`, `VAULT_SKIP_VERIFY` | Disables certificate verification. `CNF-030`: construction still succeeds, but logs one warning and `ClientConfig.IsInsecure` becomes `true`. |
| `TlsServerName` | string | none | `BASTIONVAULT_TLS_SERVER_NAME`, `VAULT_TLS_SERVER_NAME` | SNI / hostname verified against, overriding cluster discovery's own choice (`CFG-042`, `CFG-043`). |
| `AllowInsecureHttp` | bool | `false` | `BASTIONVAULT_ALLOW_INSECURE_HTTP` | Required for a non-loopback `http://` address (`CFG-011`/`CNF-035`), else `BV-CONFIG-002`. |
| `Timeout` | duration | `30s` | `BASTIONVAULT_TIMEOUT`, `VAULT_CLIENT_TIMEOUT` | Per-request total timeout (connect + headers + body). Zero or negative raises `BV-CONFIG-003`. |
| `ConnectTimeout` | duration | `10s` | `BASTIONVAULT_CONNECT_TIMEOUT` | TCP + TLS handshake only. Same validation as `Timeout`. |
| `RetryPolicy` | object | `MaxAttempts=3`, `InitialBackoff=250ms`, `MaxBackoff=5s`, `BackoffMultiplier=2.0`, `Jitter=0.2` | `BASTIONVAULT_MAX_RETRIES`, `VAULT_MAX_RETRIES` | The env var sets `MaxAttempts = value + 1` (the var counts *retries*, the field counts *attempts*). See [Retry policy](../../specifications/02-client-configuration.md#retry-policy). |
| `RateGate` | object | `RatePerSecond=8`, `Burst=16` | `BASTIONVAULT_RATE_PER_SEC`, `BASTIONVAULT_RATE_BURST` | Client-side token bucket ([14](../../specifications/14-batch-and-request-efficiency.md#client-rate-gate)). Either field at `0` disables the gate. |
| `ClusterDiscovery` | bool | `true` | `BASTIONVAULT_NO_CLUSTER_DISCOVERY`, `VAULT_NO_CLUSTER_DISCOVERY` (set ⇒ `false`) | See [13 — cluster discovery](../../specifications/13-cluster-discovery-and-resilience.md). |
| `DiscoveryProbeTimeout` | duration | `1500ms` | `BASTIONVAULT_DISCOVERY_PROBE_TIMEOUT` | Health-probe timeout per discovery candidate. |
| `Headers` | map | `{}` | — | Extra headers on every request. A reserved header name (`X-BastionVault-Token`, `X-Vault-Token`, `Authorization`, `Cookie`, `X-BastionVault-Namespace`, `Host`, `Content-Length`) raises `BV-CONFIG-008`. |
| `UserAgent` | string | `bastionvault-sdk-dotnet/<version>` | — | Appended to, never replacing, the SDK's own user agent. |
| `ApiPrefix` | `v1` \| `v2` | `v1` | — | Default prefix for raw logical operations; typed operations choose their own. |
| `MaxResponseBytes` | integer (bytes) | `134217728` (128 MiB) | — | A larger response is aborted with `BV-TRANSPORT-004`. |
| `UseSystemProxy` | bool | `false` | — | Proxies are off by default; `true` opts in to `HTTPS_PROXY`/`ALL_PROXY` and OS proxy settings. |
| `AutoRenew` | object | `Enabled=false` | — | Background token renewal ([05 — authentication](../../specifications/05-authentication.md#automatic-renewal)). No environment variable resolves any field of it. |
| `Logger` | sink | no-op | — | `IClientLogger` hook. Constructor-settable only; used for the `CNF-030` warning and any future logging. |
| `Transport` | implementation | HTTP | — | `ITransport` injection point for tests (`OVR-001`). `BastionVaultClient`'s parameterless constructor defaults this to the SDK's own HTTP transport (DR-0020). |

## Precedence (CFG-001)

Every setting above resolves in this fixed order, first match wins:

1. The value passed explicitly on `BastionVaultClientOptions`.
2. The `BASTIONVAULT_*` environment variable.
3. The `VAULT_*` environment variable (compatibility alias).
4. The token helper file — `Token` only, and only when `UseTokenHelper` is `true` (`CFG-030`).
5. The built-in default.

`CFG-002`: environment variables are read exactly once, at construction, never per request — so
changing `BASTIONVAULT_ADDR` after `new BastionVaultClient()` has nothing to affect. `CFG-003` and
`CFG-004` fix the accepted spellings for booleans (`1`/`true`/`yes`/`on`, case-insensitive, and
their negatives) and durations (`30s`, `1m30s`, `500ms`, `2h`, or a bare integer as seconds); any
other spelling raises `BV-CONFIG-003` naming the setting in `Details.setting`.

## TLS

- The default trust store is the operating system's; `CaCertPath`/`CaCertPem` are *added* to it
  unless `CaCertReplacesSystemRoots` is `true` (`CFG-040`).
- The minimum negotiated TLS version is always 1.2 (`CFG-041`, `ClientConfig.MinimumTlsProtocol`);
  TLS 1.3 is offered whenever the platform supports it.
- `TlsServerName`, when set, is used for both SNI and hostname verification (`CFG-042`); cluster
  discovery otherwise verifies against the SRV target hostname (`CFG-043`).
- `ClientCertPath`/`ClientKeyPath` are presented on every connection once configured (`CFG-044`),
  and are what `Auth.LoginCert` relies on.
- `TlsSkipVerify` is the only way to turn verification off, and it is not a silent one: `CNF-030`
  requires exactly one warning through `Logger`, and `ClientConfig.IsInsecure` stays `true` for the
  life of the client so calling code can refuse to run with it in production.

## Proxy

`UseSystemProxy` defaults to `false`: the SDK talks to `Address` directly and ignores
`HTTPS_PROXY`/`ALL_PROXY` and the OS proxy configuration unless the application opts in. Setting it
`true` hands the decision to the .NET runtime's own proxy resolution (`TRN-091`); there is no
BastionVault-specific proxy setting beyond the on/off switch.

## Validation errors

Every `BV-CONFIG-*` code, in catalogue order. `Details.setting` names the offending setting for the
three codes that cover more than one; `Details.path` names the file for the two file-related codes.

| Code | Meaning |
|---|---|
| `BV-CONFIG-001` | `Address` is empty, not a valid URL, or not `http`/`https` (`CFG-010`). |
| `BV-CONFIG-002` | `Address` is `http://` to a non-loopback host and `AllowInsecureHttp` is `false` (`CFG-011`). |
| `BV-CONFIG-003` | A setting has the wrong type or an out-of-range value: a malformed bool or duration, a non-positive `Timeout`/`ConnectTimeout`, or a negative rate-gate component (`CFG-016`, `CFG-003`, `CFG-004`). |
| `BV-CONFIG-004` | Exactly one of `ClientCertPath`/`ClientKeyPath` is set (`CFG-012`). |
| `BV-CONFIG-005` | `CaCertPath`, `ClientCertPath` or `ClientKeyPath` cannot be read (`CFG-013`). `TokenFile`'s absence is exempt — that is silently "no token", not a failure. |
| `BV-CONFIG-006` | A PEM value could not be parsed, or parsed to zero certificates (`CFG-014`). |
| `BV-CONFIG-007` | `Namespace` has a leading `/`, a `//`, whitespace, or a control character (`CFG-015`). |
| `BV-CONFIG-008` | `Headers` contains a reserved header name (`CFG-017`). |
| `BV-CONFIG-009` | The HTTP stack in use cannot send the SDK's custom `LIST` verb. Use the default transport. |
| `BV-CONFIG-010` | `TokenFile` holds the CLI's encrypted (`BVTOK1:`) format, not a plaintext token. |
| `BV-CONFIG-011` | The token file cannot be written by `Auth.PersistToken` (`CFG-031`). |

## Sample: explicit construction

Most of the samples across these docs let the parameterless constructor read
`BASTIONVAULT_*`/`VAULT_*` from the environment (`CFG-005`'s convenience form). The settings table
above is what to reach for instead when the application wants every value spelled out in code —
for example, pinning a CA bundle that does not come from the process environment at all:

<!-- docs:sample configuration/explicit-options -->
```csharp
// Every value spelled out in code, overriding whatever the process environment
// holds (CFG-001: an explicit value always wins). CaCertPath is validated at
// construction, so it must already point at a real, readable PEM bundle.
using BastionVaultClient client = new(new BastionVaultClientOptions
{
    Address = "https://vault.example.com:8200",
    Token = "s.FAKEtoken",
    Namespace = "team-a",
    CaCertPath = caCertPath,
});

Console.WriteLine($"address:   {client.Config.Address}");
Console.WriteLine($"namespace: {client.Config.Namespace}");
Console.WriteLine($"CA bundle: {client.Config.CaCertPath}");
```

`CaCertPath` is validated at construction (`CFG-013`): a PEM bundle that does not exist or cannot
be parsed raises `BV-CONFIG-005`/`BV-CONFIG-006` before any network call is attempted, which is why
this sample writes a real, readable PEM file first rather than pointing at a literal path.

### What goes over the wire

Once constructed this way, the client behaves identically to one built from the environment: the
same request headers, the same TLS verification. A read against the namespace configured above
sends:

```http
GET /v1/secret/data/app/db HTTP/1.1
Host: vault.example.com:8200
X-BastionVault-Token: s.FAKEtoken
X-BastionVault-Namespace: team-a
Accept: application/json
```

```json
{
  "renewable": false,
  "lease_id": "",
  "lease_duration": 0,
  "auth": null,
  "data": {
    "data": { "username": "admin", "password": "p2" },
    "metadata": { "version": 2, "created_time": "2026-09-13T10:00:00Z", "deletion_time": "", "destroyed": false }
  }
}
```

`X-BastionVault-Namespace` only appears because `Namespace` was set explicitly above; a root-scoped
client sends no such header (`CFG-001`'s default is `""`).

## Next steps

- **Getting started** — the one-line construction most applications use.
- **Error reference** — every `BV-CONFIG-*` code alongside every other category, with its default
  message and hint.
- **Security guide** — TLS, mTLS and `TlsSkipVerify` in more depth than a settings table can carry.
- **Resilience and operations guide** — `RetryPolicy` and `RateGate` in execution, not just as
  defaults.
