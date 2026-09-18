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

---

# Addendum — parts 0 and 1: the B1 verification, four record fixes, and the Strategic rulings

**Status:** **proposed** — authored by an Engineering-tree Claude Opus 5 deep worker
(`agents.md` §4.2 rows 3 and 6), awaiting Strategic-tree Claude Opus 5 architecture review.
Appended per D-M7-1. `D-M7-1`…`D-M7-24` are **not** renumbered; `D-M7-17` is **overturned**
in part, by a Strategic-tree ruling recorded as **D-M7-26**.
**Risk tier:** R3, assigned in the brief and **not lowered**.
**Date:** 2026-09-18

## Decisions

- **D-M7-25 (the SYS-026 cache key is the *effective* namespace, and the fix is verified
  against the pre-fix code).** The B1 defect: `SysOperations` keyed the SYS-026 mount-type
  cache on the **view's** `activeNamespace` while `RequestExecutor.EffectiveNamespace`
  builds the `X-BastionVault-Namespace` header from `options.Namespace ?? activeNamespace`.
  A per-call namespace override therefore filed one tenant's table under another tenant's
  key. `SysOperations.CacheNamespace(options)` now computes
  `(options?.Namespace ?? activeNamespace).TrimEnd('/')` — the same expression, including
  the trim — and is called at **all five** sites: the read and the store in
  `MountTypeOfAsync`, and the invalidation in `MountAsync`, `UnmountAsync` and
  `RemountAsync`.

  The four points the brief asked to be confirmed, confirmed:

  1. **Expression parity.** `SysOperations.cs:48-51` and
     `Internal/RequestExecutor.cs:1352-1355` are the same expression modulo the
     null-conditional the nullable parameter needs. `"tenant-a"` and `"tenant-a/"` cannot
     become two keys.
  2. **Five sites.** Verified by grep and by reading: `SysOperations.cs:318` (`Mount`),
     `:328` (`Unmount`), `:353` (`Remount`), and `:398`/`:402`'s read-and-store pair.
  3. **The tests fail against the pre-fix keying.** The key expression was reverted
     locally to `activeNamespace.TrimEnd('/')` and the suite re-run: **6 tests failed** —
     `A_per_call_namespace_override_does_not_poison_the_views_cache_entry`,
     `A_per_call_namespace_override_is_never_served_from_another_namespaces_entry`,
     `A_mutation_under_a_namespace_override_invalidates_the_namespace_it_mutated` in all
     three of its `mount`/`unmount`/`remount` cases, and
     `DetectVersion_under_a_namespace_override_never_answers_from_another_tenants_table`.
     A **second** mutation dropping only the `.TrimEnd('/')` failed
     `A_trailing_slash_on_the_namespace_does_not_open_a_second_cache_entry` — the trim is
     separately load-bearing and separately guarded, which the first mutation alone would
     not have shown. The expression was restored and `git diff` on the file is empty.
  4. **All three failure modes are covered**, one test each and named as such in their
     comments: poisoning (an override call caches under the wrong key), cross-tenant read
     (a cache hit answers a differently-directed call with **no request issued**), and
     missed invalidation (a mutation under an override leaves the other tenant stale while
     clearing the wrong one). A fourth covers the trailing-slash key split and a fifth
     covers the end-to-end consequence through `Kv.DetectVersion`.

- **D-M7-26 (SYS-045's `root` refusal is **sent**, and the `400` is remapped; D-M7-17's
  client-side refusal is overturned).** The Strategic tree ruled, and the ruling is
  applied: `06-system-api.md:153-155` (`SYS-041`) says "client-side" in as many words,
  while `:172-173` (`SYS-045`) names **HTTP status codes** and pairs the root refusal with
  an unreadable-policy `403` that cannot be known client-side. The specification
  distinguishes the two cases by wording and the SDK honours it (spec wins,
  `skills/claude/SKILLS.md` §7 rule 1).

  `TestPolicyAsync` now sends `name`, and remaps the server's `400` to `BV-INPUT-010`.
  `Attempts == 1` and `StatusCode == 400` are both observable, which is the whole point of
  the reversal. The name is **trimmed before it is sent**, for D-M7-16's reason applied to
  this route: the remap keys on the trimmed value, so sending the untrimmed one would let
  `" root "` be checked as `root` and sent as something else.

  D-M7-17's rejected alternative — "remap *any* `400` on this route" — **stays rejected**,
  and that is what keeps the reversal narrow: the remap is guarded on the call having
  actually named `root`, so a malformed draft still reaches the caller as `BV-INPUT-100`.
  A test asserts the round trip, the two observables, the wire body, and the malformed-draft
  case in one method.

  **Gives up:** an operation-local remap where a reader of Appendix B sees nothing, and one
  more place Rust and Python must reimplement rather than inherit — the same cost D-M7-6
  already pays for `SYS-023`. It buys the code `SYS-045` names, at the status `SYS-045`
  names.

