# DR-0016 — M9: PKI, SSH and SSH-broker endpoint bindings in .NET

**Status:** **accepted, revision 4** (2026-09-21) — approved at Strategic-tree Claude Opus 5
architecture review after three blocked rounds. Authored by the Strategic Orchestrator as the
milestone's framing record. Each slice appends its own `D-M9-n` entries below rather than
opening a second record. **Two architecture-review rounds, both BLOCKED**
(`agents.md` §4.2 row 4, §4.4).
**Revision 1** was blocked on four findings, every one a public-API decision it left to a
row-2 delegate to take — which **FAM-002** forbids it to take. D-M9-7 … D-M9-14 are the
rulings.
**Revision 2** was blocked again, on the fix for the first of those: D-M9-7 had ruled that
Appendix A contradicts §14 on route prefixes, and booked an R3 correction against a document
that is not wrong. Both the review and this record had cited
`appendix-a-endpoint-catalogue.md:221` without reading the legend at `:3-4` that defines what
the column means. D-M9-7 is reversed, D-M9-13 is withdrawn, D-M9-10 is rewritten, and the
process defect that produced it is recorded as D-M9-15 rather than tidied away.
**Revision 3** was blocked on one decision: D-M9-10's untyped `Response` fallback would have
routed `Pki.Csr.Generate`'s exported private key — `PKI-002` material — into a container
whose `RawBody` is documented for diagnostics. The fallback now stops at `PKI-002`. Also
corrected: the slice table still routed slice c to row 3 against D-M9-10's own text, and
D-M9-6 asked for a hand-edit of a generated gate artefact that `dotnet/README.md:34-37` says
is never hand-edited.
Corrections are marked in place throughout.

