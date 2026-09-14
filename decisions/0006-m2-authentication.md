# DR-0006 — M2: Authentication, Core methods

**Status:** **accepted** at revision 5 — approved by Strategic Claude Opus 5 architecture
review (`agents.md` §4.2 row 4) after four rounds. Three rounds blocked, and each block was
a seam pinned on one side only:

| Round | Blocked on | Ruled in |
|-------|-----------|----------|
| 1 | Executor → `TokenSource` unpinned; AUT-011 never reaches recognition from a 200 | D-M2-9, D-M2-4a |
| 2 | `CFG-020` dropped from the ID allocation; replay's `requestId`/`attempts` accounting unstated | D-M2-1, D-M2-9 |
| 3 | `Resolve()` concurrency unpinned, voiding `CFG-070`'s thread-safety basis | D-M2-11 |
| 4 | *cleared* — three record-accuracy fixes, applied at revision 5 | D-M2-11(b) |

**Closed and not reopened by a delegate:** D-M2-1's allocation and arithmetic, D-M2-2,
D-M2-3, D-M2-4a (on `ERR-006` grounds), D-M2-5's all-to-M6 ruling, D-M2-7's clock
instrument, D-M2-9's replay placement and its implementability, D-M2-10's
operation-not-requirement rule.
**Risk tier:** R3 (`CRS-003`: auth, tokens and secret material; `CRS-006`: unknown blast radius scored high)
**Milestone:** M2 · **Date:** 2026-09-14
**Supersedes nothing. Inherits:** [DR-0003](0003-m1a-configuration.md) (configuration),
[DR-0004](0004-m1b-transport.md) (transport seam, logical layer, retry),
[DR-0005](0005-m1c-error-model.md) (error model, recognition, enrichment, and D-M1c-25).

## Problem

M2 must give the three SDKs the token source model, the Core login methods, the token-store
operations, automatic renewal and the section-05 security posture. Section 05 is the first
milestone that creates a **sub-API grouping** (`Client.Auth`, OVR-008); until now the only
public operation surface has been `Client.Logical`. It is also the first milestone whose
subject matter *is* secret material, which is why it is R3 and why CNF-031/032 and TST-051
are exit criteria rather than later hygiene.

Three facts from the grounding survey shape every decision below:

1. **There is no auth-shaped code to extend.** No `Auth` property, no token source, no
   renewal loop exists in any of the three SDKs. The only artefact is the disabled
   `AutoRenew` config value carried from M1a. M2 is a new subsystem, not an increment.
2. **The fixture harness cannot express two things M2's fixtures require.** No driver in
   any language reads `fixture.clock` (`clock.start` / `clock.advance`), although the
   schema has defined it since M0 and `auth.token.lookup-self-remaining-ttl` already uses
   it. No language has a capturing logger, so **TST-051 has never been asserted anywhere**.
3. **Rust's injected clock cannot tell the time.** `Clock::now()` returns
   `std::time::Instant` — monotonic, with no wall-clock projection. AUT-014's
   `RemainingTtl = creation_time + creation_ttl − now` is unix-epoch arithmetic, so on
   Rust it is not merely untested, it is uncomputable.

## Decisions

### D-M2-1 — M2 is decomposed into M2a, M2b, M2c

**Forces.** M2 is booked as Large / ~28 requirements. Its true content is 39 requirement
IDs, a new public subsystem in three languages, two net-new harness capabilities, and the
R-10 gate re-proof sweep that `ROADMAP.md` §8 makes an M2 exit condition. That does not fit
a Large brief, and **TOK-011** says a task that cannot fit its tier budget is decomposed,
never granted a larger budget.

**Decision.** M2 lands in three sub-slices, serial, each exiting independently exactly as
M1a/M1b/M1c did.

| Slice | Content | Requirement IDs | Count |
|-------|---------|-----------------|-------|
| **M2a** | Token source model and the **resolution seam** (D-M2-9), `Client.Auth` grouping, Token method, the nine token-store operations, the token-helper write path, **plus the two harness instruments** (fixture `clock` support, capturing logger) | `AUT-001`, `AUT-004`, `AUT-014`, `AUT-020`, `AUT-080`…`AUT-085`, `CFG-031`, `CFG-032`, `CFG-070` (D-M2-11c), `TST-051` | 14 |
| **M2b** | Login response contract, Userpass, AppID, the lazy-login and missing-token rules, and the section-05 security requirements | `AUT-002`, `AUT-003`, `AUT-010`…`AUT-013`, `AUT-030`…`AUT-032`, `AUT-040`…`AUT-042`, `AUT-044`, `AUT-100`, `AUT-101`, `CFG-020`, `CNF-031`, `CNF-032`, `ERR-022` | 19 |
| **M2c** | Automatic renewal | `AUT-090`…`AUT-095` | 6 |

**M2c carries one exit criterion that is not a requirement ID:** the **R-10 gate re-proof
sweep** (`ROADMAP.md` §8 — every gate re-proven by seeded violation and revert, evidence
recorded once in one place). It is a proof obligation, not a behaviour, so it is
deliberately **not** given a requirement ID — minting one would put a non-specification
entry on the baseline. **M2 does not exit until the sweep is done and recorded.**

**`ERR-022` moved from M2a to M2b at revision 2** (review finding B1). Revision 1 put the
client-side missing-token preflight in M2a and the lazy login in M2b, which contradicted
itself: a `Login` source legitimately holds no token until first use, so M2a's `ERR-022`
preflight would have raised `BV-AUTH-001` on the first authenticated request and the lazy
login would never have run — forcing M2b to reopen M2a's preflight in three languages.
`ERR-022` now lands in the same slice as `AUT-002`, where the unresolved-source state it
must distinguish first becomes observable.

**Total removal at M2 exit: 39** (M2a 14 with `CFG-070` re-opened per D-M2-11(c),
M2b 19, M2c 6). Baseline 305 → re-opened to **306** → **267** at exit.

**`CFG-020` was missing from revision 2 and is added at revision 3** (review finding F1).
Two accepted records book it to M2 by name — [DR-0003](0003-m1a-configuration.md):206
(`CFG-020 | M2 | BV-AUTH-001 needs an authenticated operation to refuse`, with :188-190
holding it in the baseline *until M2* precisely so partial coverage could not be mistaken
for coverage) and [DR-0004](0004-m1b-transport.md):370. Revision 2 claimed `CFG-031` and
`CFG-032` out of that carried-forward trio and silently dropped the third.

It is not bookkeeping. `CFG-020`'s second MUST is that an authenticated operation attempted
with no token fails client-side with `BV-AUTH-001` *before any network call*
(`02-client-configuration.md:82-86`) — which is **exactly** what D-M2-9's resolution-order
ruling decides. Left out, M2 would have implemented `CFG-020`'s behaviour in M2b and exited
without removing it from the baseline: the `CFG-001` false-positive-on-the-ratchet shape
this record cites twice as the thing not to repeat. It lands in **M2b**, the same slice as
`ERR-022` and `AUT-002`, because all three turn on the same resolution-order rule.

**Why this order.** M2a is the foundation every later slice needs: the token cell, the
source model, and the two instruments without which M2b's and M2c's fixtures cannot be
written at all. M2c is last because AUT-093 (relogin after renewal stops) requires a
`Login` source, which M2b creates.

**Rejected:** *one Large brief.* Rejected on M1's evidence — M1 was booked as one milestone
and its three sub-slices each found blocking defects the others did not, including two that
sat below the fixture seam. A 39-ID auth brief reviewed as one unit is the R-7 risk this
project already retired once by splitting.

**Rejected:** *harness instruments as a separate M2z slice.* Rejected because an instrument
with no fixture to drive it is an instrument nobody has proven, which is R-10's exact
shape. The clock support ships in the slice whose fixtures need it.

**Consequence.** Three briefs and three handback gates instead of one. Each slice is Large
and fits the 15 k / 4 k budget.

### D-M2-2 — The injected clock states which time it means, in all three languages

**Forces.** AUT-014 needs wall-clock unix time. AUT-090/AUT-092 need both: the renewal
schedule is wall-clock (`IssuedAt + LeaseDuration × RenewAtFraction`), the backoff delay is
a duration. Today .NET's `IClock.Now()` and Python's `Clock.now()` return wall-clock
(`DateTimeOffset`, `datetime`) while Rust's `Clock::now()` returns a monotonic `Instant`.
**One member name means two different things depending on the language**, which is exactly
the R-9 defect class — a differing public contract behind an identical name, invisible to
fixtures, coverage and traceability alike.

**Decision.** The concept is named in the member, everywhere:

| Canonical | .NET | Rust | Python |
|-----------|------|------|--------|
| `Clock.NowUtc` | `IClock.NowUtc() -> DateTimeOffset` | `Clock::now_utc() -> SystemTime` | `Clock.now_utc() -> datetime` (tz-aware UTC) |
| `Clock.NowMonotonic` | — | `Clock::now_monotonic() -> Instant` | — |
| `Clock.Delay` | `IClock.Delay(TimeSpan, CancellationToken)` | `Clock::delay(Duration)` | `Clock.delay(timedelta)` |

`IClock.Now()` → `NowUtc()`, `Clock.now()` → `now_utc()`, and Rust's monotonic `now()` →
`now_monotonic()`. Rust gains `now_utc()` as a new trait method. Existing backoff call
sites move to `now_monotonic()` on Rust and stay on `NowUtc()`/`now_utc()` elsewhere, which
is what they already did.

