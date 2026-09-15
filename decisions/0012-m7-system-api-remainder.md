# DR-0012 — M7: the system API remainder in .NET

**Status:** **proposed** — authored by an Engineering-tree Claude Opus 5 deep worker
(`agents.md` §4.2 rows 3 and 6), awaiting Strategic-tree Claude Opus 5 architecture review
(§4.2 row 4, §4.4), **revision 1** — no architecture-review round yet. This record covers
**all three M7 slices**; slices b and c append their own `D-M7-n` entries below rather than
opening a second record.
**Risk tier:** R3 (`agents.md` §5.3 — `Sys.Init`'s response is unseal-key and root-token
material, which is CRS-003's "secret material, token lifecycle" at the top tier; the tier
was assigned in the brief and is **not** lowered here). The mount and auth-method surface
alone would be R2 (a new cross-language public contract); the seal surface raises it.
**Milestone:** M7, slice a of three · **Date:** 2026-09-15
**Supersedes nothing. Amends:** nothing in `specifications/`. Discharges the M7 half of
[DR-0009](0009-m4-kv-engine.md) D-M4-2 (`KV-001`).
**Inherits:** [DR-0003](0003-m1a-configuration.md) (options-in / resolved-config-out,
injected `EnvironmentSource`, the redacting `SecretString`, D-M1a-9),
[DR-0004](0004-m1b-transport.md) (the one transport seam, the single retry loop, D-M1b-9's
per-client `ClientContext`), [DR-0005](0005-m1c-error-model.md) (recognition, enrichment,
the generated catalogue D-M1c-1, D-M1c-19's `409` default, and **D-M1c-25**: a deferred
branch returns the value the specification names, never a plausible guess),
[DR-0007](0007-m3-system-api-core.md) (the `Sys` surface shape and its Shape B handling),
[DR-0009](0009-m4-kv-engine.md) (D-M4-2's deferral-with-a-named-owner, D-M4-8's fixture
authoring precedent, D-M4-9's declined façade),
[DR-0010](0010-m5-cluster-discovery-and-resilience.md) (the `nodeLocal` failover-exclusion
seam, D-M5-3's deferral of `RES-030` **to this milestone**, D-M5-5's limits on `DSC-041`).

## Problem

M7 is the whole of `06-system-api.md` that M3 did not take. `ROADMAP.md` books it as one
milestone; the surface is too wide for one reviewable handback, so it lands in three
slices. This record's slice **a** is initialisation/seal/unseal, mounts, and auth-method
administration — thirteen baselined IDs (`SYS-010`…`SYS-013`, `SYS-020`…`SYS-026`,
`SYS-030`, plus `KV-001`) — and one operation, `Sys.HsmStatus`, that `06-system-api.md`
names and no `SYS-*` bullet covers.

Three things about this slice are not transcription, which is what puts it on row 3
rather than row 2:

1. **`SYS-011` asks for something .NET cannot literally do.** "Zero them on dispose where
   possible" is written against a language-neutral specification. .NET's `System.String`
   is immutable and may be interned; a `string`-backed secret cannot be zeroed at all. The
   requirement's escape hatch ("where possible") has to be *interpreted* into a concrete
   design, and the interpretation is the difference between a type that zeroes its buffers
   and a type whose `Dispose` is decoration.
2. **`SYS-026`'s cache is under-specified in exactly the place it matters.** "A per-client
   cache (TTL 60 s)" says nothing about namespaces, and the mount table is
   namespace-scoped. The naive reading produces *wrong* answers, not stale ones.
3. **`SYS-023`'s third row needs a code Appendix B §2 does not recognise.** Two of the
   three remount messages have recognition rules; `Unknown mount table type.` does not, and
   a `409` the recogniser misses defaults to `BV-CONFLICT-001` (D-M1c-19), not the
   `BV-INPUT-100` the requirement names.

## Routing classification

Row 3 trigger **(a)** — the pathfinder pass that first defines a contract in the first
language — holds outright: no decision record pins any of `InitResult`, `MountInfo`,
`MountRequest`, `MountTable`, `MountTypes`, `HsmStatus` or `KvVersion`, and there is no
shipped member to transcribe against. Trigger **(d)** holds as well: the change touches
`SysOperations`, `KvOperations`, `RequestExecutor`, `LogicalOperations`, `ClientContext`
and the fixture harness. **Recorded before dispatch** by the delegating brief, per §4.3
rule 4. Review at handback is Strategic-tree Claude Opus 5 (row 3's gate, and R3's).

## Decisions

- **D-M7-1 (slicing, and what slice a is *not*).** M7 lands in three sequential slices
  against one public surface, so they do not run in parallel (`agents.md` §7.4).
  - **M7a — init/seal/unseal, mounts, auth methods.** `SYS-010`…`SYS-013`,
    `SYS-020`…`SYS-026`, `SYS-030`, `KV-001` — **13 IDs**, plus `Sys.HsmStatus` (see
    D-M7-10). Baseline 205 → 192.
  - **M7b — policies and namespaces.** `SYS-040`…`SYS-043`, `SYS-045`,
    `SYS-060`…`SYS-062`.
  - **M7c — audit, identity, backup/restore, and `RES-030`.** `SYS-070`, `SYS-080`,
    `SYS-090`, `SYS-091`, `SYS-100`, `SYS-101`, `RES-030`.

  `RES-030`'s `*ClusterWide` variants are explicitly **not** in slice a even though
  `SYS-013` mentions them, and that is D-M5-3 being honoured rather than contradicted:
  D-M5-3 deferred `RES-030` to M7 because its base operations did not exist. They exist
  now, so the deferral is discharged *within* M7 — at slice c, with the rest of the
  cluster-wide surface — not silently absorbed into the slice that mints the base
  operations. A `SHOULD` landed in the same breath as the `MUST` it wraps gets no separate
  review.

- **D-M7-2 (`SYS-011`: the owned material is a `char[]`, and `SecretString` is not
  reopened).** `InitResult` holds the key shares and the root token in `char[]` buffers it
  owns, implements `IDisposable`, and `Dispose` overwrites every buffer with `'\0'`.
  `Keys` and `RootToken` build a **fresh** `SecretString` on each read rather than caching
  one, and both throw `ObjectDisposedException` after `Dispose`.

  Two residues are unavoidable on .NET and are stated rather than papered over: the
  transient `string`s `System.Text.Json` materialises while parsing the response body, and
  any `string` a caller takes from `SecretString.Reveal()`. Both are ordinary garbage and
  neither is reachable from the instance. "Where possible" means exactly this much.

  **Rejected — make `SecretString` itself `char[]`-backed and `IDisposable`.** This is the
  design a secrets SDK would pick on a green field, and it was the first choice. It is
  rejected on blast radius: `SecretString` is the token type, constructed on effectively
  every code path in the SDK, and the library builds with `AnalysisLevel latest-all` and
  `TreatWarningsAsErrors`. Making it disposable turns every `new SecretString(...)` into a
  CA2000 error, which is a repository-wide refactor inside an R3 slice — and it would
  reopen DR-0003's pinned public type for a benefit that `Reveal()`'s fresh `string` per
  call already leaks away. If the Strategic tree wants it, it is its own decision record
  and its own slice.

  **Rejected — cache the `SecretString`s on the `InitResult` and null the references on
  `Dispose`.** Cheaper and more conventional, and it is what most SDKs ship. Rejected
  because `Dispose` would then be a *claim* rather than an act: the `string`s survive in
  the heap until collected, and a test could only assert that a property throws. The
  landed design is asserted against the bytes — the test reflects the buffers out and
  checks they are all-zero — which is the only form of this assertion that could fail
  against a wrong implementation.

  **Gives up:** an allocation per `Keys`/`RootToken` read (immaterial for a once-per-vault
  call), and a small parity cost — Rust will use `zeroize` and Python cannot zero a `str`
  at all, so the three languages will zero *different amounts* while sharing the contract
  "the container owns its buffers and clears them on dispose". That divergence is
  behavioural only in a debugger and is recorded here so Stage 2 does not rediscover it.

- **D-M7-3 (`SYS-013`: two separate flags, not one).** `Seal` and `Unseal` pass
  `nodeLocal: true` (DSC-045's existing failover exclusion, D-M5-13's seam, which M5 landed
  and no operation had yet used) **and** a new `nonRetryable: true` that the retry loop
  reads beside the policy it overrides.

  They are kept separate deliberately. Folding "non-retryable" into `isIdempotent` would
  have been one fewer parameter, but `isIdempotent` is *also* the failover predicate
  (`WillFailover`), so one requirement's change would silently move the other's behaviour.
  `nonRetryable` is checked ahead of every other eligibility term, so no policy a caller
  can write — `RetryOn` carrying the failure's code, `RetryIdempotentOnly: false`, any
  `MaxAttempts` — replays either operation. The fixture `sys.unseal.invalid-key` configures
  all three of those adversarially and asserts exactly one wire attempt, so the flag is
  proven against the policy rather than against the default.

  **Rejected — rely on the operations being `PUT` and therefore non-idempotent by
  default.** True today and true for the default policy, and it would have needed no code
  at all. Rejected because `SYS-013` says *flagged*, and the default is not a flag: a
  caller who sets `RetryIdempotentOnly: false` (a supported, documented setting) would
  silently get a replayed seal.

  **Gives up:** one more internal parameter threaded through `RequestExecutor`, in a method
  that already carries fourteen.

- **D-M7-4 (`SYS-026`: the cache is keyed by active namespace, and lives on
  `ClientContext`).** `SYS-026` says "per-client". `BastionVaultClient.Sys` builds a fresh
  `SysOperations` on every property read, so a cache held there would live for one call and
  satisfy the requirement in name only; it therefore lives on `ClientContext`, beside the
  token cell and the discovery engine, which is D-M1b-9's definition of per-client state.

  `ClientContext` is shared by every `WithNamespace` view, and the mount table is
  namespace-scoped, so the cache is keyed by **active namespace**. This is an addition to
  what `SYS-026` writes and it is not optional: an un-keyed cache would answer a lookup in
  one namespace with another namespace's table, which is a *wrong* answer, not a stale one,
  and the 60-second window would make it intermittent. Invalidation is scoped the same way
  — a `Mount` in one tenant says nothing about another tenant's table.

  The **whole table** is cached rather than one path, because `SYS-025` already establishes
  that no per-mount read exists; a per-path cache would issue N requests where one suffices
  and would make `Kv.DetectVersion` over a list of mounts N round trips.

  **Rejected — invalidate every namespace's entry on any mutation.** Simpler, and trivially
  correct. Rejected because it turns one tenant's `Mount` into a refetch for every other
  tenant on the client, which is the opposite of what a cache is for.

  **Gives up:** a caller who mounts through one client and reads through a second client
  still sees the stale entry for up to 60 s. That is inherent to "per-client" and is what
  the requirement asks for.

- **D-M7-5 (`SYS-025`: `ReadMount` is deliberately *not* served from the SYS-026 cache).**
  `SYS-026` scopes its cache to `MountTypeOf`. A `ReadMount` that could be 60 seconds stale
  is a different contract from the one `SYS-025` writes, and the two operations returning
  different answers for the same mount in the same second would be indefensible. So
  `ReadMount` fetches. **Gives up:** a round trip per call, and the mild surprise that the
  cheaper-looking call is the more expensive one — which is why it is stated in the XML doc
  on the member, not only here.

- **D-M7-6 (`SYS-023`: `Unknown mount table type.` is remapped at the operation, not in
  Appendix B).** The other two `409` rows are Appendix B §2 recognition rules and need no
  code. This one is not a rule, and a `409` the recogniser misses defaults to
  `BV-CONFLICT-001` (D-M1c-19). `Remount` therefore catches its own `409`, matches the
  message, and rethrows as `BV-INPUT-100` carrying every other field unchanged.

  **Rejected — add a recognition row to `tools/error-catalogue` and regenerate.** The
  globally right place for a global rule. Rejected on two grounds: the catalogue is
  generated from `appendix-b-error-catalogue.md`, so the row is a `specifications/` change,
  which is R3 and the Strategic tree's to make (CRS-004, and this brief's explicit
  constraint); and the message is meaningful only on this one endpoint, so a global rule
  would be broader than the fact. **Gives up:** the mapping is invisible to a reader of
  Appendix B, and Rust and Python must each reimplement it at the same operation rather
  than inheriting it from the generated table. If the Strategic tree prefers the row, this
  code deletes cleanly.

