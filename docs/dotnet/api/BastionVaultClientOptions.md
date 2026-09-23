# `BastionVaultClientOptions` (.NET API reference)

Generated from doc comments in [`dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs`](../../../dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs) by
`tools/api-reference/generate.py` (D11, DR-0018 D-M11-2). Do not edit by hand —
regenerate with:

```
python3 tools/api-reference/generate.py --out docs/dotnet/api
```

### `BastionVaultClientOptions`

#### `Address`

A literal node URL (`https://host:8200`) or a bare DNS name for cluster discovery. Default `https://127.0.0.1:8200`.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:13`*

#### `Token`

The initial token. May be replaced later by a login or `SetToken` (M1b).

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:16`*

#### `TokenSource`

AUT-001's token source, when the application supplies one rather than a literal
<see cref="Token"/>. When set it is the client's one source and <see cref="Token"/>,
the token environment variables and the token file are all ignored; when unset the client
holds a `Static` source over the resolved token, which is the
pre-M2a behaviour.

This is the installation point for a `Callback`
source: a `Static` one is also reachable through
`SetToken` and `Auth.Token.Use`, but a callback has no
other way in, because AUT-004 makes `TokenSource` read-only. The
member name is not pinned by D-M2-6, which pins the factories and the property but
not the settings entry; it is proposed here as the smallest surface that stops
`TokenSource.Callback` being decorative, and it sits with the other injection points
(<see cref="Transport"/>, <see cref="Clock"/>, <see cref="Logger"/>, <see cref="Observer"/>)
rather than in <see cref="ClientConfig"/>, because it is an object and not a resolved
setting value.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:37`*

#### `TokenFile`

Path read for the token when <see cref="Token"/> is unset and <see cref="UseTokenHelper"/> is true. Default `~/.vault-token`.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:40`*

#### `UseTokenHelper`

Opt-in to reading/writing the token file. Default `false`.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:43`*

#### `Namespace`

Slash-delimited namespace path, no leading slash. Default `""` (root).

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:46`*

#### `CaCertPath`

PEM bundle path to trust instead of/in addition to system roots.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:49`*

#### `CaCertPem`

Inline PEM alternative to <see cref="CaCertPath"/>. Takes precedence when both are set.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:52`*

#### `CaCertReplacesSystemRoots`

When true, <see cref="CaCertPath"/>/<see cref="CaCertPem"/> replace the platform trust store instead of being added to it (CFG-040). Default `false`.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:55`*

#### `ClientCertPath`

mTLS client certificate (PEM). Must be set together with <see cref="ClientKeyPath"/>.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:58`*

#### `ClientKeyPath`

mTLS client private key (PEM). Must be set together with <see cref="ClientCertPath"/>.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:61`*

#### `TlsSkipVerify`

Disables certificate verification. Default `false`. See CNF-030.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:64`*

#### `TlsServerName`

SNI / hostname to verify against (CFG-042).

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:67`*

#### `AllowInsecureHttp`

Required for non-loopback `http://` addresses (CNF-035). Default `false`.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:70`*

#### `Timeout`

Per-request total timeout (connect + headers + body). Default 30s.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:73`*

#### `ConnectTimeout`

TCP + TLS handshake timeout. Default 10s.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:76`*

#### `RetryPolicy`

Retry policy defaults (CFG-050). Retry execution is M1b.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:79`*

#### `RateGate`

Client-side token bucket (`specifications/14-batch-and-request-efficiency.md#client-rate-gate`, EFF-001…EFF-006). Setting either field to `0` disables it.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:82`*

#### `BatchMaxOperations`

BAT-002: the largest batch `Sys.Batch` will send. Default 128. Constructor-settable
only — it has no settings-table row and therefore no environment variable, like
<see cref="Discovery"/> and <see cref="MaxResponseBytes"/>.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:89`*

#### `ClusterDiscovery`

See `specifications/13-cluster-discovery-and-resilience.md`. Default `true`.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:92`*

#### `DiscoveryProbeTimeout`

Health probe timeout per candidate. Default 1500ms.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:95`*

#### `SrvResolver`

DSC-014's injectable SRV resolver. No default: the SDK ships no DNS SRV implementation, and a
discovery-mode client without a resolver takes DSC-011's "no records" path, which for a plain
cluster name is DSC-012's single literal candidate.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:102`*

#### `Discovery`

DNS SRV discovery settings. Constructor-settable only — these four have no settings-table row and no environment variable (D-M5-19).

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:105`*

#### `Health`

Health-probe settings. When left unset, `ProbeTimeout` is taken from <see cref="DiscoveryProbeTimeout"/> (D-M5-8, ruling 4).

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:108`*

#### `Headers`

Extra headers added to every request. MUST NOT override reserved headers (CFG-017).

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:111`*

#### `UserAgent`

Appended, never replacing, the SDK's own user agent. Default `bastionvault-sdk-dotnet/&lt;version&gt;`.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:114`*

#### `ApiPrefix`

Default prefix for raw logical operations (`v1` or `v2`). Default `v1`.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:117`*

#### `AutoRenew`

Background token renewal (`specifications/05-authentication.md#automatic-renewal`). Default disabled; the renewal loop is M2.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:120`*

#### `Logger`

The runtime-idiomatic logging hook (CNF-030). Defaults to <see cref="NoOpClientLogger"/>.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:123`*

#### `Transport`

The transport injection point (OVR-001). No default is provided.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:126`*

#### `MaxResponseBytes`

Responses larger than this are aborted with `BV-TRANSPORT-004` (TRN-033, D-M1b-13). Default 128 MiB.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:129`*

#### `UseSystemProxy`

Proxies are disabled by default; set `true` to opt in to environment and OS proxy settings (TRN-091, D-M1b-13).

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:132`*

#### `Clock`

The injected time seam (D-M1b-7). Defaults to <see cref="SystemClock"/>.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:135`*

#### `JitterSource`

The injected randomness seam for retry jitter (D-M1b-7). Defaults to <see cref="SystemJitterSource"/>.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:138`*

#### `Observer`

The request/response observability hook (CFG-080). No default.

*Source: `dotnet/BastionVault.IntegrationSdk/BastionVaultClientOptions.cs:141`*

