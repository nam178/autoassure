# Goal: Delete the IdIndex

The reasoning lives in `deleting_id_index.md` (same folder). This file holds
only the work. Do not edit the design document.

## The problems

1. **New items 404 for a moment after they are created.** Five tables
   (environments, preconditions, evidence_definitions, scenarios, activities)
   are read through a DynamoDB index called `IdIndex`. Indexes cannot do
   strongly consistent reads, so they lag behind the table. A user creates a
   scenario, opens it a heartbeat later, and sees "not found". This looks like a
   bug to every customer.

2. **The run check gives a false 400.** Starting a run checks the environment
   through the same index. Right after an environment is created, the check
   fails. The user did nothing wrong.

3. **Tests and docs work around the lag.** Tests poll the index to wait for it.
   API comments tell users "a retry fixes it". This code is noise, and it hides
   the real cause.

4. **Routes hide the parent.** Routes like `/scenarios/{id}` do not say which
   application owns the item. The server must search by ID alone, which is why
   the index exists.

## The solutions

1. **Carry the full parent chain in every route** (fixes 4). Example:
   `/applications/{applicationId}/scenarios/{scenarioId}/activities/{activityId}`.
2. **Read by the real key with a strongly consistent read** (fixes 1).
   `GetByIdAsync` takes the parent IDs and does a direct `GetItem` with
   `ConsistentRead = true`. A row under another parent counts as "not found".
3. **Pass the known application into the run check** (fixes 2). The run route
   already knows the application.
4. **Delete the five indexes, the workaround code and the "retry" wording**
   (fixes 3). Update docs, the SDK and the web app to match.

## Task breakdown

There are 16 tasks. They run in order. Each one leaves the build green and the
tests passing before the next starts.

Test changes for tasks 1–7 are done in tasks 11–12. To keep the build green,
each of tasks 1–7 must also make the existing tests compile and pass for the
code it touches. Tasks 11–12 then finish the rest (new cases, removing the index
polling).

| Box            | Command (run in `autoassure-server`)                                                                     |
|----------------|----------------------------------------------------------------------------------------------------------|
| Code Formatted | `dotnet format A2.Server.slnx`                                                                           |
| Linting pass.  | `dotnet jb inspectcode A2.Server.slnx -o=inspect.sarif.json --no-build --severity=WARNING` (no findings) |
| Build pass.    | `dotnet build A2.Server.slnx`                                                                            |
| Tests pass     | `dotnet test tests/A2.Server.UnitTests` and `dotnet test tests/A2.Server.Tests`                          |

Tasks 9 and 13 touch no C#. For them, mark format, lint, build and test boxes as
passed only after running the commands and seeing them stay green. Task 16 uses
the web commands listed in its Verify part.

### Task 1 — Environments: direct read

**Do.** First check the environments table key. It must lead with the
application. If it does not, stop and raise it. Then change `GetByIdAsync` to
take `applicationId`. Read by the real key with `ConsistentRead = true`. Return
"not found" if the row belongs to another application. Remove the `IdIndexName`
constant and the index query. This covers uses E1–E4.

**Files.** `Repositories/IEnvironmentRepository.cs`,
`Repositories/DynamoDbEnvironmentRepository.cs`. Fix callers so the build
compiles.

**Tests.** Existing environment repository tests still pass with the new
signature.

**Verify.**
`grep -n "IdIndex" src/A2.Server/Repositories/DynamoDbEnvironmentRepository.cs`
returns nothing.

- [x] Code Formatted
- [x] Linting pass.
- [x] Build pass.
- [x] Tests pass
- [x] Output code reviewed with a seperate agent in a fresh new context for
  critical bugs
- [x] All critical bugs fixed
- [x] Code reviewed with coding standard agent in a brand new context
- [x] Coding standard violations fixed

### Task 2 — Environments: new routes

**Do.** Move the four environment routes (get, patch, set variable, delete
variable) under `/applications/{applicationId}/environments/...`. Pass
`applicationId` to the repository. Return 404 when the environment is in another
application.

**Files.** `Controllers/EnvironmentsController.cs`. Update environment
controller tests to the new routes so they pass.

