# DR-0011 — M6: the section-05 authentication remainder in .NET

**Status:** **proposed** — authored by an Engineering-tree Claude Opus 5 deep worker
(`agents.md` §4.2 row 3), **revision 2**, awaiting architecture review by a Strategic-tree
Claude Opus 5 agent (`agents.md` §4.2 row 4, §4.4). Per **REC-007** the `revision` counter
tracks architecture-review rounds only.
**Revision 2** answers revision 1's **BLOCK**: blocking defect B1 and required fixes R1, R2
and R3, the Strategic Orchestrator's **Option A** ruling on R-18, and six recorded gaps the
review found in this record itself (addendum (a)–(f)). Every revision-1 ruling stands except
where a fix below names and overturns one: **D-M6-4**'s "byte-for-byte" claim (R3),
**D-M6-8**'s not-found arm (R2), **D-M6-14**'s cache key (B1, its no-TTL half **upheld**),
and **D-M2-9**'s unclamped relogin budget (R-18).
**Risk tier:** R3 (`agents.md` §5.3 — auth flows, secret material and token lifecycle;
`skills/claude/SKILLS.md` **CRS-003** puts anything touching auth or tokens at R2 minimum
and the token-lifecycle dimension lifts it). Raised by nobody: it arrived at R3 and stayed
there.
**Milestone:** M6 · **Date:** 2026-09-15
**Supersedes nothing. Amends:** nothing in `specifications/`. Discharges D-M2-5's deferral
of AUT-035, AUT-043, AUT-050…AUT-054, AUT-060 and AUT-070, and D-M2-8's / D-M2-10's
pending entry for `auth.cert.disabled-server`.
**Inherits:** [DR-0003](0003-m1a-configuration.md) (options-in / resolved-config-out,
injected `EnvironmentSource`, redacting `SecretString`),
[DR-0004](0004-m1b-transport.md) (the single `ITransport` seam, the retry loop, the single
status→code mapping function), [DR-0005](0005-m1c-error-model.md) (recognition,
enrichment, and **D-M1c-25**: a deferred branch returns the value the specification names,
never a plausible guess), [DR-0006](0006-m2-authentication.md) (the login response
contract, `TokenSource.Login`, `AutoRenewPolicy`, the sub-API grouping shape, D-M2-4's
single seam, D-M2-9's relogin accounting, D-M2-18 item 3's non-wrapping rule),
[DR-0009](0009-m4-kv-engine.md) (fixture-authoring precedent D-M4-8),
[DR-0010](0010-m5-cluster-discovery-and-resilience.md) (D-M5-15's projection-pinning, and
D-M5-29, which booked **R-18** to this milestone).

## Problem

M6 is the remainder of `specifications/05-authentication.md`: everything M2 deferred. Nine
requirement IDs sit on `tools/traceability/baseline.json` — `AUT-035` (FIDO2),
`AUT-043` (AppID role administration), `AUT-050`…`AUT-054` (FerroGate machine identity),
`AUT-060` (OIDC and SAML) and `AUT-070` (the disabled certificate backend) — and one
fixture, `auth.cert.disabled-server`, has been pending since M2a for want of an operation.
Appendix C line 113 also names two `auth.ferrogate.*` fixtures that have never existed on
disk.

The exit condition is precise: **section 05 has zero unimplemented MUSTs**. That makes M6
the milestone that finishes a specification section outright, which no earlier milestone
has done — ⚠️ **and which, at revision 2, M6 turns out not quite to do: `AUT-060`'s
usage-guide MUST is M11's (D-M6-22).** It is why R-18 lands here — `AUT-003`'s relogin replay is finally exercised
by something other than a test.

Three things make this larger than the ID count suggests.

1. **Five new auth methods, not five new operations.** AUT-050…AUT-054 alone is a login, a
   requirement read, a status poll, an enrolment, a cached convenience and a nine-operation
   root-level admin surface. AUT-043 is twenty-three paths in Appendix A. The public
   surface grows by 112 lines of `PublicApiSurface.txt` — more than any milestone since
   M1b.
2. **Appendix A pins paths; nothing pins their payloads.** For the four typed answers the
   specification actually describes — AUT-051's requirement object, AUT-052's status enum
   and enrolment result, AUT-060's SAML login triple — there is a field list. For the
   thirty-odd administration endpoints there is a path, a verb and an auth column, and
   nothing else. D-M1c-25 governs what to do about that, and it is the reason D-M6-5 below
   exists.
3. **AUT-035 and AUT-070 both ask the SDK to be deliberately incurious.** One says pass the
   payload through uninterpreted; the other says implement an endpoint that cannot work.
   Both are easy to over-implement.

## Routing classification: row 3

`agents.md` §4.2's discriminator asks whether the contract is settled — settled meaning an
accepted decision record pins the public names and the behaviour, so the task is
transcription and verification. For M6 the answer is **no**:

- **No decision record pins any of these names.** DR-0006 explicitly declined to: D-M2-5
  deferred the five methods, and `LoginCredentials`' own doc comment records that an enum
  member with no login behind it would have been a stub. There is nothing to transcribe.
- **Trigger (a) — the pathfinder pass that first defines a contract, in the first
  language.** Stage 1 is .NET-to-completion (ROADMAP D-6), so this *is* the first language,
  and the Rust and Python parity passes will be settled row 2 work against this record.
- **Trigger (b) also holds independently.** `AuthOperations` is a landed three-language
  contract and M6 adds five properties to it; `UserpassOperations` and `AppIdOperations`
  each gain public members. `PublicApiSurface.txt` is the executing proof.
- **Trigger (d) holds too** — the change spans `AuthOperations`, four new operation groups,
  `LoginRunner`, `ClientContext`, the fixture harness and the traceability baseline.

**Recorded escalation trigger, written before dispatch** (`agents.md` §4.3 rule 4): row 3
triggers **(a)** and **(b)**, plus risk tier **R3**, which §4.3 rule 2 says never routes
below row 4 for its *review*. The handback therefore goes to a Strategic-tree Claude Opus 5
agent (§4.4), and the R3 tier additionally requires Strategic Orchestrator acceptance
(§5.3).

## Decisions

- **D-M6-1 (one shared internal endpoint helper; no second transport).** The twelve new
  operation groups reach the wire through `Internal/AuthEndpoint`, which holds a
  `LogicalOperations` and nothing else. It builds no URL beyond encoding the caller's
  parts, performs no status mapping and parses no envelope, so **D-M2-4's single seam still
  holds**: every M6 operation inherits CFG-051…055's retry loop, DSC-040's failover, ERR's
  mapping and TST-051's observer without opting in.
  **Rejected:** letting each operation group call `LogicalOperations.ExecuteShapedAsync`
  directly, as `AppIdOperations` and `SysOperations` do. That is what those two do because
  they each have three or four operations; at forty-plus it means forty-plus places where
  `pathIsEncoded` or `treatNotFoundEmptyAsAbsent` can be got wrong individually, and the
  Rust and Python transcribers would have to re-derive the right combination each time.
  **Gives up:** one more layer between an operation and the executor, which a reader
  chasing a request has to step through.

