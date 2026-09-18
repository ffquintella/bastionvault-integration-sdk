# DR-0014 — R-16: shipping an SRV resolver, and the silent discovery degradation

**Status:** **accepted** — authored by the Strategic Orchestrator (Claude Opus 5), reviewed
by a Strategic-tree Claude Opus 5 agent (`agents.md` §4.2 row 4), **revision 1** — one
architecture-review round, which returned *approve with required fixes*. All six required
fixes are folded in below and the dispositions, including the one finding ruled against,
are in the closing section. Decision A was ruled by the project owner.
**Risk tier:** **R3** — raised from the R2 the task was framed at. See the ruling below.
**Milestone:** closes `ROADMAP.md` §8 risk **R-16**, currently owned jointly by M11 and M12
· **Date:** 2026-09-18
**Supersedes nothing. Amends:** `specifications/13-cluster-discovery-and-resilience.md`
(pending human confirmation), `ROADMAP.md` §8's R-16 row and the M11/M12 rows.
**Inherits:** [DR-0010](0010-m5-cluster-discovery-and-resilience.md) — D-M5-23 is the
decision this record reopens; D-M5-8 pins `Candidate.Url` as a string and priority/weight
as nullable, which a shipped resolver must honour unchanged.

## Problem

M5 landed the whole of section 13 in .NET except one thing: nothing implements
`ISrvResolver`. `dotnet/BastionVault.IntegrationSdk/Discovery.cs:75-84` defines the seam
and ships no implementation, because the .NET BCL exposes no DNS SRV API and `DSC-014`
requires only that the resolver be **injectable**, not that one ship. D-M5-23 booked the
gap as R-16 and assigned it to M11 (documentation must say a resolver is required) and M12
(the live suite cannot reach a cluster without one).

Two things have changed since that booking, and together they are why R-16 is being closed
early rather than at M11.

**The project owner has confirmed that cluster awareness and fail detection are core to
this library's purpose.** A core capability whose every consumer must first write a DNS
wire-format parser is not a shipped capability. D-M5-23's reasoning was sound for M5's
scope — it correctly refused to absorb a supply-chain decision into an unrelated milestone
— but it assigned the gap to milestones chosen for *where the symptom shows up*
(documentation, live testing) rather than for where the defect is.

**Nothing is published yet.** No workflow pushes to NuGet, crates.io or PyPI:
`.github/workflows/build-artifacts.yml` builds artefacts and never pushes them, and there
is no publish step in `dotnet.yml`, `rust.yml`, `python.yml` or `repo-gates.yml`. The
`v0.2.0`…`v0.11.0` tags are repository releases, not registry releases. So the discovery
default can still be changed for free. After Stage 1 ships it is a breaking change to a
released consumer. **The cheapest moment to fix R-16 is now, and it is closing.**

## The defect is two defects

The task framed R-16 as one question — ship a resolver or make the degradation loud. It is
two, they are independent, and conflating them is how the second one survives.

**Decision A — does a resolver ship, and at what dependency cost?** The .NET SDK currently
has **zero** runtime NuGet dependencies; its `.csproj` carries no `PackageReference` at all,
and D-M1b-19 removed the one analyzer package it used to have rather than keep a gate that
looked green and was not. Rust pins thirteen exact versions, Python has two. So a DNS
dependency is a different-sized decision in each language, and in .NET it would be the
first. For a library that handles credentials, dependency posture is a security argument,
not a matter of taste.

**Decision B — is silent degradation legal at all?** `DSC-011` folds resolver failure into
"no records". `DSC-012` turns "no records" on a non-SRV-shaped name into exactly one
literal candidate — no error, no warning, no signal on any public surface. An application
that configures discovery and supplies no resolver therefore gets literal single-address
behaviour *where it asked for discovery*, and cannot detect that it did.

**And it loses failover, which is worse than the address story.** `DSC-042` arms failover
only when discovery produced two or more candidates. `DSC-012`'s synthesised literal
candidate is exactly one. So silent degradation does not merely pin the wrong address — it
**silently disables the single bounded failover replay as well**, which is the resilience
half of what the project owner called core. An operator who configured a cluster name for
fail detection gets neither node selection nor failover, and no signal that either is
missing. Found at architecture review; it is the strongest single argument for Decision B.