**No new dependency.** `SystemTime::duration_since(UNIX_EPOCH)` yields the unix seconds
AUT-014 needs; `chrono` and `time` are both refused, because the dependency-divergence
follow-up carried out of M1a is a watched item at CNF-024 and this buys nothing.

**Rejected:** *record a parity exception and let Rust compute `RemainingTtl` from
`Instant`.* Rejected because it cannot be done — a monotonic instant has no epoch — and
CLA-003 does not permit an exception where the requirement is simply unimplementable.

**Rejected:** *add `now_utc()` to Rust only, leave the other two names alone.* Rejected on
R-9: it leaves `now()` meaning wall-clock in two languages and monotonic in the third.
Three of this project's four cross-language defects to date were a name that read the same
and behaved differently. The rename is mechanical, pre-1.0, and the public-surface gate
(CNF-027) will show it in all three baselines.

**Consequence.** A breaking change to a public extension point in all three SDKs, in a
pre-1.0 SDK, carrying a `CHANGELOG.md` **Changed** line (and **REC-006** does not apply —
this is library code, not agent tooling). Measured blast radius: 9 `.Now()` call sites
across 15 `IClock`-touching .NET files, 2 `impl Clock for` in Rust, 4 Python `Clock`
classes. This is M2a's first task, ahead of any auth code.

The RES-004 total-timeout deadline in .NET's request path (`context.Clock.Now() + totalTimeout`)
is wall-clock today and **stays** wall-clock after the rename. It is arguably the one place
that wants monotonic time, but changing it is a resilience decision with no M2 requirement
behind it, so it is out of scope and noted here rather than silently altered.

### D-M2-3 — `[REDACTED]` is the redaction marker in all three languages

**Forces.** CNF-032 requires a redacting default string representation but names no
literal. .NET and Rust render `[REDACTED]`; Python renders `SecretString(***)`. TST-051
wants a capturing logger to assert secret material is absent from a captured run, and the
cheapest honest assertion over three languages is one marker string.

**Decision.** The marker is `[REDACTED]` everywhere. The wrapper stays idiomatic:

| Surface | .NET | Rust | Python |
|---------|------|------|--------|
| Default string form | `ToString()` → `[REDACTED]` | `Display` → `[REDACTED]` | `__str__` → `[REDACTED]` |
| Debug form | — | `Debug` → `SecretString("[REDACTED]")` | `__repr__` → `SecretString("[REDACTED]")` |

Python changes both forms; .NET and Rust are already correct and are not touched.

**Rejected:** *leave Python's `SecretString(***)`.* It satisfies CNF-032 in isolation, and
that is the problem: a developer-facing string that differs across languages is R-9's
second defect class, and here it also makes the TST-051 assertion three assertions.

### D-M2-4 — `Auth.*` operations are built on the existing executor, and nothing is extracted

**Forces.** AUT-011 and AUT-012 require refined recognition of login failures. That
recognition already exists, generated from Appendix B §2 and wired into the request path at
M1c. It is reached by `StatusCodeMapper.Map` / `mapping::status_to_code` /
`map_status_to_code` + `recognise` + `enrich`, which the executor calls on every non-2xx.
Python's retry loop is private to `Client` rather than a separable `RequestExecutor`.

**Decision.** Every `Auth.*` operation issues its request through the same executor the
logical layer uses — .NET `Internal/RequestExecutor`, Rust `logical::execute_with_retry`,
Python `Client._execute`. **No `Auth.*` operation builds its own HTTP path, its own error
mapping, or its own recognition table.** AUT-011/AUT-012 are then satisfied by generated
data plus Appendix B, not by hand-written auth branches.

**Python's executor is not extracted.** Calling `Client._execute` is the accepted pattern;
it is what `Logical` already does. An extraction has no requirement behind it and CLA-007
takes the smallest change.

**Consequence.** Any AUT-011/012 row that does not fire is a defect in Appendix B §2 or in
the generator, fixed there and regenerated — never patched in an auth branch. This is
DR-0005's rule holding at its first heavy use.

### D-M2-4a — A 200 login rejection is an explicit second call site of the shared recogniser

**Finding (architecture review, B2 — upheld).** Revision 1 of D-M2-4 claimed AUT-011 came
free from the M1c recognition pipeline. **It does not.** AUT-010's rejected-credential
shape is HTTP **200** (`05-authentication.md:36,41`), and all three executors return a 2xx
body verbatim *before* recognition runs — .NET `Internal/RequestExecutor.cs:398-401`, Rust
`logical.rs:556`, and Python's mirror of it. The `data.error` literals in
`appendix-b-error-catalogue.md:261-274` are therefore never presented to `Recognise`.
AUT-012 (a **400** from `auth/approle/login`) does travel the non-2xx path and was fine;
AUT-011 reached recognition in no language, and revision 1's prohibition left the delegate
no sanctioned route to it.

**Ruling.** The login-response parser calls the **shared** recogniser itself:

```
parse login response:
  if status == 200 and auth.client_token is present and non-empty -> AuthInfo   (AUT-013)
  if status == 200 and auth.client_token is absent or empty:                    (AUT-010)
        message  = data.error
        code     = Recognise(message, 200, path) ?? BV-AUTH-003 LoginRejected
        hint     = Enrich(code, catalogue hint, context)
        raise with ServerMessage = message; never store a token
  otherwise -> the executor's existing non-2xx path, unchanged                   (AUT-012)
```

D-M2-4's prohibition is **amended**, not lifted: no `Auth.*` operation builds its own
recognition **table**, its own status mapping, or its own HTTP path. A 200 login rejection
is an additional **call site** of the same generated table, which is a different thing. The
rows need no new guard — the generator emits an empty status list for them
(`Generated/ErrorCatalogData.g.cs:958`, guard at `Internal/MessageRecognition.cs:79-95`),
so `Recognise(dataError, 200, path)` matches today.

**Retryability is `ERR-006`'s, never the call site's.** The architecture review asked
whether recognition from a 200 should force `Retryable = false`, on the theory that
`rate_limited: …` → `BV-RATE-001` would hand back a retryable error nothing will retry.
**No override, and the question does not arise:** `ERR-006`
(`04-error-model.md:49-51`) enumerates the retryable set exhaustively — `BV-TRANSPORT-001/002/003`,
`BV-SERVER-002`, `BV-SERVER-003`, `BV-RATE-002`, `BV-DISCOVERY-003` — and `BV-RATE-001` is
not in it. `appendix-b-error-catalogue.md:123` renders it `no` and
`tools/error-catalogue/catalogue.json` emits `retryable: False`.

A call-site override would therefore violate `ERR-006` directly, which is the primary
ground; D-M1c-25 (a deferred branch returns the specification's answer, never a plausible
guess) is the secondary one. *Revision 2 asserted the opposite — that `BV-RATE-001` "is
retryable in the catalogue" — which is false against all three sources. The ruling was
right and its premise was wrong; the premise is deleted so the next reader does not
re-litigate a settled requirement.*

**The real residue is hint accuracy, not retryability, and it is an M2b item.**
`appendix-b-error-catalogue.md:123`'s hint reads "The rate gate is paused for `RetryAfter`
seconds", which is **false on this path**: the gate arms on status alone (D-M1b-22), and a
200 login rejection never arms it. Because D-M2-4a routes through `Enrich`, `ERR-032` and
`ERR-040` make this fixture-covered work in **M2b** — either the hint is corrected in
Appendix B and regenerated, or the row gains a condition. It is not patched at the call
site.

`AUT-032` is adjacent but narrower than revision 2 implied: it forbids auto-retrying a
**locked account** (`BV-AUTH-006`) specifically, not every login rejection.

**Consequence.** `BV-AUTH-003` is the fallback, so an unrecognised `data.error` degrades to
a correct-but-coarse code rather than to a wrong one. A new server rejection string is then
a one-row Appendix B edit plus regeneration, exactly as D-M2-4 intends.

### D-M2-9 — The executor-to-token-source seam is pinned, and it is asynchronous

**Finding (architecture review, B1 — upheld).** Revision 1 pinned auth → executor and was
silent on executor → `TokenSource`. Today that direction is a synchronous field read:
`RequestExecutor.ResolveToken` (`Internal/RequestExecutor.cs:546-567`) calls
`ClientContext.GetToken()`, which is `public SecretString GetToken() => token;`
(`Internal/ClientContext.cs:47`). Three M2 requirements live on that seam — AUT-002's lazy
login, the `Callback` invocation, and AUT-003's replay — and none was pinned. Left
unpinned on an R3 surface, three languages would each have invented it differently.

**Ruling — the seam.** Token resolution becomes **asynchronous** in all three languages,
because two of the three source variants perform I/O:

```
TokenSource.Resolve() -> SecretString?          // async in all three
  Static   : returns the held token
  Callback : invokes the application function                       (async, see D-M2-6)
  Login    : returns the cached token; on first use performs the login and caches it
             (AUT-002); M2a ships this variant's contract, M2b its login call
```

The executor resolves **through the source**, not from a field. `ClientContext.GetToken()`
and its Rust and Python equivalents become the source-resolution call.

**Ruling — AUT-002.** The documented answer is **lazy on the first authenticated request**.
`Auth.Authenticate()` exists to force it eagerly and to surface a credential error at
startup rather than at first use. AUT-002 requires the SDK document which; the READMEs
authored at M4 state it, and `AUT-002`'s test asserts both paths.

