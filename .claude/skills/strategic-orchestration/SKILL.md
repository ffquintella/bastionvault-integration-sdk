---
name: strategic-orchestration
description: Routing rules for the Claude Strategic Orchestrator of bastionvault-integration-sdk — which tree owns a task, which rung runs it, when to escalate to Claude Opus 5, how to size and sequence agents, how to resolve a conflict between two agents, and how to keep CHANGELOG.md and ROADMAP.md current. Use before planning, decomposing, briefing, reviewing, arbitrating, assigning a risk tier, or accepting a merge in this repository; and whenever deciding whether a task belongs to Claude directly or to a delegate.
---

# Strategic orchestration routing

The canonical rules are in the repository, not in this file. This skill exists so the
harness can find them; duplicating them here would break the single-definition rule
(`agents.md` §0).

## Read, in this order

1. [`agents.md`](../../../agents.md) — hierarchy, model registry (§4.1), CLI bindings
   (§4.1.1), routing matrix (§4.2), tie-breakers (§4.3), reviewer pairing (§4.4), tree
   isolation (§4.5), confidence and risk (§5), token policy (§6), agent-count policy (§7),
   records (§11). **Where this file and any other agent document disagree, `agents.md`
   wins.**
2. [`skills/claude/SKILLS.md`](../../../skills/claude/SKILLS.md) — the seven
   Strategic responsibilities, the model registry as Claude sees it, agent sizing and
   parallelism limits, and the conflict-resolution procedure.
3. [`claude.md`](../../../claude.md) — how Claude behaves inside those rules, and §1.1,
   the six exceptions that are the only times Claude writes code directly.

`agents.md` and `skills/claude/SKILLS.md` are imported by `claude.md`, so they are already
in context. Open them to check a specific table; do not re-read them wholesale.

## The harness bindings that enforce this

| Rule | Where it is enforced |
|------|----------------------|
| Claude Sonnet 5 is the default, Claude Opus 5 an escalation (`claude.md` §4) | `.claude/settings.json` → `"model": "sonnet"` |
| Routing matrix rows 1, 2, 3, 5, 6 (Engineering tree) | `.claude/agents/eng-mechanical.md`, `eng-implementation.md`, `eng-deep.md`, `eng-survey.md` |
| Routing matrix row 4, R3 calls, arbitration (Strategic tree) | `.claude/agents/strategic-review.md` |
| Delegation brief shape (`agents.md` §8, **TOK-004**) | [`engineering-delegation`](../engineering-delegation/SKILL.md) |
| Harness bindings agree with the documents | `scripts/validate-agent-docs.py`, check C7 |

A rule that exists only as prose is a rule the harness cannot apply. When a routing
policy changes, change the binding in the same commit — check C7 fails otherwise.

## The two reflexes this skill exists to install

1. **Before opening an implementation file to edit it**, check `claude.md` §1.1. Unless
   one of the six exceptions holds, the correct next action is a delegation brief, not an
   edit.
2. **Before reaching for Claude Opus 5**, check that Claude Sonnet 5 has attempted the
   task and that you can name the trigger (`agents.md` §4.3 rule 4). Escalation without a
   recorded trigger is premature.
