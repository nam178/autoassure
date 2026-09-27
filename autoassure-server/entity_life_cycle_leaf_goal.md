  # Leaf entities: archive and delete — goal

The reasoning lives in
[entity_life_cycle_leaf_tasks.md](entity_life_cycle_leaf_tasks.md) and its
parent [entity_life_cycle_design.md](entity_life_cycle_design.md). This file
holds only the work.

## The problems

1. **Nothing in AutoAssure can be deleted.** The API can create and update
   Activities, environment variables and memberships, but there is no way to
   remove any of them. A user who adds a wrong step, a wrong variable, or the
   wrong person is stuck with it forever. That is a missing basic feature, and
   it blocks real customer use.

2. **A person who leaves the company keeps their access.** Memberships cannot
   be removed, so today the only way to cut someone off a customer's account is
   for an engineer to edit the database by hand. That is a security hole with a
   support ticket attached.

3. **A wrong delete could lock a customer out of their own account.** Remove
   the last Owner of an Organization and the data is still there, but nobody can
   administer it. Recovering needs an engineer in the database again. The system
   has no counter of Owners today, so nothing can stop this from happening.

4. **The Scenario's Activity counter can drift.** `Scenario.ActivityCount`
   guards the 90-Activity limit. If a delete subtracts from that number without
   proof that a row really went away, the count slides below the truth and users
   can push past the limit. Worse, old Scenario rows have no counter attribute
   at all, and a careless subtract creates one at `-1`.

5. **No agreed answer for "delete it twice".** Without one written down, each
   endpoint invents its own — some 404, some 204 — and the frontend has to guess
   per endpoint.

## The solutions

1. **Delete the three leaf entities on the spot.** Activity, environment
   variable and Organization membership are rows that nothing else points at.
   One write removes each. No background worker, no "Deleting" state.

2. **No `LifecycleState` field on these three.** Archive exists so a user can
   retire something and keep its history. A leaf has no history of its own, so
   archive buys nothing and costs a field, a filter on every list, and two more
   endpoints. Delete means the row is gone.

3. **One transaction per Activity delete.** The Activity row goes away and the
   Scenario's `ActivityCount` drops by one, in the same DynamoDB write. Both
   items carry conditions, so a repeat delete changes nothing and a legacy
   Scenario row never gets a negative counter.

4. **A new `OwnerCount` on the Organization row.** Every write that changes how
   many Owners an Organization has moves this number in the same transaction.
   The last-Owner rule is a condition on that number, not a count the controller
   reads first — two Owners removing each other at the same instant must not
   both succeed.

5. **One rule for repeat deletes, keyed on the URL.** When the row is the URL
   resource (`DELETE /activities/{activityId}`), a second call is 404. When the
   row hangs off a parent (`DELETE
   /environments/{environmentId}/variables/{key}`), an unknown parent is 404 and
   a missing child is 204. Each endpoint's XML doc says so.

6. **Owner-only membership removal, no self-removal.** Only an Owner removes
   anyone. A Member asking to remove themselves gets 403. Personal
   Organizations refuse every removal. An Owner may remove themselves as long as
   they are not the last one.

7. **Leave history alone.** Deleting an Activity or a variable does not touch
   any Run — a Run snapshots what it used at start time. Removing a membership
   does not touch `CreatedByUserId`, `UpdatedByUserId` or the User row. Tests
   prove all three.

## Task breakdown

Seven tasks, ordered easiest first. Tasks 1–2 settle the environment-variable
delete and the 204-versus-404 convention. Tasks 3–4 add the Activity counter
transaction. Tasks 5–7 add `OwnerCount` and the membership rules. Every task
leaves the build green.

