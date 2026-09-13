# skills/claude/SKILLS.md — Claude Routing Rules

**Scope:** how the Claude Strategic Orchestrator selects skills, models, and agents.
**Authority:** subordinate to [`agents.md`](../../agents.md). Behaviour rules live in
[`claude.md`](../../claude.md). This file is the **routing layer** between them.
**Version:** 1.0.0 · 2026-09-13

## 1. Responsibilities owned here

Claude-side orchestration covers exactly six areas. Anything outside them routes to
[`skills/codex/SKILLS.md`](../codex/SKILLS.md).

| # | Responsibility | Produces |
|---|----------------|----------|
| 1 | **Strategic planning** | Decomposition, sequencing, agent plan, acceptance criteria |
| 2 | **Architecture design** | Design options, decision records, contract definitions |
| 3 | **Technical reviews** | Verdict plus findings tied to files, lines, requirement IDs |
| 4 | **Risk assessment** | Risk tier, failure modes, mitigations, go/no-go |
| 5 | **Cross-team coordination** | Work packages, handoffs, conflict resolutions |
| 6 | **Research synthesis** | Cited evidence brief with a recommendation |

**Not owned here:** writing production code, debugging, test authoring, refactoring,
CI/CD, deployment. Those are Codex responsibilities, without exception beyond
[`claude.md`](../../claude.md) §1.1.

## 2. Model registry

### 2.1 Models Claude runs directly

Claude runs **Claude-family models only** (`agents.md` §4.5, **FAM-001**). There are two.

| Model | Role in the Claude tree | Invoke when | Never invoke for |
|-------|------------------------|-------------|------------------|
| **Claude Sonnet** | **Primary.** Default for every Claude-side task | Planning, review, coordination, synthesis, risk triage | Bulk code generation, reading a whole repository |
| **Claude Opus** | **Escalation.** Authority of last resort below a human | Architecture review, R3 calls, conflict arbitration, enterprise planning, Sonnet result < 0.60 confidence | Routine review, first attempts, mechanical work |

### 2.2 Models Claude reaches by delegation

These are GPT-family models in the Engineering tree. Claude never calls one. Claude
writes a brief and hands it to the Codex orchestrator, which selects among them.

| Model | Role | Claude delegates to it for | Returns |
|-------|------|---------------------------|---------|
| **GPT-6** | Architecture partner | Complex architecture generation, deep debugging analysis, alternative design generation | A design Claude Opus reviews |
| **GPT-6 Mini** | Worker | Implementation, scans, simple research, data gathering | A change plus test evidence |
| **GPT Terra** | Wide reader | Impact mapping across `specifications/` and all three SDKs, large surveys | A synthesis Claude Sonnet interprets |
| **GPT Sol** | Simulation specialist | Retry and failover simulation, load and rate-limit projection, cost and timeline forecasting | A projection with stated assumptions |

Default architecture:

```
Claude Sonnet = primary              → runs by default
Claude Opus   = escalation           → runs on a recorded trigger only
GPT-6         = architecture partner → by delegation; generates, never approves
GPT Sol       = simulation specialist → by delegation; projects, never measures
```

Costs, families, and the global routing matrix are in [`agents.md`](../../agents.md) §4.
This file does not restate them.

## 3. Skill routing table

**First match wins.** Each row names the skill, the model, and the agents it may spawn.

| # | Signal in the request | Skill | Model | Agents |
|---|----------------------|-------|-------|--------|
| 1 | "what is", "where is", single lookup | Direct answer, no agent | Claude Sonnet | 0 |
| 2 | Implement, fix, refactor, test, build, deploy | **Handoff to Codex** | — | per Codex file |
| 3 | Plan, break down, sequence, estimate | Strategic planning | Claude Sonnet | 0–2 |
| 4 | Design, contract, API shape, new subsystem | Architecture design | Delegate: GPT-6 generates · Claude Opus reviews at handback | 2–3 |
| 5 | Review, audit, is this correct, parity check | Technical review | Claude Sonnet (Opus at R2+) | 1–2 |
| 6 | Risk, blast radius, what breaks, security posture | Risk assessment | Claude Sonnet, Opus at R3 | 1–2 |
| 7 | What if, forecast, under load, how long, how much | Simulation | Delegate: GPT Sol · Claude Sonnet interprets | 1–2 |
| 8 | Across the whole repo, every SDK, everywhere | Wide-context survey | Delegate: GPT Terra · Claude Sonnet synthesises | 1 |
| 9 | Compare, prior art, how do others, is X supported | Research synthesis | Delegate: GPT-6 Mini gathers · Claude Sonnet synthesises | 1–2 |
| 10 | Multi-quarter, breaking change, release commitment | Enterprise planning | Delegate: GPT-6 + GPT Sol · then Claude Opus arbitrates | 5–8 |

Ambiguous request → run row 1 against the *decomposition* question before routing.

## 4. Confidence scoring

Claude uses the global model in [`agents.md`](../../agents.md) §5.1–5.2. Claude-side
application rules:

