# Problem

Deletion in this codebase was never designed — each endpoint invented its own approach as it was added. So today
deletion is **missing in some places, wrong in others, and inconsistent everywhere**, and there is no written rule for
whoever (person or agent) adds the next delete endpoint.

Four distinct failure modes exist right now:

### 1. Deletion is not implemented at all

`Application`, `Environment`, and `Organization` have no `DELETE` endpoint. A user cannot remove an Application they
created by mistake, and an Environment's variables can never be cleaned up. When these endpoints do get added, they will
inherit every problem below unless the rules change first.

### 2. Deletion is implemented as a non-atomic cascade (the race)

`DELETE /scenarios/{id}` deletes the children first, then the parent, in two separate writes:

```
ScenariosController.cs:178   await activityRepository.DeleteAllByScenarioAsync(...)   // step 1
ScenariosController.cs:179   await scenarioRepository.DeleteAsync(scenario)           // step 2
```

Between step 1 and step 2, `POST /scenarios/{id}/activities` still succeeds — its condition check only asks *"does the
Scenario row exist?"*, and it still does. The result is an Activity whose Scenario is gone: an orphan row, invisible to
every API, never cleaned up.

This is exactly the race described for Applications; it just lives one level down, because Application deletion does not
exist yet.

### 3. Deletion is implemented as a plain row delete, leaving dangling references

`DELETE /preconditions/{id}` and `DELETE /evidence-definitions/{id}` delete one row and nothing else. Every Activity
referencing that id keeps pointing at a dead row, **and those Activities then become permanently uneditable** — because
`UpdateActivity` re-validates that every referenced library row still exists, and it no longer does.

This one needs no concurrency at all. A single user, clicking two buttons in sequence, can brick their own Activities.

### 4. References are created without checking the target exists

`POST /applications/{id}/runs` never validates `ScenarioIds` in any way (`RunsController.cs:62`), and
`DynamoDbRunRepository.TrySaveAsync` condition-checks the Application and the Environment but not the Scenarios. So a
Run can be created against Scenario ids that were deleted a second ago, or that never existed.

## Root cause

Each of these is a symptom of the same gap: **there is no agreed protocol for what "delete" means in this system.**
Some code assumes rows are removed instantly, some assumes children clean themselves up, and the condition checks that
protect references were written per-endpoint rather than from a rule.

So this change has two deliverables, not one:

1. A correct deletion mechanism (below), applied to every first-class entity.
2. A written guideline — a Claude Code skill at `.claude/skills/entity-lifecycle/` — that is loaded before any code
   that lists, gets, creates, updates, deletes, or references an entity with a `LifecycleState`, so the next entity
   added to the system follows the same protocol by default instead of inventing a fifth variant.

## Why not just make the cascade atomic?

That is the obvious narrow fix, and it is not enough on two counts.

It would leave half the problem standing: failure mode 3 needs no concurrency at all, so a Precondition delete would
still brick Activities, and failure mode 1 is missing code rather than broken code, so Applications still could not be
deleted.

It is also not available to us. DynamoDB transactions cap at 100 items, and an Application can own far more rows than
that, so parent and subtree can never be removed in one write. Any correct answer here has to be a multi-step protocol —
which is exactly why the protocol has to be written down rather than re-derived per endpoint.

# Places that have this problem

## A. Multi-step deletes with a race window

| Where | What happens |
| --- | --- |
| `Controllers/ScenariosController.cs:178-179` | Deletes Activities, then the Scenario, in two separate writes. An Activity created in between is orphaned. |
| `Repositories/DynamoDbActivityRepository.cs:176` (`DeleteAllByScenarioAsync`) | Also has a known bug (comment at line 172): it never decrements `Scenario.ActivityCount`. Harmless only because the Scenario row is deleted right after. |

## B. Deletes that leave dangling references (no concurrency needed)

| Where | What happens |
| --- | --- |
| `Controllers/PreconditionsController.cs:110` → `DynamoDbPreconditionRepository.cs:111` | Plain `DeleteItem`. Activities referencing it keep the dead id. Those Activities can no longer be updated (`DynamoDbActivityRepository.cs` `PreconditionAndEvidenceExistsCheck` rejects them). |
| `Controllers/EvidenceDefinitionsController.cs:110` → `DynamoDbEvidenceDefinitionRepository.cs:109` | Same problem. |

## C. Missing deletes that will hit the same race the moment they are added

| Where | What happens |
| --- | --- |
| `Controllers/ApplicationsController.cs` | No `DELETE`. When added it must cascade to Scenarios → Activities, Environments → EnvironmentVariables, Preconditions, EvidenceDefinitions, Runs, Tries. |
| `Controllers/EnvironmentsController.cs` | No `DELETE` for the Environment itself. EnvironmentVariables would be orphaned. |
| Organization | No `DELETE`. Out of scope for this change, but it is the next level up and inherits the same design. |

## D. Writes that do not verify their target in the same transaction

**These are repository defects.** Whether a write is pointing at a parent or at a referent, the only place the check is
safe is inside the same transaction as the write itself. Controllers show symptoms, not the defect.

| Where | What happens |
| --- | --- |
| `Repositories/DynamoDbRunRepository.cs:38,56` | **The defect.** `TrySaveAsync` condition-checks Application and Environment, but **not** the Scenarios in `ScenarioIds`. Both Run and Try go through this one method, so both are affected. |
| `Controllers/TriesController.cs` | Symptom. Reads the Scenario, then saves — a read-then-write gap. The Scenario can be deleted in between. Adding *more* controller checks cannot close this; the check has to move into the transaction. |
| `Controllers/RunsController.cs:62` | Symptom, plus a genuine but *different* controller-level gap: `ScenarioIds` has no shape validation at all (see below). |
| `Repositories/DynamoDbActivityRepository.cs` (`TryUpdateAsync`) | Condition-checks the Activity row and every referenced Precondition/EvidenceDefinition, but **never the Scenario**. So an Activity whose Scenario was deleted is still editable today. Same defect, ownership edge instead of a reference edge. |

### Why the controller cannot fix this

