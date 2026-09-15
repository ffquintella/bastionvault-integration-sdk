# DR-0007 — M3: System API, Core subset

**Status:** **accepted** — authored and approved by the Strategic Orchestrator (Claude
Sonnet 5). No Claude Opus 5 round is required: `agents.md` §5.3 gates R3 on an Opus
architecture review; M3 is **R2**, whose gate is R1 plus this decision record (the
cross-language parity check is deferred to Stage 2 per D-1/D-6, same as M2a–M2c).
**Risk tier:** R2 (`agents.md` §5.3 — cross-language-shaped contract, no secret material,
no auth/TLS/retry surface of its own).
**Milestone:** M3 · **Date:** 2026-09-15
**Supersedes nothing. Inherits:** [DR-0004](0004-m1b-transport.md) (transport seam, Shape
A/B envelope parsing, retry), [DR-0005](0005-m1c-error-model.md) (recognition and
enrichment), [DR-0006](0006-m2-authentication.md) (sub-API grouping precedent, `CFG-020`'s
unauthenticated-path list).

## Problem

M3 is the smallest useful `sys` surface: `Health`, `SealStatus`, `ServerInfo`,
`ClusterStatus`, `CapabilitiesSelf` (+ the `Can` convenience). The roadmap booked it as
`Large`/R2 against an estimated 16 requirement IDs; the estimate is fixed here, as
`ROADMAP.md` §4 says every such estimate must be at brief time.

## Routing classification: row 2, not row 3

`agents.md` §4.2's discriminator asks whether the contract is settled. Grounding shows it
already is, on every axis that would otherwise force a pathfinder (row 3) pass:

1. **Field-level shapes are pinned by fixtures, not just prose.** `sys.health.*`,
   `sys.seal-status.tn-swap` and `sys.capabilities-self.*` already exist under
   `specifications/fixtures/sys/` with exact result field names (`State`, `T`/`N`/
   `KeyShares`/`KeyThreshold`, `ByPath`/`NamespaceOperable`). Nothing about those three
   operations' public shape is undecided.
2. **The envelope seam already exists.** `dotnet/BastionVault.IntegrationSdk/Response.cs`
   documents Shape A (`data`-wrapped) and Shape B (raw body) as one already-implemented
   distinction; `sys/*` needs no new parsing path, only typed wrappers over
   `LogicalOperations.ReadAsync`/`Response.Data`/`Response.Raw`
   (`dotnet/BastionVault.IntegrationSdk/LogicalOperations.cs:21-51`).
3. **The unauthenticated-path exemption already anticipates this milestone.**
   `RequestExecutor.UnauthenticatedPaths` (`dotnet/BastionVault.IntegrationSdk/Internal/RequestExecutor.cs:33-37`)
   already lists `sys/health`, `sys/seal-status`, `sys/info` — M3's three anonymous-tier
   reads were carried as a CFG-020 exemption since before M2b, unused until now.
4. **The error mapping already exists.** `BV-AUTHZ-003` / `ClusterStatusGated` is already
   catalogued (`specifications/appendix-b-error-catalogue.md:94,246`) and generated into
   `dotnet/BastionVault.IntegrationSdk/Generated/ErrorCatalogData.g.cs`; `ClusterStatus`'s
   403 needs no new recognition rule.
