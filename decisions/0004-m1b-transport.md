# DR-0004 — M1b transport, logical layer, retry and the status→code seam

**Status:** Accepted · **Date:** 2026-09-13 · **Milestone:** M1b ([`ROADMAP.md`](../ROADMAP.md) §5)
**Author:** Strategic Orchestrator (Claude Opus 5) · **Risk tier:** R3 · **Size tier:** Enterprise
**Opus trigger:** M1 is R3 and this slice fixes the wire contract, the TLS stack and the
redirect posture ([`ROADMAP.md`](../ROADMAP.md) §5, M1 note; [`claude.md`](../claude.md) §4).
**Amends:** [DR-0003](0003-m1a-configuration.md) D-M1a-6's transport seam (see D-M1b-1).
**Requirements in scope:** `TRN-001…003`, `TRN-010…011`, `TRN-012…017`, `TRN-020…021`,
`TRN-023`, `TRN-030`, `TRN-032…033`, `TRN-040…043`, `TRN-050…054`, `TRN-060`, `TRN-090…092`,
`TRN-100`, `CFG-044`, `CFG-051…055`, `CFG-070`, `CFG-071`, `CFG-080`, `CFG-081`,
`OVR-002`, `OVR-003`, `OVR-005`, `OVR-006`, `RES-001…004`, `CNF-034`

These decisions are made. No delegate reopens them (TOK-008). A delegate that believes a
decision here is wrong escalates ([`skills/codex/SKILLS.md`](../skills/codex/SKILLS.md) §3
rows 9–10); it does not choose differently.

## Context

M1b is where the SDK first opens a socket. Three things had to be settled before code:

1. **The M1a transport seam is not one seam — it is three.** Rust shipped a marker trait
   with no method at all; Python a synchronous `request(method, url, headers, body) -> Any`;
   .NET an async `SendAsync(TransportRequest, CancellationToken)`. DR-0003's Consequences
   said M1b "inherits a transport trait it must not redesign"; that premise is false, and
   the correction is an amendment recorded here rather than three delegates each inventing
   a fourth shape.
2. **Every transport fixture asserts an error *code*, and the error model is M1c.** The
   same forward reference M1a hit with `BV-CONFIG-*`, one layer out.
3. **`CFG-051…055` describe retry as *behaviour*, but `RetryPolicy` shipped without its
   `RetryOn` field in all three languages.** The retry classifier has no input until that
   is fixed.

Following the D-M1a-20 note, this record pins the canonical name of every public member it
asks for, not only the behaviour.

## Decisions

### D-M1b-1 — The transport seam is redesigned once, here, to one canonical shape

**Problem.** Rust's `pub trait Transport: Debug + Send + Sync {}`
(`rust/…/src/transport.rs:17`) declares no method. Python's is synchronous and returns
`Any`. .NET's is async over a request/response record pair. A fixture driver cannot script
one behaviour against three seams, and `transport.retry.connection-refused-then-ok`
requires a *transport-level failure* that none of the three can express.

**Decision.** One shape, in every language:

```
TransportRequest  { Method, Url, Headers, Body?, Timeout, ConnectTimeout }
TransportResponse { StatusCode, Headers, Body }
Transport.Send(TransportRequest, cancellation) -> TransportResponse     // asynchronous
Transport.SupportsCustomVerbs -> bool                                   // default true (D-M1b-14)
```

- `Method` is the literal HTTP method string, including `LIST` (TRN-010).
- `Url` is the fully-constructed absolute URL, already encoded (TRN-020/021). Each
  language may hold it in its own URL type (`Uri`, `url::Url`, `str`), but a
  `FakeTransport` records it as the exact absolute URL **string** so fixture comparison is
  language-independent.
- `Body` is bytes, not a parsed object. Serialization happens above the transport, so the
  transport has no opinion about JSON.
- `Timeout` is the **per-attempt** budget (RES-004) and is the transport's to enforce.
- **A transport-level failure is raised as the SDK error type carrying
  `BV-TRANSPORT-001/002/003/005`, never as a raw runtime exception.** This is what lets
  the retry classifier read a code instead of matching on exception types, and what lets a
  fake transport script the fixture keyword `fail: connection_refused` without inventing a
  language-specific exception. Converting *every* failure mode of the underlying stack into
  one of those four codes is a MUST — the same obligation DR-0003 imposed on the
  file-readability check, in a place where the failure modes are far more numerous.

**Rejected — keep .NET's shape and bend the other two to it.** It is the closest of the
three, but it has no per-attempt timeout and no custom-verb capability, both of which are
requirements here. Preserving two thirds of a shape that is wrong in the same two places is
not a saving.

**Rejected — a byte-stream response for large bodies.** TRN-033 caps responses at
`MaxResponseBytes`; buffering to that cap is what enforces it. Streaming is a real need for
plugin asset downloads and recording chunks, and it is deferred to the milestone that has
one (M10), where the requirement can name it.

### D-M1b-2 — The SDK is asynchronous-only at M1b

OVR-003 requires the idiomatic asynchronous form and makes a synchronous form optional.
**Decision.** M1b ships async only: `Task`-returning in .NET, `async fn` on tokio in Rust,
`async def` in Python. No synchronous facade.

A facade is purely additive and can land in any later milestone without a breaking change
(CNF-042), whereas shipping it now doubles the public surface, the test matrix and the
cancellation semantics (OVR-006) in the milestone that is already the project's largest.
CLA-007 decides it.

**Consequence, stated so it is not discovered by a user:** a Python or Rust caller needs an
event loop / runtime. Each README says so at M1b.

### D-M1b-3 — The production HTTP stack, per language

