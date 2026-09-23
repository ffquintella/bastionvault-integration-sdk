# DR-0021 — What the first live server found: seven divergences between the specification, the SDK, and reality

**Status:** proposed (findings and routing), revision 1 (2026-09-22). Authored by the
Strategic Orchestrator on evidence returned by M12 slice 3 and independently reproduced.

**Risk tier:** **R3** (`agents.md` §5.3, `CRS-004`). Four of the seven findings are
**specification behaviour** questions, which is the limb `CRS-004` names as genuinely R3 —
not the vacuous "published artefact" limb. Work pauses for the Strategic Orchestrator on
detection (`CRS-005`) and the specification changes need Architect review plus the project
owner's confirmation before any of them lands.

**Date:** 2026-09-22 · **Found by:** M12 slice 3, running `ITG-S01`…`ITG-S12` against
managed `bvault` 0.44.5

**Amends nothing yet. This record is the catalogue and the routing; each fix is its own
change.**

## Problem

Eleven milestones of this SDK were built against `specifications/` and an in-process mock
server. **R-6 — "no live BastionVault server available" — has been open at R3 since M0**,
and `dotnet/README.md` has carried the qualifier "tested server versions: 0.42.x, **not
live-verified**" throughout. The mock server answers what the fixtures say, and the fixtures
were authored from the specification. That loop is closed: it can prove the SDK matches the
specification, and it cannot prove the specification matches the server.

M12 slice 3 opened the loop. **Eleven of twelve scenarios pass. The twelfth, and six
incidental findings, are the first evidence this project has ever had about what the real
server actually does** — and in four cases the specification is the side that is wrong.

Reproduced by the Strategic Orchestrator, not accepted on report:

```
Com falha!  – Com falha: 1, Aprovado: 41, Ignorado: 5, Total: 47
EXIT: 1
```

## The findings

| # | Finding | Who is wrong | Tier | Routing |
|---|---------|--------------|------|---------|
| **F1** | `POST auth/token/renew/{token}` returns **`204 No Content`** for a token obtained from `auth/approle/login`, but `200` with a full `auth` envelope for one from `auth/token/create`. `RenewSelfAsync`'s `RequireAuth` throws on the null envelope, surfacing as `BV-PROTOCOL-002`. **Auto-renew is therefore unusable on any `Login`-sourced credential** | SDK, and possibly the spec | **R2** | SDK fix, own slice |
| **F2** | `Auth.Token.Create` with a `Ttl` fails server-side with a `serde` error: the server wants `ttl` as a **string**, the SDK sends a **JSON number** | **Specification** — `05-authentication.md:182` says "`ttl` (seconds)" and the SDK obeys it | **R3** | Owner + Architect |
| **F3** | `Sys.Unmount` of an already-gone path returns **`500`** ("Mount not match"), not `404` | Specification / error model | **R3** | Owner + Architect |
| **F4** | `Sys.PolicyHistory`'s first entry reports `Op: "create"`; `15-testing-requirements.md:192` requires a **`write`** entry | Specification | **R3** | Owner + Architect |
| **F5** | Userpass user policies are honoured only via **`token_policies`**, not `policies` | Specification | **R3** | Owner + Architect |
| **F6** | `CapabilitiesSelf`'s `namespace_operable` is `false` only when the active namespace is a **sibling**; a root token always reads `true`, so the spec's root-vs-namespace framing cannot exercise it | Specification | **R2** | Owner + Architect |
| **F7** | `ITG-S01` requires **`Client.ServerVersion()`**. It does not exist: **zero occurrences** in `PublicApiSurface.txt`, and the name appears only inside two hint strings | Specification requires an API nobody built | **R2** | Architect |

### F2 is the one that should worry us most, and its blast radius is not yet known

`Auth.Token.Create` is where it was *observed*. But the numeric-duration convention is not
local to it: `WriteSeconds(writer, name, TimeSpan?)` → `writer.WriteNumber(...)` is
implemented **six times** — `TokenOperations.cs`, `PkiOperations.cs`/`PkiWire.cs`,
`LdapOperations.cs`/`LdapWire.cs`, `CertLifecycleWire.cs` — across **25 call sites**.