**Ruling — ERR-022.** The preflight runs **after** resolution, never before: "no token" is
a source that *resolved* to absent or empty, not a `Login` source that has not resolved
yet. This is the distinction whose absence blocked revision 1.

**Ruling — AUT-003's replay sits outside the retry loop.** Re-login-and-replay is a
one-shot outer step wrapping one complete execution attempt, not an arm inside the
CFG-051…055 loop. `BV-AUTHZ-001` is not retryable, so the loop has already exited by then;
putting the replay inside it would mean re-entering a loop on a non-retryable code. The
step fires only when all of these hold: `ReloginOnPermissionDenied` is true (default
**false**), the source is `Login`, the token is older than `MinReloginInterval` (default
30 s), and the request was idempotent.

**Idempotent, for AUT-003, means `GET`, `LIST` and `HEAD` only.** Rejected: *HTTP
idempotency, which would include `PUT` and `DELETE`.* Rejected because AUT-003's own
justification is that a 403 also means "policy does not allow" — replaying a write after a
permission change is the exact case the opt-in warns about, and a replayed `DELETE` against
a vault is not a cost this SDK gets to choose on the application's behalf.

**Ruling — the replay is one logical operation, and D-M1b-8 is amended to say so.**
(Review finding F2.) The replay's *placement* was approved; its accounting was unstated,
and unstated it would have diverged three ways. `requestId` is minted **inside** the loop
(`Internal/RequestExecutor.cs:182`, carrying D-M1b-8's invariant as its comment: "stable
across every attempt of this logical operation"), and Rust's `execute_with_retry` and
Python's `Client._execute` build loop state internally in the same shape. A one-shot outer
step that re-enters the loop would therefore mint a **second** `requestId` and restart
`attempt` at 1 — two `Attempt: 1` observer events under two ids for one caller-visible
operation, and a thrown `attempts` that under-reports the first pass entirely.

The caller made **one** call, so:

1. `requestId` is **hoisted above the replay** and passed into the loop. One caller-visible
   operation keeps one `requestId` across both passes.
2. **Two counters, because `attempt` does double duty today.** One variable currently drives
   retry eligibility (`RequestExecutor.cs:250`, `attempt < maxAttempts`), backoff
   (`:262`, `ComputeBackoff(retryPolicy, attempt, …)`) and the reported count (`:324`,
   `attempts: attempt`). Revision 3 said "`attempt` accumulates", which taken literally
   feeds the accumulation into the eligibility check: at `MaxAttempts = 3`, a first pass
   that burns 1–3 on `BV-TRANSPORT-002` and ends on `BV-AUTHZ-001` enters the replay at
   3, `3 < 3` is false, and **a transient transport failure on the replayed request is
   never retried** — CFG-051…055 would silently not apply to the replay path. The backoff
   exponent would likewise start at the first pass's value instead of `InitialBackoff`.
   So: per-pass **`attempt`** governs eligibility and backoff and resets on the replay;
   accumulated **`attemptsTotal`** is what the error's `attempts` and the observer report.
3. The interposed **login** is a separate logical operation and gets its own `requestId`,
   which is correct — it is a different request to a different path.

This **amends D-M1b-8**, extending "stable across every attempt of this logical operation"
to "…including across an AUT-003 re-login replay". Recorded here rather than left to M2b
because it is a cross-language observability contract, and because the alternative —
declaring the replay a new logical operation — makes an application's own retry accounting
wrong for a feature it explicitly opted into.

**Rejected:** *declare the replay a new logical operation and emit two ids.* Rejected
because `ReloginOnPermissionDenied` is opt-in precisely so the application need not
implement the replay itself; handing it back two uncorrelated ids for one call gives it
worse observability than it had before opting in.

**Consequence.** A public extension point (`TokenSource`) is async from the start, so no
later milestone breaks it to add I/O. This is the same lesson as M1a's three incompatible
transport seams: the seam is cheaper to pin now than to amend once three languages have
shipped it. The internal executor entry point in all three languages gains an inbound
`requestId` and starting `attempt` — an internal signature change, not a public one.

**`CFG-020`'s unauthenticated-path list is consulted by the preflight, and it gets a
fixture.** (Review open question 2.) The preflight in ruling 3 above refuses only
*authenticated* operations, so it must consult `CFG-020`'s list — `sys/health`,
`sys/seal-status`, `sys/init`, `sys/unseal`, anonymous `sys/info`, `auth/*/login`,
`auth/ferrogate/requirement`, `auth/ferrogate/enroll`. M1b already recognises the
`auth/*/login` arm of that list for token omission (CFG-020's first MUST, D-M1c-24's
`ResolveToken` fix); M2b extends the same list to the refusal decision rather than
introducing a second copy of it.

### D-M2-11 — `Resolve()` is single-flighted, a retry keeps its token, and `CFG-070` returns to the baseline

**Finding (architecture review, R2 — upheld).** D-M2-9 replaces `ClientContext.GetToken()`
with the source-resolution call. That method's own comment is CFG-070's entire
justification — `Internal/ClientContext.cs:46`: *"Thread-safe token read/write (CFG-070). A
reference assignment/read is already atomic in .NET; no lock is needed."* An atomic
reference read is a sound basis for `CFG-070`
(`02-client-configuration.md:166-167`: thread-safe, and **in-flight requests keep the token
they started with**). An **async, side-effecting, cache-filling** resolve is not, and
revision 3 ruled nothing on concurrency.

The concrete failure it allowed — **thundering-herd login.** A `Login`-sourced client with
8 concurrent operations at startup: all 8 resolve, none finds a cached token, all 8 log in.
Eight tokens issued, seven orphaned, against the one path the server's DoS guard
rate-limits; a later one returns `rate_limited: …` → `BV-RATE-001`, non-retryable under
ERR-006, so the application's first request fails at startup.

*The review's second scenario — that a retry re-reads the token and so can switch tokens
mid-operation — was **false in all three languages** and is deleted. The resolution call
executes once above the retry loop (`RequestExecutor.cs:181`, loop at `:186`), and Rust and
Python snapshot equivalently; see ruling (b). It was withdrawn by the reviewer who raised
it. Recorded rather than quietly dropped, because this record has now twice reached a
correct ruling from a false premise (the other was D-M2-4a's retryability claim), and both
times the premise came from outside the ruling's own reasoning.*

Left unpinned, three languages pick three primitives — `SemaphoreSlim`/`Lazy<Task<T>>`,
`tokio::sync::Mutex`/`OnceCell`, `asyncio.Lock` — and whether the observable result is one
login or N differs per SDK. That is the R-9 class landing on the very seam D-M2-9 exists to
pin, and nothing catches it: `CFG-070` is off the baseline so no ratchet fires, and its only
fixture is single-threaded.

**Ruling (a) — `Resolve()` on a `Login` source is single-flighted.** Concurrent first-use
resolutions await **one** login, not N. The losers do not queue a second login; they await
the in-flight one and receive its token. Per language: .NET `Lazy<Task<SecretString>>`
(re-armed on invalidation, not `SemaphoreSlim` around a null check); Rust
`tokio::sync::OnceCell` plus a `Mutex` for re-arming; Python a module-private
`asyncio.Lock` with a double-checked cache read. `Static` and `Callback` are unaffected —
`Static` is a read, and a `Callback` is the application's own function, which the SDK does
not get to deduplicate on its behalf.

**Ruling (b) — not a new ruling: this is [D-M1b-9](0004-m1b-transport.md), restated.**
D-M1b-9 already decided it — *"Each logical operation **snapshots** the token once at its
start and uses that snapshot for all of its attempts — this is CFG-070's 'in-flight
requests keep the token they started with', and it is also why the snapshot happens outside
the retry loop"* — and all three SDKs already behave that way: .NET `RequestExecutor.cs:181`
above the loop at `:186`, Rust's `headers` built by the caller and passed into
`execute_with_retry` (`logical.rs:471-480`), Python `client.py:201` above `while True:` at
`:214`.

**Nothing relocates.** The snapshot stays exactly where D-M1b-9 put it; the *only* change is
that it now comes from `TokenSource.Resolve()` instead of a field read. An earlier revision
of this paragraph said resolution "moves above the retry loop", which would have told a
delegate to relocate correct code on an R3 surface. Cited rather than re-derived, per
**TOK-008** and **CLA-008**.

**The AUT-003 replay is a new pass and therefore does re-resolve**, and that does not
violate `CFG-070`. Its second sentence protects against an **unsolicited** credential change
mid-flight — another thread's `SetToken` — not against every token change under one
`requestId`. The re-resolution is in-band and opted into via `ReloginOnPermissionDenied`,
and a replay carrying the old token would be pointless: AUT-003 requires re-login *then*
replay.

**The two senses of "one operation" are therefore both live, and they do not conflict:**
one for **observability** (D-M2-9 — one `requestId`, accumulated `attemptsTotal` across the
replay) and one for **credential identity** (D-M1b-9 — one snapshot per pass). A delegate
reading D-M2-9 and this ruling together must not infer a contradiction; there is none.

**D-M1b-9 covers (b) entirely and (a) not at all**, which is exactly why (a) was worth
blocking on: each pass snapshots independently, so N concurrent first-use passes each call
`Resolve()` and each triggers a login. Single-flight is the load-bearing ruling here.

**Single-flight and the replay compose, which is a benefit worth claiming:** five concurrent
403s all trigger a replay and all await **one** re-login, achieving `MinReloginInterval`'s
intent structurally rather than by clock arithmetic.

**Ruling (c) — `CFG-070` is re-opened onto the baseline, owned by M2a.** It was closed at
M1a on a justification D-M2-9 invalidates, and closing an ID on a mechanism that no longer
exists is the same defect as never closing it — it just looks green. Re-opening is the
D-M1a-10 practice applied in the direction nobody has needed yet. Its M2a test asserts the
single-flight and the in-flight-token invariants under concurrency, which is what its M1a
test never did.

**Arithmetic.** Re-opening moves the baseline 305 → **306**; M2 then removes **39**
(M2a 14, M2b 19, M2c 6), reaching **267** — the same exit number, honestly derived.

**Consequence.** Concurrency is now an M2a review dimension, not an M2c afterthought.
`AUT-094`'s renewal loop in M2c lands on a seam that already states its concurrency
contract, rather than discovering it.

### D-M2-10 — `errors.recognition.missing-token-client-side` becomes green at M4, not M2

**Finding.** Deciding the `CFG-020` fixture above surfaced an error in an accepted record.
DR-0005 D-M1c-14 item 10 books `errors.recognition.missing-token-client-side` as pending
"(M2)". It cannot be: its operation is **`Kv.V2.ReadSecret`**, which does not exist until
**M4**. Its *requirements* (`CFG-020`, `ERR-022`) are M2's; its *driving operation* is M4's.
M2 would therefore have removed both IDs from the baseline while the fixture asserting them
stayed pending — a requirement marked covered by a fixture that never ran.

**Ruling.** The existing fixture is **not** edited: changing it is a specification change
under `FIX-012`, and it is correctly placed — it belongs to the `errors.recognition.*`
family and will go green at M4 when `Kv.V2.ReadSecret` lands. Instead M2b authors
`auth.token.lookup-self-no-token-client-side` (on disk), which asserts the same two
requirements through `Auth.Token.LookupSelf`, an M2a operation, with `attempts: 0` and no
exchanges — the shape of `auth.token.create-reserved-meta-client-side`.

**This amends DR-0005 D-M1c-14 item 10:** that fixture's owner is **M4**, not M2. The
`errors.format.one-line` half of the same item is unaffected — it drives
`Auth.Token.Lookup`, which is genuinely M2a, and it does go green at M2.

**Consequence.** A pending fixture's owning milestone is the milestone that lands its
**operation**, not the one that lands its requirements. All three of DR-0005's pending
fixtures were booked by *requirement*; `errors.format.one-line` is right only because its
operation happens to land at M2a anyway — `ERR-002` and `ERR-003` were likewise removed from
the baseline at M1c while that pending fixture was their only one. The rule is stated in
every M2 brief so the next deferral is booked by operation.

**The corpus has two more instances, and they get one sweep rather than three rulings.**
`auth.token.revoke-self-clears-token` asserts `CFG-070` (claimed at M1a) and
`resilience.failover.read-once` asserts `RES-001` (also already off the baseline) — the
mirror image of this case: the requirement is closed while its only fixture cannot run yet.
`CFG-070` is resolved by D-M2-11(c). `RES-001` is M5's and is **not** M2's to fix. A
one-time sweep of all 208 requirement-carrying fixtures for this pattern is booked as an
**M2-exit REC-002 item**, so the remaining instances are found once by a tool rather than
three times by a reviewer.

**`auth.token.lookup-self-no-token-client-side` is held `pending` through M2a**
(review finding R3). Its operation `Auth.Token.LookupSelf` lands at M2a, so by the rule
above it stops being pending there — one slice **before** the `CFG-020`/`ERR-022` preflight
it asserts exists, which lands at M2b. Left alone it would go red at M2a's handback, read as
a regression, and the cheapest-looking fix would be to weaken it (**CLA-004**). M2a's
pending list therefore carries it with the reason *"operation exists; asserted behaviour is
M2b"* — the first pending entry in this project justified by behaviour rather than by a
missing operation — and **M2b's exit removes the entry and the fixture goes green.**

### D-M2-5 — AUT-043 is deferred to M10; AUT-035/050…054/060/070 to M6

**Revised at revision 2 (review finding F3).** Revision 1 sent `AUT-043` to M10 on a
Complete-level argument and then booked `AUT-054` — identical in shape, also wholly
admin/Complete (`05-authentication.md:147-149`) — to M6, with no reason for the split. It
also broke a later milestone: `ROADMAP.md` §4 states M6's exit criterion as "Section 05 has
zero unimplemented MUSTs", which `AUT-043` at M10 makes unachievable. M2 would have exited
having silently invalidated M6's gate.

**Ruling.** All nine deferred section-05 IDs go to **M6**: `AUT-035` (FIDO2),
`AUT-043` (`Auth.AppId.Admin`), `AUT-050`…`AUT-054` (FerroGate), `AUT-060` (OIDC/SAML) and
`AUT-070` (Certificate). M6 keeps its exit criterion and needs no `ROADMAP.md` amendment.
The conformance **level** at which a surface is *declared* is a README claim (D-4);
implementing an admin surface earlier than its declared level is always permitted and costs
nothing. Each baseline entry names **M6** as its owner, per D-M1a-10.

`AUT-035` is at M6 rather than M2 despite hanging off `Auth.Userpass` — the grouping Core
does name — because its own surface (`Auth.Userpass.Fido2LoginBegin`/`Fido2LoginComplete`,
`Auth.Fido2.*`, `05-authentication.md:88-89`) is a distinct login method, and
`01-conformance-and-quality.md:9` scopes Core to "05 (Token + AppID + Userpass)" by
*method*, not by grouping.

`OVR-008` and `OVR-009` **stay on the baseline.** M2 creates the first sub-API grouping and
it would be easy to claim them here, but OVR-008 says *every* area, so a test asserting it
at M2 asserts one ninth of the requirement. Claiming it now is the `CFG-001` false positive
of M1a repeated deliberately. Owner: **M10**, when the last area exists.

### D-M2-6 — The public surface is pinned here, member by member

Per `ROADMAP.md` §7's post-M1a rule: the brief pins every public name, not only the
behaviour. Canonical names below; each language applies the OVR §"Naming and idiom mapping"
table and nothing else. A delegate that wants a different name escalates rather than
choosing.

**Grouping (OVR-008, AUT-001, AUT-004)**

```
Client.Auth                          -> AuthOperations           (sub-API grouping)
Auth.TokenSource                     -> TokenSource              (get; exactly one, AUT-001)
Auth.CurrentToken                    -> SecretString?            (redacting, AUT-004)
Auth.TokenInfo                       -> TokenInfo?               (last LookupSelf, AUT-004)
Auth.Authenticate()                  -> AuthInfo                 (eager login, AUT-002)
Auth.PersistToken()                  -> void                     (CFG-031)
Auth.ForgetPersistedToken()          -> void                     (CFG-032)
Auth.Token / Auth.Userpass / Auth.AppId                          (method groupings)
```

`Client.SetToken(SecretString)` and `Client.ClearToken()` already exist and are kept;
`SetToken` now replaces the source with `Static` (AUT-001).

**Token source (AUT-001, AUT-003)**

```
TokenSource.Static(SecretString token)
TokenSource.Login(AuthMethod method, LoginCredentials credentials, LoginOptions? options)
TokenSource.Callback(fn() -> async SecretString)
TokenSource.Resolve() -> async SecretString?                       (D-M2-9, the seam)
LoginOptions { ReloginOnPermissionDenied = false, MinReloginInterval = 30s }
```

**`Callback` is asynchronous** (review finding F4): `Func<CancellationToken, Task<SecretString>>`
on .NET, an async trait method on Rust, `async def`/awaitable on Python. Rejected:
*a synchronous callback.* Rejected because the spec's own example for this variant is
"secrets from a KMS" (`05-authentication.md:13`) — I/O-bound, naturally async in Rust and
Python, and a sync signature would force every real implementation to block a runtime
thread inside the request path. A sync convenience overload may be added later without a
breaking change; the reverse is not true.

`ReloginOnPermissionDenied` and `MinReloginInterval` are added to the M1a settings
catalogue with their defaults. **They are settings, so they belong in the settings table**
— M1a's `RateGate`/`AutoRenew` omission made `CFG-001` a false positive on the ratchet, and
that is not repeated.

**Login result (AUT-013, AUT-014, AUT-044)** — already pinned on the wire by
`auth.appid.login-ok-with-machine-token-and-namespace` and `auth.userpass.login-ok`.
**Amended at D-M2-26: the AUT-014 optionals below are removed from `AuthInfo`.**

```
AuthInfo { ClientToken: SecretString, Policies: string[], Metadata: map<string,string>,
           LeaseDuration: int (seconds), Renewable: bool, IssuedAt: timestamp,
           EnvironmentScope: EnvironmentScope }
EnvironmentScope { Scoped: bool, SecretGlobs: string[], MachineGlobs: string[] }
```

**Token store (AUT-080…AUT-085)**

```
Auth.Token.Use(SecretString token)                 -> void      (no network, AUT-020)
Auth.Token.Verify()                                -> TokenInfo (LookupSelf)
Auth.Token.Create(CreateTokenRequest request)      -> AuthInfo  (AUT-082)
Auth.Token.Lookup(string token)                    -> TokenInfo
Auth.Token.LookupSelf()                            -> TokenInfo
Auth.Token.Renew(string token, int increment)      -> AuthInfo
Auth.Token.RenewSelf(int increment)                -> AuthInfo  (via renew/{token}, AUT-080)
Auth.Token.Revoke(string token)                    -> void
Auth.Token.RevokeOrphan(string token)              -> void
Auth.Token.RevokeSelf()                            -> void      (clears local token, AUT-083)
Auth.Token.AuditLogin()                            -> void

TokenInfo { Id, Policies, Path, Meta, DisplayName, NumUses, CreationTime, CreationTtl,
            ExplicitMaxTtl, Period?, RemainingTtl? }
```

`TokenInfo` **does not expose the wire `ttl`** (always `0`); `RemainingTtl` is
`CreationTime + CreationTtl − NowUtc`, null when `CreationTtl == 0`.

```
CreateTokenRequest { Policies, Ttl, Period, NumUses, Renewable = true, Meta, DisplayName,
                     ExplicitMaxTtl, NoDefaultPolicy, NoParent, Id, Type, ChildVisible,
                     UseResult = false }
```

`UseResult` is the AUT-082 opt-in that switches the client's token. Default `false`.

**Login methods (AUT-030…AUT-032, AUT-040…AUT-042)** — **amended at D-M2-26: the one-shot
login methods take `RequestOptions?`, not `LoginOptions?`.** `TokenSource.Login`'s
`LoginOptions?` above is unchanged.

```
Auth.Userpass.Login(string username, SecretString password, string? totpCode = null,
                    string mount = "userpass", RequestOptions? options = null) -> AuthInfo
Auth.AppId.Login(string roleId, SecretString? secretId = null,
                 SecretString? machineToken = null,
                 string mount = "approle", RequestOptions? options = null)     -> AuthInfo
Auth.AppId.ReadRoleId(string roleName, string mount = "approle")             -> string
Auth.AppId.GenerateSecretId(string roleName, SecretIdOptions? options = null,
                            string mount = "approle")                       -> SecretIdInfo

SecretIdOptions { Metadata: map<string,string>, CidrList, TokenBoundCidrs, NumUses, Ttl,
                  Environments }
SecretIdInfo   { SecretId: SecretString, SecretIdAccessor, SecretIdTtl, SecretIdNumUses,
                 Environments }
```

`password`, `secretId`, `machineToken` and `SecretIdInfo.SecretId` are `SecretString`
(AUT-031, CNF-031). `SecretIdOptions.Metadata` is a map in the API and a JSON **string** on
the wire (AUT-042).

**Automatic renewal (AUT-090…AUT-095)**

```
AutoRenew { Enabled = false, RenewAtFraction = 0.66, MinInterval = 10s,
            Increment = null /* server default */, MaxConsecutiveFailures = 5,
            OnRenewed(RenewalEvent), OnFailed(RenewalEvent), OnStopped(RenewalStoppedReason) }
RenewalStoppedReason = RenewalFailed | ReloginFailed | TokenRevoked | NotRenewable | Disposed
```

### D-M2-7 — Two new harness instruments, and each is proven by a seeded failure

**Fixture `clock`.** All three drivers read `fixture.clock.start` and `clock.advance` and
inject a controllable clock implementing D-M2-2's contract. `clock.start` is an absolute
timestamp; each `clock.advance` entry is a duration applied between exchanges. A fixture
that declares `clock` and runs against a driver that ignores it must **fail**, not pass —
which is how `auth.token.lookup-self-remaining-ttl` has been silently unasserted since M0.

**Capturing logger (TST-051).** The fixture harness attaches a capturing logger **and a
capturing `RequestObserver`** to every fixture run in all three languages and asserts, after
every fixture, that no fixture token, password or secret-id literal appears in any captured
**log line, observer event, exception message, or rendered error**. This is a whole-run
assertion, not a per-fixture opt-in.

**The observer is named explicitly** (review finding F6) because it is where the leak this
milestone is most likely to ship actually surfaces: `RequestEvent.Path` carries the request
path, and AUT-080's `POST auth/token/renew/{currentToken}` and `Auth.Token.Lookup`'s
`GET auth/token/lookup/{token}` **put a live token in the path**. ERR-003 already requires
that path redacted in errors; the observer is the second consumer of the same string and was
not covered by revision 1's wording.

**This assertion is exactly as strong as TST-050 compliance.** It works only because
`15-testing-requirements.md:350-353` forces distinctive fixture secrets (`s.FAKE…`,
`password-fixture`), which is what makes a substring search a valid test. Every one of M2's
six new auth fixtures is checked against TST-050 before the assertion is trusted.

**Proof obligation, both instruments.** Per R-10 and DR-0001: seed a violation and show it
fails. For the clock, break `RemainingTtl` and show
`auth.token.lookup-self-remaining-ttl` goes red. For the logger, log a fixture token at
debug level and show the run goes red. An instrument that has never failed has not been
tested, and this project has now shipped four gates that were trusted on their record.

### D-M2-8 — DR-0005's D-M1c-25 rule is restated in every M2 brief

No branch in M2 returns a plausible guess pending a later milestone. Where a value must be
deferred, it returns what the specification names, so it fails loudly when its fixture
arrives. Every M2 slice's brief carries this sentence, and every M2 review checks for its
violation before checking anything else.

Concretely, for M2, these are **absent** at M2, not stubbed: `Auth.Cert.Login`, the
FerroGate operations, the OIDC/SAML operations, `Auth.AppId.Admin`, and —
added at revision 2 (review finding F5) — `Auth.Userpass.Fido2LoginBegin`,
`Auth.Userpass.Fido2LoginComplete` and `Auth.Fido2.*`. FIDO2 is called out by name because
it hangs off the `Auth.Userpass` grouping M2b does build, which makes it the deferred
surface most likely to acquire a stub. `auth.cert.disabled-server` reports `pending` until
M6, as `errors.format.one-line` did until now.

## Consequences

- `Client.Auth` fixes the shape every later engine grouping copies (OVR-008). M3's `Sys`
  and M4's `Kv` inherit it, so the grouping mechanism is reviewed here as a contract, not
  as auth detail.
- The two instruments are worth more than M2's requirements. Fixture `clock` unblocks every
  clock-driven fixture in M5 (backoff, failover) and M8 (rate gate, cache epochs); the
  capturing logger makes TST-051 assertable in every later milestone instead of being a
  requirement nobody has ever run.
- D-M2-2 and D-M2-9 are both breaking changes to public extension points in three
  languages (`Clock`, `TokenSource`), taken in the cheapest milestone that is forced to
  touch them anyway. Neither is available later without a second break.
- The baseline reaches **267**, after `CFG-070` is re-opened and re-closed (D-M2-11c). `AUT-035`, `AUT-043`, `AUT-050`…`AUT-054`, `AUT-060` and
  `AUT-070` leave M2 with **M6** as their recorded owner; `OVR-008` and `OVR-009` with
  **M10**.
- **Two `ROADMAP.md` edits are due at M2 exit (REC-002)**, both consequences of D-M2-5:
  §4/§5's M6 parenthetical "(FerroGate, Certificate, OIDC/SAML, FIDO2)" must also name
  AppID admin, and M10's row — booked at 9 IDs listing only `IDN`/`RSC`/`FIL`/`LDP`/`RUS`
  — must carry `OVR-008` and `OVR-009`, which it does not today.
- Two of the architecture review's findings were the same shape — a seam pinned on one side
  only (D-M2-4a, D-M2-9). Both were invisible to the record's own reasoning and visible
  immediately to a reader who followed the call path. That is the fourth time this project
  has found a defect by reading a path rather than running a suite, after M1b's Rust root
  store and pooling and M1c's `409`/`503`. **M2's reviews read the request path, not only
  the fixture results.**

## Addendum — M2a .NET pathfinder pass (2026-09-14)

The pathfinder returned M2a green at 452 tests / 98.47 % line / 96.32 % branch, baseline
305 → 306 → **292**, both instruments proven by seeded violation and then converted into
standing tests. Verified independently by the Strategic Orchestrator before review:
`dotnet test` exit 0, the coverage figures, the baseline arithmetic and its M2b remainder,
no surviving seed marker, `rust/` and `python/` untouched, and no deferred auth surface in
`PublicApiSurface.txt`. Six escalations are ruled here (**FAM-002** — they are all
Strategic).

### D-M2-12 — The four unpinned names are ratified

D-M2-6 pinned the public surface and told a delegate to escalate rather than choose. Four
members it did not pin were chosen provisionally and are **ratified**, because Rust and
Python will copy them verbatim:

| Member | Ruling |
|--------|--------|
| `BastionVaultClientOptions.TokenSource` | **Approved.** Without an injection point, `TokenSource.Callback` is decorative: AUT-004 makes `Auth.TokenSource` read-only and `SetToken`/`Use` install only `Static`. D-M2-6 pinned the type and forgot its way in |
| `TokenSourceKind Kind` | **Approved.** The minimal observable that makes AUT-001's "exactly one" and "`SetToken` replaces it with `Static`" assertable at all |
| `TokenInfo.Id` as `SecretString?`, not `string` | **Approved, and the reasoning is upheld.** A lookup's `id` *is* token material; a plain string would be a second, non-redacting way to read the very token AUT-004 requires `CurrentToken` to redact (CNF-031/032). Conflict rule 4 — on a tie with a security dimension, the safer result wins |
| Public `TokenSource.Login` factory deferred to M2b | **Approved.** `AuthMethod` and `LoginCredentials` have no content until M2b, and a factory that throws is a stub, which D-M1c-25 forbids. Adding a factory later is not a breaking change; fixing a throwing one is a defect no gate can see |

**These four are now pinned.** The Rust and Python brief carries them as pinned names, not
as choices.

### D-M2-13 — `BV-CONFIG-011 TokenFileNotWritable` is minted, in the Rust/Python pass

**Finding.** `CFG-031`'s `PersistToken()` can fail because the file is unwritable, and
**Appendix B has no code for it.** The pathfinder used `BV-CONFIG-005 FileNotReadable`,
whose message says the file cannot be *read*. That is a genuine specification gap, not a
deferred branch, so D-M1c-25 does not apply — there is no specified answer to return.

**Ruling.** Mint `BV-CONFIG-011 TokenFileNotWritable` in Appendix B, and land it in the
**Rust/Python slice**, which regenerates the catalogue anyway. One Appendix B edit, one
regeneration, one changelog line, and all three languages get it together; .NET's
`BV-CONFIG-005` is corrected in the same pass. Minting it in M2a instead would bolt an R3
specification change onto a slice that has already been reviewed.

**Carried forward, honestly:** .NET ships a knowingly-wrong code for one unreleased slice,
with a named owner. *Superseded within the same milestone: D-M2-16 minted both codes in M2a,
so .NET never shipped the wrong one — and `v0.5.0` is tagged (D-M2-15, overridden).*

### D-M2-16 — `BV-AUTH-017 TokenSourceFailed`, and D-M2-13 amended to mint both codes now

**Finding (R3 handback review, F4 — upheld).** D-M2-9 made token resolution
I/O-performing, and `RequestExecutor` catches only `OperationCanceledException` around it.
A `Callback` source is **application code**: an `HttpRequestException` from a KMS lookup
reaches the caller as a raw runtime exception out of `Logical.ReadAsync`, which
`ERR-020`/`TRN-054` forbid. The reviewer correctly refused to choose the code — it is a
public error-contract decision across three SDKs, and `05-authentication.md` is silent on
callback failure.

**Ruling.** No existing code fits. `BV-AUTH-001 NoToken` is the closest and is wrong: its
message is "No token is **configured**", and here a token *is* configured and its
resolution failed. So a code is minted:

```
| BV-AUTH-017 | TokenSourceFailed | The configured token source did not produce a token. |
```

Retryable **false**, because `ERR-006`'s enumeration is closed and does not list it.
The source's own exception is the cause and is preserved as such. `attempts = 0`, as with
the cancellation arm — no request was issued.

**D-M2-13 is amended: both codes are minted now, in M2a.** That ruling deferred
`BV-CONFIG-011 TokenFileNotWritable` to the Rust/Python pass to buy one regeneration. F4
removes the saving — M2a must regenerate anyway for `BV-AUTH-017` — so both rows land in
one Appendix B edit and one regeneration, and .NET stops shipping `BV-CONFIG-005` for an
unwritable file. Both rows are written by the Strategic Orchestrator (`specifications/` is
Claude-owned, and an Appendix B edit is R3 under `CRS-004`); the delegate regenerates and
wires them.

*`BV-AUTH-016` was already taken by `SecondFactorFailed`. Recorded because the obvious next
number was wrong, and a code collision in a generated catalogue is the kind of defect that
looks like a merge conflict rather than a design error.*

### D-M2-17 — A faulted single-flight re-arms; it is M2a's, not a deferral

**Finding (F3 — upheld).** `Lazy<Task<SecretString>>` with `ExecutionAndPublication` caches
the *faulted task*, and a synchronous throw from `login` caches the **exception**, rethrown
on every subsequent `.Value`. `Invalidate()` is called from tests only — never from library
code. So **one transient login failure at startup makes the client permanently unusable**,
and every later caller gets a stale exception with the wrong `Attempts` and `RequestId` and
no observer event.

**Ruling.** `ResolveAsync` **re-arms on fault**, in M2a. This is not a new decision and not
a deferral: D-M2-11(a) said concurrent callers share **one login attempt**, never that a
failure is cached for the client's lifetime. The failure path was simply unruled, and an
unruled property of a pinned primitive is exactly what the other two SDKs would copy.

The semantics that follow, and they are the right ones: the awaiters **of one flight** all
observe that flight's failure — they do not each retry — while a **subsequent** resolution
re-attempts. So there is no retry storm, and there is no permanent poisoning either.

### D-M2-14 — `IClientLogger` gains no level in M2a; `CNF-031` decides it in M2b

`IClientLogger` exposes only `Warn`, so the TST-051 proof was seeded at that level rather
than at debug. The pathfinder declined to add a public interface member with no requirement
behind it, which is right (**CLA-007**). CNF-031's carve-out — a token *may* be shown as
the first four characters plus `…` **when debug logging is explicitly enabled** — is what
implies a debug level, and `CNF-031`/`CNF-032` are **M2b**'s IDs. M2b decides whether that
carve-out is implemented and therefore whether the level exists. It is not M2a's to invent.

### D-M2-15 — No version bump until a slice is complete in all three languages — **overridden by the project owner for 0.5.0**

**The ruling as made.** `SdkInfo.SdkVersion` stays at `0.4.0`. M1b and M1c each bumped at
their slice's Strategic commit, but each of those slices was complete in all three
languages. M2a is one language of a slice, so the bump waits for the Rust and Python pass.
Release authorisation is not a delegate's (`claude.md` §5) and a partial slice is not a
release.

**Overridden, on the record (2026-09-14).** The project owner directed a minor bump, commit
and tag on M2a's acceptance. This was raised as conflicting with two things — this ruling,
and the stronger and older statement at the top of [`CHANGELOG.md`](../CHANGELOG.md) that
"a release is cut only when all three match (CLA-003)" — and reaffirmed. It is the owner's
call to make and `v0.5.0` is tagged accordingly.

**Neither rule is amended.** The `0.5.0` entry carries the deviation as a note instead,
because a rule rewritten to match the one release that broke it stops being a rule, while a
recorded exception stays visible to whoever cuts `0.6.0`. Rust and Python at `0.5.0` carry
the regenerated catalogue and none of the auth surface; anyone reading the tag needs that
stated, not inferred.

### D-M2-23 — `v0.5.0` was tagged with two red `CNF-027` gates, and this is the cost of shipping unverified

**Finding (Strategic Orchestrator, running the suites after the parity passes).** M2a
regenerated the error catalogue for all three languages, so Rust's and Python's public
surfaces each gained `AUTH_TOKEN_SOURCE_FAILED` and `CONFIG_TOKEN_FILE_NOT_WRITABLE`. .NET's
baseline was regenerated. **Rust's and Python's were not** — so `CNF-027` was red in two of
the three CI jobs at the moment `v0.5.0` was tagged and pushed.

**Root cause is mine and it is the one I flagged at the time.** The M2a commit message says
Python's suite was not runnable in that environment and its fixture-count assertion was
"updated mechanically and unverified — CI runs it". That disclosure was honest and it was
not sufficient: an unverified change is an unverified change, and the gate it broke is the
one whose entire purpose is catching a surface that moved without its baseline. The Rust
baseline had no such excuse — `cargo +nightly public-api` was available and I did not run it.

**Fixed.** Both regenerated by their documented mechanisms, never hand-edited:
`python -m tests._api_surface_extractor --write` and
`cargo +nightly public-api --simplified | grep -v '^pub mod ' | sort`. Python then shows
472 passing with 12 failures, all `test_mock_server.py`/AKI (D-M2-22 item 2).

**The rule this earns.** A slice does not exit on a language whose gates were not executed.
"CI will run it" is a forecast, not evidence, and this project has now been bitten by that
five times (D-M1b-19, D-M1c-15, D-M1c-16, D-M1c-22, and this). Where an environment genuinely
cannot run a language's suite, the honest move is to **stop and say so as a blocking
condition** — which is what M2a's Python brief told the delegate to do, and what the
orchestrator did not do to itself.

### D-M2-24 — `remaining_ttl` clamps to zero; the change is .NET's in Stage 1 and the contract's for M13

**Ruling (F3, tested adversarially by the R3 gate and upheld).** `TokenInfo.RemainingTtl`
clamps to zero for an expired token in all three languages; `None`/`null` keeps its single
specified meaning, `creation_ttl == 0`. Rust's `Duration` is unsigned, so the only way to
carry a signed value is to give this one field a different unit from `creation_ttl`,
`explicit_max_ttl` and `period` — the R-9 "one name, two contracts" class this record has
already paid for twice (D-M2-2, D-M2-3).

**The argument against is recorded because it is a real loss.** A caller that can read `−4h`
can distinguish "expired long ago, this lookup is stale" from "expires now", and can warn on
an absurd value that indicates clock skew; clamped, both collapse into `0`. The gate's
verdict is that this is diagnostic rather than actionable — AUT-014 names no diagnostic
consumer, and `creation_time` plus the clock remain exposed for anyone who wants to compute
it. Accepted on that basis, not because the loss is nil.

**Ownership after the restaging.** Rust and Python are parked, so there is no live
divergence on `main` — .NET is the only implementation, and it still returns a negative
value as shipped in `0.5.0`. The clamp lands in **.NET during Stage 1**, so that **M13**
inherits a settled contract rather than re-litigating it, and it carries its own
`CHANGELOG.md` **Changed** line because it alters a shipped public value. D-M2-6 is amended
accordingly: `RemainingTtl` is non-negative.

### D-M2-21 — `auth.token.lookup-self-remaining-ttl` did not test AUT-014's arithmetic, and now does

**Finding (Python parity pass, confirmed by the Rust pass and verified here).** The fixture's
`clock.start` was `2026-09-13T12:00:00Z` and its `creation_time` was `1789300800` — **the
same instant**. So `creation_time + creation_ttl − now` degenerated to `creation_ttl`, and an
implementation that returned the bare `creation_ttl` field passed. The fixture proved the
clock was injected and read; it did not prove the formula.

That also means **.NET's instrument proof was weaker than its record claimed**: the seeded
violation there was `RemainingTtl` off by one second, which the fixture does catch — but the
likelier real defect, returning `creation_ttl` unchanged, it did not.

**Ruling (`FIX-012`, therefore a specification change and the Strategic tree's).**
`clock.start` moves to `2026-09-13T12:30:00Z` and the expectation to `PT30M`. Now the
correct formula yields 1800 s, the bare field yields 3600 s, and zero yields zero — all three
plausible wrong implementations fail.

**Used as a cross-language probe, which is what a shared fixture is for.** After the edit,
`dotnet test`, `cargo test` and the Python suite were each re-run: **all three still pass**, so
all three genuinely compute the formula rather than echoing the field. Had one failed, this
edit would have found a real parity defect that the ratchet, coverage and the surface diff
are all blind to.

**Consequence, and the general lesson.** A fixture whose inputs make two implementations
indistinguishable is not a test, and neither coverage nor the ratchet can see that: this one
was 100 %-covered and "green" in three languages while asserting less than it claimed.
**When a fixture pins a formula, its inputs must make every operand load-bearing** — if any
input can be deleted from the computation without changing the expected output, the fixture
does not pin the formula. Worth applying to the clock-driven fixtures M2c will author.

### D-M2-22 — Three defects in the project's own instruments, found while verifying M2a

None is M2a's, all are recorded so they have owners rather than being rediscovered.

1. **`agents.md` §9's Python verification command has never worked.** It reads
   `PYTHONPATH=./python/src python -m unittest discover -s ./python/tests`, which collects
   **12 of 588 tests and errors on all 12** — the suite has been pytest-style since M1b
   (module-level functions, `parametrize`, `tmp_path`). The normative verification contract
   named a command that cannot verify anything. Corrected in `agents.md` and
   `skills/codex/SKILLS.md` to CI's own command,
   `(cd python && python -m pytest tests -m "not integration")`, and verified to collect 578.
   *This is R-10's shape applied to the verification contract itself: three milestones
   quoted a Python test run, and the command in the contract was not the one that ran.*
2. **The mock server's self-signed certificates are rejected by OpenSSL from Python 3.13.**
   12 `test_mock_server.py` tests fail with `SSLCertVerificationError: Missing Authority Key
   Identifier`; the certs lack an AKI extension newer OpenSSL requires. CI pins **3.12**, so
   CI is green and the defect is invisible there. M0 harness debt with a deadline set by
   someone else's release schedule — the pin hides it until it cannot.
3. **`FIX-012` requires a changelog that does not exist.** It says a fixture change "MUST be
   reflected in the changelog of this spec", and `specifications/` has no changelog. D-M2-21
   is recorded in the project `CHANGELOG.md` and here instead. Either `FIX-012` should name
   the project changelog or `specifications/` should grow one; that is a specification edit
   and goes to the Architect queue with D-M2-18 item 2.

### D-M2-20 — A parity pass is row 2, not row 3. M2a's parity passes were over-routed

**Finding (raised by the project owner).** M2a's Rust and Python passes were dispatched to
`eng-deep`, which `.claude/agents/eng-deep.md` binds to **Claude Opus 5** — 15× — on the
strength of routing-matrix row 3 ("new subsystem, cross-language contract"). **No escalation
trigger was recorded**, although that agent's own description requires one and says "never a
first attempt". That is a direct lapse against `agents.md` §4.3 rule 4 and **TOK-012**
("upgrading a model requires a recorded reason").

**Ruling.** What justifies row 3 is the **design and contract work**, and by the parity pass
that work is spent: D-M2-6 and D-M2-12 pin every public name, the record is closed, the
fixtures exist, and a reference implementation exists. That is the literal scope of
`eng-implementation` (row 2, Claude Sonnet 5) — "any multi-file code change that is not a
new subsystem or a cross-language contract" — and §4.3 tie-breaker 1 takes the lower row.

**From M2b: the pathfinder pass is row 3; every parity pass is row 2.** An Opus parity pass
needs a trigger recorded *before* dispatch — a failed Sonnet attempt, or a named defect class
the cheaper rung has already missed on this surface.

**Not applied retroactively to M2a.** The two passes were left to finish rather than killed
and re-dispatched: interrupting implementation mid-flight on an R3 surface leaves two
half-written trees, which costs more than the rung difference. The lapse is recorded instead
of being repaired, because the repair is worse than the disclosure.

**And the tempting justification is refused.** There *is* a real argument for Opus here —
the .NET pathfinder ran on Opus and still shipped four blocking defects on this surface, and
D-M2-18 names two transcription traps. But constructing that trigger *after* dispatch is the
"this feels important" reasoning `claude.md` §4 forbids. A trigger recorded afterwards is not
a trigger; it is a rationalisation, and the whole point of requiring it in writing is that it
has to precede the spend.

### D-M2-19 — The M2a clock instrument cannot drive M2c's schedule, and that is found now rather than in M2c

**Finding (Strategic Orchestrator, reading the shipped instrument).** M2a's `FixtureClock`
is a **frozen** clock: `NowUtc()` returns a fixed instant that moves only on
`AdvanceAfterExchange()`, and `Delay(duration, ct)` returns a completed task without
advancing anything. That is exactly right for M2a — `auth.token.lookup-self-remaining-ttl`
needs one known instant — and it is **not sufficient for `AUT-090`**.

The renewal loop computes its wake time as `IssuedAt + LeaseDuration × RenewAtFraction`
and awaits `Delay`. With `Delay` completing instantly and `NowUtc()` frozen, the loop wakes,
sees that its wake time has not arrived, and either spins or — if it renews anyway — asserts
nothing about the schedule `AUT-090` specifies. And `clock.advance` cannot rescue it,
because those entries are consumed **after an exchange**, whereas the first thing a renewal
fixture needs is for time to pass *before* any request exists.

**Not ruled here, deliberately.** The obvious answer is virtual time — `Delay(d)` advances
`now` by `d` and completes immediately — but that changes a **shipped** instrument that
retry and backoff fixtures already depend on, and `RES-004`'s total-timeout deadline is
computed from the same clock. It is therefore a change with a blast radius outside M2c, and
it should be decided knowing how Rust and Python implemented `Delay` in their M2a passes,
which is in flight. **Decided at the top of M2c, from all three implementations, not
guessed at now.**

**Why it is recorded now.** This is the third time an instrument has turned out to be
narrower than its record implied — after `CNF-027` being inert in .NET and `fixture.clock`
being unread everywhere. The pattern is that the gap is invisible until something tries to
use the instrument for its next purpose. Writing `auth.autorenew.schedule-and-renew` before
this is answered would have pinned semantics one of the three languages might not be able to
express, which is why those two fixtures are still unauthored: **M2c authors them once its
clock question is settled.**

### D-M2-18 — Three items carried out of M2a

Raised by the handback review and ruled here so the Rust/Python brief and the Architect
queue both have them.

1. **The exception-filter ordering in `BV-AUTH-017`'s guard is load-bearing, and neither
   Rust nor Python has exception filters.** .NET reads
   `catch (OperationCanceledException)` first, then
   `catch (Exception) when (exception is not BastionVaultException)`. Inverted, cancellation
   maps to `BV-AUTH-017` instead of `BV-TRANSPORT-005`. A naive transcription inverts it, so
   the parity brief states the order and the reason.
2. **`AUT-085` would be better expressed as an Appendix B row scoped by `PathContains`**,
   a mechanism that already exists (`MessageRecognition.cs:82`). That would delete the
   hand-written predicate entirely. It is a specification change, therefore R3 and mine —
   **Architect queue, not M2a**. The predicate as shipped is a *detected* coupling: the test
   drives the real message through the generated table, so a reworded row, a changed code or
   a deleted row all go red.
3. **M2b decides whether to distinguish a source's own coded failure from one it leaked.**
   A `BastionVaultException` from a `TokenSource` is deliberately not wrapped, so M2b's
   `Login` source keeps its recogniser codes — `AUT-003`'s replay keys on `BV-AUTHZ-001`
   specifically, and wrapping would break it outright. The residue is that a `Callback`
   leaking an unrelated coded exception surfaces with the source's internal `Path` and
   `Method`. Closing it needs a distinguished internal wrapper the guard unwraps; that is
   M2b's, not M2a's.

### D-M2-25 — M2b's three open questions, ruled before the pathfinder brief

Raised as open items in D-M2-14, D-M2-18 item 3, and the "Open" list below. Ruled here so
the M2b implementation brief does not reopen them (**TOK-008**, **CLA-008**).

1. **`IClientLogger` gains no level in M2b either.** CNF-031's carve-out — a token *may*
   be shown redacted (first 4 chars + `…`) *when debug logging is explicitly enabled* — is
   a conditional permission, not a mandate that this SDK ship debug-level logging. No M2b
   requirement (the 19 IDs in D-M2-1) has a call site that would use a `Debug` method:
   AUT-101's restriction governs the CFG-080 observer (`RequestEvent`), which already
   exists and is unrelated to `IClientLogger`. Adding a `Debug` method with nothing behind
   it is a stub, which D-M1c-25 forbids. **Ruling: `IClientLogger` stays `Warn`-only.**
   CNF-031/032 are satisfied in M2b the same way M2a satisfied them for the token — Userpass
   `password` and AppID `secret_id`/`machine_token` are held in `SecretString` (or an
   equivalent redacting type), never logged, and never appear in a default `ToString`.
   Revisit only when a later milestone's requirement text names a debug-log call site.

2. **A source's own recognised failure is distinguished from one it leaks, by an internal
   marker, not by type.** `RequestExecutor`'s `BV-AUTH-017` guard
   (`RunLoopAsync`, `Internal/RequestExecutor.cs:243`) currently passes every
   `BastionVaultException` through unwrapped, on the reasoning that a `TokenSource`'s own
   coded failure must keep its code — `AUT-003`'s re-login/replay path keys on
   `BV-AUTHZ-001`, produced by the *outer* request, so this ruling does not touch it.
   The residue: a `Callback` (or M2b's `Login`) source can leak an unrelated
   `BastionVaultException` — one raised by code *inside* the delegate that is not the
   login-response-contract recognizer itself (e.g. the delegate happens to call another
   SDK operation) — and today it surfaces with the source's internal `Path`/`Method`
   rather than the request that triggered resolution.
   **Ruling, corrected by D-M2-26 (handback review finding R3): mark by origin, not by a
   code whitelist.** The enumerated-codes phrasing originally ruled here is superseded and
   must not be transcribed into the Rust/Python brief as written — a code list cannot tell
   AUT-041's gated-login `BV-AUTHZ-001` (the source's own failure, must not trigger AUT-003
   replay) from an outer request's `BV-AUTHZ-001` (must trigger it), since both carry the
   same code. Introduce an internal marker, e.g. `internal interface IRecognizedAtSource`,
   set at the point the login itself raises or catches its own exception — i.e. by where
   the exception originates, not by which code it carries. Change the guard's condition
   from `exception is not BastionVaultException` to
   `exception is not IRecognizedAtSource`. A recognized login failure still passes through
   verbatim (AUT-010…013 unaffected, and the gated-login `BV-AUTHZ-001` case above is now
   correctly excluded from replay); any other `BastionVaultException` a source delegate
   leaks is now wrapped as `BV-AUTH-017 AuthTokenSourceFailed` with the original preserved
   as `cause`, exactly as an unrecognised exception already is — so its `Path`/`Method`
   never masquerade as the outer request's. This is an internal contract, not a public API
   change, so it does not raise the risk tier.

3. **The mock server's `login-failure-as-200` simulation (TST-021) is sourced from the
   same generated table Appendix B backs, not hand-maintained.** D-M2-4a already sharpened
   this: AUT-011's rows are now reached from a 200 body, which is exactly what
   `login-failure-as-200` produces, so the two lists overlapping stops being hypothetical
   the moment M2b's fixtures exercise it. **Ruling: yes, share the source.** Extend
   `tools/error-catalogue`'s generated intermediate to also emit the
   message-pattern → code rows AUT-011/012 need (it already parses Appendix B §1/§2; this
   is the same table, not a new one), and have each language's mock server load that list
   to parameterise `login-failure-as-200`'s `data.error` body instead of a hand-written
   string. This keeps M1c's rule — "generated, never hand-transcribed" — from being broken
   a fourth time, and means a fixture can never assert a `data.error` message the mock
   cannot produce.

### D-M2-26 — M2b handback: two D-M2-6 pins corrected, one D-M2-25 ruling amended

Ruled on the Strategic-tree Claude Opus 5 review of the .NET pathfinder pass (R3 gate,
`agents.md` §4.4). Two of the delegate's deviations from D-M2-6 are **not exceptions
granted to this delegate** — they are corrections to a pin that was wrong, so they bind the
Rust/Python pass too (**TOK-008**).

1. **`Auth.Userpass.Login`/`Auth.AppId.Login` take `RequestOptions?`, not `LoginOptions?`.**
   `LoginOptions.ReloginOnPermissionDenied` is unobservable on a one-shot login: it
   `install`s a `Static` source, so `Descriptor` is null and the re-login path can never
   fire. Shipping it there is worse than an ordinary D-M1c-25 stub — it reads as a
   security-relevant opt-in that silently does nothing. `RequestOptions` is independently
   required (AUT-041's namespace header). `TokenSource.Login`'s own `LoginOptions?`
   parameter is unchanged — re-login only ever applies there, where credentials are
   retained. D-M2-6's signature block is corrected in place above.
2. **`AuthInfo` does not gain AUT-014's `Accessor`/`EntityId`/`TokenType`/`Orphan`/`NumUses`.**
   D-M2-6's own annotation ("populated only after `LookupSelf`") was incoherent as pinned:
   `LookupSelf` returns `TokenInfo`, and nothing was ever specified to merge it into
   `AuthInfo`, so the five fields could never be populated by any code path — permanently
   null, which D-M1c-25 forbids as a stub whether or not the delegate calls it one. Adding
   `init` properties later is not a breaking change. **Follow-up, not fixed here:**
   `tools/traceability/baseline.json` already marks AUT-014 as covered, by tests that
   assert `TokenInfo.RemainingTtl`/`Ttl` (D-M2-6's *TokenInfo* ruling) rather than anything
   about `AuthInfo`. That is a pre-existing false positive on the ratchet, not M2b's to
   fix — carried to the spec-tree follow-up list (D-M2-18-style) for whoever next touches
   AUT-014.
3. **D-M2-25 item 2 is amended in place above**: marking is by origin, not by an enumerated
   code list. A parity pass transcribing the original list would mark an outer request's
   `BV-AUTHZ-001` and silently break AUT-003 for every gated login. Corrected before the
   Rust/Python brief is written, per §4.3 rule 4 — the record is written before the mistake
   ships twice, not after.
4. **Recorded, not ruled:** when an AUT-003 re-login itself fails, the caller sees the
   *second* failure's code (e.g. `BV-AUTH-004`), not the original `BV-AUTHZ-001`. AUT-003
   does not specify which; this is the .NET pathfinder's choice and Rust/Python must match
   it rather than choosing independently (D-2).

**Verification independently re-run by the reviewer, not merely quoted:** `dotnet test`
533/533, 98.9 % line / 96.02 % branch; `tools/error-catalogue/generate.py` regeneration
byte-identical on `rust/`/`python/`; `tools/traceability` 147/273/420, baseline delta exactly
the 19 IDs; `rust/`, `python/`, `specifications/`, `CHANGELOG.md`, `ROADMAP.md` untouched.
Verdict: **approve with required fixes** — the two corrections above, plus the `CHANGELOG.md`
entry landed at acceptance (REC-001, below).

### Accepted without further comment

- **`RenewSelf` with no token sends `auth/token/renew/`.** Correct for M2a: the `CFG-020`
  preflight is M2b's, so this is the absence of an unimplemented requirement, not a guess.
- **The harness fixture count moved 203 → 208 and the suite was red before the pathfinder
  started.** That was the Strategic Orchestrator's doing — five fixtures were authored to
  disk mid-flight, and a counted harness assertion is exactly the kind of thing that
  notices. Reported per **VER-003** rather than absorbed silently, which is the correct
  behaviour; the lesson is that fixtures are a tracked artefact and land in a commit, not
  on disk beside one.

### The three defects the pathfinder found by reading the path

Recorded because all three are D-M2-7's and D-M1c-25's predictions coming true, and because
the Rust and Python brief must carry them as behaviour-to-avoid:

1. **`RequestEvent.Path` carried the unredacted path**, so AUT-080's
   `auth/token/renew/{token}` and `Auth.Token.Lookup`'s `auth/token/lookup/{token}` would
   have shipped a live token to every CFG-080 observer. This is the exact leak D-M2-7 named
   when it insisted the observer join the TST-051 assertion — the instrument caught the
   defect the same milestone it was built.
2. **A cancelled resolve escaped as a bare `OperationCanceledException`**, past the in-loop
   mapping, because D-M2-9 moved resolution above the loop and made it perform I/O.
   ERR-020/TRN-054 forbid an untyped escape; now `BV-TRANSPORT-005` with `attempts = 0`.
   A consequence of this record's own ruling, found by reading it.
3. **A test was reading the developer's real `~/.vault-token`** (`UseTokenHelper = true`
   with no `TokenFile`). `BV-CONFIG-010` turned it into a hard failure because this
   machine's token file is genuinely `BVTOK1:`-prefixed. A latent test-isolation defect
   since M1a, surfaced by the feature that made it observable.

## Open

- ~~Whether the generated recognition list should be shared with the mock server's
  simulation list (TST-021)~~ **Decided at D-M2-25 item 3: yes, shared.**
- The `(policy write)` qualifier on one Appendix B §2 row is advisory and unenforced until
  M4 (D-M1c-14 item 3). Unchanged by M2.
