# DR-0010 — M5: cluster discovery and resilience in .NET

**Status:** **accepted** — authored by the Strategic Orchestrator (Claude Opus 5), reviewed
by a Strategic-tree Claude Opus 5 agent (`agents.md` §4.2 row 4), **revision 2** — one
architecture-review round, which returned *approve with required fixes*; all five are
folded in below and the dispositions are in the closing section. **Three addenda** follow
the original decisions, carrying the rulings that arose at M5a's and M5b's handbacks.
Per **REC-007** the `revision` counter tracks architecture-review rounds only and is
deliberately **not** bumped for an addendum or a handback ruling, so it stays at 2.
**Risk tier:** R2 (`agents.md` §5.3 — a new cross-language public contract; it moves the
retry surface, which CRS-003 would read as R2 on its own). No secret material of its own.
**Milestone:** M5 · **Date:** 2026-09-15
**Supersedes nothing. Amends:** `ROADMAP.md` §4's M5 row (see D-M5-2 and D-M5-3).
**Inherits:** [DR-0003](0003-m1a-configuration.md) (`ParseAddress`, the fixed validation
order, `ClientConfig` shape), [DR-0004](0004-m1b-transport.md) (the `ITransport` seam, the
retry loop, `RequestExecution` accounting, `IJitterSource`), [DR-0005](0005-m1c-error-model.md)
(recognition, enrichment, and D-M1c-25: a deferred branch returns what the specification
names, never a plausible guess), [DR-0006](0006-m2-authentication.md) (sub-API grouping),
[DR-0009](0009-m4-kv-engine.md) (two-slice milestone shape, fixture authoring precedent
D-M4-8, deferral-with-named-owner precedent D-M4-2).

## Problem

M5 is the whole of `specifications/13-cluster-discovery-and-resilience.md`: address
classification, DNS SRV discovery, health probing, node ranking, sticky sessions with
bounded failover, the retry interaction, TLS under discovery, diagnostics, and the
operator fan-out. `ROADMAP.md` §4 books it as 33 requirement IDs, `Large`, R2, Stage 1,
with the exit gate ".NET failover + sticky-session fixtures green".

Grounding the booking against `tools/traceability/baseline.json` and the .NET tree changes
the count twice, and grounding DSC-041 against the landed M1b behaviour surfaces the one
decision this milestone actually turns on. Both are below.

## Routing classification: both slices are row 3

`agents.md` §4.2's discriminator asks whether the contract is settled — settled meaning an
accepted decision record pins the public names and the behaviour. For M5 the answer is
**no**, on four axes:

1. **No decision record pins any `DSC`/`RES` name.** Section 13 gives language-neutral
   config blocks (`DiscoveryConfig`, `HealthConfig`) and member names on `Client`
   (`SelectedNode`, `InputLabel`, `Discover()`, `Reconnect()`); it gives no C# signatures
   and there is no already-shipped member to transcribe against.
2. **Fixture coverage is partial in a way that hides the hard cases.** Appendix C
   (line 133) names **ten** `resilience.*` fixtures; **four** exist on disk. The six
   missing ones (D-M5-4) are exactly the classification table, the SRV ordering rules, the
   probe-state table, the RTT/weight tiebreak, the not-armed path and the backoff maths —
   i.e. every rule with a branch in it.
3. **The specification's own composition rule is under-determined.** §13's retry-policy
   list says a node failure becomes `BV-DISCOVERY-003`, while `CFG-050` pins a default
   `RetryOn` that does not contain that code and `appendix-b` marks it `retryable: yes`.
   Read naively, M5 would silently stop retrying transport failures that M1b retries
   today. D-M5-5 and D-M5-6 resolve this; it is not a question an implementation agent may
   answer by choosing.
4. **The change spans more than three components** — a new discovery subsystem, the
   configuration resolver, `ClientContext`, `RequestExecutor`'s attempt loop, the error
   mapper, and the fixture harness — which is row 3 trigger (d) on its own.

**Recorded escalation trigger, written before dispatch** (`agents.md` §4.3 rule 4): row 3
trigger **(a)**, the pathfinder pass that first defines a contract in the first language,
together with trigger **(d)**, three or more components. For the second slice (D-M5-1)
trigger **(a)** is refuted once this record is accepted, but trigger **(b)** — "a change to
an existing cross-language contract, a public API shape" — holds on its own two counts:
M5b mints public `ReconnectAsync`, and it changes the observable error code and the
retryability of the transport-failure path (D-M5-5 limb (i)), which is a landed
three-language contract. Trigger **(d)** also holds — M5b edits `RequestExecutor`,
`ClientContext`, the discovery subsystem and the harness, and it is the one slice that can
regress all 721 landed tests, because every operation in the SDK goes through the loop it
changes — but (b) is the stronger ground and the review recommended citing it, since a
component count for a single slice is arguable in a way (b) is not. Row 2's "Out" column is explicit that row 2 does not
apply when a row 3 condition actually holds, and §4.3 tie-breaker 1 does not override a
literal trigger. So both slices route to `eng-deep`.

**Review at handback:** Strategic-tree Claude Opus 5 for both slices — row 3's gate is Opus
by §4.2, and R2 is Opus by §4.4.

## Decisions

- **D-M5-1 (slicing).** M5 lands in two sequential slices. They touch the same public
  contract, so they do not run in parallel (`agents.md` §7.4).
  - **M5a — discovery, probing, picking, diagnostics.** IDs: `DSC-001`, `DSC-002`,
    `DSC-010`…`DSC-014`, `DSC-020`…`DSC-022`, `DSC-030`…`DSC-036`, `RES-010`, `RES-011`,
    `RES-020`, `RES-021` — **21 IDs**.
  - **M5b — sticky session, bounded failover, reconnect, retry composition.** IDs:
    `DSC-040`…`DSC-046` — **7 IDs**.

- **D-M5-2 (`RES-001`…`RES-004` are already landed; M5 books 29, not 33).**
  `ROADMAP.md` §4 attributes all 33 `DSC`+`RES` IDs to M5. `RES-001`, `RES-002`, `RES-003`
  and `RES-004` are **already covered** — they left the baseline at M1b with the retry
  loop, `IJitterSource` and `RequestOptions.TotalTimeout` (DR-0004). The baseline therefore
  holds 29 M5 IDs, not 33. M5 re-verifies `RES-001`'s attempt cap under failover (D-M5-7)
  but mints and lands nothing for it.