| | Stack | Why this one |
|---|---|---|
| .NET | `SocketsHttpHandler` + `HttpClient`, `new HttpMethod("LIST")` | In-box; `SslClientAuthenticationOptions` gives per-connection control of CFG-040/041/042/044 |
| Rust | `hyper` + `hyper-util` legacy client + `rustls` (via `hyper-rustls`) + `tokio` | `rustls`, `tokio` and `hyper` are already pinned dev-dependencies of this crate; promoting them adds almost nothing to the CNF-024 audit surface, and building the `rustls::ClientConfig` by hand is the only way to satisfy CFG-040's *add* (not replace) semantics and CFG-042's SNI override |
| Python | `httpx.AsyncClient` | Custom verbs via `request("LIST", …)`, an injectable `ssl.SSLContext` (CFG-040/041/042/044), `trust_env=False` for TRN-091, pooling for TRN-090, and `follow_redirects=False` by default for TRN-060 |

**Rejected for Rust — `reqwest`.** It would be far less code, and that is its whole case.
Against it: a large transitive tree against a `cargo audit` gate, defaults (redirect
following, proxy from environment) that TRN-060 and TRN-091 require us to switch off
anyway, and no clean way to override SNI independently of the URL host, which CFG-042
requires. Trading audit surface and a requirement we cannot meet for less code is the wrong
trade in the milestone that fixes the security posture.

**Rejected for Python — `aiohttp`, `requests`, stdlib `http.client`.** `requests` and
`http.client` are synchronous, which D-M1b-2 rules out as the only form. `aiohttp` is
viable; `httpx` wins on `SSLContext` injection being the documented path rather than a
per-request keyword, and on `trust_env=False` being a constructor flag.

