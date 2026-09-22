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

### D-M10-5 — `Files.Sync`'s credential fields ship as an opaque bag, recorded as **R-36**, not guessed

**Decision.** Section 12 states only that `SyncTarget`'s per-target credential fields exist
and are write-only (`12-other-engines-and-identity.md:65`); it names no wire field for
`local-fs` or `smb`. Slice b types `SyncTarget.Kind` (documented) and carries everything
else as an opaque `Fields: JsonElement?` merged into the request body verbatim, rather than
inventing `username`/`password`-shaped members — the same "never a plausible guess" rule
D-M1c-25 states and R-23/R-31 already cost this repository once. The R2 handback review
confirmed this is not a live leak: `RequestObserver.cs`'s `RequestEvent` never carries a
request body, so the transport-level observability hook exposes nothing, and no error path
(`BastionVaultException`, `HintEnrichment`) reads a request body back either. `Fields` is
guarded client-side (a non-object value or a `kind`-shadowing key inside it is rejected as
`BV-INPUT-001` before any request is sent), so a malformed caller value cannot silently ship
a credential-less sync target.

**Rejected — invent plausible field names (`username`, `password`) so the type can carry
`SecretString` members now.** Exactly the guess D-M1c-25 forbids; a wrong guess here is
worse than an opaque bag, because it would silently accept and forward a caller's value
under the wrong key while looking typed and safe.

**Consequence.** Recorded as **R-36**: not a defect, but a forward-compatibility cost. Once
`local-fs`/`smb`'s real field names are known (a server capture or the API source, the same
provenance R-31/R-32/PKI-030 wait on), replacing `Fields` with typed `SecretString` members
is a public-API change the Rust/Python parity pass will otherwise transcribe verbatim as an
untyped bag.

### D-M10-6 — `Resources.Rename`'s wire field names for `Files.RepointResource` are inferred, not guessed at the D-M1c-25 line

**Decision.** `Files.RepointResource(old, new)` names only its two parameters
(`12-other-engines-and-identity.md:64`); no wire field name is given. Slice b sends
`old_resource`/`new_resource`, following the spelling of the documented sibling operation
`Resources.Rename`'s `{new_name}` (`:43`). This is ruled distinct from R-23/R-31's guesses:
those are *silent* — a mis-guessed recognition message or response shape misfires with no
signal to anyone. A mis-guessed **request** field name here fails loudly (a server 400) on
first exercise against a real backend, which is why it is accepted as shipped rather than
held back as a gap.

**Consequence.** Ships as-is. If a real server capture later shows different wire names,
correcting them is a one-field, pre-publication fix (unpublished-API limb of CRS-004), not a
repeat of R-23's silent-failure shape.

### D-M10-7 — `Resources.Secrets.Read` returns a per-value redacting map built from the envelope's `data`, decoded, not the whole envelope's raw text

**Decision.** RSC-002 requires `Secrets.Read`'s *values* to redact; it says nothing about the
envelope. Slice b's first pass wrapped `Response.Raw` (the whole Shape A envelope —
`request_id`, `lease_id`, `renewable`, `warnings` and `data` together) in a single
`SecretString`, found overbroad at the R2 handback review: it seals non-secret envelope
metadata inside the secret, and forces every ordinary read of a value, not only a genuinely
secret one, through `Reveal()`. Corrected to `ResourceSecret.Data:
IReadOnlyDictionary<string, SecretString>`, built from `Response.Data` (TRN-040's Shape A/B
resolution — same accessor every other reader in this slice uses) and keyed by the server's
own field names, never invented ones.

A second defect surfaced by the same review in the corrected code: the per-value wrap used
`JsonElement.GetRawText()` unconditionally, which returns a JSON **string** value still
quoted and escaped (`"hunt\"er2\\path"`, 17 characters) rather than the decoded value
(`hunt"er2\path`, 13). Every other wire-field-to-`SecretString` site in this codebase
(`SshWire`, `PkiWire`, `TotpWire`, `LogicalOperations`, `TokenOperations`, and this same
file's `connect_ticket`) unquotes a string value first. Fixed to `GetString()` for a
`JsonValueKind.String` value, falling back to `GetRawText()` only for a non-string value
(object/array/number), so no information is lost on the fallback path.

**Rejected — leave the whole-envelope `SecretString` wrap.** Broader than RSC-002 requires
and defeats `SecretString.Reveal()`'s own stated purpose (a visible, searchable call site for
reading a secret out) by forcing it onto non-secret reads too.