- **D-M5-3 (`RES-030` defers to M7, with a named owner).** `RES-030`'s
  `Sys.UnsealClusterWide` / `Sys.SealClusterWide` variants are variants **of** `Sys.Unseal`
  and `Sys.Seal`, which are `SYS-012` / `SYS-013` and booked to **M7**. Implementing a
  cluster-wide wrapper around operations that do not exist would mean inventing the
  operations, which is M7's contract, not M5's. `RES-030` is a SHOULD, so deferring it
  breaks no MUST and no conformance level. It stays baselined with M7 as owner — the same
  shape as `KV-001`→M7 and `KV-010`→M8 (D-M4-2). **M5 therefore lands 28 `DSC`/`RES` IDs**, taking the
  baseline 234 → 206; D-M5-16a adds `CFG-043` as a 29th, for a final 205.

- **D-M5-4 (the six missing Appendix C fixtures are authored by this brief).** Following
  D-M4-8: Appendix C already names all ten `resilience.*` fixtures, so authoring the six
  absent files fills in Appendix C's own list rather than changing specified behaviour.
  They are authored to the existing `fixtures/schema/fixture.schema.json` — which already
  admits `resolver`, `fail`, `clientState` and `absentHeaders`, so **the schema does not
  change** and this is not a `specifications/` behaviour change (CRS-004 is not triggered).
  Ownership: `resilience.address.classification`,
  `resilience.srv.sorted-and-verbatim-underscore`, `resilience.probe.classify`,
  `resilience.pick.leader-over-follower-rtt-weight` → M5a;
  `resilience.failover.not-armed-single-candidate`, `resilience.backoff.math-seeded` → M5b.
  Fixture count on disk goes 218 → 224.

- **D-M5-5 (DSC-041 has two limbs; they are ruled on separately). This is the
  milestone's load-bearing decision.** `DSC-041` makes a node failure either (i) a
  transport-level failure "(connection refused/reset, timeout)" or (ii) "a `5xx` whose
  message contains `sealed`, `uninitialized`, or `standby` (case-insensitive)". The two
  limbs do not get the same answer, and the arrow "→ `BV-DISCOVERY-003`" binds only the
  first.

  **Limb (i) — transport failures.** Reclassified to `BV-DISCOVERY-003 NodeUnavailable`
  (with `Details.host`, `Details.reason`) **only when the client's endpoint was chosen by
  discovery**. In literal mode — an address with `://`, an explicit `:port`, an IP literal,
  or any address under `ClusterDiscovery = false` — the M1b mapping is untouched and the
  error stays `BV-TRANSPORT-001/002`. Scope is exactly the three failure kinds `DSC-041`
  names: `ConnectionRefused`, `Reset`, `Timeout`. `Dns`, `TlsVerify` and `TlsHandshake`
  are **not** node failures in either mode and keep their M1b codes, because `DSC-041`'s
  parenthetical does not name them and D-M1c-25 forbids widening a list the specification
  closed. This protects `errors.enrichment.tls-no-ca` (`BV-TRANSPORT-003`) as well as the
  literal-mode fixtures below. Precisely: all 18 transport fixtures use literal addresses,
  and **three** fixtures repo-wide assert a `BV-TRANSPORT-*` code as their expected error —
  `transport.retry.write-not-retried` (001),
  `errors.enrichment.connection-refused-default-address` (001) and
  `errors.enrichment.tls-no-ca` (003). Folding in limb (ii) raises the at-risk set to
  twelve. The mode scoping is what keeps every one of them green without amendment.

  **Limb (ii) — a 5xx whose message matches.** The **error code is never replaced**, in
  either mode: a matching 5xx keeps the Appendix B code it already maps to —
  `BV-SERVER-001` (sealed), `BV-SERVER-003` (standby), `BV-SERVER-007` (uninitialized)
  (`specifications/appendix-b-error-catalogue.md:241-242`, `CFG-053`). What limb (ii)
  contributes in discovery mode is the **failover trigger only**: such a response arms the
  single `DSC-042` replay for an idempotent operation on an armed client, and if the replay
  recovers, the caller sees success. Where failover is unarmed or the replay also fails,
  the caller sees the Appendix B code, not `BV-DISCOVERY-003`.
  **Why:** §13:125 legislates that "`BV-SERVER-003` … is retried only via failover", a
  sentence that presupposes `BV-SERVER-003` *survives as the code* under discovery;
  `CFG-053` makes a sealed 503 never-retryable, which reclassifying it to the retryable
  `BV-DISCOVERY-003` would invert; and nine landed fixtures
  (`errors.recognition.bv-server-001.{1,2}`, `bv-server-002.{1,2,3}`, `bv-server-003.1`,
  `bv-server-007.{1,2}`, `transport.status.503-sealed`) assert those codes. `DSC-044`'s
  "the original `BV-DISCOVERY-003`" is about limb (i), which is the only limb that mints
  that code.
  **Composition with `CFG-050`:** unchanged. `BV-SERVER-003` stays in the default `RetryOn`.
  §13 step 3's "retried only via failover" is read as describing *which mechanism recovers
  first* on an armed discovery client — the replay runs before any backoff retry, so a
  successful replay means no retry happens at all. Where failover is unarmed, `CFG-050`
  governs exactly as it does today.
  **Rejected alternative:** reclassify both limbs unconditionally and amend the affected
  fixtures. Rejected — it rewrites a landed, three-language-agreed contract to serve a
  section that never asked for it (CLA-007), and `specifications/` is the source of truth,
  not the newest section.
  **Fixture gap, recorded rather than closed:** Appendix C names ten `resilience.*`
  fixtures and none of them exercises limb (ii). M5b covers it with .NET unit tests against
  `DSC-041`; minting an eleventh fixture id would be an Appendix C change, which is R3 and
  out of M5's scope (CRS-004). The gap is named here so a later agent finds it rather than
  rediscovers it.

