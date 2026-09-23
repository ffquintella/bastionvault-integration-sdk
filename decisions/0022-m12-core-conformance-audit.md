# DR-0022 — The D-M12-4 audit: what actually blocks a `Core` declaration

**Status:** proposed (audit findings), revision 1 (2026-09-23). Authored by the Strategic
Orchestrator on evidence returned by two Engineering-tree survey passes, each claim
independently re-checked before acceptance (**CCF-002**).

**Risk tier:** **R2.** The audit changes no behaviour and ships no code. It is R2 because
its output gates an **R3** act — the conformance-level declaration — under `CRS-004`'s
`specifications/` limb, and because an unsupported classification here would authorise a
claim the project cannot support.

**Date:** 2026-09-23 · **Discharges:** [DR-0019](0019-m12-live-integration-suite.md)
D-M12-4 · **Answers:** `ROADMAP.md` §8 risk **R-14**, on its fifth encounter

## Problem

DR-0019 D-M12-4 booked M12 to produce a per-ID audit classifying every baselined
requirement inside `Core`'s sections as either a **`CNF-014`** gap (implemented but
untested, closable by writing a test) or a **`CNF-001`** gap (unimplemented, which
`CNF-002` forbids declaring over).

The audit exists because R-14 — "the conformance-level declaration schedule is
unsatisfiable" — has now cost **four** milestone gates their stated exit criterion. M4 was
booked to declare `Core`, M8 `Standard`, M10 `Complete`, M11 the lot; each exited declaring
nothing and regenerating a gap list instead. Each of those costs would have been the price
of answering the question once, at M4.

The question was never answerable from the baseline alone. `tools/traceability/baseline.json`
records **"no referencing test"**, which is a different claim from "not implemented" —
the inference DR-0018 D-M11-7 was corrected in review for making. Answering it requires
looking at the implementation, per ID.

## What the audit's subject set actually is

**D-M12-4's named subject set of 33 is an undercount.** The record names "18 `CNF`, 3
`OVR`, 4 `TRN`, 3 `ERR`, 5 `FIX`". Enumerated mechanically against
`appendix-d-requirement-index.md` and the level table at
`01-conformance-and-quality.md:9`, the non-`ITG` baselined set inside `Core`'s sections is
**54**:

| Group | Count | Why it is in `Core` |
|-------|-------|---------------------|
| `CNF` 18, `OVR` 3, `TRN` 4, `ERR` 3 | 28 | Sections 00–07, 13 — named by D-M12-4 |
| `FIX` 5 | 5 | Appendix C, normative for fixtures — named by D-M12-4 |
| **`DOC` 19, `TST` 7** | **26** | **Sections 15 and 16 — omitted by D-M12-4** |

`Core`'s required sections are `00, 01, 02, 03, 04, 05, 06, 07, 13, 15, 16, 17`. Sections
15 and 16 are **full, unqualified members**, so their MUST requirements bind `CNF-001`
exactly as section 03's do. D-M12-4 simply did not enumerate them.

**This omission was load-bearing, not cosmetic.** `DOC-030` is one of the 26, and
[DR-0018](0018-m11-documentation-and-usage-guides.md) D-M11-8 held it back explicitly one
milestone ago. An audit that stopped at 33 would have reported a clear path to declaring
`Core` over a set that excludes a gap this project had already recorded against itself.
That is the R-14 failure shape reproduced *inside the very artefact built to end it* — the
audit was nearly the fifth recurrence rather than the answer to it.

## Findings

### Two `CNF-001` gaps block `Core`. Both are nameable; neither is vague incompleteness.

**`TRN-081` — `Client.ServerVersion()` does not exist.**

> The SDK MUST provide `Client.ServerVersion()` that caches the `sys/info` version for the
> `Client` lifetime and returns `null` when unauthenticated. — `03-transport-and-protocol.md:268`

Zero occurrences in `dotnet/BastionVault.IntegrationSdk/PublicApiSurface.txt`. The only two
occurrences of the name anywhere in the library are **hint strings instructing the caller to
call a method the SDK does not expose** — `Internal/HintEnrichment.cs:60` and
`Generated/ErrorCatalogData.g.cs:700`. Section 03 is a `Core` section.

Independently corroborated: `15-testing-requirements.md:188-192` already records the same
absence, found when `ITG-S01` needed the member. The project owner amended that scenario
rather than add the API, and the amendment says so explicitly — it "does not discharge
TRN-081, which still requires the member and is now the only place the gap is recorded".
**This record is now the second place.**

**`DOC-030` — the rendered documentation is not published.**