**The seam conflates three states, not two.** `DiscoveryEngine.cs:563-580` returns `[]`
when no resolver is supplied, `[]` from the catch-all when a resolver throws, and `[]` when
the SDK's own `ResolveTimeout` cancels. Byte-identical, so `DSC-016` cannot be a single
boolean: operator remediation differs for each (configure a resolver / fix DNS / raise the
timeout).

**B is the more serious defect, and B is not fixed by A.** A missing implementation is a
gap an application can fill once it knows. `DSC-012`'s silence is a gap an application
cannot *detect*, which is why it has survived a full milestone and its architecture review.
And it survives shipping a resolver: a real resolver that receives a genuine NXDOMAIN, or
whose UDP query is dropped, takes the byte-identical silent path. **B must be fixed whether
or not A ships anything.** Recorded here so no delegate treats B as A's consequence.

## Risk tier: R3, not R2

The task was framed R2 (new cross-language public contract plus a dependency decision).
The tier is **R3**, and it rests on **one** limb, which is dispositive on its own:

**`CRS-004` — the `specifications/` limb.** Closing R-16 changes section 13 either way.
Decision B cannot ship without new requirement IDs, and a shipped default resolver changes
what section 13's default behaviour *is*. `CRS-004` makes any `specifications/` change R3
without qualification, so nothing else has to hold.

Two further factors corroborate at **R2** and are recorded as R2, not as R3 triggers. An
earlier draft claimed three independent R3 limbs; architecture review found that claim
wrong on both of the other two, and it is corrected here rather than carried:

- **`agents.md` §5.3 blast radius — R2, and narrower than drafted.** A resolver changes how
  every discovery path picks a node, but **Rust and Python have no discovery module at
  all** (verified: no discovery or SRV source file in either tree, no `ISrvResolver`), so
  the seam exists in one language. The draft invoked `CRS-006`, which scores *unknown*
  blast radius high; the radius here is **known**, so `CRS-006` was the wrong rule. This is
  ordinary R2 cross-language public API shape.
- **`CRS-003` — the TLS floor, R2.** `RES-010` and `CFG-043` make the SRV target the SNI and
  hostname-verification name, so a resolver chooses which certificate name gets verified.
  `CRS-003` is an R2 *floor*, not an R3 trigger, and the draft's attempt to combine it with
  the blast-radius limb to reach R3 was self-contradictory.

A tier resting on one sound limb is stronger than a tier resting on three of which two do
not hold. The outcome is unchanged — R3 either way — but the reasoning is now checkable.

What R3 requires, per §5.3: the R2 gates (Architect decision record plus three-language
parity check), **plus** Claude Opus 5 review, **plus** Strategic Orchestrator acceptance,
**plus human confirmation before release**. `CRS-005` pauses R3 work on detection and
routes it to the Strategic Orchestrator — that is this record.

The tier is recorded here, before dispatch, rather than discovered at handback
(`agents.md` §4.3 rule 4).

## Routing classification

Design generation for this record is **row 3** of `agents.md` §4.2, on condition (a): the
pathfinder pass that first defines a contract, in the first language. The §4.2
discriminator asks whether the contract is settled, and it is not — no accepted decision
record pins a resolver's public names or behaviour, and D-M5-23 pinned only the *absence*
of one. Conditions (b) and (d) also hold: a new cross-language public contract, and a
change spanning `specifications/`, three language trees and the records.

Per `skills/claude/SKILLS.md` §3 row 4, design for a new subsystem is **generated** by an
Engineering-tree Claude Opus 5 agent and **reviewed** by a Strategic-tree Claude Opus 5
agent at handback (**REV-002**: the gate is never the author re-reading its own work). The
escalation trigger is recorded above, before dispatch, not after.

The implementation that follows this record is **row 2** — a settled contract, once this
record is accepted — and the Rust and Python passes are row 2 parity passes by definition.

## Coordination: what this session did not do, and why