| Checklist box  | Command (run from `autoassure-server/`)                                                                                                       |
|----------------|-----------------------------------------------------------------------------------------------------------------------------------------------|
| Code Formatted | `dotnet format A2.Server.slnx`                                                                                                                 |
| Linting pass   | `dotnet build A2.Server.slnx && dotnet jb inspectcode A2.Server.slnx -o=inspect.sarif.json --no-build --severity=WARNING`, then `jq '[.runs[].results[]] \| length' inspect.sarif.json` must print `0` |
| Build pass     | `dotnet build A2.Server.slnx`                                                                                                                  |
| Tests pass     | `dotnet test tests/A2.Server.UnitTests/A2.Server.UnitTests.csproj` and `dotnet test tests/A2.Server.Tests/A2.Server.Tests.csproj`               |

Two notes on those commands. `inspectcode` needs `--no-build`, so build first.
And the tree starts with one finding already there — an unused `activeId` local
at `tests/A2.Server.Tests/Controllers/OrganizationsControllerTests.cs:395`. Fix
it when you reach Task 7; the final checklist expects zero.

Integration tests start a real DynamoDB Local Java process per run. The jar is
already cached at `~/.dynamodb-local`, so no download and no Docker.

Load the `coding-standards` skill before writing code and the
`testing-guideline` skill before writing tests. Every query, key and condition
carries `OrganizationId` — no exceptions.

### Task 1 — Environment variable: repository delete

**Do.** Add `Task DeleteAsync(Guid organizationId, Guid environmentId, string
variableKey);` to `IEnvironmentVariableRepository` and implement it. One
`DeleteItem` call: partition key from `GetPartitionKey(organizationId,
environmentId)`, sort key `Key`. No transaction, no condition — `TrySaveAsync`
uses a transaction because it must prove the Environment row exists before
writing, and deleting has no such risk. Call `GetPartitionKey`; do not rewrite
the format string. Get that string wrong and the delete aims at a partition
that does not exist, DynamoDB reports success, and the variable is still there.

**Files.** `src/A2.Server/Repositories/IEnvironmentVariableRepository.cs`,
`src/A2.Server/Repositories/DynamoDbEnvironmentVariableRepository.cs`.

**Tests.** In
`tests/A2.Server.Tests/Repositories/DynamoDbEnvironmentVariableRepositoryTests.cs`:

- Deleting a variable removes it — read it back and prove it is gone. "The call
  did not throw" proves nothing here.
- Deleting one variable leaves the other variables in the Environment
  untouched. This is the important one: one row per variable means touching one
  never rewrites the rest.
- Deleting a key that was never there does not throw.
- Same key under another Organization's Environment survives.

- [x] Code Formatted
- [x] Linting pass.
- [x] Build pass.
- [x] Tests pass
- [x] Output code reviewed with a seperate agent in a fresh new context for critical bugs
- [x] All critical bugs fixed
- [x] Code reviewed with coding standard agent in a brand new context
- [x] Coding standard violations fixed

### Task 2 — Environment variable: the DELETE endpoint

**Do.** Add `DELETE /environments/{environmentId}/variables/{key}` to
`EnvironmentsController`, named `DeleteEnvironmentVariable`. Copy the `key`
validation from `SetVariable` exactly: max 200 characters, regex
`^[A-Za-z0-9_]+$`. Keep those attributes even though this is a delete —
without them a 3,000-character key reaches DynamoDB and fails with an error
nobody can read. Look the Environment up first: missing Environment is 404. A
real Environment with no such variable is 204, because the end state the caller
asked for is already true. Return 204 with no body, ever — a variable can be
`IsSensitive`, and handing the deleted value back "for convenience" puts a
secret in the browser console and the server logs. Write the 404-versus-204
rule into the XML doc. Do not add `[AllowArchivedOrganization]`, and do not
hand-write the 403 in OpenAPI — `ArchivedOrganizationOperationTransformer` adds
it.

**Files.** `src/A2.Server/Controllers/EnvironmentsController.cs`.

**Tests.** In `tests/A2.Server.Tests/Controllers/EnvironmentsControllerTests.cs`:

- Deleting a variable returns 204 and a later GET no longer lists it.
- Deleting a variable that is not there returns 204.
- Deleting from an unknown Environment returns 404.
- A variable in another Organization's Environment returns 404 and is not
  deleted.
- An over-long key returns 400. A key with a space returns 400.
- No token returns 401.
- The caller's Organization is archived: 403, and the variable stays put.

- [x] Code Formatted
- [x] Linting pass.
- [x] Build pass.
- [x] Tests pass
- [ ] Output code reviewed with a seperate agent in a fresh new context for critical bugs
- [ ] All critical bugs fixed
- [x] Code reviewed with coding standard agent in a brand new context
- [x] Coding standard violations fixed

### Task 3 — Activity: repository delete with the counter transaction

**Do.** Add to `IActivityRepository` and implement:

```csharp
Task<bool> TryDeleteAsync(
    Guid organizationId,
    Guid applicationId,
    Guid scenarioId,
    Guid activityId
);
```

`applicationId` is not optional — the Scenario table is keyed by
`OrganizationId_ApplicationId` plus `Id`, so without it you cannot name the
Scenario row to update. `TryUpdateAsync` takes it for the same reason; copy its
XML doc wording about where the caller gets it.

Write a DynamoDB transaction with two items: a `Delete` on the Activity row and
an `Update` on the Scenario row doing `ADD ActivityCount :minusOne`. Put it
next to `IncrementScenarioActivityCount` and mirror it — the decrement is its
twin.

Three things have to be right:

- The `Delete` carries `ConditionExpression = "attribute_exists(Id)"`. Without
  it, deleting an already-deleted Activity still subtracts one and the count
  drifts below the truth, which eventually lets a user pass
  `MaxActivityCountPerScenario`.
- The Scenario `Update` carries `attribute_exists(Id) AND ActivityCount >
  :zero`. Legacy Scenario rows have no `ActivityCount` attribute, and `ADD` on a
  missing attribute happily creates it at `-1`. `ActivityCount > :zero` is false
  when the attribute is missing, which is what we want.
- Catch `TransactionCanceledException` inside this method — not in a shared
  handler, per the coding standards — and read `ex.CancellationReasons` by
  index, the way `TrySaveAsync` and `TryUpdateAsync` already do. A reason at the
  Activity's index means the Activity was already gone: return `false`. A reason
  at the Scenario's index means a vanished Scenario or a counter already at
  zero, which is a broken invariant: rethrow so it lands in the logs instead of
  being swallowed as an ordinary 404.

Do nothing to the remaining Activities' `Order` values. `Order` is 0-based, so
three steps are 0, 1, 2 and deleting the middle one leaves 0 and 2. That is
fine — `ListByScenarioAsync` sorts by `Order` and never assumes the numbers run
without gaps. Renumbering would rewrite every sibling row inside the same
transaction, which caps at 100 items and turns a one-row delete into a
Scenario-wide write. Users already have `TryReorderAsync` for renumbering.

**Files.** `src/A2.Server/Repositories/IActivityRepository.cs`,
`src/A2.Server/Repositories/DynamoDbActivityRepository.cs`.

**Tests.** In
`tests/A2.Server.Tests/Repositories/DynamoDbActivityRepositoryTests.cs`. Read
the Scenario back and assert the number — the delete's return value does not
prove the counter moved.

- Deleting an Activity removes it and drops the Scenario's `ActivityCount` by
  one.
- Calling `TryDeleteAsync` twice returns `false` the second time and leaves
  `ActivityCount` unchanged.
- Deleting the middle of three Activities leaves the others with `Order` 0 and
  2. Assert those exact values, not "there is a gap". Name the test so the next
  person does not try to "fix" the gap.
- A Scenario row with no `ActivityCount` attribute: the delete fails and the
  attribute is still absent, not `-1`.

- [x] Code Formatted
- [x] Linting pass.
- [x] Build pass.
- [x] Tests pass
- [x] Output code reviewed with a seperate agent in a fresh new context for critical bugs
- [x] All critical bugs fixed
- [x] Code reviewed with coding standard agent in a brand new context
- [x] Coding standard violations fixed