> The rendered documentation MUST be published per release (GitHub Pages, docs.rs,
> ReadTheDocs, NuGet README) and versioned so that users of older SDK versions can read
> matching docs. — `16-documentation-requirements.md:79`

Held back with a named owner by DR-0018 D-M11-8: no release has been cut, publication is
outward-facing and irreversible, and claiming `DOC-030` on a pipeline that has never
published anything was refused. M11 closed at 20 of 21 `DOC` requirements for this reason.
Section 16 is a `Core` section. **Confirmed, not newly found.**

### Everything else in the 54 clears

| Class | Count | Meaning |
|-------|-------|---------|
| Met, with artefact evidence | 24 | The 19 `DOC` / 7 `TST` set less `DOC-030` and less `TST-060` |
| `CNF-014` — implemented, untested | 7 | `OVR-007`, `OVR-008`, `OVR-009`, `TRN-022`, `TRN-070`, `TRN-080`, `ERR-032` |
| Not applicable to library code | 25 | 18 `CNF`, `ERR-060`, `ERR-061`, 5 `FIX` — addressed to CI, release process, the provenance manifest, or the fixture corpus |
| SHOULD, so outside `CNF-001` | 1 | `TST-060` (mutation testing), `15-testing-requirements.md:375` |
| **`CNF-001` gaps** | **2** | **`TRN-081`, `DOC-030`** |

54 = 24 + 7 + 25 + 1 + 2. ✓

The seven `CNF-014` entries are the audit's genuinely useful middle: each **is** implemented
and merely lacks a test referencing its ID, so each is closable by writing a test and none
of them blocks a declaration. Evidence, per ID: `OVR-007` at `Internal/SysWire.cs:17-24` and
`ServerInfo.cs:9-27`; `OVR-008` at `BastionVaultClient.cs:137-254`; `OVR-009` at
`SysOperations.cs:119-120`; `TRN-022` at `KvV2Operations.cs:811-830`; `TRN-070` at
`Generated/ErrorCatalogData.g.cs:252,706,920`; `TRN-080` at `SysOperations.cs:111-126`;
`ERR-032` at `Internal/HintEnrichment.cs:44-66`.

## Decisions

### D-M12-18 — `Core` is not declarable today, and the two reasons are recorded rather than re-derived

**Decision.** No conformance level is declared at M12. `CNF-002` forbids claiming a level
whose sections contain unimplemented MUST requirements, and `Core` contains two: `TRN-081`
and `DOC-030`. `Standard` and `Complete` are supersets of `Core` and are therefore blocked
by the same two.