- **D-M5-6 (`BV-DISCOVERY-003` is not added to the default `RetryOn`).** `CFG-050` pins the
  default `RetryOn` list and M5 does not edit it. Appendix B's `retryable: yes` means a
  *caller* may retry; the SDK's own automatic recovery for a node failure is exactly the
  single failover replay of `DSC-042`, never a backoff retry of the same dead node. §13's
  retry-policy step 2 is then read as it is written — "the (possibly new) pinned node":
  the **replayed** request's own outcome classifies normally, so a post-failover
  `BV-SERVER-002` still retries under `CFG-050`. **No landed fixture discriminates this ruling, and the record says so rather than
  implying otherwise.** `resilience.failover.write-never` expects `attempts: 1` at
  `MaxAttempts = 3`, but its operation is a write, so `CFG-051` plus the default
  `RetryIdempotentOnly = true` already forbid retry under *either* reading;
  `resilience.failover.read-once` pins `MaxAttempts: 1`, so it has no retry budget to
  spend. The discriminating case — an **idempotent read at `MaxAttempts = 3`** whose
  failover is unarmed, which must end at `attempts: 1` and not at `attempts: 3` — is
  authored into `resilience.failover.not-armed-single-candidate` (M5b, D-M5-4). D-M5-6 is
  unverified until that fixture is green.

