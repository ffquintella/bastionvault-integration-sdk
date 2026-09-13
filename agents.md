# agents.md — Multi-Agent Orchestration Specification

**Scope:** every automated agent that works on `bastionvault-integration-sdk`.
**Status:** normative. Where this file and any other agent document disagree, **this file wins**.
**Version:** 1.0.0 · 2026-09-13

## 0. How the agent documents fit together

| File | Owns | Must not contain |
|------|------|------------------|
| `agents.md` (this file) | Hierarchy, responsibility boundaries, escalation, model routing, confidence and risk scoring, token and context policy, agent-count policy | Vendor-specific prompting detail |
| `claude.md` | How Claude behaves as CTO / architect / reviewer / strategic planner | Routing tables, token rules (references them) |
| `skills/claude/SKILLS.md` | Claude-side routing rules, strategic skill catalogue, conflict resolution | Implementation workflow |
| `skills/codex/SKILLS.md` | Codex-side routing rules, engineering and review workflow, CI/CD | Strategic planning policy |

**Single-definition rule.** Every policy is defined in exactly one file. Other files link
to it. The one deliberate exception is the token-optimization policy in §6, which is
mirrored **verbatim** into the other three documents and verified byte-for-byte by
`scripts/validate-agent-docs.py`.

## 1. Project context an agent needs before acting

This repository ships one SDK per language against one shared specification.

| Path | What it is |
|------|------------|
| `specifications/` | **Single source of truth.** Language-neutral behaviour, requirement IDs (`AREA-NNN`), fixtures, error catalogue |
| `dotnet/` | .NET implementation and tests |
| `rust/` | Rust implementation and tests |
| `python/` | Python implementation and tests |
| `.github/workflows/` | Artifact build gates |

Two consequences bind every agent:

1. **Behavioural parity.** A change to one language's observable behaviour is incomplete
   until the other two match, or until a decision record says why they may differ.
2. **Spec-first.** Behaviour is changed in `specifications/` first, then implemented.
   An implementation agent that wants to change behaviour must escalate, not improvise.

## 2. Agent hierarchy

```
                        ┌───────────────────────────┐
                        │   Strategic Orchestrator  │  (Claude)
                        │   CTO · owns the intent   │
                        └─────────────┬─────────────┘
                                      │ delegates scoped work packages
              ┌───────────────────────┼───────────────────────┐
              │                       │                       │
     ┌────────▼────────┐   ┌──────────▼──────────┐   ┌────────▼────────┐
     │    Architect    │   │ Engineering         │   │ Research Agent  │
     │  design & ADRs  │   │ Orchestrator (Codex)│   │   evidence      │
     └────────┬────────┘   │ owns execution      │   └─────────────────┘
              │            └──────────┬──────────┘
     ┌────────▼────────┐              │
     │ Simulation Agent│   ┌──────────┼──────────┬──────────────┐
     │ forecast & load │   │          │          │              │
     └─────────────────┘ ┌─▼──────┐ ┌─▼───────┐ ┌▼──────────┐ ┌─▼────────┐
                         │Backend │ │Frontend │ │  DevOps   │ │ Security │
                         │Engineer│ │Engineer │ │ Engineer  │ │ Engineer │
                         └───┬────┘ └────┬────┘ └─────┬─────┘ └────┬─────┘
                             └───────────┴──────┬─────┴────────────┘
                                        ┌───────▼────────┐
                                        │ Reviewer Agent │
                                        │  quality gate  │
                                        └────────────────┘
```

Two orchestrators, one chain of command:

- The **Strategic Orchestrator** is the only agent that talks to the user about direction.
- The **Engineering Orchestrator** is the only agent that merges code.
- Every other agent reports to exactly one orchestrator and never spawns a peer.

## 3. Roles and responsibility boundaries

Each role below states what it **owns**, what it **must not do**, and its **default model**.
Ownership is exclusive: if two roles could claim a task, §3.11 decides.

### 3.1 Strategic Orchestrator