A controller-level `GetByIdAsync` before the write is exactly the read-then-write gap this whole design exists to
eliminate — it is Rule 2 of the `entity-lifecycle` skill. The fix is one `ConditionCheck` per Scenario id inside
`TrySaveAsync`.

That also makes two checks the controller was doing (or should have been doing) redundant, because they fall out of key
construction for free: the Scenario table's key is `OrganizationId_ApplicationId` + `Id`, so a `ConditionCheck` built
from the Run's own `OrganizationId` and `ApplicationId` **can only match a Scenario that belongs to this Application and
this tenant**. A Scenario id from another Application simply has no row at that key, and the transaction is cancelled.

### What genuinely does belong in the controller

Only input *shape*, never existence:

- `ScenarioIds` non-empty.
- `ScenarioIds` capped. The transaction is 1 Application check + 1 Environment check + N Scenario checks + 1 `Put`, so
  N ≤ 97 hard. Cap at 50 in the contract for headroom and document the limit.
- Duplicates rejected or deduped — a DynamoDB transaction rejects two operations on the same item.
  (`PreconditionAndEvidenceExistsCheck` already handles this with `.Distinct()`; do the same.)

One knock-on: `TrySaveAsync` returns `bool` today, which cannot say *which* check failed. It needs a typed result (like
the existing `ActivitySaveResult`) so the controller can map "Scenario not found" to 400 and "Application/Environment
gone" to 404.

## E. Leaf reads that do not check their parent's state

Once the fixes land, orphans become impossible — so "orphaned Activities stay visible" is *not* the reason this needs
fixing. The real reason is that **deletion is asynchronous by design**, and Activities carry no `LifecycleState` of
their own (§2, they are leaves). Between the moment a Scenario is marked `Deleting` and the moment the purge removes its
Activities, those Activity rows are entirely valid — their parent exists, it is just on its way out. The parent's state
is the only thing that says so.

This section covers **only the leaf case**, which is a genuine defect. Applying the `Active` filter to every other
List/Get endpoint is new policy rather than a per-endpoint bug, so the full endpoint list lives in Task 3.

| Where | What happens |
| --- | --- |
| `Controllers/ActivitiesController.cs:93` (`ListActivities`) | Queries the Activity partition directly. During the purge window `GET /scenarios/{id}` returns 404 while `GET /scenarios/{id}/activities` still returns the full list — the API contradicts itself, and a stale UI keeps rendering a deleted Scenario's steps. Needs a consistent read of the Scenario's state; one `GetItem` per call. |
| EnvironmentVariables | **Not affected.** They are only ever returned embedded in `EnvironmentResponse` via `ToResponseAsync`, which is reached only after the Environment itself was fetched and checked. The parent check is already implicit. |

`UpdateActivity` was previously listed here. It belongs in §D: the guard is a condition check inside the repository's
update transaction, not a controller read.

One-off cleanup: any orphan rows created *before* this change ships will still be there afterwards, since nothing sweeps
them. Worth a one-time script if the dev database has any; not a design concern.

## F. Related but out of scope

`DynamoDbScenarioRepository.TryUpdateAsync` reconciles folder/tag mapping rows against a `previousState` that was read
earlier in the request. Two concurrent updates can leave a stale mapping row. Same family of problem (read-then-write),
different fix (optimistic version). Not addressed here — worth its own ticket.

# Solution

## 1. Naming

| Your name | Suggested name | Why |
| --- | --- | --- |
| `VisiblityState` | **`LifecycleState`** | The field controls more than visibility — it also blocks writes and blocks other entities from referencing it. "Lifecycle" says that. |
| `Default` | **`Active`** | "Default" describes how the value got there, not what it means. `Active` is what the entity *is*. |
| `Archived` | `Archived` | Good as-is. |
| `Deleting` | `Deleting` | Good as-is. Present-continuous is right: it is a promise that the row is on its way out, not that it is gone. |

```csharp
// Models/LifecycleState.cs
/// <summary>Where an entity sits in its lifecycle. Only Active entities can be listed, edited, or
/// referenced by newly created entities.</summary>
public enum LifecycleState
{
    /// <summary>Fully usable. Every entity is Active unless explicitly archived or deleted.</summary>
    Active,

    /// <summary>Kept for the record but out of the way: hidden from List APIs and cannot gain new
    /// children or references. Can be restored to Active.</summary>
    Archived,

    /// <summary>Marked for deletion and already gone as far as the API is concerned. Its children are
    /// being removed in the background. This state is irreversible.</summary>
    Deleting,
}
```

## 2. Which entities get the field

Only entities that **can gain children or be referenced** need the field. Pure leaves inherit their parent's fate, so
giving them a state would be dead weight and one more thing to keep in sync.

| Entity | Gets `LifecycleState`? | Why |
| --- | --- | --- |
| Application | Yes | Parent of everything. |
| Scenario | Yes | Parent of Activities. |
| Environment | Yes | Parent of EnvironmentVariables; referenced by Runs. |
| Precondition | Yes | Referenced by Activities. |
| EvidenceDefinition | Yes | Referenced by Activities. |
| Activity | No | Leaf. Only reachable via its Scenario. A Run embeds a *snapshot* of it (§8), not a reference, so it constrains nothing. |
| EnvironmentVariable | No | Leaf. |
| Run / Try | No | Immutable history. It carries snapshots, never references (§8), so nothing needs to gate it. |
| Organization | Not now | Add when Organization deletion is designed. |

Deciding *which* entities own which is a separate question from deciding *how* each relationship is cleaned up — see
§7, which classifies every edge in the system as ownership or reference.

Serialization: `LifecycleState` is written as a string attribute by `DynamoDbMapper`, same as the existing
`Classification` / `ValueSource` enums. **A missing attribute reads back as `Active`**, so no data backfill is needed —
this mirrors how `ActivityCount` already handles pre-existing rows (`DynamoDbActivityRepository.cs:366`).

## 3. What each state means