M8 is in flight in a separate session and owns `dotnet/`, `ROADMAP.md`, `CHANGELOG.md`,
`decisions/0013-*` and the Transit/TOTP fixture trees. Confirmed with that session directly
before any work started, per the task's instruction not to start while M8 is in flight.
Its rulings, accepted:

| Deferred | Owner / reason |
|----------|----------------|
| `ROADMAP.md` R-16 row and M11/M12 rows | M8's session is mid-edit in the file for M8's own exit. **REC-004** makes both records Strategic-tree owned; this session hands over proposed text rather than writing it |
| `CHANGELOG.md` line | Same |
| `dotnet/.../Discovery.cs` | M8 **slice d** touches the discovery probe path — `EFF-005` exempts health probes from the client rate gate, and slice d lands the token bucket that probing must honour. A resolver adds DNS I/O that is also un-gated, so the design must follow slice d's seam rather than inventing a second one. Waiting on slice d specifically, not on M8 generally |
| `specifications/13-*.md` | R3 under `CRS-004`. Goes to the project owner, not into a delegation |

This session runs in its own git worktree (`claude/brave-kirch-3eebff`, branched from
`main` at 42dd778), so it does not share the checkout that two other sessions are writing.
It therefore does **not** see M8's uncommitted slice a — the regenerated error catalogue and
the corpus count's move from 230 to 236 — and any implementation must rebase on `main`
after M8 merges rather than reason from this tree.

**Not absorbed into this record:** the fixture-count assertions are hand-maintained and
have gone stale twice in one session. A separate session has that booked as an R2
follow-up with its own decision record, gated behind the M8 merge. A shipped resolver will
move the corpus count again in the same assertions, but fixing the count *design* here
would widen an already-R3 change (**CLA-007**). R-16 updates the counts; it does not
redesign them.

## Forces

- **Cluster awareness is core** (project owner). This is what reopens D-M5-23.
- **Cross-language behavioural parity is a hard project goal** (`CLA-003`, **VER-002**).
  Three languages each taking their idiomatic DNS library means three different
  search-list, trailing-dot, truncation and empty-vs-NXDOMAIN behaviours at the SDK's
  observable surface.
- **Zero runtime dependencies in .NET is a deliberate, documented posture**, not an
  accident of youth.
- **A hand-rolled parser consumes untrusted network input.** Compression-pointer loops,
  label-length abuse and response spoofing are the classic failure modes and are the real
  cost of the zero-dependency option.
- **`DSC-014` is already satisfied.** The seam exists; only the default is in question.
  Whatever ships must implement the existing interface unchanged (D-M5-8).
- **The window is closing.** Free before publication, breaking after.

## Decisions

Decision A was ruled by the **project owner** (dependency posture and supported
environments are outward-facing and R3, so `agents.md` §5.4's last row sends them to a
human). Decision B's shape was generated in the Engineering tree and ruled here.

- **D-R16-1 (R-16 closes now; M11 and M12 stop owning it).** D-M5-23 assigned R-16 to the
  milestones where the *symptom* appears — M11 documents it, M12 trips over it — not to
  where the defect is. Two facts move it: cluster awareness is core to the library
  (project owner), and nothing is published, so the discovery default is free to change
  today and a breaking change after Stage 1. M11 keeps only the documentation obligation
  that survives a shipped resolver; M12 keeps nothing of R-16, because a shipped resolver
  is what its live suite needed.

- **D-R16-2 (the tier is R3).** Ruled above, on three independent limbs. Recorded before
  dispatch (`agents.md` §4.3 rule 4), not discovered at handback. One qualification found
  after drafting: **Rust and Python have no discovery module at all** — verified, there is
  no discovery or SRV source file in either tree and no `ISrvResolver` — so M5 was
  .NET-only and `CRS-006`'s blast-radius limb is presently one language. Limb 1
  (`CRS-004`, the `specifications/` change) is unaffected and sufficient on its own.

- **D-R16-3 (two decisions, and B does not wait for A).** Recorded above. The operational
  consequence: **B ships first and separately.** A delegate must not treat B as A's
  consequence, and a reviewer must not accept A without B.

