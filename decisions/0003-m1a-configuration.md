# DR-0003 — M1a configuration model, validation and transport seam

**Status:** Accepted · **Date:** 2026-09-13 · **Milestone:** M1a ([`ROADMAP.md`](../ROADMAP.md) §5)
**Author:** Strategic Orchestrator (Claude Opus 5) · **Risk tier:** R3 · **Size tier:** Large
**Opus trigger:** M1 is R3 and fixes public API shape plus TLS/HTTP security posture
([`ROADMAP.md`](../ROADMAP.md) §5, M1 note; [`claude.md`](../claude.md) §4).
**Requirements in scope:** `CFG-001…005`, `CFG-010…018`, `CFG-030`, `CFG-040…042`,
`CFG-050`, `CFG-060/061`, `CFG-072`, `OVR-001`, `OVR-004`, `CNF-030`, `CNF-033`, `CNF-035`

These decisions are made. No delegate reopens them (TOK-008). A delegate that believes a
decision here is wrong escalates (`skills/codex/SKILLS.md` §3 rows 9–10); it does not
choose differently.

## Context

M1a is the first milestone that produces SDK public API. Everything after it is expressed
in terms of the types named here, so the cost of getting a name or a seam wrong is paid
nine more times. Two structural problems had to be settled before any code:

1. **CFG validation is specified in terms of `BV-CONFIG-*` errors, but the error model is
   M1c.** M1a cannot raise the errors the spec requires without some of M1c existing.
2. **`CFG-*` is 36 IDs, but many of them are only observable once a request can be sent.**
   Scope has to be cut by *testability*, not by section heading, or the traceability
   ratchet (D-M0-1) records coverage that does not exist.

## Decisions

### D-M1a-1 — The error type lands whole in M1a; only the `BV-CONFIG-*` codes are populated

**Problem.** CFG-010…018 mandate `BV-CONFIG-*` errors and forbid a generic exception. The
error taxonomy is M1c.

**Decision.** M1a ships the single error type with **all** ERR-001 canonical fields and the
ERR-002 one-line string form, plus the `ErrorCategory` enum with every category from
[04](../specifications/04-error-model.md) already present. Only `BV-CONFIG-001…008` are
populated as code constants with messages and hints, transcribed from
[Appendix B](../specifications/appendix-b-error-catalogue.md) rows 14–21. M1c fills the
remaining categories and adds the server-response mapping algorithm; it does not reshape
the type.

`Retryable` is `false` for every `BV-CONFIG-*` code (ERR-006). `Attempts` is `0` — no
request was sent. `StatusCode`, `ServerMessage`, `ServerErrors`, `RetryAfter`, `Method`,
`Path`, `Address` are absent on a configuration error.

**Rejected — raise a temporary `ConfigurationException` and replace it at M1c.** That is a
breaking public-API change between two milestones (CNF-027, CNF-042) to save one milestone
of type definition.
**Rejected — pull all of M1c forward.** M1 was split into M1a/M1b/M1c precisely because
105 requirements do not review as one unit (R-7). Un-splitting it to avoid one forward
reference trades a reviewable slice for an unreviewable one.

### D-M1a-2 — Configuration is two types: a mutable input and an immutable resolved value

**Decision.** Each language has an **options/builder** type the application fills in, and a
distinct **`ClientConfig`** that is the result of resolution (CFG-001…004) and validation
(CFG-010…018). `ClientConfig` is immutable, fully resolved (no "unset" settings remain —
defaults are materialised), and is the only thing the rest of the SDK reads. Resolution
happens exactly once, at construction (CFG-002).

This is what makes CFG-061 ("per-call options MUST NOT mutate the `Client`") and CFG-002
structurally true rather than a convention a later milestone can erode.

**Rejected — one mutable config object read per request.** Directly contradicts CFG-002
and makes OVR-005 (thread safety) a locking problem instead of a non-problem.

### D-M1a-3 — Environment access is an injected source, and that seam is also CFG-005's opt-out

**Decision.** Resolution never calls the process environment directly. It reads an
`EnvironmentSource` with three provided forms:

| Form | Behaviour |
|------|-----------|
| Process (**default**) | Reads the real process environment |
| None | Reads nothing; built-in defaults and explicit values only |
| Map | Reads a caller-supplied key/value map |

