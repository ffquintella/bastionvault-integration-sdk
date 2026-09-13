# DR-0001 addendum — M0 CI gate proof

> **Note on elision.** Tool output in this document is verbatim except that
> token-shaped literals are truncated with `…` (for example `s.FAKEtoken…`). The seeds
> actually used during the proof were the full-length values; they are elided here only so
> that this document does not itself trip the CNF-025 secret scan it documents. Nothing
> else about the captured output has been altered (ENG-005, CLA-005).


**Status:** Accepted · **Date:** 2026-09-13 · **Milestone:** M0
**Author:** Engineering-tree agent (Claude Sonnet 5, in-process subagent transport, D-M0-13)
**Scope:** requirement 4 of the CI-layer delegation brief — proof that each of the
CNF-020..CNF-027 / TST-041 gates, as wired into `.github/workflows/dotnet.yml`,
`rust.yml`, `python.yml` and `repo-gates.yml`, actually fails on a deliberately seeded
violation and passes again once the violation is reverted.

All commands below were run locally with the exact command each workflow step invokes.
Every seed was reverted; the final `git status --porcelain` state (last section) shows
only the intended new files (four workflow YAMLs plus this document — `specifications/`
is unchanged).

---

## Row 1 — Traceability (TST-041)

**Seed:** remove the `CNF-020` requirement marker from the two tests that jointly cover
it — `[Requirement("CNF-020")]` in
`dotnet/BastionVault.IntegrationSdk.Tests/HarnessTests.cs` (the
`Quality_gates_and_requirement_markers_are_committed` test) and `@req CNF-020` in the
docstring of `python/tests/test_quality_gates.py`
(`test_quality_gates_are_committed_and_library_has_no_exclusion_pragma`). CNF-020 was
covered by exactly these two tests and by no others, so removing the marker from both
drives its covering-test count to zero without touching any other requirement's markers.

**Command:** `python tools/traceability/traceability.py --check --out <dir>`

**Diff (seed):**
```diff
--- a/dotnet/BastionVault.IntegrationSdk.Tests/HarnessTests.cs
+++ b/dotnet/BastionVault.IntegrationSdk.Tests/HarnessTests.cs
@@
     [Fact]
-    [Requirement("CNF-020")]
     [Requirement("CNF-022")]
--- a/python/tests/test_quality_gates.py
+++ b/python/tests/test_quality_gates.py
@@
-    """@req CNF-020 @req CNF-022 @req CNF-023 @req TST-030 @req TST-031 @req TST-040"""
+    """@req CNF-022 @req CNF-023 @req TST-030 @req TST-031 @req TST-040"""
```

**Failing output (verbatim), exit code 1:**
```
OFFENDING CNF-020
covered: 18 / baselined: 401 / total: 420
```

**Revert:** both files restored from the pre-seed copy (byte-identical to `git diff`
showing no changes against HEAD).

**Passing output (verbatim), exit code 0:**
```
covered: 19 / baselined: 401 / total: 420
```

---

## Row 2 — Coverage (CNF-022), per language

### .NET — reused from prior review evidence, not re-derived (per delegation brief)

> Row 2 for .NET is already proven — reuse this verbatim evidence rather than
> re-deriving it: seeding an uncovered internal branch produced
> `BastionVault.IntegrationSdk | 25% | 0% | 66.66%`, then `error : The total line
> coverage is below the specified 95` and `error : The total branch coverage is below
> the specified 95` from `coverlet.msbuild.targets(72,5)`, exit code 1; after
> reverting, `Passed! - Failed: 0, Passed: 20` and `100% | 100% | 100%`, exit code 0.

This session's own full `dotnet test` re-run (see closing section) confirms the
reverted, green state independently: `Passed! - Failed: 0, Passed: 20, Skipped: 0,
Total: 20` and `Total | 100% | 100% | 100%`.

### Rust

**Seed:** appended to `rust/bastionvault-integration-sdk/src/lib.rs`:
```rust
pub fn seeded_uncovered_branch(flag: bool) -> &'static str {
    if flag {
        "seeded-true"
    } else {
        "seeded-false"
    }
}
```

**Command:** `cargo llvm-cov --fail-under-lines 95 --fail-under-regions 95` (the
`cargo coverage` alias, run from `rust/bastionvault-integration-sdk`)

