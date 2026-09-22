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

### D-M12-7 — Project-owner rulings, 2026-09-22

Both questions this record escalated were put to the project owner and answered the same
day. Recorded here so no slice re-opens them (**TOK-008**, **CLA-008**).

**Ruling 1 — the server gap is closed by installing a current binary.** The owner will place
a `bvault` **server** binary at **0.42.0 or newer** on this machine. `test-matrix.json` is
confirmed correct and unchanged; no `specifications/` amendment is made; registry
credentials are not pursued. This is the remedy D-M12-2 identified as needing no repository
change at all — `BASTIONVAULT_TEST_BIN` is already the documented input.

**Consequences.** Slice 7 and the `ITG-030`/`ITG-032` matrix run stay **held** until the
binary is present, exactly as D-M12-2 sequenced them; slices 1–6 proceed. The milestone exit
criterion 4 resolves to its first limb — a real matrix run — rather than its fallback, and
**M12 is not expected to exit with slice 7 outstanding**. Should the binary not materialise
before the other six slices land, the fallback limb applies and the blocker is named at the
exit rather than discovered there.

**Ruling 2 — held-back gaps close only against a supported version.** `PKI-030` (**R-31**)
and `R-35` are captured only once the running server is 0.42.0 or newer, and **every fixture
records the server version it came from**. This confirms D-M12-5 as written and rejects
capturing from 0.38.3. The `FIX-010` provenance rule holds: a capture from an unsupported
version is not evidence, and `R-23` is what treating it as evidence already cost this
project once.

**Consequence.** Both IDs stay on the traceability baseline until the binary lands. If it
lands before slice 6 completes, the captures become a follow-up slice under D-M12-5; if it
does not, both remain held back with their existing owners and M12 claims neither.

**Addendum, same day — the first install attempt updated the wrong component.** A
BastionVault update was installed and re-verified on request. Result:

| Component | Before | After |
|---|---|---|
| `/Applications/BastionVault.app` (`CFBundleShortVersionString`) | 0.44.4 | **0.44.5** |
| `/usr/local/bin/bvault` (the server CLI) | 0.38.3, dated 29 July | **unchanged** — still 0.38.3, still dated 29 July |

The `.app` bundle contains exactly three files and its only executable is
`Contents/MacOS/bastion-vault-gui`, a Tauri/WebKit desktop binary linking
`WebKit.framework` with no `server` subcommand in its strings. **It is a GUI client and
cannot serve the integration suite.** No other `bvault` binary exists under `/usr/bin`,
`/usr/local/bin`, `/opt/homebrew/bin`, `/opt/local/bin`, `~/bin`, `~/.local/bin` or
`~/.cargo/bin`.

**What this changes: nothing in the decisions, one thing in the instructions.** Ruling 1
stands, and the 0.44.5 bump further confirms the product line is past 0.42.0, so the matrix
remains correct. But the remedy must name the component precisely, because the obvious
update channel updates the desktop app instead: what M12 needs is the **`bvault` server
CLI** at 0.42.0 or newer — the binary that answers `bvault server` — on `PATH` or at a path
given to `BASTIONVAULT_TEST_BIN`. Recorded so the next session does not re-run the same
update and re-measure the same 0.38.3.

**Second addendum, 2026-09-22 — the server CLI is now 0.44.5 and ruling 1 is discharged.**
Re-verified after a second install:

| Component | Was | Now |
|---|---|---|
| `/usr/local/bin/bvault` | 0.38.3, 70 MB, dated 29 July, answering `bastion_vault 0.38.3` | **0.44.5**, 89 MB, dated 22 September 16:24, answering `bvault 0.44.5` |

`bvault server` and `bvault operator init` are both present. **0.44.5 clears
`test-matrix.json`'s 0.42.0 minimum**, so managed mode can now run against a supported
version and **R-6 is discharged for local runs** — the first time in this project that a
supported BastionVault server has been available to it.

