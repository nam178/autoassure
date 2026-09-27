# Goal

Today a Scenario can never be deleted, and nothing stops you from adding an
Activity to a Scenario that should be closed for changes. This doc adds three
things:

1. A Scenario gets a state: **Active** or **Archived**. Archiving hides it
   from the normal list and blocks new Activities, without deleting anything.
2. Adding an Activity to a non-Active Scenario is rejected instead of quietly
   allowed.
3. Deleting a Scenario removes the Scenario and every one of its Activities in
   one single database transaction. No "Deleting" in-between state, no
   background queue, no worker.

Point 3 only works because a Scenario can never have more than 90 Activities
(see `Quota.MaxActivityCountPerScenario`). A DynamoDB transaction can touch at
most 100 rows. Scenario + up to 90 Activities + its folder/tag mapping rows
comfortably fits under that ceiling, so we don't need the general
mark-then-queue-then-clean-up pattern the `entity-lifecycle` skill describes
for parent entities. **This is a one-off shortcut for Scenario specifically.**
Do not copy it for an entity whose child count isn't capped (e.g.
Application) — task 6 below updates the skill doc to say so explicitly.

Out of scope for this pass: Archiving does not block edits to a Scenario's
Title/Description/Tags (only new Activities). If Nam wants that too, it's a
follow-up.

# How the pieces fit together (read this before coding)

- `Models/LifecycleState.cs` already has the enum: `Active`, `Archived`,
  `Deleting`. Scenario will only ever use `Active`/`Archived` — never
  `Deleting`.
- Organization already does the Active/Archived pattern. Copy its shape:
  - `Models/Organization.cs` has a `LifecycleState` field.
  - `DynamoDbOrganizationRepository.TrySetLifecycleStateAsync` flips it with a
    conditional `UpdateItem`.
  - `OrganizationsController` has `Archive`/`Unarchive` endpoints, and splits
    `List` (Active only) from `ListArchived` (Archived only).
- Scenario's primary key is `OrganizationId_ApplicationId` (partition) + `Id`
  (range) — not just `Id` like Organization. Every method below needs
  `applicationId` as well as `scenarioId`.

# Task 1 — Add LifecycleState to Scenario

Files:

- `src/A2.Server/Models/Scenario.cs` — add
  `public LifecycleState LifecycleState { get; init; } = LifecycleState.Active;`
- `src/A2.Server/Repositories/DynamoDbMapper.Scenario.cs`:
  - In `ToDynamoDbRow`, write `["LifecycleState"] = new(scenario.LifecycleState.ToString())`.
  - In `ToScenario`, read it back the same way `DynamoDbMapper.Organization`
    does: if the attribute is missing (old rows written before this field
    existed), default to `LifecycleState.Active`.
- `src/A2.Server/Repositories/IScenarioRepository.cs` — add:
  ```
  Task<bool> TrySetLifecycleStateAsync(
      Guid organizationId, Guid applicationId, Guid scenarioId, LifecycleState newState);
  ```
- `src/A2.Server/Repositories/DynamoDbScenarioRepository.cs` — implement it:
  same shape as `DynamoDbOrganizationRepository.TrySetLifecycleStateAsync`
  (conditional `UpdateItem`, `SET LifecycleState = :newState`,
  `ConditionExpression = "attribute_exists(Id)"`), but keyed on
  `OrganizationId_ApplicationId` + `Id` instead of just `Id`.

# Task 2 — Block adding an Activity to a non-Active Scenario

Files:

- `src/A2.Server/Repositories/DynamoDbActivityRepository.cs`,
  `IncrementScenarioActivityCount` (around line 384): add
  `LifecycleState = :active` to the existing `ConditionExpression`, so it
  reads:
  ```
  attribute_exists(Id) AND LifecycleState = :active AND
  (attribute_not_exists(ActivityCount) OR ActivityCount < :max)
  ```
  Add `:active` to `ExpressionAttributeValues`
  (`LifecycleState.Active.ToString()`). Note: Scenarios written before Task 1
  have no `LifecycleState` attribute at all, so `LifecycleState = :active`
  would wrongly fail for them — add
  `(attribute_not_exists(LifecycleState) OR LifecycleState = :active)` instead,
  mirroring how the existing `ActivityCount` clause handles missing
  attributes.
- Same file, `TrySaveAsync`'s catch block (around line 71–81): today, when the
  Scenario Update fails, it calls `ScenarioExistsAsync` to tell
  `ScenarioNotFound` apart from `ScenarioActivityLimitReached`. It now needs a
  third outcome. Replace `ScenarioExistsAsync` with something that fetches the
  Scenario itself (or at least its `LifecycleState`), and branch:
  - no such Scenario → `ScenarioNotFound`
  - Scenario exists but not Active → `ScenarioNotActive`
  - otherwise → `ScenarioActivityLimitReached`
- `src/A2.Server/Repositories/ActivitySaveResult.cs` — add `ScenarioNotActive`
  to the enum.
- `src/A2.Server/Controllers/ActivitiesController.cs`, `Create` (around line
  70): add a case
  `ActivitySaveResult.ScenarioNotActive => Conflict(new ErrorResponse("Scenario is not active."))`.
  Use `Conflict` (HTTP 409), not `NotFound`: the Scenario exists, it's just
  closed for new Activities, and 404 would wrongly suggest it doesn't exist.
  Add the matching `[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]`
  and an `<response code="409">` doc comment.

# Task 3 — Archive / Unarchive endpoints, and split Scenario listing

Files: `src/A2.Server/Controllers/ScenariosController.cs`