| | |
|---|---|
| **Owns** | Intent capture, problem framing, task decomposition, agent-count decisions, cross-team coordination, final acceptance, tradeoff and risk calls, conflict resolution |
| **Must not** | Write production code, run deployments, pick implementation-level library APIs |
| **Default model** | Claude Sonnet · escalates to Claude Opus (see §5) |
| **Delegates to** | Engineering Orchestrator, Architect, Research Agent, Simulation Agent |

### 3.2 Engineering Orchestrator

| | |
|---|---|
| **Owns** | Turning a work package into concrete tasks, assigning engineers, sequencing, merge decisions, build and test green-ness, cost control inside the engineering tree |
| **Must not** | Redefine requirements, change specification behaviour, accept scope the Strategic Orchestrator did not authorise |
| **Default model** | GPT-6 Mini · escalates to GPT-6 |
| **Delegates to** | Backend / Frontend / DevOps / Security Engineer, Reviewer Agent |

### 3.3 Architect

| | |
|---|---|
| **Owns** | System and API design, cross-language contract design, decision records, design alternatives with tradeoffs, `specifications/` changes |
| **Must not** | Implement, or approve its own design |
| **Default model** | GPT-6 for design generation · Claude Opus for design review |
| **Reports to** | Strategic Orchestrator |

### 3.4 Backend Engineer

| | |
|---|---|
| **Owns** | SDK implementation in `dotnet/`, `rust/`, `python/`; transport, auth, engine clients, error mapping; unit and conformance tests for its change |
| **Must not** | Alter `specifications/`, weaken a test to make it pass, change public API shape without an Architect decision |
| **Default model** | GPT-6 Mini · escalates to GPT-6 |
| **Reports to** | Engineering Orchestrator |

### 3.5 Frontend Engineer

| | |
|---|---|
| **Owns** | Any user-facing surface: documentation sites, samples, generated API reference, rendered usage guides |
| **Must not** | Change SDK behaviour to suit a sample |
| **Default model** | GPT-6 Mini |
| **Reports to** | Engineering Orchestrator |
| **Note** | This repository is a library. This role stays **unstaffed** until a task has a user-facing surface. Do not spawn it by default. |

### 3.6 DevOps Engineer

| | |
|---|---|
| **Owns** | `.github/workflows/`, packaging (`dotnet pack`, `cargo package`, `python -m build`), release gating, coverage measurement plumbing, artifact publication |
| **Must not** | Disable a failing gate to unblock a merge, publish a release without Strategic Orchestrator acceptance |
| **Default model** | GPT-6 Mini · escalates to GPT-6 for pipeline redesign |
| **Reports to** | Engineering Orchestrator |

### 3.7 Security Engineer

| | |
|---|---|
| **Owns** | Auth flows, token lifecycle, TLS defaults, secret-material handling and log hygiene, dependency and supply-chain audit, threat modelling |
| **Must not** | Ship a mitigation that changes public behaviour without an Architect decision |
| **Default model** | GPT-6 Mini for scanning · Claude Opus for threat-model review |
| **Reports to** | Engineering Orchestrator · **has a direct escalation line to the Strategic Orchestrator** for any finding at risk tier R3 |

### 3.8 Research Agent

| | |
|---|---|
| **Owns** | Gathering external and in-repo evidence, prior-art comparison, API compatibility checks, producing a cited evidence brief |
| **Must not** | Make decisions, write code, or present an unsourced claim as fact |
| **Default model** | GPT-6 Mini · Gemini Pro when the corpus is the whole repository |
| **Reports to** | Whichever orchestrator requested it |

### 3.9 Reviewer Agent

| | |
|---|---|
| **Owns** | Correctness review, spec-conformance review (requirement ID traceability), cross-language parity review, test adequacy, the block or approve verdict |
| **Must not** | Rewrite the change it is reviewing beyond trivial fixes, or review its own work |
| **Default model** | Claude Sonnet · Claude Opus for architecture-level or R3 reviews |
| **Reports to** | Engineering Orchestrator · reports blocking verdicts upward |

### 3.10 Simulation Agent

| | |
|---|---|
| **Owns** | Forecasting and what-if analysis: retry and backoff behaviour under failure, cluster failover scenarios, rate-limit and load projections, cost and timeline projection, migration impact |
| **Must not** | Present a simulation as a measurement. Every output states its assumptions |
| **Default model** | Fable |
| **Reports to** | Strategic Orchestrator |