**Failing output (verbatim), exit code 1:**
```
Filename                                                                             Regions    Missed Regions     Cover   Functions  Missed Functions  Executed       Lines      Missed Lines     Cover    Branches   Missed Branches     Cover
------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
E:\Dev\bastionvault-integration-sdk\rust\bastionvault-integration-sdk\src\lib.rs          11                 5    54.55%           3                 1    66.67%          11                 5    54.55%           0                 0         -
------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
TOTAL                                                                                     11                 5    54.55%           3                 1    66.67%          11                 5    54.55%           0                 0         -
```
(process exit code 1; `cargo coverage`'s `--fail-under-lines 95 --fail-under-regions 95`
rejects the 54.55%/54.55% result)

**Revert:** `lib.rs` restored from the pre-seed copy; `git diff` against HEAD is empty.

**Passing output (verbatim), exit code 0:**
```
Filename                                                                             Regions    Missed Regions     Cover   Functions  Missed Functions  Executed       Lines      Missed Lines     Cover    Branches   Missed Branches     Cover
------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
E:\Dev\bastionvault-integration-sdk\rust\bastionvault-integration-sdk\src\lib.rs           6                 0   100.00%           2                 0   100.00%           6                 0   100.00%           0                 0         -
------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
TOTAL                                                                                      6                 0   100.00%           2                 0   100.00%           6                 0   100.00%           0                 0         -
```

### Python

**Seed:** appended to `python/src/bastionvault_integration_sdk/__init__.py` (named with
a leading underscore so it is not itself a public-API change, keeping this seed
isolated to the coverage gate rather than also tripping CNF-027):
```python
def _seeded_uncovered_branch(flag: bool) -> str:
    if flag:
        return "seeded-true"
    else:
        return "seeded-false"
```

**Command:** `python -m pytest tests -m "not integration"` (from `python/`)

**Failing output (verbatim), exit code 1:**
```
============================= test session starts =============================
platform win32 -- Python 3.12.10, pytest-9.1.1, pluggy-1.6.0
rootdir: E:\Dev\bastionvault-integration-sdk\python
configfile: pyproject.toml
plugins: anyio-4.15.1, cov-7.1.0
collected 32 items

tests\test_api_surface.py .                                              [  3%]
tests\test_fixture_driver.py ............                                [ 40%]
tests\test_fixture_loader.py .....                                       [ 56%]
tests\test_mock_server.py ............                                   [ 93%]
tests\test_quality_gates.py .                                            [ 96%]
tests\test_version_metadata.py .
ERROR: Coverage failure: total of 50 is less than fail-under=95
                                                                         [100%]

=============================== tests coverage ================================
______________ coverage: platform win32, python 3.12.10-final-0 _______________

Name                                           Stmts   Miss Branch BrPart  Cover   Missing
------------------------------------------------------------------------------------------
src\bastionvault_integration_sdk\__init__.py       8      3      2      0    50%   27-30
------------------------------------------------------------------------------------------
TOTAL                                              8      3      2      0    50%
Coverage HTML written to dir coverage/html
Coverage XML written to file coverage.xml
FAIL Required test coverage of 95% not reached. Total coverage: 50.00%
============================= 32 passed in 8.58s ==============================
```
(process exit code 1, despite "32 passed", because pytest-cov's `--cov-fail-under=95`
fails the run)

**Revert:** `__init__.py` restored from the pre-seed copy; `git diff` against HEAD is
empty.

**Passing output (verbatim), exit code 0:**
```
=============================== tests coverage ================================
______________ coverage: platform win32, python 3.12.10-final-0 _______________

Name                                           Stmts   Miss Branch BrPart  Cover   Missing
------------------------------------------------------------------------------------------
src\bastionvault_integration_sdk\__init__.py       4      0      0      0   100%
------------------------------------------------------------------------------------------
TOTAL                                              4      0      0      0   100%
Coverage HTML written to dir coverage/html
Coverage XML written to file coverage.xml
Required test coverage of 95% reached. Total coverage: 100.00%
============================= 32 passed in 8.54s ==============================
```

---

## Row 3 — Secret scan (CNF-025, TST-050)

