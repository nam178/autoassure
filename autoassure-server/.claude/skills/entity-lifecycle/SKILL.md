---
name: entity-lifecycle
description:
  MUST load this BEFORE writing or editing any repository, service, or
  controller code that LISTS, GETS, CREATES, UPDATES, DELETES, or REFERENCES
  an entity that has a LifecycleState. Covers how to handle Active / Archived / Deleting
  states.
---

An entity does not simply exist or not exist. It moves through `LifecycleState` — `Active` → `Archived` → `Deleting` →
gone — and **every** endpoint that touches it MUST agree on what each state means.

# Classify first

Entities:

- **First-class** — owns children, or is referenced. Organization, Application, Scenario, Environment, Precondition,
  EvidenceDefinition. If you cannot classify a new entity, it is first-class (stricter case).
- **Leaf** — one parent, owns nothing, nothing references it. Activity, EnvironmentVariable.
- **Historical record** — an immutable record of something that already happened. Run, Try. It MUST NOT reference live
  entities at all; it **snapshots** them (Rule 5c). So it can never dangle, and a later edit to the source can never
  rewrite history.

Edges — a property of the **edge, not the entity**; one entity sits on both kinds at once (a Precondition is an owned
child of its Application *and* a reference target of Activities):

- **Ownership** — Y is meaningless without X (Activity → Scenario). Deletion travels **down**.
- **Reference** — Y points at X but stands alone (Activity → Precondition). Deletion travels **backwards**: X dies, Y
  survives, edited in place.

MUST classify every edge before writing a delete: wrong kind either destroys data that should have survived or leaves
data that should have gone.

# Rule 1 — Mark, never hard-delete in the request

- Every first-class entity MUST have `LifecycleState` (`Active` / `Archived` / `Deleting`). `Deleting` is irreversible,
  `Archived` is reversible.
- `DELETE` MUST NOT remove rows. In **one** `TransactWriteItems`: conditionally set `LifecycleState = Deleting` **and**
  insert a deletion job row — a mark never without a job, a job never without a mark.
- Row removal ("purge") happens in the background worker only.

# Rule 2 — Every create/update condition-checks its parent AND its referents, in the same transaction

This is what makes Rule 1 safe. Applies to **both edge kinds**: when Y belongs to or points at X, Y's write carries, in
the same transaction:

```
attribute_exists(Id) AND (attribute_not_exists(LifecycleState) OR LifecycleState = :active)
```

(`attribute_not_exists` so rows written before `LifecycleState` existed still count as `Active`.)

- **Immediate parent only**, never the ancestor chain — the purge marks each level as it descends, so each row carries
  its own truth and reads stay single-row.
- **Every** referent too, not just the owner: an Activity checks its Scenario *and* each Precondition /
  EvidenceDefinition it references.
- `TryUpdateX` MUST also require the entity's **own** row to be `Active`.
- MUST NOT use a separate `GetItem` first — read-then-write leaves a gap, a `ConditionCheck` does not.
- **Referential integrity is a repository concern.** Controllers validate input *shape* only (required, non-empty,
  deduped, capped) and MUST NOT check that a referenced entity exists — that answer is stale by write time. A controller
  wanting to check means a missing `ConditionCheck` is the real bug.
- **Let key construction scope it.** Build the check key from the writing entity's own `OrganizationId` and parent ids:
  a foreign-tenant or foreign-parent id has no row at that key, so the transaction cancels for free.
- A repository method with several different condition checks MUST return a **typed result**, not `bool` — see
  `ActivitySaveResult` — otherwise the caller cannot map failures to status codes.