**Tests.** Get, patch, set variable and delete variable work on the new routes.
Get with the wrong `applicationId` returns 404.

- [x] Code Formatted
- [x] Linting pass.
- [x] Build pass.
- [x] Tests pass
- [x] Output code reviewed with a seperate agent in a fresh new context for
  critical bugs
- [x] All critical bugs fixed
- [x] Code reviewed with coding standard agent in a brand new context
- [x] Coding standard violations fixed

### Task 3 — Runs: fix environment check

**Do.** The create-run endpoint already knows the application. Pass
`applicationId` into `GetByIdAsync`. Remove the
`environment.ApplicationId != applicationId` check, since it is no longer
needed. This covers E5 and removes the false 400.

**Files.** `Controllers/RunsController.cs`.

**Tests.** Create an environment, then start a run at once: it succeeds. An
environment from another application still gives an error.

- [x] Code Formatted
- [x] Linting pass.
- [x] Build pass.
- [x] Tests pass
- [x] Output code reviewed with a seperate agent in a fresh new context for
  critical bugs
- [x] All critical bugs fixed
- [x] Code reviewed with coding standard agent in a brand new context
- [x] Coding standard violations fixed

### Task 4 — Preconditions

**Do.** Check the table key leads with the application. Change the repository
read to use the real key with a strongly consistent read, taking
`applicationId`. Move `PATCH` to
`/applications/{applicationId}/preconditions/{preconditionId}`. This covers P1.

**Files.** `Repositories/IPreconditionRepository.cs`,
`Repositories/DynamoDbPreconditionRepository.cs`,
`Controllers/PreconditionsController.cs`. Update the existing tests so they
compile and pass.

**Tests.** Patch works on the new route. Patch with the wrong application
returns 404.

**Verify.**
`grep -n "IdIndex" src/A2.Server/Repositories/DynamoDbPreconditionRepository.cs`
returns nothing.

- [x] Code Formatted
- [x] Linting pass.
- [x] Build pass.
- [x] Tests pass
- [x] Output code reviewed with a seperate agent in a fresh new context for
  critical bugs
- [x] All critical bugs fixed
- [x] Code reviewed with coding standard agent in a brand new context
- [x] Coding standard violations fixed

### Task 5 — Evidence definitions

**Do.** Same as task 4, for evidence definitions. Route:
`/applications/{applicationId}/evidence-definitions/{evidenceDefinitionId}`.
This covers D1.

**Files.** `Repositories/IEvidenceDefinitionRepository.cs`,
`Repositories/DynamoDbEvidenceDefinitionRepository.cs`,
`Controllers/EvidenceDefinitionsController.cs`. Update existing tests.

**Tests.** Patch works on the new route. Patch with the wrong application
returns 404.

**Verify.**
`grep -n "IdIndex" src/A2.Server/Repositories/DynamoDbEvidenceDefinitionRepository.cs`
returns nothing.

- [x] Code Formatted
- [x] Linting pass.
- [x] Build pass.
- [x] Tests pass
- [x] Output code reviewed with a seperate agent in a fresh new context for
  critical bugs
- [x] All critical bugs fixed
- [x] Code reviewed with coding standard agent in a brand new context
- [x] Coding standard violations fixed

### Task 6 — Scenarios: direct read and routes

**Do.** Check the table key leads with the application. `GetByIdAsync` takes
`applicationId` and reads by the real key with a strongly consistent read. Move
`GET` and `PATCH` under `/applications/{applicationId}/scenarios/{scenarioId}`.
This covers S1 and S2. Leave `GetByIdsAsync` alone. It already reads by
application key.

**Files.** `Repositories/IScenarioRepository.cs`,
`Repositories/DynamoDbScenarioRepository.cs`,
`Controllers/ScenariosController.cs`. Update existing tests.

**Tests.** Get and patch work on the new routes. Both return 404 for the wrong
application.

**Verify.**
`grep -n "IdIndex" src/A2.Server/Repositories/DynamoDbScenarioRepository.cs`
returns nothing.

