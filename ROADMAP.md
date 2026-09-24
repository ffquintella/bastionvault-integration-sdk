# Roadmap — implementing the specifications

**Owner:** Strategic Orchestrator (Claude) · **Authority:** subordinate to [`agents.md`](agents.md) and [`claude.md`](claude.md)
**Source of truth for behaviour:** [`specifications/`](specifications/README.md) · **Version:** 1.43.0 · 2026-09-24

## 1. Objective

One verifiable outcome: **the .NET, Rust and Python SDKs each satisfy every MUST
requirement of the 389 requirements in [Appendix D](specifications/appendix-d-requirement-index.md)
at conformance level Complete (CNF-003), with the quality gates CNF-020…CNF-027 green.**

**The work is staged by language (D-1, amended 2026-09-14 at the project owner's
direction).** **Stage 1** takes `dotnet/` alone through every remaining milestone, M2b to
M12, until .NET is `Complete` with its integration suite green. **Stage 2** then brings
`rust/` and `python/` to the same level from the decision records and fixtures Stage 1
settled, and the shared `1.0.0` tag closes it. No Stage 2 work starts before Stage 1 exits.

Definition of done, per language:

1. Every applicable requirement ID appears in the traceability report with at least one test (CNF-014, TST-041).
2. Every applicable fixture under `specifications/fixtures/**` is exercised (CNF-015).
3. Line **and** branch coverage ≥ 95 % on library code, no exclusion pragmas (CNF-010, TST-030).
4. The three implementations are behaviourally identical or carry a recorded parity exception (CLA-003).
5. README declares `Complete`, the spec version, and the tested server versions (CNF-041).

Items 1, 2, 3 and 5 are checked for .NET at Stage 1 exit. Item 4 is a Stage 2 exit
criterion; during Stage 1 it is satisfied by the recorded exception in D-1 and D-6.

## 2. Current state (2026-09-23, **M11 complete; M12 complete, held at its R3 gate** — the live suite's reds measured to their causes, and four of five closed)

**M11 and M12 are running concurrently at the project owner's direction**, against §7's
`M11 ──▶ M12` arrow. The deviation is recorded and reasoned in
[DR-0019](decisions/0019-m12-live-integration-suite.md) D-M12-3: what M12 actually needs
from M11 is the "Running integration tests" README section and the conformance statement,
both at the *exit*. Nothing in the harness or the 32 scenarios depends on documentation, so
the arrow is a documentation dependency at the end rather than a build dependency at the
start.

**A supported BastionVault server exists for the first time.** `/usr/local/bin/bvault` is
**0.44.5**, above `test-matrix.json`'s 0.42.0 minimum, installed by the project owner on
2026-09-22 after the first attempt updated only the desktop GUI. **R-6 is discharged for
local runs and simultaneously materialised** — see §8.

**M11: complete, all sixteen slices (2026-09-23).** The documentation
contract and **D2** (slice a), **D3** and **D7** (slice b), **D4** and **D5** (slice c), all
eleven **D6** engine guides (slices d1, d2), **D8**, **D9**, **D10** and **D13** (slice e),
the doc-comment worksheet generator (slice f0), **481 of 481 operations** carrying
`DOC-005`/`DOC-006` — 218 citing a requirement ID, **263 on D-M11-21's section-file
fallback** (corrected 2026-09-23, D-M11-27; the milestone closed claiming 473 of 473 over a
corpus that structurally excluded the client's own eight entry points, and the 254/219 split
was itself a pre-D-M11-26 tally never updated after that sweep converted 43 tags) — D1 and the generated D11 (slice g1), and the six gates, each proven against a
seeded violation (slice g2). `docs/` exists, with a `DocsSamples` project in which every documented sample is
a compiled, **executed** test and a drift check that fails when a markdown fence stops
matching the source that ran.

**The fallback count is the milestone's most useful output, not its blemish.** 263 of 481
tagged operations have no requirement ID of their own, which is a worklist for a future
specification revision that was invisible while every operation could wear a
plausible-looking ID ([DR-0018](decisions/0018-m11-documentation-and-usage-guides.md)
D-M11-21). Two surfaces dominate it. **Section 12's Rustion surface is the largest gap
found: 40 operations — sessions, targets, master, policy — governed by no requirement at
all.** `RUS-001` and `RUS-002` govern recordings downloads; `RUS-003` maps error tokens,
names no operation, and has no `BV-RUSTION` throw site in the SDK. M10 booked all three as
landed and that stays true *at the requirement level* — it was never a claim about the 40
operations that carry no requirement. **Section 09's PKI surface is second**, at 50
fallbacks in 69 operations. Both are now named, which is what the count was built to do.

**D-M11-23 was added mid-milestone after two slices made the same error.** A transport
invariant — `SSB-001`'s and `SYS-080`'s "routes MUST be pinned to `/v2`" — is not
per-operation governance, however much it resembles the legitimate `AUT-043` reuse that
enumerates its paths. the sweep, widened to audit all 103 distinct IDs in use, found **43** — the two that
prompted the rule were 17 % of it. Two independent slices converging on the same substitution, one of them citing
the good precedent while doing it, is an under-specified rule rather than carelessness.

**M12: complete, held at its R3 gate — six slices of seven, and the seventh's blocker
named.** The live-server harness (slice 1), the four whole-run assertions `ITG-020`…`ITG-023`
(slice 2), and scenarios `ITG-S01`…`ITG-S32` (slices 3–6). **31 of 32 scenarios pass** against
live `bvault` 0.44.5.

**The five recorded reds were measured to their causes, and four were ours.** The previous
revision of this section said they would "go green on their own when F10's `KvWire` fix lands
and R-37 is decided". That was three claims and two of them were wrong
([DR-0021](decisions/0021-live-server-findings.md) fourth addendum):

- **F10's fix had already landed.** Every PKI timestamp already routed through the widened
  helpers, so `ITG-S21`'s `ListCertificatesInfo` failure was **mis-tagged**. Measured
  directly, a `certs-info` row omits `source`, `is_orphaned` and `key_id` entirely. Types
  widened; section 09 amended (`PKI-022` proposed).
- **R-37 was never on the path.** R-37 covers the **fourteen unmeasured** duration call sites;
  every failing assertion was against one of the **eleven measured** ones. R-37 is untouched
  and stays exactly as open as it was.
- **Two landed specification amendments had never been implemented.** `09-pki-engine.md`'s
  Go-style duration rule and `crl_number`'s optionality — the latter in the document since
  `0.21.0` — were both live in `specifications/` while `PkiWire` still wrote numbers and still
  threw on an absent `crl_number`. **This is a new failure shape and it has its own row,
  R-40**: an amendment is a claim about the SDK's behaviour, and landing it without the
  implementation moves the repository from "the SDK disagrees with the server" to "the SDK
  disagrees with its own specification", which is worse, because `CNF-001` is measured against
  the latter.
- **One plain SDK defect.** `IdentityKernelWire.ReadArrayEnvelope` could not read an array
  nested under a named key inside `data`, which is what the server returns. Ten call sites
  were re-measured against a live server and all ten needed a key — `Files.Versions`,
  `Files.History`, `Resources.Secrets.History`, `Resources.History`, `Identity.Aliases`,
  `Identity.Groups.History`, `AssetGroups.History` and all three `Notifications` list routes.
  The specification makes no envelope claim for any of them, so this one is purely ours.

**All three harness states are now demonstrated, closing acceptance criterion 1.** External
mode had never been exercised. A server was stood up outside the harness, initialised and
unsealed, and the full suite run through the `External` path; the `ITG-003` skip state was
re-proven in the same session and its reason string **observed**, not asserted.

| Run | Result |
|-----|--------|
| Managed, before | 55 pass · 6 fail · 6 skip |
| Managed, after | **59 pass · 2 fail · 6 skip** |
| External, after | **59 pass · 1 fail · 7 skip** |
| Skip state | 24 pass · 43 skip, `no BastionVault test server available` observed |
| Managed, re-run 2026-09-24 | **60 pass · 2 fail · 5 skip** |
| External, re-run 2026-09-24 | **58 pass · 2 fail · 7 skip** |

**The `0.24.0` re-run against `bvault 0.44.5` confirms the reds and refines one of them.**
`ITG-S26` is unchanged on all five findings (**R-41**), as expected. `ITG-S11` failed in
**both** modes this time, so **F9's recorded control — "it passed the external run of the
same commit" — did not reproduce**. It was measured rather than assumed: run in isolation
against the same external server, `Scenario11_AutoRenew` **passes**. Load-sensitivity
therefore holds on a *better* control than the original — isolation versus full suite, rather
than external versus managed, the latter being itself load-dependent and so never a clean
control. Not a regression and not a new defect; the F1 residual behind it is unchanged and
still routed to the owner. The failure now surfaces as a `403` on the follow-up
`LookupSelfAsync` rather than as a non-positive TTL — the same final assertion point, one
notch further along. **Only one skip in the whole suite is an `ITG-031` version skip** (the
self-test proving the mechanism, `requires server >= 99.0.0`); four are opt-in proof gates
and the two external-only skips are mode-dependent.

**Slice 7 stays outstanding, blocker re-measured 2026-09-24 rather than inherited.** `0.42.0`
is unobtainable by every available route: `ghcr.io` returns **403** to an anonymous token, the
Docker daemon is **down** (CLI present, no daemon), and the upstream repository has **zero
published releases**, so no binary artefact exists to download either. D-M12-15 Ruling A puts
`ITG-030`'s whole per-version obligation on the container path, so the local `0.44.5` binary
cannot discharge it whatever happens to the binary route. **No version claim is made**
(acceptance criterion 4).

**One scenario stays red, and it is the one with no SDK limb at all.** `ITG-S26`'s five unmet
requirements are two server-side gaps: a group's policy is not resolved into a member's token
on the member's next login, and none of the three sharing list routes indexes a group-target
share. **M12 therefore still does not meet acceptance criterion 2 as literally written**, and
that is the correct outcome rather than a shortfall to paper over — an `ITG-031` skip would
claim the scenario could not run here, which is false: it ran, and the server failed it.
Booked as **R-41**. Reported verbatim per **VER-003**.

**`ITG-S11` is not among the reds.** It failed the managed full-suite run and **passed the
external run of the same commit**, minutes apart — F9's load signature, recorded with the
external pass as its control. A known load-sensitive scenario, not a regression.

**Slice 7 and the `ITG-030` matrix run stay held**, blocker re-measured rather than assumed:
`ghcr.io/ffquintella/bastionvault` returns `DENIED` to an anonymous pull token, and the Docker
daemon is down again. One `docker login ghcr.io` with a `read:packages` PAT clears it.
D-M12-15 Ruling A puts `ITG-030`'s whole per-version obligation on the container path, so the
local 0.44.5 binary satisfies `ITG-002` and cannot satisfy `ITG-030`. M12 exits with slice 7
explicitly outstanding and its blocker named, exactly as acceptance criterion 4 permits.

**The D-M12-4 conformance audit is discharged, and `Core` has a closed blocker list for the
first time.** [DR-0022](decisions/0022-m12-core-conformance-audit.md) classifies every
baselined requirement in `Core`'s sections — and corrects D-M12-4's own subject set from 33
to **54**, because `Core` includes sections 15 and 16 outright and the record had omitted
their 26 `DOC`/`TST` IDs. `TRN-081` is implemented on the owner's implement-over-amend ruling
(D-M12-22). **The list is now one item: `DOC-030`**, which closes on the first real
publication and is not M12's to close (D-M12-21). No conformance level is declared at M12.

**The milestone's real output is knowledge, not code.**
[DR-0021](decisions/0021-live-server-findings.md) now catalogues **fourteen divergences**
between the specification, the SDK and the real server. Eleven milestones of fixture-based
development could not have found any of them: the fixtures were authored from the
specification, so the loop could only ever prove the SDK matched the document (**R-38**,
`FIX-010` unmet corpus-wide).

**One user-facing defect found and fixed out of band.** M11 slice a's first compiled sample
proved `dotnet/README.md`'s quick start never worked — `BastionVaultClient` left `Transport`
null and threw `InvalidOperationException` on first use, against
`02-client-configuration.md:37`'s documented default of HTTP.
[DR-0020](decisions/0020-default-transport-conformance-gap.md) fixed it; the sample mechanism
paid for itself before the milestone that introduced it finished its first slice.

**1694 .NET unit tests, 99.19 % line / 95.03 % branch**; traceability **327 covered / 104
baselined** of 431. **Branch coverage sits 0.03 points above the `CNF-010` floor** and did not
move across any change in this close — including the `DOC` tagging pass below, which added
four tests and moved it not at all: the next change that adds a branch without a test breaks
CI, so this is a number to watch rather than a margin to spend.

**M11's `DOC` traceability was corrected on 2026-09-24, and the correction is small because
most of the gap is structural rather than untagged.** M11 built the content and the six gates
but never tagged the requirement IDs onto asserting tests, so 19 of its 21 `DOC` IDs sat on
the baseline. Three come off here — `DOC-002`, `DOC-007` and `DOC-024`, on a new
`ErrorDocsDriftTests` that diffs D7's generated error table against `ErrorCatalog.All` and was
proven against a seeded hint drift. `DOC-002` and `DOC-024` each name **two** generated tables,
so their D3 limb is tagged on the existing `ConfigurationDocsDriftTests` and their D7 limb on
the new class; tagging only the new one would have pointed traceability at half a requirement. **The other sixteen do not, and the reason is worth
recording rather than retrying:** the expectation that most were "asserted but untagged" does
not hold. Each remaining ID is in one of three states — its checker is a Python script run
from `repo-gates.yml` rather than a test (`DOC-001`, `DOC-020`, `DOC-023`, `DOC-025`); its
assertion is a real executed test that the scanner structurally cannot see, because
`BastionVault.IntegrationSdk.DocsSamples` is a genuine xunit project that sits outside any
`.Tests` directory (`DOC-003`, `DOC-011`, `DOC-022`, and `DOC-005`/`DOC-006` in `tools/`); or
nothing asserts it at all (`DOC-004`, `DOC-010`, `DOC-012`, `DOC-013`, `DOC-014`, `DOC-015`).
`DOC-030` stays held under D-M11-8. **No ID was tagged onto a test that does not assert it**
(**CLA-004**), which is what the second group would have required. Booked as **R-42**.

**Three specification changes go to the R3 human gate with this work**: the `pki/*` and
`auth/token/create` duration amendments, `crl_number`'s optionality, and the new `certs-info`
optionality landed under the owner's Ruling 1 and flagged as an application of their principle
rather than an extension of their mandate. **No tag is cut here** — a release is outward-facing
and R3, `v0.23.0` had the owner's explicit confirmation (D-M12-24) and this work has none, so it
lands under `## [Unreleased]`.

**`rust/` and `python/` remain untouched** under the D-1/D-6 Stage 1 freeze. Parity for
DR-0020's transport default and for whatever DR-0021 settles is **owed at M13**, and both are
recorded so M13 implements the corrected behaviour rather than copying .NET's former one.

## Previous state (2026-09-22, **M10 complete** — sections 05 and 12 clear entirely in .NET; no conformance level declared)

**M10 is complete, all five slices.** `Client.Identity`'s kernel, `Client.AssetGroups`,
`Client.Resources`, `Client.Files`, `Client.Ldap`, `Client.CertLifecycle`,
`Client.Notifications`, `Auth.Userpass.Admin.*` and `Client.Rustion` are all in .NET.
**1652 .NET tests, 99.17 % line / 95.06 % branch**; traceability **321 covered / 110
baselined** of 431; **253 fixtures on disk**, unchanged — none of M10's mounts carry an
Appendix C mandatory fixture.

**All nine booked IDs landed, none held back.** `IDN-001`/`IDN-002` (slice a),
`RSC-001`/`RSC-002`/`FIL-001` (slice b), `LDP-001` (slice c), `RUS-001`/`RUS-002`/`RUS-003`
(slice e). Slice d carries no requirement ID of its own — its deliverable is closing **R-29**,
the `Auth.Userpass.ListUsersInfo` naming question M8 left open: `ListUsersInfo`/
`ListUsersInfoAll` moved to `Auth.Userpass.Admin` (D-M10-3), a breaking rename on an
unpublished API.

**`Complete` is not declared, exactly as `Standard` was not at M8 and `Core` was not at M4.**
R-14's schedule is now three for three: sections 16–17 are M11's, CNF-002 forbids the claim,
`dotnet/README.md`'s gap list is regenerated instead. The row has moved from forecast to
pattern.

**`rust/` and `python/` are unchanged** under the D-1/D-6 Stage 1 freeze — no fixture, no
tripwire movement, since M10 added none. R-29's closure is cheap only because of that freeze:
the rename must reach Stage 2 before its first publication, or the unpublished-API limb it
rests on no longer holds.

**The review cost is again the story of this milestone — see §5.** Four of five slices were
blocked at least once at their R2 handback gate; slice d's only blocking condition was a
missing record, not a code defect. One decision-record citation — DR-0017's own, claiming
`RUS-002`'s node-local mechanism was "reused by `sshbroker` at M9" — was checked at slice e's
gate and found false, the first time in this project a citation the Strategic tree wrote
itself was the one that needed correcting.

**Two risk rows open (R-35, R-36), both past M10.** `identity.self`'s missing fixture capture
and `Files.Sync`'s undocumented credential-field names are recorded gaps, not defects, and
neither blocks M11.

## Previous state (2026-09-21, **M9 complete** — sections 09 and 10 bound in .NET; no conformance level declared)

**M9 is complete, all four slices.** `Client.Pki` (with `.Acme`, `.Csr`, `.SignRequests`),
`Client.Ssh` and `Client.SshBroker` bind roughly **91 operations** across sections 09 and 10.
**1504 .NET tests, 99.48 % line / 95.86 % branch**; traceability **305 covered / 120
baselined** of 425; **253 fixtures on disk**, all seven of Appendix C's `pki.*`, `ssh.*` and
`sshbroker.*` green — including `sshbroker.effective-v2-pinned`, which had sat on disk
undriven since the original specification import.

**10 of the 11 booked IDs landed, and the 11th is held back rather than missed.**
`PKI-030`'s second limb requires recognising a queue-cap breach *by message*, and
`BV-QUOTA-002` has no recognition row in Appendix B and no server message in any document.
Inventing one is what R-23 already cost this project three milestones, so the ID **stays
baselined** and the gap is **R-31**, owned by M10 (D-M9-11). `TRN-031` came off the baseline
on evidence, as M7's `TRN-072` did.

**`rust/` and `python/` are unchanged** under the D-6 freeze, touched only for the
fixture-count tripwire (250 → 253). The Python suite could not be executed in this
environment — no `pytest` module — so its corpus assertion is verified by diff and by the
.NET and Rust harnesses, which both run green at 253. It is **not** reported as green.

**The review cost is the story of this milestone, and M10 should read §5 before it starts.**
The framing record was blocked **three times**; every slice was blocked or returned at least
once; and **three separate repairs introduced a new defect while closing an old one** — the
worst being a GET form, added to satisfy a "bind both verbs" finding, that put an export
password in a query string. Two of the milestone's own rulings were wrong and were reversed
by the record itself, both because a column was read without its legend.

**Four risk rows open (R-31…R-34) and one sub-question closes.** R-27's standing instruction
— assume a third section-14-versus-catalogue contradiction until all seven `*-info` rows are
checked — is discharged: all seven are checked and there is no third. The row stays open
because the specification reconciliation is still unmade.

## 3. Sequencing decisions

These are decided. No delegate reopens them (TOK-008).

### D-1 — Superseded 2026-09-14: .NET runs to completion first (Stage 1), then Rust and Python (Stage 2)

**Original decision (2026-09-13, retained below for the record):** each milestone lands in
.NET, Rust and Python before it exits, so the parity check is the exit criterion rather than
a later phase. Rejected alternative at the time was *.NET to Complete first, then port*,
on the grounds that a first port re-litigates every contract decision .NET made implicitly
and tunes the fixture suite to one language's idioms.

**Superseding decision, directed by the project owner (2026-09-14):** the rejected
alternative is now the plan. **Stage 1** takes `dotnet/` through every remaining milestone
— M2b, M2c, M3 … M12 — to `Complete` conformance and a green integration suite, entirely on
its own. **Stage 2** starts only once Stage 1 exits, and brings `rust/` and `python/` to
parity from the decision records, fixtures and .NET behaviour Stage 1 fixed, milestone by
milestone, closing with the shared `1.0.0` tag.

**Forces re-examined:** CLA-003 (no behaviour change in one language only) is not violated
by staging, because CLA-003 governs a *shipped* behavioural claim, and Stage 1's releases
are recorded as `.NET`-only per the D-M2-15 exception pattern, exactly as `0.5.0` already
was. The parity risk the original D-1 was written against (R-4, R-9, R-9a) does not
disappear — it is deferred to Stage 2 and must be paid there in full, milestone by
milestone, using the same fixtures and public-surface diff this file already requires.

**Rejected (2026-09-14 revisit):** *keep horizontal slicing and simply move faster.*
Rejected because the project owner's direction is explicit and is a scope decision the
Strategic Orchestrator accepts rather than re-argues (`claude.md` §1); Claude's role here is
to record the consequence honestly, not to relitigate the choice a second time.

**Consequence.** Stage 1 wall-clock is bounded by .NET alone, which is faster per milestone.
The cost moves to Stage 2: every contract .NET settles implicitly — not only the ones an
explicit decision record captured — is a candidate defect for Rust and Python to inherit
silently, and Stage 2 has no .NET-review round-trip to catch it before it lands in two
languages at once, because there is no longer a same-milestone .NET pathfinder pass ahead
of it. D-6 below is the mitigation: Stage 2 re-opens the review rigor D-2 used to buy one
milestone at a time, applied instead across the whole backlog Stage 1 leaves behind.

### D-2 — Within Stage 1, .NET is simply the only lane; within Stage 2, .NET is the settled reference

**Original decision (2026-09-13):** within a milestone, the shared design is settled first,
then .NET implements as pathfinder, then Rust and Python implement in parallel from the
same brief and fixtures — not from the .NET source — so an under-specified brief produces
one divergent reading to fix, not two.

**Amended for staging (2026-09-14).** During **Stage 1** this decision is dormant: there is
only one lane, so "pathfinder" and "parity pass" collapse into the same .NET-only step, and
its design-review round-trip is the sole gate (§7). During **Stage 2**, D-2's mechanism is
restored but its source changes: Rust and Python implement in parallel from the same
decision records and fixtures **and from .NET's already-reviewed behaviour**, which is now
the working reference in the way "the brief" was during horizontal slicing. Stage 2 does not
re-run .NET as a pathfinder a second time; it treats every Stage-1 decision record as
already settled (TOK-008) and every Stage-1 fixture pass as the target, not a hint.

**Rejected:** *all three in parallel from the brief, disregarding .NET's behaviour, during
Stage 2.* Rejected for the same reason as before — an under-specified brief still produces
divergent readings — and because it would throw away the twelve milestones of .NET-only
review Stage 1 paid for.

**Consequence:** Stage 1 has no serialisation cost (one lane, moving alone). Stage 2 inherits
the parity risk in full (see D-1) and must budget a genuine cross-language review pass per
milestone, not a lighter one, because it is verifying against eleven milestones of
accumulated .NET-only decisions rather than one.