**Measured:** one endpoint rejects a numeric `ttl`.
**Unknown, and stated as unknown:** whether the other 24 call sites are also rejected. Some
endpoints may accept both forms. **Nobody has tested them, which is the whole point of this
record.** Slices 4–6 will establish it as they exercise PKI, LDAP and cert lifecycle; until
then the SDK's ability to set a duration against a real server is **unverified**, not
broken, and must not be described as either.

This is precisely the exposure R-6 has carried since M0. It is not a surprise that it
existed; it is a measurement of what it cost.

## Decisions

### D-0021-1 — The scenarios stay red where reality is red

`ITG-S11` is committed **failing**. It was not weakened, skipped, or marked inconclusive.
`CLA-004` forbids weakening a test to make it pass and `VER-003` forbids describing a
failing thing as working; a scenario that encodes the specification faithfully and fails
against the server **is the finding**, and deleting it would delete the evidence.

The integration suite therefore exits non-zero until F1 is resolved. That is correct and it
is not a reason to hurry a fix.

### D-0021-2 — Four specification changes go to the project owner, together, not piecemeal

F2, F3, F4 and F5 are `specifications/` amendments. Under `CRS-004` and `agents.md` §5.4
each needs Architect review, Claude Opus 5 review, and the owner's confirmation. They are
escalated **as one batch**, because they share a single question the owner should answer
once: *when the specification and the real server disagree, which one moves?*

Three answers are possible and they are not equivalent:

1. **The server is authoritative** — the specification was written ahead of the
   implementation and reality wins. Amend the four, regenerate fixtures, and treat every
   remaining unverified claim as suspect until M12 finishes.
2. **The specification is authoritative** — these are server defects to be filed against
   BastionVault, and the SDK keeps implementing the specification while the scenarios stay
   red.
3. **Case by case** — the honest answer for a mixed set, and the most work.

**The Strategic Orchestrator's recommendation is (1) for F2 and F4, (3) for F3 and F5.**
F2 and F4 are plainly descriptive errors — the specification guessed a wire detail. F3 is
arguably a server bug (a `500` for an idempotent delete of an absent mount is poor
behaviour, and the error model has opinions about it); F5 may be either, depending on
whether `policies` is meant to be an accepted alias.

### D-0021-3 — F1 is an SDK fix and is routed now, not batched

Unlike F2–F5, F1 needs no specification change to proceed: whatever the server *ought* to
return, an SDK whose auto-renew loop dies on a `204` is fragile. The fix is to tolerate a
content-free renewal response rather than throwing, and it lands as its own slice with its
own test — with the `ITG-S11` scenario as the acceptance criterion, which is the best
possible position to be in: **the test exists and fails before the fix.**

Parity is owed at M13 (`CLA-003`); Rust and Python must implement the tolerant behaviour,
not copy .NET's former one.

### D-0021-4 — F7 is an Architect question, not a slice's

`Client.ServerVersion()` is required by a specification scenario and does not exist. Two
resolutions: add the member (public API shape — Architect, `FAM-002`, parity at M13), or
amend `ITG-S01` to describe what the SDK actually offers. **Slice 3 correctly refused to
invent it.** Until it is resolved, `ITG-S01` passes on its other limbs with the caching
sub-assertion unexercised, and that gap is recorded here rather than hidden in a comment.

### D-0021-5 — The incidental findings are recorded, not absorbed into test code

Slice 3 also found that `RevokeSelf` clears the local token so the next call fails
client-side `BV-AUTH-001` rather than server-side `BV-AUTHZ-001`, and that
`Sys.ReadPolicy` maps `BV-NOTFOUND-005` to a `null` result by design. Both are correct SDK
behaviour that the scenario prose did not anticipate. They are listed here so the next
reader does not rediscover them from a test assertion, per **TOK-008**.

## Consequences for the project record

- **R-6 is not closed by having a server; it is closed by finishing M12.** The risk's real
  content was never "we lack a binary" — it was "nothing has ever checked our assumptions."
  Seven findings in the first twelve scenarios is the measurement of that. `ROADMAP.md` §8
  should carry R-6 as *materialised*, with this record as its evidence.
- **A new risk is owed:** the SDK's duration encoding is unverified at 24 of 25 call sites
  (F2). It should be a numbered row in `ROADMAP.md` §8 with M12 slices 4–6 as its owner.