**What changes, and it is not nothing.** R-14's four previous encounters each ended with "we
still cannot declare" and no statement of what would make it possible. This one ends with a
closed list of length two. **The gap list is no longer regenerated; it is enumerated.**
`dotnet/README.md` continues to declare no level and to list gaps by requirement ID
(**CNF-002**'s "Known gaps"), which remains the honest control.

### D-M12-19 — D-M12-4's subject set is corrected to 54, and the correction is the finding

**Decision.** The audit's subject set is **54**, not 33. Any later reader of D-M12-4 reads
this record's enumeration instead (**TOK-008**).

**Why it is recorded as a finding rather than a typo.** D-M12-4 was written to stop R-14
recurring, and it under-scoped itself in the same direction R-14 always fails: by taking a
category of requirement (documentation, testing) as not really part of the conformance
level, when the level table says it is without qualification. The project's most expensive
recurring defect is a gate that does not bind; this was nearly a *remedy* that did not bind.

### D-M12-20 — `TRN-081` is escalated, not closed here

**Decision.** Adding `Client.ServerVersion()` is a **public-API change** — Strategic-tree
territory that Claude does not delegate, and the project owner's call under
`agents.md` §5.3 (R2 minimum on public API shape). It is **not** taken as part of M12.

**The options, with what each gives up:**

| Option | Cost |
|--------|------|
| **A. Implement `TRN-081`** — add the member, cache `sys/info`'s version, return `null` unauthenticated | A public-API addition that `rust/` and `python/` must mirror at M13. Closes the gap and makes the SDK's own error hints truthful |
| **B. Amend `TRN-081`** — remove the requirement from section 03 | R3 (`specifications/` limb). Requires deleting or rewording the two hint strings that reference the method, or they instruct callers to call nothing |
| **C. Leave it open** | `Core` stays undeclarable indefinitely. This is the status quo that has already cost four gates |

**Recommendation: A.** It is small, it is additive rather than breaking, `sys/info` is
already bound (`SysOperations.cs:111-126`, `TRN-080` met), and B pays an R3 specification
change to remove a member the SDK's own error catalogue tells users to call. B's real cost
is that it makes two shipped hint strings wrong.

### D-M12-21 — `DOC-030` closes on the first real publication, and not before

**Decision.** Unchanged from DR-0018 D-M11-8; restated here so the `Core` blocker list is
readable in one place. `DOC-030` closes when a release is actually published, which is
outward-facing and human-gated. It is not closable by M12 and is not M12's to close.

### D-M12-22 — Project-owner ruling on `TRN-081`, 2026-09-23: implement it

**Ruling.** Option **A** of D-M12-20. `Client.ServerVersion()` is implemented in .NET rather
than amended out of the specification. Recorded on the day it was given, before the work was
dispatched, so no later session re-opens a question the owner has already closed
(**TOK-008**, **CLA-008**).

**What the ruling settles, and what it does not.** It closes the *whether*. The *how* was
pinned by the Strategic Orchestrator before dispatch, because `TRN-081`'s two sentences leave
four things undecided that a delegate would otherwise have to invent — and an invented public
API is the thing `agents.md` §5.3 puts at R2:

| Undecided by the requirement | Pinned as | Why |
|---|---|---|
| Sync or async | `Task<string?> ServerVersionAsync(...)` | The first call performs a network round trip. `OVR-009` is met by the identifier and the `<spec>` tag, exactly as `Sys.ServerInfo` → `ServerInfoAsync` already does |
| Where the cache lives | `ClientContext` | `BastionVaultClient.Sys` builds a **fresh `SysOperations` per property read**; a cache held there lives for one call and satisfies `TRN-081` in name only. `SYS-026` hit this and documented it at `Internal/MountTypeCache.cs:1-12` |
| Whether `null` is cached | **No** | "Caches the version for the `Client` lifetime" governs the *value*. A `null` means "no live token **when asked**". Caching it would break the ordinary construct-then-authenticate order permanently — the member would answer `null` forever to a client that logged in one line later |
| What a transport failure returns | **Throws** | `null` must mean exactly one thing. Folding errors into it would conflate "unauthenticated" with "server unreachable" and make the member useless for the diagnostic purpose its own error hints cite it for |

Unlike the mount cache, the entry needs **no namespace keying**: a server's version is
server-wide, so the single `ClientContext` shared by every `WithNamespace` view (D-M1b-9) is
the correct scope rather than a hazard.

**Consequence for `Core`.** One of the two blockers in D-M12-18 closes on this landing.
`DOC-030` remains, and it is not closable by M12 (D-M12-21) — so **`Core` is still not
declared at M12**, and the declaration moves to the first real publication. The list stays at
two items until then; it does not become zero.

**Consequence for M13.** Parity is owed in `rust/` and `python/`, which are frozen under
D-1/D-6. M13 implements the member from the start rather than mirroring an absence — the same
carry-forward already recorded for DR-0020's transport default and DR-0021's findings.

## Rejected alternatives

**Classifying the 26 `DOC`/`TST` IDs as "not applicable because they are not library code."**
The first survey pass proposed this reasoning for the 25 `CNF`/`ERR`/`FIX` IDs, where it is
correct — those are addressed to CI, the release process, and the fixture corpus. Extending
it to sections 15 and 16 would be wrong: `CNF-001` binds "every MUST requirement of every
section in its declared level", and the level table admits sections 15 and 16 without
qualification. A documentation MUST in a `Core` section is a `Core` obligation whether or
not a C# file implements it. Accepting the extension would have hidden `DOC-030`.

**Accepting the first pass's 33-ID result and declaring the audit discharged.** It reported
a single `CNF-001` gap and 0.82 confidence, with the undercount raised as its own open
question. Discharging D-M12-4 on it would have produced a record asserting one blocker where
there are two.

**Inferring "unimplemented" from baseline membership.** Explicitly forbidden by DR-0018
D-M11-7's correction. Seven of the 54 are implemented and merely untested; a baseline-only
reading would have reported all seven as `CNF-001` gaps and put the blocker count at nine.

## Consequences

- **M12 exits declaring no conformance level**, as M4, M8, M10 and M11 did — but for the
  first time with a closed, evidenced list of what would change that.
- **R-14 becomes actionable.** Its `ROADMAP.md` §8 row is updated from a forecast-turned-
  measurement into two named blockers with an owner each.
- **`TRN-081` needs a project-owner decision** (D-M12-20). Until it is taken, no level is
  declarable at any milestone — including M13's shared `1.0.0`.
- **M13 inherits `TRN-081`'s parity obligation** if option A is chosen: `rust/` and
  `python/` implement the member from the start rather than mirroring an absence.
- The seven `CNF-014` entries are **test-writing work**, available to any later milestone
  that wants to shrink the baseline. None of them gates a declaration.
