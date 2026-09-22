# DR-0017 — M10: remaining engine bindings and identity, `Complete` target in .NET

**Status:** accepted (framing), revision 1 (2026-09-22). Authored by the Strategic
Orchestrator as the milestone's framing record, before any slice is dispatched
(**CRS-001**). Each slice appends its own `D-M10-n` entries below rather than opening a
second record.

**Risk tier:** R2 (`agents.md` §5.3), assigned here before dispatch. No auth/secret/TLS
surface is newly introduced (`Resources.Secrets`, `Ldap` bind-password and `Rustion`
credential fields reuse `SecretString`/write-only conventions already settled at M2/M4),
so this stays off the R2→R3 floor in `skills/claude/SKILLS.md` **CRS-003**; the `RSC-002`
redaction requirement and the LDAP/Rustion secret fields are the reason it is not R1.

**Milestone:** M10, five slices · **Date:** 2026-09-22

**Supersedes nothing. Amends nothing in `specifications/`.**

**Discharges:** `ROADMAP.md` §4's M10 row — `IDN-001`, `IDN-002`, `RSC-001`, `RSC-002`,
`FIL-001`, `LDP-001`, `RUS-001`, `RUS-002`, `RUS-003` (9 IDs) — plus, inherited from M8/R-29,
the rest of `Auth.Userpass.Admin.*` (no requirement ID of its own; catalogue-only surface).