**What is unblocked:** slices 1–6 verify against a supported server; D-M12-5's fixture
captures become permissible under the owner's ruling 2, since the running server is now
≥ 0.42.0, and each capture records `0.44.5` as its source.

**What is *not* unblocked, and must not be quietly claimed.** `ITG-002` pins *the versions
the matrix lists*, and the matrix lists two: `minimum` = **0.42.0** exactly, and `latest`.
A 0.44.5 binary is best read as satisfying the **`latest`** row; it is **not** the
`minimum` row, which names 0.42.0 specifically. So:

- A local managed run against 0.44.5 is evidence for the `latest` entry only.
- `ITG-030`'s "CI MUST run against each listed version" still needs **0.42.0**, which
  remains reachable only through `ghcr.io/ffquintella/bastionvault` — still returning
  `denied` here. **Slice 7 stays held.**
- Reporting a 0.44.5 run as a matrix-conformant run would be `VER-003`, and the narrower
  version of exactly the error this record was written to prevent.

### D-M12-15 — Rulings on slice 1's open questions

Slice 1 landed the harness and, as instructed, flagged rather than settled four calls.
Settled here so slices 2–7 read a decision instead of re-deriving one (**TOK-008**).

**Ruling A — a managed *binary* never claims a matrix entry; it claims "supported".**
Slice 1's proposed rule is adopted, in preference to the reading the Strategic Orchestrator
floated earlier (that 0.44.5 "is" the `latest` row). The delegate's rule is better and the
reason is worth recording: **`latest` is a tag, not a version.** A harness running offline
cannot know what `latest` currently resolves to, so "this binary is the `latest` entry" is
a claim with no procedure to check it. The only comparison with a well-defined answer is
against the pinned `minimum`, `0.42.0`.

Therefore: the binary path gates on `>= minimum` and records **supported / not supported**;
it asserts no matrix entry. `ITG-030`'s per-version obligation is carried **entirely by the
container path**, where the image tag *is* the version identity. **Consequence for slice 7:**
the CI matrix job must run the container path, not the binary path. A binary-only CI job
would satisfy `ITG-002` and silently not satisfy `ITG-030`.

**Ruling B — `BASTIONVAULT_TEST_ALLOW_UNSUPPORTED_VERSION` is removed.** It is an
unused hole in an R3 gate. The environment that motivated it (a 0.38.3 binary) no longer
exists, and this project's most expensive recurring defect is gates that do not bind — three
milestone exits to R-14, plus D-M1b-19's analyzer that enforced nothing while looking green.
An override that lets a run proceed against an unsupported server is exactly the thing that
reaches CI by accident and is noticed a milestone later.

The cost is accepted and named: **a developer whose only server is below the minimum cannot
run the integration suite at all.** That is the correct outcome — the suite measures
conformance against supported versions, and a run against an unsupported one measures
nothing the project may claim. If the need returns, re-adding it is a three-line change that
must carry a recorded reason (**TOK-012**'s discipline applied to a gate rather than a
model).

**Ruling C — `ITG-020`…`ITG-023` are slice 2's**, exactly as D-M12-1 books them. Slice 1 was
right to scope them out. They need an SDK-level log and observer capture wired into the
`IntegrationTest` base class, which is why they are their own slice rather than a rider on
the harness.

**Ruling D — two harness limits are accepted and recorded, not fixed here.** (i) The
**container path has never been executed** — the Docker daemon is down and the registry
returns `denied` — so it is written to the same contract as the binary path and reported as
**untested**, never as working. Slice 7 is the first thing that exercises it, which is a
second reason Ruling A puts `ITG-030` there. (ii) `bvault` creates a **global
`/tmp/bastion_vault` work directory**, so two managed servers on one machine can collide;
managed-mode concurrency is therefore **one run per machine**. Slice 7's matrix job must run
its versions on separate runners or sequentially — if it fans out two versions onto one
runner, they will fight over that directory.

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