**Seed:** `git add`ed a new tracked file `seeded-secret-proof.txt` at the repo root
containing:
```
token=s.FAKEtoken0…
```

**Command (exactly as wired into `repo-gates.yml`'s "Secret scan" step):** a Python
script that lists `git ls-files`, excludes anything under `specifications/fixtures/**`,
and searches the remaining tracked files for `s\.[A-Za-z0-9]{20,}` and `hvs\.`.

**Failing output (verbatim), exit code 1:**
```
FAIL: 11 potential secret(s) found
 - dotnet/BastionVault.IntegrationSdk.Tests/Harness/InProcessHttpsMockServer.cs: matched 's\\.[A-Za-z0-9]{20,}' -> s.ClientCert…
 - dotnet/BastionVault.IntegrationSdk.Tests/Harness/InProcessHttpsMockServer.cs: matched 's\\.[A-Za-z0-9]{20,}' -> s.RequireCli…
 - dotnet/BastionVault.IntegrationSdk.Tests/Harness/InProcessHttpsMockServer.cs: matched 's\\.[A-Za-z0-9]{20,}' -> s.ClientCert…
 - dotnet/BastionVault.IntegrationSdk.Tests/Harness/InProcessHttpsMockServer.cs: matched 's\\.[A-Za-z0-9]{20,}' -> s.OversizedR…
 - dotnet/BastionVault.IntegrationSdk.Tests/Harness/InProcessHttpsMockServer.cs: matched 's\\.[A-Za-z0-9]{20,}' -> s.OversizedR…
 - dotnet/BastionVault.IntegrationSdk.Tests/HarnessTests.cs: matched 's\\.[A-Za-z0-9]{20,}' -> s.RemoteCert…
 - seeded-secret-proof.txt: matched 's\\.[A-Za-z0-9]{20,}' -> s.FAKEtoken0…
 - specifications/01-conformance-and-quality.md: matched 'hvs\\.' -> hvs.
 - specifications/04-error-model.md: matched 's\\.[A-Za-z0-9]{20,}' -> s.AuthPermis…
 - specifications/appendix-c-conformance-fixtures.md: matched 's\\.[A-Za-z0-9]{20,}' -> s.FAKEtoken0…
 - specifications/appendix-c-conformance-fixtures.md: matched 's\\.[A-Za-z0-9]{20,}' -> s.FAKEtoken0…
```

The seeded finding (`seeded-secret-proof.txt`) is present and correctly identified,
proving detection works.

**Revert:** `git restore --staged seeded-secret-proof.txt && rm seeded-secret-proof.txt`.
`git status --porcelain seeded-secret-proof.txt` shows nothing.

**Output after revert (verbatim), exit code 1 — still failing:**
```
FAIL: 10 potential secret(s) found
 - dotnet/BastionVault.IntegrationSdk.Tests/Harness/InProcessHttpsMockServer.cs: matched 's\\.[A-Za-z0-9]{20,}' -> s.ClientCert…
 - dotnet/BastionVault.IntegrationSdk.Tests/Harness/InProcessHttpsMockServer.cs: matched 's\\.[A-Za-z0-9]{20,}' -> s.RequireCli…
 - dotnet/BastionVault.IntegrationSdk.Tests/Harness/InProcessHttpsMockServer.cs: matched 's\\.[A-Za-z0-9]{20,}' -> s.ClientCert…
 - dotnet/BastionVault.IntegrationSdk.Tests/Harness/InProcessHttpsMockServer.cs: matched 's\\.[A-Za-z0-9]{20,}' -> s.OversizedR…
 - dotnet/BastionVault.IntegrationSdk.Tests/Harness/InProcessHttpsMockServer.cs: matched 's\\.[A-Za-z0-9]{20,}' -> s.OversizedR…
 - dotnet/BastionVault.IntegrationSdk.Tests/HarnessTests.cs: matched 's\\.[A-Za-z0-9]{20,}' -> s.RemoteCert…
 - specifications/01-conformance-and-quality.md: matched 'hvs\\.' -> hvs.
 - specifications/04-error-model.md: matched 's\\.[A-Za-z0-9]{20,}' -> s.AuthPermis…
 - specifications/appendix-c-conformance-fixtures.md: matched 's\\.[A-Za-z0-9]{20,}' -> s.FAKEtoken0…
 - specifications/appendix-c-conformance-fixtures.md: matched 's\\.[A-Za-z0-9]{20,}' -> s.FAKEtoken0…
```

### Finding (prominent, not fixable within this unit's scope)

The seeded violation is correctly detected and correctly stops being detected once
reverted (11 → 10, and the `seeded-secret-proof.txt` line disappears) — the *mechanism*
is proven. But the gate as specified (verbatim regex `s\.[A-Za-z0-9]{20,}` / `hvs\.`,
single whitelist `specifications/fixtures/**`) does **not** return to a passing state on
the repository as it exists today: **10 pre-existing matches remain**, none of which is
an actual secret:

- Six are C# member-access expressions in already-committed test/harness source
  (`dotnet/BastionVault.IntegrationSdk.Tests/Harness/InProcessHttpsMockServer.cs`,
  `HarnessTests.cs`) where an identifier ending in `s` is immediately followed by `.` and
  a 20+ character member name (e.g. `this.RequireClientCertificate` contains the
  substring `s.RequireCli…`).
- Four are prose/example content in `specifications/*.md` files that are **not** under
  `specifications/fixtures/**` (only that exact subtree is whitelisted): the literal
  word `hvs.` in `01-conformance-and-quality.md`, an error-code identifier
  `s.AuthPermis…`-shaped substring in `04-error-model.md`, and two literal
  example fake tokens in `appendix-c-conformance-fixtures.md`.

Per this unit's constraints, none of this can be fixed here: the regex and whitelist are
specified verbatim and must not be narrowed or widened (no second exception), and fixing
the false positives would require editing already-committed `dotnet/` source or
`specifications/*.md` prose, both outside this unit's write scope (and the latter is
ENG-002-forbidden regardless). **This means `repo-gates.yml`'s secret-scan step will be
red on the very first run against `main`, for reasons unrelated to any actual secret.**
This is recorded here as an open finding for the Strategic Orchestrator: either the
regex needs a word-boundary anchor (e.g. requiring `s.` not be preceded by a word
character) or the whitelist needs a second, narrowly-scoped exception for the four
`specifications/*.md` prose matches — either of those is a decision-record change this
unit is not authorized to make.

