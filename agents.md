# agents.md — Multi-Agent Orchestration Specification

**Scope:** every automated agent that works on `bastionvault-integration-sdk`.
**Status:** normative. Where this file and any other agent document disagree, **this file wins**.
**Version:** 1.3.0 · 2026-09-14

## 0. How the agent documents fit together

| File | Owns | Must not contain |
|------|------|------------------|
| `agents.md` (this file) | Hierarchy, responsibility boundaries, escalation, model routing, confidence and risk scoring, token and context policy, agent-count policy | Vendor-specific prompting detail |
| `claude.md` | How Claude behaves as CTO / architect / reviewer / strategic planner | Routing tables, token rules (references them) |
| `skills/claude/SKILLS.md` | Claude-side routing rules, strategic skill catalogue, conflict resolution | Implementation workflow |
| `skills/codex/SKILLS.md` | Codex-side routing rules, engineering and review workflow, CI/CD | Strategic planning policy |
| `ROADMAP.md` | Milestone plan, sequencing decisions, risk register, current state | Behaviour requirements (they live in `specifications/`) |
| `CHANGELOG.md` | What has shipped, release by release, newest first | Rationale (it lives in `decisions/`) and plans (they live in `ROADMAP.md`) |

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
| `ROADMAP.md` | Where the work is going: milestones, sequencing, risks (§11, **REC-002**) |
| `CHANGELOG.md` | What has already happened, release by release (§11, **REC-001**) |
| `decisions/` | Decision records. Read one, never re-derive it (**TOK-008**) |

Three consequences bind every agent:

1. **Behavioural parity.** A change to one language's observable behaviour is incomplete
   until the other two match, or until a decision record says why they may differ.
2. **Spec-first.** Behaviour is changed in `specifications/` first, then implemented.
   An implementation agent that wants to change behaviour must escalate, not improvise.
3. **Recorded.** A landed change is not finished until `CHANGELOG.md` reflects it, and a
   closed milestone is not closed until `ROADMAP.md` reflects it (§11).

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
| **Default model** | Claude Sonnet 5 · escalates to Claude Opus 5 (see §5) |
| **Delegates to** | Engineering Orchestrator, Architect, Research Agent, Simulation Agent |

### 3.2 Engineering Orchestrator

| | |
|---|---|
| **Owns** | Turning a work package into concrete tasks, assigning engineers, sequencing, merge decisions, build and test green-ness, cost control inside the engineering tree |
| **Must not** | Redefine requirements, change specification behaviour, accept scope the Strategic Orchestrator did not authorise |
| **Default model** | Claude Sonnet 5 · escalates to Claude Opus 5 |
| **Delegates to** | Backend / Frontend / DevOps / Security Engineer, Reviewer Agent |

### 3.3 Architect

| | |
|---|---|
| **Owns** | System and API design, cross-language contract design, decision records, design alternatives with tradeoffs, `specifications/` changes |
| **Must not** | Implement, or approve its own design |
| **Default model** | Claude Opus 5 for design generation (Engineering tree) · a second Claude Opus 5 agent for design review (Strategic tree) |
| **Reports to** | Strategic Orchestrator |

### 3.4 Backend Engineer

| | |
|---|---|
| **Owns** | SDK implementation in `dotnet/`, `rust/`, `python/`; transport, auth, engine clients, error mapping; unit and conformance tests for its change |
| **Must not** | Alter `specifications/`, weaken a test to make it pass, change public API shape without an Architect decision |
| **Default model** | Claude Sonnet 5 · Claude Haiku 4.5 for mechanical edits · escalates to Claude Opus 5 |
| **Reports to** | Engineering Orchestrator |

### 3.5 Frontend Engineer

| | |
|---|---|
| **Owns** | Any user-facing surface: documentation sites, samples, generated API reference, rendered usage guides |
| **Must not** | Change SDK behaviour to suit a sample |
| **Default model** | Claude Haiku 4.5 · Claude Sonnet 5 for prose-heavy surfaces |
| **Reports to** | Engineering Orchestrator |
| **Note** | This repository is a library. This role stays **unstaffed** until a task has a user-facing surface. Do not spawn it by default. |

### 3.6 DevOps Engineer