| | Listed by `ListX` | Listed by `ListX/archived` | Returned by `GetX(id)` | Can be edited | Can gain children / new references |
| --- | --- | --- | --- | --- | --- |
| `Active` | Yes | No | Yes | Yes | Yes |
| `Archived` | No | Yes | Yes (so it can be un-archived) | Yes | **No** |
| `Deleting` | No | No | **No (404)** | No | No |

`Archived` still resolving on `GetX(id)` matters: an old bookmark or a Run that points at an archived Scenario should
still open, it just should not clutter the list. `Deleting` behaves as if the row were already gone, because it will be.

**Archived entities need their own way back.** If `Archived` were hidden from the default list and reachable only by
direct id, nothing could ever be un-archived through the UI — there would be no way to find it. So every entity that can
be archived MUST also expose a sibling list endpoint returning **only** archived rows
(`GET /applications/{appId}/scenarios/archived` → `ListArchivedScenarios`). A sibling path rather than a `?state=`
parameter, so each gets a real operation name and the default list stays unambiguous.

**This table is enforced at the API layer, not in repositories.** Repositories return rows as stored; controllers apply
the policy through one shared helper in `Models`. Two reasons. It keeps repositories dumb, as the coding standards
require — "hide deleted entities" is a business rule. And it means the purge, which must list the very rows it just
marked `Deleting`, reuses the same reads as the API: had repositories filtered, the purge would have silently skipped
every row it was supposed to delete.

## 4. The one invariant that makes this safe

> **Every write that creates or points at a child must, in the same DynamoDB transaction, condition-check that the
> immediate parent row exists AND its `LifecycleState` is `Active`.**

The codebase is already 90% of the way there — it already does the "row exists" half everywhere
(`ApplicationExistsCheck`, `IncrementScenarioActivityCount`, `PreconditionAndEvidenceExistsCheck`, …). The change is
mechanical: extend each `ConditionExpression` from

```
attribute_exists(Id)
```

to

```
attribute_exists(Id) AND (attribute_not_exists(LifecycleState) OR LifecycleState = :active)
```

(the `attribute_not_exists` clause is what lets rows written before this change count as `Active`.)

**Immediate parent only, not the whole ancestor chain.** Checking the full chain (an Activity write checking both its
Scenario *and* its Application) would let us freeze an entire subtree by marking only the root — but then every *read*
would also have to fetch the ancestors to know whether a row is visible, which is expensive on the hot path. Instead
each row carries its own truth, so reads stay single-row, and the purge marks each level as it descends (§5).

## 5. Deletion flow

Two phases.

**Phase 1 — request (synchronous, inside the API call).** One transaction:

1. Conditionally set the entity's `LifecycleState = Deleting` (condition: row exists AND state is not already `Deleting`).
2. Insert a **deletion job row** into a new job table.

Both in the same `TransactWriteItems`, so a job can never be lost and an entity can never be marked without a job.
The API returns immediately. From this instant the entity is invisible and nothing new can attach to it.

**Phase 2 — purge (asynchronous, background worker).** For the marked entity, top-down:

```
mark children Deleting  →  recurse into each child  →  hard-delete the children's rows  →  hard-delete own row
```

Concretely:

| Purging | Steps, in order |
| --- | --- |
| Application | mark + purge all Scenarios (which takes their Tries with them) → mark + purge all Environments → mark + purge Preconditions and EvidenceDefinitions (skipping the detach step, §7) → hard-delete all Runs of the app → hard-delete the Application row |
| Scenario | hard-delete all Activities (batched) → hard-delete every Try in its partition (§8) → hard-delete its folder mapping row and every tag mapping row → hard-delete the Scenario row |
| Environment | hard-delete all EnvironmentVariables (paged) → hard-delete the Environment row |
| Precondition | detach: remove its id from every referring Activity in the Application (§7) → hard-delete the row |
| EvidenceDefinition | same as Precondition |

Activities and EnvironmentVariables are never marked — they are leaves, and their parent is already `Deleting`, so by
the invariant in §4 no new one can be created.

Every step is **idempotent**, so a crashed job can simply be re-run from the start.

**The purge reads through the same repository methods the API uses.** This works only because lifecycle filtering
lives at the API layer (§3, Task 3) and never in the repository. Had repositories filtered to `Active`, the purge —
which marks children `Deleting` and *then* lists them — would have received an empty list and silently leaked every
child it was supposed to delete. That bug is designed out rather than guarded against: there is no second set of
"unfiltered" repository methods to pick the wrong one from.

DynamoDB limits to respect in the purge: `BatchWriteItem` = 25 items / 16 MB per call; `TransactWriteItems` = 100 items
/ 4 MB; `Query` returns at most 1 MB per page, so every list-then-delete loop must page and must retry
`UnprocessedItems` (the existing `DeleteAllByScenarioAsync` already shows the right retry shape).

## 6. Why this is race-free

Take the Scenario → Activity case. The worker does:

```
T1: mark Scenario Deleting        (conditional write)
T2: query Activities of Scenario  (ConsistentRead)
T3: batch-delete them, then delete the Scenario row
```

A concurrent `CreateActivity` is a single `TransactWriteItems`, so its condition check and its `Put` commit atomically:

- If it commits **before T1**, the Activity is on disk before T1, so the consistent read at T2 sees it → deleted.
- If it commits **after T1**, its condition check sees `LifecycleState = Deleting` → the whole transaction is rejected,
  no Activity is written.

There is no third case. The same argument applies at every level, which is why the mark must happen level by level.

## 7. Ownership edges vs reference edges

Not every relationship is parent-child. An Activity does not *belong to* a Precondition — it **points at** one. Deleting
a Precondition must therefore not delete the Activity; it must remove the pointer and leave the Activity alone.

These are properties of **edges, not entities**. A Precondition sits on both kinds at once: it is an owned child of its
Application, and it is the target of references from Activities. So an entity cannot be labelled "parent" or "child" —
each edge has to be classified on its own.