- **D-M6-2 (the four new login flows reuse `LoginRunner`, via a path-and-body overload).**
  AUT-035's FIDO2 completion, AUT-050's FerroGate login, AUT-060's OIDC and SAML callbacks
  and AUT-070's certificate login are all **logins**, so they go through the one
  implementation of the login response contract. `LoginRunner` gains an overload taking an
  already-encoded path and a body; the `LoginCredentials` entry point delegates to it. They
  therefore get AUT-010's empty-token rule, AUT-011's refinements, AUT-013's recording and
  the `RecognizedAtSource` marker with no new code and no possibility of divergence.
  **Rejected:** widening `AuthMethod` and `LoginCredentials` with five more members. That
  type exists to let a `TokenSource.Login` **retain credentials for re-login** (AUT-100);
  a WebAuthn assertion is single-use and an OIDC authorisation code is single-use, so a
  `Login` source built on either could not re-login, and the member would be a shape the
  type's own purpose contradicts. Whether a FerroGate child token *should* become a
  `TokenSource.Login` variant is a real question and is left open below.
  **Gives up:** two entry points into `LoginRunner` instead of one.

- **D-M6-3 (AUT-035's WebAuthn payloads are text, both ways).**
  `WebAuthnAssertionOptions` has exactly one member, `Json`, carrying the server's
  assertion options verbatim; `Fido2LoginComplete` takes the credential as a JSON string.
  AUT-035 says the SDK "MUST NOT attempt to interpret them", so the SDK models no WebAuthn
  field at all.
  **Rejected:** returning `JsonElement`. It is the natural .NET shape, but Rust and Python
  have no counterpart that is both idiomatic and identical, and the member would then be
  three different types in three SDKs for a value whose whole contract is "do not look at
  it". A string is the one representation all three can promise byte-identically.
  **Also rejected:** a typed `PublicKeyCredentialRequestOptions`. It would go stale at the
  next WebAuthn level and is exactly what AUT-035 forbids.
  **Gives up:** .NET callers parse the string themselves; the SDK cannot validate the
  payload and does not try.

- **D-M6-4 (the FIDO2 completion body is `{"username", "credential"}`, and the credential
  is validated for well-formedness only).** The caller's JSON is written as a JSON *value*
  under a `credential` member, beside the username the two-argument signature requires.
  ⚠️ **Revision 2 restates this: "as an equivalent JSON value", not "byte-for-byte"** — see
  **D-M6-20**, which is the correction and its justification. Before that the SDK parses it once — solely to decide it *is* JSON —
  and raises `BV-INPUT-001` if it is not.
  **Rejected:** sending the credential as the whole request body. Purer, but then the
  `username` parameter AUT-035's signature names has nowhere to go, since Appendix A's
  `auth/{mount}/fido2/login/complete` carries no username segment.
  **Also rejected:** merging the credential's members into the top-level object. That
  requires knowing what they are, which AUT-035 forbids.
  **Also rejected:** sending the string through unparsed. A malformed credential would then
  produce a malformed request body and a puzzling server `400`; parsing for well-formedness
  is not interpretation, and it moves the failure before the network call.
  **Gives up:** ⚠️ **the wire member name `credential` is not pinned by any requirement.**
  This is the one guess in M6 and it is flagged as open question 1.

- **D-M6-5 (administration surfaces exchange `JsonElement` in and `Response` out; the four
  typed answers the specification pins stay typed).** AUT-043, AUT-054 and AUT-060's admin
  halves enumerate **paths**, and Appendix A gives them no field sets. A typed record per
  endpoint would be the SDK writing a contract the specification has not, which D-M1c-25
  forbids in the strongest terms available to this project. The four answers section 05
  *does* describe — `FerrogateRequirement` (AUT-051's four named fields), `MachineStatus`
  and `EnrollResult` (AUT-052's enum), `SamlLoginRequest` (AUT-060's triple) — are typed,
  and `MachineStatus`/`EnrollResult` additionally expose `Raw` so nothing the server sent
  is lost to the model.
  **Rejected:** typed records inferred from the BastionVault server source. That source is
  not the specification, the inference would be unreviewable against a requirement ID, and
  Rust and Python would have to reproduce the guess exactly.
  **Also rejected:** `IReadOnlyDictionary<string, object?>` instead of `JsonElement`. It
  loses the distinction between a JSON number and a string, which the `require_machine`
  and `token_ttl` fields both turn on.
  **Gives up:** an application administering a role types its own document. That is the
  honest position until a requirement describes one, and it is additive to fix later.

- **D-M6-6 (OIDC and SAML share one admin type, `AuthRoleAdminOperations`, carrying a
  default mount).** Appendix A gives them the identical surface —
  `auth/{mount}/config` and `auth/{mount}/role[/{name}]`. One type, constructed twice with
  `"oidc"` and `"saml"`, means one place for each path.
  **Rejected:** two near-identical classes. Two places for the same path string to drift,
  in three languages, for no caller-visible difference — `Auth.Oidc.Admin` and
  `Auth.Saml.Admin` read exactly the same either way.
  **Gives up:** the type name is not method-specific, so a reader who lands on
  `AuthRoleAdminOperations` must look at the property to know which mount it defaults to.
  The `mount` parameter is nullable rather than defaulted for the same reason.

- **D-M6-7 (`MachineIdentityStatus.Unknown` is a member of the set, not a parse failure).**
  AUT-052 lists `unknown` alongside the other four, so a status string outside the five
  also reads as `Unknown` rather than raising. Parsing is case-insensitive.
  **Rejected:** `BV-PROTOCOL-002` on an unrecognised status. The specification already
  supplies the answer for "the server said something I do not recognise", and D-M1c-25's
  rule is to return what the specification names. A new server status would otherwise break
  every existing client.
  **Gives up:** a genuinely new sixth status is indistinguishable from the server not
  knowing the machine. `MachineStatus.Raw` still carries the wire string.

- **D-M6-8 (AUT-070's override is scoped to `Auth.Cert.Login` and keyed on the code, not on
  the message).** Appendix B §2 maps `logical backend path not supported` →
  `BV-SERVER-004` and `router mount not found` → `BV-NOTFOUND-002`. AUT-070 requires
  **both** to become `BV-SERVER-004` on this path. `CertOperations` therefore catches the
  failure the shared table already produced and re-raises `BV-SERVER-004` with a hint
  naming the mount, preserving `Attempts`, `StatusCode`, `ServerMessage` and `Details`.
  **Rejected:** adding or changing a row in `tools/error-catalogue/catalogue.json`. It would
  mis-map every other `router mount not found` in the SDK — a mistyped `sys/mounts` path is
  a not-found, not an unsupported server — and the catalogue is a regenerated artefact with
  a CI gate that this tree does not author. **No code was minted:** `BV-SERVER-004` already
  exists and its catalogue hint already names `cert` auth.
  **Also rejected:** re-matching the two message strings inside `CertOperations`. That is a
  second recognition table, which D-M2-4a exists to prevent.
  **Gives up:** an operator reading `BV-SERVER-004` on `auth/cert/login` cannot tell from
  the code alone which of the two server messages arrived. `ServerMessage` carries it.

- **D-M6-9 (`AppIdRoleField` is an enum over Appendix A's thirteen, backed by a positional
  table).** Appendix A's `Auth.AppId.Admin.<Field>` row enumerates exactly thirteen
  sub-paths. They are one enum and one `ReadField`/`WriteField`/`DeleteField` triple rather
  than thirty-nine methods.
  **Rejected:** thirty-nine typed methods. Three times the public surface, three times the
  parity transcription, and no additional safety — the field set is closed either way.
  **Also rejected:** a free `string field`. A typo reaches the server as a role sub-path
  that does not exist and returns a puzzling `404`.
  **Gives up:** a caller cannot reach a fourteenth field the server adds before the SDK
  does. Adding a member is additive and non-breaking. A value cast in from outside the
  thirteen is refused client-side with `BV-INPUT-001` rather than sent.

- **D-M6-10 (the unauthenticated exemption is a property of the operation, not of the
  path).** AUT-051 makes `Requirement` callable without a token *at any mount*; Appendix A
  marks `status`, `enroll`, `auth_url` and the SAML `login` `Auth: no`. CFG-020's list is
  **literal** (`auth/ferrogate/requirement`, `auth/ferrogate/enroll`) and does not cover a
  non-default mount, `status`, or `auth_url`. M6 therefore routes those operations through
  `AuthEndpoint.ReadTokenlessAsync` / `WriteTokenlessAsync`, which reach the executor's
  existing `isLogin` seam; **`UnauthenticatedPaths` is untouched**.
  **Rejected, and this is the important one:** widening the executor's shared path list
  with a mount-generic pattern `^auth/[^/]+/(requirement|status|enroll|auth_url)$`. It was
  written, and it *failed a landed M2b test* —
  `AuthLoginTests.An_authenticated_operation_with_no_token_is_refused_before_any_network_call`
  pins `auth/ferrogate/status` as **not** exempt, with a comment saying so deliberately.
  Changing that would have meant changing the behaviour of a path CFG-020 enumerates, which
  is a specification question (R3, **CRS-004**) and not this tree's to answer, and it would
  have weakened a gate (**CLA-004**). The pattern was reverted.
  **Gives up:** an asymmetry. `Auth.Ferrogate.Status(...)` works without a token;
  `Logical.Write("auth/ferrogate/status")` still does not. Each is defensible on its own
  requirement, but they disagree, and closing the gap needs a CFG-020 amendment — open
  question 2.
  **Second cost, deliberately taken:** the tokenless seam omits the token header entirely,
  whereas `UnauthenticatedPaths` exempts only the refusal and still sends a token when one
  is held. For *new* operations there is no prior behaviour to preserve, and not sending a
  credential to an endpoint that does not need one is the better security posture.

- **D-M6-11 (every new operation is registered in the fixture driver, including the
  thirty-one no fixture drives).** `AuthFixtureOperations` registers the whole M6 surface
  behind one adapter. The registry is not only what the .NET fixtures dispatch through — it
  is the **cross-language operation vocabulary** a later Rust or Python fixture names, and
  `Auth.AppId.Admin.ReadField` must mean the same thing in all three.
  **Rejected:** registering only the three operations M6's fixtures drive. It would make
  every future admin fixture a two-tree change.
  **Gives up:** ~250 lines of test-assembly code that nothing executes today.

- **D-M6-12 (M6 bodies use the relaxed JSON escaper, for parity, and this is a defect
  found by a test).** .NET's default `Utf8JsonWriter` escapes `+` as `+`; Rust's
  `serde_json` and Python's `json` do not. AUT-060's `saml_response` is **base64**, so it
  contains `+` and `/` routinely — a SAML callback would have put a different byte sequence
  on the wire from .NET than from the other two SDKs on essentially every call. The
  assertion `Saml_callback_is_the_login_and_posts_the_assertion` caught it.
  `AuthEndpoint.JsonObject`, `AuthEndpoint.Payload` and `Fido2LoginFlow`'s body writer all
  use `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, matching the precedent
  `AppIdOperations.BodyWriterOptions` set at M4 for the same reason.
  **Not fixed here:** `LoginRunner.LoginBody`, which still uses the default encoder for
  AUT-030's `password` and AUT-040's `secret_id`. That is landed M2 behaviour, it is not in
  M6's scope, and changing it changes bytes on the userpass and AppID login paths. Open
  question 3.
  **Gives up:** nothing behavioural — both spellings are valid JSON and parse identically.
  What it buys is that a byte-for-byte fixture comparison across the three SDKs will agree.

- **D-M6-13 (the two Appendix C fixtures are authored, and nothing else under
  `specifications/` is touched).** Following D-M4-8 and D-M5-4: Appendix C line 113 already
  names `auth.ferrogate.requirement-unauthenticated` and `auth.ferrogate.enrolment-pending`,
  so authoring the two absent files realises Appendix C's own list rather than changing
  specified behaviour. Both validate against the unchanged
  `fixtures/schema/fixture.schema.json`, so **CRS-004 is not triggered**. Fixture count on
  disk goes 224 → 226.
  `auth.ferrogate.enrolment-pending` is written as a **login** rejected with
  `enrolment_pending: …` → `BV-AUTH-012`, because that is what the name describes in
  AUT-011's vocabulary, and it doubles as the wire proof of AUT-050's twice-sent DPoP proof
  — header and `dpop` body field in one fixture, with `absentHeaders` asserting the login
  carries no token.
  **Rejected:** writing it as an `Auth.Ferrogate.Enroll` call returning `pending`. That
  asserts a `data.status` shape no requirement names, i.e. it would have encoded a guess
  into `specifications/` — the R-19 defect class, one milestone after it was recorded.

- **D-M6-14 (`IsMachineIdentityRequired`'s cache lives on `ClientContext`, ~~keyed by
  mount~~, and never expires).** ⚠️ **Revision 2 overturns the key and upholds the lifetime:**
  the key is **(effective namespace, mount)** per **D-M6-16**, which is where the reasoning
  about the key now lives. Everything below about *placement* and *expiry* stands, and the
  rejected TTL stays rejected. `BastionVaultClient.Auth` constructs a fresh `AuthOperations` on
  every read (CFG-071), so a cache on the view would never hit. It is a
  `ConcurrentDictionary<string, bool>`; two callers racing the first fetch both perform it
  and both write the same answer, which is cheaper than a lock over a value that does not
  change. `RequirementAsync` refreshes it as a side effect, which is the documented way
  back to a live answer.
  **Rejected:** a TTL. AUT-051 says "cached convenience" and names no lifetime; inventing
  one is a guess, and a caller who needs freshness has `RequirementAsync`.
  **Gives up:** an operator who flips `require_machine_identity` mid-process sees the stale
  answer from the convenience until the process restarts or `RequirementAsync` is called.

- **D-M6-15 (CFG-044 is verified, not rewritten, and not re-tested).** AUT-070's other half
  is already covered end to end by
  `CoverageGapTests.Client_certificate_is_presented_and_required_by_an_mTLS_server`, which
  stands up an in-process HTTPS server that *refuses* a client presenting no certificate
  and shows the configured one being accepted. That was re-run green. A second, weaker
  assertion in `AuthM6UnitTests` — that two path strings survive configuration resolution —
  was written, failed for the right reason (the resolver parses real PEM), and was
  **deleted** rather than propped up with synthetic material: it would have diluted the
  `CFG-044` traceability marker rather than strengthened it.

### Decisions added at revision 2

Seven, all forced by the R3 handback. Each names the revision-1 ruling it overturns, or
states that it overturns none.

- **D-M6-16 (AUT-051's cache is keyed by (effective namespace, mount); overturns D-M6-14's
  key).** ⚠️ **This was the blocking defect (B1).** `ClientContext` is shared by every
  `WithNamespace` view of one client (D-M1b-9) and `RequestOptions.Namespace` overrides the
  namespace per call (CFG-060), so a mount-only key answers one namespace's question out of
  another's entry — and with no TTL (upheld, see D-M6-14) a wrong answer is permanent for the
  process lifetime. The dangerous ordering is the **fail-open** one: a namespace that does not
  require a machine identity is asked first, and a namespace that *does* is then told it does
  not, so the application skips a FerroGate login it is subject to. AUT-051 defines the answer
  per mount **as the server reports it**, and AUT-041 makes the namespace a request-scoping
  dimension on auth paths; two namespaces are therefore two questions. The key is built by
  `ClientContext.MachineIdentityKey`, length-prefixed so neither part can forge the separator,
  from `AuthEndpoint.EffectiveNamespace(options)` — `options?.Namespace ?? activeNamespace`,
  `TrimEnd('/')`, derived **once** so it cannot disagree with `RequestExecutor`'s own trim and
  turn `tenant-a` and `tenant-a/` into two entries and two fetches.
  **Rejected:** a cache per `WithNamespace` view. `Client.Auth` constructs a fresh view on
  every read (CFG-071), so it would be a cache that never hits — which is D-M6-14's original
  reasoning and is untouched.
  **Also rejected:** adding a TTL as the fix. It would reduce the window rather than close the
  hole, and it would invent a lifetime AUT-051 does not name (D-M1c-25).
  **Also rejected:** keying on the *raw* namespace string rather than the trimmed one. Two
  spellings of one namespace would then be two fetches — harmless, but it makes the cache's
  identity disagree with the wire's, which is the class of bug this whole entry is about.
  **Gives up:** one more thing a Rust or Python transcriber must get right, and it is not
  optional. A parity pass that keys on the mount alone reproduces a security defect, so the
  key is stated here as part of the contract rather than left to the implementation.

- **D-M6-17 (`AuthEndpoint.ReadBool` reads all three spellings of a true flag; overturns
  nothing).** Its one caller is AUT-051's `require_machine_identity`, which is a security
  gate, so the failure direction is what matters: reading only literal JSON `true` means a
  server spelling the flag `"true"` or `1` is understood as *not* requiring a machine
  identity, which is the unsafe answer. `True`, a string that parses as a boolean, and a
  non-zero integer all read as true.
  **Rejected:** treating everything that is not `false` as true. Absence is `false` by
  AUT-051's own shape (the server omits a false flag), and an object, an array or a word
  outside the set is a response the requirement does not describe at all — inventing a truth
  value for it is the guess D-M1c-25 forbids, and it is `EnvelopeMismatch`'s territory.
  **Gives up:** a reader who expected strict JSON typing has to read the switch to see that
  it is deliberately lenient in one direction only.

- **D-M6-18 (a `router mount not found` is AUT-070's disabled backend only at the `cert`
  mount, and the hint names the backend rather than the mount; overturns D-M6-8's scope and
  its hint text).** D-M6-8's scoping to `Auth.Cert.Login` is right and stays. The defect was
  *inside* that scope: `Auth.Cert.LoginAsync(mount: "typo")` against a server where `cert`
  **is** enabled produced `BV-SERVER-004` claiming the `typo` backend was disabled, which is
  false, and is precisely the mis-mapping D-M6-8's own rejected-alternative paragraph argues
  against — committed one paragraph later, at a different layer. `BV-SERVER-004` from
  `logical backend path not supported` is unaffected: that message is the backend answering
  for itself, at whatever mount it was asked. Separately, AUT-070 requires the hint to name
  *the disabled backend*, which is `cert`; the hint interpolated the caller's `mount`, so at a
  non-default mount it named something that is not a backend at all. It now names `cert`,
  carries `Details.backend = "cert"`, and keeps the mount asked for in the hint text and in
  `Details.mount` so nothing an operator had is lost.
  **Rejected:** dropping the not-found arm entirely. AUT-070 names both server messages and
  requires both to map, so removing it would fail the requirement at the mount the requirement
  is about.
  **Also rejected:** keeping the arm at every mount and merely softening the hint to "the
  backend at this mount may be disabled". A hedged hint on a wrong code is worse than a right
  code: `BV-NOTFOUND-002` is what a mistyped mount *is*, and it is what the shared table
  already produced.
  **Gives up:** an operator who deliberately mounts the `cert` backend at a non-default mount
  on a future server that enables it, and mistypes that mount, gets `BV-NOTFOUND-002` rather
  than the friendlier disabled-backend hint. That is the correct answer for a mount that does
  not exist, and it is the trade the mistyped-mount case demands.

- **D-M6-19 (`AuthEndpoint`'s tokenless pair passes `pathIsEncoded: true` explicitly;
  overturns nothing).** `ReadTokenlessAsync` and `WriteTokenlessAsync` were correct only by
  coincidence: `RequestExecutor` computes `PathIsEncoded: isLogin || pathIsEncoded`, and they
  set `isLogin`. The class's own documented contract is "every path built here is already
  encoded", and relying on another type's disjunction to make that true leaves a trap for a
  Rust or Python transcriber whose executor need not fold the two flags the same way.
  **Rejected:** removing the `||` from the executor instead. That is landed M1b behaviour on
  a shared seam, out of M6's scope, and it would change the encoding of every login path
  rather than documenting one class's assumption.
  **Gives up:** two redundant arguments at two call sites.

- **D-M6-20 (the FIDO2 credential is embedded as an *equivalent JSON value*, not as a byte
  copy; overturns D-M6-4's wording, not its behaviour).** `credential.RootElement.WriteTo`
  **is** a re-serialisation: insignificant whitespace is dropped, escapes are normalised
  (a `\u002B` arrives as `+`), numbers canonicalise, and a duplicate member collapses. The
  claim "byte-for-byte as the caller supplied it, never re-serialised" was therefore untrue.
  It is **sufficient** that it is only value-equivalent, and the reason is specific rather
  than general: WebAuthn's signed material travels inside base64url **string values**
  (`clientDataJSON`, `authenticatorData`, `signature`), and a JSON string's *value* survives
  re-serialisation unchanged — what a verifier hashes is the decoded bytes of the member, not
  the document's spelling. The SDK still interprets no member, which is what AUT-035 forbids:
  re-serialising a value is not re-modelling it.
  **Why it is recorded rather than shrugged off:** this is an R3 record that Rust and Python
  will transcribe as a contract. `serde_json` and Python's `json` re-serialise differently
  from each other in ways a "byte-for-byte" promise would make into a parity bug that does not
  exist, and a transcriber who believed the promise might reach for a raw-bytes splice to keep
  it. "Equivalent JSON value" is a promise all three can keep.
  **Rejected:** making it literally true by splicing the caller's bytes into the body
  unparsed. It defeats the well-formedness check D-M6-4 kept for good reason, and it would put
  a malformed body on the wire as a puzzling server `400`.
  **Also rejected:** no change at all, on the grounds that it is harmless in fact. It is
  harmless *here*, for a reason that is worth one paragraph and is not worth leaving to be
  rediscovered.
  **Gives up:** nothing behavioural. No code changed.

- **D-M6-21 (R-18, Option A: AUT-003's relogin replay is clamped to
  `max(1, MaxAttempts + 1 - AttemptsBefore)`; overturns the unclamped half of D-M2-9's
  *implementation*, and touches neither `RES-001` nor `AUT-003`).** The Strategic
  Orchestrator's ruling, recorded before implementation. The cause was one expression —
  `pass(execution with { AttemptsBefore = denied.Attempts })`, a fresh per-pass budget with no
  bound. The fix carries the bound the way D-M5-28 already carries it for failover, and the
  flag that selects it is renamed `RequestExecution.IsFailoverReplay` →
  **`IsBoundedReplay`**, because after this ruling the question the loop asks is "is this pass
  a replay?" and not "which mechanism replayed?".
  **Rationale, as ruled:** `RES-001` says "total attempts", unqualified, and the specification
  wins (`skills/claude/SKILLS.md` §7 rule 1). D-M5-28 made this exact call on the sibling
  mechanism four weeks ago, and two mechanisms sharing one cap must share one rule across
  three languages. The measured breach was `Error.Attempts` of **6** against a cap of **4**,
  on a literal-mode client at `MaxAttempts = 3`, and it is now a standing test —
  `AuthLoginTests.The_relogin_replay_runs_inside_what_is_left_of_the_RES_001_cap`, which
  asserts the cap and never the observed 6, and which fails with `Actual: 6` if the clamp is
  removed.
  **The accepted cost, stated because it is real:** `AUT-003`'s replay is weakened to **one**
  attempt in the worst case — a first pass that burnt its whole budget on retryable failures
  before the `403`. If that one attempt draws a `502`, the re-login is wasted and the caller
  sees a transport error rather than the result the new token would have fetched. `AUT-003`
  says the SDK "MAY re-login once and replay", so a one-attempt replay satisfies it; it is
  less useful, and `Math.Max(1, …)` is what guarantees it is never *zero*, which would fail
  the MUST-adjacent shape outright.
  **Rejected (ruled, not merely argued): Option B**, amending `RES-001` to exempt the relogin
  replay. It is an R3 `specifications/` change and a genuine product decision, it makes
  `Error.Attempts` unbounded by any single configured number, and it doubles a single caller
  call's worst-case load on the server.
  **Also rejected: Option C**, a new `RetryPolicy.MaxTotalAttempts` knob — over-engineering
  (CLA-007), new public API on a settled config surface, and it answers a question nobody
  asked by making the caller answer it.
  **Gives up:** the worst-case usefulness of AUT-003's replay, in exchange for one cap, one
  rule and one sentence to transcribe into Rust and Python.

- **D-M6-22 (the milestone's exit claim is qualified: `AUT-060`'s usage-guide MUST is
  outstanding and is M11's; overturns revision 1's Consequences bullet 1).** `AUT-060` carries
  two MUSTs. The SDK implements the two endpoints per method, which is the first. The second —
  "MUST document a loopback-redirect recipe in the usage guides" — is **not** satisfied:
  `specifications/17-usage-guides.md` contains no `loopback`, `oidc` or `saml` content, and
  the recipe exists only as an XML doc comment at `OidcOperations.cs:19-24`. Section 05
  therefore has no unimplemented MUST **except that one**, which `ROADMAP.md` books to M11
  (documentation and usage guides) along with the rest of sections 16–17. Every place that
  made the unqualified claim now carries the qualification, including
  `AuthFixturesTests.cs`'s comment, which is where a future reader is likeliest to meet it.
  **Dropping `AUT-060` from `baseline.json` stays correct.** The traceability tool keys on
  test markers and cannot see a prose MUST about a document; leaving the ID on the baseline
  would assert the *behavioural* half is missing, which is a different and equally false
  claim. The honest instrument for a prose MUST is the qualified sentence, not the tool.
  **Rejected:** writing the usage guide in this pass. `specifications/` is R3 and CRS-004
  territory, the section is M11's, and M6 has no authority to open it.
  **Also rejected:** keeping the unqualified claim on the grounds that the traceability gate
  is green. A gate that cannot see a requirement is not evidence about that requirement, and
  "the tool says so" is the precise failure mode CNF-002 exists to prevent.
  **Gives up:** M6's exit reads less cleanly than "section 05 is done". It is the true
  sentence, and the milestone that closes section 05 outright is now M11 rather than M6.

## R-18 — analysis, and the ruling taken at revision 2

⚠️ **The ruling is in: Option A, taken by the Strategic Orchestrator and implemented as
D-M6-21.** The analysis below is left as written, at revision 1, because it is the evidence
the ruling was taken on and an Engineering-tree record that silently rewrites its own
premises after a ruling is not a record. Read it as history; read **D-M6-21** for what the
code now does.

`ROADMAP.md` R-18, booked to M6 by D-M5-29: D-M2-9 deliberately gives `AUT-003`'s relogin
replay a **fresh** `MaxAttempts`, so `AttemptsBefore` can reach `2 × MaxAttempts` with no
failover involved, which is in tension with `RES-001`'s `MaxAttempts + 1` cap.

### It is real, caller-observable, and exactly as large as predicted

Measured, not inferred. A literal-mode client (no discovery, so no failover), `MaxAttempts
= 3`, a `Login` source with `ReloginOnPermissionDenied = true` and `MinReloginInterval =
Zero`, one `GET`: pass 1 answers `502`, `502`, `403`; the SDK re-logs in; the replay pass
answers `502`, `502`, `403`.

```
RES-001 cap (MaxAttempts + 1) = 4
observed Error.Attempts        = 6
observed non-login wire attempts = 6   (8 requests total, including two logins)
```

This is the same shape and the same magnitude as the failover breach D-M5-28 found and
fixed (6 against 4), reached by the other mechanism. The scratch harness that produced it
was deleted; it is reproducible from the paragraph above. **No standing test was added,
because a test asserting `6` would pin the disputed behaviour as a contract.**

Two aggravating facts:

1. **The default hides it.** `MinReloginInterval` defaults to 30 s and
   `ReloginOnPermissionDenied` defaults to `false`, so a default client never sees this.
   An application that opts in and sets the interval low does.
2. **M6 is where it starts mattering.** Before M6, `AUT-003` was exercised by tests. M6
   ships four more login flows, and FerroGate's whole reason for existing is
   short-lived machine credentials whose `403`s are exactly the case `AUT-003` is for.

### Option A — clamp the relogin replay the way D-M5-28 clamped the failover replay

Give the relogin pass `max(1, MaxAttempts + 1 - AttemptsBefore)` instead of a fresh
`MaxAttempts`, by carrying the bound on `RequestExecution` as the failover clamp already
does. One expression in `RunWithReloginAsync`; the mechanism is built and tested.

- **For:** `RES-001` is a MUST and "total attempts" has no qualifier in its text. It makes
  the two replay mechanisms obey one rule, which is a smaller thing to explain in three
  languages than two rules. It composes with D-M5-28 with no interaction: the clamp is over
  `AttemptsBefore`, and both mechanisms increment the same counter.
- **Against:** it weakens `AUT-003`'s replay in exactly the case the replay is for. The
  measurement above is the worst case — pass 1 burnt its whole budget on `502`s — and the
  clamp leaves the replay **one** attempt. If that attempt draws a `502`, the re-login is
  wasted and the caller sees a transport error rather than the result the new token would
  have fetched. `AUT-003` says "MAY re-login once and replay", so a one-attempt replay does
  not violate it; it just makes it less useful.
- **Gives up:** behavioural parity is *gained*; error clarity is unchanged; testability is
  unchanged; security posture unchanged; maintenance cost falls (one rule, not two); token
  cost of the parity passes falls slightly.

### Option B — exempt the relogin replay from `RES-001` in the specification

Amend `RES-001` to read "`MaxAttempts + 1` per authentication epoch", or add a sentence
naming `AUT-003`'s replay as a second permitted `+MaxAttempts`. No code changes; D-M2-9
stands as written.

- **For:** it is arguably what both requirements already mean. `RES-001` lives in section
  13 and its `+1` is explicitly "the single failover replay"; it is written about failover
  and was very likely never intended to bound a re-authentication. A relogin replay is
  semantically a *different operation* — a different credential — in a way a failover
  replay is not.
- **Against:** it doubles the worst-case latency and server load of a single caller call
  and makes `Error.Attempts` unbounded by any single configured number, which is a poor
  property for an operator reading `RetryPolicy`. It is an R3 `specifications/` change
  requiring architecture review and human confirmation, and it must land before the Rust
  and Python passes transcribe either behaviour.
- **Gives up:** error clarity (the attempt count no longer relates to `MaxAttempts` by any
  simple rule); some operator trust in the retry budget. Parity is fine either way, since a
  spec change binds all three.

### Option C — bound the total explicitly, at a number that is neither

Introduce an explicit `RetryPolicy.MaxTotalAttempts` (default `MaxAttempts + 1`) that every
replay mechanism clamps against, and let an application that wants a full-budget relogin
replay raise it.

- **For:** it makes the invariant a configured number rather than an emergent one, which is
  the only option under which an operator can *read* the worst case.
- **Against:** it is new public API on a settled config surface (R2 on its own), it is a
  third knob where CFG-050…055 already has several, and it answers a question nobody asked
  by making the caller answer it. **Rejected as over-engineering** (CLA-007) — recorded
  because it is the obvious third option and its rejection should be on the record rather
  than left to be re-proposed.

### Recommendation

**Option A, and soon — but it is not mine to take.** Three reasons for A over B:

1. **`skills/claude/SKILLS.md` §7 rule 1: specification wins.** `RES-001` is a MUST and
   says "total attempts", unqualified. D-M5-28 reached exactly this conclusion four weeks
   ago on the sibling mechanism, and reached it by *reversing* the same inference that
   `RES-001` must have meant something narrower than it says. Making the same inference
   again, on the other mechanism, would be repeating a mistake the project has already
   recorded.
2. **The cost of A is small and bounded; the cost of B is unbounded.** A's worst case is a
   one-attempt replay in the rare shape where pass 1 exhausted its budget. B's worst case
   is every caller call being able to hit the server `2 × MaxAttempts` times.
3. **Stage 2 is the deadline, not the milestone boundary.** Whichever way this goes, it is
   about to be transcribed into Rust and Python as a settled contract. D-M5-28's own
   closing note — "this is the decisive reason to fix it now rather than book it" — applies
   verbatim.

The one argument that could carry B is the semantic one: if the Strategic Orchestrator
reads a re-authenticated replay as a genuinely new operation rather than a continuation,
then `RES-001` does not govern it and there is nothing to fix. That is a **requirement
interpretation**, which is **FAM-002** territory and explicitly not an Engineering-tree
call.

**What M6 did about it at revision 1: nothing to the code.** Reopening D-M2-9 is R3 and
belongs to the Strategic Orchestrator (`agents.md` §5.4: "Request to change
`specifications/`" and "Risk tier R3 detected"). **At revision 2 that ruling has been taken
— Option A — and the change is D-M6-21**: one expression in `RunWithReloginAsync`, plus the
standing adverse-shape test D-M5-28 established the pattern for.

## Consequences

- The baseline goes **205 → 196**: `AUT-035`, `AUT-043`, `AUT-050`, `AUT-051`, `AUT-052`,
  `AUT-053`, `AUT-054`, `AUT-060`, `AUT-070`. **No `AUT` ID remains on it**, and section 05
  has no unimplemented MUST **except `AUT-060`'s second one — the loopback-redirect recipe in
  the usage guides, which `ROADMAP.md` assigns to M11**. See **D-M6-22**: revision 1 claimed
  the unqualified form, and the unqualified form is false.
- Fixtures on disk go **224 → 226**. Appendix C's `auth.*` list becomes fully realised, and
  the `auth.*` pending list becomes **empty** for the first time since M2a —
  `auth.cert.disabled-server` has been pending since M2a and is now green.
- `PublicApiSurface.txt` grows by 112 lines: five properties on `AuthOperations`, two
  methods on `UserpassOperations`, one property on `AppIdOperations`, and eleven new public
  types.
- `.NET` coverage goes 99.13 % → **99.21 %** line and 97.02 % → **97.13 %** branch, over
  **970** tests, so M6 does not spend the headroom it inherited. (Revision 1 measured
  97.06 % branch and then added `ReadBool`'s hardened arms without re-measuring; revision 2's
  spelling theory for `require_machine_identity` covers them, which is where the extra branch
  coverage comes from.) No exclusion pragma was added (CNF-010, TST-030).
- **Nothing in `specifications/` changed** except the two fixture files D-M6-13 authorises.
  No error code was minted; Appendix B stays at 121 codes and its generator reproduces the
  committed output byte-for-byte.
- **Six** open questions below are for the Strategic tree. Question 4 — a pre-existing red
  CI gate M6 did not cause — is closed at revision 2; question 1 is now a gate on the Rust
  pass rather than a question; question 6 is new.
- `CHANGELOG.md` gains an `Added` entry and, at revision 2, four `Fixed` entries (REC-001),
  and `ROADMAP.md` §2, §4, §5 and §8 close M6 at its exit (REC-002) — both Strategic-tree
  writes (REC-004). R-18's row in §8 closes with D-M6-21; **R-21's does not** — the tree-wide
  cache audit is a separate unit of work and this pass fixed only M6's instance of it.

## Revision 2 addendum — six gaps the review found in this record

Each of these is a fact the record should have carried at revision 1. None of them changes
.NET code; (a) and (f) are debts booked against the Rust and Python passes, and (e) is a gate
on Stage 2 rather than a question.

**(a) D-M6-12 does not achieve three-way parity, and must not be transcribed as if it did.**
The relaxed escaper aligns .NET with Rust's `serde_json`, and for the ASCII characters the
decision was about (`+ < > & '`) it aligns all three. It does **not** align Python:
`json.dumps` defaults to `ensure_ascii=True`, and
`python/src/bastionvault_integration_sdk/logical.py:179` uses that default, so every
non-ASCII character in a body is `\uXXXX`-escaped from Python and literal UTF-8 from .NET and
Rust. A byte-for-byte cross-SDK fixture comparison on any body containing non-ASCII still
diverges — two SDKs against one, rather than one against two. **The Python pass needs
`ensure_ascii=False`** on that call and on any other `json.dumps` that produces a request
body. Recorded, not fixed: `python/` is out of this pass's scope, and the change is a
one-line parity fix that belongs with the pass that can test it.

**(b) Why AUT-051's cache has no TTL while SYS-026's has 60 seconds.** Two caches in one SDK
with two lifetime policies is a thing a parity pass will otherwise resolve at random, in
whichever direction the first transcriber guesses. The answer is that the requirements differ
and neither SDK chose: `SYS-026` states the TTL — "a per-client cache (TTL 60 s, invalidated
by `Mount`/`Unmount`/`Remount`)" — so the SDK implements 60 seconds because it was told to.
`AUT-051` says "cached convenience" and names no lifetime, no invalidation event and no
refresh, so inventing one is the guess D-M1c-25 forbids, and the refresh path the requirement
*does* imply is `Requirement`, which writes the cache as a side effect. The rule the parity
passes should carry is therefore **not** "auth caches never expire" but "the specification
names the lifetime, or there is none": SYS-026 names one, AUT-051 does not. The two are also
different kinds of fact — a mount table changes when an operator mounts something, a
deployment's machine-identity posture is a deployment-level setting — but that is the
justification for the requirements differing, not this SDK's reason for differing.

**(c) FIDO2's Complete-level administration surface does not ship, and that was never
stated.** `appendix-a-endpoint-catalogue.md:133-135` lists `Auth.Fido2.Config` (R/W),
`Auth.Fido2.Credentials.List/Read/Write/Delete` and `Auth.Fido2.RegisterBegin/Complete`, all
at level **X**. M6 ships `Auth.Fido2.LoginBegin/Complete` and nothing else from that table.
**This is not a conformance gap** — `CNF-001` binds sections, no section-05 MUST names those
paths, and Appendix A is a catalogue rather than a requirement list — which is why the
traceability gate is silent about it and why it went unnoticed. It is recorded because every
*other* auth method's X-level surface landed in M6 (`Auth.AppId.Admin`,
`Auth.Ferrogate.Admin`, `Auth.Oidc.Admin`, `Auth.Saml.Admin`) and this one silently did not,
so a reader comparing the five methods will find an asymmetry with no stated reason. There is
one: FIDO2's admin surface is credential *registration*, which is the WebAuthn ceremony
AUT-035 explicitly keeps the SDK out of the middle of, and none of it is required. Whether to
add it is additive, non-breaking and a public-API-shape question (FAM-002) — open question 6.