- **D-R16-4 (Decision B — the loudness contract, three new requirement IDs).**
  `DSC-015`…`DSC-017` sit directly after the `DSC-011`/`DSC-012` whose silence they fix,
  inside section 13's existing *SRV discovery* block:

  | ID | Rule |
  |----|------|
  | `DSC-015` | Degrading to a synthesised literal candidate MUST emit a warning through the client logger, naming the cluster name and the reason (no resolver supplied / resolver returned no records / resolver failed) |
  | `DSC-016` | The degradation MUST be **programmatically observable**, not only loggable. The discovery surface carries the *cause*, not a boolean — the seam conflates no-resolver, resolver-threw and resolve-timeout, and `DSC-001`'s legitimately configured literal mode must be distinguishable from all three |
  | `DSC-017` | Strict discovery: when set, a cluster name that yields no SRV records MUST raise rather than synthesise a literal candidate. **It defaults to strict.** It overrides `DSC-011`'s non-propagation and `DSC-012`'s synthesis, both of which gain an explicit exception clause naming it |
  | `DSC-018` | The degradation cause MUST be recomputed by `DSC-046`'s `Reconnect()`, so a cleared condition clears it and a re-degraded run re-sets it |
  | `DSC-019` | Strict-mode refusal MUST carry its **own error code**, distinct from `BV-DISCOVERY-001`'s SRV-shaped-name-with-no-records case |

  `DSC-016` is the load-bearing one, and it is a **cause, not a boolean**. A warning an
  application never reads is not a fix; the caller needs to be able to *ask*, and a flag
  that only says "something happened" sends an operator to the wrong remedy. `CNF-031`
  forbids secret material in log output and a cluster name is not secret, so `DSC-015` is
  safe.

  `DSC-018` and `DSC-019` were both added at architecture review. Without `DSC-018` the
  flag's lifetime across `DSC-046` is undefined, which is a guaranteed three-language
  divergence at M13. Without `DSC-019` strict mode reuses `BV-DISCOVERY-001` and conflates
  two different operator problems — and it is the item with the widest blast radius in
  Decision B, because a new code regenerates the error catalogue in all three trees and so
  **collides directly with M8 slice a**, which has that catalogue regenerated and
  uncommitted. `DSC-019` lands after the M8 merge, not before.

  `DSC-001` deserves its own note: `ClusterDiscovery = false` forces literal mode
  legitimately. If `DSC-016` fires on that, it fires on correct configuration — and an
  operator who sees it on every correct startup learns to ignore it, which leaves the real
  degradation as invisible as it is today, through habituation rather than silence.

- **D-R16-5 (`DSC-017` defaults to strict, now).** Ruled by the project owner. The cheap
  alternative — default off, flip booked before Stage 1 — was rejected on the evidence of
  R-16 itself: a booked flip is exactly the instrument that left this gap open for a full
  milestone. Defaulting to strict is free while nothing is published and breaking
  afterwards, so the window is the argument. An application that genuinely wants the
  fallback opts out explicitly, which is the direction the default should lean in a
  library whose purpose includes fail detection.