- **D-M5-7 (`RES-001`'s cap, restated as an invariant M5b must test).** Total attempts
  never exceed `MaxAttempts + 1`. Mechanically: the failover replay is the `+1` and does
  **not** consume a retry attempt; the per-pass attempt counter restarts at 1 for the
  replay while the accumulated `AttemptsBefore` carries forward, which is D-M2-9's existing
  two-counter rule reused verbatim rather than a second mechanism.

- **D-M5-8 (public API, pinned).** New public surface, `namespace BastionVault.IntegrationSdk`:

  ```csharp
  public enum NodeState { Unreachable, Uninitialized, Sealed, Follower, ActiveLeader }

  public sealed record DiscoveryConfig
  {
      public string SrvService { get; init; } = "_bvault._tcp";
      public string DefaultScheme { get; init; } = "https";
      public int DefaultPort { get; init; } = 8200;
      public TimeSpan ResolveTimeout { get; init; } = TimeSpan.FromSeconds(5);
  }

  public sealed record HealthConfig
  {
      public TimeSpan ProbeTimeout { get; init; } = TimeSpan.FromMilliseconds(1500);
      public int Parallelism { get; init; } = 4;
  }

  public sealed record SrvRecord(string Target, int Port, int Priority, int Weight);

  public interface ISrvResolver                                    // DSC-014
  {
      Task<IReadOnlyList<SrvRecord>> ResolveAsync(string ownerName, CancellationToken cancellationToken = default);
  }

  // Priority/Weight are null — not 0, not -1 — on a DSC-012 synthesised literal candidate,
  // which has no SRV record behind it. RES-020's table prints "-" for a null. Pinned because
  // three languages would otherwise each pick a different sentinel and the shared
  // `resilience.address.classification` fixture would stop being portable.
  public sealed record Candidate(string Url, string Target, int Port, int? Priority, int? Weight);

  public sealed record ProbeResult(Candidate Candidate, NodeState State, double? RttMs)
  {
      public bool ClusterHealthy { get; init; } = true;            // DSC-022
      public string? ClusterId { get; init; }                      // DSC-021, forward-compat
      public string? Version { get; init; }
  }

  public sealed record NodeSelection(string Url, NodeState State, double? RttMs)
  {
      public string? ClusterId { get; init; }
      public string? Version { get; init; }
  }

  public sealed record DiscoveryReport(
      string InputLabel,
      IReadOnlyList<ProbeResult> Ranked,
      NodeSelection? Picked)
  {
      public string Render() { /* RES-020's table, exactly as §13:143-152 prints it */ }
  }
  ```

  On `BastionVaultClient`:

  ```csharp
  public string InputLabel { get; }                                        // DSC-035
  public NodeSelection? SelectedNode { get; }                              // DSC-035
  public Task<NodeSelection> ConnectAsync(CancellationToken ct = default);
  public Task<DiscoveryReport> DiscoverAsync(CancellationToken ct = default);   // DSC-036
  public Task<NodeSelection> ReconnectAsync(CancellationToken ct = default);    // DSC-046
  ```

  On `BastionVaultClientOptions`: `ISrvResolver? SrvResolver`, `DiscoveryConfig? Discovery`,
  `HealthConfig? Health`. On `ClientConfig`: `DiscoveryConfig Discovery`, `HealthConfig Health`.

  Four naming rulings, each with its reason:
  1. **`NodeSelection` is the type, `SelectedNode` is the member.** DSC-035 names the
     *member*. A type named `SelectedNode` would give `public SelectedNode SelectedNode`,
     C#'s "Color Color" hazard, for no gain; `rust` gets `struct NodeSelection` with field
     `selected_node` and `python` `NodeSelection` / `selected_node`, so parity is unharmed.
  2. **`…Async` suffixes** on the three new methods, against DSC-036's and DSC-046's
     `Discover()` / `Reconnect()`. Same language-idiom mapping DR-0007 applied to
     `Sys.CanAsync`; the requirement names the operation, not the C# identifier.
  3. **`ConnectAsync` is new surface that section 13 does not name.** It exists because
     `resilience.pick.priority-floor` and `resilience.pick.none-healthy` both drive the
     operation `Client.Connect` and expect the pick (or `BV-DISCOVERY-002`) as the
     *operation's own* result. Discovery cannot run in the constructor: it is async and it
     can fail, and CFG-005-style construction must stay synchronous and non-networking.
  4. **`HealthConfig.ProbeTimeout` is populated from the already-shipped
     `ClientConfig.DiscoveryProbeTimeout`** (the settings-table row at
     `specifications/02-client-configuration.md:29`, governed by `CFG-001`; the row carries
     no requirement ID of its own, and there is no `CFG-029` in the specification)
     rather than becoming a second knob for one value. An explicit `HealthConfig` on options
     wins over the resolved setting; otherwise the setting wins over the literal default.

- **D-M5-9 (discovery runs lazily, and exactly once).** The first operation on a
  discovery-mode client runs discovery before its first attempt, as if `ConnectAsync` had
  been called. `ConnectAsync` is idempotent — a second call on a pinned client returns the
  existing pick without re-probing — and `ReconnectAsync` is the only way to re-run it
  (DSC-046). `DiscoverAsync` probes and ranks without touching the pin (DSC-036) and
  without arming or disarming failover.

- **D-M5-10 (`SelectedNode` is `null` in literal mode).** DSC-001 makes literal mode "no
  DNS, no probing", so nothing was probed and nothing was chosen. Reporting a literal URL
  as `ActiveLeader` would state a health claim the SDK never verified, which D-M1c-25
  forbids as much for states as for error codes. `InputLabel` is always the raw configured
  address, in both modes. `DiscoverAsync` on a literal client returns a one-row report
  whose single candidate **is** probed — an operator asking for diagnostics has asked for
  the probe, which is the one place DSC-001's "no probing" does not bind, because no pick
  results from it.

- **D-M5-11 (the pinned endpoint becomes a per-attempt read).** `RequestExecutor` builds
  its `Uri` **once, before** the attempt loop, from `config.Address`. Failover requires the
  authority to change between attempts, so the base endpoint moves to a cell on
  `ClientContext` (`context.Endpoint`, guarded, read at the top of each attempt) and
  `BuildUri` moves inside the loop. In literal mode the cell holds `config.Address` and
  never changes, so literal-mode behaviour is byte-identical to M1b's — which the 18
  transport fixtures must continue to prove without amendment.

- **D-M5-12 (DSC-043's lock, and what a late arrival does).** One `SemaphoreSlim(1,1)` per
  `ClientContext` serialises failover. A task that acquires it compares the endpoint it
  failed on with the endpoint now pinned; if they already differ, another task has moved
  off the dead node and this one **reuses the new pick without re-probing**. Re-probing is
  bounded to the cached candidate set — no second SRV lookup (DSC-042).

- **D-M5-13 (DSC-045's node-local exclusion lands as an internal seam).** Every operation
  DSC-045 names — SSH/RDP connect sessions, `watch=1` long polls, FIDO2 ceremonies, plugin
  asset downloads, recording chunk streams — belongs to M6, M9 or M10. M5b lands the seam,
  not invented operations: an internal `bool nodeLocal = false` parameter on
  `RequestExecutor.ExecuteAsync` / `ExecuteRawAsync` that suppresses failover and surfaces
  `BV-DISCOVERY-003`, tested directly through the test assembly's existing
  `InternalsVisibleTo`. No public knob is minted: section 13 names none, and inventing one
  would pre-empt the operations' own contracts. Same precedent as M2a landing D-M2-9's
  replay accounting one milestone before the replay existed.

- **D-M5-14 (the jitter instrument carries values, not a seed).** `resilience.backoff.math-seeded`
  needs deterministic backoff. The fixture instrument is
  `settings.__jitter: { "values": [d, d, …] }`, consumed in order by an `IJitterSource`
  the harness installs — **not** a PRNG seed. A seeded PRNG produces different sequences in
  .NET, Rust and Python, so a seed would make the fixture unportable and silently
  non-parity, which is the one thing a shared fixture exists to prevent. Backoff waits
  themselves are asserted with M2c's existing `clock.delay: "virtual"` /
  `clock.expectWaits` mechanism (D-M2-27); no new clock surface.

- **D-M5-15 (new harness surface, pinned so all three languages implement one shape).**
  - Fixture `resolver` block → an `ISrvResolver` returning the declared answers verbatim;
    an owner name absent from the block resolves to **no records**, which DSC-011 requires
    be indistinguishable from a resolver failure.
  - `settings.__pinned` (string) and `settings.__candidates` (string array) → seed a
    discovery-mode client with an already-pinned node and a cached candidate set, so a
    failover fixture need not re-script the initial discovery. Used by
    `resilience.failover.read-once` and `resilience.failover.write-never`, which are on
    disk and assume them.
  - `settings.__jitter` per D-M5-14.
  - Operations, with their **result projections pinned**. `expect.result` is schema-free
    and compared structurally by `FixtureComparisons.AssertResult`, so the JSON shape *is*
    the cross-language contract and an unpinned projection is an unportable fixture — the
    same failure D-M5-14 refuses for the jitter seed.

    ```jsonc
    // Client.Connect  → ConnectAsync
    { "SelectedNode": { "Url": "…", "State": "Follower", "RttMs": 14,
                        "ClusterId": null, "Version": null } }

    // Client.Reconnect → ReconnectAsync — same projection as Client.Connect

    // Client.Discover → DiscoverAsync
    { "InputLabel": "vault.corp.example",
      "Ranked": [ { "Url": "https://bv-1.corp.example:8200", "Target": "bv-1.corp.example",
                    "Port": 8200, "Priority": 10, "Weight": 50, "State": "ActiveLeader",
                    "RttMs": 12, "ClusterHealthy": true, "ClusterId": null,
                    "Version": null } ],
      "Picked": { "Url": "…", "State": "ActiveLeader", "RttMs": 12 } }

    // Client.Classify → the DSC-001 table. args: { addresses: [...], clusterDiscovery?: bool }
    { "Rows": [ { "Input": "bv-1.corp.example:8200", "Mode": "Literal",
                  "Candidates": [ { "Url": "https://bv-1.corp.example:8200",
                                    "Target": "bv-1.corp.example", "Port": 8200,
                                    "Priority": null, "Weight": null } ] },
                { "Input": "10.0.0.5:8200", "Mode": "Literal", "Candidates": [ … ] },
                { "Input": "vault.corp.example", "Mode": "Discovery", "Candidates": [] } ] }
    ```

    `Ranked` is in rank order (DSC-033) and flattens `ProbeResult` over its `Candidate`, so
    one row renders one line of RES-020's table. `Mode` is the two-valued string
    `"Literal" | "Discovery"`; an input that raises `BV-CONFIG-001` (DSC-002's unbracketed
    IPv6) appears as an `expect.error`, not as a row, so `resilience.address.classification`
    carries the legal inputs and a .NET unit test carries the rejection.
    `Client.Classify` exists because the schema admits one operation per fixture while
    Appendix C names the classification table as a **single** fixture id; it reports what
    DSC-001's table specifies and mints no public API. It is used by that one fixture only:
    `resilience.srv.sorted-and-verbatim-underscore` uses `Client.Discover` on the single
    address `_bvault._tcp.vault.corp.example`, with a `resolver` block carrying both that
    owner name (unsorted records, to prove DSC-010's ascending sort) and the
    double-prefixed `_bvault._tcp._bvault._tcp.vault.corp.example` (which must go unqueried,
    proving the verbatim rule).
  - `expect.clientState` already exists in the driver (`FixtureDriver.cs:221`); M5 adds
    `SelectedNode.Url` to what it can address.

- **D-M5-16a (`CFG-043` is booked into M5 as its 29th ID).** `CFG-043` — "when cluster
  discovery selects a node, the SRV target hostname MUST be used for SNI and verification
  unless `TlsServerName` overrides it" (`specifications/02-client-configuration.md:106-108`)
  — is §02's twin of `RES-010`: same code path, same test, and it is baselined
  (`tools/traceability/baseline.json:25`). M5 implements its behaviour whether or not the ID
  is booked, and leaving it baselined with no owner would misreport the remaining-work
  counter. It comes off the baseline on the same evidence as `RES-010` (D-M5-16), in M5a.
  **M5 therefore lands 29 IDs, taking the baseline 234 → 205.** This is not scope widening:
  no line of code is added that D-M5-16 did not already require.

- **D-M5-16 (`RES-010`/`RES-011` are discharged by construction plus tests, not new code).**
  SNI and hostname verification already follow the request URI's host, and candidate URLs
  are built from the SRV **target** (DSC-013), so RES-010 holds as long as probes and
  requests both go through the one `ITransport` the client owns — which is also RES-011.
  The decision this records is that **probes go through `ITransport`**, not a private HTTP
  path: it is what keeps the TLS material identical, and it is what lets the scripted
  transport script `/v1/sys/health` at all. `TlsServerName`'s override is `CFG-042`,
  already landed. Both IDs come off the baseline on test evidence.

- **D-M5-17 (probes bypass the rate gate and carry no token).** Section 13's probe
  definition says both, and `resilience.pick.priority-floor` asserts the absent
  `X-BastionVault-Token` header. Probes are therefore issued outside `RequestExecutor`'s
  token and rate-gate path; they are not caller operations and do not appear in
  `Error.Attempts`. They **are** reported to the observability hook, because RES-002 says
  every attempt is. Recorded plainly: this *adds* observable behaviour to `RES-002`, an ID
  already off the baseline (D-M5-2), rather than restating it — `RES-002` sits under §13's
  retry-policy heading and a probe has no attempt number, so the hook reports probes with
  attempt number `0`. No shared fixture covers it; the shape is pinned here so the Stage 2
  parity pass has one answer to copy instead of three to reconcile.

- **D-M5-18 (`ParseAddress` classifies two ways today; DSC-001 needs six, and two landed
  public values change).** `ConfigurationResolver.cs:405-428` returns
  `IsClusterName = true` for **anything** without `://`, so `bv-1.corp.example:8200` and
  `10.0.0.5` are cluster names today and `DSC-001` makes both **literal**. M5a therefore
  flips the value of the already-public `ClientConfig.AddressIsClusterName` /
  `AddressUri` pair for those inputs. That is a deliberate correction, not a breaking
  change to defend: no landed fixture uses a non-`://` address, the pair has no documented
  meaning beyond §13's own classification, and leaving it wrong would mean shipping
  `DSC-001` and a member that contradicts it. `DSC-002`'s unbracketed-IPv6 rejection joins
  DR-0003's fixed validation order **at position 1**, inside the existing
  `BV-CONFIG-001` check, so the first-failure-wins order (D-M1a-5) is unchanged.

- **D-M5-19 (`DiscoveryConfig`'s four settings get no §02 table row and no env var).**
  `specifications/02-client-configuration.md` claims to list every setting, and
  `SrvService`, `DefaultScheme`, `DefaultPort` and `ResolveTimeout` have no row and no
  `BASTIONVAULT_*` variable. M5 does **not** mint env vars for them: adding a configuration
  surface §02 does not specify is a `specifications/` change, which is R3 and not M5's
  (CRS-004). They are constructor-settable via `BastionVaultClientOptions.Discovery` only.
  The divergence is recorded here so a later agent reads a decision instead of reopening
  the question.

## Addendum 1 — rulings arising from M5a's handback review

M5a's handback was reviewed by a Strategic Claude Opus 5 agent, which returned **block** on
one finding plus five required fixes. The blocking finding needed a decision this record had
not taken, which is why it is an addendum rather than a patch. Five further decisions were
needed to close the review; all six are below and all are binding on M5a's fix pass and on
M5b.

- **D-M5-20 (`DSC-022` is surfacing-only; `DSC-033`'s rank list is closed). The blocking
  ruling.** `DSC-022` ends "…so ranking **can** prefer healthy nodes as a tiebreak after
  RTT". That clause is a **rationale for surfacing the field, not a ranking obligation**:
  `DSC-022`'s two MUSTs are "MUST NOT by itself change the state" and "MUST be surfaced as
  `ProbeResult.ClusterHealthy`", and both are met. The normative rank is `DSC-033`, which
  is exhaustive, closed and explicitly "Deterministic — **not** RFC 2782
  weighted-random": `ActiveLeader` before `Follower`, then lower `RttMs`, then higher SRV
  weight, then lexical URL. Inserting a fifth key `DSC-033` does not name would widen a
  list the specification closed, which is exactly what D-M1c-25 forbids, and it would make
  .NET rank differently from any Stage 2 implementation that read `DSC-033` literally.
  **Consequence for `resilience.probe.classify`:** its `Ranked` order stands unamended.
  The unhealthy follower precedes the healthy one there because both probes have equal
  `RttMs` and equal weight, so `DSC-033`'s **lexical-URL** tiebreak decides — not a
  cluster-health judgement. `DSC-022` stays in that fixture's `requirements` array,
  because the fixture does exercise both of its MUSTs; the fixture's `title` is amended to
  name the lexical fallback, so no later reader mistakes the row order for a health
  preference. Preferring healthy nodes is a behaviour change to `DSC-033` and therefore a
  `specifications/` change: R3, out of M5, and recorded as `ROADMAP.md` risk **R-17**.

- **D-M5-21 (`DSC-001` row 2 is decided by the written address, not by the resolved port).**
  The classifier must ask whether the **raw string carries an explicit `:port`**, never
  `Uri.IsDefaultPort` — which is true for `http://vault.corp.example:80`, an address the
  §13 table's row 2 makes *literal*, and which would therefore route an operator's
  explicit `:80` to SRV discovery and silently replace it with `DefaultPort` 8200. The
  table's discriminator for row 6 ("`http://` prefix on a cluster name") is portless and
  non-IP; that reading of the table is correct, only its encoding was wrong.

- **D-M5-22 (probes are *issued* in candidate order; completion may overlap).** `DSC-020`
  bounds probes in flight and specifies no wire order, but every scripted fixture matches
  exchanges **by sequence**, so an unpinned issue order makes six shared fixtures
  order-nondeterministic in whichever language's scripted transport yields first. The
  invariant, binding on all three languages: **the Nth candidate's probe is issued before
  the (N+1)th**, including when a bounded slot must free first; completions may interleave
  freely. This is a harness-and-implementation contract, not a change to `DSC-020` — it
  pins something the specification leaves open, which is what a decision record is for.
  It must be stated in the implementation, not left resting on a runtime's habit of
  completing a synchronous task inline.

- **D-M5-23 (`DSC-010` is covered; shipping an `ISrvResolver` is not M5's).** The .NET BCL
  exposes no DNS SRV API. What `DSC-010` asks of the *SDK* — compose the owner name
  `_bvault._tcp.N`, or use `N` verbatim when it starts with `_`; strip trailing dots from
  targets; sort ascending by priority — is implemented and tested, and `DSC-014` requires
  the resolver be **injectable**, not that one ship. So `DSC-010` comes off the baseline.
  But an application that supplies no resolver gets `DSC-011`'s "no records" path and
  therefore literal behaviour where it asked for discovery, and that must not be a
  surprise: it is booked as `ROADMAP.md` risk **R-16**, owned jointly by M11 (the
  documentation MUST say a resolver is required for real discovery) and M12 (the
  live-server suite needs a concrete resolver to run against a cluster at all). Adding a
  DNS dependency is an R2 decision with a supply-chain dimension and belongs to whichever
  of those milestones takes it, not to M5.

- **D-M5-24 (a fixture's `requirements` array claims only what the fixture exercises).**
  Two of M5a's four over-claimed. `resilience.address.classification` drops `DSC-012` — it
  asserts `"Candidates": []` on every discovery row, so it never exercises the synthesised
  literal candidate. `resilience.pick.leader-over-follower-rtt-weight` drops `DSC-032` —
  all four of its candidates share one `cluster_id`, so the minority-drop path is never
  taken. Both rules stay covered by .NET unit tests. The general rule, which the
  traceability tool cannot enforce and a reviewer must: **an ID in a fixture's
  `requirements` array is a claim that the fixture would fail if that requirement were
  broken.** An ID that merely appears nearby is a false traceability entry, and a false
  entry in the artefact whose whole function is traceability is worse than a missing one.

- **D-M5-25 (`Render()`'s no-pick line is pinned).** §13:143-152 shows only the
  picked case. The empty case is `Picked: none`, and a row whose probe returned no RTT
  prints `-` in the `RTT(ms)` column. Pinned here rather than left to each language,
  because `RES-020`'s output is a shared, asserted artefact.

## Addendum 2 — rulings arising from M5b's handback

M5b paused and escalated one R3-shaped finding (CRS-005), correctly: repairing a landed,
shared fixture is a `specifications/` change. Both rulings are below.

- **D-M5-26 (`resilience.failover.read-once` carries a body section 07 forbids; the fixture
  is repaired).** The fixture's third exchange returns `data.metadata` as `{"version": 1}`.
  Section 07's type block declares the KV v2 metadata object as
  `{version, created_time, deletion_time, destroyed}` — `created_time` without `?` — and
  **D-M4-12** (accepted, R2) ruled its absence a server-contract violation raising
  `BV-PROTOCOL-002`. All four landed `kv.v2.read-*` fixtures carry the field. This fixture
  was authored before M4 and encodes a response the specification it exercises does not
  permit, so the KV v2 reader is right and the fixture is wrong.
  **Ruling: repair the fixture** — add `created_time`, `deletion_time` and `destroyed` to
  that one response body, matching what `kv.v2.read-soft-deleted-state` already carries.
  Nothing else in the fixture changes: not its `requirements`, not its exchange sequence,
  not its expectations, and no requirement, behaviour, error code or public API anywhere.
  **Rejected alternative 1:** relax D-M4-12 so a missing timestamp defaults. Rejected — it
  inverts an accepted R2 ruling in order to accommodate a defective artefact, which is
  CLA-004's "never weaken a test or a gate to make something pass", and D-M4-12 already
  recorded that a live-server finding, not a fixture, is what may revisit it.
  **Rejected alternative 2:** leave the fixture pending with a named owner. Rejected — it is
  not blocked on unbuilt work, which is what every other pending fixture in this repo waits
  on; the failover behaviour it exists to prove is implemented and observed. Holding it would
  also fail M5's own exit gate.
  **Risk and gate:** R3 by CRS-004 (it edits `specifications/`). Per §5.3 the R3 gate is
  R2 plus Claude Opus 5 review plus Strategic Orchestrator acceptance plus **human
  confirmation before release** — the release gate is M12's, not M5's, so M5 proceeds and
  the confirmation is carried to the release checklist. Stage 2 inherits the corrected body;
  Rust and Python will match the repaired fixture, never the original.

- **D-M5-27 (`Sys.Health` fails over, and that is correct).** M5b flagged that
  `ExecuteHealthAsync` now participates in failover, which the brief had not named. It
  stands, and the reason is recorded so a later agent does not "fix" it: `SYS-001` makes
  `Health` **not raise** for 200/429/501/503 — a sealed node returns a successful
  `HealthStatus { Sealed = true }` — so `DSC-041` limb (ii) can never fire for this
  operation, and the only thing that fails it over is a genuine transport failure, which is
  exactly what failover is for. `Sys.Health` therefore reports "the health of my session's
  node, with the same failover as any other idempotent read"; a caller wanting per-node
  state uses `Client.Discover()` (`DSC-036`), which probes every candidate and never moves
  the pin. `DSC-045`'s exclusion list does not name it, and `DSC-042` makes every idempotent
  operation eligible, so excluding it would be the deviation.

## Addendum 3 — correcting D-M5-6 and D-M5-7 after M5b's handback review

M5b's handback review **blocked**, and it was right to: it demonstrated by execution that
`RES-001`'s cap is breached whenever the node failure arrives on a later attempt of its
pass. An idempotent read at `MaxAttempts = 3`, failover armed, two `502`s and then a
refused connection produced **6 wire attempts against a cap of 4**, with
`Error.Attempts = 6`, so the breach is caller-observable.

The cause is a latent conflict between two decisions of this record, which the
implementation resolved silently in the wrong direction instead of escalating. D-M5-7 says
total attempts never exceed `MaxAttempts + 1`; D-M5-6 says the replayed request "classifies
normally, so a post-failover `BV-SERVER-002` still retries under `CFG-050`". Those are
jointly satisfiable **only** when the first pass ends at its first attempt — which is the
one shape every failover fixture and test happened to script. `skills/claude/SKILLS.md` §7
rule 1 decides it: **specification wins.** `RES-001` is a MUST; "retries normally" was an
inference of mine.

- **D-M5-28 (the failover replay's budget is bounded by the remaining global budget;
  D-M5-6 is narrowed).** The replay pass's per-pass `maxAttempts` becomes
  `max(1, MaxAttempts + 1 - AttemptsBefore)` rather than a fresh `MaxAttempts`, and D-M5-6's
  sentence is narrowed to "classifies normally, **within the remaining `RES-001` budget**".
  The code comment asserting that "a triggering failure always ends its own pass on its
  first attempt" is **false** and is deleted, not reworded.
  **Why this option and not the alternative.** The review offered a second reading that also
  satisfies the MUST: let a node failure on a later attempt **forfeit** failover, so the
  `+1` literally is the replay. Rejected, and not on the operator-experience grounds the
  review gave — on a harder one. `DSC-042` makes the single replay a **MUST**, conditioned
  only on failover being armed and the operation idempotent; it says nothing about which
  attempt the node died on. Forfeiting the replay would satisfy `RES-001` by violating
  `DSC-042`. The bound above satisfies both, because `AttemptsBefore` can never exceed
  `MaxAttempts` and the clamp therefore always leaves the replay at least one attempt.
  It also keeps `resilience.failover.read-once` satisfiable at `MaxAttempts: 1`
  (1 + 1 = 2 = `MaxAttempts + 1`).
  **Why D-M5-7's mechanism was the trap.** D-M5-7 told the implementer to reuse D-M2-9's
  two-counter rule *verbatim*. That rule deliberately gives the AUT-003 relogin replay a
  **fresh** budget, and correctly so — `RES-001` governs the failover replay and says
  nothing about a relogin replay. The two replays are governed by different requirements
  and cannot share one accounting rule. The bound therefore applies to the **failover**
  replay only, which means the loop must be able to tell which kind of replay it is in:
  `AttemptsBefore` is non-zero for both, so the discriminator is a field on
  `RequestExecution`, not an inspection of the counter.
  **Test obligation:** the adverse shape — retries spent *before* the node failure —
  becomes a standing test asserting both wire attempts and `Error.Attempts` ≤
  `MaxAttempts + 1`. Its absence is why 848 green tests did not catch this, and that is a
  test-adequacy failure as much as a code one (`claude.md` §3.1 step 6).
  **Stage 2:** this is the decisive reason to fix it now rather than book it. The current
  behaviour is about to be transcribed into Rust and Python as a settled contract.

- **D-M5-29 (a failover replay must not mint a second re-login; the deeper tension is
  M6's).** Failover wraps re-login (`RequestExecutor` lines ~140, ~199, ~276), so the
  replay re-enters `RunWithReloginAsync` with a **fresh** one-shot and one caller-visible
  operation can re-login twice, against D-M2-9's "one caller call, one replay". The
  30-second `LoginOptions.MinReloginInterval` default hides it; an application setting it to
  zero does not. **This half is M5's own doing and M5 fixes it:** thread the relogin
  one-shot through `RequestExecution`, the way the failover step is already threaded, so the
  replay inherits a spent re-login.
  **The other half is pre-existing and is not M5's to take.** D-M2-9 gives the relogin
  replay a fresh `MaxAttempts`, so a relogin replay alone can exceed `MaxAttempts + 1`
  without any failover involved. That tension between D-M2-9 and `RES-001` predates this
  milestone, and resolving it means reopening an accepted M2 ruling — R3, and out of M5's
  scope (CLA-007). It is booked as `ROADMAP.md` risk **R-18**, owned by **M6**, where
  AUT-003's replay is actually exercised. M5 guarantees only that *failover* never causes
  the cap to be exceeded.

- **D-M5-30 (two fixture defects in M5b's own authored files).** `resilience.backoff.math-seeded`
  claims `RES-003` in full while its `MaxBackoff: PT5S` against waits of 80 ms and 240 ms
  never exercises the `min(MaxBackoff, …)` cap — D-M5-24's rule, applied to a fixture
  authored one addendum after the rule. Set `MaxBackoff` so the second wait clips. The same
  fixture's third exchange also carries `"metadata": {"version": 1}` — the exact body
  D-M5-26 ruled a server-contract violation. It passes only because `Logical.Read` never
  runs the KV v2 reader, which makes it a trap for the Stage 2 pass and an inconsistency
  with an R3 repair landing in the same change. Complete the body.

## Consequences

- `BastionVaultClient` gains its first async, network-touching entry points. Nothing about
  construction changes: it stays synchronous, environment-only, and non-networking.
- `RequestExecutor`'s per-attempt URI construction (D-M5-11) is the one change that can
  regress landed work. Its guard is the existing corpus, which must stay green with no
  amendment: **on disk** 18 transport, 134 error, 18 auth, 20 kv and 16 sys fixtures;
  **green in .NET today** 18, 133, 18, 19 and 10 of those respectively (the remainder are
  pending with named owners — `auth.cert.disabled-server`→M6,
  `errors.enrichment.404-kv2-hint`→M7, `kv.read-many-batch`→M8, and six `sys.*`→M7).
  D-M5-5 exists so that none of them needs amending.
- The baseline goes 234 → **205** (28 `DSC`/`RES` plus `CFG-043`). `RES-030` stays on it,
  owned by M7.
- Fixtures on disk go 218 → 224. Appendix C's `resilience.*` list becomes fully realised,
  and one pre-existing member of it is repaired (D-M5-26).
- No conformance level is declared or affected; sections 16–17 remain M11's (D-M4-3, R-14).
- Addendum 3 adds `ROADMAP.md` risk **R-18** (D-M2-9's relogin replay can exceed
  `RES-001`'s cap independently of failover — M6), and carries D-M5-26's R3 human
  confirmation to M12's release checklist as an explicit line.
- Addendum 1 adds two `ROADMAP.md` risks: **R-16** (no `ISrvResolver` ships, so discovery
  is inert until an application supplies one — M11/M12) and **R-17** (`DSC-033` cannot
  prefer healthy nodes without a `specifications/` change — R3, unowned).
- `CHANGELOG.md` gains an `Added` entry per slice (REC-001); `ROADMAP.md` §2, §4, §5 and §8
  close M5 at its exit (REC-002).

## Review response — architecture review (revision 2)

The architecture review (`agents.md` §4.2 row 4, Strategic Claude Opus 5, confidence 0.88)
returned **approve with required fixes**. Dispositions, all landed in revision 2:

| Finding | Disposition |
|---------|-------------|
| **RF-1** (blocking) — `DSC-041`'s second limb unruled | **Fixed.** D-M5-5 rewritten to rule on both limbs. The reviewer's `13:125` and `CFG-053` argument is adopted: limb (ii) never replaces the Appendix B code, in either mode, and contributes only the failover trigger. The reviewer's open question 1 is answered there |
| **RF-2** (blocking) — `CFG-029` does not exist | **Fixed.** A fabricated requirement ID in a record whose function is traceability, and it inverted D-M1c-25, which this record inherits. D-M5-8 ruling 4 now cites `02-client-configuration.md:29` under `CFG-001`. Every other ID cited in this record was re-checked against `appendix-d-requirement-index.md` |
| **RF-3** — `CFG-043` implemented but left unbooked | **Fixed.** D-M5-16a books it as the 29th ID; totals corrected to 234 → 205 |
| **RF-4** — both evidence claims overstated | **Fixed.** D-M5-5 now names the three fixtures that actually assert a `BV-TRANSPORT-*` code (the reviewer found two; `errors.enrichment.tls-no-ca` is the third, and it is why limb (i)'s scope is pinned to the three failure kinds `DSC-041` names). D-M5-6 now states plainly that **no landed fixture discriminates it**, and names the fixture that will |
| **RF-5** — two portability gaps | **Fixed.** `Candidate.Priority`/`Weight` become `int?` with `null` pinned for a `DSC-012` synthesised candidate; every fixture result projection is now pinned as JSON; `Client.Classify`'s single-fixture scope and `srv.sorted-and-verbatim-underscore`'s two-owner-name `resolver` shape are both recorded |
| Routing note — cite trigger (b) for M5b | **Adopted.** (b) is now the primary ground, (d) the secondary |
| `RES-002` extended to probes | **Adopted** as an explicit addition in D-M5-17, with the attempt number pinned at `0` |
| `ParseAddress` classifies two ways | **Adopted** as D-M5-18 — it flips two already-public values, so it needed a decision, not a note |
| Corpus counts misstated | **Fixed** in Consequences: disk counts and .NET green counts are now given separately |
| `Render()` sketch not compilable | **Fixed** — a pinned-API block is transcribed literally by delegates |
| `DiscoveryConfig` absent from §02's table | **Adopted** as D-M5-19: no env vars minted, divergence recorded |

## Review response — M5a handback (addendum 1)

The M5a handback review (Strategic Claude Opus 5, verdict **block**, own confidence 0.89,
author confidence recomputed 0.87 → 0.79 under `CCF-002`) is answered as follows.

| Finding | Disposition |
|---------|-------------|
| **B-1** (blocking) — `DSC-022`'s ranking tiebreak absent, and `resilience.probe.classify` pins its negation | **Ruled, not patched.** D-M5-20: `DSC-022` is surfacing-only and `DSC-033`'s list is closed, so the implementation is correct and the fixture stands. The reviewer's alternative — fold `ClusterHealthy` in after RTT — was rejected because it widens a list `DSC-033` closes and would diverge from any Stage 2 implementation reading `DSC-033` literally. The reviewer was right that the record had not ruled; it was the one genuine gap. The fixture `title` is amended to name the lexical fallback so the row order cannot be misread |
| **RF-1** — `Uri.IsDefaultPort` misclassifies `http://host:80` | **Accepted as a defect.** D-M5-21. Latent, not a landed regression — no fixture uses that shape — but `DSC-001` is off the baseline, so it must be right |
| **RF-2** — the open-question-3 amendment is not in the tree | **Accepted.** `ConnectAsync`/`ReconnectAsync` become `Task<NodeSelection?>`, literal mode does not probe and returns `null`, `ProbeConfiguredEndpointAsync` is deleted. The reviewer's note that `PublicApiSurface.txt` cannot detect nullability is itself a finding about the gate, recorded here |
| **RF-3** — probe issue order deterministic only by accident | **Accepted.** D-M5-22 pins issue order as a cross-language invariant. The reviewer's evidence — that `DiscoveryEngine.cs:297` asserts the conclusion without naming its premise — is exactly right |
| **RF-4** — `DSC-010` off the baseline with no shipping resolver | **Ruled.** D-M5-23: the ID stays covered, and the real gap becomes `ROADMAP.md` R-16 with named owners, rather than a silent surprise for an application |
| **RF-5** — two `requirements` arrays overstate | **Accepted.** D-M5-24, generalised into a reviewable rule |
| Non-blocking: diff noise; double classification; `Render()`'s no-pick line; `AmbiguousIpv6` hint wrong for a portless input | **All accepted.** The `Render()` case is pinned as D-M5-25 because it is a shared artefact; the other three go into the fix pass |