### D-6 — Stage 2 entry gate

**Decision:** Stage 2 does not start milestone-by-milestone alongside Stage 1, and does not
start on any single .NET milestone's exit. It starts once **all** of M2a through M12 have
exited in .NET — i.e. once §1's Stage 1 definition of done holds for `dotnet/` in full,
including the live-server integration suite (M12) and the `Complete` conformance
declaration.

**Rejected:** *Start Stage 2 on Rust/Python as soon as each .NET milestone exits,
milestone-by-milestone.* This is closer to the original horizontal-slice plan under a
different name and reintroduces the parallel-milestone cost the project owner's direction
was meant to remove. It also risks a Rust/Python pass built on a .NET contract that a later
.NET milestone still revises (as M1a's transport seam was revised at M1b) — paying the
rework cost D-1's original rejection warned about, just deferred rather than avoided.

**Consequence:** `rust/` and `python/` stay frozen at the M2a/`0.5.0` state — catalogue
regenerated, no authentication surface — for the full duration of Stage 1. This is a
known, accepted gap, not a defect to fix mid-stage.

### D-7 — M15 exists because eleven risk rows had no owner, and six of them looked owned

**Decided 2026-09-24 on the project owner's direction** (§10 question 15: *assign the most
indicated risk owner*). A §8 sweep at M12's close found **eleven** rows with no live owner —
two more than the nine first reported, because R-18 and R-25 name owners (M6, and "the
count-derivation session") that read as live and are not.
Two shapes, one symptom:

- **Orphaned to a closed milestone.** R-18 (M6), R-30 (an "M9/M10 candidate"), R-31, R-32,
  R-35 (M10) and R-36 (M12) all name an owner that has exited. These read as *owned* to
  anyone scanning the register, which makes them worse than the unowned ones.
- **Never owned.** R-17, R-22, R-24, R-25 and R-33 name no milestone at all.

**This is R-16's own recorded lesson recurring eleven times.** That row's moral is that *a
gap booked with no owner survives a milestone*; it was raised at M5 and reached M8 untouched.
Nothing detected the recurrence, because a row saying "owned by M10" is indistinguishable
from a row that is genuinely handled until you check whether M10 is still open.

**The assignment rule used, in order:** (1) if the fix is a `specifications/` change, it is
M14's, which already owns specification work and is already R3; (2) if the fix is Rust or
Python code, it is M13's, which is the only milestone that may touch them under D-1/D-6;
(3) if the fix is .NET code or a shared artefact that **M13 would otherwise transcribe into
two more languages**, it goes to **M15**, new here; (4) if a live server settles it, it goes
to the D-M12-5 follow-up slice, already authorised and running.

**Why M15 rather than folding everything into M13.** Rule 3 is the whole reason M15 exists.
R-30, R-32, R-33 and R-25 are all cheap in one language and expensive in three: the moment
M13 begins, each becomes three fixes plus a parity exception instead of one fix. This is
exactly the reasoning R-29 already applied to the `Auth.Userpass.Admin` rename and R-32
applies to its own typed-record replacement — *a breaking change on an unpublished API is
cheap if taken deliberately and a surprise if not*. M15 is the deliberate place to take them.

**Sequencing.** M15 runs **after M12 and before M13**, and it does **not** relax D-6: D-6's
gate is that all of M2a…M12 exited in .NET first, which M15 does not change. M15 is .NET-only
and shared-artefact work, so Stage 2 still starts from a settled reference — a *more* settled
one than it would have otherwise.

**M15 is small and it is capped.** It is carried debt, not a feature milestone; if it grows
past its tier it is decomposed (**TOK-011**), never granted a larger budget. Any row it
cannot close is **re-assigned explicitly**, never returned to the register unowned.

**The standing control matters more than the assignment.** The rows were not undone because
anyone decided to defer them; they were undone because nothing noticed their owner had left.
`scripts/validate-agent-docs.py` gains check **C8**: a milestone marked ✅ in §4 while a §8
row still names it as owner is a gate failure. That is what stops the twelfth instance.

**C8's own boundary is enumerated, not assumed** (D-M11-27, **R-10**). The gate was seeded
*inside* its corpus — revert a row to `owned by M10` and it fails, correctly — and seeded
*outside* it, which is the test M11 learned to run: C8 recognises the literal phrase
`owned by M<n>`, so of the eleven rows found here it would have caught **six and missed
three** — R-30 (*"an M9/M10 candidate"*), R-36 (*"whichever milestone next has the server
capture"*) and R-25 (*"owned by the count-derivation session"*). A candidate is not an owner,
a description is not a name, and a session is not a milestone. Those exclusions are written
into the check itself with their reasons, and the register's convention is now to say
`owned by <milestone>` or `unowned` outright. **A row that describes its owner in prose is
outside this gate and stays a human-review item** — which is a smaller claim than "C8 stops
this recurring", and the true one.

### D-3 — Harness before features

The traceability tool, fixture loader, mock server and CI gates (M0) precede all feature
work. Coverage and traceability are pass/fail gates on every later milestone; building them
late means every earlier milestone is re-opened to satisfy them.

**Rejected:** *ship KV first to prove value, add gates after.* Rejected because CNF-014 and
TST-041 are themselves MUST requirements — deferring them just hides the debt.

### D-4 — Conformance level is declared incrementally

`Core` is declared at M4, `Standard` at M8, `Complete` at M10. Until a level's sections are
complete, the README lists known gaps by requirement ID (CNF-002). This keeps the repo
honest at every commit rather than silent until the end.

**Amended at M1a (D-M1a-22):** no per-language README exists yet and none makes a conformance
claim, so there is nothing for CNF-002 to qualify. `tools/traceability/baseline.json` is the
gap list until M4, where the READMEs are authored and the baseline is rendered into prose.
**Discharged at M4:** `dotnet/README.md` now exists and carries the gap list.

**Broken, and found so at M4 (DR-0009 D-M4-3):** the schedule above cannot be executed as
written. `Core` requires every MUST of sections **16** and **17** as well as 07; those are
the `DOC` requirements, booked to **M11**, which falls *after* M8's `Standard` and M10's
`Complete`. So none of the three declarations is legal at the milestone that claims it, and
M4 exits declaring nothing. The fix is a **resequencing decision for the project owner**
(§10 question 4): either M11 moves ahead of M8, or all three declarations move to the end
and the `0.x` previews stay level-less until then. Claude has not chosen for them, because
it changes milestone order rather than milestone content; either answer leaves the work of
M4–M10 unchanged.

### D-5 — Fixtures are loaded from the repository, never copied

Per TST-010. Each language gets a loader that reads `specifications/fixtures/**` by relative
path. A fixture edit must break all three suites simultaneously, or the parity instrument is
worthless.

## 4. Milestone plan

`Reqs` = requirement IDs whose implementation lands in that milestone. Tiers per
[`agents.md`](agents.md) §5.3 (risk) and §7.1 (size). `Stage` marks which side of D-1's
2026-09-14 supersession a milestone falls on: **1** = .NET only, exits without Rust/Python;
**2** = Rust and Python parity pass over a Stage-1 milestone, using its decision records
and fixtures. M0 and M1 predate the staging decision and already exited in all three
languages, so they carry no stage marker.

| # | Milestone | Reqs | Count | Size | Risk | Stage | Gate at exit |
|---|-----------|------|-------|------|------|-------|--------------|
| **M0** ✅ | Harness, gates and traceability | `CNF`, `FIX`, `TST` | 54 | Large | R2 | — | CI fails on a seeded coverage/traceability regression |
| **M1** ✅ | Client skeleton: config, transport, error model | `OVR`, `CFG`, `TRN`, `ERR` | 105 | Enterprise | R3 | — | **Met** — all transport and error fixtures green in all three languages |
| ├ **M1a** ✅ | Configuration, error skeleton, transport seam | `CFG`, `OVR` | 27 | Large | R3 | — | **Met** — 27 IDs off the baseline, `Client.Construct` fixture green ×3 |
| ├ **M1b** ✅ | Transport, logical layer, retry | `TRN` | 50 | Enterprise | R3 | — | **Met** — 50 IDs off the baseline, all 18 transport fixtures green ×3 |
| └ **M1c** ✅ | Error model | `ERR` + Appendix B | 19 | Large | R3 | — | **Met** — 19 IDs off the baseline, 131 of 134 error fixtures green ×3, Appendix B generated |
| **M2** ✅ | Authentication — Core methods | `AUT` (token, userpass, AppID, token store, auto-renew, security) | **39** | Enterprise | R3 | — | **Met in .NET** — all 39 IDs off the baseline (305→267), auth fixtures green, no token in any captured log (TST-051), R-10 gate sweep done. Parity (×3): deferred to M13/Stage 2 (D-6) |
| ├ **M2a** 🔶 | Token source, token store, harness instruments | `AUT`, `CFG`, `TST` | 14 | Large | R3 | **1** done | **.NET met** — 14 IDs off the baseline, six auth fixtures green, both instruments proven. Rust and Python deferred to Stage 2 |
| ├ **M2b** ✅ | Login response contract, Userpass, AppID, security | `AUT`, `CFG-020`, `CNF`, `ERR-022` | 19 | Large | R3 | **1 done** | **Met** — .NET login fixtures green (10/10), CNF-031/032 asserted (TST-051 extended), 19 IDs off the baseline |
| └ **M2c** ✅ | Automatic renewal + **R-10 gate re-proof sweep** | `AUT-090`…`AUT-095` | 6 | Large | R3 | — | **Met** — 6 IDs off the baseline (267 total for M2), .NET clock-driven auto-renew fixtures green, R-10 sweep complete (3 findings tracked as R-11/R-12/R-13, none blocking) |
| **M3** ✅ | System API — Core subset | `SYS-001,002,005,006,008,050,051,052,053` (health, seal-status, server/cluster info, capabilities; fixed off the `~16` estimate by DR-0007) | 9 | Large | R2 | **1 done** | **Met** — .NET `sys` fixtures green (10/10 of M3's slice; the other 6 `sys.*` fixtures on disk stay pending, owned by M7), 9 IDs off the baseline |
| **M4** ✅ | KV v1 + KV v2; **`Core` found undeclarable** | `KV`, `KV1`, `KV2` | 27 booked, **25 landed** (`KV-001`→M7, `KV-010`→M8) | Large | R2 | **1 done** | **Met, with the gate corrected** — .NET `kv` fixtures green (19/20; `kv.read-many-batch` is M8), 25 IDs off the baseline (259→234), and `dotnet/README.md` authored with the CNF-002 gap list. `Core` is **not** declared: sections 16–17 are unimplemented, so CNF-002 forbids the claim (DR-0009 D-M4-3, R-14) |
| **M5** ✅ | Cluster discovery and resilience | `DSC`, `RES` (+`CFG-043`) | 33 booked, **29 landed** (`RES-001`…`004` were M1b's; `RES-030`→M7; `CFG-043` booked in) | Large | R2 | **1 done** | **Met** — all ten `resilience.*` fixtures green, 29 IDs off the baseline (234→205), `DSC-041`'s scope ruled in [DR-0010](decisions/0010-m5-cluster-discovery-and-resilience.md) D-M5-5 |
| **M6** ✅ | Authentication — remaining methods | `AUT` (FerroGate, Certificate, OIDC/SAML, FIDO2) | ~12 booked, **9 landed** | Large | R3 | **1 done** | **Met, with one stated exception.** All 21 `auth.*` fixtures green — the pending list is empty for the first time since M2a — and 9 IDs off the baseline. Section 05 has no unimplemented MUST **except `AUT-060`'s usage-guide recipe, which is M11's**; the milestone's first exit claim omitted that qualification and review caught it. Blocked once on R-21's `AUT-051` cache and cleared. Also closes **R-18** with the **R-22** residual named ([DR-0011](decisions/0011-m6-authentication-remainder.md)) |
| **M7** ✅ | System API — remainder | `SYS` (init/seal/unseal, mounts, auth methods, policies, namespaces, audit, backup/restore) + `RES-030` | 27 booked, **30 landed** (+`KV-001`, `TRN-071`, `TRN-072`) | Large | R3 | **3 done** (slices a, b, c) | **Met.** All 24 `sys.*` fixtures green with none pending, and Appendix C line 123's list has no gap. Ran as three slices because 27 IDs exceeds one Large brief (TOK-011); slice a was blocked on R-21's `SYS-026` cache and cleared. Landed three IDs more than booked: `KV-001`, stuck since M4 waiting on `SYS-026`, and `TRN-071`/`TRN-072`, cleared on evidence — `TRN-072` had no test asserting it anywhere in the tree until M7b wrote one ([DR-0012](decisions/0012-m7-system-api-remainder.md)) |
| **M8** ✅ | Transit, TOTP and request-efficiency bindings | `TRS`, `TOT`, `BAT`, `PAG`, `CCH`, `EFF` (+`KV-010`) | 39 booked, **39 landed** | Enterprise | R3 | **1 done** | **Met on requirement content; the booked gate was unsatisfiable.** All five slices in, then `CCH-006` after the close-out at the project owner's direction: **39 of 39** IDs off the baseline (158→130 across the milestone), 1348 .NET tests at 99.39 % line / 96.50 % branch, 247 fixtures. **No conformance level declared:** the booked exit "declare `Standard`" is forbidden by `CNF-002` while sections 16–17 are M11's (**R-14**), so `dotnet/README.md`'s gap list is updated instead, as M4 did. Resequencing is §10 question 4, untaken. Every slice was gated and **none passed first time** — see §5 for what the five gates found ([DR-0013](decisions/0013-m8-transit-totp-and-efficiency.md), D-M8-1…D-M8-52) |
| **M9** ✅ | PKI and SSH endpoint bindings | `PKI`, `SSH`, `SSB` (+`TRN-031`) | 11 booked, **11 landed** (10 of the 11 booked, `PKI-030` held back; `TRN-031` cleared on evidence) | Large | R2 | **4 done** (slices a–d) | **Met.** Sections 09 and 10 are bound in .NET — roughly 91 operations across `Client.Pki`, `Client.Pki.Acme`, `Client.Pki.Csr`, `Client.Pki.SignRequests`, `Client.Ssh` and `Client.SshBroker` — with all seven Appendix C `pki.*`/`ssh.*`/`sshbroker.*` fixtures green and the corpus at **253**. **`PKI-030` is held back, not missed**: its queue-cap limb needs a server message string no document states, so the ID stays baselined rather than reporting half a requirement as covered (D-M9-11, **R-31**). `TRN-031` came off the baseline on evidence, as M7's `TRN-072` did. **The framing record was blocked three times before acceptance and every slice was blocked at least once** — see §5. No conformance level declared: section 09 still carries `PKI-030`, and sections 16–17 are M11's (**R-14**) ([DR-0016](decisions/0016-m9-pki-and-ssh.md), D-M9-1…D-M9-30) |
| **M10** ✅ | Remaining engine bindings and identity — **`Complete` found undeclarable** | `IDN`, `RSC`, `FIL`, `LDP`, `RUS` | 9 booked, **9 landed** (`IDN-001`, `IDN-002` at slice a; `RSC-001`, `RSC-002`, `FIL-001` at slice b; `LDP-001` at slice c; `RUS-001`, `RUS-002`, `RUS-003` at slice e; slice d carries no requirement ID of its own) | Large | R2 | **5 of 5** | **Met on requirement content; the booked gate was unsatisfiable, exactly as M4 and M8's were (R-14).** Section 12 clears entirely: `Client.Identity`, `Client.AssetGroups`, `Client.Resources`, `Client.Files`, `Client.Ldap`, `Client.CertLifecycle`, `Client.Notifications`, `Client.Rustion` and `Auth.Userpass.Admin.*` are all in .NET, all nine booked IDs landed, none held back. `Complete` is **not** declared: sections 16–17 are M11's, so CNF-002 forbids the claim — `dotnet/README.md`'s gap list is regenerated instead, as M4 and M8's were. **R-29 closed**: `Auth.Userpass.ListUsersInfo`/`ListUsersInfoAll` moved to `Auth.Userpass.Admin` (D-M10-3), a breaking rename on an unpublished API. **1652 .NET tests, 99.17 % line / 95.06 % branch**; traceability **321 covered / 110 baselined** of 431; 253 fixtures unchanged. **Framing record accepted 2026-09-22** ([DR-0017](decisions/0017-m10-remaining-bindings-and-identity.md)): five slices, dispatched a→b→d→c→e — **slices b, c and d ran concurrently** (b and d directly, c in an isolated worktree) at the project owner's direction, deviating from D-M10-1's serial default, reconciled at merge with no real conflict. **Every slice but one was blocked at least once at its R2 handback gate** — see §5. One decision-record citation (DR-0017's own, on `RUS-002`) was found wrong by the milestone it was written for and corrected as an addendum. New risks **R-35** (`identity.self` fixture has no real capture to author from) and **R-36** (`Files.Sync`'s credential fields ship as an opaque bag, no documented wire names), both open past M10 |
| **M11** ✅ | Documentation and usage guides | `DOC` | 21 booked, **20 targeted** (`DOC-030` held back, D-M11-8) | Large | R1, **c/e/g at R2** | **16 of 16** ✅ | Every .NET doc sample compiles/runs (CNF-026); documents R-26's macOS scoped-resolver caveat and the `DSC-050` nameserver override. **Traceability corrected 2026-09-24: 3 of the 19 untagged `DOC` IDs came off the baseline** (`DOC-002`, `DOC-007`, `DOC-024`, on `ErrorDocsDriftTests`, proven against a seeded drift); the remaining 16 are held on a structural cause, not an oversight — see §2 and **R-42**. M11's exit claim of "20 targeted" was a claim about *content*, and the milestone closed without noticing that content and traceability are two different facts |. **Re-planned twice: 1 slice to ~17 (D-M11-1, D-M11-20), then the remaining DOC-005 pass cut into ten measured slices (D-M11-22) — 473 operations at a measured ~40 per slice, PKI split by line range because 69 exceeded the rate by 70 %.** 438 of 473 operations tagged, 193 of them on D-M11-21's section-file fallback; **D-M11-23** added mid-milestone, ruling that a transport invariant is not per-operation governance and converting 29 such tags in slice g's sweep. ([DR-0018](decisions/0018-m11-documentation-and-usage-guides.md) D-M11-1, D-M11-20): 473 facade operations at a measured ~40 per slice. `DOC-021` was already met on arrival; `DOC-030` is held back with a named owner rather than claimed on a pipeline that has never published |
| **M12** 🔶 | Live-server integration suite, closing Stage 1 | `ITG` | 16 + 32 `ITG-S` scenarios | Enterprise | R3 | **6 of 7** (1–6) | **Complete on content, held at the R3 human gate.** Ran **in parallel with M11** ([DR-0019](decisions/0019-m12-live-integration-suite.md) D-M12-3). All 32 `ITG-S` scenarios exist and **31 pass** against live `bvault` 0.44.5 — **59 pass / 2 fail / 6 skip** of 67 in managed mode, **59 / 1 / 7** in external mode. **The five recorded reds were measured to their causes and four were closed** (D-M12-25, [DR-0021](decisions/0021-live-server-findings.md) fourth addendum): F10's fix had already landed and `ITG-S21`'s remaining failure was **mis-tagged** (a `certs-info` row omits `source`/`is_orphaned`/`key_id`, section 09 amended); **R-37 was never on the path** — it covers the fourteen *unmeasured* duration sites, not the eleven measured ones; **two landed specification amendments had never been implemented** (**R-40**, a new failure shape); and `ReadArrayEnvelope` could not read the `data.<key>` envelope at ten measured call sites. **Acceptance criterion 1 is now met**: all three harness states demonstrated, external mode exercised for the first time and the `ITG-003` skip reason observed rather than asserted. **Criterion 2 stays unmet for `ITG-S26` alone**, on two server-side gaps with no SDK limb — booked as **R-41** and reported red rather than reclassified as an `ITG-031` skip, which would be false. **Slice 7 held**: `ITG-030`'s container path needs a `ghcr.io` credential; blocker re-measured 2026-09-23, not assumed. **D-M12-4 audit discharged** ([DR-0022](decisions/0022-m12-core-conformance-audit.md)): subject set corrected 33 → 54, `TRN-081` implemented on the owner's ruling (D-M12-22), **`DOC-030` remains and no level is declared**. **1690 tests, 99.19 % line / 95.03 % branch.** Three specification changes await the owner's R3 confirmation; **no tag cut** |
| **M13** | Rust and Python parity — M2a through M12 | *(same IDs as M2a–M12)* | ~229 | Enterprise | R3 | **2** | All Stage-1 gates re-met in Rust and Python; parity check across all three; shared `1.0.0` tag |
| **M14** | **Specification coverage for the unspecified surface (R-39)** | new `RUS`, `PKI`, `RSC`, `FIL`, `LDP`, `IDN` IDs, or recorded non-specification | 193 fallback-tagged operations triaged | Enterprise | **R3** | **0** | Every operation carrying a D-M11-21 section-file fallback is triaged: a requirement ID is minted, it is folded under an existing ID that genuinely governs it (the `AUT-043` enumeration test in D-M11-23), or it is recorded as deliberately unspecified with a reason. Exit is the triage being complete and the fallback count being *explained*, not necessarily zero. **Prerequisite: M11**, which produces the worklist. `specifications/` changes are R3 and need Architect decisions plus human confirmation (`CRS-004`, §5.3) |
| **M15** | **Carried debt and pre-parity cleanup** (D-7) | no new IDs | 5 risk rows | Medium | R2 | **0** | **R-25**, **R-30**, **R-32**, **R-33** and **R-36**'s fallback, each closed or explicitly re-assigned. Created at M12's close because eleven rows had no live owner and six read as owned (§10 question 15). **Runs after M12 and before M13**, and does not relax D-6. The selection rule is rule 3 of D-7: every row here is **cheap in one language and expensive in three**, so taking it before Stage 2 starts is the difference between one fix and three plus a parity exception — R-29's reasoning applied again. Capped as carried debt: over tier it is decomposed (**TOK-011**), never given a larger budget, and any row it cannot close is re-assigned by name rather than returned to the register unowned |

**Total: 389.** The `AUT` and `SYS` splits (M2/M6, M3/M7) are estimates against the section
headings; the exact ID lists are fixed when each milestone brief is authored, and the two
halves always sum to 40 and 35 respectively (`SYS` gained `SYS-006` at M3's brief, DR-0007).
**M13's ~229 is likewise an estimate**, summing M2a's 14 plus M3's now-fixed 9 plus the `~`
counts of M6 and M7 as booked; the exact list is fixed per source milestone as each M13
block is briefed, same as every earlier estimate in this table.
**M13 is Stage 2 in full** — see §5 for why it is tracked as one long-running milestone with
per-source-milestone exit criteria rather than re-split into M2a′…M12′, and D-6 for its
entry gate.

## 5. Milestone detail

### M0 — Harness, gates and traceability *(blocks everything)*

**Deliverables**

- `tools/traceability` — parses Appendix D, scans the three test suites for requirement
  markers (TST-040), emits the report, exits non-zero on an uncovered applicable MUST (TST-041).
- Fixture loader per language reading `specifications/fixtures/**` in place (TST-010/012, `FIX-*`).
- Fixture test driver implementing the five steps of TST-011.
- In-process HTTPS mock server per language with the TST-020 capability list and the
  TST-021 simulation list (sealed, standby, uninitialised, DoS ban, quota, 404/405/204,
  login-failure-as-200).
- CI gates: build-warnings-as-errors (CNF-020), tests (CNF-021), coverage ≥ 95 %
  line and branch with artifact publication (CNF-022, CNF-013, TST-031), lint (CNF-023),
  dependency audit (CNF-024), secret scan whitelisting only `specifications/fixtures/**`
  (CNF-025, TST-050), public-API diff (CNF-027).
- Scaffold cleanup: remove `Class1.cs`, `UnitTest1.cs`, placeholder `lib.rs` contents.

**Exit criterion.** Seed a deliberate violation of each gate (a dropped requirement marker,
a 94 % coverage file, a fake token outside fixtures) and prove CI fails on each. A gate that
has never failed has not been tested.

**Note.** The mock server and traceability tool are free of SDK types by design, so M0 does
not presuppose M1's API shape.

### M1 — Client skeleton *(the contract everything else inherits)*

Sections 00, 02, 03, 04. This is the single highest-leverage milestone: 105 requirements,
and every later milestone is expressed in terms of the types it defines.

**Sub-slices, in order:**

1. ~~**M1a — Configuration (`CFG`, `OVR`).**~~ **Complete (2026-09-13).** Configuration model,
   environment-variable precedence, TLS options, timeouts, token helper, plus the
   configuration-shaped security baseline CNF-030, CNF-033, CNF-035. Design and exit record:
   [`decisions/0003-m1a-configuration.md`](decisions/0003-m1a-configuration.md). 27 IDs left
   the baseline; every deferred `CFG-*`/`OVR-*` ID now names its owning milestone (D-M1a-10).
2. ~~**M1b — Transport (`TRN`).**~~ **Complete (2026-09-13).** HTTP mapping, headers,
   envelope, status-code handling, the `LIST` verb, redirects refused (CNF-034), rate-limit
   handling and the retry policy subset section 13 needs, plus the M1a deferrals `CFG-044`,
   `CFG-051…055`, `CFG-070/071`, `CFG-080/081`, `OVR-002/003/005/006` and `BV-CONFIG-009`.
   Design and exit record: [`decisions/0004-m1b-transport.md`](decisions/0004-m1b-transport.md).
   50 IDs left the baseline.

   **The inheritance clause did not hold, and that is the milestone's main lesson.** M1b was
   told it "may not quietly redesign the transport seam (OVR-001)" — but M1a had shipped
   *three* incompatible seams, Rust's with no method at all, so there was no single seam to
   inherit. The redesign was taken as an amendment (DR-0004 D-M1b-1), which is the process
   working; what failed was M1a's exit certifying a cross-language seam nobody had compared.
3. ~~**M1c — Error model (`ERR` + Appendix B).**~~ **Complete (2026-09-14).** Taxonomy,
   stable codes, hints, retryability, server-message recognition, hint enrichment and
   CNF-043. Design and exit record:
   [`decisions/0005-m1c-error-model.md`](decisions/0005-m1c-error-model.md). 19 IDs left
   the baseline.

   **The "generated, never hand-transcribed" instruction was the milestone's best
   decision, and it was nearly not taken.** M1a and M1b had each transcribed a slice of
   Appendix B by hand, three times over, and the natural move was to keep going. Instead
   `tools/error-catalogue` makes Appendix B executable: one parser, one intermediate,
   three emitters, and a CI gate that fails on a hand edit to generated source or a
   specification edit without regeneration. Every defect the milestone found afterwards
   was in *behaviour*, not in a string — which is the point.

   **The milestone's other lesson is that four gates were doing less than their record
   claimed.** Two were outright red on `main`, one was inert, one was too coarse to see a
   rename. None of that was visible from inside a milestone; all of it was visible the
   moment someone ran the gate instead of reading about it.

**R3 because** this milestone fixes the public API shape and the TLS/redirect security
posture. Architecture review by Claude Opus 5 before implementation; decision record required.

**Risk.** Getting the error taxonomy wrong here is the most expensive defect available in
this project — it is public API, it is cross-language, and it is load-bearing for every
engine binding. Budget explicit design time before any code.

### M2 — Authentication, Core methods

Token source model, login response contract, Token / Userpass / AppID, token store
operations, automatic renewal, and the section-05 security requirements. Design and rulings:
[`decisions/0006-m2-authentication.md`](decisions/0006-m2-authentication.md).

**Decomposed into three sub-slices (D-M2-1).** M2 was booked as Large / ~28 requirements;
its real content is **39** requirement IDs, a new public subsystem in three languages, two
net-new harness capabilities and the R-10 sweep. That does not fit a Large brief, and
**TOK-011** decomposes rather than raising a budget.

1. **M2a — token source, token store, harness instruments.** **Complete in .NET
   (2026-09-14), `v0.5.0`.** 14 IDs off the baseline; Rust and Python outstanding.
2. **M2b — login response contract, Userpass, AppID, security.** **Complete in .NET
   (2026-09-14), `v0.6.0`.** 19 IDs off the baseline, including the `CFG-020`/`ERR-022`
   preflight and the `AUT-002` lazy login, which shared a slice because all three turn on
   the same resolution-order rule. Design and exit record:
   [`decisions/0006-m2-authentication.md`](decisions/0006-m2-authentication.md) D-M2-25,
   D-M2-26. Rust and Python outstanding, deferred to M13 (D-6).
3. **M2c — automatic renewal, plus the R-10 gate re-proof sweep.** **Complete in .NET
   (2026-09-14), `v0.7.0`.** 6 IDs off the baseline (`AUT-090`…`AUT-095`), closing M2's
   full 39-ID removal (305→267). Design and exit record:
   [`decisions/0006-m2-authentication.md`](decisions/0006-m2-authentication.md) D-M2-27,
   D-M2-28. The R-10 sweep is recorded in
   [`decisions/0001-m0-harness-gate-proof.md`](decisions/0001-m0-harness-gate-proof.md)'s
   addendum; three findings it surfaced are tracked as risk-register rows R-11/R-12/R-13
   (§8), none blocking this exit. Rust and Python outstanding, deferred to M13 (D-6).

**M2 is now fully exited in .NET.** All three sub-slices (M2a/M2b/M2c) are complete.

**R3, non-negotiable checks:** CNF-031/032 (no secret material in logs, `ToString`, `Debug`
or `repr` at any level; redaction in the default string representation), TST-051 (tests
assert the captured log is clean), and auto-renew lifecycle correctness under concurrency.

**M2a's lesson is that the instruments were worth more than the requirements.** Two
capabilities the repository had been missing since M0 landed here: the fixture `clock`,
which the schema had defined and **no language read** — so `auth.token.lookup-self-remaining-ttl`
had never asserted anything — and the TST-051 capturing logger and observer, which had
**never been asserted in any language**. The logger immediately found a real leak: the
CFG-080 observer was receiving unredacted paths, and `auth/token/renew/{token}` puts a live
token in the path.

**The four design-review rounds and the one blocked handback were not overhead.** Every
block was a seam pinned on one side only — the executor-to-`TokenSource` direction, AUT-011
never reaching recognition from a 200, `CFG-020` dropped from the allocation, and
`Resolve()`'s concurrency voiding `CFG-070`'s thread-safety basis. Two of the four handback
defects were consequences of D-M2-9, a ruling in this very record, found by reading the
call path rather than running the suite. **A milestone that changes a seam reviews the
path, not only the results.**

**M2b's lesson is that a pin can be wrong, and the delegate that catches it should not be
the one that overrides it.** The .NET pathfinder deviated from two of D-M2-6's public-API
pins — `RequestOptions?` instead of `LoginOptions?` on the one-shot login methods (the
latter is unobservable on a call that never retains credentials to re-login with), and
`AuthInfo` dropping AUT-014's five optionals (D-M2-6 had specified they populate "after
`LookupSelf`", but nothing was ever specified to merge a `LookupSelf` result into an
`AuthInfo` — the pin could never have been satisfied by any implementation). Both
deviations were correct, and both were flagged as open questions for ratification rather
than decided unilaterally — exactly the R3 discipline `agents.md` §4.2/FAM-002 asks for:
public API shape is Claude's to decide, not a delegate's, even when the delegate is the one
that found the defect. Ratified at
[`decisions/0006-m2-authentication.md`](decisions/0006-m2-authentication.md) D-M2-26, which
also corrects a genuine mistake in this project's own D-M2-25 ruling — a code-whitelist
design for the token-source-failure marker that would have silently broken `AUT-003`'s
re-login/replay for a gated `AppId` login, caught by the R3 handback review before the
Rust/Python brief could inherit it.

**A real security defect was found and fixed in the same pass, unprompted by any
requirement ID.** A Userpass username containing `/` or `?` was sent as a different,
unauthenticated request path — path injection via unescaped path segments. `AUT-030` names
URL-path-encoding but no fixture exercised a hostile username, so this shipped invisibly
until the pathfinder read the code rather than the fixture, continuing this project's
pattern (M1b's Rust pooling, M1c's `409`/`503`, M2a's observer leak) of defects sitting
below the fixture seam.

**Verification, quoted:** `dotnet test` 533/533 passing, 98.9 % line / 96.02 % branch
(≥ 95 % floor held, no exclusion pragma); traceability 147/273/420, baseline delta exactly
the 19 IDs; error-catalogue regeneration byte-identical on `rust/`/`python/`; both untouched
along with `specifications/`. Independently re-run by the Strategic-tree R3 reviewer, not
merely quoted from the delegate.

### M3 / M4 — System API Core subset, then KV → **Core**

M3 is the smallest useful `sys` surface: health, seal-status, server info, cluster status,
capabilities. M4 is the whole of section 07 — KV v1, KV v2 versions, CAS, soft delete,
destroy, metadata.

**M3 is met. Complete in .NET (2026-09-15), `v0.8.0`.** `Client.Sys` ships `Health`, `SealStatus`, `ServerInfo`, `ClusterStatus`,
`CapabilitiesSelf`, `Capabilities.Can`/`Sys.CanAsync` — the nine IDs DR-0007 fixed off the
`~16` estimate. `HsmStatus` stays out of scope, booked to M7. The routing classification
itself is the notable part: DR-0007 found the contract already settled by existing
fixtures, the M1b Shape-B seam and the M2b sub-client precedent, so this landed as a
row-2 implementation pass rather than the row-3 pathfinder the roadmap's `Large`/R2 booking
might suggest. The mandatory R2 handback review (Opus) blocked once on a genuine
`specifications/` self-contradiction the drafting introduced (SYS-006's "MUST NOT raise"
text, worked out against SYS-001's actual pattern) — corrected in `06-system-api.md` and
recorded as DR-0007's addendum, not carried as a lingering caveat — plus two required
Engineering-tree fixes (a false `RateGateState.Paused` signal on a standby health probe,
and the spec-named `Sys.CanAsync` member, which the first pass had built only as
`Capabilities.Can`). Both closed in the same decision; nothing carries into M4.

**M4 is met. Complete in .NET (2026-09-15).** `Client.Kv` ships the version-explicit
`Kv.V1`/`Kv.V2` sub-clients, per-environment overrides, the `KV2-030` path helpers and the
`WriteIfAbsent`/`UpdateWithRetry`/`ReadField` conveniences — 25 of the 27 booked IDs, with
`KV-001` and `KV-010` deferred to M7 and M8 because `SYS-026` and `BAT-007` do not exist
yet and D-M1c-25 forbids guessing in their place. Routed as a **row-3 pathfinder**, unlike
M3: no decision record pinned a KV name, a third of the surface was unfixtured, and
KV2-009's prose argues with itself, so the contract was genuinely unsettled until
[DR-0009](decisions/0009-m4-kv-engine.md) settled it.

**M4 exit was supposed to be the first externally meaningful gate** — the README declaring
`Core`. It is not, and the reason is a roadmap defect rather than a KV shortfall: `Core`
requires sections 16 and 17, which are M11's. The README therefore declares no level and
lists the gaps by ID (CNF-002, CNF-041). What *is* true from this point is the substance:
the .NET SDK can read and write secrets and authenticate, which is what `Core` was chosen
to mean. Rust and Python reach the same point only at Stage 2's M13 pass over M2b–M4
(D-1, D-6).

**Three defects were found by reading the code rather than running the suite**, continuing
the project's pattern (M1b's Rust pooling, M1c's `409`/`503`, M2a's observer leak, M2b's
path injection). The transport's path builder never split the query off a pre-encoded path
— harmless only because login was the single pre-encoded caller, and KV v2 puts `?version=`
and `?env=` on exactly that path. The `..`-segment guard the pathfinder added covered
`path` and `prefix` but not `mount`, so `mount: "secret/../auth/token/lookup-self"` still
re-routed a request and `DataPath` propagated the traversal into a string KV2-030 exists to
hand to `Sys.Batch`. And `WriteSecret` validated `Env` but not the keys of `Envs`. All three
are fixed inside M4; the second was found by the mandatory R2 handback review, which is the
gate working as designed.

### M5 — Cluster discovery and resilience

SRV discovery, health probing, node ranking, sticky sessions, bounded failover, retry
interaction, TLS with discovery, diagnostics, operator fan-out.

**Dependency.** Consumes the retry primitives built in M1b; do not rebuild them.

**M5 is met. Complete in .NET (2026-09-15)**, in two sequential slices over one contract
([DR-0010](decisions/0010-m5-cluster-discovery-and-resilience.md)). **850 tests,
99.13 % line / 97.02 % branch.** All ten of Appendix C's `resilience.*` fixtures are green;
six of them did not exist and were authored here (218→224 on disk).

**The booking was wrong in three places, and grounding it before briefing is what found
them.** `RES-001`…`RES-004` had already left the baseline at M1b with the retry loop,
`IJitterSource` and `RequestOptions.TotalTimeout`. `RES-030`'s `*ClusterWide` variants wrap
`Sys.Seal`/`Sys.Unseal`, which are `SYS-012`/`SYS-013` and booked to **M7** — implementing a
cluster-wide wrapper around operations that do not exist would have meant inventing M7's
contract, and `RES-030` is a SHOULD, so the deferral breaks no MUST (D-M5-3, the
`KV-001`→M7 shape). Against that, `CFG-043` — §02's twin of `RES-010`, the same code path
and the same test — was baselined with **no owner**, and M5 implements it either way, so it
is booked in (D-M5-16a). Net: **29 landed, not 33**, and the baseline is at **205**.

**The milestone's load-bearing decision was scoping `DSC-041`, not writing the failover.**
Read naively, "a transport-level failure … is a node failure → `BV-DISCOVERY-003`" would
have changed the observed error code of every transport failure in the SDK. D-M5-5 rules it
in two limbs: transport failures reclassify **only in discovery mode** and only for the
three kinds `DSC-041` names, so literal mode stays byte-identical to M1b; and a matching
`5xx` **never** has its code replaced, in either mode, because §13:125 presupposes
`BV-SERVER-003` surviving, `CFG-053` makes a sealed 503 never-retryable, and nine landed
fixtures assert those codes. Twelve landed fixtures would otherwise have needed amending —
the fixture corpus is the evidence, and it reads one way only.

**Two handback reviews blocked, and both blocks were correct.** M5a's was blocked on
`DSC-022`: ruled surfacing-only, because `DSC-033`'s rank list is closed and explicitly
deterministic, so adding a fifth sort key would diverge from any Stage 2 implementation
reading `DSC-033` literally (D-M5-20; preferring healthy nodes is now risk **R-17**). M5b's
was blocked on a **proven `RES-001` breach**: an idempotent read at `MaxAttempts = 3`
produced six wire attempts against a cap of four, because the failover replay restarted with
a fresh retry budget. The cause was a conflict between two decisions of DR-0010 itself —
D-M5-7's cap and D-M5-6's "the replay retries normally" are jointly satisfiable only when
the first pass ends at its first attempt, which is the one shape every failover test happened
to script. Corrected by bounding the replay to `max(1, MaxAttempts + 1 - AttemptsBefore)`,
failover-only (D-M5-28). The alternative — let a late node failure forfeit failover — would
have satisfied `RES-001` by violating `DSC-042`, whose MUST is conditioned only on arming
and idempotency. Both fixes are proven by seeded violation and revert, the R-10 pattern.

**One `specifications/` change, authorised and R3.** `resilience.failover.read-once`
returned KV v2 metadata as `{"version": 1}`, which section 07's type block forbids and
D-M4-12 maps to `BV-PROTOCOL-002`. The fixture predates M4 and contradicted the section it
exercises, so it is repaired rather than the ruling relaxed (D-M5-26) — relaxing D-M4-12
would have been CLA-004's "weaken a gate to make something pass". Its §5.3 human
confirmation is carried to the release-checklist line below. A second instance of the same
defect class was then found in a fixture authored *inside* this milestone (D-M5-30), which
is why the corpus sweep is recorded as risk **R-19**.

**Deliberately not shipped: an `ISrvResolver`.** The .NET BCL exposes no DNS SRV API and
`DSC-014` requires the resolver be injectable, not shipped, so `DSC-010` is covered by the
seam — but an application that supplies none silently gets literal behaviour where it asked
for discovery. Risk **R-16**, owned by M11 (documentation) and M12 (the live suite cannot
reach a cluster without one). Adding a DNS dependency is an R2 call with a supply-chain
dimension and belongs to those milestones.

**M5 exit criteria, all met:** ten of ten `resilience.*` fixtures green; the 192 previously
landed fixtures green with **none amended** except the one authorised repair; 29 IDs off the
baseline; coverage above the 95 % floor on both axes; no conformance level claimed or
affected (sections 16–17 are still M11's — R-14).

**Release-checklist line carried forward (D-M5-26, `agents.md` §5.3).** Before the M12
release is cut, the project owner must confirm the `resilience.failover.read-once` repair:
it is a `specifications/` change, which CRS-004 makes R3, and §5.3's R3 gate ends in human
confirmation before release. Rust and Python inherit the corrected body at Stage 2.

### M6 / M7 — Auth and Sys to completion

FerroGate machine identity, Certificate (mTLS), OIDC/SAML (browser-mediated), FIDO2; then
init/seal/unseal, mounts, auth-method administration, policies plus dry-run, namespaces,
audit, dashboard, backup/restore/export/import.

**R3** — unseal keys and init responses are the most sensitive payloads in the API.

### M8 — Transit, TOTP and request-efficiency bindings ✅

Bindings for the Transit routes — keys, encrypt/decrypt, rewrap, sign/verify, HMAC, random,
hash, datakeys — and for TOTP; plus all of section 14: the client rate gate, the batch
endpoint, `*-info` cursor pagination and `sys/cache/version` coherence epochs. **No
cryptography is performed in the SDK**; Transit is a set of HTTP routes (see §2's definition
of "engine"). Section 14 mandates its own documentation guidance (14 § "Guidance the SDK
documentation MUST include"), so that slice of M11 was pulled forward into this milestone and
is in `dotnet/README.md`.

Ran as five slices, because 39 IDs exceeds one Large brief (**TOK-011**): **a** the R-23
recognition-qualifier fix, **b** Transit, **c** TOTP, **d** the rate gate + `Sys.Batch` +
`Kv.ReadMany`, **e** pagination + cache coherence + the guidance. Rulings:
[DR-0013](decisions/0013-m8-transit-totp-and-efficiency.md), D-M8-1…D-M8-52.

**Exit, as met:** all 39 of the milestone's IDs landed — 38 across the five slices, and
`CCH-006` after the close-out when the project owner directed that the declined `MAY` be built
after all (D-M8-53…D-M8-56).

**No conformance level is declared, and the booked gate was unsatisfiable as written.** §4
booked M8's exit as "declare `Standard`"; `CNF-002` forbids claiming a level whose sections
carry unimplemented MUSTs, and sections 16–17 are M11's (**R-14**). M8 therefore exits on
requirement content and updates `dotnet/README.md`'s `CNF-002` gap list instead, exactly as M4
did (D-M8-6, DR-0009 D-M4-3). Resequencing remains §10 question 4 — a project-owner decision
this milestone did not take.

**What review cost, and what it bought.** Every slice was gated in the Strategic tree and
**not one passed first time.** The gates found: a false unreachability claim in an R3 decision
record, falsified by a passing test in a sibling slice (D-M8-22, D-M8-27); a rate gate that let
exactly one request out *into* a server ban window (D-M8-43); a guessed public API shape that
would have silently returned `0` (D-M8-47); an unverified cross-language claim in shipped source
(D-M8-35); and an unbounded paging loop (D-M8-50). **The pattern worth carrying to M9: five
times, an author was right about what to do and wrong about why — and every one was caught by
checking the artefact rather than the sentence describing it.**

### M9 — PKI, SSH and the SSH broker ✅

Typed bindings for the PKI routes — CA management, roles, issue/sign, revoke, CRL, bulk
listings, managed keys, tidy, ACME config, and both queues — and for the SSH routes — CA,
roles, signing, OTP, brokering policy. **No certificate is signed, and no key is generated,
inside the SDK**: `Pki.Issue` posts to the server's issue route and returns the certificate
the server minted (D-M9-1).

Four slices, dispatched serially because all four regenerate one `PublicApiSurface.txt`.

**What the gates found, and why this milestone is worth reading before M10 starts.** The
framing record was **blocked three times** before acceptance, and **every slice was blocked
or returned at least once**. Almost every finding came from reading an artefact against a
document rather than from reading the code's own account of itself:

- **Twice, secret material reached a container that does not redact.** `Pki.ExportCertificate`
  returned an untyped map although its own `includePrivateKey` and `mode=backup` parameters
  guarantee a private key — while `PKI-002` was coming off the baseline in the same diff, and
  its `PKI-002`-tagged test drove exactly that path and asserted only non-null. **The repair
  then introduced a worse leak than it closed**: the GET form it added put the export password
  in a query string, which `ErrorPaths.Redact` does not cover (**R-33**), so it reached the
  request observer, the exception path and hint enrichment. Both export routes are POST-only
  as a result (D-M9-16, D-M9-19, D-M9-22).
- **A privilege-escalation path in an accepted design.** `SignRequests.Approve` splatted the
  caller's untyped `overrides` map beside the named `role` field, and `Utf8JsonWriter` does not
  reject duplicate property names — so `overrides["role"]` emitted two `role` keys and every
  mainstream decoder takes the last, on the route that authorises an issuance (D-M9-24).
- **A wire-format defect found in the specification's punctuation.** Three durations went out
  as integer seconds where their endpoints declare Go-style strings; §09 **quotes** exactly
  those three defaults and leaves `not_before_duration` (30) unquoted, and two of the three are
  the mount `config` case `TRN-031` names. The read half accepted only JSON numbers, so a
  read-modify-write of `config/crl` would have silently reverted the CRL expiry.
- **A false explanation of a coverage figure.** Slice d attributed a drop to a uniform coverlet
  async-state-machine artefact; the file it blamed contains no `async` method at all, and all
  the misses were its own untested arms. The margin was **2 branch outcomes**; it is now 31.
- **A requirement tag that asserts something else, in all four slices** — found by four
  separate gates, now the standing rule **D-M9-30**. `traceability.py` machine-reads those
  tags, so a mis-tag feeds the gate a false positive.

**Two of the milestone's own rulings were wrong and were reversed by the record itself.** The
`*-info` prefix question was decided, twice, against Appendix A's legend at `:3-5` — which
defines `v1` as "uses `ApiPrefix`, do not pin" rather than "lives at `/v1`" — and against
R-27 and D-M8-45, which had already settled it at M8. A defect was booked against
`Sys.ListNamespacesInfo`, which is correct as shipped; the booking was withdrawn (D-M9-7,
D-M9-13). **Both errors had one cause: a column read without its legend.**

**Exit:** sections 09–10 bound, seven fixtures green, corpus 253, 10 of 11 booked IDs off the
baseline plus `TRN-031`. **No conformance level declared** — section 09 still carries
`PKI-030` (**R-31**), and sections 16–17 are M11's (**R-14**).

### M10 — the remaining engine bindings and identity → **Complete**

Identity, asset groups, Resources, Files, LDAP, cert lifecycle, notifications, Rustion.

**M10 inherits four rows from M9, and three of them need input this repository does not
hold** — the server's queue-cap message (**R-31**), the PKI queue response shapes and the
`exportable` read-route question (**R-32**), and `Pki.GenerateIntermediate`'s `issuer_name`.
Those belong in M10's planning input, not its dispatch: a capture or the server source is a
dependency outside the SDK's control. **R-33** gates any return of the two export routes'
GET forms.

**Inherited from M8, and neither is optional bookkeeping.** M9 takes
`efficiency.pagination.zip-mismatch-protocol-error`, which sits on disk **pending** because it
drives `Pki.ListCertificatesInfo` — an area M8 could not build, so re-pointing the fixture
would have been a `FIX-012` specification change rather than a test fix (D-M8-48). It goes
green with the PKI bindings. M10 takes the **`Auth.Userpass.ListUsersInfo` naming** question (**R-29**, D-M8-46).
`CCH-006`, which M8 had handed it, is no longer M10's — it was implemented after the close-out. Both M9 and M10 reuse M8's `PagingWire` machinery for the five
`*-info` endpoints M8 left unwired, rather than re-implementing cursor paging (D-M8-7).

These are written into the milestone rows and here, not only into
[DR-0013](decisions/0013-m8-transit-totp-and-efficiency.md), because **R-16 is this roadmap's
own evidence that a gap recorded somewhere a briefer will not look survives a milestone** — it
was raised at M5 and reached M8 untouched.

**M10 exit:** `Complete` declared in the .NET README — CNF-003 satisfied for .NET, **subject to
R-14**, which as sequenced forbids the claim. This is **Stage 1's conformance target**; Rust and
Python reach `Complete` only inside M13.

**M10 is complete, all five slices, released as `0.15.0`…`0.18.0` (`v0.19.0` for slice e).**
`Client.Identity`'s kernel (`IDN-001`, `IDN-002`), `Client.AssetGroups`, `Client.Resources`
and `Client.Files` (`RSC-001`, `RSC-002`, `FIL-001`), `Client.Ldap` (`LDP-001`),
`Client.CertLifecycle`, `Client.Notifications`, `Auth.Userpass.Admin.*` (R-29 closed,
D-M10-3) and `Client.Rustion` (`RUS-001`, `RUS-002`, `RUS-003`) are all in .NET. **All nine
booked IDs landed** — the first milestone since M7 to land its full booked count with no ID
held back, unlike M8's +1-beyond-booked, M9's `PKI-030` held back, or M7's own +3. Section 12
clears entirely: every requirement ID it defines is off the baseline. **1652 .NET tests,
99.17 % line / 95.06 % branch**; traceability **321 covered / 110 baselined** of 431; **253
fixtures on disk**, unchanged — no slice added one, since none of M10's mounts carry an
Appendix C mandatory fixture.

**Every slice was gated, and four of five needed at least one repair round — see the pattern,
not just the count.** Slice b was blocked twice: a raw `FormatException` escaping the error
model, an unvalidated caller-supplied JSON body, and a secrets-read shape that wrapped the
whole response envelope instead of a per-field map — whose own first fix then introduced a
second defect (a JSON-quoted, unescaped secret value), caught only because the follow-up
review re-verified the fix rather than trusting the worker's account of it (**CCF-002**, the
rule this milestone is the clearest evidence for yet). Slice c was blocked twice: an
undocumented optionality relaxation, an invented recognition code on a surface with no
requirement ID behind it, and a stale doc comment its own fix left behind. Slice d needed no
code fix at all — the one blocking condition was pure record-keeping, `CHANGELOG.md` and
`ROADMAP.md` both silent on a shipped breaking rename. Slice e was blocked once, on two test
adequacy defects rather than a production one: a test that passed by exercising the wrong
request, and eleven public members with no route assertion at all — both in the exact code
the slice's own coverage number said was fine. **No slice's blocking finding was found by its
own author**; every one came from the handback gate, the same result this milestone's
predecessors already established and this one repeats rather than contradicts.

**One decision record citation was found wrong by the milestone it was written for.** DR-0017's
own framing (written before any slice was dispatched) claimed `RUS-002`'s node-local exclusion
mechanism was "reused by `sshbroker` at M9." Slice e's handback checked this by grepping the
actual M9 code and found it false: `SshBrokerOperations.cs` never sets `nodeLocal: true`
anywhere; the mechanism's only real precedent is `Sys.Seal`/`Sys.Unseal` (M3). Corrected in
DR-0017 as an addendum rather than a new revision (REC-007). The lesson is not new — R-27 and
R-23 are both a citation trusted instead of checked — but this is the first time in this
project's history that the citation was the *Strategic* tree's own, in its own framing record,
not a delegate's.

**Slices b, c and d ran concurrently, at the project owner's direction, deviating from
D-M10-1's serial-dispatch default** — slice c in an isolated git worktree, slice b and d
directly in the main tree since neither touched the other's files beyond the always-regenerated
`PublicApiSurface.txt`. The reconciliation cost was real but bounded: one hand-merged pair of
additive `BastionVaultClient.cs` property blocks (no true conflict, same insertion point), one
full regeneration each of `PublicApiSurface.txt` and the traceability baseline post-merge, and
one hand-reconciled `dotnet/README.md` gap table. Landed in order a→b→d→c→e; tagged and pushed
as `0.16.0`, `0.17.0`, `0.18.0` in that same order, `0.19.0` for slice e's close.

**Parity is owed, not forgiven.** `rust/` and `python/` are untouched through all five slices
(D-1/D-6, Stage 1), and R-29's rename is cheap *only* because nothing here has published to a
package registry — the closure recorded against it holds only until Stage 2's first
publication, which is exactly the dependency the parity pass at M13 must carry forward rather
than rediscover.

### M11 — Documentation and usage guides

Section 16 requirements plus the guides of section 17, for .NET (Stage 1). Every sample
compiles and runs in CI (CNF-026); every public symbol carries a doc comment. Rust and
Python guides are authored in M13 once each language's surface is implemented, not adapted
speculatively ahead of it.

M11 also carries **R-26**: the documentation MUST state that default SRV resolution cannot see
macOS scoped resolvers, so on a split-horizon VPN the resolver queries the wrong nameserver and
`DSC-017`'s strict default refuses rather than degrades, and MUST document the `DSC-050`
nameserver override as the remedy. This replaces R-16's original M11 obligation — "a resolver is
required for real discovery" — which stops being true once one ships.

**Met, with one held-back requirement and one declaration blocked — closed 2026-09-23.**
**Corrected 2026-09-23 (D-M11-27): the corpus was 481, not 473.** M11's exit claim was
measured over a set that `tools/doc-worksheet/apisurface.py` built by keeping only types whose
name `endswith("Operations")` — so all eight public methods declared on `BastionVaultClient`
itself (`ClearToken`, `ConnectAsync`, `DiscoverAsync`, `Dispose`, `ReconnectAsync`,
`ServerVersionAsync`, `SetToken`, `WithNamespace`) were discarded at parse time, before the
gate ever ran. Found when `TRN-081` added a ninth and the count stayed at 473. **Seven of the eight
carried no `<spec>` tag at all** — only `ServerVersionAsync` did, and the gate never read
it — so the blind spot was not merely a miscount: seven public operations were missing the
`DOC-006` tag the milestone declared universal, and all seven were written on 2026-09-23 as
part of this correction. "473 of 473" was a
gate reporting completeness over a corpus that structurally could not contain the client's own
entry points, which is this project's recurring "gate that does not bind" defect
(D-M1b-19, D-M12-16, D-M12-19). The scanner now reports **481 of 481**, all tagged.

All sixteen slices landed. **481 of 481 public .NET operations** carry `DOC-005` comments
and `DOC-006` tags; all thirteen documents exist under `docs/dotnet/` plus D1 and the
generated D11; the six gates `DOC-020`…`DOC-025` and `DOC-031` are wired and **each was
demonstrated failing on a seeded violation** before it landed (D-M11-5). `DocsSamples` is
now executed in CI, not merely compiled — a gap open since slice a. R-26's macOS
scoped-resolver caveat and the `DSC-050` remedy are documented in D8, discharging the
obligation above. 1673 .NET tests, 110 DocsSamples, coverage 99.18 % line / 95.02 % branch;
traceability covered 321 → 323, baseline 110 → 108.

**`DOC-030` is held back**, not missed: it names a publication pipeline that has never
published, so it stays baselined with a named owner rather than being claimed against
infrastructure that does not exist (D-M11-8) — the control M9 used for `PKI-030`.

**No conformance level is declared, for the fourth milestone running, and this time the
reason is new.** M4, M8 and M10 were blocked by R-14's scheduling deadlock, which the
project owner answered on 2026-09-23 (§10 question 4). M11 is blocked instead by **M12's
unmet dependency**: DR-0019 D-M12-4 books the `CNF-001`-vs-`CNF-014` audit to M12 and it
does not exist, leaving 33 baselined `Core`-section IDs unclassified. Declaring over an
unperformed audit would be a claim resting on nothing (D-M11-25). The declaration is booked
to whichever milestone first has D-M12-4 in hand.

**What the milestone measured, beyond what it delivered.** 263 of 481 tagged operations
cite a specification *section* because no requirement governs them, and **43 more were found
citing an ID that did not govern them** and converted (D-M11-23, D-M11-26). Every one of the
43 would have passed a gate checking only that the ID exists — and the shipped `DOC-006`
gate still cannot verify governance, which it states rather than implies. The unspecified
surface is now risk **R-39**, owned by **M14**.

**Three re-plans, each from a measurement rather than an estimate.** One slice became
seventeen (D-M11-1), then slice f's three became twelve once the rate was measured at ~40
operations per slice (D-M11-20), then the remaining pass was cut into ten from the worksheet
rather than estimated (D-M11-22), and slice g split in two when it outgrew its tier
(D-M11-25). The recurring lesson is that the unexamined estimate, not the hard work, was
what kept breaking.

### M12 — Live-server integration suite and 1.0.0-dotnet, closing Stage 1

The 16 `ITG` requirements and the `ITG-S<nn>` scenarios of 15 § Required scenarios, against
the server versions in [`test-matrix.json`](specifications/test-matrix.json), with the
provisioning, isolation and cleanup rules of 15, **run against .NET only**. Then the
Stage-1 exit checklist: every .NET gate green, coverage stated, traceability report clean
for the .NET-applicable requirement set, changelog, README conformance statement.

**M12 does not cut the shared `1.0.0` tag.** CLA-003 and the versioning rule at the top of
`CHANGELOG.md` still require all three packages to match before a release is called
`1.0.0`; M12 closes Stage 1 and hands the project to Stage 2 (M13). If an interim tag is
wanted for the .NET-only milestone, it follows the `0.5.0` precedent — an explicit,
recorded exception (D-M2-15), never a silent redefinition of what `1.0.0` means.

**R-16 no longer constrains M12.** The live suite previously could not reach a cluster without
an application-supplied resolver; [DR-0014](decisions/0014-r16-srv-resolver-and-silent-discovery-degradation.md)
ships one, so cluster scenarios need no fixture substitute. M12's R3 human-confirmation gate is
unchanged, and it still carries D-M5-26's `resilience.failover.read-once` confirmation — that is a
separate R3 carry-forward which DR-0014 does not touch, so "R-16 no longer constrains M12" must
not be read as M12's gate having loosened. It has not.

**Exit status, 2026-09-23 — complete on content, held at the R3 gate.** Against DR-0019's
eight acceptance criteria, re-established by measurement rather than by argument (D-M12-25):

| # | Criterion | Status |
|---|-----------|--------|
| 1 | Harness in all three states | **Met.** Managed, external and the `ITG-003` skip all exercised on one commit; the skip reason `no BastionVault test server available` was **observed**, not asserted |
| 2 | All 32 scenarios exist, each passing or `ITG-031`-skipping | **Unmet for one scenario, and correctly so.** 31 of 32 pass; `ITG-S26` fails on two server-side gaps with no SDK limb (**R-41**). An `ITG-031` skip would claim it could not run here, which is false — it ran, and the server failed it |
| 3 | `ITG-020`…`ITG-023` hold over a full run | Met — enforced per scenario since D-M12-16, proven against seeded violations |
| 4 | `ITG-030`…`ITG-032`, or slice 7 outstanding with its blocker named | **Met by the second limb.** Blocker re-measured, not assumed: `ghcr.io` returns `DENIED` to an anonymous token and the Docker daemon is down |
| 5 | D-M12-4 audit classifies every baselined `Core` ID | Met, and the audit corrected its own subject set 33 → 54 ([DR-0022](decisions/0022-m12-core-conformance-audit.md)) |
| 6 | Unit suite green, coverage above floor, say how `CNF-011` was handled | Met — 1690 green, 99.19 / 95.03. `CNF-011` is satisfied **by construction**: `.github/workflows/dotnet.yml:52` collects coverage from the unit project only, and the integration project appears in no workflow |
| 7 | `PublicApiSurface.txt` unchanged by every slice | **Met in intent, not in letter.** The file gained one member from `TRN-081` and two property types widened (`Crl.CrlNumber`, `CertificateSummary.IsOrphaned`) — all three required by landed specification amendments, none by a slice. `Source`'s widening does not appear in the file because the scanner carries no nullable-reference annotations; that is the instrument's limit, not an absence of change |
| 8 | `CHANGELOG.md` and `ROADMAP.md` updated on acceptance | Met — this revision |

**One criterion is not cleanly met, and it is not papered over.** `ITG-S26` is red because
the server fails it. The alternative — reclassifying a server defect as an environmental skip
— is how a suite stops being able to tell you anything, and it is the same reasoning that
rejected asserting the defect as the expected outcome at slice 5 (`CLA-004`, D-0021-1).

**M12 exits with four things outstanding**, each with a named owner: slice 7's `ghcr.io`
credential, `DOC-030`'s first publication, `ITG-S26`'s upstream server defect (**R-41**), and
the R3 human gate itself — which now carries **three specification changes** for the project
owner's confirmation (the `pki/*` and `auth/token/create` duration amendments, `crl_number`'s
optionality, and the `certs-info` optionality landed under Ruling 1's principle and flagged as
such), alongside D-M5-26's `resilience.failover.read-once` carry-forward.

**Human confirmation required before any tag** (R3 rule, `agents.md` §5.3).

### M13 — Stage 2: Rust and Python parity, M2a through M12, then the shared 1.0.0

**Entry gate:** D-6 — all of M2b…M12 exited in .NET first. Nothing here starts early.

**Shape.** M13 is tracked as one milestone rather than re-split into M2b′…M12′ because its
unit of work is no longer "design, then implement" (the design is already settled) — it is
"transcribe and verify against an already-reviewed reference," which is the row-2 shape
`agents.md` §4.2 describes, run once per source milestone's requirement-ID block — M2a included, since Rust and Python never got that pass either — in Rust
and Python **in parallel with each other** (not with .NET — D-2 amended). Each source
milestone's block is its own exit criterion inside M13:

- Same requirement IDs as the .NET milestone it mirrors.
- Same fixtures, run to green, not a re-derived assertion (D-5).
- The same public-surface diff this file has required since M1b (R-9), now run three ways
  for the first time since M2a.
- The parity exception carried in D-M2-15 / D-M2-18 (the `catch` ordering in `BV-AUTH-017`,
  named there as the sharpest known trap) is checked explicitly, not assumed closed by
  "the tests pass."

**Risk.** M13 carries the deferred cost of D-1's supersession: eleven milestones' worth of
.NET-only implicit decisions, not one, land on Rust and Python at once, with no same-cycle
.NET review to catch a gap before two languages inherit it. Budget the review pass
accordingly — this is not a mechanical port (§7, D-2).

**Exit and close:** every Stage-1 gate re-met in Rust and Python, the three-way parity check
green, `CHANGELOG.md` records the parity pass per source milestone, `ROADMAP.md` §2 and §4
reflect `Complete` in all three languages, and the shared `1.0.0` tag is cut per the release
checklist — with human confirmation before the tag (R3, `agents.md` §5.3).

## 6. Dependency graph

```
STAGE 1 — .NET only
M0 ✅ ▶ M1a ✅ ▶ M1b ✅ ▶ M1c ✅ ─┬──▶ M2a 🔶 ▶ M2b ✅ ▶ M2c ✅ ──▶ M3 ✅ ──▶ M4 ✅ ═══ CORE ⛔ (R-14)
                             │                   │
                             │                   ├──▶ M5 ──┐
                             │                   ├──▶ M6 ──┤
                             │                   └──▶ M7 ──┤
                             │                             ├──▶ M8 ✅ ═══ STANDARD ⛔ (R-14)
                             │                             │
                             └─────────────────────────────┴──▶ M9 ──▶ M10 ═══ COMPLETE ⛔ (R-14)
                                                                        │
                                                              M11 ──────┴──▶ M12 ═══ STAGE 1 EXIT
                                                                                      │
                                                                                      ▼
                                                              STAGE 2 — Rust + Python parity
                                                                        M13 (M2a…M12, parallel
                                                                        Rust ∥ Python per block)
                                                                                      │
                                                                                      ▼
                                                                              1.0.0 (all three)
```

**Serial by necessity within Stage 1:** M0 → M1a → M1b → M1c → M2a → M2b → M2c → M3 → M4 →
… → M12, entirely in .NET.
**⛔ The three level markers above are blocked, not reached.** `CORE`, `STANDARD` and
`COMPLETE` each require sections 16–17, which are M11's, so no declaration is legal until
M11 lands. M4 exited with the KV work done and the level undeclared. The graph keeps the
markers where the milestones are so the discrepancy stays visible (R-14, §10 question 4).
**🔶 M2a is .NET only, and — since D-1's supersession — stays that way for the whole of
Stage 1.** Its Rust and Python pass no longer blocks M2b; it is folded into M13 (D-6) and
does not start until M12 exits.
**Parallel within Stage 1, after M4:** M5, M6 and M7 are independent of one another; M9 and
M10 depend only on M1 and M4. M11 can start as soon as the .NET surface it documents is
frozen — per section, not as one block at the end.
**M13 (Stage 2) is serial after M12, but parallel inside itself:** Rust and Python run
against each other, not against a further .NET pass (D-2 amended), and per-source-milestone
blocks inside M13 follow the same M2a→…→M12 dependency order .NET already proved.

**Hard constraint:** at most 10 concurrent agents system-wide (`agents.md` §7.2). During
Stage 1 this is far less binding than under horizontal slicing — one language at a time
does not saturate the ceiling the way three did. During M13, two languages × up to several
parallel source-milestone blocks can approach it again; run at most two M13 blocks
concurrently per the same rule.

## 7. Delegation shape

Per `claude.md` §1.1 and §5, implementation does not start with Claude. The repeating unit
of work is:

| Step | Owner | Output |
|------|-------|--------|
| 1. Frame and ground | Claude Sonnet 5 | Milestone objective, exact requirement ID list, risk tier |
| 2. Design | Claude Sonnet 5 (Claude Opus 5 for R3) | Decision record: API shape, type names per the 00 mapping rules, parity contract |
| 3. Brief | Claude | One four-section brief (TOK-004) per language, budget per tier (TOK-011) |
| 4. Implement | Codex Engineering Orchestrator | Stage 1 (M2b–M12): .NET only. Stage 2 (M13): Rust and Python in parallel with each other, against .NET's already-reviewed behaviour (D-1, D-2 amended) |
| 5. Review | Claude | Verdict per `claude.md` §3.1, findings tied to file, line and requirement ID |
| 6. Parity check | Claude | Same fixtures, three suites, identical assertions |
| 7. Accept | Claude (Strategic Orchestrator for R3) | Milestone closed, README gap list updated, `CHANGELOG.md` entry written, this file updated (`agents.md` §11) |

Briefs cite requirement IDs and `file:line` ranges — never spec prose (TOK-005, TOK-006).
A milestone that cannot fit its tier budget is decomposed, not granted a bigger budget.

**Brief rule added after M1a: pin every public name, not only the behaviour.** Three of
M1a's five review findings were the same shape — three agents independently invented three
names for one spec concept (`Details.setting`, `RateGate`'s fields), or one agent omitted a
public setter the other two provided. A brief that fixes the error *code* for every path but
leaves the developer-facing *string* and the *member names* to the implementer will get three
defensible, mutually incompatible answers. This costs one line per member in the brief and is
the cheapest defect prevention available; it matters most in **M1c**, where Appendix B is a
30 KB table of codes, messages and hints that all three languages must expose identically.

**What the M2a pathfinder pass bought (D-2 evidence, second data point).** The .NET slice
surfaced three defects itself — an unredacted observer path, a cancelled resolve escaping
uncoded, and a test reading the developer's real `~/.vault-token` — and the handback gate
found four more, including a faulted single-flight that bricked the client permanently and
an `AUT-085` refinement firing on any unmapped 4xx on the renew path. **All seven were in
code Rust and Python would have transcribed verbatim**, and two were consequences of a
ruling in the milestone's own decision record. D-2 is now supported by two milestones rather
than one; the extra serialisation step keeps paying.

**What the M1a pathfinder pass actually bought (D-2 evidence).** The .NET slice surfaced one
scope error in the design (`RateGate`/`AutoRenew` omitted from the settings table, which would
have made `CFG-001` a false positive on the ratchet) and two under-specified boundaries, all
corrected in the decision record *before* Rust and Python started. Both defects found in .NET
review were written into the Rust and Python briefs as behaviour-to-avoid, and **neither
recurred**. The extra serialisation step is paid back; D-2 stands.

## 8. Risk register

| # | Risk | Tier | Mitigation |
|---|------|------|------------|
| R-1 | ~~Error taxonomy (M1c) is wrong; it is public API and cross-language~~ **Retired at M1c.** The taxonomy is generated from Appendix B and regeneration is a CI gate, so the catalogue cannot drift from the specification or between languages. What remains is behavioural, and is covered by R-9 | — | Closed — [DR-0005](decisions/0005-m1c-error-model.md) D-M1c-1 |
| R-2 | 95 % branch coverage (CNF-010) is expensive on error paths, which CNF-011 explicitly puts in scope | R2 | Write the failure-path fixture with the feature (TST-013); never weaken the floor (CLA-004) |
| R-3 | Rust branch coverage may be unavailable on the toolchain | R1 | 15 § Coverage permits line and region ≥ 95 as the documented substitute — record the substitution once |
| R-4 | Three languages drift silently | R2 | Shared fixtures loaded from the repo (D-5, TST-010); parity is a milestone exit criterion |
| R-5 | Secret material leaks into logs or `Debug`/`repr` | R3 | CNF-031/032 asserted by capturing-logger tests (TST-051) in every auth and KV suite. M2b extended the assertion to the Userpass/AppID login paths and found a real path-injection defect (not a leak) while doing so — see M2b's milestone-detail note |
| R-6 | No live BastionVault server available. **Re-tiered R3 and pulled forward to M1 by DR-0001 D-M0-7** — FIX-010 requires fixture response bodies to be captured from a real server exchange, and 66 of Appendix C's ~140 mandatory fixtures are unwritten, so fixture authoring is blocked from M1 rather than M12 | R3 | Integration tests are skippable per run but mandatory in the CI matrix. **Provisioning a server matching `specifications/test-matrix.json` is now an M1 entry condition, not an M12 one** — escalated to the project owner at M0 exit (§10 question 3). **Discharged as a provisioning problem and materialised as a correctness one, 2026-09-23.** A supported server (`bvault` **0.44.5** > the 0.42.0 matrix minimum) is installed, so managed-mode runs are real; `ITG-030`'s CI limb still needs 0.42.0 from a registry returning `denied`, so slice 7 is held. **The row's real content was never "we lack a binary" — it was "nothing has ever checked our assumptions", and the first twelve scenarios found seven divergences** ([DR-0021](decisions/0021-live-server-findings.md)). **Status at M12's gate, 2026-09-23:** the suite now runs 67 tests against the real server — 57 pass, 5 fail on recorded divergences, 5 skip — so the row's real content has been fully discharged in the sense that mattered: the assumptions have now been checked. What remains is not a provisioning gap but a **registry** one: `ghcr.io/ffquintella/bastionvault` returns `DENIED` to an anonymous pull token, the only authenticated `gh` host here is `github.fgv.br`, and no container runtime is installed, so `ITG-030`'s per-version matrix (which D-M12-15 Ruling A puts entirely on the container path) cannot run. **Closed on M12's content, 2026-09-23**, with the registry gap re-booked where it belongs. The suite now runs 67 tests against the real server in **both** managed and external mode (59 pass / 2 fail / 6 skip and 59 / 1 / 7), so the row's real content is fully discharged: the assumptions have been checked, and the checking found fourteen divergences no fixture corpus could have. What remains is not this row's — `ITG-030`'s container matrix is blocked on one `docker login ghcr.io` with a `read:packages` PAT and is tracked as slice 7 outstanding under acceptance criterion 4, not as a live-server risk |
| R-7 | ~~M1 is 105 requirements — too large to review as one unit~~ **Retired at M1c.** All three sub-slices exited independently; the split did what it was for | — | Closed |
| R-9a | **A public name that reads the same and behaves differently.** M2a found the worst instance yet: `Clock.now()` returned wall-clock in .NET and Python and a monotonic `Instant` in Rust, so `AUT-014`'s `RemainingTtl` was not merely untested on Rust but **uncomputable** — invisible to fixtures, coverage and traceability alike, and to the public-surface diff, which sees the name and not the contract | **R2** | Renamed to `NowUtc`/`now_utc`/`now_utc` in all three (D-M2-2). The general control: when a member's *kind* is ambiguous, the kind goes in the name. Watch for the same shape wherever two languages agree and the third is idiomatic |
| R-9 | **Cross-language drift that no gate can see.** Fixtures pin wire behaviour, coverage pins executed lines, traceability pins requirement IDs. None of the three sees a differing public *name*, a differing developer-facing *string*, or a *capability present in two SDKs and absent in the third* — M1a shipped all three of those defects at 98–100 % coverage with every gate green, and `CFG-050` was legitimately "covered" the whole time Rust could not set `InitialBackoff` | **R2** | Three controls now. From M1b: the brief pins every public member name (§7), and milestone exit includes an explicit **public-surface diff across the three languages**. Added at M1c: **a deferred branch returns the specification's answer, never a plausible guess** (D-M1c-25) — M1c found three divergences that were all plausible guesses on paths no fixture reaches, one of which silently suppressed a permitted retry. Caveat on the second control: Python's `CNF-027` baseline is names-only, so the three-way diff is member-level for .NET and Rust and name-level for Python until D-M1c-22 is done. **M2b shows the control working one layer up:** a pin itself (D-M2-6's `LoginOptions?` on one-shot logins, and its `AuthInfo` AUT-014 optionals) was wrong, and a wrong code-whitelist design in this project's own D-M2-25 ruling would have silently broken `AUT-003` for a gated login — both caught by the R3 handback review **before** the Rust/Python brief could inherit them (D-M2-26). Stage 1's single-lane structure means this catch happens once, in .NET, instead of three times independently |
| R-10 | **A gate's record is trusted instead of its execution.** M1c found four: `CNF-025` red on `main` since M1a, `CNF-010` red in the Python job since M1b, `CNF-027` inert in .NET (D-M1b-19) and names-only in Python (D-M1c-22). M2a found a fifth shape — two *instruments* the schema and the spec had defined that no language executed at all: the fixture `clock`, unread since M0, and `TST-051`, never asserted anywhere **M11 found a sixth shape, and it is the one this row's mitigation cannot reach (D-M11-27, 2026-09-23): a gate that runs, fires correctly on a seeded violation, and measures a corpus smaller than the claim written over it.** `tools/doc-worksheet` kept only declaring types ending in `Operations`, so `BastionVaultClient`'s own eight public methods were never in the 473 the milestone declared complete — and seven of them had no `DOC-006` tag at all. Same family as D-M1b-19's inert analyzer, D-M12-16's ProcessExit code and D-M12-19's audit undercount, but distinct in mechanism: nothing here was inert or unrun | **R2** | Re-prove every gate by seeded violation and revert, as DR-0001 required and only partly delivered. **Added at M11 (D-M11-27): seeding a violation proves the predicate fires; it proves nothing about the boundary of the set the predicate runs over.** Every seeded violation in slice g2 sat *inside* the 473 and passed. The added control is to seed one **outside** the presumed corpus as well, and to require that a gate's exclusions be enumerated with a reason each and tripwired, so a corpus boundary is a reviewable decision rather than a by-product of an extraction heuristic. Ask of any gate: not only "does it fire?" but "what is it looking at, and who decided that?" **Formally M2c's exit condition** (D-M2-1) with its evidence recorded once, in one place. M2a proved its own two instruments this way and kept both as standing tests, which is the pattern the sweep should follow |
| R-8 | Appendix A lists 167 endpoints; mechanical volume swamps design attention | R1 | Endpoint plumbing is Engineering-tree bulk work — route it, do not hand-write it in the Claude tree |
| R-11 | ~~**CNF-023 (.NET analyzer/style diagnostics) is inert.**~~ **Closed 2026-09-15.** M2c's R-10 sweep found `dotnet/.editorconfig`'s bulk `dotnet_analyzer_diagnostic.category-<X>.severity = none` lines silently defeated every `dotnet_style_*_ = *:error` option-embedded severity beneath them — only the literal `dotnet_diagnostic.<ID>.severity` form survived. Fixed by adding a literal `dotnet_diagnostic.<ID>.severity` override for every already-declared option (no rule added or dropped), plus correcting two option keys that were not valid Roslyn keys. All 97+~150 violations the fix surfaced across both .NET projects were fixed in code, not suppressed (CLA-004); the gate-fires proof was re-run by seeded violation and revert | — | Closed — [DR-0008](decisions/0008-r11-cnf-023-remediation.md). Evidence: `decisions/0001-m0-harness-gate-proof.md` addendum, Row 7 |
| R-12 | ~~**`cargo audit` is genuinely red on `main`, independent of anything M2c did.**~~ **Closed 2026-09-15.** The pinned `rustls = "=0.23.40"` (`rust/bastionvault-integration-sdk/Cargo.toml`) was named in RUSTSEC-2026-0285 (TLS 1.3 handshake messages accepted across encryption-level boundaries, medium 5.3), fix `>=0.23.45`. CRS-003: TLS surface, R2 minimum | — | Closed — the pin is now `=0.23.45`. Brought forward from its M13/Stage 2 entry gate because the R-15 harness fix re-ran `rust.yml` on `main` and the CNF-024 gate failed there: a red gate on `main` is not something a freeze can hold open. The Stage-1 freeze (D-1/D-6) is intact — `rust/` library code is untouched, the crate's public API is unchanged (CNF-027, `rustls` is not re-exported), and Rust is 219/219 green with clippy (CNF-023) and `cargo audit` (CNF-024) both clean. `rust/*/Cargo.lock` is gitignored, so the exact pin in `Cargo.toml` is the whole fix |
| R-13 | **`python -m pip_audit` is genuinely red on `main`**, for an unrelated reason: a fresh `pip install -e ".[dev]"` pulls `requests 2.32.5` as a transitive dependency of `pip-audit` itself (not a direct or shipped project dependency), named in PYSEC-2026-2275, fix `2.33.0` | R1 | **Not fixed at M2c** — a dev-tooling transitive finding, not a shipped-artifact one, but `python.yml`'s `pip_audit` invocation has no scope restriction, so Python's CI job fails on it today regardless of Stage 1 focus. Owner: whoever next touches `python/` (M13 at the latest); a `pip-audit`/`requests` version bump is expected to be sufficient. Evidence: `decisions/0001-m0-harness-gate-proof.md` addendum, Row 13 |
| R-14 | **MEASURED AND REDUCED TO A LIST OF ONE, 2026-09-23.** The D-M12-4 audit ([DR-0022](decisions/0022-m12-core-conformance-audit.md)) ended five milestones of "still cannot declare" with a closed, evidenced blocker list. Two `CNF-001` gaps stood inside `Core`: **`TRN-081`** (`Client.ServerVersion()` absent — the SDK's own error hints told callers to call a member it did not expose) and **`DOC-030`** (documentation unpublished). `TRN-081` is **implemented and landed** on the project owner's implement-over-amend ruling (D-M12-22). **`DOC-030` is the only remaining blocker**, and it closes on the first real publication, which is outward-facing and human-gated — not M12's to close (D-M12-21). The audit also caught itself under-scoping: D-M12-4 named 33 subject IDs where the true set is **54**, having omitted the 26 `DOC`/`TST` IDs in sections 15–16 that `Core` requires outright — the remedy built to stop R-14 recurring was nearly the fifth recurrence (D-M12-19). **ANSWERED 2026-09-23 (§10 question 4); closes on the declaration itself, not on the answer.** The project owner ruled: **declare `Core`, `Standard` and `Complete` at M11 exit**, once sections 16–17 land and `CNF-002`'s blocker dissolves, audited against `CNF-001`/`CNF-014` and sequenced behind M12's D-M12-4 reconciliation. **Four milestone gates are spent and stay spent** — M4, M8, M10 and M11 each exited or will exit declaring nothing — and that cost is the measurement this row became. The original framing follows. **The conformance-level declaration schedule is unsatisfiable, and three milestone gates are stated in terms of it.** `Core` (M4), `Standard` (M8) and `Complete` (M10) each require sections 16–17, which are M11's. Found at M4 by grounding the gate against CNF-001/CNF-002 rather than against the KV work | R2 | **Open — project-owner decision** (§10 question 4): move M11 ahead of M8, or move all three declarations to the end. Meanwhile the control is honesty, not a claim: `dotnet/README.md` declares no level and lists the gaps by requirement ID, so no release can imply a conformance level it does not hold. **Hit a second time at M8 (2026-09-18), exactly as predicted at M4** — M8's booked exit was "declare `Standard`", it exited declaring nothing, and the gap list was regenerated instead (D-M8-6). **Hit a third time at M10 (2026-09-22), the same way**: M10's booked exit was "declare `Complete`", every one of its nine requirement IDs is landed and section 12 clears entirely, and it still exits declaring nothing — `dotnet/README.md`'s gap list is regenerated instead, exactly as M4 and M8's were. **All three blocked gates are now spent.** The row has therefore stopped being a forecast and become a measurement, twice over: the question has cost three milestones their stated exit criterion in a row, and every one of those costs would have been the price of answering it once, at M4. Evidence: [DR-0009](decisions/0009-m4-kv-engine.md) D-M4-3, [DR-0013](decisions/0013-m8-transit-totp-and-efficiency.md) D-M8-6, [DR-0017](decisions/0017-m10-remaining-bindings-and-identity.md) |
| R-15 | ~~**A gate that passes only on the CI matrix's single version.**~~ **Closed 2026-09-15.** Python's suite had 12 failures under Python 3.14 and none under 3.12: all three in-process mock servers issued CA and leaf certificates with no Subject Key Identifier and no Authority Key Identifier, which OpenSSL 3.5+ rejects during chain verification. CI pinned 3.12, so it was green on a harness that did not work. This is the R-10 shape one layer out — the gate is green because of what CI does not run. Found at M4 by running the Python suite locally while verifying an unrelated fixture-count change | — | Closed — both extensions are now issued in .NET, Rust and Python, `KeyUsage` is explicit, and `python.yml` runs a 3.12 **and** 3.14 matrix so a version-only failure cannot hide again. Python 484/484 green on 3.14 (was 472 passed / 12 failed), .NET 741/741, Rust 219/219. The Stage-1 freeze (D-1/D-6) does not cover a test harness that does not run; `rust/` and `python/` library code is untouched |
| R-16 | **Cluster discovery ships inert** — no `ISrvResolver` implementation ships, so an application that supplies none takes `DSC-011`'s "no records" path and gets literal single-address behaviour where it asked for discovery, silently; `DSC-042` also leaves failover unarmed at one candidate, so it loses fail detection too. **Framed and ruled by [DR-0014](decisions/0014-r16-srv-resolver-and-silent-discovery-degradation.md); re-tiered R2 → R3 under `CRS-004`** | **R3** | **No longer M11/M12's.** The project owner ruled a hand-rolled zero-dependency SRV resolver in core plus the `DSC-015`…`DSC-019` loudness contract, with strict mode **defaulting to strict**. Closes on implementation. **The M8 blocker is discharged (2026-09-21): `0.13.0` merged at `e44ce2a`**, so `DSC-019`'s error-catalogue regeneration no longer collides with M8a's uncommitted generator change, and the seam slice d was expected to provide now exists — `EgressKind { Request, DiscoveryProbe, SrvResolution }`, where **`SrvResolution` is already a live, exercised call site**, so a shipped resolver's DNS I/O has a named exemption waiting for it rather than an un-gated path someone must justify later (D-M8-25, D-M8-26, D-M8-30). **Both decisions have landed in .NET (2026-09-22).** Decision B: `DSC-015`…`DSC-018` — `DiscoveryConfig.StrictDiscovery` (default `true`), `DiscoveryReport.Degraded` as a cause not a boolean, the DSC-015 logger warning and the DSC-018 `Reconnect()` recompute; `BV-DISCOVERY-004` (`DSC-019`) minted and generated in all three languages' catalogues. Decision A, dispatched separately and after B per D-R16-3 ("B ships first"): `DSC-050`, a hand-rolled zero-dependency default `ISrvResolver` (`Internal/DnsSrvResolver.cs`) — UDP with mandatory TCP retry on truncation, absolute-only qualification with single-label rejection, no caching, the full D-R16-9 parser bounds contract (backward-and-hop-capped compression pointers, per-label/name size caps, RDLENGTH-anchored RR-walk resync, cryptographic query ID with an ephemeral port, ID/QR/echoed-question verification) and an injectable nameserver override (`DiscoveryConfig.Nameservers`) that is also R-26's remedy. 1537/1537 .NET tests green (including 15 new wire-level tests against an in-process fake DNS server exercising every parser bound), coverage held above the floor, zero new runtime dependency. Committed and pushed to `main` as `0.14.1` at `78dd7d4`. **The project owner confirmed the `specifications/` §13 change directly on 2026-09-22**, satisfying `agents.md` §5.3's R3 human-confirmation gate (recorded in [DR-0014](decisions/0014-r16-srv-resolver-and-silent-discovery-degradation.md)'s addendum) — **R-16 is closed, on implementation and on the record.** M11 retains only the narrowed documentation obligation in D-R16-7. **The lesson this row is itself the evidence for:** a gap booked with no owner survives a milestone — it was raised at M5 and reached M8 untouched | 
| R-17 | **`DSC-033` cannot prefer healthy nodes without a `specifications/` change.** `DSC-022` says surfacing `cluster_healthy` lets ranking "prefer healthy nodes as a tiebreak after RTT", but `DSC-033`'s rank list is closed, exhaustive and explicitly deterministic, and does not contain it. At equal RTT and weight an unhealthy node can therefore be picked over a healthy one on the lexical-URL tiebreak | R3 | **Open — **ASSIGNED 2026-09-24 (D-7, §10 q15): owner M14**, with R-18 and R-22.** The fix is a `specifications/` change, which is M14's subject and already R3 under `CRS-004`'s first limb; no other milestone may touch `specifications/`.** Original framing: Ruled surfacing-only at M5 (D-M5-20): adding a fifth sort key would widen a list the specification closes and would diverge from any Stage 2 implementation reading `DSC-033` literally. Changing it is a `specifications/` edit, R3 by CRS-004. The control meanwhile is that the behaviour is pinned and fixture-asserted, so all three languages will at least be wrong identically |
| R-18 | **`AUT-003`'s relogin replay can exceed `RES-001`'s attempt cap on its own.** D-M2-9 deliberately gives the relogin replay a fresh `MaxAttempts`, which is correct for `AUT-003` but means `AttemptsBefore` can reach `2 × MaxAttempts` with no failover involved. `RES-001`'s cap is written about the failover replay and says nothing about a relogin replay, so the two accepted rulings are in tension | R2 | **Open — **ASSIGNED 2026-09-24 (D-7, §10 q15): owner M14.** M6 closed 2026-09-15 and this row went with it, still reading as owned. It belongs with R-22, which is the same `RES-001` cap tension seen from the failover side, and the row says itself that fixing it means reopening an accepted M2 ruling — an R3 `specifications/` change, therefore M14's.** Original framing: owned by M6, Not M5's to resolve: it predates the milestone and fixing it means reopening an accepted M2 ruling (R3). M5 guarantees only that *failover* never causes the cap to be exceeded — D-M5-28's clamp is what stops the two mechanisms compounding multiplicatively. Evidence: [DR-0010](decisions/0010-m5-cluster-discovery-and-resilience.md) D-M5-29 |
| R-19 | **Fixtures can encode response bodies their own specification section forbids, and pass.** M5 found two instances of one defect class: `resilience.failover.read-once` (landed since before M4) and `resilience.backoff.math-seeded` (authored *inside* M5, one addendum after the rule against it) both returned KV v2 metadata without `created_time`, which section 07 declares non-optional and D-M4-12 maps to `BV-PROTOCOL-002`. Both passed for incidental reasons — the first because the reader ran before M4 existed, the second because `Logical.Read` never invokes the KV v2 reader | R2 | **Open, unowned; a sweep is §10 question 5.** Two instances in one milestone implies more across the 224-fixture corpus, and each is a latent Stage 2 trap: Rust and Python must reproduce these bodies exactly, and will fail on the ones whose reader they implement. Mechanical Engineering-tree work — validate every fixture body against its section's type block — but it needs a milestone slot before M13, not an ad-hoc pass. Evidence: [DR-0010](decisions/0010-m5-cluster-discovery-and-resilience.md) D-M5-26, D-M5-30 |
| R-20 | **`specifications/` is derived from a server that moves independently, and until now nothing tracked the link.** The specification is dated 2026-09-13 and declares a `≥ 0.42` floor; upstream `ffquintella/BastionVault` was already at `v0.44.4` on 2026-09-15. The gap was real and undetectable: `docs/api.md` had gained the `<list>-info` and `sys/cache/version` sections that `14-batch-and-request-efficiency.md` exists to specify, and no mechanism in this repository could surface that. Everything downstream inherits it — an SDK can be perfectly conformant to a specification that is itself stale | **R3** | `specifications/provenance.json` pins the upstream ref and the git object id of all 35 sources feeding the specification, and `tools/provenance` reports drift grouped by the specification document needing review ([DR-0011](decisions/0011-specification-provenance-tracking.md), CNF-044…CNF-047). Baseline deliberately pinned at the declared `v0.42.0` floor, so nothing is assumed reviewed. CI runs non-blocking (an upstream release must not redden an unrelated PR); the binding use is release-checklist item 6, which forbids releasing with unreconciled `authoritative` drift. **Open backlog: the first report names 8 specification documents** — `03`, `04`, `05`, `06`, `12`, `14`, Appendix A, Appendix B — which is now visible, enumerable work rather than an unknown |
| R-21 | **A per-client cache keyed without the effective namespace serves one tenant's answer to another.** Two independent Engineering-tree agents produced this same defect in one milestone pair, in isolated worktrees, neither aware of the other: M7a's `SYS-026` mount-type cache (keyed on the `activeNamespace` field while the request went out under `options.Namespace ?? activeNamespace`, poisoning and cross-reading across tenants and feeding a wrong `KvVersion` to `KV-001`) and M6's `AUT-051` machine-identity cache (keyed on mount alone, on a `ClientContext` shared across `WithNamespace` views, with no TTL — so permanent, and failing **open**). Both were caught by R3 handback review, neither by the author, and neither by any test | **R3** | **M7a's is fixed on `m7-sys-remainder` and unverified; M6's is unfixed on `m6-auth-remainder`.** The generalisation is the point: namespace is a request-scoping dimension (`AUT-041`, `CFG-041`) and every memoised value on `ClientContext` must include the effective namespace in its key. A tree-wide audit of every cache and memoised value was briefed into both fix passes and neither completed it — **it is the first thing to finish when work resumes**. **The tree-wide audit is now complete** (2026-09-18, read-only survey of `main` and both branches) and found **no third instance** — every other retained value on `ClientContext` is either keyed correctly or genuinely namespace-invariant. `SYS-026`'s fix is confirmed complete at all four call sites, using textually the same expression as `RequestExecutor.EffectiveNamespace` so key and wire header cannot disagree. Two residues: `ClientContext.TokenInfo` is judged safe only on the strength of `05-authentication.md:38` (a namespace-mismatched request is gated to 403 rather than answered with another namespace's data), which is a server-behaviour claim this repository cannot verify from its own source; and six of the seven safe verdicts rest on reasoning rather than on a test that would fail if they were wrong. **The inspection rule, which catches both known instances without running anything:** a value fetched by a namespace-header request and stored on `ClientContext` — which is what `WithNamespace` views share — must be keyed by exactly `(options.Namespace ?? activeNamespace).TrimEnd('/')`, never by `activeNamespace` alone, the view's namespace, or the mount alone; and where a correct key helper exists, verify it is actually *called*, because an unused correct helper reads as a landed fix in review when it is not one |
| R-22 | **`RES-001`'s attempt cap and `DSC-042`'s guaranteed failover replay cannot both hold at `MaxAttempts = 1`.** M6 implemented R-18's Option A clamp on the relogin replay, matching D-M5-28's clamp on the failover replay. Both carry a `Math.Max(1, …)` floor, and the floors **stack**: a call that fires both replays reaches `MaxAttempts + 2`, one over `RES-001`'s unqualified "total attempts". Measured at handback review against `FailoverUnitTests.A_failover_replay_does_not_mint_a_second_re_login` with `MaxAttempts = 1` (cap 2), observed `Attempts` **3** | R2 | **Open — **ASSIGNED 2026-09-24 (D-7, §10 q15): owner M14**, with R-17 and R-18.** It needs a `RES-001` amendment, and R-18 is the same tension from the relogin side; the two are one edit.** Original framing: Not a regression — the pre-clamp code produced the same 3 at that setting — and Option A still strictly improves the single-mechanism case (7 → 4 in DR-0011's worked example), so R-18 is **closed with this residual named**, not closed outright. The floor must stay: removing it breaches `DSC-042`'s MUST that the failover replay happen at least once. This is therefore a tension between two requirements rather than a defect in either clamp, and resolving it means amending `RES-001` to say what it means by "total" when two bounded replays compose — an R3 `specifications/` change, Architect queue. Carried into the Rust and Python briefs so no parity pass transcribes DR-0011's original, false "composes with no interaction" claim. Evidence: [DR-0011](decisions/0011-m6-authentication-remainder.md) D-M6-21 |
| R-23 | **`tools/error-catalogue` compiled Appendix B §2's `+ a/b/c` form as a conjunction where the appendix means alternation, so five recognition rules could never fire.** `/` is alternation everywhere else in the §2 table, but the generator emitted the alternatives following a `+` into `RecognitionRule.ContainsAll` — "extra substrings the message must **also** contain". Row 283 settled the intent past argument: a message cannot be both `is below min_decryption_version` and `not found on key`. Affected: **`BV-INPUT-102`** (SYS-042, landed M7b), **`BV-INPUT-103`** (SYS-091, landed M7c), **`BV-AUTH-011`** (machine binding — landed in M2 and **shipped since `v0.5.0`**), `BV-TRANSIT-004` and `BV-SSH-005` | **R3** | **Closed at M8a (2026-09-18), with two of its own statements corrected.** The generator now compiles a qualifier group's items as alternatives into a new `containsAny` slot in all three languages, and M7c's `RestoreAsync` remap (D-M7-36) deleted with the defect, as designed. Consequences (3) and (4) are discharged with it: M8 and M10 no longer inherit a latent defect each, and the corpus no longer masks the bug — the generator emits **one fixture per alternative** (124→130 generated, corpus 230→236), so each affected rule has a fixture the pre-fix generator fails. Eleven do, and the whole pre-change corpus replays against the new table with zero regressions. **Two corrections to this row's original text, both found at the handback gate.** (a) It claimed `BV-SERVER-005` is retryable and that a corrupt-restore rejection therefore looked retryable; `appendix-b-error-catalogue.md:138` reads `R = no`, and every code on every side of the change is non-retryable — the blast radius was an error **code** change, never a retryability one, and the claim had already propagated into a downstream brief. (b) It quoted `errors.recognition.bv-input-102.1` as answering `namespace refuse cross-namespace`; that fixture now answers `namespace refuse`, with `.2` carrying the other alternative. The R3 tier was not lowered, but it rests on a different limb of CRS-004 than first recorded: no workflow publishes to a package registry (`build-artifacts.yml` builds, never pushes), so `v0.5.0` and its successors are repository tags rather than released packages — the limb that bites is the `specifications/` one, since the slice edited Appendix C. Evidence: [DR-0013](decisions/0013-m8-transit-totp-and-efficiency.md) D-M8-2, D-M8-3, D-M8-8…D-M8-13 |
| R-24 | **Rust does not discharge `FIX-001`: its fixture "validation" validates nothing.** `rust/bastionvault-integration-sdk/tests/harness/fixture.rs:227-236` loads `schema/fixture.schema.json` — but only to locate the repository root — and then checks solely that the fixture's root is a JSON object. It never validates the document against the schema. Python uses `Draft202012Validator` and .NET a shared `JsonSchema`; Rust has a stub. `FIX-001` (`appendix-c-conformance-fixtures.md:65`) is a **MUST**: every fixture MUST validate against the schema | **R2** | **Open — **ASSIGNED 2026-09-24 (D-7, §10 q15): owner M13.** This *is* Rust work, and M13 is the only milestone permitted to touch `rust/` under D-1/D-6 — it could not have been fixed earlier and has no cheaper home. It joins R-28 there.** Verified at source, not inferred. Four Rust call sites (`fixture_harness.rs:25`, `:60`, `error_fixtures.rs:33`, `transport_fixtures.rs:17`) carry the message `all repository fixtures must validate` against an implementation that does not, so **Rust's 220 green tests prove less than they appear to** — a malformed fixture that Python and .NET would reject passes in Rust. This is R-19's and R-23's shape a third time, now in the harness rather than in a fixture or a generator: a check that passes for a reason unrelated to what it claims. **Consequences.** (1) Any parity evidence resting on "all three languages validate the corpus" is overstated for Rust, including in earlier milestone records. (2) The exposure grows at **Stage 2**, when Rust starts consuming the whole corpus in earnest rather than the transport and error subsets. (3) It compounds the three-language exclusion-rule divergence in the count-derivation work (R-25): Rust both under-validates and under-counts by a different rule than the other two. **Not fixed in M8**: `rust/` is frozen for Stage 1 (D-6), the defect predates this milestone and is no part of its content, and folding it in would give one defect two owners (CLA-008). Found by the count-derivation session's architecture review, referred rather than folded in — the correct call — and verified here before recording | 
| R-25 | **The conformance-fixture corpus count is hand-transcribed into three languages, and the three fixture loaders exclude non-fixture JSON by three different rules.** The count went stale twice and left `main` red on the Rust and Python workflows from `0.10.0` (2026-09-15) until M8a. Separately, Python excludes any path with a `schema` directory component, .NET excludes exactly `<root>/schema/fixture.schema.json` by full path, and Rust excludes any file *named* `fixture.schema.json` anywhere; they agree today only because exactly one such file exists at exactly that path | **R2** | **Open — owner M15, and CONCRETE as of 2026-09-24.** Adding one fixture (`identity.self`) turned **`main` red in both the Rust and Python jobs**: the count is hand-copied into **five assertion sites across three languages** and only .NET's was updated. Caught by *running* the suites, not by reasoning about them — Python failed 2 of 586, Rust 3 assertions — and all five corrected by hand, again. **The Stage 1 freeze does not license leaving `main` red**: a count constant is not behaviour, and `CLA-004` applies with more force to knowingly shipping a broken gate than to fixing one.  A *session* is not an owner, and DR-0015 is in fact **accepted**, not proposed — an accepted design, unbuilt, with nobody holding it. Its own precondition ("after all of M8") was met on 2026-09-18. The generated manifest is a shared artefact three loaders read, so D-7's rule 3 applies: built once in M15, or built three times in M13.** Original framing: owned by the count-derivation session (`decisions/0015-fixture-corpus-count-derivation.md`). The count half is a generated, committed manifest outside `specifications/fixtures/`, read by all three harnesses and gated by regenerate-and-diff — the shape the error catalogue already uses (D-M1c-1). The exclusion half is latent, not active: a *second* JSON file under `specifications/fixtures/schema/` (a schema revision beside the current one is the ordinary way it happens) would be dropped by Python and counted by .NET and Rust, so **the three languages would disagree on which way the count moved** — which no single assertion can express, and which would present as an unexplainable red in two of three workflows. **Note for any later reader:** the count assertion is *unrequired* defence-in-depth. `TST-010` requires fixtures be loaded from the repository rather than copied and `FIX-001` requires schema validation; neither mandates a count, and `specifications/` states no corpus count anywhere. Do not cite either as requiring it. M8's slices are briefed not to add a non-fixture JSON under `specifications/fixtures/`, which keeps this latent for the milestone's duration. **The count half stopped being a prediction at M9's merge.** M9 updated the three transcription sites it knew about (250 → 253); a **fourth** — `rust/bastionvault-integration-sdk/tests/fixture_instruments.rs:156` — was added on `main` by the M2a parity pass while M9 was in flight, and went red the moment the two branches met. Neither branch was careless: each updated every site it could see. That is the failure this row predicts, demonstrated **across branches** rather than within one tree, and it is an argument the generated manifest cannot come soon enough — a branch cannot enumerate transcription sites that do not exist yet on its own base | 
| R-26 | **The shipped SRV resolver cannot see macOS scoped resolvers.** `GetIPProperties().DnsAddresses` was *measured* returning one identical global list on all 27 interfaces, including down ones, which collapses macOS's scoped resolvers — so on a split-horizon VPN the resolver queries the wrong nameserver and gets NXDOMAIN. Linux and Windows resolution is in-platform and unaffected | R1 | **Accepted knowingly** ([DR-0014](decisions/0014-r16-srv-resolver-and-silent-discovery-degradation.md) D-R16-7), not discovered late: under `DSC-017`'s strict default the failure is **loud rather than silent**, and D-R16-6 makes the nameserver list an injected input, so an operator has a remedy. Documentation obligation held by **M11**. Strictly narrower than the R-16 it replaces — a diagnosable failure in one environment instead of an undetectable one in all of them | 
| R-27 | **Section 14's endpoint table writes `/v2/` uniformly and contradicts the endpoint catalogue.** `14-batch-and-request-efficiency.md:98-107` prefixes all seven `*-info` rows with `/v2/`, but `appendix-a-endpoint-catalogue.md` gives `Sys.ListNamespacesInfo` as **v1** (`:40`) and `Ssh.ListRolesInfo` as **v1** (`:221`), while agreeing on v2 for `Sys.CacheVersion` (`:43`) and "v2 recommended" for `ListUsersInfo` (`:87`). Found at M8e when the slice pinned the two new routes to `/v2` per section 14 and had to leave the already-shipped `sys/namespaces-info` on `/v1` to match its accepted fixture — the apparent inconsistency is the specification's, not the SDK's | **R3** (the `specifications/` limb of CRS-004; **not** the published-artefact limb, which is vacuous — `build-artifacts.yml` builds and never pushes) | **Open — project-owner decision.** M8 shipped every pin matching Appendix A, which is also what D-M8-5 ruled for the `Page<Namespace>` contradiction: the owning section and the catalogue win over section 14's cross-cutting table. **This is the second place section 14 disagrees with the section that owns the endpoint, and that is the actual finding** — section 14 was written as a cross-cutting chapter and its endpoint table was never reconciled with Appendix A, so a third instance should be assumed until someone checks all seven rows. Resolving it is a `specifications/` edit. Until then the control is that .NET follows the catalogue and the fixtures pin it, so Stage 2 will transcribe the same choice rather than diverge. **The row's own instruction — "a third instance should be assumed until someone checks all seven rows" — is now discharged at M9: all seven were checked and there is no third.** `certs-info`, `csr-info` and `sign-request-info` carry no Prefix column at all (Appendix A's PKI table has none), `roles-info` and `namespaces-info` read `v1`, `users-info` reads "v2 recommended", `targets-info` is M10's. The disagreement is uniform and is section 14's alone. M9 also recovered the reading that makes the rows consistent: **Appendix A's legend (`:3-5`) defines `v1` as "uses `ApiPrefix`, both serve it" — an instruction *not to pin*, not a claim that the route lives at `/v1`** — so the catalogue and section 14 were never in conflict about *where* a route is, only about whether the SDK pins. **Two independent agents misread that legend in the same session (2026-09-21) and each booked a specification correction against a document that is not wrong**, which is why the legend is quoted here rather than cited. The row stays open: the §14 reconciliation is still unmade. Evidence: [DR-0013](decisions/0013-m8-transit-totp-and-efficiency.md) D-M8-45, [DR-0016](decisions/0016-m9-pki-and-ssh.md) D-M9-7 |
| R-28 | **The client rate gate is .NET-only, and one of its semantics contradicts shipped Rust.** M8d landed the `EFF-001`…`EFF-006` token bucket in .NET alone (D-1/D-6 freeze Stage 2). Four behaviours the parity pass must match or consciously overturn: (a) **a pause is never shortened by a nearer one** — .NET and Python take the maximum, but `rust/…/rate.rs:42` assigns `paused_until = Some(now + bounded)` **unconditionally**, so a second `429` carrying a shorter `Retry-After` moves Rust's resume instant *backwards*; (b) exactly one reservation is grantable at the pause end, not a full burst; (c) **a pause holds a waiter that was already in the queue** — the leak M8d's own R3 gate found, where a `429` arriving mid-sleep let one request out *into the ban window*; (d) a disabled gate reports a maximum sentinel, and what crosses languages is the invariant `AvailableTokens > 0` means "may proceed", never the literal `int.MaxValue`. **(a) is a pre-existing Rust defect this milestone exposed, not one it created** — two of three languages already agreed. **(c) is a behavioural MUST, not a refinement:** whichever language implements the bucket next will reproduce the leak if it reserves before sleeping | R2 | **Open, owned by M13.** Cannot be fixed now — `rust/` and `python/` are frozen for Stage 1 and this is library code, not a harness gap. Carried here rather than in the decision record alone so the M13 brief inherits it as behaviour-to-avoid, which is the mechanism D-2 credits with stopping M1a's defects from recurring. Related: `EFF-002`'s FIFO ordering is **not expressible in the shared fixture corpus** (a fixture drives one operation and the harness transport is sequential), so each language needs its own concurrency test; pinning it portably would need a concurrency primitive in the fixture schema, which is a `specifications/` change. Evidence: [DR-0013](decisions/0013-m8-transit-totp-and-efficiency.md) D-M8-33, D-M8-34, D-M8-42, D-M8-43 |
| R-29 | ~~**`Auth.Userpass.ListUsersInfo` is named for section 14, not for the catalogue that will own its neighbours.**~~ **Closed at M10 slice d.** Section 14:123 named it `Auth.Userpass.ListUsersInfo`; `appendix-a-endpoint-catalogue.md:87` nests it as `Auth.Userpass.Admin.ListUsersInfo`. M8e shipped section 14's name because no `Auth.Userpass.Admin` sub-client existed and creating a one-member one speculatively is the anticipatory structure CLA-007 rules out. Slice d built the rest of `Userpass.Admin.*` and the sub-client is no longer speculative, so the rename is taken (D-M10-3): `ListUsersInfoAsync`/`ListUsersInfoAllAsync`/`UserSummary` moved from `UserpassOperations` onto the new `UserpassOperations.Admin`, verbatim (verified byte-identical at the R2 handback gate). Breaking on an **unpublished** API only — nothing in this repository publishes to a package registry, so no released consumer is affected | R1 | Closed. Evidence: [DR-0013](decisions/0013-m8-transit-totp-and-efficiency.md) D-M8-46, [DR-0017](decisions/0017-m10-remaining-bindings-and-identity.md) D-M10-3 |

| R-30 | **`BV-TRANSPORT-005` means "something cancelled", not "the caller cancelled", and two call sites now compensate for it locally.** `Internal/RequestExecutor.cs:948` catches `OperationCanceledException` with **no** `when (cancellationToken.IsCancellationRequested)` filter and maps it through `TransportFailureMapper.MapCancelled`, so the code does not record *which* token fired. `ITransport` is public API and settable via `BastionVaultClientOptions.Transport`, so a user transport carrying its own internal deadline — the exact pattern `HttpClientTransport.cs:130-131` uses — produces `BV-TRANSPORT-005` while the caller's token is still live. `Internal/RequestExecutor.cs:829` is a second unfiltered catch on the token-resolution path. Found at `CCH-006`'s R2 gate, which refuted an author claim that the resulting fall-through was unreachable | R2 | **Open — **ASSIGNED 2026-09-24 (D-7, §10 q15): owner M15.** M9 and M10 both closed while this read as a "candidate" for them. A .NET-only error-semantics fix, and D-7's rule 3 applies squarely: left until M13 it becomes three fixes and a parity exception instead of one.** Original framing: an M9/M10 candidate. Not a defect today: both consumers handle it correctly — `Internal/TokenRenewal.cs:210` and `CacheWatcher.cs:91` each re-check the caller's token and treat a live one as an ordinary failure to back off from. **The smell is that there are two of them.** A compensation repeated at every call site is a contract the callee should be stating instead, and the third consumer is the one that will forget. `HttpClientTransport.cs:138` already disambiguates correctly (`when (!cancellationToken.IsCancellationRequested)` → `Timeout`), which shows the filter is expressible — it is simply not applied at the executor. Fixing it means deciding whether a transport-internal cancellation deserves its own code rather than sharing the caller's, which is an error-model change (section 04) and therefore Architect work, not a local edit. Evidence: [DR-0013](decisions/0013-m8-transit-totp-and-efficiency.md) D-M8-55 |
| R-31 | **`PKI-030`'s second limb is unimplementable as specified: `BV-QUOTA-002` has no recognition row and no server message anywhere.** `09-pki-engine.md:107-108` requires a queue-cap breach (500 pending) to map to `BV-QUOTA-002 QueueFull` **"by message"**. The *code* exists (`appendix-b-error-catalogue.md:128`, generated into `ErrorCatalogData.g.cs`), but Appendix B §2 carries **no recognition row** for it — only `BV-QUOTA-001` (`namespace quota exceeded`, `:245`) — and §09's own recognition table omits the message too. Recognition by message requires the message, and no document states it. Found at M9 framing, before any slice was dispatched | **R3** (the `specifications/` limb of CRS-004; the published-artefact limb stays vacuous) | **Open — MEASURED 2026-09-24, and the measurement found a live defect.** The string exists: HTTP **429 without `Retry-After`**, body *"sign-request/import: 500 requests are already awaiting a decision on this mount; decide or delete some before importing more"*. **`BV-QUOTA-002` still has no recognition row**, so this currently falls through `04-error-model.md:101`'s generic 429 rule and is reported as **`BV-RATE-002` — the wrong family**: a caller who filled the queue is told to back off, which never clears it. Appendix B's hinted wording and its promised `Details.max` are both absent from the wire. **Now owner M14** (the fix is a `specifications/` change, R3, and must be ordered ahead of the generic fallback) — escalated as **§10 question 18**. `PKI-030` **stays baselined** until the rule lands with a test naming the ID (**CLA-004**). Prior framing: M10 closed 2026-09-22 while this still named it. `PKI-030` is the **only non-documentation requirement blocking a `Complete` claim**, and its blocker was never a design question — it needed a live server to state the `BV-QUOTA-002 QueueFull` message no document carries. D-M12-5 authorised exactly that capture once a supported server existed; the server arrived 2026-09-22 and the slice was never dispatched.** Original framing: owned by M10, which builds the rest of the PKI queue surface. `PKI-030` **stays on the traceability baseline** and M9 landed 10 of its 11 IDs rather than reporting a half-covered requirement as covered ([DR-0016](decisions/0016-m9-pki-and-ssh.md) D-M9-11). Closing it needs the server's actual message text — the same provenance every Appendix B row has — and then an Appendix B row, a regenerated catalogue, and a fixture, since Appendix B §3 requires every recognition row to produce its code for at least one fixture. **Do not guess the string:** R-23 is the precedent for a recognition rule that passes its own fixture and cannot fire in production |
| R-32 | **Neither PKI queue table defines any response shape, so eight routes return untyped maps.** `09-pki-engine.md:86-108` is two columns — operation and HTTP — for all fifteen queue routes. `Pki.Csr.Read`, `Generate`, `SetSigned`, `SignRequests.Read`, `Preflight`, `Approve`, `ApproveVerbatim` and both `*-info` listings have no documented response; §14:118-119 names `CsrSummary` and `SignRequestSummary` and defines neither. M9 bound them to the raw response map and `Page<IReadOnlyDictionary<string, JsonElement>>` rather than invent public records (D-M9-10, D-M9-21) | R2 | **Open — owner M15 for the typed records. Both soft edges CLOSED 2026-09-24, and neither was a defect** (§10 q16, DR-0021 fifth addendum): `Pki.ReadKey`/`Pki.Csr.Read` return **no** private material for an `exportable: true` object — it appears only where a request parameter announces it, so **D-M9-16's rule is measured sound**; and `issuer_name` is **accepted** by both `root/generate` and `intermediate/generate`, so **D-M9-23 is measured correct** and §09 is the document in error. One new soft edge replaces them (**F18**): `intermediate/generate` returns `{csr}` alone, omitting the documented `key_id`. Prior framing: M10 closed 2026-09-22 while this still named it, and the soft edges were booked to M12's integration suite where **no scenario ever mentioned `exportable` or `issuer_name`**.** Original framing: Replacing a map with a typed record is a breaking change on an **unpublished** API — nothing here publishes to a registry — so it is cheap *if taken deliberately at M10* and a surprise if not, which is R-29's reasoning applied again. **Two soft edges ride on this row**, both booked to M12's integration suite because no document answers them: whether `Pki.ReadKey(ref)` or `Pki.Csr.Read(id)` returns private material for an object created `exportable: true` — D-M9-16's rule keys on *request parameters*, the only signal §09 gives, so a route that returns a secret without a parameter announcing it is invisible to it — and whether `Pki.GenerateIntermediate` accepts `issuer_name` at all (D-M9-23 keeps it on whole-set-reuse grounds while recording that §09 points the other way) |
| R-33 | **`ErrorPaths.Redact` inspects path segments and not query strings, so a query-borne secret would reach three logging surfaces.** `Internal/ErrorPaths.cs:9,25-45` rewrites only the segment following `lookup`, `renew`, `revoke` and `revoke-orphan`. A query string is not a segment it looks at, so a secret in one flows unredacted into `RequestEvent.Path` via `RequestExecutor.cs:892` — the `CFG-080`/`TST-051` observer, which a consumer wires to a logger and which **fires on success** — plus `BastionVaultException.Path` / `Details["path"]` and `HintEnrichment`'s "The path as sent was …" | R2 | **Open — **ASSIGNED 2026-09-24 (D-7, §10 q15): owner M15.** Latent, not active — and that is the argument for taking it before Stage 2, not after: a latent defect copied into three languages is three latent defects and one shared root cause nobody re-derives.** Original framing: unowned — and latent, not active. No shipped route places secret material in a query. M9 came within one handback of shipping two that would have: `Pki.ExportCertificate`'s and `Pki.ExportIssuer`'s GET forms both carry a `password`, and both are bound **POST only** for exactly this reason (D-M9-19, D-M9-22). **Those two routes stay POST-only until this row closes** — that is the dependency worth carrying, since a later reader will otherwise read the restriction as arbitrary and "fix" it. Closing it is a transport-layer change, not an engine milestone's |
| R-35 | ~~**`identity.self` is in Appendix C's mandatory fixture set with no capture on disk to author it from.** `appendix-c-conformance-fixtures.md:130` lists `identity.self` alongside the already-authored `identity.sharing.target-base64url`; only the latter exists under `specifications/fixtures/identity/`. `FIX-010` requires a fixture body copied from a real server exchange, and `12-other-engines-and-identity.md:12`'s field list for `GET identity/entity/self` is a description, not a capture. Found at M10 framing, before any slice was dispatched | R1 | **CLOSED 2026-09-24 by the D-M12-5 follow-up slice** (§10 q16). `specifications/fixtures/identity/identity.self.json` is captured from a real `bvault` 0.44.5 exchange and is the **first fixture in this corpus with genuine `FIX-010` provenance** — the other 253 still have none (**R-38**). The blocker was never design: it needed a live server, and once one existed the capture took minutes. Prior framing: M10 closed 2026-09-22 while this still named it. Like R-31 this was never a design gap — `identity.self` needed a real exchange to copy, which `FIX-010` requires and which only a live server produces.** Original framing: owned by M10 ([DR-0017](decisions/0017-m10-remaining-bindings-and-identity.md) D-M10-4). `Identity.Self()`/`Identity.Aliases()` ship in slice a on ordinary unit-test coverage; the fixture stays absent and the gap goes into `dotnet/README.md`'s CNF-002 list, as `PKI-030` did at M9. **Do not guess the body:** same rule as R-31, R-23's precedent |
| R-34 | **§10 states the CSV wire form for one of its two list-valued request fields and not the other.** `10-ssh-engine.md:38` says "`valid_principals` is CSV on the wire"; `:81`'s `asset_group_ids[]` says nothing about encoding, and the accepted fixture `sshbroker.effective-v2-pinned.json` — which predates the SDK — sends `"asset_group_ids": "g1"`, a CSV string. The `[]` is parameter cardinality in an operation signature, the same notation position as `:12`'s `private_key?`, not a wire statement; `:38` existing at all is the proof that CSV is not §10's default | R1 | **Open, owned by the Architect queue beside R-27.** The fix is a four-word §10 edit, which is R3. **The control already exists**, which is what keeps this R1: the fixture is driven in all three languages, so a Stage 2 pass that transcribed a JSON array from §10 alone would go red rather than diverge silently. Settled for M9 by [DR-0016](decisions/0016-m9-pki-and-ssh.md) **D-M9-28**, which states the general rule this row is an instance of: **a fixture is authoritative on wire encoding where its section states none; a section is authoritative on operation shape, element type and name, and a fixture may not contradict it** — the same "each artefact answers the question it owns" principle as D-M9-7 (Appendix A owns the prefix notation) and D-M9-9 (the area section owns element types) |
| R-36 | **`Files.Sync`'s per-target credential fields (`SyncTarget.Fields`) have no documented wire names anywhere in section 12** — only that they exist and are write-only (`12-other-engines-and-identity.md:65`). M10 slice b ships them as an opaque `JsonElement?` bag merged into the request body verbatim, rather than typed `SecretString` members, because naming the fields would be the plausible guess D-M1c-25 forbids (R-23/R-31's precedent). Found at slice b's R2 handback review | R2 | **Open — owner M15. The field names are MEASURED 2026-09-24** (§10 q16): every target needs `kind` and `target_path`; `smb` additionally requires **`smb_username`** and **`smb_password`**, with **`smb_domain`** optional, and reads back as `smb_password_set` (boolean, credential redacted by omission). The typed `SecretString` replacement for the opaque bag is now writable and goes to M15 **before** M13 mirrors the bag into two more languages (D-7 rule 3). **A second finding rides along (F17):** the server also returns `ssh_username`, `ssh_password_set`, `ssh_passphrase_set`, `ssh_private_key_set` and `ssh_host_key_fingerprint` on every target regardless of `kind`, so its schema is a **superset** of the documented `local-fs | smb`. An `ssh` kind may exist; it was **not driven and not invented** (R-23, R-35). Prior framing: "Whichever milestone next has the server capture" was M12, which had the server for two days and never drove `Files.Sync` — no scenario mentions `SyncTarget`. If the names cannot be measured they stay **unmeasured and recorded as such**, never invented (R-23, R-35's own lesson), and M15 carries the row forward.** Original framing: an M12 integration-suite candidate, PKI-030/R-35-style. Not a live leak today: `RequestObserver.cs`'s `RequestEvent` never carries a request body (verified at the same handback), so no observability surface exposes it, and the caller's own `JsonElement` is the only thing that could stringify unredacted. The cost this row holds open is forward-compatibility: once the field names are known, replacing `Fields` with typed `SecretString` members is a public-API change three languages will by then have mirrored, so the earlier it is taken deliberately, the cheaper — the same reasoning R-29 and R-32 already apply to their own unpublished-API changes. Evidence: [DR-0017](decisions/0017-m10-remaining-bindings-and-identity.md) D-M10-5 |
| R-37 | **The SDK's duration encoding is unverified against a real server at 24 of 25 call sites.** `Auth.Token.Create` was *measured* rejecting a numeric `ttl` — the server wants a string, while `05-authentication.md:182` says "(seconds)" and the SDK obeys it. The same `WriteSeconds` → `WriteNumber` convention is implemented six times (`TokenOperations`, `PkiOperations`/`PkiWire`, `LdapOperations`/`LdapWire`, `CertLifecycleWire`) across 25 call sites | R2 | **Open — owner decision pending** on DR-0021 F2's batch. **Stated as unverified, not as broken**: one endpoint is measured, the other 24 are untested and some may accept both forms. M12 slices 4–6 establish it as they exercise PKI, LDAP and cert lifecycle. The fix is a `specifications/` amendment (R3) if reality wins, not a unilateral SDK change ([DR-0021](decisions/0021-live-server-findings.md) F2, D-0021-2) |
| R-38 | **`FIX-010` is unmet across the whole fixture corpus.** Of 254 fixtures, **zero** were captured from a real server exchange: 130 are generated from Appendix B and **123 are hand-derived**, labelled `"BastionVault 0.42.x (derived from crates/… behaviour)"`. `FIX-010` requires bodies copied from a real exchange | R2 | **Open — new at M12 (2026-09-23).** This is the mechanical cause of every finding in [DR-0021](decisions/0021-live-server-findings.md): eleven milestones were verified against a corpus authored from the same document the corpus was meant to check, so the loop could only confirm the SDK matched the specification. Re-capturing 123 fixtures against a live server is a **milestone, not a slice**, and `R-35`/`PKI-030` establish that a capture nobody has is not one you may invent. Owner: unassigned, after M12 |
| R-39 | **A large share of the .NET public surface is governed by no requirement at all, and M11 is the first pass that measured it.** Of 438 operations tagged under `DOC-005`/`DOC-006`, **193 cite a specification *section file* rather than a requirement ID**, because no per-operation MUST exists to cite ([DR-0018](decisions/0018-m11-documentation-and-usage-guides.md) D-M11-21). Two surfaces dominate: **section 12's Rustion surface — 40 operations (sessions, targets, master, policy) governed by nothing**, where `RUS-001`/`RUS-002` govern recordings downloads and `RUS-003` maps error tokens naming no operation and has no `BV-RUSTION` throw site in the SDK; and **section 09's PKI at 50 fallbacks in 69 operations**. **This does not contradict M8, M9 or M10**, which booked and landed their requirement IDs correctly — a milestone closing every ID it booked is silent about operations that no ID covers, and that silence is exactly what the fallback count made visible. **Discovered by measurement, not by audit:** the count only exists because D-M11-21 refused to let a near-miss ID stand in for a missing one | R2 | **Open — owned by M14**, booked below rather than left to a later planning pass. **The precedent this row is written against is R-16's**, whose recorded lesson is that *a gap booked with no owner survives a milestone* — it was raised at M5 and reached M8 untouched. The count is not a defect to fix in code: the SDK behaves correctly, the specification is thinner than its surface. M14 decides per operation whether to mint a requirement ID, fold it under an existing one, or record that it is deliberately unspecified. **Blast radius is `specifications/`, so every change M14 makes is R3 under `CRS-004`'s first, non-vacuous limb** |
| R-40 | **An amendment can land without its implementation, and nothing detects it.** Found at M12's close, 2026-09-23: `09-pki-engine.md`'s Go-style duration rule and `crl_number`'s optionality were both live in `specifications/` — the latter since release `0.21.0` — while `PkiWire` still wrote integer seconds and still threw on an absent `crl_number`. Four of the five red integration scenarios were this, and the milestone record had attributed all five to something else ([DR-0021](decisions/0021-live-server-findings.md) fourth addendum, D-M12-25). **This is worse than the divergence it was meant to fix:** before the amendment the SDK disagreed with the *server*; after it, the SDK disagreed with its own *specification*, which is the document `CNF-001` is measured against. **Same family as R-10** — a record trusted instead of its execution — but in the other document, and R-10's mitigation cannot reach it: seeding a violation proves a gate fires, and no gate here was ever looking | **R2** | **Open — no owner yet.** The structural fix is that a `specifications/` amendment should not be mergeable without either its implementation or an explicitly recorded, dated gap, so the interval between the document changing and the code changing is a reviewable decision rather than an accident nobody is looking for. Note what did *not* catch it: traceability stayed green throughout, because both amendments used unnumbered normative prose (**F11** — a requirement ID cannot be minted in a specification-only change), so there was no ID for the ratchet to notice was uncovered. **F11 and this row are the same defect seen from two ends**, and fixing F11's ID-minting deadlock is a prerequisite for the obvious mitigation here. Only the live suite found it, which is the M12 lesson generalised: a loop that verifies a document against itself cannot fail |
| R-41 | **`ITG-S26`'s five unmet requirements are server-side, and the scenario is red with no SDK limb to fix.** Measured against `bvault` 0.44.5 in both managed and external mode, 2026-09-23: a group's policy is **not resolved into a member's token** on the member's next login (the member is then denied the shared secret, `BV-AUTHZ-001`), and none of `Sharing.ListByGrantee`, `ListByTarget` or `Sharing.ForMe` **indexes a group-target share**. The SDK's requests and parsing are both correct; the server returns nothing to parse | **R2** | **Open — needs an upstream server issue, filed by the project owner.** This is the single reason M12 does not meet acceptance criterion 2 as literally written, and it is recorded as unmet rather than resolved: an `ITG-031` skip would assert the scenario *could not run here*, which is false — it ran, and the server failed it. Reclassifying a server defect as an environmental skip is the inverse of `CLA-004` and the same failure D-0021-1 rejected at slice 5. **The scenario stays red until the server changes**, which is the control: the day the gap closes, the suite says so without anyone remembering to look |
| R-42 | **A requirement can be asserted by an executed test and still sit on the traceability baseline, because `tools/traceability/traceability.py` only sees .NET tests under a directory whose name ends in `.Tests`.** Found 2026-09-24 while clearing M11's untagged `DOC` IDs. `dotnet/BastionVault.IntegrationSdk.DocsSamples` is a real xunit project (`IsTestProject=true`, run in CI at `.github/workflows/dotnet.yml:71-72`) whose tests genuinely assert `DOC-003` (every documented C# sample is byte-identical to executed source), `DOC-011` (each guide's `hcl` policy equals `PolicyBuilder.Build()`) and, by running at all, `DOC-022` — yet a `[Trait]` marker there is invisible to the ratchet, so those IDs cannot come off the baseline without a parser-contract change. `DOC-005`/`DOC-006` have the same shape under `tools/doc-worksheet/tests/`. **The blindness is deliberate and load-bearing**: the DocsSamples csproj comment records that the harness was kept inside a `.Tests` directory precisely so the filter's view would not change silently, so widening it is a decision, not a fix. **Two further consequences, both measured rather than inferred:** `DocsSamples` is *not* run by `agents.md` §9's verification command, which names only the `.Tests` project — so those assertions execute in CI and never locally; and four more `DOC` IDs (`DOC-001`, `DOC-020`, `DOC-023`, `DOC-025`) are enforced by Python checkers invoked from `repo-gates.yml`, which no scanner sees either. **This is R-14's shape applied to traceability rather than conformance:** a milestone closed on content and the ratchet measured tagging, and nothing reconciled the two. | R1 | **Open — owner unassigned, and the decision it needs is not a code change.** The options are (a) widen the .NET directory filter to include any project with `IsTestProject=true`, (b) leave the filter and accept that some requirements are verified but permanently baselined, or (c) move or mirror the asserting tests into a `.Tests` project. Each changes what the `TST-041` ratchet counts, so it is an Architect call with a decision record, not a tagging pass — which is why this pass stopped at the three IDs it could clear honestly. Booked with an owner rather than left as a note, per **R-16**'s recorded lesson that an unowned gap survives a milestone |


## 9. Tracking

- **Per-milestone truth:** the traceability report (TST-041). A milestone is done when its
  requirement IDs move from uncovered to covered and stay there.
- **Per-commit truth:** the CI gate set from M0. A red gate is never weakened (CLA-004).
- **Known gaps:** `tools/traceability/baseline.json` remains the authoritative, machine-readable
  gap list. **Done at M4:** `dotnet/README.md` renders it into CNF-002 prose — the two
  section-07 gaps by requirement ID, the rest by section with counts and a pointer to the
  baseline. **Regenerate it from `baseline.json`, never hand-count it** — at M8 it was
  rewritten from the file and went 234 → **131**, and a hand-maintained count had already
  drifted. Note that a per-section count can *rise* while the total falls, because the
  specification gains requirement IDs (DR-0011 added `CNF-044`…`CNF-047`); say so in the
  README, or the number reads as the ratchet running backwards. It is updated at every Stage-1 milestone exit, and a milestone that
  changes the baseline and not the README has left the README wrong. Rust and Python gain their own READMEs and gap lists inside M13.
- **Parity:** during Stage 1 there is only one language to compare, so the three-way
  public-surface comparison (R-9) is dormant — it resumes as M13's per-block exit criterion,
  where it carries more weight than it ever did under horizontal slicing (D-1). Same names,
  same reachable capabilities, checked against .NET as the reference.
- **Decisions:** recorded once, where they belong, and linked thereafter (CLA-008). This file
  records only the sequencing decisions D-1…D-6; per-milestone design decisions belong in
  their own decision records — M0 in [`decisions/0001-m0-harness.md`](decisions/0001-m0-harness.md),
  M1a in [`decisions/0003-m1a-configuration.md`](decisions/0003-m1a-configuration.md).
- **Shipped truth:** [`CHANGELOG.md`](CHANGELOG.md). Every user-visible change gets an entry
  as it lands (REC-001); the roadmap says what is next, the changelog says what is done.
- **Keeping this file honest (REC-002).** A milestone exit updates §2 current state, its row
  in §4, its exit criteria in §5, and §8 where the milestone changed a risk. The milestone is
  not closed until that edit lands in the same change as the work. Claude owns both files;
  Engineering-tree agents propose entries, they do not edit them (REC-004, ENG-008).

## 10. Open questions for the project owner

**Index, current at 2026-09-24.** Eight questions are open and ten are answered. The
answered ones are kept in place with their original framing, because a question's framing is
what makes its answer legible later.

**Questions 15-17 were found by sweeping §8 and `decisions/`, not by any row raising its
hand** — which was the finding, and all three are now answered. Eleven risk rows had no live
owner (**D-7**, and gate **C8** so it is detectable next time); four measurements M12 was
authorised to take went untaken while a supported server sat idle for two days (the slice is
dispatched); and two closed milestones rested on records still reading *proposed* (the owner
confirms the reviews happened). **None of the three surfaced on its own**, which is the part
worth keeping: the register and the decision records can both describe a state that is not
the real one, and neither is detectable by reading the document that contains it.

| # | Question | Status | Blocks |
|---|----------|--------|--------|
| 1 | Target date and cadence | **Open** | Nothing; estimates stay order-of-magnitude |
| 2 | Release strategy — `0.x` previews or one `1.0.0` | Answered 2026-09-14 (D-1) | — |
| 3 | Live server access for M12 | Answered 2026-09-22/23 | — |
| 4 | When a conformance level is declared | Answered 2026-09-23 | R-14 closes on the declaration |
| 5 | Fixture conformance sweep before Stage 2 | **Open** — fold into 14 | R-19 |
| 6 | Reconcile section 14's endpoint table | **Open**, R3 | R-27; a Stage 2 parity trap |
| 7 | Specification vs real server, which moves | Answered 2026-09-23 (Rulings 1–2) | R-37 stays open for the fourteen |
| 8 | Do M12's three specification amendments stand | **Open**, R3 | M12's R3 gate |
| 9 | Does M12 close with `ITG-S26` red | **Open** | M12's exit, R-41 |
| 10 | Cut `0.24.0`; D-M5-26 carry-forward | **Open**, R3 | The tag |
| 11 | Unblocking `ITG-030`'s matrix | **Open** | Slice 7 |
| 12 | Measure the fourteen remaining durations | **Open** | R-37, and M13's transcription |
| 13 | Requirement-ID deadlock (F11) and R-40's owner | **Open** | R-40's mitigation |
| 14 | Re-capturing the fixture corpus | **Open** | R-38, and the shared `1.0.0` |
| 15 | Who owns the ownerless risk rows | Answered 2026-09-24 — D-7, M15, gate C8 | Eleven rows assigned |
| 16 | Take M12's untaken measurements now | Answered 2026-09-24 — slice dispatched | R-31, R-32, R-35, R-36 |
| 17 | Were M6 and M7 ever actually accepted | Answered 2026-09-24 — yes, reviews happened | — |
| 18 | `PKI-030`'s queue-cap recognition rule | **Open**, R3 | `PKI-030`, R-31, and a live wrong-family report |

**Four of the seven open questions gate M12's exit or its tag — 8, 9, 10 and 11.** Question
16 was the fifth and is answered: the D-M12-5 follow-up slice is running, which matters
because `PKI-030` is the only requirement-shaped thing between the SDK and a `Complete` claim
that is not documentation.

**Three are Stage 2 exposure** (12, 13, 14): each is a guess M13 would otherwise transcribe
into two more languages, the mechanism R-38 records as the cause of every divergence found so
far. **Two are about the register and the records themselves** (15, 17), and they matter for
the same reason the rest do — a row that reads *owned by M10* and a record that reads
*proposed* both describe a state that is not the real one, and neither is detectable by
reading the document that contains it.

1. **Target date and cadence.** This plan is sequenced but not calendared. Milestone
   durations depend on delegate throughput. Two datapoints now exist: M0, and M1a at 27
   requirement IDs across three languages in one session — design, pathfinder pass, two
   parallel passes, five review round-trips. M1b is roughly 45 IDs and carries more
   behavioural surface, so it is not a simple multiple of M1a; treat any estimate built on
   these two points as an order of magnitude, not a schedule.
2. **Release strategy — answered 2026-09-14.** Ship `Core` as a `0.x` preview at M4, or hold
   everything to a single `1.0.0` at M12 (now M13)? The project owner directed the staged
   plan in D-1: .NET ships `0.x` previews milestone by milestone through Stage 1 (M2b–M12),
   each an explicit exception to the shared-version rule per the `0.5.0` precedent
   (D-M2-15), and the shared `1.0.0` waits for M13. The gap lists (CNF-002) make each .NET
   preview honest about what it does not yet cover, including "Rust and Python" itself.
3. **Live server access for M12. — ANSWERED 2026-09-22/23, and it produced a new question.**
   The project owner installed `bvault` **0.44.5**, above `test-matrix.json`'s 0.42.0
   minimum, so managed-mode runs are real and no specification change was needed. Two
   residuals: `ITG-030`'s CI limb still needs the **0.42.0** image from
   `ghcr.io/ffquintella/bastionvault`, which returns `denied` here (slice 7 held); and the
   server immediately disagreed with the specification in four places. **The new question is
   §10.7** (numbered §10.6 when written, before the collision above was found)**.**
4. **When is a conformance level declared? — ANSWERED 2026-09-23 by the project owner, after
   costing four milestones.** The ruling is **(c), a third option neither (a) nor (b)
   anticipated: declare all three at M11 exit.** M11 lands sections 16–17, which is the
   only thing `CNF-002` was ever waiting for, so the blocker dissolves rather than moves.
   On M11's exit the Strategic Orchestrator audits `Core`, `Standard` and `Complete`
   against `CNF-001`/`CNF-014` and declares in `dotnet/README.md` whichever actually hold.
   **Sequenced behind M12's D-M12-4 audit**, which owes the `CNF-001`-vs-`CNF-014`
   reconciliation and must not be duplicated or contradicted here. Neither (a) nor (b) is
   taken: nothing is re-sequenced, and nothing is deferred to Stage 1's exit either.
   **R-14 closes on the declaration, not on this answer.** The four blocked gates — M4, M8,
   M10 and now M11 — are spent and stay spent; the row records the cost as measured, and
   the lesson stands that answering it once at M4 would have been cheaper than the four
   exits that paid for it. What follows is the original framing, kept for the record.

   **(original framing)** `Core`,
   `Standard` and `Complete` each require specification sections 16 and 17 (the `DOC`
   requirements), which are booked to **M11** — after M8's `Standard` and M10's `Complete`
   declarations. As sequenced, none of the three declarations is legal at the milestone that
   claims it, and **two milestones have now exited declaring nothing — M4 and, on 2026-09-18,
   M8** (R-14; DR-0009 D-M4-3, DR-0013 D-M8-6). Only M10's `Complete` is left to spend. Two
   answers, and it is a scope call rather than a design one:
   **(a) move M11 ahead of M8** — `Core` becomes declarable as soon as the documentation and
   guides 1–5 land, which is also when an external consumer can actually adopt the SDK;
   documentation is written against a smaller, more stable surface, and the cost is a
   milestone of prose before the next feature.
   **(b) move all three declarations to the end**, after M11, and let the `0.x` previews stay
   level-less with gap lists. Nothing is re-sequenced and the previews stay honest, but the
   SDK ships usable for a long stretch with no formal conformance claim, which is precisely
   what a level exists to communicate.
   Claude's recommendation is **(a)**: a conformance level whose documentation requirements
   are unmet is not a level, and deferring it to the end concentrates the one milestone whose
   content is hardest to parallelise at the point where the release pressure is highest.

5. **Does the fixture corpus need a conformance sweep before Stage 2? — new at M5.** M5 found
   two fixtures encoding a KV v2 response body that section 07 forbids (R-19), one landed
   since before M4 and one authored inside M5 itself. Both passed, for different incidental
   reasons. Fixtures are the mechanism that makes three languages agree, so a fixture that
   contradicts its own section is a defect that propagates: Rust and Python must reproduce
   these bodies exactly, and will fail on whichever ones their reader implements. Two
   instances in one milestone is weak evidence about 224 files, which is the question —
   **(a) sweep now**, as a small Engineering-tree milestone validating every fixture body
   against its section's type block, paying a slot before M13 to find the rest; or
   **(b) let M13 find them**, treating each as a Stage 2 parity defect at the point it fires.
   Claude's recommendation is **(a)**, and narrowly: the work is mechanical and cheap at the
   Haiku rung, whereas under (b) each instance surfaces as a confusing Rust or Python failure
   whose cause is in a shared artefact rather than in the code being written, which is the
   most expensive place to discover it. The counter-argument is real — (a) spends a slot on a
   corpus that may hold no further instances.

6. **Should section 14's endpoint table be reconciled with the sections that own its
   endpoints? — new at M8, and it has now bitten twice.** Implementing section 14 surfaced two
   places where it disagrees with the owning section or the endpoint catalogue: it writes
   `Page<NamespaceSummary>` where `06-system-api.md:203` says `Page<Namespace>` (D-M8-5), and
   it prefixes all seven `*-info` routes with `/v2/` where
   `appendix-a-endpoint-catalogue.md` gives `sys/namespaces-info` (`:40`) and
   `{mount}/roles-info` (`:221`) as **v1** (**R-27**). Both were resolved the same way — the
   owning section and the catalogue win — and both shipped that way in `0.13.0`.

   **Why this needs an answer rather than another per-instance ruling:** the two instances
   share one cause. Section 14 is a cross-cutting chapter describing endpoints that other
   sections define, and its table was never reconciled with them. Two of the seven `*-info`
   rows are known wrong; the remaining five are simply unchecked, and M9 and M10 build exactly
   those five. Each unreconciled row is also a forced guess a Stage 2 parity transcription
   would freeze into three SDKs, which is why it now sits in §2's list of items to close
   before the Rust pass opens.

   **(a) reconcile section 14's table against Appendix A and the owning sections now**, as one
   specification edit covering all seven rows — cheap while nothing is published, and it
   removes the question from M9's and M10's path. **(b) keep ruling per instance** as each row
   is implemented, accepting that M9 and M10 each spend review time rediscovering the same
   class of defect. Claude's recommendation is **(a)**: the cost is one pass over seven table
   rows, and under (b) the third instance is found by whoever is least expecting it. **This is
   a `specifications/` change and therefore R3 — it is the project owner's, not Claude's**
   (CRS-004, `agents.md` §5.4).

7. **When the specification and the real server disagree, which one moves? — ANSWERED
   2026-09-23.** *(Numbered 6 when written, colliding with the section-14 question above;
   renumbered to 7 at M12's close. The collision is itself an instance of the identifier
   rule — numbers are allocated centrally, not by reaching for the next free one.)*
   **The project owner ruled (a), the server is authoritative, and amended all**, which is
   broader than the recommendation below: F3 was to be filed as a server defect and the owner
   chose to amend it too, with that reasoning in view. `crl_number`'s optionality joined the
   same batch. On the rider: **Ruling 2 — amend `ITG-S01`, do not add `Client.ServerVersion()`**;
   `TRN-081` was later amended the same way for consistency, and then the owner ruled
   implement-over-amend on it after the D-M12-4 audit found it independently blocking `Core`
   (D-M12-22). The Strategic Orchestrator attached one constraint that is not a re-litigation:
   *amend all* means all the **findings**, not all 25 duration call sites — eleven are measured,
   fourteen are not, and **R-37** stays open for those. What follows is the original framing.

   **(original framing)** New at M12,
   and blocking four amendments. [DR-0021](decisions/0021-live-server-findings.md) found
   seven divergences in the first twelve live scenarios. Four are `specifications/`
   amendments and therefore R3 (`CRS-004`, `agents.md` §5.4): `ttl` as string vs number
   (F2), `Unmount` 500 vs 404 (F3), policy history `create` vs `write` (F4), userpass
   `token_policies` vs `policies` (F5). They are escalated **as one batch** because they
   share this single question. Three answers: **(a) the server is authoritative** — the
   specification was written ahead of the implementation and reality wins; **(b) the
   specification is authoritative** — these are server defects to file, and the scenarios
   stay red; **(c) case by case.** Claude's recommendation is **(a) for F2 and F4**, which
   are plainly descriptive errors where the document guessed a wire detail, and **(c) for F3
   and F5** — a `500` for an idempotent delete of an absent mount is arguably a server
   defect the error model has opinions about, and `policies` may have been intended as an
   accepted alias. **A second, smaller question rides along:** `ITG-S01` requires
   `Client.ServerVersion()`, which does not exist in `PublicApiSurface.txt` — add the member
   (public API shape, parity owed at M13) or amend the scenario to describe what the SDK
   offers?

8. **Do the three specification amendments from M12's close stand? — new at M12, 2026-09-24.
   This is the one that most needs you, because one of the three was not yours.** All three
   are `specifications/` changes and therefore R3 (`CRS-004`, `agents.md` §5.4).
   - **The `pki/*` and `auth/token/create` duration amendments** — yours directly, under
     Ruling 1. Both limbs measured. The SDK now implements them.
   - **`crl_number`'s optionality** — also yours, named in Ruling 1's batch. Now implemented.
   - **`certs-info`'s optionality (`PKI-022` proposed)** — **not yours.** Measured on
     2026-09-23, *after* Ruling 1 was given: a `certs-info` row omits `source`, `is_orphaned`
     and `key_id` entirely, and the SDK threw `BV-PROTOCOL-002` on `source`. The Strategic
     Orchestrator landed it by applying Ruling 1's principle — *the server is authoritative* —
     to a case the ruling could not have named, in the same way the third addendum applied
     Ruling 2's principle to `TRN-081`. **That is a judgement call about the scope of your
     mandate, and it is flagged rather than assumed.**

   **(a) Confirm all three.** The principle was stated generally and this is an instance of it.
   **(b) Confirm the two you named, and treat `certs-info` as overreach** — it is reverted and
   re-asked as its own question, with the scenario red in the meantime. **(c) Case by case.**
   Claude's recommendation is **(a)**, with the reservation stated plainly: the cost of (a) is
   that "apply the owner's principle to new instances" becomes precedent, and precedent of that
   shape is how a mandate widens without anyone deciding to widen it. If you would rather that
   *every* specification change come back to you regardless of how obviously it follows from a
   ruling you gave, say so and the default flips — that is a cheap rule to hold and an
   expensive one to reconstruct after the fact.

9. **Does M12 close with `ITG-S26` red? — new at M12, 2026-09-24.** DR-0019's acceptance
   criterion 2 says every scenario passes or skips with an `ITG-031` reason. `ITG-S26` does
   neither: it fails on five unmet requirements that are **entirely server-side** (**R-41**) —
   a group's policy is not resolved into a member's token on the member's next login, and none
   of the three sharing list routes indexes a group-target share. The SDK's requests and
   parsing are both correct. Reproduced in managed *and* external mode.
   **(a) Accept the close with criterion 2 formally unmet**, file the server issue upstream,
   and leave the scenario red until the server changes. **(b) Hold M12 open** until it goes
   green, which makes a .NET milestone's exit depend on a server fix nobody here controls.
   **(c) Convert it to an `ITG-031` skip** so the criterion reads green.
   Claude's recommendation is **(a)**, and **(c) should be refused**: an `ITG-031` skip asserts
   the scenario *could not run here*, which is false — it ran, and the server failed it. A red
   scenario with a named off-SDK cause is the only report that stays true, and it is its own
   control: the day the server gap closes, the suite says so without anyone remembering to look.

10. **Cut `0.24.0`, and does the D-M5-26 carry-forward clear? — new at M12, 2026-09-24.** M12's
    work is on `main` under `## [Unreleased]`; no tag was cut, because a release is
    outward-facing and R3 and this work has no confirmation from you (`v0.23.0` had yours
    explicitly, D-M12-24). **Two things ride on the same answer.** First, D-M5-26 has carried a
    release-checklist line since M5: before the M12 release is cut, you must confirm the
    `resilience.failover.read-once` fixture repair, which is a `specifications/` change and
    therefore R3. It has never been confirmed. Second, `0.24.0` would be another **.NET-only
    interim tag** under the `0.5.0` precedent (D-M2-15), with `rust/` and `python/` left at
    `0.5.0` — an explicit recorded exception, never a redefinition of what `1.0.0` means.
    **(a) Cut `0.24.0` now**, with the D-M5-26 confirmation recorded alongside it.
    **(b) Hold the tag until slice 7 clears**, so the release carries a real `ITG-030` matrix
    run. **(c) Hold until `ITG-S26` goes green**, which ties the tag to question 9.
    Claude's recommendation is **(a)**: the work is verified, the records are current, and
    holding a tag for a blocker that is a registry credential (question 11) or someone else's
    server (question 9) buys nothing a gap list does not already say honestly.

11. **How does `ITG-030`'s per-version matrix get unblocked? — new at M12, 2026-09-24.** Slice
    7 is the only slice of seven not delivered, and its blocker has been re-measured rather
    than assumed on each of the last two milestone gates: `ghcr.io/ffquintella/bastionvault`
    returns `DENIED` to an anonymous pull token, and D-M12-15 Ruling A puts `ITG-030`'s whole
    per-version obligation on the **container** path, so the local 0.44.5 binary satisfies
    `ITG-002` and can never satisfy `ITG-030`.
    **(a) Supply a `read:packages` PAT** and run `docker login ghcr.io` — one command, and the
    container path is otherwise ready and has still never been executed. **(b) Make the package
    public**, which removes the credential from CI's path permanently as well as this machine's.
    **(c) Re-rule D-M12-15 Ruling A** so a pinned **binary** per version can satisfy `ITG-030`,
    which needs a supported way to obtain 0.42.0 as a binary and is a weaker guarantee than an
    image digest. **(d) Record `ITG-030` as unmet in this environment** and move on.
    Claude's recommendation is **(b)** over (a): a private package makes every future CI run,
    and every contributor, depend on a secret somebody has to hold — and nothing in this
    repository publishes yet, so the usual reason to keep it private does not apply. (a) is the
    fast version of the same fix and is fine if (b) is not wanted.

12. **Do the fourteen unmeasured duration call sites get measured now? — new at M12,
    2026-09-24 (R-37).** A server exists, and measuring one field costs a single request — the
    whole F2 amendment was settled by *five* requests in four minutes. Eleven sites are
    measured; fourteen are not: `sign-verbatim` and `approve-verbatim` `ttl`,
    `intermediate/generate` `ttl`, `not_before_duration`, the CRL config `expiry`, tidy's
    `safety_buffer` and auto-tidy `interval`, and the `ssh/*` and `totp/*` durations (the last
    two are measured *accepting* numbers, so the engine is known non-uniform and no blanket
    rule can be inferred).
    **(a) Measure all fourteen now**, as one small Engineering-tree pass, and amend or confirm
    each on evidence. **(b) Leave R-37 open to M13**, where Rust and Python will transcribe
    whatever .NET does today. Claude's recommendation is **(a)**: under (b) each unmeasured
    site is a guess about to be frozen into three SDKs, which is precisely the mechanism
    **R-38** records as the cause of all fourteen divergences found so far. The counter-argument
    is honest — (a) spends a slot on sites that may all turn out fine.

13. **Does the requirement-ID deadlock get fixed, and who owns R-40? — new at M12, 2026-09-24.**
    Two findings share one root. **F11**: a new requirement ID cannot be minted in a
    specification-only change, because `traceability.py` draws its applicable set from Appendix
    D — an ID omitted from Appendix D makes that document falsely claim to be generated from
    the spec, and an ID added to it fails the ratchet because nothing covers it. So every
    amendment so far has used unnumbered normative prose, and **seven IDs are now proposed but
    unmintable**: `PKI-003`, `PKI-012`, `PKI-021`, `PKI-022`, `SYS-027`, `SYS-044`, `TRN-044`.
    **R-40**: an amendment can land with no implementation and nothing detects it — which is
    exactly what happened twice and cost four red scenarios. **The two are the same defect from
    opposite ends:** R-40's obvious mitigation is a gate that notices an amended rule with no
    covering test, and that gate cannot exist while amendments carry no IDs.
    **(a) One follow-up change that mints all seven IDs in Appendix D together with the tests
    claiming them**, which is legal today and unblocks R-40's mitigation. **(b) Fold it into
    M14**, which already owns triaging the 263 fallback-tagged operations. **(c) Leave the rules
    as unnumbered prose** and accept that they are normative but untraceable.
    Claude's recommendation is **(a) then (b)**: the seven are already written and measured, so
    minting them is transcription rather than design, and leaving them until M14 leaves R-40
    unmitigated across the whole of Stage 2 — the period when a stale amendment gets copied into
    two more languages.

14. **When does the fixture corpus get re-captured, and does question 5 fold into it? — new at
    M12, 2026-09-24 (R-38).** Of 254 fixtures, **zero** were captured from a real server
    exchange: 130 are generated from Appendix B and 123 are hand-derived. `FIX-010` requires
    bodies copied from a real exchange, so `FIX-010` is unmet corpus-wide. **This is the
    single-sentence explanation for every divergence M12 found**: eleven milestones were
    verified against a corpus authored from the same document the corpus was meant to check, so
    the loop could only ever confirm the SDK matched the specification.
    **Question 5 above is the smaller version of this question** and should be answered with it:
    validating fixture bodies against their sections (question 5) and re-capturing them from a
    real server (this one) are the same pass done twice if taken separately, because a
    re-captured fixture is correct by construction.
    **(a) One milestone before M13** that re-captures the 123 hand-derived fixtures against
    `bvault` 0.44.5 and retires question 5 with it. **(b) Inside M13**, as each language's
    parity block meets the fixtures it needs. **(c) After Stage 2.**
    Claude's recommendation is **(a)**: under (b) a wrong fixture surfaces as a confusing Rust
    or Python failure whose cause is in a shared artefact rather than in the code being written,
    which is the most expensive place to find it — and under (c) the shared `1.0.0` would ship
    on a corpus that has never met a server. The real cost of (a) is a milestone slot, and
    `R-35`/`PKI-030` set the limit on it: **a capture nobody has is not one you may invent**, so
    any fixture that cannot be driven against a real server stays uncaptured and is recorded as
    such rather than hand-written to fill the gap.

15. **Nine risk rows have no live owner, and five of them look owned. Who takes them? —
    ANSWERED 2026-09-24.** The project owner directed: **assign the most indicated risk
    owner.** Done, and the sweep widened to **eleven** rows on the way — R-18 and R-25 name
    owners (M6, and "the count-derivation session") that read as live and are not. Recorded as
    **D-7**, which states the assignment rule and creates **M15** for the rows that are cheap
    in one language and expensive in three. **R-17, R-18, R-22 → M14** (all three need a
    `specifications/` change). **R-24 → M13** (it is Rust code, and only M13 may touch it).
    **R-25, R-30, R-32's typed records, R-33 → M15.** **R-31, R-35, R-32's soft edges, R-36 →
    the D-M12-5 follow-up slice** (question 16). The standing control matters more than the
    assignment: `scripts/validate-agent-docs.py` check **C8** now fails a milestone marked ✅
    while a risk row still names it as owner — seeded both inside and outside its corpus, with
    the three phrasings it cannot see enumerated in the check itself. What follows is the
    original framing.

    **(original framing)** New
    at M12's close, 2026-09-24.** Found by sweeping §8 rather than by any row raising its
    hand, which is the point. Two distinct failures, one symptom:
    - **Orphaned to a closed milestone** — the row names an owner that has exited, so it reads
      as owned to anyone scanning: **R-30** ("an M9/M10 candidate" — both closed),
      **R-31**/`PKI-030` ("owned by M10"), **R-32** ("owned by M10"), **R-35** ("owned by
      M10"), **R-36** ("whichever milestone next has the server capture" — that was M12, now
      closing). M0–M11 are all ✅ and M12 closes here.
    - **Never owned at all** — **R-17** (`DSC-033` cannot prefer healthy nodes without a
      specification change), **R-22** (`RES-001`'s cap and `DSC-042`'s replay cannot both
      hold), **R-24** (Rust's `FIX-001` fixture validation validates nothing), **R-33**
      (`ErrorPaths.Redact` ignores query strings — latent, not active).

    **This is R-16's recorded lesson recurring at scale.** That row's own stated moral is that
    *a gap booked with no owner survives a milestone*; it was raised at M5 and reached M8
    untouched. The register has since acquired nine more of the same shape, and the orphaned
    five are worse than the unowned four, because "owned by M10" reads as handled.
    **(a) One carried-debt milestone** that triages all nine and assigns or closes each.
    **(b) Fold them into M13**, which is already the milestone that will trip over R-24 and
    R-28. **(c) Accept "unowned" as a resting state** for R1/R2 rows with no exit dependency,
    and say so explicitly in the row so it stops reading as pending work.
    Claude's recommendation is **(a) for the five orphaned rows and (c) for the four unowned
    ones**, with (c) written into each row rather than left implicit — the cost of the current
    state is not that the work is undone, it is that nobody can tell the difference between
    undone and unassigned by reading the register. **A standing control is worth more than
    either:** a milestone should not be marked ✅ while a risk row still names it as owner.

16. **M12 had a supported server for two days and four booked measurements were never taken.
    Are they taken now? — ANSWERED 2026-09-24: the project owner said fix it.** The D-M12-5
    follow-up slice is dispatched. What follows is the original framing.

    **(original
    framing)** New at M12's close, 2026-09-24.** D-M12-5 held two requirements back
    for want of a real server and ruled they would be *dispatched as a follow-up slice* the
    moment a **supported** one existed. Ruling 2 confirmed it. `bvault` **0.44.5** has been
    installed since 2026-09-22 — above the 0.42.0 matrix minimum — **and the slice was never
    dispatched.** Neither the decision record nor the risk rows record the decision point being
    reached and passed over. Four things are affected, all measurable in minutes:
    - **`PKI-030`** (**R-31**) — needs the server's `BV-QUOTA-002 QueueFull` message string,
      which no document states. Still on the traceability baseline. **It bars `Complete`.**
    - **`R-35`** — `identity.self` is in Appendix C's mandatory fixture set with no capture to
      author from. `specifications/fixtures/identity/` still holds exactly one file, and it is
      not this one.
    - **`R-32`'s two soft edges**, both explicitly *booked to M12's integration suite*: whether
      `Pki.ReadKey`/`Pki.Csr.Read` return private material for an object created
      `exportable: true`, and whether `Pki.GenerateIntermediate` accepts `issuer_name` at all
      (D-M9-23 keeps it on whole-set-reuse grounds while recording that §09 points the other
      way). **No integration scenario mentions `exportable` or `issuer_name`.**
    - **`R-36`** — `Files.Sync`'s per-target credential wire names, booked to "whichever
      milestone next has the server capture". **No scenario mentions `SyncTarget`.**

    **(a) Dispatch the D-M12-5 follow-up slice now**, before the tag, and take all four
    measurements while a server is running. **(b) Book them to a carried-debt milestone** with
    question 15. **(c) Leave them held back** and record explicitly that the opportunity was
    declined, so the next reader does not rediscover it as a surprise.
    Claude's recommendation is **(a)**: D-M12-5 already authorised the slice on exactly this
    precondition, so this is executing a decision rather than taking a new one, and `PKI-030`
    is the only requirement-shaped thing between the SDK and a `Complete` claim that is not
    documentation. The constraint from Ruling 2 holds unchanged — **every captured fixture
    records the server version it came from**, and anything that cannot be driven against a
    real server stays uncaptured rather than invented (`R-23`, `R-35`'s own lesson).

17. **Were M6 and M7 ever actually accepted? — ANSWERED 2026-09-24.** The project owner
    confirms **the reviews happened**. `DR-0011` and `DR-0012` now read *accepted*, each with
    a note recording that the status line was corrected on the owner's confirmation rather
    than inferred from the milestone being green. **Neither `revision` counter moved**:
    **REC-007** ties it to architecture-review rounds, and this was not one. What follows is
    the original framing.

    **(original framing)** `DR-0011`
    (M6) and `DR-0012` (M7) both still read **"Status: proposed — awaiting Strategic-tree
    Claude Opus 5 architecture review"**, while `DR-0013`, `DR-0016` and `DR-0017` all read
    *accepted* with a date. Both milestones are marked ✅ in §4 **citing those records as their
    evidence**. Either the review happened and two status lines were never updated, or two
    milestones closed on records that never passed their gate.
    **The Strategic Orchestrator cannot answer this by inspection** — nothing in either file,
    or elsewhere in the repository, records a review that happened, and writing "accepted" on
    the strength of the milestone being green would be asserting a gate was passed because its
    outcome was assumed. That is the exact failure **R-10** exists for.
    **(a) You confirm the reviews happened**, and the two status lines are corrected to
    *accepted* with your date. **(b) They did not**, and the two records go through the review
    they never had — which may find nothing, and is cheap next to two closed milestones resting
    on an unverified gate. **(c) Rule that a milestone marked ✅ constitutes acceptance** of its
    governing record, and the status line becomes derived rather than authoritative.
    Claude's recommendation is **(a) if you remember, (b) if you do not**, and against (c): it
    would make the status line unable to distinguish "reviewed" from "shipped", which is the
    distinction the field exists to carry. **A standing control falls out of this either way:**
    `scripts/validate-agent-docs.py` could refuse a milestone marked ✅ whose governing decision
    record still reads *proposed*, which is a mechanical check for exactly this drift.

18. **`PKI-030`'s queue-cap error is currently reported as the wrong error family. What is the
    recognition rule? — new 2026-09-24 (R-31), R3.** The D-M12-5 follow-up slice drove the
    500-request cap for the first time. The server answers **HTTP 429 with no `Retry-After`**:

    ```
    {"error":"sign-request/import: 500 requests are already awaiting a decision on this
      mount; decide or delete some before importing more"}
    ```

    R-31 said this string existed in no document. It does now. **But the measurement found a
    second and worse thing:** `BV-QUOTA-002 QueueFull` has **no recognition row** in Appendix B
    §2, so nothing matches this response, and a 429 *without* `Retry-After` falls through the
    generic rule at `04-error-model.md:101` to **`BV-RATE-002 NamespaceRateQuotaExceeded`**.
    **A caller who fills the approval queue is told they are being rate-limited.** The
    remedies are opposite: back off and retry, versus decide or delete the pending requests.
    Backing off never clears a full queue. Appendix B's hinted wording (*"the server queue is
    full"*) and its promised structured `Details.max` are also both absent from the wire.

    **(a) Add a recognition row** for `BV-QUOTA-002` keyed on a stable fragment of the measured
    message (`"already awaiting a decision"` is the load-bearing part; the leading
    `sign-request/import:` is route-specific and the 500 is configuration), **ordered ahead of
    the generic 429 fallback**, and correct `09-pki-engine.md:172`'s message and
    `appendix-b-error-catalogue.md:128`'s hint to what the server actually sends.
    **(b) Keep recognition by status alone** and accept that a queue-cap breach reports as a
    rate limit, documenting the conflation. **(c) Ask for a server change** so the response
    carries a machine-readable discriminator instead of a prose message.
    Claude's recommendation is **(a) now and (c) as the durable fix**. (a) is what makes
    `PKI-030` implementable at all — it has been held back since M9 for exactly this — and the
    ordering matters as much as the row: placed after the generic fallback it would never be
    reached. (c) is better than (a) in the long run because recognition-by-message is brittle
    across server versions, which is the same fragility `R-23` already recorded; it is listed
    second only because it depends on someone else's release. **(b) should be refused**: an
    error model whose whole purpose is a stable code and a useful remedy is not entitled to
    report the wrong remedy because the right one is inconvenient to recognise.

    **`PKI-030` stays on the traceability baseline** until the rule lands **with a test naming
    the ID**. Taking an ID off the baseline without covering it is the `CLA-004` failure this
    project has already paid for once.