**Consequence.** `ResourceSecret.Data` is the settled public shape a Rust/Python parity pass
transcribes. The regression is guarded by an equality assertion (not a substring check) over
a fixture value containing an embedded quote and backslash, chosen so a `GetRawText`-only
regression fails the test rather than passing it by coincidence — the same weak-assertion
failure mode that let the original whole-envelope wrap through undetected once already.

### D-M10-8 — `LdapCheckConnectionResult`'s optionality is deliberately looser than every other slice-b/c result type, and that is now recorded

**Decision.** `LdapStaticCred` and `LdapLibraryCheckOut` follow the codebase's usual rule:
an un-`?`-marked member is `required` and a missing one throws `EnvelopeMismatch`.
`LdapCheckConnectionResult` does not — only `Ok` is `required`; `Stage`, `Url`, `BindDn`,
`Host`, `Port`, `Scheme` and `LatencyMs` are all nullable, because a probe that fails early
(`ok: false`) has nothing to report for most of them. Found under-recorded at slice c's R2
handback review: the relaxation was correct but undocumented, leaving no settled rule for
the Rust/Python parity pass to transcribe.

**Consequence.** `LdapCheckConnectionResult`'s doc comment now states the rule directly:
only `Ok` is guaranteed; every other member is present only as far as the probe got. Pinned
by a new test asserting the `ok: false` shape (`Host`/`Port`/`LatencyMs` absent) alongside
the existing `ok: true` full-body case.

### D-M10-9 — `Notifications.Send`'s required `title` is a plain `ArgumentException`, not an invented recognition code

**Decision.** Cert lifecycle and Notifications carry no requirement ID (this record's
Problem section, above) — every MUST governing them is the generic Shape A envelope and
standard error mapping. Slice c's first pass refused an empty `title` with a client-side
`BV-INPUT-001`, which invents a recognition code this repository has no requirement to back.
The actual precedent for a spec `(req)` field with no requirement ID is
`PkiOperations.SignAsync`'s `csr (req)`, which uses a plain `ArgumentException`
(`ArgumentException.ThrowIfNullOrEmpty`). Corrected to match: `SendAsync` now throws
`ArgumentException` via `ThrowIfNullOrWhiteSpace`, not a coded `BastionVaultException`.

**Consequence.** A caller catching `BastionVaultException`/`ErrorCodes.InputInvalidArgument`
on this path catches nothing, by design — the same as every other requirement-ID-less
required-field guard in this codebase. `NotificationSendRequest.Title`'s doc comment states
this explicitly rather than naming a code the SDK does not emit (found stale, and fixed, at
the same handback that closed this decision).

### D-M10-10 — R-29 executed: `Auth.Userpass.Admin.ListUsersInfo`, the rename D-M10-3 already decided

**Decision.** Slice d built the rest of `Auth.Userpass.Admin.*`
(`appendix-a-endpoint-catalogue.md:86-93`) and, per D-M10-3 (already decided, not
reopened here), relocated `UserpassOperations.ListUsersInfoAsync`/`ListUsersInfoAllAsync`/
`UserSummary` onto the new `UserpassOperations.Admin` sub-client, verbatim — verified
byte-identical (including the relocated doc-comment block) at the R2 handback review. Every
other member of `Auth.Userpass.Admin.*` follows `AppIdAdminOperations`/
`FerrogateAdminOperations`'s raw-`JsonElement`/`Response` idiom for a catalogue-only surface
with no field-level schema (D-M6-5); `SetPassword`'s one field is typed `SecretString`
directly, its wire name (`password`) confirmed against `Internal/LoginRunner.cs`'s existing
use for the same account, not guessed.

**Consequence.** **R-29 is closed.** The rename is breaking on an **unpublished** API only —
nothing in this repository publishes to a package registry, so no released consumer is
affected — but it is exactly the change the Rust/Python parity pass must carry *before*
Stage 2's first publication, or the unpublished-API limb this closure rests on no longer
holds by the time that pass runs. Carried forward as a note for that pass, not a new risk
row: the closure is real today, and stays real only as long as Stage 1 stays unpublished.

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
