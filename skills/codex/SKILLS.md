# skills/codex/SKILLS.md — Codex Routing Rules

**Scope:** how the Codex Engineering Orchestrator selects skills, models, and agents.
**Authority:** subordinate to [`agents.md`](../../agents.md). Strategic policy lives in
[`claude.md`](../../claude.md) and [`skills/claude/SKILLS.md`](../claude/SKILLS.md).
This file is the **execution layer**.
**Version:** 1.3.0 · 2026-09-14

## 1. Responsibilities owned here

| # | Responsibility | Produces |
|---|----------------|----------|
| 1 | **Software implementation** | Working code in `dotnet/`, `rust/`, `python/` |
| 2 | **Coding** | Feature and defect changes traced to requirement IDs |
| 3 | **Debugging** | Root cause plus a failing-then-passing test |
| 4 | **Testing** | Unit, conformance, and parity tests above the 95 % floor |
| 5 | **Refactoring** | Behaviour-preserving change with tests proving preservation |
| 6 | **CI/CD** | `.github/workflows/`, packaging, gates |
| 7 | **Deployment support** | Release artefacts, versioning, publication readiness |

**Not owned here:** requirement interpretation, public API shape, `specifications/`
changes, risk tier assignment, merge acceptance, release authorisation, and editing
`CHANGELOG.md` or `ROADMAP.md` (**ENG-008**). Those belong to
the Strategic tree. A Codex agent that needs one of them **escalates**; it does not take
the decision itself, whichever model it is running.

## 2. Model preference

Codex runs Claude models, like the Strategic tree does. What Codex must not do is take a
Strategic decision (`agents.md` §4.5, **FAM-002**). Strict order, start at 1, move down
only on a recorded trigger.

| Order | Model | Use | Escalate when |
|-------|-------|-----|---------------|
| **1** | **Claude Haiku 4.5** | Single-file mechanical edits, scans, fixture generation, data gathering | The change is behavioural, or spans more than one file |
| **2** | **Claude Sonnet 5** | Every implementation, test, refactor, debug, and CI task by default, **including every parity pass of a settled contract** | Two failed attempts, or confidence < 0.60 |
| **3** | **Claude Opus 5** | The pathfinder pass that first defines a contract, complex debugging after two rung-2 failures, cross-cutting refactors, pipeline redesign, designs Claude reviews at handback. **Never a parity pass** | Design or risk decision required, recorded **before** dispatch |

Two Engineering-tree capabilities sit outside the ladder and are available at any rung:

| Capability | Model | Codex-side use |
|------------|-------|----------------|
| **Wide-context survey** | Claude Sonnet 5 | Impact mapping before a cross-cutting change; locating every call site across all three SDKs |
| **Simulation** | Claude Opus 5 | Projecting retry, backoff, failover, and rate-limit behaviour before implementing it |

### 2.1 Reviewers, reached by handback

The review authority is a **Strategic-tree agent**, in this order. Codex does **not**
invoke it, even though the Engineering tree can run the same models. Codex hands the work
back and the Claude orchestrator runs the review in the Strategic tree.

| Order | Reviewer | Use | Escalate when |
|-------|----------|-----|---------------|
| **4** | **Claude Sonnet 5** (Strategic) | **Mandatory reviewer** of Codex output. Parity and spec-conformance review | Finding at R2+, or reviewer confidence < 0.60 |
| **5** | **Claude Opus 5** (Strategic) | Reviewer for architecture-level change, R3 risk, or a blocked review | Irreversible or outward-facing → human |

Registry, trees, and costs: [`agents.md`](../../agents.md) §4.1. Global routing
matrix: §4.2. Tree isolation: §4.5.

## 3. Skill routing table

**First match wins.**