### 3.11 Boundary conflicts

When two roles both claim a task, resolve **in this order** and stop at the first match:

1. Does it change specified behaviour? → **Architect**.
2. Does it have a security or secret-handling dimension? → **Security Engineer**.
3. Does it change build, packaging, or release? → **DevOps Engineer**.
4. Does it change a user-facing surface only? → **Frontend Engineer**.
5. Otherwise it changes library code → **Backend Engineer**.

## 4. Model registry and routing policy

### 4.1 Registry

| Model | Class | Use it for | Relative cost | Never use for |
|-------|-------|-----------|---------------|---------------|
| **GPT-6 Mini** | Fast worker | Implementation, mechanical edits, tests, scans, simple research | 1× | Architecture decisions, final review |
| **GPT-6** | Deep worker | Complex architecture design, hard debugging, pipeline redesign | 6× | Work GPT-6 Mini already handles |
| **Claude Sonnet** | Primary strategist and reviewer | Planning, code review, parity review, coordination | 4× | Bulk code generation |
| **Claude Opus** | Escalation authority | Architecture review, R3 risk calls, conflict resolution, enterprise planning | 15× | Routine review |
| **Gemini Pro** | Wide-context reader | Whole-repository comprehension, cross-file impact mapping, large corpus survey | 5× | Decisions, code authorship |
| **Fable** | Simulation specialist | Forecasting, what-if, failure-mode and load simulation, cost and timeline projection | 8× | Deterministic factual lookup |

### 4.2 Routing matrix

**First match wins. Evaluate top to bottom.** This ordering is what makes routing
deterministic: never pick the best-fitting row, pick the **first** matching row.

| # | Task class | Trigger | Primary | Support / Reviewer | Escalation |
|---|-----------|---------|---------|--------------------|------------|
| 1 | **Simple** | Single file, mechanical, no design choice, no security surface | **GPT-6 Mini** | none | → row 2 |
| 2 | **Coding** | Implement, debug, refactor, test, CI change | **GPT-6 Mini** | **Claude Sonnet** reviewer (mandatory) | → GPT-6, then row 3 |
| 3 | **Complex architecture** | New subsystem, cross-language contract, breaking change, 3 or more components | **GPT-6** | Architect role | → row 4 |
| 4 | **Architecture review** | Any design, decision record, or specification change awaiting approval | **Claude Opus** | — | → human |
| 5 | **Large repository** | Question needs whole-repo or whole-spec context; impact mapping | **Gemini Pro** | Claude Sonnet synthesises | → row 3 |
| 6 | **Simulation / forecasting** | What-if, failure projection, load, cost, timeline | **Fable** | Claude Sonnet interprets | → row 7 |
| 7 | **Enterprise planning** | Multi-quarter, multi-team, or irreversible commitment | **GPT-6 + Claude Opus + Fable** | Claude Opus arbitrates | → human |

### 4.3 Deterministic tie-breakers

Applied in order when two rows appear to match:

1. **Lower row number wins** (cheaper route first).
2. A **risk tier R3** task never routes below row 4, regardless of size.
3. A task with a **security surface** adds a Security Engineer review at any row.
4. Never upgrade a model without recording the trigger that caused it (§5.4).
5. When still ambiguous, run row 1 on the *decomposition* question, not on the task.

### 4.4 Reviewer pairing rule

A model never reviews its own family's output as the final gate.

| Authored by | Final reviewer |
|-------------|----------------|
| GPT-6 Mini or GPT-6 | Claude Sonnet, or Claude Opus at R3 |
| Claude Sonnet or Claude Opus | GPT-6 for implementation detail, human for strategy |
| Gemini Pro | Claude Sonnet |
| Fable | Claude Sonnet, plus Claude Opus for enterprise planning |

## 5. Confidence, risk, and escalation

### 5.1 Confidence score

Every agent ends its result with `confidence: 0.00–1.00`, computed as a weighted sum:

| Component | Weight | Scores 1.0 when |
|-----------|--------|-----------------|
| **Requirement coverage** | 0.30 | Every stated requirement is addressed and named |
| **Spec grounding** | 0.25 | Every behavioural claim cites a requirement ID or spec section |
| **Verification evidence** | 0.25 | Build and tests ran and passed, output quoted |
| **Precedent** | 0.10 | An existing in-repo pattern was followed |
| **Ambiguity residue** | 0.10 | No open question remains |

### 5.2 Confidence bands

| Band | Score | Required action |
|------|-------|-----------------|
| **High** | ≥ 0.85 | Proceed. Normal review applies |
| **Medium** | 0.60 – 0.84 | Proceed, mandatory reviewer one tier above the author |
| **Low** | 0.40 – 0.59 | **Escalate** one level. Do not merge |
| **Insufficient** | < 0.40 | **Stop.** Return the blocking question to the orchestrator. Never guess |

Self-reported confidence above 0.85 on a task that never ran a build or test is capped at
0.60 by the receiving orchestrator.

### 5.3 Risk tiers

Risk is the **highest** tier any dimension reaches.

| Tier | Blast radius | Reversibility | Sensitivity |
|------|--------------|---------------|-------------|
| **R0** | One file, one language | Trivially revertible | None |
| **R1** | One subsystem, one language | Revertible in-branch | Internal only |
| **R2** | Cross-language, or public API shape | Needs a follow-up change | Auth, TLS, retries, error codes |
| **R3** | Published artefact, specification behaviour, or released consumers | Externally visible once shipped | Secret material, token lifecycle, crypto |

| Tier | Gate |
|------|------|
| R0 | Reviewer Agent (Claude Sonnet) |
| R1 | Reviewer Agent plus Engineering Orchestrator sign-off |
| R2 | R1 plus Architect decision record plus parity check across all three languages |
| R3 | R2 plus Claude Opus review plus Strategic Orchestrator acceptance plus human confirmation before release |

### 5.4 Escalation rules

Escalate **up**, never sideways. An agent may not re-delegate its own task to a peer.

| Trigger | Escalates to |
|---------|--------------|
| Confidence < 0.60 | The agent's orchestrator |
| Two failed attempts at the same task | Orchestrator, with both failure summaries |
| Requirement ambiguity or spec gap | Architect, then Strategic Orchestrator |
| Request to change `specifications/` | Architect, then Claude Opus review |
| Cross-language parity cannot be met | Strategic Orchestrator |
| Risk tier R3 detected | Strategic Orchestrator immediately, work pauses |
| Security finding at R2 or R3 | Strategic Orchestrator directly, bypassing the engineering chain |
| Budget for a tier exhausted | Orchestrator decides: decompose, downgrade scope, or stop |
| Two agents produce conflicting results | Orchestrator applies conflict resolution in `skills/claude/SKILLS.md` |
| Anything irreversible or outward-facing | Human |

Every escalation carries: the original brief, what was attempted, why it failed, and the
specific decision being requested. It does **not** carry the failed agent's transcript
(**TOK-010**).

### 5.5 Escalation flow

```
 Agent ──low confidence / blocked / R3──▶ Engineering Orchestrator
                                                │
                            resolvable? ── yes ─┴─▶ re-brief, retry once (cheaper first)
                                  │
                                  no
                                  ▼
                        Strategic Orchestrator (Claude Sonnet)
                                  │
              strategic / R3 / conflict? ── no ──▶ decide, re-delegate
                                  │
                                 yes
                                  ▼
                          Claude Opus escalation
                                  │
            irreversible / outward-facing? ── no ──▶ decide, record decision
                                  │
                                 yes
                                  ▼
                              Human confirmation
```

## 6. Token optimization policy

<!-- BEGIN:TOKEN-OPTIMIZATION-POLICY -->
<!-- Canonical source: agents.md section 6. Mirrored verbatim into claude.md,
     skills/claude/SKILLS.md and skills/codex/SKILLS.md. Verified byte-for-byte by
     scripts/validate-agent-docs.py. Never edit a mirrored copy in isolation. -->

### Mandatory token rules

These rules are normative for every orchestrator and every agent in this repository.

| ID | Rule |
|----|------|
| **TOK-001** | **Never pass the entire chat history** to a delegated agent. Pass a distilled brief only. |
| **TOK-002** | **Never pass a full repository** unless whole-repo comprehension is the task itself (the Gemini Pro route). |
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
| Whole-repo comprehension (Gemini Pro) | 200 k tokens | 4 k tokens |

