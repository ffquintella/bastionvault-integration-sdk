# DR-0009 — M4: the KV engine (v1 + v2) in .NET, and what `Core` actually requires

**Status:** **accepted** — authored by the Strategic Orchestrator (Claude Opus 5).
**Risk tier:** R2 (`agents.md` §5.3 — a new public contract of cross-language shape; no
secret material of its own, no TLS/retry surface of its own). One R3-shaped finding is
recorded below (D-M4-3) and is a *planning* correction, not a `specifications/` change.
**Milestone:** M4 · **Date:** 2026-09-15
**Supersedes nothing. Amends:** `ROADMAP.md` D-4 (see D-M4-3).
**Inherits:** [DR-0004](0004-m1b-transport.md) (transport seam, Shape A/B envelope, retry),
[DR-0005](0005-m1c-error-model.md) (recognition, enrichment, and D-M1c-25: a deferred
branch returns the value the specification names, never a plausible guess),
[DR-0006](0006-m2-authentication.md) (sub-API grouping, `EnvironmentScope` from AUT-044),
[DR-0007](0007-m3-system-api-core.md) (sub-client wiring precedent, typed-wrapper pattern).

## Problem

M4 is the whole of `specifications/07-kv-engine.md`: KV v1, KV v2 versions, CAS, soft
delete, destroy, metadata, per-environment overrides, path helpers and convenience
helpers. `ROADMAP.md` §4 books it as 27 requirement IDs, `Large`, R2, and names its exit
gate as **"conformance level `Core` declared in the .NET README"**.

Grounding the gate against `specifications/01-conformance-and-quality.md` shows the gate as
written cannot be met at M4. That finding is D-M4-3 and it is the reason this record exists
before any code does.

## Routing classification: M4a is row 3, M4b is row 2

`agents.md` §4.2's discriminator asks whether the contract is settled — settled meaning an
accepted decision record pins the public names and the behaviour, so the task is
transcription and verification rather than design. For M3 the answer was yes and DR-0007
routed it to row 2. For M4's first slice the answer is **no**, on four axes:

1. **No decision record pins any KV name.** Section 07 gives a language-neutral type block
   and an operation table; it does not give C# signatures, and unlike M3 there is no
   already-shipped member to transcribe against.
2. **Fixture coverage is partial.** 20 fixtures exist under `specifications/fixtures/kv/`
   (15 pre-existing, 5 authored for this brief — D-M4-8) and they pin the data-path
   members precisely. They say nothing about `WriteConfig`/`UpdateConfig`'s split, the
   path helpers (KV2-030), or the convenience helpers (KV-011…013). Roughly a third of the
   surface is unfixtured.
3. **The specification contains an unresolved question in its own prose.** KV2-009 argues
   with itself in-line ("server treats omitted `environments` as unchanged? — no: …") and
   then mandates a read-merge-write helper whose shape it never gives.