| | **Ownership edge** | **Reference edge** |
| --- | --- | --- |
| Meaning | Y *belongs to* X. Y is meaningless without X. | Y *points at* X. Y stands on its own. |
| Deletion travels | **down** the edge: X deleted → Y deleted | **backwards** along the edge: X deleted → Y edited in place |
| Find the affected rows by | querying Y's own partition (Y is keyed by X) | querying **who points at X** — needs a reverse index |
| Strategy | **Cascade** | **Detach** — and only detach (§8 removes the historical-record exception) |

### Every edge in the system today

| Edge | Kind | On delete of the target |
| --- | --- | --- |
| Application → Scenario / Environment / Precondition / EvidenceDefinition / Run / Try | Ownership | Cascade |
| Scenario → Activity | Ownership | Cascade |
| Scenario → folder & tag mapping rows | Ownership (derived) | Cascade |
| Environment → EnvironmentVariable | Ownership | Cascade |
| **Activity → Precondition** | **Reference** | **Detach** |
| **Activity → EvidenceDefinition** | **Reference** | **Detach** |
| **Scenario → Try** | **Ownership** | **Cascade.** The try table's partition key is `OrganizationId_ScenarioId`, so a Try physically lives inside its Scenario — see §8. |
| Run/Try → Scenario / Environment / Activity | **No edge — snapshot** | Nothing to do. The Run carries its own copy of what it ran (§8). |

Note that a Run sits on ownership edges only: it is owned by its Application, a Try is owned by its Scenario, and it
holds **no** reference edges at all. That is deliberate — see §8.

### Why detach, and not the alternatives

| Option | Verdict |
| --- | --- |
| Leave the dead id in place, hide it on read | Rejected. The Activity stays uneditable, because its update re-validates every referenced id. |
| Refuse to delete a Precondition that is in use | Rejected. Needs the same reverse index anyway, and gives the user a dead end ("used by 12 Activities" — now what?). |
| **Detach: remove the id from every referring Activity during purge** | **Chosen.** Bounded by one Application, runs in the background, and because the row is already `Deleting` no new reference can appear — so the pass converges. |

Marking `Deleting` first is what makes detach terminate. Without it, detach would race forever against new references.

### The reverse index

Detach has to answer "who points at me?", which ownership never has to ask. The Activity table is keyed by
`OrganizationId_ScenarioId` + `Id`, so a new GSI is required: **`ApplicationIdIndex`, hash key
`OrganizationId_ApplicationId`** (the attribute must be written onto Activity rows by the mapper). The same index also
makes the Application-level cascade cheaper.

When a Precondition is purged as part of an *Application* purge, skip the detach — those Activities are about to be
deleted anyway.

### The transient-uneditable window (and why the obvious fix is wrong)

While detach is running, the Precondition row still exists with `LifecycleState = Deleting`. An Activity update that
still carries that id will be rejected by the Rule-2 condition check. So the *permanent* brick is fixed, but a short
window remains where editing such an Activity returns 400.

The tempting fix — "only validate ids the client is **adding**, allow ids already stored" — is wrong, and it is worth
recording why. If detach has already stripped the id from Activity A, the id is no longer in A's stored list, so a client
holding a pre-detach copy would be *re-adding* it... but the same client's list also looks "already stored" from its own
point of view. Evaluating added-vs-existing against a value read earlier in the request is a read-then-write gap: the
update can re-introduce an id the detach pass has already swept past, and the Precondition row is then hard-deleted
underneath it. That is the original dangling-reference bug, reintroduced through a different door.

Making it safe requires comparing against the stored value **at write time** — i.e. an optimistic-concurrency version
attribute on Activity, with the update conditioned on `Version = :expected`.

**Decision: accept the transient window for now.** It lasts as long as one background pass over one Application, it is
self-healing, and the client recovers by refetching. To make it actionable rather than confusing, the 400 MUST name the
offending ids so the UI can say "this Precondition was deleted — remove it and save again", instead of today's generic
"PreconditionIds/EvidenceIds must reference existing library rows".

Adding the version attribute is the documented upgrade path if that window turns out to bite. It would also fix the
unrelated lost-update race noted in §F, so the two are worth doing together when they are done.

## 8. Runs and Tries snapshot, they do not reference

**A historical record MUST NOT reference a live entity. It copies what it needs.**

The deletion argument is the weaker one. The real problem exists today, with nothing deleted at all: a Run stores
`ScenarioIds`, so the UI renders a past result by dereferencing the Scenario *as it is now*. Edit a Scenario — retitle
it, rewrite a step, reorder activities — and the history of every Run that used it is silently rewritten. A three-month-old
result gets shown next to today's test steps. For a test platform, "which version of the test produced this result?" is
the whole point of keeping the result, so this is a correctness bug in the product, not a tidiness issue.

Snapshotting fixes that and, as a side effect, makes the dangling reference impossible. So **retain disappears as a
strategy**, and the system gets an absolute rule with no exceptions: *no reference in this system ever points at a row
that is gone.*

### Domain model

```csharp
public record Run
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required Guid ApplicationId { get; init; }        // ownership — stays a real reference
    public required RunKind Kind { get; init; }
    public required RunEnvironmentSnapshot Environment { get; init; }   // was Guid EnvironmentId
    public required IReadOnlyList<RunScenarioSnapshot> Scenarios { get; init; }  // was IReadOnlyList<Guid> ScenarioIds
    ...
}
```

**Snapshot types, not the live domain models.** `RunEnvironmentSnapshot`, `RunScenarioSnapshot`, `RunActivitySnapshot`
— not `Environment`, `Scenario`, `Activity`. Embedding the live model welds the immutable record to a type that keeps
evolving: Task 1 adds `LifecycleState` to `Environment`, and every Run row would then carry a meaningless
`LifecycleState: Active` for an environment that may be long gone. `CreatedByUserId` / `UpdatedAt` are noise inside a
run record too. A snapshot copies only what the run needs to be rendered and (later) executed.

**The source ids stay, demoted.** Each snapshot keeps `SourceScenarioId` / `SourceEnvironmentId` so the UI can offer
"open the current Scenario". Its XML doc MUST say it is a **navigation hint that may no longer resolve**, and that it
MUST NOT be condition-checked or dereferenced for data — otherwise someone re-adds the reference edge through the back
door.