**Revision 4 approved.** Two notes came with the approval, neither a condition, both carried
into the handback gates. **(N1)** The record pins the *shapes* of the typed records but not
their *type names* — `Pki.Csr.Generate`'s return and, in slice b, `Pki.GenerateIntermediate`'s.
A type name is public API surface, so in principle this is the class of thing that blocked
revision 1; in practice M8's idiom (D-M9-3) settles it and every name lands visibly in
`PublicApiSurface.txt`. **Each gate reads the names, not only the shapes.** **(N2)** "Fifteen
decisions" counts D-M9-13, which is withdrawn but deliberately retained; fourteen are active.
**Risk tier:** R2 (`agents.md` §5.3), assigned here before dispatch (**CRS-001**); see D-M9-2
for the limb it stands on, and D-M9-10 and D-M9-11 for the two R3 specification corrections
M9 **carves out** rather than takes.
**Milestone:** M9, four slices · **Date:** 2026-09-21
**Supersedes nothing. Amends:** nothing in `specifications/`. **Two corrections to
`specifications/` are *identified* by this record and deliberately not made in it** —
**R-30** (D-M9-11: `BV-QUOTA-002` has no recognition row) and **R-31** (D-M9-10: neither PKI
queue table defines any response shape). Both are R3, both are booked with an owner rather
than folded in. **Discharges a sub-question of R-27** without closing the row: all seven
`*-info` rows are now checked against Appendix A, and the third contradiction R-27 told
readers to assume does not exist (D-M9-7).
**Discharges:** the `PKI`, `SSH` and `SSB` half of `ROADMAP.md` §4's M9 row — 11 requirement
IDs: `PKI-001`, `PKI-002`, `PKI-010`, `PKI-011`, `PKI-020`, `PKI-030`, `SSH-001`, `SSH-002`,
`SSH-003`, `SSB-001`, `SSB-002`.
**Inherits:** [DR-0004](0004-m1b-transport.md) (the one transport seam, the single retry
loop), [DR-0005](0005-m1c-error-model.md) (recognition, enrichment, the generated catalogue
D-M1c-1, and **D-M1c-25**: a deferred branch returns the value the specification names, never
a plausible guess), [DR-0009](0009-m4-kv-engine.md) (D-M4-8's fixture-authoring precedent),
[DR-0012](0012-m7-system-api-remainder.md) (the `Page<T>` shape landed at `SYS-060`),
[DR-0013](0013-m8-transit-totp-and-efficiency.md) (the engine-binding idiom: an operations
class over `LogicalOperations`, a `*Wire` static for parse and serialise, `SecretString` /
`SecretBytes` for secret-bearing fields, and cursor pagination as `Page<T>`).

## Problem

`ROADMAP.md` §4 books M9 as sections 09 and 10 complete in .NET at Large size and R2 risk,
carrying 11 requirement IDs. The requirement count understates the work by a wide margin:
Appendix A lists **66** PKI routes, **13** SSH routes and **12** SSH-broker operations — about
**91** in total — and the 11 IDs are constraints *on* those bindings rather than a census of
them. *(Revision 1 said "roughly sixty / fifteen / nine"; the counts are corrected here
because this is the milestone whose own governing lesson is counting the artefact rather than
the sentence describing it.)* M8's lesson is that lesson — five slices, and **not one passed
its gate first time**, every defect found by checking the artefact rather than the sentence.

Fifteen things are decided here rather than inside a slice, because a delegate that reopened
any of them would be re-litigating a Strategic call (**FAM-002**). Revision 1 decided six and
was blocked for leaving four public-API decisions unmade; D-M9-7 … D-M9-14 make them, and
D-M9-15 records a process defect in this record's own execution.

## Decisions

### D-M9-1 — "engine" means endpoint bindings; the SDK signs nothing and generates no key

`ROADMAP.md` records this correction at M8 exit precisely so it does not recur at M9's `PKI`
and `SSH` (§2, the "engine" paragraph). Restated as a decision so no slice has to re-derive
it: sections 09 and 10 are tables of HTTP endpoints. `Pki.Issue` posts to the server's issue
route and returns the certificate **the server minted**; `Ssh.Sign` posts a public key and
returns the certificate line **the server signed**. `PKI-001` states the rule from the other
side — PEM fields are returned verbatim, and the SDK MUST NOT parse certificates at all
except in optional helpers explicitly named `Parse*` that delegate to the runtime's X.509
library (`00-overview.md` Purpose and Non-goals, `OVR-002`).

**Consequence for the slices:** no slice adds a dependency for cryptography, and no slice
writes a `Parse*` helper. `PKI-001`'s helper clause is permissive (`optional helpers`), and an
unrequested public surface is the defect D-M8-47 already caught once this year.

**Rejected:** *ship `Pki.ParseCertificate` because the clause allows it.* Rejected under
**CLA-007** — the smallest change that satisfies the requirement is the one that returns the
PEM untouched. A `Parse*` helper is M10-or-later surface if a consumer asks for it.

### D-M9-2 — M9 is R2, and the one thing that raises it to R3

**The tier stands on two independent limbs, both named here so a reviewer need not
reconstruct which one bites.** `CRS-003` puts anything touching secret material at **R2
minimum**, and M9 has two such fields: PKI's exported `PrivateKey`
(`09-pki-engine.md:11,21`) and SSH's OTP `Key` (`10-ssh-engine.md:49`). Independently,
`agents.md` §5.3's R2 row is reached by "public API shape" alone, given ~91 new public
operations. Either limb alone gives R2.

`CRS-004`'s **second** limb is vacuous, as `skills/claude/SKILLS.md` §5 records: no workflow
publishes to a package registry (`build-artifacts.yml` builds and never pushes). Its **first**
limb — a `specifications/` change — **is live once**, which revision 1 denied: `BV-QUOTA-002`
has no recognition row (D-M9-11), and adding one is a genuine R3 specification correction.
**It is not made in M9.** It is carved out, booked with an owner, and M9's *content* —
endpoint bindings against unchanged prose — stays R2. The carve-out is what keeps the tier
honest; had M9 made that edit, it would be R3 and would pause.

*(Revision 2's first draft named a second live instance — a wrong prefix in Appendix A. There
is none; see D-M9-7.)*

The six fixtures M9 authors (D-M9-4) are files Appendix C **already names**; adding them moves
the corpus to match the specification rather than moving the specification. That is the
distinction M8a's R-23 row turns on, and it is recorded here so a reviewer does not have to
reconstruct it.

**Decision:** M9 is **R2**. Every slice's brief carries the tier. A slice that finds it must
edit prose in `specifications/*.md` — Appendix A, Appendix B, or a section body —
**raises to R3 and pauses** (`CRS-002`: a delegate may raise a tier, only Claude may lower
one; `CRS-005`: R3 work pauses on detection).

**Rejected:** *tier the whole milestone R3 because D-M9-10 and D-M9-11 exist.* Rejected
because the tier would then be scoring work M9 does not do. The two specification
corrections they identify (R-30, R-31) are R3 and are tiered R3 where they are booked; M9's
bindings are R2. *(Revision 1 rejected a different alternative here — "R3 because it authors
fixtures" — which nobody had proposed and which the M8a precedent already settles. Replaced.
Revision 3 named D-M9-7 here, which no longer identifies a correction.)*

**Rejected:** *`CRS-006` (unknown blast radius scores at the higher tier) forces R3, since the
record itself says the surface is larger than the ID count suggests.* Rejected because the
blast radius is now **measured**, not unknown: 91 operations, all additive, and **none
changing a landed binding** — the one candidate, `Sys.ListNamespacesInfo`, proved correct as
shipped (D-M9-13, withdrawn).

### D-M9-3 — the contract is settled here, so the slices run at routing row 2

Sections 09 and 10 pin operation names, HTTP verbs, paths and field names exhaustively, and
Appendix A repeats the route table independently. The binding **idiom** was settled by M8's
Transit and TOTP slices and reviewed at their gates. So M9 is transcription against a settled
contract, which is `agents.md` §4.2 **row 2** — `eng-implementation`, Claude Sonnet 5 — and
not row 3. §4.2's own discriminator says so: row 3 is the pathfinder pass that *first defines*
a contract, and this contract is defined by the specification and pinned by this record.

The following are pinned here so no slice chooses them:

| Pinned | Value | Source |
|--------|-------|--------|
| Operations class shape | one `sealed class` over a private `LogicalOperations`, `mount` as a defaulted parameter, `RequestOptions?` + `CancellationToken` last | `TransitOperations.cs` |
| Parse / serialise | a `PkiWire` / `SshWire` `internal static` class beside it | `TransitWire`, `TotpWire` |
| PKI exported `PrivateKey`, SSH OTP `Key` | `SecretString` (`PKI-002`; PEM and OTP are text, not bytes) | `TotpTypes.cs:38` |
| `*-info` listings | the existing `Page<T>` (`Namespaces.cs:126`), not a new page type | `DR-0012`, `PAG-003`/`PAG-005` |
| Client entry points | `BastionVaultClient.Pki`, `.Ssh`, `.SshBroker`, each `new(context, namespaceOverride)` per access | `BastionVaultClient.cs:155` |
| Sub-surfaces | `Pki.Csr`, `Pki.SignRequests`, `Pki.Acme` as nested operations classes, as `Transit.Byok` is | `TransitOperations.cs:36` *(revision 1 cited `:34`, a blank line)* |
| `SSB-001`'s v2 pin | the existing `RequestOptions with { ApiVersion = "v2" }` seam, **never** a literal `v2/` in a path | `Internal/IdentityWire.cs:18` |
| Every `*-info` route's prefix | **unpinned**, following `ApiPrefix` — see **D-M9-7** | `appendix-a-endpoint-catalogue.md:3-5,40,87,221`; R-27; DR-0013 D-M8-45 |
| `*-info` element types and operation names | the **area** section wins over §14 — see **D-M9-9** | `ROADMAP.md`'s `Page<Namespace>` precedent |
| `PAG-004` iterators | in scope, reusing `PagingWire` — see **D-M9-8** | `14-batch-and-request-efficiency.md:132-135` |
| `SSH-002`'s `0644` | applied at file creation, never write-then-chmod — see **D-M9-12** | `Internal/TokenFiles.cs:17,37-42,48` |

**Rejected:** *route slice a at row 3 because it lands the PKI type layer first.* Rejected
under tie-breaker 1 (lower row wins) and **TOK-012**. A row-3 claim over a settled contract is
the misroute §4.2 names explicitly. If a slice fails twice at row 2, §5.4 escalates it — that
is the recorded trigger, and a trigger written after the fact is a rationalisation (§4.3
rule 4).

**Correction from the revision-1 review.** Row 2 was the right call and the record was not
entitled to it yet. §4.2 settles a contract when *a record pins the public names and
behaviour*; revision 1 claimed the specification had already done so, and for four decisions
it had not — the `*-info` prefix, the `*-info` element types, `PAG-004`, and the `0644` mode.
A row-2 delegate meeting any of them would have been taking a public-API-shape decision.
D-M9-7 … D-M9-12 pin them, which is what makes row 2 true rather than asserted. **The fix is
to pin, not to reroute.** That holds for every slice including c: D-M9-10 initially rerouted
it to row 3 and was rewritten, because the rung was never the problem — an undefined response
shape is guessed at just as readily by a deeper model. Pinning the *fallback* resolves it.
**M9 has no row-3 slice.**

### D-M9-4 — six fixtures, named by Appendix C, authored by the slice that binds them

`appendix-c-conformance-fixtures.md:128-129` names seven M9 fixtures. One,
`sshbroker.effective-v2-pinned`, is already on disk. The remaining six are authored by the
slice that lands their operation, following D-M4-8's precedent:

| Fixture | Slice |
|---------|-------|
| `pki.issue`, `pki.certs-info-page`, `pki.role-not-found` | a |
| `ssh.sign`, `ssh.creds-ip-not-allowed`, `ssh.verify-invalid-otp` | d |

Each is exercised through the fixture driver's operation registry (`CNF-015`), not merely
parked on disk. The corpus moves **247 → 253**; the count assertions the R-25 tripwire covers
move with it, in the same commit. **No slice adds a non-fixture JSON under
`specifications/fixtures/`** — R-25's exclusion half stays latent for M9's duration, as it did
for M8's.

**Rejected:** *author all six up front in one pass.* It has a real advantage the per-slice
split gives up: the R-25 count tripwire is then edited **once**, not twice, and a count
assertion edited twice is exactly the thing that left `main` red from `0.10.0` to M8a.
Rejected anyway, because a fixture authored before its binding exists cannot be *driven* by
the slice that authors it, and an undriven fixture is a file that proves nothing (`CNF-015`,
and R-19's shape). The tripwire is edited twice and each slice states the count it moves
to — 247 → 250 at a, 250 → 253 at d — so a wrong intermediate value fails its own slice's
gate rather than surviving to the merge.

**Why M9 hand-edits the count at all, given that an accepted decision exists to stop it.**
[DR-0015](0015-fixture-corpus-count-derivation.md) is **accepted** and replaces the
hand-transcribed count with a generated, committed manifest read by all three harnesses; its
stated precondition was all of M8, which cleared at `0.13.0`. M9 is the first milestone after
that gate, so the argument above about *which* hand-edit pattern is safer would be a
re-derivation of a settled decision (**TOK-008**) if it went unqualified.

**Decision:** M9 does **not** land the manifest, and does not reopen DR-0015. It is that
record's work, with that record's owner, and folding it into a PKI/SSH milestone would give
one change two owners (**CLA-008**) — the same reason M8 declined to fold in R-24. M9's two
hand-edits are the last two, and DR-0015's owner is unblocked as of `0.13.0`. Note also that
`ROADMAP.md` R-25 records the count assertion as *unrequired* defence-in-depth — `FIX-001`
and `TST-010` mandate schema validation and repository loading, not a count, and
`specifications/` states no corpus count anywhere. The assertion is worth keeping correct;
it is not worth citing as a requirement, and this record does not.

### D-M9-5 — four slices, dispatched serially

M9 does not fit one Large brief (**TOK-011**), so it is decomposed rather than granted a
larger budget.

| Slice | Content | Rung | IDs · gate |
|-------|---------|------|------------|
| **a** | PKI types and `PkiWire`; roles; issuance (`Issue`, `Sign`, `SignVerbatim`); certificates and CRL; `Pki.ListCertificatesInfo` as `Page<CertificateSummary>`, unpinned; its `PAG-004` iterator; three fixtures | row 2 | `PKI-001`, `PKI-002`, `PKI-010`, `PKI-011`, `PKI-020`, and `PAG-004`'s PKI half |
| **b** | PKI CA lifecycle (root, intermediate, issuers, `config/*`, `ca`/`ca_chain`); managed keys; tidy; `Pki.Acme` config and `DirectoryUrl` | row 2 | **no IDs — see D-M9-14 for what its gate checks instead** |
| **c** | `Pki.Csr.*` and `Pki.SignRequests.*`; typed returns where §09 defines a shape or `PKI-002` applies, `Response` / `Page<IReadOnlyDictionary<string, JsonElement>>` where it does not (D-M9-10) | row 2 | `PKI-030` **first limb only** (D-M9-11) |
| **d** | SSH engine (CA config, roles, `Sign`, OTP) and SSH broker; `Ssh.ListRolesInfo` + its iterator; three fixtures | row 2 | `SSH-001`, `SSH-002`, `SSH-003`, `SSB-001`, `SSB-002`, and `PAG-004`'s SSH half |

Slices run **serially**, not in parallel. They are not disjoint in the sense §7.4 requires:
b and c transcribe against the type layer and `PkiWire` helpers a lands, and all four touch
`BastionVaultClient.cs` and the `PublicApiSurface.txt` baseline. Two agents editing one public
contract produce a conflict that costs more than the time saved.

**Rejected:** *run d in parallel with a, since SSH shares no PKI file.* Rejected on the
`PublicApiSurface.txt` baseline alone — it is a single generated file that every slice
regenerates, so a parallel pair produces a baseline conflict that neither agent can resolve
without re-running the other's build.

### D-M9-6 — M9 declares no conformance level

`ROADMAP.md` §4 books M9's exit as "sections 09–10 complete in .NET", which is requirement
content and not a level claim — so unlike M4's and M8's, M9's stated gate is satisfiable as
written. **R-14** still binds: `CNF-002` forbids claiming a level whose sections carry
unimplemented MUSTs, and sections 16–17 are M11's. M9 declares nothing.

**Two corrections from the revision-3 review, both to how the gap list is updated.** First,
the list is **not hand-edited**: `dotnet/README.md:34-37` states it is regenerated by
`tools/traceability/traceability.py --write-baseline` and is "the authoritative,
machine-readable gap list (CNF-002)". M9 therefore *regenerates* it; a slice briefed to
"remove sections 09 and 10" would be hand-editing a gate artefact, which is `CLA-004`'s
shape. Second, **section 09 does not clear.** `PKI-030` stays baselined under D-M9-11, so
the regenerated `PKI` row still carries a gap and `CNF-002` still binds section 09.
Section 10 clears; section 09 does not. D-M9-6 was written when M9 expected 11 of 11 and was
not revisited when D-M9-11 made it 10 of 11 — so its conclusion is unchanged and is now
over-determined, which is a weaker claim than the one it originally made. Resequencing remains §10 question 4, a project-owner decision this milestone does
not take either.

**Rejected:** *declare `Complete` for sections 09 and 10 specifically — a per-section claim
rather than a level claim.* Rejected because `CNF-002` governs level claims and the
specification defines no per-section conformance vocabulary, so the claim would be one this
project invented, in a README, where a consumer would read it as the level. The gap list is
the honest form of the same information and already exists.

### D-M9-7 — no M9 `*-info` route is pinned; they follow `ApiPrefix`. R-27 already ruled this

**This decision was written the opposite way in the first draft of revision 2, and is
corrected here. The correction is the substance, so it is recorded rather than tidied away.**

`14-batch-and-request-efficiency.md:92-104` lists all seven `*-info` endpoints with an
explicit `/v2` prefix. Reading that as a per-route instruction gives "pin all four M9 routes
to `/v2`", which is what the first draft decided and what slice a was briefed mid-flight.
It is wrong, for two reasons that were both already on disk.

**First, Appendix A's Prefix column does not mean what it appears to.**
`appendix-a-endpoint-catalogue.md:3-5` defines it: "**Prefix `v1` means the operation uses
`ApiPrefix` (both `/v1` and `/v2` serve it); `v2` means the SDK MUST pin `/v2`**". So `v1` is
not a claim that the route lives at `/v1` — it is an instruction *not to pin*. Only an
explicit `v2` is a pin. `appendix-a-endpoint-catalogue.md:221`'s `v1` on `Ssh.ListRolesInfo`
therefore does not contradict `14:101` about where the route *is*; it answers a different
question — whether the SDK pins — and §14's table never addresses that question at all.

**Second, this was decided at M8 and this record was not entitled to re-derive it**
(**TOK-008**). `ROADMAP.md` **R-27** records exactly this contradiction — "section 14's
endpoint table writes `/v2/` uniformly and contradicts the endpoint catalogue" — and
DR-0013 **D-M8-45** / **D-M8-5** ruled it: *the owning section and the catalogue win over
section 14's cross-cutting table*, and M8 shipped every pin matching Appendix A. Section 14
is a cross-cutting chapter whose endpoint table was never reconciled; its uniform `/v2/`
carries no per-route weight.

**Decision:** no M9 `*-info` route is pinned. `Pki.ListCertificatesInfo`,
`Pki.Csr.ListInfo`, `Pki.SignRequests.ListInfo` and `Ssh.ListRolesInfo` pass `options`
through unmodified and resolve against `ClientConfig.ApiPrefix`, like every other route in
their areas. Appendix A's PKI table carries **no** Prefix column, so the catalogue states
nothing for the three PKI routes and the default applies; Appendix A:221 states `v1` for the
SSH one, which under the preamble *is* the instruction not to pin. Each operation carries a
short comment recording why it is unpinned, so the next reader does not "fix" it back.

`SSB-001` is untouched by this: `10-ssh-engine.md:84` makes the `/v2` pin a **requirement**
for the broker routes, and `appendix-a-endpoint-catalogue.md:225-227` marks all three
**v2**. Section and catalogue agree, so the pin stands there and only there.

**Section 10 corroborates the reading, weakly.** §10 is not uniformly silent about prefixes:
`10-ssh-engine.md:77-81` writes `/v2` explicitly on all five broker rows. So §10 writes the
prefix where Appendix A marks **v2** and stays silent where it marks `v1`.

**This argument is confounded, and it is ranked last deliberately.** An earlier draft called
it the strongest evidence, which was wrong. The broker rows differ from the engine rows in
*two* respects at once: they are `/v2`, and `ssh-broker` is a fixed logical mount rather than
the `{mount}` placeholder the engine rows use (`10-ssh-engine.md:12,22`). So their explicit
absolute path may be saying "this path is not mount-relative" rather than anything about
prefix convention, and the evidence cannot isolate which. It corroborates; it does not
establish.

The two load-bearing reasons are the ones above it, and both are first-hand: the **legend**
at `appendix-a-endpoint-catalogue.md:3-5`, which is a definition rather than an inference,
and the **prior ruling** at R-27 / D-M8-45 / D-M8-5, which this record was not entitled to
re-derive at all (**TOK-008**).

**R-27 asked for something this milestone can supply.** It warns that "a third instance
should be assumed until someone checks all seven rows". All seven are now checked:
`certs-info`, `csr-info` and `sign-request-info` — PKI, no Prefix column, catalogue silent;
`roles-info` — SSH, `v1`; `targets-info` — cert lifecycle, M10's; `users-info` — "v2
recommended" (`:87`), which is why M8 pinned it; `namespaces-info` — `v1` (`:40`). **No third
instance exists.** The disagreement is uniform and is section 14's alone. R-27's row is
updated with this, which closes its open sub-question without closing the row.

**Rejected:** *follow §14, because it owns the bulk-listing family and defines `Page<T>`,
`PAG-001`…`PAG-006` and the envelope.* This is the strongest argument for the other side and
it is why the first draft went that way. Rejected because ownership is per-question, not per
route family: §14 owns the envelope, the paging semantics and the shared contract, and
Appendix A owns the prefix — it is the only document with a column for it and a preamble
defining what the column means. M8 resolved this precise clash already; a second milestone
answering it differently would leave the SDK pinning two of seven sibling routes for no
reason a reader could reconstruct.

**Rejected:** *pin anyway, because `/v2` is served in both readings and pinning is harmless.*
Rejected because it is not harmless: under Appendix A's semantics an unpinned route honours a
consumer's `ApiPrefix`, and pinning silently overrides a configuration choice the consumer
made. It also diverges from the fixtures M8 accepted, which pin nothing.

### D-M9-8 — `PAG-004`'s iterator is in scope for both new areas, and is already off the baseline

`14-batch-and-request-efficiency.md:132-135` makes an iterator/stream helper a MUST **per
area**, walking until `Truncated == false`, honouring the rate gate, with a `MaxRecords` cap
defaulting to 5000 → `BV-INPUT-005` carrying `Total`. PKI and SSH are areas; their iterators
can only land in M9.

`PAG-004` carries **no entry** in `tools/traceability/baseline.json` — M8 took it off when it
covered the Sys and Userpass areas. So if M9 ships the pages and not the iterators, **no gate
fires**: the ID is already green, coverage is satisfied by M8's tests, and the omission is
invisible until a consumer asks for it.

**Decision:** slice a ships the PKI iterator, slice d the SSH one, each reusing
`PagingWire.ValidateLimit` and `PagingWire.IteratePagesAsync` (`SysOperations.cs:784,840`)
rather than opening a fourth paging path, and each covered by a test of its own. Both briefs
state that no gate will catch this, which is why it is written down.

**Rejected:** *book the iterators to M10 with the remaining engines.* Rejected under the
ROADMAP's own lesson that a gap booked with no owner survives a milestone — R-16 was raised
at M5 and reached M8 untouched. An already-green requirement ID is the worst possible place
to park work, because the tracking system actively reports it as done.

### D-M9-9 — where §14 and an area section disagree, the **area section** wins

Three disagreements, one rule. `10-ssh-engine.md:22` returns `Page<SshRole>`; `14:120` returns
`Page<SshRoleSummary>`. `09-pki-engine.md:91,100` name `Pki.Csr.ListInfo` and
`Pki.SignRequests.ListInfo`; `14:118-119` name `Pki.ListCsrInfo` and
`Pki.ListSignRequestsInfo`. Appendix A:211-212 agrees with §09.

The precedent is recorded and was paid for: at M7/M8 the same clash between
`06-system-api.md:203` and `14:123` was resolved **in the owning section's favour**, landing
`Page<Namespace>` rather than `Page<NamespaceSummary>`, with the specification left needing
correction.

**Decision:** the area section owns the element type and the operation name; §14 owns the
route, the prefix, the envelope and the paging semantics. So: `Ssh.ListRolesInfo` returns
`Page<SshRole>`; the queue listings are `Pki.Csr.ListInfo` and `Pki.SignRequests.ListInfo`;
`Pki.ListCertificatesInfo` returns `Page<CertificateSummary>`, where the two agree anyway.
Note this cuts the other way from D-M9-7, and for the same reason both times: §14 owns the
*route family*, the area section owns the *types and names*. Each question goes to whichever
section defines it.

**Rejected:** *§14 wins throughout, for internal consistency with D-M9-7.* Rejected because
it would invent `SshRoleSummary` — a type with no field list in any document — where §10
already defines `SshRole` in full, and because it would contradict the `Page<Namespace>`
precedent, giving two milestones opposite answers to one question.

### D-M9-10 — where §09 defines no response shape, the binding returns `Response`. Slice c stays row 2

**Rewritten after the revision-2 re-review, which was right that the first version sent a
delegate to guess at a higher rung, and understated the problem.** The gap is not two
`Page<T>` element types. `09-pki-engine.md:86-108` — both queue tables — is **two columns,
operation and HTTP**. It defines no response shape for *any* of its rows: not
`Pki.Csr.Read`, not `Generate`, not `SetSigned`, not `SignRequests.Read`, `Preflight`,
`Approve` or `ApproveVerbatim`. `09-pki-engine.md:11-15`'s Types block covers the
certificate surface and nothing here. §14 names `CsrSummary` and `SignRequestSummary` at
`:118-119` and defines neither. Appendix A:211-212 lists the routes and no types.

So roughly eight return types are undefined, and `D-M1c-25` forbids guessing any of them.
Routing to row 3 does not help: Claude Opus 5 guessing is still guessing. The rung was never
the problem.

**Decision:** where §09 defines a response shape, the binding returns it. **Where §09 defines
none, the binding returns the existing public `Response`** (`Response.cs:10`), whose `Data`
is the untyped `data` object and whose `RawBody` exists for exactly this — "forward-compatible
field access (TRN-043)". This invents **no public record type**. The two `*-info` listings
return `Page<IReadOnlyDictionary<string, JsonElement>>` on the same principle. Each such
member carries a doc comment naming the gap and pointing at **R-31**.

**The fallback stops at `PKI-002`.** Added after the revision-3 review, which found that the
rule as first written would launder an exported private key through an untyped container.
`09-pki-engine.md:90` gives `Pki.Csr.Generate` the parameters `exported` and `exportable`,
so with `exported` its response carries a private key. `Response.Data` is
`IReadOnlyDictionary<string, JsonElement>` and `Response.RawBody` is documented at
`Response.cs:40` as being "for **diagnostics** and forward-compatible field access" — so the
key would land, unredacted, in two places, one of them the member most likely to be logged.
`PKI-002` is one of M9's own 11 IDs and comes off the baseline in slice a; shipping that path
unredacted would report `PKI-002` covered while a path it governs is not, which is precisely
the half-covered overclaim D-M9-11 refuses a few lines below.

So: **the `Response` fallback applies only to rows whose response provably carries no
`PKI-002`-governed field.** `Pki.Csr.Generate` returns a typed record with a
`SecretString PrivateKey`, transcribed from the sibling shape the specification already
defines at `09-pki-engine.md:44` — `Pki.GenerateIntermediate` → `{Csr, KeyId?, PrivateKey?,
PrivateKeyType?}`. That is transcription from a defined shape, not invention, so slice c
stays at row 2. Any other queue row that can return exported key material gets the same
carve-out, and slice c's brief requires it to identify them from the request parameters
rather than assume `Generate` is the only one.

**Two notes for the briefs**, neither a decision. (1) `Page<IReadOnlyDictionary<string,
JsonElement>>` is a new public generic instantiation that puts `System.Text.Json` into
`PublicApiSurface.txt`; `Response.Data` already does, so this is precedent-following, but
R-31's "replacing it later is a breaking change on an unpublished API" applies to the
instantiation as well as to `Response`. (2) `appendix-c-conformance-fixtures.md:128` names a
fixture for `certs-info` and none for `csr-info` or `sign-request-info`, so the untyped
listings ship covered by **unit tests only** — correct per Appendix C, and stated so that a
later reader does not read the corpus as evidence for them.

Slice c therefore stays at **row 2**: nothing is being designed, and the return type is a
type the SDK already ships. D-M9-3's row-2 claim holds for every slice, and M9 has no row-3
slice.

**R-31 is allocated here** (R-30 is D-M9-11's; R-29 was the previous highest) for the typed
records: defining `CsrSummary`, `SignRequestSummary` and the queue read shapes is a
`specifications/` change, hence R3, owned by **M10**, which builds the rest of this surface.
Replacing `Response` with a typed record later is a breaking change on an **unpublished**
API — nothing here publishes to a registry — so it is cheap if taken deliberately at M10,
which is R-29's reasoning applied to the same kind of case.

**Rejected:** *drop the two `ListInfo` listings from M9 and book them* (the re-review's
proposal). It is the smaller change and it was close. Rejected because it fixes two of about
eight undefined returns while reading as though it fixed the class, and because dropping
routes Appendix A lists leaves "sections 09–10 complete in .NET" — M9's stated exit — false
for a reason a later reader would have to reconstruct. Returning `Response` keeps the surface
complete and the ignorance explicit.

**Rejected:** *let slice c mirror `CertificateSummary`.* Rejected because a CSR is not a
certificate — no serial, no `not_after`, no issuer — so the mirror would be a guess wearing a
precedent's clothes.

**Rejected:** *ship nothing for the queues and tell consumers to use `Client.Logical`.*
Rejected because it discards the half the specification *does* define — every route, verb,
path and request body — which is the part a typed binding is most valuable for.

### D-M9-11 — `PKI-030`'s second limb is unimplementable as specified; it stays baselined

`09-pki-engine.md:107-108` is two sentences under one ID: `Reject` with an empty reason →
`BV-INPUT-001` (client-side, implementable), and a 500-pending queue-cap breach →
`BV-QUOTA-002 QueueFull` **"by message"**.

The code exists (`appendix-b-error-catalogue.md:128`, generated into `ErrorCatalogData.g.cs`).
**The recognition row does not.** Appendix B §2's recognition table carries `BV-QUOTA-001`
(`namespace quota exceeded`, line 245) and rows for every `BV-PKI-*` and `BV-SSH-*` code, but
nothing for `BV-QUOTA-002`; §09's own recognition table does not carry the server message
either. Recognition by message requires the message, and **no document states it**.

**Decision:** slice c implements the first limb and does **not** invent a message string.
`PKI-030` **stays on the traceability baseline**, so M9 lands 10 of its 11 IDs and says so.
Adding the row would be an Appendix B prose change (R3), a generated-catalogue change, and —
by Appendix B §3's invariant that every recognition row produces its code for at least one
fixture in Appendix C — a seventh fixture, moving the corpus off 253. All three are outside
M9. It is booked as **ROADMAP risk row R-30** — the number is allocated here, by the
orchestrator, rather than by whichever slice writes the row, because "next free number"
has collided twice in this project. R-29 is the current highest. Its owner is **M10**, which
builds the rest of the PKI queue surface, and it needs the server's actual message text.

**Rejected:** *guess the message as `queue is full` and ship the rule.* Rejected under
D-M1c-25 and under R-23's lesson directly: a recognition rule that cannot fire, or fires on
the wrong string, passes its own fixture and fails in production. R-23 shipped in `v0.5.0`
and was found three milestones later.

**Rejected:** *take `PKI-030` off the baseline anyway, since the implementable limb is
implemented.* Rejected under **CLA-005**. Half a requirement reported as covered is the
`AUT-060` exit overclaim the ROADMAP already records once.

**"Stays baselined" does not mean "nothing landed".** `Reject` with an empty reason fails
client-side with `BV-INPUT-001`, shipped in slice c with its own test. The baseline entry
records the *second* limb's absence, and slice c's handback states which limb it covered so
a later reader does not read the row as "the queue has no validation".

### D-M9-12 — `SSH-002`'s `0644` is applied at creation, and section 10 is not only endpoints

`10-ssh-engine.md:42-43` (`SSH-002`) requires `Ssh.WriteCertificateFile(signedKey, path)`
writing `<key>-cert.pub` with `0644`. **D-M9-1's framing — that sections 09 and 10 are tables
of HTTP endpoints — is wrong about this one requirement**, and the exception matters because
it is a filesystem write with a POSIX mode bit, reached by users through
`17-usage-guides.md:318`. Correcting revision 1 rather than leaving the delegate to discover
it.

**Decision:** follow `Internal/TokenFiles.cs:17,37-42,48` exactly, differing only in the mode.
The mode is set via `FileStreamOptions.UnixCreateMode` **at creation**, never by a `chmod`
after the write — write-then-chmod leaves a window in which the file exists with the process
umask's default mode. Windows is reached through `PlatformNotSupportedException` rather than
an `OperatingSystem.IsWindows()` guard, so the mode is always attempted and never skipped by
a wrong guess about which platforms support it.

Note `0644` is **world-readable by design**: an SSH certificate is public material, unlike
`CFG-031`'s `0600` token. The mode is copied from the requirement, not from the precedent.

**Rejected:** *write the file and `chmod` it, which is simpler and portable.* Rejected on the
creation window, which is the reason `TokenFiles` is shaped the way it is — and rejecting it
again here is cheaper than having a reviewer rediscover the argument.

### D-M9-13 — **withdrawn.** `Sys.ListNamespacesInfo` is correct as shipped

The first draft of revision 2 recorded a defect here: `SysOperations.cs:790` issues
`sys/namespaces-info` with unpinned `options` while `14:104` specifies
`/v2/sys/namespaces-info`, and its sibling `UserpassOperations.cs:184` pins `v2` — so the two
disagreed, which looked like a defect rather than a convention. It was to be repaired by
slice d with its own **Fixed** changelog entry.

**There is no defect.** Under `appendix-a-endpoint-catalogue.md:3-5`, the `v1` against
`Sys.ListNamespacesInfo` at `:40` *means* "uses `ApiPrefix`, do not pin", and unpinned
`options` is the literal implementation of that. `users-info` differs because `:87` says "v2
recommended" for it specifically. The two are not inconsistent; they are two different
catalogue instructions, each followed. R-27 records that M8 left `sys/namespaces-info` on
`ApiPrefix` **deliberately**, to match its accepted fixture.

**Decision:** nothing to repair. Slice d's scope loses this item, M9 changes no landed
binding, and the milestone is purely additive.

**Why this is recorded instead of deleted.** The false finding and the true one (D-M9-7's
reversal) have the same single cause: a Prefix column read without its preamble. Two agents
made it independently — the architecture review raised it as B1 and this record adopted it —
and the second endorsement added no evidence, only agreement. That is worth a paragraph in a
milestone whose governing lesson is that an author can be right about what to do and wrong
about why.

### D-M9-14 — slice b has no requirement IDs, so its gate is stated explicitly

Slice b covers roughly 30 operations (`appendix-a-endpoint-catalogue.md:194-201,209-210,213`)
and carries **no** requirement ID — sections 09's CA-lifecycle, managed-keys, tidy and ACME
tables state routes and fields but mint no `PKI-nnn`. A slice with no traceable ID has no
spec-conformance acceptance criterion, so its gate would otherwise check only style and tests.

**Decision:** slice b's gate is **route-table conformance**, checked against two independent
documents. Every operation must match `09-pki-engine.md` *and*
`appendix-a-endpoint-catalogue.md:194-213` on name, HTTP verb, path and field names; any
disagreement between those two is a finding to return, not a choice to make (D-M9-7 is what
one looks like). Plus: the constraints a's IDs impose carry into b — `PKI-001` verbatim PEM,
`PKI-002` `SecretString` for `GenerateRoot`'s and `GenerateKey`'s exported `PrivateKey`,
`PKI-010` CSV round-tripping — and `PKI-002` in particular has more instances in b than in a.
The brief says so, and the reviewer checks those rather than an ID list.

**The gate includes one negative check**, because §09 carries two unnumbered MUSTs in b's
scope and a route-table comparison catches only the first. `09-pki-engine.md:115-117`
requires that `{mount}/acme/config` be typed as `Pki.Acme.ReadConfig/WriteConfig/DeleteConfig`
**and** that the SDK MUST NOT wrap the RFC 8555 protocol paths (`acme/directory`,
`new-nonce`, `new-account`, …) beyond `Pki.Acme.DirectoryUrl(mount) -> string`. A
prohibition is not a row in a table, so the gate names it separately: slice b is checked for
the *absence* of those wrappers, not only the presence of the config three.

**Rejected:** *mint new requirement IDs for the uncovered routes.* Rejected — requirement IDs
live in `specifications/`, minting them is an R3 prose change, and M9 does not make one.

### D-M9-15 — slice a was dispatched before the record was entitled to row 2, and was patched twice in flight

Recorded because it is a process defect in this record's own execution, and an unrecorded
one would be invisible to the next milestone.

Slice a was dispatched immediately after revision 1, before the architecture review. The
review then blocked revision 1 for leaving four public-API decisions unmade — and two of
them, the `*-info` prefix and `PAG-004`, were in slice a's scope. Slice a received a
mid-flight correction pinning `certs-info` to `/v2` (D-M9-7 as first written), and a second
retracting it when the Appendix A legend was read. That is **four crossings of the tree
boundary in one work package**, against **FAM-003**, which sets two as both the floor and
the cap, and against **TOK-003**, which has the brief authored once, before dispatch.

**Decision:** slice a is **not** restarted. The two prefix corrections are self-cancelling,
so a restart would buy a cleaner history and pay for it with a full slice's work.

**The net delta is three additions, not one.** An earlier draft of this decision said "the
original brief plus D-M9-8's iterator", which was false by this record's own text, in a
record whose theme is that an author can be right about what to do and wrong about why. The
three are: (1) D-M9-8's `PAG-004` iterator; (2) D-M9-7's requirement that each unpinned
`*-info` operation carry a comment recording *why* it is unpinned; (3) D-M9-4's
slice-specific corpus target, 247 → 250. All three are readable directly off the diff, which
is what makes the judgement survive the correction — and `FAM-003` is already breached, so a
restart buys history rather than correctness.

The handback gate therefore carries **four** explicit checks beyond the brief's own: no
`ApiVersion` override reached `Pki.ListCertificatesInfo` or its fixture, and each of the
three additions above landed — rather than trusting that the retraction arrived and was
applied. Slices b, c and d are dispatched only after this record is approved.

**Rejected:** *stop slice a and re-brief* (the re-review's proposal). Rejected on cost
against a defect whose only residue is one attribute in one operation and one URL in one
fixture, both of which the gate reads directly. The judgement would flip if the corrections
had been cumulative rather than self-cancelling.

**The generalisable lesson, which is the reason this is a numbered decision and not a
footnote:** dispatching the first slice in parallel with the review *of the record that
governs it* looked like free parallelism and was not. The review exists to find exactly the
unmade decisions the slice will trip over. Nothing was saved, and two corrections were spent.

### D-M9-16 — a route that can return key material and has no defined shape returns a **redacting** wrapper, never `Response`

**Handback ruling, slice a.** D-M9-10 wrote the rule — the untyped fallback applies only to
rows whose response provably carries no `PKI-002`-governed field — while looking at slice c.
Slice a's gate found the first instance had already shipped in **slice a**, in a route
neither the record nor the review had looked at: `Pki.ExportCertificate`
(`09-pki-engine.md:64`, `GET/POST {mount}/cert/{serial}/export`) takes `includePrivateKey`
and `mode = normal|backup`, returns `IReadOnlyDictionary<string, JsonElement>`, and its
`PKI-002`-tagged test drives it with `includePrivateKey: true, mode: "backup"`. The evidence
offered for the requirement included the path that breaches it.

Unlike `Pki.Csr.Generate`, there is **no sibling shape to transcribe**: §09 defines no
response for this route, and `Pki.ExportIssuer` is not one — `09-pki-engine.md:52` says
private keys are never exported there, so it is the opposite case.

**Decision:** the binding returns a single-member redacting wrapper — `PkiCertificateExport`
carrying a `SecretString Payload` holding the response body verbatim. This **invents no
field name**, because it asserts nothing about the payload's internal structure; it asserts
only that the payload may contain key material, which the route's own parameters establish.
`PKI-001`'s verbatim rule is preserved: the bytes are not re-encoded, only wrapped.

**The rule this generalises to, binding on slices b, c and d:** where §09 defines no response
shape **and** the request can cause key material to be returned, the binding returns a
redacting wrapper — not `Response`, and not an untyped map. Where §09 defines no shape and no
key material is possible, D-M9-10's `Response` fallback stands. Slice b inherits this
directly: `Pki.GenerateKey(Internal|Exported, …)` is the same shape, and D-M9-14 already
notes `PKI-002` has more instances in b than in a.

The typed shape, when the specification defines one, is booked under **R-31**, whose scope
is widened from the two queue tables to "every §09 route whose response shape is undefined" —
`cert/{serial}/export` included.

**Rejected:** *defer `Pki.ExportCertificate` out of M9 and book the whole route.* Rejected
because the route's request half is fully specified — verb, path, all four parameters — and
deferring discards that to avoid a problem only the response half has. Over-redaction is
safe and reversible; the deferral leaves a specified route unbound for a reason a later
reader would have to reconstruct.

**Rejected:** *put `PKI-002` back on the traceability baseline and ship the untyped map.*
Rejected because the requirement is satisfiable here — a wrapper costs one small type — so
baselining it would be reporting a gap the milestone chose rather than one the
specification forced. That is the opposite of D-M9-11, where the gap is genuinely forced.

**Required with it:** the `PKI-002`-tagged test must assert redaction — that the payload does
not appear in the wrapper's `ToString()` — rather than asserting the map is non-null. A test
that exercises a breach and asserts nothing about it is worse than no test, because it
reports the requirement as evidenced.

### D-M9-17 — "reuse the sibling's named fields" means the **whole** named set

**Handback ruling, slice a.** `09-pki-engine.md:30` gives `Pki.Sign` as `csr` (required)
"+ overrides", with no list anywhere. Slice a modelled the overrides by reusing
`IssueRequest`'s fields — the right instinct — but reused **five of nine**, carrying
`CommonName`, `AltNames`, `IpSans`, `Ttl`, `IssuerRef` and dropping `KeyRef`, `UpnSans`,
`EmailSans`, `AdSid`.

The gate's ruling is correct and is adopted: a subset has no document behind it. D-M9-10's
transcription argument works because `09-pki-engine.md:44` writes the sibling's shape out in
full and the *whole* shape is taken. Taking part of a set is a selection, and a selection of
public API fields is a shape decision a row-2 delegate may not take (**FAM-002**).

**Decision:** `SignRequest` carries `IssueRequest`'s complete named set. Concrete failure the
subset causes: a caller signing a CSR against a role with `allow_email_sans` or
`allow_upn_sans` cannot express those SANs at all, and the typed path offers no escape hatch.

**Rejected:** *keep the subset and document it.* Rejected — the comment at `PkiTypes.cs:155`
is where the delegate recorded the decision, and a code comment is not where a public-API
decision becomes non-reopenable (**CLA-008**). That is what this record is for, which is why
the ruling is here.

### D-M9-18 — `Crl.Crl` is renamed `CrlPem`, and the deviation is recorded here

`09-pki-engine.md:15` names the member `Crl` inside a type named `Crl`. C# forbids it
(CS0542), so the binding cannot transcribe the name. Slice a chose `CrlPem` and recorded it
in an XML doc comment.

**Decision:** `CrlPem` is accepted — the deviation is forced, minimal, and self-describing
(the member is a PEM). It is recorded **here** rather than only in the source, because a
deviation from a specification field name is exactly the kind of thing a Stage 2 parity pass
must find in one place. Rust and Python have no such restriction and may use the
specification's name; the divergence is a language artefact, not a behavioural one, and
needs no parity exception under `CLA-003`.

### D-M9-19 — `Pki.ExportCertificate` binds POST only, and the restriction is recorded rather than worked around

**Handback ruling, slice a, second round.** F5 asked for the GET form *or* a recorded
restriction. The slice chose the GET form and introduced a worse defect than the one it
closed: a `useGet: true` branch that put `SecretString password` into the **query string**.

`Internal/ErrorPaths.cs:9,25-45` redacts by rewriting the segment after `lookup`, `renew`,
`revoke` and `revoke-orphan`. It does not inspect query strings, so
`pki/cert/aa:bb:cc/export?…&password=…` passes through `Redact` unchanged into three
consumers: `RequestEvent.Path` via `Internal/RequestExecutor.cs:892` — the `CFG-080` /
`TST-051` observer, which in practice is a consumer's logger and **fires on success** —
`BastionVaultException.Path` and `Details["path"]`, and
`Internal/HintEnrichment.cs:84`'s "The path as sent was …". The slice's own test asserted
`password=hunter2` was present in the URL, under a `[Requirement("PKI-002")]` tag.

**Decision:** the `useGet` parameter and its GET branch are removed. `Pki.ExportCertificate`
binds **POST only**, where the password travels in the body and no logging surface reads it.
The one-verb restriction is recorded here, which is what F5's second option asked for and
costs zero public surface.

Three reasons the parameter should not have existed, beyond the leak. (1) Neither
`09-pki-engine.md:64` nor `appendix-a-endpoint-catalogue.md:204` states a **query grammar**
for this route — `format=`, `include_private_key=`, `mode=`, `password=` were constructed,
which is **D-M1c-25**. (2) It is not the idiom: `ReadCrl(pem)` and `ReadIssuerCrl(…, pem)`
look similar but the specification itself exposes that choice, writing `{mount}/crl[/pem]`;
no binding in this SDK exposes a **verb** toggle. (3) **CLA-007** — recording the restriction
is one line against a public parameter plus a branch.

**If the GET form is wanted later, `ErrorPaths.Redact` must cover query parameters first.**
That is a transport-layer, cross-cutting change and is not slice a's; it is booked as
**R-32**.

**Rejected:** *keep `useGet` and redact the password at the call site.* Rejected because the
redaction would live in one operation while the hole stays open for every future one — the
defect is in `Redact`'s coverage, not in this route.

### D-M9-20 — the secret-handling rule has a request half, and it binds slices b, c and d

The gate's closing observation, promoted to a decision because slice b's scope contains the
next instance: **a redacting wrapper on the response does not help if the request put the
secret in the path.** D-M9-16 wrote only the response half, and B2 is what the missing half
costs.

**Decision:** secret material never appears in a URL path segment or query string. It
travels in a request body. This binds every remaining slice, and the instance already
visible is slice b's `Pki.ImportKey({private_key, name, exportable})`
(`09-pki-engine.md:112`) — a POST with a body, which is correct by construction, and which
a slice must not "simplify" into a query form. Slice c's `Pki.SignRequests.*` and slice d's
`Ssh.Creds` / `Ssh.Verify` (the OTP is `SecretString`) inherit it too.

The two halves together: **request side** — secrets go in bodies, never in paths or queries;
**response side** — a response that can carry key material returns a typed member or a
redacting wrapper, never an untyped map (D-M9-16). Each slice's gate checks both.

**Rejected:** *leave it as a review convention rather than a decision.* Rejected because it
was a review convention for the whole of revisions 1 through 4 and the code breached it
twice — once on the response side (B1) and once on the request side (B2), the second time
**in the repair for the first**.

## Consequences

1. The .NET public surface grows by three top-level entry points and roughly **91**
   operations. `PublicApiSurface.txt` grows correspondingly, and each slice regenerates it.
2. Traceability moves by **10** IDs, not 11 — `PKI-030` stays baselined under D-M9-11.
   `131 baselined` → `121`. *(Revision 1 said 11 and `→ 120`.)* The `294 covered` figure is
   quoted from `ROADMAP.md` §2 and is re-derived by each slice's traceability run rather than
   trusted here.
3. The fixture corpus moves 247 → 250 at slice a → 253 at slice d.
4. **M9 is purely additive.** No landed binding changes behaviour — the one candidate,
   `Sys.ListNamespacesInfo`, turned out to be correct as shipped (D-M9-13, withdrawn).
5. No `specifications/` prose change, no generated-catalogue change, and no Rust or Python
   change beyond the fixture-count tripwire the corpus move forces (D-6 freeze). **This
   means M9 makes no such change, not that none is needed** — see item 6, which is why two
   of this record's fifteen decisions exist at all.
6. **Two R3 specification corrections leave M9 as booked risk rows, not as edits**:
   **R-30**, the missing `BV-QUOTA-002` recognition row plus the Appendix C fixture its §3
   invariant then requires (D-M9-11); and **R-31**, the undefined response shapes across
   both PKI queue tables (D-M9-10). Both are owned by M10 and, being R3, need human
   confirmation before release. R-30 and R-31 are allocated by this record, centrally.
8. **A third risk row, R-32, is opened and is not M9's to close**: `Internal/ErrorPaths.cs`
   redacts path *segments* and does not inspect query strings, so any future route that
   places secret material in a query would leak it through `RequestEvent.Path`,
   `BastionVaultException.Details["path"]` and `HintEnrichment`'s "The path as sent was …".
   No shipped route does so today — M9 removed the one that would have (D-M9-19) — which
   makes this **latent, not active**. R2, owned by the transport surface rather than by an
   engine milestone. Found by slice a's second handback gate.
7. **R-27 gains an answer and keeps its row.** Its instruction to assume a third
   section-14-vs-catalogue contradiction until all seven `*-info` rows are checked is
   discharged: they are checked, and there is no third (D-M9-7). The row stays open because
   the underlying `specifications/` reconciliation is still unmade.

## Open questions

1. **What is the server's message on a PKI queue-cap breach?** Blocks `PKI-030`'s second limb
   (D-M9-11, R-30). Not answerable from this repository — it needs the server source or a
   capture, which is the same provenance every recognition row in Appendix B has.
2. **What are the field lists for the PKI queue read and listing responses?** Blocks typed
   records for both queue tables (D-M9-10, R-31). Same provenance problem: the specification
   describes the requests and is silent on the responses.
3. **Is `/v1/{mount}/certs-info` in fact served?** D-M9-7 rests on Appendix A's legend, which
   is the best authority this repository holds, not a measurement — nothing in M9 talks to a
   server. **M12's integration suite is where this becomes checkable**, and it is the right
   place: a prefix assumption that no fixture can falsify is exactly what an integration
   suite exists for. Raised here so M12 inherits it rather than rediscovering it.
4. R-14 (§10 question 4) is unchanged by this milestone and is not M9's to close.