- **`dotnet/README.md`'s "not live-verified" qualifier is now partly dischargeable** — and
  must be updated carefully, since "live-verified" is exactly the sort of claim this project
  has learned to make narrowly. It is true for sections 05, 06 and part of 07, at 0.44.5,
  and false everywhere else.

## Addendum, 2026-09-23 — F1 closed, F8 and F9 opened by slice 4

### F1 is fixed and `ITG-S11` is green

The auto-renew loop now treats a content-free `204` as a successful renewal. Verified by the
Strategic Orchestrator on a live run: `Scenario11_AutoRenew` has **zero** `FAIL` matches where
it previously produced five `BV-PROTOCOL-002` events. **The suite went 41→47 passing.** One
residual is recorded in the fix's commit rather than hidden: after a `204` the next deadline
is `now + previous lease × RenewAtFraction`, and `LastLogin` is not updated by renewals, so a
long chain of renewals still reflects the original login's lease length. Widening that is an
owner call, not a defect fix.

### F8 — Transit key metadata is unusable against 0.44.5, and half of it is an SDK defect

`bvault` 0.44.5 returns `creation_time` as a **Unix-epoch number**, in both shapes:

```
POST transit/keys/testkey {"key_type":"chacha20-poly1305"}
→ "data":{"keys":{"1":1790163936}, ...}
POST transit/keys/edkey  {"key_type":"ed25519"}
→ "data":{"keys":{"1":{"creation_time":1790163936,"public_key":"..."}}, ...}
```

`TransitWire.ReadKeyVersions` (`Internal/TransitWire.cs:188-206`) requires a **string** and
parses it as ISO-8601. This blocks `CreateKey`, `ReadKey`, `RotateKey`, `ConfigureKey` and
`TrimKey` — the entire key-metadata surface. Reproduced with `curl` against a throwaway
server, bypassing the SDK, so it is a wire fact rather than a scenario bug.

**The finding splits in two, and the split is the decision.**

- **F8a — an SDK defect, fixable now, independent of any specification question.** The
  asymmetric branch calls `created.GetString()` on a `Number` and throws a raw
  `System.InvalidOperationException` out of `System.Text.Json`, **escaping the SDK's error
  model entirely** — no `BV-*` code, no hint, no `Retryable`. That is the same class of
  defect as [DR-0020](0020-default-transport-conformance-gap.md)'s transport
  `InvalidOperationException`, and it is wrong whatever the correct wire encoding turns out
  to be. The `_ => throw KvWire.EnvelopeMismatch(...)` fallback already exists for unexpected
  kinds; the `Object` branch simply slips past it.
- **F8b — what the encoding *should* be** is a specification question and joins F2–F5's batch
  for the owner.

**Ruling: the SDK accepts both encodings now (F8a), and the specification wording waits
(F8b).** Tolerant parsing — accept a JSON number as a Unix epoch *and* a string as ISO-8601 —
is **strictly widening**: it cannot break a server that sends strings, so it pre-empts none of
the owner's choices on F8b, and it unblocks Transit against the only server that exists. This
is deliberately *not* the "server is authoritative" ruling; it is the narrower one that
happens to be safe under either answer. `ITG-S18`/`ITG-S19` are the acceptance criteria and
stay red until it lands.

### F9 — the suite now trips the server's own abuse guard, and it will get worse

With 20 scenarios running, a pre-existing scenario fails intermittently with *"The server's
abuse guard temporarily blocked this client IP"* — and it struck a **different** scenario on
each run (`ITG-S07`, then `ITG-S02`). That is a volume artifact, not a defect in any scenario:
`test-matrix.json`'s `dosConfigDefaults` are `window_secs: 10, max_requests: 200`, shared
across one managed-server IP by the whole suite.

**This is a harness problem and it compounds.** Slices 5 and 6 add twelve more scenarios, so
the failure rate rises with every slice and lands on innocent tests. Left alone it produces
exactly the outcome this project keeps guarding against: **a suite that fails for reasons
unrelated to what it is testing, which trains its readers to ignore red.**

It is booked to the harness rather than to a scenario, and it is **not** to be fixed by
raising `max_requests` in `test-matrix.json` — that file describes the server a conforming
SDK must cope with, and loosening it to make our own suite pass is `CLA-004`. The legitimate
options are scenario scheduling, per-test pacing, or a documented serial section.