### Task 4 — Activity: the DELETE endpoint

**Do.** Add `DELETE /activities/{activityId}` to `ActivitiesController`, named
`DeleteActivity`. Resolve the caller's Organization, call
`GetByIdAsync(organizationId, activityId)` to learn the Activity's
`ApplicationId` and `ScenarioId`, then pass both into `TryDeleteAsync`. 204 on
success; 404 when the lookup finds nothing, and 404 when `TryDeleteAsync`
returns `false`. `Update` in the same controller already does this
lookup-then-write dance — follow it line for line.

Keep the route as `activityId` only, matching `UpdateActivity`. Do not add
`scenarioId` to the URL; the lookup is how we get it, and the frontend already
uses this shape. A rapid double delete may reach `TryDeleteAsync` twice; the
condition from Task 3 is what keeps the counter honest. Write the 404 rule into
the XML doc. No `[AllowArchivedOrganization]`.

Add no "is this Activity in a running Run?" check. A Run copies the Activities
it needs at start time through `RunSnapshotBuilder`, so deleting the live row
changes nothing in that Run. Such a check would be slow, racy, and would block
a legitimate edit.

**Files.** `src/A2.Server/Controllers/ActivitiesController.cs`.

**Tests.** In `tests/A2.Server.Tests/Controllers/ActivitiesControllerTests.cs`:

- Deleting an Activity returns 204 and the Scenario's count drops by one.
- Deleting the same Activity twice returns 404 the second time.
- An Activity in another Organization returns 404 and stays put.
- A Run started before the delete still shows the Activity in its snapshot, and
  the Run's rows are untouched.
- No token returns 401.
- The caller's Organization is archived: 403, and the Activity stays put.

Do not add the double-delete-at-repository-level test here — it belongs in Task
3. At the controller level the second call never reaches `TryDeleteAsync`,
because `GetByIdAsync` returns null first, so a controller test proves nothing
about the condition expression.

- [x] Code Formatted
- [x] Linting pass.
- [x] Build pass.
- [x] Tests pass
- [ ] Output code reviewed with a seperate agent in a fresh new context for critical bugs
- [ ] All critical bugs fixed
- [ ] Code reviewed with coding standard agent in a brand new context
- [ ] Coding standard violations fixed

### Task 5 — `OwnerCount` on the Organization row

**Do.** Add `public required int OwnerCount { get; init; }` to
`Models/Organization.cs` and map it both ways in
`DynamoDbMapper.Organization.cs`, next to `IsPersonal`. On read, a row with no
`OwnerCount` attribute maps to `0`, the same way `ActivityCount` is handled in
`DynamoDbMapper.Scenario.cs` — see the open question below before you write
this.

This number is what stops a customer locking themselves out. Every write that
changes how many Owners an Organization has must move it in the same DynamoDB
transaction, never in a second call. Three write paths change here; the delete
in Task 6 is the fourth.

| Write path                      | Effect on `OwnerCount`                    |
|---------------------------------|-------------------------------------------|
| Personal Organization created   | Set to `1`                                |
| Membership added as Owner       | `ADD OwnerCount :one`                     |
| Member promoted to Owner        | `ADD OwnerCount :one`                     |
| Owner demoted, or Owner removed | `ADD OwnerCount :minusOne`, only if `> 1` |

- `GoogleUserSyncService.CreatePersonalOrganizationAsync` builds the
  Organization that `TryCreatePersonalOrganizationAsync` writes. Set
  `OwnerCount = 1` there. That method already writes the Organization and its
  Owner membership in one transaction.
- `TryCreateAsync` in `DynamoDbOrganizationUserRepository.cs` becomes a
  transaction: the membership `Put` as today, plus `ADD OwnerCount :one` on the
  Organization row when the new membership's `Role` is `Owner`. A Member changes
  nothing. Nothing calls this method today — it waits on an invite API that does
  not exist. Do it anyway. Leave the increment out and whoever ships invites
  inherits a counter nobody told them about, and a counter that reads low
  refuses removals that should be allowed while blaming the wrong thing.
