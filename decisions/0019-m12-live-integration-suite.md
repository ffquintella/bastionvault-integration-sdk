# DR-0019 — M12: live-server integration suite, run in parallel with M11

**Status:** proposed (framing), revision 1 (2026-09-22). Authored by the Strategic
Orchestrator as the milestone's framing record, before any slice is dispatched
(**CRS-001**). Each slice appends its own `D-M12-n` entries below rather than opening a
second record.

**Risk tier:** **R3** (`agents.md` §5.3), assigned here before dispatch. Three independent
dimensions reach it and any one would be sufficient: **R-6** is an open R3 risk and this is
the milestone it was raised for; `ITG-021` asserts that no secret material appears in logs,
which is `CRS-003` secret-material surface; and M12 is the **Stage 1 exit**, which is
outward-facing. Under §5.3 the R3 gate is R2 plus Claude Opus 5 review plus Strategic
Orchestrator acceptance **plus human confirmation before release** — the last of which binds
the exit, not the implementation slices (D-M12-6).

**Milestone:** M12, seven slices, **run concurrently with M11** at the project owner's
direction · **Date:** 2026-09-22

**Supersedes nothing. Amends nothing in `specifications/`, and D-M12-2 refuses to: the
mismatch between `specifications/test-matrix.json` and the installed server is an
environment gap, and the specification is the side that is correct.**

**Discharges:** `ROADMAP.md` §4's M12 row — the 16 `ITG` requirements and the 32 `ITG-S`
scenarios of section 15 (48 baselined IDs), plus the 7 `TST` IDs section 15 carries, against
.NET only. Plus the Stage-1 exit checklist. **Does not cut the shared `1.0.0` tag**
(D-M12-6).

**Inherits:** [DR-0018](0018-m11-documentation-and-usage-guides.md) D-M11-7 (the CNF-001 vs
CNF-014 audit this milestone is booked to perform), D-M11-12 (`CHANGELOG.md` ownership),
[DR-0016](0016-m9-pki-and-ssh.md) D-M9-11 (`PKI-030` held back for want of a server message
string — a live server is the thing that could supply it, D-M12-5),
[DR-0017](0017-m10-remaining-bindings-and-identity.md) D-M10-4 (`R-35`, the `identity.self`
fixture with no capture to author from — same remedy).

## Problem

### The precondition everyone assumed was missing is half-present

`ROADMAP.md` §8 carries **R-6** — "No live BastionVault server available" — at **R3**, and
§10 question 3 has been open since M0: *if none exists, the integration suite needs a plan
of its own.* Every milestone from M1 to M11 has been built against fixtures and the
in-process mock server, and `dotnet/README.md` states its tested server version as
"BastionVault 0.42.x" with the explicit qualifier **not live-verified**.

Measured on this machine on 2026-09-22:

| Probe | Result |
|---|---|
| `bvault` binary | **present**, `/usr/local/bin/bvault`, reporting `bastion_vault 0.38.3` |
| Container runtime | `docker` CLI present, **daemon not running** |
| `ghcr.io/ffquintella/bastionvault:0.42.0` | **`denied`** — the registry requires authentication this environment does not have |
| `/Applications/BastionVault.app` | present, `CFBundleShortVersionString` **`0.44.4`** — but it ships only `bastion-vault-gui`, no server binary |
| Any other `bvault` binary on the system | none. `/usr/local/bin/bvault` is the only one, dated 29 July |
| `specifications/test-matrix.json` minimum | **`0.42.0`** |

So R-6 is not simply true any more, and it is not simply false either. **A real server
binary is available, and it is below the minimum the matrix declares supported.**

### Why that gap is a blocking conflict rather than a detail

`ITG-002` is unambiguous: managed mode **MUST** use the version pinned in
`test-matrix.json`, and the harness MUST print the resolved version. A managed run against
`0.38.3` does not satisfy `ITG-002`, and `ITG-030` requires CI to run the suite against the
listed versions. Neither listed version is reachable from here: `0.42.0` is behind a
registry denial and `latest` is the same registry.

**The GUI settles which reading is true.** The project owner pointed at the newer version in
`/Applications`; it reports **0.44.4**. A shipped 0.44.4 means the product line is past
0.42.0, so `test-matrix.json`'s minimum names a **real released version**, not an
aspirational one. The matrix is therefore *not* wrong, and the two readings that would have
justified lowering it fall away:

| Reading | Status |
|---|---|
| **`0.42.0` is real and this environment simply lacks a current server binary and registry credentials** | **This one.** Supported by the 0.44.4 GUI |
| `0.42.0` does not exist; the matrix is aspirational and should be lowered to `0.38.x` | **Falsified** by the 0.44.4 GUI |
| `0.42.0` exists but `0.38.3` is what the SDK must support | **Not supported** by anything; nothing states 0.38.3 as a support floor |