## Second addendum, 2026-09-23 — F8 generalises to F10, and a review finding on slice 5

### F10 — the date-encoding divergence is systemic, not a Transit quirk

Slice 5 found `bvault` 0.44.5 returning **Unix-epoch numbers** for PKI's `expiration`,
`issued_at` and `not_after`, where the SDK expects RFC 3339 strings — the same shape as F8,
in a different engine. Tracing it: PKI does not parse dates itself. It routes every one
through **`KvWire.RequireInstant` / `KvWire.ReadOptionalInstant`**
(`Internal/PkiWire.cs:260,261,279,281,444`), which are **shared helpers**.

**So F8 was the first instance of a systemic assumption, not a Transit defect.** The SDK
assumes throughout that a timestamp arrives as an ISO-8601 string; this server sends epochs.
Fixing Transit alone (as F8a did, in `TransitWire.ParseInstant`) treated a symptom and left
the same bug live in every other engine that reads a date.

**Ruling: fix it centrally, in `KvWire`, under F8a's existing rationale.** `RequireInstant`
and `ReadOptionalInstant` accept a JSON **number** as a Unix epoch alongside the ISO-8601
string, and anything else raises `BV-PROTOCOL-002` rather than escaping as a raw exception.
This is the same *widening* change already ruled safe for F8a: it cannot break a
string-sending server, so it still settles nothing about F8b, which remains the owner's.
`TransitWire.ParseInstant` should delegate to the shared helper rather than keep its own
copy, so the next engine to meet an epoch inherits the fix instead of rediscovering it.

**A related but distinct finding, not fixed by the above:** `{mount}/crl` omits
`crl_number` entirely, which `PkiWire.cs:299` treats as a protocol violation because
`Crl.CrlNumber` is `required`. A missing field is not a mis-encoded one; whether the field
is optional on the wire is an **F8b-class specification question** and joins the owner's
batch rather than being defaulted to zero unilaterally.

### Review finding — slice 5's scenarios assert the defect instead of the requirement

Slice 5 reported all six scenarios passing. They pass because several assert the **bug** as
the expected outcome:

```csharp
BastionVaultException rootParseDefect = await Assert.ThrowsAsync<BastionVaultException>(
    () => Client.Pki.GenerateRootAsync(...));
Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, rootParseDefect.Code);
```

`ITG-S21` requires "generate internal root, create role, issue cert → **parses**, chain
verifies". This asserts that generating a root *throws*. **The scenario is green while its
requirement is unmet**, and the test will **fail on the day the defect is fixed** — a test
that breaks when the code gets better is worse than a red one.

This is not `CLA-004`'s letter (nothing was weakened) but it is its purpose, inverted:
slices 3 and 4 committed red scenarios and those reds became this milestone's most valuable
output. **Encoding a defect as an expectation is how a suite stops being able to tell you
anything.**

**Required:** every such assertion is rewritten to assert the specification's outcome. They
will be red until F10's fix lands, which is correct and is the precedent D-0021-1 already
set. The F2 measurements slice 5 gathered are kept — they are real data and the most
complete F2 evidence so far.

### F2, narrowed considerably by slice 5

| Field | Numeric duration |
|---|---|
| `PkiRootSpec.Ttl`, `PkiRole.Ttl`/`MaxTtl`, `IssueRequest.Ttl`, `SignRequest.Ttl`, `SignIntermediateRequest.Ttl` | **rejected** |
| `SshRole.Ttl`/`MaxTtl` | **accepted** |
| `Totp` `period` (slice 4) | **accepted** |
| KV v1 `ttl` (slice 4) | already a duration string; round-trips |

The server is **not uniform**, which kills the tempting one-line fix. `pki/*` rejects every
numeric duration; `ssh/*` and `totp/*` accept theirs. R-37 stays open and the owner's F2
decision now has real data under it rather than one measurement.

### F9 is worse than first measured and now blocks the suite

Slice 5 observed the abuse guard firing **10–11 times per 61-test run**, cascading into
unrelated scenarios' setup calls. Its own six pass cleanly in isolation. **The full suite can
no longer be run as a suite**, which means `ITG-030`'s CI matrix job would be red for reasons
unrelated to conformance. F9 is therefore promoted from a nuisance to a **blocker for slice
7**, and must be fixed by scheduling or pacing — never by loosening `test-matrix.json`
(`CLA-004`).

