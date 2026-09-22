# Changelog

All notable changes to this repository are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the
project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html). The three
packages (`dotnet/`, `rust/`, `python/`) share one version number, because they ship one
specification; a release is cut only when all three match (CLA-003).

**Who maintains this file:** the Strategic Orchestrator (Claude), under the record-keeping
rules in [`agents.md`](agents.md) §11. Every change that a user of the SDKs, or a future
agent reading this repository, would need to know about gets a line here in the same change
that makes it. Behaviour is still decided in [`specifications/`](specifications/README.md);
this file records what happened, not what is required.

Sections used, in this order: **Added**, **Changed**, **Deprecated**, **Removed**,
**Fixed**, **Security**, **Agent architecture** (changes to `agents.md`, `claude.md`,
`skills/**` and the documents that govern agent behaviour — no package version implication).

## [Unreleased]

### Agent architecture

- `claude.md` §8 (new): Claude now uses the Cortex MCP server (`remember`/`recall`/
  `unified_search`/`checkpoint`) to carry working context across sessions —
  supplementary to, and never a substitute for, `decisions/`, `ROADMAP.md`, and
  `CHANGELOG.md` (**CLA-012**…**CLA-016**). Version bumped to `1.4.1` (**CLA-011**,
  **REC-007**).

## [0.14.1] — 2026-09-22

> **Risk `R-16` is closed: cluster discovery no longer ships inert or degrades silently.**
> A built-in, zero-dependency default SRV resolver (`DSC-050`) ships in `dotnet/`, and
> `DiscoveryConfig.StrictDiscovery` (default `true`, `DSC-015`…`019`) turns the previous
> silent single-candidate fallback into a loud, observable, strict-by-default one.
> ([DR-0014](decisions/0014-r16-srv-resolver-and-silent-discovery-degradation.md).) 1537
> .NET tests green, 99.09 % line / 95.09 % branch, no new runtime dependency.
>
> Also rolled up: `CacheWatcher` (`CCH-006`), M2a authentication parity for `rust/` and
> `python/`, and the accumulated fixes below. `rust/` and `python/` remain at `0.5.0`,
> frozen for Stage 1 (D-1, D-6) except the mechanical error-catalogue regeneration `R-16`
> needed for `BV-DISCOVERY-004`.
>
> **R3 human confirmation received.** The `specifications/13-*.md` behavioural change
> (`DSC-011`/`DSC-012` amendments, `DSC-015`…`019`, `DSC-050`) is R3 under `CRS-004`; the
> project owner confirmed it directly on 2026-09-22, satisfying `agents.md` §5.3's gate
> (recorded in [DR-0014](decisions/0014-r16-srv-resolver-and-silent-discovery-degradation.md)'s
> addendum). `R-16` is closed.

### Added

- **.NET: a built-in, zero-dependency default SRV resolver** (`DSC-050`) — cluster
  discovery now works without an application supplying an `ISrvResolver`. Queries the
  platform's configured nameservers by default; `DiscoveryConfig.Nameservers` overrides
  that with an explicit list, which is also the remedy for **R-26** (the default's
  platform nameserver discovery cannot see macOS's scoped resolvers on a split-horizon
  VPN). UDP first, with a mandatory TCP retry on a truncated response; absolute-only name
  qualification (a single-label cluster name is rejected, not guessed at); no caching of
  any answer; and a wire parser hardened against malformed and adversarial input —
  strictly-backward and hop-capped compression pointers, per-label and per-name size
  bounds, a resource-record walk that resumes at `RDLENGTH` regardless of what a target
  name parse consumed, a cryptographically random query ID with an ephemeral source port,
  and full ID/`QR`/echoed-question verification — since DNS is unauthenticated and this is
  a credential-handling SDK, even though TLS (not the resolver) remains the actual trust
  boundary (`RES-010`). An injected `ISrvResolver` always takes precedence over the
  default. This closes the remaining half of risk **R-16**, alongside the `DSC-015`…`019`
  loudness contract above.
  ([DR-0014](decisions/0014-r16-srv-resolver-and-silent-discovery-degradation.md)
  D-R16-6…D-R16-10.)

- **.NET: `CacheWatcher`** (`CCH-006`) — an optional helper that long-polls
  `Sys.CacheVersion` and raises a change event per topic whose epoch **increases**. A decrease
  is never reported as a change but does rebaseline, because `CCH-004`'s epochs are per node
  and reset on restart, so the rise after a reset is a real invalidation signal. It backs off
  exponentially on transport errors using **the client's own `RetryPolicy` curve** rather than
  a second one of its own, and stops terminally on `BV-AUTHZ-001`. Lifecycle follows automatic
  renewal: no `IDisposable`, no background task started for you — you own the task via
  `RunAsync(CancellationToken)` and cancel it to stop.
  ([DR-0013](decisions/0013-m8-transit-totp-and-efficiency.md) D-M8-53…D-M8-56.)

- **M2a authentication parity for Rust and Python.** `Auth.Token.*` and the token-store
  operations, the token source and token-file surfaces, and M2a's two harness instruments
  now exist in `rust/` and `python/` as well as `dotnet/`, unparking the pass deferred to
  Stage 2 by [DR-0006](decisions/0006-m2-authentication.md) D-6. Requirement content is
  M2a's (`AUT-014`, `AUT-020`, `AUT-080`, `AUT-085`, `CFG`, `TST`); no specification text
  and no public .NET behaviour changed.

### Changed

- **.NET: cluster discovery no longer degrades silently when no SRV resolver is
  configured.** `DiscoveryConfig.StrictDiscovery` (default `true`) makes a non-SRV-shaped
  cluster name that yields no SRV records raise `BV-DISCOVERY-004` instead of silently
  synthesising a single literal candidate — which also silently disabled failover, since
  failover needs two or more candidates. Set `StrictDiscovery = false` to keep the old
  fallback; when it fires (or when strict mode is off), `DiscoveryReport.Degraded` (a
  `DiscoveryDegradationCause`, not a boolean — the remedy differs for "no resolver
  configured" versus "resolver failed" versus "resolver returned nothing" versus "resolve
  timed out") and a client-logger warning naming the cluster and cause make the
  degradation observable instead of silent. `Client.Reconnect()` recomputes the cause on
  every call. This is `DSC-015`…`DSC-019`; see **Added** above for `DSC-050`, the default
  resolver that closes the other half of risk **R-16**.
  ([DR-0014](decisions/0014-r16-srv-resolver-and-silent-discovery-degradation.md) D-R16-3,
  D-R16-4, D-R16-5, D-R16-12.)

### Fixed

- **The M2a parity pass arrived with `s.FAKE…` token literals in Rust and Python test
  files, which `CNF-025`'s secret scan rejects.** The same defect .NET carried out of M1a
  and M1b (D-M1c-15). Fixed the way .NET fixed it — the tokens are assembled rather than
  written as literals, so the scan's whitelist is not widened and its pattern is not
  narrowed (**CLA-004**), and a real token pasted into a test would still be caught. Values
  are byte-identical to the literals they replace and to the ones the conformance fixtures
  carry, which the suites themselves assert.

- **Rust's fixture harness ignored three quarters of `RetryPolicy`, so `RES-003`'s backoff
  maths was never asserted there.** `configure()` read `MaxAttempts` and `InitialBackoff` and
  dropped `MaxBackoff`, `BackoffMultiplier` and `Jitter`, and it never wired the
  `settings.__jitter` sequence — so the client ran on default policy with the SDK's
  system-clock-seeded jitter source. `resilience.backoff.math-seeded` granted 100 ms/200 ms
  against a declared 80 ms/180 ms schedule, with the second wait unclipped by a `MaxBackoff`
  the harness had not applied, and nothing failed because the waits were not compared at all.
  The clock now implements D-M2-27's virtual-time mode (`delay: "virtual"`), records the waits
  it grants, and the driver asserts them against `clock.expectWaits` element-wise at ±1 ms,
  behind the same four-disjunct honour predicate .NET uses. The SDK's own
  `backoff_for_attempt` was correct throughout — this was the harness asserting nothing.

- **Python's `remaining_ttl` returned a negative value for an expired token.**
  [DR-0006](decisions/0006-m2-authentication.md) D-M2-24 ruled that `remaining_ttl` clamps
  to zero in all three languages, so that `None` keeps its single specified meaning of
  `creation_ttl == 0` (`AUT-014`). Rust satisfies this for free because `Duration` is
  unsigned; Python's `timedelta` is signed and the computation was a bare subtraction, so an
  expired token reported e.g. `-1:00:00`. Now clamped, with the expired-token assertion the
  language needed. See D-M2-24's addendum for why a type-system-satisfied ruling still needs
  an explicit test in the languages that do not get it free.

- **`dotnet/README.md` understated the SDK by four milestones.** Its "What works today" list
  stopped at KV — omitting cluster discovery (M5), the authentication remainder (M6), the
  `sys` remainder (M7) and the whole of M8 — and still described section 05 as a "subset"
  after M6 completed it and section 06 as "the Core subset" after M7 completed it. This is
  the page a consumer reads to decide whether the package does what they need, so a wrong
  capability list is worse than a short one. Now lists authentication and `sys` as complete,
  KV including `Kv.ReadMany`, Transit and TOTP, and section 14's rate gate, batching,
  pagination and cache coherence.
- `dotnet/README.md` now states two caveats a consumer would otherwise hit at runtime:
  **Transit and TOTP are typed bindings for the server's routes and the SDK performs no
  cryptography of its own** (the misreading that halted M8 once), and **cluster discovery
  needs an `ISrvResolver` you supply** because none ships — without one the client takes the
  single-address path (**R-16**). Also corrects `AUT-060`'s phrasing: the requirement is
  covered in code and is not a gap; what is outstanding is the written loopback-redirect
  recipe, which is M11's.

### Agent architecture

- `ROADMAP.md` and [DR-0013](decisions/0013-m8-transit-totp-and-efficiency.md): M8's closing
  bookkeeping. DR-0013 moves **proposed → accepted** and records that it was reviewed per
  slice at handback — seven gates, none passed first time — rather than by one up-front
  architecture round, so a later reader does not mistake the absent round for an absent
  review. M8's three forward obligations are written into the **M9 and M10 rows** rather than
  left in the decision record alone (the pending `Pki.ListCertificatesInfo` fixture → M9, the
  `ListUsersInfo` naming → M10; `CCH-006` had been handed to M10 too, and has since been
  implemented instead — see **Added** above): R-16 is this roadmap's own evidence that a
  gap a briefer will not look at survives a milestone. Also corrected: the §6 graph still
  labelled M8 `STANDARD` when it declares no level, and §9 still quoted M4's 234-ID gap count
  against the current 131. **R-14** records its second spent gate and **R-16**'s M8-merge
  blocker is discharged; **§10 question 6** asks the project owner to reconcile section 14's
  endpoint table against Appendix A once, rather than per instance (**R-27**).
- `ROADMAP.md` §2's state tables: the fixture count still read **241** (slices b and c) where
  slices d and e take it to **247**, and the fixture-driver registry still listed `kv.*` as
  "19 of the 20" with both `kv.read-many*` fixtures described as future work. Both landed at
  M8d. The registry row now also states the one real fixture gap rather than leaving it to be
  rediscovered — `efficiency.*` is **7 of 8**, and the eighth drives `Pki.ListCertificatesInfo`
  and is owned by M9.
- `ROADMAP.md` §2.1 was headed **"State as of M5"** over a table that had been maintained
  forward and carried five M8-current rows. The heading, not the rows, was the defect: it
  invited a reader to discount current figures as historical. Renamed, and the one genuinely
  stale row — traceability at M5's `216 of 421 covered, 205 baselined` — is now M8's exit
  figure, `294 of 425 covered, 131 baselined`.

## [0.14.0] — 2026-09-21

> **M9 is complete: sections 09 and 10 are bound in .NET.** `Client.Pki` (with `.Acme`,
> `.Csr`, `.SignRequests`), `Client.Ssh` and `Client.SshBroker` add roughly **91 operations**.
> **1504 .NET tests, 99.48 % line / 95.86 % branch**; traceability **294 → 305 covered,
> 131 → 120 baselined** of 425; **253 fixtures**, all seven of M9's green — including
> `sshbroker.effective-v2-pinned`, which had sat on disk undriven since the original
> specification import.
>
> **Section 10 clears the `CNF-002` gap list entirely. Section 09 does not.** `PKI-030`'s
> second limb requires recognising a queue-cap breach *by message*, and `BV-QUOTA-002` has no
> recognition row in Appendix B and no server message in any document. The ID **stays
> baselined** rather than report half a requirement as covered, and the gap is **R-31**
> (D-M9-11). `TRN-031` came off the baseline on evidence.
>
> **No conformance level is declared.** Sections 16–17 are M11's, and section 09 still carries
> `PKI-030`, so `CNF-002` forbids the claim (**R-14**) — unchanged since M4.
>
> **`rust/` and `python/` are unchanged at `0.5.0`**, frozen for Stage 1 (D-1, D-6), touched
> only for the fixture-count tripwire (250 → 253). The Python suite could not be run in this
> environment (no `pytest` module); its corpus assertion is verified by diff and by the .NET
> and Rust harnesses, and is **not** reported as green.
>
> **What review cost, and what it bought.** The framing record was blocked **three times** and
> every slice was blocked or returned at least once. Three separate repairs introduced a new
> defect while closing an old one — most sharply a GET form, added to satisfy a "bind both
> verbs" finding, that placed an export password in a query string `ErrorPaths.Redact` does not
> cover. Two of the milestone's own rulings were wrong and were reversed by the record itself,
> both because a column was read without its legend (D-M9-7, D-M9-13). Four risk rows open:
> **R-31**…**R-34**.

### Added

- **PKI engine bindings, part one (`Client.Pki`)** — roles, issuance (`Issue`, `Sign`,
  `SignVerbatim`), certificates and CRL, against
  [`specifications/09-pki-engine.md`](specifications/09-pki-engine.md). Covers `PKI-001`,
  `PKI-002`, `PKI-010`, `PKI-011` and `PKI-020`. Includes `Pki.ListCertificatesInfo` and its
  `PAG-004` iterator `ListCertificatesInfoAllAsync`. `Pki.ExportCertificate` returns the new
  redacting `PkiCertificateExport`, whose payload never appears in `ToString()` — the route
  can return a private key and section 09 defines no response shape for it
  ([DR-0016](decisions/0016-m9-pki-and-ssh.md) D-M9-16). The route binds **POST only**: the
  specification also lists `GET`, but the query form would place the export password in a URL,
  which `ErrorPaths.Redact` does not cover (D-M9-19, R-33).

- **PKI CA lifecycle, managed keys, tidy and ACME config (`Client.Pki`, `Client.Pki.Acme`)** —
  root and intermediate generation and signing, issuer management, the `config/urls`,
  `config/crl` and `config/issuers` surfaces, `ca`/`ca_chain`, managed keys, tidy and
  auto-tidy, and `Pki.Acme.ReadConfig`/`WriteConfig`/`DeleteConfig`/`DirectoryUrl`. The RFC
  8555 protocol paths are deliberately **not** wrapped (`09-pki-engine.md:115-117`).
  `Pki.GenerateRoot` and `Pki.GenerateIntermediate` hold exported private keys in
  `SecretString`; `Pki.GenerateKey`, whose response shape section 09 does not define, returns
  the redacting `PkiGeneratedKey` ([DR-0016](decisions/0016-m9-pki-and-ssh.md) D-M9-16).
  `Pki.ExportIssuer` binds POST only, for the reason `Pki.ExportCertificate` does (D-M9-22).
  Durations on `config/crl`, `tidy` and `config/auto-tidy` are sent as Go-style strings, the
  form those endpoints declare (`TRN-031`); `ttl`-shaped fields remain integer seconds.

- **PKI queues (`Client.Pki.Csr`, `Client.Pki.SignRequests`)** — the outbound CSR queue
  (`Generate`, `List`, `ListInfo`, `Read`, `Delete`, `SetSigned`) and the inbound
  sign-request approval queue (`Import`, `List`, `ListInfo`, `Read`, `Delete`, `Preflight`,
  `Approve`, `ApproveVerbatim`, `Reject`), with `PAG-004` iterators for both `*-info`
  listings. `Pki.Csr.Generate` returns the redacting `PkiGeneratedCsr`, holding an exported
  private key in `SecretString` (`PKI-002`). **Section 09 defines no response shape for any
  queue route**, so the rest return the raw response map and both listings return
  `Page<IReadOnlyDictionary<string, JsonElement>>`; typed records are booked as **R-32**
  ([DR-0016](decisions/0016-m9-pki-and-ssh.md) D-M9-10, D-M9-21).
  `Pki.SignRequests.Approve` accepts an untyped `overrides` map written flat beside `role`,
  and **rejects client-side with `BV-INPUT-001` if a key collides with a named field** —
  JSON decoders take the last duplicate key, so an override could otherwise outrank the
  `role` the caller passed, on the route that authorises issuance (D-M9-24).
  `PKI-030`'s first limb ships (`Reject` with an empty reason → `BV-INPUT-001`, no request
  issued); its queue-cap limb does not, and `PKI-030` stays on the traceability baseline
  because no document states the server message it would recognise (D-M9-11, **R-31**).