| | |
|---|---|
| **Owns** | `.github/workflows/`, packaging (`dotnet pack`, `cargo package`, `python -m build`), release gating, coverage measurement plumbing, artifact publication |
| **Must not** | Disable a failing gate to unblock a merge, publish a release without Strategic Orchestrator acceptance |
| **Default model** | Claude Sonnet 5 · escalates to Claude Opus 5 for pipeline redesign |
| **Reports to** | Engineering Orchestrator |

### 3.7 Security Engineer

| | |
|---|---|
| **Owns** | Auth flows, token lifecycle, TLS defaults, secret-material handling and log hygiene, dependency and supply-chain audit, threat modelling |
| **Must not** | Ship a mitigation that changes public behaviour without an Architect decision |
| **Default model** | Claude Haiku 4.5 for scanning · Claude Opus 5 for threat-model review |
| **Reports to** | Engineering Orchestrator · **has a direct escalation line to the Strategic Orchestrator** for any finding at risk tier R3 |

### 3.8 Research Agent

| | |
|---|---|
| **Owns** | Gathering external and in-repo evidence, prior-art comparison, API compatibility checks, producing a cited evidence brief |
| **Must not** | Make decisions, write code, or present an unsourced claim as fact |
| **Default model** | Claude Haiku 4.5 · Claude Sonnet 5 when the corpus is the whole repository |
| **Reports to** | Whichever orchestrator requested it |

### 3.9 Reviewer Agent

| | |
|---|---|
| **Owns** | Correctness review, spec-conformance review (requirement ID traceability), cross-language parity review, test adequacy, the block or approve verdict |
| **Must not** | Rewrite the change it is reviewing beyond trivial fixes, or review its own work |
| **Default model** | Claude Sonnet 5 · Claude Opus 5 for architecture-level or R3 reviews |
| **Reports to** | Engineering Orchestrator · reports blocking verdicts upward |

### 3.10 Simulation Agent

| | |
|---|---|
| **Owns** | Forecasting and what-if analysis: retry and backoff behaviour under failure, cluster failover scenarios, rate-limit and load projections, cost and timeline projection, migration impact |
| **Must not** | Present a simulation as a measurement. Every output states its assumptions |
| **Default model** | Claude Opus 5 |
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

One family, three models, no third party. Every model is a Claude model. The **tree**
decides the authority a model carries; the vendor no longer distinguishes them.

| Model | Family | Tree | Role | Use it for | Cost | Never use for |
|-------|--------|------|------|-----------|------|---------------|
| **Claude Opus 5** | Claude | Both | Deep reasoner | Strategic: architecture review, R3 risk calls, conflict resolution, enterprise planning. Engineering: complex design generation, hard debugging, simulation and forecasting | 15× | Routine review, first attempts, bulk generation |
| **Claude Sonnet 5** | Claude | Both | Balanced primary | Strategic: planning, code review, parity review, coordination, synthesis. Engineering: implementation, tests, refactors, CI changes, wide-context survey | 4× | Single-file mechanical edits that Claude Haiku 4.5 already handles |
| **Claude Haiku 4.5** | Claude | Engineering | Fast worker | Mechanical edits, scans, fixture generation, simple in-repo lookups and data gathering | 1× | Architecture decisions, parity judgement, final approval |

A model listed in **Both** trees is not one agent wearing two hats. The Strategic instance
and the Engineering instance are separate agents with separate briefs, and the handback
gate (§4.4) always sits in the other tree from the author (**REV-002**).

### 4.1.1 CLI model bindings

The registry above names tiers, not invocation identifiers. A delegation needs a concrete
identifier, so the binding is recorded here once and referenced thereafter (**CLA-008**).
Bindings are for the `claude` CLI (`claude -m <id>`). They have **not** been checked
against a local install; confirm each id resolves before the first delegation of a
session.

| Registry model | CLI model id | Note |
|----------------|--------------|------|
| Claude Opus 5 | `claude-opus-5` | Escalation target only, on a recorded trigger |
| Claude Sonnet 5 | `claude-sonnet-5` | Default above the mechanical tier, in both trees |
| Claude Haiku 4.5 | `claude-haiku-4-5-20251001` | Default Engineering-tree worker for mechanical work (**COST-001**) |

