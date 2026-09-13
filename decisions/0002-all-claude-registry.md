# DR-0002 — The model registry is Claude-only

**Status:** Accepted · **Date:** 2026-09-13 · **Milestone:** — (agent tooling)
**Author:** Strategic Orchestrator (Claude) · **Risk tier:** R2 · **Size tier:** Medium
**Supersedes:** the GPT half of the registry in `agents.md` §4.1 and the CLI binding in
D-M0-11 of [`DR-0001`](0001-m0-harness.md)

These decisions are made. No delegate reopens them (TOK-008).

## Context

`agents.md` §4.1 registered six models in two families: Claude Sonnet and Claude Opus in
the Strategic tree, and GPT-6 Mini, GPT-6, GPT Terra and GPT Sol in the Engineering tree.
`skills/claude/SKILLS.md` named the GPT models in its delegation, routing and escalation
tables, which is what surfaced the problem: the Claude-side routing document read as if
Claude selected OpenAI models.

The dual-tree structure was never the problem. The **family split** was: it bound the
orchestrator boundary — which exists to separate *authority*, so that no agent approves
its own work — to a vendor boundary, which has nothing to do with authority.

## Decisions

### D-0002-1 — One family, three models

The registry is Claude Opus 5, Claude Sonnet 5 and Claude Haiku 4.5. No GPT model, no
Gemini model, no third-party family (**FAM-005**).

**Rejected: keep the GPT Engineering tree and only rename models in the Claude file.**
Cheapest change, and wrong: `skills/claude/SKILLS.md` is subordinate to `agents.md`, so a
rename there would contradict the registry and fail `scripts/validate-agent-docs.py` C3.

**Rejected: collapse to a single tree.** The two trees earn their keep independently of
the vendor split. Removing them would remove the handback gate (§4.4), which is the only
thing that stops an author approving its own change (**REV-002**).

### D-0002-2 — The tree assigns authority, not family

`agents.md` §4.5 is now the **tree isolation rule**. FAM-001 and FAM-002 no longer say
"never invoke the other family"; they say a Strategic agent never does Engineering work
itself, and an Engineering agent never takes a Strategic decision. FAM-003 is unchanged:
exactly two crossings per work package.

**Consequence, accepted:** Claude Opus 5 and Claude Sonnet 5 now appear in both trees, so
the author and its reviewer can run the same model. The gate is satisfied by a *different
agent*, one tree up, holding the diff and the acceptance criteria and not the author's
reasoning (**CTX-003**). "Same model" is never a reason to skip the handback.

### D-0002-3 — Role-to-model mapping

| Retired role | Replacement |
|--------------|-------------|
| GPT-6 Mini | Claude Haiku 4.5 (Engineering, mechanical work only) |
| GPT-6 | Claude Sonnet 5 by default; Claude Opus 5 for complex design, hard debugging, pipeline redesign |
| GPT Terra | A **wide-context survey** run on Claude Sonnet 5 — a capability, not a model |
| GPT Sol | **Simulation** run on Engineering-tree Claude Opus 5 — a capability, not a model |

Terra and Sol were the only registry entries that named a capability rather than a tier.
Making them capabilities keeps the routing rows (`agents.md` §4.2 rows 5 and 6) intact
while removing two registry entries.

### D-0002-4 — CLI bindings move to the `claude` CLI

`agents.md` §4.1.1 now binds `claude -m <id>`: `claude-opus-5`, `claude-sonnet-5`,
`claude-haiku-4-5-20251001`. A delegation still passes `-m` explicitly, and a binding that
no longer resolves is still a blocking condition.

### D-0002-5 — COST-001 changes shape

It used to name one starting model. It now names the lowest rung that fits: Claude
Haiku 4.5 for single-file mechanical work, Claude Sonnet 5 for everything else in the
Engineering tree. Upgrading still requires a recorded trigger.

## Consequences

- Cost per unit of implementation rises. The old Engineering default was a 1× model for
  every task; the new one is 1× only for mechanical work and 4× otherwise. That is the
  price of the honest tiering in D-0002-5, and it is bounded by **COST-001**.
- `scripts/validate-agent-docs.py` check 6 becomes tree isolation. It also now rejects
  any surviving GPT or Gemini mention as an unregistered model, and rejects an
  unversioned `Claude Sonnet`, because the registry names versions.
- Model names are versioned everywhere (`Claude Sonnet 5`, not `Claude Sonnet`). A future
  model bump is a registry edit plus a mechanical rename, caught by C3 if missed.
- D-M0-11 in DR-0001 is rebound to `claude-sonnet-5`: W1–W5 are multi-file and
  behavioural, which is above the Claude Haiku 4.5 rung.

## Verification

```bash
python scripts/validate-agent-docs.py
```

Passes at the time of writing: C1–C6, with the registry reported as
`['Claude Haiku 4.5', 'Claude Opus 5', 'Claude Sonnet 5']`.