**`ActivityResult` becomes internal.** Its `ScenarioId` / `ActivityId` stop being cross-entity references and become
pointers into the Run's *own* snapshot list. The record is then fully self-contained.

### Consequence: Tries belong to their Scenario

The try table's partition key is `OrganizationId_ScenarioId` — a Try is physically stored inside its Scenario's
partition, and per-Scenario try history is the only way it is listed. So **Scenario → Try is an ownership edge** and a
Scenario purge must delete its Tries. Left alone they would sit in a partition nothing can reach. Runs are keyed by
Application and are unaffected: they die with the Application, as before.

### The cost: row size, and content-addressed snapshots

Today a Run row is a handful of Guids. After this change it carries an environment, N scenario snapshots, each
scenario's activities, **and** `ActivityResults`. At the proposed cap of 50 scenarios × ~30 activities × a few hundred
bytes of description, a Run approaches the 400 KB item limit **at creation time**, before a single result is written.

Two separate problems hide in there, and they need different fixes.

#### Problem 1 — snapshots are large and almost always identical

A nightly run of the same 50 scenarios against the same environment, for a year, stores **18,250 copies** of snapshots
that differ only on the days someone edited a test. The fix is content addressing: hash the snapshot, store the blob
once under that hash, and have the Run point at the hash.

| Row | Range key | Holds | Written |
| --- | --- | --- | --- |
| Run header | `<runId>` | status, counts, timestamps, environment content hash, and `(scenarioId, contentHash)` per scenario | once |
| Snapshot blob | `snapshot#<sha256>` | one frozen scenario or environment snapshot | conditionally — `attribute_not_exists`, so identical content is written once ever |
| Results | `<runId>#scenario#<scenarioId>` | that scenario's `ActivityResult`s | updated during execution |

**Blobs live in the same partition as the runs that use them** — `OrganizationId_ApplicationId` for the run table,
`OrganizationId_ScenarioId` for the try table. That detail is what makes this safe, and it is the whole reason to
prefer it over a shared blob table:

- **No reference counting and no garbage collector.** A blob is only reachable from runs in its own partition, and those
  runs only die when the Application is purged — which deletes the partition anyway. The blob is simply another item in
  the existing cascade. A global blob table would need refcounts or mark-and-sweep, which is the exact class of problem
  this whole document exists to remove.
- **A Query still fetches everything.** Header, blobs, and results all come back in one partition read.
- Dedup is per-Application, which is where the repetition actually is.

The environment snapshot goes through the same mechanism, so it is stored separately too — not as a special case, just
as another blob. Nightly runs against Staging share one environment blob.

**The hash must be over an explicit canonical form.** A hand-written, versioned projection with fixed field order and
fixed date/number formatting — never `JsonSerializer.Serialize(model)` with ambient options. Two failure modes if this
is sloppy: a serializer change silently resets dedup (harmless, just wasteful), or a field omitted from the canonical
form makes two genuinely different snapshots collide and a Run renders the wrong history (not harmless). Prefix the
hash with a schema version (`v1:<sha256>`) so the format can change without ambiguity.

**Bonus, and it is a real one:** the hash *is* a version identity. "Did this test change between Tuesday's run and
Friday's?" becomes a string comparison, and a run-to-run diff becomes a product feature rather than a research project.

#### Problem 2 — `ActivityResults` grow during execution

This is separate from snapshot size and is the more dangerous of the two, because it is a **write-cost** problem, not a
capacity one.

`Run.ActivityResults` is one list inside one item. A run over 50 scenarios × 30 activities holds 1,500 results, each
with a `ResolvedPreconditions` dict, an `Evidence` dict and `ContinuationReasoning`. Three things go wrong as execution
fills that list in:

1. **Every update rewrites the whole item.** DynamoDB bills writes per 1 KB of the *entire* item, not the delta. Marking
   one activity Passed on a 300 KB row costs ~300 WCU. Do that 1,500 times in a run and a single run costs ~450,000 WCU
   — before the item cap is anywhere near reached. This is the cost bomb, and splitting rows is what defuses it.
2. **Write contention.** One item means one writer. Any parallelism across scenarios turns into lost updates or
   throttling on a single key.
3. **The 400 KB cap**, eventually.

Splitting results to one row per (Run, Scenario) fixes all three: a write touches only that scenario's row, scenarios
execute in parallel without colliding, and each row is bounded by the scenario's `ActivityCount`.

**Evidence itself should not live in DynamoDB.** For an API test platform, evidence means response bodies, headers, and
assertion diffs — unbounded, occasionally megabytes, and read rarely. It belongs in S3 with the row holding a key and a
size. Out of scope now (nothing executes), but `ActivityResult.Evidence` MUST NOT be allowed to become the place large
payloads land, or the row split above buys nothing.

### The cost: secrets

`EnvironmentVariable.Value` is a plain string with no secret concept today. Snapshotting variable **values** into run
rows copies them into a table with different access patterns and a different retention policy, permanently — rotating a
credential would not scrub it from run history.

Recommendation: snapshot the environment's **name, classification, and variable keys** now, and add variable *values*
only once `EnvironmentVariable` has an explicit secret marker. Nothing executes yet, so nothing needs the values, and
this defers the decision without blocking anything.

### What this does not change

- **Application → Run stays an ownership edge**: purging an Application still hard-deletes its Runs (§5, §7).
- **§D / Task 11 still stands.** With snapshots, the per-Scenario `ConditionCheck` in `TrySaveAsync` is no longer about
  referential integrity — a snapshot is self-consistent whatever happens next. It is now about not starting work against
  an entity on its way out, and about guaranteeing the snapshot was taken from a row that was `Active` at write time.
  Same code, different justification. Do not delete it as newly redundant.
- **Actor ids are the one place "retain" could sneak back.** `TriggeredByUserId`, `CreatedByUserId`, `UpdatedByUserId`
  sit on nearly every entity and point at a User. The absolute rule above would require snapshotting a display name
  alongside the id. User and Organization deletion are out of scope for this change, so this is flagged, not solved —
  but whoever designs Organization deletion MUST resolve it rather than quietly reintroducing retain.

