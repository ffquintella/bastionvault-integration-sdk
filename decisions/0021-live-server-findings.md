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