A delegation always passes `-m` explicitly. The CLI's own configured default is **not**
authoritative for work in this repository: it can silently run a task above the tier
**COST-001** assigns it, or below the tier §4.2 requires.

A binding that no longer resolves is a blocking condition, not a licence to substitute a
neighbouring model.

### 4.1.2 Harness bindings

A policy that exists only as prose is a policy the harness cannot apply. §4.1 and §4.2 are
therefore bound to concrete configuration under `.claude/`, which is the only layer that
actually decides which model runs. The bindings are recorded here once, and verified
against the filesystem by `scripts/validate-agent-docs.py` check **C7**.

| Binding | File | Registry model |
|---------|------|----------------|
| Session default above the mechanical tier (`claude.md` §4) | `.claude/settings.json` → `"model"` | Claude Sonnet 5 |
| Routing matrix row 1 | `.claude/agents/eng-mechanical.md` | Claude Haiku 4.5 |
| Routing matrix row 2 | `.claude/agents/eng-implementation.md` | Claude Sonnet 5 |
| Routing matrix rows 3 and 6 | `.claude/agents/eng-deep.md` | Claude Opus 5 |
| Routing matrix row 5 | `.claude/agents/eng-survey.md` | Claude Sonnet 5 |
| Routing matrix row 4, R3 calls, arbitration | `.claude/agents/strategic-review.md` | Claude Opus 5 |
| Strategic routing rules, discoverable | `.claude/skills/strategic-orchestration/SKILL.md` | — |
| Delegation brief and rung selection, discoverable | `.claude/skills/engineering-delegation/SKILL.md` | — |

Three rules bind these bindings:

| ID | Rule |
|----|------|
| **BND-001** | An agent definition names a model tier its own tree is permitted to run (§4.5). A Strategic-tree definition never names the mechanical rung |
| **BND-002** | A change to §4.1 or §4.2 lands with the `.claude/` change in the same commit. Check **C7** fails otherwise, so the documents can never drift ahead of the harness |
| **BND-003** | Model tiers are bound by alias (`haiku`, `sonnet`, `opus`), not by the dated identifiers in §4.1.1, so a registry version bump does not silently repoint an agent. §4.1.1 stays authoritative for an explicit `-m` delegation |

The agent definitions are the delegation route in a Claude Code session; the `claude -m`
bindings in §4.1.1 are the route from a shell. Both cross the tree boundary exactly twice
(**FAM-003**), and neither skips the handback gate (**FAM-004**).

### 4.2 Routing matrix

**First match wins. Evaluate top to bottom.** This ordering is what makes routing
deterministic: never pick the best-fitting row, pick the **first** matching row.

**The triggers are written to be mutually exclusive**, so "first match wins" is a *check*
that the row was read correctly, not a judgement call that resolves an overlap. Each row
below states what puts a task **in** it and what puts a task **out** of it. Two rows
matching at once means a trigger was misread — re-read them before reaching for §4.3.

**The one discriminator that decides most of this repository's work: is the contract
settled?** A contract is settled when an accepted decision record pins the public names and
the behaviour, so the task is *transcription and verification* rather than design. Settled
is row 2. Unsettled is row 3. The **pathfinder** pass of a milestone is unsettled by
definition; every **parity pass** that follows it in a second or third language is settled
by definition, because the pathfinder's review is what settled it.