Future note: once Runs actually execute, the executor must re-check `LifecycleState` on the *source* rows before
starting work, and an in-flight Run against a `Deleting` Environment should abort. Out of scope now, but the state field
is what it will hang off.

## 9. "Deletion might take a long time" — how to run the purge

Options considered:

| # | Approach | Complexity | Correctness | Notes |
| --- | --- | --- | --- | --- |
| 1 | Do the whole cascade inside the HTTP request | Lowest | **Broken** | An Application with thousands of rows blows past any 30–60s proxy timeout, leaving a half-deleted tree. |
| 2 | `Task.Run` fire-and-forget in the API process | Low | Weak | Lost on deploy, crash, or scale-in. The entity is stuck in `Deleting` forever with nothing to retry it. |
| 3 | **Job row in DynamoDB + `BackgroundService` worker** | **Medium** | **Good** | Job is written in the same transaction as the mark, so it cannot be lost. Survives restarts; retries naturally. No new AWS service. Testable against DynamoDB Local. |
| 4 | SQS queue + consumer | Medium-high | Good | Standard, but adds a queue, IAM, visibility-timeout tuning, and *still* needs a table for idempotency/dedupe. |
| 5 | DynamoDB Streams → Lambda | High | Good | Best scaling story, zero polling — but a second deployment artifact, new IAM, and painful to test locally. |

**Recommendation: option 3.**

It is the only one that balances the two things you asked about: the job row makes the work durable (correctness), and
it costs one extra table plus one hosted service (complexity). Because the trigger is decoupled from the work, moving to
option 4 or 5 later is a change of *what wakes the worker*, not a rewrite of the cascade.

Worker mechanics:

- Poll the job table every ~5s for jobs whose lease has expired.
- **Claim** a job with a conditional update (`LeaseExpiresAt < :now` → set `LeaseExpiresAt = now + 5min`,
  `ActingInstance`), so two server instances can't work the same job. Even if they did, every step is idempotent.
- On success, delete the job row. On failure, log, increment `AttemptCount`, and let the lease lapse so it retries.
- After N attempts (say 10), stop retrying, log at `Critical`, and **leave the job row in place** for a human. Never
  silently drop it — the entity would be stranded in `Deleting`.
- Timestamps come from `IClock`, never `DateTimeOffset.UtcNow` (services rule in the coding standards).
- The worker is a singleton; repositories are scoped, so it must create a DI scope per job.
- The worker has no `HttpContext`, so `ICallerOrganizationService` is unusable there — every purge method takes
  `organizationId` as an explicit first parameter.

**Multi-tenancy note:** `CLAUDE.md` requires `OrganizationId` in every query. The worker's "find me a job to run" query
is the one deliberate exception — it is a system-level queue read across tenants. Every job row still carries
`OrganizationId`, and every query the purge itself issues is scoped by it. This exception must be spelled out in the
repository's XML doc so it doesn't look like an oversight.

**Hosting caveat:** a `BackgroundService` needs a long-lived process (ECS / App Runner / EC2). If the server ends up on
Lambda, replace the polling loop with an EventBridge-scheduled invocation of the same purge service — the cascade code
is unaffected.

## 10. API surface

| Endpoint | Change |
| --- | --- |
| `DELETE /applications/{id}` | **New.** Marks + enqueues. |
| `DELETE /environments/{id}` | **New.** Marks + enqueues. |
| `DELETE /scenarios/{id}` | Now marks + enqueues instead of deleting inline. |
| `DELETE /preconditions/{id}` | Now marks + enqueues. |
| `DELETE /evidence-definitions/{id}` | Now marks + enqueues. |
| `DELETE /activities/{id}` | Unchanged — already correct and atomic (row + `ActivityCount` in one transaction). |
| `DELETE /environments/{id}/variables/{key}` | Unchanged except the parent Environment must now be `Active`. |

Two contract decisions:

- **Keep `204 No Content`,** not `202 Accepted`. From the client's point of view the entity really is gone the moment
  the call returns — it vanishes from every list and every get. `202` would only leak an implementation detail.
- **Make deletes idempotent: always `204`,** including when the entity is already gone or already `Deleting`. Today
  `DELETE /scenarios/{id}` returns `404` in that case while `DELETE /preconditions/{id}` returns `204`. Standardising
  on `204` is a small behaviour change to the Scenario endpoint — needs an SDK regen and a quick check of the web app.

## 11. Storage / infra changes

1. **New table `${env}-autoassure-deletion-job-table`** (`autoassure-infra/dynamodb.tf`):
   - hash key `Id` (UUIDv7)
   - attributes: `OrganizationId`, `EntityType`, `EntityId`, `ApplicationId`, `RequestedByUserId`, `RequestedAt`,
     `LeaseExpiresAt`, `AttemptCount`, `LastError`
   - GSI `LeaseIndex`: hash `EntityType`, range `LeaseExpiresAt` — lets the worker query claimable jobs cheaply.
     (Job volume is tiny, so partition-key spread is not a concern; revisit if that stops being true.)
   - PITR when `var.environment == "prod"`, SSE enabled, `PAY_PER_REQUEST`, plus the `trivy:ignore:AWS-0025` comment,
     matching every existing table.
2. **New GSI `ApplicationIdIndex` on the Activity table**: hash `OrganizationId_ApplicationId`, projection `ALL`
   (§7). Requires the mapper to write that attribute onto Activity rows.
