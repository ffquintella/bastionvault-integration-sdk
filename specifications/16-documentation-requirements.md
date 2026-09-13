# 16 — Documentation Requirements

Documentation is a conformance requirement, not an afterthought. An SDK release with
missing or stale documentation fails the release checklist ([01](01-conformance-and-quality.md#release-checklist)).

## Mandatory documents

Every SDK MUST ship the following, in the language's conventional location (repository
`docs/` folder, package docs site, or both), with the exact titles below (translated to
the SDK's idiom where noted).

| # | Document | Contents |
|---|----------|----------|
| D1 | **README** | One-paragraph purpose; install snippet; a 15-line quick start (configure → login → read a KV v2 secret → handle one error); conformance level, spec version, tested server versions; the canonical-name mapping table (OVR §Naming); links to D2–D12; "Known gaps" (CNF-002); "Running integration tests" (ITG-004). |
| D2 | **Getting started** | Guide 1 from [17](17-usage-guides.md) adapted to the language, runnable end to end against a managed dev server. |
| D3 | **Configuration reference** | Every setting from [02](02-client-configuration.md), its env vars, default, and validation error code; precedence rules; TLS guidance; proxy behaviour. Generated from the same table the code uses where possible. |
| D4 | **Authentication guide** | Guides 2–4 from 17; token lifecycle; auto-renew; the login-failure-as-200 rule; machine identity and namespaces. |
| D5 | **Secrets (KV) guide** | Guides 5–7 from 17; v1 vs v2; CAS; soft delete vs destroy; environments; batch reads. |
| D6 | **Engine guides** | One page per engine at the SDK's conformance level (Transit, PKI, SSH, TOTP, LDAP, Files, Resources, Identity, Notifications, Cert lifecycle, Rustion), each with: what it is for, prerequisites (mount + policy snippet), 2–3 complete examples, error codes specific to the engine. |
| D7 | **Error reference** | Generated from the error catalog (ERR-036): every code, category, default message, hint, `Retryable`, HTTP triggers, server strings recognised. Includes the "reading an error" walkthrough and the one-line format (ERR-002). |
| D8 | **Resilience & operations guide** | Guides 9–10 from 17: cluster discovery, sticky sessions, `Reconnect`, retry policy, the rate gate, batch and pagination, cache coherence, observability hook and metric names. |
| D9 | **Vault compatibility gaps** | The ⚠️ items from [03](03-transport-and-protocol.md) and [06](06-system-api.md): error body shape, envelope fields, headers, `LIST` verb, health codes, `t`/`n` swap, missing leases/wrapping/cubbyhole/accessor endpoints, KV v2 route differences, cert auth disabled, `sys/mounts` two-field shape. |
| D10 | **Security guide** | What the SDK never logs; redacting types; token file handling; TLS defaults and how to pin a CA; why `TlsSkipVerify` is dangerous; reserved token metadata; recommended policies for SDK service accounts (least privilege examples). |
| D11 | **API reference** | Generated from doc comments for every public symbol (DocFX / `cargo doc` / Sphinx-autodoc or mkdocstrings). |
| D12 | **Changelog** | Keep-a-Changelog format; each entry lists added/changed/removed operations and new error codes; the spec version implemented. |
| D13 | **Contributing / testing guide** | How to run unit, conformance, contract and integration suites; how fixtures are loaded; how to add a requirement marker; coverage commands from [15](15-testing-requirements.md#coverage-measurement). |

- **DOC-001** Documents D1–D13 MUST exist with non-placeholder content before a release
  is tagged. CI MUST check for their presence (file existence + minimum word count of
  200 for D2–D10).
- **DOC-002** D3 and D7 MUST be generated from code (the configuration table and the
  error catalog) or verified against it by a test that fails when they drift.
- **DOC-003** Every code sample in D1–D10 MUST be compiled and executed in CI: .NET via
  a `docs-samples` test project referencing snippet files; Rust via doctests or
  `examples/`; Python via `pytest --doctest-glob` or `examples/` executed under the
  contract server. Samples that need a live server MUST run in the integration job.
- **DOC-004** Samples MUST show the **complete** call including error handling; snippets
  that elide error handling MUST say so in a comment.
- **DOC-005** Every public operation's doc comment MUST contain: purpose (one sentence),
  the HTTP call it performs (method + path template), parameters with wire names,
  return type semantics (including null/None cases), the specific error codes it can
  raise beyond the common set (ERR-061), the conformance level, and a link to the spec
  section/requirement ID.
- **DOC-006** Doc comments MUST use the canonical operation name in a `@spec`/`<spec>`
  tag (e.g. `<spec>Kv.V2.ReadSecret — KV2-001</spec>`) so the traceability tool can
  link docs to requirements.
- **DOC-007** Every hint in the error catalog MUST be reproduced verbatim in D7 so that
  searching the docs for an error message lands on its explanation.

## Structure and style

- **DOC-010** Guides MUST follow "task → prerequisites → steps → complete example →
  what can go wrong (error codes) → next steps".
- **DOC-011** Every guide MUST include the policy HCL the example needs, using the
  `PolicyBuilder` where the SDK offers one.
- **DOC-012** Wire-level JSON MUST be shown for at least one request/response per guide
  so readers can correlate with server audit logs.
- **DOC-013** Terminology MUST match the glossary in [00](00-overview.md#glossary).
  "AppID" is used for the `approle` auth type in prose; the wire type `approle` is
  mentioned once per page.
- **DOC-014** Documentation MUST not contain real tokens or hostnames; use
  `s.FAKE…`/`https://vault.example.com`.
- **DOC-015** Language-specific docs MUST link back to the specification section they
  implement, so a reader can compare behaviour across SDKs.

## Docs build gates (CI)

| Gate | Requirement |
|------|-------------|
| Presence | **DOC-020** D1–D13 exist (DOC-001). |
| Symbol coverage | **DOC-021** 100 % of public symbols documented (`<GenerateDocumentationFile>` + warnings as errors; `#![deny(missing_docs)]`; `interrogate --fail-under 100`). |
| Samples | **DOC-022** All samples compile/run (DOC-003). |
| Links | **DOC-023** No broken internal links (`lychee`, `markdown-link-check`, or equivalent). |
| Drift | **DOC-024** Generated tables (D3, D7) match code (DOC-002). |
| Spelling | **DOC-025** Spell-check with a project dictionary that includes the error codes and wire fields. |

## Publishing

- **DOC-030** The rendered documentation MUST be published per release (GitHub Pages,
  docs.rs, ReadTheDocs, NuGet README) and versioned so that users of older SDK versions
  can read matching docs.
- **DOC-031** The README on the package registry MUST be the same D1 file (single source).