| # | Signal | Skill | Model | Agents |
|---|--------|-------|-------|--------|
| 1 | One file, mechanical, non-behavioural | Direct edit | Claude Haiku 4.5 | 1 |
| 2 | Implement a requirement in one language | Implementation | Claude Sonnet 5, Strategic Claude Sonnet 5 reviews at handback | 2 |
| 3 | Implement across `dotnet/`, `rust/`, `python/` | Parallel implementation | Claude Sonnet 5 ×3, Strategic Claude Sonnet 5 reviews at handback | 4 |
| 4 | Test failure, incorrect behaviour | Debugging | Claude Sonnet 5, Claude Opus 5 after two failures | 1–2 |
| 5 | Behaviour-preserving restructure | Refactoring | Claude Sonnet 5, wide-context survey for the impact map first | 2–3 |
| 6 | Coverage gap, missing failure-path test | Test authoring | Claude Sonnet 5, Strategic Claude Sonnet 5 reviews at handback | 2 |
| 7 | Workflow, packaging, artefact, release gate | CI/CD | Claude Sonnet 5, Claude Opus 5 for redesign | 1–2 |
| 8 | Auth, token, TLS, secret handling, dependency audit | Security engineering | Claude Haiku 4.5 scan, Strategic Claude Opus 5 reviews at handback | 2–3 |
| 9 | Requirement is ambiguous or the spec is silent | **Escalate to Claude** | — | 0 |
| 10 | Change would alter specified behaviour | **Escalate to Claude** | — | 0 |

Rows 9 and 10 are absolute. Codex does not decide what the product should do.

## 4. Engineering workflow

Every implementation task runs these eight steps in order. No step is skipped because a
change looks small.

| Step | Action | Gate to continue |
|------|--------|------------------|
| **1. Ground** | Read the brief. Locate the governing requirement IDs in `specifications/` | Every requirement has an ID or an explicit "no spec" note |
| **2. Map** | Identify the exact files and call sites. Run a wide-context survey if the blast radius is unknown | The change set is enumerated before any edit |
| **3. Plan** | State the change in 3–6 bullets, including the test that will prove it | Plan fits the tier budget |
| **4. Test first** | Write or extend the failing test | The test fails for the right reason |
| **5. Implement** | Smallest change that makes the test pass | No unrelated edits |
| **6. Verify** | Run the language's test command and quote the result | Green, with output quoted |
| **7. Parity** | Apply the same behaviour to the other two languages, or record why not | All three match, or a recorded exception |
| **8. Propose the record** | If the change is user-visible, put the proposed `CHANGELOG.md` line in the handback summary — one line, observable change plus requirement IDs | The handback carries the line, or states why the change is not user-visible |

Verification commands:

```bash
dotnet test ./dotnet/BastionVault.IntegrationSdk.Tests/BastionVault.IntegrationSdk.Tests.csproj
cargo test --manifest-path ./rust/bastionvault-integration-sdk/Cargo.toml
PYTHONPATH=./python/src python -m unittest discover -s ./python/tests
```

| Rule | Statement |
|------|-----------|
| **ENG-001** | Never weaken, skip, or delete a test to make a build pass |
| **ENG-002** | Never edit `specifications/`. Escalate instead |
| **ENG-003** | Never change public API shape without a Claude decision in the brief |
| **ENG-004** | Never leave one language behind (**VER-002**) |
| **ENG-005** | Report failures verbatim (**VER-003**) |
| **ENG-006** | No secret material in logs, tests, fixtures, or error messages |
| **ENG-007** | Coverage must not drop below the 95 % floor (**VER-004**) |
| **ENG-008** | Never edit `CHANGELOG.md` or `ROADMAP.md`. Propose the changelog line in the handback; Claude writes it (`agents.md` §11, **REC-004**) |

## 5. Review workflow

Every Codex change gets a **Claude Sonnet 5** review in the Strategic tree before merge.
This is mandatory and not waivable by the Engineering Orchestrator. That the Engineering
tree can run the same model changes nothing: the gate is a different agent, one tree up,
holding the diff and the acceptance criteria and not the author's reasoning. Codex
requests the review, Codex does not run it (`agents.md` §4.5, **FAM-002**).

