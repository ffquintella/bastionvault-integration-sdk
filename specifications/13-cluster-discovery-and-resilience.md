# 13 — Cluster Discovery and Resilience

BastionVault runs either as a single node or as a Hiqlite (Raft) HA cluster. The server
performs **no** leader redirects and exposes **no** leader endpoint; locating a usable
node is entirely the client's job. This section specifies DNS SRV discovery, health
probing, node ranking, sticky sessions with bounded failover, and the retry policy.

## Address shapes

| Input | Mode | Example |
|-------|------|---------|
| Contains `://` | Literal URL — no DNS, no probing | `https://bv-1.corp.example:8200` |
| Contains an explicit `:port` | Literal, scheme defaults to `https` | `bv-1.corp.example:8200` |
| IP literal | Literal (IPv6 must be bracketed) | `10.0.0.5`, `[::1]:8200` |
| Bare DNS name | **Cluster name** — SRV discovery | `vault.corp.example` |
| Starts with `_` | SRV owner name queried verbatim | `_bvault._tcp.vault.corp.example` |
| `http://` prefix on a cluster name | Discovery with scheme forced to http | `http://vault.corp.example` |

- **DSC-001** The SDK MUST implement the classification above. `ClusterDiscovery = false`
  forces literal mode for a bare name using `DefaultScheme://name:DefaultPort`.
- **DSC-002** Unbracketed IPv6 with a port MUST raise `BV-CONFIG-001` ("ambiguous IPv6").

## SRV discovery

```
DiscoveryConfig { SrvService = "_bvault._tcp", DefaultScheme = "https", DefaultPort = 8200,
                  ResolveTimeout = 5s }
```

- **DSC-010** For a cluster name `N`, query SRV for `_bvault._tcp.N` (or `N` verbatim if
  it starts with `_`). Strip trailing dots from targets. Sort ascending by priority.
- **DSC-011** A resolver failure MUST be treated as "no records", not propagated.
- **DSC-012** No records + non-SRV-shaped name → exactly one literal candidate
  `(N, DefaultPort, DefaultScheme)`. No records + SRV-shaped name (`_…`) → empty
  candidate list → `BV-DISCOVERY-001`.
- **DSC-013** `Candidate.Url = "{scheme}://{target}:{port}"`; the port is always explicit.
- **DSC-014** The resolver MUST be injectable (interface) so tests supply fake SRV
  answers.

## Health probing

```
HealthConfig { ProbeTimeout = 1500ms, Parallelism = 4 }
```

Probe: `GET {candidate}/v1/sys/health` with `Accept: application/json`, **no token**,
bypassing the rate gate. Any HTTP status is accepted; the **body** is authoritative:

```json
{ "initialized": true, "sealed": false, "standby": false, "cluster_healthy": true }
```

Status codes the server uses (informational; body wins): `200` active, `429` standby,
`501` not initialised, `503` sealed or cluster unhealthy.

Classification (`NodeState`):

| Body | State |
|------|-------|
| `initialized == false` | `Uninitialized` |
| `sealed == true` | `Sealed` |
| `standby == true` (or `performance_standby == true`) | `Follower` |
| otherwise | `ActiveLeader` |
| no answer / non-JSON body / timeout | `Unreachable` |

- **DSC-020** Probes MUST run in parallel with at most `Parallelism` in flight.
- **DSC-021** Each `ProbeResult` MUST record `Candidate`, `State`, `RttMs`, and the
  optional `cluster_id` / `version` fields (absent on current servers; keep for
  forward-compat).
- **DSC-022** `cluster_healthy == false` MUST NOT by itself change the state; it MUST be
  surfaced as `ProbeResult.ClusterHealthy` so ranking can prefer healthy nodes as a
  tiebreak after RTT.

## Picking a node

- **DSC-030** Drop candidates not in `{ActiveLeader, Follower}`.
- **DSC-031** Keep only candidates at the **minimum SRV priority** among survivors (a
  higher-priority follower beats a lower-priority leader — priority is a hard floor).
- **DSC-032** If `cluster_id` values are present, drop candidates outside the dominant
  `cluster_id` (ties: best state rank, then lowest priority). Candidates without a
  `cluster_id` are kept.
- **DSC-033** Rank: `ActiveLeader` before `Follower`; then lower `RttMs`; then higher SRV
  weight; then lexical URL. Deterministic — **not** RFC 2782 weighted-random.
- **DSC-034** No survivor → `BV-DISCOVERY-002 NoHealthyNode` with `Details.candidates`
  listing `target=state` for every probe.
