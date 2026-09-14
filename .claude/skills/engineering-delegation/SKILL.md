---
name: engineering-delegation
description: How to hand implementation work to the Engineering tree of bastionvault-integration-sdk and review what comes back — picking the cheapest rung that fits, writing the four-section delegation brief, staying inside the per-tier token budget, and applying the handback gate. Use when about to implement a feature, fix a defect, write or repair tests, refactor, debug a failing build or test, change CI/CD or packaging, make any multi-file code change in dotnet/, rust/ or python/, run a whole-repository survey, or review work a delegate returned.
---

# Engineering delegation

Claude does not write production code as its first move (`claude.md` §1.1).
Implementation-heavy work is briefed, delegated, and reviewed. The saving is the rung, not
the vendor: a delegate runs Claude Haiku 4.5 or Claude Sonnet 5 where the Strategic tree
would have spent Claude Opus 5 on strategy it is not doing.

## Canonical rules

[`skills/codex/SKILLS.md`](../../../skills/codex/SKILLS.md) — Engineering
responsibilities, the model ladder (§2), the engineering and review workflow, cost
control. Subordinate to [`agents.md`](../../../agents.md), which owns the routing matrix
(§4.2) and wins any disagreement (§0). Read them; this file does not restate them.

## Pick the rung — first match wins (`agents.md` §4.2)

| Task | Agent | Rung |
|------|-------|------|
| Single file, mechanical, no design choice, no security surface | `eng-mechanical` | Claude Haiku 4.5 |
| Implement, debug, refactor, test, CI change | `eng-implementation` | Claude Sonnet 5 |
| New subsystem, cross-language contract, breaking change, 3+ components; hard debugging after two failed attempts; simulation or forecasting | `eng-deep` | Claude Opus 5 |
| Whole-repo or whole-spec context, impact mapping | `eng-survey` | Claude Sonnet 5 |

Evaluate top to bottom and take the **first** matching row, not the best-fitting one — that
is what makes the routing deterministic. Never upgrade a rung without recording the
trigger (**TOK-012**, `agents.md` §4.3 rule 4). One agent per unit of work; never spawn an
agent to do what a `grep` answers (**TOK-009**).

## The brief — exactly four sections (**TOK-004**)

**requirements** · **constraints** · **decisions** · **relevant files**

Anything else is dropped. Author it fresh; never forward the conversation (**TOK-001**,
**TOK-003**). Reference files by path and line range (**TOK-005**) and cite requirement
IDs rather than quoting spec prose (**TOK-006**). Include the decisions already taken so
the delegate does not reopen them, and the acceptance criteria you will review against.
Target 70–80 % context reduction; below 60 % re-author the brief, above 90 % confirm the
delegate can act without follow-up questions (`agents.md` §6).

Budgets — simple 2 k in / 1 k out, medium 6 k / 2 k, large 15 k / 4 k, enterprise
30 k / 8 k, wide-context survey 200 k / 4 k. A task that will not fit its tier is
**decomposed**, never granted a larger budget (**TOK-011**).

## On handback

Review in the order in `claude.md` §3.1, stopping at the first blocking finding, and
return a verdict with each finding tied to a file, a line, and a requirement ID or a
concrete failure scenario. Engineering-tree output is never approved inside the
Engineering tree (**REV-002**, **FAM-004**); at R2 or above the gate is
`strategic-review`. On acceptance, Claude writes the `CHANGELOG.md` entry from the line the
delegate proposed (**REC-001**, **REC-004**) and, at a milestone boundary, updates
`ROADMAP.md` (**REC-002**). The task is not done until both hold.