**(d) D-M6-12's security analysis, which existed only in a reviewer's notes.** Switching to
`UnsafeRelaxedJsonEscaping` widens what goes on the wire unescaped, and "unsafe" in the type
name is a warning a future reader will stop at, so the analysis belongs in the record. The
relaxed encoder still escapes `"`, `\`, U+0000, U+0009, U+000A, U+007F and U+2028 — that is,
every character that is structural in JSON and the control characters that break a parser.
What it frees is `+ < > & '`, none of which is JSON-structural. The bodies it writes are sent
as `application/json`, never interpolated into HTML, an HTTP header or a URL, so the
HTML-context injection the default encoder's aggressive escaping exists to prevent has no
context to occur in here. The posture is unchanged; only the spelling of five ASCII
characters is.

**(e) Open question 1 is a gate on the Rust pass, not a question to be carried.** The FIDO2
completion body `{"username","credential"}` is a **forced guess**, and it is the only place in
M6 where D-M1c-25 could not be honoured: `AUT-035` pins the two-argument signature, Appendix
A's `auth/{mount}/fido2/login/complete` carries no username segment, and no requirement or
appendix names the body's members — so there is no specified value to return, and refusing to
guess would have meant not implementing a MUST. The consequence if it is wrong is stated
plainly: it is wrong **in three languages** once transcribed, and correcting it is then a
breaking wire change across three published SDKs rather than a one-line fix in one.
**It must be confirmed against the BastionVault server source, or by an integration run,
before the Rust pass opens** — not before release, and not "when someone gets to it". This is
the entry that turns the question into a scheduling constraint on Stage 2.