**The consequence is good news and narrows M12's blocker to one concrete thing.** No
`specifications/` change is needed and `test-matrix.json` stands as written. What is missing
is purely local: **a `bvault` *server* binary at 0.42.0 or newer** (the GUI bundle contains
no server), or credentials for `ghcr.io/ffquintella/bastionvault`. Either one closes the gap;
the SDK repository needs no change to accept it, because `BASTIONVAULT_TEST_BIN` and
`BASTIONVAULT_TEST_IMAGE` are already the documented inputs.

**This is not another R-14.** R-14's gates were unsatisfiable *as specified* — the documents
contradicted each other, and only a scope decision could fix it. M12's gate is satisfiable
exactly as written; what is missing is a binary on a disk. The distinction matters because
the remedies are opposite: R-14 needed a decision nobody was taking, and this needs an
install nobody has run. Recording it as an R-14 recurrence would misdirect the next reader
into re-opening a specification that is correct. **What M12 does borrow from R-14 is the
discipline**: the gap is named now, before it can be discovered at the exit, and D-M12-2
says what is dispatched regardless and what waits.

### Running concurrently with M11

The project owner has directed that M12 run in parallel with M11. `ROADMAP.md` §7's graph
draws `M11 ──▶ M12`, so this is a deviation from the recorded sequencing and is recorded
here as one, with its real dependencies named rather than the arrow taken at face value
(D-M12-3). The precedent is M10, whose slices b, c and d ran concurrently against a serial
default at the owner's direction and reconciled at merge with no true conflict
([DR-0017](0017-m10-remaining-bindings-and-identity.md)).

## Decisions

### D-M12-1 — Seven slices

| Slice | Scope | IDs | Rung |
|-------|-------|-----|------|
| **1** | **Pathfinder: the integration harness.** `TestServer` (`Address`, `RootToken`, `CaCertPem?`, `Version`, `Mode`), external and managed modes, init/unseal, resolved-version print, skip-when-unavailable, TLS override, per-run `it-<run-id>-…` prefixing, parallel safety, `Cleanup-Orphans` | `ITG-001`…`ITG-005`, `ITG-010`…`ITG-013` | `eng-deep` |
| **2** | Global run assertions: recognised shapes, no secret material in debug logs, one observability event per attempt, the per-section summary | `ITG-020`…`ITG-023` | `eng-implementation` |
| **3** | Scenarios 1–12 — bootstrap, system, authentication | `ITG-S01`…`ITG-S12` | `eng-implementation` |
| **4** | Scenarios 13–20 — KV, Transit, TOTP | `ITG-S13`…`ITG-S20` | `eng-implementation` |
| **5** | Scenarios 21–26 — PKI, SSH, Files, Resources, Identity | `ITG-S21`…`ITG-S26` | `eng-implementation` |
| **6** | Scenarios 27–32 — efficiency, rate gate, cache, pagination, failover | `ITG-S27`…`ITG-S32` | `eng-implementation` |
| **7** | CI matrix, per-version artifacts, and the documented local command | `ITG-030`…`ITG-032`, `ITG-004`'s README limb | `eng-implementation` |

**Slice 1 is `eng-deep` (row 3) on conditions (a) and (d):** it is the pathfinder pass that
first defines the integration-harness contract — process lifecycle, isolation prefixing,
teardown-on-failure, parallel safety — which the other six slices consume, and it spans the
test project, the solution, a new server-process layer and CI. The trigger is recorded here,
**before dispatch**, per `agents.md` §4.3 rule 4. Slices 2–7 implement a contract slice 1
pins, which is row 2 however large they are.

**Order.** `1` alone → then `2`, `3` → then `4`, `5` → then `6`, `7`. Slices 3–6 are
file-disjoint (one scenario class per group) but all consume slice 1's harness, so none
starts before it lands.

### D-M12-2 — What runs before the server question is answered, and what does not

**Decision.** The blocker established in the Problem section — **no local `bvault` server
binary at 0.42.0 or newer, and no credentials for the container registry** — is an
environment request to the project owner, not a specification question. It remains an R3
item because R-6 is R3 and because `ITG-030`'s CI limb depends on the answer
(`agents.md` §5.4). Work is split by whether it depends on that answer:

**Dispatched now, because the answer does not change it:** slice 1 in full. `ITG-001`'s
`TestServer` must expose `Version` *because* versions differ; `ITG-003`'s skip path exists
*because* a server may be unavailable; `ITG-031`'s "requires server >= X" skip exists
*because* the running server may predate an endpoint. **The harness is the component whose
entire job is to absorb this uncertainty**, and it is specified identically under all three
readings. Slice 1 implements both modes — container *and* `BASTIONVAULT_TEST_BIN` — as the
`ITG` mode table requires, and is developed against the local `0.38.3` binary.