| Step | Action |
|------|--------|
| **1. Self-check** | Author confirms steps 1–7 ran, states confidence with evidence. Claude Sonnet 5 may pre-check Claude Haiku 4.5 work in-tree; this is not the gate |
| **2. Hand back** | Return to the Claude orchestrator, which runs Claude Sonnet 5 by default, Claude Opus 5 at R2+ or architecture-level change. The summary carries the proposed changelog line (step 8) |
| **3. Review** | Correctness → spec conformance → parity → error model → security → tests → simplicity |
| **4. Verdict** | Approve, approve with required fixes, or block. Each finding names a file, a line, and a requirement or failure scenario |
| **5. Fix** | Author fixes at the rung that authored the change. Re-review only the delta, never the whole change again (**TOK-007**) |
| **6. Merge** | Engineering Orchestrator merges at R0–R1. R2+ needs Claude acceptance |

| Rule | Statement |
|------|-----------|
| **REV-001** | Engineering-tree output is never given final approval inside the Engineering tree. The gate is the handback (`agents.md` §4.4) |
| **REV-002** | An author never reviews their own change |
| **REV-003** | Two review rounds without convergence escalates to Claude Opus 5 |
| **REV-004** | A blocking finding stops the merge, regardless of schedule |

## 6. Parallel execution limits

| Limit | Value |
|-------|-------|
| **Hard maximum agents (Codex side)** | **10** |
| System-wide ceiling shared with Claude | 10 |
| Concurrent agents editing the same language | 1 |
| Concurrent agents editing the same file | 1 |
| Concurrent handbacks awaiting Claude review | 2 |

Sizing by tier follows [`agents.md`](../../agents.md) §7.1: simple 1, medium 2–3,
large 3–5, enterprise 5–8, hard maximum 10. **Agents are created only when required,
never proactively.** The canonical parallel shape here is one agent per language plus one
reviewer, because the three SDKs are disjoint file sets driven by one shared contract.

Parallelise only across disjoint files with no shared decision (`agents.md` §7.4).
Anything touching one public contract runs sequentially.

## 7. Cost control rules

| Rule | Statement |
|------|-----------|
| **COST-001** | Start at the lowest rung that fits: Claude Haiku 4.5 for mechanical work, Claude Sonnet 5 otherwise. Upgrading requires a recorded trigger, never a preference |
| **COST-002** | Two failed attempts at one rung → escalate. Do not retry a third time at the same tier |
| **COST-003** | Never use Claude Opus 5 for bulk generation. In the Engineering tree it designs, debugs, and projects; in the Strategic tree it reviews and decides |
| **COST-004** | Never run a wide-context survey when a `grep` or a path answers the question |
| **COST-005** | Re-review deltas only, never whole changes |
| **COST-006** | One agent per unit of work. No speculative parallelism (**TOK-009**) |
| **COST-007** | A task exceeding its tier budget is decomposed, not funded further (**TOK-011**) |
| **COST-008** | Abandoned work is reported, not silently retried. Sunk cost is not a reason to continue |
| **COST-009** | Cache the impact map. A second agent in the same task reuses it rather than re-reading the repository |

## 8. Context sharing rules

| Rule | Statement |
|------|-----------|
| **CTX-001** | An engineer receives the brief only, never Claude's planning conversation (**TOK-001**) |
| **CTX-002** | Parallel language agents receive the **same** requirements, constraints, and decisions, plus only their own language's file list |
| **CTX-003** | A reviewer receives the diff, the requirements, and the acceptance criteria. Not the author's reasoning transcript |
| **CTX-004** | Results return as the structured summary in `agents.md` §8, never as a transcript (**TOK-007**) |
| **CTX-005** | Shared decisions are written once by the Engineering Orchestrator and referenced by every agent (**TOK-008**) |
| **CTX-006** | Files are passed as path plus line range, not inlined (**TOK-005**) |
| **CTX-007** | Spec content is passed as requirement IDs, not quoted prose (**TOK-006**) |

## 9. Token optimization policy

The block below is mirrored verbatim from [`agents.md`](../../agents.md) §6 and is
verified byte-for-byte by `scripts/validate-agent-docs.py`. Edit it in `agents.md` only.