- [x] Code Formatted
- [x] Linting pass.
- [x] Build pass.
- [x] Tests pass
- [x] Output code reviewed with a seperate agent in a fresh new context for
  critical bugs
- [x] All critical bugs fixed
- [x] Code reviewed with coding standard agent in a brand new context
- [x] Coding standard violations fixed

### Task 7 — Activities: direct read and routes

**Do.** Check the activities table key leads with the parent chain.
`GetByIdAsync` takes `applicationId` and `scenarioId`. Move create, list,
update, delete and reorder under
`/applications/{applicationId}/scenarios/{scenarioId}/activities/...`. Delete
must not read the row first just to learn its parents. The scenario existence
checks (S3, S4) now pass `applicationId`. This covers S3, S4, A1 and A2.

**Files.** `Repositories/IActivityRepository.cs`,
`Repositories/DynamoDbActivityRepository.cs`,
`Controllers/ActivitiesController.cs`. Update existing tests.

**Tests.** Create, list, update, delete and reorder work on the new routes.
Wrong scenario or wrong application returns 404.

**Verify.**
`grep -n "IdIndex" src/A2.Server/Repositories/DynamoDbActivityRepository.cs`
returns nothing.

- [x] Code Formatted
- [x] Linting pass.
- [x] Build pass.
- [x] Tests pass
- [x] Output code reviewed with a seperate agent in a fresh new context for
  critical bugs
- [x] All critical bugs fixed
- [x] Code reviewed with coding standard agent in a brand new context
- [x] Coding standard violations fixed

### Task 8 — Clean up API doc comments

**Do.** Remove the `DeleteActivity` note about the index and "a retry fixes it".
Remove any other wording that blames eventual consistency.

**Files.** `Controllers/ActivitiesController.cs` and any other controller
comment found by searching for `IdIndex` and "eventual".

**Verify.** `grep -rniE "IdIndex|eventual" src/A2.Server/Controllers` returns
nothing.

- [x] Code Formatted
- [x] Linting pass.
- [x] Build pass.
- [x] Tests pass
- [x] Output code reviewed with a seperate agent in a fresh new context for
  critical bugs
- [x] All critical bugs fixed
- [x] Code reviewed with coding standard agent in a brand new context
- [x] Coding standard violations fixed

### Task 9 — Remove the indexes from Terraform

**Do.** Delete the five `IdIndex` blocks and their comments. Keep
`GoogleUserIdIndex` and `UserIdIndex`. Do not run `terraform apply`. Applying
comes after the code is deployed.

**Files.** `../autoassure-infra/dynamodb.tf` (around lines 224, 310, 361, 411,
538).

**Verify.** `terraform validate` in `../autoassure-infra` passes.
`grep -n '"IdIndex"' ../autoassure-infra/dynamodb.tf` returns nothing.

- [x] Code Formatted
- [x] Linting pass.
- [x] Build pass.
- [x] Tests pass
- [x] Output code reviewed with a seperate agent in a fresh new context for
  critical bugs
- [x] All critical bugs fixed
- [x] Code reviewed with coding standard agent in a brand new context
- [x] Coding standard violations fixed

### Task 10 — Local test database

**Do.** Check how the integration tests create their tables. If they define
`IdIndex` for these five tables, remove it there. If they do not, change nothing
and note that in the task.

**Files.** `tests/A2.Server.Tests/DynamoDbLocalFixture.cs` (check first).

**Tests.** The full integration suite still passes.

- [x] Code Formatted
- [x] Linting pass.
- [x] Build pass.
- [x] Tests pass
- [x] Output code reviewed with a seperate agent in a fresh new context for
  critical bugs
- [x] All critical bugs fixed
- [x] Code reviewed with coding standard agent in a brand new context
- [x] Coding standard violations fixed

### Task 11 — Update repository tests

**Do.** Load the `testing-guideline` skill first. Move any leftover calls to the
new `GetByIdAsync` signatures. For each of the five entities, add a test: a row
in another parent returns "not found". Remove tests and helpers that wait for
the index.

**Files.**
`tests/A2.Server.Tests/Repositories/DynamoDb{Environment,Precondition,EvidenceDefinition,Scenario,Activity}RepositoryTests.cs`.