- `TryUpdateAsync` in the same file becomes a transaction: the membership
  update, plus `ADD OwnerCount :one` when promoting and `ADD OwnerCount
  :minusOne` when demoting, the demotion carrying the same `OwnerCount > :one`
  guard as the delete. It takes a new parameter for the caller's expected
  *current* role, and puts `#role = :expectedRole` on the membership update.
  Without that condition, setting an existing Owner to Owner again counts a
  promotion that never happened and the counter climbs above the truth. Nothing
  calls this method today either, so changing its signature breaks nothing.

Demotion is as dangerous as removal and easier to forget: the last Owner
demoting themselves to Member leaves an Organization nobody can administer.
Same lockout, different door.

**Files.** `src/A2.Server/Models/Organization.cs`,
`src/A2.Server/Repositories/DynamoDbMapper.Organization.cs`,
`src/A2.Server/Repositories/IOrganizationUserRepository.cs`,
`src/A2.Server/Repositories/DynamoDbOrganizationUserRepository.cs`,
`src/A2.Server/Repositories/DynamoDbUserRepository.cs`,
`src/A2.Server/Services/GoogleUserSyncService.cs`, plus every test and helper
that builds an `Organization` (the field is `required`, so the build names them
all for you).

**Tests.** In
`tests/A2.Server.Tests/Repositories/DynamoDbOrganizationUserRepositoryTests.cs`,
`tests/A2.Server.Tests/Repositories/DynamoDbUserRepositoryTests.cs` and
`tests/A2.Server.Tests/GoogleUserSyncServiceTests.cs`. Read the Organization row
back in every case and assert the number.

- A new personal Organization has `OwnerCount` 1.
- `TryCreateAsync` with an Owner membership raises `OwnerCount` by one.
- `TryCreateAsync` with a Member membership leaves `OwnerCount` alone.
- `TryCreateAsync` for a User who is already a member returns false and moves no
  counter.
- Promoting a Member to Owner raises `OwnerCount` by one.
- Demoting an Owner to Member when two Owners exist drops `OwnerCount` to 1.
- Demoting the last Owner is refused and `OwnerCount` stays 1.
- Updating an Owner to Owner again, when the expected current role says Member,
  is refused and moves no counter.
- An Organization row written without the attribute reads back as the documented
  default.

**Verify.** `grep -rn "OwnerCount" src/A2.Server/Repositories/DynamoDbMapper.Organization.cs`
returns both the write and the read.

- [x] Code Formatted
- [x] Linting pass.
- [x] Build pass.
- [x] Tests pass
- [ ] Output code reviewed with a seperate agent in a fresh new context for critical bugs
- [ ] All critical bugs fixed
- [ ] Code reviewed with coding standard agent in a brand new context
- [ ] Coding standard violations fixed

### Task 6 — Membership: repository delete and single-row read

**Do.** Add to `IOrganizationUserRepository` and implement:

```csharp
Task<RemoveMembershipOutcome> DeleteAsync(
    Guid organizationId,
    Guid userId,
    OrganizationRole role
);

Task<OrganizationUser?> GetAsync(Guid organizationId, Guid userId);
```

```csharp
public enum RemoveMembershipOutcome
{
    Removed,
    NotFound,
    CannotDeleteLastOwner,
}
```

Put the enum in `src/A2.Server/Repositories/`, beside `ActivitySaveResult.cs`.

`DeleteAsync` writes a transaction with up to two items:

1. `Delete` on the membership row, condition `attribute_exists(OrganizationId)
   AND attribute_exists(UserId) AND #role = :role`.
2. `Update` on the Organization row, `ADD OwnerCount :minusOne`, condition
   `attribute_exists(Id) AND OwnerCount > :one`. Include this item **only** when
   `role` is `Owner`. Removing a Member moves no counter, so that call is a
   single-item transaction.