<!-- BEGIN:TOKEN-OPTIMIZATION-POLICY -->
<!-- Canonical source: agents.md section 6. Mirrored verbatim into claude.md,
     skills/claude/SKILLS.md and skills/codex/SKILLS.md. Verified byte-for-byte by
     scripts/validate-agent-docs.py. Never edit a mirrored copy in isolation. -->

### Mandatory token rules

These rules are normative for every orchestrator and every agent in this repository.

| ID | Rule |
|----|------|
| **TOK-001** | **Never pass the entire chat history** to a delegated agent. Pass a distilled brief only. |
| **TOK-002** | **Never pass a full repository** unless whole-repo comprehension is the task itself (the wide-context survey route). |
| **TOK-003** | **Compress context before delegation.** The brief is authored by the delegating orchestrator, never copy-pasted from upstream. |
| **TOK-004** | A delegation brief may contain only these four sections: **requirements**, **constraints**, **decisions**, **relevant files**. Anything else is dropped. |
| **TOK-005** | Reference files by path and line range (`specifications/07-kv-engine.md:120-180`). Inline a file only when the agent cannot read it itself. |
| **TOK-006** | Cite specification requirement IDs (`CFG-003`, `KV2-011`) instead of quoting specification prose. |
| **TOK-007** | Agent results return as a **structured summary**: outcome, artefacts changed, decisions made, open questions, confidence. Never a transcript. |
| **TOK-008** | Never re-derive a fact already recorded in a decision record. Read the decision, do not recompute it. |
| **TOK-009** | Run one agent per unit of work. Do not spawn an agent to do what a `grep` answers. |
| **TOK-010** | Escalation carries the brief plus the failed attempt's summary, never the escalating agent's full working context. |
| **TOK-011** | Cap every brief at the token budget for its tier. Over budget means cut scope or split the task, never raise the cap silently. |
| **TOK-012** | Prefer the cheapest model that satisfies the routing table. Upgrading a model requires a recorded reason. |

### Context reduction target

Context reduction is measured per delegation:

```
reduction = 1 - (tokens_in_brief / tokens_in_naive_context)
```

where `tokens_in_naive_context` is the full conversation plus every file the delegating
orchestrator currently has open.

| Measurement | Target |
|-------------|--------|
| Context reduction per delegation | **70-80 %** |
| Below 60 % | Brief is under-compressed. Re-author it before delegating. |
| Above 90 % | Brief is probably missing constraints. Confirm the agent can act without follow-up questions. |

### Token budget per tier

| Tier | Brief budget (input) | Result budget (output) |
|------|----------------------|------------------------|
| Simple | 2 k tokens | 1 k tokens |
| Medium | 6 k tokens | 2 k tokens |
| Large | 15 k tokens | 4 k tokens |
| Enterprise | 30 k tokens | 8 k tokens |
| Whole-repo comprehension (wide-context survey) | 200 k tokens | 4 k tokens |

A task that cannot fit its tier budget is **decomposed**, not granted a larger budget.

<!-- END:TOKEN-OPTIMIZATION-POLICY -->

## 10. Escalation back to Claude

Codex escalates, and pauses the affected task, when any of these occur:

| Trigger | What Codex sends |
|---------|------------------|
| Requirement ambiguous or spec silent | The requirement, the two readings, the recommended one |
| Change would alter specified behaviour | The behaviour, the requirement ID, why the change needs it |
| Public API shape must change | Current shape, proposed shape, affected languages |
| Cross-language parity cannot be met | What blocks it in which language |
| Risk tier R3 discovered mid-task | The finding, the blast radius, work paused |
| Security finding at R2 or R3 | Straight to the Strategic Orchestrator, bypassing the chain |
| Two failed attempts at the same task | Both failure summaries, not both transcripts (**TOK-010**) |
| Review deadlocked after two rounds | Both positions, stated fairly |
| Tier budget exhausted | Work completed, work remaining, proposed decomposition |

Escalation carries the brief plus the failure summary. It never carries the escalating
agent's full working context.
