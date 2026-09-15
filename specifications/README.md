# BastionVault Integration SDK — Specifications

This folder is the **single source of truth** for every BastionVault Integration SDK,
regardless of implementation language (.NET, Rust, Python, or any future runtime).

The specifications describe **what** an SDK must do, **how it must behave** when talking
to a BastionVault server, **how it must fail**, **how it must be tested**, and **how it must
be documented**. They deliberately do not prescribe language idioms, package layout, or
concrete class names. Section [00-overview](00-overview.md#naming-and-idiom-mapping)
explains how canonical names in this spec map onto each language.

The server this SDK targets is BastionVault, a post-quantum-ready secret manager whose HTTP
API is compatible with HashiCorp Vault. The API surface documented here was derived from
the BastionVault repository (`docs/api.md`, `docs/authentication.md`, the `bv-client`,
`bv-server`, `bv-errors` crates and the engine crates) as of September 2026.

## Document map

| # | Document | What it specifies |
|---|----------|-------------------|
| 00 | [Overview](00-overview.md) | Scope, goals, glossary, requirement notation, naming rules |
| 01 | [Conformance & Quality](01-conformance-and-quality.md) | Conformance levels, 95 % coverage rule, CI gates, release checklist |
| 02 | [Client Configuration](02-client-configuration.md) | Configuration model, environment variables, precedence, TLS, timeouts, retries |
| 03 | [Transport & Protocol](03-transport-and-protocol.md) | HTTP mapping, headers, request/response envelope, status-code handling, redirects, rate limiting |
| 04 | [Error Model](04-error-model.md) | Error taxonomy, stable error codes, default messages, hints, server-message recognition |
| 05 | [Authentication](05-authentication.md) | Token, Userpass, AppID/AppRole, Certificate, FerroGate; token lifecycle and auto-renew |
| 06 | [System API](06-system-api.md) | `sys/*` operations: init, seal, mounts, auth methods, policies, leases, capabilities, namespaces, health |
| 07 | [KV Engine](07-kv-engine.md) | KV v1, KV v2 (versions, CAS, soft delete, destroy, metadata), per-environment secrets |
| 08 | [Transit Engine](08-transit-engine.md) | Keys, encrypt/decrypt, rewrap, sign/verify, HMAC, random, hash, datakeys |
| 09 | [PKI Engine](09-pki-engine.md) | CA management, roles, issue/sign, revoke, CRL, bulk cert listings |
| 10 | [SSH Engine](10-ssh-engine.md) | CA, roles, signing, OTP, brokering policy |
| 11 | [TOTP Engine](11-totp-engine.md) | Key management, code generation and validation |
| 12 | [Other Engines & Identity](12-other-engines-and-identity.md) | Files, Resources, Asset groups, LDAP, Notifications, Cert lifecycle, Rustion, Identity, Sharing, Cubbyhole |
| 13 | [Cluster Discovery & Resilience](13-cluster-discovery-and-resilience.md) | SRV discovery, health probing, node ranking, sticky sessions, retry policy |
| 14 | [Batch & Request Efficiency](14-batch-and-request-efficiency.md) | Batch endpoint, `*-info` cursor pagination, cache-coherence epochs, client rate gate |
| 15 | [Testing Requirements](15-testing-requirements.md) | Test pyramid, mock server contract, conformance fixtures, coverage measurement, traceability |
| 16 | [Documentation Requirements](16-documentation-requirements.md) | Mandatory docs, structure, API reference rules, example verification |
| 17 | [Usage Guides](17-usage-guides.md) | Language-neutral usage guides that every implementation must adapt and ship |
| A | [Appendix A — Endpoint Catalogue](appendix-a-endpoint-catalogue.md) | Complete table of endpoints the SDK must expose |
| B | [Appendix B — Error Catalogue](appendix-b-error-catalogue.md) | Stable code → default message → hint → retryability, server string recognition |
| C | [Appendix C — Conformance Fixtures](appendix-c-conformance-fixtures.md) | Canonical request/response JSON fixtures shared by all implementations |
| D | [Appendix D — Requirement Index](appendix-d-requirement-index.md) | Every requirement ID with its owning document (generated) |
| — | [`fixtures/`](fixtures/) | Machine-readable conformance fixtures (JSON) and their schema, replayed by every SDK's test suite |
| — | [`provenance.json`](provenance.json) | The upstream BastionVault ref this specification was derived from, and the pinned hash of every upstream source feeding it (CNF-044) |
| — | [`test-matrix.json`](test-matrix.json) | Server versions and environment variables for the live-server integration suite |

## How to use these specifications

1. **Implementers** read 00 → 04 first; they define the skeleton every other section builds on.
   Then implement 05 → 14 area by area. Each area is independently testable.
2. **Reviewers** use the requirement IDs (`CFG-003`, `KV2-011`, ...) to verify a pull
   request implements, tests and documents a requirement. See
   [Appendix D](appendix-d-requirement-index.md).
3. **Test authors** implement [15](15-testing-requirements.md) using the fixtures in
   [Appendix C](appendix-c-conformance-fixtures.md); the same fixtures are used by every
   language so behaviour stays identical across SDKs.
4. **Documentation authors** follow [16](16-documentation-requirements.md) and adapt the
   guides in [17](17-usage-guides.md).

## Requirement notation

Requirements use RFC 2119 keywords: **MUST**, **MUST NOT**, **SHOULD**, **SHOULD NOT**,
**MAY**. Each normative statement carries an identifier in the form `AREA-NNN`
(e.g. `ERR-007`). Identifiers are stable: they are never renumbered; a withdrawn
requirement keeps its ID and is marked *Withdrawn*.

## Versioning of this specification

The specification version is recorded in [00-overview](00-overview.md#specification-version).
An SDK release MUST state which specification version it implements. The version moves
under Semantic Versioning, applied to the specification's own requirements:

| Change | Bump |
|--------|------|
| A requirement is withdrawn, or an existing requirement's meaning changes such that a conforming SDK stops conforming | **major** |
| A requirement is added, or an existing one is extended in a way a conforming SDK may already satisfy | **minor** |
| Wording, formatting, cross-references or examples change with no normative effect | **patch** |

### Provenance: which server release this was derived from

The paragraph above dates this specification; it does not say what it was derived *from*.
That link is recorded machine-readably in [`provenance.json`](provenance.json)
(CNF-044…CNF-047): the pinned upstream BastionVault ref, and the git object id of every
upstream document and crate that feeds a specification document. `tools/provenance`
compares that pin against any later upstream ref and reports which specification
documents a server change touches, so drift is triaged in seconds rather than by
re-reading the server's API docs.