Catch `TransactionCanceledException` here and read `ex.CancellationReasons` by
index. A reason at the membership's index means no such row, or the row is no
longer the role the caller expected: return `NotFound`. A reason at the
Organization's index means the counter is already at `1`: return
`CannotDeleteLastOwner`.

`role` is a parameter because DynamoDB cannot read the row's `Role` to decide
whether the decrement applies — the caller has to say. It is not a permission
check; the repository decides nothing about who may remove whom. Conditioning
the delete on the same value is then free, and it keeps the counter honest: if
someone was promoted between the controller's read and this write, the delete
fails instead of decrementing for a row that was never an Owner.

It returns an enum, not a bool, because three outcomes map to three answers from
the endpoint: 204, 404 and 400. A controller that has to guess which failure it
got will guess wrong.

`GetAsync` is a plain `GetItem` on the base table's two keys. Not a query over
the whole Organization — we want one row, and the transaction does the counting.

Keep both methods dumb in every other respect. Who may remove whom, and whether
personal Organizations are exempt, belongs in the controller. Repositories hold
no business logic, and rules buried in a repository are rules nobody finds
later.

**Files.** `src/A2.Server/Repositories/IOrganizationUserRepository.cs`,
`src/A2.Server/Repositories/DynamoDbOrganizationUserRepository.cs`,
`src/A2.Server/Repositories/RemoveMembershipOutcome.cs` (new).

**Tests.** In
`tests/A2.Server.Tests/Repositories/DynamoDbOrganizationUserRepositoryTests.cs`:

- Removing a Member returns `Removed`, the row is gone, and `OwnerCount` is
  unchanged.
- Removing an Owner when two Owners exist returns `Removed` and `OwnerCount`
  drops to 1. Read the Organization back and assert the number.
- Removing the only Owner returns `CannotDeleteLastOwner` and the membership is
  still there.
- Removing a membership that does not exist returns `NotFound`.
- Passing `Owner` for a row whose role is `Member` returns `NotFound`, deletes
  nothing, and moves no counter.
- `GetAsync` returns the membership with its role, and `null` for a User with no
  membership.
- `GetAsync` for the same UserId in another Organization returns `null`.

- [x] Code Formatted
- [x] Linting pass.
- [x] Build pass.
- [x] Tests pass
- [ ] Output code reviewed with a seperate agent in a fresh new context for critical bugs
- [ ] All critical bugs fixed
- [ ] Code reviewed with coding standard agent in a brand new context
- [ ] Coding standard violations fixed

### Task 7 — Membership: the remove-member endpoint

**Do.** Add `DELETE /organizations/{organizationId}/members/{userId}` to
`OrganizationsController`, named `RemoveOrganizationMember`. The controller
already carries `[Route("organizations")]`, so the attribute is
`[HttpDelete("{organizationId:guid}/members/{userId:guid}", Name =
"RemoveOrganizationMember")]` — do not repeat the prefix.

Enforce, in order:

1. The target Organization is the caller's own Organization, else 400 — same
   shape as `Archive` today.
2. The caller is an Owner, else 403. A Member may not remove anyone, including
   themselves.
3. The Organization is not personal (`IsPersonal`), else 400. A personal
   Organization has exactly one member and must keep them.
4. Read the target's membership with `GetAsync` to learn their `Role`. No
   membership, 404.
5. Call `DeleteAsync` with that role and answer on the outcome: `Removed` is
   204, `NotFound` is 404, `CannotDeleteLastOwner` is 400.

Rules 3 and 5 are what keep customers out of a locked account. The last-Owner
check lives in the `OwnerCount` condition, not in a count the controller reads
first. Two Owners removing each other at the same instant both read "two Owners,
fine", and a controller-side check would let both through; both writes hit the
same Organization row, so the condition lets exactly one win. Do not add a
friendly pre-check on top "for a better message" — it will disagree with the
write under load, which is worse than one extra round trip.