## Project-owner rulings, 2026-09-23

All four questions this record escalated were answered. Recorded so no slice re-opens them
(**TOK-008**, **CLA-008**).

**Ruling 1 — the server is authoritative; amend all.** F2, F3, F4, F5 and F8b become
`specifications/` amendments, plus `crl_number`'s optionality. This is broader than the
Strategic Orchestrator's recommendation, which was to amend F2/F4/F8b and **file F3 as a
server defect** — a `500` for an idempotent delete of an absent mount is poor behaviour the
error model has opinions about. The concern was put to the owner and the owner chose "amend
all" with it in view; that is the decision and the work proceeds on it (`agents.md` §5.4,
human gate satisfied). F3's amendment should still *record* that the behaviour is
surprising, so a future server fix is not mistaken for a regression.

**The constraint the Strategic Orchestrator attaches, which is not a re-litigation.**
"Amend all" means all the findings, **not all 25 duration call sites**. Eleven have been
driven against a real server; fourteen have not. Amending an endpoint nobody has exercised
would replace a specification guess with a different guess, which is the exact failure mode
this record exists to document. **R-37 stays open for the fourteen**, and the amendment
brief forbids touching them.

**Ruling 2 — amend `ITG-S01`, do not add `Client.ServerVersion()`.** The scenario is
rewritten to describe what the SDK offers. No public API is added to satisfy a test, and no
parity debt is created for M13.

**Ruling 3 — the Docker daemon is started.** Confirmed running (29.7.2). **It does not
unblock slice 7**: `ghcr.io/ffquintella/bastionvault` still returns `DENIED` for `0.42.0`
and `latest`, an anonymous token request is refused, and no ghcr credential exists in the
local Docker config — the package is private. What remains is one `docker login ghcr.io`
with a PAT carrying `read:packages`. Slice 7 stays held on that alone; the container path
is otherwise ready and has still never been executed.

**Ruling 4 — declare the first legal conformance level as soon as the audit permits.** This
ends the pattern R-14 has tracked since M4, in which four milestones in a row declared
nothing. It makes **D-M12-4's `CNF-001`-vs-`CNF-014` audit a gating deliverable** rather
than a nice-to-have: the audit is now the only thing standing between M12 and the project's
first conformance claim, and its quality decides whether that claim is honest. It must
separate "no test references this ID" from "this MUST is unimplemented" for all 33 baselined
IDs inside `Core`'s sections, and `PKI-030` (**R-31**) still bars `Complete` regardless.

## Third addendum, 2026-09-23 — the amendment landed, and what drafting it exposed

### The one inferred claim is now measured

The draft amended `auth/token/create` and every `pki/*` duration to a Go-style string on the
strength of a *negative* measurement — the number is rejected — and flagged, correctly, that
the positive form was an inference from KV v1's round-trip. **An amendment resting on an
inference is the defect this record exists to document**, so the Strategic Orchestrator stood
up a server and measured it directly:

| Request | Result |
|---|---|
| `POST auth/token/create {"ttl":3600}` | `invalid type: integer 3600, expected a string` |
| `POST auth/token/create {"ttl":"1h","policies":["default"]}` | **succeeds**, `lease_duration: 3600` |
| `POST pkitest/root/generate/internal {"ttl":"8760h"}` | **succeeds**, returns a certificate |
| `POST pkitest/root/generate/internal {"ttl":31536000}` | `Request field is invalid.` |

Both limbs now measured, in both engines. The hedged wording is replaced in
`05-authentication.md` and `09-pki-engine.md`. **One server, five requests, four minutes** —
the cost of turning the project's first specification amendment from a well-reasoned guess
into a fact.

### F11 — a requirement ID cannot be added in a specification-only change

Four amendments wanted a new normative rule. The drafter found no legal way to mint an ID:
`traceability.py` draws its applicable set from `appendix-d-requirement-index.md`, so a new
ID **omitted** from Appendix D leaves that document falsely claiming to be generated from the
spec, and a new ID **added** to it fails the ratchet, because nothing covers it. Baselining a
brand-new requirement to make the gate green would be `CLA-004`'s purpose if not its letter.