One seam serves three needs: CFG-005 (the env-ignoring constructor is `None`), fixture
`environment` injection (Appendix C), and parallel-safe tests that never mutate process
state (OVR-004).

**CFG-005 answer, binding on all three READMEs:** the primary constructor **does** read the
environment in every language. Cross-language uniformity of the default beats per-language
idiom here, because an application ported between SDKs must not silently change which
server it talks to.

**Rejected — `setenv` in tests.** Process environment is global mutable state shared by
every test in the process; it makes the suite order-dependent and unparallelisable, and it
is the exact hazard OVR-004 exists to prevent.

### D-M1a-4 — Settings are a declared table, not per-setting hand-written lookups

**Decision.** Each language declares the settings of
[`02-client-configuration.md`](../specifications/02-client-configuration.md) as data — one
descriptor per setting carrying canonical name, type, default, and its ordered environment
variable list — and resolution is a single loop over that table. Adding a setting is a row,
not a branch.

The same argument the roadmap makes for Appendix B in M1c (R-1: table-driven, never
hand-transcribed) applies here: 24 settings × 2 env aliases × 3 languages is 144
opportunities for a silent parity defect, and a table makes a missing row visible.

**Two settings are irregular and MUST be handled in the table, not by special-casing at the
call site:**

- `BASTIONVAULT_NO_CLUSTER_DISCOVERY` / `VAULT_NO_CLUSTER_DISCOVERY` are **inverted**:
  a truthy value sets `ClusterDiscovery = false`.
- `BASTIONVAULT_MAX_RETRIES` / `VAULT_MAX_RETRIES` set `RetryPolicy.MaxAttempts = value + 1`.

### D-M1a-5 — Validation order is fixed and normative

**Problem.** A config with two defects must produce the *same* error code in all three
languages, or the fixtures pass three different behaviours.

**Decision.** Resolution runs first, in settings-table declaration order; a malformed
boolean (CFG-003) or duration (CFG-004) raises `BV-CONFIG-003` naming the setting in
`Details.setting` before any validation runs. Validation then runs in this order and
**stops at the first failure**:

| # | Check | Requirement | Code |
|---|-------|-------------|------|
| 1 | `Address` present, parseable, scheme `http`/`https` | CFG-010 | `BV-CONFIG-001` |
| 2 | `http://` to non-loopback without `AllowInsecureHttp` | CFG-011, CNF-035 | `BV-CONFIG-002` |
| 3 | `Namespace` well-formed (trailing `/` stripped silently) | CFG-015 | `BV-CONFIG-007` |
| 4 | `Headers` contains no reserved name | CFG-017 | `BV-CONFIG-008` |
| 5 | `Timeout`, `ConnectTimeout` > 0 | CFG-016 | `BV-CONFIG-003` |
| 6 | `ClientCertPath`/`ClientKeyPath` both or neither | CFG-012 | `BV-CONFIG-004` |
| 7 | Referenced files readable, in order `CaCertPath`, `ClientCertPath`, `ClientKeyPath` | CFG-013 | `BV-CONFIG-005` |
| 8 | PEM parses, same order | CFG-014 | `BV-CONFIG-006` |
| 9 | `TlsSkipVerify` warning; `IsInsecure = true` — **not** a failure | CFG-018, CNF-030 | — |

Pure in-memory checks precede filesystem I/O so that a config that is wrong on paper never
touches the disk to find out. `TokenFile` is exempt from check 7 by CFG-013's own
exception: an absent token file means "no token", not an error.

### D-M1a-6 — A minimal `Client` lands in M1a, with no operations

**Decision.** M1a ships `Client` holding a `ClientConfig` and a transport, exposing
`IsInsecure` (CFG-018) and nothing else. It has no `Read`/`Write`/`List`, no `Auth`, no
engines. It exists so that the public constructor and the transport injection point
(OVR-001) are designed once, and so the Appendix C operation `Client.Construct` — which
fixture `transport.headers.reserved-rejected` already depends on — resolves to real SDK
code rather than to a test-only shim.

`Client.SetAddress` MUST NOT exist (CFG-072); this is asserted by the public-API baseline
(CNF-027), not by a runtime test.

**Rejected — defer `Client` to M1b and register `Client.Construct` to a test helper.** The
fixture would then pass against code that is not the SDK, which is exactly the failure mode
D-M0-2 rejected a stub client to avoid.

### D-M1a-7 — TLS material is loaded, parsed and validated at construction