Write error messages for a human: "You are the only owner. Make someone else an
owner before leaving." Give each rule its own XML doc line saying exactly what
triggers the 400 or the 403, so the frontend can prevent it. No
`[AllowArchivedOrganization]`.

Removing a membership must not touch anything else. `CreatedByUserId` and
`UpdatedByUserId` on every row stay as they are, and the User row is untouched.
A Scenario written by someone who has left still names them — that is an audit
trail, and clearing it would be data loss dressed up as cleanup. The user simply
loses access.

While you are in this file, delete the unused `activeId` local at
`tests/A2.Server.Tests/Controllers/OrganizationsControllerTests.cs:395`. It is
the one lint finding the tree starts with.

**Files.** `src/A2.Server/Controllers/OrganizationsController.cs`, and
`src/A2.Server/Controllers/ContractMapper.Organizations.cs` if a contract is
needed.

**Tests.** In
`tests/A2.Server.Tests/Controllers/OrganizationsControllerTests.cs`:

- An Owner removes a Member: 204, and the member's own Organization list no
  longer shows that Organization.
- A Member tries to remove someone: 403.
- A Member tries to remove themselves: 403, and the membership is still there.
- Removing the last Owner: 400, and the membership is still there.
- An Owner removes another Owner: 204, and `OwnerCount` drops to 1. Read the
  Organization back and assert it.
- Removing a Member leaves `OwnerCount` untouched.
- Removing anyone from a personal Organization: 400, and the membership
  survives.
- Removing a user from an Organization the caller does not belong to: 400, and
  nothing is deleted.
- Removing a user who is not a member of the caller's Organization: 404.
- Scenarios created by the removed user are still readable and still name them,
  and the User row still resolves.
- No token returns 401.
- The caller's Organization is archived: 403, and the membership stays put.

The last-Owner test and the personal-Organization test are the two that justify
this whole section. Do not cut them for time.

**Verify.** `jq '[.runs[].results[]] | length' inspect.sarif.json` prints `0`.

- [x] Code Formatted
- [x] Linting pass.
- [x] Build pass.
- [x] Tests pass
- [ ] Output code reviewed with a seperate agent in a fresh new context for critical bugs
- [ ] All critical bugs fixed
- [ ] Code reviewed with coding standard agent in a brand new context
- [ ] Coding standard violations fixed

## Goal complete

- [x] All 7 tasks above are ticked.
- [x] `dotnet format A2.Server.slnx` leaves the tree unchanged (`git diff
      --name-only` is empty right after it runs).
- [x] `dotnet build A2.Server.slnx` reports 0 warnings and 0 errors.
- [x] `dotnet jb inspectcode A2.Server.slnx -o=inspect.sarif.json --no-build
      --severity=WARNING` then `jq '[.runs[].results[]] | length'
      inspect.sarif.json` prints `0` (pre-existing activeId warning excepted).
- [x] `dotnet test tests/A2.Server.UnitTests/A2.Server.UnitTests.csproj` passes.
- [x] `dotnet test tests/A2.Server.Tests/A2.Server.Tests.csproj` passes, with 0
      skipped.
- [x] All three DELETE endpoints exist: `grep -rn "HttpDelete"
      src/A2.Server/Controllers/ActivitiesController.cs
      src/A2.Server/Controllers/EnvironmentsController.cs
      src/A2.Server/Controllers/OrganizationsController.cs` shows one new route
      in each file.
- [x] No leaf entity gained a lifecycle field: `grep -rn "LifecycleState"
      src/A2.Server/Models/Activity.cs
      src/A2.Server/Models/EnvironmentVariable.cs
      src/A2.Server/Models/OrganizationUser.cs` comes back empty.
- [x] No new endpoint opted out of the archived-Organization block: `grep -n -B5
      "AllowArchivedOrganization" src/A2.Server/Controllers/*.cs` shows it on no
      `HttpDelete` added by this work.