- **D-R16-6 (Decision A — a hand-rolled, zero-dependency SRV resolver ships in core).**
  Ruled by the project owner. Feasibility is **verified, not assumed**: a working SRV
  resolver was built against the BCL alone and queried production DNS successfully, at 133
  lines of message build, parse and validation. Grounds, in the order that decided it:

  1. **Parity.** Cross-language behavioural parity is a hard project goal (`CLA-003`,
     **VER-002**). One algorithm transcribed three times can be held to it; three
     idiomatic DNS libraries cannot, because they diverge on search-list semantics,
     how they spend one timeout budget, and truncation. Truncation is not hypothetical:
     a real SRV name returned **11 records over UDP with `TC=1` and 18 over TCP**. Since
     `DSC-031` makes priority a hard floor, losing records can drop the entire
     minimum-priority set and change the pick outright. The existing fixture corpus cannot
     catch any of this, because fixtures feed a fake `ISrvResolver` and start *below* the
     resolver.
  2. **Dependency posture.** The .NET SDK has zero runtime NuGet dependencies. A DNS
     library would be its first, in a library that holds credentials.
  3. **Timing.** Rust and Python have no discovery module yet, so the resolver lands
     inside their M13 parity pass rather than as a retrofit.

  **One requirement added to this ruling at architecture review: the nameserver list is an
  injected input, not a hard-wired platform call.** The measured macOS defect in D-R16-7 is
  a defect in *nameserver discovery*, not in SRV parsing — the parser was verified working
  against production DNS. Binding the resolver to
  `NetworkInterface.GetAllNetworkInterfaces()` with no override would make the one part of
  the design that is demonstrably wrong also the only part an operator cannot fix. So:
  platform discovery is the **default** source, and an explicit nameserver list overrides
  it. This preserves out-of-the-box discovery, which is what the ruling asked for, while
  giving the macOS-VPN operator a configuration remedy rather than a dead end. It is also
  what makes the resolver testable against an in-process fake DNS server without touching
  the host's resolver configuration.

  **Rejected: a third-party DNS library per language.** It is correct on exactly the part
  hand-rolling gets wrong (search lists, scoped resolvers) and cheapest to write, and it
  was rejected anyway — it takes .NET's first runtime dependency and puts parity, the
  harder goal, beyond reach with no fixture mechanism to police it.

  **Rejected: ship nothing in core; leave the resolver to the application.** `DSC-014`
  already makes it injectable, so this is coherent, and it was the design's own
  counter-recommendation. Rejected because a core capability whose every consumer must
  first write a DNS wire-format parser is not shipped. Its strongest point is preserved as
  the residual in D-R16-7 rather than dismissed.

- **D-R16-7 (the macOS residual is accepted, named, and made loud — not waved away).**
  The measured finding against this decision: `GetIPProperties().DnsAddresses` returns an
  identical global list on every interface on macOS, including down ones, collapsing
  macOS's scoped resolvers. So on a split-horizon VPN the shipped resolver queries the
  wrong nameserver, receives NXDOMAIN, and — before `DSC-015`…`DSC-017` — would take
  `DSC-012`'s silent literal path. **That is R-16 recurring inside its own fix**, and it
  is why B ships first: under `DSC-017`'s strict default the same environment now fails
  loudly at startup instead of degrading in silence. The residual is real and narrower than
  the defect it replaces: it is a *diagnosable* failure in one environment, not an
  *undetectable* one in all. It is booked as `ROADMAP.md` risk **R-26** — landed by the M8
  session per **REC-004**, which holds the pen on that file; R-24 and R-25 were allocated
  to other findings while the handover was in flight — owned by the documentation
  obligation M11 retains under D-R16-1. Linux and Windows nameserver discovery is in-platform and unaffected.

- **D-R16-8 (scope guard: no search-list emulation, and the limitation is specified).**
  The BCL exposes one DNS suffix but neither the ordered `search` list nor `ndots`, so
  search-list emulation would be the divergence this decision exists to avoid, invented
  in-house. Therefore: qualification is **absolute-only**, and a **single-label cluster
  name is rejected** rather than guessed at. `DSC-010` composes `_bvault._tcp.N`, which has
  at least two dots and so is queried absolute-first by a stock resolver anyway — the
  common case is already correct. A resolver honest about one specified, testable
  limitation beats one that diverges silently in three languages.