| # | Task class | Trigger | Tree | Primary | Review at handback | Escalation |
|---|-----------|---------|------|---------|--------------------|------------|
| 1 | **Simple** | **In:** one file, mechanical, no design choice, no security surface, no behavioural change. **Out:** anything behavioural, anything spanning two files, any parity judgement | Engineering | **Claude Haiku 4.5** | none | → row 2 |
| 2 | **Coding** | **In:** implement, debug, refactor, test, or change CI **against a settled contract** — including every **parity pass** of an already-reviewed design, however large. **This is the default rung for implementation.** **Out:** only when a row 3 condition below actually holds | Engineering | **Claude Sonnet 5** | **Claude Sonnet 5** in the Strategic tree (mandatory) | → Claude Opus 5, then row 3 |
| 3 | **Complex architecture** | **In:** exactly one of — (a) the **pathfinder** pass that first defines a contract, in the first language; (b) a change to an existing cross-language contract, a public API shape, or a published artefact; (c) a breaking change; (d) a change spanning 3 or more components; (e) debugging that has **already failed twice** at row 2. **Out:** implementing a contract that a decision record has already pinned | Engineering | **Claude Opus 5** | **Claude Opus 5** in the Strategic tree | → row 4 |
| 4 | **Architecture review** | Any design, decision record, or specification change awaiting approval | Strategic | **Claude Opus 5** | — | → human |
| 5 | **Large repository** | Question needs whole-repo or whole-spec context; impact mapping | Engineering | **Claude Sonnet 5**, wide-context survey | Claude Sonnet 5 synthesises | → row 3 |
| 6 | **Simulation / forecasting** | What-if, failure projection, load, cost, timeline | Engineering | **Claude Opus 5** | Claude Sonnet 5 interprets | → row 7 |
| 7 | **Enterprise planning** | Multi-quarter, multi-team, or irreversible commitment | Both | **Claude Opus 5** design plus simulation, then Strategic **Claude Opus 5** | Claude Opus 5 arbitrates | → human |

Row 7 is the only task class that runs in both trees at once. It still crosses the
boundary exactly twice: one delegation down, one handback up. Rows 2, 3 and 7 name the
same model on both sides of a gate; the gate is satisfied by a **different agent** in the
other tree, never by the author re-reading its own work (**REV-002**).

### 4.3 Deterministic tie-breakers

Applied in order when two rows appear to match. Because §4.2's triggers are mutually
exclusive, reaching this section usually means a trigger was misread — check that first.

1. **Lower row number wins** (cheaper route first). A row-3 claim over a settled contract
   is the common misroute, and it is always wrong: see §4.2's discriminator.
2. A **risk tier R3** task never routes below row 4, regardless of size.
3. A task with a **security surface** adds a Security Engineer review at any row.
4. Never upgrade a model without recording the trigger that caused it (§5.4). **The
   record is written before the delegation is dispatched, not after.** A trigger produced
   afterwards is a rationalisation, which is what writing it down exists to prevent —
   "this surface is risky" and "the cheaper rung might miss something" are not triggers. A
   *demonstrated* row-2 failure on this surface is.
5. The tree decides the authority, never the other way round (§4.5). A task never moves
   to the other tree, or skips a handoff, to reach a model it could reach anyway.
6. When still ambiguous, run row 1 on the *decomposition* question, not on the task.

### 4.4 Reviewer pairing rule

Engineering-tree output is never approved inside the Engineering tree. The final gate is
always one level up, which is also where the authority changes.

| Authored by | Final gate |
|-------------|------------|
| Claude Haiku 4.5 (Engineering) | Claude Sonnet 5 at handback, Claude Opus 5 at R2 or above |
| Claude Sonnet 5 (Engineering) | Claude Sonnet 5 at handback, Claude Opus 5 at R2 or above |
| Claude Opus 5 (Engineering) | Claude Opus 5 at handback, plus Claude Opus 5 arbitration for enterprise planning |
| Claude Sonnet 5 (Strategic) | Claude Opus 5 for strategy and architecture |
| Claude Opus 5 (Strategic) | Human, for anything irreversible or outward-facing |

Because one family now staffs both trees, "same model" is never a reason to skip the gate.
The reviewer is a separate agent, in the Strategic tree, holding the requirements and the
diff and not the author's reasoning (**CTX-003**). Claude Sonnet 5 may review Claude
Haiku 4.5 work *inside* the Engineering tree, but that is a pre-check, not the gate. The
gate is the handback.

### 4.5 Tree isolation rule

**One tree, one authority. The orchestrator boundary is the only place authority
changes.**

| Tree | Orchestrator | Permitted models |
|------|--------------|------------------|
| **Strategic** | Claude | Claude Opus 5, Claude Sonnet 5 |
| **Engineering** | Codex | Claude Opus 5, Claude Sonnet 5, Claude Haiku 4.5 |

**Codex** names the Engineering tree and its orchestrator, not a model family. Since
§4.1, every agent in both trees runs a Claude model.

