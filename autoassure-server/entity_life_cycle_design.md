# Problem

Deletion in this codebase was never designed and implemented.

# Notes

When working on this doc, do not add, update or remove contents without discussing with me first. When you are unsure, ask.

## Root cause

Deletion is hard. **There is no agreed protocol for what "delete" means in this system.**. In some situtations, deleting
a parent object must handle deletion of all its children objects. It is also possible that deleting is permitted without
cleaning up the refernces at all, for example, when deleting scenarios, it should never remove the scenarios
associations from the runs, as Runs are immutable records of what happened. In some other situations, deletion should
never happen, we should use "Archive" instead.

So this change has two deliverables, not one:

1. A correct deletion mechanism, applied to every first-class entity.
2. A written guideline — a Claude Code skill at `.claude/skills/entity-lifecycle/` — that is loaded before any code that
   lists, gets, creates, updates, deletes, or references an entity with a `LifecycleState`, so the next entity added to
   the system follows the same protocol by default instead of inventing a fifth variant.

## Why not just make the cascade atomic?

DynamoDB transactions cap at 100 items, and an Application can own far more rows than that, so parent and subtree can
never be removed in one write. Any correct answer here has to be a multi-step protocol — which is exactly why the
protocol has to be written down rather than re-derived per endpoint.

## High-level plan

We will support two deletion types:

- **Archive** — the entity is hidden from the list API, and no new entity can reference it when being added.
- **Physical delete** — the entity is marked as being deleted. Then all its children and grandchildren are deleted, before
the entity itself is deleted.

| Entity                         | Archive | Physical delete |
|--------------------------------|---------|-----------------|
| Organization                   | yes     | no              |
| Organization membership        | no      | yes             |
| User                           | no      | no              |
| Refresh token                  | no      | no              |
| Application                    | yes     | yes             |
| Scenario                       | yes     | yes             |
| Activity                       | no      | yes             |
| Precondition                   | yes     | yes             |
| EvidenceDefinition             | yes     | yes             |
| Environment                    | yes     | yes             |
| EnvironmentVariable            | no      | yes             |
| Run                            | no      | yes             |
| RunStatusUpdate                | no      | no              |
| RunningRun row                 | no      | no              |
| Scenario folder / tag mappings | no      | no              |