---

## Row 4 — Build warning (CNF-020), per language

### .NET

**Seed:** added to `dotnet/BastionVault.IntegrationSdk/SdkInfo.cs`:
```csharp
public static void SeededBuildWarning()
{
    int unusedSeededVariable = 42;
}
```

**Command:** `dotnet build dotnet/BastionVault.IntegrationSdk.sln`

**Failing output (verbatim), exit code 1:**
```
  Determining projects to restore...
  All projects are up-to-date for restore.
E:\Dev\bastionvault-integration-sdk\dotnet\BastionVault.IntegrationSdk\SdkInfo.cs(24,13): error CS0219: The variable 'unusedSeededVariable' is assigned but its value is never used [E:\Dev\bastionvault-integration-sdk\dotnet\BastionVault.IntegrationSdk\BastionVault.IntegrationSdk.csproj]

Build FAILED.

E:\Dev\bastionvault-integration-sdk\dotnet\BastionVault.IntegrationSdk\SdkInfo.cs(24,13): error CS0219: The variable 'unusedSeededVariable' is assigned but its value is never used [E:\Dev\bastionvault-integration-sdk\dotnet\BastionVault.IntegrationSdk\BastionVault.IntegrationSdk.csproj]
    0 Warning(s)
    1 Error(s)

Time Elapsed 00:00:03.37
```

**Revert:** `SdkInfo.cs` restored from the pre-seed copy; `git diff` against HEAD empty.

**Passing output (verbatim), exit code 0:**
```
  Determining projects to restore...
  All projects are up-to-date for restore.
  BastionVault.IntegrationSdk -> E:\Dev\bastionvault-integration-sdk\dotnet\BastionVault.IntegrationSdk\bin\Debug\net10.0\BastionVault.IntegrationSdk.dll
  BastionVault.IntegrationSdk.Tests -> E:\Dev\bastionvault-integration-sdk\dotnet\BastionVault.IntegrationSdk.Tests\bin\Debug\net10.0\BastionVault.IntegrationSdk.Tests.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:02.92
```