4. **The change spans four components** — the client root, a new two-level sub-client
   tree, error enrichment (KV2-023 plus DR-0005's carried KV v2 mount-hint row), and the
   test harness (a new `__authInfo` instrument the KV2-022 fixture already assumes) —
   which is §4.2 row 3 trigger (d) on its own.

**Recorded escalation trigger, written before dispatch** (`agents.md` §4.3 rule 4): row 3
trigger **(a)**, the pathfinder pass that first defines a contract in the first language,
together with trigger **(d)**, three or more components. Neither is "this surface feels
risky"; both are conditions §4.2 states literally, and (a) is refuted only by a decision
record pinning the contract, which does not exist until this one is accepted.

Once this record is accepted, the contract *is* settled, which is why M4's second slice
routes to row 2 (`eng-implementation`): it implements names this record pins.

**Review at handback:** Strategic-tree Claude Opus 5 for both slices — row 3's gate is Opus
by §4.2, and R2 is Opus by §4.4.

## Decisions

- **D-M4-1 (slicing).** M4 lands in two sequential slices. They touch the same public
  contract, so they do not run in parallel (`agents.md` §7.4).
  - **M4a — the KV contract and the data path.** `Client.Kv`, `Kv.V1` in full, `Kv.V2`'s
    data/metadata/config paths including environments and path helpers. IDs: KV-002,
    KV1-001…004, KV2-001…011, KV2-020…024, KV2-030 (22 of the 27).
  - **M4b — helpers, carried follow-ups, and the README.** KV-011, KV-012, KV-013, the
    DR-0005 KV v2 mount-hint enrichment row, registration of
    `errors.recognition.missing-token-client-side` (M4's per D-M2-10), and the .NET README
    with its CNF-002 gap list (D-M1a-22's obligation). IDs: KV-011…013 (3 of the 27).

- **D-M4-2 (two IDs are deferred, with named owners).** 25 of the 27 IDs land in M4; two
  cannot, because their dependencies are booked later and D-M1c-25 forbids a plausible
  guess in their place:
  - **KV-001 `Kv.DetectVersion`** requires `Sys.MountTypeOf` (SYS-026), which requires
    `Sys.ListMounts` and the `MountInfo` shape — M7's contract. Deferred to **M7**.
    Implementing a private mount lookup now would pin M7's contract by accident, which is
    exactly the failure mode Stage 1 is most exposed to (D-1's consequence paragraph).
  - **KV-010 `Kv.ReadMany`** requires `Sys.Batch` (BAT-007) — M8. Its only fixture,
    `kv.read-many-batch`, carries `BAT-001`/`BAT-005`/`BAT-007` and no `KV` ID, so
    Appendix C already books it with the batch work. Deferred to **M8**.
  Both stay on the traceability baseline and both appear by ID in the README's known-gaps
  list. M4's exit is therefore **25 IDs off the baseline**, not 27; `ROADMAP.md` §4's count
  is corrected accordingly at handback.

- **D-M4-3 (the `Core` declaration does not belong to M4, and the roadmap's D-4 is wrong
  about all three levels).** `01-conformance-and-quality.md` defines `Core` as sections 00,
  01, 02, 03, 04, 05 (Token + AppID + Userpass), 06 (health, seal-status, capabilities,
  token ops), 07, 13 (retry only), **15, 16, 17 (guides 1–5)**. CNF-001 requires every MUST
  of every section in the declared level; CNF-002 forbids claiming a level whose sections
  contain unimplemented MUSTs. Sections **16 and 17** are the `DOC` requirements, booked to
  **M11** — *after* M8's `Standard` and M10's `Complete` declarations. So the declaration
  schedule in `ROADMAP.md` D-4 is unsatisfiable at M4, M8 and M10 alike, and no amount of
  KV work fixes it.
  **Ruling.** M4 does **not** declare `Core`. M4b authors the .NET README as CNF-041 and
  D-M1a-22 require — target level (`Complete`, CNF-003), **no level declared yet**, the
  known-gaps list by requirement ID, spec revision, tested server version — and states
  plainly that the first declarable level waits on sections 16 and 17. Whether M11 moves
  ahead of M8 or the declarations move to the end is a **sequencing question for the
  project owner**, recorded as `ROADMAP.md` §10 and not decided here: it changes milestone
  order, which is the owner's call, and either answer leaves M4's deliverable unchanged.
  This is a planning correction. It changes no requirement text, so it is not a
  `specifications/` change and does not trip CRS-004.

- **D-M4-4 (public shape: parameter order and the default mount).** Section 07 writes every
  operation as `(mount, path, …)` and gives `mount` the default `"secret"`. C# cannot
  default a leading parameter, and the fixture driver binds arguments **by name**, so the
  wire contract is indifferent to order. Therefore: **`path` first, `mount` second with
  default `"secret"`, then the `RequestOptions? options = null, CancellationToken
  cancellationToken = default` tail every existing operation already carries.** One method
  per operation; no overload pairs. `List` takes `(prefix = "", mount = "secret", …)`.
  Rejected: mount-first with per-operation overloads (doubles a 20-member surface for one
  default), and a `Kv.Mount("kv")` scoping accessor (invents a shape section 07 does not
  describe, and CNF-027's surface baseline would then carry it forever).

- **D-M4-5 (public shape: types).** Exactly the spec's type block, PascalCase members as
  the fixtures already expect: `KvV1Secret`, `KvV2Secret`, `KvV2VersionMetadata`,
  `KvV2Metadata`, `KvV2Config`, and `KvV2SecretState { Live, SoftDeleted }`. Two
  deliberate deviations, both naming-only: the spec's `WriteOptions` ships as
  **`KvWriteOptions`** (a bare `WriteOptions` beside `RequestOptions`/`LoginOptions` reads
  as transport-level and is not KV-scoped), and `KvV2VersionMetadata.Operation` ships as a
  **`string?`**, not an enum — KV2-011 makes it an optional BastionVault extension whose
  value set the server owns, and an enum would have to invent a fallback member for an
  unknown value, which D-M1c-25 forbids.

- **D-M4-6 (`UpdateConfig` is the read-merge-write, `WriteConfig` replaces).** KV2-009's
  mandate ships as three members: `ReadConfigAsync` → `KvV2Config?`; `WriteConfigAsync(
  KvV2Config)` which sends all four fields and is documented as a full replace; and
  `UpdateConfigAsync(KvV2ConfigPatch)` which reads, merges the set fields, writes the full
  config back, and returns the merged `KvV2Config` it wrote. `KvV2ConfigPatch` is
  `{ MaxVersions: int?, CasRequired: bool?, DeleteVersionAfter: string?, Environments:
  string[]? }` — null means "leave alone", which is the only way a patch can distinguish
  "unset" from "set to the default". Pinned on the wire by the authored fixture
  `kv.v2.config-environments` (two exchanges, GET then POST).

- **D-M4-7 (KV2-022's client-side fail-fast reads the credential the client already
  holds).** `EnvironmentScope` exists since M2b as a projection of AUT-044's metadata.
  KV2-022's check is therefore a read of the *current* credential's `AuthInfo`, and M4a
  decides where that lives — if the client does not already retain the last resolved
  `AuthInfo`, retaining it is part of this slice, and the retained value is one field of
  one existing object, never a second copy of the scope (the reason `EnvironmentScope` is
  derived rather than stored, per its own doc comment). The check fires only for v2 **data**
  operations (`data/{path}` read and write) with no `env`, raises `BV-KV-009` with
  `secret_globs` and `machine_globs` in the details and the globs in the hint, and costs
  **zero** requests (`attempts: 0`), all three as `kv.v2.env-scoped-token-requires-env`
  already asserts. `Kv.V1.*` is never subject to it (KV1-004).

- **D-M4-8 (five fixtures authored with this brief).** Appendix C mandates 21 `kv.*`
  fixtures; 15 were on disk. Authored here, Claude-owned per D-M0-7: `kv.v1.list`,
  `kv.v1.write-empty-data-rejected`, `kv.v2.undelete`, `kv.v2.metadata-read`,
  `kv.v2.config-environments`. The 21st, `kv.read-many-fallback-on-unsupported`, belongs
  with `kv.read-many-batch` to M8 per D-M4-2. Fixture count on disk: **213 → 218**, all 218
  schema-valid (FIX-001 gate run locally).

- **D-M4-9 (no `Kv.ReadSecret` façade).** KV-002 offers a version-agnostic façade as a
  **MAY** and makes it depend on `DetectVersion`, which D-M4-2 defers. The MAY is declined:
  a façade that guesses `V2` whenever the detection call is unavailable is a plausible
  guess in a public API, and adding it later is not a breaking change while removing it
  would be. KV-002's MUST — that the operations are version-explicit under `Kv.V1`/`Kv.V2`
  — is satisfied by the shape D-M4-4 pins, so KV-002 is covered in M4a.

## Consequences

- M4 exits with 25 of 27 IDs off the baseline (273 → 248 expected), a `Client.Kv` surface
  usable for real application integration, and the first per-language README in the repo.
- It exits **without** a declared conformance level, which is a visible, deliberate
  admission rather than a silent gap — the README says what is missing by ID.
- Stage 2's M13 pass inherits this record as the pinned KV contract. The parameter-order
  ruling in D-M4-4 is the item most likely to be transcribed wrongly into Rust and Python
  (both of which *can* default a leading parameter and will be tempted to follow the spec's
  literal order); the fixtures bind by name, so nothing would catch it.
- The sequencing question in D-M4-3 is now the project's largest open planning item: three
  of the roadmap's milestone gates are stated in terms of a declaration none of them can
  make.

## Verification required at handback

`agents.md` §9 plus, for this milestone:

1. `dotnet test` green, with line **and** branch coverage ≥ 95 % and no exclusion pragma
   (CNF-010, VER-004), figures quoted.
2. All 20 `kv.*` fixtures on disk accounted for: the **19** in M4's scope green, and
   `kv.read-many-batch` explicitly pending with M8 named. (Appendix C's 21st fixture,
   `kv.read-many-fallback-on-unsupported`, is unauthored and is M8's to write — corrected
   from this record's first draft, which said 18.)
3. `specifications/`, `rust/` and `python/` untouched (D-6, Stage 1), and the error
   catalogue regeneration byte-identical.
4. Traceability delta exactly the IDs the slice claims, with KV-001 and KV-010 still
   baselined.
5. CNF-027's public-surface baseline updated in the same change, never suppressed.
6. The proposed `CHANGELOG.md` line returned in the summary, not written by the delegate
   (REC-004).

## Addendum — M4a handback rulings (accepted 2026-09-15)

The M4a handback review (Strategic tree, Claude Opus 5, R2 gate per `agents.md` §4.4)
returned **approve with required fixes**. It confirmed every claim first-hand: 708/708
tests, 99.18 % line / 96.92 % branch with no exclusion pragma, zero warnings on a clean
rebuild, 19 of 20 `kv.*` fixtures green, and a baseline delta of exactly the 22 IDs. The
Strategic Orchestrator re-ran the traceability tool and the surface diff independently
(CCF-002), and confirms the same figures.

Three behaviours the delegate implemented **beyond the literal requirement** were carried
back as open questions, justified only in code comments ending "recorded for Strategic
review". A behaviour that lives in a comment is a behaviour Stage 2 will drop silently —
the fixtures bind by name, so nothing catches its absence. They are ruled on here, and the
comments now cite the ruling instead of requesting one.

- **D-M4-10 (an explicit empty `versions` list is an error, not "latest").**
  `Kv.V2.SoftDelete` with `versions: []` raises `BV-INPUT-002` client-side. Section 07's
  table says "no body → latest only", which describes the **absence** of a list; reading an
  empty list as "latest" widens a delete the caller deliberately narrowed, and a caller
  whose loop produced no versions almost never means "delete the current one". `null`
  (absent) keeps the specified meaning: latest only, no body sent. Consistent with KV2-007,
  which already makes an empty list an error for `Undelete`/`Destroy`.

- **D-M4-11 (`..` is refused as a whole path segment, on every caller-supplied KV path and
  on `mount`).** KV2-008 names only `List` prefixes. That is too narrow to be the whole
  rule: percent-encoding cannot neutralise `..` (`.` is unreserved, so it survives
  encoding), segment separators are deliberately preserved, and a normalising HTTP stack
  can collapse `secret/data/a/../../auth/token/lookup-self` onto a different route. The
  rule is therefore applied to every caller-supplied path, prefix **and mount**, and is
  narrowed from "contains `..`" to "has a `..` segment" so that a legitimate key such as
  `release..candidate` stays addressable. KV2-008 remains satisfied as the specified
  subset. This is the same defect class as M2b's Userpass path injection (AUT-030/TRN-020),
  found the same way: by reading the code rather than the fixture.

- **D-M4-12 (a missing metadata timestamp is a protocol error, not a defaulted value).**
  `created_time` and `updated_time` are declared without `?` in section 07's type block, so
  their absence is a server contract violation and raises `BV-PROTOCOL-002`. D-M1c-25
  forbids the alternative — a defaulted `DateTimeOffset.MinValue` is exactly the plausible
  guess that produced three M1c defects. **The consequence is recorded explicitly because
  it is not free:** a `200` read of a *live* secret whose metadata omits a timestamp now
  fails, denying the caller data the server did return. That is the correct trade while the
  SDK has never run against a live server (M12); if a real BastionVault build is found to
  omit either field, this ruling is what must be revisited, not the type block.

- **D-M4-13 (`ttl` must be non-negative).** Section 07 is silent on a negative `ttl` for
  `Kv.V1.Write`, and `GoDuration.Format` will happily emit `"-1h"`. A negative lease has no
  meaning the server defines, so passing it through is a guess: it is rejected client-side
  with `BV-INPUT-001`.

Two required fixes are **defects**, not rulings, and are corrected in M4b:

- **RF-1 (security).** The `..` guard introduced by M4a was applied to `path` and `prefix`
  at 13 sites and never to `mount`, which is equally caller-supplied and equally
  multi-segment. `Kv.V1.Read("db", mount: "secret/../auth/token/lookup-self")` built
  `GET /v1/secret/../auth/token/lookup-self/db`, and `KvV2Operations.DataPath` propagated
  the traversal into a string KV2-030 exists to hand to `Sys.Batch` or a policy document.
  D-M4-11 above is the rule this fix completes.
- **RF-3 (error model).** `WriteSecret` validated `KvWriteOptions.Env` but not the **keys**
  of `KvWriteOptions.Envs`, while `WriteAllEnvironments` validated every key — two
  client-side contracts for one wire shape. KV2-002's `Env` rule applies to an environment
  name wherever it appears.

Carried out of M4a as follow-ups, neither blocking:

- **`Details.path` is inconsistent between client-side and server-side errors.** A
  client-side KV error reports the *logical* path (`secret/data/app db`); a server-side one
  reports the *encoded* route plus query (`secret/data/app%20db?version=2`), because
  `BuildDisplayPath` receives the raw route. ERR-034 interpolates whichever it is given.
  Pre-existing since login; M4a widens the exposure. Owner: **M11** (the error-model
  documentation pass is where the inconsistency becomes user-visible prose).
- **KV2-023's enrichment note fires too widely.** `IsKvV2DataReadWithoutEnv` filters on
  neither HTTP method nor engine version, so a 403 on a KV **v1** read of a secret named
  `data/foo`, and a 403 on a v2 **write**, both collect a v2-environment hint. Harmless
  noise rather than a wrong code, and narrowing it is cheap while the condition is pinned
  by one test. Corrected in **M4b**.

`ROADMAP.md` §4's count for M4 is corrected from 27 to **25 landing in M4**, with KV-001
(M7) and KV-010 (M8) deferred per D-M4-2 and listed by ID in the README's known gaps.

- **D-M4-14 (the `404`-on-a-KV-v2-mount enrichment row moves from M4 to M7, and its fixture
  needs re-authoring).** DR-0005 booked this row, and `errors.enrichment.404-kv2-hint`,
  to M4. It cannot land here: the fixture's first exchange is `GET sys/mounts`, so the row
  needs the mount-type cache (SYS-026) that D-M4-2 defers to M7, and D-M1c-25 forbids
  stubbing the branch in the meantime. Re-booked to **M7**, where SYS-026 lands.
  The fixture is also **internally inconsistent** and is Strategic's to fix, not a
  delegate's: it names the operation `Kv.V2.ReadSecret` but expects
  `GET /v1/secret/app/db`, a path with no `data/` segment, which KV2-001 and section 07's
  route table make impossible for any `Kv.V2.*` call. The realistic user error the hint
  addresses is a **v1-shaped read against a v2 mount** — `Kv.V1.Read` on `secret/` — and
  the hint text it asserts names `Kv.ReadSecret`, the façade D-M4-9 declined. M7 therefore
  re-authors the fixture against whichever it decides: `Kv.V1.Read`, or the façade
  reinstated once `DetectVersion` exists. Until then the fixture stays `pending` with M7
  named, never edited to fit (FIX-012, CLA-004).
