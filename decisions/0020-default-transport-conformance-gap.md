# DR-0020 — `Transport` does not default to HTTP, and the SDK's primary construction path throws

**Status:** accepted (remediation), revision 1 (2026-09-22). Authored by the Strategic
Orchestrator on a finding returned by M11 slice a.

**Risk tier:** **R2** (`agents.md` §5.3). Public observable behaviour of the primary
construction path changes; the transport is the seam every request crosses. Not R3: no
`specifications/` amendment is made — the specification is the side that is already
correct — and no published artefact is affected.

**Date:** 2026-09-22 · **Found by:** M11 slice a, on its first compiled documentation sample

**Amends nothing in `specifications/`. Implements what `specifications/02-client-configuration.md:37`
already requires.**

## Problem

`new BastionVaultClient(new BastionVaultClientOptions { Address = …, Token = … })` — the
construction every reader will write first — **compiles, and then throws on the first
request**:

```
InvalidOperationException: No transport is configured on this client (OVR-001).
```

raised at `dotnet/BastionVault.IntegrationSdk/Internal/RequestExecutor.cs:939` and
`Internal/DiscoveryEngine.cs:484`. `BastionVaultClient.Transport` is `ITransport?`, assigned
straight from `effectiveOptions.Transport` with no fallback, so it is `null` unless the
caller supplies one. The working construction is three lines and is documented **nowhere** —
not in `dotnet/README.md`, not in `specifications/`, and no test in the repository builds a
working end-to-end client that way.

**The specification does not permit this.** `specifications/02-client-configuration.md:37`:

| Canonical setting | Type | Default | Environment variable(s) | Notes |
|---|---|---|---|---|
| `Transport` | implementation | **HTTP** | — | Injection point for tests (OVR-001). |

The default is **HTTP**, and injection is described as the *test* path, not the required
one. `appendix-b-error-catalogue.md`'s `BV-CONFIG-009` hint independently assumes the same
thing — "Use **the SDK's default transport** or an HTTP client that allows non-standard
methods". Two documents therefore describe a default transport that the .NET implementation
does not have. **This is a conformance defect against a settled default, not a design choice
and not a missing feature.**

Two aggravating facts:

1. **`dotnet/README.md`'s quick start does not work as written.** It is labelled
   "illustrative, not compiled" — and it is wrong. This is precisely the class of defect
   `DOC-003` exists to catch, found by the very first sample compiled under the M11 contract
   ([DR-0018](0018-m11-documentation-and-usage-guides.md) D-M11-3). The mechanism earned its
   cost before the milestone that introduced it finished its first slice.
2. **The error is the wrong kind.** An `InvalidOperationException` carries no `BV-*` code, no
   hint, no `Retryable`, and does not flow through the error model sections 04 and
   Appendix B define for every other misconfiguration. A caller cannot catch it as
   `BastionVaultException` and cannot switch on a code.

## Decision

**D-1 — `BastionVaultClient` defaults `Transport` to the HTTP transport when the caller
supplies none**, matching `02-client-configuration.md:37`. Explicit injection keeps working
unchanged; that is OVR-001's actual purpose and no test that injects a transport may change
behaviour.

**D-2 — Ownership follows construction.** A transport the client created, the client
disposes; a transport the caller injected, the caller owns and the client MUST NOT dispose.
This is the one genuine design question in the change, it is not settled by any existing
record, and it is settled here so the delegate does not settle it by accident.

**D-3 — The `InvalidOperationException` paths stay, and become unreachable-by-configuration.**
Once a default exists, a null transport can only arise from a caller explicitly passing
null. Both throw sites are retained as internal invariants rather than deleted, because
deleting a guard because you believe it is now unreachable is how it becomes reachable
again. They are **not** reclassified to a `BV-CONFIG-*` code: that would require minting a
catalogue entry, which is a `specifications/` change (R3) for a path no correctly-written
program can now reach.

**D-4 — Parity is owed, not forgiven.** `rust/` and `python/` implement the same table row
at **M13**. The .NET fix does not create a divergence — it removes one, since .NET was the
language disagreeing with the specification. Recorded so M13 implements the default rather
than copying .NET's former behaviour.

**D-5 — Sequenced before M11's content slices, deliberately.** M11 slices b, c, d1, d2 and e
write 13 guides and 11 engine pages, all of which show client construction. Landing this
after them means rewriting every one; landing it before means they are written once, in the
form a reader will actually use. **The cost of this fix multiplies by the number of guides
written before it**, which is the whole argument for interrupting the milestone rather than
queueing the fix behind it. D2 is the only guide already written and is slice a's to amend.

## Rejected alternatives

| Option | Why rejected |
|---|---|
| **Leave the behaviour; fix `dotnet/README.md`'s snippet to show the three-line form** (slice a's option (a)) | Documents a defect instead of fixing it. The specification says the default is HTTP; a guide teaching the workaround makes the workaround the contract, and M13 would then implement it in two more languages |
| **Reclassify the throw to a new `BV-CONFIG-*` code** (slice a's option (c), its own recommendation) | Treats the symptom. It requires a new Appendix B entry — a `specifications/` change, R3, three catalogue regenerations — to improve the error message on a path that should not exist. Slice a proposed it as the cheap option; it is the expensive one *and* it leaves the SDK unusable in one line |
| **Add a `BastionVaultClient.Create(...)` convenience factory** | A new public API to work around a default that should already exist, and a new name for M13 to implement twice. D-1 needs no new public surface at all |
| **Defer to M13, when the parity cost is being paid anyway** | Leaves Stage 1 shipping an SDK whose documented first example throws, and forces every M11 guide to teach the workaround (D-5) |

## Acceptance criteria

1. `new BastionVaultClient(new BastionVaultClientOptions { Address, Token })` performs a real
   request with no further setup. A test asserts it, and that test is the one
   `dotnet/README.md`'s quick start will later be checked against.
2. Explicit injection is unchanged: every existing test that supplies a transport passes
   untouched, and an injected transport is **not** disposed by the client. Both directions
   asserted.
3. The unit suite stays green (1652+) and coverage stays above the 95 % floor (`CNF-010`).
4. `PublicApiSurface.txt` changes **only** if a member is genuinely added; a defaulted value
   is not a surface change. Any diff is justified line by line.
5. D2 and its samples are updated to the one-line construction, and the DocsSamples drift
   check stays green (**DR-0018** D-M11-3).
6. `CHANGELOG.md` records it under **Fixed** (**REC-001**) — this is user-visible behaviour.