- **SSH engine and SSH broker (`Client.Ssh`, `Client.SshBroker`)** — CA configuration, roles
  (including `ListRolesInfo` and its `PAG-004` iterator), CA-mode signing, OTP-mode
  credentials, and the four-tier login-brokering policy surface. Covers `SSH-001`,
  `SSH-002`, `SSH-003`, `SSB-001`, `SSB-002` and `TRN-031`.
  `Ssh.WriteCertificateFile` writes `<key>-cert.pub` at mode **`0644` applied at file
  creation**, never by a `chmod` after the write, which would leave the file briefly at the
  process umask ([DR-0016](decisions/0016-m9-pki-and-ssh.md) D-M9-12); `0644` is
  world-readable by design, because an SSH certificate is public material.
  `Ssh.Creds`' OTP is a `SecretString`, and `Ssh.Verify` sends it in the request body only.
  `Ssh.ListRolesInfo` returns `Page<SshRole>` (D-M9-9) and, like every other `*-info` route,
  **follows `ApiPrefix` rather than pinning `/v2`** (D-M9-7).
  **The twelve `ssh-broker` routes are pinned to `/v2`** (`SSB-001`) — the one place in M9
  that pins, because section 10 requires it and Appendix A marks all three rows `v2`. The
  four policy writes send `PUT`: section 10 states that verb for `policy/global` and is
  silent for the other three, which follow it as siblings in one policy family and are
  booked to M12 for confirmation (D-M9-27).
  `SshRole`'s allow-lists are sent as CSV and **read from either CSV or a JSON array**,
  because section 10 pins the CSV form only for `valid_principals` — the tolerant read
  removes a silent-`null` path on an authorisation-relevant field (D-M9-29, **R-34**).

### Agent architecture

- [DR-0016](decisions/0016-m9-pki-and-ssh.md) records M9's framing and its handback rulings.
  It confirms that the `*-info` routes follow `ApiPrefix` rather than pinning `/v2`, which
  [`ROADMAP.md`](ROADMAP.md) R-27 and DR-0013 D-M8-45 had already settled and this record
  initially re-derived wrongly; all seven `*-info` rows are now checked against Appendix A and
  the third contradiction R-27 told readers to assume **does not exist** (D-M9-7). It also
  states the two-sided secret-handling rule the milestone's gates produced: a response that can
  carry key material returns a typed member or a redacting wrapper, and a request never places
  secret material in a path segment or query string (D-M9-16, D-M9-20).

## [0.13.0] — 2026-09-18

> **M8 is complete: all five slices.** `Client.Transit` (`TRS`), `Client.Totp` (`TOT`), the
> client rate gate (`EFF`), `Sys.Batch` and `Kv.ReadMany` (`BAT`, `KV-010`), cursor pagination
> (`PAG`) and cache coherence (`CCH`) are in — **38 of the milestone's 39 requirement IDs**.
> Traceability moves **267 → 294 covered, 158 → 131 baselined** of 425. **1336 .NET tests,
> 99.39 % line / 96.47 % branch**; 247 fixtures on disk.
>
> **The 39th was declined at the time of this release.** `CCH-006`'s `CacheWatcher` is a `MAY`;
> it was deferred to **M10** as a lifecycle surface that should earn its own design rather than
> be bolted on at the end of a milestone (D-M8-44).
>
> **Superseded after this release (2026-09-21):** the project owner directed that it be built,
> and it is now in — see `CacheWatcher` under `[Unreleased]`. The deferral's `MAY` ground was
> sound; its *lifecycle* ground was not, because the design already existed in automatic
> renewal's loop. M8 therefore stands at **39 of 39**, not 38.
>
> **No conformance level is declared, and the booked exit gate was unsatisfiable as written.**
> M8 was booked to "declare `Standard`", but `CNF-002` forbids claiming a level whose sections
> carry unimplemented MUSTs and sections 16–17 are M11's (**R-14**). `dotnet/README.md`'s
> `CNF-002` gap list is updated instead — 131 IDs, regenerated from the baseline rather than
> hand-counted. Resequencing is a project-owner decision that has not been taken.
>
> **`rust/` and `python/` are unchanged at `0.5.0`**, frozen for Stage 1 (D-1, D-6), touched
> only for the fixture-count tripwire. This release is .NET-only and is an explicit exception to
> the shared-version rule at the top of this file, on the `0.5.0` precedent (D-M2-15).
>
> **Two specification defects were found and deliberately not resolved here**, both for the
> project owner: section 14's endpoint table prefixes all seven `*-info` routes with `/v2/` and
> contradicts Appendix A on at least two of them (**R-27**), and the `Page<Namespace>` /
> `Page<NamespaceSummary>` contradiction between sections 06 and 14 still stands (D-M8-5).

### Added

- **.NET: the client rate gate is live** — a FIFO token bucket on every outgoing request
  (`RateGate { RatePerSecond = 8, Burst = 16 }` by default; setting *either* field to `0`
  disables it). A `429` carrying `Retry-After` pauses the whole queue for
  `min(Retry-After, 30s)` and drops the accumulated tokens; a `429` without one pauses for 1 s.
  The request that received the `429` fails with `BV-RATE-001` and is never replayed, and
  requests already queued are held rather than failed. Cluster-discovery health probes and DNS
  SRV resolution are exempt **by name**, not by omission, so an un-gated path cannot be mistaken
  for a forgotten one (`EFF-001`…`EFF-006`; [DR-0013](decisions/0013-m8-transit-totp-and-efficiency.md)
  D-M8-28…D-M8-35, D-M8-43).
- **.NET: `Sys.Batch(operations)`** — one `POST /v2/sys/batch` carrying up to
  `BatchMaxOperations` operations, each result carrying its own mapped error so the overall call
  succeeds even when every operation failed. Batches are sequential and **non-transactional**
  on the server; nothing in the API is named as though they were (`BAT-001`…`BAT-008`).
- **.NET: `Kv.ReadMany(mount, paths)`** — many KV v2 secrets in one request, falling back to
  `1 + N` sequential reads *through the rate gate* against a server that predates batching.
  Parked since M4 waiting on `BAT-007` (`KV-010`, `BAT-007`; DR-0009 D-M4-2 discharged).
- **.NET: `ClientConfig.BatchMaxOperations`** (default 128), settable through
  `BastionVaultClientOptions`. Constructor-only: `CFG-001`'s settings table names no environment
  variable for it, and inventing one would be a specification change (D-M8-36).