### Rust

**Seed:** appended to `rust/bastionvault-integration-sdk/src/lib.rs`:
```rust
fn seeded_build_warning() {
    let unused_seeded_variable = 42;
}
```

**Command:** `cargo test` (from `rust/bastionvault-integration-sdk`; `-D warnings` comes
from `.cargo/config.toml`, not this command)

**Failing output (verbatim), exit code 101:**
```
   Compiling bastionvault-integration-sdk v0.2.1 (E:\Dev\bastionvault-integration-sdk\rust\bastionvault-integration-sdk)
error: unused variable: `unused_seeded_variable`
  --> src\lib.rs:25:9
   |
25 |     let unused_seeded_variable = 42;
   |         ^^^^^^^^^^^^^^^^^^^^^^ help: if this is intentional, prefix it with an underscore: `_unused_seeded_variable`
   |
   = note: `-D unused-variables` implied by `-D warnings`
   = help: to override `-D warnings` add `#[allow(unused_variables)]`

error: function `seeded_build_warning` is never used
  --> src\lib.rs:24:4
   |
24 | fn seeded_build_warning() {
   |    ^^^^^^^^^^^^^^^^^^^^
   |
   = note: `-D dead-code` implied by `-D warnings`
   = help: to override `-D warnings` add `#[expect(dead_code)]` or `#[allow(dead_code)]`

error: could not compile `bastionvault-integration-sdk` (lib) due to 2 previous errors
warning: build failed, waiting for other jobs to finish...
error: could not compile `bastionvault-integration-sdk` (lib test) due to 2 previous errors
```

**Revert:** `lib.rs` restored from the pre-seed copy; `git diff` against HEAD empty.

**Passing output (verbatim), exit code 0:**
```
test exposes_spec_and_sdk_version_functions_cnf_041 ... ok

test result: ok. 1 passed; 0 failed; 0 ignored; 0 measured; 0 filtered out; finished in 0.00s

   Doc-tests bastionvault_integration_sdk

running 0 tests

test result: ok. 0 passed; 0 failed; 0 ignored; 0 measured; 0 filtered out; finished in 0.00s
```
(full suite: all `fixture_harness`, `mock_server` and `version_metadata` tests also
passed — see the closing section for the complete final re-run.)

### Python

Python has no compiler warnings-as-errors mechanism; CNF-020's Python-side equivalent in
this SDK is the `ruff` lint step (CNF-023 shares the same enforcement point here — see
`dotnet.yml`'s and `rust.yml`'s comments on where CNF-020/023 converge per language).

**Seed:** appended to `python/src/bastionvault_integration_sdk/__init__.py`:
```python
def _seeded_build_warning() -> None:
    unused_seeded_variable = 42
```

**Command:** `python -m ruff check .` (from `python/`)

**Failing output (verbatim), exit code 1:**
```
F841 Local variable `unused_seeded_variable` is assigned to but never used
  --> src\bastionvault_integration_sdk\__init__.py:24:5
   |
23 | def _seeded_build_warning() -> None:
24 |     unused_seeded_variable = 42
   |     ^^^^^^^^^^^^^^^^^^^^^^
help: Remove assignment to unused variable `unused_seeded_variable`

Found 1 error.
No fixes available (1 hidden fix can be enabled with the `--unsafe-fixes` option).
```
(the same seed also produced `pytest` exit code 1 via the coverage floor, since the
added function was uncovered — consistent with row 2's Python evidence.)

**Revert:** `__init__.py` restored from the pre-seed copy; `git diff` against HEAD empty.

**Passing output (verbatim), exit code 0:**
```
All checks passed!
```

---

## Row 5 — Fixture schema (FIX-001)

**Seed (transient, permitted only under ENG-002's row-5 exception):** edited
`specifications/fixtures/transport/transport.method.list-verb.json`, changing
`"level": "core"` to `"level": "invalid-level-seeded"` (not one of the schema's enum
values `core` / `standard` / `complete`).

**Command:** the fixture-schema-validation step exactly as wired into `repo-gates.yml`
— a Python script that loads `specifications/fixtures/schema/fixture.schema.json` as a
Draft 2020-12 schema and validates every `*.json` file under
`specifications/fixtures/**` except the `schema/` directory itself.

**Failing output (verbatim), exit code 1:**
```
Validated 74 fixtures against specifications\fixtures\schema\fixture.schema.json
FAIL: 1 schema violation(s)
 - specifications\fixtures\transport\transport.method.list-verb.json: 'invalid-level-seeded' is not one of ['core', 'standard', 'complete'] (path: ['level'])
```

**Revert:** `git checkout -- specifications/fixtures/transport/transport.method.list-verb.json`.
`git diff specifications/fixtures/transport/transport.method.list-verb.json` against
HEAD is empty (byte-identical content; a raw `md5sum` taken before vs. after differed
only because `git checkout` re-applies the repository's `core.autocrlf` line-ending
normalization on write — `git diff`, which normalizes line endings for comparison,
confirms there is no content difference). `git status --porcelain specifications/` is
clean.

**Passing output (verbatim), exit code 0:**
```
Validated 74 fixtures against specifications\fixtures\schema\fixture.schema.json
All fixtures conform to schema.
```

---

## Row 6 — Public API diff (CNF-027), per language

### .NET

**Seed:** in `dotnet/BastionVault.IntegrationSdk/SdkInfo.cs`, changed
`public static string SdkVersion => "0.2.1";` to `public static int SdkVersion => 0;`
without touching `PublicAPI.Shipped.txt`/`PublicAPI.Unshipped.txt` or bumping
`<Version>`.

**Command:** `dotnet build dotnet/BastionVault.IntegrationSdk.sln`

**Failing output (verbatim), exit code 1:**
```
  Determining projects to restore...
  All projects are up-to-date for restore.
E:\Dev\bastionvault-integration-sdk\dotnet\BastionVault.IntegrationSdk\PublicAPI.Unshipped.txt(3,1): error RS0017: Symbol 'static BastionVault.IntegrationSdk.SdkInfo.SdkVersion.get -> string!' is part of the declared API, but is either not public or could not be found (https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/PublicApiAnalyzers/PublicApiAnalyzers.Help.md) [E:\Dev\bastionvault-integration-sdk\dotnet\BastionVault.IntegrationSdk\BastionVault.IntegrationSdk.csproj]

Build FAILED.

E:\Dev\bastionvault-integration-sdk\dotnet\BastionVault.IntegrationSdk\PublicAPI.Unshipped.txt(3,1): error RS0017: Symbol 'static BastionVault.IntegrationSdk.SdkInfo.SdkVersion.get -> string!' is part of the declared API, but is either not public or could not be found (https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/PublicApiAnalyzers/PublicApiAnalyzers.Help.md) [E:\Dev\bastionvault-integration-sdk\dotnet\BastionVault.IntegrationSdk\BastionVault.IntegrationSdk.csproj]
    0 Warning(s)
    1 Error(s)

Time Elapsed 00:00:01.44
```

**Revert:** `SdkInfo.cs` restored from the pre-seed copy; `git diff` against HEAD empty.

**Passing output (verbatim), exit code 0:**
```
  Determining projects to restore...
  All projects are up-to-date for restore.
  BastionVault.IntegrationSdk -> E:\Dev\bastionvault-integration-sdk\dotnet\BastionVault.IntegrationSdk\bin\Debug\net10.0\BastionVault.IntegrationSdk.dll
  BastionVault.IntegrationSdk.Tests -> E:\Dev\bastionvault-integration-sdk\dotnet\BastionVault.IntegrationSdk.Tests\bin\Debug\net10.0\BastionVault.IntegrationSdk.Tests.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.40
```

### Rust

**Implementation note (not previously recorded):** no committed mechanism existed to
diff the crate's public API against `public-api-baseline.txt` before this unit —
`grep` confirmed no test or script referenced that file. Rustdoc JSON (the only viable
extraction path) is unstable on every Rust channel and only emitted by rustdoc when a
nightly toolchain passes `-Z unstable-options`; `cargo-public-api` wraps exactly that.
Installing a nightly toolchain **only** to run `cargo +nightly public-api` (never to
build, test, lint, or measure coverage — those all stay on the stable toolchain
throughout `rust.yml`) does not reopen D-M0-14's rejection of "pin CI to nightly to get
branch coverage": it is a narrower, self-contained use for a different purpose, and the
stable-toolchain steps are unaffected. Locally: `rustup toolchain install nightly
--profile minimal` then `cargo install cargo-public-api --locked` (both succeeded
without incident). The committed baseline was generated without the crate-root `pub mod
bastionvault_integration_sdk` line that `cargo public-api --simplified` also emits and
in a different order than the tool's default, so the workflow strips `^pub mod ` lines
and sorts both sides before diffing — verified to produce a clean, zero-diff match
against the unmodified crate before any seed was introduced.

**Seed:** in `rust/bastionvault-integration-sdk/src/lib.rs`, changed
`pub fn sdk_version() -> &'static str { env!("CARGO_PKG_VERSION") }` to
`pub fn sdk_version() -> u32 { 0 }`, without touching `public-api-baseline.txt` or
bumping the crate `version` in `Cargo.toml`.

**Command:**
```
cargo +nightly public-api --simplified | grep -v '^pub mod ' | sort > current.txt
sort public-api-baseline.txt > baseline.txt
diff -u baseline.txt current.txt
```

**Failing output (verbatim), exit code 1:**
```
--- baseline.txt
+++ current.txt
@@ -1,2 +1,2 @@
-pub fn bastionvault_integration_sdk::sdk_version() -> &'static str
+pub fn bastionvault_integration_sdk::sdk_version() -> u32
 pub fn bastionvault_integration_sdk::specification_version() -> &'static str
```

**Revert:** `lib.rs` restored from the pre-seed copy; `git diff` against HEAD empty.

**Passing output:** `diff -u baseline.txt current.txt` exit code 0 (empty diff).

### Python

**Seed:** in `python/src/bastionvault_integration_sdk/__init__.py`, renamed
`def sdk_version() -> str:` to `def sdk_version_renamed() -> str:`, without updating
`api_surface.txt`.

**Command:** `python -m pytest tests/test_api_surface.py` (subset of the full
`python -m pytest tests -m "not integration"` the workflow runs)

**Failing output (verbatim), exit code 1:**
```
    def test_package_api_matches_committed_m0_baseline() -> None:
        """@req CNF-027 @req TST-040"""
        package_init = Path(__file__).parents[1] / "src" / "bastionvault_integration_sdk" / "__init__.py"
        baseline = Path(__file__).parents[1] / "api_surface.txt"
        tree = ast.parse(package_init.read_text(encoding="utf-8"))
        public_names = sorted(
            node.name
            for node in tree.body
            if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef))
            and not node.name.startswith("_")
        )
        expected_names = [
            line.strip()
            for line in baseline.read_text(encoding="utf-8").splitlines()
            if line.strip() and not line.lstrip().startswith("#")
        ]
    
>       assert public_names == expected_names == ["sdk_version", "specification_version"]
E       AssertionError: assert ['sdk_version...tion_version'] == ['sdk_version...tion_version']
E         
E         At index 0 diff: 'sdk_version_renamed' != 'sdk_version'
E         Use -v to get more diff

tests\test_api_surface.py:24: AssertionError
=============================== tests coverage ================================
FAIL Required test coverage of 95% not reached. Total coverage: 0.00%
=========================== short test summary info ===========================
FAILED tests/test_api_surface.py::test_package_api_matches_committed_m0_baseline
============================== 1 failed in 0.12s ==============================
```

**Revert:** `__init__.py` restored from the pre-seed copy; `git diff` against HEAD
empty.

**Passing output (verbatim), exit code 0** (full run):
```
tests\test_api_surface.py .                                              [  3%]
...
============================= 32 passed in 8.51s ==============================
```

---

## Additional findings recorded during this unit

1. **Secret-scan false positives (see Row 3).** This is the most significant open item:
   as specified, `repo-gates.yml`'s secret-scan step fails on the unmodified repository.
   Not fixable within this unit's write scope or ENG-002.

2. **Local-environment mypy artifact, not a repo defect.** Running `python -m mypy
   --strict src tests` on this workstation fails with:
   ```
   C:\Users\felip\AppData\Local\Programs\Python\Python312\Lib\site-packages\numpy\__init__.pyi:737: error: Type statement is only supported in Python 3.12 and greater  [syntax]
   Found 1 error in 1 file (errors prevented further checking)
   ```
   `numpy` is not a project dependency (`grep -rn numpy python/` finds nothing); it is a
   leftover package in this machine's global, non-virtualenv Python 3.12 installation
   that mypy's site-packages scan picks up incidentally. `python.yml`'s
   `actions/setup-python` + `pip install -e ".[dev]"` on a fresh `ubuntu-latest` runner
   will not have this package present, so this does not affect CI. Recorded here rather
   than silently worked around, per CLA-006.

3. **.NET CNF-020/CNF-023/CNF-027 share one mechanism.** All three are enforced as
   compiler diagnostics inside a single `dotnet build` (`TreatWarningsAsErrors`,
   `EnforceCodeStyleInBuild`/`AnalysisLevel=latest-all`, and
   `Microsoft.CodeAnalysis.PublicApiAnalyzers` respectively) — there is no separate lint
   or API-diff command in this SDK's .NET toolchain. `dotnet.yml` names the step
   accordingly rather than fabricating artificial separate steps that would just
   re-invoke the same build.

4. **.NET mock server on Linux.** `dotnet test` (which exercises
   `InProcessHttpsMockServer`, built with `X509KeyStorageFlags.Exportable` per the
   Windows SChannel workaround) was **not** run on `ubuntu-latest` as part of this local
   proof — this machine is Windows. The workflow's first `ubuntu-latest` CI run is the
   first real Linux verification of that TLS fix, as flagged in the delegation brief; no
   local evidence exists either way and none is fabricated here.

---

## Final clean-state verification

`git status --porcelain specifications/` → empty (no output) after every row, confirmed
again at the end of this unit's work.

`git status --porcelain` (repo root) at the end of this unit shows only the pre-existing
snapshot from session start plus the four new workflow files and this document — no
seed file or seed edit survives:
```
?? .github/workflows/dotnet.yml
?? .github/workflows/python.yml
?? .github/workflows/repo-gates.yml
?? .github/workflows/rust.yml
?? decisions/0001-m0-harness-gate-proof.md
```
(plus the same pre-existing modified/untracked entries — `agents.md`, `claude.md`,
`ROADMAP.md`, `tools/`, the three language harnesses, etc. — that were already present
before this unit began; none of them were touched by this unit except transient,
reverted seeds.)

## Final re-run of all four gate command sets

**.NET** — `dotnet build dotnet/BastionVault.IntegrationSdk.sln`: `Build succeeded. 0
Warning(s) 0 Error(s)`. `dotnet test
dotnet/BastionVault.IntegrationSdk.Tests/BastionVault.IntegrationSdk.Tests.csproj`:
`Passed! - Failed: 0, Passed: 20, Skipped: 0, Total: 20` and coverage table `Total | 100%
| 100% | 100%`. `dotnet list ... package --vulnerable`: "has no vulnerable packages
given the current sources."

**Rust** — `cargo test`: all suites `test result: ok` (0 failed). `cargo llvm-cov
--fail-under-lines 95 --fail-under-regions 95`: `TOTAL ... 100.00% ... 100.00% ...
100.00%`, exit 0. `cargo clippy --all-targets -- -D warnings`: clean, exit 0. `cargo
audit --file Cargo.lock`: no advisories, exit 0. `cargo +nightly public-api --simplified`
diffed against the baseline: empty diff, exit 0.

**Python** — `python -m pytest tests -m "not integration"`: `32 passed`, `Required test
coverage of 95% reached. Total coverage: 100.00%`. `python -m ruff check .`: `All checks
passed!`. `python -m pip_audit`: `No known vulnerabilities found` (package itself is
correctly skip-reported as not on PyPI, since it is unpublished).

**repo-gates** — `python tools/traceability/traceability.py --check`: `covered: 19 /
baselined: 401 / total: 420`, exit 0. Fixture schema validation: `Validated 74 fixtures
... All fixtures conform to schema.`, exit 0. `python scripts/validate-agent-docs.py`:
`PASS: agent documents are consistent`, exit 0. Secret scan: **exit 1**, for the reason
recorded in Row 3 and finding 1 above — this is the one gate that does not return to
green, and it is not a defect introduced by this unit.