- Add `POST applications/{applicationId}/scenarios/{scenarioId}/archive` and
  `.../unarchive`, copying `OrganizationsController.Archive`/`Unarchive`:
  call `scenarioRepository.TrySetLifecycleStateAsync(...)`, return
  `NoContent()` on success, `NotFound()` if it returns false. Idempotent:
  archiving an already-Archived Scenario just succeeds again. Unlike
  Organization, there's no "Owner role" check to copy — Scenario mutations in
  this controller don't check caller role today, so don't add one here either.
- `List` (currently returns every Scenario regardless of state): filter to
  `LifecycleState.Active` only, in all three branches (all/by folder/by tag),
  the same way `OrganizationsController.List` filters after fetching.
- Add `GET applications/{applicationId}/scenarios/archived` (`ListArchived`),
  filtering to `LifecycleState.Archived` only.

# Task 4 — Delete Scenario (single transaction)

New file: `src/A2.Server/Repositories/ScenarioDeleteResult.cs`
```
public enum ScenarioDeleteResult
{
    Success,
    ScenarioNotFound,
    ScenarioModifiedConcurrently, // see below — caller should just retry
}
```

`src/A2.Server/Repositories/IScenarioRepository.cs` — add:
```
Task<ScenarioDeleteResult> TryDeleteAsync(Guid organizationId, Guid applicationId, Guid scenarioId);
```

`src/A2.Server/Repositories/DynamoDbScenarioRepository.cs` — implement it.
`DynamoDbScenarioRepository` will need a constructor dependency on
`IActivityRepository` to list the Scenario's Activities (it doesn't have one
today — add it). Steps:

1. `GetByIdAsync` the Scenario with a consistent read. If null, return
   `ScenarioNotFound`. Keep its `Folder`, `Tags`, and `ActivityCount` — you'll
   need all three below.
2. `activityRepository.ListByScenarioAsync(organizationId, scenarioId)` to get
   every Activity under it (at most 90, so this is cheap and safe to load in
   full).
3. Build one `TransactWriteItemsRequest` containing:
   - **Delete** the Scenario item. Condition:
     `attribute_exists(Id) AND ActivityCount = :expectedCount`, where
     `:expectedCount` is the `ActivityCount` you read in step 1. This closes a
     race: if someone adds or removes an Activity between step 1/2 and this
     transaction committing, the count won't match, the whole transaction is
     cancelled, and you don't end up deleting the Scenario while leaving a
     stray Activity behind (or vice versa).
   - **Delete** the Folder mapping row for the Scenario's `Folder` (same key
     shape as `DeleteFolderMapping` in this file).
   - **Delete** the Tag mapping row for each tag in `Tags` (same as
     `DeleteTagMapping`).
   - **Delete** each Activity item found in step 2 (same key shape
     `DynamoDbActivityRepository.TryDeleteAsync` uses for its own Activity
     delete — `OrganizationId_ScenarioId` + `Id` — but here you don't touch
     `ActivityCount` afterwards, since the Scenario row itself is being
     removed in the same transaction).
4. Run it. On success, return `Success`.
5. On `TransactionCanceledException` where the Scenario Delete's
   `ConditionalCheckFailed` fired, return `ScenarioModifiedConcurrently`.
   Any other cancellation reason should `throw` (unexpected failure, not a
   business outcome) — follow the same pattern as
   `DynamoDbActivityRepository.TrySaveAsync`'s catch block.

**Known limitation, not fixed in this pass:** Tags aren't capped anywhere
today. Item count in the transaction is `2 + Tags.Count + Activities.Count`
(Scenario delete + folder mapping + one row per tag + one row per Activity).
With ≤90 Activities that leaves roughly 8 tags of headroom before hitting
DynamoDB's hard 100-item transaction limit — at which point the delete call
fails outright with a DynamoDB-level error, not a clean result. Flag this to
Nam as a possible follow-up (e.g. cap Tags per Scenario) rather than solving
it here.

`src/A2.Server/Controllers/ScenariosController.cs` — add
`DELETE applications/{applicationId}/scenarios/{scenarioId}`:
- `Success` → `NoContent()` (204)
- `ScenarioNotFound` → `NotFound()` (404)
- `ScenarioModifiedConcurrently` → `Conflict(...)` (409), telling the caller
  to retry

# Task 5 — Tests

Follow the `testing-guideline` skill. At minimum:

- `tests/A2.Server.Tests/Repositories/DynamoDbScenarioRepositoryTests.cs`:
  `TrySetLifecycleStateAsync` (success, not-found), `TryDeleteAsync` (success
  — Scenario, its Activities, and its folder/tag mappings are all gone
  afterwards; not-found; concurrent-modification).
- `tests/A2.Server.Tests/Repositories/DynamoDbActivityRepositoryTests.cs`: add
  a case creating an Activity under an Archived Scenario → expect
  `ScenarioNotActive`.
- `tests/A2.Server.Tests/Controllers/ScenariosControllerTests.cs`: Archive,
  Unarchive, Delete (all three outcomes), `List` excludes Archived,
  `ListArchived` returns only Archived.
- `tests/A2.Server.Tests/Controllers/ActivitiesControllerTests.cs`: add the
  409 case.

# Task 6 — Update the entity-lifecycle skill

File: `.claude/skills/entity-lifecycle/SKILL.md`

Add a "Special Case: Scenario" section (same spot as the existing "Special
Case: Organization" one), saying: Scenario's physical delete is a single
atomic transaction instead of the general mark-Deleting-and-queue pattern,
because its child count (Activities) is hard-capped at 90, which always fits
under DynamoDB's 100-item transaction limit. This is specific to Scenario —
don't reuse it for a parent entity whose child count isn't bounded (e.g.
Application, which can own far more rows than that, per this repo's other
design doc).

# After this is done

The API surface changed (new endpoints, new response fields). Per this
repo's `CLAUDE.md`, ask Nam if he wants `../scripts/generate-sdk.sh` run
before wrapping up.