3. **Run and Try table layout changes** (§8, Task 10). Same partition keys and GSIs; three range-key shapes instead of
   one: `<runId>` (header), `snapshot#<v1:sha256>` (content-addressed snapshot blob, shared across runs in the
   partition), `<runId>#scenario#<scenarioId>` (that scenario's results). No new table — blobs deliberately live in the
   run/try partitions so the Application cascade already covers them. Update the "schema must stay in sync with
   Models/Run.cs" comments above both tables.
4. **No schema change to the other existing tables** — `LifecycleState` is just a new attribute.
5. `DynamoDbOptions.DeletionJobTableName` + entries in `appsettings.json` / `appsettings.Development.json`, and the
   table added to the test fixture's setup.

# Implementation Details

Ordered so the tree is always in a working state. Tasks 1–3 are pure groundwork and change no user-visible behaviour.

Task 0 comes first on purpose: the guideline is what the remaining tasks are checked against.

- **Task 0 — Write the entity lifecycle skill.** `.claude/skills/entity-lifecycle/SKILL.md`, loaded before any
  repository/service/controller code that lists, gets, creates, updates, deletes, or references an entity. It states the
  protocol as rules: first-class entities are marked `Deleting`, never hard-deleted in the request; every create/update
  condition-checks its immediate parent exists AND is `Active`, in the same transaction; every `List` returns only
  `Active` and every `Get` 404s `Deleting`, enforced at the API layer; the purge marks top-down and
  deletes bottom-up; no dangling references except documented historical records; deletes are idempotent `204`. Ends
  with two checklists — "adding a new entity" and "adding a reference from Y to X". **Status: written.**

- **Task 1 — Introduce the state.** Add `Models/LifecycleState.cs` and a `LifecycleState` property to `Application`,
  `Scenario`, `Environment`, `Precondition`, `EvidenceDefinition`. Add a marker interface
  `ILifecycleTracked { LifecycleState LifecycleState { get; } }` implemented by all five, so visibility rules live in one
  place instead of five. Map the field in each `DynamoDbMapper.*.cs` (missing attribute → `Active`). Set `Active` on
  create. No behaviour change yet.

- **Task 2 — Write-side gating.** Extend every existing parent `ConditionExpression` to also require the parent be
  `Active`: `DynamoDbScenarioRepository.ApplicationExistsCheck`, `DynamoDbActivityRepository`
  (`IncrementScenarioActivityCount`, `DecrementScenarioActivityCount`, `PreconditionAndEvidenceExistsCheck`,
  `UpdateActivity`, `TryReorderAsync`), `DynamoDbEnvironmentRepository.TrySaveAsync`,
  `DynamoDbEnvironmentVariableRepository.TrySaveAsync`, `DynamoDbPreconditionRepository`,
  `DynamoDbEvidenceDefinitionRepository`, `DynamoDbRunRepository.TrySaveAsync`. Also require the entity's *own* row to
  be `Active` on every `TryUpdateX`.

- **Task 3 — Read-side gating, at the API layer.** Add `Models/LifecycleStateRules.cs` with `OnlyActive<T>()` and
  `IsVisibleToApi()` over the `ILifecycleTracked` interface from Task 1. Every List endpoint returns only `Active`
  entities through that helper; every Get-by-id endpoint 404s on `Deleting` and still returns `Archived`.
  **Repositories are not touched** — they keep returning rows as stored (§3), which is what lets the purge reuse them.

  Every affected endpoint, so none is missed:

  | Endpoint | Change |
  | --- | --- |
  | `ListApplications`, `GetApplicationById` | filter / 404 |
  | `ListScenarios`, `GetScenarioById` | filter / 404 — including the folder and tag variants, which filter after the `BatchGetItem` in `ListByMappingAsync` |
  | `ListEnvironments`, `GetEnvironmentById` | filter / 404 |
  | `ListPreconditions` | filter (no Get-by-id endpoint exists) |
  | `ListEvidenceDefinitions` | filter (no Get-by-id endpoint exists) |
  | `ListRuns`, `GetRunById`, `GetTryById` | **no change** — Runs have no `LifecycleState` (§2) |
  | `ListActivities` | **different fix**: read the parent Scenario's state and 404 when it is not `Active`, since Activities carry no state of their own (§E) |

  The Activity *update* guard is not part of this task — it is a condition check inside the repository transaction,
  covered by Task 2 (§D).

- **Task 4 — Deletion job storage.** Terraform table (§11.1), `DynamoDbOptions.DeletionJobTableName`, appsettings,
  `Models/DeletionJob.cs`, `IDeletionJobRepository` + `DynamoDbDeletionJobRepository` with
  `TryClaimNextAsync(entityType, leaseExpiresAt)`, `ReleaseAsync`, `CompleteAsync`, `RecordFailureAsync`. Document the
  cross-tenant queue query as the deliberate multi-tenancy exception.

- **Task 5 — "Request deletion" services.** `Services/ApplicationDeletionService`, `ScenarioDeletionService`,
  `EnvironmentDeletionService`, `PreconditionDeletionService`, `EvidenceDefinitionDeletionService`, each with
  `RequestDeletionAsync(Guid organizationId, Guid id)` returning a small enum (`Requested` / `AlreadyGone`). Each calls
  a new repository method `TryMarkDeletingAsync(...)` that does the mark **and** the job insert in one
  `TransactWriteItems`.

- **Task 6 — Purge services.** One per entity, `PurgeAsync(Guid organizationId, Guid id)`, implementing the cascades in
  §5. Paging + `UnprocessedItems` retry + documented batch limits. Every step idempotent. New repository methods:
  `DeleteAllVariablesByEnvironmentAsync`, `DeleteAllRunsByApplicationAsync`, `DeleteScenarioMappingsAsync`, and an
  Activity-by-Application read over the new GSI. The existing `ListX` methods are reused as-is for the walk, since
  Task 3 leaves repositories unfiltered.

- **Task 7 — Reference detach.** Add the `ApplicationIdIndex` GSI (§11.2) — the reverse index that answers "who points
  at me?" — and write the attribute onto Activity rows in the mapper. `DetachLibraryReferenceAsync(organizationId,
  applicationId, referenceId)` walks the Application's Activities and removes the id from `PreconditionIds` /
  `EvidenceIds`. Skipped when the parent Application is itself being purged. Also reword the Activity update's 400 to
  name the offending ids (§7), so the transient window is actionable.

- **Task 8 — Background worker.** `Services/DeletionWorker : BackgroundService` — poll, lease, scope, dispatch to the
  right purge service, complete or fail with backoff, stop retrying and log `Critical` after 10 attempts. Registered in
  `Program.cs`. Poll interval and max attempts come from a bound options record, not constants.

- **Task 9 — Controllers.** Add `DELETE /applications/{id}` and `DELETE /environments/{id}`. Point the three existing
  cascade deletes at their new deletion services. Make all deletes idempotent `204`. Full XML docs stating the exact
  condition for each status code, per the coding standards.

- **Task 10 — Runs and Tries become content-addressed snapshots (§8).** Replace `Run.ScenarioIds` /
  `Run.EnvironmentId` with `RunScenarioSnapshot` / `RunEnvironmentSnapshot` records in `Models`, each keeping its source
  id documented as a non-resolving navigation hint. Add `Models/SnapshotHash.cs`: a hand-written, versioned canonical
  projection per snapshot type plus SHA-256 over it, producing `v1:<hex>` — never `JsonSerializer.Serialize` with
  ambient options, and unit-tested for stability against a checked-in golden string. `TrySaveAsync` reads the Scenarios
  and Environment, computes hashes, writes each blob with `attribute_not_exists(Id)` so duplicates are skipped, and
  writes a header row holding the hashes. Rework `DynamoDbRunRepository` for the three range-key shapes (§11.3) and
  update the Terraform comments. Snapshot environment name, classification, and variable *keys* only — not values —
  until `EnvironmentVariable` has a secret marker. Add the Try cascade to the Scenario purge (Task 6). Breaking contract
  change: `RunResponse` / `TryResponse` carry resolved snapshots instead of ids, so this needs an SDK regen and a web
  update (Task 14).

- **Task 11 — Close the write-verification gaps from §D, in the repository.** `DynamoDbRunRepository.TrySaveAsync` gains one
  `ConditionCheck` per distinct Scenario id (exists AND `Active`), keyed by the Run's own `OrganizationId` +
  `ApplicationId` so cross-Application and cross-tenant ids are rejected by key construction alone. Change its return
  type from `bool` to a typed result so the controller can tell "Scenario not found" (400) from "Application/Environment
  gone" (404). This one change fixes Runs and Tries together, since both use this method — delete the read-then-write
  Scenario lookup in `TriesController` rather than adding to it. Contracts get shape validation only: `ScenarioIds`
  non-empty, deduped, and capped at 50 (transaction limit is 97 here), with the cap documented.

- **Task 12 — Stuck-job safety net.** The worker logs a `Critical` metric-friendly message for any job older than N
  hours. No separate sweeper is needed: the job row *is* the record of unfinished work.

- **Task 13 — Tests** (ask before writing, per `CLAUDE.md`): repository integration tests against DynamoDB Local for
  the mark/claim/purge paths; a concurrency test that marks a Scenario `Deleting` and asserts a concurrent
  `CreateActivity` is rejected; controller tests for list/get filtering per state; a purge test driven by calling the
  purge service directly (never by waiting on the worker's timer).

- **Task 14 — SDK + web.** Run `../scripts/generate-sdk.sh`, then check `autoassure-web` for the two new endpoints, the
  `DELETE /scenarios/{id}` 404 → 204 change, and the reshaped Run/Try responses from Task 10 (the web app must render
  the embedded snapshot instead of looking scenarios up by id).

- **Task 15 (optional, can be dropped) — Archive.** `POST /applications/{id}/archive` + `/unarchive`, same for
  Scenario, **plus the `/archived` sibling list endpoint for each** (§3) — without it, archived entities cannot be found
  to un-archive, so shipping archive without it makes the feature one-way. The state and all the filtering already exist
  after Tasks 1–3; this is the endpoints plus a `TryUpdateLifecycleStateAsync`. Listed last because nothing else depends
  on it — say if you want it in this change or in a follow-up.

# Open decisions for you

1. **Idempotent `204` on all deletes** (§10) — changes `DELETE /scenarios/{id}` from `404` to `204` when already gone.
   Agreed?
2. **Archive in scope or follow-up?** (Task 15). The schema supports it either way.
3. **Detaching references needs a new GSI** on the Activity table (§7) — a reference edge has to be walked backwards,
   which ownership never requires. Small ongoing cost, and it is the only way to stop a library delete from bricking
   Activities. Agreed?
4. **Content-addressed snapshots** (§8). Recommendation is to hash each scenario/environment snapshot, store the blob
   once per partition, and have Runs point at the hash — worth it because nightly runs repeat the same snapshots
   thousands of times, and because blobs living in the run's own partition means no refcounting and no GC. Costs a
   canonical-serialization discipline that MUST be got right. Agreed, or store snapshots inline per (Run, Scenario) and
   accept the duplication until it hurts?
5. **Secrets in the environment snapshot** (§8). Recommendation is to snapshot name, classification, and variable
   **keys** only, deferring values until `EnvironmentVariable` has a secret marker. Agreed, or snapshot values now and
   treat run rows as secret-bearing?
6. **Tries die with their Scenario** (§8) — they live in the Scenario's partition, so a Scenario purge must take them.
   That means deleting a Scenario destroys its try history. Confirm that is the product behaviour you want; the
   alternative is re-keying the try table by Application, which is a bigger change.

# Completion Criteria

- All tasks are completed.
- `/code-review` command MUST be executed find REAL bugs.
- Verifications executed (test, lint, compilation)
- Server-side SDK re-generated.
- All REAL bugs are fixed, and `/code-review` command confirms there is no REAL bugs left.
- All code produced are checked and VERIFIED to comply with coding standards. Coding standard skills are LOADED before
  the check.
- `.claude/skills/entity-lifecycle/SKILL.md` exists, and every entity touched by this change complies with it —
  verified against its two checklists.
- The final code is reviewed by a **separate sub-agent** — a fresh context that did not write the code — whose only job
  is to confirm every repository, service, and controller write path complies with this deletion philosophy. It MUST
  load `entity-lifecycle` first and report per file: every edge classified as ownership or reference, every
  create/update carrying the parent/referent `Active` condition check in the same transaction, every first-class delete
  marking rather than hard-deleting, every purge ordered mark-down / delete-up, every reference edge detached, and every
  historical record snapshotting rather than referencing. Findings are fixed and the review re-run until it passes.