**Tests.** Five new "other parent returns not found" cases. For activities,
cover both a wrong application and a wrong scenario.

**Verify.** `grep -rn "IdIndex" tests/A2.Server.Tests/Repositories` shows only
`GoogleUserIdIndex` or `UserIdIndex` matches.

- [x] Code Formatted
- [x] Linting pass.
- [x] Build pass.
- [x] Tests pass
- [x] Output code reviewed with a seperate agent in a fresh new context for
  critical bugs
- [x] All critical bugs fixed
- [x] Code reviewed with coding standard agent in a brand new context
- [x] Coding standard violations fixed

### Task 12 — Update controller tests

**Do.** Load the `testing-guideline` skill first. Use the new routes everywhere.
Delete the code that polls `IdIndex` to wait for consistency. Add a test that a
create followed at once by a get, update or delete works.

**Files.**
`tests/A2.Server.Tests/Controllers/{Environments,Preconditions,EvidenceDefinitions,Scenarios,Activities,Runs,RunStatusUpdates}ControllerTests.cs`.

**Tests.** One create-then-immediate-use test per entity: environment,
precondition, evidence definition, scenario, activity.

**Verify.** `grep -rn "IdIndex" tests/A2.Server.Tests/Controllers` shows only
`GoogleUserIdIndex` or `UserIdIndex` matches (if any).

- [x] Code Formatted
- [x] Linting pass.
- [x] Build pass.
- [x] Tests pass
- [x] Output code reviewed with a seperate agent in a fresh new context for
  critical bugs
- [x] All critical bugs fixed
- [x] Code reviewed with coding standard agent in a brand new context
- [x] Coding standard violations fixed

### Task 13 — Update design docs

**Do.** Remove the "Watch out" notes about the eventually consistent index.
Update the routes in any doc that lists them.

**Files.** `entity_life_cycle_leaf_goal.md`, `entity_life_cycle_leaf_tasks.md`,
`../docs/about.md`, `../docs/create_test_flow.md` (only if they list these
routes). Do not touch `deleting_id_index.md`.

**Verify.**
`grep -rniE "eventual|IdIndex" entity_life_cycle_leaf_goal.md entity_life_cycle_leaf_tasks.md ../docs`
returns nothing about the five entities.

- [x] Code Formatted
- [x] Linting pass.
- [x] Build pass.
- [x] Tests pass
- [x] Output code reviewed with a seperate agent in a fresh new context for
  critical bugs
- [x] All critical bugs fixed
- [x] Code reviewed with coding standard agent in a brand new context
- [x] Coding standard violations fixed

### Task 14 — Run the checks

**Do.** From a clean tree, run the four commands in the table above, in order.
Fix everything they report. For `inspectcode`, fix real issues. Suppress only
members that are intentionally not used yet.

**Files.** None. Fixes only.

- [x] Code Formatted
- [x] Linting pass.
- [x] Build pass.
- [x] Tests pass
- [x] Output code reviewed with a seperate agent in a fresh new context for
  critical bugs
- [x] All critical bugs fixed
- [x] Code reviewed with coding standard agent in a brand new context
- [x] Coding standard violations fixed

### Task 15 — Regenerate the SDK

**Do.** Ask Nam for approval first. Do not run the script without it. Once Nam
says yes, run `../scripts/generate-sdk.sh`.

**Files.** `../autoassure-server-sdk/openapi.json`,
`../autoassure-server-sdk/src/Api.ts` (generated, do not hand-edit).

**Verify.** `grep -c "IdIndex" ../autoassure-server-sdk/src/Api.ts` prints 0.
The old routes (for example `/scenarios/{scenarioId}` with no application
prefix) are gone from `openapi.json`.

- [x] Code Formatted
- [x] Linting pass.
- [x] Build pass.
- [x] Tests pass
- [x] Output code reviewed with a seperate agent in a fresh new context for
  critical bugs
- [x] All critical bugs fixed
- [x] Code reviewed with coding standard agent in a brand new context
- [x] Coding standard violations fixed

### Task 16 — Update the web app

