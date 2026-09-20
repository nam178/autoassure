---
name: entity-lifecycle
description: Load when implementing deletion, listing, addition, or updates of entities. Entities that reference each other require careful planning to avoid leaving the database in an invalid state.
---

## Lifecycle States

Each business entity in AutoAssure has three possible `LifecycleState`s.

**Active**

- Default state for new entities.
- The entity is in use.

**Archived**

- User has explicitly archived it.
- Hidden from normal list APIs.
- Still accessible via "list archived" or "get" APIs.
- No new entities should reference archived entities.

**Deleting**

- User has explicitly deleted it.
- Deletion is processed in a background queue.
- Once complete, the entity is physically deleted.
- No new entities should reference deleting entities.

## Adding Rows

When adding a row to DynamoDB, use a conditional update to ensure referenced
entities are active.

- Example: When adding a scenario, add a condition to verify the application
  exists and is active.
- Method name: `TrySaveX()`. Return false if the condition fails.

## Updating Rows

When updating a row that changes a reference, verify the new reference exists
using a condition.

- Example: When moving a scenario from application A to B, add a condition to
  verify B exists.
- Method name: `TryUpdateX()`. Return false if the condition fails.

## Listing Rows

Repository methods must support an option to exclude archived or deleting rows.

## Deleting Rows

The deletion strategy depends on the entity's role:

**Leaf entity** (nothing references it)

- Delete immediately.

**Parent entity** (other entities reference it)

- Mark as "Deleting".
- Submit a worker queue request for background deletion.
- Delete children and grandchildren first.

**Friend/Sibling entity** (references exist in complex ways)

- Discuss the deletion strategy with Nam before implementing.
- Example: Runs reference deleted Scenarios/Environments. We handle this by
  snapshotting the Scenarios/Environments at Run creation time.

## Special Case: Organization

Organizations have a global filter that returns 403 when accessing data from an
archived org. No additional actions needed when adding records—no need to verify
the org is active.