- **D-M7-7 (`SYS-022`: two path forms, named and separated).** `MountPaths` holds the
  normalisation once. The **wire** form is the URL segment with no trailing `/`
  (`sys/mounts/kv2`); the **table** form always ends in exactly one `/` and is what a
  result is keyed by. Input is accepted in either form everywhere, including with
  surrounding whitespace and a leading `/`. `Remount`'s body is the *table* form on both
  sides, because that is the form the server's own error text quotes back
  (`no matching mount at kv/`).

  A key the **server** sent is normalised by a different function from a key the *caller*
  passed, and the distinction is load-bearing: an empty caller path is `BV-INPUT-001`
  (`SYS-022` says so, because the server's bare `404` is indistinguishable from "no such
  mount"), while a degenerate server key is passed through — refusing it would blame the
  caller for the server's answer.

- **D-M7-8 (`KV-001` lands; `KV-002`'s façade stays declined).** D-M4-2 deferred
  `Kv.DetectVersion` **solely** because `Sys.MountTypeOf` did not exist. It exists, so
  `DetectVersionAsync` lands and `KV-001` leaves the baseline. It calls `Sys.MountTypeOf`
  rather than doing its own lookup, exactly as `KV-001` words it, so detection across N
  mounts costs one request; a private lookup would have been a second, uncoordinated cache.

  D-M4-9's declined façade is **not** reopened. `KV-002` offers `Kv.ReadSecret` as a `MAY`
  and requires it to assume `V2` when detection is refused, which is a guess in a public
  API (D-M1c-25's shape). A caller who wants it can now write it in three lines over
  `DetectVersionAsync`, which is the smaller surface to own. The M4 test that asserted
  `DetectVersionAsync`'s *absence* is flipped to assert its presence, and still asserts the
  other two absences — the ruling it guarded is intact.

- **D-M7-9 (two fixtures authored; the other two Appendix C names belong to slice b).**
  Following D-M4-8 and D-M5-4: Appendix C line 123 already names all sixteen `sys.*`
  fixtures, so authoring an absent file fills in Appendix C's own list rather than changing
  specified behaviour, and the schema is unchanged (CRS-004 is not triggered).
  `sys.mount.204` and `sys.unseal.invalid-key` are authored here; `sys.policies.acl-read`
  and `sys.namespaces.write-full-replace` are slice b's and are deliberately left absent.
  Fixture count on disk 224 → 226. The three `sys.*` fixtures that were on disk and pending
  for want of an operation (`sys.init.validation`, `sys.mounts.two-fields`,
  `sys.remount.409-in-use`) are now green, leaving three pending, all owned by slice b.

- **D-M7-10 (`Sys.HsmStatus` lands, and its traceability is an open question, not a
  minted ID).** `06-system-api.md` gives `Sys.HsmStatus` a heading, a body and a `/v2`
  pin, and **no `SYS-*` bullet**. The only requirement ID it traces to is `TRN-071`, which
  is still on `tools/traceability/baseline.json`. Tagging its test `TRN-071` would take a
  *fourteenth* ID off the baseline, and this slice was scoped to thirteen.

  The operation is therefore implemented and tested, and the test is deliberately left
  **untagged**, with the reasoning in a comment above it. No ID is invented, and no
  neighbouring `SYS-*` is misattributed to it. This is a decision for the Strategic tree:
  re-tagging is a two-line change (one attribute, one baseline entry). Leaving it untagged
  preserves the status quo exactly — `TRN-071` was uncovered before this slice and is
  uncovered after it — which is the most reviewable of the three options.

## Consequences

- **Public surface.** 77 new lines in `PublicApiSurface.txt`, regenerated mechanically:
  `InitResult`, `HsmStatus`, `MountInfo`, `MountDetail`, `MountTable`, `MountRequest`,
  `MountTypes`, `AuthTypes`, `KvVersion`, fifteen `SysOperations` members and
  `KvOperations.DetectVersionAsync`. Additive only; nothing removed, nothing re-shaped.
- **Traceability.** Baseline 205 → 192, exactly thirteen removals, no ID minted.
  `tools/traceability/traceability.py --check` exits 0.
- **Tests.** 850 → 904, all green. Coverage 99.19 % line / 96.93 % branch, both above the
  95 % floor (`CNF-010`, `TST-030`), with no exclusion pragma anywhere. Every new member is
  at 100 % branch coverage; the branch total moved 97.02 % → 96.93 % because the
  denominator grew against pre-existing partials elsewhere, not because anything landed
  uncovered.
- **Parity.** .NET only, per `ROADMAP.md` D-1 as superseded and D-6. Rust and Python
  inherit this record's rulings when Stage 2 opens; D-M7-2's zeroing residue and D-M7-6's
  operation-local remap are the two places they will diverge in *implementation* while
  holding the same *contract*, and both are recorded above for that reason.
- **`CHANGELOG.md`** gains one `Added` entry (REC-001), written by the Strategic tree on
  acceptance (REC-004). `ROADMAP.md` is not touched: M7 does not close until slice c.

## Rejected alternatives, collected

| Alternative | Why rejected |
|-------------|--------------|
| `SecretString` becomes `char[]`-backed and `IDisposable` | Repository-wide CA2000 refactor inside an R3 slice, and reopens DR-0003's pinned type (D-M7-2) |
| `InitResult` caches its `SecretString`s and nulls them on dispose | `Dispose` becomes a claim rather than an act; unassertable against the bytes (D-M7-2) |
| Rely on `PUT` being non-idempotent instead of a non-retryable flag | `RetryIdempotentOnly: false` is a supported setting and would silently replay a seal (D-M7-3) |
| One flag for both the retry and the failover exclusion | `isIdempotent` is also the failover predicate; one requirement would silently move the other (D-M7-3) |
| Un-keyed `MountTypeOf` cache | Answers one namespace's lookup with another's table — wrong, not stale (D-M7-4) |
| Invalidate all namespaces on any mount mutation | Turns one tenant's write into a stampede for every other tenant (D-M7-4) |
| Serve `ReadMount` from the SYS-026 cache | `SYS-026` scopes the cache to `MountTypeOf`; two operations would disagree about one mount (D-M7-5) |
| Add an Appendix B §2 row for `Unknown mount table type.` | A `specifications/` change, which is R3 and the Strategic tree's; and the message is endpoint-local (D-M7-6) |
| Land `RES-030`'s `*ClusterWide` variants alongside `SYS-013` | A `SHOULD` landed with the `MUST` it wraps gets no separate review; slice c owns it (D-M7-1) |
| Land `KV-002`'s version-agnostic façade now that detection exists | Its fallback-to-`V2` rule is a guess in a public API; D-M4-9 stands (D-M7-8) |
| Tag the `HsmStatus` test with a neighbouring `SYS-*` id | Misattribution in the one artefact whose purpose is traceability (D-M7-10) |

---

# Slice b — policies and namespaces

**Status:** **proposed** — authored by an Engineering-tree Claude Opus 5 deep worker
(`agents.md` §4.2 rows 3 and 6), awaiting Strategic-tree Claude Opus 5 architecture review
(§4.2 row 4, §4.4). Appended to this record rather than opening a second one, per D-M7-1.
Slice a's `D-M7-1`…`D-M7-10` are **not** renumbered and **not** reopened.
**Risk tier:** R3, assigned in the brief and **not lowered**. Policies are the authorisation
surface itself and namespaces are the tenancy boundary, which is CRS-003's territory; the
`sys.policies.acl-read` and `sys.namespaces.write-full-replace` fixtures are new artefacts
under `specifications/`, which CRS-004 scores R3 on its own.
**Milestone:** M7, slice b of three · **Date:** 2026-09-15
**Scope:** `SYS-040`…`SYS-043`, `SYS-045`, `SYS-060`…`SYS-062`, plus the `TRN-071` ruling
D-M7-10 asked for and a `TRN-072` disposition. **Ten IDs**, baseline 192 → 182.

## Problem

Slice a took the parts of `06-system-api.md` a caller reaches to *operate* a vault. Slice b
takes the two surfaces that decide *who may do what* — the ACL policy surface, including
its dry-run, and the namespace surface. Four things here are not transcription, which is
what keeps this on row 3 rather than row 2:

1. **`SYS-045`'s `policies` field is tri-state, and C#'s defaults collapse it.** Absent,
   `[]`, and a populated list are three different requests with three different answers.
   Every idiomatic .NET shape for "an optional list" — a `string[]` defaulting to empty, a
   `params` array, an `IEnumerable<string>` with a null-coalescing read — turns the first
   two into one. The requirement exists because that collapse is the expected defect.
2. **`SYS-040` has two surfaces and two document keys, and `SYS-041` has two *different*
   reserved-name sets.** The asymmetry (`test` is reserved against writes, `default`
   against deletes) reads like a typo in the specification and is not one.
3. **`SYS-060`'s write is a full replace on an upsert route.** A caller who reaches for the
   obvious method to change one quota clears the other five. The requirement's answer is a
   second operation, and the second operation has to decide what happens when the
   namespace does not exist — a question `SYS-060` does not answer.
4. **The two types the specification names collide with .NET analyzer rules.** `Policy` and
   `Namespace` are the cross-language contract; `CA1724` and `CA1716` are errors under this
   project's `AnalysisLevel=latest-all` and `TreatWarningsAsErrors`.

## Routing classification

Row 3 trigger **(a)** — the pathfinder pass that first defines a contract in the first
language — holds: no decision record pins `Policy`, `PolicyBuilder`, `PolicyTestCase`,
`PolicyTestResult`, `PolicyMatchKind`, `Namespace`, `NamespaceSpec`, `NamespacePatch`,
`NamespacesSelf` or `Page<T>`, and there is no shipped member to transcribe against.
Trigger **(d)** holds too: the change touches `SysOperations`, a new
`LegacyPolicyOperations`, a new `Internal/SysWire`, the fixture harness, the traceability
baseline and the public-surface baseline. Recorded before dispatch by the delegating brief
(§4.3 rule 4). Review at handback is Strategic-tree Claude Opus 5.

## Decisions

- **D-M7-11 (`Policy` and `Namespace` keep the names the specification gives them; two
  analyzer rules are suppressed at the type, with the reason attached).** `CA1724` fires on
  `Policy` (it matches the `System.Security.Policy` namespace) and `CA1716` on `Namespace`
  (it matches a Visual Basic keyword). Both are errors here. The names are kept and both
  rules are suppressed with a `[SuppressMessage]` whose `Justification` cites the
  requirement, so the reason travels with the code rather than living only here.

  The two rules are weak on their own terms in this specific case: `System.Security.Policy`
  does not exist on `net10.0`, and a VB consumer — of which this SDK has none stated —
  reaches the type as `[Namespace]`. What is strong is the cost of the alternative.

  **Rejected — rename to `AclPolicy` and `NamespaceInfo`.** This is what the analyzers ask
  for and it needs no suppression. Rejected because the type names *are* the cross-language
  contract: `06-system-api.md` writes `Sys.ReadPolicy(name) -> Policy?` and
  `Namespace { Uuid, Path, … }`, and Rust and Python will hit neither rule, so .NET would
  become the one SDK of three whose public type names do not match the specification a
  reader has open beside it. It is also the *irreversible* choice of the two: a rename
  becomes the published surface, while a suppression is a two-line revert. Public API shape
  is the Strategic tree's call (**FAM-002**), so this is landed in the form that is cheapest
  to overturn.

  **Gives up:** two analyzer suppressions where the repository previously had none, which is
  a precedent as much as a change. If the Strategic tree prefers the rename, it is one
  mechanical pass plus a `PublicApiSurface.txt` regeneration.

- **D-M7-12 (`TRN-071` is tagged and leaves the baseline; D-M7-10's open question is
  closed).** The Strategic tree ruled in this slice's brief that `TRN-071` is implemented
  and tested, and that leaving it baselined makes the baseline — the project's
  remaining-work counter (D-M0-1) — report work that does not exist. The ruling is applied,
  following M5's `CFG-043` precedent (D-M5-16a).

  **Three tests carry the marker, and each of them would fail if the pin were removed**,
  which is the bar CLA-004 sets for tagging:
  - `SysAdminUnitTests.HsmStatus_pins_the_v2_prefix_…` — calls with
    `ApiVersion = "v1"` and asserts `/v2/sys/hsm/status` on the wire (slice a's test, now
    tagged; the untagged-with-a-comment form D-M7-10 landed is replaced).
  - `SysPolicyUnitTests.TestPolicy_omits_the_policies_key_…` — asserts
    `/v2/sys/policies/acl/test` from a `v1` client.
  - `SysPolicyUnitTests.The_dry_run_route_is_v2_pinned_against_both_the_client_prefix_and_a_per_call_override`
    — drives all three `/v2`-pinned SYS-045 routes with `ApiVersion = "v1"` and asserts
    `/v2` on each.

  M3's `capabilities-self` pin is also covered by `sys.capabilities-self.v2-pinned`, but
  that fixture's test is tagged `SYS-050`…`SYS-053`; it is cited as corroboration, not as
  the evidence, because the marker is what the scanner reads.

  **Rejected — tag only the `HsmStatus` test.** Sufficient to clear the counter and
  therefore tempting. Rejected because `TRN-071` says "endpoints that exist *only* on
  `/v2`", plural, and one endpoint is a weaker guard than four: a future change that
  unpinned `TestPolicy` would leave `TRN-071` green.

- **D-M7-13 (`TRN-072` also leaves the baseline — but only because this slice adds the test
  that proves it, which did not exist).** The brief asked for evidence, not a counter. The
  finding first: **no test in the tree asserted `TRN-072`.** Every typed `sys` operation did
  in fact route through `ApiPrefix`, and the two pinned ones did not, so the requirement was
  *implemented*; but the only `ApiPrefix` assertions anywhere were
  `ClientConfigurationCoverageTests`' checks that the resolved config holds the string, and
  `HarnessTests`' check that a fixture's configuration parses. Neither runs a typed `sys`
  operation under two prefixes, which is the whole claim.

  `SysNamespaceUnitTests.Unpinned_typed_sys_operations_follow_ApiPrefix_while_pinned_ones_do_not`
  is added and tagged. It runs `Sys.ListPolicies` and `Sys.ListNamespaces` under a `v1`
  client and a `v2` client and asserts the URL *moves*, and runs `Sys.HsmStatus` under both
  and asserts it does *not*. Both halves of "typed sys operations MUST use `ApiPrefix`
  unless pinned" are asserted, and the test fails against an implementation that hard-coded
  either answer.

  **Rejected — leave `TRN-072` baselined and note that it is implemented.** The honest move
  if no test existed and none were added, and it is what requirement 3 of the brief permits.
  Rejected because the test is eight lines over operations this slice already lands, and
  "implemented but unproven" is the state a traceability baseline exists to make visible
  rather than to record permanently.

  **Rejected — tag an existing test.** No existing test asserts the pin, and tagging one
  that does not is the failure mode CLA-004 and the brief both name explicitly.

- **D-M7-14 (`Sys.Legacy` is reads only).** `SYS-040` exposes the legacy surface as a
  **MAY**. `Sys.Legacy.ListPolicies` and `Sys.Legacy.ReadPolicy` land; the legacy write and
  delete do not.

  Appendix A line 33 writes the legacy row as `Sys.Legacy.ListPolicies / ReadPolicy …` with
  the note "`rules` field", and the ellipsis is the problem: the *response* key is
  specified and the *request* key is not. A legacy write would have to guess between
  `{"rules": …}` and `{"policy": …}`, and a wrong guess fails **silently** — the server
  stores nothing under a name the caller believes it wrote. That is D-M1c-25's rule applied
  to a request body rather than a response field.

  Nothing is lost: `Sys.WritePolicy` and `Sys.DeletePolicy` act on the same policies through
  the surface the requirement says to use. `SYS-040`'s actual content — that `Hcl` is filled
  from whichever of `policy`/`rules` is present — is landed once, in
  `Internal/SysWire.ToPolicy`, and *shared* by both surfaces rather than written twice.

  **Rejected — expose the full legacy CRUD with `rules` as the request key.** Symmetrical
  and probably correct. Rejected on the silent-failure argument above. **Gives up:** a
  caller migrating from a legacy tool must switch surfaces to write, which is what
  `SYS-040`'s "MUST use the `policies/acl` surface" asks for anyway.

- **D-M7-15 (a recognised `404` becomes absence, and the catch is scoped to exactly one
  code).** `SYS-040` and `SYS-060` both write their reader's answer as "→ null /
  `BV-NOTFOUND-005`" and "→ null / `BV-NOTFOUND-007`". `ReadPolicy` and `ReadNamespace`
  therefore catch that one code and return `null`; every other failure propagates.

  The scoping is the decision, not the catch. A `catch (BastionVaultException)` filtered on
  the status code, or on the `NotFound` category, would also swallow a `403` — and a reader
  that returns `null` for "you may not look" tells the caller the policy does not exist when
  the truth is that the token cannot see it, which is an authorisation failure disguised as
  an empty result. Two tests assert the negative case directly.

  **Gives up:** a caller who wants the exception rather than the `null` has to check for
  `null` and raise it itself. The specification names `null` as the reader's answer, so
  that is the right way round.

- **D-M7-16 (`SYS-041`'s two reserved sets are different sets, and the trim happens before
  the check).** `WritePolicy` refuses `{root, test}`; `DeletePolicy` refuses
  `{root, default}`. This is not a typo in the specification and the implementation does not
  "fix" it: `test` is reserved against writes because the dry-run route owns the
  `sys/policies/acl/test` segment, and `default` is reserved against deletes because it is
  the policy every token carries. A test writes `default` and deletes `test` in one method,
  so an implementation that used one set for both fails it.

  The name is trimmed **before** the comparison and the trimmed value is what is sent. The
  order matters: checking the raw argument and sending the trimmed one would let `" root "`
  through the refusal and then write `root`. Comparison is ordinal — the specification names
  two exact strings, and refusing `Root` would be a client-side refusal of a name the server
  may well accept (D-M1c-25).

- **D-M7-17 (`SYS-045`'s tri-state is decided in one writer, and `name = "root"` is refused
  client-side).** `PolicyTestCase.Policies` is `IReadOnlyList<string>?`, and exactly one
  method — `WritePolicyTestCase` — turns it into wire bytes: `null` writes no key, `[]`
  writes `"policies":[]`, a list writes itself. `TestPolicy` and `WritePolicyTests` both go
  through it, so the tri-state cannot survive on one route and be lost on the other.
  `ReadPolicyTests` parses it back symmetrically, because a reader that defaulted a missing
  key to an empty list would silently rewrite a saved case on the next write. Three separate
  wire-level tests cover the three states, and a fourth covers the read.

  `SYS-045` writes "Naming `root` → 400 `BV-INPUT-010`". It is refused **client-side** with
  that code and zero attempts. The reading is deliberate and is the one place this slice
  departs from a literal sentence: the `400` describes what the *server* does, and honouring
  it literally would mean sending the request and then remapping its answer — but the
  message text is not specified, so there is nothing to match on, and an unrecognised `400`
  defaults to `BV-INPUT-100` (D-M1c-19's neighbourhood), not the `BV-INPUT-010` the
  requirement names. Refusing client-side produces the code the requirement names, on the
  same grounds `SYS-041` already refuses `root` for `WritePolicy`.

  **Rejected — send it and remap any `400` on this route to `BV-INPUT-010`.** Literal about
  the status code. Rejected because it would mis-map every *other* `400` the dry-run route
  can answer, starting with a malformed draft.

  **Gives up:** an observable difference from the specification's sentence —
  `Attempts == 0` and `StatusCode == null` rather than `400`. **This is the one ruling in
  this slice most likely to be overturned, and it is flagged as such**; reversing it means
  deleting five lines and accepting `BV-INPUT-100` until Appendix B grows a rule, which is a
  `specifications/` change and therefore the Strategic tree's (CRS-004).

  The second row needs no code at all: naming an unreadable policy is a `403`, and
  `StatusCodeMapper` already answers `403` with `BV-AUTHZ-001`. Asserted rather than
  assumed.

- **D-M7-18 (`SYS-042` needs no code, and that claim is tested rather than stated).** Both
  strings the requirement names are already Appendix B §2 recognition rules —
  `sentinel (rgp/egp) policies cannot be created` → `BV-INPUT-100`, and the
  cross-namespace contains-rule → `BV-INPUT-102`. **No code was added and no code was
  minted.** This is the opposite of D-M7-6's situation, where the third remount message had
  no rule and needed an operation-local remap, and the difference is worth recording because
  the two look alike from the requirement text.

  Two tests: one drives both messages through `WritePolicy` and asserts the codes, and one
  asserts that `BV-INPUT-102`'s catalogue **name** is `CrossNamespacePolicyPath`, which is
  the half of `SYS-042` a code-only assertion would miss.

- **D-M7-19 (`SYS-060`: `WriteNamespace` writes every field explicitly, and
  `UpdateNamespace` refuses to create).** The full-replace body always carries
  `child_visible_default` and all six quotas, even when the spec set none of them. Omitting
  the fields and letting the server default them would produce the same result *today* and
  would hide the requirement's ⚠️ from anyone reading a captured request; writing them makes
  "an omitted quota is a reset" visible on the wire, which is where a caller debugging a
  cleared quota will look.

  `UpdateNamespace` reads, merges and writes — two round trips, because the route has no
  partial update — and raises `BV-NOTFOUND-007` when the namespace is absent rather than
  creating it. An upsert there would write the patch's *unset* members as zeroes under the
  name of a partial update, which is precisely the accident the method exists to prevent.
  `NamespacePatch` uses nullable members throughout, including `bool?`, so
  `ChildVisibleDefault = false` is expressible as an intent distinct from "leave it".

  **Rejected — make `UpdateNamespace` an upsert for symmetry with `WriteNamespace`.**
  Rejected per above. **Gives up:** a caller creating a namespace must use
  `WriteNamespace`, which is the operation whose name says "replace".

- **D-M7-20 (`MountPaths` is reused for policy names and namespace paths; no second
  normaliser).** D-M7-7's `ToWire` is exactly the rule both surfaces need — trim whitespace
  and slashes, refuse an empty result with `BV-INPUT-001` — so it is called rather than
  re-implemented, and `Internal/SysWire` holds only what is genuinely new (SYS-040's
  two-key read, SYS-060's record, the reserved-name refusal).

  What *is* new is the encoding. Mount paths are single segments the caller rarely
  parameterises; a policy name and a namespace path are neither. A policy name is encoded
  with `UrlBuilder.EncodePathSegment` (so `team/ops?admin` cannot become a second path
  segment plus a query) and a namespace path with `EncodePathFragment` (so `/` stays a
  separator — namespace paths are hierarchical — while the rest is escaped). Both then
  travel with `pathIsEncoded: true`. This is AUT-030's seam, used for the reason it exists.

  `SYS-061` says the root namespace record is unreachable over HTTP and names
  `WriteNamespace("")` as the client-side refusal. The refusal is applied to **all four**
  path-carrying namespace members, not only the write: `sys/namespaces/` addresses the
  collection, so the server's answer to a read or a delete there would not tell the caller
  that the root record is simply not exposed. A reflection test asserts that no
  root-namespace operation exists, and a second asserts `SYS-062`'s naming — `DeleteNamespace`
  is present and no member begins with `Remove` or `Drop`. A naming requirement gets a
  naming test.

- **D-M7-21 (`PolicyMatchKind` is a preserved-unknown struct, not an enum).** `SYS-045`
  names four `match_kind` values. A CLR enum cannot hold a fifth, so a server that grows one
  would force a choice between a parse failure and silently flattening it onto `none` —
  and `none` is a *meaningful* value here ("nothing matched"), so the flattening would be a
  wrong answer rather than a lossy one. `PolicyMatchKind` therefore copies `Capability`'s
  shape exactly (SYS-051, DR-0007): a `readonly record struct` over the wire string, with
  `IsOther`. An absent `match_kind` reads as `none`, which is the value the specification
  names for that case (D-M1c-25). **Gives up:** a `switch` over it is not exhaustive-checked
  by the compiler — the same cost `Capability` already pays, and the same reason.

- **D-M7-22 (`PolicyBuilder` escapes five characters and emits deterministically;
  `allowed_parameters` is a list).** `SYS-043` says "MUST escape quotes"; the emitter escapes
  `\` first, then `"`, `\n`, `\r`, `\t` — the backslash first so the escapes it adds are not
  re-escaped, and the three control characters because none of them is legal inside an HCL
  quoted string either, and a raw newline would let a caller add a line to the document. The
  hostile-input test feeds a path that tries to close its own block and open a second one
  granting `root` on `*`, and asserts **line by line** that the document still has one
  `path` line and one `capabilities` line. Counting substrings would prove nothing: the
  injected text is still present as characters, and correctly so — it is inside the quoted
  string.

  `SYS-043`'s "MUST be tested with round-trip fixtures" is read as a real round trip:
  a built document is written through `WritePolicy` and read back through `ReadPolicy`
  against a scripted wire, and must return byte-identical. That requires the emitter to be
  deterministic, so block order is insertion order, key order within a block is the
  requirement's order, and setting a metadata key twice replaces it *in place* rather than
  appending.

  `allowed_parameters` is emitted as a list of strings, like the other three optionals.
  `SYS-043` lists all four in one breath with no shape given, and the parallel construction
  is the only evidence available; a map-of-lists (which is what some other vaults use) would
  be an invention. **Flagged as an open question** rather than presented as settled.

- **D-M7-23 (`TestPolicyAsync`'s parameter order differs from the specification's
  signature, and nothing else does).** `SYS-045` writes
  `Sys.TestPolicy(draft, name?, cases[])`. C# has no optional parameter before a required
  one, so the landed signature is `TestPolicyAsync(draft, cases, name = null, …)`. The wire
  body is unaffected — `policy`, then `name` when present, then `cases`. Recorded because a
  parity reader comparing three SDKs will notice, and because Rust and Python can both
  express the specification's order and should.

- **D-M7-24 (two fixtures authored; Appendix C line 123 is now closed).** Following D-M4-8,
  D-M5-4 and D-M7-9: Appendix C line 123 already names all sixteen `sys.*` fixtures, so
  authoring an absent file fills in Appendix C's own list rather than changing specified
  behaviour, and no schema changes (CRS-004 is not triggered).
  `sys.policies.acl-read` and `sys.namespaces.write-full-replace` are authored here — the
  exact two D-M7-9 deliberately left for this slice — with slice a's `capturedFrom`
  provenance string. Fixture count on disk 226 → 228. The three `sys.*` fixtures that were
  pending for want of an operation (`sys.policy.legacy-rules-field`, `sys.policy.not-found`,
  `sys.namespaces-info.page`) are now green, and **no `sys.*` fixture is pending**.

  `sys.namespaces.write-full-replace` asserts the request body field-by-field, because the
  wire spelling of `Namespace`'s six quotas is derived rather than quoted: `06-system-api.md`
  gives the record's CLR-shaped field names and confirms `child_visible_default` in
  `SYS-060`'s prose and in Appendix B's `cannot set child_visible` recognition rule, but
  shows no JSON body. The snake_case mapping is this repository's universal convention and
  is applied; the fixture is where that derivation becomes checkable, and where a capture
  from a real server would contradict it loudly rather than quietly.

## Consequences

- **Public surface.** 190 new lines in `PublicApiSurface.txt`, regenerated mechanically by
  `PublicApiSurfaceScanner` (never hand-edited): `Policy`, `PolicyHistoryEntry`,
  `PolicyTestCase`, `PolicyTestCaseResult`, `PolicyTestResult`, `PolicyMatchKind`,
  `PolicyBuilder`, `LegacyPolicyOperations`, `Namespace`, `NamespaceQuotas`,
  `NamespaceSpec`, `NamespacePatch`, `NamespacesSelf`, `Page<T>`, and fifteen
  `SysOperations` members plus the `Legacy` property. **Additive only**; nothing removed,
  nothing re-shaped, and slice a's lines are untouched.
- **Traceability.** Baseline 192 → 182, exactly ten removals — `SYS-040`, `SYS-041`,
  `SYS-042`, `SYS-043`, `SYS-045`, `SYS-060`, `SYS-061`, `SYS-062`, `TRN-071`, `TRN-072` —
  and **no ID minted**. `tools/traceability/traceability.py --check` exits 0 with
  `covered: 239 / baselined: 182 / total: 421`.
- **No error code minted.** Every code `SYS-041`, `SYS-042` and `SYS-045` name
  (`BV-INPUT-010`, `BV-INPUT-100`, `BV-INPUT-102 CrossNamespacePolicyPath`, `BV-AUTHZ-001`,
  `BV-NOTFOUND-005`, `BV-NOTFOUND-007`) was already in the generated catalogue, and both
  `SYS-042` messages were already Appendix B §2 recognition rules.
  `tools/error-catalogue/generate.py --check` reports 130 artefacts and "would change 0".
- **Tests.** 904 → 957, all green. Coverage **99.25 % line / 96.67 % branch**, both above
  the 95 % floor (`CNF-010`, `TST-030`), with no exclusion pragma anywhere. Line coverage is
  up on slice a's 99.19 %; branch is down 0.26 pp on its 96.93 % for the same reason slice a
  recorded — every file this slice touched or added carries **zero** uncovered lines and
  **zero** partial branches, and the denominator grew against pre-existing partials in
  `ConfigurationResolver`, `HttpClientTransport` and `RequestExecutor`.
- **Parity.** .NET only, per `ROADMAP.md` D-1 as superseded and D-6. D-M7-11 is the one
  ruling that is .NET-specific by construction: Rust and Python will hit neither analyzer
  rule and should use the specification's names unadorned.
- **`CHANGELOG.md`** gains one `Added` entry (REC-001), written by the Strategic tree on
  acceptance (REC-004). `ROADMAP.md` is not touched: M7 does not close until slice c.

## Rejected alternatives, collected (slice b)

| Alternative | Why rejected |
|-------------|--------------|
| Rename `Policy`/`Namespace` to satisfy CA1724/CA1716 | The names are the cross-language contract, and a rename is the irreversible choice of the two (D-M7-11) |
| Tag only the `HsmStatus` test for `TRN-071` | `TRN-071` is plural; one endpoint is a weaker guard than four (D-M7-12) |
| Leave `TRN-072` baselined as "implemented but unproven" | The proving test is eight lines over operations this slice already lands (D-M7-13) |
| Tag an existing test for `TRN-072` | No existing test asserts the pin; tagging one that does not is the CLA-004 failure (D-M7-13) |
| Expose the full legacy policy CRUD | The legacy *request* key is unspecified, and a wrong guess fails silently (D-M7-14) |
| Catch the whole `NotFound` category, or the `404` status, in the readers | Would swallow a `403` and report "does not exist" for "may not look" (D-M7-15) |
| One reserved-name set for both `WritePolicy` and `DeletePolicy` | `SYS-041`'s asymmetry is deliberate: `test` guards writes, `default` guards deletes (D-M7-16) |
| Model `PolicyTestCase.Policies` as a non-nullable list | Collapses `null` and `[]`, which is the exact defect `SYS-045` exists to prevent (D-M7-17) |
| Send `TestPolicy(name: "root")` and remap the `400` | Would mis-map every other `400` the route answers, starting with a malformed draft (D-M7-17) |
| Add an operation-local remap for `SYS-042`, as D-M7-6 did for `SYS-023` | Both `SYS-042` strings are already Appendix B §2 rules; the remap would be dead code (D-M7-18) |
| Omit unset fields from the `WriteNamespace` body | Hides `SYS-060`'s ⚠️ from a captured request, where a caller debugging a cleared quota will look (D-M7-19) |
| Make `UpdateNamespace` an upsert | Would write the patch's unset members as zeroes under the name of a partial update (D-M7-19) |
| A second path normaliser for namespaces | `MountPaths.ToWire` is already the rule; only the *encoding* differs, and that reuses AUT-030's seam (D-M7-20) |
| Refuse the empty path only on `WriteNamespace`, as `SYS-061` literally writes | `sys/namespaces/` addresses the collection; a read or delete there answers unintelligibly (D-M7-20) |
| A CLR `enum` for `match_kind` | Cannot hold a fifth value, and flattening onto `none` would be a wrong answer, not a lossy one (D-M7-21) |
| Prove `PolicyBuilder`'s escaping by counting substrings | The injected text is still present as characters, and correctly so; only line structure proves it (D-M7-22) |

## Open questions for the Strategic tree

1. **D-M7-17's client-side refusal of `TestPolicy(name: "root")`** diverges observably from
   `SYS-045`'s "→ 400": `Attempts == 0` and no status code. Accept, or send-and-accept
   `BV-INPUT-100` until Appendix B grows a rule?
2. **D-M7-22's `allowed_parameters` as a list of strings.** `SYS-043` gives no shape. If the
   server expects a map of parameter → allowed values, the emitter is wrong and the fixture
   that would have caught it does not exist.
3. **`Sys.ReadPolicyTests`/`WritePolicyTests` wire shape.** `SYS-045` names the operations
   and the route but not the body. `{"cases": [...]}` is used, reusing the dry-run's own
   field name for the same array of the same type. Derived, not quoted.
4. **`Page<T>` versus `Page<NamespaceSummary>`.** `06-system-api.md:203` writes
   `Page<Namespace>` and `14-batch-and-request-efficiency.md` writes
   `Page<NamespaceSummary>`. `Page<Namespace>` is landed, following the section that owns
   the operation. **The two specification documents disagree**; this is reported, not fixed
   (R3, CRS-004).
5. **`PAG-001`, `PAG-003` and `PAG-005` are now genuinely exercised** by
   `Sys.ListNamespacesInfo` — the 100 default, the 1…500 validation, the empty-`next`-is-null
   rule and the length-mismatch refusal are all implemented and tested. They are
   **deliberately left on the baseline**: `PAG-001` binds all seven `*Info` operations and
   `PAG-004`'s iterator helper (`ListNamespacesInfoAll`) is not landed, so removing them
   would claim six areas that do not exist. Flagged so the decision is visible rather than
   an omission.
6. **`Sys.NamespaceLinks.*`** (Appendix A line 41, Complete tier) has no `SYS-*` bullet and
   is not implemented. Slice c's scope does not name it either. Recorded so it is not lost.
