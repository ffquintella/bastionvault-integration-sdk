# DR-0005 — M1c error model: the generated catalogue, message recognition and hint enrichment

**Status:** Accepted · **Date:** 2026-09-14 · **Milestone:** M1c ([`ROADMAP.md`](../ROADMAP.md) §5)
**Author:** Strategic Orchestrator (Claude Opus 5) · **Risk tier:** R3 · **Size tier:** Large
**Opus trigger:** M1 is R3 and this slice fixes the public error surface every later
milestone raises errors through ([`ROADMAP.md`](../ROADMAP.md) §5 M1 note, risk R-1;
[`claude.md`](../claude.md) §4).
**Extends:** [DR-0004](0004-m1b-transport.md) D-M1b-4 (the status→code seam), D-M1b-4b
(`Retryable` vs `RetryOn`), D-M1b-4c (`Details` key spelling), D-M1b-21 (unmapped status).
**Requirements in scope:** `ERR-001…006`, `ERR-010`, `ERR-020…022`, `ERR-030…037`,
`ERR-040`, `ERR-050`, `CNF-043`.
**Explicitly out of scope:** `ERR-060`, `ERR-061` (see D-M1c-9).

These decisions are made. No delegate reopens them (TOK-008). A delegate that believes a
decision here is wrong escalates ([`skills/codex/SKILLS.md`](../skills/codex/SKILLS.md) §3
rows 9–10); it does not choose differently.

## Context

M1a and M1b both raised errors against a catalogue that was **hand-transcribed** from
Appendix B — 9 rows at M1a, 24 at M1b, in three languages independently. Appendix B holds
roughly 130 codes and roughly 90 recognition rows. Transcribing the remainder by hand is
three chances per row to drift on a message, a hint, a category or a retryability flag, and
none of the three existing gates can see that drift: fixtures pin wire behaviour, coverage
pins executed lines, traceability pins requirement IDs. This is risk **R-9** in its purest
form, on the largest table in the specification.

The roadmap already anticipated this: M1c's entry says the mapping is "generated or
table-driven in all three languages, never hand-transcribed" (`ROADMAP.md` §5). This record
decides *which*, and pins the surface.

Three further facts shape the slice:

1. **The seam is already there and must not move.** D-M1b-4 landed `StatusCodeMapper.Map`
   whole, in its final home, with its final signature, and said in the source comment that
   M1c "adds step-by-step message recognition ahead of this function … it MUST NOT reshape
   this function or the branches already here." That holds.
2. **Half of the enrichment table needs state M1c does not have.** Two of the nine
   enrichment rows read a mount cache (`Sys.ListMounts`) or `capabilities-self` output,
   which arrive at M3/M4.
3. **Some Appendix C error fixtures name operations that do not exist yet.** The already
   on-disk `errors.format.one-line` fixture drives `Auth.Token.Lookup`, which is M2.

## Decisions

### D-M1c-1 — The catalogue is generated from Appendix B; hand-transcription ends here

**Problem.** ~130 codes × 4 columns × 3 languages = ~1560 strings that must be identical,
maintained forever, with no gate that can see a mistake.

**Decision.** One generator, `tools/error-catalogue/`, written in Python 3 to match
[`tools/traceability/traceability.py`](../tools/traceability/traceability.py):

1. **Parse** `specifications/appendix-b-error-catalogue.md` §1 (codes) and §2 (recognition)
   into one canonical intermediate, `tools/error-catalogue/catalogue.json`, checked in.
   Parsing is strict: a malformed row, a duplicate code, an unknown category prefix or a
   recognition row naming a code that §1 does not define is a hard error, not a warning.
2. **Emit** generated source for each language from that intermediate:
   - `dotnet/BastionVault.IntegrationSdk/Generated/ErrorCatalogData.g.cs`
   - `rust/bastionvault-integration-sdk/src/generated/error_catalog_data.rs`
   - `python/src/bastionvault_integration_sdk/_generated/error_catalog_data.py`
3. **Emit** the code constants (ERR-005) into the same generated files, replacing the
   hand-written bodies of `ErrorCodes` / `error_codes` / `ErrorCodes`. The public *type*
   names stay exactly where they are; only their contents become generated.
4. **Emit** the recognition rule list (D-M1c-3) in Appendix B §2 table order.

