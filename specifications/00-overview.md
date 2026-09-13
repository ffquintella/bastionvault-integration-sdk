# 00 — Overview

## Specification version

| Field | Value |
|-------|-------|
| Specification version | 1.0.0 |
| Target server | BastionVault ≥ 0.42 (HTTP API `/v1` and `/v2`) |
| Compatibility | HashiCorp Vault HTTP API (subset) plus BastionVault extensions |
| Date | 2026-09-13 |

## Purpose

The BastionVault Integration SDK is a **client library** that lets an application
integrate with a running BastionVault server over HTTPS. It is *not* an embedded vault,
not a CLI, and not a server plugin. Its responsibilities are:

1. Build well-formed HTTP requests to the BastionVault API and parse responses into typed
   results.
2. Manage authentication: obtain tokens through the supported auth methods, attach them
   to every request, renew them, and revoke them.
3. Provide typed, ergonomic operations for the system API and every secrets engine
   BastionVault ships.
4. Fail loudly and helpfully: every error carries a stable code, a default human message,
   and a *hint* that tells the developer what to check.
5. Behave safely in production: TLS by default, no secret material in logs, bounded
   retries, cluster awareness, and cooperation with the server's abuse protection.

## Goals

- **Behavioural parity across languages.** Two SDKs given the same configuration and the
  same server responses produce the same observable behaviour and the same error codes.
- **Testable without a server.** The full behaviour is verifiable with the shared
  conformance fixtures in Appendix C and an in-memory fake transport.
- **95 % test coverage minimum** on every implementation (see
  [01](01-conformance-and-quality.md)).
- **Discoverable failures.** Developers should be able to fix most integration mistakes
  from the error message and hint alone, without reading server logs.
- **Complete documentation.** Every public operation is documented; every guide in
  [17](17-usage-guides.md) exists in every SDK.

## Non-goals

- Implementing server features (policy evaluation, encryption at rest).
- Providing a GUI or an interactive CLI (the `bvault` CLI exists separately).
- Transparent mid-session failover between cluster nodes (see
  [13](13-cluster-discovery-and-resilience.md) for why this is deliberately out).
- Supporting arbitrary HashiCorp Vault deployments. Vault compatibility is a side effect,
  not a requirement; BastionVault-specific behaviour wins when they differ.

## Glossary

| Term | Meaning |
|------|---------|
| **Server** | A BastionVault node or cluster reached through its HTTP listener (default `https://127.0.0.1:8200`). |
| **Client** | The SDK object an application configures once and uses to perform operations. |
| **Operation** | A single logical action (`Read`, `Write`, `Delete`, `List`) or a typed convenience built on one. |
| **Mount** | A path prefix at which a secrets engine or auth method is enabled (e.g. `secret/`, `auth/approle/`). |
| **Logical path** | A path relative to the API root without the `/v1/` prefix, e.g. `secret/data/app/db`. |
| **Envelope** | The JSON structure every successful response uses (`request_id`, `lease_id`, `data`, `auth`, ...). |
| **Token** | The bearer credential sent as `X-Vault-Token`. Service tokens are renewable; batch tokens are not. |
| **Namespace** | A tenant container selected with the `X-BastionVault-Namespace` header. |
| **Environment** (KV) | A per-environment override set on a KV v2 secret selected with `?env=`. |
| **Lease** | Server-side lifetime record for a dynamic secret or token, identified by `lease_id`. |
| **Sealed** | Server state in which the barrier key is not in memory; every data request fails with 503. |
| **Standby / Follower** | Cluster node that is not the Raft leader. |
| **Error code** | Stable SDK-defined identifier (e.g. `BV-AUTH-002`) independent of language. |
| **Hint** | Actionable text attached to an error that tells the developer what to check. |
| **Fixture** | A canonical JSON request/response pair in Appendix C used by conformance tests. |

## Architecture the SDK must present

The SDK MUST be layered so that each layer is independently testable:

```
┌──────────────────────────────────────────────────────────────┐
│  Typed API surface                                            │
│  Sys · Auth · Kv · Transit · Pki · Ssh · Totp · Identity ...  │  (05–12)
├──────────────────────────────────────────────────────────────┤
│  Logical layer: Read / Write / Delete / List on a path        │  (03)
│  builds requests, parses the envelope, maps errors            │
├──────────────────────────────────────────────────────────────┤
│  Auth & token management: token source, renewal, revocation   │  (05)
├──────────────────────────────────────────────────────────────┤
│  Transport: HTTP client abstraction, TLS, timeouts, retries,  │  (02, 03, 13)
│  rate gate, cluster discovery, sticky node                    │
├──────────────────────────────────────────────────────────────┤
│  Error model: codes, messages, hints                          │  (04)
└──────────────────────────────────────────────────────────────┘
```

- **OVR-001** The transport layer MUST be replaceable through an interface/trait/protocol
  so tests can inject a fake that returns canned responses without opening a socket.
- **OVR-002** The typed API surface MUST be implemented on top of the logical layer; it
  MUST NOT bypass it to talk to the transport directly (this keeps error mapping,
  headers, and namespace handling uniform).
- **OVR-003** Every public operation MUST be available in the runtime's idiomatic
  asynchronous form. A synchronous form MAY be provided in addition.
- **OVR-004** The SDK MUST NOT hold global mutable state. Two `Client` instances in one
  process MUST be fully independent (different servers, tokens, namespaces).
- **OVR-005** The SDK MUST be safe to use from multiple threads/tasks concurrently on a
  single `Client` instance.
- **OVR-006** The SDK MUST support cancellation/timeouts through the runtime's idiomatic
  mechanism (cancellation token, `Drop`/`select`, `asyncio` cancellation). A cancelled
  operation MUST surface as `BV-TRANSPORT-005` (see [04](04-error-model.md)).

## Naming and idiom mapping

This specification uses **canonical names** written in `PascalCase` for types and
operations (`Client`, `Kv.ReadSecret`) and `snake_case` for wire-level JSON fields
(`lease_duration`). Implementations MUST map canonical names to their language convention
and MUST document the mapping table in their README (see [16](16-documentation-requirements.md)).

| Canonical | .NET | Rust | Python |
|-----------|------|------|--------|
| `Client` | `BastionVaultClient` | `Client` | `Client` |
| `Kv.ReadSecret` | `client.Kv.ReadSecretAsync(...)` | `client.kv().read_secret(...)` | `client.kv.read_secret(...)` |
| `Error.Code` | `BastionVaultException.Code` | `Error::code()` | `BastionVaultError.code` |
| wire field `lease_duration` | `LeaseDuration` | `lease_duration` | `lease_duration` |

- **OVR-007** Wire field names MUST NOT be altered on the wire; only the language-facing
  accessor may be renamed.
- **OVR-008** Every area in this spec (`Sys`, `Auth`, `Kv`, `Transit`, ...) MUST be exposed
  as a distinct grouping (namespace, sub-client, module) reachable from `Client`, so that
  discoverability in IDEs mirrors the spec.
- **OVR-009** Operation names in this spec are canonical; an implementation MAY add
  aliases but MUST keep the canonical name (after idiom mapping) available.

## Reading order for a new implementer

1. 00 Overview → 01 Conformance → 04 Error model (build the error type first; everything
   returns it).
2. 02 Configuration → 03 Transport (build the logical layer and the fake transport).
3. 05 Authentication (token source and login flows).
4. 07 KV (the most used engine; validates the whole stack end to end).
5. 06 System API, then the remaining engines in any order.
6. 13 Resilience, 14 Efficiency.
7. 15 Testing and 16 Documentation continuously, not at the end.
