# `ClientConfig` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/ClientConfig.cs`](../../../dotnet/BastionVault.IntegrationSdk/ClientConfig.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `ClientConfig`

#### `Address`

The literal address value as resolved (a URL or a bare cluster name).

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:87`*

#### `AddressUri`

The parsed URL form of <see cref="Address"/>, or `null` when it is a bare cluster name.

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:90`*

#### `AddressIsClusterName`

Whether <see cref="Address"/> is a bare DNS name that triggers cluster discovery rather than a literal node URL.

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:93`*

#### `Token`

The initial token (CFG-020: absence is not an error).

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:104`*

#### `TokenFile`

Path read for the token when <see cref="Token"/> is unset and <see cref="UseTokenHelper"/> is true.

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:107`*

#### `UseTokenHelper`

Opt-in to reading/writing the token file.

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:110`*

#### `Namespace`

Slash-delimited path, no leading slash, trailing slash silently stripped (CFG-015).

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:113`*

#### `CaCertPath`

PEM bundle path to trust instead of/in addition to system roots.

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:116`*

#### `CaCertPem`

Inline PEM alternative to <see cref="CaCertPath"/>. Takes precedence when both are set.

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:119`*

#### `CaCertReplacesSystemRoots`

When true, <see cref="CaCertPath"/>/<see cref="CaCertPem"/> replace the platform trust store instead of being added to it (CFG-040).

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:122`*

#### `ClientCertPath`

mTLS client certificate path (PEM).

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:125`*

#### `ClientKeyPath`

mTLS client private key path (PEM).

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:128`*

#### `TlsSkipVerify`

Disables certificate verification. See CNF-030.

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:131`*

#### `TlsServerName`

SNI / hostname to verify against (CFG-042).

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:134`*

#### `AllowInsecureHttp`

Required for non-loopback `http://` addresses (CNF-035).

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:137`*

#### `Timeout`

Per-request total timeout (connect + headers + body).

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:140`*

#### `ConnectTimeout`

TCP + TLS handshake timeout.

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:143`*

#### `RetryPolicy`

Retry policy defaults (CFG-050). Retry execution is M1b.

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:146`*

#### `RateGate`

Client-side rate gate values (D-M1a-13, EFF-001…EFF-006). The token bucket is `Internal.ClientRateGate`.

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:149`*

#### `BatchMaxOperations`

BAT-002: the largest number of operations `Sys.Batch` will send in one request.
Default 128, rejected client-side with `BV-INPUT-003` above it.

Constructor-settable only, with no environment variable, for the same reason as
<see cref="Discovery"/> and <see cref="MaxResponseBytes"/>: CFG-001's settings table in
`specifications/02-client-configuration.md` is the normative list of environment-bound
settings and has no row for this one. Section 14 says "configurable" without saying by
what, so the SDK offers the option and declines to invent a `BASTIONVAULT_*` name that
the specification would then have to be amended to match.

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:163`*

#### `ClusterDiscovery`

See `specifications/13-cluster-discovery-and-resilience.md`.

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:166`*

#### `DiscoveryProbeTimeout`

Health probe timeout per candidate.

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:169`*

#### `Discovery`

DNS SRV discovery settings (DSC-010…014). No environment variable (D-M5-19).

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:172`*

#### `Health`

Health-probe settings (DSC-020). `ProbeTimeout` is populated from
<see cref="DiscoveryProbeTimeout"/> unless an explicit
`Health` overrides it (D-M5-8, ruling 4).

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:179`*

#### `Headers`

Extra headers added to every request. Never contains a reserved header (CFG-017).

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:182`*

#### `UserAgent`

Appended, never replacing, the SDK's own user agent.

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:185`*

#### `ApiPrefix`

Default prefix for raw logical operations (`v1` or `v2`).

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:188`*

#### `AutoRenew`

Background token renewal (D-M1a-13). Always disabled at milestone M1a; the renewal loop is M2.

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:191`*

#### `IsInsecure`

True when <see cref="TlsSkipVerify"/> is set (CFG-018). A client with `IsInsecure == true`
has already logged one CNF-030 warning by the time construction returns.

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:197`*

#### `CaCertificates`

The parsed CA certificate(s) from <see cref="CaCertPem"/> or <see cref="CaCertPath"/>
(CFG-013, CFG-014), or `null` when neither was configured. Materialised at
construction; wiring into the platform trust store (CFG-040) is transport work (M1b).

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:204`*

#### `ClientCertificate`

The parsed mTLS client certificate/key pair (CFG-012, CFG-013, CFG-014), or
`null` when neither <see cref="ClientCertPath"/> nor <see cref="ClientKeyPath"/>
was configured.

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:211`*

#### `MaxResponseBytes`

Responses larger than this are aborted with `BV-TRANSPORT-004` (TRN-033, D-M1b-13). Default 128 MiB.

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:214`*

#### `UseSystemProxy`

Proxies are disabled by default; `true` opts in to environment and OS proxy settings (TRN-091, D-M1b-13).

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:217`*

#### `MinimumTlsProtocol`

*(no `<summary>` doc comment.)*

*Source: `dotnet/BastionVault.IntegrationSdk/ClientConfig.cs:224`*