**Inherits:** [DR-0004](0004-m1b-transport.md) (the one transport seam, the single retry
loop), [DR-0005](0005-m1c-error-model.md) (recognition, enrichment, D-M1c-25's "never a
plausible guess" rule), [DR-0009](0009-m4-kv-engine.md) / [DR-0013](0013-m8-transit-totp-and-efficiency.md)
(the engine-binding idiom this record extends: an operations class over `LogicalOperations`,
a `*Wire` static for parse/serialise, `SecretString`/`SecretBytes` for secret-bearing
fields, cursor pagination as `Page<T>`, `PagingWire` reused rather than re-implemented —
D-M8-7), [DR-0012](0012-m7-system-api-remainder.md) D-M7-27/D-M7-43 item 5 (`Client.Identity`
already exists for `sys/identity/*`; section 12's wider `Identity.*` is additive to the
*same* class, not a new one).

## Problem

`ROADMAP.md` §4 books M10 at 9 requirement IDs, Large size, R2 risk. As with M9, the count
understates the surface: `specifications/12-other-engines-and-identity.md` describes eight
distinct mounts — Identity (kernel), Asset groups, Resources, Files, LDAP, Cert lifecycle,
Notifications, Rustion — and none of the last six has a single line of implementation on
disk today (verified: no `AssetGroup`, `Resources`, `Files`, `Ldap`, `CertLifecycle`,
`Notifications` or `Rustion` type exists in `dotnet/BastionVault.IntegrationSdk/`). Cert
lifecycle and Notifications carry **no requirement ID at all** — every MUST governing them
is the generic Shape A envelope and standard error mapping sections 03/04 already bind — so
they are real, mandatory deliverable surface that the 9-ID count is silent about, exactly as
`R-14`'s discussion of "engine" vs. "bindings" warned M8 and M9's text already flagged for
M10 (`ROADMAP.md` §2, "Engine means REST bindings").

A second, separately-sized surface rides in on R-29: `Auth.Userpass.Admin.*`
(`ListUsers`, `ReadUser`/`WriteUser`/`DeleteUser`, `SetPassword`, `Unlock`,
`ReadFido2`/`DeleteFido2`, `ReadLockout`/`WriteLockout`, `ReadMfa`/`WriteMfa` —
`appendix-a-endpoint-catalogue.md:86-93`) has zero implementation beyond the single
`ListUsersInfo` member M8e shipped. It carries no requirement ID either (section 05 and
section 15 mention `Unlock` only as an M12 integration scenario, `15-testing-requirements.md:206`),
but `ROADMAP.md`'s M10 row is explicit that M10 "builds the rest of that surface and must
decide whether the member moves" (R-29, D-M8-46). Not booking it here would repeat R-29's
own warning: a gap recorded somewhere a briefer does not look survives a milestone (the
row's own citation of R-16's history).

## Decisions

### D-M10-1 — Five slices, dispatched serially

**Decision.** M10 splits into five slices, each a self-contained `eng-implementation`
brief, dispatched one at a time because all five regenerate the same
`PublicApiSurface.txt` (same constraint as M9, D-M9's framing):

| Slice | Scope | IDs |
|-------|-------|-----|
| **a** | Identity kernel (`identity/entity`, `group`, `sharing`, `owner`) + Asset groups (`resource-group/`) | `IDN-001`, `IDN-002` |
| **b** | Resources (`resource`) + Files (`files`) | `RSC-001`, `RSC-002`, `FIL-001` |
| **c** | LDAP (`openldap`) + Cert lifecycle (`cert-lifecycle`) + Notifications (`notifications`) | `LDP-001` |
| **d** | `Auth.Userpass.Admin.*` — the rest of the surface, plus the R-29 rename | — (catalogue-only) |
| **e** | Rustion (`rustion`) — targets, master key, sessions v1/v2, recordings, policy/groups/dispatcher/telemetry | `RUS-001`, `RUS-002`, `RUS-003` |

**Rejected — one Enterprise-tier brief.** Eight mounts and 9+ IDs exceeds one brief's token
budget (**TOK-011**) the same way M9's 91 operations did; M9's four-slice precedent is
followed rather than re-derived (**TOK-008**).

**Rejected — slice by requirement-ID count instead of by mount.** `RUS` alone (3 IDs) is
Rustion's whole surface and the largest slice by code; `LDP` (1 ID) covers only the LDAP
mount's insecure-TLS guard while the mount itself has ~9 operation groups. Slicing by ID
count would produce wildly uneven slices and split a mount's error-mapping tests from its
own bindings. Slicing by mount, as M9 did by engine, keeps each slice's tests and bindings
together.

### D-M10-2 — Slice a extends `Client.Identity`, does not create a second identity surface

**Decision.** Section 12's `Identity.Self()`, `Identity.Aliases()`, `Identity.Groups`,
`Identity.Sharing`, `Identity.Owner` are added as new members of the existing
`IdentityOperations` class (`dotnet/BastionVault.IntegrationSdk/IdentityOperations.cs`),
reached via the existing `Client.Identity` property. This is the placement M7 flagged for
confirmation before this milestone starts (D-M7-43 item 5) and it is confirmed: moving
SYS-080's four sub-surfaces to a different property now would be the breaking change D-M7-27
was written to avoid, for no gain, since Appendix A already reads both surfaces under one
`Identity.*` canonical name.

**Rejected — a second property (`Client.IdentityKernel` or similar).** Appendix A's
canonical-operation column and `12-other-engines-and-identity.md`'s own table both write
`Identity.Self`, `Identity.Groups`, etc. with no qualifying prefix distinguishing them from
SYS-080's `Identity.Profile`. Two `Identity`-rooted properties would be the divergence
D-M7-27 already rejected once, now on the other side of the same class.

### D-M10-3 — R-29 resolved: `Auth.Userpass.Admin.ListUsersInfo`, moved from the flat name

**Decision.** Once slice d builds the rest of `Auth.Userpass.Admin.*`
(`appendix-a-endpoint-catalogue.md:86-93`), `UserpassOperations.ListUsersInfoAsync` (and its
`ListUsersInfoAllAsync` iterator) move under a new `Admin` sub-client, matching Appendix A's
nesting exactly: `Auth.Userpass.Admin.ListUsersInfo`. D-M8-46 named this as cheap now and a
surprise later, and it is now cheaper than the alternative it warned about: shipping eight
new siblings under `.Admin` while their ninth relative sits flat under `Userpass` is a worse
anticipatory-avoidance failure than the one-member speculative sub-client D-M8-46 originally
declined to build — the sub-client is no longer speculative once this slice exists.

**Rejected — leave `ListUsersInfo` flat, nest only the eight new members.** Reproduces
R-29 exactly, permanently this time: nothing schedules a second look once the "M10 decides"
marker is spent. CLA-007's anticipatory-structure objection no longer applies, because the
structure is no longer anticipatory — this slice populates it.

**Consequence.** This is a breaking rename of a member M8e shipped, on the unpublished-API
limb of **CRS-004** (`build-artifacts.yml` builds and never pushes — no release consumes
this rename). Slice d's brief carries the rename explicitly so it is not mistaken for scope
creep by the handback gate.

### D-M10-4 — `identity.self`'s mandatory fixture is a recorded gap, not a guess

**Decision.** `appendix-c-conformance-fixtures.md:130` lists `identity.self` in the
mandatory fixture set; only `identity.sharing.target-base64url.json` exists on disk today.
`FIX-010` requires a fixture's response body be copied from a real server exchange. This
repository holds no such capture for `GET identity/entity/self`, and
`12-other-engines-and-identity.md:12`'s field list (`entity_id, username, mount_path,
role_name, primary_mount?, primary_name?, created_at?, aliases[]?`) is a description, not a
capture. **Do not invent one** — `R-23` is the precedent for a fixture whose body was never
real passing its own test while proving nothing (D-M9-11 applies the same rule to `R-31`,
one milestone ago). Recorded as **R-35** below rather than folded into slice a's exit claim.

**Consequence.** `Identity.Self()` and `Identity.Aliases()` ship in slice a with ordinary
unit-test coverage against a hand-built response the test itself controls (not a canonical
fixture), and IDN-001/002's traceability rests on that unit coverage, not on the missing
fixture. The gap goes into `dotnet/README.md`'s CNF-002 list, as `PKI-030` did at M9.

## Rejected globally

| Option | Why rejected |
|--------|--------------|
| Treat `RUS-002`'s "node-local, no failover" as requiring a new client-side capability | `DSC-045`'s internal node-local seam already exists (M5, reused by `sshbroker` at M9); RUS-002 is a *citation* of an existing mechanism, not a new one |
| Type `CertLifecycle`'s and `Notifications`' bodies as public records now that they have no documented response shape beyond field lists | Both areas' tables give named fields with no separate response-shape ambiguity like R-32's untyped PKI maps — unlike PKI, section 12 states the fields the response carries, so typing them is not the guess R-32 flagged. No R-32 analogue is opened here |
| Build `Auth.Userpass.Admin.*` in slice a alongside `Identity`, since both start from "nothing exists" | Different mount, different section (05/Appendix A vs. 12), different reviewer attention (a rename decision vs. new-surface review). Bundling them risks the R-29 rename getting lost in a larger diff, the failure mode D-M8-46 already named |

## Open questions for the Strategic tree

1. **R-31, R-32 and `Pki.GenerateIntermediate`'s `issuer_name`** (inherited from M9) are
   **not** M10 slice work. Each needs input this repository does not hold (a server capture
   or the server source) and stays exactly as M9 left it — booked with an owner, not
   dispatched. Confirmed here so no slice brief tries to close them.
2. **R-33** (query-string redaction gap) is unrelated to M10's mounts on current evidence —
   none of section 12's routes place secret material in a query string — but slice b
   (`Resources.Connect.MfaVerify`'s `connect_ticket`) and slice e (`Rustion.Session.Open`'s
   `credential_material`) are reviewed against it explicitly, since both are exactly the
   shape (a single-use secret token) that produced the M9 near-miss.