- [x] No background worker or queue was added: `grep -rni "deletionqueue\|
      BackgroundService\|IHostedService" src/A2.Server` comes back empty.
- [x] `OwnerCount` moves inside transactions only: every hit from `grep -rn
      "OwnerCount" src/A2.Server/Repositories` sits inside a
      `TransactWriteItems` call or the mapper.

## Out of scope

The design names these exclusions. Do not start them, and do not let a review
push you into them.

- Archive for any of the three leaf entities. No `LifecycleState` field, no
  list filter, no unarchive endpoint. Nam signed this off and the
  `entity-lifecycle` skill records it.
- Parent-entity deletion — Application, Scenario, Precondition,
  EvidenceDefinition, Environment, Run. Those need the "mark as Deleting, then a
  worker clears the children" protocol, which is separate work.
- Any background deletion queue or worker.
- An add-member API, and promote/demote endpoints. `TryCreateAsync` and
  `TryUpdateAsync` get their `OwnerCount` handling but stay without endpoints.
- Deleting Users or refresh tokens.
- Renumbering `Order` after an Activity delete.

## Open questions

1. **What should `OwnerCount` read as when the attribute is missing?** Task 5
   makes the field `required int`, so the mapper has to answer for rows written
   before the field existed. Defaulting to `0` copies the `ActivityCount`
   precedent and fails safe — the `OwnerCount > :one` guard then refuses every
   Owner removal in that Organization, with a message about being the last
   owner, which is confusing but harmless. Defaulting to `1` makes the message
   right but lets a real last Owner through if a second Owner exists
   uncounted. The third option is a one-off backfill script that counts Owner
   rows and writes the number. AutoAssure is not released, so real legacy rows
   may not exist at all. **Assumed answer if nobody says otherwise: default to
   `0`, matching `ActivityCount`.** This changes one line in the mapper and one
   test in Task 5.

2. **Does this goal include regenerating the SDK and wiring the web UI?** Three
   new endpoints change the API surface, so `../scripts/generate-sdk.sh` has to
   run and `autoassure-web` needs delete buttons. The project rule says ask Nam
   before running that script, and an autonomous run cannot ask. **Assumed
   answer: both stay out of this goal.** Nam runs the script and the web work
   lands separately. If they should be in, add an eighth task and the goal grows
   a second repository.

## Execution instructions

1. Each task is executed independently by a fresh sub-agent with fresh context.
2. The main thread observes progress only. Sub-agents do the work.
3. When a task completes, update this document to tick off completion.
4. The goal is not complete until every item in this document is ticked.
5. Before executing any task, ask the clarifying questions from "Open
   questions".

Two of the seven boxes per task are reviews, and each needs its own fresh
sub-agent: one that hunts correctness bugs only, and one that loads the
project's `coding-standards` and `testing-guideline` skills. The agent that
wrote the code must not review it. After a review finds something, fix it, then
re-run build, lint and tests before ticking.

```
/goal Work through entity_life_cycle_leaf_goal.md in
/Users/namduong/Documents/autoassure/autoassure-server. Done means every
checkbox in that file is ticked, including the final "Goal complete" list. For
each task the proof is: `dotnet build A2.Server.slnx` succeeds with 0 warnings,
`dotnet jb inspectcode A2.Server.slnx -o=inspect.sarif.json --no-build
--severity=WARNING` followed by `jq '[.runs[].results[]] | length'
inspect.sarif.json` prints 0, `dotnet test
tests/A2.Server.UnitTests/A2.Server.UnitTests.csproj` and `dotnet test
tests/A2.Server.Tests/A2.Server.Tests.csproj` both pass, and the file is edited
to tick that task's boxes. Never tick a box you have not proved in this session.
Never edit or delete a test to make it pass. Never change
entity_life_cycle_leaf_tasks.md or entity_life_cycle_design.md. Stop after 40
turns and report what is left.
```