- **D-M7-27 (`Client.Identity`, not `Client.Sys.Identity`).** Every SYS-080 route is under
  `sys/identity/*`, so `Sys.Identity` would mirror the wire. The surface is nevertheless
  `Client.Identity`, because `06-system-api.md`'s own table writes `Identity.Profile.Read()`
  and Appendix A's canonical-operation column writes `Identity.Profile.Read`, and that
  column is the cross-language contract Rust and Python transcribe.

  **Rejected — `Sys.Identity.*`.** Mirrors the routes and keeps one `sys` surface. Rejected
  because it would make .NET the one SDK of three whose operation names do not match the
  name the specification gives them, which is D-M7-11's argument reused.

  **Gives up:** `Client.Identity` now exists holding only the SYS-080 sub-surfaces, while
  `12-other-engines-and-identity.md` owns a wider `Identity.*` on the `identity/` mount
  (`Self`, `Aliases`, `Groups`, `Sharing`, `Owner`) that a later milestone must add **to
  the same class**. That is additive and nothing here is re-shaped by it, but the class is
  a partial surface until then and a reader could mistake the absence for a decision.

- **D-M7-28 (Appendix A's `v1` prefix for `ns-assignment` loses to SYS-080).** Appendix A
  line 66 marks `Identity.NamespaceAssignment.*` prefix **v1**; `06-system-api.md`'s table
  writes the route as `/v2/sys/identity/ns-assignment/…` and SYS-080 says **all**
  `/v2/sys/identity/*` paths are pinned. Pinned to `/v2`, on the same "the owning section
  wins" rule the Strategic tree applied to `Page<Namespace>`. **Reported as a
  specification contradiction, not fixed** (R3, CRS-004).

- **D-M7-29 (three specification-level dispositions recorded, none fixed in code).** The
  Strategic rulings this slice is told to record, so a later pass finds them:
  - **`Page<Namespace>` vs `Page<NamespaceSummary>`.** `06-system-api.md:203` owns the
    namespace surface; `14-batch-and-request-efficiency.md:123` owns pagination mechanics.
    The owning section wins, so **`Page<Namespace>` stands as landed**. The two documents
    disagree; correcting one is R3 and the Strategic tree's.
  - **`PAG-001`, `PAG-003` and `PAG-005` stay on the traceability baseline.** Upheld, and
    recorded here as a **decision rather than an omission**: `PAG-001` binds all seven
    `*Info` operations and `PAG-004`'s iterator is not landed, so removing them would claim
    six areas that do not exist.
  - **D-M7-11's analyzer suppressions stand.** The specification's `Policy` and `Namespace`
    names beat `CA1724`/`CA1716`; the suppression stays scoped at the type with the
    requirement cited in its `Justification`, never at file or project level. Flagged as a
    first-in-repo precedent, and it remains the cheapest choice to overturn.

- **D-M7-30 (two confirmation gates, blocking before the Rust pass).** Neither shape below
  is derivable from any requirement, and **neither is re-guessed now** — a second guess is
  not better than the first. Both stand as landed and both become **confirmation gates**,
  the same treatment M6 gave the FIDO2 completion body:

  | Landed shape | Where | If wrong |
  |--------------|-------|----------|
  | `allowed_parameters` as a list of strings (D-M7-22) | `PolicyBuilder`'s HCL emitter | A breaking wire change across three SDKs after transcription, plus every policy a caller has already built with it |
  | `{"cases": [...]}` as `Sys.ReadPolicyTests`/`WritePolicyTests`' body (slice b open question 3) | `SerialisePolicyTestCases`, `ReadPolicyTestsAsync` | A breaking wire change across three SDKs, and saved effectivity cases that silently do not round-trip |

  **Both MUST be confirmed against the server source before the Rust pass opens.** The cost
  of confirming is one grep in `crates/bv-server`; the cost of not confirming is paid three
  times.

- **D-M7-31 (F1: DR-0012's coverage evidence, restated accurately).** Slice a's and slice
  b's Consequences each claim "every new member is at 100 % branch coverage". **That claim
  was false as written** and `coverage.cobertura.xml` contradicted it: `MountPaths.ToWire`
  0.75, `MountPaths.ToAuthWire` 0.75, `SysOperations.InitAsync` 0.90. An R3 record must not
  carry an evidence sentence its own artefact contradicts (CLA-005), so the two uncovered
  arms are now **covered** rather than the claim rephrased:
  `MountAsync(null!, …)` and `EnableAuthMethodAsync(null!, …)` are added to
  `An_empty_mount_path_is_refused_client_side_on_every_operation_that_takes_one`, which
  reaches the `path ?? string.Empty` arm of both functions. `InitAsync`'s residue is the
  async state machine's own branch, not a source arm, and is accounted for per-member in
  the Consequences below rather than claimed away.

  **F2 is not a defect and is recorded as corrected.** The brief reports
  `MountPaths.NormaliseServerKey`'s degenerate-key pass-through (`:60-70`) as untested.
  It is tested:
  `A_server_key_with_no_trailing_slash_is_normalised_and_an_unusable_one_is_passed_through`
  drives a `"/"` key through `ListMounts`, and the cobertura line for the ternary reads
  `100% (2/2)`. The test has been **extended** with an all-whitespace key so the second
  degenerate form the ruling names is also explicit, but the arm was already reachable and
  already reached. Reported rather than silently accepted (CLA-005).

- **D-M7-32 (F3: `SYS-011`'s third residue, and the argument D-M7-2 owed).** D-M7-2
  concedes two unzeroable residues. There is a third, and it is the one the landed design
  itself creates: `InitResult.cs:60` and `:71` allocate `new string(buffer)` on **every**
  read of `Keys`/`RootToken`, so a caller that reads `RootToken` in a loop produces *N*
  unzeroable heap copies where the rejected cached-`SecretString` option would have produced
  one.

  The design is still the right one, and this is the argument D-M7-2 should have made:
  the cached `SecretString` is **reachable from the live instance** after `Dispose`, so
  `Dispose` would be a claim the test could not falsify; these *N* strings are ordinary
  garbage, reachable from nothing the SDK holds, and the buffers the instance *does* own are
  asserted zero against the bytes. The trade is "one long-lived reachable copy" against "N
  short-lived unreachable ones", and for a once-per-vault call N is 1 in every realistic
  use. **Gives up:** a caller that reads the property repeatedly pays an allocation per read
  and leaves more copies in the heap than the rejected design would have — which is now
  stated on the member's XML doc as well as here.

- **D-M7-33 (mount-table invalidation is on success only, and that is a choice).**
  `Mount`, `Unmount` and `Remount` invalidate the SYS-026 cache **after** a successful
  call, so a mutation that times out *after* the server applied it leaves the cache stale
  for up to 60 s. This was unrecorded and is recorded now.

  It is deliberate and it stays: invalidating on failure would let any transport blip —
  a connection reset before the request was ever processed — clear a valid cache, which
  turns a cheap read path into a refetch on every flaky call. The stale window is bounded
  by the TTL the requirement itself sets, and the failure mode it produces is *stale*, not
  *wrong*, which is the distinction D-M7-4 already draws.

  **Rejected — invalidate in a `finally`.** Correct for the timed-out-but-applied case and
  trivially implementable. Rejected on the above. **Gives up:** the one case where it is
  wrong is also the case a caller is least likely to notice, because the caller already
  believes the mutation failed.

## Consequences (parts 0 and 1)

- **Public surface.** Unchanged. D-M7-26 alters `TestPolicyAsync`'s *behaviour*, not its
  signature; `PublicApiSurface.txt` is untouched by this part.
- **Tests.** **964 → 964**: parts 0 and 1 are test-*count* neutral and deliberately so.
  Every addition is a new case inside an existing test — the two `null!` arguments and the
  whitespace server key join tests that already enumerate their inputs — and the `SYS-045`
  root test was rewritten rather than duplicated, growing a third scenario (the malformed
  draft that must *not* be remapped). Counting tests would have overstated the change;
  the branch coverage of `MountPaths.ToWire` and `ToAuthWire` moving from 0.75 to 1.00 is
  the measurable one.
- **No error code minted.** `BV-INPUT-010` was already in the generated catalogue.
- **`CHANGELOG.md`** gains one `Fixed` entry and one `Changed` entry, written by the
  Strategic tree on acceptance (REC-004).

---

# Slice c — audit, identity, backup/restore, the Complete tier, and `RES-030`

**Status:** **proposed** — authored by an Engineering-tree Claude Opus 5 deep worker
(`agents.md` §4.2 rows 3 and 6), awaiting Strategic-tree Claude Opus 5 architecture review
(§4.2 row 4, §4.4). Appended per D-M7-1. `D-M7-1`…`D-M7-33` are **not** renumbered.
**Risk tier:** R3, assigned in the brief and **not lowered**. Backup and restore move the
whole vault's contents as a single opaque artefact and `SYS-091` is its integrity check,
which is CRS-003's "secret material" at the top tier; `RES-030` fans a state-changing
operation out across every node in a cluster.
**Milestone:** M7, slice c of three · **Date:** 2026-09-18
**Scope:** `SYS-070`, `SYS-080`, `SYS-090`, `SYS-091`, `SYS-100`, `SYS-101`, `RES-030` —
**seven IDs**, baseline 182 → 175 — plus the Complete-tier surfaces `06-system-api.md`
names without an id, and the re-authoring of `errors.enrichment.404-kv2-hint`.

## Problem

Slice c is what is left of `06-system-api.md` once the operating surface (slice a) and the
authorisation surface (slice b) are landed. Five things here are not transcription:

1. **`SYS-090` asks for streaming, and this SDK's transport contract returns a buffer.**
   `ITransport` hands back a `ReadOnlyMemory<byte>`; a `Stream`-returning backup would be a
   new transport shape across three languages.
2. **`SYS-090`'s two exclusions already exist, separately, for two different reasons** —
   and the brief is explicit that a third mechanism must not appear.
3. **`RES-030` needs to address a *named node*, and every request in this SDK is built
   against the pinned endpoint.** There was no seam for "this call, that node".
4. **`SYS-100` is satisfied by an absence**, and an absence that nothing asserts is not a
   guarantee.
5. **A third of the surface this slice lands carries no requirement ID at all** — DoS
   admin, dashboard, SSO, owner transfer, exchange — and D-M7-10's rule says no ID is
   minted for it.

And one thing that is a defect rather than a design question: Appendix B §2's recognition
rule for `BV-INPUT-103` does not match three of the four messages `SYS-091` names.

## Routing classification

Row 3 trigger **(a)** — the pathfinder pass that first defines a contract in the first
language — holds: no decision record pins `AuditDevice`, `AuditEvent`, `IdentityProfile`,
`DefaultAccount`, `SshSecurityKey`, `NamespaceAssignment`, `RestoreResult`, `DosConfig`,
`DashboardSummary`, `ClusterNodeResult` or `VaultCompatibilityGaps`. Trigger **(d)** holds
too: the change touches `RequestExecutor`, `LogicalOperations`, `DiscoveryEngine`,
`SysOperations`, `KvV1Operations`, `HintEnrichment`, `BastionVaultException`, the fixture
harness, the traceability baseline, the public-surface baseline and a `specifications/`
fixture. Recorded before dispatch by the delegating brief (§4.3 rule 4).

## Decisions

- **D-M7-34 (`SYS-090`: "MUST stream" is read as TRN-033's existing bound, and a
  `Stream`-returning overload is declined).** `SYS-090` writes the requirement as "MUST
  stream bodies (**no full buffering above `MaxResponseBytes`**)", and the parenthetical is
  what the requirement actually constrains. `HttpClientTransport` already bounds the read
  and aborts past `MaxResponseBytes` *while reading* (TRN-033, D-M1b-20), so a backup larger
  than the configured bound never lands in memory: it raises `BV-TRANSPORT-004`. A backup
  *within* the bound is returned as `byte[]`.

  A test proves the bound rather than assuming it: a 64-byte body against a 32-byte
  `MaxResponseBytes` raises rather than returning.

  **Rejected — `Task<Stream> BackupAsync()` and `RestoreAsync(Stream)`.** What "MUST stream"
  reads like on first pass, and what a backup tool would prefer. Rejected on blast radius
  and on parity: `ITransport` is DR-0004's single canonical shape and returns
  `ReadOnlyMemory<byte>`, so a streaming backup means a **new transport contract** that
  Rust and Python must also grow, inside an R3 slice, for a requirement whose own
  parenthetical is already satisfied. It is also a public-API-shape decision, which is the
  Strategic tree's (**FAM-002**). If the Strategic tree wants it, it is its own record and
  its own slice, and adding an overload later is not a breaking change.

  **Gives up:** a caller taking a backup larger than `MaxResponseBytes` must raise the
  bound rather than stream to disk, and holds the whole file in memory while doing it. Said
  on the member's XML doc, not only here.

- **D-M7-35 (`SYS-090`'s exclusions reuse SYS-013's flag and M5's seam; no third path).**
  `Sys.Backup` and `Sys.Restore` pass `nodeLocal: true` and `nonRetryable: true` — the
  identical pair D-M7-3 landed for `Seal`/`Unseal` — through a new
  `RequestExecutor.ExecuteBinaryAsync` that runs the **same** D-M1b-24 retry loop and
  differs only in how a success is classified and which half of the exchange is
  `application/octet-stream`.

  Both exclusions are proved against an adversarial policy rather than against the default,
  as D-M7-3's were: `MaxAttempts = 5`, `RetryIdempotentOnly = false`, and the failure's own
  code in `RetryOn` still yields exactly one wire attempt; and on a failover-armed
  discovery client a refused connection is terminal with no health probe and no second
  node, where `Logical.Read` on the same client replays.

  **Rejected — a flag on `ExecuteAsync`.** One fewer entry point. Rejected because every
  caller of `ExecuteAsync` receives an envelope-parsed `Outcome`, and a backup file is not
  JSON: a flag would let a future caller ask for a parse that cannot succeed.

  **Gives up:** a third public entry point on `RequestExecutor` (`ExecuteAsync`,
  `ExecuteRawAsync`, `ExecuteBinaryAsync`), and one more optional parameter on a loop that
  now carries sixteen.

- **D-M7-36 (`SYS-091` needs an operation-local remap **because the generated catalogue
  cannot express the rule**, and that is a reported defect rather than a design choice).**
  Appendix B §2 writes the row as
  `prefix `backup hmac verification failed` / `backup` + `invalid magic`/`unsupported
  version`/`corrupted` → BV-INPUT-103`. The generator splits the **top-level** `/`
  alternation into two rules correctly, but renders the second rule's `+ a/b/c` as a
  **`ContainsAll`** — an `AND` over all three tokens — where the appendix plainly means an
  alternation. Verified against the generated artefact:

  ```csharp
  new(RecognitionKind.Prefix, "backup", ["invalid magic", "unsupported version", "corrupted"],
      null, null, null, "BV-INPUT-103", -1),
  ```

  No real message contains all three, so **three of `SYS-091`'s four named failure modes
  fall through to the status table as `BV-SERVER-005`** instead of the `BV-INPUT-103` the
  requirement names — and, worse, `BV-SERVER-005` is a `5xx` code a caller may reasonably
  retry, where `BV-INPUT-103` is not. The HMAC arm is unaffected (its own prefix rule, plus
  a `contains (500) hmac verification failed` row), which is why the defect survived to
  here.

  **The same defect reaches `BV-INPUT-102`**, whose rule is
  `contains `namespace` + `refuse`/`cross-namespace``, generated as
  `Contains "namespace", ["refuse", "cross-namespace"]` — also an `AND`. Slice b's D-M7-18
  test passes only because its message happens to contain both words.

  `RestoreAsync` therefore remaps, on D-M7-6's precedent and one more of its own: the fix
  belongs in the generator or in Appendix B's notation, both of which change **recognition
  semantics for three languages** and are therefore R3 and the Strategic tree's (CRS-004).
  The remap is guarded on a `500` whose message starts with `backup` and contains **any one
  of** Appendix B's own three tokens, so it is the appendix's own rule and nothing wider; a
  `500` that is not an integrity failure, and an integrity-shaped message at a `400`, both
  keep the shared mapping's answer, and a test asserts each.

  **This code deletes cleanly the moment the generator renders `+ a/b/c` as
  `ContainsAny`.** **Reported, not silently corrected** — see the open questions.

  **Rejected — leave it and let `BV-SERVER-005` stand.** Honest about the defect and no new
  code. Rejected because `SYS-091` is a MUST and the wrong code here is also *retryable*
  where the right one is not, so the divergence is not cosmetic.

- **D-M7-37 (`RES-030`: an internal per-call endpoint override, sequential, unfiltered, and
  failures returned as values).** Four decisions in one, each with an alternative:
  - **Addressing.** `RunLoopAsync` grows an `endpointOverride` parameter that replaces
    `context.Endpoint` for every attempt of that call. Null for every other operation, so
    D-M5-11's per-attempt read is byte-identical. *Rejected — a public
    `RequestOptions.Endpoint`*: it would let any caller repoint any operation off the pinned
    node, which is DSC-040's whole subject, for one `SHOULD`'s benefit.
  - **The candidate set is unprobed and unfiltered.** `RES-030` says "**all** discovered
    candidates (including sealed/unreachable)", so `DiscoveryEngine.ClusterWideEndpointsAsync`
    returns the resolved candidates without ranking them — DSC-030…033 exist to pick *one*
    node and this operation exists to reach *all* of them. On a literal-address client the
    set is the one configured address, because DSC-001 makes literal mode "no DNS, no
    probing" and a set of one is what "all discovered candidates" then means; refusing
    instead would make the variant unusable on the commonest client shape.
  - **Sequential, in candidate order.** *Rejected — parallel fan-out*: faster, and the first
    thing a reader would reach for. Rejected because unseal progress is a **per-node share
    counter** and a caller reading a partial map while the operation runs cannot tell a slow
    node from a failed one.
  - **A failure is a value, not a throw.** `ClusterNodeResult` carries `Succeeded` and the
    `BastionVaultException`. Throwing on the first unreachable node would hide the eight
    that succeeded, which is the opposite of what "unsealing must reach every node" asks
    for.

  **Gives up:** a caller must inspect the map rather than rely on an exception, and a
  wholly-failed fan-out returns normally. The map makes that visible; a partial success
  could not have been reported any other way.

- **D-M7-38 (`SYS-100` is asserted by reflection over the operation surface, not by a
  hand-kept list).** An absence that nothing checks is a hope. The test enumerates every
  exported type whose name ends in `Operations`, plus `BastionVaultClient`, and fails on any
  declared member whose name **begins** with `Lease`, `Renew`, `Revoke`, `Wrap`, `Unwrap` or
  `Cubbyhole`.

  The scoping is the decision. A whole-assembly scan matches `NamespaceQuotas.MaxLeases`,
  which is a quota field on a namespace record, and `Response.LeaseId`, which is TRN-041's
  informational passthrough that **SYS-100's own last sentence preserves**. `TokenOperations`
  is excluded by name because AUT-080's renewal is `auth/token/renew*`, a surface that
  exists; SYS-100 names the `sys` routes only. The test also asserts it actually saw the
  surfaces (`>= 10` types, including `SysOperations` and `IdentityOperations`), so a
  refactor that renames the convention cannot turn the guard into a vacuous pass.

- **D-M7-39 (`SYS-101` lands as both a code surface and a README section).** SYS-101 says
  "documentation MUST include a Vault compatibility gaps page". `dotnet/README.md` gains the
  section with a five-row table naming each absence, its HashiCorp equivalent and what to use
  instead; `VaultCompatibilityGaps.AbsentSurfaces` carries the same five as data, and a test
  asserts **the README contains every entry in the list**, so the prose and the code cannot
  drift. The usage-guide MUSTs in `17-usage-guides.md` are M11's and are not claimed here.

- **D-M7-40 (`SYS-070`: RFC 3339 UTC by conversion, and no upper bound on `limit`).**
  `from`/`to` are converted to UTC and formatted `yyyy-MM-ddTHH:mm:ssZ` under the invariant
  culture. A non-UTC `DateTimeOffset` is **converted, not refused**: the type names an
  unambiguous instant, so the conversion is lossless, where a refusal would make a
  perfectly well-formed argument an error.

  `limit` is validated `>= 1` with `BV-INPUT-004`, and **has no upper bound**. PAG-001's
  `1…500` governs the `*-info` cursor listings; `SYS-070` names only the lower bound, so
  capping at 500 here would be a client-side refusal of a request the requirement does not
  refuse (D-M1c-25). The `500` in the signature is the requirement's **default**, not a cap.

  The values go through `UrlBuilder.EncodeQueryValue` — the one query encoder (D-M7-20, no
  second one). RFC 3339 UTC happens to contain no character a query component must escape,
  so the encoded and literal forms coincide; the requirement is that the value is *encoded*,
  not that it is mangled, and the test says so rather than asserting a `%3A` the encoder
  correctly does not produce.

  Event order is the **server's**, passed through unsorted: `SYS-070` states the server
  orders newest first, which is a fact about the wire and not an instruction to re-sort.
  A client-side sort would disagree with the server whenever two events share a timestamp.

- **D-M7-41 (`SYS-080`: the write-preserve tri-state, and `windows_password` as an absence
  rather than an emptiness).** `UpdateContactAsync(string? email, string? phone)` preserves
  three states on the wire: `null` omits the key (**keep**), `""` writes an empty string
  (**clear**), any other value replaces. This is D-M7-17's tri-state shape applied to a
  second requirement, and for the same reason — every idiomatic non-nullable signature
  collapses "keep" and "clear" into one request.

  `DefaultAccount.WindowsPassword` is a `SecretString?` and is **`null` when the server did
  not send it**, never an empty `SecretString`. SYS-080 withholds the field outside a GET by
  the owner, so "the server withheld it" and "there is no password" are different facts and
  a caller may need to tell them apart. It is wrapped in `SecretString` so `ToString()` can
  never put it in a log (CNF-031), and the fixture driver reports only *whether* it was sent.

- **D-M7-42 (no ID is minted for the Complete-tier surfaces, and their tests are
  deliberately untagged).** `Sys.Dos.*`, `Sys.DashboardSummary`, `Sys.SsoSettings`,
  `Sys.SsoProviders`, `Sys.OwnerTransfer.*` and `Sys.Exchange.*` are named in
  `06-system-api.md`'s Complete-tier tables and in Appendix A, and **none carries a `SYS-*`
  bullet**. They are implemented and tested; the tests carry no `[Requirement]` attribute
  and say why in a comment, exactly as D-M7-10 did for `Sys.HsmStatus`. No neighbouring ID
  is misattributed and none is invented.

  Where the specification gives a body shape (the DoS config's six fields) it is modelled.
  Where it does not — the owner-transfer spec, every exchange body, the DoS stats and SSO
  responses — the body is forwarded or returned as a `JsonElement` rather than typed from a
  guess: a wrong guess on a **write** body fails silently, which is exactly the argument
  that kept the legacy policy write out of D-M7-14.

  The DoS config write is a **partial update** (the table says so in as many words), which
  is the opposite of `SYS-060`'s full-replace namespace write. Both are the requirement's
  choice, not the SDK's, and each is asserted in its own test so the pair cannot be
  "harmonised" later.

- **D-M7-43 (`errors.enrichment.404-kv2-hint` is re-authored against `Kv.V1.Read`, and
  ERR-040's KV-v2 row lands at the operation).** D-M4-14 re-booked this fixture to M7 for
  two reasons, both now discharged. The fixture named `Kv.V2.ReadSecret` on
  `GET /v1/secret/app/db`, a route with no `data/` segment that KV2-001 makes impossible for
  any `Kv.V2.*` call. It is re-authored as the realistic user error the hint addresses — a
  **v1-shaped read against a v2 mount** — driving `Kv.V1.Read`, and its mount-table exchange
  now sends `{"type":"kv-v2"}`, the two-field shape `SYS-020` specifies, rather than the
  `options.version` form `MountInfo` does not carry. `ErrorFixturesTests`' pending set is now
  **empty**: every `errors.*` fixture runs against real SDK code.

  The enrichment itself lives at **`KvV1Operations.ReadAsync`**, not in `HintEnrichment`.
  The row's condition needs the SYS-026 mount type *and* a mount/name split, and at the
  operation both arrive as separate arguments so nothing is guessed by splitting a path.
  The lookup goes through `Sys.MountTypeOf`, so it is free on a warm cache; and if the
  lookup itself fails — no read on `sys/mounts` is the ordinary case — the caller's original
  error is returned **unchanged**, because an enrichment must never replace the failure it
  was trying to explain. Both halves are tested.

  **Rejected — enrich in `RequestExecutor`, where the other seven rows live.** The globally
  right place. Rejected because it would put a mount-table lookup behind **every** `404` the
  SDK can raise, and would have to guess the mount/name split from a path string.

  ⚠️ The note's text names **`Kv.ReadSecret`**, the version-agnostic façade D-M4-9 declined
  and D-M7-8 kept declined. The text is `04-error-model.md`'s, character for character, and
  ERR-040 requires the enrichment to be deterministic and fixture-covered, so it is emitted
  as written rather than rephrased. Recorded as an open question, not papered over.

- **D-M7-44 (an `Undefined` `JsonElement` body is refused client-side).** Found while
  testing: `default(JsonElement)` is `JsonValueKind.Undefined`, and `JsonSerializer` answers
  it with a bare `InvalidOperationException` — a runtime exception type, which ERR-020 and
  TRN-054 say a caller never has to catch. Every operation that forwards an unmodelled body
  goes through `SysWire.RequireJsonBody`, which refuses `Undefined` with `BV-INPUT-001` at
  zero attempts. One function, so the refusal cannot be present on one route and missing on
  another; eight routes asserted in one test.

- **D-M7-45 (`Sys.NamespaceLinks.*` is out of scope and recorded as unimplemented).**
  Appendix A line 41 names it at the Complete tier with no `SYS-*` bullet, and slice c's
  scope does not include it. **Not implemented**, recorded here so it is not mistaken for an
  oversight, and carried forward as an open question with the rest of the untyped Appendix A
  rows (`Sys.Raw.*`, `Sys.Plugins.*`, `Sys.ScheduledExports.*`, `Sys.KvOwnerClaim`,
  `Sys.Export`/`Sys.Import`).

## Consequences (slice c)

- **Public surface.** **143 new lines** in `PublicApiSurface.txt`, regenerated mechanically
  by `PublicApiSurfaceScanner` (never hand-edited) and verified **purely additive** —
  `git diff --stat` reports `143 insertions(+), 0 deletions(-)`, so nothing was removed and
  nothing re-shaped. New types: `AuditDevice`, `AuditDeviceSpec`, `AuditEvent`,
  `AuditOperations`, `IdentityOperations`, `IdentityProfileOperations`,
  `DefaultAccountOperations`, `SshSecurityKeyOperations`, `NamespaceAssignmentOperations`,
  `IdentityProfile`, `DefaultAccount`, `DefaultAccountSpec`, `SshSecurityKey`,
  `SshSecurityKeySpec`, `NamespaceAssignment`, `RestoreResult`, `DosConfig`,
  `DosOperations`, `DashboardSummary`, `OwnerTransferOperations`, `ExchangeOperations`,
  `ClusterNodeResult`, `VaultCompatibilityGaps`; plus `BastionVaultClient.Identity` and
  eight `SysOperations` members.
- **Traceability.** Baseline **182 → 175**, exactly seven removals — `SYS-070`, `SYS-080`,
  `SYS-090`, `SYS-091`, `SYS-100`, `SYS-101`, `RES-030` — and **no ID minted**.
  `tools/traceability/traceability.py --check` exits 0 with
  `covered: 246 / baselined: 175 / total: 421`.
- **No error code minted.** `BV-INPUT-004` and `BV-INPUT-103 BackupFileInvalid` were both
  already in the generated catalogue. `tools/error-catalogue/generate.py --check` reports
  130 artefacts and "would change 0". The `BV-INPUT-103` **recognition rule** is defective
  (D-M7-36) and that is reported, not patched.
- **Tests.** 964 → **1006**, all green — 42 new tests, all in
  `SysCompleteUnitTests.cs`. Coverage **99.34 % line / 96.75 % branch**, both
  above the 95 % floor (`CNF-010`, `TST-030`) and both **above slice b's** 99.25 % / 96.67 %,
  with no exclusion pragma anywhere. Every file this slice added carries zero uncovered
  lines; the residual partial branches in touched files are, per member:
  `SysOperations.IsUnknownMountTableType` 0.50, `SerialiseInit` 0.83,
  `ReadPolicyTestResults` 0.75, `ToPolicyTestCase` 0.75, `IsBackupIntegrityFailure` 0.83,
  `SysWire.ToPolicy` 0.83, `ToNamespace` 0.90, `DiscoveryEngine.ClusterWideEndpointsAsync`
  0.75 — each the compiler-generated short-circuit half of a `&&`/`?:` whose two *source*
  outcomes are both exercised, plus the `candidates ?? await Resolve` arm that needs an
  unresolved discovery client. The async `MoveNext` entries
  (`InitAsync` 0.90, `UpdateNamespaceAsync` 0.94, `ListNamespacesInfoAsync` 0.93,
  `CapabilitiesSelfAsync` 0.80, `ClusterStatusAsync` 0.75) are state-machine branches, not
  source arms, which is the accurate restatement F1 asked for.
- **Fixtures.** No new fixture authored; **one existing fixture re-authored**
  (`errors.enrichment.404-kv2-hint`), which is the single `specifications/` edit this brief
  authorises. Fixture count on disk unchanged at 228. `ErrorFixturesTests`' pending set is
  empty for the first time since M1c.
- **Parity.** .NET only, per `ROADMAP.md` D-1 as superseded and D-6. The places Rust and
  Python will diverge in *implementation* while holding the same *contract* are D-M7-34's
  buffer-versus-stream reading (both languages can stream more cheaply and may) and
  D-M7-36's remap (which both must reimplement until the generator is fixed, or neither
  needs if it is).
- **`CHANGELOG.md`** gains one `Added` entry and one `Fixed` entry (REC-001), written by the
  Strategic tree on acceptance (REC-004). **`ROADMAP.md`: M7 is now complete** and its
  §2/§4/§5 rows are the Strategic tree's to close (REC-002, REC-004).

## Rejected alternatives, collected (slice c)

| Alternative | Why rejected |
|-------------|--------------|
| `Task<Stream> BackupAsync()` / `RestoreAsync(Stream)` | A new `ITransport` shape for three languages inside an R3 slice, for a requirement whose own parenthetical TRN-033 already satisfies (D-M7-34) |
| A `binary` flag on `ExecuteAsync` | Every caller of `ExecuteAsync` gets an envelope-parsed `Outcome`; a backup file is not JSON (D-M7-35) |
| A third retry/failover exclusion mechanism for SYS-090 | SYS-013's `nonRetryable` and DSC-045's `nodeLocal` are exactly the two exclusions the requirement names (D-M7-35) |
| Let `SYS-091`'s three non-HMAC failures stand as `BV-SERVER-005` | SYS-091 is a MUST, and the wrong code is also *retryable* where the right one is not (D-M7-36) |
| Fix the generator's `+ a/b/c` rendering here | Recognition semantics are a cross-language contract; an Appendix B / generator change is R3 and the Strategic tree's (D-M7-36) |
| A public `RequestOptions.Endpoint` for RES-030 | Would let any caller repoint any operation off the pinned node — DSC-040's whole subject — for one `SHOULD` (D-M7-37) |
| Probe and rank RES-030's candidates | RES-030 says "all discovered candidates (including sealed/unreachable)"; ranking exists to pick one node (D-M7-37) |
| Fan out to the nodes in parallel | Unseal progress is a per-node share counter; a partial map read mid-flight cannot distinguish slow from failed (D-M7-37) |
| Throw on the first unreachable node | Would hide the nodes that succeeded, which is the opposite of "unsealing must reach every node" (D-M7-37) |
| Assert SYS-100 with a hand-kept list of forbidden members | A list is only as current as its last edit; reflection over the operation surface fails on a member that does not exist yet (D-M7-38) |
| Scan the whole assembly for SYS-100 | Matches `NamespaceQuotas.MaxLeases` and `Response.LeaseId`, the latter of which SYS-100's own last sentence preserves (D-M7-38) |
| Cap `Sys.Audit.Events`' `limit` at 500 like PAG-001 | SYS-070 names only the lower bound; the cap would refuse a request the requirement does not (D-M7-40) |
| Refuse a non-UTC `from`/`to` | A `DateTimeOffset` names an unambiguous instant, so the conversion is lossless (D-M7-40) |
| Re-sort audit events newest-first client-side | SYS-070 states a fact about the wire; a client sort would disagree with the server on ties (D-M7-40) |
| Non-nullable `string` parameters on `UpdateContact` | Collapses "keep" and "clear", which is the defect write-preserve exists to prevent (D-M7-41) |
| Default `windows_password` to an empty `SecretString` | Makes "the server withheld it" indistinguishable from "there is no password" (D-M7-41) |
| Type the owner-transfer and exchange request bodies | The bodies are unspecified, and a wrong guess on a *write* fails silently (D-M7-14's argument, D-M7-42) |
| Mint a `SYS-*` id for the DoS / dashboard / SSO / transfer / exchange surfaces | D-M7-10's rule: no ID is invented, and no neighbouring one is misattributed (D-M7-42) |
| `Sys.Identity.*` mirroring the `sys/identity/*` routes | The specification and Appendix A both write `Identity.Profile.Read`; .NET would be the only SDK of three that differs (D-M7-27) |
| Enrich ERR-040's KV-v2 row in `RequestExecutor` | Would put a mount-table lookup behind every `404` the SDK can raise, and must guess the mount/name split (D-M7-43) |
| Rephrase the KV-v2 note so it does not name the declined `Kv.ReadSecret` façade | The text is `04-error-model.md`'s, and ERR-040 requires determinism against a fixture (D-M7-43) |
| Let `default(JsonElement)` reach `JsonSerializer` | Surfaces a bare `InvalidOperationException`, which ERR-020/TRN-054 forbid (D-M7-44) |

## Open questions for the Strategic tree (slice c)

1. **Appendix B §2's `+ a/b/c` notation generates a `ContainsAll`, and it should be a
   `ContainsAny`.** Two rows are affected and both are wrong today: `BV-INPUT-103`'s
   `backup` rule (three of `SYS-091`'s four named messages miss it — worked around by
   D-M7-36's operation-local remap) and `BV-INPUT-102`'s cross-namespace rule (slice b's
   D-M7-18 test passes only because its message contains both alternatives). **This is a
   `specifications/`-or-generator change, R3, and not a delegate's** — reported, with the
   remap written so it deletes cleanly. Fixing it also lets D-M7-36's code go.
2. **`SYS-090`'s "MUST stream" read as TRN-033's bound (D-M7-34).** Accept, or open a slice
   for a `Stream`-returning overload and the `ITransport` change it needs across three
   languages?
3. **ERR-040's KV-v2 note names `Kv.ReadSecret`, which this SDK does not ship** (D-M4-9,
   D-M7-8). The text is the specification's and is emitted verbatim. Accept the note
   pointing at an absent API, reinstate the façade, or amend `04-error-model.md`'s row —
   the last two are R3.
4. **Appendix A line 66 marks `Identity.NamespaceAssignment.*` as `v1`; SYS-080 pins all
   `/v2/sys/identity/*`** (D-M7-28). Landed pinned. Which document is wrong?
5. **`Client.Identity` now exists holding only SYS-080's four sub-surfaces** (D-M7-27),
   while `12-other-engines-and-identity.md` owns a wider `Identity.*` a later milestone must
   add to the same class. Confirm the placement before that milestone starts, because moving
   it afterwards is a breaking change.
6. **Six Appendix A `sys` rows remain untyped and unimplemented**, none of which carries a
   `SYS-*` bullet: `Sys.NamespaceLinks.*` (line 41, explicitly out of this brief's scope —
   D-M7-45), `Sys.Raw.*`, `Sys.Plugins.*`, `Sys.ScheduledExports.*`, `Sys.KvOwnerClaim` /
   `OwnerBackfill`, and `Sys.Export` / `Sys.Import`. The last of these is named in
   `SYS-090`'s own table but carries no MUST of its own. Recorded so none is lost; which
   milestone owns them is the Strategic tree's.
7. **The CNF-025 secret scan is red at `HEAD` and remains red.** DR-0001 recorded it as an
   open finding (11 matches of the `s.FAKEtoken…` test constant against tracked files, none
   a real secret). This slice adds a **twelfth** instance of the identical constant in
   `SysCompleteUnitTests.cs`. The pattern was **not narrowed** — the brief forbids it and so
   does CLA-004 — so the gate's state is unchanged in kind and worse by one in count. The
   decision DR-0001 asked for is still outstanding.
