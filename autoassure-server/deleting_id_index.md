# Deleting IdIndex

## Goal

Remove the `IdIndex` from five tables: environments, preconditions,
evidence_definitions, scenarios, activities.

The index is the reason a new item can 404 for a moment after it is created.
DynamoDB indexes cannot do strongly consistent reads, so the index is always a
little behind.

We fix the cause. Every route will carry its full parent chain. The server then
reads the row by its real key, and that read is strongly consistent.

Breaking changes are fine. AutoAssure is not released yet.

## New routes

| Entity                | Old                                             | New                                                                            |
|-----------------------|-------------------------------------------------|--------------------------------------------------------------------------------|
| Environment           | `/environments/{environmentId}`                 | `/applications/{applicationId}/environments/{environmentId}`                   |
| Environment variable  | `/environments/{environmentId}/variables/{key}` | `/applications/{applicationId}/environments/{environmentId}/variables/{key}`   |
| Precondition          | `/preconditions/{preconditionId}`               | `/applications/{applicationId}/preconditions/{preconditionId}`                 |
| Evidence definition   | `/evidence-definitions/{evidenceDefinitionId}`  | `/applications/{applicationId}/evidence-definitions/{evidenceDefinitionId}`    |
| Scenario              | `/scenarios/{scenarioId}`                       | `/applications/{applicationId}/scenarios/{scenarioId}`                         |
| Activity list, create | `/scenarios/{scenarioId}/activities`            | `/applications/{applicationId}/scenarios/{scenarioId}/activities`              |
| Activity              | `/activities/{activityId}`                      | `/applications/{applicationId}/scenarios/{scenarioId}/activities/{activityId}` |
| Activity reorder      | `/scenarios/{scenarioId}/activities/order`      | `/applications/{applicationId}/scenarios/{scenarioId}/activities/order`        |

Create and list routes for environments, preconditions, evidence definitions and
scenarios already sit under the application. They do not change.

## Every use of the index today

| Id | Where                                       | What it does                                                |
|----|---------------------------------------------|-------------------------------------------------------------|
| E1 | `GET /environments/{id}`                    | Get one                                                     |
| E2 | `PATCH /environments/{id}`                  | Read, then update                                           |
| E3 | `PUT /environments/{id}/variables/{key}`    | Find the environment, then set a variable                   |
| E4 | `DELETE /environments/{id}/variables/{key}` | Find the environment, then delete a variable                |
| E5 | `POST /applications/{a}/runs`               | Check the body's `EnvironmentId` belongs to the application |
| P1 | `PATCH /preconditions/{id}`                 | Read, then update                                           |
| D1 | `PATCH /evidence-definitions/{id}`          | Read, then update                                           |
| S1 | `GET /scenarios/{id}`                       | Get one                                                     |
| S2 | `PATCH /scenarios/{id}`                     | Read the previous version, then update                      |
| S3 | `POST /scenarios/{id}/activities`           | Check the scenario exists                                   |
| S4 | `PATCH /scenarios/{id}/activities/order`    | Check the scenario exists                                   |
| A1 | `PATCH /activities/{id}`                    | Read, then update                                           |
| A2 | `DELETE /activities/{id}`                   | Read, then delete                                           |

All 13 are removed by this plan. Nothing else reads the index.

## Key rule for every repository change

`GetByIdAsync` gains the parent IDs it needs (`applicationId`, plus `scenarioId`
for activities). It becomes a direct `GetItem` with `ConsistentRead = true`,
using the table's real key. If a row exists but belongs to another parent, the
result is "not found".

Check each table's key layout before writing the read. If a table's key does not
already lead with the parent, stop and raise it. We do not want to trade the
index for a scan.

## Tasks

Work in this order. Each task should build and pass its tests before the next
one starts.