**Dispatched now with a stated assumption:** slices 2–6. Scenarios are written against the
specification, not against a server build. Where the local `0.38.3` lacks an endpoint a
scenario needs, the correct outcome is **`ITG-031`'s counted skip**, not a weakened
assertion and not a deleted scenario. A scenario that cannot be made to pass is reported as
skipped with its reason, never quietly softened (**CLA-004**, **VER-003**).

**Held until the owner answers:** slice 7, and the milestone exit. `ITG-030` cannot be
satisfied against a registry that returns `denied`, and `ITG-002` cannot be satisfied by a
binary the matrix does not list. Slice 7 is dispatched when the answer names the versions CI
will actually run.

**What is explicitly refused:** editing `specifications/test-matrix.json` to make the gate
pass. Lowering the matrix minimum to match the stale binary that happens to be installed
would be `CLA-004` — weakening a gate so something passes — and the 0.44.4 GUI removes even
the argument that the matrix is wrong. The matrix stands; the environment is what changes.

### D-M12-3 — Concurrency with M11: what is shared, and how the three handoff slots are split

**Decision.** M11 and M12 run concurrently. `skills/claude/SKILLS.md` §8 caps **concurrent
Codex handoffs at 3**, system-wide agents at 10; the cap binds both milestones together, not
each separately. Allocation: **2 slots to M11, 1 slot to M12**, until M11's slice a lands;
thereafter the split is re-judged per wave by the Strategic Orchestrator. M11 keeps the
larger share because seven of its eight slices are blocked behind its pathfinder, so
starving it serialises it.

**The dependency `ROADMAP.md` §7 draws as `M11 ──▶ M12` is real but narrow.** What M12
genuinely needs from M11:

| M12 needs | From | When |
|---|---|---|
| `ITG-004`'s "Running integration tests" README section | M11 slice g (D1) and slice e (D13) | At M12 slice 7, not before |
| The conformance statement in the Stage-1 exit checklist | M11's completion (sections 16–17) | At milestone exit only |

Nothing in the harness or the 32 scenarios depends on documentation. **The arrow is a
documentation dependency at the exit, not a build dependency at the start**, which is why
parallelising is sound rather than merely faster.

**Two collision points are named in advance**, because §7.4 permits parallelism only on
disjoint files:

1. **`dotnet/BastionVault.IntegrationSdk.sln`** — M11 slice a adds a `DocsSamples` project
   and M12 slice 1 adds an integration project. Both are additive single-line insertions in
   the same file, the same shape of conflict M10 hand-merged with no true conflict. M12
   slice 1 is instructed to re-read the solution immediately before writing it.
2. **`.github/workflows/`** — M11 slice g adds docs gates and M12 slice 7 adds the matrix
   job. Both are last in their milestones and both are held: g behind M11's content, 7
   behind the owner's answer. The Strategic Orchestrator sequences them rather than letting
   them race.

Both milestones regenerate nothing in common otherwise: M12 touches no file under
`dotnet/BastionVault.IntegrationSdk/`, so it cannot disturb `PublicApiSurface.txt`, and M11's
doc-comment slices touch only that directory.

### D-M12-4 — M12 performs the CNF-001 vs CNF-014 audit DR-0018 booked to it

**Decision.** Before any conformance level is declared, M12 produces a per-ID audit over every
requirement in `Core`'s sections that currently sits on `tools/traceability/baseline.json`,
classifying each as **(i)** implemented but untested (a `CNF-014` coverage gap, closable by
writing a test) or **(ii)** unimplemented (a `CNF-001` gap, which `CNF-002` forbids declaring
over). The 33 non-`ITG` baselined IDs inside `Core`'s sections — 18 `CNF`, 3 `OVR`, 4 `TRN`,
3 `ERR`, 5 `FIX` — are the audit's subject.

**Why here and why explicitly.** DR-0018 D-M11-7 was corrected in review for inferring
"unimplemented" from baseline membership when the baseline means "no referencing test". That
correction leaves a real question unanswered, and answering it is a precondition of any
declaration, not a by-product of one. Recording the audit as a deliverable is what stops the
fifth R-14 recurrence; `TOK-008` then makes it a fact later milestones read rather than
re-derive.

### D-M12-5 — A live server is an opportunity to close two held-back gaps, taken carefully or not at all

**Decision.** Two requirements are held back for want of something only a real server
produces, and both become addressable the moment one runs:

- **`PKI-030`** (**R-31**): its queue-cap limb needs the server's `BV-QUOTA-002 QueueFull`
  message string, which no document states. A live server can be driven to produce it.