- **.NET: cursor pagination over the `*-info` listings** — `limit` defaults to 100 and is
  validated to `1…500`, the `after` cursor is passed verbatim as a key (never an offset), records
  arrive zipped to their keys and a length mismatch is `BV-PROTOCOL-002`. `ListNamespacesInfoAll`
  and `ListUsersInfoAll` walk pages until the server stops truncating, **through the rate gate**,
  under a `MaxRecords` safety cap (default 5000 → `BV-INPUT-005` carrying the server's `Total`).
  Section 14 names seven `*-info` endpoints; the five belonging to PKI, SSH and cert lifecycle
  arrive with those areas in M9/M10 (`PAG-001`…`PAG-007`; D-M8-7).
- **.NET: `Sys.CacheVersion(topics, watch?, ifNoneMatch?)`** — cache-coherence epochs, up to 64
  topics in a single comma-joined parameter, `If-None-Match` support with `304` mapped to a
  distinct `NotModified` result rather than an error, and a per-call timeout raised to at least
  40 s when long-polling. Epochs are **per node and reset on restart**, so only an *increase* is
  a change signal; a topic absent from the response means "not authorised or unknown" and is
  never synthesised as `0` (`CCH-001`…`CCH-005`).
- **.NET: `Auth.Userpass.ListUsersInfo`**, returning `UserSummary` (`Username`, `Fido2Enabled`).
  The wire's `registered_keys` is **deliberately not modelled**: the specification names the
  field once and gives no shape, and no captured fixture exercises it, so guessing would have
  put a silent wrong value in a public API. Adding it later is additive; guessing wrong would
  have been breaking (`D-M1c-25`, D-M8-47).
- .NET: section 14's three mandated guidance items are now in `dotnet/README.md` — never
  `map(read)` over a list, cache with a TTL and invalidate on `Sys.CacheVersion`, and treat a
  `429` as the client's fault rather than something to retry harder.

### Changed

- **.NET (breaking): `RateGateState` gains `AvailableTokens`**, so its positional constructor and
  `Deconstruct` take three members rather than two. The type is diagnostic-only and no package is
  published from this repository, so no consumer is broken in practice — recorded as breaking
  because the shape genuinely changed (`EFF-006`).
- .NET: a **disabled** rate gate now reports `AvailableTokens = int.MaxValue`. The invariant a
  caller may rely on is `AvailableTokens > 0` means "may proceed without waiting" (D-M8-35).

### Fixed

- .NET: `RateGate.IsDisabled` now reads **both** limbs — `EFF-001` says setting *either*
  `RatePerSecond` or `Burst` to `0` disables the gate, and only the first was checked. Rust has
  read both since M1a, so this closes a .NET-only gap rather than opening one (D-M8-32).
- .NET: `RateGateState.Paused` now expires with `PausedUntil` instead of latching `true` for the
  lifetime of the client, matching Rust and Python (D-M8-32).
- .NET: the paging iterator is now bounded on fetches as well as records. A server answering
  `{"keys":[],"records":[],"truncated":true}` never incremented the record count, so the
  `MaxRecords` cap could not fire and a null cursor restarted the walk — an unbounded loop of
  real requests, throttled by the rate gate and terminated by nothing (D-M8-50).

### Agent architecture

- `ROADMAP.md`: **"engine" now means typed REST endpoint bindings**, stated as a standing
  definition rather than as a post-mortem. M8 was halted by the project owner, who reasonably read
  "implement the Transit engine" as *build an encryption engine*; the SDK performs no cryptography
  (`specifications/00-overview.md`, Purpose and Non-goals). §4 and §5 now say "bindings", which is
  where the same misreading was queued to recur at M9 (`PKI`, `SSH`) and M10.
- [DR-0013](decisions/0013-m8-transit-totp-and-efficiency.md): slice b's fix `b-2` received the R3
  verdict it shipped without, and D-M8-27 records the one branch that seam leaves uncovered as a
  *deliberate stop* rather than as unreachable — the distinction D-M8-22 got wrong.

## [0.12.0] — 2026-09-18

> **M8 is incomplete: slices a, b and c of five.** `Client.Transit` (`TRS-001`…`013`) and
> `Client.Totp` (`TOT-001`…`004`) are in — 11 of M8's 39 requirement IDs. **Slices d and e are
> not started**, so `BAT` (8), `PAG` (7), `CCH` (6), `EFF` (6) and `KV-010` are still absent:
> there is no batch endpoint, no cursor-pagination helpers, no cache-coherence surface and **no
> client rate-gate token bucket** (only M1b's pause half). Section 14 is unimplemented.
>
> **No conformance level is declared**, and none can be: `CNF-002` forbids claiming a level whose
> sections carry unimplemented MUSTs, and sections 16–17 are M11's (**R-14**).
>
> **One gate was unclosed at the time of this release.** Slice b's required fix `b-2` shipped
> verified green but without its R3 verdict — the reviewing agent terminated on a session rate
> limit. What it closes is a false unreachability claim in an R3 decision record, so it warranted
> the re-run it did not get.
>
> **Closed 2026-09-18, after this release**, before slice d opened: the gate was re-run against
> the merged tree and returned *approve with required fixes* — four prose inaccuracies, **no code
> or test change**. The retraction was confirmed true by dataflow, and the omission of a
> `200`-with-empty-body case was confirmed legitimate. `v0.12.0`'s shipped behaviour is unchanged
> and was never in question; what was wrong was a statement of fact about it. See
> [DR-0013](decisions/0013-m8-transit-totp-and-efficiency.md) D-M8-27 and the b-2 gate re-run
> section.
>
> **`rust/` and `python/` are unchanged at `0.5.0`**, frozen for Stage 1 (D-1, D-6). This release
> is .NET-only and is therefore an explicit exception to the shared-version rule at the top of
> this file, on the `0.5.0` precedent (D-M2-15) — not a redefinition of it. The .NET package
> version also moves `0.9.0` → `0.12.0`, correcting drift: it had been stale against the `v0.10.0`
> and `v0.11.0` tags.
>
> **Minor, not patch**, because slices b and c are purely additive public surface —
> `Client.Transit`, `Client.Totp` and the new `SecretBytes` type. Nothing in the existing public
> API changed shape. The one observable change to *existing* behaviour is M8a's: five Appendix B
> recognition rules now fire where they previously fell through to the status table, so a caller
> matching on `BV-INPUT-100` or `BV-SERVER-005` for those five messages will now see the specific
> code. That is a fix to a defect, not a contract change — and no consumer can have depended on
> it, since nothing here is published to a package registry.

### Added

- **`Client.Transit` — the transit engine's REST bindings** (section 08, `TRS-001`…`TRS-013`).
  Key lifecycle (`ListKeys`, `CreateKey`, `ReadKey`, `DeleteKey`, `RotateKey`, `ConfigureKey`,
  `TrimKey`), the crypto endpoints (`Encrypt`, `Decrypt`, `Rewrap`, `Sign`, `Verify`, `Hmac`,
  `VerifyHmac`), datakeys (`GenerateDataKey`, `UnwrapDataKey`), `Random`, `Hash`, and a
  feature-gated `Transit.Byok.*` that surfaces `BV-SERVER-004` unchanged when the server lacks
  the feature. **No cryptography is performed in the SDK** — every operation is an HTTP call
  whose result the server computes (`00-overview.md` Non-goals). `Transit.ParseCiphertext`
  validates the `bvault:` framing client-side before `Decrypt`/`Rewrap` (`TRS-002`), binary
  inputs accept bytes or a pre-encoded `*Base64` string so a caller cannot double-encode
  (`TRS-003`), and `Random` rejects `bytes > 4096` without a request (`TRS-011`).
- **`Client.Totp` — the TOTP engine's REST bindings** (section 11, `TOT-001`…`TOT-004`). Key
  CRUD, `GenerateCode` and `ValidateCode`. **No OTP algorithm is implemented in the SDK**; the
  server owns the seed and the computation. `CreateKey` validates client-side before sending:
  exactly one of `generate`/`key`/`url`, `digits ∈ {6,8}`, `period ≥ 1`, a known algorithm, and
  `account_name` unless the `otpauth://` URL carries a label (`TOT-001`). Codes travel as
  strings, so a leading zero survives (`TOT-004`), and `ValidateCode` documents that a replayed
  code is indistinguishable from a wrong one at the API level (`TOT-003`).
- **`SecretBytes`**, the byte-oriented sibling of `SecretString`. Decrypted plaintext and datakey
  plaintext are returned in it so they redact in logs and in interpolation (`TRS-013`); TOTP
  seeds and `otpauth://` URLs use `SecretString` (`TOT-002`).
- **Five conformance fixtures**, completing Appendix C's `transit.*` and `totp.*` lists:
  `transit.encrypt-decrypt`, `transit.below-min-decryption`, `transit.random-cap`,
  `totp.generate-mode-create`, `totp.validate-false`. Corpus 236 → **241**.

### Fixed

- **A `TST-051` log-hygiene assertion that could not fail.** The TOTP seed-leak test scanned a
  log capture that is empty on the path under test — the only request-path log emission in the
  SDK fires solely for a server `warnings` array, which the test's response did not carry — so
  it passed without observing anything. It now asserts both captures are non-empty before
  scanning, scans a request observer as well as the logger, and covers the outbound leg, where
  a seed is most likely to leak and which had no test at all.

  **Stated precisely, because the first wording overclaimed:** neither scan can fail today
  either — the logged line is a server-supplied warning string, and `RequestEvent` carries no
  request body, response body or headers. `TOT-002` is proven by the redaction assertion on
  `Key`/`Url` plus the structural absence of bodies from both observability surfaces; the scans
  are a **regression tripwire** that begins to bite if a body or header dump is ever added to
  either. The security property held throughout; what was missing, and is now honestly
  bounded, is the proof. `TST-051` is the same instrument R-10 records as
  defined-but-never-executed once before, at M2a. See
  [DR-0013](decisions/0013-m8-transit-totp-and-efficiency.md) D-M8-23.
- **`Transit.Verify`/`VerifyHmac` reported an unparseable envelope as a cryptographic failure.**
  Both read `valid` through a helper returning `false` for anything that is not JSON `true`, so a
  `200` with an absent or non-boolean `valid` — a proxy answering `{"data":{}}`, say — surfaced as
  "signature invalid" rather than a protocol error. `TRS-012` requires `false` *from
  `{valid: false}`*; an absent field is not that.
- **Five unguarded base64 decodes of server output** in the transit bindings threw a raw
  `FormatException`, escaping the error model with no code and no hint.
- **A malformed `barcode` was silently swallowed** as `null`, making a corrupt value
  indistinguishable from the legitimate absence when `generate && exported` is false (`TOT-002`).

### Agent architecture

- **R-16 framed and ruled: cluster discovery will no longer ship inert.** Splits R-16 into a
  resolver decision and an independent silent-degradation contract, and rules both: a hand-rolled
  zero-dependency SRV resolver ships in core with the nameserver list as an injected input, and
  `DSC-015`…`DSC-019` make degradation logged, programmatically observable by cause, refusable by
  a strict mode that **defaults to strict**, stable across `Reconnect()`, and carrying its own
  error code. Re-tiers R-16 R2 → R3 (`CRS-004`) and reassigns it from M11/M12. Supersedes
  [DR-0010](decisions/0010-m5-cluster-discovery-and-resilience.md) D-M5-23. No behaviour has
  changed yet: the section 13 edit carries `agents.md` §5.3's R3 human confirmation, and
  implementation is sequenced behind the M8 merge. The accepted residual is tracked as R-25's
  neighbour R-26 (macOS scoped resolvers). See
  [DR-0014](decisions/0014-r16-srv-resolver-and-silent-discovery-degradation.md).
- **`CRS-004`'s published-artefact limb is documented as currently vacuous**
  (`skills/claude/SKILLS.md` §5, **REC-006**). Nothing in this repository publishes to a package
  registry — `build-artifacts.yml` builds and never pushes — so an R3 resting on "published
  artefact" rests on nothing until the first real publication, and the rule now says to name the
  limb a tier actually stands on. Written because two independent agents conflated the two limbs
  in one session, in opposite directions, and both tiers survived only on the `specifications/`
  limb.
- **Two verified findings recorded as risks rather than fixed in place.** **R-24**: Rust does not
  discharge `FIX-001` — its fixture validation checks only that the root is a JSON object and
  never validates against `schema/fixture.schema.json`, while Python and .NET both do, so four
  Rust call sites assert `all repository fixtures must validate` against an implementation that
  does not. **R-25**: the corpus count is hand-transcribed into three languages and the three
  loaders exclude non-fixture JSON by three different rules. Both predate M8 and neither is fixed
  inside it — `rust/` is frozen for Stage 1 (D-6) and folding either in would give one defect two
  owners (CLA-008).

### Fixed

- **Five Appendix B §2 recognition rules can fire again (R-23).** `tools/error-catalogue`
  compiled a qualifier group — `` `stem` + `a`/`b` ``, or a parenthesised list — as a
  *conjunction* where the appendix means an *alternation*, so `BV-INPUT-102`,
  `BV-INPUT-103`, `BV-AUTH-011`, `BV-TRANSIT-004` and `BV-SSH-005` were unreachable for
  every real single-phrase server message: each fell through to the status table as
  `BV-INPUT-100` or `BV-SERVER-005` instead. `BV-AUTH-011` has been unreachable since
  `v0.5.0`. The compiled rule gains `containsAny` beside `containsAll` in all three
  languages, and `Sys.RestoreAsync`'s operation-local remap deleted with the defect it was
  covering, as it was designed to. `BV-INPUT-103` now also answers at a status other than
  `500`, which its Appendix B row never qualified. All seven codes involved are
  non-retryable, so no observed retryability changes — R-23's claim that it did is corrected
  in `ROADMAP.md` §8. See [DR-0013](decisions/0013-m8-transit-totp-and-efficiency.md)
  D-M8-2, D-M8-3 and D-M8-8…D-M8-13.
- **The generated recognition fixtures no longer certify the bug they are meant to catch.**
  `errors.recognition.bv-input-102.1` answered a message containing *both* alternatives, so
  it passed identically under either semantics — as did the `SYS-042` and `AUT-012` unit
  tests. The generator now emits one fixture per alternative: 124 → 130 generated,
  `errors.*` 134 → 140, corpus 230 → 236. Without this the corpus would have certified the
  defect into Rust and Python at Stage 2 (R-19's shape; D-M8-3, D-M8-9, D-M8-12).
- **`main` had been red on the Rust and Python workflows since `0.10.0` (2026-09-15).** Both
  harnesses asserted a shared-corpus size of 218 while the committed corpus had moved on:
  run 35006009906 failed `224 == 218` at M5 and run 35344102617 failed `230 == 218` at
  M6/M7. The mechanism is that .NET's count was maintained per milestone and the other two
  were not — the second occurrence, not the first. All three now read 236 in one place each.
  Test data only: no library code, no fixture and no assertion changed beyond the stale
  count (CLA-004, TST-010, FIX-001).

## [0.11.0] — 2026-09-18

> **M6 (authentication remainder) and M7 (System API remainder) are both complete in .NET**,
> continuing the Stage 1 exception the shared-version rule at the top of this file describes.
> `rust/` and `python/` are unchanged (D-1, D-6). **No conformance level is declared** —
> sections 16–17 remain M11's, so CNF-002 still forbids the claim (R-14). Sections **05 and 06
> now have no unimplemented MUST in .NET**, with one stated exception: `AUT-060`'s
> loopback-redirect recipe belongs to the usage guides and is M11's.

### Added

- **.NET: the remainder of section 05 (M6)** — FIDO2 login on the userpass and standalone
  mounts (`AUT-035`); the FerroGate machine-identity method, its cached
  `IsMachineIdentityRequired` convenience and its administration surface (`AUT-050`…`AUT-054`);
  OIDC and SAML with role and config administration (`AUT-060`); `Auth.Cert.Login`, which maps
  a disabled `cert` backend to `BV-SERVER-004` with a hint naming the backend rather than the
  mount it was reached at (`AUT-070`); and the full AppID role-administration surface under
  `Auth.AppId.Admin` (`AUT-043`). See
  [DR-0011](decisions/0011-m6-authentication-remainder.md).
- **.NET: the remainder of section 06 (M7)** — `Sys.InitStatus`/`Init`/`Seal`/`Unseal`, where
  `Init` returns a disposable `InitResult` whose key shares and root token are redacted and
  zeroed on dispose (`SYS-010`…`SYS-013`); the mount surface including the 60-second
  `Sys.MountTypeOf` cache (`SYS-020`…`SYS-026`); auth-method administration (`SYS-030`); the
  `policies/acl` surface with the legacy `rules` key read through `Sys.Legacy.*`, reserved-name
  refusals, a `PolicyBuilder` HCL emitter, and the `/v2`-pinned dry-run whose tri-state
  `policies` field is preserved on the wire (`SYS-040`…`SYS-045`); the namespace surface with
  its full-replace warning and read-merge-write companion (`SYS-060`…`SYS-062`); audit device
  administration and event query (`SYS-070`); `Client.Identity`'s `/v2`-pinned self-service
  surface (`SYS-080`); `Sys.Backup`/`Sys.Restore`, excluded from retry and failover
  (`SYS-090`, `SYS-091`); `Sys.SealClusterWide`/`Sys.UnsealClusterWide` returning a per-node
  result map (`RES-030`); a "Vault compatibility gaps" surface for the routes the server does
  not serve (`SYS-100`, `SYS-101`); the v2-only `Sys.HsmStatus`; and the Complete-tier DoS,
  dashboard, SSO, owner-transfer and exchange surfaces. See
  [DR-0012](decisions/0012-m7-system-api-remainder.md).
- **.NET: `Kv.DetectVersion`** (`KV-001`), deferred at M4 solely because `SYS-026` did not
  exist (DR-0009 D-M4-2). It now resolves a mount's KV version through `Sys.MountTypeOf`.

### Changed

- **`Sys.TestPolicy(draft, cases, name: "root")` now sends the request** and maps the server's
  `400` to `BV-INPUT-010`, instead of refusing client-side, so `Attempts` and `StatusCode`
  reflect the round trip. `SYS-045` states its refusals as HTTP status codes where `SYS-041`
  states its own as client-side, and the distinction is deliberate (DR-0012 D-M7-26,
  overturning D-M7-17).

### Fixed

- **The relogin replay no longer gets a fresh attempt budget** (`RES-001`, `AUT-003`). A
  `Login` token source that re-authenticated on `BV-AUTHZ-001` started its replay with a full
  `MaxAttempts`, so a client configured for 3 could reach 6 attempts with no failover involved
  — measured, not inferred. The replay is now clamped to what is left of the cap, exactly as
  D-M5-28 clamped the failover replay. **This closes R-18 with a residual:** both clamps floor
  at one attempt, so a call that fires *both* replays still reaches `MaxAttempts + 2`.
  Resolving that means amending `RES-001`, because removing the floor would breach `DSC-042`
  — tracked as **R-22** (DR-0011 D-M6-21).
- **Two per-client caches served one namespace's answer to another** (**R-21**).
  `Sys.MountTypeOf`'s mount-type cache (`SYS-026`) was keyed by the client view's namespace
  while the request went out under the per-call `RequestOptions.Namespace` override, so an
  override could store one tenant's mount table under another tenant's key, answer a
  differently-directed lookup from it with no request issued, and invalidate the wrong tenant
  on a mutation — reaching `Kv.DetectVersion` as a wrong `KvVersion` across a tenancy
  boundary. `Auth.Ferrogate.IsMachineIdentityRequired`'s cache (`AUT-051`) was keyed by mount
  alone with no expiry, so it answered **permanently** and **failed open**: a namespace that
  does not require a machine identity, asked first, would tell a namespace that does that it
  does not. Both are now keyed by the effective namespace. A tree-wide audit found no third
  instance (DR-0012 D-M7-25, DR-0011 D-M6-16).
- **`Auth.Cert.Login` no longer reports a mistyped mount as a disabled backend**, and its hint
  names `cert` rather than interpolating the caller's mount, which is what `AUT-070` asks for
  (DR-0011 D-M6-18).
- **`require_machine_identity` is read by value, not by exact JSON spelling.** The gate
  previously treated anything other than literal `true` — including the string `"true"` and
  `1` — as false, which is the unsafe default for a machine-identity check (DR-0011 D-M6-17).
- **A restore rejected for a bad magic number, an unsupported version or corruption** now
  reaches the caller as the non-retryable `BV-INPUT-103 BackupFileInvalid` rather than the
  retryable `BV-SERVER-005`. The generated recognition rule for that form can never fire, so
  the mapping is made at the operation for now; the underlying generator defect is **R-23**
  and the workaround deletes when it is fixed (DR-0012 D-M7-36).
- **A `404` from `Kv.V1.Read` on a KV v2 mount** now carries `ERR-040`'s
  `<mount>/data/<name>` note — the last deferred enrichment row, re-booked from M4 (DR-0012
  D-M7-43).
- **`Sys.OwnerTransfer.*` and `Sys.Exchange.*` refuse a `default(JsonElement)` body** with
  `BV-INPUT-001` instead of surfacing a runtime `InvalidOperationException` (DR-0012 D-M7-44).
- **The `CNF-025` secret scan was red on `main`**, and on both milestone branches, from
  placeholder token literals in test files outside the `specifications/fixtures/**` whitelist.
  Repaired in the literals; the pattern and the whitelist are untouched (D-M0-18, CLA-004).

### Security

- `InitResult` holds the unseal key shares and the root token in `char[]` buffers it zeroes on
  dispose, exposes them only through the redacting `SecretString`, and throws
  `ObjectDisposedException` after disposal (`SYS-011`). Three residues are recorded rather
  than claimed away: JSON parsing materialises transient strings that cannot be zeroed, every
  read allocates a fresh unzeroable copy, and `Reveal()` output is the caller's to manage
  (DR-0012 D-M7-2, D-M7-32).

## [0.10.0] — 2026-09-15

> **M5 (cluster discovery and bounded failover) is complete in .NET**, continuing the Stage 1
> exception the shared-version rule at the top of this file describes. `rust/` and `python/`
> are unchanged (D-1, D-6). **No conformance level is declared** — sections 16–17 remain
> M11's, so CNF-002 still forbids the claim (R-14).

### Changed

- **Specification version 1.0.0 → 1.1.0** — four additive requirements (`CNF-044`…`CNF-047`)
  and a new release-checklist item; no existing requirement changed meaning and none was
  withdrawn, so the bump is minor under the rule now stated in
  [`specifications/README.md`](specifications/README.md) (DR-0011 D-PRV-10).

### Added

- **Specification provenance tracking** — `specifications/` now records, machine-readably,
  which BastionVault release it was derived from. `specifications/provenance.json` pins the
  upstream ref plus the **git object id** of every upstream document and crate tree that
  feeds a specification document, and names the specification documents each one feeds;
  `tools/provenance` compares that pin against any later upstream ref and reports which
  specification documents a server change touches. Git object ids are used because the
  GitHub tree API and a local `git ls-tree` return identical values, so one manifest serves
  CI (no checkout) and a maintainer's working copy (no network). Upstream documents are
  pinned as `authoritative` and can block a release; upstream crate trees are pinned as
  `corroborating` and are reported without blocking, which keeps refactor noise out of exit
  status while still catching behaviour that changed without the docs following.
  The baseline is pinned at `v0.42.0` — the floor the specification itself declares —
  rather than at current upstream, so nothing is assumed reviewed that was not.
  `tools/provenance/provenance.py` re-pins the manifest (`--update`), compares it against
  any upstream ref (`--check`) or a local checkout (`--verify-local`), and distinguishes
  `unchanged` / `changed` / `missing` / `unknown` — an upstream lookup that could not be
  performed exits non-zero rather than reading as clean. CI runs it non-blocking against
  `main`. New requirements `CNF-044`…`CNF-047`, a new release-checklist item, and an
  explicit minor/patch bump rule for the specification's own version;
  [DR-0011](decisions/0011-specification-provenance-tracking.md).

  All three SDKs expose the pin next to the specification version they implement —
  `SdkInfo.SpecificationSourceRelease` / `SpecificationSourceRef` in .NET,
  `specification_source_release()` / `specification_source_ref()` in Rust and Python — so a
  deployed application can report what it was built against without the repository in hand
  (`CNF-047`, D-PRV-8). A test in each language asserts the accessor against
  `provenance.json`, so the two cannot drift. Landing all three at once is a recorded
  exception to the Stage 1 freeze on `rust/` and `python/` (D-PRV-9): the value is a
  property of `specifications/`, not of any .NET contract, so no later milestone can
  revise it.

- **.NET: cluster discovery (M5a)** — DNS SRV resolution through an injectable
  `ISrvResolver`, parallel health probing bounded by `HealthConfig.Parallelism`,
  deterministic node ranking, and the `Client.Discover()` diagnostics table. New public
  surface: `NodeState`, `DiscoveryConfig`, `HealthConfig`, `SrvRecord`, `ISrvResolver`,
  `Candidate`, `ProbeResult`, `NodeSelection`, `DiscoveryReport`, plus `InputLabel`,
  `SelectedNode`, `ConnectAsync`, `DiscoverAsync` and `ReconnectAsync` on
  `BastionVaultClient`. `ConnectAsync` and `ReconnectAsync` return `null` for a
  literal-mode address — DSC-001 makes literal mode "no DNS, no probing", so nothing is
  probed and nothing is selected; `DiscoverAsync` probes such a client deliberately,
  because diagnostics are an explicit operator request that yields no pick.
  **No SRV resolver ships**: the .NET BCL exposes no DNS SRV API, DSC-014 requires the
  resolver be injectable rather than shipped, and an application that supplies none takes
  DSC-011's "no records" path — so cluster discovery is inert until one is supplied
  (risk R-16). `DSC-001`, `DSC-002`, `DSC-010`…`DSC-014`, `DSC-020`…`DSC-022`,
  `DSC-030`…`DSC-036`, `RES-010`, `RES-011`, `RES-020`, `RES-021`, `CFG-043`;
  [DR-0010](decisions/0010-m5-cluster-discovery-and-resilience.md).

- **.NET: sticky sessions with bounded failover (M5b)** — after discovery pins a node all
  requests go to it, and an idempotent operation that meets a node failure fails over
  **once**: the cached candidate set is re-probed with no new SRV lookup, the failed URL
  excluded, and the request replayed against the new pick. Writes and deletes are never
  replayed (ambiguous commit). Concurrent failures serialise on one lock, so a
  cluster-wide outage re-probes once and a late arrival reuses the new pick.
  `Client.Reconnect()` re-runs full discovery and is safe to call concurrently. Operations
  DSC-045 marks node-local are excluded from failover through an internal seam; every
  operation on that list belongs to M6, M9 or M10, so no public option is minted for it
  yet. `DSC-040`…`DSC-046`;
  [DR-0010](decisions/0010-m5-cluster-discovery-and-resilience.md).

### Changed

- **.NET: a transport-level failure on a node cluster discovery selected is now
  `BV-DISCOVERY-003 NodeUnavailable`**, carrying `Details.host` and `Details.reason`,
  instead of `BV-TRANSPORT-001`/`BV-TRANSPORT-002`. Scope is deliberately narrow and is
  the milestone's load-bearing decision (D-M5-5): only connection-refused, reset and
  timeout reclassify, only in discovery mode. A literal address keeps its existing codes,
  and DNS and TLS failures keep theirs in both modes. A `5xx` whose **server** message
  names `sealed`, `uninitialized` or `standby` keeps its own Appendix B code
  (`BV-SERVER-001`/`003`/`007`) in both modes and only triggers the failover replay —
  reclassifying it would have inverted `CFG-053`'s never-retry rule for a sealed node and
  contradicted section 13's own statement that `BV-SERVER-003` is retried via failover.
  `BV-DISCOVERY-003` is **not** added to the default `RetryOn` (`CFG-050` pins that list):
  the SDK's automatic recovery for a dead node is the single replay, never a backoff retry
  of the same node (D-M5-6). Total attempts still never exceed `MaxAttempts + 1`
  (`RES-001`).

- **.NET: `ClientConfig.AddressIsClusterName` and `AddressUri` now report DSC-001's
  classification.** A `host:port` or bare-IP address is **literal** where it was
  previously reported as a cluster name (D-M5-18); an `http://` address on a *portless*
  bare DNS name is cluster discovery with the scheme forced to http, while an explicit
  `:port` — including `:80` — keeps it literal (D-M5-21). Insecure http is still refused
  without `AllowInsecureHttp` (`CNF-035`), and the guard was re-keyed onto the classified
  scheme so `http://` plus discovery cannot pass it.

### Fixed

- **The `CNF-025` secret-scan gate was red on `main` and nobody knew.** M5's two new test
  files carried six `s.<20+ alnum>` placeholder literals outside the
  `specifications/fixtures/**` whitelist, which is the gate's only permitted exception. The
  tokens were obviously fake, but the gate does not read intent and D-M0-18 pins the pattern
  and the whitelist as specified, so neither was narrowed to make the scan pass (CLA-004).
  Repaired in the literals instead — a hyphen breaks the alphanumeric run the pattern
  requires. Found by a delegate's repo-gate sweep, not by M5's own handback: **this is the
  fourth gate this project has certified by a record rather than by an execution**
  (D-M1b-19, D-M1c-15, D-M1c-16). The standing instruction to run the full gate set, not
  just `dotnet test`, before calling a milestone done is now overdue rather than optional.

- **A conformance fixture encoded a KV v2 response the specification forbids.**
  `specifications/fixtures/resilience/resilience.failover.read-once` returned
  `data.metadata` as `{"version": 1}`, but section 07's type block declares that object as
  `{version, created_time, deletion_time, destroyed}` with `created_time` non-optional, and
  D-M4-12 ruled its absence a server-contract violation raising `BV-PROTOCOL-002`. The
  fixture predates M4 and contradicted the section it exercises; the reader was right, so
  the fixture is repaired rather than the ruling relaxed (D-M5-26). Four lines in one
  response body; no requirement, behaviour, error code or public API changes. Rust and
  Python inherit the corrected body when they reach M5 in Stage 2. **R3 under CRS-004:**
  the human confirmation §5.3 requires for a `specifications/` change is carried to M12's
  release checklist, recorded in `ROADMAP.md` §5.

- **The test harness's mock-server certificate is now accepted by current OpenSSL.** All
  three in-process HTTPS mock servers issued a CA and a leaf certificate with no Subject
  Key Identifier and no Authority Key Identifier, which OpenSSL 3.5+ (shipped with Python
  3.14) rejects during chain verification. Python's suite had 12 failures on 3.14 and none
  on 3.12, so CI — which pinned 3.12 — was green on a harness that did not work. Both
  extensions are now issued in .NET, Rust and Python, `KeyUsage` is set explicitly, and
  `python.yml` runs a 3.12 **and** 3.14 matrix so a version-only failure cannot hide again
  (risk R-15). Test-harness and CI only; no SDK behaviour changes.

### Security

- **`rustls` bumped from 0.23.40 to 0.23.45 (RUSTSEC-2026-0285).** The pinned version
  accepted TLS 1.3 handshake messages across encryption-level boundaries (CVSS 5.3), which
  failed the `cargo audit` gate (CNF-024) the day the advisory was published. The exact-pin
  convention for Rust dependencies means the lockfile alone could not carry the fix, so the
  `Cargo.toml` pin moved too. No SDK code changed and the crate's public API is unchanged
  (CNF-027); `rustls` is not re-exported. Rust only — .NET and Python do not use `rustls`,
  so there is no parity obligation here (CLA-003).

## [0.9.0] — 2026-09-15

> **M4 (KV engine) is complete in .NET**, continuing the Stage 1 exception the shared-version
> rule at the top of this file describes. `rust/` and `python/` are unchanged from `0.5.0`
> apart from a test-data count (D-1, D-6). **No conformance level is declared** — see the
> `Changed` entry below, which is the more important half of this milestone.

### Added

- **`Client.Kv`, the KV secrets engine (M4, .NET only).** Version-explicit `Kv.V1` and
  `Kv.V2` sub-clients: v1 read/get/write/delete/list; v2 read/get/write/soft-delete/
  undelete/destroy, version metadata, engine config (`ReadConfig`/`WriteConfig` replace,
  `UpdateConfig` read-merge-write), per-environment overrides via `PatchEnvironment` and
  `WriteAllEnvironments`, the `DataPath`/`MetadataPath`/`DestroyPath`/`UndeletePath`
  helpers, and the `WriteIfAbsent`/`UpdateWithRetry`/`ReadField` conveniences. A credential
  carrying AppID environment scoping now fails fast client-side with `BV-KV-009` at zero
  requests rather than collecting a server `403`, and a `403` on a KV v2 data read sent
  without `env` gains the "the policy may require `env`" note.
  (`KV-002`, `KV1-001`…`KV1-004`, `KV2-001`…`KV2-011`, `KV2-020`…`KV2-024`, `KV2-030`,
  `KV-011`…`KV-013`; [DR-0009](decisions/0009-m4-kv-engine.md); baseline 259→234.)
- **A duration type is accepted for KV v2's `DeleteVersionAfter`** —
  `KvV2Config.DeleteVersionAfterDuration` on the read side and
  `KvV2ConfigPatch.DeleteVersionAfterDuration` on the write side, parsed and formatted
  Go-style, with `"0s"` meaning disabled. A patch whose string and duration forms disagree
  is rejected client-side as `BV-INPUT-001`. The wire form is unchanged; this closes the
  half of `KV2-010` that the first KV pass left as a string-only surface.
- **The first per-language README, `dotnet/README.md`** (`CNF-002`, `CNF-041`). It states
  the conformance target, the known gaps by requirement ID, the specification revision, and
  that the SDK's behaviour is fixture-derived and has never met a live server.

### Changed

- **No conformance level is declared, and `Core` was not declarable at this milestone.**
  `Core` requires every MUST of specification sections **16** and **17** — the
  documentation and usage-guide requirements — which are booked to M11, after the
  milestones that were supposed to declare `Standard` and `Complete`. `CNF-002` forbids
  claiming a level whose sections carry unimplemented MUSTs, so the README claims none and
  lists what is missing instead. The declaration schedule itself needs resequencing; that
  is a project-owner decision, recorded in `ROADMAP.md` §10 and
  [DR-0009](decisions/0009-m4-kv-engine.md) D-M4-3.
- **Two of section 07's requirements are deferred with named owners rather than guessed at:**
  `KV-001` (`Kv.DetectVersion`) waits on `Sys.MountTypeOf`/`SYS-026` at **M7**, and
  `KV-010` (`Kv.ReadMany`) waits on `Sys.Batch`/`BAT-007` at **M8**. Both stay on the
  traceability baseline and both are named in the README's gap list (D-M4-2). The
  version-agnostic `Kv.ReadSecret` façade `KV-002` permits is deliberately **not** offered,
  because without version detection it could only guess (D-M4-9).

### Fixed

- **A pre-encoded request path no longer swallows its query string.** The transport's path
  builder never split the query off a path it had been told was already encoded — harmless
  only because login was the single pre-encoded caller. KV v2 puts `?version=` and `?env=`
  on exactly that path (`KV2-001`).
- **A caller-supplied KV secret path or mount can no longer re-route a request.** Every
  segment is percent-encoded, and a `..` **segment** is refused on every KV path, prefix and
  mount, including the `KV2-030` path helpers whose output is meant to be handed to
  `Sys.Batch` or pasted into a policy document. Percent-encoding cannot neutralise `..`,
  because `.` is unreserved. Same defect class as the Userpass path injection fixed at M2b
  (D-M4-11).
- `Kv.V2.WriteSecret` validates the keys of `KvWriteOptions.Envs` exactly as it validates
  `Env` — previously one write shape rejected a control character in an environment name
  and the other did not (`KV2-002`).
- `Kv.V1.Write` rejects a negative `ttl` client-side instead of sending a negative Go
  duration the server gives no meaning to (D-M4-13).
- **`SdkInfo.SdkVersion`, and therefore the default `User-Agent`, said `0.7.0` while the
  package shipped as `0.8.0`.** The `0.8.0` release bumped the csproj and not the constant,
  and the `CNF-041` test that should have caught it asserted the stale literal, so it stayed
  green throughout. Both now read `0.9.0`, and that test reads the version out of the built
  assembly instead of restating it — a literal is what made the drift invisible (the R-10
  shape: a gate trusting a record over an execution).

### Security

- An environment-scoped credential performing a KV v2 data operation without `env` is
  refused client-side, so a token whose scope the server would reject never reaches the
  wire (`KV2-022`).

## [0.8.0] — 2026-09-15

> **This release is .NET only for M3**, continuing the Stage 1 exception the shared-version
> rule at the top of this file describes. `rust/` and `python/` are unchanged from `0.5.0`
> (D-1, D-6). **M3 (System API, Core subset) is now complete in .NET.**

### Added

- **`Client.Sys`, the System API's Core subset (M3, .NET only).** `Health`, `SealStatus`,
  `ServerInfo`, `ClusterStatus`, `CapabilitiesSelf` and the `Can` convenience
  (`SYS-001`, `SYS-002`, `SYS-005`, `SYS-006`, `SYS-008`, `SYS-050`…`SYS-053`). `Health`
  and `ServerInfo` follow the existing anonymous/authenticated tiering; `SealStatus`
  exposes the server's swapped `T`/`N` alongside derived `KeyShares`/`KeyThreshold`;
  `ClusterStatus` surfaces its documented `403` as `BV-AUTHZ-003` like any other mapped
  error; `Capabilities.Can`/`Sys.CanAsync` both cover the convenience check, one with and
  one without an extra round trip. `SYS-006` is a newly minted requirement — the spec had
  reserved the slot for `ClusterStatus` but never written it.
  ([DR-0007](decisions/0007-m3-system-api-core.md); baseline 267→259, +1 minted/-9 landed.)

### Fixed

- **`CNF-023` (.NET analyzer/style gate) had never fired.** `dotnet/.editorconfig`'s bulk
  `dotnet_analyzer_diagnostic.category-<X>.severity = none` lines silently overrode every
  `dotnet_style_*`/`csharp_style_*` option-embedded severity beneath them, and two option
  keys were not valid Roslyn keys — `dotnet build` had never failed on a real style
  violation (R-11, found by M2c's R-10 sweep, `decisions/0001-m0-harness-gate-proof.md`
  addendum Row 7). Fixed by adding a literal `dotnet_diagnostic.<ID>.severity` override for
  every already-declared option and correcting the two invalid keys — no rule added or
  dropped. Every violation the fix surfaced across both .NET projects was corrected in
  code, not suppressed; the gate-fires proof was re-run by seeded violation and revert.
  ([DR-0008](decisions/0008-r11-cnf-023-remediation.md); `ROADMAP.md` §8 R-11 closed.)

### Agent architecture

- **A self-versioning governance document's `Version:` header must bump in the same
  commit that substantively edits it — now written down as a rule (`agents.md` §11
  **REC-007**, `claude.md` **CLA-011**).** This convention already existed by hand across
  five prior `ROADMAP.md` edits (1.0.0 → 1.5.0), but the M2c commit substantively edited
  `ROADMAP.md` (§2, §4, §5, §6, §8) without bumping it — an instance of the unwritten rule
  being missed precisely because it was unwritten. Fixed here: `ROADMAP.md` 1.5.0 → 1.6.0,
  `agents.md` 1.3.0 → 1.4.0, `claude.md` 1.3.0 → 1.4.0. `decisions/*.md` files are exempt —
  a decision record's `revision N` counts its original architecture-review rounds, not
  every later addendum.

## [0.7.0] — 2026-09-14

> **This release is .NET only for M2c**, continuing the Stage 1 exception the shared-version
> rule at the top of this file describes. `rust/` and `python/` are unchanged from `0.6.0`
> apart from two shared fixture-count assertions ([DR-0006](decisions/0006-m2-authentication.md)
> D-1, D-6). **M2 (authentication) is now fully complete in .NET** — M2a, M2b and M2c all
> exited.

### Added

- **M2c — automatic token renewal (`AUT-090`…`AUT-095`).** `AutoRenewPolicy` (disabled by
  default: `RenewAtFraction`, `MinInterval`, `Increment`, `MaxConsecutiveFailures`,
  `OnRenewed`/`OnFailed`/`OnStopped`), `RenewalEvent`, `RenewalStoppedReason`.
  `BastionVaultClient` is now `IDisposable`; disposing stops the renewal loop without
  disposing an application-supplied transport. A `Login`-sourced client re-attempts one
  fresh login after renewal stops, re-armed by the next successful renewal
  ([DR-0006](decisions/0006-m2-authentication.md) D-M2-6, D-M2-27, D-M2-28). .NET only;
  Rust and Python follow from the same record at M13.
- **`IClientLogger.Info`**, a default-interface no-op method — AUT-095's single info-level
  log line when `AutoRenew` is enabled on a batch or non-renewable token. Additive; no
  existing implementer needs to change (D-M2-28 item 4).
- **Fixture schema: `clock.delay: "instant" | "virtual"` and `clock.expectWaits`.** An
  opt-in virtual-time mode lets a scheduled wait be driven and asserted deterministically
  without sleeping, which is what makes `AUT-090`…`AUT-095` testable at all. The default
  (`"instant"`) is byte-identical to every one of the 208 pre-existing fixtures
  ([DR-0006](decisions/0006-m2-authentication.md) D-M2-27).
- Conformance fixtures `auth.autorenew.schedule-and-renew` and `auth.autorenew.stops-on-403`.

### Changed

- **The build order is now staged by language instead of sliced horizontally across all
  three (`ROADMAP.md` D-1, superseding the original D-1).** At the project owner's
  direction, `dotnet/` runs to `Complete` conformance and a green integration suite first —
  Stage 1, milestones M2b through M12 — with `rust/` and `python/` deferred entirely to a
  new Stage 2 (milestone **M13**), which brings both to parity from .NET's decision records
  and fixtures. `rust/` and `python/` stay frozen at the M2a/`0.5.0` catalogue-only state
  for the duration of Stage 1; no Stage 2 work starts before all of Stage 1 exits (D-6).
  The shared `1.0.0` tag now waits for M13, not M12. This is a planning and sequencing
  change with no SDK behavioural effect; it carries no package version implication.
- **`BastionVaultClient` implements `IDisposable`** (see Added, above) — additive, no
  existing caller is required to change.
- **The fixture `clock` object is now `additionalProperties: false`, and
  `clock.advance`/`clock.delay: "virtual"` are mutually exclusive.** No existing fixture
  combines them today, so no existing fixture is affected
  ([DR-0006](decisions/0006-m2-authentication.md) D-M2-27 item 5).

### Fixed

- **`CNF-027` was red in the Rust and Python CI jobs at `v0.5.0`.** M2a regenerated the error
  catalogue for all three languages, so both surfaces gained `AUTH_TOKEN_SOURCE_FAILED` and
  `CONFIG_TOKEN_FILE_NOT_WRITABLE`, but only .NET's baseline was regenerated. Both are now
  regenerated by their documented mechanisms
  ([DR-0006](decisions/0006-m2-authentication.md) D-M2-23).
- **`agents.md` §9's Python verification command had never worked.** It collected 12 of 588
  tests and errored on all 12 — the suite has been pytest-style since M1b. Corrected in
  `agents.md` and `skills/codex/SKILLS.md` to CI's own command (D-M2-22).
- **`auth.token.lookup-self-remaining-ttl` did not test `AUT-014`'s arithmetic.** Its
  `clock.start` equalled its `creation_time`, so `creation_time + creation_ttl − now`
  degenerated to `creation_ttl` and an implementation returning the bare field passed. The
  fixture now starts 30 minutes later and expects `PT30M`, so all three plausible wrong
  implementations fail. A `FIX-012` specification change; all three suites were re-run
  against it and all three still pass, so all three genuinely compute the formula (D-M2-21).
- **`SdkInfo.SdkVersion` was stuck at `"0.5.0"` after the `0.6.0` package bump**, and the
  test asserting it checked the same stale literal rather than catching the drift. Both
  now read `0.7.0`, and the underlying "three independently-editable copies of one fact"
  risk is tracked, not yet closed (D-M2-28 item 5).
- **The R-10 gate re-proof sweep** ([DR-0001](decisions/0001-m0-harness-gate-proof.md)
  addendum) re-proved 6 previously-unproven-or-stale CI gates by seeded violation and
  revert (CNF-022 .NET coverage, CNF-025 secret scan against the current regex, CNF-027
  .NET and Python against their current mechanisms, the traceability parser's own tests,
  and the error-catalogue regeneration gate).

### Security

- **The R-10 sweep found three pre-existing, unfixed conditions, tracked as `ROADMAP.md`
  §8 R-11/R-12/R-13, none of them M2c's to fix:** CNF-023 (.NET style/analyzer
  enforcement) has silently never fired since M0/M1a, `cargo audit` is red on `main`
  (`rustls 0.23.40`, RUSTSEC-2026-0285, TLS-surface, fix `>=0.23.45` — already carried by
  the published `v0.5.0`/`v0.6.0` tags), and `pip_audit` is red on a transitive
  dependency of the audit tool itself, not of the shipped package. Rust and Python are
  frozen for Stage 1 (D-1/D-6); the `rustls` bump is now a named entry gate for M13.

### Agent architecture

- **The routing matrix's triggers are now mutually exclusive, so "first match wins" is a
  check rather than a judgement call** (`agents.md` §4.2). Row 2's trigger previously read
  "implement, debug, refactor, test, CI change", which matches every implementation and
  made row 3 unreachable on a literal reading; row 3's read as a set of qualifiers on top.
  Each row now states what puts a task **in** it and what puts it **out**, and the
  discriminator is named: **is the contract settled?** An accepted decision record pinning
  the public names makes the task transcription-and-verification (row 2, Claude Sonnet 5);
  the **pathfinder** pass is unsettled by definition and every **parity pass** after it is
  settled by definition. §4.3 rule 4 now also requires an upgrade's trigger to be recorded
  **before** dispatch, because one written afterwards is a rationalisation.
- The same distinction landed in the two places that restate the rung choice —
  `.claude/agents/eng-implementation.md`, `.claude/agents/eng-deep.md`,
  `.claude/skills/engineering-delegation/SKILL.md` and `skills/codex/SKILLS.md` §2 — so the
  documents and the harness cannot drift (**BND-002**).
- Prompted by a real misroute: M2a's Rust and Python parity passes were dispatched to
  `eng-deep` (Claude Opus 5, 15×) with no recorded trigger, where the settled contract put
  them at Claude Sonnet 5. Recorded in
  [DR-0006](decisions/0006-m2-authentication.md) D-M2-20.

## [0.6.0] — 2026-09-14

> **This release is .NET only for M2b**, continuing the Stage 1 exception the shared-version
> rule at the top of this file describes. `rust/` and `python/` are unchanged from `0.5.0`
> ([DR-0006](decisions/0006-m2-authentication.md) D-1, D-6).

### Added

- **M2b — Userpass and AppID, the login response contract, and the client-side missing-token
  preflight.** `Auth.Userpass.Login`, `Auth.AppId.Login`/`ReadRoleId`/`GenerateSecretId`, the
  public `TokenSource.Login(AuthMethod, LoginCredentials, LoginOptions?)` factory and
  `Auth.AuthenticateAsync()`, plus the recognised login-failure codes and the derived
  `AuthInfo.EnvironmentScope` (`AUT-002`, `AUT-003`, `AUT-010`…`AUT-013`, `AUT-030`…`AUT-032`,
  `AUT-040`…`AUT-042`, `AUT-044`; [DR-0006](decisions/0006-m2-authentication.md) D-M2-25,
  D-M2-26). `AuthInfo` gains a `required IssuedAt` member — **source-breaking** for any
  existing caller constructing an `AuthInfo` directly; no such caller exists in this SDK's
  own surface today. .NET only; Rust and Python follow from the same record at M13.
- **The mock server's login-failure simulation (`TST-021`) is now generated from the same
  Appendix B table the real recognizer reads**, instead of a hand-written message, so a
  fixture can never assert a `data.error` string the mock cannot produce (D-M2-25 item 3).

### Changed

- **An authenticated operation attempted with no token now fails client-side with
  `BV-AUTH-001`, before any network call**, instead of reaching the server and surfacing
  whichever of three inconsistent codes the server happens to return for that path
  (`CFG-020`, `ERR-022`). Unauthenticated endpoints (`sys/health`, `sys/seal-status`,
  `sys/init`, `sys/unseal`, anonymous `sys/info`, `auth/*/login`,
  `auth/ferrogate/requirement`, `auth/ferrogate/enroll`) are unaffected, and
  `Logical.Raw` still returns the server's own answer unchanged — the preflight applies
  only to typed operations, per `ERR-022`'s own wording.
- **A `BastionVaultException` a token-source delegate leaks is now wrapped as
  `BV-AUTH-017 TokenSourceFailed`**, with the original preserved as its `cause`; a login's
  own recognised failure still reaches the caller unwrapped, distinguished internally by
  origin rather than by error code (D-M2-25 item 2, corrected at D-M2-26 item 3 after the
  R3 handback review found the original code-list design would have silently broken
  `AUT-003`'s re-login/replay for a gated `AppId` login).

### Security

- **A Userpass username containing `/` or `?` was sent as a different, unauthenticated
  request path** — a path-injection defect found while implementing `AUT-030`; path
  parameters that may contain a separator are now percent-encoded per segment before the
  request is built (`AUT-030`, `TRN-020`).
- **Login credentials are held only in redacting types, and retained only when the token
  source is `TokenSource.Login`** (needed for `AUT-003`'s re-login). No login path exposes
  the `auth` object, a password, a TOTP code, a secret id or a machine token to the logger
  or the `CFG-080` observer (`AUT-031`, `AUT-100`, `AUT-101`, `CNF-031`, `CNF-032`).

## [0.5.0] — 2026-09-14

> **This release is .NET only for M2a, and that deviates from the shared-version rule
> stated at the top of this file.** Rust and Python carry the regenerated error catalogue
> but none of the authentication surface below. The bump and the tag were authorised by the
> project owner; the rule is not amended, because one authorised exception should stay
> visible as an exception ([DR-0006](decisions/0006-m2-authentication.md) D-M2-15).

### Added

- **M2a — `Client.Auth`, the first sub-API grouping (OVR-008).** The token source model
  (`Static` / `Callback`, with the `Login` variant's contract in place and its public
  factory landing in M2b), the `Token` method group, and the nine token-store operations
  the server actually implements — `Create`, `Lookup`, `LookupSelf`, `Renew`, `RenewSelf`,
  `Revoke`, `RevokeOrphan`, `RevokeSelf`, `AuditLogin` (`AUT-001`, `AUT-004`, `AUT-014`,
  `AUT-020`, `AUT-080`…`AUT-085`). .NET only; Rust and Python follow from the same record.
- **The token-helper write path** — `Auth.PersistToken()` with owner-only permissions and
  `Auth.ForgetPersistedToken()` (`CFG-031`, `CFG-032`), plus `BV-CONFIG-010` for a token
  file in the CLI's encrypted `BVTOK1:` format.
- **Two error codes**, minted in Appendix B and generated into all three languages:
  `BV-AUTH-017 TokenSourceFailed` and `BV-CONFIG-011 TokenFileNotWritable`
  (D-M2-16). The catalogue is now 121 codes; the 127 recognition rules are unchanged,
  because both codes are raised client-side.
- **Two fixture-harness instruments the repository has never had.** The driver now honours
  `fixture.clock` (`clock.start` / `clock.advance`), which the fixture schema has defined
  since M0 and which no language read — so `auth.token.lookup-self-remaining-ttl` had
  never actually asserted anything. And a capturing logger plus a capturing
  `RequestObserver` now assert on **every** fixture run that no fixture token, password or
  secret-id appears in any log line, observer event, exception message or rendered error
  (`TST-051`, D-M2-7). Both were proven by seeded violation and then kept as standing
  tests.

### Changed

- **Breaking: `IClock.Now()` is now `IClock.NowUtc()`**, naming the kind of time it
  returns. Rust's `Clock::now()` returned a monotonic `Instant` while .NET's and Python's
  returned wall-clock, so one member name meant two different things across the three SDKs
  — and `AUT-014`'s `RemainingTtl` arithmetic was not merely untested on Rust but
  uncomputable (`AUT-014`, D-M2-2).
- **Breaking: token resolution is asynchronous.** The executor resolves through
  `TokenSource.ResolveAsync()` instead of reading a field (D-M2-9). A concurrent first-use
  login is single-flighted, so eight concurrent operations at startup perform one login
  rather than eight, and a *failed* login is no longer cached for the client's lifetime
  (`CFG-070`, D-M2-11, D-M2-17).
- `Auth.PersistToken` reports `BV-CONFIG-011` rather than `BV-CONFIG-005`, whose message
  describes a file that cannot be *read*.

### Fixed

- `BV-AUTH-015 TokenNotRenewable` and `BV-NOTFOUND-006 TokenNotFound` are raised only under
  the exact conditions `AUT-085` and `AUT-084` state. A `400` caused by a malformed request
  body on the renew path was reported as `TokenNotRenewable`, which could make a caller
  re-login in response to its own error; and any user path containing `auth/token/lookup`
  was refined as though it were the token-store endpoint.
- `Auth.Token.RenewSelf` resolves the current token once, so the token in the path and the
  token in the `X-BastionVault-Token` header can no longer differ (`AUT-080`).
- A token source that fails now raises `BV-AUTH-017` instead of letting a runtime exception
  escape the SDK uncoded (`ERR-020`, `TRN-054`).

### Security

- **The `CFG-080` request observer no longer receives an unredacted request path.**
  `auth/token/renew/{token}` and `auth/token/lookup/{token}` put a live token in the path,
  so every observer was receiving one (`ERR-003`, `TST-051`). Found by the TST-051
  instrument in the milestone that built it.
- A cancelled token resolution reports `BV-TRANSPORT-005` rather than escaping as a
  runtime cancellation (`ERR-020`).

## [0.4.0] — 2026-09-14

### Added

- **M1c — the error catalogue is generated from Appendix B, in three languages.**
  `tools/error-catalogue` parses `specifications/appendix-b-error-catalogue.md` §1 and §2
  into a checked-in `catalogue.json` and emits, for .NET, Rust and Python, the 119 error
  codes, the 127 ordered message-recognition rules and every `ErrorCodes` constant — plus
  124 `errors.recognition.*` conformance fixtures. A new `repo-gates.yml` job regenerates
  and fails on any diff, so a hand edit to generated source and a specification edit
  without regeneration are both build failures. Appendix B's ~1560 strings are now
  single-sourced rather than transcribed three times (ERR-010, ERR-036, ERR-037,
  [DR-0005](decisions/0005-m1c-error-model.md) D-M1c-1).
- **Public `ErrorCatalog` / `ErrorCatalogEntry` in all three SDKs.** `Get`/`get` returns
  the entry for a code and never throws; `All`/`all` is Appendix B order, so generated
  documentation is stable (ERR-036, D-M1c-7).
- **`ERR-031` is enforced rather than eyeballed.** Generation fails if any hint exceeds two
  sentences, and the compiled catalogue is asserted against the same rule (D-M1c-13).
- **Server-message recognition (ERR-020 step 4)** now runs ahead of the status table in all
  three languages, with `Details` extraction for the seven ERR-035 rows, ERR-034 path
  interpolation, and hint enrichment for the seven client-side-decidable ERR-040 rows. The
  two enrichment rows needing server state are deferred to M3 and M4 and are absent, not
  stubbed. ERR-003 path redaction and the ERR-002 one-line form are applied in the error
  constructor, so no surface can miss them.
- Server `warnings` are surfaced and logged at warning level in all three SDKs, and are
  never converted to errors (ERR-050).

### Changed

- **Behavioural: `400` and every other unmapped 4xx now map to `BV-INPUT-100
  ServerRejectedRequest`**, the code `04-error-model.md` step 5 names. They mapped to
  `BV-INPUT-001 InvalidArgument`, which D-M1b-21 chose only because the hand-transcribed
  catalogue did not carry `BV-INPUT-100`. `BV-INPUT-001` remains the code for client-side
  argument validation (D-M1c-12).
- **Behavioural: an unrecognised `409` now maps to `BV-CONFLICT-001 Conflict`.** The M1b
  `Resolve409` heuristic, which guessed `BV-CONFLICT-002`/`003` from the response body, is
  deleted — Appendix B §2 answers the digest/sha256 and brokered-credential messages at
  step 4. Before this change the three SDKs gave three different answers for an
  unrecognised `409` and none of them was the specification's (D-M1c-19).
- **Behavioural: a `503` now maps to `BV-SERVER-002 Unavailable` unconditionally.** The M1b
  `sealed`-substring heuristic is deleted; §2's `bastionvault is sealed` and
  `(5xx) is sealed` rows answer the sealed case at step 4, leaving the heuristic to cover
  only a body containing `sealed` without `is sealed` — unspecified and untested. Because
  `BV-SERVER-001` is not retryable and `BV-SERVER-002` is, those bodies were silently
  having a permitted retry suppressed (ERR-006, D-M1c-23).
- **Behavioural: `Error.Path` carries the `[ns=…]` display prefix in .NET**, as the `Error`
  field table in `04-error-model.md` defines it and as Rust and Python already did. .NET
  also prepended a leading `/` the other two do not; both are corrected, in `Error.Path`,
  `Details["path"]`, the ERR-034 hint note and `RequestEvent.Path`. Redaction still applies
  through the prefix (ERR-001, D-M1c-17, D-M1c-24).
- **Breaking (pre-1.0, CNF-040):** `RateNamespaceQuotaExceeded` is renamed
  `RateNamespaceRateQuotaExceeded` in all three SDKs, and Rust and Python additionally
  rename `NOTFOUND_PATH_NOT_FOUND` to `NOT_FOUND_PATH_NOT_FOUND`, following the generated
  naming rule. The code strings are unchanged (D-M1c-2, D-M1c-21).
- **Breaking (pre-1.0, CNF-040):** Rust's `DetailValue` gains a `List(Vec<String>)` variant
  so the ordered `keys` capture keeps its order, as .NET's `string[]` does (D-M1c-20).
- **Python's `CNF-027` public-API gate is now member-level, matching .NET and Rust.**
  `python/tests/_api_surface_extractor.py` replaces the 34 name-only lines of
  `python/api_surface.txt` with 360 lines covering every public class, method signature,
  property, dataclass field, enum member, constant *value* and public instance attribute
  assigned in `__init__`. Renaming a constant, removing a method, or renaming an attribute
  users read — `BastionVaultError.code` among them — now fails the gate rather than passing
  it silently. The baseline is mechanically generated and byte-identical on Python 3.11 and
  3.12; both the gap and its closure are proven by seeded violation (CNF-027, D-M1c-22,
  [DR-0001](decisions/0001-m0-harness-gate-proof.md)).

### Fixed

- **The `CNF-025` secret scan was red on `main` and is green again.** Eleven literal
  `s.<20+ alnum>` fake tokens sat in tracked test sources outside
  `specifications/fixtures/**`, so the gate exited 1 at `HEAD` — M1a and M1b both exited
  with it failing. The tokens are now assembled at runtime; the scan pattern and its
  whitelist are byte-for-byte unchanged (D-M1c-15, CLA-004).
- **The `CNF-010` coverage floor was red on `main` in the Python job and is green again.**
  `python.yml` runs `pytest -m "not integration"`, and `test_httpx_transport.py` is
  entirely integration-marked, so `httpx_transport.py` was covered at 29 % in CI and
  `--cov-fail-under=95` failed the job. It now has unit coverage through
  `httpx.MockTransport` alongside the untouched integration suite: 29 % → 100 %, and the
  job's own invocation passes at 98.92 %. No omit rule, no pragma, floor unchanged
  (D-M1c-16, CLA-004).
- A namespaced login would have sent a token once the display path landed, because .NET
  matched the anchored login pattern against it; token resolution reads the raw path, as
  Rust already did (CFG-020, D-M1c-24).

### Agent architecture

- **The orchestration documents are now bound to the harness.** `agents.md` §4.1 and §4.2
  described a model-routing policy that nothing executed: the harness loads only
  `claude.md`, so the routing matrix, the scoring bands and the token policy were never in
  context; `skills/claude/SKILLS.md` and `skills/codex/SKILLS.md` sat outside
  `.claude/skills/**` and in a format the harness does not discover, so neither was ever
  loaded; no agent definitions existed, so the Claude Haiku 4.5 / Claude Sonnet 5 /
  Claude Opus 5 rungs had nothing to instantiate; and no `model` setting existed, so the
  session ran whatever model the client happened to select. Adds `.claude/settings.json`
  (session default Claude Sonnet 5, per `claude.md` §4), five agent definitions under
  `.claude/agents/` covering routing-matrix rows 1–6, and two discoverable skills under
  `.claude/skills/` that route to the canonical `SKILLS.md` files rather than restating
  them. `claude.md` gains §0, which imports `agents.md` and `skills/claude/SKILLS.md` so
  the policy is in context from the first turn.
- **`agents.md` §4.1.2 — harness bindings**, recording each binding once (**CLA-008**) with
  three rules: an agent definition may only name a model its own tree may run
  (**BND-001**), a routing change lands with its `.claude/` change in the same commit
  (**BND-002**), and tiers bind by alias rather than by dated identifier so a registry
  version bump cannot silently repoint an agent (**BND-003**).
- **`scripts/validate-agent-docs.py` check C7** verifies the filesystem against that
  table — settings default, agent frontmatter and rungs, tree permissions, skill
  discoverability, and the `claude.md` import chain — so the documents can no longer drift
  ahead of the configuration. Proven by seeded violation and revert. The check runs in the
  existing `repo-gates.yml` agent-docs step; no workflow change was needed.

## [0.3.0] — 2026-09-13

### Added

- **M0 — conformance harness, quality gates and traceability.** Test harness in all three
  languages, the CNF-020…CNF-027 gate set wired into `dotnet.yml`, `rust.yml`, `python.yml`
  and `repo-gates.yml`, and the ratcheting traceability tool (`tools/traceability`, TST-041)
  with a 374-entry baseline as the remaining-work counter. Every gate was proven by seeded
  violation and revert — [`decisions/0001-m0-harness-gate-proof.md`](decisions/0001-m0-harness-gate-proof.md),
  [`decisions/0001-m0-harness.md`](decisions/0001-m0-harness.md).
- **M1a — client configuration, error skeleton and transport seam.** `BastionVaultClient` /
  `Client`, `ClientConfig`, the error base type and the transport abstraction in .NET, Rust
  and Python, with a redacting secret type, an injected environment source and the
  options-in / resolved-config-out shape the rest of the SDK inherits —
  [`decisions/0003-m1a-configuration.md`](decisions/0003-m1a-configuration.md).
- **M1b — transport, logical layer and retry.** The SDKs can now talk to a server. Adds the
  logical layer (`Logical.Read` / `Write` / `Delete` / `List` / `Raw`, TRN-001…003) over a
  production HTTP transport in all three languages, with URL encoding (TRN-020/021), the
  custom `LIST` verb (TRN-010), the reserved-header and login-path token rules
  (TRN-012…017), both response envelope shapes (TRN-040…043), status-code handling
  (TRN-050…054), and redirects refused rather than followed (TRN-060, CNF-034) —
  [`decisions/0004-m1b-transport.md`](decisions/0004-m1b-transport.md).
- **Retry policy execution** (CFG-051…055, RES-001…004): idempotency classification,
  exponential backoff with injectable jitter, `Retry-After` handling, and `Error.Attempts`.
  Sealed vaults (`BV-SERVER-001`) and abuse-guard rate limits (`BV-RATE-001`) are never
  retried automatically.
- **Runtime mutation and observability**: `Client.SetToken` / `ClearToken` /
  `WithNamespace` (CFG-070/071), and a request/response hook carrying method, path,
  namespace, status, duration, attempt and error code — and never bodies or tokens
  (CFG-080/081).
- **`FakeTransport`** as public test-support surface in each package (TRN-100), so
  applications can test against the SDK without a server. It is the same object the
  conformance fixture driver uses.
- `CHANGELOG.md` (this file) and the record-keeping rules **REC-001…REC-006**
  (`agents.md` §11) that require it and `ROADMAP.md` to be kept current.

### Changed

- **The transport seam is a new shape** (`Transport`, `TransportRequest`,
  `TransportResponse`). The M1a placeholders had diverged into three incompatible
  definitions — Rust's had no method at all — and were replaced by one asynchronous
  contract. Transport failures now surface as SDK errors with `BV-TRANSPORT-*` codes
  rather than as each runtime's native exception.
- `RequestOptions.Idempotent` is now optional (tri-state) rather than a plain boolean, so a
  caller can mark a write retryable *or* a read not-retryable.
- `RetryPolicy` gained the `RetryOn` field it was specified to have and shipped without.
- `RequestOptions` gained `ApiVersion` and `TotalTimeout`; the settings table gained
  `MaxResponseBytes` and `UseSystemProxy`. All four are required by section 03/13 text that
  section 02's tables had omitted.
- The SDKs are **asynchronous-only**; Rust and Python callers need a runtime or event loop.
  A synchronous facade is additive and deferred.
- Model registry narrowed to Claude models only, in both agent trees —
  [`decisions/0002-all-claude-registry.md`](decisions/0002-all-claude-registry.md).

### Fixed

- An unmapped HTTP status — including `400`, the most common error the server returns —
  raised a generic runtime exception instead of a coded SDK error (TRN-054). Unmapped
  statuses now fall back by class and carry the server message through.
- `MaxResponseBytes` was checked only *after* the whole response had been buffered, so the
  limit reported a violation without preventing one. It is now enforced while reading, and
  an over-limit `Content-Length` is refused unread (TRN-033).
- Unbracketed IPv6 literals with a port were accepted instead of rejected with
  `BV-CONFIG-001` (TRN-092).

### Security

- **Rust trusted no certificate authorities.** Its root store was seeded only from an
  explicitly configured CA, so with the default configuration every TLS connection would
  have failed — and any deployment that worked around it by configuring one CA silently
  lost the platform trust store. The platform store is now loaded and configured CA
  material is *added* to it, never substituted, unless `CaCertReplacesSystemRoots` is set
  (CFG-040).
- Client certificates are now presented on every connection where configured (CFG-044), TLS
  1.2 is the enforced floor with 1.3 offered (CFG-041), and `TlsServerName` drives both SNI
  and hostname verification (CFG-042). M1a parsed this material; M1b is where it reaches
  the wire.
- Rust and Python gained connection pooling (TRN-090) and have proxies disabled by default
  (TRN-091).

### Dependencies

- Python adds `httpx`. Rust adds `hyper`, `hyper-util`, `rustls`, `rustls-native-certs`,
  `tokio`, `tower-service` and supporting crates, taking its audited dependency set from
  131 to 143 crates. .NET adds nothing (in-box `SocketsHttpHandler`). `cargo audit`,
  `pip-audit` and `dotnet list package --vulnerable` are clean (CNF-024).

### Agent architecture

- **The .NET public-API gate (CNF-027) had never been enforced.** `AnalysisLevel=latest-all`
  imports an MSBuild props file that reassigns `$(CodeAnalysisRuleIds)`, silently discarding
  the `RS00xx` rules `Microsoft.CodeAnalysis.PublicApiAnalyzers` registers — so an
  undeclared public type compiled with zero warnings, and the M0 and M1a exits certified a
  baseline nothing had checked. The analyzer and its `PublicAPI.*.txt` files are replaced by
  an executing surface diff (`PublicApiSurface.txt`), matching the mechanism Rust and Python
  already used, and the new gate was proven by seeding a violation and watching it fail.
  `.github/workflows/dotnet.yml`'s claim to the contrary is corrected.
- `agents.md` §11 added: changelog and roadmap upkeep is now a normative obligation with an
  owner, a trigger and a definition of done, referenced from `claude.md`,
  `skills/claude/SKILLS.md` and `skills/codex/SKILLS.md`. Claude gains responsibility 7
  (project record upkeep) and rules CLA-009/CLA-010; Codex gains workflow step 8 and
  ENG-008 (propose the entry, never edit the file); `agents.md` gains VER-006. All four
  agent documents and `ROADMAP.md` bumped to 1.2.0.

## [0.2.1] — 2026-09-13

### Changed

- Agent trees restricted to the Claude and GPT model families; every other family removed
  from the registry and the routing tables. (Superseded in `Unreleased` by the all-Claude
  registry.)

## [0.2.0] — 2026-09-13

### Added

- **Technology-agnostic SDK specifications** under `specifications/`: 18 documents, 4
  appendices, 388 requirement IDs (`AREA-NNN`) and the shared fixture corpus. This is the
  single source of truth for behaviour in all three languages.
- **Dual-orchestrator agent architecture**: `agents.md`, `claude.md`,
  `skills/claude/SKILLS.md`, `skills/codex/SKILLS.md`, the token-optimization policy
  (TOK-001…TOK-012) and `scripts/validate-agent-docs.py` to keep the four documents
  consistent.
- `ROADMAP.md`: milestones M0…M12, sequencing decisions D-1…D-5 and the risk register.

## [0.1.0] — 2026-09-13

Untagged. Recorded here for completeness from the repository history.

### Added

- Base .NET, Rust and Python SDK projects, the shared solution layout, and the
  `build-artifacts.yml` workflow that builds and validates artifacts for all three.

[Unreleased]: https://github.com/ffquintella/bastionvault-integration-sdk/compare/v0.4.0...HEAD
[0.4.0]: https://github.com/ffquintella/bastionvault-integration-sdk/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/ffquintella/bastionvault-integration-sdk/compare/v0.2.1...v0.3.0
[0.2.1]: https://github.com/ffquintella/bastionvault-integration-sdk/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/ffquintella/bastionvault-integration-sdk/releases/tag/v0.2.0