**Decision.** `CaCertPath`, `CaCertPem`, `ClientCertPath` and `ClientKeyPath` are read and
PEM-parsed during construction, and the result is materialised onto `ClientConfig`. CFG-013
and CFG-014 require fail-fast; a lazy load moves a `BV-CONFIG-005/006` to first-request
time, where the spec says it cannot be.

Also settled here, because they are properties of the TLS parameters and not of any
request: minimum TLS version is 1.2 with 1.3 offered (CFG-041); `CaCert*` is **added** to
the platform trust store, not substituted, unless `CaCertReplacesSystemRoots` is true
(CFG-040); `TlsServerName` drives both SNI and hostname verification (CFG-042).

### D-M1a-8 — Loopback is read literally as `127.0.0.1`, `::1`, `localhost`

**Decision.** CNF-035's exemption list is exhaustive. `http://127.0.0.2:8200` is rejected
with `BV-CONFIG-002`. Host comparison is case-insensitive; `::1` matches with or without
brackets.

**Rejected — treat all of `127.0.0.0/8` as loopback.** It is defensible and it is what the
OS does, but widening a security exemption beyond its written text is not a decision an
implementation gets to make on the Strategic tree's behalf. If the wider range is wanted,
CNF-035 is amended first and this record is superseded.

### D-M1a-9 — Secret-bearing settings use a redacting type from the start

**Decision.** `Token` and any future secret setting are held in a `SecretString` whose
default string/`Debug`/`repr` form does not contain the value (CNF-031, CNF-032). It lands
in M1a because M1a is where the token first enters the SDK; leaving it a bare string until
M2 means M2 must change a public field type.

CNF-031/032 **do not** leave the traceability baseline at M1a — the capturing-logger tests
that prove them (TST-051) are M2 work. M1a proves only the narrower CNF-033: nothing is
written to disk unless `UseTokenHelper` plus an explicit `PersistToken` call, and M1a ships
no code that writes a token at all.

### D-M1a-10 — An ID leaves the traceability baseline only when every MUST in it is tested

**Decision.** Partial coverage is not coverage. `CFG-020` has two MUSTs — constructible
without a token (testable now) and `BV-AUTH-001` before any network call (needs M2) — so
`CFG-020` stays in the baseline until M2, even though M1a writes the first test.

Applied to M1a, the baseline removals are exactly:

```
CFG-001 CFG-002 CFG-003 CFG-004 CFG-005
CFG-010 CFG-011 CFG-012 CFG-013 CFG-014 CFG-015 CFG-016 CFG-017 CFG-018
CFG-030 CFG-040 CFG-041 CFG-042 CFG-050 CFG-060 CFG-061 CFG-072
OVR-001 OVR-004
CNF-030 CNF-033 CNF-035
```

**27 IDs.** Everything else in `CFG-*`/`OVR-*` is deferred with a named milestone:

| Deferred | To | Because |
|----------|----|---------|
| `CFG-020` | M2 | `BV-AUTH-001` needs an authenticated operation to refuse |
| `CFG-031`, `CFG-032` | M2 | `Client.Auth.PersistToken` / `ForgetPersistedToken` are auth surface |
| `CFG-043` | M5 | SRV target SNI needs cluster discovery |
| `CFG-044` | M1b | "presented on every connection" is observable only on a connection |
| `CFG-051…055` | M1b | Retry *behaviour*; M1a ships only CFG-050's defaults |
| `CFG-070`, `CFG-071` | M1b | `SetToken`/`WithNamespace` are meaningless without operations |
| `CFG-080`, `CFG-081` | M1b | The hook fires on a request |
| `OVR-002`, `OVR-003`, `OVR-005`, `OVR-006` | M1b | Need an operation to layer, await, or cancel |
| `OVR-007`, `OVR-008`, `OVR-009` | M4 | Need typed engine surfaces to name |

### D-M1a-11 — No new fixture directory; M1a covers `CFG-*` by marked unit tests

**Decision.** Appendix C's mandatory set contains exactly one configuration fixture,
`transport.headers.reserved-rejected` (CFG-017/`BV-CONFIG-008`). M1a makes it pass and
covers the other 26 IDs with requirement-marked unit tests (TST-040) in each language.