- **`R-35`**: `identity.self`'s mandatory Appendix-C fixture has no real capture to author
  from, and `FIX-010` requires fixture bodies to be copied from a real exchange.

**They are not booked into a slice, and that is deliberate.** Any capture taken from
`0.38.3` would be a capture from a server the matrix does not list as supported, and
`FIX-010`'s whole point is provenance. Baking a `0.38.3` wire body into the corpus as though
it were `0.42.0`'s would be the same defect as guessing the string — which is what
`R-23` already cost this project once, and the reason both IDs were held back rather than
guessed at M9 and M10.

**Therefore:** both stay held back at M12 unless the owner's answer to D-M12-2 establishes
that the running server is a supported version. If it does, they are dispatched as a
follow-up slice and **every captured fixture records the server version it came from**.

### D-M12-6 — No `1.0.0`, and the exit is human-gated

**Decision.** M12 closes Stage 1 and cuts **no shared `1.0.0` tag**, per `ROADMAP.md` §5 and
the versioning rule at the top of `CHANGELOG.md`: `CLA-003` requires all three packages to
match before a release is called `1.0.0`, and `rust/` and `python/` are untouched through the
whole of Stage 1. An interim .NET-only tag, if wanted, follows the `0.5.0` precedent
(D-M2-15) as an explicit recorded exception.

**The R3 human-confirmation gate binds the exit, not the slices.** Slices 1–6 are
implementation and review as usual. Strategic acceptance plus the project owner's explicit
confirmation is required before: declaring any conformance level, cutting any tag, and
enabling any publication — the last now materially closer, since the root `Makefile` added a
Cloudsmith publish target, which is the first thing in this repository capable of engaging
`CRS-004`'s "published artefact" limb for real.

## Rejected alternatives

| Option | Why rejected |
|---|---|
| **Block all of M12 until the server question is answered** | Seven slices' worth of work is specified identically under all three readings (D-M12-2). Blocking would trade real progress for a certainty the harness is built to not need |
| **Lower `test-matrix.json`'s minimum to `0.38.0` so the gate passes** | `CLA-004`. It may be the correct change under readings 2 and 3, which is precisely why it is an Architect-plus-owner decision and not a slice's |
| **Run the suite against `0.38.3` and report it as the matrix run** | `VER-003`. It would be the fifth unsatisfiable gate reported as satisfied, in a project whose most expensive recurring defect is exactly that |
| **Write the scenarios against the mock server and swap the server later** | The mock server is what the unit suite already uses; a "live" suite that never touches a live server tests the mock. `ITG-020`'s premise — that every response shape the SDK met was recognised — is only evidence if the responses came from a real server |
| **Capture the missing `PKI-030` / `R-35` fixtures now, from `0.38.3`** | `FIX-010` is a provenance requirement. An unlabelled capture from an unsupported version is the `R-23` defect with extra steps (D-M12-5) |
| **Serialise M12 behind M11, as `ROADMAP.md` §7 draws it** | The owner directed otherwise, and the dependency analysis in D-M12-3 shows the arrow is a documentation dependency at the exit rather than a build dependency at the start |

## Acceptance criteria for the milestone

1. The harness satisfies `ITG-001`…`ITG-005` and `ITG-010`…`ITG-013`, and **is demonstrated
   in all three states**: managed mode against a real server, external mode, and the
   `ITG-003` skip when neither is possible — the skip observed, not asserted.
2. All 32 `ITG-S` scenarios exist. Every one either passes against a live server or skips
   with an `ITG-031` reason that is counted and reported. **No scenario is weakened to pass**
   (`CLA-004`), and the skip list is reported verbatim at the milestone gate (`VER-003`).
3. `ITG-020`…`ITG-023` hold over a full run, including that no secret material appears in
   debug-level logs.
4. `ITG-030`…`ITG-032` are satisfied against the versions the owner's answer names — or the
   milestone exits with slice 7 explicitly outstanding and its blocker named, in the manner
   `DOC-030` is held back at M11 (DR-0018 D-M11-8). **No version claim is made that a CI run
   has not produced.**
5. The D-M12-4 audit exists and classifies every baselined `Core`-section ID as a `CNF-014`
   or a `CNF-001` gap.
6. The unit suite stays green throughout and coverage stays above the 95 % floor
   (`CNF-010`); `CNF-011` excludes the integration project from the coverage figure — say
   which way it was handled.
7. `PublicApiSurface.txt` is unchanged by every slice.
8. `CHANGELOG.md` (**REC-001**) and `ROADMAP.md` §2, §4, §5, §8 (**REC-002**) are updated by
   the Strategic Orchestrator on acceptance, including R-6's re-statement in light of the
   0.38.3 finding and §10 question 3's resolution or re-escalation.
