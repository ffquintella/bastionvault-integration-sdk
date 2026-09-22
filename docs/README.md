# Documentation index

The thirteen documents [16 — documentation requirements](../specifications/16-documentation-requirements.md)
makes mandatory, where each one lives, and the conventions every one of them follows.

This repository ships one SDK per language against one shared specification. `docs/dotnet/`
holds the .NET set. `docs/rust/` and `docs/python/` are **reserved for M13** and are deliberately
absent until there is something to put in them — an empty directory would satisfy a presence
check without satisfying a reader.

## The thirteen documents

| # | Document | Location | Status |
|---|----------|----------|--------|
| D1 | README | [`dotnet/README.md`](../dotnet/README.md) | Rewritten at M11 slice g. `DOC-031` requires the package-registry README to be this same file, and `dotnet pack` takes the one beside the `.csproj`, so D1 does not move here (DR-0018 D-M11-2) |
| D2 | Getting started | [`dotnet/getting-started.md`](dotnet/getting-started.md) | **Landed** |
| D3 | Configuration reference | `dotnet/configuration.md` | M11 slice b |
| D4 | Authentication guide | `dotnet/authentication.md` | M11 slice c |
| D5 | Secrets (KV) guide | `dotnet/secrets-kv.md` | M11 slice c |
| D6 | Engine guides | `dotnet/engines/<engine>.md` — one page per mount, eleven pages (DR-0018 D-M11-10) | M11 slices d1, d2 |
| D7 | Error reference | `dotnet/errors.md` | M11 slice b |
| D8 | Resilience and operations guide | `dotnet/resilience-and-operations.md` | M11 slice e |
| D9 | Vault compatibility gaps | `dotnet/compatibility-gaps.md` | M11 slice e |
| D10 | Security guide | `dotnet/security.md` | M11 slice e |
| D11 | API reference | generated, `dotnet/api/` | M11 slice g |
| D12 | Changelog | [`CHANGELOG.md`](../CHANGELOG.md) at the repository root | Maintained continuously. Strategic-tree owned (**REC-004**); no slice edits it directly (DR-0018 D-M11-12) |
| D13 | Contributing / testing guide | `dotnet/contributing.md` | M11 slice e |

Filenames above are the contract, not suggestions: `DOC-020`'s presence gate (slice g) will glob
for them. A slice that wants a different name changes this table first.

## File and heading conventions

- **One document per file, lower-case-hyphenated, `.md`.** No front matter. The renderer takes
  the title from the first heading.
- **`# Title (.NET)`** is the first line. The parenthesised language is what keeps a search
  result unambiguous once `docs/rust/` and `docs/python/` exist.
- **The second block is the specification link** (`DOC-015`): the section the page implements,
  plus the requirement IDs whose behaviour it relies on. Cite IDs, never quote specification
  prose (**TOK-006**).
- **Relative links only**, `../specifications/...` from `docs/dotnet/`. `DOC-023`'s link check
  lands in slice g and will follow them.
- **No conformance level is stated in any document.** `CNF-002` forbids claiming one while
  section 15's integration MUSTs are unimplemented, and DR-0018 D-M11-7 settles that M11 does not
  change this. Do not write "Core" into a page.

### The DOC-010 section order

Every guide uses exactly this order, with these headings:

1. `## The task` — one paragraph: what the reader will have working at the end.
2. `## Prerequisites` — a table, plus a `### The policy the example needs` subsection carrying
   the HCL (`DOC-011`).
3. `## Step 1 — …`, `## Step 2 — …`, … — one sample each, in the order a reader runs them.
4. `### What goes over the wire` — the request and response JSON for at least one call
   (`DOC-012`), placed under the step it belongs to.
5. `## The whole program` — the complete example, runnable as written.
6. `## What can go wrong` — a table of `Code | Meaning | Fix`, then a sample that handles them.
7. `## Next steps` — links to the documents that continue the thread.

A reference document (D3, D7, D9, D11) is not a guide and does not use this order. Everything
else does.

### Terminology (DOC-013)