**Rejected — add `specifications/fixtures/config/**` and author ~15 fixtures.** That is a
specification change (Appendix C layout and mandatory set) made to serve an implementation
milestone, and CLA-007 asks for the smallest change that satisfies the requirement. The
fixture mechanism buys cross-language agreement on *wire* behaviour; configuration
validation has no wire.

### D-M1a-12 — The Rust fixture operation registry is brought to parity as part of M1a

The M0 registries are not equivalent: .NET and Python resolve an operation *handler*, while
Rust's `OperationRegistry` (`rust/.../tests/harness/driver.rs:482`) stores only a name set
and returns `Err("registered operations are not implemented at M0")` for anything
registered. Registering `Client.Construct` in Rust therefore turns a `pending` into a
failure. Rust's registry gains real handlers in M1a. This is M0 debt, paid by the milestone
that first needs it.

## Public API shape

Canonical → language mapping, extending the table in
[`00-overview.md`](../specifications/00-overview.md#naming-and-idiom-mapping). These names
are fixed here (OVR-008, CNF-027); a delegate that wants a different one escalates.

| Canonical | .NET | Rust | Python |
|-----------|------|------|--------|
| `Client` | `BastionVaultClient` | `Client` | `Client` |
| options input | `BastionVaultClientOptions` | `ClientConfigBuilder` | `ClientOptions` |
| resolved config | `ClientConfig` | `ClientConfig` | `ClientConfig` |
| `Error` | `BastionVaultException` | `Error` | `BastionVaultError` |
| error category | `ErrorCategory` | `ErrorCategory` | `ErrorCategory` |
| error code constants | `ErrorCodes.ConfigInvalidAddress` | `error_codes::CONFIG_INVALID_ADDRESS` | `ErrorCodes.CONFIG_INVALID_ADDRESS` |
| transport seam (OVR-001) | `ITransport` | `trait Transport` | `Transport` (`typing.Protocol`) |
| env seam (D-M1a-3) | `EnvironmentSource` | `EnvironmentSource` | `EnvironmentSource` |
| secret holder | `SecretString` | `SecretString` | `SecretString` |
| `RequestOptions` | `RequestOptions` | `RequestOptions` | `RequestOptions` |
| `RetryPolicy` | `RetryPolicy` | `RetryPolicy` | `RetryPolicy` |

ERR-005 requires both forms: the constants above **and** the literal string
(`"BV-CONFIG-001"`) reachable, so logs from different SDKs correlate.

## Consequences

- The public-API baselines (`dotnet/.../PublicAPI.Unshipped.txt`,
  `rust/.../public-api-baseline.txt`, `python/api_surface.txt`) all change in M1a. That is
  expected; CNF-027's check is that the change is *reviewed*, not that it is absent.
- M1b inherits a `Client` that compiles and a transport trait it must not redesign. If M1b
  finds the trait shape wrong, that is an escalation and an amendment to this record, not a
  quiet change.
- The `EnvironmentSource` seam means no M1a test needs `setenv`; a test that reaches for
  process environment mutation is a review finding.
- 27 IDs leave `tools/traceability/baseline.json`. The remaining `CFG-*`/`OVR-*` entries
  now have a named owning milestone, which they did not before.

## Open

Nothing blocking. `CaCertReplacesSystemRoots` (CFG-040) is named in the spec's prose but
absent from the settings table in
[`02-client-configuration.md`](../specifications/02-client-configuration.md); M1a adds it to
the table as a `bool`, default `false`, with no environment variable. This is a
specification clarification, made here under `claude.md` §1.1 (specification changes are
Claude's), not an implementation choice.

---

## Addendum — .NET pathfinder pass (2026-09-13)

Recorded under roadmap D-2: the .NET pass exists to surface gaps in the design cheaply,
before Rust and Python hit the same gap. These are binding on the Rust and Python slices.

### D-M1a-13 — `RateGate` and `AutoRenew` are in M1a's settings table after all

**This corrects an error in D-M1a-10's scope list.** The pathfinder reported that
`RateGate` and `AutoRenew` were left out of the resolver because no `CFG-*` ID names them
individually. That reasoning is wrong, and the omission would have made `CFG-001` a false
positive on the traceability ratchet: CFG-001 governs *every* setting in the
[`02`](../specifications/02-client-configuration.md) table, so a table with two settings
missing does not satisfy it, and by D-M1a-10 `CFG-001` could not have left the baseline.

**Decision.** M1a resolves the full settings table:

- `RateGate` — `BASTIONVAULT_RATE_PER_SEC`, `BASTIONVAULT_RATE_BURST`; defaults 8 req/s,
  burst 16; `0` disables. Values are resolved and validated only; the token bucket itself
  is M8.
- `AutoRenew` — no environment variable; default disabled. A materialised, disabled value
  on `ClientConfig`; the renewal loop is M2.
- `Logger`, `Transport` — no environment variable; defaults no-op and none respectively.

A negative `RateGate` value is `BV-CONFIG-003` with `Details.setting`. This costs three
table rows and keeps `CFG-001` honest.

### D-M1a-14 — "Materialised" means parsed onto `ClientConfig`, not wired into a TLS stack

The pathfinder asked where M1a stops on CFG-040/041/042. **Decision:** M1a reads and parses
the trust material and records the TLS parameter values (minimum version, server name,
replaces-system-roots, parsed CA collection, parsed client certificate) as data on
`ClientConfig`. M1a constructs **no** `HttpClient`, `rustls::ClientConfig`, or
`ssl.SSLContext`. M1b builds the real TLS stack from that data and is where CFG-044 and the
wire-level assertions land. A Rust or Python slice that builds a live TLS stack in M1a has
over-built.

### D-M1a-15 — A weak requirement is better than a false one

`CFG-041` is proved at M1a by asserting the recorded minimum-TLS-version constant, which is
a thin test. That is the honest ceiling for a value that nothing consumes until M1b, and it
is accepted. It is *not* a licence to satisfy other IDs by asserting a constant exists —
`CFG-002`, `CNF-033` and `OVR-004` must be behavioural, as they are in the .NET pass.

### D-M1a-16 — The user-profile directory is an allowed read outside `EnvironmentSource`

`TokenFile` defaults to `~/.vault-token`, which requires the OS home directory. **Decision:**
resolving the home directory through the platform API (rather than through
`EnvironmentSource`) is permitted and is not an OVR-004 violation — it is a read of
immutable process identity, not of configuration. The *file* is only opened when
`UseTokenHelper` is true (CFG-030), and its absence is never an error (CFG-013).

### D-M1a-17 — Accepted pathfinder decisions, binding on all three languages

| Decision | Binding form |
|----------|--------------|
| Logging seam | A public logger interface (`IClientLogger` / equivalent) with a no-op default, so CNF-030's warning is observable in tests without capturing global stdout |
| `CaCertPem` precedence | When `CaCertPem` is set, `CaCertPath` is **not** opened and its readability is **not** checked — the table says the inline PEM wins, so the path is not a referenced file |
| Empty PEM | A PEM body that parses to zero certificates is `BV-CONFIG-006`, not success. .NET's `ImportFromPem` silently imports nothing rather than throwing; every language must assert a non-empty result explicitly |
| D-M1a-4 shape | Ordered per-setting resolution blocks sharing one boolean parser and one duration parser satisfy D-M1a-4. The requirement is *one* parser per type and a single declared order — not a reflection-driven descriptor object |
| `NO_CLUSTER_DISCOVERY` | Parsed as a CFG-003 boolean and inverted, so `=0` leaves discovery **enabled**. Presence alone does not disable it |
| Secret accessor | Revealing a secret is an explicitly named method (`Reveal()` / equivalent), never an implicit conversion or a plain property, so every read of secret material is a searchable call site |

### Defects found in review, fixed before handback

Both were reproduced, not inferred:

1. `ClientConfig.Headers` aliased the caller's mutable dictionary, so a reserved header
   rejected by CFG-017 at construction could be injected afterwards. **Every language must
   defensively copy `Headers` at resolution time and validate the copy**, with a
   case-insensitive comparer.
2. An unusable path (invalid characters) escaped as a raw `ArgumentException` rather than
   `BV-CONFIG-005`, breaking CFG-013's "never a generic exception". **Every language must
   ensure the file-readability check converts *every* failure mode of opening the file into
   `BV-CONFIG-005`**, not just the obvious not-found and permission-denied cases.

### D-M1a-18 — `Details.setting` carries the canonical setting name from the spec table

**Found by the three-language parity check, not by any single review.** All three slices
invented their own vocabulary for the `BV-CONFIG-003` `Details.setting` value, and none of
the three matched the specification:

| Environment variable | .NET | Rust | Python | **Canonical** |
|---|---|---|---|---|
| `BASTIONVAULT_MAX_RETRIES` | `RetryPolicy.MaxRetries` | `MaxRetries` | `RetryPolicy` | `RetryPolicy.MaxAttempts` |
| `BASTIONVAULT_RATE_PER_SEC` | `RateGate.RequestsPerSecond` | `BASTIONVAULT_RATE_PER_SEC` | `RateGate.RatePerSec` | `RateGate.RatePerSecond` |
| `BASTIONVAULT_RATE_BURST` | `RateGate.Burst` | `BASTIONVAULT_RATE_BURST` | `RateGate.RateBurst` | `RateGate.Burst` |

The other seven settings already agreed. Rust's two entries are the worst of the three: they
report the *environment variable name*, which is not a setting name at all and which has no
meaning for a caller who set the value in code.

This matters because `Details.setting` is developer-facing API, not an internal label —
Appendix B's own hint for `BV-CONFIG-003` instructs the developer to "Check
`Details.setting`", and ERR-001 makes `Details` a canonical field. Three SDKs answering that
instruction with three different words is precisely the cross-language drift the fixtures
exist to prevent.

**Decision.** `Details.setting` carries the canonical setting name from the table in
[`02-client-configuration.md`](../specifications/02-client-configuration.md), dot-qualified
with the spec's own field name when the environment variable targets a sub-field:
`RetryPolicy.MaxAttempts` (CFG-050's block names the field `MaxAttempts`),
`RateGate.RatePerSecond` and `RateGate.Burst` (named in
[`14`](../specifications/14-batch-and-request-efficiency.md) and used in Appendix C's
`settings` example). Never the environment variable name; a setting may be reachable from
two aliases and from code, and the canonical name is the only stable identifier among them.

**This was my gap, not the delegates'.** The brief pinned the error *code* for every path and
never pinned the detail *value*, so three agents reasonably invented three answers. A brief
that fixes a developer-facing string only by example fixes nothing.

### D-M1a-19 — Dynamically typed languages must validate configuration input types

Python's `ClientOptions.timeout` is annotated `timedelta | None`, and passing an `int`,
`float` or `str` — the natural thing for a Python caller to do, and invisible to
`mypy --strict` at any untyped call site — raised a bare
`TypeError: '<=' not supported between instances of 'int' and 'datetime.timedelta'`.

CFG-016's preamble requires construction to fail with a `BV-CONFIG-*` error and "never a
generic exception", and it does not distinguish a wrong *value* from a wrong *type*.

**Decision.** In a dynamically typed implementation, a configuration value of the wrong type
is `BV-CONFIG-003` with `Details.setting`, exactly like a value of the wrong range. The type
annotation is documentation, not enforcement. This obligation is Python's alone — .NET and
Rust get it from their compilers — and is the first case in this project where behavioural
parity requires *more* code in one language, not merely different code.

### D-M1a-20 — `RateGate`'s public field names follow the canonical spec names

The `Details.setting` correction in D-M1a-18 exposed a second, deeper divergence one layer
down: the *public field names* of `RateGate` disagree in all three SDKs, and none matches
the specification.

| Canonical ([`14`](../specifications/14-batch-and-request-efficiency.md):13) | .NET | Rust | Python |
|---|---|---|---|
| `RatePerSecond` | `RequestsPerSecond` | `per_second` | `rate_per_sec` |
| `Burst` | `Burst` ✓ | `burst` ✓ | `rate_burst` |

Four names for one field. The .NET slice noticed the inconsistency and correctly declined to
act on it unasked, since renaming public API is not a delegate's call.

**Decision.** OVR-007/008 defer the *typed engine surfaces* to M4, but the canonical-to-idiom
mapping rule in [`00-overview.md`](../specifications/00-overview.md#naming-and-idiom-mapping)
binds every public name the moment it ships. `RateGate` becomes `RatePerSecond`/`Burst` in
.NET and `rate_per_second`/`burst` in Rust and Python.

Fixing this at M1a costs three renames and three baseline updates. Deferring it to M8, when
the token bucket is actually built and the field is referenced throughout the rate-gate
implementation and its fixtures, costs a deprecation cycle on shipped public API (CNF-042).
The cheapest moment to fix a public name is before anything depends on it.

**Note for later milestones.** Two consecutive findings now (D-M1a-18, D-M1a-20) have been
"three agents invented three names for one spec concept". The brief for M1b and M1c must
pin the canonical name of every public member it asks for, not only the behaviour — the
fixtures do not catch a name that no fixture mentions.

### D-M1a-21 — Object-valued settings are set as objects in every language

The [`02`](../specifications/02-client-configuration.md) settings table types `RetryPolicy`,
`RateGate` and `AutoRenew` as **objects**. .NET and Python both model them that way
(`options.RetryPolicy`, `options.rate_gate`, …). Rust's builder instead flattened them into
scalar setters — `max_retries(i64)`, `rate_per_second(i64)`, `rate_burst(i64)`,
`auto_renew_enabled(bool)` — with no setter accepting the object at all.

This is not a naming preference; it is a **missing capability**. Rust's `RetryPolicy` struct
carries all seven CFG-050 fields, but a Rust caller could reach only `max_attempts` through
the builder. `InitialBackoff`, `MaxBackoff`, `BackoffMultiplier`, `Jitter`,
`RespectRetryAfter` and `RetryIdempotentOnly` were configurable in two SDKs and unreachable
in the third, which is precisely the behavioural divergence CLA-003 forbids. It was also
internally inconsistent: `ClientConfig::rate_gate()` already *returned* a `&RateGate`, so the
object existed on the way out but not on the way in.

**Decision.** Rust's builder takes the objects — `retry_policy(RetryPolicy)`,
`rate_gate(RateGate)`, `auto_renew(AutoRenew)` — and the flattened scalar setters are
removed rather than kept alongside, so there is one way to set each setting and no
precedence question between a flattened setter and an object setter. Resolution precedence
is unchanged and identical in all three languages: explicit object, then
`BASTIONVAULT_*`, then `VAULT_*`, then the default (CFG-001).

**Why this was invisible until now.** The fixtures exercise *configuration values*, never
*configuration-setting APIs*, and the traceability ratchet counts requirement IDs, not
capabilities. Neither instrument can see a setter that was never written. This is the third
finding in M1a that only a deliberate cross-language comparison could surface — and the
first where the divergence was a missing feature rather than a differing name.

### D-M1a-22 — Until M4, the traceability baseline *is* the CNF-002 gap list

[`ROADMAP.md`](../ROADMAP.md) §9 requires each README's CNF-002 gap list to be updated at
every milestone exit. At M1a exit there are no per-language READMEs (only `README.md` and
`python/README.md`), and no README makes a conformance claim — CNF-002 qualifies a declared
conformance level, and the first declaration is M4's `Core`.

**Decision.** `tools/traceability/baseline.json` serves as the gap list until M4. It is
machine-generated, it is enforced by a CI gate, and it cannot drift from reality the way a
hand-maintained README list can. M4 — which is the first milestone to make an external
conformance claim, and which the roadmap already calls "the first externally meaningful
gate" — is where the three READMEs are authored and where the baseline is rendered into the
CNF-002 prose form.

Recording this so that M1a's exit is not silently short of a roadmap obligation: the
obligation is deferred, with a named milestone, not dropped.

## M1a exit record

**Status: closed, 2026-09-13.** Verified by the Strategic Orchestrator, not taken from
delegate reports.

| | Tests | Coverage | Gates |
|---|---|---|---|
| .NET | 94 | 97.94 % line / 97.76 % branch | build 0 warnings, audit clean |
| Rust | 99 | 98.59 % line / 97.69 % region (R-3 substitute) | clippy 0 warnings, audit clean |
| Python | 176 | 100 % line / 100 % branch | ruff clean, `mypy --strict` clean, pip-audit clean |

Traceability: `covered: 46 / baselined: 374 / total: 420`, exit 0, `baseline.json` showing
27 deletions and **zero** additions — the ratchet moved one way only (D-M0-1).

Fixture `transport.headers.reserved-rejected` passes in all three suites against real SDK
code. All three emit identical canonical `Details.setting` values.

**Four defects were found in review and fixed; none reached the exit.** Two were found by
reading and reproducing (.NET header aliasing, .NET generic-exception leak), one by a
cross-language probe (`Details.setting` divergence, all three languages), one by comparing
public surfaces (Rust's missing object-valued setters). A fifth, Python's wrong-typed
duration leak, was found by probing the validation order. The instruments the project
already had — fixtures, coverage, traceability — would have caught **none** of them.