**Do.** Fix every caller of the changed SDK methods. They now need
`applicationId` (and `scenarioId` for activities). Find them by SDK method name.

**Files.** `../autoassure-web/src/**`.

**Tests.** Existing web tests pass. Update any that call the old method
signatures.

**Verify.** In `../autoassure-web`, run `npm run format`, `npm run lint`,
`npm run build` and `npm test`. All must pass. Use these in place of the .NET
commands for the first four boxes.

- [x] Code Formatted
- [x] Linting pass.
- [x] Build pass.
- [x] Tests pass
- [x] Output code reviewed with a seperate agent in a fresh new context for
  critical bugs
- [x] All critical bugs fixed
- [x] Code reviewed with coding standard agent in a brand new context
- [x] Coding standard violations fixed

## Goal complete

- [x] All 16 tasks above are ticked.
- [x] `dotnet build A2.Server.slnx` passes from a clean tree.
- [x] `dotnet format A2.Server.slnx --verify-no-changes` passes.
- [x] 
  `dotnet jb inspectcode A2.Server.slnx -o=inspect.sarif.json --no-build --severity=WARNING`
  reports no findings.
- [x] `dotnet test tests/A2.Server.UnitTests` passes.
- [x] `dotnet test tests/A2.Server.Tests` passes.
- [x] In `../autoassure-web`: `npm run lint`, `npm run build` and `npm test`
  pass.
- [x] `grep -rn "IdIndex" src` shows only `GoogleUserIdIndex` and `UserIdIndex`.
- [x] `grep -n '"IdIndex"' ../autoassure-infra/dynamodb.tf` returns nothing.
- [x] `grep -rniE "eventual|retry fixes" src/A2.Server/Controllers` returns
  nothing.
- [x] `deleting_id_index.md` has no changes in `git diff`.

## Out of scope

Do not start these. Do not let a review push you into them.

- Runs stay fully nested. They already work this way.
- The other indexes stay: `GoogleUserIdIndex`, `UserIdIndex` and
  `RunHeaderIndex`.
- The organization route fixes (`{id}` to `{organizationId}`, and
  `/organizations/archived` to a query filter).
- Scenario delete. It does not exist yet.

## Open questions

1. **Do all five tables lead their key with the parent?** Tasks 1–7 assume yes.
   If one does not, a direct read would need a scan. Stop and raise it. The
   design may need a key change for that table.
2. **May the SDK be regenerated (task 15)?** The project rules say to ask Nam.
   If no, skip 15 and 16 and the goal stays open.
3. **When do the Terraform changes get applied?** Task 9 only edits the file.
   Applying drops real indexes, and the old code still reads them. Apply after
   the new code is deployed. This goal does not apply it.

## Execution instructions

1. Each task is executed independently by a fresh sub-agent with fresh context.
2. The main thread observes progress only. Sub-agents do the work.
3. When a task completes, update this document to tick off completion.
4. The goal is not complete until every item in this document is ticked.
5. Before executing any task, ask the clarifying questions from "Open
   questions".

Two boxes per task are reviews. Each uses its own fresh sub-agent. The bug
review hunts correctness bugs only. The coding-standard review loads the
`coding-standards` skill. The implementing agent never reviews its own work.
After a review finds something, fix it, then re-run build, lint and tests before
ticking.

```
/goal Work through deleting_id_index_goal.md in /Users/namduong/Documents/autoassure/autoassure-server. Done means every checkbox in that file is ticked, including the final "Goal complete" list. For each task the proof is: `dotnet build A2.Server.slnx` succeeds, `dotnet jb inspectcode A2.Server.slnx -o=inspect.sarif.json --no-build --severity=WARNING` reports no findings, `dotnet test tests/A2.Server.UnitTests` and `dotnet test tests/A2.Server.Tests` pass, and the file is edited to tick that task's boxes. Task 16 uses `npm run lint`, `npm run build` and `npm test` in ../autoassure-web instead. Never tick a box you have not proved in this session. Never edit or delete a test to make it pass. Never change deleting_id_index.md. Do not run the SDK script until Nam approves. Stop after 150 turns and report what is left.
```