5. **The sub-client wiring pattern is precedented twice over.** `AuthOperations.cs` and
   `LogicalOperations.cs` already establish "a typed operation calls the shared executor
   and shapes the result"; `Sys` is a third instance of an existing pattern, not a new one
   (`OVR-008`'s sub-API grouping, first used for `Client.Auth` in DR-0006).

Net: nothing about M3 requires generating an alternative design or resolving an
ambiguity — it is transcription against a settled contract, `agents.md` §4.2 row 2.
`eng-deep`/Opus generation would cost 15× for work `eng-implementation`/Sonnet fully
covers.

## Decisions

- **D-M3-1 (scope).** M3 implements exactly: `Sys.Health()`, `Sys.SealStatus()`,
  `Sys.ServerInfo()`, `Sys.ClusterStatus()`, `Sys.CapabilitiesSelf(paths)`, `Sys.Can(path,
  capability)`. `Sys.HsmStatus()` is named in `06-system-api.md` but not in M3's roadmap
  description (`health, seal-status, server/cluster info, capabilities`); it stays booked
  under M7 and is **not** implemented here.
- **D-M3-2 (requirement list, fixing the ~16 estimate).** Nine IDs: `SYS-001`, `SYS-002`
  (Health), `SYS-005` (SealStatus), `SYS-006` (ClusterStatus — new, see D-M3-3), `SYS-008`
  (ServerInfo), `SYS-050`, `SYS-051`, `SYS-052`, `SYS-053` (Capabilities). These are the
  IDs removed from `tools/traceability/baseline.json` on exit.
- **D-M3-3 (spec gap closed).** `06-system-api.md`'s `ClusterStatus` section had no
  `SYS-0xx` bullet — the numbering left `SYS-006`/`SYS-007` open between `SealStatus`
  (005) and `ServerInfo` (008), which is exactly `ClusterStatus`'s and `HsmStatus`'s slot.
  `SYS-006` is minted for `ClusterStatus`'s no-raise-on-403 and optional-field behaviour.
  `SYS-007` is left unassigned for `HsmStatus`, authored when M7 needs it — minting it now
  with no fixture and no consumer would be exactly the kind of premature abstraction
  `claude.md` §CLA-007 rules out.
- **D-M3-4 (`RaftMetrics` shape).** `ClusterStatus`'s `raft_metrics` has no fixed key set
  anywhere in section 06. It is modelled as an opaque
  `IReadOnlyDictionary<string, JsonElement>?` (absent, not defaulted, when the backend
  omits it — same optionality rule as every other field in this shape), consistent with
  `Response.Raw`'s existing "forward-compatible field access" precedent (TRN-043).
- **D-M3-5 (fixtures authored).** `sys.info.tiers` (referenced by `appendix-c` since M0,
  never authored) and `sys.cluster-status.ok`/`sys.cluster-status.forbidden` (new) are
  added under `specifications/fixtures/sys/`, closing the appendix-C gap this grounding
  pass found. `HsmStatus` gets no fixture, per D-M3-1.
- **D-M3-6 (parity).** Rust and Python are not touched. Per D-1/D-6 they reach M3's
  requirement IDs only inside M13.

## Consequences

- `tools/traceability/baseline.json` gains `SYS-006` (this record) and loses all nine
  D-M3-2 IDs on implementation exit.
- `specifications/appendix-d-requirement-index.md`'s total moves from 388 to 389.
- M7's booked count absorbs one fewer id than its `~18` estimate implied, since `SYS-006`
  is now spent here; M7's own brief fixes its list the same way this one did.

## Verification required at handback

`dotnet test`, all `sys/*` fixtures green (including the three new ones), coverage ≥ 95 %
line and branch, traceability delta exactly the nine D-M3-2 IDs, `CHANGELOG.md` entry.

## Addendum — handback ruling (R3 spec contradiction, resolved)

The first implementation handback's mandatory R2 review (Claude Opus 5, `strategic-review`,
`agents.md` §4.4) found that this record's original D-M3-3 text ("MUST NOT raise on the
documented 403") contradicted `06-system-api.md`'s own SYS-001/SYS-006 pattern: "MUST NOT
raise" only ever pairs with a named carrier for the state it reports (`HealthStatus.State`
for SYS-001); SYS-006 named no carrier, so the requirement as minted was unsatisfiable, and
the implementation's fixture (`sys.cluster-status.forbidden.json`) had encoded the
*opposite* of the prose it was meant to verify — a `specifications/` self-contradiction,
which is R3 by **CRS-004** regardless of the milestone's own R2 tier, so work paused per
**CRS-005** rather than being waved through as a review nit.

**Ruling (Opus, adopted by the Strategic Orchestrator without amendment):** a permission
refusal is a genuine error, unlike `Health`'s 503 (which *is* the state `Health` exists to
report) — `ClusterStatus`'s 403 raises like any other mapped error. `06-system-api.md`'s
SYS-006 bullet is corrected to say so. **No code or fixture changed**: `SysOperations.cs`
already threw on the 403 and `sys.cluster-status.forbidden.json` already asserted the
throw; only this record's D-M3-3 and the spec bullet it summarised were wrong. Revision
counter unchanged per `agents.md` **REC-007**'s carve-out — this is a handback ruling, not
a fresh architecture round.

The same review found two required (non-R3) fixes, dispatched back to `eng-implementation`
as a row-2 pass under this same decision:

- **A standby `Sys.Health()` probe left a false positive on `BastionVaultClient.RateGateState`.**
  `sys/health` is DoS-guard-exempt (spec header, `06-system-api.md:15`) but its 429 was
  still reaching `RequestExecutor`'s generic `PauseRateGate` before classification. No
  request is actually delayed (the gate is observation-only downstream), but the public
  property lies to a caller polling it. Fixed by exempting the health path from the pause.
- **`Sys.Can(path, capability)` (SYS-053) was missing from `Client.Sys`.** The delegate
  built the no-round-trip form as `Capabilities.Can(...)` instead, which is a good shape to
  *keep* but not a substitute for the spec-named `Client.Sys` member (`FAM-002` — renaming
  a MUST-provided public member is a Strategic-tree call, not an Engineering one). Fixed by
  adding `SysOperations.CanAsync` as a thin wrapper that fetches and delegates to
  `Capabilities.Can`, so both shapes exist.

**Recorded for M13 (Rust/Python parity), not acted on now:**
- `Capability`'s "preserve unknown values" requirement (SYS-051) rules out a bare enum in
  every language, not just .NET's. .NET's `Other(string)` compares equal to a named value
  with the same wire string (`Capability.Other("read") == Capability.Read`); a naive Rust
  `enum { Read, …, Other(String) }` would not have that property unless `from_wire` matches
  named values before falling back to `Other`. Python needs the same wrapper shape as .NET
  (`enum.Enum` cannot carry the payload either).
- `ClusterStatus.StorageType`/`Cluster` default to `string.Empty` when the wire omits them,
  unlike every optional field in the same shape (which stay `null`). Not a requirement
  violation — SYS-006's optionality clause covers the `?`-marked fields only — but it is an
  unrecorded choice the parity pass must match rather than re-derive.
- Shape B (`sys/*` responses are always raw) is asserted by spec header, not enforced by
  code: `LogicalOperations.Shape` still infers Shape A/B from whether a top-level `data`
  key is present. Latent for M3 (none of its six payloads carries `data`); live the moment
  an M7 `sys` response does. Booked against M7, not this record.