**Gate.** `repo-gates.yml` gains one job: run the generator and `git diff --exit-code`. A
hand edit to a generated file, or a spec edit without regeneration, fails CI. Per
`ROADMAP.md` §2's M1b carry-forward, **this gate is proven by a seeded violation and a
revert**, like every other gate in this repository; the proof goes in the exit record.

**Rejected — runtime load of a JSON data file.** The SDK would need to ship and locate a
data file at runtime. D-5 loads fixtures from the repository for *tests*; a shipped library
resolving a file path at import time is a different and worse thing.

**Rejected — one embedded JSON blob parsed at startup in each language.** Byte-identical by
construction, but it hides ~130 codes behind an opaque string, costs a parse on first use,
and puts nothing in the public-API diff. Generated source gives the same single-source
guarantee (all three emitters read one intermediate) and stays readable and reviewable.

**Rejected — each language parses Appendix B at build time.** Three markdown parsers to
maintain, three chances to parse differently, and a build that depends on the spec tree.

**Consequence.** Appendix B becomes executable. A spec change to a hint is a one-command
change in three languages. It also means **the generator, not a reviewer, is the parity
instrument for this table** — review effort moves to the generator and to the rules below.

### D-M1c-2 — Generated constant names: category token + Appendix B `Name`, never doubled

The constant name is `<CategoryToken><Name>`, where `Name` is Appendix B's `Name` column
verbatim and `CategoryToken` is fixed:

| Prefix | Token | Prefix | Token | Prefix | Token |
|---|---|---|---|---|---|
| `BV-CONFIG` | `Config` | `BV-CONFLICT` | `Conflict` | `BV-KV` | `Kv` |
| `BV-INPUT` | `Input` | `BV-RATE` | `Rate` | `BV-TRANSIT` | `Transit` |
| `BV-TRANSPORT` | `Transport` | `BV-QUOTA` | `Quota` | `BV-PKI` | `Pki` |
| `BV-PROTOCOL` | `Protocol` | `BV-SERVER` | `Server` | `BV-SSH` | `Ssh` |
| `BV-AUTH` | `Auth` | `BV-DISCOVERY` | `Discovery` | `BV-TOTP` | `Totp` |
| `BV-AUTHZ` | `Authz` | `BV-NOTFOUND` | `NotFound` | `BV-IDENTITY` | `Identity` |
| | | | | `BV-RUSTION` | `Rustion` |

If `Name` already starts with the token, the token is not repeated (`BV-RATE-001`
`RateLimitedByDosGuard` stays `RateLimitedByDosGuard`, not `RateRateLimitedByDosGuard`).
Rust is the `SCREAMING_SNAKE_CASE` of the same identifier; Python likewise.

**This renames exactly one shipped constant**: `BV-RATE-002` becomes
`RateNamespaceRateQuotaExceeded` (from `RateNamespaceQuotaExceeded`) in all three
languages, because Appendix B names it `NamespaceRateQuotaExceeded`. It is a breaking
public-API change, allowed pre-1.0 (`CNF-040`, current version 0.3.0), it updates the three
CNF-027 baselines, and it gets a `CHANGELOG.md` line. Taking the rename now is cheaper than
carrying a permanent hand-maintained exception in the generator.

### D-M1c-3 — Recognition is one ordered rule list, applied before the status table