The drafter chose unnumbered normative prose — for which the specification has precedent
(`AUT-070`, the token-store blocks) — and named the six proposed IDs inline: `PKI-003`,
`PKI-012`, `PKI-021`, `SYS-027`, `SYS-044`, `TRN-044`.

**Ruling: that is correct, and the constraint is a feature that was never written down.**
The repository enforces *a requirement arrives with its test*. That is good discipline and it
explains the deadlock rather than excusing it. The six IDs land in a follow-up that adds
them to Appendix D **and** the tests claiming them, in one change. Until then the rules are
normative but untraceable, which is recorded here rather than left for someone to discover.

### F12 — `FIX-010` is unmet corpus-wide, and it is the mechanical cause of everything above

Of **254 fixtures, zero were captured from a real server exchange.** 130 are generated from
Appendix B; **123 are hand-derived**, labelled `"BastionVault 0.42.x (derived from crates/…
behaviour)"`. `FIX-010` requires fixture bodies copied from a real exchange.

**This is the single-sentence explanation for this entire record.** Eleven milestones were
verified against a corpus authored from the same document the corpus was meant to check. The
mock server answered what the fixtures said, the fixtures said what the specification said,
and so the loop could only ever confirm the SDK matched the document. Every one of the twelve
findings here was invisible to it *by construction*.

It is booked as a **new risk row**, not fixed here: re-capturing 123 fixtures against a live
server is a milestone, not a slice, and `R-35`/`PKI-030` already show that a capture nobody
has is not a capture you may invent.

### `TRN-081` — amending `ITG-S01` moved the gap rather than closing it

The owner ruled "amend `ITG-S01`, do not add `Client.ServerVersion()`". The drafter found
that **`TRN-081` independently requires the same unbuilt member**, and correctly did not
amend it, since the ruling named the scenario.

**Ruling: amend `TRN-081` the same way, consistently.** The owner's decision answered the
underlying question — *do not add public API to satisfy a document* — and `TRN-081` is that
question wearing a different number. Amending one and leaving the other would leave the SDK
non-conformant against a requirement nobody intends to implement. This is applying the
owner's principle, not extending their mandate, and it is flagged to them as such.


## Fourth addendum, 2026-09-23 — M12's remaining reds, measured to their causes

The milestone record said the five red scenarios would "go green on their own when F10's
`KvWire` fix lands and R-37 is decided". That turned out to be three separate claims, two of
them wrong, and the only way to tell was to stop reasoning about the reds and measure them.
What follows is the result: **the five reds have four distinct causes and only one of them
was the one the record named.**

### The specification amendment landed; the implementation never followed it

F2's `pki/*` duration amendment and `crl_number`'s optionality are both **in
`specifications/`** — the first at `09-pki-engine.md:48-68`, the second at `:105-118`, the
latter since release `0.21.0`. Neither reached the SDK. `PkiWire` still writes every `pki/*`
duration through `WriteSeconds`, and `ReadCrl` still throws on an absent `crl_number`.

**This is a new failure shape and it deserves its own name.** R-10 tracks gates whose record
is trusted instead of their execution; this is the same disease in the other document. An
amendment is a *claim about the SDK's behaviour*, and landing it without the implementation
makes the specification the thing that is now wrong — the repository went from "the SDK
disagrees with the server" to "the SDK disagrees with its own specification", which is
strictly worse, because `CNF-001` is measured against the latter. Four scenario failures
(`ITG-S21`'s three duration assertions, `ITG-S22`'s one) and one more (`ITG-S21`'s
`crl_number`) were this, not R-37.

**R-37 is not what was blocking them.** R-37 covers the **fourteen unmeasured** duration call
sites, which the amendment deliberately did not touch. Every assertion these scenarios make
is against one of the **eleven measured** ones. R-37 stays exactly as open as it was and is
not on M12's path.

### F13 — a `certs-info` row is not a `cert` read, and `ListCertificatesInfo` was mis-tagged F10

`ITG-S21`'s `ListCertificatesInfoAsync should parse and page results` carried the comment
"(F10)" and the milestone record repeated it. F10's `KvWire` fix **had** landed, and the
scenario still failed, so the tag was wrong — every date in `ReadCertificateSummary` already
routed through the widened helpers.

Measured directly, `bvault` 0.44.5, 2026-09-23. A `GET {mount}/certs-info` row is exactly:

```json
{"common_name":"leaf.probe.test","issued_at":1790193478,"issuer_dn":"CN=probe-root",
 "issuer_id":"3c3e…","not_after":1790195278,"serial_number":"2240f7a99cdf0f22"}
```

`source`, `is_orphaned` and `key_id` are **absent**. `PkiWire.ReadCertificateSummary` threw
`BV-PROTOCOL-002` on `source` and silently defaulted `is_orphaned` to `false`.

**Ruling: widen the types, under the owner's Ruling 1, exactly as `crl_number` was.** This is
an absent field, not a mis-encoded one, so the tolerant-timestamp rule does not reach it.
`Source` and `IsOrphaned` become optional in `09-pki-engine.md` and in the SDK, and neither
is given a substitute value — `IsOrphaned` defaulting to `false` was the SDK asserting "this
certificate has an issuer on record" on the strength of a route that never said so. The
amendment also removes an inconsistency *inside* section 09: `CertificateRecord` already
marks the same five fields optional for the single-certificate read.

**Flagged to the project owner as an R3 specification change**, applying Ruling 1's principle
to an instance measured after it was given, in the same way the third addendum applied it to
`TRN-081`. It lands with M12's other R3 material at the human gate, not around it.

### F14 — the array envelope the SDK never learned to read

`ITG-S24` and `ITG-S25` reported empty lists from `Files.Versions` and
`Resources.Secrets.History`. `IdentityKernelWire.ReadArrayEnvelope` unwraps a bare top-level
array, or `data` when `data` is itself an array. The server returns neither: it nests the
array under a **named key inside `data`**.

The specification makes **no envelope claim at all** for these routes — section 12 names only
the HTTP route — so unlike F2, F13 and `crl_number` there is nothing here for the document to
have gotten wrong. **This one is purely an SDK defect**, written narrower than the shapes it
had to cover, on an inference that went unmeasured for eleven milestones (D-M1c-25, F12).

**Design ruling: an explicit key, not a heuristic.** The helper takes an optional
`nestedKey`; resolution is bare array, then `data` as array, then `data.<nestedKey>`. The
tempting alternative — unwrap whichever single property under `data` is an array — is
rejected because it is not single: the `certs-info` payload measured above carries **two**
arrays under `data` (`records` and `keys`), so the heuristic is one server-side field away
from silently handing a caller the wrong list. R-10's lesson generalised: a corpus boundary,
or an envelope, should be a reviewable decision rather than a by-product of a heuristic.

### `ITG-S11` is a load artefact, not a regression, and the evidence is the external run

`Scenario11_AutoRenew` failed the managed full-suite run at its **final** assertion — the
renewal fired, and the follow-up lookup a second later found the remaining TTL already
non-positive. It is not in the five reds M12 recorded, and it **passed in the external-mode
run of the same commit**, one minute apart.

That is F9's signature: `AbuseGuardPacer` exempts `auth/token/renew/*` precisely so an
`AutoRenewPolicy` loop can meet its real-time deadline, but the exemption cannot protect the
call from the *server's* guard or from 32 scenarios' worth of concurrent login traffic. The
residual it exposes is already recorded in the F1 addendum: after a content-free `204` the
SDK schedules the next deadline from the previous lease and fires `OnRenewed`
unconditionally, without confirming the server extended anything.

**Not fixed here, and not dismissed.** Recorded as a **known load-sensitive scenario** with
the external-mode pass as its control. Making `OnRenewed` conditional on an observed lease
extension is the F1 residual, which the addendum already routed to the owner, and it stays
there.

### `ITG-S26` is the one red with no SDK limb at all

`ITG-S26`'s five unmet requirements are two server-side gaps: a group's policy is not
resolved into a member's token on the member's next login, and none of the three sharing
list routes (`ListByGrantee`, `ListByTarget`, `Sharing.ForMe`) index a group-target share.
Nothing in the SDK's request or its parsing is implicated — the calls succeed and return
what the server has, which is nothing.

**This is the first finding in this record that is neither an SDK defect nor a specification
error.** It is a server gap, it is outside the SDK's control, and the scenario stays red
because red is what it is (D-0021-1). It needs a server-side issue, not a slice, and it is
the reason M12's acceptance criterion 2 can be *honestly* reported as unmet rather than
engineered to green.