- **D-R16-9 (the parser's bounds are normative, not implementation detail).** It consumes
  untrusted network input in a credential-handling SDK, so the bounds belong in
  `specifications/` where all three languages inherit them, and each gets a
  malformed-message test or the zero-dependency security argument is unearned:
  compression pointers must point strictly backwards **and** be hop-capped; label ≤63 and
  name ≤255, bounds-checked per label; the RR walk resumes at `offset + RDLENGTH`
  regardless of what the target parse consumed, so a lying `RDLENGTH` cannot desynchronise
  it; query IDs come from a cryptographic RNG, not `Random`, with an ephemeral source port
  and the ID, `QR` bit and question echo all verified; answer records must match the
  question in name, type and class, and the authority and additional sections are ignored.
  **`TC=1` MUST trigger the TCP retry** — this is `DSC-031` correctness, not robustness.
  **No caching**, which deletes the cache-poisoning class outright and costs nothing,
  because `DSC-042` already re-probes the cached candidate set without a fresh SRV lookup.

  The trust boundary is worth stating plainly, because it is what makes a hand-rolled
  parser defensible: DNS here is unauthenticated either way, and `RES-010`/`CFG-043` verify
  TLS against the SRV target, so a spoofed answer yields a certificate failure, not a
  credential leak. **The parser is not the trust boundary; TLS is.**

- **D-R16-10 (requirement placement: amend by adjacency, and a new block for the
  resolver).** `DSC-015`…`DSC-017` are free immediately after `DSC-014` and sit in the
  block they amend. The default resolver gets a **new subsection at `DSC-050`**, the first
  free block, covering ships-by-default, nameserver discovery, UDP with the mandatory TCP
  retry on `TC=1`, absolute-only qualification, single-label rejection, the D-R16-9 bounds,
  no caching, and the precedence rule that an injected resolver always wins over the
  default. `DSC-014` is **not** edited: injectability is already correct, and the default
  is additive to it.

  **`DSC-011` and `DSC-012` are each amended by one clause, and this is required, not
  cosmetic.** `DSC-011` says a resolver failure MUST be treated as "no records", *not
  propagated*; `DSC-017`'s strict mode must propagate. Adjacency alone would leave two
  requirements from which a conformance test could be written to prove either behaviour —
  in the one document whose whole function is to be the single source of truth. Each
  therefore gains an explicit "except as `DSC-017` provides" clause, and `DSC-017` names
  which it overrides. Still a far smaller edit than rewriting `DSC-011` (**CLA-007**).

  `specifications/appendix-d-requirement-index.md` is **generated** from the `AREA-NNN`
  markers by `tools/traceability/traceability.py`, so it is regenerated, not hand-edited,
  and its `Total requirements: 393` moves with the new IDs. It is not a second edit site.

- **D-R16-11 (sequencing, and what this record does not authorise).** Order: the section 13
  change (human-confirmed, R3) → B in .NET → A in .NET → Rust and Python at M13 as row-2
  parity passes. Implementation is **row 2** once this record is accepted — a settled
  contract — and must not be routed to row 3 on the grounds that R-16 felt hard.

- **D-R16-14 (the M8 slice-d dependency is inverted; R-16 does not wait for it).** An
  earlier revision of this record had both B and A waiting on M8 slice d, on the grounds
  that `EFF-005` exempts discovery health probes from the client rate gate and a shipped
  resolver adds DNS I/O that is also un-gated. M8 then finalised at three slices of five
  with **d and e never started**, which forced the dependency to be re-examined rather than
  inherited — and it does not hold in that direction:

  - **Discovery does not touch the rate gate today.** Verified: `DiscoveryEngine.cs` contains
    no reference to `RateGate` at all. The gate is still config-plus-pause-only as M1a and
    M1b left it; the token bucket `EFF-005`'s exemption would have to bypass **does not
    exist**.
  - So `EFF-005` is not a constraint R-16 must satisfy — it is presently
    **unimplementable**, and it is slice d's work whenever slice d happens.
  - The real relation is the reverse: **slice d must honour a constraint from this record**,
    because the resolver adds a second class of un-gated I/O that `EFF-005`'s eventual seam
    has to account for. Waiting would have R-16 blocked on a design nobody has started, in
    order to satisfy a requirement that cannot yet be satisfied by anyone.

  Therefore: **Decision B is unblocked entirely** — `DSC-015`…`DSC-018` touch the logger,
  the discovery surface and the strict path, and none of them touches a gate surface.
  Decision A is unblocked except `DSC-019`. The constraint handed forward, for whoever
  takes slice d: **the shipped resolver's DNS I/O is not HTTP and is not subject to the
  client rate gate; `EFF-005`'s seam must exempt it explicitly rather than by omission**, so
  that a later reader cannot mistake "never gated" for "forgotten".

  Recorded because the original dependency was stated as fact in this record and was wrong.
  It was wrong in the safe direction — it would have delayed work, not broken it — but a
  blocker that outlives its cause is how R-16 itself reached M8 untouched.

  This record authorises no code. It is the framing; the `specifications/` change carries
  §5.3's R3 human confirmation before release, and the records are the M8 session's to
  write under **REC-004**.

- **D-R16-12 (`DSC-016` is a property, not a `Render()` column).** Left unstated, a delegate
  picks the column, and the column is the expensive choice: `RES-020`'s table is asserted
  against the inline example at `specifications/13-*.md:147-152` by
  `DiscoveryUnitTests.cs:773` and `:797`, and D-M5-25 pinned `Render()` as a shared
  artefact, so a new column becomes a three-language render-parity change *and* a
  specification edit. As a property it is none of those.

  A claim in this record's own draft was **wrong** here and is corrected: `Render()` is
  *not* a byte-compared fixture artefact. The corpus compares the **structured** report
  (`ResilienceFixtureOperations.cs:194`), and the comparator enumerates the *expected*
  object's properties only (`FixtureComparisons.cs:233`), so object comparison is **subset,
  not exact**. Verified by reading both. Adding a property to `DiscoveryReport` therefore
  breaks **zero** existing fixtures, and Decision B is **not** a fixture-corpus change and
  **does not** collide with M8's ownership of `specifications/fixtures/`.

  Recorded because the evidence now sits here and should not have to be re-established:
  subset comparison **cuts both ways**. It is why this decision is cheap, and it also means
  a fixture cannot detect an *unexpected* property — the same silent-pass shape as R-24 and
  R-25. That is **not** R-16's to fix and is deliberately not opened here (**CLA-007**);
  it is named so that whoever does open it starts from `FixtureComparisons.cs:233` rather
  than from a guess.

- **D-R16-13 (the two collisions that are real, now that the fixture one is not).**
  1. **The hand-maintained corpus counts.** Any *new* fixture file moves
     `HarnessTests.cs:98` and `:153` and the literal count in `ROADMAP.md` §2 — the same
     lines M8 slice a is already moving 230 → 236. Textual conflict in two files, resolvable
     only after the M8 merge. A separate session has the count *design* booked as its own R2
     follow-up; R-16 updates the counts and does not redesign them (**CLA-007**).
  2. **`DSC-019`'s error code** regenerates the catalogue across `dotnet/`,
     `rust/src/generated/` and `python/_generated/`, which is exactly what M8 slice a has
     landed on branch `m8a-recognition-qualifier-alternation` (PR #2, **open and unmerged**;
     `main` is still at 42dd778). `DSC-019` is therefore the **one** item in this record
     with a real external dependency, and it is on **PR #2 merging**, not on slice d.
     One constraint inherited from that slice,
     supplied by the M8 session and recorded here so the implementation brief does not have
     to rediscover it: slice a changed the **compiled rule shape**, adding `containsAny`
     beside `containsAll` at row position 4, which moved the destructuring arity in Rust and
     Python, and a second qualifier group on one stem now **raises** rather than flattening.
     If `DSC-019`'s Appendix B row carries a qualifier group, read **D-M8-2** and **D-M8-11**
     before authoring it.
  3. **Number allocation stays with the delegating orchestrator, never with a delegate.**
     M8 lost two decision numbers to a collision when two concurrent fix agents each took
     "the next free number" from a record the other was appending to. Any fan-out under this
     record that touches `DSC-015`…`DSC-019`, the `DSC-050` block or a decision number gets
     its identifiers **assigned in the brief**, not chosen by the worker. This is cheap to
     honour and was demonstrated to be expensive to skip.

## Architecture review — dispositions

One round, Strategic-tree Claude Opus 5, verdict *approve with required fixes*, six fixes.
Five are folded in above. One is ruled against, with reasons.

| Finding | Disposition |
|---------|-------------|
| Tier over-claimed: `CRS-004` is dispositive, `CRS-006` misapplied (radius is *known*, not unknown), `CRS-003` is an R2 floor and the draft contradicted itself | **Accepted in full.** Tier section rewritten: one dispositive limb, two corroborating R2 factors. The self-contradiction was real |
| The seam conflates **three** states, not two — `ResolveTimeout` cancellation is also flattened to `[]` | **Accepted.** `DSC-016` becomes a cause, not a boolean (D-R16-4) |
| `DSC-011`/`DSC-017` are in genuine tension; adjacency alone lets a conformance test prove either behaviour | **Accepted.** Both `DSC-011` and `DSC-012` gain an "except as `DSC-017` provides" clause (D-R16-10) |
| The draft's fixture-collision premise is factually wrong: `Render()` is unit-test-asserted, the comparator is subset, the flag breaks no fixture | **Accepted, and independently verified** before acting on it. D-R16-12; real collisions restated in D-R16-13 |
| Rule `Degraded` a property, not a `Render()` column | **Accepted.** D-R16-12 |
| Unstated consequences: `DSC-042` failover also silently disabled; `DSC-046` flag lifetime; `DSC-001` false positives; no error code for strict refusal; the booked flip has no ID | **Accepted in full.** `DSC-042` added to *Problem* — it is the strongest argument for B and the draft omitted it. `DSC-018`, `DSC-019` added. `DSC-001` noted. The flip question is moot: D-R16-5 defaults to strict now, so nothing is booked |
| **Defer Decision A entirely**; specify only a resolver *contract*, and if a resolver ever ships in core, ship it behind explicit construction and never as an implicit default | **Ruled against**, on two grounds. First, **it is the project owner's call and the owner made it** — dependency posture and supported environments are outward-facing R3 (`agents.md` §5.4), the owner was shown the measured macOS finding in the options put to them, and chose a core resolver anyway. Second, **the review's own step 3 concedes the argument**: it grants that with `DSC-015`/`DSC-016` in place the macOS failure is loud, and that this record's sequencing already neutralises the counter-argument's core. A recommendation that concedes its own premise does not outweigh an owner ruling. What the finding *did* earn is adopted: its real content is that **nameserver discovery**, not SRV parsing, is the weak part — so the nameserver list becomes an injected input with platform discovery as the default (D-R16-6), and the residual is booked as a named risk (D-R16-7). Its reframe of the truncation measurement is also adopted: **TCP-fallback-on-`TC`** is specified in the `DSC-050` block as binding on *any* resolver, injected or shipped, which is where it belongs |

Two of the review's "confirm" items were resolved by reading rather than left open:
`appendix-d-requirement-index.md` is generated (D-R16-10), and the comparator is subset
(D-R16-12).

**Confidence: 0.84.** Requirement coverage and spec grounding are high; the design's
feasibility claims are execution-verified rather than asserted (a working resolver,
truncation measured at 11 records over UDP versus 18 over TCP, the macOS resolver collapse
measured), and the three claims this record leans on hardest were re-verified here rather
than taken from the delegate (**CCF-002**). Held below 0.85 because **no code has run for
this record** — it is framing, and `agents.md` §5.2 caps a build-free result at 0.60 for
*implementation*, which this is not. The residual is D-R16-7's macOS behaviour on
platforms not measured (Windows and container `DnsAddresses` are believed correct and were
not tested here) and the unknown shape of M8 slice d's rate-gate seam.

## Open questions

1. **~~Slice d's seam shape~~ — closed, and not by an answer.** The question was whether
   `EFF-005`'s exemption lands as a flag on the probe call, an ambient bypass, or a separate
   un-gated path. M8 confirmed it is **undesigned and unstarted**, and D-R16-14 establishes
   that R-16 does not wait on it. The constraint is handed forward to slice d instead.
2. **`DSC-011`'s deeper residual.** Even amended, `DSC-011` folds resolver *failure* into
   "no records" for the non-strict path, so `DSC-015`/`DSC-016` report causes that
   `DSC-011` still treats alike. Whether `DSC-011` should be rewritten to distinguish them
   outright is deliberately **not** decided here: it is a separate behavioural change and
   would widen an already-R3 record (**CLA-007**). Named so it is not lost the way R-16 was.