Match the glossary in [00 — overview](../specifications/00-overview.md#glossary): *server*,
*client*, *operation*, *mount*, *logical path*, *envelope*, *token*, *namespace*, *lease*,
*sealed*, *standby*. In prose the AppRole-style auth type is called **AppID**; its wire
spelling is written out exactly **once per page**, where the reader first needs to recognise it
in a path or a payload.

### Never real (DOC-014)

Hostnames are `https://vault.example.com:8200`. Tokens are `s.FAKEtoken`. **Keep the part after
`s.` under twenty alphanumeric characters** — the `CNF-025` secret scan in
`.github/workflows/repo-gates.yml` matches `s.` followed by twenty or more, and `docs/` is not on
its whitelist. Slice g extends that scan to `docs/`, at which point a longer fake token stops
being a style problem and starts being a red build.

## Samples: how they are stored, executed, and kept honest

`DOC-003` requires every code sample in D1–D10 to be **compiled and executed**. In .NET that is
[`dotnet/BastionVault.IntegrationSdk.DocsSamples`](../dotnet/BastionVault.IntegrationSdk.DocsSamples),
a test project in the solution. A sample is a real method that `dotnet test` runs against the
in-process mock server the test suite already uses; there is no `examples/` console app, because
an `examples/` app is compiled and, in practice, never run (DR-0018 D-M11-3).

### Adding a sample to a guide

1. **Write the test.** Add a method to a class under `Samples/`. The class takes
   `IClassFixture<MockVaultFixture>`; the fixture starts the mock server, binds the routes your
   sample calls, and points `BASTIONVAULT_*` at it.
2. **Mark the region.** Wrap the lines the reader should see:

       // docs:begin <document>/<name>
       …
       // docs:end <document>/<name>

   The id is `<document>/<name>`, lower-case-hyphenated, unique across the whole project —
   `getting-started/read-secret`, `transit/encrypt`. Markers must pair by id.
3. **Assert something outside the region.** Arrangement above the region, assertions below it.
   The reader sees the sample; the suite proves it did what the guide claims.
4. **Anchor it in the document.** Put the anchor immediately above an empty fence — an HTML
   comment on its own line at column 0, then the opening fence on the very next line, then the
   closing fence:

       <!-- docs:sample getting-started/read-secret -->
       ```csharp
       ```

5. **Fill the fence.** Run

   ```bash
   BASTIONVAULT_DOCS_SAMPLES=update dotnet test dotnet/BastionVault.IntegrationSdk.DocsSamples/BastionVault.IntegrationSdk.DocsSamples.csproj
   ```

   which copies the region into the fence. Then run it again *without* the variable and confirm
   it is green.

### What the check enforces

`SampleEmbeddingTests` fails the build when any of these is true:

| Failure | Why it is a failure |
|---|---|
| A fence's text differs from its region by one byte | The document is lying about code that runs |
| A `csharp` fence has no `docs:sample` anchor above it | `DOC-003` admits no unexecuted C# in D1–D10 |
| An anchor names an id no region defines | A sample was renamed or deleted under the document |
| A region no document shows | Dead sample code |
| Two regions share an id | The mapping stops being one-to-one |
| A region's method is neither `[Fact]`/`[Theory]` nor called anywhere | Compiled but not executed — `DOC-003`'s second limb |

Comparison is byte-for-byte after two normalisations, and only these two: the region's common
leading indentation is removed, and trailing whitespace is stripped from each line. Blank lines
inside a region are preserved. Nothing else is rewritten — no reflowing, no comment stripping —
so what you read in the guide is what the compiler read.

**The check compares; it does not silently regenerate.** The error-catalogue gate
(`tools/error-catalogue/`, regenerate-then-`git diff --exit-code`) can regenerate because its
output is code no human edits. Here both sides are hand-written and both are read by humans, so
an automatic rewrite would settle every disagreement in favour of the code and quietly edit a
guide. Regeneration is therefore opt-in, through the environment variable above, and CI never
sets it.

### Fences that are not samples

Only `csharp` fences are checked and only `csharp` fences may show SDK code. Use `hcl` for
policy, `json` for wire bodies, `http` for request lines, `bash` for commands, `text` for
anything illustrative. If you show a language's code in a fence other than `csharp` to escape the
check, you have defeated `DOC-003`; do not.

One page already goes further: D2's `hcl` policy block is asserted against `PolicyBuilder`'s
output by a test, so `DOC-011`'s snippet cannot go stale either. Do the same wherever a guide
shows something the SDK can generate.

### Samples that need a live server

`DOC-003`'s last sentence puts them in the integration job, which is **M12**'s. If a sample
cannot be driven against the mock server, leave it out of the guide and report it in your
handback with the reason, so slice e records it in D13. Do not fake it, and do not drop it
silently. As of slice a, **no sample has needed this**: every D2 sample runs against the mock.

### Coverage

The docs-samples project collects **no** coverage (`CNF-011` excludes example programs). The 95 %
floor (`CNF-010`, `VER-004`) is measured exactly where it always was — `dotnet test` on
`BastionVault.IntegrationSdk.Tests`, filtered to `[BastionVault.IntegrationSdk]*`. Do not add
`CollectCoverage` to the samples project: a second coverage mechanism measuring the same assembly
is the failure that project's own csproj comment documents at length.