`ERR-020` requires steps 1–5 in one function. That function already exists
(`StatusCodeMapper.Map` / `mapping.rs` / `logical.py`'s mapper). M1c inserts step 4 ahead of
step 5 inside it, as a call to one generated, ordered rule list.

**Normalisation, applied once to the server message before any rule runs:** trim; strip a
single trailing `.`; strip a trailing `(retry after Ns)` suffix (case-insensitive, any
integer `N`, optional surrounding whitespace); lower-case. The *original* server message is
preserved unchanged on `Error.ServerMessage` — normalisation is matching-only.

**Rule kinds**, exactly three, as Appendix B §2 spells them: `exact`, `prefix`, `contains`.
A rule may carry a **status guard** (`(5xx)`, `(409, recordings)`, `(500)`) and is skipped
when the guard does not hold. A rule row listing alternatives separated by `/` compiles to
one rule per alternative, in the order written. A row combining a stem and a `contains`
qualifier (`batch has` + `exceeds max`) compiles to a rule requiring both.

**First match wins, in Appendix B §2 table order.** The table's order is normative and the
generator must preserve it; the ordering is what makes `contains (5xx) standby` reachable
without shadowing the exact rows above it.

**No match ⇒ fall through to the D-M1b-4 status table, unchanged.**

### D-M1c-4 — `Details` extraction is an explicit table, never inferred

Seven recognition rows carry a variable part that `ERR-035` requires in `Details`. The
capture pattern is pinned here, per row, and lives in the generator's input — the generator
does not infer a regex from prose:

| Recognition row | `Details` key | Value |
|---|---|---|
| `cannot assign policy <name>` | `policy` | the policy name |
| `account temporarily locked` (+ `retry after Ns`) | `retry_after_secs` | `N` as an integer |
| `source address <ip> … unauthorized` | `source_ip` | the address |
| `batch has N operations, exceeds max M` | `count`, `max` | integers |
| `meta key(s) … are reserved` | `keys` | the listed keys, in order |
| `no policy named <name>` | `policy` | the policy name |
| `no such namespace <path>` | `namespace` | the path |

Keys are spelled as Appendix B spells them (D-M1b-4c). A capture that fails to match on a
message the rule matched is **not** an error: the code is still assigned and the key is
simply absent. Recognition must never be more fragile than the code it produces.

### D-M1c-5 — Enrichment is a separate ordered function, and two rows are deferred

`ERR-040` requires deterministic, fixture-covered enrichment. Enrichment runs **after** the
catalogue hint is attached, appends notes to `Hint` separated by a single space, in the
table order of [`04-error-model.md`](../specifications/04-error-model.md#hint-enrichment-from-context),
and never rewrites the catalogue hint.

Landing at M1c — the seven rows decidable from client-side state alone:

`403` + empty `Namespace` under `auth/`/`secret/`; `400 API version mismatch`; `429` with
`Retry-After`; `503 sealed`; `500 Logical backend path not supported.`; TLS verification
failure with `CaCertPath` unset; connection refused to the default address.

**Deferred, each with an owning milestone:**

- `403` + `Details.namespace_operable == false` — needs `Sys.CapabilitiesSelf`. **M3.**
- `404` on a KV v2 mount — needs the `Sys.ListMounts` cache. **M4.**

The enrichment function ships with both rows present as unreachable-by-construction
branches removed, not stubbed: a stub is dead code the coverage gate must then excuse
(`CNF-010`, no exclusion pragmas). The owning milestone adds the branch and its fixture.

### D-M1c-6 — `CNF-043` is a recognition row, not a special case

`logical backend path not supported` → `BV-SERVER-004 UnsupportedByServer` is already a row
in Appendix B §2 and needs no bespoke code path. `CNF-043` leaves the baseline on the
strength of that row plus its fixture, and the `errors.enrichment` note for it.

### D-M1c-7 — The public catalogue surface, pinned member by member

`ERR-036` names `ErrorCatalog.Get(code)` — note the spelling, **`ErrorCatalog`, not
`Catalogue`**. The existing internal `.NET` `ErrorCatalogue` is renamed and made public.
Per `ROADMAP.md` §7's post-M1a rule, every member is pinned:

**.NET** (`BastionVault.IntegrationSdk`)
```
public sealed record ErrorCatalogEntry(
    string Code, string Name, ErrorCategory Category,
    string Message, string Hint, bool Retryable);

public static class ErrorCatalog
{
    public static ErrorCatalogEntry? Get(string code);
    public static IReadOnlyList<ErrorCatalogEntry> All { get; }   // Appendix B order
}
```

**Rust** (`bastionvault_integration_sdk::error_catalog`)
```
pub struct ErrorCatalogEntry { code, name, category, message, hint, retryable }  // pub getters, &'static str
pub struct ErrorCatalog;
impl ErrorCatalog {
    pub fn get(code: &str) -> Option<&'static ErrorCatalogEntry>;
    pub fn all() -> &'static [ErrorCatalogEntry];
}
```

**Python** (`bastionvault_integration_sdk`)
```
@dataclass(frozen=True)
class ErrorCatalogEntry:
    code: str; name: str; category: ErrorCategory; message: str; hint: str; retryable: bool

class ErrorCatalog:
    @staticmethod
    def get(code: str) -> ErrorCatalogEntry | None
    @staticmethod
    def all() -> tuple[ErrorCatalogEntry, ...]
```

`Get` returns null/`None`/`None` for an unknown code — it does not throw. `All` is ordered
as Appendix B orders the codes, so generated documentation is stable.

Recognition and enrichment stay **internal** in all three languages. They are an
implementation of `ERR-020`, not a supported extension point, and making them public would
freeze the rule representation.

### D-M1c-8 — `Retryable` stays computed from `ERR-006`, not from the generated `R` column

Appendix B's `R` column and `ERR-006`'s list must agree, but `ERR-006` is the normative
sentence. The generator emits `Retryable` from the `R` column **and** asserts, at generation
time, that the resulting true-set is exactly `BV-TRANSPORT-001/002/003`, `BV-SERVER-002`,
`BV-SERVER-003`, `BV-RATE-002`, `BV-DISCOVERY-003`. A mismatch fails generation. This keeps
D-M1b-4b's separation intact: `Retryable` is reported; `RetryPolicy.RetryOn` is what the SDK
actually retries, and the two are still not the same set.

### D-M1c-9 — `ERR-060` and `ERR-061` do not land at M1c

`ERR-060` (a published *Error Reference* page per SDK) and `ERR-061` (per-operation code
lists) are documentation deliverables of section 16 and depend on the per-language doc
sites, which are **M11**. The generator makes `ERR-060` nearly free when M11 arrives — one
more emitter over the same intermediate — and that is the point of D-M1c-1. `ERR-061`
additionally needs typed operations that do not exist before M2.

Both stay on the traceability baseline with **M11** recorded as the owning milestone. M1c
therefore removes **21** IDs, not the ~33 the roadmap estimated; the estimate counted
Appendix B rows, not requirement IDs.

### D-M1c-10 — Fixtures: generated for recognition, hand-authored for enrichment

Appendix C says the recognition fixtures are "generated" — the same generator emits
`specifications/fixtures/errors/errors.recognition.<code>.<n>.json`, one per recognition
row, driving `Logical.Read` (or `Logical.Write` where the row is write-only) with the row's
server text and asserting the expected code.

- **Fixture ids are stable and new fixtures append** (Appendix C). The four hand-authored
  `errors.recognition.*` fixtures already on disk keep their current ids; the generator
  skips rows those four already cover and never renames or deletes an existing file.
- The five `errors.enrichment.*` fixtures are hand-authored, not generated.
- `errors.format.one-line` drives `Auth.Token.Lookup` (M2) and therefore **stays
  `pending`** after M1c. `ERR-002`/`ERR-003` are covered instead by unit tests over the
  one-line form and the path redaction, plus a `Logical.Read` fixture on a
  `auth/token/lookup/<token>`-shaped path. Do not edit the fixture to fit the milestone
  (CLA-004).

### D-M1c-11 — `ERR-050`: warnings surface, plus the log line

All three already carry an empty-by-default `Warnings` list on the response. M1c adds only
the missing half: when a server *does* send `warnings`, log each at **warning** level
through the existing `IClientLogger` / `logger` seam, and never convert one to an error. One
fixture and one unit test per language.

## Public API shape

Net additions: `ErrorCatalog`, `ErrorCatalogEntry` (3 languages). Net change: `ErrorCodes` /
`error_codes` grows from ~35 to ~130 constants, generated. One rename (D-M1c-2). Nothing
else in the public surface moves — in particular `BastionVaultException` / `Error` keeps its
M1a shape, and the transport seam is untouched.

Three CNF-027 baselines are regenerated as part of the change, and the diff is reviewed as a
diff, not accepted wholesale (D-M1b-19: the .NET gate was inert once already).

## Consequences

- Appendix B becomes the single source for ~1560 strings across three languages, enforced
  by a CI gate rather than by review attention.
- The cost moves up-front: the generator and its rule compiler are the review surface, and
  they are reviewed as production code even though they ship nothing.
- Two enrichment rows and both documentation requirements leave M1c with named owners, so
  the baseline stays an honest remaining-work counter.
- M2 onward adds codes by editing Appendix B and regenerating — no per-milestone catalogue
  patching in three languages.

## Open

- Whether the generated recognition list should be shared with the mock server's simulation
  list (TST-021) so a fixture cannot assert a message the mock cannot produce. Deferred;
  raise at M2 when the auth recognition rows get their first heavy use.

## Addendum — .NET pathfinder pass (2026-09-14)

The pathfinder returned the generator, the .NET wiring, the gate and its proof, at
394 tests / 98.78 % line / 95.51 % branch. It also returned thirteen decisions DR-0005 did
not make and seven escalations. The ones that bind all three languages are ruled here.

### D-M1c-12 — `400` maps to `BV-INPUT-100`, correcting D-M1b-21 (blocking, ruled)

**Finding.** `04-error-model.md` step 5 maps `400` — and "other 4xx" — to
`BV-INPUT-100 ServerRejectedRequest`. `StatusCodeMapper.ResolveCode` routes every unmapped
4xx to `BV-INPUT-001 InvalidArgument`. D-M1b-21 chose that only because `BV-INPUT-100` did
not exist in the hand-transcribed catalogue at M1b; the record says so in as many words.

**Ruling.** The specification is the source of truth (CLA-001) and the code it names now
exists. `400` and any other unmapped 4xx map to `BV-INPUT-100` in all three languages. This
amends D-M1b-21, which stands only for its general principle — an unmapped status is still
a typed SDK error, never a runtime exception. `BV-INPUT-001` remains the code for
client-side argument validation, which is what its message says.

Tests asserting the old behaviour are corrected, not deleted; the correction is a
behavioural change and carries its `CHANGELOG.md` line.

### D-M1c-13 — `ERR-031` was violated by the specification, and the specification is fixed

**Finding.** `BV-RATE-001`'s hint is three sentences; `ERR-031` says ≤ 2. It is the only
row of 119 that violates it.

**Ruling.** Appendix B is corrected (the second and third sentences join), not `ERR-031`
relaxed and not an exception recorded — one malformed row does not justify weakening a
requirement (CLA-004). The generator then gains an invariant: **every hint is ≤ 2
sentences**, which makes `ERR-031` mechanically asserted rather than eyeballed, so it
leaves the baseline. `ERR-032` (canonical setting names in hints) stays on the baseline —
it is not mechanically checkable — with **M11** as its owner, where the Error Reference is
authored and every hint is read once against the spec's own vocabulary.

`ERR-022` needs a typed operation to assert and moves to **M2**.

Removal count at M1c exit is therefore **19**: the 18 the pathfinder measured, plus
`ERR-031`.

### D-M1c-14 — Accepted pathfinder decisions, binding on Rust and Python

Accepted as recorded in the pathfinder's return, and binding without re-derivation:

1. §2 cell grammar: `/` separates stem alternatives; `+` closes the stem and its qualifier
   binds to the last alternative only.
2. Rule literals are lower-cased, never trimmed — the trailing space in `machine `, `key `,
   `version `, `role `, `ip ` is load-bearing.
3. `(409, recordings)` compiles to a path guard; `(ssh mount)` and `(policy write)` are
   advisory and unenforced until M4 and M2 respectively.
4. Captures are declarative descriptors, not regexes, and attach only to `prefix` rules —
   the generator hard-fails otherwise.
5. `ERR-034` path interpolation is a distinct step before the `ERR-040` table, appended
   only to hints that mention `Details.path`. `ERR-040` remains exactly the seven
   D-M1c-5 rows.
6. `ERR-003` redaction is applied in the error constructor, so `Path`, the hint and the
   one-line form cannot drift apart.
7. Enrichment runs in the request executor, not in the mapper: two of the seven rows fire
   on transport failures, which never reach the mapper. The mapper's signature is unchanged.
8. The TLS enrichment row keys on `BV-TRANSPORT-003` plus no configured CA; the
   connection-refused row keys on address equality with `https://127.0.0.1:8200`.
9. Defensive branches are removed rather than left dead, per D-M1c-5's reasoning.
10. Fixtures driving operations that do not exist yet report `pending` rather than being
    edited to fit: `errors.format.one-line` and `errors.recognition.missing-token-client-side`
    (M2), `errors.enrichment.404-kv2-hint` (M4).
11. The generator self-checks first-match-wins over every fixture it emits.

Rust and Python reproduce these behaviours from the same generated data. They do not
re-derive them and they do not add a second rule representation.

### D-M1c-15 — `CNF-025` is red on `main` and is fixed inside M1c

**Finding.** The secret scan finds 11 literal `s.<20+ alnum>` tokens in tracked test
sources outside `specifications/fixtures/**` — `ClientConfigurationTests.cs`,
`CoverageGapTests.cs`, `LogicalOperationsUnitTests.cs`, `transport_fixtures.rs` — and exits
1. Verified at `HEAD`, so M1a and M1b both exited with this gate red. It is the same shape
as D-M1b-19: a gate certified by a record rather than by an execution.

**Ruling.** Fixed in this milestone, by assembling the fake token in the tests rather than
writing the literal — never by widening the whitelist or narrowing the pattern (CLA-004).
It is out of M1c's requirement scope but a milestone cannot exit with a red gate, and the
fix is mechanical.

### Carried forward, not fixed here

- **`tools/traceability/tests/test_traceability.py` is Windows-only** (it shells out to
  `cmd.exe /c mkdir`) and **no CI job runs it**. The traceability tool is the project's
  remaining-work counter and its own tests have never executed in CI. M0 harness debt;
  recorded in `ROADMAP.md` §2 and owned separately, not folded into M1c.
  **Correction (R-10 sweep, 2026-09-14):** this note is stale as of the M1c commit itself
  (`0f974d3`), which removed the `cmd.exe` calls and added `repo-gates.yml`'s "Traceability
  parser and gate tests (TST-041)" step running this exact file. See
  `decisions/0001-m0-harness-gate-proof.md`'s R-10 addendum for the seed→red→revert→green
  proof against the file as it exists today.
- Rust and Python still carried a hand-transcribed catalogue in `error.rs` and `errors.py`
  at the end of the pathfinder pass; deleting them is the Rust and Python slices' work.

## Addendum — Python slice (2026-09-14)

### D-M1c-16 — `CNF-010` is red on `main` in the Python CI job, and is fixed inside M1c

**Finding.** `.github/workflows/python.yml:43` runs `pytest tests -m "not integration"`.
`python/tests/test_httpx_transport.py` is the only file in the suite carrying
`pytestmark = pytest.mark.integration`, so in CI `httpx_transport.py` is covered at ~29 %
and `--cov-fail-under=95` fails the job. Verified structurally here and measured at
93.63 % on an untouched tree by the Python slice. **The third gate this milestone has found
certified by a record rather than by an execution**, after D-M1b-19 and D-M1c-15.

**Ruling.** Fixed inside M1c — a milestone does not exit with a red gate. `HttpxTransport`
gains non-integration unit coverage through `httpx.MockTransport`. This does **not** reopen
D-M1b-3: the integration tests against a real listener keep their mark and their scope;
unit coverage is added alongside them. No `omit` rule, no pragma, no lowered floor
(CLA-004).

### D-M1c-17 — `Error.Path` carries the `[ns=…]` prefix; .NET was the outlier

**Finding.** `04-error-model.md`'s `Error` field table defines `Path` as the logical path
*with* the namespace prefix for display. Rust and Python implement it; .NET records the raw
path. Found by the Python slice, not by any gate — `ERR-001` was about to leave the
baseline on a test that never checked it.

**Ruling.** .NET is corrected to the spec and to the other two. The display path is built
where the error is constructed so it cannot drift per call site, and `ERR-003` redaction
must hold regardless of ordering. This is R-9's exact failure mode — a differing value
behind an identical requirement ID — and it is the second one M1c has caught by reading
across the three languages rather than by running the suite.

### D-M1c-18 — Accepted Python-slice decisions

1. **A second constant rename.** `NOTFOUND_PATH_NOT_FOUND` → `NOT_FOUND_PATH_NOT_FOUND`,
   alongside the `BV-RATE-002` rename. D-M1c-2's "exactly one shipped constant" was
   measured on .NET and is wrong for Python; the generated rule wins, because a
   hand-maintained exception is what D-M1c-2 exists to prevent. Breaking, pre-1.0.
2. **Python's `409` discriminator.** Python had no `409` branch and would have sent a
   conflict to `BV-INPUT-100` under D-M1c-12 where .NET yields `BV-CONFLICT-002/003`.
   Adding it closes a pre-existing D-M1b-23 parity gap rather than opening a new one.
3. **`ErrorCategory` moved to a private `_categories` module**, re-exported unchanged from
   `errors`, purely to make the `errors` ↔ `_generated` import direction resolvable. No
   public import path changes.
4. **`RETRYABLE_CODES` stays** as the independent `ERR-006` cross-check D-M1c-8 requires.

## Addendum — Rust slice and the `409` ruling (2026-09-14)

### D-M1c-19 — An unrecognised `409` is `BV-CONFLICT-001`; `Resolve409` is deleted (blocking, ruled)

**Finding (Rust slice, ESC-1).** `04-error-model.md` step 5 maps `409` to
`BV-CONFLICT-001 Conflict`. **No language did that.** .NET's `Resolve409` — an M1b
best-effort arm whose own comment called it unexercised by any fixture — returned
`BV-CONFLICT-002` for any unrecognised `409`. Python had no `409` branch at all until the
Python slice added a copy of `Resolve409` (accepted at the time as closing a D-M1b-23
parity gap, which it did). Rust had no branch, so under D-M1c-12 an unrecognised `409`
would have fallen to `BV-INPUT-100`.

Three languages, three answers, none of them the specification's — and every
fixture-covered `409` path is identical in all three, because message recognition now
handles the digest/sha256 and brokered-credential rows at step 4. The divergence lives
entirely on the path no fixture reaches, which is exactly where D-M1b-4's own comment
warned it would.

**Ruling.** `409 => BV-CONFLICT-001` in all three languages, per step 5. `Resolve409` and
its Python copy are **deleted**, not adjusted: message recognition now covers the cases the
heuristic was guessing at, and the smallest change that satisfies the requirement wins
(CLA-007). This supersedes D-M1c-18 item 2 — the Python `409` discriminator was the right
call against the .NET of an hour ago and is the wrong call against the specification.

This is the third defect M1c has found on a path no fixture reaches, after the Rust empty
root store and connection pooling at M1b. The pattern is now established well enough to
state as a rule: **a best-effort branch written "pending a later milestone" is a defect
with a scheduled discovery date, not a placeholder.** M2 briefs say so explicitly.

### D-M1c-20 — Accepted Rust-slice decisions

1. `DetailValue` gains a `List(Vec<String>)` variant so D-M1c-4's ordered `keys` capture
   keeps its order, as .NET's `string[]` does. Public addition, pre-1.0.
2. `status_to_code` gains a `path` parameter — the minimum signature change the
   `(409, recordings)` guard needs; .NET's `Context` already carried `Path`.
3. `config_errors` + `mapping_errors` collapse into one macro-generated `catalog_errors`
   module. The macro has no body to hold a string, so a second per-code string list is
   structurally impossible.
4. `Display` renders the server clause by hand rather than through `{:?}`: Rust's debug
   form escapes a newline to a literal `\n` where .NET collapses it to a space, which would
   have been a silent `ERR-002` divergence (CLA-003).
5. `to_ascii_lowercase`, not `to_lowercase`, for recognition normalisation — an ASCII fold
   preserves byte length, which makes a `prefix` literal's length a provably valid offset
   into the original message and removes the need for a defensive bounds branch
   (D-M1c-14 item 9). Every Appendix B literal is ASCII.
6. Warnings are read from the top-level envelope for every TRN-040 shape, and non-string
   entries are kept as raw JSON text — the M1b Rust code read them only in the Shape-A
   branch and dropped non-strings. Aligned to .NET.

### D-M1c-21 — D-M1c-2's rename count was understated

D-M1c-2 said the generated naming rule renames exactly one shipped constant. That was
measured on .NET. Rust and Python each rename **two**: the `BV-RATE-002` rename plus
`NOTFOUND_PATH_NOT_FOUND` → `NOT_FOUND_PATH_NOT_FOUND`, because the `BV-NOTFOUND` → `NotFound`
token screaming-snakes with the underscore the hand-written constants omitted. .NET was
already spelled `NotFoundPathNotFound` and is unaffected. Code strings are unchanged in all
three. The rule stands; the count in D-M1c-2 is corrected here.

## Addendum — cross-language parity probe (2026-09-14)

Run by the Strategic Orchestrator, reading the three surfaces rather than running the
suites (R-9: a fixture run cannot see a differing name or a missing member).

**Catalogue parity — clean.** 119 generated code constants in each language, counted from
the three generated files independently. `ErrorCatalog` and `ErrorCatalogEntry` expose
exactly D-M1c-7's members in all three: `Get`/`get`, `All`/`all`, and the six entry fields.
Rust's `get` returns `Option<&'static …>`, .NET's returns a nullable reference, Python's
returns `… | None` — the same contract in three idioms, and none of them throws.

### D-M1c-22 — Python's `CNF-027` gate is materially weaker than .NET's and Rust's

**Finding.** `python/api_surface.txt` is 34 lines of **top-level names only**.
`dotnet/…/PublicApiSurface.txt` and `rust/…/public-api-baseline.txt` are member-level: they
record every method, property, parameter and return type. The Python gate therefore cannot
see a removed method, a renamed parameter, a changed return type, or a changed constant —
and in fact **D-M1c-21's two Python constant renames were invisible to it**, because all it
records is the name `ErrorCodes`.

This is R-9's own shape turned on the tooling: a capability present in two SDKs and absent
in the third. It is the fourth gate this milestone has found doing less than its record
claims, after D-M1b-19, D-M1c-15 and D-M1c-16.

**Ruling.** Recorded and owned separately, **not** folded into M1c. The three gates M1c did
absorb (D-M1c-15, D-M1c-16, and the regeneration gate) were *red* — a milestone cannot exit
on a failing gate. This one is *weak*, not failing, and fixing it means writing a
member-level surface extractor for Python, which is its own piece of work with its own
proof obligation. M1c's exit record states the limitation explicitly rather than claiming a
three-way member-level parity check it did not have.

## Addendum — `Resolve503`, and the rule this milestone earned (2026-09-14)

### D-M1c-23 — `Resolve503` is deleted too; `503` is `BV-SERVER-002`

**Finding (.NET slice).** `Resolve503` is `Resolve409`'s closest relative: it returns
`BV-SERVER-001` when the body contains `sealed`, else `BV-SERVER-002`. Appendix B §2 now
carries `exact bastionvault is sealed` and `contains (5xx) is sealed` → `BV-SERVER-001`,
both of which fire at step 4 before the status table is reached. The heuristic therefore
now answers exactly one case: a `503` whose body contains `sealed` but **not** `is sealed`
— a case no fixture covers and no spec row describes. Step 5 says `503 → BV-SERVER-002`
flatly.

**Ruling.** Deleted in all three languages, for D-M1c-19's reasons and with its evidence
requirement: the `503 sealed` fixtures must stay green **and** must still be answered by
recognition, not by the status arm. Ruling on it now rather than deferring it, because
leaving it would mean M1c knowingly shipping the exact shape it spent the milestone ruling
against.

### D-M1c-24 — .NET's leading `/` on `Error.Path` is removed (approved)

.NET prepended a leading `/` to `Error.Path` that Rust and Python do not. It is the same
`ERR-001` drift as the missing `[ns=…]` prefix and could not be left while claiming parity.
Approved, including the wider blast radius the .NET slice flagged: `RequestEvent.Path`
changes too, which is correct — Rust's observer already reports the display form.

The .NET slice also caught a bug its own fix would have introduced: `ResolveToken` matched
the anchored login pattern against the display path, so a namespaced login would have sent
a token (CFG-020). Token resolution now reads the raw path, as Rust's `build_headers` does.
Recorded because the *fixture for it existed* (`transport.headers.login-omits-token`) and
would have caught it — the one case this milestone where the instrument worked.

### D-M1c-25 — Deferred branches return the specification's answer, not a guess

Three defects this milestone — the `400` code (D-M1c-12), `Error.Path` (D-M1c-17) and
`409`/`503` (D-M1c-19, D-M1c-23) — share one shape: **a branch written to a milestone
rather than to a requirement, with a comment promising a later pass.** Nothing watches that
promise. No fixture reaches the branch (that is why it was deferred), coverage cannot tell
"executed" from "correct", and the traceability baseline records the requirement as
uncovered, which makes the branch's wrongness look expected rather than wrong.

**Rule, binding from M2.** When a branch must be deferred, it returns the value the
specification names, never a plausible guess. A wrong-but-specified value fails loudly the
moment its fixture arrives; a plausible guess passes and survives. Where the deferral is a
whole requirement rather than a branch, the baseline entry names its owning milestone
(already the D-M1a-10 practice — extend it to deferred branches).

This is the third control added after the fact to catch something no gate could see, after
R-9's name-pinning and the public-surface diff. Unlike those two it costs nothing: it is a
choice of return value.