| Rule | Statement |
|------|-----------|
| **CCF-001** | Claude reports its own confidence on every plan, review, and decision |
| **CCF-002** | Claude **recomputes** a delegate's confidence rather than trusting it. A delegate claiming ≥ 0.85 with no build or test evidence is capped at 0.60 |
| **CCF-003** | A review verdict below 0.60 confidence is not a verdict. Escalate to Opus |
| **CCF-004** | Two consecutive sub-0.60 results on the same task means the task is wrongly framed. Re-decompose rather than retry |
| **CCF-005** | Confidence is never raised by adding Opus. Opus changes the decision, not the evidence |

## 5. Risk scoring

Tiers R0–R3 and their gates are defined in [`agents.md`](../../agents.md) §5.3.
Claude-side application rules:

| Rule | Statement |
|------|-----------|
| **CRS-001** | Claude assigns the risk tier **before** delegating, and states it in the brief |
| **CRS-002** | A delegate may raise a tier. Only Claude may lower one, with a recorded reason |
| **CRS-003** | Anything touching auth, tokens, TLS, or secret material starts at **R2** minimum |
| **CRS-004** | Anything changing `specifications/` or a published artefact is **R3** |
| **CRS-005** | R3 work pauses on detection and goes to the Strategic Orchestrator immediately |
| **CRS-006** | Unknown blast radius is scored at the **higher** tier, never the lower |

## 6. Escalation policy

The global ladder is [`agents.md`](../../agents.md) §5.4–5.5. Claude-side specifics:

| From | Trigger | To |
|------|---------|-----|
| Claude Sonnet | Confidence < 0.60, R3 tier, arbitration needed, architecture review | **Claude Opus** |
| Claude Sonnet | Design generation needed before review | **Codex orchestrator → GPT-6** (delegation, not escalation) |
| Claude Sonnet | Question needs whole-repo context | **Codex orchestrator → GPT Terra** |
| Claude Sonnet | Question is a projection, not a fact | **Codex orchestrator → GPT Sol** |
| Claude Opus | Irreversible, outward-facing, or a genuine product decision | **Human** |
| Any Claude agent | Task is implementation | **Codex orchestrator** (handoff, not escalation) |

Downgrade is mandatory too: once an Opus-level decision is recorded, follow-up work
returns to Sonnet. Claude does not stay at Opus because the topic was once hard.

## 7. Conflict resolution

Applies when two agents return incompatible results, or when a reviewer and an author
disagree. Claude arbitrates. Evaluate in order, stop at the first rule that resolves it:

| # | Rule | Rationale |
|---|------|-----------|
| 1 | **Specification wins.** The result matching `specifications/` is correct | Spec is the single source of truth |
| 2 | **Evidence wins.** A result with a passing build or test beats one without | Verification beats assertion |
| 3 | **Parity wins.** The result preserving cross-language behaviour is correct | Parity is a hard project goal |
| 4 | **Security wins.** On a tie with a security dimension, the safer result wins | R2+ default |
| 5 | **Reviewer wins on correctness, author wins on style** | Scope the disagreement |
| 6 | **Higher confidence wins**, only when both cite evidence of the same kind | Last mechanical tie-break |
| 7 | **Unresolved → Claude Opus arbitrates.** Opus decides, records the decision, closes it | Someone must decide |
| 8 | **Opus cannot resolve → human**, with both positions stated fairly | Genuine product judgement |

Every resolution is recorded as a decision. A resolved conflict is never reopened by a
delegate; reopening requires new evidence and goes to Claude.

## 8. Parallel agent limits

| Limit | Value |
|-------|-------|
| **Maximum parallel agents (Claude side)** | **8** |
| System-wide ceiling shared with Codex | 10 |
| Concurrent Claude Opus agents | 1 |
| Concurrent Codex handoffs in flight | 3 |

Sizing by task tier follows [`agents.md`](../../agents.md) §7.1: simple 1, medium 2–3,
large 3–5, enterprise 5–8, hard maximum 10. **Agents are created only when required,
never proactively.**

Claude reserves at least 2 of the 10 system-wide slots for Codex whenever an
implementation handoff is pending.

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
| **TOK-002** | **Never pass a full repository** unless whole-repo comprehension is the task itself (the GPT Terra route). |
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
| Whole-repo comprehension (GPT Terra) | 200 k tokens | 4 k tokens |

A task that cannot fit its tier budget is **decomposed**, not granted a larger budget.

<!-- END:TOKEN-OPTIMIZATION-POLICY -->

## 10. Handoff to Codex

The handoff is the **only** point at which the model family changes on the way down, and
the handback is the only point on the way up (`agents.md` §4.5, **FAM-003**). Claude does
not call a GPT model to "check something quickly" mid-task; that is a second crossing and
it costs a second brief.

Claude hands implementation work over with a brief in the format of
[`agents.md`](../../agents.md) §8, plus two Claude-side additions:

```markdown
## Risk tier
R0 | R1 | R2 | R3   ← assigned by Claude, may be raised by Codex, never lowered by Codex

## Acceptance criteria
- <what Claude will check on review; the delegate is told the gate in advance>
```

Claude receives back: outcome, artefacts changed, decisions made, open questions,
confidence. Claude then reviews per [`claude.md`](../../claude.md) §3.1 and either accepts,
returns required fixes, or escalates.
