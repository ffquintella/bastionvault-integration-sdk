# DR-0008 — R-11 remediation: making CNF-023 (.NET style/analyzer gate) fire

**Status:** accepted, revision 1 · 2026-09-15
**Owner:** Strategic Orchestrator (Claude), per `ROADMAP.md` R-11's "Architect queue" mandate
**Supersedes nothing.** Extends the finding at `decisions/0001-m0-harness-gate-proof.md:691-744`
(DR-0001 addendum, Row 7).

## 1. Problem

`dotnet/.editorconfig`'s bulk lines

```
dotnet_analyzer_diagnostic.severity = none
dotnet_analyzer_diagnostic.category-Style.severity = none
dotnet_analyzer_diagnostic.category-Usage.severity = none
dotnet_analyzer_diagnostic.category-Reliability.severity = none
dotnet_analyzer_diagnostic.category-Maintainability.severity = none
```

silently override every `dotnet_style_*`/`csharp_style_*` option-embedded `:error`/`:warning`
severity beneath them. Only the literal `dotnet_diagnostic.<ID>.severity` form survives the
bulk override (Roslyn precedence: exact rule ID > category bulk > global bulk > analyzer
default). `dotnet build` has therefore never failed on a real style violation in this
repository, at 100% of prior gate proofs (DR-0001 addendum). Separately, two option keys
(`csharp_style_unused_value_expression_statement`, `csharp_style_unused_value_assignment`)
are missing Roslyn's required `_preference` suffix and carry an invalid boolean value —
both are no-ops independent of the bulk-override bug.

CNF-023 (`specifications/01-conformance-and-quality.md:48`): "Language-standard linter
passes with the project's rule set committed to the repo." The rule set is already
committed — the gate just does not evaluate it.

## 2. Decision

Fix the mechanism only. Add one literal `dotnet_diagnostic.<ID>.severity` line per
already-declared option, at the severity already committed for that option, and fix the
two misspelled option keys. **No rule is added, dropped, or retargeted** — this re-enables
exactly the rule set `dotnet/.editorconfig` already claims to enforce (CLA-007, smallest
change satisfying the requirement).

Diagnostic-ID mapping, verified against Microsoft Learn (`dotnet/fundamentals/code-analysis/style-rules/`),
not derived from memory:

| Option (already in `.editorconfig`) | Declared severity | Diagnostic ID(s) added |
|---|---|---|
| `dotnet_style_qualification_for_{field,property,method,event}` | `false:error` (×4) | `IDE0003 = error` (removal direction only; `IDE0009` left unset — all four are `false`) |
| `dotnet_style_require_accessibility_modifiers` | `always:error` | `IDE0040 = error` |
| `dotnet_style_predefined_type_for_locals_parameters_members` / `_for_member_access` | `true:error` (×2) | `IDE0049 = error` |
| `dotnet_style_object_initializer` | `warning` | `IDE0017 = warning` |
| `dotnet_style_collection_initializer` | `warning` | `IDE0028 = warning` |
| `dotnet_style_coalesce_expression` | `warning` | `IDE0029 = warning`, `IDE0030 = warning`, `IDE0270 = warning` |
| `dotnet_style_null_propagation` | `warning` | `IDE0031 = warning` |
| `dotnet_style_prefer_is_null_check_over_reference_equality` | `warning` | `IDE0041 = warning` |
| `dotnet_style_prefer_simplified_boolean_expressions` | `warning` | `IDE0075 = warning` |
| `dotnet_style_prefer_simplified_interpolation` | `warning` | `IDE0071 = warning` |
| `csharp_style_var_for_built_in_types` / `_when_type_is_apparent` / `_elsewhere` | `error` / `warning` / `warning` — **conflicting**, one shared ID | `IDE0007` unset (none prefer `var`); `IDE0008 = warning` — see §3 |
| `csharp_style_expression_bodied_methods` | `warning` | `IDE0022 = warning` |
| `csharp_style_expression_bodied_properties` | `warning` | `IDE0025 = warning` |
| `csharp_style_expression_bodied_accessors` | `warning` | `IDE0027 = warning` |
| `csharp_style_prefer_switch_expression` | `warning` | `IDE0066 = warning` |
| `csharp_style_prefer_pattern_matching` | `warning` | `IDE0078 = warning` |
| `csharp_style_prefer_not_pattern` | `warning` | `IDE0083 = warning` |
| `csharp_style_prefer_null_check_over_type_check` | `warning` | `IDE0150 = warning` |
| `csharp_style_throw_expression` | `warning` | `IDE0016 = warning` |
| `csharp_style_unused_value_expression_statement` → renamed `..._preference` | `warning` (key was invalid) | `IDE0058 = warning` |
| `csharp_style_unused_value_assignment` → renamed `..._preference` | `warning` (key was invalid) | `IDE0059 = warning` |
| `csharp_prefer_braces` | `true:error` | `IDE0011 = error` |

The five bulk `= none` lines are kept as the "everything else off" baseline for the ~1,500
analyzer rules this project has never considered; only the above literal overrides are added
beneath them.

## 3. Ruling: the shared `IDE0008` severity (`var` preference)

`csharp_style_var_for_built_in_types` (`:error`), `_when_type_is_apparent` (`:warning`) and
`_elsewhere` (`:warning`) all resolve to the same pair of diagnostic IDs (`IDE0007`/`IDE0008`),
which carry one severity each — the three sub-options cannot be split. Project owner's
explicit ruling (2026-09-15): **`IDE0008 = warning`**, the majority value (2 of 3
sub-options), not `error` (`built_in_types`'s value). This is a deliberate, recorded
exception — `built_in_types`'s committed `:error` is knowingly not carried forward for the
shared ID, rather than silently dropped.

## 4. Consequence

Because CNF-020 (build-warnings-as-errors) is a separate, already-enforced gate, every
newly-live rule above — `warning` included — becomes build-breaking. The addendum's partial
probe found 68 `IDE0022`, 9 `IDE0040`, and one unrelated `CA1416` from two isolated checks;
the full count across all rules above, in both `dotnet/` projects, is unknown until built.
Every violation is fixed in code to actually conform (CLA-004 — no pragma, no
`[SuppressMessage]`, no severity lowering to get green).

## 5. Scope boundary

.NET only. `rust/` and `python/` are frozen for the duration of Stage 1 (D-1/D-6,
`ROADMAP.md`); this record and its implementation touch neither.