**(f) `LoginRunner.LoginBody`'s escaper residue has a deadline, and "whichever milestone next
touches `LoginRunner`" was not one.** D-M6-12 left AUT-030's `password` and AUT-040's
`secret_id` on .NET's default encoder, correctly scoped out of M6. The deadline is not a
milestone boundary but an event: **it must land before the Rust pass authors byte-comparing
fixtures on the userpass and AppID login paths.** After that point those fixtures encode
.NET's spelling of `+ < > & '` as the cross-SDK expectation, and the one-line fix becomes a
fixture-rewriting change in three trees instead. The fix itself is unchanged — give
`LoginBody` the same `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` the rest of the SDK's body
writers use — and it is R2 rather than R0, because it changes bytes on two landed login paths
and therefore needs the parity check all three languages share.

## Open questions for the Strategic tree

1. ⚠️ **The FIDO2 completion wire shape is a forced guess, and at revision 2 it is a gate
   rather than a question (D-M6-4, addendum (e)).** No requirement and no appendix names the
   request body of `auth/{mount}/fido2/login/complete`. M6 sends
   `{"username": …, "credential": <the caller's JSON>}`. **Confirm it against the server
   source or an integration run before the Rust pass opens.** If it is wrong and the Rust and
   Python passes have already transcribed it, correcting it is a breaking wire change across
   three SDKs. This is the only place in M6 where D-M1c-25's rule could not be honoured,
   because there is no specified value to return and refusing to guess would have failed the
   MUST outright.
2. **CFG-020's exemption list is literal where AUT-051 is general (D-M6-10).** CFG-020 names
   `auth/ferrogate/requirement` and `auth/ferrogate/enroll` as paths; AUT-051 states the
   property of the *operation*, which takes a mount. The result is that
   `Logical.Read("auth/fg/requirement")` is refused client-side while
   `Auth.Ferrogate.Requirement("fg")` is not. Making CFG-020 mount-generic — and adding
   `status` and `auth_url`, which Appendix A marks `Auth: no` — is a `specifications/`
   change (R3, CRS-004) and would also change the behaviour a landed M2b test pins.
3. **`LoginRunner.LoginBody` still uses .NET's default JSON escaper (D-M6-12).** A password
   or `secret_id` containing `+`, `<`, `>` or `&` goes on the wire from .NET with different
   bytes than from Rust or Python. Harmless to the server, fatal to a byte-for-byte
   cross-SDK fixture comparison on those paths. Landed M2 code, out of M6's scope — but
   **not** "whichever milestone next touches `LoginRunner`": addendum (f) gives it the only
   deadline that matters, which is *before* the Rust pass authors byte-comparing fixtures on
   the userpass and AppID login paths. Python additionally needs `ensure_ascii=False`
   (addendum (a)) before any such comparison means anything.
4. **✅ Closed at revision 2. The CNF-025 secret-scan gate was red on this branch and is
   now green.** Revision 1 reported six `s.<20+ alnum>` literals outside
   `specifications/fixtures/**`, all introduced by commit `09aa293` ("Land M5"):
   `DiscoveryUnitTests.cs` (2) and `FailoverUnitTests.cs` (4). M6 introduced none — its tests
   use `FakeTokens`, which exists for exactly this reason (D-M1c-15). The Strategic tree fixed
   it on `main` in commit `8225f7d` by hyphenating the six literals
   (`s.FAKE-token-…`, `s.FAKE-relogin-…`), which stops them *looking* like tokens without
   touching the pattern or the whitelist (CLA-004 holds). **This branch mirrors that commit's
   six substitutions byte-for-byte** rather than inventing a second fix — two different
   remedies for one gate would conflict at merge, and the branch must be green on its own
   before it is reviewed. The gate now reports clean here.
5. **Should a FerroGate child token be a `TokenSource.Login` variant (D-M6-2)?** M6 exposes
   `Auth.Ferrogate.Login` as a one-shot that installs a `Static` source. A machine that
   wants automatic re-login would need an `AuthMethod.Ferrogate` member and a
   `LoginCredentials.ForFerrogate`, which is a public API shape question (FAM-002). It is
   additive and nothing in section 05 requires it.
6. **Should FIDO2's Complete-level administration surface ship (addendum (c))?** Appendix A
   lists `Auth.Fido2.Config`, `Auth.Fido2.Credentials.*` and
   `Auth.Fido2.RegisterBegin/Complete` at level X; M6 ships the login pair only, while every
   other auth method's X-level surface landed. No section-05 MUST names them, so this is not
   a conformance gap — it is a public-API-shape question (FAM-002), additive and
   non-breaking, and it is better answered before the Rust and Python passes fix the shape of
   `Auth.Fido2` in three languages than after.

## Proposed `CHANGELOG.md` line

Under `## [Unreleased]` → `### Added`:

```
- .NET: the remainder of `specifications/05-authentication.md` — FIDO2 login on the
  userpass and standalone mounts (`AUT-035`), the FerroGate machine-identity method and its
  administration surface (`AUT-050`…`AUT-054`), OIDC and SAML with role and config
  administration (`AUT-060`), `Auth.Cert.Login`, which maps a disabled `cert` backend to
  `BV-SERVER-004` with a hint naming it (`AUT-070`), and the full AppID role-administration
  surface under `Auth.AppId.Admin` (`AUT-043`). Section 05 now has no unimplemented MUST
  except `AUT-060`'s usage-guide recipe, which is M11's.
  See [DR-0011](decisions/0011-m6-authentication-remainder.md).
```

And under `## [Unreleased]` → `### Fixed`:

```
- .NET: an AUT-003 re-login replay is now bounded by `RES-001`'s total attempt budget
  (`max(1, MaxAttempts + 1 - attempts already spent)`) instead of starting a fresh
  `MaxAttempts`, so one caller call can no longer reach `2 × MaxAttempts` wire attempts —
  measured at 6 against a cap of 4. The same bound `DSC-042`'s failover replay already
  obeys. Closes ROADMAP risk R-18; see D-M6-21 in
  [DR-0011](decisions/0011-m6-authentication-remainder.md).
- .NET: `Auth.Ferrogate.IsMachineIdentityRequired()` caches per (namespace, mount) rather
  than per mount, so one namespace's `AUT-051` answer is no longer served to another —
  including in the fail-open direction, where a namespace that requires a machine identity
  was told it does not (D-M6-16).
- .NET: `Auth.Cert.Login` at a mount other than `cert` no longer reports a `router mount not
  found` as a disabled backend; it surfaces `BV-NOTFOUND-002`. The `BV-SERVER-004` hint now
  names the disabled backend (`cert`) rather than the mount asked for, which `AUT-070`
  requires, and keeps that mount in `Details.mount` (D-M6-18).
- .NET: `AUT-051`'s `require_machine_identity` now reads `"true"` and a non-zero number as
  true, not only literal JSON `true`. Read strictly, a server using either spelling was
  understood as not requiring a machine identity — the unsafe direction for a security gate
  (D-M6-17).
```