Why it works: the check and the `Put` commit atomically. Either the create commits before the parent is marked (the
purge's consistent read then sees it) or after (the check rejects it). No third case.

# Rule 3 — Lifecycle visibility is enforced at the API layer

> **Every List API returns only `Active`. Every Get-by-id returns `Active` and `Archived`, and 404s `Deleting`.**

|            | `ListX` | `ListX/archived` | `GetX(id)`   | Editable | Can gain children |
|------------|---------|------------------|--------------|----------|-------------------|
| `Active`   | yes     | no               | yes          | yes      | yes               |
| `Archived` | no      | yes              | yes          | yes      | **no**            |
| `Deleting` | no      | no               | **no (404)** | no       | no                |

`Archived` still resolving on `GetX(id)` matters: an old link should still open, it just should not clutter a list.

- An archivable entity MUST expose a sibling endpoint returning **only** archived rows (`GET .../scenarios/archived` →
  `ListArchivedScenarios`), or archiving is a one-way trip. Sibling path, not `?state=`, so each endpoint gets a real
  operation name.
- **Repositories MUST NOT filter by lifecycle state** — they return rows as stored; filtering is a business rule and
  repositories are dumb (see coding-standards).
- Apply it through the **one shared helper** in `Models` (`OnlyActive<T>()` / `IsVisibleToApi()`), never hand-rolled —
  an endpoint that forgot is then visible by the helper's absence.
- **Why above the repository:** the purge must list the rows it just marked `Deleting`. A filtering repository would
  hand it an empty list and silently leak every child — no error, no log. Sharing one read method makes that bug
  unwritable.

## What Rule 3 cannot express

- **Leaves have no state.** An endpoint listing or fetching a leaf MUST read its **parent's** state and 404
  accordingly — not for orphans (the protocol prevents those) but because deletion is async: during the purge window
  leaf rows are still valid and only the parent says they are on their way out. **Reads only** — leaf writes are guarded
  by Rule 2.
- **Ancestors may be `Deleting` while a row is `Active`.** Rule 2 checks the immediate parent and the purge marks
  top-down, so an Application can be `Deleting` while its Scenarios still answer 200. Accepted deliberately — ancestor
  walks on every read are the cost that decision avoids, and the window shrinks as the purge descends. MUST NOT "fix" it
  with ancestor lookups on reads.
- **Counts and pagination happen before the filter.** A denormalized count MUST count the same set its list returns, or
  the UI shows "5 items" above a list of 3. A page of N can become fewer than N after filtering, so paginated endpoints
  MUST keep paging while `LastEvaluatedKey` is set and MUST NOT treat a short or empty page as end-of-results.

# Rule 4 — Purge marks top-down, deletes bottom-up

Per level: **mark the children `Deleting` → recurse → hard-delete the children's rows → hard-delete own row.**

- Children MUST be marked before their own children are touched: marking only the root is not enough, since a
  grandchild's write only checks its immediate parent, which is still `Active`.
- Leaves MUST NOT be marked — their parent is already `Deleting`, so by Rule 2 no new one can appear.
- Every purge step MUST be idempotent, so a crashed job re-runs from the start.

# Rule 5a — Ownership edges cascade

When X is purged, everything that *belongs to* X goes with it: child entities and denormalized secondary rows
(`scenarios-by-folder`, `scenarios-by-tag`, …). Children are found by querying **their own partition**, keyed by the
parent — no extra index. Ordering per Rule 4.

# Rule 5b — Reference edges detach, never cascade

When X is purged, a Y that merely *points at* X MUST NOT be deleted — that is data loss; an Activity does not stop being
valid because one Precondition it mentioned was removed. **Detach**: remove X's id from Y during X's purge; Y survives,
edited in place. There is no second option — a dangling reference is never acceptable (Rule 5c is how historical records
avoid needing one).

Two consequences ownership does not have:

- A reference edge is walked **backwards**, so it needs a **reverse index** ("who points at me?") — budget for a GSI.
- Detach only terminates because the referent is already marked `Deleting` (Rule 1), which stops new references
  appearing mid-pass. Never detach without marking first.

Skip the detach when X is purged inside *its own parent's* cascade and Y is going away in that same cascade.

**The transient window:** while detach runs, X exists as `Deleting`, so Rule 2 rejects an update to Y that still carries
X's id — Y is briefly uneditable. Accepted, but the error MUST name the offending ids so the UI can say what to remove.
MUST NOT "fix" it by validating only the ids the caller is *adding* against a value read earlier in the request: that
read-then-write gap lets the update re-add an id the detach pass already swept past, and the referent is hard-deleted
underneath it — the same dangling-reference bug through another door. Close the window with an optimistic-concurrency
version attribute on Y (`Version = :expected`) or not at all.

# Rule 5c — Historical records snapshot, they never reference

A record of something that happened (Run, Try, audit entry, invoice) MUST copy the data it describes, not point at it.
Two independent reasons, and the second bites long before any deletion:

- A reference can dangle. A copy cannot.
- A reference is re-read at render time, so **editing the source silently rewrites history** — a year-old Run would show
  today's test steps next to last year's result. The record would be lying.

- MUST define dedicated snapshot types (`RunScenarioSnapshot`), never embed the live domain model. The live model keeps
  growing fields (`LifecycleState`, audit stamps) that are meaningless frozen inside a historical row.
- MAY keep the source id inside the snapshot as a **navigation hint**. Its XML doc MUST say it may no longer resolve and
  MUST NOT be condition-checked or dereferenced for data — otherwise the reference edge is back through a side door.
- Snapshots grow rows. MUST check the copied payload against DynamoDB's **400 KB item limit** and split into child rows
  in the same partition when it can approach it.
- Snapshots copy data past its original access controls. MUST NOT copy secret values into a record with a different
  retention or read policy — rotating the secret would not scrub the copies.
- The snapshot is still taken from a live row, so the write that takes it MUST carry Rule 2's `Active` condition check:
  not for referential integrity, but so history never records work started against an entity on its way out.

**The one legal pointer: content-addressed blobs.** Storing the same snapshot on every record is wasteful when records
repeat (nightly runs of the same tests). A record MAY instead point at a blob keyed by a hash of its own content, if all
three hold — otherwise store the snapshot inline:

1. **Immutable by construction.** The key is a hash of the content, so the target can never change under the record.
   Different content is a different key, never an overwrite. This is why it does not violate the rule above: neither
   dangling nor silent rewriting is expressible.
2. **Lifetime at least as long as every record pointing at it.** Keep blobs in the *same partition* as those records so
   the existing cascade already covers them. MUST NOT put them in a shared global table — that needs reference counting
   or mark-and-sweep, which is the failure mode this whole skill exists to avoid.
3. **An explicit, versioned canonical form.** Hand-written projection, fixed field order and date/number formatting,
   version-prefixed key (`v1:<sha256>`), and a unit test pinning the hash of a known input. MUST NOT hash
   `JsonSerializer.Serialize(model)` with ambient options: a silent format change resets dedup, and a field left out of
   the canonical form makes two different snapshots collide and renders the wrong history.

# Rule 6 — Deletes are idempotent

`DELETE` returns `204 No Content` whether the entity was `Active`, already `Deleting`, or already gone — never `404`,
the caller's intent is satisfied. `204` not `202`: from the client's side it really is gone the moment the call returns,
because Rule 3 hides it everywhere.

# Rule 7 — Background purge code has no HTTP context

- Purge/deletion services MUST take `organizationId` as an explicit first parameter — `ICallerOrganizationService` reads
  `HttpContext` and is unusable in the worker.
- The worker is a singleton and repositories are scoped → it MUST create a DI scope per job.
- Timestamps and leases MUST come from `IClock`, never `DateTimeOffset.UtcNow` (see coding-standards).
- Every purge query MUST be scoped by `OrganizationId`. The one exception — the worker's "claim the next job"
  cross-tenant system read — MUST be spelled out in the repository's XML doc so it does not read as an oversight.

# DynamoDB limits

- `TransactWriteItems`: 100 items, 4 MB; rejects two operations on the same item.
- `BatchWriteItem`: 25 items, 16 MB; returns `UnprocessedItems` on throttling instead of throwing — MUST retry until
  empty.
- `Query`: 1 MB per page — every list-then-delete loop MUST page.
- MUST document these limits wherever they shape the code (e.g. why a list is chunked by 25).

# Checklists

New entity:

- [ ] Classified first-class / leaf / historical record. Historical record → snapshots its subject, per Rule 5c.
- [ ] First-class → has `LifecycleState` (mapped in `DynamoDbMapper.*`, defaulting to `Active` when absent), a
  `TryMarkDeletingAsync` (mark + job insert in one transaction), and a purge service.
- [ ] Every edge it sits on classified and handled per Rule 5a / 5b; its parent's cascade updated to include it.
- [ ] `ListX` only `Active`; `GetX` `Active` + `Archived`, 404 for `Deleting` — via the shared helper in the controller.
- [ ] Archivable → `/archived` sibling list endpoint exists.
- [ ] `DELETE` is idempotent `204`, XML docs state the exact condition for every status code.

New edge Y → X:

- [ ] Edge kind classified and written in the XML doc.
- [ ] Y's create carries a `ConditionCheck` that X exists and is `Active`, same transaction as the write; Y's update
  carries that plus a check that Y itself is `Active`.
- [ ] Ownership → X's purge cascades to Y. Reference → X's purge detaches the id and a reverse index exists to find the
  Y's. If Y is a historical record there MUST be no edge at all — it snapshots X instead (Rule 5c).
- [ ] If Y references many X's, the count is capped in the contract (100-item transaction limit) and the cap documented.