| Rule | Statement |
|------|-----------|
| **FAM-001** | A Strategic-tree agent never does Engineering work itself, even though it could run the same model. It delegates to the Engineering orchestrator instead |
| **FAM-002** | An Engineering-tree agent never takes a Strategic decision: requirement interpretation, public API shape, risk tier, merge acceptance. It hands back instead |
| **FAM-003** | Exactly two crossings exist per work package: the handoff down (delegation brief) and the handback up (structured summary plus diff) |
| **FAM-004** | The review gate survives as the handback (§4.4), not as a Strategic-tree call made from inside the Engineering tree |
| **FAM-005** | No model outside the §4.1 registry runs in either tree. A non-Claude model is out of registry by definition |

**Why this reduces tokens.** One authority per tree means one context format and one set
of conventions for the whole of a work package. Nothing is re-briefed mid-task to satisfy
a second reader. Each crossing costs one compressed brief, and two crossings per work
package is both the floor and the cap. Unifying the families removes the translation cost
that used to sit on each crossing; the crossings themselves stay, because they are the
gate, not a vendor artefact.

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
| R0 | Reviewer Agent (Claude Sonnet 5) |
| R1 | Reviewer Agent plus Engineering Orchestrator sign-off |
| R2 | R1 plus Architect decision record plus parity check across all three languages |
| R3 | R2 plus Claude Opus 5 review plus Strategic Orchestrator acceptance plus human confirmation before release |

### 5.4 Escalation rules

Escalate **up**, never sideways. An agent may not re-delegate its own task to a peer.

| Trigger | Escalates to |
|---------|--------------|
| Confidence < 0.60 | The agent's orchestrator |
| Two failed attempts at the same task | Orchestrator, with both failure summaries |
| Requirement ambiguity or spec gap | Architect, then Strategic Orchestrator |
| Request to change `specifications/` | Architect, then Claude Opus 5 review |
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
                        Strategic Orchestrator (Claude Sonnet 5)
                                  │
              strategic / R3 / conflict? ── no ──▶ decide, re-delegate
                                  │
                                 yes
                                  ▼
                          Claude Opus 5 escalation
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
| **VER-006** | A change is unrecorded, and therefore unfinished, until `CHANGELOG.md` carries it (**REC-001**) and, at a milestone boundary, `ROADMAP.md` does too (**REC-002**) |

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

## 11. Project record keeping

Two files are the project's running memory: [`ROADMAP.md`](ROADMAP.md), which says where the
work is going, and [`CHANGELOG.md`](CHANGELOG.md), which says what has already happened. An
agent that lands work and leaves them stale has produced an undocumented change, which counts
as incomplete work, not as finished work awaiting paperwork.

| ID | Rule |
|----|------|
| **REC-001** | **Every user-visible or behavioural change gets a `CHANGELOG.md` entry in the same change that makes it.** Public API, error codes and messages, defaults, configuration surface, dependencies, security posture, and packaging all qualify. Internal refactors, test-only edits and formatting do not |
| **REC-002** | **Every milestone exit updates `ROADMAP.md`**: §2 current state, the milestone's row in §4, its §5 exit criteria, and the §8 risk register where the milestone changed a risk. A milestone is not closed until this is done |
| **REC-003** | New entries go under `## [Unreleased]` in the section that fits (**Added**, **Changed**, **Deprecated**, **Removed**, **Fixed**, **Security**, **Agent architecture**). A version heading is created only when a release is cut, and only by the Strategic Orchestrator |
| **REC-004** | **Both files are Strategic-tree owned.** An Engineering-tree agent never edits them; it returns the proposed changelog line as part of its structured summary (**TOK-007**), and the reviewing orchestrator writes it on acceptance |
| **REC-005** | An entry states the observable change and links to the decision record or requirement IDs behind it (**TOK-006**, **CLA-008**). It never restates specification prose and never duplicates a decision record's reasoning |
| **REC-006** | A change to `agents.md`, `claude.md`, `skills/**` or `scripts/validate-agent-docs.py` is recorded under **Agent architecture**. It carries no package version implication, but it is still a change the next agent must be able to find |

**Definition of done, extended.** The verification contract in §9 says a change is unverified
until its tests pass. §11 adds: a change is **unrecorded** until REC-001 (and REC-002 at a
milestone boundary) holds. Reviewers check both. "The changelog entry comes later" is the
same failure mode as "the test comes later".