**Note for CNF-024 and `ROADMAP.md`:** this is the second dependency-surface divergence
(after Python's `cryptography`). All three languages now carry a TLS/HTTP dependency; Rust
and Python carry new ones. The dependency audit gate is the control, and it already runs.

### D-M1b-4 — The status→code mapping function lands whole; only status-derived codes are populated

**Problem.** Every fixture in `specifications/fixtures/transport/` asserts `expect.error.code`.
`ERR-020` requires the mapping to live in **one** function in the logical layer. The
message-recognition table (Appendix B, ERR-036/037) is M1c.

**Decision.** M1b writes that one function, in its final home and with its final signature,
and populates the branches that section 03's status table decides **by status alone, or by
a discriminator section 03 itself names** (empty body, `Retry-After` present, sealed body).
The populated set is exactly:

```
BV-TRANSPORT-001  BV-TRANSPORT-002  BV-TRANSPORT-003  BV-TRANSPORT-004  BV-TRANSPORT-005
BV-PROTOCOL-001   BV-PROTOCOL-002   BV-PROTOCOL-003
BV-NOTFOUND-001   BV-RATE-001       BV-RATE-002
BV-SERVER-001     BV-SERVER-002     BV-SERVER-005
BV-AUTH-002       BV-AUTHZ-001
BV-INPUT-006      BV-INPUT-007      BV-INPUT-008
BV-CONFLICT-002   BV-CONFLICT-003   BV-QUOTA-001      BV-CONFIG-009
```

Messages and hints are transcribed from [Appendix B](../specifications/appendix-b-error-catalogue.md)
for these rows only. M1c adds step-by-step message recognition, the remaining rows, and the
ERR-036/037 exhaustiveness assertion; **M1c does not reshape the function or the error type.**

`Retryable` per row follows ERR-006, not the mapper's opinion.

**Rejected — return a placeholder code and map properly at M1c.** The fixtures assert the
final codes now; a placeholder fails them now and teaches nothing.

### D-M1b-4a — The six fixture failure kinds map to fixed codes

`specifications/fixtures/schema/fixture.schema.json` lets an exchange fail in six ways. The
mapping is pinned here rather than left to each language, because it is the only thing
standing between `fail: "dns"` and three different codes:

| `fail` | Code |
|---|---|
| `connection_refused`, `dns`, `reset` | `BV-TRANSPORT-001` |
| `timeout` | `BV-TRANSPORT-002` |
| `tls_verify`, `tls_handshake` | `BV-TRANSPORT-003` |

The production transport classifies its own stack's failures into the same three codes,
plus `BV-TRANSPORT-005` for caller cancellation (OVR-006) and `BV-TRANSPORT-004` for
`MaxResponseBytes` (TRN-033).

### D-M1b-4b — `Retryable` is a reported property; `RetryOn` is what the SDK actually retries

These are two different things and conflating them is the likeliest defect in this
milestone. `transport.retry.write-not-retried` is the proof: it asserts
`"retryable": true` **and** `"attempts": 1` on the same error. The error truthfully reports
that this class of failure is retryable in general; the SDK did not retry it, because the
operation was a `Logical.Write` and `RetryIdempotentOnly` is true.

- `Error.Retryable` follows **ERR-006** exactly: true only for `BV-TRANSPORT-001/002/003`,
  `BV-SERVER-002`, `BV-SERVER-003`, `BV-RATE-002`, `BV-DISCOVERY-003`.
- `RetryPolicy.RetryOn` follows **CFG-050** exactly, and is a strictly smaller set. Notably
  `BV-TRANSPORT-003` and `BV-RATE-002` are `Retryable = true` and are **not** auto-retried.

An implementation that derives one from the other is wrong even when its fixtures pass.

### D-M1b-4c — `Error.Details` keys are spelled as Appendix B spells them

`transport.envelope.non-json` asserts `detailsKeys: ["snippet"]`, so TRN-053's body excerpt
is `Details.snippet` — not `body`, `excerpt` or `content`. Likewise `Details.path` for
`BV-NOTFOUND-001`/`BV-AUTHZ-001` and `Details.setting` for `BV-CONFIG-003` (D-M1a-18).
Where Appendix B's hint column names a `Details` key, that spelling is normative.

### D-M1b-5 — Error-body parsing is total (TRN-052) and `Retry-After` is read before the body (TRN-051)

All three shapes are parsed: `{"error": "…"}`, `{"errors": [ … ]}` (joined with `"; "` for
`Message`, array kept in `Error.ServerErrors`), and empty body (`HTTP <status> (no body)`).
A non-JSON content type on a JSON endpoint is `BV-PROTOCOL-002` carrying the first 256
bytes, sanitised, in `Error.Details` — **never** a JSON parse exception escaping (TRN-053).
Every error carries `StatusCode`, `Method`, `Path` (namespace-qualified) and `Code` (TRN-054),
and `Attempts` (CFG-055).

### D-M1b-6 — Idempotency is a table, not a per-call-site judgement (CFG-051)

| Operation | Idempotent |
|---|---|
| `Logical.Read`, `Logical.List` | yes |
| `Logical.Write`, `Logical.Delete` | no |
| `Logical.Raw` | by method: `GET`, `HEAD`, `OPTIONS`, `LIST` yes; everything else no |

`RequestOptions.Idempotent` becomes **optional** (tri-state) in every language and, when
set, wins in **both** directions — a caller may mark a write retryable and may mark a read
not-retryable. Python's M1a `idempotent: bool = False` is a non-optional field and must
change; .NET's `bool Idempotent` likewise. `RetryPolicy.RetryIdempotentOnly = false` makes
every operation eligible regardless of the table.

### D-M1b-7 — `RetryPolicy` gains its missing `RetryOn` field; the clock and the jitter source are injected seams

**Defect, all three languages.** CFG-050's block lists seven fields and a `RetryOn` list.
All three M1a `RetryPolicy` types shipped the seven and dropped `RetryOn`. This is the same
class as D-M1a-21: a field that is configurable in the specification and unreachable in
every SDK. `RetryOn` is added, defaulting to
`[BV-TRANSPORT-001, BV-TRANSPORT-002, BV-SERVER-002, BV-SERVER-003]`.

**The retry loop:**

- Eligible iff the mapped code ∈ `RetryOn` **and** the operation is idempotent per D-M1b-6
  (unless `RetryIdempotentOnly` is false).
- `BV-SERVER-001` (sealed, CFG-053) and `BV-RATE-001` (abuse guard, CFG-052) are **never**
  retried, even if a caller puts them in `RetryOn`. These two are hard exclusions, not
  defaults — CFG-052 and CFG-053 are prohibitions on the SDK, not preferences.
- Backoff is `min(MaxBackoff, InitialBackoff × Multiplier^(attempt−1))` with ±`Jitter`
  uniform randomisation (RES-003).
- With `Retry-After` present and `RespectRetryAfter` true, the wait is
  `min(max(RetryAfter, backoff), MaxBackoff × 6)` (CFG-054).
- Total attempts ≤ `MaxAttempts` at M1b. RES-001's `MaxAttempts + 1` allowance is the
  single failover replay, and failover does not exist until M5.
- `Error.Attempts` is the number of attempts actually made (CFG-055).

**Seams.** RES-003 requires the random source to be injectable. Jitter and time are not
user configuration and do not belong on `RetryPolicy`; they are two injected seams beside
`Transport`:

| Canonical | Shape |
|---|---|
| `Clock` | `Now()` → instant; `Delay(duration, cancellation)` → async |
| `JitterSource` | `NextDouble()` → `[0.0, 1.0)` |

Defaults are the system clock and the system RNG. Every backoff and every pause wait goes
through `Clock.Delay`, so **no test in any language sleeps in real time** — this is the
mechanism, and a test that calls a real sleep is a review finding.

### D-M1b-8 — The observability hook, and where `request_id` comes from

**Decision.** Canonical name `RequestObserver`, one method
`OnRequestCompleted(RequestEvent)`, settable on the options object as `Observer`.

`RequestEvent { Method, Path, Namespace, StatusCode?, Duration, RequestId, Attempt, ErrorCode? }`
— and nothing else. No body, no token, no headers (CFG-080 is explicit, and CNF-031/032
make it a security requirement rather than a style one). It fires **once per attempt**
(RES-002), not once per logical operation.

**`RequestId` is generated by the SDK**, is opaque, and is stable across the attempts of one
logical operation. TRN-042 forbids fabricating `request_id` **on the `Response`**, because
that would claim the server sent one; CFG-080 requires a correlation id **on the hook**,
where no such claim is made. Both are satisfied: the id exists on the event and never on
the `Response`.

CFG-081's documented metric names: `bastionvault.client.request.duration`,
`bastionvault.client.request.retries`, `bastionvault.client.request.errors{code}`. Named in
each README at M1b, since CFG-081 says "MUST document them".

### D-M1b-9 — Runtime mutation: a shared token cell, and views that share it (CFG-070/071)

`Client.SetToken` / `Client.ClearToken` write a thread-safe cell. Each logical operation
**snapshots** the token once at its start and uses that snapshot for all of its attempts —
this is CFG-070's "in-flight requests keep the token they started with", and it is also why
the snapshot happens outside the retry loop.

`Client.WithNamespace(ns)` returns a lightweight view sharing the transport, the config and
**the same token cell** — so `SetToken` on the parent is visible to the view (CFG-071) —
differing only in namespace. The view type is the `Client` type itself in every language;
a separate `ClientView` type would double every operation signature for one differing field.

### D-M1b-10 — `Response` gains no `NotModified` field; `304` is `StatusCode == 304` with absent `Data`

TRN-040…043 fix the `Response` shape, and a `304` is specified as "a `NotModified` result
(not an error)". **Decision.** That result is a `Response` with `StatusCode == 304` and
`Data` absent. No field is added to the canonical type to express a state its
`StatusCode` already expresses, and TRN-042 forbids fabricating fields.

Shape detection is TRN-040 exactly: Shape A when the body is an object with a `data` key
**or** an `auth` object containing `client_token`; otherwise the whole body is `Data`.
`lease_id == ""` is absent (TRN-041). The exact parsed body is retained in `Raw` (TRN-043).

### D-M1b-11 — `404` returns null only for `Read` and `List`, and only with an empty body

TRN-050. `Logical.Read` and `Logical.List` return null/`None`/`Option::None` on a `404`
whose body is empty. A `404` with a body is an error mapped by message (M1c) and defaults to
`BV-NOTFOUND-001` at M1b. `Logical.Write`, `Logical.Delete` and `Logical.Raw` always raise
on `404`. The `Kv.ReadSecret`-optional / `Kv.GetSecret`-raising convention is M4's to apply;
M1b only fixes the logical layer's half.

### D-M1b-12 — `Logical.Raw` maps errors but does not parse envelopes

`Logical.Raw` takes an **absolute** path, adds no prefix (TRN-003), and returns
`RawResponse { StatusCode, Headers, Body }` with the body unparsed. Error statuses map
through the same D-M1b-4 function — `transport.status.405-empty` is a `Logical.Raw` fixture
expecting `BV-PROTOCOL-001`, so "raw" means *no envelope*, not *no error model*.

### D-M1b-13 — Specification clarifications made here (claude.md §1.1)

Three settings and two per-call options are named in section 03 or section 13 and absent
from section 02's tables. They are added, in the same way `CaCertReplacesSystemRoots` was at
M1a:

| Added to | Name | Type | Default | Env |
|---|---|---|---|---|
| settings table | `MaxResponseBytes` | integer bytes | `134217728` (128 MiB) | — |
| settings table | `UseSystemProxy` | bool | `false` | — |
| `RetryPolicy` | `RetryOn` | list of code strings | see D-M1b-7 | — |
| `RequestOptions` | `ApiVersion` | `v1` \| `v2` \| unset | unset | — |
| `RequestOptions` | `TotalTimeout` | duration | unset | — |

`ApiVersion` is required by TRN-002's own text ("the per-call `ApiVersion` option") and
`TotalTimeout` by RES-004's; neither appears in the option table those requirements point
at. `TotalTimeout` bounds attempts **plus** backoff together; `Timeout` bounds each attempt.

### D-M1b-14 — TRN-010's `BV-CONFIG-009` is proved through a transport capability flag

All three stacks in D-M1b-3 send a custom verb, so the failure TRN-010 mandates can never
fire in production — and an untestable MUST is an uncovered MUST.

**Decision.** The seam carries `SupportsCustomVerbs`, default `true`. Client construction
asserts it and raises `BV-CONFIG-009` when false. The failure path is proved by a fake
transport that declares `false`. One boolean buys a tested requirement; the alternative is
leaving TRN-010 baselined until a runtime appears that cannot do it, which is never.

### D-M1b-15 — `FakeTransport` is public test-support surface (TRN-100)

TRN-100 requires the `FakeTransport` to ship "in test utilities or a test-support package",
and it must script: custom `LIST`, `204` without body, `404` with empty body, `429` with
`Retry-After`, and transport failures (connection refused, timeout, TLS error). It records
`method, url, headers, body` per request. It is the same object the fixture driver drives,
not a second implementation beside it — a fixture that passes against a private shim is the
failure mode D-M0-2 and D-M1a-6 both rejected.

**Consequence:** `FakeTransport` is in the public-API baseline of each language (as
test-support surface), and CNF-027 reviews it like any other public type.

### D-M1b-16 — The rate gate's *pause* lands; the token bucket does not, and neither `EFF-*` ID leaves the baseline

`transport.status.429-dos-guard` asserts `clientState: { "RateGate.Paused": true }`, and a
fixture that satisfies four of five assertions is failing.

**Decision.** M1b ships the pause half of EFF-003/EFF-004: on a `429`, pause for
`min(Retry-After, 30s)`, or `1s` when no `Retry-After` is present, and expose `Paused` and
`PausedUntil` (part of EFF-006). The request that received the `429` fails with
`BV-RATE-001` and is not replayed. The token bucket, FIFO queueing, `AvailableTokens` and
the probe exemption (EFF-001, EFF-002, EFF-005) are M8.

**No `EFF-*` ID leaves `baseline.json` at M1b.** EFF-003 has a "drop accumulated tokens"
MUST and EFF-006 an `AvailableTokens` MUST, and by D-M1a-10 partial coverage is not
coverage. M1b writes the behaviour the fixture needs and claims none of the credit; M8
claims it when the bucket exists.

### D-M1b-17 — Scope exclusions, each with a named owning milestone

| Deferred | To | Because |
|---|---|---|
| `TRN-022` | M4 | Its MUST is about the *typed* surface sending selectors as query parameters |
| `TRN-031` | M4 | Duration conversion is a typed-operation obligation |
| `TRN-070` | M1c | `BV-SERVER-006` is reached by message recognition |
| `TRN-071`, `TRN-072` | M3 | Nothing is prefix-pinned until typed `sys` operations exist |
| `TRN-080`, `TRN-081` | M3 | `Sys.ServerInfo` is the `sys` surface |
| `OVR-007`, `OVR-008`, `OVR-009` | M4 | Unchanged from DR-0003 |
| `CFG-020`, `CFG-031`, `CFG-032` | M2 | Unchanged from DR-0003 |
| `CFG-043` | M5 | Unchanged from DR-0003 |
| `EFF-001…006` | M8 | D-M1b-16 |
| `RES-010`, `RES-011`, `RES-020`, `RES-021`, `RES-030` | M5+ | Discovery |

### D-M1b-18 — Baseline removals at M1b exit

An ID leaves `tools/traceability/baseline.json` only when **every** MUST in it is tested
(D-M1a-10). Applied to M1b, the removals are exactly:

```
TRN-001 TRN-002 TRN-003
TRN-010 TRN-011
TRN-012 TRN-013 TRN-014 TRN-015 TRN-016 TRN-017
TRN-020 TRN-021 TRN-023
TRN-030 TRN-032 TRN-033
TRN-040 TRN-041 TRN-042 TRN-043
TRN-050 TRN-051 TRN-052 TRN-053 TRN-054
TRN-060 TRN-090 TRN-091 TRN-092 TRN-100
CFG-044 CFG-051 CFG-052 CFG-053 CFG-054 CFG-055
CFG-070 CFG-071 CFG-080 CFG-081
OVR-002 OVR-003 OVR-005 OVR-006
RES-001 RES-002 RES-003 RES-004
CNF-034
```

**50 IDs**, every one of them verified present in `tools/traceability/baseline.json` before
this record was issued. A delegate that cannot honestly remove one of these reports it rather than
removing it; the ratchet moves one way only (D-M0-1) and a false removal is worse than a
deferral.

## Public API shape

Extends the DR-0003 table. Every name below is fixed here (OVR-008, CNF-027, and the
D-M1a-20 note); a delegate that wants a different one escalates.

| Canonical | .NET | Rust | Python |
|-----------|------|------|--------|
| logical grouping | `client.Logical` | `client.logical()` | `client.logical` |
| `Logical.Read` | `ReadAsync(path, options?, ct)` | `read(path, options) -> Result<Option<Response>>` | `await read(path, options=None)` |
| `Logical.Write` | `WriteAsync(path, body?, options?, ct)` | `write(path, body, options)` | `await write(path, body=None, options=None)` |
| `Logical.Delete` | `DeleteAsync(path, body?, options?, ct)` | `delete(path, body, options)` | `await delete(path, body=None, options=None)` |
| `Logical.List` | `ListAsync(path, options?, ct)` | `list(path, options)` | `await list(path, options=None)` |
| `Logical.Raw` | `RawAsync(method, absolutePath, body?, options?, ct)` | `raw(method, absolute_path, body, options)` | `await raw(method, absolute_path, body=None, options=None)` |
| `Response` | `Response` | `Response` | `Response` |
| `RawResponse` | `RawResponse` | `RawResponse` | `RawResponse` |
| `AuthInfo` | `AuthInfo` | `AuthInfo` | `AuthInfo` |
| `TransportRequest` | `TransportRequest` | `TransportRequest` | `TransportRequest` |
| `TransportResponse` | `TransportResponse` | `TransportResponse` | `TransportResponse` |
| `FakeTransport` | `FakeTransport` | `FakeTransport` | `FakeTransport` |
| `Clock` | `IClock` | `trait Clock` | `Clock` (`Protocol`) |
| `JitterSource` | `IJitterSource` | `trait JitterSource` | `JitterSource` (`Protocol`) |
| `RequestObserver` | `IRequestObserver` | `trait RequestObserver` | `RequestObserver` (`Protocol`) |
| `RequestEvent` | `RequestEvent` | `RequestEvent` | `RequestEvent` |
| `RateGate` state | `RateGateState` | `RateGateState` | `RateGateState` |
| `Client.SetToken` | `SetToken(SecretString)` | `set_token(SecretString)` | `set_token(SecretString)` |
| `Client.ClearToken` | `ClearToken()` | `clear_token()` | `clear_token()` |
| `Client.WithNamespace` | `WithNamespace(string)` | `with_namespace(&str)` | `with_namespace(str)` |

Wire field names are never altered (OVR-007): `lease_id`, `lease_duration`, `client_token`.

## Consequences

- Rust and Python both gain a production TLS/HTTP dependency. `cargo audit` and `pip-audit`
  are the control (CNF-024) and already gate CI.
- The three public-API baselines change substantially. Expected; CNF-027 checks that the
  change is reviewed, not that it is absent.
- `RequestOptions.Idempotent` changes type in .NET and Python (bool → optional bool). This
  is a public-API change inside one milestone of the type landing, which CNF-042's
  deprecation obligation does not reach (nothing is shipped), and it is the last moment it
  is free.
- M1c inherits a mapping function it must not reshape — only extend with message
  recognition and the remaining Appendix B rows. If M1c finds the signature wrong, that is
  an escalation and an amendment here, not a quiet change. (DR-0003 made the same promise
  about the transport seam and it did not hold; the difference this time is that the
  function has a single caller and a fixture suite asserting its output.)

## Open

Nothing blocking. `MaxResponseBytes` and `UseSystemProxy` (D-M1b-13) are clarifications to
section 02's settings table, made under `claude.md` §1.1, and section 02 is updated as part
of this milestone.

---

## Addendum — .NET pathfinder pass (2026-09-13)

Recorded under roadmap D-2: the .NET pass exists to surface gaps in the design cheaply,
before Rust and Python hit the same gap. These are binding on the Rust and Python slices.

### D-M1b-19 — The .NET public-API gate (CNF-027) has never been enforced

**Found in review, reproduced independently, not taken from the delegate's report.** A
brand-new public type absent from `PublicAPI.Unshipped.txt` compiles with **0 warnings and
0 errors**:

```
public sealed class ReviewProbeType { private readonly int _v = 1; public int ProbeValue => _v; }
→ 0 Warning(s) / 0 Error(s)
```

Root cause, verbatim from `dotnet build -v:diag`:

```
Microsoft.CodeAnalysis.NetAnalyzers.props(21,5): Property reassignment:
$(CodeAnalysisRuleIds)="CA1000;CA1001;…"
(previous value: "RS0016;RS0017;RS0022;RS0024;RS0025;RS0026;RS0027;…")
```

`AnalysisLevel=latest-all` imports `Microsoft.CodeAnalysis.NetAnalyzers.props`, which
**reassigns** `$(CodeAnalysisRuleIds)`, discarding the `RS00xx` set that
`PublicApiAnalyzers.props` had put there. `NetAnalyzers.targets` additionally overrides
PublicApiAnalyzers' own `_CodeAnalysisTreatWarningsAsErrors` target. The analyzer assembly
*is* loaded and the two `PublicAPI.*.txt` files *are* passed as `AdditionalFiles`; it simply
produces no diagnostics. Setting `dotnet_diagnostic.RS0016.severity = error` in
`dotnet/.editorconfig` does **not** revive it — tried and reverted during review.

**Why this matters beyond .NET.** `.github/workflows/dotnet.yml:33` states that CNF-027 is
"enforced as compiler diagnostics inside a single `dotnet build`". That comment is false and
has been false since M0. Rust (`cargo public-api` diff) and Python (`tests/test_api_surface.py`)
have real, executing checks; .NET alone relied on an analyzer that was silently off. **M1a's
exit record therefore certified a .NET public-API baseline that nothing had checked.**

**Decision.** The gate is repaired as part of M1b, and repaired the way M0 requires every
gate to be proven (DR-0001, "M0 is done when a seeded violation of each gate makes CI
fail"): by seeding a violation and showing CI fails. If the analyzer cannot be revived under
`AnalysisLevel=latest-all`, .NET adopts the mechanism the other two languages already use —
a CI step that diffs the emitted public surface against the committed baseline — rather than
keeping a gate whose enforcement depends on MSBuild property ordering.

**This was my gap, not the delegate's.** The delegate found it, reported it, and correctly
declined to certify a baseline it could not mechanically verify. Three milestones of
"CNF-027 green" rested on a check nobody had ever seeded a violation against — which is the
one thing DR-0001 said every gate must have.

### D-M1b-20 — `MaxResponseBytes` must bound the read, not audit it afterwards

**Defect.** The pathfinder enforces TRN-033 by testing `response.Body.Length >
MaxResponseBytes` in `RequestExecutor` — *after* `HttpClientTransport` has already called
`ReadAsByteArrayAsync` with no bound. `MaxResponseBytes` never reaches the transport at all.
The resulting error code is correct and the protection is absent: a malfunctioning or
hostile server still forces an unbounded allocation, which is the entire reason TRN-033
exists. TRN-033 says such a response "MUST be **aborted**".

**Decision.** `MaxResponseBytes` is carried on `TransportRequest` and enforced **by the
transport, while reading**, by reading the response stream with a bound and aborting as soon
as the bound is passed. A `Content-Length` already over the bound is rejected without
reading a byte. The post-hoc length check may remain as a cheap backstop; it is not the
mechanism.

TRN-033 stays on the D-M1b-18 removal list only once this holds. The pathfinder's
`Raw_response_over_MaxResponseBytes_is_aborted` test proves the code, not the abort, and
must be extended to prove that the full body was never read.

**The general rule this instance belongs to:** the milestone that removes a requirement from
the baseline asserts every MUST in it (D-M1a-10). "Aborted" is a MUST about *when*, and a
test that only inspects the resulting error cannot see *when*.

### D-M1b-21 — An unmapped status is a `BastionVaultException`, never a generic exception

**Defect, and the most serious of the pass.** `StatusCodeMapper.ResolveCode`'s default arm
throws `NotSupportedException`. `400` — "invalid request, missing token, CAS mismatch,
not-initialised", the most common error status in section 03's own table — has no branch and
therefore reaches it. A passing test,
`Unmapped_status_throws_not_supported_since_message_recognition_is_m1c`, **asserts** this
behaviour, so the defect is enshrined rather than merely present.

Every error MUST carry `StatusCode`, `Method`, `Path` and a mapped `Code` (TRN-054), and the
error model exists so that callers never catch a runtime exception type. An SDK that throws
`NotSupportedException` on a `400` fails both.

**Decision.** `ResolveCode` has no throwing arm. Unmapped statuses fall back by class:

| Status class | Fallback code |
|---|---|
| `4xx` with no branch (including `400`) | `BV-INPUT-001` |
| `5xx` with no branch | `BV-SERVER-005` (section 03's own stated default for `500`) |
| anything else unmapped | `BV-PROTOCOL-002` |

The `ServerMessage` is carried through unchanged, so M1c's message recognition refines a
correct-but-coarse code into a precise one rather than replacing a crash. The test that
asserts `NotSupportedException` is replaced by one asserting the fallback — a correction of
the assertion, not a weakening of it (ENG-001 is not in tension: the test was asserting the
wrong behaviour).

### D-M1b-22 — The rate-gate pause fires on any `429`, not only `BV-RATE-001`

The pathfinder pauses the gate only for `BV-RATE-001`. EFF-003 pauses on "a `429` response
carrying `Retry-After`" and EFF-004 on "a `429` without `Retry-After`" — neither is written
in terms of a code, and `BV-RATE-002` is a `429`.

**Decision, pinned for all three languages** so they cannot diverge on a behaviour no
fixture asserts: the pause is driven by **status `429`**, for `min(Retry-After, 30s)` when
the header is present and `1s` when it is not. The retry decision is unaffected and stays
code-driven (`BV-RATE-001` is never retried; `BV-RATE-002` is simply not in `RetryOn`).

This is a behaviour with no fixture, in a milestone that removes no `EFF-*` ID — exactly the
kind of silent three-way divergence D-M1a-21 was written about, which is why it is pinned
here rather than left to each slice.

### D-M1b-23 — Accepted pathfinder decisions, binding on all three languages

| Decision | Binding form |
|----------|--------------|
| `TotalTimeout` enforcement | A deadline computed once per logical operation (`Clock.Now() + TotalTimeout`), gating retry eligibility **and** clamping the backoff wait |
| Non-JSON parse failure | Maps to `BV-PROTOCOL-002` unconditionally, on **any** status, so no parse exception can escape (TRN-053) |
| `429` discrimination | `Retry-After` present → `BV-RATE-001`; absent → `BV-RATE-002`. Matches both fixtures and section 03's table |
| `503` discrimination | `ServerMessage` containing `sealed` (case-insensitive) → `BV-SERVER-001`, else `BV-SERVER-002` |
| Header order | `HttpCompletionOption.ResponseHeadersRead` or equivalent, so `Retry-After` is read before the body (TRN-051) |
| CFG-081 documentation | Root `README.md` until M4 authors the per-language READMEs (D-M1a-22) |
| mTLS on Windows | A PEM-loaded client certificate has an ephemeral key that SChannel refuses; reload via PKCS#12 export. Language-specific, recorded so Rust/Python recognise the symptom |
| `409` discrimination | Best-effort and **unexercised by any fixture**; explicitly deferred to M1c. All three languages leave it best-effort rather than inventing three different heuristics |

### D-M1b-25 — Rust's production transport trusts nothing and pools nothing (blocking)

**Found in review by reading `src/transport_http.rs`, after the delegate disclosed both in its
handback.** Credit where it is due: the delegate reported these honestly rather than burying
them (CLA-005). But it then reported the slice complete and stated it had "verified
independently that the Rust implementation genuinely satisfies every MUST for all 50"
baseline IDs — a statement its own next paragraph contradicts.

**1. The root certificate store is empty.** `RootCertStore::empty()` is populated *only*
from a configured `CaCertPath`/`CaCertPem`. No platform trust store is consulted, because no
native-roots crate was pinned. CFG-040 says configured CA material is **added to** the
platform trust store, not substituted for it — and DR-0003 D-M1a-14 explicitly deferred
building the real TLS stack from M1a to M1b, so M1b is where CFG-040 becomes true or does
not.

This is not only a conformance gap. With no `CaCert*` configured — the default — the Rust
SDK's root store is empty and **every** HTTPS connection fails certificate verification.
The Rust SDK cannot talk to any ordinary BastionVault deployment. No fixture caught it
because fixtures run through `FakeTransport` and never build a TLS stack.

CFG-040, CFG-041 and CFG-042 left the traceability baseline at **M1a**. They are recorded as
covered in all three languages today, and in Rust one of them is not implemented at all.

**2. Connection reuse does not exist.** `TcpStream::connect` runs per request; there is no
pool. TRN-090 ("the transport MUST reuse connections") is on this milestone's own
D-M1b-18 removal list. The handback's claim that TRN-090 is "fully proven via `FakeTransport`"
is not tenable: a fake transport cannot demonstrate connection reuse, because it opens no
connections. This is the D-M1a-10 failure mode — an ID leaving the baseline while a MUST
inside it is untested and, here, unimplemented.

D-M1b-3 named "`hyper` + **`hyper-util` legacy client**" precisely because that client *is*
the pool. Substituting a raw `TcpStream` per request is a departure from a binding decision,
and the route for that was escalation (`skills/codex/SKILLS.md` §3 rows 9–10), not a
quiet substitution plus a disclosure note.

**Decision.** Both are fixed before M1b exits.

- Pooling uses `hyper_util::client::legacy::Client`, as D-M1b-3 already specified.
- A native root store is pinned (`rustls-native-certs` or equivalent) and loaded, with
  configured CA material **added** to it; `CaCertReplacesSystemRoots = true` is the only
  path that yields a store without the platform roots.
- Adding that crate is **within** D-M1b-3's authorisation, not a new dependency decision: the
  record chose the hyper/rustls stack expressly so CFG-040's add-not-replace semantics could
  be met, and a stack that cannot meet them was not what was chosen.

**Parity note (CLA-003).** .NET satisfies CFG-040 through a validation callback that trusts
"system-trusted **or** the extra CA validates", and pools by default through
`SocketsHttpHandler`. Python's `ssl.create_default_context()` loads platform roots before
`load_verify_locations` adds to them, and `httpx` pools. Rust was the only divergence, in
both cases — and in both cases the divergence is invisible to every instrument this project
has, because both live below the fixture seam.

### D-M1b-24 — Non-blocking: the retry loop is written twice

`RequestExecutor.ExecuteAsync` and `ExecuteRawAsync` each carry a full copy of the
CFG-051…055 loop — rate-gate pause, eligibility, hard exclusions, backoff, `Clock.Delay`.
Two copies of one requirement drift, and the next milestone to touch retry will fix one of
them. Not blocking M1b: it is behaviour-preserving to merge and both copies are currently
correct. **Rust and Python MUST implement the loop once**, with `Raw` differing only in how
its result is shaped (D-M1b-12). .NET converges in the same pass.

### D-M1b-26 — Three green slices composed into a red repository gate

**Found only after all three slices landed.** Each slice reported the traceability ratchet
green. Combined, it failed:

```
OFFENDING ERR-020
OFFENDING ERR-031
exit=1
```

`evaluate_gate` treats `applicable & covered & baseline` as a violation — an ID that has a
test marker **and** is still baselined. The Python slice had marked two tests
`@req ERR-020` and `@req ERR-031`, both M1c scope. ERR-020 requires the mapping's five steps
to live in one function, and steps 3–5 are message recognition, which D-M1b-4 explicitly
defers; the ERR-031 test asserts hint *concatenation*, not the hint *style* rule it named.

Fixed in review by stripping the two markers — the tests are good, the claims attached to
them were premature. `TRN-054` stayed, being genuinely in scope.

**The general lesson, which outlives this instance.** `baseline.json` is a single
repository-wide file and the ratchet is a repository-wide gate, but M1b ran three slices in
parallel against it. Every slice can be honestly green against its own language and the
composition still be red, because the gate's unit is the repository and the slice's unit is
a directory. **The parallel phase of a milestone is not finished when the last slice
reports; it is finished when the repository-wide gates are run once, after all of them.**
That run belongs to the reviewer, not to any slice.

A second-order note: the `+1` semantics here cut the right way. The ratchet caught an
*over*-claim, which is the direction that matters — a slice claiming coverage it had not
earned. D-M0-1's one-way ratchet is doing exactly the job it was built for.

## M1b exit record

**Status: closed, 2026-09-13.** Verified by the Strategic Orchestrator by re-running every
gate, not taken from delegate reports.

| | Tests | Coverage | Gates |
|---|---|---|---|
| .NET | 214 | 98.04 % line / 95.22 % branch | build 0 warnings, audit clean, CNF-027 surface diff **proven by a seeded violation** |
| Rust | 172 | 96.25 % line / 95.77 % region (D-M0-14 substitute) | clippy 0 warnings, `cargo audit` 143 crates clean, `cargo public-api` 0 diff |
| Python | 277 | 97.66 % line+branch | ruff clean, `mypy --strict` clean over 36 files, `pip-audit` clean |

Traceability: `covered: 96 / baselined: 324 / total: 420`, exit 0, `baseline.json` showing
**50 deletions and zero additions** — the ratchet moved one way only (D-M0-1).

All 18 `specifications/fixtures/transport/**` fixtures pass in all three suites against real
SDK code, driven through each SDK's own public `FakeTransport` (D-M1b-15).

### Cross-language parity probe

Run by the reviewer after all three slices landed, over the behaviours **no fixture
asserts** — the instrument that found three of M1a's five defects. All three agree on:

| Probed | Result |
|---|---|
| `RetryOn` defaults (CFG-050) | identical four codes |
| Unmapped-status fallbacks (D-M1b-21) | `4xx → BV-INPUT-001`, `5xx → BV-SERVER-005`, else `BV-PROTOCOL-002` |
| CFG-054 cap | `MaxBackoff × 6` in all three |
| Rate-gate pause (D-M1b-22) | 30 s cap, 1 s default, driven by status `429` |
| `RequestEvent` fields (CFG-080) | identical eight, idiomatically mapped |
| `Details` key spelling (D-M1b-4c) | `snippet` in all three |
| `Logical.Raw` idempotency (D-M1b-6) | `GET`/`HEAD`/`OPTIONS`/`LIST` in all three |
| TRN-092 unbracketed IPv6 | covered in all three |

No divergence found. This is the first milestone where the parity probe came back clean —
M1a's came back with three findings. The difference is that DR-0004 pinned the developer-facing
*values*, not only the behaviours, which is what the D-M1a-20 note asked for.

### Defects found in review, fixed before exit

Six, none of which any existing instrument caught, and none of which reached the exit:

1. **CNF-027 has never been enforced in .NET** (D-M1b-19) — reproduced by seeding an
   undeclared public type and watching a clean build. Root-caused to an MSBuild property
   reassignment. The analyzer was replaced with an executing surface diff, and the new gate
   was **proven the way DR-0001 requires**: I seeded a violation and it failed, naming the
   member. This one predates M1b — M1a's exit certified a baseline nothing had checked.
2. **`MaxResponseBytes` audited instead of bounding** (D-M1b-20) — the right error code with
   none of the protection the requirement exists for.
3. **An unmapped status threw a generic exception** (D-M1b-21) — `400` reached a throwing
   default arm, and a passing test *asserted* it.
4. **Rust's root store was empty** (D-M1b-25) — the Rust SDK could not have connected to any
   ordinary deployment. Below the fixture seam, so invisible to all 18 fixtures.
5. **Rust had no connection pooling** (D-M1b-25) — TRN-090 claimed as covered and "proven"
   by a fake transport that opens no connections.
6. **Two premature `ERR-*` coverage markers** (D-M1b-26) — surfaced only when the three
   slices were composed and the repository-wide ratchet was run once.

Findings 1, 4 and 5 sit **below the fixture seam**; findings 2 and 3 were covered by passing
tests that asserted the wrong thing. The instruments this project already has — fixtures,
coverage, traceability — would have caught **none of the six**. That is now the second
consecutive milestone where that sentence is true, and it is the strongest argument this
project has for keeping a reading review in the loop.

### Carried forward

| Item | To | Note |
|---|---|---|
| `PublicApiSurface.txt` mechanism | M1c | New in .NET; confirm it survives a milestone that changes public API substantially |
| Rust connection-reuse test asserting `accepted_connections() == 1` | M5 | Correct today; cluster discovery adds nodes and the assertion will need re-expressing per-node |
| `409` discrimination | M1c | Best-effort in all three, unexercised by any fixture, deliberately not three different heuristics |
| OVR-005 under real threads (not just tasks) | M2 | Python proves it structurally; M2's auto-renew concurrency is where it gets a behavioural test |