| Order | Task name                          | Description                                                                                                                                                                                                                                                                                                                                     | Files to modify                                                                                                                                         |
|-------|------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------|
| 1     | Environments: direct read          | Change `GetByIdAsync` to take `applicationId`. Read by the real key with a strongly consistent read. Remove the `IdIndexName` constant and the index query. Covers E1–E4.                                                                                                                                                                       | `Repositories/IEnvironmentRepository.cs`, `Repositories/DynamoDbEnvironmentRepository.cs`                                                               |
| 2     | Environments: new routes           | Move the four routes under `/applications/{applicationId}/environments/...`. Pass `applicationId` to the repository. Return 404 when the environment is in another application.                                                                                                                                                                 | `Controllers/EnvironmentsController.cs`                                                                                                                 |
| 3     | Runs: fix environment check        | E5 already knows the application. Pass `applicationId` into `GetByIdAsync`. The `environment.ApplicationId != applicationId` check becomes unnecessary and can go. This also removes the false 400 right after creating an environment.                                                                                                         | `Controllers/RunsController.cs`                                                                                                                         |
| 4     | Preconditions                      | Repository read by real key, then move `PATCH` under `/applications/{applicationId}/preconditions/{preconditionId}`. Covers P1.                                                                                                                                                                                                                 | `Repositories/IPreconditionRepository.cs`, `Repositories/DynamoDbPreconditionRepository.cs`, `Controllers/PreconditionsController.cs`                   |
| 5     | Evidence definitions               | Same as preconditions. Covers D1.                                                                                                                                                                                                                                                                                                               | `Repositories/IEvidenceDefinitionRepository.cs`, `Repositories/DynamoDbEvidenceDefinitionRepository.cs`, `Controllers/EvidenceDefinitionsController.cs` |
| 6     | Scenarios: direct read and routes  | `GetByIdAsync` takes `applicationId`. Move `GET` and `PATCH` under `/applications/{applicationId}/scenarios/{scenarioId}`. Covers S1, S2. Keep `GetByIdsAsync` as is. It already reads by application key.                                                                                                                                      | `Repositories/IScenarioRepository.cs`, `Repositories/DynamoDbScenarioRepository.cs`, `Controllers/ScenariosController.cs`                               |
| 7     | Activities: direct read and routes | `GetByIdAsync` takes `applicationId` and `scenarioId`. Move create, list, update, delete and reorder under `/applications/{applicationId}/scenarios/{scenarioId}/activities/...`. Delete no longer reads the row first just to learn its parents. Update the scenario existence checks to pass `applicationId` (S3, S4). Covers S3, S4, A1, A2. | `Repositories/IActivityRepository.cs`, `Repositories/DynamoDbActivityRepository.cs`, `Controllers/ActivitiesController.cs`                              |
| 8     | Clean up API doc comments          | Remove the `DeleteActivity` note about the index and "a retry fixes it". Remove other wording that blames eventual consistency.                                                                                                                                                                                                                 | `Controllers/ActivitiesController.cs` and any other controller comment found by searching for `IdIndex` and "eventual"                                  |
| 9     | Remove the indexes from Terraform  | Delete the five `IdIndex` blocks and their comments.                                                                                                                                                                                                                                                                                            | `autoassure-infra/dynamodb.tf` (lines about 224, 310, 361, 411, 538)                                                                                    |
| 10    | Local test database                | Check how the integration tests create their tables. If they define `IdIndex` themselves, remove it there too.                                                                                                                                                                                                                                  | `tests/A2.Server.Tests/DynamoDbLocalFixture.cs` (check first)                                                                                           |
| 11    | Update repository tests            | Change calls to the new `GetByIdAsync` signatures. Add a test per entity: a row in another parent returns "not found". Remove tests and helpers that wait for the index.                                                                                                                                                                        | `tests/A2.Server.Tests/Repositories/DynamoDb{Environment,Precondition,EvidenceDefinition,Scenario,Activity}RepositoryTests.cs`                          |
| 12    | Update controller tests            | Use the new routes. Delete the code that polls `IdIndex` to wait for consistency. Add a test that a create followed at once by a get, update or delete works.                                                                                                                                                                                   | `tests/A2.Server.Tests/Controllers/{Environments,Preconditions,EvidenceDefinitions,Scenarios,Activities,Runs,RunStatusUpdates}ControllerTests.cs`       |
| 13    | Update design docs                 | Remove the "Watch out" notes about the eventually consistent index. Update the routes in any doc that lists them.                                                                                                                                                                                                                               | `entity_life_cycle_leaf_goal.md`, `entity_life_cycle_leaf_tasks.md`, `../docs/about.md`, `../docs/create_test_flow.md` (only if they list these routes) |
| 14    | Run the checks                     | Run, in order: `dotnet build A2.Server.slnx`, `dotnet format A2.Server.slnx`, `dotnet jb inspectcode ...`, `A2.Server.UnitTests`, `A2.Server.Tests`. Fix everything they report.                                                                                                                                                                | none (fixes only)                                                                                                                                       |
| 15    | Regenerate the SDK (ask Nam first) | The API changed, so run `../scripts/generate-sdk.sh` once Nam approves. The old index comment in the SDK goes away with it.                                                                                                                                                                                                                     | `../autoassure-server-sdk/openapi.json`, `../autoassure-server-sdk/src/Api.ts` (generated)                                                              |
| 16    | Update the web app                 | Fix callers of the changed SDK methods. They now need `applicationId` (and `scenarioId` for activities).                                                                                                                                                                                                                                        | `../autoassure-web/src/**` (find by SDK method name)                                                                                                    |

## Out of scope

- Runs stay fully nested. They already work this way.
- The other indexes stay: `GoogleUserIdIndex`, `UserIdIndex` and
  `RunHeaderIndex`. They answer different questions, not "find by ID".
- The organization route fixes (`{id}` to `{organizationId}`, and
  `/organizations/archived` to a query filter) are a separate change.
- There is no scenario delete yet, so nothing to do there.

## Risks

- **Table keys.** Task 1 through 7 assume each table's key leads with the
  parent. Confirm this per table first.
- **Live infra.** Task 9 drops real indexes. This is safe only because nothing
  is released. Apply it after the code change is deployed, since the old code
  reads the index.
- **Web app.** Every changed route breaks its caller until task 16 is done.