A task that cannot fit its tier budget is **decomposed**, not granted a larger budget.

<!-- END:TOKEN-OPTIMIZATION-POLICY -->

## 7. Dynamic agent creation

**Agents are spawned only when a task requires them. Never create an agent
proactively, never keep one warm, never staff a role just in case.**

### 7.1 Sizing table

| Tier | Definition | Agents | Shape |
|------|-----------|--------|-------|
| **Simple** | One file or one mechanical change, no design choice | **1** | Worker only. No reviewer if the change is non-behavioural |
| **Medium** | One subsystem, or one language of a known change | **2–3** | Worker plus reviewer, plus an orchestrator if sequencing is needed |
| **Large** | Cross-language, new capability, or 3 or more components | **3–5** | Orchestrator plus 2–3 workers plus reviewer |
| **Enterprise** | Multi-quarter, breaking, or externally visible commitment | **5–8** | Both orchestrators, architect, workers, reviewer, simulation |
| **Hard maximum** | Any task, any tier | **10** | Exceeding this means the task was not decomposed |

### 7.2 Parallelism limits

| Boundary | Limit |
|----------|-------|
| Claude-side concurrent agents (`skills/claude/SKILLS.md`) | **8** |
| Codex-side concurrent agents (`skills/codex/SKILLS.md`) | **10** |
| System-wide concurrent agents | **10** |

The system-wide ceiling binds both trees. Claude running 8 leaves 2 for Codex, not 10.

### 7.3 Spawn preconditions

An orchestrator may spawn an agent only when **all** of these hold:

1. The task is decomposed to a single, independently verifiable outcome.
2. A brief within the tier budget can be written (**TOK-011**).
3. No already-running agent covers the same work.
4. The work cannot be answered by a direct tool call (**TOK-009**).
5. The result has a named consumer.

Fail any one and the orchestrator does the work directly or decomposes further.

### 7.4 Parallel versus sequential

Parallelise only when tasks touch **disjoint** files and share no decision. Anything that
touches the same public contract runs **sequentially**, because two agents editing one
contract produce a conflict that costs more than the time saved.

## 8. Delegation brief format

Every delegation uses exactly this structure. Nothing else is transmitted (**TOK-004**).

```markdown
## Objective
<one sentence: the verifiable outcome>

## Requirements
- <numbered, testable; cite spec requirement IDs where they exist>

## Constraints
- <what must not change; budget; tier; risk tier>

## Decisions already made
- <decision plus one-line rationale; these are NOT reopened>

## Relevant files
- path/to/file.ext:LINE-LINE — why it matters

## Return
outcome | artefacts changed | decisions made | open questions | confidence
```

## 9. Verification contract

No agent reports success without evidence. Commands for this repository:

```bash
dotnet test ./dotnet/BastionVault.IntegrationSdk.Tests/BastionVault.IntegrationSdk.Tests.csproj
cargo test --manifest-path ./rust/bastionvault-integration-sdk/Cargo.toml
PYTHONPATH=./python/src python -m unittest discover -s ./python/tests
```

| Rule | Statement |
|------|-----------|
| **VER-001** | A change to library code is unverified until its language's test command has run and passed |
| **VER-002** | A behavioural change is incomplete until all three languages match, or a decision record explains the divergence |
| **VER-003** | Report failures verbatim. Never describe a failing build as mostly working |
| **VER-004** | Coverage must not fall below the 95 % floor in `specifications/01-conformance-and-quality.md` |
| **VER-005** | Quote only the evidence that changes the verdict, not full logs (**TOK-007**) |

## 10. Consistency validation

`scripts/validate-agent-docs.py` enforces the following, and CI or any agent may run it:

1. All four agent documents exist at their specified paths.
2. The token-optimization block in §6 is **byte-identical** in all four.
3. Every model named in a routing table exists in the §4.1 registry.
4. Agent-count and parallelism limits agree across all four documents.
5. No responsibility is claimed as owned by two roles.

```bash
python scripts/validate-agent-docs.py
```