- **DSC-035** The chosen node MUST be exposed as `Client.SelectedNode { Url, State,
  RttMs, ClusterId?, Version? }` and the original input as `Client.InputLabel`.
- **DSC-036** `Client.Discover()` (diagnostics) MUST return the full ranked table without
  changing the pinned node.

## Sticky session and bounded failover

- **DSC-040** After the pick, all requests go to the pinned node. There is **no**
  transparent mid-session re-pinning on success paths.
- **DSC-041** A request that fails at transport level (connection refused/reset, timeout)
  or with a `5xx` whose message contains `sealed`, `uninitialized`, or `standby`
  (case-insensitive) is a **node failure** → `BV-DISCOVERY-003 NodeUnavailable`
  (`Details.host`, `Details.reason`). A `4xx` is never a node failure.
- **DSC-042** When failover is armed (discovery produced ≥ 2 candidates) and the
  operation is idempotent (`Read`, `List`, and typed operations flagged idempotent), the
  SDK MUST perform **exactly one** failover attempt: re-probe the cached candidate set
  (no new SRV lookup), exclude the failed URL, pick, swap the pinned node, replay the
  request once. Writes and deletes MUST NOT be replayed (ambiguous commit).
- **DSC-043** Concurrent failures MUST serialise on one lock so the candidate set is
  re-probed once; a task acquiring the lock after another has already moved off the
  dead node MUST reuse the new pick.
- **DSC-044** When failover is not armed or the replay also fails, the error returned MUST
  be the original `BV-DISCOVERY-003`, with `Attempts` incremented.
- **DSC-045** Node-local operations MUST be excluded from failover: SSH/RDP connect
  sessions, long-poll watchers (`watch=1`), FIDO2 ceremonies, plugin asset downloads,
  recording chunk streams. They fail with `BV-DISCOVERY-003` and the caller reconnects.
- **DSC-046** `Client.Reconnect()` MUST re-run full discovery (SRV + probe) and re-pin;
  it is the explicit recovery path. It MUST be safe to call concurrently.

## Retry policy interaction

The retry policy of [02](02-client-configuration.md#retry-policy) and failover compose
as follows:

1. Transport failure on the pinned node → failover (if armed, idempotent) **once**.
2. If still failing and the retry policy allows (idempotent, attempts left,
   code ∈ `RetryOn`) → backoff and retry against the (possibly new) pinned node.
3. `BV-SERVER-001` (sealed) is never retried; `BV-RATE-001` is never retried (gate
   handles it); `BV-SERVER-003` (standby 429 from health-style bodies) is retried only
   via failover.

- **RES-001** Total attempts MUST never exceed `MaxAttempts + 1` (the `+1` is the single
  failover replay).
- **RES-002** Every attempt MUST be reported to the observability hook with its attempt
  number and outcome.
- **RES-003** Backoff MUST be `min(MaxBackoff, InitialBackoff × Multiplier^(attempt−1))`
  with ±`Jitter` uniform randomisation; the random source MUST be injectable for tests.
- **RES-004** Per-call `Timeout` bounds each attempt, not the total; the SDK MUST also
  offer `TotalTimeout` in `RequestOptions` bounding attempts + backoff together.

## TLS with discovery

- **RES-010** SNI and hostname verification MUST use the SRV **target** hostname of the
  chosen candidate (each node's certificate needs a SAN for its own target). When
  `TlsServerName` is set it overrides this for every candidate.
- **RES-011** Probes MUST use the same TLS material as the eventual requests.

## Diagnostics

- **RES-020** `Client.Discover()` output MUST be renderable as the table the CLI prints:

```
Target                                  Pri  Wt  State         RTT(ms)  ClusterId
https://bv-1.corp.example:8200          10   50  ActiveLeader       12  -
https://bv-2.corp.example:8200          10   50  Follower           14  -
https://bv-3.corp.example:8200          10   50  Sealed              -  -
Picked: https://bv-1.corp.example:8200 (ActiveLeader, 12 ms)
```

- **RES-021** Error hints for `BV-DISCOVERY-002` MUST include the `target=state` list so
  an operator sees *why* nothing was picked.

## Operator-facing operations that fan out

- **RES-030** `Sys.Unseal` and `Sys.Seal` SHOULD offer `*ClusterWide` variants that
  iterate **all** discovered candidates (including sealed/unreachable) and return a
  per-node result map, since unsealing must reach every node. These variants MUST NOT be
  subject to failover or retry.
